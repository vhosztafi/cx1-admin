using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application;
using BackOffice.Application.Parties;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Parties;

public sealed class ContactOperationException(int status,string code):Exception("The contact change cannot be applied.")
{
    public int Status{get;}=status;
    public string Code{get;}=code;
}

// Call AuthorizeAsync before command replay. Mutations run only in the supplied
// audited command transaction, never in a second independently committed context.
public static class ContactService
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull};

    public static async Task<ClientAgencyRelationship> AuthorizeAsync(BackOfficeDbContext db,ActorContext actor,Guid relationshipId,
        Guid? contactId=null,Guid? reusablePersonId=null,CancellationToken token=default)
    {
        if(!actor.HasCapability("contact-write"))throw new ContactOperationException(403,"contact-write-forbidden");
        var scope=new PartyScope(actor);
        var relationship=await scope.Relationships(db).SingleOrDefaultAsync(x=>x.Id==relationshipId,token)
            ?? throw new ContactOperationException(404,"contact-record-not-found");
        if(contactId is Guid contact && !await scope.Contacts(db,includeEnded:true).AnyAsync(x=>x.Id==contact && x.RelationshipId==relationshipId,token))
            throw new ContactOperationException(404,"contact-record-not-found");
        if(reusablePersonId is Guid person && await scope.FindReusablePersonAsync(db,relationship.ClientId,person,token) is null)
            throw new ContactOperationException(404,"contact-record-not-found");
        return relationship;
    }

    public static async Task<Contact> CreateAsync(BackOfficeDbContext db,ActorContext actor,Guid relationshipId,byte[] expectedParent,
        ValidatedContact input,DateTimeOffset now,CancellationToken token=default)
    {
        var parent=await LockParent(db,actor,relationshipId,null,input.PersonId,token);CheckVersion(parent.RowVersion,expectedParent);
        Guid personId;
        if(input.PersonId is Guid existing)personId=existing;
        else
        {
            var person=new Person {FullName=input.FullName,FirstName=input.FirstName,Surname=input.Surname,CreatedAt=now,UpdatedAt=now,CreatedBy=actor.UserId};
            db.Add(person);personId=person.Id;
        }
        var count=await Active(db,relationshipId).CountAsync(token);
        var primary=ContactRules.PrimaryOnCreate(count,input.IsPrimary);
        if(primary)await DemoteOthers(db,relationshipId,null,token);
        var contact=new Contact {ClientId=parent.ClientId,RelationshipId=relationshipId,PersonId=personId,IsPrimary=primary,CreatedAt=now,UpdatedAt=now,CreatedBy=actor.UserId};
        ApplyIdentity(contact,input);db.Add(contact);
        await Finish(db,parent,contact,actor,"contact.created",now,token);return contact;
    }

    public static async Task<Contact> UpdateAsync(BackOfficeDbContext db,ActorContext actor,Guid relationshipId,Guid contactId,byte[] expected,
        ValidatedContact input,DateTimeOffset now,CancellationToken token=default)
    {
        var parent=await LockParent(db,actor,relationshipId,contactId,null,token);
        var contact=await CurrentContact(db,relationshipId,contactId,expected,token);
        if(input.PersonId is Guid personId && personId!=contact.PersonId)throw new ContactOperationException(422,"person-identity-immutable");
        if(!ContactRules.CanDemote(contact.IsPrimary,input.IsPrimary))throw new ContactOperationException(409,"choose-replacement-primary");
        if(input.IsPrimary)await DemoteOthers(db,relationshipId,contactId,token);
        ApplyIdentity(contact,input);contact.IsPrimary=input.IsPrimary;
        await Finish(db,parent,contact,actor,"contact.updated",now,token);return contact;
    }

    public static async Task<Contact> MakePrimaryAsync(BackOfficeDbContext db,ActorContext actor,Guid relationshipId,Guid contactId,byte[] expected,
        DateTimeOffset now,CancellationToken token=default)
    {
        var parent=await LockParent(db,actor,relationshipId,contactId,null,token);
        var contact=await CurrentContact(db,relationshipId,contactId,expected,token);
        await DemoteOthers(db,relationshipId,contactId,token);contact.IsPrimary=true;
        await Finish(db,parent,contact,actor,"contact.primary-changed",now,token);return contact;
    }

    public static async Task<Contact> EndAsync(BackOfficeDbContext db,ActorContext actor,Guid relationshipId,Guid contactId,byte[] expected,
        string reason,DateTimeOffset now,CancellationToken token=default)
    {
        if(string.IsNullOrWhiteSpace(reason) || reason.Length>1000 || reason.Any(char.IsControl))throw new ContactOperationException(422,"end-reason-required");
        var parent=await LockParent(db,actor,relationshipId,contactId,null,token);
        var contact=await CurrentContact(db,relationshipId,contactId,expected,token);
        if(!ContactRules.CanEnd(contact.IsPrimary,await Active(db,relationshipId).CountAsync(token)))throw new ContactOperationException(409,"choose-replacement-primary");
        contact.IsPrimary=false;contact.EndedAt=now;contact.EndedBy=actor.UserId;contact.EndReason=reason.Trim();
        await Finish(db,parent,contact,actor,"contact.ended",now,token);return contact;
    }

    private static async Task<ClientAgencyRelationship> LockParent(BackOfficeDbContext db,ActorContext actor,Guid relationshipId,Guid? contactId,Guid? reusablePersonId,CancellationToken token)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Contact mutations require the caller's audited transaction.");
        await AuthorizeAsync(db,actor,relationshipId,contactId,reusablePersonId,token);
        var parent=await db.Set<ClientAgencyRelationship>().FromSqlInterpolated($"SELECT * FROM [ClientAgencyRelationship] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={relationshipId}").SingleOrDefaultAsync(token)
            ?? throw new ContactOperationException(404,"contact-record-not-found");
        if(parent.State!="active")throw new ContactOperationException(409,"relationship-inactive");
        // Recheck reuse after acquiring the parent lock as well; selection never grants access.
        if(reusablePersonId is Guid person && await new PartyScope(actor).FindReusablePersonAsync(db,parent.ClientId,person,token) is null)
            throw new ContactOperationException(404,"contact-record-not-found");
        return parent;
    }
    private static async Task<Contact> CurrentContact(BackOfficeDbContext db,Guid relationshipId,Guid contactId,byte[] expected,CancellationToken token)
    {
        var contact=await db.Set<Contact>().SingleOrDefaultAsync(x=>x.Id==contactId && x.RelationshipId==relationshipId,token)
            ?? throw new ContactOperationException(404,"contact-record-not-found");
        CheckVersion(contact.RowVersion,expected);
        if(contact.EndedAt is not null)throw new ContactOperationException(409,"contact-ended");
        return contact;
    }
    private static void CheckVersion(byte[] current,byte[] expected)
    {
        if(expected.Length!=8 || !CryptographicOperations.FixedTimeEquals(current,expected))throw new ContactOperationException(412,"stale-contact-version");
    }
    private static IQueryable<Contact> Active(BackOfficeDbContext db,Guid relationshipId)=>db.Set<Contact>().Where(x=>x.RelationshipId==relationshipId && x.EndedAt==null);
    private static async Task DemoteOthers(BackOfficeDbContext db,Guid relationshipId,Guid? except,CancellationToken token)
    {
        var prior=await Active(db,relationshipId).Where(x=>x.IsPrimary && x.Id!=except).ToListAsync(token);
        foreach(var contact in prior)contact.IsPrimary=false;
        if(prior.Count>0)await db.SaveChangesAsync(token); // Avoid transient filtered-index conflict; caller rollback covers this save.
    }
    private static void ApplyIdentity(Contact contact,ValidatedContact input)
    {
        contact.DeclaredFullName=input.FullName;contact.NormalizedName=input.NormalizedName;contact.DeclaredFirstName=input.FirstName;contact.DeclaredSurname=input.Surname;
        contact.Role=input.Role;contact.Email=input.Email;contact.Telephone=input.Telephone;contact.MarketingConsent=JsonSerializer.Serialize(input.MarketingConsent,Json);
    }
    private static async Task Finish(BackOfficeDbContext db,ClientAgencyRelationship parent,Contact contact,ActorContext actor,string eventType,DateTimeOffset now,CancellationToken token)
    {
        parent.UpdatedAt=now;db.Entry(parent).Property(x=>x.UpdatedAt).IsModified=true;
        if(db.Entry(contact).State!=EntityState.Added){contact.UpdatedAt=now;db.Entry(contact).Property(x=>x.UpdatedAt).IsModified=true;}
        db.Add(new ClientActivity {ClientId=parent.ClientId,RelationshipId=parent.Id,ActorId=actor.UserId,CreatedBy=actor.UserId,CreatedAt=now,
            OccurredAt=now,EventType=eventType,RecordId=contact.Id,RecordKind="contact"});
        await db.SaveChangesAsync(token);
        var active=Active(db,parent.Id);
        var count=await active.CountAsync(token);var primary=await active.CountAsync(x=>x.IsPrimary,token);
        if(primary!=(count>0 ? 1 : 0))throw new ContactOperationException(409,"choose-replacement-primary");
    }
}
