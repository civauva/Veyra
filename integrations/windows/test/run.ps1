<#
.SYNOPSIS
Checks integrations/windows against a real Collector on this Windows machine (VEY-35).

.DESCRIPTION
Runs otelcol-contrib with collector.yaml and iis.yaml for 45 seconds, writing to a file instead of Veyra, then checks
what it wrote with check.mjs: the host's metrics, Event Log errors, and IIS requests as logs of a site's service.
Without -WithIis, the W3C log reader reads a sample log written here, and IIS's own metrics are not checked. Needs
Node.js.

.PARAMETER Collector
The path to otelcol-contrib.exe, 0.140.1 like Veyra's agent.

.PARAMETER WithIis
IIS runs on this machine and serves http://localhost/: the check requests pages and expects IIS's metrics too.

.PARAMETER WriteEvent
Writes an error to the Application log first, so the Event Log check does not depend on the log's history. It needs
an administrator and registers the event source VeyraCheck, so it is for throwaway machines such as CI runners.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File integrations\windows\test\run.ps1 -Collector C:\otelcol\otelcol-contrib.exe
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $Collector,

    [switch] $WithIis,

    [switch] $WriteEvent
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $here "out"

if (Test-Path $out) {
    Remove-Item -Recurse -Force $out
}

New-Item -ItemType Directory -Path $out | Out-Null

$env:CHECK_OUT = $out
$env:VEYRA_OTLP_ENDPOINT = "http://127.0.0.1:1"
$env:VEYRA_INGESTION_KEY = "vyk_check"
$env:VEYRA_ENVIRONMENT = "production"

$configs = @(
    "--config", (Join-Path $here "..\collector.yaml"),
    "--config", (Join-Path $here "..\iis.yaml"),
    "--config", (Join-Path $here "to-file.yaml")
)

if (-not $WithIis) {
    # A day's log of the Default Web Site, as IIS writes it: UTF-8 with a byte order mark before its first header,
    # then one request per line, in UTC.
    $site = Join-Path $out "iis\W3SVC1"
    New-Item -ItemType Directory -Path $site | Out-Null
    $now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss")
    $fields = "date time s-ip cs-method cs-uri-stem cs-uri-query s-port cs-username c-ip cs(User-Agent) " +
        "cs(Referer) sc-status sc-substatus sc-win32-status time-taken"
    $lines = @(
        "#Software: Microsoft Internet Information Services 10.0",
        "#Version: 1.0",
        "#Date: $now",
        "#Fields: $fields",
        "$now 10.0.0.5 GET /orders - 443 - 10.0.0.9 Mozilla/5.0 - 200 0 0 42",
        "$now 10.0.0.5 GET /missing - 443 - 10.0.0.9 Mozilla/5.0 - 404 0 2 3",
        "$now 10.0.0.5 POST /checkout id=7 443 - 10.0.0.9 Mozilla/5.0 - 500 0 0 1290"
    )
    [System.IO.File]::WriteAllText(
        (Join-Path $site "u_ex.log"),
        ($lines -join "`r`n") + "`r`n",
        (New-Object System.Text.UTF8Encoding $true))
    $env:IIS_LOG_DIR = Join-Path $out "iis"
    $configs += @("--config", (Join-Path $here "no-iis.yaml"))
}

$wroteEvent = $false

if ($WriteEvent) {
    & eventcreate /T ERROR /ID 900 /L APPLICATION /SO VeyraCheck /D "Veyra's Windows check wrote this error." | Out-Null
    $wroteEvent = $LASTEXITCODE -eq 0
}

$log = Join-Path $out "collector.log"
$process = Start-Process -FilePath $Collector -ArgumentList $configs -PassThru -NoNewWindow `
    -RedirectStandardError $log

if ($WithIis) {
    Start-Sleep -Seconds 5

    foreach ($path in @("/", "/missing-page")) {
        try {
            Invoke-WebRequest -Uri "http://localhost$path" -UseBasicParsing | Out-Null
        }
        catch {
            # A 404 is what the second request is for.
        }
    }

    # IIS buffers its log for up to a minute; this writes it now.
    & netsh http flush logbuffer | Out-Null
}

Write-Host "Collecting for 45 seconds..."
Start-Sleep -Seconds 45
if ($process.HasExited) {
    Write-Host "The Collector stopped by itself:"
    Get-Content $log -Tail 20 | ForEach-Object { Write-Host $_ }
    exit 1
}

Stop-Process -Id $process.Id -Force
Start-Sleep -Seconds 1

# An event from a provider whose message files are gone, such as an uninstalled driver, arrives without its rendered
# message, and the receiver says so as an error. Real machines have them; they are not the configuration's fault.
$errors = Select-String -Path $log -Pattern '"level":"error"|\terror\t' -SimpleMatch:$false |
    Where-Object { $_.Line -notmatch 'failed to open the metadata for the' }

if ($errors) {
    Write-Host "The Collector logged errors:"
    $errors | Select-Object -First 20 | ForEach-Object { Write-Host $_.Line }

    # The Collector names the file a line came from but not the line, so the site's logs are shown too, with the
    # characters a parser trips over made visible.
    $iisLogs = if ($env:IIS_LOG_DIR) { $env:IIS_LOG_DIR } else { "C:\inetpub\logs\LogFiles" }
    Get-ChildItem -Path $iisLogs -Filter *.log -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "$($_.FullName):"
        # Opened as IIS shares it, still writing, and decoded without dropping a byte order mark.
        $stream = [System.IO.File]::Open($_.FullName, "Open", "Read", "ReadWrite")
        $reader = New-Object System.IO.StreamReader($stream, (New-Object System.Text.UTF8Encoding $false), $false)
        $text = $reader.ReadToEnd()
        $stream.Dispose()
        $text -split "`n" | Select-Object -First 20 | ForEach-Object {
            Write-Host ("  " + ($_ -replace "`r", "<CR>" -replace "`t", "<TAB>" -replace [char]0xFEFF, "<BOM>"))
        }
    }

    exit 1
}

$arguments = @((Join-Path $here "check.mjs"), (Join-Path $out "telemetry.jsonl"))

if ($WithIis) {
    $arguments += "--iis"
}

if ($wroteEvent) {
    $arguments += "--own-event"
}

& node @arguments
exit $LASTEXITCODE
