using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingTermsStorage(BackOfficeDbContext db,DecisionFixture f,ServicingCycle cycle)
    {
        // Demo initialization now retains servicing templates. Roll back only
        // the empty delivery schema; older template kinds cannot be removed
        // while their immutable seeded documents exist.
        var migrator=db.GetService<IMigrator>();await migrator.MigrateAsync("20260918095455_ServicingTermsEvidence");await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(0,await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ServicingTermsVersion").SingleAsync());
        var now=f.Clock.GetUtcNow();var rating=await db.Set<ServicingRatingResult>().AsNoTracking().SingleAsync(x=>x.Id==cycle.CurrentRatingId);
        var template=new TemplateVersion{Code="fictional-servicing-terms-storage",Kind="servicing-terms",Version=1,ProductId=cycle.ProductId,
            EffectiveFrom=now.AddDays(-1),EffectiveTo=now.AddYears(1),ContentJson="{\"format\":\"servicing-template-1\",\"title\":\"Fictional servicing terms\",\"notice\":\"Fictional retained risk and price\"}"};
        db.Add(template);await db.SaveChangesAsync();
        using var input=JsonDocument.Parse(cycle.InputJson);
        var dates=input.RootElement.GetProperty("slices").EnumerateArray().Select(x=>x.GetProperty("effectiveAt").GetDateTimeOffset()).ToArray();
        string Payload(Guid? owner=null,decimal? premium=null,DateTimeOffset[]? effective=null)=>JsonSerializer.Serialize(new{
            format="servicing-contract-1",draftId=owner??cycle.DraftId,cycleId=cycle.Id,revisionId=cycle.RevisionId,baseVersionId=cycle.BaseVersionId,
            ratingId=rating.Id,templateVersionId=template.Id,inputHash=Convert.ToHexStringLower(cycle.InputHash),ratingHash=Convert.ToHexStringLower(rating.ResultHash),
            effectiveDates=effective??dates,price=new{premium=premium??rating.Premium,tax=rating.Tax,fee=rating.Fee,brokerCommission=rating.BrokerCommission,grossPayable=rating.GrossPayable,netDue=rating.NetDue}});
        var payload=Payload();var assurance=new string('a',64);
        Task<int> Insert(Guid id,int sequence,string json,Guid? owner=null,Guid? baseId=null,string? hash=null)=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingTermsVersion (Id,DraftId,CycleId,RevisionId,BaseVersionId,RatingId,TemplateVersionId,Sequence,TermsJson,TermsHash,AssuranceHashAtPreparation,PreparedBy,PreparedAt,CreatedAt,CreatedBy) VALUES ({id},{owner??cycle.DraftId},{cycle.Id},{cycle.RevisionId},{baseId??cycle.BaseVersionId},{rating.Id},{template.Id},{sequence},{json},{hash??Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)))},{assurance},{f.Underwriter.UserId},{now},{now},{f.Underwriter.UserId})");
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),1,payload,Guid.NewGuid()));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),1,payload,baseId:Guid.NewGuid()));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),1,payload,hash:new string('f',64)));
        Assert.Equal(51500,(await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),1,Payload(Guid.NewGuid())))).Number);
        Assert.Equal(51500,(await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),1,Payload(premium:rating.Premium+1)))).Number);
        Assert.Equal(51500,(await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),1,Payload(premium:rating.Premium+0.001m)))).Number);
        Assert.Equal(51500,(await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),1,Payload(effective:[dates[0].AddDays(1)])))).Number);
        var first=Guid.NewGuid();await Insert(first,1,payload);
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),1,payload));
        Assert.Equal(51501,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingTermsVersion SET AssuranceHashAtPreparation={new string('b',64)} WHERE Id={first}"))).Number);
        Assert.Equal(51501,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE ServicingTermsVersion WHERE Id={first}"))).Number);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCycle SET CurrentTermsVersionId={first} WHERE Id={cycle.Id}");
        var second=Guid.NewGuid();await Insert(second,2,payload);await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCycle SET CurrentTermsVersionId={second} WHERE Id={cycle.Id}");
        Assert.Equal(51502,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCycle SET CurrentTermsVersionId={first} WHERE Id={cycle.Id}"))).Number);
        await VerifyTermsProofOwnership(db,f,cycle,first,second,now);
        await VerifyServicingDeliveryStorage(db,f,cycle,first,second,now);
        now=rating.ExpiresAt;Assert.Equal(51500,(await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),3,payload))).Number);
        Assert.Equal(2,await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ServicingTermsVersion").SingleAsync());
    }

    private static async Task VerifyTermsProofOwnership(BackOfficeDbContext db,DecisionFixture f,ServicingCycle cycle,Guid oldTerms,Guid currentTerms,DateTimeOffset now)
    {
        var bytes=Encoding.UTF8.GetBytes("Fictional signed servicing terms for storage tests.");
        var file=new ServicingEvidenceFile{DraftId=cycle.DraftId,FileName="fictional-terms.txt",ContentType="text/plain",Content=bytes,ByteLength=bytes.Length,
            Sha256=Convert.ToHexStringLower(SHA256.HashData(bytes)),CreatedBy=f.Underwriter.UserId,CreatedAt=now};
        db.Add(file);await db.SaveChangesAsync();
        Task<int> Insert(Guid id,string purpose,Guid? terms)=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingEvidenceAssociation (Id,DraftId,CycleId,RevisionId,RatingId,FileId,RequirementCode,TermsVersionId,InputFingerprint,Reason,CreatedAt,UpdatedAt,CreatedBy) VALUES ({id},{cycle.DraftId},{cycle.Id},{cycle.RevisionId},{cycle.CurrentRatingId},{file.Id},{purpose},{terms},{new string('a',64)},{"Fictional exact terms ownership proof"},{now},{now},{f.Underwriter.UserId})");
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),"signed-statement",null));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),"motor-trader-proof",currentTerms));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),"signed-statement",Guid.NewGuid()));
        Assert.Equal(51503,(await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),"signed-statement",oldTerms))).Number);
        var signed=Guid.NewGuid();await Insert(signed,"signed-statement",currentTerms);
        await Insert(Guid.NewGuid(),"acceptance-proof",currentTerms);
        Assert.Equal(51504,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingEvidenceAssociation SET TermsVersionId={oldTerms} WHERE Id={signed}"))).Number);
    }
}
