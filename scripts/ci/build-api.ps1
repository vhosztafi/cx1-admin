[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$out = Join-Path $repo 'artifacts/teamcity-api'
$sqlInstance = 'CoverMGA_TeamCity_Cx1'
$sqlStarted = $false
function Run([string]$Exe, [string[]]$Arguments) {
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Exe failed with exit code $LASTEXITCODE" }
}
Push-Location $repo
try {
    if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
    New-Item -ItemType Directory -Path (Join-Path $out 'test-results') -Force | Out-Null
    Run 'dotnet' @('restore', 'backend/BackOffice.slnx', '--configfile', 'NuGet.Config', '--packages', '.local/nuget')
    if (-not (Get-Command 'SqlLocalDB.exe' -ErrorAction SilentlyContinue)) {
        throw 'SQL Server 2022+ LocalDB is required on the TeamCity agent for API integration tests.'
    }
    $versions = & SqlLocalDB.exe versions
    if ($LASTEXITCODE -ne 0) { throw 'Could not query SQL LocalDB versions.' }
    $supported = $versions | ForEach-Object {
        if ($_ -match '^\d+\.\d+(?:\.\d+)*$') { [version]$_ }
        elseif ($_ -match '\((\d+\.\d+(?:\.\d+)*)\)') { [version]$Matches[1] }
    } | Where-Object { $_.Major -ge 16 } | Sort-Object -Descending
    if (-not $supported) { throw 'SQL Server 2022+ LocalDB is required; SQL integration tests will not be skipped.' }
    & SqlLocalDB.exe info $sqlInstance *> $null
    if ($LASTEXITCODE -ne 0) { Run 'SqlLocalDB.exe' @('create', $sqlInstance, $supported[0].ToString()) }
    Run 'SqlLocalDB.exe' @('start', $sqlInstance)
    $sqlStarted = $true
    $env:COVER_SQL_TEST_CONNECTION = "Server=(localdb)\$sqlInstance;Database=CoverMGA_Demo;Integrated Security=true;Encrypt=true;TrustServerCertificate=true"
    Run 'dotnet' @('test', 'backend/BackOffice.slnx', '-c', 'Release', '--no-restore', '--logger', 'trx', '--results-directory', (Join-Path $out 'test-results'))
    Run 'dotnet' @('tool', 'restore')
    Run 'dotnet' @('ef', 'migrations', 'script', '--idempotent', '--project', 'backend/src/BackOffice.Infrastructure', '--startup-project', 'backend/src/BackOffice.Infrastructure', '--configuration', 'Release', '--no-build', '-o', (Join-Path $out 'migrations.sql'))
    $packageStart = Get-Date
    & (Join-Path $repo 'scripts/deploy/package-api.ps1')
    if (-not $?) { throw 'API packaging failed' }
    $package = Get-ChildItem -LiteralPath (Join-Path $repo 'artifacts') -Filter 'cx1-admin-api-dev-*.zip' -File |
        Where-Object { $_.LastWriteTime -ge $packageStart.AddSeconds(-2) } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $package) { throw 'API package missing' }
    Copy-Item -LiteralPath $package.FullName -Destination (Join-Path $out 'api.zip')
    $revision = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine Git revision' }
    @{ revision = $revision; sha256 = (Get-FileHash -LiteralPath (Join-Path $out 'api.zip') -Algorithm SHA256).Hash } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'api-manifest.json') -Encoding ascii
} finally {
    if ($sqlStarted) {
        & SqlLocalDB.exe stop $sqlInstance
        if ($LASTEXITCODE -ne 0) { Write-Warning "Could not stop owned SQL LocalDB instance $sqlInstance" }
    }
    Pop-Location
}
