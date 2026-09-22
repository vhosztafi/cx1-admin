import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir,open,unlink} from 'node:fs/promises';
import {chromium} from 'playwright';
import {openDemoJournal} from './demo-command-journal.mjs';

// Fictional local correspondence only. Preserve the journal to resume exact
// requests; never reopen a response that a demonstrator has already closed.
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';
assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const directory='.local/agency-response-demo-v1';await mkdir(directory,{recursive:true});
const fixtures=JSON.parse(await readFile('.local/operational-incidents-demo-v1/fixtures.json','utf8'));
assert.equal(fixtures.length,2);
const lock=await open(directory+'/running.lock','wx');let browser;
try{
 await lock.writeFile(String(process.pid));const journal=await openDemoJournal(directory+'/commands.json',origin);
 browser=await chromium.launch({headless:true});const page=await browser.newPage({viewport:{width:1480,height:980}});page.setDefaultTimeout(30000);
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(origin+'/login');await page.getByLabel('Email address',{exact:true}).fill('senior-underwriter@cover.example');
 await page.getByLabel('Password',{exact:true}).fill((await readFile('.local/demo-password.txt','utf8')).trim());
 await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');
 async function get(path){const response=await page.request.get(origin+path);assert.equal(response.status(),200,`${path}: ${response.status()}`);return response.json();}
 async function command(name,path,data,scope,etag){
  await get(scope);
  return journal.command(name,async()=>({path,data,...(etag?{etag}:{})}),async(request,key)=>{
   const csrf=await get('/api/v1/auth/csrf');
   const response=await page.request.post(origin+request.path,{data:request.data,headers:{'X-CSRF-Token':csrf.requestToken,'Idempotency-Key':key,...(request.etag?{'If-Match':request.etag}:{})}});
   assert.ok(response.ok(),`${request.path}: ${response.status()}`);return response.json();
  });
 }
 const rows=[];
 for(const fixture of fixtures){
  const scope='/api/v1/policies/'+fixture.policyId,policy=await get(scope),prefix=policy.id;
  const subject=await command(prefix+':subject','/api/v1/operational-subjects',{kind:'policy',parentId:policy.id},scope);
  const thread=await command(prefix+':thread',`/api/v1/records/${subject.id}/threads`,{visibility:'agency',relationshipId:policy.relationshipId,subject:`Fictional response demonstration · ${policy.reference}`},scope);
  const recipients=(await get(`/api/v1/threads/${thread.id}/recipient-options`)).items;assert.ok(recipients.length,'A current demo contact is required.');
  const draft=await command(prefix+':draft',`/api/v1/threads/${thread.id}/messages`,{body:`Fictional demonstration: please confirm the main agency contact for ${policy.reference}. This request does not change policy cover.`,recipientContactIds:[recipients[0].id],attachmentVersionIds:[]},scope);
  await command(prefix+':send',`/api/v1/messages/${draft.id}/send`,{},scope,draft.etag);
  let delivered=false;
  for(let attempt=0;attempt<120;attempt++){
   const history=await get(`/api/v1/messages/${draft.id}/deliveries`);
   if(history.items.some(x=>x.state==='delivered')){delivered=true;break;}
   assert.ok(!history.items.some(x=>x.state==='failed'),'The saved demo delivery failed; retain its journal and resolve the actual failure before tracking.');
   await page.waitForTimeout(500);
  }
  assert.ok(delivered,'Delivery remains pending; resume the same journal after the worker completes.');
  const tracked=await command(prefix+':track',`/api/v1/messages/${draft.id}/agency-response`,{reason:'Track the fictional business demonstration contact request'},scope);
  const current=await get(`/api/v1/messages/${draft.id}/agency-response`);assert.equal(current.id,tracked.id);assert.equal(current.instruction,draft.body);
  const shared=await get(`/api/v1/agencies/${policy.agencyId}/sharing/open-items?q=${encodeURIComponent(current.reference)}`);
  assert.equal(shared.totalCount,current.state==='awaiting-response'?1:0);
  await page.goto(origin+`/agents/${policy.agencyId}/sharing`);await page.getByLabel('Search shared open items',{exact:true}).fill(current.reference);
  await page.getByRole('button',{name:'Search open items',exact:true}).click();
  if(current.state==='awaiting-response')await page.getByRole('table',{name:'Shared open items',exact:true}).getByText(current.reference,{exact:true}).waitFor();
  else await page.getByText('No shared open items match this search.',{exact:true}).waitFor();
  await page.screenshot({path:`${directory}/${policy.snapshot.productCode}.png`,fullPage:true});
  rows.push({policyId:policy.id,productCode:policy.snapshot.productCode,agencyId:policy.agencyId,messageId:draft.id,response:current,sharedCount:shared.totalCount});
 }
 assert.deepEqual(errors,[]);await writeFile(directory+'/report.json',JSON.stringify({passed:true,checkedAt:new Date().toISOString(),rows,errors},null,2));
 console.log('Two retained fictional agency response requests verified without altering cover.');
}finally{await browser?.close();await lock.close();await unlink(directory+'/running.lock');}
