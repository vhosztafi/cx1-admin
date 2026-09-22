param([Parameter(Mandatory=$true)][ValidateSet('Prepare','Restore')][string]$Mode,
 [string]$Journal='.local/operational-retry-demo-settings.json',[string]$Server='.\SQL2022')
$ErrorActionPreference='Stop'
# Explicit local demonstration configuration, never application startup. Only
# the two new operations queued in this setup window should select these rows.
# Restore appends the prior values; immutable selected versions remain retained.
$connection=New-Object System.Data.SqlClient.SqlConnection("Server=$Server;Database=CoverMGA_Demo;Integrated Security=true;Encrypt=true;TrustServerCertificate=true")
$connection.Open();$tx=$connection.BeginTransaction([System.Data.IsolationLevel]::Serializable)
function Command([string]$Sql){$c=$connection.CreateCommand();$c.Transaction=$tx;$c.CommandText=$Sql;return $c}
try{
 $guard=Command "IF DB_NAME()<>'CoverMGA_Demo' THROW 52097,'Local demo required.',1; DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'CoverMGA.OperationalRetryDemoSettings',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r<0 THROW 52098,'Demo setting setup busy.',1;"
 try{$null=$guard.ExecuteNonQuery()}finally{$guard.Dispose()}
 if($Mode -eq 'Prepare'){
  if(Test-Path -LiteralPath $Journal){throw 'Retain existing setting journal; restore or resume its queued operations, never recreate it.'}
  $entries=@()
  foreach($scope in @('operational-delivery','operational-mid')){
   $read=Command 'SELECT TOP(1) Version,[Values] FROM SettingVersion WHERE Scope=@scope AND EffectiveFrom<=SWITCHOFFSET(SYSDATETIMEOFFSET(),0) ORDER BY Version DESC'
   $null=$read.Parameters.Add('@scope',[System.Data.SqlDbType]::NVarChar,100);$read.Parameters['@scope'].Value=$scope
   $reader=$read.ExecuteReader()
   try{if(!$reader.Read()){throw 'Missing seeded operational configuration.'};$version=$reader.GetInt32(0);$prior=$reader.GetString(1)}finally{$reader.Dispose();$read.Dispose()}
   $values=$prior|ConvertFrom-Json
   if(!$values.demo -or $values.kind -ne $scope -or $values.scenario -ne 'success'){throw 'Expected the existing success demo configuration.'}
   $values.scenario='retry-required'
   $entries+=@{scope=$scope;id=[guid]::NewGuid().ToString();version=$version+1;prior=$prior;values=($values|ConvertTo-Json -Compress)}
  }
  @{database='CoverMGA_Demo';server=$Server;preparedAt=[DateTimeOffset]::UtcNow.ToString('o');entries=$entries}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $Journal
 }else{
  $saved=Get-Content -LiteralPath $Journal -Raw|ConvertFrom-Json
  if($saved.database -ne 'CoverMGA_Demo' -or $saved.server -ne $Server){throw 'Setting journal target mismatch.'}
  $entries=$saved.entries
 }
 foreach($entry in $entries){
  if($Mode -eq 'Prepare'){
   $insert=Command 'INSERT INTO SettingVersion(Id,Scope,Version,EffectiveFrom,[Values],CreatedAt) VALUES(@id,@scope,@version,SWITCHOFFSET(SYSDATETIMEOFFSET(),0),@values,SWITCHOFFSET(SYSDATETIMEOFFSET(),0))'
   $settingId=[guid]$entry.id;$settingVersion=[int]$entry.version;$settingValues=$entry.values
  }else{
   # Never replace someone else''s later configuration; exact restored versions
   # are idempotent and keep the original retry setting pinned to its work.
   $insert=Command @'
IF EXISTS(SELECT 1 FROM SettingVersion WHERE Id=@original AND Scope=@scope AND Version=@version-1)
BEGIN
 IF EXISTS(SELECT 1 FROM SettingVersion WHERE Scope=@scope AND Version=@version AND [Values]=@values) RETURN;
 IF EXISTS(SELECT 1 FROM SettingVersion WHERE Scope=@scope AND Version>=@version) THROW 52099,'Newer configuration requires review.',1;
 INSERT INTO SettingVersion(Id,Scope,Version,EffectiveFrom,[Values],CreatedAt) VALUES(@id,@scope,@version,SWITCHOFFSET(SYSDATETIMEOFFSET(),0),@values,SWITCHOFFSET(SYSDATETIMEOFFSET(),0));
END
'@
   $null=$insert.Parameters.Add('@original',[System.Data.SqlDbType]::UniqueIdentifier);$insert.Parameters['@original'].Value=[guid]$entry.id
   $settingId=[guid]::NewGuid();$settingVersion=[int]$entry.version+1;$settingValues=$entry.prior
  }
  $null=$insert.Parameters.Add('@id',[System.Data.SqlDbType]::UniqueIdentifier);$insert.Parameters['@id'].Value=$settingId
  $null=$insert.Parameters.Add('@scope',[System.Data.SqlDbType]::NVarChar,100);$insert.Parameters['@scope'].Value=$entry.scope
  $null=$insert.Parameters.Add('@version',[System.Data.SqlDbType]::Int);$insert.Parameters['@version'].Value=$settingVersion
  $null=$insert.Parameters.Add('@values',[System.Data.SqlDbType]::NVarChar,-1);$insert.Parameters['@values'].Value=$settingValues
  try{$null=$insert.ExecuteNonQuery()}finally{$insert.Dispose()}
 }
 $tx.Commit();Write-Output "$Mode completed for two immutable fictional adapter settings."
}finally{$tx.Dispose();$connection.Dispose()}


