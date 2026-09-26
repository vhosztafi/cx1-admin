[CmdletBinding()]
param(
 [string]$AppPath = 'C:\Sites\Cx1AdminDev\app',
 [string]$SqlConnection = 'Server=.\sql2022;Database=Cx1_Dev;Integrated Security=true;Encrypt=true;TrustServerCertificate=true',
 [Security.SecureString]$InitialPassword
)
$ErrorActionPreference='Stop'
if (!(Test-Path (Join-Path $AppPath 'BackOffice.Api.exe'))) { throw 'Published API executable not found.' }
if (!$InitialPassword) { $InitialPassword=Read-Host 'Initial fictional user password (12+ characters; upper/lower/digit/symbol)' -AsSecureString }
$ptr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($InitialPassword)
$oldEnvironment=$env:ASPNETCORE_ENVIRONMENT;$oldConnection=$env:COVER_SQL_CONNECTION;$oldPassword=$env:COVER_DEMO_PASSWORD
try {
 $env:ASPNETCORE_ENVIRONMENT='Staging';$env:COVER_SQL_CONNECTION=$SqlConnection;$env:COVER_DEMO_PASSWORD=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
 & (Join-Path $AppPath 'BackOffice.Api.exe') --initialize-hosted-dev
 if ($LASTEXITCODE) { throw 'Database initialization failed. Do not start the site until resolved.' }
} finally {
 [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
 $env:ASPNETCORE_ENVIRONMENT=$oldEnvironment;$env:COVER_SQL_CONNECTION=$oldConnection;$env:COVER_DEMO_PASSWORD=$oldPassword
}
