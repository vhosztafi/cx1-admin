import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir,open,unlink} from 'node:fs/promises';
import {resolve,sep} from 'node:path';
import {chromium} from 'playwright';
import {openDemoJournal} from './demo-command-journal.mjs';

// Normal local API commands only. Retain the journal to resume the original
// request/key after an interrupted acknowledgement. No provider is contacted.
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';
assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const apiOrigin=process.env.COVER_COMMERCIAL_DEMO_API_ORIGIN??origin;
assert.ok(['localhost','127.0.0.1'].includes(new URL(apiOrigin).hostname));assert.notEqual(new URL(apiOrigin).port,'5000');
const knownAt=()=>process.env.COVER_COMMERCIAL_DEMO_KNOWN_AT??new Date().toISOString();
const file=process.argv[2];assert.ok(file,'Pass the JSON result of --seed-commercial-proposals-demo.');
const stage=process.argv[3]??'issue';assert.ok(['issue','adjustment','renewal','cancellation','scenarios','full'].includes(stage));
const operational=process.argv[4]==='operational-incident';
assert.ok(process.argv[4]===undefined||operational,'Unknown demo scenario selector.');
if(operational)assert.equal(stage,'issue','Operational incident setup only issues its separate policy.');
const supplied=JSON.parse(await readFile(file,'utf8'));
assert.ok(Array.isArray(supplied));
const fixtures=supplied.map(x=>({scenario:x.scenario??x.Scenario,quoteId:x.quoteId??x.QuoteId}));
assert.equal(new Set(fixtures.map(x=>x.scenario)).size,fixtures.length);
for(const f of fixtures)assert.match(f.quoteId,/^[a-f0-9-]{36}$/i);
const directory=process.env.COVER_COMMERCIAL_DEMO_DIRECTORY??'.local/commercial-lifecycle-demo-v1';
if(operational)assert.notEqual(resolve(directory),resolve('.local/commercial-lifecycle-demo-v1'),'Use a separate operational journal.');
assert.ok(resolve(directory).startsWith(resolve('.local')+sep));await mkdir(directory,{recursive:true});
const lock=await open(directory+'/running.lock','wx');let browser;
try {
 await lock.writeFile(String(process.pid));
 try{assert.deepEqual(JSON.parse(await readFile(directory+'/fixtures.json','utf8')),fixtures,'This journal belongs to different demonstration proposals.');}
 catch(error){if(error.code!=='ENOENT')throw error;await writeFile(directory+'/fixtures.json',JSON.stringify(fixtures,null,2),{flag:'wx'});}
 const journal=await openDemoJournal(directory+'/commands.json',origin);
 browser=await chromium.launch({channel:'chrome',headless:true});const page=await browser.newPage();
 if(apiOrigin!==origin)await page.route('**/api/v1/**',async route=>{const url=new URL(route.request().url());const response=await route.fetch({url:apiOrigin+url.pathname+url.search});await route.fulfill({response});});
 await page.goto(origin+'/login');await page.getByLabel('Email address',{exact:true}).fill('senior-underwriter@cover.example');
 await page.getByLabel('Password',{exact:true}).fill(process.env.COVER_COMMERCIAL_DEMO_PASSWORD??(await readFile('.local/demo-password.txt','utf8')).trim());
 await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');
 async function get(path){const response=await page.request.get(apiOrigin+path);assert.equal(response.status(),200,`${path}: HTTP ${response.status()}`);return{data:await response.json(),etag:response.headers().etag};}
 async function command(name,path,data,scope,file,options={}){
  const current=scope?await get(scope):null; // Current scoped read precedes local journal disclosure.
  return journal.command(name,async()=>({path,data,file,etag:current?.etag,method:options.method??'POST',lease:options.lease}),async(request,key)=>{
   assert.ok(request.path.startsWith('/api/v1/'));
   const csrf=(await get('/api/v1/auth/csrf')).data.requestToken;
   const response=await page.request.fetch(apiOrigin+request.path,{method:request.method??'POST',headers:{'X-CSRF-Token':csrf,'Idempotency-Key':key,...(request.etag?{'If-Match':request.etag}:{}),...(request.lease?{'X-Edit-Lease':request.lease}:{})},
    ...(request.file?{multipart:{fileName:request.file.name,contentType:'text/plain',...(request.file.purpose?{purpose:request.file.purpose}:{}),file:{name:request.file.name,mimeType:'text/plain',buffer:Buffer.from(request.file.content)}}}:{data:request.data})});
   if(!response.ok()){let code='request-failed';try{const body=await response.json();code=body.code??body.type??code;}catch{}throw Error(`${request.path}: HTTP ${response.status()} ${code}`);}
   return response.json();
  });
 }
 async function until(path,predicate){const end=Date.now()+120000;do{const data=(await get(path)).data;if(predicate(data))return data;await new Promise(r=>setTimeout(r,300));}while(Date.now()<end);throw Error(`Persisted demo result did not arrive: ${path}`);}
 async function proof(prefix,route,purpose){
  assert.ok(purpose,'A current proof purpose is required.');
  const step=`${prefix}:proof:${purpose.code}:${purpose.riskItemId??'policy'}:${purpose.termsVersionId??'risk'}`;
  const file=await command(step+':upload',route+'/underwriting/evidence-files',undefined,route,{name:'fictional-commercial-proof.txt',content:`Fictional demonstration proof for ${purpose.code}; no real claim or insurer communication.`});
  const assessment=(await get(route+'/underwriting')).data;
  const attached=await command(step+':attach',route+'/underwriting/evidence',{cycleId:assessment.context.cycleId,fileId:file.id,requirementCode:purpose.code,inputFingerprint:purpose.inputFingerprint,
   ...(purpose.riskItemId?{riskItemId:purpose.riskItemId}:{}),...(purpose.conditionId?{conditionId:purpose.conditionId}:{}),...(purpose.termsVersionId?{termsVersionId:purpose.termsVersionId}:{}),reason:'Supply exact fictional commercial demonstration proof'},route);
  const saved=(await get(route+'/underwriting/evidence')).data.items.find(x=>x.id===attached.id);assert.ok(saved);
  await command(step+':review',route+`/underwriting/evidence/${saved.id}/reviews`,{cycleId:assessment.context.cycleId,associationEtag:saved.etag,expectedFingerprint:purpose.inputFingerprint,outcome:'accepted',reason:'Review fictional proof against its exact current purpose'},route);
  return saved;
 }
 const prefix=operational?'commercial-incident':'two-location';
 const fixture=fixtures.find(x=>x.scenario===prefix);assert.ok(fixture,`The ${prefix} demonstration proposal is required.`);
 const route='/api/v1/quotes/'+fixture.quoteId;
 const quote=(await get(route)).data;assert.equal(quote.productCode,'commercial-combined');
 let policy;
 if(quote.boundPolicyId)policy=(await get('/api/v1/policies/'+quote.boundPolicyId)).data;
 else {
  assert.equal(quote.proposal.risk.materialFacts,operational?'Fictional operational demo v1: commercial-incident':'Fictional commercial demo v1: two-location','Edited scenario requires review before automated demonstration issue.');
  await command(prefix+':rate',route+'/rate',{revisionId:quote.revisionId,reason:`Rate the fictional ${prefix} commercial demonstration`},route);
  let assessment=await until(route+'/underwriting',x=>!!x.ratingId);
  for(const purpose of assessment.proofRequirements.filter(x=>!x.satisfied))await proof(prefix,route,purpose);
  const referrals=(await get('/api/v1/referrals?quoteId='+fixture.quoteId)).data.items;
  if(referrals.length)await command(prefix+':decisions',route+'/referral-decisions',{cycleId:assessment.context.cycleId,decisions:referrals.map(x=>({referralId:x.id,etag:x.etag,outcome:'approve',reason:'Review the fictional demonstration within current commercial authority'}))},route);
  let terms=(await get(route+'/terms')).data;assert.ok(terms.templates.length);
  await command(prefix+':prepare',route+'/terms/prepare',{cycleId:assessment.context.cycleId,ratingId:assessment.ratingId,templateVersionId:terms.templates[0].id},route);
  assessment=(await get(route+'/underwriting')).data;await proof(prefix,route,assessment.proofRequirements.find(x=>x.code==='signed-statement'));
  terms=(await get(route+'/terms')).data;const contract=terms.terms[0],recipient=terms.recipientOptions[0];assert.ok(recipient);
  await command(prefix+':deliver',route+'/terms',{termsVersionId:contract.id,recipientContactIds:[recipient.id]},route);
  await until(route+'/terms',x=>x.deliveries.some(d=>d.termsVersionId===contract.id&&d.state==='delivered'));
  assessment=(await get(route+'/underwriting')).data;const acceptedProof=await proof(prefix,route,assessment.proofRequirements.find(x=>x.code==='acceptance-proof'));
  assessment=(await get(route+'/underwriting')).data;
  await command(prefix+':accept',route+'/acceptances',{cycleId:assessment.context.cycleId,ratingId:assessment.ratingId,termsVersionId:contract.id,termsHash:assessment.termsHash,assuranceHash:assessment.assuranceHash,
   accepterLabel:'Fictional commercial demonstration customer',acceptedAt:knownAt(),channel:'written',evidenceAssociationId:acceptedProof.id},route);
  assessment=(await get(route+'/underwriting')).data;assert.equal(assessment.capabilities.canIssue,true);
  const receipt=await command(prefix+':issue',route+'/issue',{cycleId:assessment.context.cycleId,ratingId:assessment.ratingId,acceptanceId:assessment.acceptanceId,termsHash:assessment.termsHash,assuranceHash:assessment.assuranceHash,reason:'Issue the fictional commercial demonstration through current authority'},route);
  policy=(await get('/api/v1/policies/'+receipt.policyId)).data;
 }
 assert.equal(policy.sourceQuoteId,fixture.quoteId);assert.equal(policy.snapshot.productCode,'commercial-combined');
 await writeFile(directory+'/issued-base.json',JSON.stringify({policyId:policy.id,reference:policy.reference,termId:policy.termId,versionId:policy.versionId,contentHash:policy.contentHash,sourceQuoteId:fixture.quoteId},null,2));
 if(['adjustment','renewal','cancellation','full'].includes(stage)){
  const {commercialDemoAdjustment,commercialDemoRenewal}=await import('./commercial-demo-servicing.mjs');
  const adjusted=await commercialDemoAdjustment({policy,get,command,until,knownAt,directory});
  if(['renewal','cancellation','full'].includes(stage)){
   const renewed=await commercialDemoRenewal({policy:adjusted,get,command,until,knownAt,directory});
   if(['cancellation','full'].includes(stage)){
    const {commercialDemoCancellation}=await import('./commercial-demo-cancellation.mjs');
    await commercialDemoCancellation({policy:renewed,get,command,until,knownAt,directory});
   }
  }
 }
 if(['scenarios','full'].includes(stage)){
  const {commercialDemoReferrals}=await import('./commercial-demo-referrals.mjs');
  const scenarios=await commercialDemoReferrals({fixtures,get,command,until,knownAt,directory});
  if(stage==='full'){
   const {commercialDemoReport}=await import('./commercial-demo-report.mjs');
   await commercialDemoReport({directory,origin,scenarios});
  }
 }
 console.log(`Commercial ${stage} demonstration verified through persisted API readback. Reports: ${directory}`);
}finally{await browser?.close();await lock.close();await unlink(directory+'/running.lock');}
