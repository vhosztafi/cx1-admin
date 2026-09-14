namespace BackOffice.Infrastructure.Persistence;

public sealed class AgencyOnboarding:MutableRecord
{
    public Guid AgencyId {get;set;}
    public string SchemaVersion {get;set;}="1.0";
    public string Details {get;set;}="{}";
}
public sealed class AgencyDraftProduct:StoredRecord
{
    public Guid AgencyId {get;set;}
    public Guid ProductVersionId {get;set;}
    public DateOnly EffectiveFrom {get;set;}
    public int BrokerCommissionBasisPoints {get;set;}
}
public sealed class AgencyActivity:StoredRecord
{
    public Guid AgencyId {get;set;}
    public Guid? ActorId {get;set;}
    public string Action {get;set;}="";
    public DateTimeOffset OccurredAt {get;set;}=DateTimeOffset.UtcNow;
}
