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
    $acl = Get-Acl -LiteralPath $path
    foreach ($rule in $acl.Access) {
        Write-Host ("ACL: {0} | {1} | inherited={2} | propagation={3}" -f
            $rule.IdentityReference, $rule.FileSystemRights, $rule.IsInherited, $rule.InheritanceFlags)
    }
}
$events = Get-WinEvent -FilterHashtable @{ LogName='Application'; StartTime=(Get-Date).AddHours(-2) } -ErrorAction SilentlyContinue |
    Where-Object { $_.ProviderName -match 'IIS|ASP.NET Core|\.NET Runtime|Application Error' } |
    Select-Object -First 20
foreach ($event in $events) {
    $firstLine = ($event.Message -split "[\r\n]+", 2)[0]
    if ($firstLine -match '(?i)secret|password|connection|token|key=') { $firstLine = '<redacted>' }
    if ($firstLine.Length -gt 240) { $firstLine = $firstLine.Substring(0, 240) }
    Write-Host ("Event: {0:o} | {1} | {2} | {3}" -f $event.TimeCreated, $event.ProviderName, $event.Id, $firstLine)
}
