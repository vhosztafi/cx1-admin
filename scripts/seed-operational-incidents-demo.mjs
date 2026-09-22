import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir,open,unlink} from 'node:fs/promises';
import {resolve,sep} from 'node:path';
import {chromium} from 'playwright';
import {openDemoJournal} from './demo-command-journal.mjs';

// Explicit fictional local scenarios, through normal scoped APIs. Preserve the
// journal: completed commands are never repeated with new keys or new bodies.
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';
assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const directory=process.env.COVER_OPERATIONAL_DEMO_DIRECTORY??'.local/operational-incidents-demo-v1';
assert.ok(resolve(directory).startsWith(resolve('.local')+sep));
const fixtures=JSON.parse(await readFile(process.argv[2],'utf8'));
assert.equal(fixtures.length,2);assert.equal(new Set(fixtures.map(x=>x.policyId)).size,2);
for(const fixture of fixtures){assert.match(fixture.policyId,/^[a-f0-9-]{36}$/i);assert.match(fixture.occurredOn,/^\d{4}-\d{2}-\d{2}$/);}
await mkdir(directory,{recursive:true});const lock=await open(directory+'/running.lock','wx');let browser;
try{
 await lock.writeFile(String(process.pid));
 try{assert.deepEqual(JSON.parse(await readFile(directory+'/fixtures.json','utf8')),fixtures);}
 catch(error){if(error.code!=='ENOENT')throw error;await writeFile(directory+'/fixtures.json',JSON.stringify(fixtures,null,2),{flag:'wx'});}
 const journal=await openDemoJournal(directory+'/commands.json',origin);
 browser=await chromium.launch({headless:true});const page=await browser.newPage({viewport:{width:1440,height:1000}});page.setDefaultTimeout(30000);
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(origin+'/login');await page.getByLabel('Email address',{exact:true}).fill('senior-underwriter@cover.example');
 await page.getByLabel('Password',{exact:true}).fill((await readFile('.local/demo-password.txt','utf8')).trim());
 await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');
 async function get(path){const r=await page.request.get(origin+path);assert.equal(r.status(),200,`${path}: ${r.status()}`);return{data:await r.json(),etag:r.headers().etag};}
 async function command(name,path,data,scope){
  const current=await get(scope);
  return journal.command(name,async()=>({path,data,etag:scope.includes('/incidents/')?current.etag:undefined}),async(request,key)=>{
   const csrf=(await get('/api/v1/auth/csrf')).data.requestToken;
   const r=await page.request.post(origin+request.path,{headers:{'X-CSRF-Token':csrf,'Idempotency-Key':key,...(request.etag?{'If-Match':request.etag}:{})},data:request.data});
   assert.ok(r.ok(),`${request.path}: ${r.status()} ${await r.text()}`);return r.json();
  });
 }
 async function until(path,predicate){for(let n=0;n<120;n++){const value=(await get(path)).data;if(predicate(value))return value;await page.waitForTimeout(500);}throw Error('Pending retained operation: '+path);}
 const reports=[];
 for(const f of fixtures){
  const policy=(await get('/api/v1/policies/'+f.policyId)).data;
  const cc=policy.snapshot.productCode==='commercial-combined',prefix=cc?'commercial':'motor';
  const draft={policyId:f.policyId,productCode:policy.snapshot.productCode,occurrence:{occurredOn:f.occurredOn,timeZone:'Europe/London',precision:'exact',occurredAt:f.occurredOn+'T12:00:00+01:00'},
   kind:'property-damage',thirdPartyInvolvement:'no',reportedBy:'Fictional operational demonstration reporter',reportingRoute:'agency',bestContactDescription:'01632 960002',
   description:`Fictional ${prefix} operational demonstration: reported property damage for historical cover and administrator handoff.`,
   ...(cc?{commercialSubject:{kind:'property',locationId:policy.snapshot.risk.locations[0].id,coverCode:'buildings',owner:'insured',estimatedValueAtRisk:'2500.00'}}:{motorSubject:{kind:'third-party-only',itemDescription:'Fictional customer boundary wall'}})};
  const created=await command(prefix+':create','/api/v1/incidents',draft,'/api/v1/policies/'+f.policyId),route='/api/v1/incidents/'+created.id;
  const logged=await command(prefix+':log',route+'/log',{},route);assert.equal(logged.resolution.state,'resolved');assert.deepEqual(logged.missing,[]);
  const administrators=(await get(route+'/administrators')).data.items;assert.ok(administrators.length);
  await command(prefix+':handoff',route+'/handoff',{revisionId:logged.revisionId,resolutionId:logged.resolution.id,providerId:administrators[0].id},route);
  await until(route,x=>x.state==='handed-off');
  await command(prefix+':refresh',route+'/refresh',{},route);
  const summaries=await until(route+'/summaries',x=>x.items.length>=2);
  const final=(await get(route)).data,handoffs=(await get(route+'/handoffs')).data;
  assert.equal(handoffs.items.length,1);assert.equal(handoffs.items[0].state,'acknowledged');
  assert.equal(final.draft.policyId,f.policyId);assert.equal(final.resolution.state,'resolved');
  assert.ok(summaries.items.every(x=>x.paid===null&&x.reserved===null));
  await page.goto(origin+'/policies/'+f.policyId);await page.getByRole('tab',{name:'Claims',exact:true}).click();
  await page.getByRole('button',{name:final.reference,exact:true}).click();
  await page.getByRole('tab',{name:'Summary from administrator',exact:true}).click();
  await page.getByRole('heading',{name:/CLM-DEMO-.*Open/}).waitFor();
  await page.getByRole('tabpanel',{name:'Administrator summaries'}).scrollIntoViewIfNeeded();
  await page.screenshot({path:directory+`/${prefix}-desktop.png`});await page.setViewportSize({width:390,height:844});
  await page.getByRole('tabpanel',{name:'Administrator summaries'}).scrollIntoViewIfNeeded();
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),true);
  await page.screenshot({path:directory+`/${prefix}-mobile.png`});await page.setViewportSize({width:1440,height:1000});
  reports.push({policyId:f.policyId,reference:final.reference,incidentId:final.id,productCode:final.draft.productCode,revisionId:final.revisionId,resolution:final.resolution,handoffId:handoffs.items[0].id,providerReference:final.providerReference,summaryIds:summaries.items.map(x=>x.id)});
 }
 assert.deepEqual(errors,[]);await writeFile(directory+'/report.json',JSON.stringify({passed:true,checkedAt:new Date().toISOString(),reports,errors},null,2));
 console.log('Two retained historical incidents, acknowledged handoffs, append-only summaries and browser readbacks verified.');
}finally{await browser?.close();await lock.close();await unlink(directory+'/running.lock');}
