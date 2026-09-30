[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$out = Join-Path $repo 'artifacts/teamcity-api'
function Run([string]$Exe, [string[]]$Arguments) {
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Exe failed with exit code $LASTEXITCODE" }
}
Push-Location $repo
try {
    if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
    New-Item -ItemType Directory -Path $out -Force | Out-Null
    Run 'dotnet' @('restore', 'backend/src/BackOffice.Api/BackOffice.Api.csproj', '--configfile', 'NuGet.Config', '--packages', '.local/nuget')
    Run 'dotnet' @('build', 'backend/src/BackOffice.Infrastructure/BackOffice.Infrastructure.csproj', '-c', 'Release', '--no-restore')
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
} finally { Pop-Location }
