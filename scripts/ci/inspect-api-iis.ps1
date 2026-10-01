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
    $webConfig = Join-Path $path 'web.config'
    if (Test-Path -LiteralPath $webConfig -PathType Leaf) {
        [xml]$config = Get-Content -LiteralPath $webConfig -Raw
        $variables = @($config.SelectNodes('//aspNetCore/environmentVariables/environmentVariable'))
        Write-Host ("Configured environment names: " + (($variables | ForEach-Object { $_.GetAttribute('name') } | Sort-Object) -join ', '))
        $keysPath = $variables | Where-Object { $_.GetAttribute('name') -eq 'Cover__DataProtectionPath' } | Select-Object -First 1
        if ($keysPath) {
            $keysDirectory = [IO.Path]::GetFullPath($keysPath.GetAttribute('value'))
            Write-Host "Data Protection path: $keysDirectory"
            Write-Host "Data Protection directory exists: $(Test-Path -LiteralPath $keysDirectory -PathType Container)"
        }
    }
    Write-Host ("Top-level files: " + ((Get-ChildItem -LiteralPath $path -File | Select-Object -ExpandProperty Name | Sort-Object) -join ', '))
    $acl = Get-Acl -LiteralPath $path
    foreach ($rule in $acl.Access) {
        Write-Host ("ACL: {0} | {1} | inherited={2} | propagation={3}" -f
            $rule.IdentityReference, $rule.FileSystemRights, $rule.IsInherited, $rule.InheritanceFlags)
    }
}
$documentedKeys = 'C:\ProgramData\Cx1AdminDev\keys'
Write-Host "Documented key directory exists: $(Test-Path -LiteralPath $documentedKeys -PathType Container)"
Write-Host "Agent ASPNETCORE_ENVIRONMENT: $env:ASPNETCORE_ENVIRONMENT"
Write-Host "Agent DOTNET_ENVIRONMENT: $env:DOTNET_ENVIRONMENT"
foreach ($candidate in @(
    (Join-Path $app '.local\data-protection'),
    (Join-Path $root 'keys'),
    (Join-Path $root 'data-protection'),
    'C:\ProgramData\cx1-admin-api-dev.gyongyos.co.uk\keys'
)) {
    $exists = Test-Path -LiteralPath $candidate -PathType Container
    $count = if ($exists) { @(Get-ChildItem -LiteralPath $candidate -File -Filter '*.xml').Count } else { 0 }
    Write-Host "Key candidate: $candidate | exists=$exists | XML files=$count"
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
