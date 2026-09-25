param(
    [string]$ApiDll = 'backend/src/BackOffice.Api/bin/Debug/net10.0/BackOffice.Api.dll',
    [string]$EvidenceDirectory = '.local/phase10-final/preservation'
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Use a fresh finance preservation evidence directory.' }
if (!(Test-Path -LiteralPath $ApiDll -PathType Leaf) -or !(Test-Path -LiteralPath '.local/demo-password.txt' -PathType Leaf)) {
    throw 'The built API DLL or retained demo password is missing.'
}
New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
$oldConnection = $env:COVER_SQL_CONNECTION
$oldPassword = $env:COVER_DEMO_PASSWORD
$env:COVER_SQL_CONNECTION = 'Server=.\SQL2022;Database=CoverMGA_Demo;Integrated Security=true;Encrypt=true;TrustServerCertificate=true'
$env:COVER_DEMO_PASSWORD = (Get-Content -LiteralPath '.local/demo-password.txt' -Raw).Trim()

function Snapshot([string]$name) {
    $directory = Join-Path $EvidenceDirectory $name
    & "$PSScriptRoot/verify-retained-upgrade.ps1" -Mode Capture -EvidenceDirectory $directory -FileRoots @('.local/data-protection', '.local/operational-files') | Out-Null
    $snapshot = Get-Content -LiteralPath (Join-Path $directory 'baseline.json') -Raw | ConvertFrom-Json
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($table in $snapshot.tables | Sort-Object name) {
        $lines.Add("table:$($table.name):$($table.cols):$($table.keys):$($table.rows.Count)")
        foreach ($row in $table.rows | Sort-Object id) { $lines.Add("$($row.id):$($row.hash)") }
    }
    foreach ($file in $snapshot.files | Sort-Object path) { $lines.Add("file:$($file.path):$($file.hash)") }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [Convert]::ToHexString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($lines -join "`n"))) }
    finally { $sha.Dispose() }
    $refundRequests = @($snapshot.tables | Where-Object name -eq '[dbo].[RefundRequest]')
    $refundPayments = @($snapshot.tables | Where-Object name -eq '[dbo].[FinanceRefundPayment]')
    if ($refundRequests.Count -ne 1 -or $refundPayments.Count -ne 1) { throw 'Expected retained refund and payment tables are missing.' }
    return [pscustomobject]@{
        hash = $hash
        tables = $snapshot.tables.Count
        rows = ($snapshot.tables | ForEach-Object { $_.rows.Count } | Measure-Object -Sum).Sum
        files = $snapshot.files.Count
        refundRequests = @($refundRequests[0].rows).Count
        refundPayments = @($refundPayments[0].rows).Count
    }
}

function MigrationState {
    $connection = [System.Data.SqlClient.SqlConnection]::new($env:COVER_SQL_CONNECTION)
    $connection.Open()
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = 'SELECT COUNT(*), MAX(MigrationId) FROM __EFMigrationsHistory'
        $reader = $command.ExecuteReader()
        try {
            if (!$reader.Read()) { throw 'Migration history unavailable.' }
            return [pscustomobject]@{ count = $reader.GetInt32(0); head = $reader.GetString(1) }
        } finally { $reader.Dispose(); $command.Dispose() }
    } finally { $connection.Dispose() }
}

try {
    $before = Snapshot 'before'
    $migration = MigrationState
    $after = @()
    foreach ($pass in 1, 2) {
        & dotnet $ApiDll --initialize-demo *> (Join-Path $EvidenceDirectory "initialize-$pass.log")
        if ($LASTEXITCODE -ne 0) { throw "Additive initialization $pass failed; inspect its local log." }
        $current = Snapshot "after-$pass"
        if ($current.hash -cne $before.hash) { throw "Additive initialization $pass changed retained rows, table structure or file bytes." }
        $currentMigration = MigrationState
        if ($currentMigration.count -ne $migration.count -or $currentMigration.head -cne $migration.head) {
            throw "Additive initialization $pass changed migration history."
        }
        $after += $current
    }
    [ordered]@{
        passed = $true
        checkedAt = [DateTimeOffset]::UtcNow.ToString('o')
        initializations = 2
        migration = $migration
        before = $before
        after = $after
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'report.json')
    Write-Output "Two additive initializations preserved $($before.rows) rows across $($before.tables) tables and $($before.files) files exactly."
} finally {
    if ($null -eq $oldConnection) { Remove-Item Env:COVER_SQL_CONNECTION -ErrorAction SilentlyContinue } else { $env:COVER_SQL_CONNECTION = $oldConnection }
    if ($null -eq $oldPassword) { Remove-Item Env:COVER_DEMO_PASSWORD -ErrorAction SilentlyContinue } else { $env:COVER_DEMO_PASSWORD = $oldPassword }
}
