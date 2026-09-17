import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
const schema=JSON.parse(await readFile(new URL('../contracts/schemas/servicing.schema.json',import.meta.url),'utf8'));
const ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);ajv.addSchema(schema);
const check=name=>ajv.compile({$ref:`${schema.$id}#/$defs/${name}`});
const proposal=check('ServicingProposal'),change=check('ServicingChange');
const id='10000000-0000-4000-8000-000000000001',other='10000000-0000-4000-8000-000000000002';
const intent={localDate:'2026-09-15',localTime:'00:00',timeZone:'Europe/London'};
const base={schemaVersion:'1.0',baseVersionId:id,reason:'Insured requested adjustment',requestedBy:{kind:'internal'},commonEffectiveIntent:intent,changes:[]};
test('servicing proposal saves incomplete typed data without exposing issued authority',()=>{
 assert.ok(proposal(base),JSON.stringify(proposal.errors));
 for(const key of ['actorId','premium','status','acceptanceId','agencyId','supportFlags'])assert.equal(proposal({...base,[key]:id}),false,key);
});
test('only cover operations accept a later effective intent and remove cannot carry payload',()=>{
 const item={changeId:id,riskItemId:other,kind:'cover',operation:'update',payload:{},effectiveIntent:intent};
 assert.ok(change(item),JSON.stringify(change.errors));
 assert.equal(change({...item,kind:'driver'}),false);
 assert.equal(change({...item,operation:'remove'}),false);
 assert.ok(change({changeId:id,riskItemId:other,kind:'driver',operation:'remove'}));
});
test('typed change payloads reject arbitrary properties and unsupported categories',()=>{
 for(const kind of ['driver','vehicle','premises','business','cover','policyholder']){
  const item={changeId:id,riskItemId:other,kind,operation:'update',payload:{}};
  assert.ok(change(item),`${kind}: ${JSON.stringify(change.errors)}`);
  assert.equal(change({...item,payload:{unrecognizedRisk:'fake risk'}}),false,kind);
 }
 assert.equal(change({changeId:id,riskItemId:other,kind:'claims',operation:'add',payload:{}}),false);
 assert.ok(change({changeId:id,riskItemId:other,kind:'business',operation:'update',payload:{description:'Vehicle servicing'}}));
});

test('editor projection contains typed capture and cannot expose issued pricing as editable data',async()=>{
 const validate=check('ServicingEditor');
 for(const product of ['motor-trade-road-risks','motor-trade-combined']) {
  const issued=JSON.parse(await readFile(new URL(`../contracts/examples/issued-${product}.json`,import.meta.url),'utf8'));
  const {clientId,clientAgencyRelationshipId,...insured}=issued.insured;
  const {driverBasis,...risk}=issued.risk;
  const {sections,endorsements,warranties,...cover}=issued.cover;
  const capture={schemaVersion:'1.0',productCode:product,insured,risk,cover,termIntent:{kind:'annual',timeZone:'Europe/London',localStartDate:'2026-09-15',localStartTime:'09:00',utcOffsetMinutes:60}};
  const value={draftId:id,revisionId:other,clientId,captureVersions:{schemaVersion:'1.0',questionSetVersion:'mt-capture-57b711ca02317ca0',referenceDataVersion:'mt-capture-57b711ca02317ca0'},
   assessment:{base:capture,proposed:structuredClone(capture),changes:[],readinessIssues:[{code:'required-driver-field',path:'/risk/drivers/1/fullName',questionId:null}],slices:[]}};
  assert.ok(validate(value),JSON.stringify(validate.errors));
  value.assessment.proposed.premium=issued.premium;
  assert.equal(validate(value),false);
 }
});

test('explicit typed replacement is update-only and never admits target IDs or unknown fields',()=>{
 for(const kind of ['driver','vehicle','premises','business','cover','policyholder']) {
  const value={changeId:id,riskItemId:other,kind,operation:'update',payload:{},payloadMode:'replace'};
  assert.ok(change(value),JSON.stringify(change.errors));
  assert.equal(change({...value,operation:'add'}),false);
  assert.equal(change({...value,operation:'remove'}),false);
  assert.equal(change({...value,payloadMode:'unset-paths'}),false);
  assert.equal(change({...value,payload:{id}}),false);
  assert.equal(change({...value,payload:{agencyId:id}}),false);
 }
});

