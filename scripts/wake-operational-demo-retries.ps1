param([string]$Server='.\SQL2022')
$ErrorActionPreference='Stop'
# Explicit demo time compression, matching the SQL/browser fixtures. Only bring
# due the two recorded transient examples. The actual hosted workers still claim
# leases, invoke the persistent adapter, record attempts and exhaust the budget.
# Never edit payloads, attempt counts, outcomes, provider receipts or authority.
$mid=Get-Content .local/phase9-16-mid-demo.json -Raw|ConvertFrom-Json
$pack=Get-Content .local/operational-pack-demo-v1/work.json -Raw|ConvertFrom-Json
$connection=New-Object System.Data.SqlClient.SqlConnection("Server=$Server;Database=CoverMGA_Demo;Integrated Security=true;Encrypt=true;TrustServerCertificate=true")
$connection.Open()
try{
 $results=@()
 foreach($item in @(@{id=$mid.workId;kind='mid-update'},@{id=$pack.id;kind='operational-delivery'})){
  $command=$connection.CreateCommand();$command.CommandText=@'
SET XACT_ABORT ON;
IF DB_NAME()<>'CoverMGA_Demo' THROW 52100,'Local demo required.',1;
BEGIN TRANSACTION;
DECLARE @state nvarchar(20),@attempts int,@limit int,@next datetimeoffset;
SELECT @state=w.State,@attempts=w.Attempts,@limit=w.AttemptLimit,@next=w.NextAttemptAt
FROM OutboxWork w WITH(UPDLOCK,HOLDLOCK) JOIN SettingVersion s ON s.Id=w.ScenarioVersionId
WHERE w.Id=@id AND w.Kind=@kind AND s.Scope=CASE WHEN @kind='mid-update' THEN 'operational-mid' ELSE @kind END
 AND JSON_VALUE(s.[Values],'$.demo')='true' AND JSON_VALUE(s.[Values],'$.scenario')='retry-required';
IF @state IS NULL THROW 52101,'Recorded retry example does not match its immutable scenario.',1;
DECLARE @now datetimeoffset=SWITCHOFFSET(SYSDATETIMEOFFSET(),0),@changed int=0;
IF @state='pending' AND @attempts BETWEEN 1 AND 5 AND @limit=6 AND @next>@now
BEGIN
 UPDATE OutboxWork SET NextAttemptAt=@now WHERE Id=@id;
 SET @changed=@@ROWCOUNT;
END;
COMMIT;
SELECT @state AS state,@attempts AS attempts,@limit AS attemptLimit,@next AS previousDue,@changed AS accelerated;
'@
  $null=$command.Parameters.Add('@id',[System.Data.SqlDbType]::UniqueIdentifier);$command.Parameters['@id'].Value=[guid]$item.id
  $null=$command.Parameters.Add('@kind',[System.Data.SqlDbType]::NVarChar,60);$command.Parameters['@kind'].Value=$item.kind
  $reader=$command.ExecuteReader()
  try{if(!$reader.Read()){throw 'Missing retry readback.'};$results+=@{workId=$item.id;kind=$item.kind;state=$reader.GetString(0);attempts=$reader.GetInt32(1);attemptLimit=$reader.GetInt32(2);previousDue=$reader.GetDateTimeOffset(3).ToString('o');accelerated=$reader.GetInt32(4)}}finally{$reader.Dispose();$command.Dispose()}
 }
 $stamp=[DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff')
 @{checkedAt=[DateTimeOffset]::UtcNow.ToString('o');timeCompression='NextAttemptAt only; real hosted lease/provider/application';operations=$results}|ConvertTo-Json -Depth 4|Set-Content ".local/operational-pack-demo-v1/schedule-$stamp.json"
 $results|Select-Object kind,state,attempts,accelerated
}finally{$connection.Dispose()}
