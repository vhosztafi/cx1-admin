namespace BackOffice.Infrastructure.Persistence;

public sealed class ServicingCondition : MutableRecord
{
    public Guid DraftId {get;set;}
    public Guid CycleId {get;set;}
    public Guid RevisionId {get;set;}
    public Guid RatingId {get;set;}
    public Guid ReferralId {get;set;}
    public Guid DecisionId {get;set;}
    public int Sequence {get;set;}
    public string Code {get;set;}="";
    public string Kind {get;set;}="";
    public string DefinitionJson {get;set;}="{}";
    public string EffectiveDatesJson {get;set;}="[]";
}
