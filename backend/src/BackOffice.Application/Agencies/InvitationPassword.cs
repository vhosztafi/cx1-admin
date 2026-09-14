namespace BackOffice.Application.Agencies;

public static class InvitationPassword
{
    public static void Validate(string? password)
    {
        if(password is null||password.Length is <12 or >128)throw new AgencyCommandException(422,"invalid-invitation-password");
    }
}
