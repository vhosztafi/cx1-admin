using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class OperationalCommunicationTests
{
    [Fact]
    public async Task RealSqlOperationalCommunicationNotesAndDraftsRetainScopeAndReplay()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,s=>s.UseCompatibilityLevel(160)).Options;
        var password="Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1";
        try
        {
            await using var db=new BackOfficeDbContext(options);await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,password);
            var agency=new Agency{Reference="AG-COMMS",LegalName="Fictional communication agency",State="active"};
            var other=new Agency{Reference="AG-COMMS-OTHER",LegalName="Other fictional agency"};
            var clientRecord=new ClientAccount{Reference="CL-COMMS",LegalName="Fictional communication client",NormalizedName="FICTIONAL COMMUNICATION CLIENT"};
            var person=new Person{FullName="Fictional recipient"};db.AddRange(agency,other,clientRecord,person);await db.SaveChangesAsync();
            var relationship=new ClientAgencyRelationship{ClientId=clientRecord.Id,AgencyId=agency.Id};
            var foreignRelationship=new ClientAgencyRelationship{ClientId=clientRecord.Id,AgencyId=other.Id};db.AddRange(relationship,foreignRelationship);await db.SaveChangesAsync();
            var consent=JsonSerializer.Serialize(new{state="not-asked",email=false,telephone=false,source="Fictional fixture",recordedAt=DateTimeOffset.UtcNow});
            var contact=new Contact{ClientId=clientRecord.Id,RelationshipId=relationship.Id,PersonId=person.Id,DeclaredFullName="Fictional recipient",NormalizedName="FICTIONAL RECIPIENT",Role="Director",MarketingConsent=consent,Email="recipient@example.test"};
            var foreign=new Contact{ClientId=clientRecord.Id,RelationshipId=foreignRelationship.Id,PersonId=person.Id,DeclaredFullName="Other recipient",NormalizedName="OTHER RECIPIENT",Role="Director",MarketingConsent=consent,Email="other@example.test"};db.AddRange(contact,foreign);await db.SaveChangesAsync();
            using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Cover:SqlConnection",connection.ConnectionString)
                .UseSetting("Cover:DiagnosticWorkerEnabled","false").UseSetting("Cover:DataProtectionPath",Path.GetFullPath(Path.Combine(".local","communication-test-keys",owned))));
            using var client=host.CreateClient();
            var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            (await Send(client,csrf,HttpMethod.Post,"/api/v1/auth/login",new{email="agency-admin@cover.example",password})).EnsureSuccessStatusCode();
            csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            var registration=await Send(client,csrf,HttpMethod.Post,"/api/v1/operational-subjects",new{kind="agency",parentId=agency.Id});registration.EnsureSuccessStatusCode();
            var subject=(await registration.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var basePath=$"/api/v1/records/{subject}";
            var sharingPaths=new[]{ $"/api/v1/agencies/{agency.Id}/sharing/clients", $"/api/v1/agencies/{agency.Id}/sharing/relationships/{relationship.Id}/contacts", $"/api/v1/agencies/{agency.Id}/sharing/relationships/{relationship.Id}/instructions" };
            var sharingBefore=new List<string>();
            foreach(var sharingPath in sharingPaths){using var response=await client.GetAsync(sharingPath);response.EnsureSuccessStatusCode();sharingBefore.Add(await response.Content.ReadAsStringAsync());}
            var noteKey=Guid.NewGuid().ToString();var noteBody=new{body="Internal fictional review; never an agency message."};
            var note=await Send(client,csrf,HttpMethod.Post,basePath+"/notes",noteBody,noteKey);
            Assert.Equal(HttpStatusCode.Created,note.StatusCode);
            var savedNote=await note.Content.ReadAsStringAsync();
            Assert.Equal(savedNote,await(await Send(client,csrf,HttpMethod.Post,basePath+"/notes",noteBody,noteKey)).Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Conflict,(await Send(client,csrf,HttpMethod.Post,basePath+"/notes",new{body="Changed"},noteKey)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(client,null,HttpMethod.Post,basePath+"/notes",noteBody)).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(client,csrf,HttpMethod.Post,basePath+"/notes",new{body=(string?)null})).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await Send(client,csrf,HttpMethod.Post,basePath+"/notes",new{body="Note",visibility="agency"})).StatusCode);
            var notes=await client.GetFromJsonAsync<JsonElement>(basePath+"/notes");Assert.Single(notes.GetProperty("items").EnumerateArray());
            Assert.Equal(noteBody.body,notes.GetProperty("items")[0].GetProperty("body").GetString());
            Assert.False(string.IsNullOrWhiteSpace(notes.GetProperty("items")[0].GetProperty("authorLabel").GetString()));
            var emptyThreads=await client.GetFromJsonAsync<JsonElement>(basePath+"/threads");Assert.Equal(0,emptyThreads.GetProperty("totalCount").GetInt32());
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(client,csrf,HttpMethod.Post,basePath+"/threads",new{visibility="internal",subject="Invalid null relationship",relationshipId=(Guid?)null})).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await Send(client,csrf,HttpMethod.Post,basePath+"/threads",new{visibility="internal",subject="Invalid audience relationship",relationshipId=relationship.Id})).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await Send(client,csrf,HttpMethod.Post,basePath+"/threads",new{visibility="agency",subject="Foreign",relationshipId=foreignRelationship.Id})).StatusCode);
            var thread=await Send(client,csrf,HttpMethod.Post,basePath+"/threads",new{visibility="agency",subject="Fictional evidence follow-up",relationshipId=relationship.Id});
            Assert.Equal(HttpStatusCode.Created,thread.StatusCode);var threadId=(await thread.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.Created,(await Send(client,csrf,HttpMethod.Post,basePath+"/threads",new{visibility="internal",subject="Private underwriting discussion"})).StatusCode);
            var agencyThreads=await client.GetFromJsonAsync<JsonElement>(basePath+"/threads?visibility=agency");
            Assert.Equal(1,agencyThreads.GetProperty("totalCount").GetInt32());Assert.DoesNotContain("Private underwriting",agencyThreads.GetRawText());Assert.DoesNotContain(noteBody.body,agencyThreads.GetRawText());
            var relationships=await client.GetFromJsonAsync<JsonElement>(basePath+"/thread-relationships");
            Assert.Equal(relationship.Id,Assert.Single(relationships.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
            var recipients=await client.GetFromJsonAsync<JsonElement>($"/api/v1/threads/{threadId}/recipient-options");
            Assert.Equal(contact.Id,Assert.Single(recipients.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
            var attachmentOptions=await client.GetFromJsonAsync<JsonElement>($"/api/v1/threads/{threadId}/attachment-options");Assert.Empty(attachmentOptions.GetProperty("items").EnumerateArray());
            var path=$"/api/v1/threads/{threadId}/messages";
            object Body(string text,Guid recipient)=>new{body=text,recipientContactIds=new[]{recipient},attachmentVersionIds=Array.Empty<Guid>()};
            Assert.Equal(HttpStatusCode.NotFound,(await Send(client,csrf,HttpMethod.Post,path,Body("Foreign",foreign.Id))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await Send(client,csrf,HttpMethod.Post,path,new{body="Foreign attachment",recipientContactIds=new[]{contact.Id},attachmentVersionIds=new[]{Guid.NewGuid()}})).StatusCode);
            var created=await Send(client,csrf,HttpMethod.Post,path,Body("",contact.Id));Assert.Equal(HttpStatusCode.Created,created.StatusCode);
            var message=(await created.Content.ReadFromJsonAsync<JsonElement>());var id=message.GetProperty("id").GetGuid();var etag=created.Headers.ETag!.Tag;
            var key=Guid.NewGuid().ToString();var updated=await Send(client,csrf,HttpMethod.Put,$"/api/v1/messages/{id}",Body("Saved fictional draft",contact.Id),key,etag);
            Assert.Equal(HttpStatusCode.OK,updated.StatusCode);
            var exact=await updated.Content.ReadAsStringAsync();
            Assert.Equal(exact,await(await Send(client,csrf,HttpMethod.Put,$"/api/v1/messages/{id}",Body("Saved fictional draft",contact.Id),key,etag)).Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.PreconditionFailed,(await Send(client,csrf,HttpMethod.Put,$"/api/v1/messages/{id}",Body("Stale",contact.Id),etag:etag)).StatusCode);
            var read=await client.GetFromJsonAsync<JsonElement>(path);var row=Assert.Single(read.GetProperty("items").EnumerateArray());
            using var directRead=await client.GetAsync($"/api/v1/messages/{id}");Assert.Equal(HttpStatusCode.OK,directRead.StatusCode);Assert.Equal(updated.Headers.ETag,directRead.Headers.ETag);
            Assert.Equal("draft",row.GetProperty("state").GetString());Assert.Equal("Saved fictional draft",row.GetProperty("body").GetString());
            Assert.DoesNotContain(noteBody.body,read.GetRawText());
            for(var i=0;i<sharingPaths.Length;i++){using var response=await client.GetAsync(sharingPaths[i]);response.EnsureSuccessStatusCode();var projection=await response.Content.ReadAsStringAsync();Assert.Equal(sharingBefore[i],projection);Assert.DoesNotContain(noteBody.body,projection);Assert.DoesNotContain("Saved fictional draft",projection);}
            Assert.False(db.Database.HasPendingModelChanges());
            var storedNote=await db.Set<InternalNote>().SingleAsync();
            db.Add(new InternalNote{SubjectId=subject,Body=new string('x',8001),AuthorLabel=storedNote.AuthorLabel,CreatedBy=storedNote.CreatedBy});
            var oversized=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());Assert.Equal(52001,Assert.IsType<SqlException>(oversized.InnerException).Number);db.ChangeTracker.Clear();
            var historyError=await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE InternalNote SET Body=N'rewritten' WHERE Id={storedNote.Id}"));Assert.Equal(52000,historyError.Number);
            var deleteError=await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM InternalNote WHERE Id={storedNote.Id}"));Assert.Equal(52000,deleteError.Number);
            var retargetError=await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OperationalThread SET RelationshipId={foreignRelationship.Id} WHERE Id={threadId}"));Assert.Equal(52000,retargetError.Number);
            var ending=await db.Set<Contact>().SingleAsync(x=>x.Id==contact.Id);ending.EndedAt=DateTimeOffset.UtcNow;ending.EndedBy=await db.Set<StaffUser>().Where(x=>x.Email=="agency-admin@cover.example").Select(x=>x.Id).SingleAsync();ending.EndReason="Fictional contact ended";await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.NotFound,(await Send(client,csrf,HttpMethod.Put,$"/api/v1/messages/{id}",Body("Saved fictional draft",contact.Id),key,etag)).StatusCode);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Email=N'agency-admin@cover.example'");
            Assert.Equal(HttpStatusCode.Unauthorized,(await Send(client,csrf,HttpMethod.Post,basePath+"/notes",noteBody,noteKey)).StatusCode);
        }
        finally
        {
            if(connection.InitialCatalog!=owned||!owned.StartsWith("CoverMGA_Test_",StringComparison.Ordinal))throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();
        }
    }
    private static Task<HttpResponseMessage> Send(HttpClient client,string? csrf,HttpMethod method,string path,object body,string? key=null,string? etag=null)
    {
        var request=new HttpRequestMessage(method,path){Content=JsonContent.Create(body)};
        if(csrf is not null)request.Headers.Add("X-CSRF-Token",csrf);
        request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString("N"));if(etag is not null)request.Headers.Add("If-Match",etag);
        return client.SendAsync(request);
    }
}
