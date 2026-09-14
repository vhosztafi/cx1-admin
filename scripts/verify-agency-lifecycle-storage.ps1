param([string]$ResultPath = '.local/browser-evidence/agency-lifecycle-result.json')
$ErrorActionPreference = 'Stop'
$result = Get-Content -LiteralPath $ResultPath -Raw | ConvertFrom-Json
if (!$result.passed) { throw 'A successful lifecycle browser result is required.' }
$agencyId = [guid]::Parse($result.agencyId).ToString()
$query = @"
SET NOCOUNT ON;
DECLARE @agency uniqueidentifier='$agencyId';
IF (SELECT COUNT(*) FROM AgencyStateRequest WHERE AgencyId=@agency AND State='applied')<>3 THROW 51000,'Expected three applied state requests',1;
IF (SELECT COUNT(*) FROM AgencyTermsRequest WHERE AgencyId=@agency AND State='applied')<>1 THROW 51000,'Expected one applied terms request',1;
IF (SELECT COUNT(*) FROM AgencyTermsVersion WHERE AgencyId=@agency)<>2 THROW 51000,'Expected two immutable versions',1;
IF (SELECT COUNT(*) FROM AgencyInvitation WHERE AgencyId=@agency AND State='pending')<>1 THROW 51000,'Expected one fresh invitation',1;
IF (SELECT COUNT(*) FROM AgencyInvitation WHERE AgencyId=@agency AND State='revoked')<>1 THROW 51000,'Expected one retained revoked invitation',1;
IF (SELECT COUNT(*) FROM AgencyFollowUp WHERE AgencyId=@agency)<>2 THROW 51000,'Expected initial follow-ups without reactivation duplicates',1;
IF (SELECT COUNT(*) FROM AgencyNotification WHERE AgencyId=@agency)<>4 THROW 51000,'Expected three activation notices and one fresh invitation notice',1;
SELECT 'Lifecycle SQL invariants passed' AS Result;
"@
& sqlcmd -S '.\SQL2022' -E -C -b -d CoverMGA_Demo -Q $query
if ($LASTEXITCODE -ne 0) { throw 'Lifecycle SQL invariant check failed.' }
