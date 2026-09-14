using System.Globalization;
using System.Text.Json;

namespace BackOffice.Application.Agencies;

// This is a readiness assessment, never permission to activate or skip approval.
public sealed record AgencyActivationDependencies(bool? EligibleEffectiveProducts,bool? ViableBrokerAdministrator);
public static class AgencyActivationRules
{
    public static IReadOnlyList<AgencyChecklistItem> Evaluate(JsonElement details,IReadOnlyDictionary<string,AgencyEvidenceFact> evidence,Guid rule,DateOnly today,decimal minimumPi,string tobaVersion,AgencyActivationDependencies dependencies)
    {
        var items=new List<AgencyChecklistItem>();
        void Require(string path,int stage,string label)
        {
            var present=AgencyEvidenceRules.Present(details,path);
            items.Add(new("field-"+path,"/details/"+path.Replace('.','/'),stage,present?"satisfied":"missing",present?label+" saved.":"Enter "+label+"."));
        }
        void Condition(string code,string path,int stage,bool valid,string message)=>items.Add(new(code,path,stage,valid?"satisfied":"failed",valid?"Requirement satisfied.":message));
        string? Text(string path)=>AgencyEvidenceRules.Text(details,path);
        foreach(var (path,label) in new[]{("legalName","legal name"),("entityType","entity type"),("address.line1","registered address"),("address.town","registered town"),("address.postcode","registered postcode"),("address.country","registered country"),("tradingAddressMode","trading address choice"),("regulatoryReference","FCA reference"),("regulatoryStatus","regulatory status"),("clientMoneyBasis","client money basis"),("territory","territory")})Require(path,1,label);
        if(Text("entityType") is "limited-company" or "llp")Require("companyNumber",1,"company number");
        if(Text("tradingAddressMode")=="different")foreach(var part in new[]{"line1","town","postcode","country"})Require("tradingAddress."+part,1,"trading address "+part);
        if(Text("regulatoryStatus") is "appointed-representative" or "introducer-appointed-representative")Require("principalFirm",1,"principal firm");
        Condition("arranging-permission","/details/arrangesGeneralInsurance",1,Text("arrangesGeneralInsurance")=="confirmed"&&Text("regulatoryStatus") is "directly-authorised" or "appointed-representative","Confirm arranging permission; an introducer-only or restricted agency cannot be activated for arranging.");
        foreach(var contact in new[]{"mainContact","complianceContact"})foreach(var field in new[]{"name","email","telephone"})Require(contact+"."+field,2,contact=="mainContact"?"main contact "+field:"compliance contact "+field);
        Require("relationshipManagerId",2,"relationship manager");Require("correspondencePreference",2,"correspondence preference");
        foreach(var field in new[]{"effectiveFrom","commissionBasis","feeSharing","volumeCommitmentMode","minimumPremiumOverrideMode","referralRouting"})Require("commercialTerms."+field,3,"commercial "+field);
        if(Text("commercialTerms.commissionBasis")=="flat-rate")Require("commercialTerms.flatCommissionBasisPoints",3,"flat commission percentage");
        if(Text("commercialTerms.feeSharing")=="agreed-split")Require("commercialTerms.feeShareBasisPoints",3,"fee share percentage");
        if(Text("commercialTerms.volumeCommitmentMode") is "target-no-penalty" or "target-tiered")Require("commercialTerms.volumeCommitment",3,"volume commitment");
        if(Text("commercialTerms.minimumPremiumOverrideMode")=="capacity-provider-agreed")Require("commercialTerms.minimumPremiumOverride",3,"minimum premium override");
        Condition("terms-effective","/details/commercialTerms/effectiveFrom",3,DateOnly.TryParseExact(Text("commercialTerms.effectiveFrom"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var effective)&&effective<=today,"Initial agency terms must be effective on or before activation day.");
        foreach(var kind in AgencyEvidenceRules.CheckKinds.Concat(AgencyEvidenceRules.AttestationKinds))
        {
            if(kind=="client-money"&&Text("clientMoneyBasis")!="cass5-client-money")continue;
            var latest=evidence.GetValueOrDefault(kind);var item=AgencyEvidenceRules.EvidenceItem(details,kind,latest,rule,today);
            // Re-evaluate date/limit/version declarations at readiness time, too.
            if(item.State=="satisfied"&&AgencyEvidenceRules.AttestationKinds.Contains(kind)&&!AgencyEvidenceRules.AttestationValid(details,kind,today,minimumPi,tobaVersion,latest?.ExpiresOn))item=item with{State="failed",Message="The current "+kind.Replace('-',' ')+" declarations do not meet the demo rule. Update the details and record new evidence."};
            items.Add(item);
        }
        foreach(var field in new[]{"creditLimit","paymentTermsDays","settlement.statementCycle","settlement.method","settlement.premiumCollection","settlement.commissionSettlement"})Require(field,5,field.Replace("settlement.","settlement "));
        items.Add(Dependency("eligible-products","/products",3,dependencies.EligibleEffectiveProducts,"At least one selected product must be distribution-eligible and effective on activation day.","Product distribution approval is not available yet."));
        items.Add(Dependency("broker-administrator","/users",2,dependencies.ViableBrokerAdministrator,"Stage or retain a viable broker administrator.","Broker administrator staging is not available yet."));
        return items;
    }
    private static AgencyChecklistItem Dependency(string code,string path,int stage,bool? state,string missing,string unavailable)=>new(code,path,stage,state is null?"unavailable":state.Value?"satisfied":"missing",state is null?unavailable:state.Value?"Requirement satisfied.":missing);
}
