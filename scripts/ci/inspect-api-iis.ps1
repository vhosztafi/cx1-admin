[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($env:CX1_INSTALL_ROOT)
$app = Join-Path $root $env:CX1_APP_DIRECTORY
$failed = Get-ChildItem -LiteralPath $root -Directory -Filter '_teamcity-failed-*' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
Import-Module WebAdministration
Write-Host "Pool $env:CX1_APP_POOL state: $((Get-WebAppPoolState -Name $env:CX1_APP_POOL).Value)"
foreach ($path in @($app, $failed.FullName)) {
    if (-not $path -or -not (Test-Path -LiteralPath $path -PathType Container)) { continue }
    Write-Host "Directory: $path"
    Write-Host "Executable present: $(Test-Path -LiteralPath (Join-Path $path 'BackOffice.Api.exe') -PathType Leaf)"
    Write-Host "Web config present: $(Test-Path -LiteralPath (Join-Path $path 'web.config') -PathType Leaf)"
    Write-Host ("Top-level files: " + ((Get-ChildItem -LiteralPath $path -File | Select-Object -ExpandProperty Name | Sort-Object) -join ', '))
    $acl = Get-Acl -LiteralPath $path
    foreach ($rule in $acl.Access) {
        Write-Host ("ACL: {0} | {1} | inherited={2} | propagation={3}" -f
            $rule.IdentityReference, $rule.FileSystemRights, $rule.IsInherited, $rule.InheritanceFlags)
    }
}
$events = Get-WinEvent -FilterHashtable @{ LogName='Application'; StartTime=(Get-Date).AddHours(-4) } -ErrorAction SilentlyContinue |
    Where-Object { $_.ProviderName -match 'IIS|ASP.NET Core|\.NET Runtime|Application Error' -and
        $_.Message -match 'cx1-admin-api-dev\.gyongyos\.co\.uk|BackOffice\.Api' } |
    Select-Object -First 30
foreach ($event in $events) {
    $lines = ($event.Message -split "[\r\n]+") | Where-Object { $_.Trim() } | Select-Object -First 3
    $summary = $lines -join ' / '
    if ($summary -match '(?i)secret|password|connection|token|key=') { $summary = '<redacted>' }
    if ($summary.Length -gt 500) { $summary = $summary.Substring(0, 500) }
    Write-Host ("Event: {0:o} | {1} | {2} | {3} | {4}" -f $event.TimeCreated, $event.ProviderName, $event.Id, $event.LevelDisplayName, $summary)
}
