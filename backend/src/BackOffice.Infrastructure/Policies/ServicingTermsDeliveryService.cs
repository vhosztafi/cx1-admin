using System.Text;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record ServicingTermsRecipient(Guid Id,string Name,string Email);

public sealed partial class ServicingTermsService
{
    public const string WorkKind="servicing-delivery";

    public Task<CommandOutcome> SendAsync(ActorContext actor,Guid draftId,Guid cycleId,Guid termsId,IReadOnlyList<Guid> recipientIds,
        byte[] version,Guid lease,string key,Guid correlation,CancellationToken token=default)
    {
        if(new[]{draftId,cycleId,termsId,lease}.Contains(Guid.Empty) || version is null || version.Length!=8 || recipientIds is null ||
            recipientIds.Count is <1 or >20 || recipientIds.Contains(Guid.Empty) || recipientIds.Distinct().Count()!=recipientIds.Count)
            throw new QuoteOperationException(422,"servicing-delivery-input-invalid");
        var ids=recipientIds.Order().ToArray();ServicingDecisionContext? held=null;ServicingTermsVersion? terms=null;ServicingTermsRecipient[]? recipients=null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/drafts/{draftId:D}/terms/send",key,correlation),
            new{draftId,cycleId,termsId,ids,version=Convert.ToBase64String(version),lease},"servicing.terms-queued",
            async(db,ct)=>
            {
                var now=time.GetUtcNow();held=await ServicingDecisionContext.Hold(db,actor,draftId,"policy-draft-write",now,ct,cycleId);
                terms=await CurrentTerms(db,held,termsId,now,ct);await Ready(db,held,now,true,ct);
                recipients=await Recipients(db,held,ids,ct);
            },
            async(db,ct)=>
            {
                await held!.Current(db,factory,time,version,lease,ct);var now=time.GetUtcNow();
                var setting=await db.Set<SettingVersion>().FromSqlRaw("SELECT * FROM SettingVersion WITH(HOLDLOCK) WHERE Scope=N'servicing-delivery'").AsNoTracking()
                    .Where(x=>x.EffectiveFrom<=now).OrderByDescending(x=>x.Version).FirstOrDefaultAsync(ct);
                if(setting is null || ServicingTermsSeed.Scenario(setting) is null)throw new QuoteOperationException(503,"servicing-delivery-configuration-unavailable");
                var row=new ServicingTermsDelivery{DraftId=draftId,CycleId=cycleId,RevisionId=held.Cycle.RevisionId,RatingId=held.Rating.Id,TermsVersionId=termsId,
                    ScenarioVersionId=setting.Id,RecipientSnapshotJson=JsonSerializer.Serialize(recipients,ServicingRatingService.Json),AssuranceHashAtSend=await Assurance(db,held,ct),
                    SentBy=actor.UserId,CreatedAt=now,UpdatedAt=now,CreatedBy=actor.UserId};
                row.PayloadJson=DeliveryPayload(row,terms!);row.PayloadHash=Hash(row.PayloadJson);
                var work=new OutboxWork{Kind=WorkKind,SubjectRecordId=row.Id,ScenarioVersionId=setting.Id,OperationKey=$"servicing-delivery/{row.Id:N}",
                    Payload=JsonSerializer.Serialize(new{draftId,deliveryId=row.Id}),CreatedAt=now,UpdatedAt=now,CreatedBy=actor.UserId,NextAttemptAt=now,CorrelationId=correlation};
                db.Add(work);await db.SaveChangesAsync(ct);row.WorkId=work.Id;db.Add(row);await db.SaveChangesAsync(ct);
                held.Cycle.CurrentAcceptanceId=null;held.Cycle.CurrentDeliveryId=row.Id;
                return await held.Receipt(db,row.Id,202,now,ct);
            },token);
    }

    internal static async Task<ServicingTermsRecipient[]> Recipients(BackOfficeDbContext db,ServicingDecisionContext held,IReadOnlyList<Guid> ids,CancellationToken token)
    {
        var source=held.Scope.Source.Quote;
        var rows=await db.Set<Contact>().FromSqlInterpolated($"SELECT * FROM Contact WITH(HOLDLOCK) WHERE ClientId={source.ClientId} AND RelationshipId={source.RelationshipId}").AsNoTracking()
            .Where(x=>ids.Contains(x.Id)).ToArrayAsync(token);
        var result=new List<ServicingTermsRecipient>();
        foreach(var id in ids.Order())
        {
            var row=rows.SingleOrDefault(x=>x.Id==id && x.EndedAt==null);
            if(row is null || string.IsNullOrWhiteSpace(row.DeclaredFullName) || string.IsNullOrWhiteSpace(row.Email) ||
                !System.Net.Mail.MailAddress.TryCreate(row.Email,out var address) || address.Address!=row.Email || row.Email.Any(char.IsControl))
                throw new QuoteOperationException(409,"servicing-recipient-unavailable");
            result.Add(new(row.Id,row.DeclaredFullName,row.Email));
        }
        return result.ToArray();
    }

    private static string DeliveryPayload(ServicingTermsDelivery delivery,ServicingTermsVersion terms)
    {
        using var stream=new MemoryStream();using(var writer=new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();writer.WriteString("format","servicing-delivery-1");writer.WriteString("deliveryId",delivery.Id);
            writer.WriteString("draftId",delivery.DraftId);writer.WriteString("cycleId",delivery.CycleId);writer.WriteString("termsVersionId",terms.Id);
            writer.WriteString("termsHash",terms.TermsHash);writer.WritePropertyName("recipients");writer.WriteRawValue(delivery.RecipientSnapshotJson);
            // Embed the retained contract bytes; reparsing/reserializing would
            // change escaping and therefore the document's exact identity.
            writer.WritePropertyName("document");writer.WriteRawValue(terms.TermsJson);writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
