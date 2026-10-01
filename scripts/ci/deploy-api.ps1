[CmdletBinding()]
param([switch]$ValidateOnly, [string]$ArtifactDirectory = '')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not $ArtifactDirectory) { $ArtifactDirectory = Join-Path $repo 'incoming' }
$zip = Join-Path $ArtifactDirectory 'api.zip'
$manifestFile = Join-Path $ArtifactDirectory 'api-manifest.json'
foreach ($file in @($zip, $manifestFile)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing deployment artifact: $file" }
}
$manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
if ($manifest.sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or $manifest.revision -notmatch '^[A-Fa-f0-9]{40}$') { throw 'Invalid artifact manifest' }
if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $manifest.sha256) { throw 'Artifact checksum mismatch' }
Push-Location $repo
try { $revision = (& git rev-parse HEAD).Trim(); if ($LASTEXITCODE -ne 0) { throw 'Cannot determine checkout revision' } }
finally { Pop-Location }
if ($revision -ne $manifest.revision) { throw 'Artifact and deployment checkout revisions differ' }
if ([string]::IsNullOrWhiteSpace($env:CX1_INSTALL_ROOT) -or [string]::IsNullOrWhiteSpace($env:CX1_APP_DIRECTORY) -or [string]::IsNullOrWhiteSpace($env:CX1_APP_POOL)) { throw 'Set CX1_INSTALL_ROOT, CX1_APP_DIRECTORY, and CX1_APP_POOL' }
$root = [IO.Path]::GetFullPath($env:CX1_INSTALL_ROOT)
if ($root -eq [IO.Path]::GetPathRoot($root) -or $root.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Installation root must be an explicit server directory outside checkout' }
if ($env:CX1_APP_DIRECTORY -notmatch '^[^\\/:*?"<>|.][^\\/:*?"<>|]*$' -or $env:CX1_APP_DIRECTORY -eq '..') { throw 'CX1_APP_DIRECTORY must be a single directory name' }
$app = Join-Path $root $env:CX1_APP_DIRECTORY
if ($ValidateOnly) { Write-Host "Deployment preflight passed for $revision; server untouched."; return }
if ([string]::IsNullOrWhiteSpace($env:CX1_ORIGIN_SECRET) -or [string]::IsNullOrWhiteSpace($env:CX1_HEALTH_URL)) { throw 'Set secured CX1_ORIGIN_SECRET and CX1_HEALTH_URL' }
$uri = [uri]$env:CX1_HEALTH_URL
if ($uri.Scheme -ne 'https' -or $uri.AbsolutePath -ne '/health/live') { throw 'Health URL must be HTTPS /health/live' }
Import-Module WebAdministration
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'Existing IIS installation root is missing' }
if (-not (Test-Path "IIS:\AppPools\$env:CX1_APP_POOL")) { throw 'IIS application pool is missing' }
if (-not (Test-Path -LiteralPath (Join-Path $app 'web.config') -PathType Leaf)) { throw 'Existing configured web.config is missing' }
$stamp = [guid]::NewGuid().ToString('N')
$stage = Join-Path $root "_teamcity-stage-$stamp"
$backupRoot = Join-Path $root '_teamcity-backups'
$backup = Join-Path $backupRoot $stamp
$swapped = $false
try {
    New-Item -ItemType Directory -Path $stage,$backupRoot -Force | Out-Null
    Expand-Archive -LiteralPath $zip -DestinationPath $stage
    $release = Join-Path $stage 'app'
    if (-not (Test-Path -LiteralPath (Join-Path $release 'BackOffice.Api.exe') -PathType Leaf)) { throw 'Package has no API executable' }
    $inner = Get-Content -LiteralPath (Join-Path $stage 'manifest.json') -Raw | ConvertFrom-Json
    if ($inner.revision.Trim() -ne $revision -or $inner.workingTreeChanges -ne $false) { throw 'Package revision or clean-checkout check failed' }
    Copy-Item -LiteralPath (Join-Path $app 'web.config') -Destination (Join-Path $release 'web.config') -Force
    Stop-WebAppPool -Name $env:CX1_APP_POOL -ErrorAction Stop
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-WebAppPoolState -Name $env:CX1_APP_POOL).Value -ne 'Stopped' -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    if ((Get-WebAppPoolState -Name $env:CX1_APP_POOL).Value -ne 'Stopped') { throw 'Application pool did not stop' }
    Move-Item -LiteralPath $app -Destination $backup
    try { Move-Item -LiteralPath $release -Destination $app; $swapped = $true }
    catch { Move-Item -LiteralPath $backup -Destination $app; throw }
    Start-WebAppPool -Name $env:CX1_APP_POOL
    $healthy = $false
    for ($attempt = 0; $attempt -lt 12; $attempt++) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $uri -Headers @{ 'X-Cx1-Origin-Key' = $env:CX1_ORIGIN_SECRET } -TimeoutSec 15
            if ($response.StatusCode -eq 200) { $healthy = $true; break }
        } catch { Write-Warning ("Health attempt {0}/12 failed ({1})" -f ($attempt + 1), $_.Exception.GetType().Name) }
        Start-Sleep -Seconds 5
    }
    if (-not $healthy) { throw 'Protected API health check failed' }
    Write-Host "API deployment healthy at revision $revision; previous app retained at $backup"
} catch {
    $failure = $_
    if ($swapped) {
        try {
            if ((Get-WebAppPoolState -Name $env:CX1_APP_POOL).Value -ne 'Stopped') { Stop-WebAppPool -Name $env:CX1_APP_POOL }
            $failed = Join-Path $root "_teamcity-failed-$stamp"
            Move-Item -LiteralPath $app -Destination $failed
            Move-Item -LiteralPath $backup -Destination $app
            Start-WebAppPool -Name $env:CX1_APP_POOL
            Write-Warning "Previous application restored; failed release retained at $failed"
        } catch { Write-Warning 'Automatic application restoration failed; inspect IIS and retained directories immediately.' }
    } elseif (Test-Path -LiteralPath $app -PathType Container) {
        try {
            if ((Get-WebAppPoolState -Name $env:CX1_APP_POOL).Value -eq 'Stopped') { Start-WebAppPool -Name $env:CX1_APP_POOL }
        } catch { Write-Warning 'Application pool restart failed.' }
    }
    throw $failure
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
