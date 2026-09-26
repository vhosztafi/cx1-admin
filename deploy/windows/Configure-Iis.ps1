[CmdletBinding()]
param(
  [string]$SiteName = 'Cx1AdminDev',
  [string]$HostName = 'cx1-admin-api-dev.gyongyos.co.uk',
  [string]$InstallRoot = 'C:\Sites\Cx1AdminDev',
  [string]$DataRoot = 'C:\ProgramData\Cx1AdminDev',
  [string]$SqlConnection = 'Server=.\sql2022;Database=Cx1_Dev;Integrated Security=true;Encrypt=true;TrustServerCertificate=true',
  [string]$CertificateThumbprint,
  [switch]$Tunnel,
  [int]$TunnelPort = 5096,
  [Security.SecureString]$OriginSecret
)
$ErrorActionPreference = 'Stop'
# Run in elevated Windows PowerShell 5.1 on the destination server.
Import-Module WebAdministration
if (Test-Path "IIS:\Sites\$SiteName") { throw 'Site already exists. Use the documented update procedure; this installer does not overwrite an existing site.' }
if (Test-Path "IIS:\AppPools\$SiteName") { throw 'App pool already exists; use a dedicated new pool or the update procedure.' }
if (!(Get-WebGlobalModule | Where-Object Name -eq 'AspNetCoreModuleV2')) { throw 'Install the .NET 10 Hosting Bundle / ASP.NET Core IIS module first.' }
if (!$Tunnel -and (!$CertificateThumbprint -or !(Test-Path "Cert:\LocalMachine\My\$CertificateThumbprint"))) { throw 'Supply the installed HTTPS certificate thumbprint, or choose -Tunnel with a loopback-only listener.' }
if (!$OriginSecret) { $OriginSecret = Read-Host 'Worker BACKOFFICE_ORIGIN_SECRET (from private credentials file)' -AsSecureString }
$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($OriginSecret)
try { $originValue = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
if ($originValue.Length -lt 32) { throw 'Origin secret must be at least 32 characters.' }
$appPath = Join-Path $InstallRoot 'app'
if (Test-Path $appPath) { throw 'Install app directory already exists; no files were replaced.' }
$packageApp = Join-Path $PSScriptRoot 'app'
if (!(Test-Path (Join-Path $packageApp 'BackOffice.Api.exe'))) { throw 'Extract the complete deployment ZIP first.' }
New-Item -ItemType Directory -Force $InstallRoot,$DataRoot | Out-Null
# Secrets, keys and documents are private to administrators, SYSTEM and this pool.
& icacls.exe $InstallRoot /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE) { throw 'Install directory ACL failed.' }
& icacls.exe $DataRoot /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE) { throw 'Data directory ACL failed.' }
Copy-Item -LiteralPath $packageApp -Destination $appPath -Recurse
New-Item -ItemType Directory -Force (Join-Path $DataRoot 'keys'),(Join-Path $DataRoot 'files') | Out-Null
$configPath = Join-Path $appPath 'web.config'
[xml]$config = Get-Content -LiteralPath $configPath -Raw
$asp = $config.configuration.location.'system.webServer'.aspNetCore
$envs = $config.CreateElement('environmentVariables')
$settings = @{
 ASPNETCORE_ENVIRONMENT='Staging'; 'Cover__SqlConnection'=$SqlConnection; 'Cover__DataProtectionPath'=(Join-Path $DataRoot 'keys');
 'Cover__FileStoragePath'=(Join-Path $DataRoot 'files'); 'Cover__OriginSecret'=$originValue;
 'Cover__HostedDemoEnabled'='true'; 'Cover__DiagnosticWorkerEnabled'='false';
 'Cover__TrustGatewayHttps'=$Tunnel.IsPresent.ToString().ToLowerInvariant();
 'Cover__FileWorkerEnabled'='true'; 'Cover__DocumentWorkerEnabled'='true'; 'Cover__WorkflowTaskWorkerEnabled'='true'; 'Cover__RenewalLifecycleWorkerEnabled'='true';
 'AllowedHosts'=$HostName; 'Logging__LogLevel__Default'='Warning'
}
foreach($name in $settings.Keys) { $item=$config.CreateElement('environmentVariable');$item.SetAttribute('name',$name);$item.SetAttribute('value',$settings[$name]);[void]$envs.AppendChild($item) }
if ($asp.environmentVariables) { [void]$asp.RemoveChild($asp.environmentVariables) }
[void]$asp.AppendChild($envs);$config.Save($configPath);$originValue=$null
New-WebAppPool -Name $SiteName | Out-Null
Set-ItemProperty "IIS:\AppPools\$SiteName" managedRuntimeVersion ''
Set-ItemProperty "IIS:\AppPools\$SiteName" enable32BitAppOnWin64 $false
Set-ItemProperty "IIS:\AppPools\$SiteName" processModel.loadUserProfile $true
Set-ItemProperty "IIS:\AppPools\$SiteName" processModel.idleTimeout ([TimeSpan]::Zero)
Set-ItemProperty "IIS:\AppPools\$SiteName" startMode AlwaysRunning
& icacls.exe $InstallRoot /grant "IIS AppPool\${SiteName}:(OI)(CI)RX" | Out-Null
if ($LASTEXITCODE) { throw 'App pool read ACL failed.' }
& icacls.exe $DataRoot /grant "IIS AppPool\${SiteName}:(OI)(CI)M" | Out-Null
if ($LASTEXITCODE) { throw 'App pool data ACL failed.' }
if ($Tunnel) {
 New-Website -Name $SiteName -PhysicalPath $appPath -ApplicationPool $SiteName -IPAddress '127.0.0.1' -Port $TunnelPort -HostHeader $HostName | Out-Null
} else {
 New-Website -Name $SiteName -PhysicalPath $appPath -ApplicationPool $SiteName -Port 443 -HostHeader $HostName -Ssl -SslFlags 1 | Out-Null
 (Get-WebBinding -Name $SiteName -Protocol https).AddSslCertificate($CertificateThumbprint,'My')
}
Stop-Website -Name $SiteName
Write-Host "Installed $SiteName, left stopped. Grant its SQL login, initialize Cx1_Dev, then start only this site."
Write-Host 'Never run iisreset on this shared server. See README.md for SQL permissions, TLS/DNS and checks.'
