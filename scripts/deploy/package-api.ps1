[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
$root=Join-Path $repo "artifacts\cx1-admin-api-dev-$stamp"
$app=Join-Path $root 'app'
New-Item -ItemType Directory -Force $root | Out-Null
Push-Location $repo
try {
 dotnet publish backend/src/BackOffice.Api/BackOffice.Api.csproj -c Release -r win-x64 --self-contained true -o $app --configfile NuGet.Config --packages .local/nuget -p:RestoreLockedMode=false -p:PublishReadyToRun=false
 if ($LASTEXITCODE) { throw 'API publish failed.' }
 Copy-Item -Path (Join-Path $repo 'deploy\windows\*') -Destination $root
 $revision=git rev-parse HEAD
 $dirty=!( [string]::IsNullOrWhiteSpace((git status --porcelain | Out-String)) )
 $files=Get-ChildItem -LiteralPath $app -File -Recurse | ForEach-Object { @{path=$_.FullName.Substring($app.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} }
 @{builtAt=(Get-Date).ToUniversalTime().ToString('o');revision=$revision;workingTreeChanges=$dirty;runtime='win-x64';selfContained=$true;files=$files} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $root 'manifest.json')
 Compress-Archive -Path (Join-Path $root '*') -DestinationPath "$root.zip"
 $hash=Get-FileHash -LiteralPath "$root.zip" -Algorithm SHA256
 $hash.Hash | Set-Content "$root.zip.sha256"
 $hash
 Write-Host "Package: $root.zip"
} finally { Pop-Location }
