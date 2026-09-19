param(
    [string]$Server = '.\SQL2022',
    [string]$ApiExecutable = 'backend/src/BackOffice.Api/bin/Release/net10.0/BackOffice.Api.exe',
    [string]$SqlCommand = 'sqlcmd',
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
# Stop verified owned demo previews/workers before this command. It never resets
# data or stops arbitrary processes; initialization is additive and runs twice.
if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Use a fresh evidence directory.' }
$executable = (Resolve-Path -LiteralPath $ApiExecutable).Path
New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
$started = [DateTimeOffset]::UtcNow.ToString('o')
function Snapshot([string]$Name) {
    $raw = & $SqlCommand -S $Server -E -C -b -d CoverMGA_Demo -i scripts/servicing-preservation.sql -h -1 -W -s '|' -w 65535
    if ($LASTEXITCODE -ne 0) { throw 'Preservation SQL failed.' }
    $rows = @($raw | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($rows.Count -lt 133 -or @($rows | Where-Object { $_ -notmatch '^[A-Za-z0-9_]+\.[A-Za-z0-9_]+\|\d+\|[A-F0-9]{64}$' }).Count) {
        throw 'Expected all current tables (at least 133), each with count and SHA256.'
    }
    if (@($rows | ForEach-Object { $_.Split('|')[0] } | Sort-Object -Unique).Count -ne $rows.Count) { throw 'Duplicate table fingerprints.' }
    $rows | Set-Content -LiteralPath (Join-Path $EvidenceDirectory "$Name.txt")
    return $rows
}
$before = Snapshot 'before'
$priorPassword = $env:COVER_DEMO_PASSWORD
$priorConnection = $env:COVER_SQL_CONNECTION
try {
    $env:COVER_DEMO_PASSWORD = (Get-Content -LiteralPath .local/demo-password.txt -Raw).Trim()
    $env:COVER_SQL_CONNECTION = "Server=$Server;Database=CoverMGA_Demo;Integrated Security=true;Encrypt=true;TrustServerCertificate=true"
    foreach ($pass in 1..2) {
        & $executable --initialize-demo > (Join-Path $EvidenceDirectory "initialize-$pass.log") 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'Additive initialization failed.' }
        $after = Snapshot "after-$pass"
        if (Compare-Object $before $after) { throw 'A retained table count/hash changed during initialization.' }
    }
    @{ startedAt=$started; finishedAt=[DateTimeOffset]::UtcNow.ToString('o'); passed=$true; tableCount=$before.Count; initializations=2 } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'report.json')
    Write-Output "Two additive initializations preserved all $($before.Count) table fingerprints."
} finally {
    $env:COVER_DEMO_PASSWORD = $priorPassword
    $env:COVER_SQL_CONNECTION = $priorConnection
}
