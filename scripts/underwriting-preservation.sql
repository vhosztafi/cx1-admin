SET NOCOUNT ON;
SELECT 'Quote',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT Id,Reference,AgencyId,ClientId,RelationshipId,ProductId,State,CurrentRevisionId,CaptureClosedAt,CaptureClosedReason,AssignedUserId,ClonedFromQuoteRevisionId,CreatedAt,CreatedBy,UpdatedAt,RowVersion FROM Quote ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM Quote;
SELECT 'QuoteRevision',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM QuoteRevision ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM QuoteRevision;
SELECT 'QuoteEvidenceFile',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM QuoteEvidenceFile ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM QuoteEvidenceFile;
SELECT 'QuoteCaptureEvidence',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM QuoteCaptureEvidence ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM QuoteCaptureEvidence;
SELECT 'AgencyTermsVersion',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM AgencyTermsVersion ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM AgencyTermsVersion;
SELECT 'ProductVersion1',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM ProductVersion WHERE Version=1 ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM ProductVersion WHERE Version=1;
SELECT 'UserCredential',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM UserCredential ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM UserCredential;
SELECT 'UnderwritingCycle',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT [Id],[QuoteId],[QuoteRevisionId],[AgencyId],[ClientId],[RelationshipId],[ProductId],[ProductVersionId],[AgencyTermsVersionId],[RatingRuleVersionId],[BinderVersionId],[AuthorityVersionId],[Sequence],[WorkId],[PricingInputHash],[InputJson],[StartsAt],[EndsAt],[RequestedBy],[State],[CurrentRatingId],[SupersededAt],[SupersededReason],[CreatedAt],[CreatedBy],[UpdatedAt],[RowVersion] FROM UnderwritingCycle ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM UnderwritingCycle;
SELECT 'QuoteRatingResult',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM QuoteRatingResult ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM QuoteRatingResult;
SELECT 'QuoteSubmission',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM QuoteSubmission ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM QuoteSubmission;
SELECT 'QuoteReferral',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT Id,CycleId,QuoteId,RatingId,Sequence,RuleCode,Dimension,RiskItemId,TargetKey,RequiredAuthorityJson,Reason,State,AssignedUserId,CreatedAt,CreatedBy,UpdatedAt,RowVersion FROM QuoteReferral ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM QuoteReferral;
SELECT 'UserAuthorityGrant',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM UserAuthorityGrant ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM UserAuthorityGrant;
SELECT 'QuoteReferralDecision',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM QuoteReferralDecision ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM QuoteReferralDecision;
SELECT 'QuoteCondition',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM QuoteCondition ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM QuoteCondition;
SELECT 'QuoteConditionResolution',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM QuoteConditionResolution ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM QuoteConditionResolution;
SELECT 'UnderwritingEvidenceEvent',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM UnderwritingEvidenceEvent ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM UnderwritingEvidenceEvent;
SELECT 'UnderwritingEvidenceAssociation',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT Id,QuoteId,CycleId,FileId,RequirementCode,RiskItemId,ConditionId,TermsVersionId,InputFingerprint,Reason,LatestReviewId,WithdrawnEventId,CreatedAt,CreatedBy,UpdatedAt,RowVersion FROM UnderwritingEvidenceAssociation ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM UnderwritingEvidenceAssociation;
SELECT 'RetainedSettingVersion',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM SettingVersion WHERE Scope NOT LIKE 'capacity-escalation/%' AND Scope<>'quote-delivery' ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM SettingVersion WHERE Scope NOT LIKE 'capacity-escalation/%' AND Scope<>'quote-delivery';

SELECT 'CapacityEscalation',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM [CapacityEscalation] ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM [CapacityEscalation];
SELECT 'CapacitySubmission',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM [CapacitySubmission] ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM [CapacitySubmission];
SELECT 'CapacityMessage',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM [CapacityMessage] ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM [CapacityMessage];
SELECT 'CapacitySubmissionEvidence',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM [CapacitySubmissionEvidence] ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM [CapacitySubmissionEvidence];
SELECT 'CapacityOriginalSettings',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM SettingVersion WHERE Scope LIKE 'capacity-escalation/%' AND Version=1 ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM SettingVersion WHERE Scope LIKE 'capacity-escalation/%' AND Version=1;


SELECT 'FullTemplateVersion',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM [TemplateVersion] WHERE Kind='quote-terms' ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM [TemplateVersion] WHERE Kind='quote-terms';

SELECT 'FullQuoteTermsVersion',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM [QuoteTermsVersion] ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM [QuoteTermsVersion];

SELECT 'FullQuoteTermsDelivery',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM [QuoteTermsDelivery] ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM [QuoteTermsDelivery];

SELECT 'FullQuoteAcceptance',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM [QuoteAcceptance] ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM [QuoteAcceptance];

SELECT 'FullSettingVersion',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM [SettingVersion] ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM [SettingVersion];

SELECT 'FullUnderwritingCycle',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM [UnderwritingCycle] ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM [UnderwritingCycle];

SELECT 'FullEvidenceAssociation',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM [UnderwritingEvidenceAssociation] ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM [UnderwritingEvidenceAssociation];

SELECT 'QuoteCyclePointer',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT Id,CurrentUnderwritingCycleId FROM Quote ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM Quote;

SELECT 'Policy',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM Policy ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM Policy;
SELECT 'PolicyTerm',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM PolicyTerm ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM PolicyTerm;
SELECT 'PolicyTransaction',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM PolicyTransaction ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM PolicyTransaction;
SELECT 'PolicyVersion',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM PolicyVersion ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM PolicyVersion;
SELECT 'PolicyRegistration',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM PolicyRegistration ORDER BY VersionId,RiskItemId FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM PolicyRegistration;
SELECT 'IssueFinancialObligation',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM IssueFinancialObligation ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM IssueFinancialObligation;
SELECT 'IssueFinancialComponent',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM IssueFinancialComponent ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM IssueFinancialComponent;
SELECT 'Journal',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM Journal ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM Journal;
SELECT 'JournalLine',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM JournalLine ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM JournalLine;
SELECT 'PolicyDocumentRequest',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM PolicyDocumentRequest ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM PolicyDocumentRequest;
SELECT 'RetainedClientActivity',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM ClientActivity WHERE EventType<>N'policy.issued' ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM ClientActivity WHERE EventType<>N'policy.issued';
SELECT 'PolicyOutbox',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM OutboxWork WHERE Kind=N'policy-document' ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM OutboxWork WHERE Kind=N'policy-document';
SELECT 'AllTemplates',COUNT_BIG(*),CONVERT(varchar(64),HASHBYTES('SHA2_256',COALESCE((SELECT * FROM TemplateVersion ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]')),2) FROM TemplateVersion;