test('specified-vehicle declarations are explicit vehicle-only booleans without foreign targets',()=>{
 const value={changeId:id,riskItemId:other,kind:'vehicle',operation:'remove',specifiedVehicle:{selected:false,required:false}};
 assert.ok(change(value),JSON.stringify(change.errors));
 assert.equal(change({...value,kind:'driver'}),false);
 assert.equal(change({...value,specifiedVehicle:{selected:false}}),false);
 assert.equal(change({...value,specifiedVehicle:{selected:false,required:false,riskItemId:id}}),false);
 assert.equal(change({...value,specifiedVehicle:{selected:'false',required:false}}),false);
});
test('lease commands enforce takeover reason and prevent client supplied expiry or holder',()=>{
 const validate=check('ServicingLeaseAcquire');
 assert.ok(validate({mode:'acquire'}));
 assert.ok(validate({mode:'takeover',reason:'Editor unavailable; assigned case'}));
 assert.equal(validate({mode:'takeover'}),false);
 assert.equal(validate({mode:'acquire',expiresAt:'2026-09-15T00:00:00Z'}),false);
 assert.equal(validate({mode:'acquire',holderId:id}),false);
});
test('cancellation approval and experience preserve bounded reviewed provenance',()=>{
 const approval=check('ServicingCancellationApproval');
 assert.ok(approval({previewId:id,previewHash:'a'.repeat(64),reason:'Reviewed notice and return'}));
 assert.equal(approval({previewId:id,previewHash:'a'.repeat(64),reason:'ok',approvedBy:id}),false);
 const experience=check('ServicingExperience');
 const value={observationStartsOn:'2025-01-01',observationEndsOn:'2026-01-01',claimCount:2,paid:'200.00',outstanding:'300.00',earnedPremium:'1000.00',sourceCode:'agency',sourceReference:'Demo annual statement',evidenceAssociationId:id};
 assert.ok(experience(value),JSON.stringify(experience.errors));
 for(const paid of ['-1.00','1.1','NaN','1e3'])assert.equal(experience({...value,paid}),false);
 assert.equal(experience({...value,lossRatio:'0.50'}),false);
});
test('acceptance requires exact terms delivery and evidence; prices cannot be supplied',()=>{
 const validate=check('ServicingAcceptance');
 const value={termsId:id,deliveryId:other,accepter:'Fictional broker',receivedAt:'2026-09-15T10:00:00Z',channel:'written',evidenceAssociationId:id};
 assert.ok(validate(value));
 for(const key of ['termsId','deliveryId','evidenceAssociationId']){const copy={...value};delete copy[key];assert.equal(validate(copy),false,key);}
 assert.equal(validate({...value,premium:'1.00'}),false);
});
test('command arrays and proposal changes have bounded sizes',()=>{
 assert.equal(proposal({...base,changes:Array.from({length:101},()=>({changeId:id,riskItemId:other,kind:'driver',operation:'remove'}))}),false);
 const validate=check('ServicingSelectedDecision');
 assert.equal(validate({cycleId:id,ratingId:id,outcome:'approve',reason:'Within current authority',selected:[]}),false);
});

test('fictional fixtures preserve the full cancellation reason catalogue and one-fee arithmetic',async()=>{
 const fixture=JSON.parse(await readFile(new URL('../contracts/examples/servicing-demo.json',import.meta.url),'utf8'));
 assert.ok(proposal(fixture.proposal),JSON.stringify(proposal.errors));
 assert.deepEqual(fixture.cancellationRules.map(x=>x.code),['insured-request','non-payment','non-disclosure','trade-ceased','insurer-instruction']);
 assert.deepEqual(fixture.cancellationRules.map(x=>[x.noticeDays,x.separateApprover]),[[0,false],[7,true],[7,true],[0,false],[0,true]]);
 const pennies=value=>BigInt(value.replace('.',''));
 const sum=field=>fixture.mixedDateAdjustment.slices.reduce((n,x)=>n+pennies(x[field]),0n);
 assert.equal(sum('premium')+sum('tax')+pennies(fixture.mixedDateAdjustment.singleFee),12914n);
 assert.equal(sum('premium')+sum('tax')+pennies(fixture.mixedDateAdjustment.singleFee)-sum('commission'),11895n);
 assert.equal(pennies(fixture.cancellation.premium)+pennies(fixture.cancellation.tax)-pennies(fixture.cancellation.commission),-32696n);
 assert.equal(fixture.cancellation.cashPaid,false);
});

test('new issued servicing format requires its own immutable provenance and rejects quote masquerading',async()=>{
 const issued=JSON.parse(await readFile(new URL('../contracts/schemas/issued-servicing.schema.json',import.meta.url),'utf8'));
 const validate=ajv.compile(issued);
 for(const product of ['motor-trade-road-risks','motor-trade-combined']) {
  const value=JSON.parse(await readFile(new URL(`../contracts/examples/issued-${product}.json`,import.meta.url),'utf8'));
  assert.equal(validate(value),false);
  value.snapshotFormat='issued-servicing-1';
  value.provenance={source:'backoffice',sourceQuoteId:id,servicingIssueDecisionId:other,baseVersionId:id,revisionId:other,transactionId:id,effectiveAt:'2026-09-15T00:00:00Z',processedAt:'2026-09-14T10:00:00Z',sliceOrdinal:0,inputHash:'a'.repeat(64)};
  assert.ok(validate(value),JSON.stringify(validate.errors));
  value.provenance.quoteRevisionId=id; assert.equal(validate(value),false);
 }
});

test('issue decisions cannot mix quote, cancellation and adjustment provenance',()=>{
 const validate=check('ServicingIssueDecision');
 const value={id,draftId:id,policyId:id,baseTermId:id,baseVersionId:id,revisionId:id,kind:'cancellation',cancellationPreviewId:id,cancellationApprovalId:other,effectiveAt:'2026-09-15T00:00:00Z',inputHash:'a'.repeat(64),createdAt:'2026-09-14T10:00:00Z',createdBy:id};
 assert.ok(validate(value));
 assert.equal(validate({...value,cycleId:id}),false);
 assert.equal(validate({...value,quoteRevisionId:id}),false);
 const missing={...value};delete missing.cancellationApprovalId;assert.equal(validate(missing),false);
});
