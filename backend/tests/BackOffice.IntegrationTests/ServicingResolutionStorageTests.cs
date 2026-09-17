using System.Security.Cryptography;
using System.Text;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingResolutionStorage(BackOfficeDbContext db,ServicingCycle cycle,Guid referral,Guid condition,DateTimeOffset now,Guid actor)
    {
        var grant=await (from g in db.Set<UserAuthorityGrant>() join a in db.Set<AuthorityVersion>() on g.AuthorityVersionId equals a.Id
            where g.UserId==actor && g.RevokedAt==null && a.ProductVersionId==cycle.ProductVersionId && a.BinderVersionId==cycle.BinderVersionId select g).FirstAsync();
        var bytes=Encoding.UTF8.GetBytes("Fictional condition evidence");
        var file=new ServicingEvidenceFile{DraftId=cycle.DraftId,FileName="condition.txt",ContentType="text/plain",Content=bytes,ByteLength=bytes.Length,
            Sha256=Convert.ToHexStringLower(SHA256.HashData(bytes)),CreatedBy=actor,CreatedAt=now};
        db.Add(file);await db.SaveChangesAsync();
        var proof=new ServicingEvidenceAssociation{DraftId=cycle.DraftId,CycleId=cycle.Id,RevisionId=cycle.RevisionId,RatingId=cycle.CurrentRatingId!.Value,
            FileId=file.Id,RequirementCode="trading-history",InputFingerprint=new string('a',64),Reason="Fictional condition proof",CreatedBy=actor,CreatedAt=now,UpdatedAt=now};
        db.Add(proof);await db.SaveChangesAsync();
        var review=new ServicingEvidenceEvent{AssociationId=proof.Id,DraftId=cycle.DraftId,CycleId=cycle.Id,RevisionId=cycle.RevisionId,RatingId=cycle.CurrentRatingId.Value,
            Sequence=1,Kind="review",Outcome="accepted",Reason="Fictional condition review",ActorId=actor,AuthorityVersionId=grant.AuthorityVersionId,RecordedAt=now,
            InputFingerprint=proof.InputFingerprint,CreatedBy=actor,CreatedAt=now};
        db.Add(review);await db.SaveChangesAsync();proof.LatestReviewId=review.Id;await db.SaveChangesAsync();
        async Task Insert(Guid id,int sequence,Guid? owner=null,Guid? evidence=null,Guid? reviewed=null,Guid? grantId=null,string outcome="satisfied")=>
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingConditionResolution (Id,ConditionId,ReferralId,DraftId,CycleId,RevisionId,RatingId,AssociationId,ReviewId,InputFingerprint,Sequence,Outcome,Reason,ActorId,AuthorityVersionId,GrantId,RecordedAt,CreatedBy,CreatedAt) VALUES ({id},{owner??condition},{referral},{cycle.DraftId},{cycle.Id},{cycle.RevisionId},{cycle.CurrentRatingId},{evidence??proof.Id},{reviewed??review.Id},{proof.InputFingerprint},{sequence},{outcome},'Fictional condition resolution',{actor},{grant.AuthorityVersionId},{grantId??grant.Id},{now},{actor},{now})");
        var resolution=Guid.NewGuid();await Insert(resolution,1);
        var wrongPurpose=new ServicingEvidenceAssociation{DraftId=cycle.DraftId,CycleId=cycle.Id,RevisionId=cycle.RevisionId,RatingId=cycle.CurrentRatingId.Value,
            FileId=file.Id,RequirementCode="motor-trader-proof",InputFingerprint=proof.InputFingerprint,Reason="Fictional other proof purpose",CreatedBy=actor,CreatedAt=now,UpdatedAt=now};
        db.Add(wrongPurpose);await db.SaveChangesAsync();
        var otherReview=new ServicingEvidenceEvent{AssociationId=wrongPurpose.Id,DraftId=cycle.DraftId,CycleId=cycle.Id,RevisionId=cycle.RevisionId,RatingId=cycle.CurrentRatingId.Value,
            Sequence=1,Kind="review",Outcome="accepted",Reason="Fictional other proof review",ActorId=actor,AuthorityVersionId=grant.AuthorityVersionId,RecordedAt=now,
            InputFingerprint=proof.InputFingerprint,CreatedBy=actor,CreatedAt=now};
        db.Add(otherReview);await db.SaveChangesAsync();wrongPurpose.LatestReviewId=otherReview.Id;await db.SaveChangesAsync();
        Assert.Equal(51372,(await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),2,evidence:wrongPurpose.Id,reviewed:otherReview.Id))).Number);
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),2,evidence:proof.Id,reviewed:otherReview.Id));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),2,owner:Guid.NewGuid()));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),2,evidence:Guid.NewGuid()));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),2,reviewed:Guid.NewGuid()));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),3));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),1));
        var otherGrant=await db.Set<UserAuthorityGrant>().Where(x=>x.UserId!=actor).Select(x=>x.Id).FirstAsync();
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),2,grantId:otherGrant));
        await using(var transaction=await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={actor},RevocationReason='Fictional grant revocation' WHERE Id={grant.Id}");
            Assert.Equal(51373,(await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),2))).Number);
            await transaction.RollbackAsync();
        }
        Assert.Equal(51370,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingConditionResolution SET Outcome='rejected' WHERE Id={resolution}"))).Number);
        Assert.Equal(51370,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE ServicingConditionResolution WHERE Id={resolution}"))).Number);
        var rejected=new ServicingEvidenceEvent{AssociationId=proof.Id,DraftId=cycle.DraftId,CycleId=cycle.Id,RevisionId=cycle.RevisionId,RatingId=cycle.CurrentRatingId.Value,
            Sequence=2,Kind="review",Outcome="rejected",Reason="Fictional subsequent rejection",ActorId=actor,AuthorityVersionId=grant.AuthorityVersionId,RecordedAt=now,
            InputFingerprint=proof.InputFingerprint,CreatedBy=actor,CreatedAt=now};
        db.Add(rejected);await db.SaveChangesAsync();proof.LatestReviewId=rejected.Id;await db.SaveChangesAsync();
        Assert.Equal(51371,(await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),2))).Number);
        Assert.Equal(51371,(await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),2,reviewed:rejected.Id))).Number);
        await Insert(Guid.NewGuid(),2,reviewed:rejected.Id,outcome:"rejected");
        var withdrawal=new ServicingEvidenceEvent{AssociationId=proof.Id,DraftId=cycle.DraftId,CycleId=cycle.Id,RevisionId=cycle.RevisionId,RatingId=cycle.CurrentRatingId.Value,
            Sequence=3,Kind="withdrawal",Reason="Fictional condition withdrawal",ActorId=actor,RecordedAt=now,
            InputFingerprint=proof.InputFingerprint,CreatedBy=actor,CreatedAt=now};
        db.Add(withdrawal);await db.SaveChangesAsync();proof.WithdrawnEventId=withdrawal.Id;await db.SaveChangesAsync();
        Assert.Equal(51371,(await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),3,reviewed:rejected.Id,outcome:"rejected"))).Number);
        Assert.Equal(2,await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM ServicingConditionResolution WHERE ConditionId={condition}").SingleAsync());
    }
}
