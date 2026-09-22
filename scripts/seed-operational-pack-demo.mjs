import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir,open,unlink} from 'node:fs/promises';
import {chromium} from 'playwright';
import {openDemoJournal} from './demo-command-journal.mjs';

const stage=process.argv[2];assert.ok(['prepare','queue','retry','read'].includes(stage));
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const directory='.local/operational-pack-demo-v1';await mkdir(directory,{recursive:true});
const lock=await open(directory+'/running.lock','wx');let browser;
try{
 await lock.writeFile(String(process.pid));const journal=await openDemoJournal(directory+'/commands.json',origin);
 browser=await chromium.launch({headless:true});const page=await browser.newPage();
 await page.goto(origin+'/login');await page.getByLabel('Email address',{exact:true}).fill('senior-underwriter@cover.example');
 await page.getByLabel('Password',{exact:true}).fill((await readFile('.local/demo-password.txt','utf8')).trim());
 await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');
 async function get(path){const r=await page.request.get(origin+path);assert.equal(r.status(),200,`${path}: ${r.status()}`);return{data:await r.json(),etag:r.headers().etag};}
 async function command(name,path,data,scope,versioned=false){
  const current=await get(scope);
  return journal.command(name,async()=>({path,data,etag:versioned?current.etag:undefined}),async(request,key)=>{
   const csrf=(await get('/api/v1/auth/csrf')).data.requestToken;
   const r=await page.request.post(origin+request.path,{data:request.data,headers:{'X-CSRF-Token':csrf,'Idempotency-Key':key,...(request.etag?{'If-Match':request.etag}:{})}});
   assert.ok(r.ok(),`${request.path}: ${r.status()} ${await r.text()}`);return r.json();
  });
 }
 if(stage==='prepare'){
  const issued=JSON.parse(await readFile('.local/operational-commercial-demo-v1/issued-base.json','utf8'));
  const policy=(await get('/api/v1/policies/'+issued.policyId)).data;
  const subject=await command('subject','/api/v1/operational-subjects',{kind:'policy',parentId:policy.id},'/api/v1/policies/'+policy.id);
  const originals=(await get(`/api/v1/records/${subject.id}/documents`)).data.items.filter(x=>x.visibility==='internal').map(x=>x.currentVersion).filter(x=>x?.state==='ready'&&x.sourceVersionId===issued.versionId&&x.contentType==='application/pdf');
  assert.ok(originals.length,'Wait for the existing issued policy PDFs; do not create substitutes.');
  const documents=[];
  for(const original of originals){
   // Original requests are internal. Explicitly generate agency-visible copies
   // from the same immutable source/template; never broaden existing visibility.
   const generated=await command('agency-copy:'+original.id,`/api/v1/records/${subject.id}/documents/generate`,{kind:original.kind,source:{kind:'policy-version',policyVersionId:issued.versionId},templateVersionId:original.templateVersionId,visibility:'agency',relationshipId:policy.relationshipId,reason:'Prepare fictional agency policy pack from the exact issued source and template'},'/api/v1/policies/'+policy.id);
   let ready;
   for(let n=0;n<120;n++){ready=(await get('/api/v1/document-versions/'+generated.id)).data;if(ready.state==='ready')break;assert.equal(ready.state,'pending');await page.waitForTimeout(500);}
   assert.equal(ready.state,'ready');assert.equal(ready.sourceHash,original.sourceHash);assert.equal(ready.templateHash,original.templateHash);documents.push(ready);
  }
  const recipients=(await get(`/api/v1/records/${subject.id}/document-delivery-recipients/${policy.relationshipId}`)).data.items;
  assert.ok(recipients.length,'An eligible actual demo relationship contact is required.');
  const fixture={policyId:policy.id,subjectId:subject.id,sourceVersionId:issued.versionId,documents,recipientId:recipients[0].id};
  try{const existing=JSON.parse(await readFile(directory+'/agency-fixture.json','utf8'));assert.deepEqual(existing,fixture);}
  catch(error){if(error.code!=='ENOENT')throw error;await writeFile(directory+'/agency-fixture.json',JSON.stringify(fixture,null,2),{flag:'wx'});}
  console.log('Exact ready policy PDFs and eligible recipient retained for the fictional retry pack.');
 }else{
  const f=JSON.parse(await readFile(directory+'/agency-fixture.json','utf8')),scope=`/api/v1/records/${f.subjectId}/documents`;
  await get(scope);
  if(stage==='queue'){
   const result=await command('agency-pack',`/api/v1/records/${f.subjectId}/document-deliveries`,{documentVersionIds:f.documents.map(x=>x.id),recipientContactIds:[f.recipientId],subject:'Fictional operational retry demonstration pack',body:'Fictional policy pack demonstrating persistent delivery failure and retry of the exact selected agency PDF versions.'},scope);
   await writeFile(directory+'/work.json',JSON.stringify(result,null,2));
  }
  const work=JSON.parse(await readFile(directory+'/work.json','utf8'));
  const deliveries=(await get(`/api/v1/records/${f.subjectId}/document-deliveries`)).data.items;
  const delivery=deliveries.find(x=>x.jobId===work.id||x.workId===work.id);assert.ok(delivery,'The original pack must remain associated with its work.');
  const route='/api/v1/document-deliveries/'+delivery.id;
  if(stage==='retry')await command('retry',route+'/retry',{reason:'Retry the exact fictional policy pack after the original transient failure budget was exhausted'},route,true);
  const saved=(await get(route)).data;
  await writeFile(directory+'/readback.json',JSON.stringify({checkedAt:new Date().toISOString(),fixture:f,workId:work.id,delivery:saved},null,2));
  console.log(`Original pack readback saved; stage=${stage}.`);
 }
}finally{await browser?.close();await lock.close();await unlink(directory+'/running.lock');}
