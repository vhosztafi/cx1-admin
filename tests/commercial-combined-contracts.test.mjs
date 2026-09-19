import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
const read=async p=>JSON.parse(await readFile(p,'utf8'));
async function fixture(){const schema=await read('contracts/schemas/commercial-combined.schema.json');const ajv=new Ajv2020({allErrors:true,strict:true});addFormats(ajv);return {schema,validate:ajv.compile(schema),value:await read('contracts/examples/commercial-combined-capture.json')};}
test('CC closed draft accepts missing facts and a complete two-location capture fixture',async()=>{
 const {validate,value}=await fixture();assert.ok(validate(value),JSON.stringify(validate.errors));
 assert.ok(validate({schemaVersion:'1.0',format:'commercial-combined-capture-1',productCode:'commercial-combined'}));
 assert.equal(validate({format:'commercial-combined-capture-1',productCode:'commercial-combined'}),false,'QuoteRevision storage requires schemaVersion');
 assert.equal(value.risk.locations.length,2);
});
test('CC rejects MT risk and all computed or privileged write members',async()=>{
 const {validate,value}=await fixture();
 for(const [path,key,val] of [[[],'premium',{annualPremium:'1.00'}],[[],'actorId','forged'],[['risk'],'drivers',[]],[['risk'],'districtTotal','0.00'],[['risk','locations',0],'postcodeDistrict','S9']]){
  const p=structuredClone(value);let target=p;for(const part of path)target=target[part];target[key]=val;assert.equal(validate(p),false,key);
 }
 const p=structuredClone(value);p.productCode='motor-trade-combined';assert.equal(validate(p),false);
});
test('CC monetary and reference inputs remain exact and closed',async()=>{
 const {validate,value}=await fixture();
 for(const amount of ['1.001','-1.00','01.00',1,'1e3','10000000000000.00']){const p=structuredClone(value);p.risk.locations[0].buildings=amount;assert.equal(validate(p),false,String(amount));}
 const p=structuredClone(value);p.risk.locations[0].responses.answers.push({questionId:'unknown-question',kind:'boolean',value:false});assert.equal(validate(p),false);
});
test('CC schema keeps unknown distinct from No and limits questions to their owning subject',async()=>{
 const {validate,value}=await fixture();const p=structuredClone(value);
 p.risk.locations[0].responses.answers=[{questionId:'prototype.addloc.sole-occupier',kind:'boolean',value:false}];assert.ok(validate(p),JSON.stringify(validate.errors));
 p.risk.locations[0].responses.answers[0].value=null;assert.equal(validate(p),false);
 p.risk.locations[0].responses.answers=[{questionId:'prototype.quote.36ef01068295',kind:'boolean',value:true}];assert.equal(validate(p),false);
});
test('CC cross-field contract rejects duplicate identities, foreign loss subjects and contradictory selected cover',async()=>{
 const {commercialSemanticIssues,normalizeCommercialPostcode}=await import('../scripts/commercial-combined-invariants.mjs');
 const {value}=await fixture();const duplicate=structuredClone(value);duplicate.risk.locations[1].id=duplicate.risk.locations[0].id;
 assert.ok(commercialSemanticIssues(duplicate).invalid.includes('duplicate-risk-id'));
 const foreign=structuredClone(value);foreign.risk.losses=[{id:'00000000-0000-4000-8000-000000000099',riskItemId:'00000000-0000-4000-8000-000000000098'}];
 assert.ok(commercialSemanticIssues(foreign).invalid.includes('loss-location-not-owned'));
 const disabled=structuredClone(value);disabled.risk.declarations.answers[0].value=false;
 assert.ok(commercialSemanticIssues(disabled).readiness.includes('el-disabled-details'));
 const repeated=structuredClone(value);repeated.risk.declarations.answers.push({...repeated.risk.declarations.answers[0],value:false});
 assert.ok(commercialSemanticIssues(repeated).invalid.includes('duplicate-question-id'));
 assert.deepEqual(normalizeCommercialPostcode('s9 2qt'),{postcode:'S9 2QT',district:'S9'});
 assert.deepEqual(normalizeCommercialPostcode('GIR0AA'),{postcode:'GIR 0AA',district:'GIR'});
 assert.equal(normalizeCommercialPostcode('ZZ99ZZ'),null);
});
test('CC exposure audience schemas prohibit foreign identifiers and agency book totals',async()=>{
 const {schema}=await fixture();const ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);ajv.addSchema(schema);
 const validate=ajv.compile({$ref:schema.$id+'#/$defs/Exposure'});
 const p={format:'commercial-exposure-1',audience:'agency',observedAt:'2026-09-19T12:00:00Z',effectiveAt:'2026-09-19T12:00:00Z',knownAt:'2026-09-19T12:00:00Z',advisory:true,districts:[{district:'S9',interval:{startsAt:'2026-04-01T00:00:00Z',endsAt:'2027-04-01T00:00:00Z'},ownProposedSumInsured:'1350000.00',outcome:'within-capacity'}]};
 assert.ok(validate(p),JSON.stringify(validate.errors));p.districts[0].bookSumInsured='3000000.00';assert.equal(validate(p),false);
 delete p.districts[0].bookSumInsured;p.districts[0].policyIds=['00000000-0000-4000-8000-000000000001'];assert.equal(validate(p),false);
});
test('CC issued shape is distinct and cannot weaken retained Motor Trade snapshots',async()=>{
 const [schema,oldSchema]=await Promise.all([read('contracts/schemas/commercial-combined-issued.schema.json'),read('contracts/schemas/issued-policy.schema.json')]);
 const ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);const validate=ajv.compile(schema);
 const {value}=await fixture();const old=await read('contracts/examples/motor-trade-road-risks.json');
 const p=structuredClone(value);delete p.format;delete p.termIntent;
 Object.assign(p,{schemaVersion:'1.0',snapshotFormat:'issued-commercial-1',productVersionId:old.productVersionId,term:old.term,premium:old.premium,provenance:old.provenance});
 Object.assign(p.insured,{clientId:old.insured.clientId,clientAgencyRelationshipId:old.insured.clientAgencyRelationshipId});
 Object.assign(p.cover,{sections:[{id:'00000000-0000-4000-8000-000000000077',code:'property',limit:'2000000.00'}],endorsements:[],warranties:[]});
 assert.ok(validate(p),JSON.stringify(validate.errors));
 p.risk.driverBasis={kind:'named'};assert.equal(validate(p),false);
 assert.deepEqual(oldSchema.properties.productCode.enum,['motor-trade-road-risks','motor-trade-combined']);
 assert.equal(oldSchema.properties.snapshotFormat.const,'issued-quote-1');
});
test('CC published demo examples reconcile independent exact-penny factors and signed movements',async()=>{
 const {configuration:c,annualExample:a,halfTermMovements}=await read('contracts/examples/commercial-combined-rating.json');
 const n=x=>BigInt(x.replace('.',''));
 const round=(a,b)=>a<0n?-((-a+b/2n)/b):(a+b/2n)/b;
 const rate=(value,bps)=>round(n(value)*BigInt(bps),10000n);
 for(const [key,basis,bps] of [['buildings','buildings','buildingsRateBps'],['contents','contents','contentsRateBps'],['stock','stock','stockRateBps'],['businessInterruption','businessInterruption','businessInterruptionRateBps'],['employees','manualEmployees','manualEmployeeRateBps'],['labourOnly','manualLabourOnly','manualLabourOnlyRateBps'],['bonaFide','bonaFide','bonaFideRateBps'],['publicLiability','turnover','publicLiabilityTurnoverRateBps'],['productsLiability','turnover','productsLiabilityTurnoverRateBps']])assert.equal(rate(a[basis],c[bps]),n(a.factors[key]),key);
 const premium=Object.values(a.factors).reduce((v,x)=>v+n(x),0n);assert.equal(premium,n(a.expected.annualPremium));
 for(const result of [a.expected,a.withoutElAndBi]){
  assert.equal(rate(result.annualPremium,c.taxRateBps),n(result.tax));assert.equal(rate(result.annualPremium,c.commissionRateBps),n(result.commission));
  assert.equal(n(result.annualPremium)+n(result.tax)+n(result.fee),n(result.gross));assert.equal(n(result.gross)-n(result.commission),n(result.netAgencyDue));
 }
 assert.equal(premium-n(a.factors.businessInterruption)-n(a.factors.employees)-n(a.factors.labourOnly),n(a.withoutElAndBi.annualPremium));
 for(const x of halfTermMovements){assert.equal(round(n(x.annualDelta)*BigInt(x.termFractionNumerator),BigInt(x.termFractionDenominator)),n(x.premium));assert.equal(rate(x.premium,c.taxRateBps),n(x.tax));assert.equal(rate(x.premium,c.commissionRateBps),n(x.commission));assert.equal(n(x.premium)+n(x.tax)+n(x.fee),n(x.gross));assert.equal(n(x.gross)-n(x.commission),n(x.netAgencyDue));}
});
