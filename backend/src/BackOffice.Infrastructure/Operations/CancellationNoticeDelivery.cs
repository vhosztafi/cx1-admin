using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

internal static class CancellationNoticeDelivery
{
    // Only this explicit source association permits the internally retained notice
    // to be delivered. General agency attachment visibility remains unchanged.
    internal static async Task<DeliveryContent> Capture(BackOfficeDbContext db, ActorContext actor, Guid consequenceId, Guid documentVersionId, CancellationToken token)
    {
        var hint = await db.Set<CancellationConsequence>().AsNoTracking().SingleAsync(x => x.Id == consequenceId, token);
        var source = await CancellationOperationsAuthority.Hold(db, hint.WorkId, token);
        if (source.Actor.UserId != actor.UserId || hint.Kind != "notice") throw CancellationOperationsAuthority.Invalid();
        var document = await (from version in db.Set<DocumentVersion>() join d in db.Set<OperationalDocument>() on version.DocumentId equals d.Id
            where version.Id == documentVersionId && version.CancellationConsequenceId == consequenceId && version.PolicyVersionId == hint.VersionId && d.Kind == "cancellation-notice" select d).AsNoTracking().SingleOrDefaultAsync(token)
            ?? throw CancellationOperationsAuthority.Invalid();
        var held = await OperationalScope.HoldSubjects(db, actor, [document.SubjectId], "document-send", token);
        if (held.Subjects.Single().PolicyId != hint.PolicyId) throw CancellationOperationsAuthority.Invalid();
        await CommunicationScope.Audience(db, held.Subjects.Single(), source.Policy.RelationshipId, token);
        using var payload = JsonDocument.Parse(hint.PayloadJson);
        var recipients = payload.RootElement.GetProperty("recipients").EnumerateArray()
            .Select(x=>new DeliveryRecipient(x.GetProperty("id").GetGuid(),x.GetProperty("name").GetString()!,x.GetProperty("email").GetString()!)).ToArray();
        if (recipients.Length == 0 || recipients.Select(x => x.ContactId).Distinct().Count() != recipients.Length) throw new OperationalAccessException(409, "cancellation-no-recipient");
        foreach (var recipient in recipients.OrderBy(x => x.ContactId))
        {
            var contact = await db.Set<Contact>().FromSqlInterpolated($"SELECT * FROM Contact WITH(HOLDLOCK,ROWLOCK) WHERE Id={recipient.ContactId}").AsNoTracking().SingleOrDefaultAsync(token);
            if (contact is null || contact.RelationshipId != source.Policy.RelationshipId || contact.ClientId != source.Policy.ClientId || contact.EndedAt is not null ||
                contact.DeclaredFullName != recipient.Name || contact.Email != recipient.Email || !CommunicationRules.Email(contact.Email)) throw CancellationOperationsAuthority.Invalid();
        }
        await DocumentService.AttachmentVersion(db, actor, document.SubjectId, documentVersionId, true, token);
        var file = await (from version in db.Set<DocumentVersion>() join content in db.Set<DocumentVersionContent>() on version.Id equals content.VersionId
            join f in db.Set<FileObject>() on content.FileObjectId equals f.Id where version.Id == documentVersionId
            select new { version.OriginalName, f.Id, f.Sha256, f.MediaType, f.ByteLength }).SingleAsync(token);
        return new("operational-delivery-1", document.SubjectId, source.Policy.RelationshipId, "Cancellation notice · " + source.Policy.Reference,
            "Please find the cancellation notice attached. Cancellation takes effect at " + source.Decision.EffectiveAt.ToString("O") + ". Any posted credit is not a cash refund.",
            recipients.OrderBy(x => x.ContactId).ToArray(), [new(documentVersionId, file.Id, file.Sha256, file.OriginalName, file.MediaType, file.ByteLength)]);
    }
}
