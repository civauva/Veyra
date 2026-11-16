# Installs the Veyra agent on Windows (ADR 0019): the official OpenTelemetry Collector run by its OpAMP supervisor,
# as the Windows service "veyra-agent". Veyra serves this script with its own address filled in.
#
# Run in PowerShell as an administrator, with the code from Settings -> Agents:
#   irm __VEYRA_URL__/v1/agents/install.ps1 | iex; Install-VeyraAgent -Code XXXX-XXXX-XXXX
# Remove the agent again with:
#   irm __VEYRA_URL__/v1/agents/install.ps1 | iex; Uninstall-VeyraAgent
#
# The programs come from OpenTelemetry's releases on GitHub and are checked against the SHA-256 values below, which
# Veyra pins with the version it tests. The agent only connects out: to Veyra, and to the servers it reads.

$VeyraUrl = '__VEYRA_URL__'
$VeyraAgentVersion = '__VERSION__'
$VeyraAgentSupervisorSha256 = '__SUPERVISOR_WINDOWS_AMD64__'
$VeyraAgentCollectorSha256 = '__COLLECTOR_WINDOWS_AMD64__'
$VeyraAgentPrograms = '__WINDOWS_PROGRAMS__'
$VeyraAgentData = '__WINDOWS_DATA__'
$VeyraAgentService = 'veyra-agent'

function Assert-VeyraAgentAdministrator {
    $identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()

    if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run PowerShell as an administrator: installing a service needs it.'
    }
}

function Get-VeyraAgentFile([string] $Url, [string] $Path, [string] $Sha256) {
    # A file downloaded before and still intact is used again, so a second try does not download it again.
    if ((Test-Path $Path) -and ((Get-FileHash $Path -Algorithm SHA256).Hash -eq $Sha256)) {
        return
    }

    Write-Host "Downloading $(Split-Path $Url -Leaf)..."
    Invoke-WebRequest -Uri $Url -OutFile $Path -UseBasicParsing

    if ((Get-FileHash $Path -Algorithm SHA256).Hash -ne $Sha256) {
        Remove-Item $Path -Force
        throw "$(Split-Path $Url -Leaf) does not match the SHA-256 Veyra expects. Nothing was installed."
    }
}

function Install-VeyraAgent {
    [CmdletBinding()]
    param(
        # The one-time code from Settings -> Agents, such as K7QM-4TWX-9PRA.
        [Parameter(Mandatory = $true)] [string] $Code
    )

    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 'Tls12'
    Assert-VeyraAgentAdministrator

    $downloads = Join-Path $VeyraAgentData 'downloads'
    $state = Join-Path $VeyraAgentData 'state'
    $config = Join-Path $VeyraAgentData 'supervisor.yaml'
    $release = 'https://github.com/open-telemetry/opentelemetry-collector-releases/releases/download'
    New-Item -ItemType Directory -Force -Path $VeyraAgentPrograms, $downloads, $state | Out-Null

    # Only the system and administrators may read the agent's folder: its start-up file holds the agent's key.
    icacls $VeyraAgentData /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null

    $supervisorFile = Join-Path $downloads "opampsupervisor_$($VeyraAgentVersion)_windows_amd64.exe"
    $collectorFile = Join-Path $downloads "otelcol-contrib_$($VeyraAgentVersion)_windows_amd64.tar.gz"
    Get-VeyraAgentFile "$release/cmd%2Fopampsupervisor%2Fv$VeyraAgentVersion/$(Split-Path $supervisorFile -Leaf)" `
        $supervisorFile $VeyraAgentSupervisorSha256
    Get-VeyraAgentFile "$release/v$VeyraAgentVersion/$(Split-Path $collectorFile -Leaf)" `
        $collectorFile $VeyraAgentCollectorSha256

    Write-Host 'Asking Veyra for the agent''s settings...'
    $body = @{ code = $Code; platform = 'Windows' } | ConvertTo-Json

    try {
        $startup = Invoke-RestMethod -Method Post -Uri "$VeyraUrl/v1/agents/enroll" -ContentType 'application/json' `
            -Body $body -UseBasicParsing
    }
    catch {
        throw 'Veyra did not accept the code: it is wrong, used, or older than an hour. Make a new one under Settings -> Agents.'
    }

    $existing = Get-Service -Name $VeyraAgentService -ErrorAction SilentlyContinue

    if ($existing -and $existing.Status -ne 'Stopped') {
        Stop-Service -Name $VeyraAgentService -Force
    }

    Copy-Item $supervisorFile (Join-Path $VeyraAgentPrograms 'opampsupervisor.exe') -Force
    tar.exe -xzf $collectorFile -C $VeyraAgentPrograms otelcol-contrib.exe

    if ($LASTEXITCODE -ne 0) {
        throw 'The Collector could not be unpacked.'
    }

    # Without a byte-order mark, which YAML readers do not expect.
    [IO.File]::WriteAllText($config, $startup, (New-Object Text.UTF8Encoding $false))

    # The supervisor writes to the Application event log under its service's name.
    if (-not [Diagnostics.EventLog]::SourceExists($VeyraAgentService)) {
        [Diagnostics.EventLog]::CreateEventSource($VeyraAgentService, 'Application')
    }

    if (-not $existing) {
        New-Service -Name $VeyraAgentService -DisplayName 'Veyra agent' -StartupType Automatic `
            -Description 'Reads database servers for Veyra: the OpenTelemetry Collector run by its OpAMP supervisor.' `
            -BinaryPathName "`"$(Join-Path $VeyraAgentPrograms 'opampsupervisor.exe')`" --config `"$config`"" | Out-Null

        # Restarted a minute after it stops unexpectedly.
        sc.exe failure $VeyraAgentService reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
    }

    Start-Service -Name $VeyraAgentService
    Write-Host 'The Veyra agent is installed and running. Settings -> Agents shows it online within a minute.'
}

function Uninstall-VeyraAgent {
    [CmdletBinding()]
    param()

    $ErrorActionPreference = 'Stop'
    Assert-VeyraAgentAdministrator

    if (Get-Service -Name $VeyraAgentService -ErrorAction SilentlyContinue) {
        Stop-Service -Name $VeyraAgentService -Force
        sc.exe delete $VeyraAgentService | Out-Null
    }

    if ([Diagnostics.EventLog]::SourceExists($VeyraAgentService)) {
        [Diagnostics.EventLog]::DeleteEventSource($VeyraAgentService)
    }

    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $VeyraAgentPrograms, $VeyraAgentData
    Write-Host 'The Veyra agent is removed. Remove it under Settings -> Agents too, which revokes its key.'
}
