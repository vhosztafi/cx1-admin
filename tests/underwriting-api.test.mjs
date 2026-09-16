import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const spec=await read('openapi.json');
const examples=(await read('examples/underwriting-api.json')).requests;
const operations=Object.values(spec.paths).flatMap(path=>Object.values(path)).filter(x=>x.operationId);
const ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);ajv.addFormat('binary',true);
const aliases={};
for(const name of ['policy','policy-draft','quote-draft']){
  const schema=await read(`schemas/${name}.schema.json`);ajv.addSchema(schema);aliases[`./schemas/${name}.schema.json`]=schema.$id;
}
const root='https://contracts.cover-mga.example/underwriting-test';
function relocate(value){
  if(Array.isArray(value))return value.map(relocate);
  if(value&&typeof value==='object')return Object.fromEntries(Object.entries(value).map(([k,v])=>[k,k==='$ref'?(aliases[v]??v.replace('#/components/schemas/',`${root}#/$defs/`)):relocate(v)]));
  return value;
}
ajv.addSchema({$id:root,$defs:relocate(spec.components.schemas)});
const operation=name=>{const op=operations.find(x=>x.operationId===name);assert.ok(op,name);return op;};
const request=name=>ajv.compile(relocate(operation(name).requestBody.content['application/json'].schema));

test('rating UI projections require explicit refresh offers and bounded scoped history',()=>{
  const assessment=spec.components.schemas.UnderwritingAssessment;
  assert.ok(assessment.required.includes('refreshOptions'));
  assert.equal(assessment.properties.refreshOptions.maxItems,32);
  assert.ok(assessment.properties.jobId);
  const history=operation('listQuoteRatings');
  for(const key of ['cursor','pageSize'])assert.ok(history.parameters.some(parameter=>parameter.name===key));
  assert.equal(spec.components.schemas.UnderwritingRatingHistoryItem.additionalProperties,false);
});

test('every Phase6 JSON command has an actual positive fixture and rejects client authority overrides',()=>{
  const writes=operations.filter(op=>op['x-runtime-status']?.startsWith('phase-6-')&&op.requestBody?.content?.['application/json']);
  assert.equal(writes.length,Object.keys(examples).length);
  for(const op of writes){
    const body=examples[op.operationId];assert.ok(body,op.operationId);
    const validate=request(op.operationId);assert.ok(validate(body),`${op.operationId}: ${JSON.stringify(validate.errors)}`);
    for(const key of ['actorId','ignoreAuthority','manualPremium','state'])assert.equal(validate({...body,[key]:true}),false,`${op.operationId}/${key}`);
    for(const key of Object.keys(body)){const missing=structuredClone(body);delete missing[key];assert.equal(validate(missing),false,`${op.operationId} requires ${key}`);}
  }
});
test('all underwriting writes require CSRF, command identity and owning quote concurrency',()=>{
  for(const op of operations.filter(x=>x['x-runtime-status']?.startsWith('phase-6-')&&x['x-idempotency']==='required')){
    assert.equal(op['x-etag-resource'],'quote',op.operationId);
    assert.ok(op.parameters.some(x=>x.name==='If-Match'&&x.required),op.operationId);
    assert.ok(op.parameters.some(x=>x.name==='Idempotency-Key'&&x.required),op.operationId);
    assert.ok(op.security.some(x=>'Session' in x&&'Csrf' in x),op.operationId);
    for(const code of ['401','403','404','409','412','422','503'])assert.ok(op.responses[code],`${op.operationId}/${code}`);
  }
});
test('exact acceptance rejects absent assurance, evidence or terms identity and unsupported channels',()=>{
  const validate=request('recordQuoteAcceptance'),base=examples.recordQuoteAcceptance;
  for(const key of ['termsHash','assuranceHash'])assert.equal(validate({...base,[key]:'not-a-hash'}),false,key);
  assert.equal(validate({...base,channel:'automatic'}),false);
  assert.equal(validate({...base,acceptedAt:'yesterday'}),false);
  assert.equal(validate({...base,evidenceAssociationId:null}),false);
});
test('conditional decisions require typed nonempty conditions and bulk decisions retain each child ETag',()=>{
  const validate=request('decideQuoteReferrals'),base=structuredClone(examples.decideQuoteReferrals);
  base.decisions[0].outcome='approve-with-conditions';assert.equal(validate(base),false);
  base.decisions[0].conditions=[];assert.equal(validate(base),false);
  base.decisions[0].conditions=[{code:'provide-trading-history'}];assert.ok(validate(base));
  base.decisions[0].etag='W/"v1"';assert.equal(validate(base),false);
  base.decisions[0].etag='"v1"';base.decisions[0].conditions=[{description:'Do anything'}];assert.equal(validate(base),false);
});
test('supplied capacity outcomes cannot hide missing evidence or approval extent in free text',()=>{
  const validate=request('recordCapacityResponse'),base=structuredClone(examples.recordCapacityResponse);
  assert.ok(validate(base));
  base.authorisedLimits=[{dimension:'stock-limit',maximumAmount:'150000.00',allQuotes:true}];assert.equal(validate(base),false);
  base.authorisedLimits=[{dimension:'stock-limit',maximumAmount:'150000.00'}];base.outcome='approve-with-conditions';assert.equal(validate(base),false);
  base.conditions=[{code:'provide-trading-history'}];assert.ok(validate(base));
  base.outcome='decline';assert.equal(validate(base),false,'decline cannot smuggle approval extent');
});
test('issue response exposes durable identities without a fabricated paid balance',()=>{
  const schema=spec.components.schemas.UnderwritingIssueResult;
  for(const key of ['policyId','policyReference','termId','versionId','transactionId','obligationId','documentRequestIds','quoteEtag'])assert.ok(schema.required.includes(key),key);
  for(const key of ['totalCollected','paidAt','cashReceiptId'])assert.equal(schema.properties[key],undefined);
});
test('proof and decision histories are paged and policy discovery has real registration/filter contracts',()=>{
  for(const name of ['listUnderwritingEvidence','listUnderwritingEvidenceEvents','listReferralDecisions','listEscalationMessages']){
    const op=operation(name);for(const key of ['cursor','pageSize'])assert.ok(op.parameters.some(p=>p.name===key),`${name}/${key}`);
  }
  for(const key of ['registration','clientId','productCode','inceptionFrom','inceptionTo','sort','direction'])assert.ok(operation('listPolicies').parameters.some(x=>x.name===key),key);
  assert.ok(spec.components.schemas.QuoteCaptureView.properties.state.enum.includes('bound'));
  assert.ok(spec.components.schemas.QuoteCaptureView.properties.state.enum.includes('accepted'));
});
