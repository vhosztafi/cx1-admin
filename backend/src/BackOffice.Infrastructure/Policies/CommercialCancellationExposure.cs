using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public static partial class CommercialExposureService
{
    internal static async Task RecordCancellation(BackOfficeDbContext db,PolicyVersion version,CancellationIssueDecision decision,
        DateTimeOffset now,CancellationToken token)
    {
        await CommercialExposureLock.RequireAsync(db,token);
        var basis=await db.Set<CommercialExposureVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.VersionId==decision.BaseVersionId,token)
            ??throw new QuoteOperationException(409,"commercial-cancellation-base-exposure-required");
        if(basis.PolicyId!=version.PolicyId || basis.TermId!=version.TermId || basis.TransactionKind=="cancellation" ||
            decision.PolicyId!=version.PolicyId || decision.BaseTermId!=version.TermId || decision.EffectiveAt!=version.EffectiveAt || version.ProcessedAt!=now)
            throw new InvalidOperationException("Commercial cancellation release must retain its exact owned base and decision.");
        // A reduction requires current cancellation authority, not approval for
        // additional capacity. An over-limit book must still permit release.
        var header=await CommercialExposureProjection.AppendAsync(db,version.Id,basis.BinderVersionId,decision.ActorId,token);
        if(header.BookId!=basis.BookId || header.LocationsJson!="[]")throw new InvalidOperationException("Commercial cancellation requires an empty exposure in the original book.");
        var json=JsonSerializer.Serialize(new{format="commercial-cancellation-exposure-decision-1",bookId=basis.BookId,
            policyId=version.PolicyId,termId=version.TermId,transactionId=version.TransactionId,versionId=version.Id,
            sourceHash=Convert.ToHexStringLower(version.ContentHash),baseExposureVersionId=basis.Id,baseVersionId=basis.VersionId,
            baseSourceHash=Convert.ToHexStringLower(basis.SourceHash),binderVersionId=basis.BinderVersionId,
            cancellationIssueDecisionId=decision.Id,cancellationPreviewId=decision.PreviewId,cancellationApprovalId=decision.ApprovalId,
            previewHash=Convert.ToHexStringLower(decision.PreviewHash),authorityGrantId=decision.AuthorityGrantId,authorityVersionId=decision.AuthorityVersionId,
            assessedAt=now,effectiveAt=version.EffectiveAt,termStartsAt=header.TermStartsAt,termEndsAt=header.TermEndsAt,propertySum="0.00"},QuoteRatingService.Json);
        db.Add(new CommercialExposureIssueDecision{ExposureVersionId=header.Id,AssessedAt=now,DecisionJson=json,
            DecisionHash=SHA256.HashData(Encoding.UTF8.GetBytes(json)),CreatedAt=now,CreatedBy=decision.ActorId});
        await db.SaveChangesAsync(token);
    }
}
