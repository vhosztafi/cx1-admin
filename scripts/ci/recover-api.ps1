[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($env:CX1_INSTALL_ROOT) -or
    [string]::IsNullOrWhiteSpace($env:CX1_APP_DIRECTORY) -or
    [string]::IsNullOrWhiteSpace($env:CX1_APP_POOL)) {
    throw 'Set CX1_INSTALL_ROOT, CX1_APP_DIRECTORY, and CX1_APP_POOL'
}
if ($env:CX1_APP_DIRECTORY -notmatch '^[^\\/:*?"<>|.][^\\/:*?"<>|]*$') {
    throw 'CX1_APP_DIRECTORY must be a single directory name'
}
$root = [IO.Path]::GetFullPath($env:CX1_INSTALL_ROOT)
$app = Join-Path $root $env:CX1_APP_DIRECTORY
$backupRoot = Join-Path $root '_teamcity-backups'
Import-Module WebAdministration
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Installation root missing: $root" }
if (-not (Test-Path "IIS:\AppPools\$env:CX1_APP_POOL")) { throw "IIS pool missing: $env:CX1_APP_POOL" }

$backup = $null
if (Test-Path -LiteralPath $backupRoot -PathType Container) {
    $backup = Get-ChildItem -LiteralPath $backupRoot -Directory |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'web.config') -PathType Leaf } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}
Write-Host "Recovery target: $app"
Write-Host "Backup candidate: $(if ($backup) { $backup.FullName } else { '<none>' })"

$state = (Get-WebAppPoolState -Name $env:CX1_APP_POOL).Value
if ($state -ne 'Stopped') { Stop-WebAppPool -Name $env:CX1_APP_POOL }
$deadline = (Get-Date).AddSeconds(30)
while ((Get-WebAppPoolState -Name $env:CX1_APP_POOL).Value -ne 'Stopped' -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
}
if ((Get-WebAppPoolState -Name $env:CX1_APP_POOL).Value -ne 'Stopped') { throw 'IIS pool did not stop for recovery' }

if ($backup) {
    $failed = Join-Path $root ("_teamcity-failed-recovery-{0}" -f [guid]::NewGuid().ToString('N'))
    $movedCurrent = $false
    if (Test-Path -LiteralPath $app -PathType Container) {
        Move-Item -LiteralPath $app -Destination $failed -ErrorAction Stop
        $movedCurrent = $true
        Write-Host "Retained current release at $failed"
    }
    try {
        Move-Item -LiteralPath $backup.FullName -Destination $app -ErrorAction Stop
        Write-Host "Restored previous release from $($backup.FullName)"
    } catch {
        Write-Warning "Could not restore backup: $($_.Exception.Message)"
        if ($movedCurrent -and -not (Test-Path -LiteralPath $app)) {
            Move-Item -LiteralPath $failed -Destination $app -ErrorAction Stop
            Write-Warning 'Put the current release back because backup restoration failed'
        }
        throw
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $app 'web.config') -PathType Leaf)) {
    throw "No configured web.config at $app"
}
Start-WebAppPool -Name $env:CX1_APP_POOL
Write-Host "IIS pool state: $((Get-WebAppPoolState -Name $env:CX1_APP_POOL).Value)"
if ([string]::IsNullOrWhiteSpace($env:CX1_HEALTH_URL) -or [string]::IsNullOrWhiteSpace($env:CX1_ORIGIN_SECRET)) {
    throw 'Health URL or origin secret missing'
}
$healthy = $false
for ($attempt = 1; $attempt -le 6; $attempt++) {
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $env:CX1_HEALTH_URL -Headers @{ 'X-Cx1-Origin-Key' = $env:CX1_ORIGIN_SECRET } -TimeoutSec 15
        if ($response.StatusCode -eq 200) { $healthy = $true; break }
        Write-Warning "Recovery health attempt $attempt returned $($response.StatusCode)"
    } catch {
        $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { $_.Exception.Message }
        Write-Warning "Recovery health attempt $attempt failed: $status"
    }
    Start-Sleep -Seconds 5
}
if (-not $healthy) { throw 'Recovered files and started pool, but protected health did not pass' }
Write-Host 'Previous API release is healthy'
