param(
    [ValidateSet('Capture','Verify')][string]$Mode = 'Verify',
    [string]$ResultPath = '.local/browser-evidence/agency-lifecycle-result.json',
    [string]$SnapshotPath = '.local/browser-evidence/agency-restart-storage.json'
)
$ErrorActionPreference = 'Stop'
$result = Get-Content -LiteralPath $ResultPath -Raw | ConvertFrom-Json
if (!$result.passed) { throw 'A successful lifecycle browser result is required.' }
$agencyId = [guid]::Parse($result.agencyId).ToString()
# Emit hashes only, never evidence bodies, invitation secrets or personal data.
# Workers must be disabled and demo edits paused between capture and verification.
$tables = @('AgencyOnboarding','AgencyDraftProduct','AgencyEvidenceFile','AgencyEvidence',
    'AgencyStateRequest','AgencyTermsRequest','AgencyTermsVersion','AgencyFollowUp',
    'AgencyPermissionRequest','AgencyPermissionGrant','AgencyInvitation','AgencyNotification','AgencyActivity')
$statements = @("DECLARE @json nvarchar(max);", "SET @json=(SELECT * FROM Agency WHERE Id='$agencyId' ORDER BY Id FOR JSON PATH); SELECT 'Agency:'+CONVERT(varchar(64),HASHBYTES('SHA2_256',@json),2);")
foreach ($table in $tables) {
    $statements += "SET @json=(SELECT * FROM [$table] WHERE AgencyId='$agencyId' ORDER BY Id FOR JSON PATH); SELECT '${table}:'+CONVERT(varchar(64),HASHBYTES('SHA2_256',@json),2);"
}
$statements += "SET @json=(SELECT p.* FROM AgencyProduct p JOIN AgencyTermsVersion t ON t.Id=p.AgencyTermsVersionId WHERE t.AgencyId='$agencyId' ORDER BY p.Id FOR JSON PATH); SELECT 'AgencyProduct:'+CONVERT(varchar(64),HASHBYTES('SHA2_256',@json),2);"
$query = "SET NOCOUNT ON; SET XACT_ABORT ON; IF DB_NAME()<>N'CoverMGA_Demo' THROW 51000,'Demo only',1; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; BEGIN TRANSACTION; " + ($statements -join ' ') + ' COMMIT;'
$rows = @(& sqlcmd -S '.\SQL2022' -E -C -b -d CoverMGA_Demo -h -1 -W -Q $query)
if ($LASTEXITCODE -ne 0) { throw 'Persistence snapshot query failed.' }
$hashes = @($rows | ForEach-Object {$_.Trim()} | Where-Object {$_})
if ($hashes.Count -ne $tables.Count + 2 -or @($hashes | Where-Object {$_ -notmatch '^[A-Za-z]+:[A-F0-9]{64}$'}).Count) { throw 'Incomplete persistence snapshot.' }
if ($Mode -eq 'Capture') {
    @{agencyId=$agencyId;capturedAt=[DateTimeOffset]::UtcNow.ToString('o');hashes=$hashes} | ConvertTo-Json | Set-Content -LiteralPath $SnapshotPath
    Write-Output 'Captured agency aggregate hashes. Restart owned preview processes before Verify.'
} else {
    $before = Get-Content -LiteralPath $SnapshotPath -Raw | ConvertFrom-Json
    if ($before.agencyId -ne $agencyId -or (Compare-Object @($before.hashes) $hashes)) { throw 'Agency persisted state differs from the captured snapshot.' }
    Write-Output "Persistence verified: $($hashes.Count) agency data sets match the pre-restart snapshot."
}
