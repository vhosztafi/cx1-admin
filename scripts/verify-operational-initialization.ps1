param(
    [Parameter(Mandatory=$true)][string]$ApiPath,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory
)
$ErrorActionPreference='Stop'
$workspace=(Resolve-Path .).Path
$binary=(Resolve-Path -LiteralPath $ApiPath).Path
if(!$binary.StartsWith((Join-Path $workspace '.local')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($binary) -ne 'BackOffice.Api.exe'){throw 'Use an owned local API build.'}
if(Test-Path -LiteralPath $EvidenceDirectory){throw 'Use a fresh evidence directory.'}
if(!(Test-Path -LiteralPath '.local/demo-password.txt')){throw 'Retained demo password is missing.'}
if(Get-NetTCPConnection -LocalPort 5087 -State Listen -ErrorAction SilentlyContinue){throw 'Stop the verified owned preview API workers before preserving the database.'}
New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
$started=[DateTimeOffset]::UtcNow.ToString('o')
function Snapshot([string]$Name){
    $directory=Join-Path $EvidenceDirectory $Name
    & "$PSScriptRoot/verify-retained-upgrade.ps1" -Mode Capture -EvidenceDirectory $directory -FileRoots @('.local/data-protection','.local/operational-files')
    $snapshot=Get-Content -LiteralPath (Join-Path $directory 'baseline.json') -Raw | ConvertFrom-Json
    $lines=[Collections.Generic.List[string]]::new()
    foreach($table in $snapshot.tables | Sort-Object name){
        $lines.Add("table:$($table.name):$($table.cols):$($table.keys):$($table.rows.Count)")
        foreach($row in $table.rows | Sort-Object id){$lines.Add("$($row.id):$($row.hash)")}
    }
    foreach($file in $snapshot.files | Sort-Object path){$lines.Add("file:$($file.path):$($file.hash)")}
    $sha=[Security.Cryptography.SHA256]::Create()
    try{$fingerprint=[Convert]::ToHexString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($lines -join "`n")))}finally{$sha.Dispose()}
    return [pscustomobject]@{fingerprint=$fingerprint;tables=$snapshot.tables.Count;rows=($snapshot.tables | ForEach-Object {$_.rows.Count} | Measure-Object -Sum).Sum;files=$snapshot.files.Count}
}
$env:COVER_SQL_CONNECTION='Server=.\SQL2022;Database=CoverMGA_Demo;Integrated Security=true;Encrypt=true;TrustServerCertificate=true'
$env:COVER_DEMO_PASSWORD=(Get-Content -LiteralPath '.local/demo-password.txt' -Raw).Trim()
try{
    # The capture helper emits progress before the final summary object;
    # retain that final object for strict equality below.
    $before=@(Snapshot 'before')[-1]
    $after=@()
    foreach($number in 1,2){
        & $binary --initialize-demo *> (Join-Path $EvidenceDirectory "initialize-$number.log")
        if($LASTEXITCODE -ne 0){throw "Initialization $number failed; inspect its local log."}
        $current=@(Snapshot "after-$number")[-1]
        if($current.fingerprint -cne $before.fingerprint){throw "Initialization $number changed retained rows, table structure or file bytes."}
        $after+=$current
    }
    @{passed=$true;startedAt=$started;finishedAt=[DateTimeOffset]::UtcNow.ToString('o');initializations=2;before=$before;after=$after} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'report.json')
    Write-Output "Two initializations preserved $($before.rows) rows across $($before.tables) business tables and $($before.files) files exactly."
}finally{Remove-Item Env:COVER_DEMO_PASSWORD}
