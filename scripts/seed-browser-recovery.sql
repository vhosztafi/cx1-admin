-- Fictional browser-demo fixtures only. No existing job/history is reset or deleted.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() <> N'CoverMGA_Demo' THROW 51000, 'Browser fixtures require CoverMGA_Demo.', 1;
DECLARE @actor uniqueidentifier = (SELECT Id FROM [User] WHERE Email = N'system-admin@cover.example');
DECLARE @scenario uniqueidentifier = (SELECT TOP (1) Id FROM SettingVersion WHERE Scope = N'diagnostic-probe/success' AND EffectiveFrom <= SYSUTCDATETIME() ORDER BY Version DESC);
IF @actor IS NULL OR @scenario IS NULL THROW 51000, 'Initialize fictional demo data first.', 1;
DECLARE @ids TABLE (Id uniqueidentifier);
DECLARE @now datetimeoffset = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');
DECLARE @index int = 0;
BEGIN TRANSACTION;
WHILE @index < 2
BEGIN
    DECLARE @id uniqueidentifier = NEWID(), @correlation uniqueidentifier = NEWID();
    INSERT INTO OutboxWork (Id,CreatedAt,CreatedBy,UpdatedAt,Kind,OperationKey,Payload,State,NextAttemptAt,Attempts,AttemptLimit,ScenarioVersionId,CorrelationId,CompletedAt,ErrorCode)
    VALUES (@id,@now,@actor,@now,N'diagnostic-probe',N'browser-fixture/'+CONVERT(nvarchar(36),@id),N'{"probe":"foundation"}',N'failed',@now,6,6,@scenario,@correlation,@now,N'provider-unavailable');
    DECLARE @attempt int = 1;
    WHILE @attempt <= 6
    BEGIN
        INSERT INTO AdapterAttempt (Id,CreatedAt,CreatedBy,UpdatedAt,WorkId,AttemptNumber,StartedAt,EndedAt,Outcome,Request,ErrorCode)
        VALUES (NEWID(),@now,@actor,@now,@id,@attempt,DATEADD(minute,@attempt-7,@now),DATEADD(second,1,DATEADD(minute,@attempt-7,@now)),N'transient-failure',N'{"fictionalBrowserFixture":true}',N'provider-unavailable');
        SET @attempt += 1;
    END;
    INSERT INTO JobException (Id,CreatedAt,CreatedBy,WorkId,Code,OccurredAt) VALUES (NEWID(),@now,@actor,@id,N'provider-unavailable',@now);
    INSERT INTO AuditEvent (Id,CreatedAt,CreatedBy,ActorId,EventType,OccurredAt,Reason,CorrelationId,SubjectRecordId)
    VALUES (NEWID(),@now,@actor,@actor,N'diagnostic.requested',@now,N'Fictional browser recovery fixture with six simulated attempt records.',@correlation,@id);
    INSERT INTO @ids VALUES (@id);
    SET @index += 1;
END;
COMMIT;
SELECT CONVERT(nvarchar(36),Id) FROM @ids;
