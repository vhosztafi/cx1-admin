using System.Text.Json;
using BackOffice.Application.Operations;
using Xunit;
namespace BackOffice.UnitTests;
public sealed class OperationalMidTests
{
    private static readonly Guid Vehicle=Guid.Parse("67b5dbb4-eccd-450c-bf77-57bf8c44720d"),Plate=Guid.Parse("58232944-f9dc-4a77-aeaf-07377b0c902a");
    private static readonly DateTimeOffset At=new(2026,10,1,11,0,0,TimeSpan.FromHours(1));
    private static JsonElement Source(string registration="AB12CDE",bool report=true,bool plate=true,string product="motor-trade-road-risks",string model="Demo")=>JsonSerializer.SerializeToElement(new{productCode=product,risk=new{vehicles=new[]{new{id=Vehicle,registration,model,responses=new{answers=new[]{new{questionId="prototype.addveh.report-mid",value=report}}}}},tradePlates=plate?new[]{new{id=Plate,number="123AB"}}:[],responses=new{answers=new[]{new{questionId="MTS-07-Q01",value=plate}}}},cover=new{sections=new[]{new{code="road-risks",coverLevel="comprehensive"}}}});
    [Theory][InlineData("motor-trade-road-risks")][InlineData("motor-trade-combined")]
    public void BothIssuedMotorProductsAreSupported(string product)
    {Assert.Equal(2,MidRules.Items(Source(product:product),null,"new-business",At,At.AddYears(1)).Count);}
    [Fact] public void InitialIssueIncludesOnlyExplicitlyReportableVehiclesAndCoveredPlates()
    {var items=MidRules.Items(Source(report:false),null,"new-business",At,At.AddYears(1));Assert.Single(items);Assert.Equal("trade-plate",items[0].Kind);Assert.Equal("add",items[0].Action);Assert.Equal(At,items[0].EffectiveAt);}
    [Fact] public void NewBusinessIncludesBothKindsWithStableSourceIds()
    {var items=MidRules.Items(Source(),null,"new-business",At,At.AddYears(1));Assert.Equal(2,items.Count);Assert.Contains(items,x=>x.RiskItemId==Vehicle&&x.Registration=="AB12CDE");Assert.Contains(items,x=>x.RiskItemId==Plate&&x.Registration=="123AB");}
    [Fact] public void RegistrationReplacementRemovesOldAndAddsNewWithoutLosingEither()
    {var items=MidRules.Items(Source("XY26ABC",plate:false),Source(plate:false),"adjustment",At,At.AddMonths(6));Assert.Equal(2,items.Count);Assert.Contains(items,x=>x.Action=="remove"&&x.Registration=="AB12CDE");Assert.Contains(items,x=>x.Action=="add"&&x.Registration=="XY26ABC");}
    [Fact] public void ChangedRetainedVehicleIsAChangeAndUnchangedPlateIsOmitted()
    {var items=MidRules.Items(Source(model:"Changed"),Source(),"adjustment",At,At.AddMonths(6));Assert.Single(items);Assert.Equal("change",items[0].Action);Assert.Equal(Vehicle,items[0].RiskItemId);}
    [Fact] public void TurningOffReportingRemovesPreviouslySubmittedVehicle()
    {var items=MidRules.Items(Source(report:false,plate:false),Source(plate:false),"adjustment",At,At.AddMonths(6));Assert.Single(items);Assert.Equal("remove",items[0].Action);}
    [Fact] public void CancellationRemovesExactPriorItemsAtCancellationInstant()
    {var items=MidRules.Items(Source(),Source(),"cancellation",At,At.AddMonths(6));Assert.Equal(2,items.Count);Assert.All(items,x=>{Assert.Equal("remove",x.Action);Assert.Equal(At,x.EffectiveAt);});}
    [Fact] public void RenewalChangesExistingRegistrationsForTheNewTerm()
    {var items=MidRules.Items(Source(),Source(),"renewal",At,At.AddYears(1));Assert.Equal(2,items.Count);Assert.All(items,x=>Assert.Equal("change",x.Action));}
    [Fact] public void NoChangeDoesNotInventAProviderAction()
    {Assert.Empty(MidRules.Items(Source(),Source(),"adjustment",At,At.AddMonths(6)));}
    [Theory][InlineData("commercial-combined")][InlineData("unknown")]
    public void NonMotorSourceIsRejected(string product)
    {Assert.Throws<MidRuleException>(()=>MidRules.Items(Source(product:product),null,"new-business",At,At.AddYears(1)));}
    [Fact] public void CommercialBasisCannotBeSmuggledIntoMotorOperation()
    {Assert.Throws<MidRuleException>(()=>MidRules.Items(Source(),Source(product:"commercial-combined"),"adjustment",At,At.AddYears(1)));}
    [Theory][InlineData("adjustment")][InlineData("renewal")][InlineData("cancellation")]
    public void ServicingRequiresExactRetainedBasis(string purpose)
    {Assert.Throws<MidRuleException>(()=>MidRules.Items(Source(),null,purpose,At,At.AddYears(1)));}
    [Fact] public void NewBusinessCannotAcceptAnUnrelatedBasis()
    {Assert.Throws<MidRuleException>(()=>MidRules.Items(Source(),Source(),"new-business",At,At.AddYears(1)));}
    [Fact] public void NormalizedRegistrationDoesNotCreateFalseReplacement()
    {Assert.Empty(MidRules.Items(Source("ab12 cde"),Source(),"adjustment",At,At.AddYears(1)));}
    [Fact] public void MissingReportDeclarationFailsClosed()
    {var source=JsonSerializer.SerializeToElement(new{productCode="motor-trade-road-risks",risk=new{vehicles=new[]{new{id=Vehicle,registration="AB12CDE"}}}});Assert.Throws<MidRuleException>(()=>MidRules.Items(source,null,"new-business",At,At.AddYears(1)));}
    [Fact] public void InvalidEffectiveIntervalIsRejected()
    {Assert.Throws<MidRuleException>(()=>MidRules.Items(Source(),null,"new-business",At,At));}
}
