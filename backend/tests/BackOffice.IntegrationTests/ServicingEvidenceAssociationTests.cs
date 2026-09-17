using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingEvidenceAssociationStorage(BackOfficeDbContext db,ServicingCycle cycle,Guid otherDraftId,Guid writer,Guid reviewer,DateTimeOffset now)
    {
        var content=Encoding.UTF8.GetBytes("Fictional reviewed proof");var hash=Convert.ToHexStringLower(SHA256.HashData(content));
        var file=new ServicingEvidenceFile{DraftId=cycle.DraftId,FileName="proof.txt",ContentType="text/plain",Content=content,ByteLength=content.Length,Sha256=hash,CreatedBy=writer,CreatedAt=now};
        var foreignFile=new ServicingEvidenceFile{DraftId=otherDraftId,FileName="foreign.txt",ContentType="text/plain",Content=content,ByteLength=content.Length,Sha256=hash,CreatedBy=writer,CreatedAt=now};
        db.AddRange(file,foreignFile);await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var fingerprint=new string('a',64);var evidenceId=Guid.NewGuid();var ratingId=cycle.CurrentRatingId!.Value;
        using var input=JsonDocument.Parse(cycle.InputJson);var driver=input.RootElement.GetProperty("slices")[0].GetProperty("input").GetProperty("drivers")[0].GetProperty("id").GetGuid();
        async Task Attach(Guid id,Guid rating,Guid revision,Guid fileId,Guid? target,string code="driving-record") =>
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingEvidenceAssociation (Id,DraftId,CycleId,RevisionId,RatingId,FileId,RequirementCode,RiskItemId,InputFingerprint,Reason,CreatedBy,CreatedAt,UpdatedAt) VALUES ({id},{cycle.DraftId},{cycle.Id},{revision},{rating},{fileId},{code},{target},{fingerprint},'Fictional evidence association',{writer},{now},{now})");
        await Attach(evidenceId,ratingId,cycle.RevisionId,file.Id,driver);
        Assert.Equal(547,(await Assert.ThrowsAsync<SqlException>(()=>Attach(Guid.NewGuid(),ratingId,cycle.RevisionId,foreignFile.Id,driver))).Number);
        await Assert.ThrowsAsync<SqlException>(()=>Attach(Guid.NewGuid(),Guid.NewGuid(),cycle.RevisionId,file.Id,driver));
        await Assert.ThrowsAsync<SqlException>(()=>Attach(Guid.NewGuid(),ratingId,Guid.NewGuid(),file.Id,driver));
        await Assert.ThrowsAsync<SqlException>(()=>Attach(Guid.NewGuid(),ratingId,cycle.RevisionId,Guid.NewGuid(),driver));
        await Assert.ThrowsAsync<SqlException>(()=>Attach(Guid.NewGuid(),ratingId,cycle.RevisionId,file.Id,Guid.NewGuid()));
        await Assert.ThrowsAsync<SqlException>(()=>Attach(Guid.NewGuid(),ratingId,cycle.RevisionId,file.Id,null));
        await Assert.ThrowsAsync<SqlException>(()=>Attach(Guid.NewGuid(),ratingId,cycle.RevisionId,file.Id,driver,"premises-security"));
        var otherId=Guid.NewGuid();await Attach(otherId,ratingId,cycle.RevisionId,file.Id,null,"motor-trader-proof");
        async Task Event(Guid id,Guid association,int sequence,string kind,string? outcome,Guid? authority,string digest) =>
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingEvidenceEvent (Id,AssociationId,DraftId,CycleId,RevisionId,RatingId,Sequence,Kind,Outcome,Reason,ActorId,AuthorityVersionId,InputFingerprint,RecordedAt,CreatedBy,CreatedAt) VALUES ({id},{association},{cycle.DraftId},{cycle.Id},{cycle.RevisionId},{ratingId},{sequence},{kind},{outcome},'Fictional evidence review',{reviewer},{authority},{digest},{now},{reviewer},{now})");
        var accepted=Guid.NewGuid();await Event(accepted,evidenceId,1,"review","accepted",cycle.AuthorityVersionId,fingerprint);
        await Assert.ThrowsAsync<SqlException>(()=>Event(Guid.NewGuid(),evidenceId,2,"review","accepted",cycle.AuthorityVersionId,new string('b',64)));
        await Assert.ThrowsAsync<SqlException>(()=>Event(Guid.NewGuid(),evidenceId,2,"review","accepted",null,fingerprint));
        await Assert.ThrowsAsync<SqlException>(()=>Event(Guid.NewGuid(),evidenceId,3,"review","accepted",cycle.AuthorityVersionId,fingerprint));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingEvidenceAssociation SET LatestReviewId={accepted} WHERE Id={evidenceId}");
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingEvidenceAssociation SET LatestReviewId={accepted} WHERE Id={otherId}"));
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingEvidenceAssociation SET WithdrawnEventId={accepted} WHERE Id={evidenceId}"));
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingEvidenceAssociation SET RequirementCode='photocard-both-sides' WHERE Id={evidenceId}"));
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE ServicingEvidenceAssociation WHERE Id={otherId}"));
        var rejected=Guid.NewGuid();await Event(rejected,evidenceId,2,"review","rejected",cycle.AuthorityVersionId,fingerprint);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingEvidenceAssociation SET LatestReviewId={rejected} WHERE Id={evidenceId}");
        Assert.Equal(51328,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingEvidenceAssociation SET LatestReviewId={accepted} WHERE Id={evidenceId}"))).Number);
        Assert.Equal(51328,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingEvidenceAssociation SET LatestReviewId=NULL WHERE Id={evidenceId}"))).Number);
        var withdrawn=Guid.NewGuid();await Event(withdrawn,evidenceId,3,"withdrawal",null,null,fingerprint);
        Assert.Equal(51328,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingEvidenceAssociation SET LatestReviewId={withdrawn} WHERE Id={evidenceId}"))).Number);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingEvidenceAssociation SET WithdrawnEventId={withdrawn} WHERE Id={evidenceId}");
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingEvidenceAssociation SET WithdrawnEventId=NULL WHERE Id={evidenceId}"));
        await Assert.ThrowsAsync<SqlException>(()=>Event(Guid.NewGuid(),evidenceId,4,"review","accepted",cycle.AuthorityVersionId,fingerprint));
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingEvidenceEvent SET Outcome='rejected' WHERE Id={accepted}"));
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE ServicingEvidenceEvent WHERE Id={withdrawn}"));
        Assert.Equal(3,await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ServicingEvidenceEvent").SingleAsync());
    }
}
