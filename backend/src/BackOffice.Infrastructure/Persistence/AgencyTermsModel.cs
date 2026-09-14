using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Persistence;
public sealed partial class BackOfficeDbContext
{
    private static void ConfigureAgencyTerms(ModelBuilder model)
    {
        var request=Record<AgencyTermsRequest>(model,"AgencyTermsRequest");
        Text(request,("ProposedInputFingerprint",64),("RequestReason",1000),("State",20),("DecisionReason",1000));
        request.Property(x=>x.BaseVersion).HasColumnType("binary(8)");Json(request,"ProposedSnapshot");
        request.HasAlternateKey(x=>new{x.Id,x.AgencyId});
        request.HasOne<Agency>().WithMany().HasForeignKey(x=>x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        request.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.RequestedBy).OnDelete(DeleteBehavior.NoAction);
        request.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.DecisionBy).OnDelete(DeleteBehavior.NoAction);
        request.HasIndex(x=>x.AgencyId).IsUnique().HasFilter("[State] = 'pending'");
        request.HasIndex(x=>new{x.AgencyId,x.CreatedAt,x.Id});
        Check(request,"State","[State] IN ('pending','applied','rejected','stale')");
        Check(request,"Requester","[CreatedBy] IS NOT NULL AND [CreatedBy]=[RequestedBy] AND LEN(TRIM([RequestReason]))>0");
        Check(request,"Fingerprint","LEN([ProposedInputFingerprint])=64 AND [ProposedInputFingerprint] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
        Check(request,"SnapshotBounds","DATALENGTH([ProposedSnapshot])<=131072 AND LEFT(LTRIM([ProposedSnapshot]),1)='{' AND JSON_VALUE([ProposedSnapshot],'$.effectiveFrom') IS NOT NULL AND TRY_CONVERT(date,JSON_VALUE([ProposedSnapshot],'$.effectiveFrom'),23) IS NOT NULL AND TRY_CONVERT(date,JSON_VALUE([ProposedSnapshot],'$.effectiveFrom'),23)=[EffectiveFrom]");
        Check(request,"Decision","([State]='pending' AND [DecisionBy] IS NULL AND [DecisionReason] IS NULL AND [DecidedAt] IS NULL) OR ([State] IN ('applied','rejected') AND [DecisionBy] IS NOT NULL AND [DecisionBy]<>[RequestedBy] AND [DecisionReason] IS NOT NULL AND LEN(TRIM([DecisionReason]))>0 AND [DecidedAt] IS NOT NULL AND [DecidedAt]>=[CreatedAt]) OR ([State]='stale' AND [DecisionBy] IS NULL AND [DecisionReason] IS NOT NULL AND LEN(TRIM([DecisionReason]))>0 AND [DecidedAt] IS NOT NULL AND [DecidedAt]>=[CreatedAt])");
        request.ToTable(t=>t.UseSqlOutputClause(false));
        var terms=Record<AgencyTermsVersion>(model,"AgencyTermsVersion");Json(terms,"Snapshot");
        terms.HasOne<Agency>().WithMany().HasForeignKey(x=>x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        terms.HasOne<AgencyStateRequest>().WithMany().HasForeignKey(x=>new{x.ApprovedStateRequestId,x.AgencyId}).HasPrincipalKey(x=>new{x.Id,x.AgencyId}).OnDelete(DeleteBehavior.NoAction);
        terms.HasOne<AgencyTermsRequest>().WithMany().HasForeignKey(x=>new{x.ApprovedTermsRequestId,x.AgencyId}).HasPrincipalKey(x=>new{x.Id,x.AgencyId}).OnDelete(DeleteBehavior.NoAction);
        terms.HasIndex(x=>new{x.AgencyId,x.Version}).IsUnique();terms.HasIndex(x=>new{x.AgencyId,x.EffectiveFrom}).IsUnique();
        terms.HasIndex(x=>x.ApprovedStateRequestId).IsUnique();terms.HasIndex(x=>x.ApprovedTermsRequestId).IsUnique();
        Check(terms,"Approval","([Version]=1 AND [ApprovedStateRequestId] IS NOT NULL AND [ApprovedTermsRequestId] IS NULL) OR ([Version]>1 AND [ApprovedStateRequestId] IS NULL AND [ApprovedTermsRequestId] IS NOT NULL)");
        Check(terms,"SnapshotBounds","DATALENGTH([Snapshot])<=131072 AND LEFT(LTRIM([Snapshot]),1)='{' AND JSON_VALUE([Snapshot],'$.effectiveFrom') IS NOT NULL AND TRY_CONVERT(date,JSON_VALUE([Snapshot],'$.effectiveFrom'),23) IS NOT NULL AND TRY_CONVERT(date,JSON_VALUE([Snapshot],'$.effectiveFrom'),23)=[EffectiveFrom]");
        terms.ToTable(t=>t.UseSqlOutputClause(false));
        var product=Record<AgencyProduct>(model,"AgencyProduct");
        product.HasOne<AgencyTermsVersion>().WithMany().HasForeignKey(x=>x.AgencyTermsVersionId).OnDelete(DeleteBehavior.NoAction);
        product.HasOne<ProductVersion>().WithMany().HasForeignKey(x=>x.ProductVersionId).OnDelete(DeleteBehavior.NoAction);
        product.HasIndex(x=>new{x.AgencyTermsVersionId,x.ProductVersionId}).IsUnique();
        Check(product,"Commission","[BrokerCommissionBasisPoints] BETWEEN 0 AND 10000");
        product.ToTable(t=>t.UseSqlOutputClause(false));
    }
}
