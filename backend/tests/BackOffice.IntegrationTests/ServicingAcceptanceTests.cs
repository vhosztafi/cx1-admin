using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task<string> VerifyServicingAcceptance(BackOfficeDbContext db,DecisionFixture f,ServicingCycle cycle,ServicingTermsVersion contract,Guid fileId,Guid fence,string etag,bool posting=false,bool issue=false,string? issuePassword=null)
    {
        static byte[] Version(string value)=>Convert.FromBase64String(value.Trim('"'));
        static string Key()=>Guid.NewGuid().ToString();
        var terms=new ServicingTermsService(f.Factory,f.Clock);var evidence=new ServicingEvidenceService(f.Factory,f.Clock);
        var delivery=await db.Set<ServicingTermsDelivery>().AsNoTracking().SingleAsync(x=>x.CycleId==cycle.Id);
        var purpose=Assert.Single((await evidence.RequirementsAsync(f.Underwriter,cycle.DraftId)).Requirements,x=>x.Requirement.Code=="acceptance-proof").Requirement;
        var attached=await evidence.AttachAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(etag),fence,fileId,purpose.Code,null,purpose.InputFingerprint,"Attach fictional separate acceptance",Key(),Guid.NewGuid());
        var proof=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==attached.ResourceId);
        var reviewed=await evidence.ReviewAsync(f.Underwriter,cycle.DraftId,cycle.Id,proof.Id,Version(attached.Etag!),fence,proof.RowVersion,"accepted",purpose.InputFingerprint,"Review fictional customer acceptance",Key(),Guid.NewGuid());
        var assessment=await terms.ReadAsync(f.Underwriter,cycle.DraftId);Assert.False(assessment.AcceptanceApplicable);
        var input=new ServicingAcceptanceInput(cycle.Id,cycle.CurrentRatingId!.Value,contract.Id,delivery.Id,contract.TermsHash,assessment.AssuranceHash,
            "Fictional policy customer",f.Clock.GetUtcNow(),"email",proof.Id);
        var version=Version(reviewed.Etag!);var key=Key();
        foreach(var changed in new[]{input with{TermsHash=new string('f',64)},input with{AssuranceHash=new string('a',64)},
            input with{EvidenceAssociationId=Guid.NewGuid()},input with{AcceptedAt=delivery.CompletedAt!.Value.AddTicks(-1)}})
            await Assert.ThrowsAsync<QuoteOperationException>(()=>terms.AcceptAsync(f.Underwriter,cycle.DraftId,version,fence,changed,Key(),Guid.NewGuid()));
        var accepted=await terms.AcceptAsync(f.Underwriter,cycle.DraftId,version,fence,input,key,Guid.NewGuid());Assert.Equal(201,accepted.Status);
        Assert.True((await terms.AcceptAsync(f.Underwriter,cycle.DraftId,version,fence,input,key,Guid.NewGuid())).Replayed);
        var row=await db.Set<ServicingAcceptance>().AsNoTracking().SingleAsync();
        Assert.Equal(proof.Id,row.EvidenceAssociationId);Assert.Equal(contract.Id,row.TermsVersionId);Assert.Equal(delivery.Id,row.DeliveryId);
        Assert.Equal(input.AssuranceHash,row.AssuranceHash);Assert.Equal(input.TermsHash,row.TermsHash);
        Assert.True((await terms.ReadAsync(f.Underwriter,cycle.DraftId)).AcceptanceApplicable);
        if(posting){await VerifyServicingPosting(db,f,cycle,row);return accepted.Etag!;}
        if(issue){await VerifyServicingIssue(db,f,cycle,row,fence,Version(accepted.Etag!),issuePassword!);return accepted.Etag!;}
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingAcceptance SET AccepterLabel='Changed customer' WHERE Id={row.Id}"));
        proof=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==proof.Id);
        var withdrawn=await evidence.WithdrawAsync(f.Underwriter,cycle.DraftId,cycle.Id,proof.Id,Version(accepted.Etag!),fence,proof.RowVersion,"Withdraw fictional acceptance evidence",Key(),Guid.NewGuid());
        Assert.False((await terms.ReadAsync(f.Underwriter,cycle.DraftId)).AcceptanceApplicable);
        await Assert.ThrowsAsync<QuoteOperationException>(()=>terms.AcceptAsync(f.Underwriter,cycle.DraftId,version,fence,input,key,Guid.NewGuid()));
        Assert.Equal(1,await db.Set<ServicingAcceptance>().CountAsync());return withdrawn.Etag!;
    }
}
