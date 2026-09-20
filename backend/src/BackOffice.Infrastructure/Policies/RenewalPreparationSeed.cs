using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public static class RenewalPreparationSeed
{
    public static async Task SeedCommercialAsync(BackOfficeDbContext db,CancellationToken token=default)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Commercial renewal seed requires held initialization.");
        var actor=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="system-admin@cover.example",token);
        if(actor.State!="active")throw new InvalidOperationException("Commercial renewal initialization requires the active demo administrator.");
        var clock=await db.Set<DemoClock>().SingleAsync(token);var now=clock.FrozenAt??DateTimeOffset.UtcNow;
        if(!await db.Set<SettingVersion>().AnyAsync(x=>x.Scope==RenewalConfiguration.CommercialScope,token))
        {
            var json=JsonSerializer.Serialize(new{demo=true,kind=RenewalConfiguration.CommercialScope,schemaVersion="1",ruleVersion="commercial-demo-renewal-1",currency="GBP",
                allowedTermMonths=new[]{6,12},defaultTermMonths=12,lossRatioThresholdBasisPoints=5000,experienceLoadingBasisPoints=800,
                renewalFee="45.00",invitationDaysBeforeExpiry=45,lapseDaysAfterExpiry=14});
            if(RenewalConfiguration.Parse(json,RenewalConfiguration.CommercialScope) is null)throw new InvalidOperationException("Bundled commercial renewal settings are invalid.");
            db.Add(new SettingVersion{Scope=RenewalConfiguration.CommercialScope,Version=1,EffectiveFrom=now,Values=json,CreatedAt=now,CreatedBy=actor.Id});
        }
        var product=await db.Set<Product>().SingleAsync(x=>x.Code=="commercial-combined",token);
        var version=await db.Set<ProductVersion>().SingleAsync(x=>x.ProductId==product.Id&&x.Version==3,token);
        var binder=await db.Set<BinderVersion>().SingleAsync(x=>x.ProductId==product.Id&&x.Version=="commercial-demo-binder-1",token);
        // Only the original fictional publication may receive seed evidence;
        // any existing assessment, including an adverse one, is authoritative.
        if(version.State=="published"&&binder.State=="published"&&
            !await db.Set<FairValueAssessmentVersion>().AnyAsync(x=>x.ProductVersionId==version.Id&&x.BinderVersionId==binder.Id,token))
        {
            using var definition=JsonDocument.Parse(version.Definition);
            if(!definition.RootElement.TryGetProperty("demo",out var demo)||demo.ValueKind!=JsonValueKind.True)
                throw new InvalidOperationException("Commercial renewal cannot seed an assessment for a non-demo product.");
            var start=version.EffectiveFrom>binder.EffectiveFrom?version.EffectiveFrom:binder.EffectiveFrom;
            var end=version.EffectiveTo is{} until&&until<binder.EffectiveTo?until:binder.EffectiveTo;
            if(start<end)
            {
                var content=Encoding.UTF8.GetBytes($"FICTIONAL COMMERCIAL RENEWAL FAIR VALUE ASSESSMENT\nProduct version: {version.Id:D}\nBinder: {binder.Id:D}\nSynthetic demonstration only; not a real commercial or regulatory assessment.\nRecorded: {now:O}\n");
                var file=new ProductEvidenceFileVersion{ProductId=product.Id,ProductVersionId=version.Id,BinderVersionId=binder.Id,
                    FileName="fictional-fair-value-commercial-combined.txt",ContentType="text/plain",Content=content,ByteLength=content.Length,
                    Sha256=Convert.ToHexStringLower(SHA256.HashData(content)),CreatedBy=actor.Id,CreatedAt=now};
                db.Add(file);await db.SaveChangesAsync(token);
                db.Add(new FairValueAssessmentVersion{ProductId=product.Id,ProductVersionId=version.Id,BinderVersionId=binder.Id,EvidenceFileVersionId=file.Id,
                    ValidFrom=start,ValidTo=end,Outcome="pass",ApprovedBy=actor.Id,ApprovedAt=now,CreatedBy=actor.Id,CreatedAt=now,
                    Reason="FICTIONAL commercial demonstration assessment with retained synthetic evidence; not a real assessment."});
            }
        }
        await db.SaveChangesAsync(token);
    }

    public static async Task SeedAsync(BackOfficeDbContext db,CancellationToken token=default)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Renewal seed requires held initialization.");
        const string marker="renewal-demo-initialized";
        if(await db.Set<SettingVersion>().AnyAsync(x=>x.Scope==marker,token))return;
        var actor=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="system-admin@cover.example",token);
        if(actor.State!="active")throw new InvalidOperationException("Fictional assessment initialization requires the active demo administrator.");
        var clock=await db.Set<DemoClock>().SingleAsync(token);var now=clock.FrozenAt??DateTimeOffset.UtcNow;
        if(!await db.Set<SettingVersion>().AnyAsync(x=>x.Scope==RenewalConfiguration.Scope,token))
        {
            var json=JsonSerializer.Serialize(new{demo=true,kind=RenewalConfiguration.Scope,schemaVersion="1",ruleVersion="demo-servicing-1",currency="GBP",
                allowedTermMonths=new[]{6,12},defaultTermMonths=12,lossRatioThresholdBasisPoints=5000,experienceLoadingBasisPoints=800,
                renewalFee="35.00",invitationDaysBeforeExpiry=45,lapseDaysAfterExpiry=14});
            if(RenewalConfiguration.Parse(json) is null)throw new InvalidOperationException("Bundled renewal settings are invalid.");
            db.Add(new SettingVersion{Scope=RenewalConfiguration.Scope,Version=1,EffectiveFrom=now,Values=json,CreatedAt=now,CreatedBy=actor.Id});
        }
        foreach(var code in new[]{"motor-trade-road-risks","motor-trade-combined"})
        {
            var product=await db.Set<Product>().SingleAsync(x=>x.Code==code,token);
            var version=await db.Set<ProductVersion>().SingleAsync(x=>x.ProductId==product.Id && x.Version==2,token);
            var binder=await db.Set<BinderVersion>().SingleAsync(x=>x.ProductId==product.Id && x.Version=="demo-binder-1",token);
            // Restrict automatic demo facts to the original bundled identities.
            // Retirement and a pre-existing assessment remain authoritative.
            if(version.State!="published" || binder.State!="published" ||
                await db.Set<FairValueAssessmentVersion>().AnyAsync(x=>x.ProductVersionId==version.Id && x.BinderVersionId==binder.Id,token))continue;
            using var definition=JsonDocument.Parse(version.Definition);
            if(!definition.RootElement.TryGetProperty("demo",out var demo) || demo.ValueKind!=JsonValueKind.True)
                throw new InvalidOperationException("Renewal demo assessment cannot approve a non-demo product.");
            var start=version.EffectiveFrom>binder.EffectiveFrom?version.EffectiveFrom:binder.EffectiveFrom;
            var end=version.EffectiveTo is{} until && until<binder.EffectiveTo?until:binder.EffectiveTo;
            if(start>=end)continue;
            var content=Encoding.UTF8.GetBytes($"FICTIONAL DEMONSTRATION FAIR VALUE ASSESSMENT\nProduct: {code}\nProduct version: {version.Id:D}\nBinder: {binder.Id:D}\nOutcome: pass for this synthetic demonstration only. This is not a real commercial or regulatory assessment.\nRecorded: {now:O}\n");
            var file=new ProductEvidenceFileVersion{ProductId=product.Id,ProductVersionId=version.Id,BinderVersionId=binder.Id,
                FileName=$"fictional-fair-value-{code}.txt",ContentType="text/plain",Content=content,ByteLength=content.Length,
                Sha256=Convert.ToHexStringLower(SHA256.HashData(content)),CreatedBy=actor.Id,CreatedAt=now};
            db.Add(file);await db.SaveChangesAsync(token);
            db.Add(new FairValueAssessmentVersion{ProductId=product.Id,ProductVersionId=version.Id,BinderVersionId=binder.Id,EvidenceFileVersionId=file.Id,
                ValidFrom=start,ValidTo=end,Outcome="pass",ApprovedBy=actor.Id,ApprovedAt=now,CreatedBy=actor.Id,CreatedAt=now,
                Reason="FICTIONAL demonstration assessment with retained synthetic evidence; not a real commercial assessment."});
        }
        db.Add(new SettingVersion{Scope=marker,Version=1,EffectiveFrom=now,Values="{\"demo\":true,\"schemaVersion\":\"1\"}",CreatedAt=now,CreatedBy=actor.Id});
        await db.SaveChangesAsync(token);
    }
}
