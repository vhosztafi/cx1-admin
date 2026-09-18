namespace BackOffice.Application.Policies;

public static class ServicingSubmissionRules
{
    public static string Validate(Guid draft,Guid cycle,Guid revision,byte[]? version,Guid lease,string? reason)
    {
        if(draft==Guid.Empty || cycle==Guid.Empty || revision==Guid.Empty || lease==Guid.Empty || version?.Length!=8)
            throw new ArgumentException("Current draft, cycle, revision, version and editing fence are required.");
        if(string.IsNullOrWhiteSpace(reason) || reason.Trim().Length<10 || reason.Length>2000 || reason.Any(char.IsControl))
            throw new ArgumentException("A submission requires a bounded audit reason without control characters.");
        return reason.Trim();
    }
}
