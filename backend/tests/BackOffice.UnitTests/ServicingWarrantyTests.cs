using System.Text.Json;
using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ServicingWarrantyTests
{
    [Fact]
    public void AcknowledgementBindsEveryCurrentWarrantyAndItsDatedWording()
    {
        var context=new ServicingProofContext(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new string('a',64),
            new(Guid.NewGuid(),Guid.NewGuid(),"1","1","1"));
        var driver=Guid.NewGuid();var other=Guid.NewGuid();var start=new DateTimeOffset(2026,10,1,0,0,0,TimeSpan.Zero);
        ServicingEvidenceSlice Slice(DateTimeOffset date,string name)=>new(date,JsonSerializer.SerializeToElement(new {
            productCode="motor-trade-road-risks",risk=new {drivers=new[]{new {id=driver,firstName=name,surname="Demo"},new {id=other,firstName="Other",surname="Demo"}},premises=Array.Empty<object>()}
        }),6);
        var slices=new[]{Slice(start,"First"),Slice(start.AddDays(14),"Second")};
        var named=new ServicingWarrantyInput(Guid.NewGuid(),JsonSerializer.SerializeToElement(new {code="named-drivers-only",driverIds=new[]{driver,other},wordingVersion="1"}),[start,start.AddDays(14)]);
        var licence=new ServicingWarrantyInput(Guid.NewGuid(),JsonSerializer.SerializeToElement(new {code="any-driver-minimum-licence",minimumYears=3,wordingVersion="1"}),[start]);
        Assert.Null(ServicingWarrantyRules.Requirement(context,slices,[]));
        var first=Assert.IsType<ServicingProofRequirement>(ServicingWarrantyRules.Requirement(context,slices,[named,licence]));
        Assert.Equal("warranty-acknowledgement",first.Code);Assert.Null(first.RiskItemId);Assert.Equal(2,first.EffectiveDates.Count);
        Assert.Equal(first.InputFingerprint,ServicingWarrantyRules.Requirement(context,slices,[licence,named])!.InputFingerprint);
        Assert.NotEqual(first.InputFingerprint,ServicingWarrantyRules.Requirement(context,slices,[named])!.InputFingerprint);
        Assert.NotEqual(first.InputFingerprint,ServicingWarrantyRules.Requirement(context,slices,[named with {Id=Guid.NewGuid()},licence])!.InputFingerprint);
        Assert.NotEqual(first.InputFingerprint,ServicingWarrantyRules.Requirement(context,slices,[named with {EffectiveDates=[start]},licence])!.InputFingerprint);
        Assert.NotEqual(first.InputFingerprint,ServicingWarrantyRules.Requirement(context,[Slice(start,"Changed"),slices[1]],[named,licence])!.InputFingerprint);
        Assert.NotEqual(first.InputFingerprint,ServicingWarrantyRules.Requirement(context with {CycleId=Guid.NewGuid()},slices,[named,licence])!.InputFingerprint);
        Assert.Throws<ArgumentException>(()=>ServicingWarrantyRules.Requirement(context,slices,Enumerable.Range(0,101).Select(_=>named with {Id=Guid.NewGuid()}).ToArray()));
        Assert.Throws<ArgumentException>(()=>ServicingWarrantyRules.Requirement(context,slices,[named with {Definition=JsonSerializer.SerializeToElement(new {code="named-drivers-only",driverIds=new[]{Guid.NewGuid()},wordingVersion="1"})}]));
        Assert.Throws<ArgumentException>(()=>ServicingWarrantyRules.Requirement(context,slices,[named,named]));
        Assert.Throws<ArgumentException>(()=>ServicingWarrantyRules.Requirement(context,slices,[named with {Id=Guid.Empty}]));
        Assert.Throws<ArgumentException>(()=>ServicingWarrantyRules.Requirement(context,slices,[licence with {Definition=JsonSerializer.SerializeToElement(new {code="provide-trading-history"})}]));
        Assert.Throws<ArgumentException>(()=>ServicingWarrantyRules.Requirement(context,slices,[named with {EffectiveDates=[start.AddDays(1)]}]));
    }
}
