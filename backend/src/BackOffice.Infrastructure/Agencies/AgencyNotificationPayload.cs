using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;

namespace BackOffice.Infrastructure.Agencies;

// Deliberately not a record: synthesized ToString must not disclose a delivery secret.
public sealed class AgencyDeliveryEnvelope
{
    public required string Recipient {get;init;}
    public required string Template {get;init;}
    public required string Content {get;init;}
    public override string ToString()=>"Protected agency demo delivery";
}

public sealed class AgencyNotificationPayload(IDataProtectionProvider protection)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    private IDataProtector Protector(Guid agencyId,Guid notificationId)=>protection.CreateProtector(
        "CoverMGA.AgencyNotification.v1",agencyId.ToString("N"),notificationId.ToString("N"));

    public string Protect(Guid agencyId,Guid notificationId,AgencyDeliveryEnvelope envelope)
    {
        Validate(envelope);
        if(agencyId==Guid.Empty||notificationId==Guid.Empty)throw new ArgumentException("Delivery identity is required.");
        return Protector(agencyId,notificationId).Protect(JsonSerializer.Serialize(envelope,Json));
    }

    public AgencyDeliveryEnvelope Unprotect(Guid agencyId,Guid notificationId,string ciphertext)
    {
        try
        {
            var envelope=JsonSerializer.Deserialize<AgencyDeliveryEnvelope>(Protector(agencyId,notificationId).Unprotect(ciphertext),Json)
                ??throw new JsonException();
            Validate(envelope);return envelope;
        }
        catch(Exception exception) when(exception is CryptographicException or JsonException or ArgumentException)
        {throw new CryptographicException("Agency delivery payload is unavailable.");}
    }

    public static byte[] Fingerprint(AgencyDeliveryEnvelope envelope)
    {Validate(envelope);return SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(envelope,Json));}

    private static void Validate(AgencyDeliveryEnvelope envelope)
    {
        if(string.IsNullOrWhiteSpace(envelope.Recipient)||envelope.Recipient.Length>254||envelope.Recipient.Any(char.IsControl)||
            !System.Net.Mail.MailAddress.TryCreate(envelope.Recipient,out var address)||address.Address!=envelope.Recipient||
            envelope.Template is not ("agency-activated" or "agency-invitation")||
            string.IsNullOrWhiteSpace(envelope.Content)||Encoding.UTF8.GetByteCount(envelope.Content)>8192)
            throw new ArgumentException("Invalid agency delivery envelope.");
    }
}
