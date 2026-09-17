param(
    [string]$Server = '.\SQL2022',
    [string]$ApiExecutable = 'backend/src/BackOffice.Api/bin/Release/net10.0/BackOffice.Api.exe',
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
# Run only after stopping the explicitly owned demo preview/worker processes.
# Tests use their own databases. Never reset or expose credentials in the report.
if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Use a fresh preservation evidence directory.' }
New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
function Snapshot([string]$Name) {
    $raw = & sqlcmd -S $Server -E -C -b -d CoverMGA_Demo -i scripts/underwriting-preservation.sql -h -1 -W -s '|' -w 65535
    if ($LASTEXITCODE -ne 0) { throw 'Preservation SQL failed; error output cannot count as evidence.' }
    $rows = @($raw | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($rows.Count -ne 44 -or @($rows | Where-Object { $_ -notmatch '^[A-Za-z0-9]+\|\d+\|[A-F0-9]{64}$' }).Count) {
        throw 'Expected exactly 44 named count/SHA256 records.'
    }
    if (@($rows | ForEach-Object { $_.Split('|')[0] } | Sort-Object -Unique).Count -ne 44) { throw 'Duplicate preservation records.' }
    $rows | Set-Content -LiteralPath (Join-Path $EvidenceDirectory "$Name.txt")
    return $rows
}
$before = Snapshot 'before'
$previousPassword = $env:COVER_DEMO_PASSWORD
$previousConnection = $env:COVER_SQL_CONNECTION
try {
    $env:COVER_DEMO_PASSWORD = (Get-Content -LiteralPath .local/demo-password.txt -Raw).Trim()
    $env:COVER_SQL_CONNECTION = "Server=$Server;Database=CoverMGA_Demo;Integrated Security=true;Encrypt=true;TrustServerCertificate=true"
    foreach ($pass in 1..2) {
        & $ApiExecutable --initialize-demo > (Join-Path $EvidenceDirectory "initialize-$pass.log") 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'Additive initialization failed.' }
        $after = Snapshot "after-$pass"
        if (Compare-Object $before $after) { throw 'A retained count/hash changed during additive initialization.' }
    }
    Write-Output 'Two additive initializations preserved all 44 independently validated count/hash records.'
} finally {
    $env:COVER_DEMO_PASSWORD = $previousPassword
    $env:COVER_SQL_CONNECTION = $previousConnection
}
