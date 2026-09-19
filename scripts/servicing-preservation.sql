SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() <> N'CoverMGA_Demo' THROW 51600, 'Preservation requires the local demonstration database.', 1;
IF EXISTS (SELECT 1 FROM sys.tables t WHERE t.is_ms_shipped=0 AND NOT EXISTS
    (SELECT 1 FROM sys.indexes i WHERE i.object_id=t.object_id AND i.is_primary_key=1))
    THROW 51601, 'Every retained table requires deterministic primary-key ordering.', 1;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
DECLARE @sql nvarchar(max)=N'';
SELECT @sql=STRING_AGG(CONVERT(nvarchar(max),N'SELECT '+QUOTENAME(s.name+N'.'+t.name,'''')+
 N',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES(''SHA2_256'',COALESCE((SELECT '+c.cols+
 N' FROM '+QUOTENAME(s.name)+N'.'+QUOTENAME(t.name)+N' ORDER BY '+k.keys+
 N' FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]'')),2) FROM '+QUOTENAME(s.name)+N'.'+QUOTENAME(t.name)+N';'),CHAR(10)) WITHIN GROUP(ORDER BY s.name,t.name)
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
CROSS APPLY(SELECT STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(name)),N',') WITHIN GROUP(ORDER BY column_id) cols FROM sys.columns WHERE object_id=t.object_id)c
CROSS APPLY(SELECT STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(col.name)),N',') WITHIN GROUP(ORDER BY ic.key_ordinal) keys
 FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
 JOIN sys.columns col ON col.object_id=ic.object_id AND col.column_id=ic.column_id
 WHERE i.object_id=t.object_id AND i.is_primary_key=1)k
WHERE t.is_ms_shipped=0;
EXEC sp_executesql @sql;
COMMIT;
