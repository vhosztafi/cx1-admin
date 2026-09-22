param(
    [Parameter(Mandatory=$true)][ValidateSet('Capture','Verify')][string]$Mode,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
    [string]$Server = '.\SQL2022',
    [string[]]$FileRoots = @('.local/data-protection')
)
$ErrorActionPreference = 'Stop'
# Stop owned workers before capture/verification. Read-only: freeze the old
# columns and hash individual rows, allowing additive tables/columns/rows.
# Neither business content nor key bytes are written to evidence.
$manifestPath = Join-Path $EvidenceDirectory 'baseline.json'
if ($Mode -eq 'Capture') {
    if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Use a fresh evidence directory.' }
    New-Item -ItemType Directory -Path $EvidenceDirectory | Out-Null
} else {
    $baseline = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($baseline.server -ne $Server -or $baseline.database -ne 'CoverMGA_Demo') { throw 'Baseline target mismatch.' }
}
$connection = New-Object System.Data.SqlClient.SqlConnection("Server=$Server;Database=CoverMGA_Demo;Integrated Security=true;Encrypt=true;TrustServerCertificate=true")
$connection.Open()
$transaction = $connection.BeginTransaction([System.Data.IsolationLevel]::Serializable)
function Read-Rows([string]$Sql) {
    $command = $connection.CreateCommand(); $command.Transaction = $transaction
    $command.CommandText = $Sql; $command.CommandTimeout = 180
    $reader = $command.ExecuteReader()
    try {
        while ($reader.Read()) {
            $row = [ordered]@{}
            for ($i=0; $i -lt $reader.FieldCount; $i++) { $row[$reader.GetName($i)] = $reader.GetValue($i) }
            [pscustomobject]$row
        }
    } finally { $reader.Dispose(); $command.Dispose() }
}
try {
    if ($Mode -eq 'Capture') {
        $tables = @(Read-Rows @'
SELECT QUOTENAME(s.name)+'.'+QUOTENAME(t.name) AS name,c.cols,k.keys
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
CROSS APPLY(SELECT STRING_AGG(CONVERT(nvarchar(max),'r.'+QUOTENAME(name)),',') WITHIN GROUP(ORDER BY column_id) cols FROM sys.columns WHERE object_id=t.object_id)c
OUTER APPLY(SELECT STRING_AGG(CONVERT(nvarchar(max),'r.'+QUOTENAME(col.name)),',') WITHIN GROUP(ORDER BY ic.key_ordinal) keys
 FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
 JOIN sys.columns col ON col.object_id=ic.object_id AND col.column_id=ic.column_id
 WHERE i.object_id=t.object_id AND i.is_primary_key=1)k
WHERE t.is_ms_shipped=0 AND t.name<>'__EFMigrationsHistory' ORDER BY s.name,t.name
'@)
        if ($tables.Count -eq 0) { throw 'No retained tables found.' }
        foreach ($table in $tables) {
            if ($table.keys -is [DBNull] -or !$table.keys) { throw "Missing primary key: $($table.name)" }
            $sql = "SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT $($table.keys) FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER)),2) AS id,CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT $($table.cols) FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER)),2) AS hash FROM $($table.name) r ORDER BY $($table.keys)"
            $table | Add-Member query $sql
            $table | Add-Member rows @(Read-Rows $sql)
        }
        $files = @(foreach ($root in $FileRoots) {
            $resolved = (Resolve-Path -LiteralPath $root).Path
            foreach ($file in Get-ChildItem -LiteralPath $resolved -File -Recurse) {
                @{path=$file.FullName;hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
            }
        })
        if ($files.Count -eq 0) { throw 'No retained files captured.' }
        @{server=$Server;database='CoverMGA_Demo';capturedAt=[DateTimeOffset]::UtcNow.ToString('o');tables=$tables;files=$files} |
            ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath
        Write-Output "Captured $($tables.Count) tables and $($files.Count) retained files."
    } else {
        $rowCount = 0
        foreach ($table in $baseline.tables) {
            $current = @{}
            foreach ($row in @(Read-Rows $table.query)) {
                if ($current.ContainsKey($row.id)) { throw "Duplicate key fingerprint in $($table.name)." }
                $current[$row.id] = $row.hash
            }
            foreach ($row in $table.rows) {
                if (!$current.ContainsKey($row.id) -or $current[$row.id] -cne $row.hash) { throw "Retained row changed/missing in $($table.name): $($row.id)" }
                $rowCount++
            }
        }
        foreach ($file in $baseline.files) {
            if (!(Test-Path -LiteralPath $file.path -PathType Leaf) -or (Get-FileHash -LiteralPath $file.path -Algorithm SHA256).Hash -cne $file.hash) { throw "Retained file changed/missing: $($file.path)" }
        }
        @{passed=$true;verifiedAt=[DateTimeOffset]::UtcNow.ToString('o');tables=$baseline.tables.Count;retainedRows=$rowCount;retainedFiles=$baseline.files.Count} |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'verification.json')
        Write-Output "Preserved $rowCount original rows and $($baseline.files.Count) files."
    }
    $transaction.Commit()
} finally { $transaction.Dispose(); $connection.Dispose() }
