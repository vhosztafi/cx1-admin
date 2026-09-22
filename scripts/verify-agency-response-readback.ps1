param([string]$Report='.local/agency-response-demo-v1/report.json',[string]$Server='.\SQL2022')
$ErrorActionPreference='Stop'
$data=Get-Content -LiteralPath $Report -Raw|ConvertFrom-Json
if(!$data.passed -or $data.rows.Count -ne 2){throw 'Two retained agency response examples are required.'}
$connection=New-Object System.Data.SqlClient.SqlConnection("Server=$Server;Database=CoverMGA_Demo;Integrated Security=true;Encrypt=true;TrustServerCertificate=true")
$connection.Open()
try{
 foreach($row in $data.rows){
  $command=$connection.CreateCommand()
  $command.CommandText=@'
IF NOT EXISTS(SELECT 1 FROM AgencyResponseRequest r
 JOIN OperationalMessageVersion v ON v.Id=r.MessageVersionId
 JOIN OperationalMessageDraft m ON m.Id=v.MessageId
 JOIN OperationalThread t ON t.Id=m.ThreadId
 JOIN OperationalSubject s ON s.Id=t.SubjectId
 JOIN Policy p ON p.Id=s.PolicyId
 CROSS APPLY OPENJSON(v.ContentJson) WITH(body nvarchar(max) '$.body',subject nvarchar(300) '$.subject') c
 WHERE r.Id=@request AND v.MessageId=@message AND s.PolicyId=@policy AND p.AgencyId=@agency
 AND t.Visibility='agency' AND t.RelationshipId=r.RelationshipId AND p.RelationshipId=r.RelationshipId AND r.SubjectId=s.Id
 AND r.State=@state AND r.Reference=@reference AND r.Instruction COLLATE Latin1_General_100_BIN2=c.body COLLATE Latin1_General_100_BIN2
 AND r.Subject COLLATE Latin1_General_100_BIN2=c.subject COLLATE Latin1_General_100_BIN2
 AND DATALENGTH(r.Instruction)=DATALENGTH(c.body)
 AND EXISTS(SELECT 1 FROM OperationalDelivery d JOIN DemoProviderOperation op ON op.Id=d.ProviderOperationId
   JOIN OutboxWork w ON w.Id=d.WorkId WHERE d.MessageVersionId=v.Id AND d.State='delivered' AND w.State='succeeded'))
 THROW 52090,'Retained response provenance or saved state differs.',1;
IF (SELECT COUNT(*) FROM AgencyResponseRequest r JOIN OperationalMessageVersion v ON v.Id=r.MessageVersionId WHERE v.MessageId=@message)<>1
 THROW 52091,'Repeated setup created duplicate response tracking.',1;
'@
  foreach($entry in @{request=$row.response.id;message=$row.messageId;policy=$row.policyId;agency=$row.agencyId}.GetEnumerator()){
   $null=$command.Parameters.Add('@'+$entry.Key,[Data.SqlDbType]::UniqueIdentifier);$command.Parameters['@'+$entry.Key].Value=[guid]$entry.Value
  }
  $null=$command.Parameters.Add('@state',[Data.SqlDbType]::NVarChar,30);$command.Parameters['@state'].Value=$row.response.state
  $null=$command.Parameters.Add('@reference',[Data.SqlDbType]::NVarChar,40);$command.Parameters['@reference'].Value=$row.response.reference
  try{$null=$command.ExecuteNonQuery()}finally{$command.Dispose()}
 }
 @{passed=$true;checkedAt=[DateTimeOffset]::UtcNow.ToString('o');requests=2;exactDeliveredContent=$true;rows=@($data.rows|ForEach-Object{@{id=$_.response.id;messageId=$_.messageId;policyId=$_.policyId;state=$_.response.state}})}|
  ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path (Split-Path $Report) 'sql-readback.json')
 Write-Output 'Two retained response requests have exact delivered content, policy ownership and unique tracking in SQL.'
}finally{$connection.Dispose()}
