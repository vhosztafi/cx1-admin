param([string]$Report='.local/operational-incidents-demo-v1/report.json',[string]$Server='.\SQL2022')
$ErrorActionPreference='Stop'
$reportData=Get-Content -LiteralPath $Report -Raw|ConvertFrom-Json
if(!$reportData.passed -or $reportData.reports.Count -ne 2){throw 'Two accepted demo browser records required.'}
$connection=New-Object System.Data.SqlClient.SqlConnection("Server=$Server;Database=CoverMGA_Demo;Integrated Security=true;Encrypt=true;TrustServerCertificate=true")
$connection.Open()
try{
 foreach($row in $reportData.reports){
  $command=$connection.CreateCommand()
  $command.CommandText=@'
IF NOT EXISTS(SELECT 1 FROM Incident WHERE Id=@incident AND PolicyId=@policy AND CurrentRevisionId=@revision AND CurrentResolutionId=@resolution AND State='handed-off') THROW 52090,'Incident readback differs.',1;
IF (SELECT COUNT(*) FROM IncidentRevision WHERE IncidentId=@incident)<>1 THROW 52091,'Repeat setup created another revision.',1;
IF (SELECT COUNT(*) FROM ClaimsHandoff WHERE IncidentId=@incident)<>1 OR NOT EXISTS(SELECT 1 FROM ClaimsHandoff WHERE Id=@handoff AND IncidentId=@incident AND State='acknowledged' AND RevisionId=@revision AND ResolutionId=@resolution) THROW 52092,'Handoff readback differs.',1;
IF (SELECT COUNT(*) FROM ClaimsSummary WHERE HandoffId=@handoff)<>2 THROW 52093,'Summary history differs.',1;
IF EXISTS(SELECT 1 FROM ClaimsSummary WHERE HandoffId=@handoff AND (JSON_VALUE(SummaryJson,'$.paid') IS NOT NULL OR JSON_VALUE(SummaryJson,'$.reserved') IS NOT NULL)) THROW 52094,'Unknown money was replaced.',1;
IF (SELECT COUNT(*) FROM ClaimsRequest WHERE HandoffId=@handoff)<>2 OR EXISTS(SELECT 1 FROM ClaimsRequest r JOIN OutboxWork w ON w.Id=r.WorkId WHERE r.HandoffId=@handoff AND w.State<>'succeeded') THROW 52095,'Original requests are incomplete or duplicated.',1;
IF NOT EXISTS(SELECT 1 FROM IncidentResolutionSource s JOIN PolicyVersion v ON v.Id=s.VersionId WHERE s.ResolutionId=@resolution AND v.PolicyId=@policy AND s.SourceHash=LOWER(CONVERT(varchar(64),v.ContentHash,2))) THROW 52096,'Historical policy provenance differs.',1;
'@
  foreach($entry in @{incident=$row.incidentId;policy=$row.policyId;revision=$row.revisionId;resolution=$row.resolution.id;handoff=$row.handoffId}.GetEnumerator()){
   $null=$command.Parameters.Add('@'+$entry.Key,[System.Data.SqlDbType]::UniqueIdentifier);$command.Parameters['@'+$entry.Key].Value=[guid]$entry.Value
  }
  try{$null=$command.ExecuteNonQuery()}finally{$command.Dispose()}
 }
 @{passed=$true;checkedAt=[DateTimeOffset]::UtcNow.ToString('o');incidents=2;revisions=2;handoffs=2;summaries=4;requests=4;originalHistoricalHashes=$true}|
  ConvertTo-Json|Set-Content -LiteralPath (Join-Path (Split-Path $Report) 'sql-readback.json')
 Write-Output 'Two incidents, original revisions/historical hashes, two handoffs and four immutable summaries verified in SQL.'
}finally{$connection.Dispose()}

