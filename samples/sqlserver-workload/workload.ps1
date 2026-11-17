<#
.SYNOPSIS
Keeps a SQL Server busy for a few minutes, so Veyra's SQL Server screen has something to show.

.DESCRIPTION
Creates one database, VeyraWorkload, with two small tables, and touches nothing else. Then, at the same time:
- a transaction holds a lock, and three sessions wait behind it: a blocking chain on the Live and Blocking tabs;
- one session keeps running a CPU-heavy query: a long request on the Live tab, a busy statement on the Queries tab;
- once a minute, two transactions take the same two rows in opposite order: a deadlock on the Blocking tab.
Every session names itself veyra-workload. Remove the database afterwards with -Cleanup.

.PARAMETER Server
The SQL Server, as SSMS names it, for example localhost or .\SQLEXPRESS.

.PARAMETER Minutes
How long the workload runs.

.PARAMETER Credential
A SQL login to sign in with; without it, the script signs in as you, with Windows authentication.

.PARAMETER Cleanup
Drops the VeyraWorkload database instead of running the workload.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File samples\sqlserver-workload\workload.ps1 -Server localhost -Minutes 3
#>
param(
    [string] $Server = "localhost",

    [ValidateRange(1, 30)]
    [int] $Minutes = 3,

    [pscredential] $Credential,

    [switch] $Cleanup
)

$ErrorActionPreference = "Stop"
$database = "VeyraWorkload"

# One connection string per database; the server's certificate is trusted, as for a server on this machine.
function Get-ConnectionString([string] $Catalog) {
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder["Data Source"] = $Server
    $builder["Initial Catalog"] = $Catalog
    $builder["Application Name"] = "veyra-workload"
    $builder["Encrypt"] = $true
    $builder["TrustServerCertificate"] = $true

    if ($Credential) {
        $builder["User ID"] = $Credential.UserName
        $builder["Password"] = $Credential.GetNetworkCredential().Password
    }
    else {
        $builder["Integrated Security"] = $true
    }

    return $builder.ConnectionString
}

# Runs one batch on its own connection, after a pause; returns the error, such as a deadlock, instead of throwing it.
$runBatch = {
    param([string] $ConnectionString, [string] $Sql, [int] $DelaySeconds)

    Start-Sleep -Seconds $DelaySeconds
    $connection = New-Object System.Data.SqlClient.SqlConnection $ConnectionString

    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 0
        $command.CommandText = $Sql
        [void] $command.ExecuteNonQuery()
    }
    catch {
        $_.Exception.GetBaseException().Message
    }
    finally {
        $connection.Dispose()
    }
}

$master = Get-ConnectionString "master"
$workload = Get-ConnectionString $database

if ($Cleanup) {
    & $runBatch $master @"
IF DB_ID(N'$database') IS NOT NULL
BEGIN
    ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$database];
END;
"@ 0
    Write-Host "Dropped $database on $Server."

    return
}

$failure = & $runBatch $master "IF DB_ID(N'$database') IS NULL CREATE DATABASE [$database];" 0

if ($failure) {
    throw "Could not create $database on ${Server}: $failure"
}

$failure = & $runBatch $workload @"
IF OBJECT_ID(N'dbo.accounts') IS NULL
BEGIN
    CREATE TABLE dbo.accounts (id int PRIMARY KEY, balance money NOT NULL);
    INSERT dbo.accounts (id, balance)
    SELECT TOP (1000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 100 FROM sys.all_objects;
END;
IF OBJECT_ID(N'dbo.orders') IS NULL
BEGIN
    CREATE TABLE dbo.orders (id int PRIMARY KEY, total money NOT NULL);
    INSERT dbo.orders (id, total)
    SELECT TOP (1000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)), 10 FROM sys.all_objects;
END;
"@ 0

if ($failure) {
    throw "Could not create the tables in ${database}: $failure"
}

$seconds = $Minutes * 60
$hold = [TimeSpan]::FromSeconds($seconds).ToString("hh\:mm\:ss")
$jobs = @()

function Start-Batch([string] $Sql, [int] $DelaySeconds = 0) {
    $script:jobs += Start-Job -ScriptBlock $runBatch -ArgumentList $workload, $Sql, $DelaySeconds
}

# The blocking chain: one transaction holds account 1; two readers and a writer wait behind it.
Start-Batch "BEGIN TRAN; UPDATE dbo.accounts SET balance = balance + 1 WHERE id = 1; WAITFOR DELAY '$hold'; ROLLBACK;"
Start-Batch "SELECT balance FROM dbo.accounts WHERE id = 1;" 5
Start-Batch "SELECT SUM(balance) FROM dbo.accounts;" 5
Start-Batch "UPDATE dbo.accounts SET balance = balance - 1 WHERE id = 1;" 5

# The long request: a CPU-heavy query, run again and again until the time is up.
Start-Batch @"
DECLARE @end datetime2 = DATEADD(SECOND, $seconds, SYSDATETIME()), @rows bigint;
WHILE SYSDATETIME() < @end
    SELECT @rows = COUNT_BIG(*) FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b
    WHERE a.object_id % 7 = b.object_id % 11;
"@

# A deadlock once a minute: order 1 and account 2 taken in opposite order; SQL Server ends one as the victim.
for ($minute = 0; $minute -lt $Minutes; $minute++) {
    $at = 20 + 60 * $minute
    Start-Batch @"
BEGIN TRAN;
UPDATE dbo.orders SET total = total + 1 WHERE id = 1;
WAITFOR DELAY '00:00:05';
UPDATE dbo.accounts SET balance = balance + 1 WHERE id = 2;
COMMIT;
"@ $at
    Start-Batch @"
BEGIN TRAN;
UPDATE dbo.accounts SET balance = balance + 1 WHERE id = 2;
WAITFOR DELAY '00:00:05';
UPDATE dbo.orders SET total = total + 1 WHERE id = 1;
COMMIT;
"@ ($at + 1)
}

Write-Host "Running on $Server for $Minutes minutes. Open Veyra's SQL Server screen: the Live tab shows the work now."
$jobs | Wait-Job | Out-Null

$results = $jobs | Receive-Job
$jobs | Remove-Job
$deadlocks = @($results | Where-Object { $_ -match "deadlock" }).Count
$others = @($results | Where-Object { $_ -and $_ -notmatch "deadlock" })

Write-Host "Done: $deadlocks deadlock victims, as planned."
Write-Host "Remove the database with -Cleanup when you no longer need it."

foreach ($other in $others) {
    Write-Warning $other
}
