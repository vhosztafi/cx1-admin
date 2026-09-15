import { mkdir, writeFile } from 'node:fs/promises';
import {writeQuoteSchemas} from './generate-quote-contracts.mjs';
import {writeQuoteConfigurationSchemas} from './quote-configuration-contracts.mjs';

const object=(properties,required=Object.keys(properties))=>({type:'object',additionalProperties:false,properties,required});
const array=(items,minItems=0)=>({type:'array',items,minItems,maxItems:1000});
const str=(maxLength=500)=>({type:'string',minLength:1,maxLength});
const ref=name=>({$ref:`#/$defs/${name}`});
const enumeration=(...values)=>({type:'string',enum:values});
const bool={type:'boolean'}, count={type:'integer',minimum:0,maximum:1000000};
const defs={
  Id:{type:'string',format:'uuid'}, Date:{type:'string',format:'date'}, Instant:{type:'string',format:'date-time'},
  Amount:{type:'string',pattern:'^(0|[1-9][0-9]{0,12})\\.[0-9]{2}$'},
  Reference:object({collection:str(100),value:{anyOf:[{type:'integer'},str(100)]},label:str(250),version:str(100)}),
  Address:object({line1:str(200),line2:{type:'string',maxLength:200},town:str(100),county:{type:'string',maxLength:100},postcode:{type:'string',pattern:'^[A-Z0-9 ]{5,10}$'},country:{const:'GB'}},['line1','town','postcode','country']),
  Answer:{oneOf:[
    object({questionId:str(100),kind:{const:'boolean'},value:bool}),
    object({questionId:str(100),kind:{const:'text'},value:{type:'string',maxLength:4000}}),
    object({questionId:str(100),kind:{const:'date'},value:ref('Date')}),
    object({questionId:str(100),kind:{const:'count'},value:count}),
    object({questionId:str(100),kind:{const:'money'},value:ref('Amount'),currency:{const:'GBP'}}),
    object({questionId:str(100),kind:{const:'reference'},value:ref('Reference')}),
    object({questionId:str(100),kind:{const:'references'},value:array(ref('Reference'))}),
    object({questionId:str(100),kind:{const:'percentage'},value:{type:'integer',minimum:0,maximum:10000},unit:{const:'basis-points'}})
  ]},
  Responses:object({questionSetVersion:str(100),answers:array(ref('Answer'))}),
  Activity:object({id:ref('Id'),code:ref('Reference'),turnoverBasisPoints:{type:'integer',minimum:0,maximum:10000}}),
  Loss:object({id:ref('Id'),occurredOn:ref('Date'),type:ref('Reference'),description:str(4000),amount:ref('Amount'),status:enumeration('open','closed','notification-only'),fault:enumeration('fault','non-fault','unknown'),riskItemId:ref('Id')},['id','occurredOn','type','description','amount','status','fault']),
  Conviction:object({id:ref('Id'),occurredOn:ref('Date'),code:ref('Reference'),fine:ref('Amount'),points:{type:'integer',minimum:0,maximum:100},banMonths:{type:'integer',minimum:0,maximum:1200}}),
  Driver:object({id:ref('Id'),firstName:str(100),surname:str(100),dateOfBirth:ref('Date'),address:ref('Address'),relationship:ref('Reference'),usage:ref('Reference'),licence:object({type:ref('Reference'),number:str(40),testDate:ref('Date')}),convictions:array(ref('Conviction')),losses:array(ref('Loss')),responses:ref('Responses')}),
  Vehicle:object({id:ref('Id'),registration:{type:'string',pattern:'^[A-Z0-9 ]{2,12}$'},make:str(100),model:str(100),manufactureYear:{type:'integer',minimum:1900,maximum:2200},body:ref('Reference'),ownership:enumeration('business-owned','stock-for-sale','driver-personal','customer'),ownerDriverId:ref('Id'),value:ref('Amount'),purchasePrice:ref('Amount'),purchasedOn:ref('Date'),grossWeightKg:count,engineCc:count,responses:ref('Responses')},['id','registration','make','model','manufactureYear','body','ownership','value','responses']),
  Premises:object({id:ref('Id'),address:ref('Address'),use:ref('Reference'),security:ref('Reference'),buildings:ref('Amount'),contents:ref('Amount'),responses:ref('Responses')}),
  Business:object({description:str(4000),startedOn:ref('Date'),turnover:ref('Amount'),wageRoll:ref('Amount'),activities:array(ref('Activity'),1),responses:ref('Responses')}),
  PreviousInsurance:object({insurer:str(200),expiresOn:ref('Date'),noClaimsYears:{type:'integer',minimum:0,maximum:100},evidenceDocumentIds:array(ref('Id')),responses:ref('Responses')}),
  DriverBasis:{oneOf:[object({kind:{const:'named'},responses:ref('Responses')}),object({kind:{const:'any-driver'},minimumAge:{type:'integer',minimum:16,maximum:100},maximumAge:{type:'integer',minimum:16,maximum:100},maximumWeightKg:count,maximumEngineCc:count,driverCount:count,responses:ref('Responses')})]},
  RoadRisk:object({business:ref('Business'),driverBasis:ref('DriverBasis'),drivers:array(ref('Driver')),vehicles:array(ref('Vehicle')),specifiedVehicleIds:array(ref('Id')),tradePlates:array(object({id:ref('Id'),number:{type:'string',pattern:'^[A-Z0-9 ]{2,12}$'}})),premises:array(ref('Premises')),previousInsurance:ref('PreviousInsurance'),declarations:ref('Responses'),materialFacts:{type:'string',maxLength:8000}}),
  Location:object({id:ref('Id'),address:ref('Address'),occupancy:str(200),construction:str(500),security:ref('Reference'),floodZone:enumeration('1','2','3'),buildings:ref('Amount'),contents:ref('Amount'),stock:ref('Amount'),maximumEstimatedLoss:ref('Amount'),responses:ref('Responses')}),
  Wage:object({id:ref('Id'),category:ref('Reference'),employees:ref('Amount'),labourOnlySubcontractors:ref('Amount'),bonaFideSubcontractors:ref('Amount')}),
  CommercialRisk:object({business:ref('Business'),locations:array(ref('Location'),1),wages:array(ref('Wage')),liability:object({employersLimit:ref('Amount'),publicLimit:ref('Amount'),productsLimit:ref('Amount'),maximumHeightMetres:{type:'number',minimum:0,maximum:1000},employersReferenceNumber:str(50)},['employersLimit','publicLimit','productsLimit','maximumHeightMetres']),businessInterruption:object({basis:enumeration('gross-profit','gross-revenue','increased-cost'),sumInsured:ref('Amount'),indemnityMonths:{type:'integer',minimum:1,maximum:60}}),losses:array(ref('Loss')),declarations:ref('Responses'),materialFacts:{type:'string',maxLength:8000}}),
  Cover:object({sections:array(object({id:ref('Id'),code:str(100),limit:ref('Amount'),excess:ref('Amount')}),1),endorsements:array(object({code:str(100),version:str(50),text:str(8000)})),warranties:array(object({code:str(100),version:str(50),text:str(8000)})),responses:ref('Responses')}),
  Premium:object({currency:{const:'GBP'},annualPremium:ref('Amount'),termPremium:ref('Amount'),tax:ref('Amount'),fee:ref('Amount'),brokerCommission:ref('Amount'),grossPayable:ref('Amount'),netBrokerDue:ref('Amount'),insurerDue:ref('Amount'),taxBasisPoints:{type:'integer',minimum:0,maximum:10000},commissionBasisPoints:{type:'integer',minimum:0,maximum:10000},ratingResultId:ref('Id'),ruleVersion:str(100)}),
};
// Detail forms contain product questions beyond common loss/conviction columns.
for(const name of ['Loss','Conviction','Wage'])defs[name].properties.responses=ref('Responses');
defs.Driver.properties.fullName=str(200);
defs.Driver.required=defs.Driver.required.filter(key=>!['firstName','surname'].includes(key));
defs.Driver.anyOf=[{properties:{fullName:str(200)},required:['fullName']},{properties:{firstName:str(100),surname:str(100)},required:['firstName','surname']}];
defs.Settlement=object({termsVersionId:ref('Id'),collector:enumeration('agency','mga'),mode:enumeration('net-remittance','separate-payment'),feeShareBasisPoints:{type:'integer',minimum:0,maximum:10000},brokerFeeShare:ref('Amount'),mgaFeeIncome:ref('Amount'),brokerRemuneration:ref('Amount'),invoiceDue:ref('Amount'),remunerationPayable:ref('Amount'),netEconomicDue:ref('Amount')});
defs.Premium.properties.settlement=ref('Settlement');defs.Premium.required.push('settlement');
defs.CommercialRisk.properties.businessInterruption.properties.basis.enum.push('estimated-gross-profit');
defs.PreviousInsurance.properties.noClaimsYearsBasis=enumeration('exact','at-least');
defs.PreviousInsurance.required.push('noClaimsYearsBasis');
defs.PreviousInsurance.properties.policyNumber=str(100);
defs.PreviousInsurance.properties.policyholderName=str(200);
// Repeated source histories retain their own identity; optional additions keep
// existing policy snapshots valid. Quote readiness adds applicability rules.
defs.DriverOccupation=object({id:ref('Id'),occupation:ref('Reference'),businessUseRequired:bool});
defs.CriminalConviction=object({id:ref('Id'),occurredOn:ref('Date'),code:ref('Reference'),sentenceYears:{type:'integer',minimum:0,maximum:100},sentenceMonths:{type:'integer',minimum:0,maximum:11}});
defs.CountyCourtJudgment=object({id:ref('Id'),occurredOn:ref('Date'),status:ref('Reference'),amount:ref('Amount'),circumstances:str(4000)});
defs.VehicleModification=object({id:ref('Id'),code:ref('Reference')});
defs.Driver.properties.occupations={...array(ref('DriverOccupation')),maxItems:5};
defs.Driver.properties.criminalConvictions={...array(ref('CriminalConviction')),maxItems:100};
defs.Driver.properties.countyCourtJudgments={...array(ref('CountyCourtJudgment')),maxItems:100};
defs.Vehicle.properties.modifications=array(ref('VehicleModification'));
defs.Vehicle.properties.register=enumeration('owned-not-for-sale','held-for-sale');
// Vehicle lookup/manual declarations are distinct from normalized policy fields.
// In particular registration year is not manufacture year, and a declared owner
// reference cannot be converted into an ownership enum without pinned metadata.
Object.assign(defs.Vehicle.properties,{
  abiCode:str(100),abiGroup:count,vehicleType:ref('Reference'),bodyDescription:str(200),
  seats:count,doors:count,fuelType:str(100),transmission:str(100),declaredEngineSize:str(100),
  registrationYear:{type:'integer',minimum:1900,maximum:2200},registeredOn:ref('Date'),
  imported:bool,declaredOwnerType:ref('Reference'),keptOvernightType:ref('Reference'),
  keptOvernightAddress:str(1000),partOfLeaseAgreement:bool,
  leaseLengthYears:{type:'number',minimum:0,maximum:100},customerLoan:bool,modified:bool
});
defs.RoadRisk.properties.specifiedVehiclesRequested=bool;
defs.RoadRisk.properties.responses=ref('Responses');
// Held inventory is distinct from the source's requested covered plate list.
defs.RoadRisk.properties.heldTradePlates=structuredClone(defs.RoadRisk.properties.tradePlates);
defs.Business.properties.declaredActivitySplit=object(Object.fromEntries(['sales','servicing','mechanicalRepair','breakdownRecovery','bodyRepairs','valeting','other'].map(key=>[key,{type:'integer',minimum:0,maximum:10000}])));
defs.Premises.properties.yearsTrading={type:'number',minimum:0,maximum:1000};
defs.Premises.properties.sharedWorksite=bool;
// Trading activity at the premises is not the source physical premise type.
defs.Premises.properties.declaredUse=ref('Reference');
defs.PreviousInsurance.properties.noClaimsBonusExpiresOn=ref('Date');
// Preserve original declarations rather than conflating issue/test dates or
// losing address components when a formatted address is assembled.
defs.Address.properties.houseNumber=str(50);
defs.Address.properties.street=str(100);
defs.Address.properties.city=str(100);
defs.Driver.properties.licence.properties.issuedOn=ref('Date');
defs.Conviction.properties.disqualified=bool;
// Prototype ranges are declarations, not exact durations. Never replace a
// range with a guessed month count or collapse split liability into fault.
defs.Conviction.properties.declaredBanPeriod=enumeration('none','under-3-months','3-to-6-months','6-to-12-months','over-12-months');
defs.Loss.properties.fault.enum.push('split');
defs.Loss.properties.declaredStatus=ref('Reference');
defs.Loss.properties.declaredType=ref('Reference');
defs.AnnualEuropeanCover=object({id:ref('Id'),registration:{type:'string',pattern:'^[A-Z0-9 ]{2,12}$'},usage:ref('Reference')});
defs.TemporaryEuropeanCover=object({id:ref('Id'),registration:{type:'string',pattern:'^[A-Z0-9 ]{2,12}$'},startsOn:ref('Date'),endsOn:ref('Date'),area:ref('Reference'),driverIds:{...array(ref('Id')),uniqueItems:true},cover:ref('Reference'),usage:ref('Reference')});
defs.Cover.properties.annualEuropeanCover=array(ref('AnnualEuropeanCover'));
defs.Cover.properties.temporaryEuropeanCover=array(ref('TemporaryEuropeanCover'));
const properties={
 schemaVersion:{const:'1.0'},productCode:enumeration('motor-trade-road-risks','motor-trade-combined','commercial-combined'),productVersionId:ref('Id'),
 insured:object({clientId:ref('Id'),clientAgencyRelationshipId:ref('Id'),entityType:enumeration('sole-trader','partnership','limited-company','llp'),legalName:str(200),tradingName:str(200),companyNumber:str(30),address:ref('Address')},['clientId','clientAgencyRelationshipId','entityType','legalName','address']),
 term:object({kind:enumeration('annual','short-period'),startsAt:ref('Instant'),endsAt:ref('Instant'),timeZone:{const:'Europe/London'}}),
 risk:{oneOf:[ref('RoadRisk'),ref('CommercialRisk')]},cover:ref('Cover'),premium:ref('Premium'),
 provenance:object({source:enumeration('backoffice','demo-seed'),quoteRevisionId:ref('Id'),authorityVersionId:ref('Id')})
};
properties.insured.properties.proposerNames=array(str(200),1);
properties.insured.properties.contact=object({telephone:str(50),email:{type:'string',format:'email'}},[]);
properties.insured.properties.contact.properties.mobile=str(50);
Object.assign(properties.insured.properties,{
  firstName:str(100),surname:str(100),title:ref('Reference'),declaredCompanyType:ref('Reference'),responses:ref('Responses')
});
const schema={
 $schema:'https://json-schema.org/draft/2020-12/schema',$id:'https://schemas.cover-mga.example/policy/1.0',title:'Issued policy risk snapshot v1.0',
 ...object(properties),$defs:defs,
 allOf:[{if:{properties:{productCode:{const:'commercial-combined'}},required:['productCode']},then:{properties:{risk:ref('CommercialRisk')}},else:{properties:{risk:ref('RoadRisk')}}}]
};
const out=new URL('../contracts/schemas/',import.meta.url);await mkdir(out,{recursive:true});
await writeFile(new URL('policy.schema.json',out),JSON.stringify(schema,null,2)+'\n');
await writeQuoteSchemas(schema);
await writeQuoteConfigurationSchemas();
function partial(value){
 if(Array.isArray(value))return value.map(partial);
 if(!value||typeof value!=='object')return value;
 const result={};
 for(const [key,v] of Object.entries(value)){
  if(key==='required')continue;
  if(['anyOf','oneOf'].includes(key)&&v.every(branch=>Object.keys(branch).every(k=>k==='required')))continue;
  result[key==='oneOf'?'anyOf':key]=key==='minItems'?0:partial(v);
 }
 return result;
}
const draft=partial(schema);
draft.$id='https://schemas.cover-mga.example/policy-draft/1.0';
draft.title='Incomplete policy proposal v1.0';
draft.required=['schemaVersion','productCode','productVersionId'];
draft.allOf[0].if.required=['productCode'];
await writeFile(new URL('policy-draft.schema.json',out),JSON.stringify(draft,null,2)+'\n');
const id=n=>`00000000-0000-4000-8000-${String(n).padStart(12,'0')}`;
const reference=(collection,value,label)=>({collection,value,label,version:'demo-2026-09'});
const responses={questionSetVersion:'demo-1',answers:[]};
const address={line1:'1 Example Yard',town:'Exampleton',postcode:'S9 2QT',country:'GB'};
const business={description:'Fictional vehicle repair and sales business',startedOn:'2020-01-01',turnover:'500000.00',wageRoll:'100000.00',activities:[{id:id(10),code:reference('activities','sales-repair','Sales and repair'),turnoverBasisPoints:10000}],responses};
const driver={id:id(11),firstName:'Alex',surname:'Example',dateOfBirth:'1985-05-20',address,relationship:reference('relationships','director','Director'),usage:reference('usage','trade','Trade only'),licence:{type:reference('licences','full-uk','Full UK'),number:'DEMO-LICENCE-001',testDate:'2005-04-01'},convictions:[],losses:[],responses};
const roadRisk={business,driverBasis:{kind:'named',responses},drivers:[driver],vehicles:[{id:id(12),registration:'DEMO 01',make:'Example',model:'Demonstrator',manufactureYear:2022,body:reference('body','van','Van'),ownership:'business-owned',value:'10000.00',responses}],specifiedVehicleIds:[],tradePlates:[],premises:[],previousInsurance:{insurer:'Demo Insurer',expiresOn:'2025-12-31',noClaimsYears:5,noClaimsYearsBasis:'exact',evidenceDocumentIds:[],responses},declarations:responses,materialFacts:'Fictional data only.'};
const policy={schemaVersion:'1.0',productCode:'motor-trade-road-risks',productVersionId:id(1),insured:{clientId:id(2),clientAgencyRelationshipId:id(3),entityType:'limited-company',legalName:'Example Motor Traders Ltd',address},term:{kind:'annual',startsAt:'2026-01-01T00:00:00Z',endsAt:'2027-01-01T00:00:00Z',timeZone:'Europe/London'},risk:roadRisk,cover:{sections:[{id:id(20),code:'road-risks',limit:'30000.00',excess:'500.00'}],endorsements:[],warranties:[],responses},premium:{currency:'GBP',annualPremium:'1200.00',termPremium:'1200.00',tax:'144.00',fee:'35.00',brokerCommission:'120.00',grossPayable:'1379.00',netBrokerDue:'1259.00',insurerDue:'1224.00',taxBasisPoints:1200,commissionBasisPoints:1000,ratingResultId:id(30),ruleVersion:'demo-rate-1'},provenance:{source:'demo-seed',quoteRevisionId:id(31),authorityVersionId:id(32)}};
policy.premium.settlement={termsVersionId:id(33),collector:'agency',mode:'net-remittance',feeShareBasisPoints:0,brokerFeeShare:'0.00',mgaFeeIncome:'35.00',brokerRemuneration:'120.00',invoiceDue:'1259.00',remunerationPayable:'0.00',netEconomicDue:'1259.00'};
const mtc=structuredClone(policy);mtc.productCode='motor-trade-combined';mtc.risk.premises=[{id:id(40),address,use:reference('premises-use','sales-repair','Sales and repair'),security:reference('security','alarm','Monitored alarm'),buildings:'250000.00',contents:'50000.00',responses}];mtc.cover.sections.push({id:id(21),code:'stock-custody',limit:'100000.00',excess:'1000.00'});
const cc=structuredClone(policy);cc.productCode='commercial-combined';cc.insured.legalName='Example Engineering Ltd';cc.risk={business:{...business,description:'Fictional engineering workshop'},locations:[{id:id(50),address,occupancy:'Fabrication',construction:'Brick and steel frame',security:reference('security','monitored','Monitored alarm'),floodZone:'1',buildings:'1000000.00',contents:'250000.00',stock:'100000.00',maximumEstimatedLoss:'1100000.00',responses}],wages:[{id:id(51),category:reference('wage','manual','Manual on premises'),employees:'100000.00',labourOnlySubcontractors:'0.00',bonaFideSubcontractors:'0.00'}],liability:{employersLimit:'10000000.00',publicLimit:'5000000.00',productsLimit:'5000000.00',maximumHeightMetres:2,employersReferenceNumber:'DEMO-ERN'},businessInterruption:{basis:'gross-profit',sumInsured:'500000.00',indemnityMonths:12},losses:[],declarations:responses,materialFacts:'Illustrative CC assumptions.'};cc.cover.sections=[{id:id(22),code:'property',limit:'1350000.00',excess:'1000.00'}];
const examples=new URL('../contracts/examples/',import.meta.url);await mkdir(examples,{recursive:true});
for(const p of [policy,mtc,cc])await writeFile(new URL(`${p.productCode}.json`,examples),JSON.stringify(p,null,2)+'\n');
console.log('Generated policy schema and three fictional product fixtures.');
