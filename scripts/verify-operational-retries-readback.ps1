param([string]$Server='.\SQL2022')
$ErrorActionPreference='Stop'
$directory='.local/operational-pack-demo-v1'
$browser=Get-Content "$directory/retry-browser.json" -Raw|ConvertFrom-Json
$fixture=Get-Content "$directory/agency-fixture.json" -Raw|ConvertFrom-Json
if(!$browser.passed -or $browser.packAttempts -ne 7 -or $browser.midAttempts -ne 7){throw 'Current successful browser retries required.'}
$connection=New-Object System.Data.SqlClient.SqlConnection("Server=$Server;Database=CoverMGA_Demo;Integrated Security=true;Encrypt=true;TrustServerCertificate=true")
$connection.Open()
try{
 foreach($item in @(@{work=$browser.workId;task=$browser.tasks.packTaskId},@{work=$browser.midWorkId;task=$browser.tasks.midTaskId})){
  $command=$connection.CreateCommand();$command.CommandText=@'
IF NOT EXISTS(SELECT 1 FROM OutboxWork WHERE Id=@work AND State='succeeded' AND Attempts=7 AND AttemptLimit=12) THROW 52102,'Original retry job differs.',1;
IF (SELECT COUNT(*) FROM AdapterAttempt WHERE WorkId=@work)<>7 THROW 52103,'Attempt history differs.',1;
IF (SELECT COUNT(*) FROM JobException WHERE WorkId=@work)<>1 THROW 52104,'Exception duplicated.',1;
IF (SELECT COUNT(*) FROM WorkflowTaskBinding b JOIN JobException e ON e.Id=b.SourceEventId WHERE e.WorkId=@work AND b.SourceKind='job-exception')<>1 OR NOT EXISTS(SELECT 1 FROM WorkflowTaskBinding b JOIN JobException e ON e.Id=b.SourceEventId WHERE e.WorkId=@work AND b.SourceKind='job-exception' AND b.TaskId=@task) THROW 52105,'Original exception task differs.',1;
IF (SELECT COUNT(*) FROM DemoProviderOperation p JOIN OutboxWork w ON p.Kind=w.Kind AND p.OperationKey=w.OperationKey WHERE w.Id=@work)<>1 THROW 52106,'Provider effect duplicated.',1;
'@
  $null=$command.Parameters.Add('@work',[System.Data.SqlDbType]::UniqueIdentifier);$command.Parameters['@work'].Value=[guid]$item.work
  $null=$command.Parameters.Add('@task',[System.Data.SqlDbType]::UniqueIdentifier);$command.Parameters['@task'].Value=[guid]$item.task
  try{$null=$command.ExecuteNonQuery()}finally{$command.Dispose()}
 }
 $command=$connection.CreateCommand();$command.CommandText=@'
IF NOT EXISTS(SELECT 1 FROM OperationalDelivery d JOIN OutboxWork w ON w.Id=d.WorkId JOIN DemoProviderOperation p ON p.Id=d.ProviderOperationId WHERE d.Id=@delivery AND w.Id=@pack AND d.State='delivered' AND d.ResendOfId IS NULL AND d.ContentJson=w.Payload AND d.ContentHash=LOWER(CONVERT(varchar(64),p.RequestHash,2))) THROW 52107,'Pack immutable content differs.',1;
IF (SELECT COUNT(*) FROM OperationalDelivery WHERE WorkId=@pack OR ResendOfId=@delivery)<>1 THROW 52108,'Retry created a resend.',1;
IF NOT EXISTS(SELECT 1 FROM MidSubmission s JOIN MidResult r ON r.SubmissionId=s.Id JOIN DemoProviderOperation p ON p.Id=r.ProviderOperationId WHERE s.Id=@mid AND s.WorkId=@midWork AND JSON_VALUE(r.ResultJson,'$.state')='accepted' AND s.RequestHash=LOWER(CONVERT(varchar(64),p.RequestHash,2))) THROW 52109,'MID original provider request differs.',1;
IF (SELECT COUNT(*) FROM MidResult WHERE SubmissionId=@mid)<>1 THROW 52110,'MID result duplicated.',1;
IF (SELECT COUNT(*) FROM OperationalDeliveryAttachment WHERE DeliveryId=@delivery)<>3 THROW 52111,'Pack document count differs.',1;
IF EXISTS(SELECT 1 FROM OPENJSON(@files) WITH(id uniqueidentifier '$.id',sha256 nvarchar(64) '$.sha256') f WHERE NOT EXISTS(SELECT 1 FROM OperationalDeliveryAttachment a WHERE a.DeliveryId=@delivery AND a.DocumentVersionId=f.id AND a.ContentHash=f.sha256)) THROW 52112,'Exact selected file/hash changed.',1;
'@
 foreach($entry in @{delivery=$browser.deliveryId;pack=$browser.workId;mid=$browser.midSubmissionId;midWork=$browser.midWorkId}.GetEnumerator()){
  $null=$command.Parameters.Add('@'+$entry.Key,[System.Data.SqlDbType]::UniqueIdentifier);$command.Parameters['@'+$entry.Key].Value=[guid]$entry.Value
 }
 $null=$command.Parameters.Add('@files',[System.Data.SqlDbType]::NVarChar,-1);$command.Parameters['@files'].Value=ConvertTo-Json -InputObject @($fixture.documents) -Depth 6 -Compress
 try{$null=$command.ExecuteNonQuery()}finally{$command.Dispose()}
 @{passed=$true;checkedAt=[DateTimeOffset]::UtcNow.ToString('o');sameOperations=$true;packExceptionTasks=1;midExceptionTasks=1;packAttempts=7;midAttempts=7;providerOperations=2;exactFiles=3;timeCompression='Only initial retry due times; real worker attempts and provider outcomes'}|
  ConvertTo-Json|Set-Content "$directory/retry-acceptance.json"
 Write-Output 'Original pack/MID jobs, one task each, seven attempts each, two total provider operations and three exact PDFs verified in SQL.'
}finally{$connection.Dispose()}
