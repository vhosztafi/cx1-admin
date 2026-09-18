using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record ServicingTermsSnapshot(Guid Id,int Sequence,Guid RatingId,Guid TemplateVersionId,string TermsHash,DateTimeOffset PreparedAt,JsonElement Document,bool Applicable);
public sealed record ServicingDeliverySnapshot(Guid Id,Guid TermsVersionId,string State,Guid JobId,string JobState,DateTimeOffset QueuedAt,DateTimeOffset? CompletedAt,
    string? OutcomeCode,IReadOnlyList<ServicingTermsRecipient> Recipients);
public sealed record ServicingAcceptanceSnapshot(Guid Id,Guid TermsVersionId,Guid DeliveryId,string AccepterLabel,string Channel,DateTimeOffset AcceptedAt,
    DateTimeOffset RecordedAt,Guid EvidenceAssociationId,Guid EvidenceReviewId);
public sealed record ServicingTemplateOption(Guid Id,string Code,int Version,string Title);
public sealed record ServicingTermsView(Guid DraftId,Guid CycleId,Guid RevisionId,Guid RatingId,string DraftEtag,bool Applicable,string AssuranceHash,
    bool CanPrepare,bool CanSend,string? BlockingCode,string? SendBlockingCode,bool AcceptanceApplicable,ServicingTermsSnapshot? Terms,
    ServicingDeliverySnapshot? Delivery,ServicingAcceptanceSnapshot? Acceptance,IReadOnlyList<ServicingTemplateOption> Templates,IReadOnlyList<ServicingTermsRecipient> RecipientOptions);

public sealed partial class ServicingTermsService
{
    public async Task<ServicingTermsView> ReadAsync(ActorContext actor,Guid draftId,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var now=time.GetUtcNow();var held=await ServicingDecisionContext.Hold(db,actor,draftId,"policy-read",now,token,write:false);
        var assurance=await Assurance(db,held,token);string? blocking=null;string? sendBlocking=null;
        try{await Ready(db,held,now,false,token);}catch(QuoteOperationException e){blocking=e.Code;}
        ServicingTermsSnapshot? terms=null;ServicingDeliverySnapshot? delivery=null;ServicingAcceptanceSnapshot? acceptance=null;var acceptanceApplicable=false;
        if(held.Cycle.CurrentTermsVersionId is {} termsId)
        {
            var row=await db.Set<ServicingTermsVersion>().AsNoTracking().SingleAsync(x=>x.Id==termsId && x.CycleId==held.Cycle.Id,token);var applicable=false;
            try{_=await CurrentTerms(db,held,row.Id,now,token);applicable=held.Rating.ExpiresAt>now;}catch(QuoteOperationException){ }
            terms=new(row.Id,row.Sequence,row.RatingId,row.TemplateVersionId,row.TermsHash,row.PreparedAt,JsonSerializer.Deserialize<JsonElement>(row.TermsJson),applicable);
        }
        if(terms?.Applicable==true)
        {try{await Ready(db,held,now,true,token);}catch(QuoteOperationException e){sendBlocking=e.Code;}}
        else sendBlocking="servicing-terms-required";
        if(held.Cycle.CurrentDeliveryId is {} deliveryId)
        {
            var row=await db.Set<ServicingTermsDelivery>().AsNoTracking().SingleAsync(x=>x.Id==deliveryId && x.CycleId==held.Cycle.Id,token);
            var jobState=await db.Set<OutboxWork>().Where(x=>x.Id==row.WorkId).Select(x=>x.State).SingleAsync(token);
            delivery=new(row.Id,row.TermsVersionId,row.State,row.WorkId,jobState,row.CreatedAt,row.CompletedAt,row.OutcomeCode,
                JsonSerializer.Deserialize<ServicingTermsRecipient[]>(row.RecipientSnapshotJson,ServicingRatingService.Json)!);
        }
        if(held.Cycle.CurrentAcceptanceId is {} acceptanceId)
        {
            var row=await db.Set<ServicingAcceptance>().AsNoTracking().SingleAsync(x=>x.Id==acceptanceId && x.CycleId==held.Cycle.Id,token);
            acceptance=new(row.Id,row.TermsVersionId,row.DeliveryId,row.AccepterLabel,row.Channel,row.AcceptedAt,row.RecordedAt,row.EvidenceAssociationId,row.EvidenceReviewId);
            acceptanceApplicable=await AcceptanceCurrent(db,held,row,assurance,now,token);
        }
        var templates=await db.Set<TemplateVersion>().AsNoTracking().Where(x=>x.ProductId==held.Cycle.ProductId && x.Kind=="servicing-terms" && x.State=="published" &&
            x.EffectiveFrom<=now && x.EffectiveTo>now).OrderBy(x=>x.Code).ThenByDescending(x=>x.Version).Take(51).ToArrayAsync(token);
        if(templates.Length>50)throw new QuoteOperationException(409,"servicing-template-limit");
        var options=new List<ServicingTemplateOption>();
        foreach(var template in templates)
        {
            _=await Template(db,held,template.Id,now,token);
            using var doc=JsonDocument.Parse(template.ContentJson);options.Add(new(template.Id,template.Code,template.Version,doc.RootElement.GetProperty("title").GetString()!));
        }
        var source=held.Scope.Source.Quote;
        var contacts=await db.Set<Contact>().AsNoTracking().Where(x=>x.ClientId==source.ClientId && x.RelationshipId==source.RelationshipId && x.EndedAt==null && x.Email!=null)
            .OrderBy(x=>x.Id).Take(201).Select(x=>new{x.Id,x.DeclaredFullName,x.Email}).ToArrayAsync(token);
        if(contacts.Length>200)throw new QuoteOperationException(409,"servicing-recipient-limit");
        var recipients=new List<ServicingTermsRecipient>();
        foreach(var contact in contacts)
            if(!string.IsNullOrWhiteSpace(contact.DeclaredFullName) && !string.IsNullOrWhiteSpace(contact.Email) &&
                System.Net.Mail.MailAddress.TryCreate(contact.Email,out var address) && address.Address==contact.Email && !contact.Email.Any(char.IsControl))
                recipients.Add(new(contact.Id,contact.DeclaredFullName,contact.Email));
        if(options.Count==0 && blocking is null)blocking="servicing-template-unavailable";
        var canWrite=held.Scope.Source.Scope.Actor.HasCapability("policy-draft-write");
        var result=new ServicingTermsView(draftId,held.Cycle.Id,held.Cycle.RevisionId,held.Rating.Id,"\""+Convert.ToBase64String(held.Scope.Draft.RowVersion)+"\"",
            held.Rating.ExpiresAt>now,assurance,canWrite && blocking is null,canWrite && sendBlocking is null,blocking,sendBlocking,acceptanceApplicable,terms,delivery,acceptance,options,recipients);
        await tx.CommitAsync(token);return result;
    }
}
