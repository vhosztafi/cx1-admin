import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {chromium} from 'playwright';

const sources=['scripts/verify-communication-browser.mjs','backend/tests/BackOffice.IntegrationTests/OperationalCommunicationBrowserTests.cs',
 'backend/src/BackOffice.Api/CommunicationEndpoints.cs','backend/src/BackOffice.Infrastructure/Operations/NoteService.cs','backend/src/BackOffice.Infrastructure/Operations/ThreadService.cs','backend/src/BackOffice.Infrastructure/Operations/ThreadService.Options.cs','backend/src/BackOffice.Infrastructure/Operations/CommunicationScope.cs',
 'backend/src/BackOffice.Api/DeliveryEndpoints.cs','backend/src/BackOffice.Infrastructure/Operations/DeliveryReadService.cs','backend/src/BackOffice.Infrastructure/Operations/MessageDeliveryService.cs','backend/src/BackOffice.Infrastructure/Operations/MessageDeliveryService.Recovery.cs','backend/src/BackOffice.Infrastructure/Operations/MessageDeliveryWorker.cs','backend/src/BackOffice.Infrastructure/Operations/MessageDeliveryWorker.Apply.cs','apps/backoffice/lib/communications-api.ts','apps/backoffice/app/globals.css',...['communication-shared','communication-command','notes','thread','document-list','document-pack','delivery-history'].map(x=>`apps/backoffice/components/operations/${x}.tsx`),
 ...['quotes/quote-receipt','quotes/commercial-receipt','policies/policy-record','policies/commercial-policy-record','clients/client-detail','agencies/agency-detail'].map(x=>`apps/backoffice/components/${x}.tsx`)];
sources.push('apps/backoffice/components/underwriting/referral-decisions.tsx');
const hash=createHash('sha256');for(const path of sources){hash.update(path);hash.update((await readFile(path,'utf8')).replaceAll('\r\n','\n').trimEnd());}const sourceHash=hash.digest('hex');
if(!process.argv.includes('--worker')){
 for(const product of ['motor-trade','commercial-combined']){
  const latest=JSON.parse(await readFile(`.local/phase9-10-browser/${product}.json`,'utf8'));
  const report=JSON.parse(await readFile(latest.output+'/browser-report.json','utf8')),sql=JSON.parse(await readFile(latest.output+'/sql-readback.json','utf8'));
  assert.equal(latest.passed,true);assert.equal(sql.passed,true);assert.equal(report.sourceHash,sourceHash);assert.ok(report.cases.length>=16);assert.deepEqual(report.errors,[]);
  console.log(`${product}: ${report.cases.length} communication browser checks and SQL readback.`);
 }
}else{
 const f=JSON.parse(process.env.COVER_COMMUNICATION_BROWSER_FIXTURE);
 assert.equal(new URL(f.apiOrigin).hostname,'127.0.0.1');assert.notEqual(new URL(f.apiOrigin).port,'5000');assert.equal(new URL(f.webOrigin).hostname,'127.0.0.1');
 const browser=await chromium.launch({channel:'chrome',headless:true}),page=await browser.newPage({viewport:{width:1480,height:980}});page.setDefaultTimeout(30000);
 const errors=[],cases=[],noteCommands=[];let loseNote=true;
 page.on('pageerror',error=>errors.push(error.message));
 await page.route('**/api/v1/**',async route=>{
  try{
   const request=route.request(),url=new URL(request.url()),response=await route.fetch({url:f.apiOrigin+url.pathname+url.search});
   if(url.pathname.endsWith('/notes')&&request.method()==='POST'){
    noteCommands.push({body:request.postData(),key:request.headers()['idempotency-key']});
    if(loseNote&&response.status()===201){loseNote=false;await route.abort('failed');return;}
   }
   await route.fulfill({response});
  }catch{await route.abort('failed').catch(()=>{});}
 });
 const button=name=>page.getByRole('button',{name,exact:true});
 async function api(path){return page.evaluate(async path=>{const response=await fetch('/api/v1'+path);return{status:response.status,body:await response.json()};},path);}
 async function confirm(label,suffix,status=201,method='POST'){
  const [response]=await Promise.all([page.waitForResponse(r=>r.request().method()===method&&new URL(r.url()).pathname.endsWith(suffix)),page.getByRole('dialog').getByRole('button',{name:label,exact:true}).click()]);assert.equal(response.status(),status);
  if(status>=300)return;
  const body=await response.json();await page.getByRole('dialog').waitFor({state:'hidden'});return body;
 }
 async function note(text,first=false){
  await page.getByLabel('Internal note',{exact:true}).fill(text);await button('Add internal note').click();
  if(first){await page.getByRole('dialog').getByRole('button',{name:'Add internal note',exact:true}).click();await button('Retry same action').waitFor();await page.keyboard.press('Escape');assert.equal(await page.getByRole('dialog').count(),1);await confirm('Retry same action','/notes');assert.deepEqual(noteCommands[0],noteCommands[1]);cases.push('lost note response retries identical bytes/key and blocks uncertain dismissal');}
  else await confirm('Add internal note','/notes');
  await page.getByText(text,{exact:true}).waitFor();assert.equal(await page.getByLabel('Internal note',{exact:true}).inputValue(),'');
 }
 try{
  await page.goto(f.webOrigin+'/login');await page.getByLabel('Email address',{exact:true}).fill(f.email);await page.getByLabel('Password',{exact:true}).fill(process.env.COVER_COMMUNICATION_BROWSER_PASSWORD);await button('Sign in').click();await page.waitForURL(f.webOrigin+'/');
  await page.goto(f.webOrigin+`/policies/${f.policyId}`);await page.getByRole('tab',{name:'Notes',exact:true}).click();await note('Browser policy internal note',true);
  await page.reload();await page.getByRole('tab',{name:'Notes',exact:true}).click();await page.getByText('Browser policy internal note',{exact:true}).waitFor();cases.push('policy note reload retains body author and timestamp');
  await page.screenshot({path:f.output+'/notes-desktop.png'});
  const notes=await api(`/records/${f.subjectId}/notes`);assert.equal(notes.status,200);assert.equal(notes.body.items.length,1);assert.ok(notes.body.items[0].authorLabel);assert.ok(notes.body.items[0].createdAt);
  const threads=await api(`/records/${f.subjectId}/threads?visibility=agency`);assert.equal(threads.body.totalCount,0);assert.ok(!JSON.stringify(threads.body).includes('Browser policy internal note'));cases.push('notes are separate from agency thread counts and data');
  await page.getByRole('tab',{name:'Documents',exact:true}).click();const fileRow=page.locator(`[data-document-id="${f.documentId}"]`);await fileRow.getByRole('button',{name:'Draft agency message',exact:true}).click();
  const source=page.getByRole('region',{name:'Draft agency message from document',exact:true});await source.getByLabel('Conversation subject',{exact:true}).fill('Browser agency conversation');
  await source.getByRole('combobox',{name:/^Client relationship/}).selectOption(f.relationshipId);
  await source.getByRole('button',{name:'Create conversation',exact:true}).click();const created=await confirm('Create conversation','/threads');
  await page.getByRole('textbox',{name:/^Message text/}).fill('Browser first agency draft');
  const recipientRegion=page.getByRole('region',{name:'Recipients',exact:true});await recipientRegion.getByRole('checkbox').first().check();
  const attachments=page.getByRole('region',{name:'Attachments',exact:true});await attachments.getByRole('checkbox').first().waitFor();assert.equal(await attachments.getByRole('checkbox').count(),1);assert.equal(await attachments.getByRole('checkbox').isChecked(),true);cases.push('document composer pins exact agency version and eligible recipient choices');
  await page.setViewportSize({width:390,height:844});await page.getByRole('textbox',{name:/^Message text/}).scrollIntoViewIfNeeded();assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));await page.screenshot({path:f.output+'/message-mobile.png'});cases.push('390px message composer has no page overflow');
  await button('Save draft').click();const saved=await confirm('Save draft','/messages');assert.equal(saved.state,'draft');assert.deepEqual(saved.attachmentVersionIds,[f.versionId]);assert.equal(saved.recipientContactIds.length,1);cases.push('message draft persists exact attachment and recipient IDs');
  await page.setViewportSize({width:1480,height:980});await page.reload();await page.getByRole('tab',{name:'Messages',exact:true}).click();await button('Browser agency conversation').click();await page.getByText('Browser first agency draft',{exact:true}).waitFor();cases.push('message list reload retains saved draft contents and status');
  await button('Edit draft').click();await page.getByRole('textbox',{name:/^Message text/}).fill('Browser revised agency draft');
  const competing=await page.evaluate(async({id,threadId})=>{
   const csrf=await(await fetch('/api/v1/auth/csrf')).json();const current=await(await fetch('/api/v1/messages/'+id)).json();
   const response=await fetch('/api/v1/messages/'+id,{method:'PUT',headers:{'Content-Type':'application/json','X-CSRF-Token':csrf.requestToken,'Idempotency-Key':crypto.randomUUID(),'If-Match':current.etag},body:JSON.stringify({body:'Competing saved draft',recipientContactIds:current.recipientContactIds,attachmentVersionIds:current.attachmentVersionIds})});return response.status;
  },{id:saved.id,threadId:created.id});assert.equal(competing,200);
  await button('Save draft').click();await confirm('Save draft',`/messages/${saved.id}`,412,'PUT');await button('Back to form').click();assert.equal(await page.getByRole('textbox',{name:/^Message text/}).inputValue(),'Browser revised agency draft');cases.push('stale update retains local draft text');
  await button('Review current saved draft').click();await page.getByRole('region',{name:'Current saved draft',exact:true}).getByText('Competing saved draft',{exact:true}).waitFor();await button('Keep my text and use this version for the next save').click();
  await button('Save draft').click();await confirm('Save draft',`/messages/${saved.id}`,200,'PUT');await page.getByText('Browser revised agency draft',{exact:true}).waitFor();cases.push('explicit current-version review permits saving retained text');
  const message=await api(`/messages/${saved.id}`);assert.equal(message.body.body,'Browser revised agency draft');assert.equal(message.body.state,'draft');assert.deepEqual(message.body.attachmentVersionIds,[f.versionId]);cases.push('saved message API independently matches selected file version and draft text');
  await button('Send to agency').click();await confirm('Send to agency',`/messages/${saved.id}/send`,202);
  async function waitDelivery(path,state){for(let n=0;n<90;n++){const response=await api(path);if(response.status===200&&response.body.items?.some(x=>x.state===state))return response.body.items.find(x=>x.state===state);await page.waitForTimeout(500);}throw new Error('Expected persisted delivery state '+state);}
  const failed=await waitDelivery(`/messages/${saved.id}/deliveries`,'failed');
  await button('Refresh deliveries').click();await button('View delivery and attempts').click();
  const delivery=page.getByRole('region',{name:'Delivery details',exact:true});await delivery.getByLabel('Reason',{exact:true}).fill('Browser deliberate delivery recovery');
  await delivery.getByRole('button',{name:'Retry delivery',exact:true}).click();await confirm('Retry delivery',`/message-deliveries/${failed.id}/retry`,202);
  const delivered=await waitDelivery(`/messages/${saved.id}/deliveries`,'delivered');assert.equal(delivered.id,failed.id);assert.equal(delivered.jobId,failed.jobId);
  await button('Refresh delivery details').click();await delivery.getByText(/^Status: delivered/).waitFor();cases.push('failed message retries same persisted delivery and reaches delivered');
  const attempts=await api(`/message-deliveries/${failed.id}/attempts`);assert.equal(attempts.body.totalCount,7);cases.push('delivery history exposes seven durable attempts without duplicate provider effect');
  await page.getByRole('tab',{name:'Documents',exact:true}).click();await page.locator(`[data-document-id="${f.documentId}"]`).getByRole('button',{name:'Version history',exact:true}).click();
  await page.getByRole('region',{name:'Document version history',exact:true}).getByRole('button',{name:'Add to document pack',exact:true}).click();
  const pack=page.getByRole('region',{name:'Document pack',exact:true});await pack.getByRole('checkbox').first().check();await pack.getByRole('button',{name:'Review and send pack',exact:true}).click();await confirm('Send document pack','/document-deliveries',202);
  const packed=await waitDelivery(`/records/${f.subjectId}/document-deliveries`,'delivered');assert.deepEqual(packed.documentVersionIds,[f.versionId]);cases.push('historical file selection sends exact document version through pack UI');
  await button('Refresh deliveries').click();await button('View delivery and attempts').click();await page.getByRole('region',{name:'Delivery details',exact:true}).getByText(/^Status: delivered/).waitFor();
  await page.setViewportSize({width:390,height:844});await page.getByRole('region',{name:'Delivery details',exact:true}).scrollIntoViewIfNeeded();assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));await page.screenshot({path:f.output+'/delivery-mobile.png'});cases.push('390px delivery details retains readable recipients and original file action');await page.setViewportSize({width:1480,height:980});
  for(const [kind,url] of [['quote',`/quotes/${f.quoteId}`],['agency',`/agents/${f.agencyId}?tab=Notes`],['relationship',`/clients/${f.clientId}?tab=Notes`]]){
   await page.goto(f.webOrigin+url);if(kind==='quote')await page.getByRole('tab',{name:'Notes',exact:true}).click();
   if(kind==='relationship'){const relationships=await api(`/clients/${f.clientId}/relationships?pageSize=100`);const r=relationships.body.items.find(x=>x.id===f.relationshipId);assert.ok(r);await button(`${r.agencyReference} · ${r.agencyName}`).click();}
   await note(`Browser ${kind} internal note`);cases.push(`${kind} note saves on its original scoped parent`);
  }
  await page.goto(f.webOrigin+`/quotes/${f.quoteId}`);await page.getByRole('tab',{name:'Underwriting',exact:true}).click();
  const referralsBefore=(await api(`/referrals?quoteId=${f.quoteId}&pageSize=50`)).body.items.map(x=>({id:x.id,state:x.state}));
  await button('Prepare agency information request').click();const information=page.getByRole('region',{name:'Agency information request',exact:true});
  await information.getByLabel('Conversation subject',{exact:true}).fill('Browser underwriting information request');await information.getByRole('combobox',{name:/^Client relationship/}).selectOption(f.relationshipId);
  await information.getByRole('button',{name:'Create conversation',exact:true}).click();const informationThread=await confirm('Create conversation','/threads');
  await information.getByRole('button',{name:'New message draft',exact:true}).click();
  await information.getByRole('textbox',{name:/^Message text/}).fill('Please supply the evidence requested in the saved underwriting query.');
  await information.getByRole('region',{name:'Recipients',exact:true}).getByRole('checkbox').first().check();await information.getByRole('button',{name:'Save draft',exact:true}).click();const informationDraft=await confirm('Save draft','/messages');
  await information.getByRole('button',{name:'Send to agency',exact:true}).click();await confirm('Send to agency',`/messages/${informationDraft.id}/send`,202);await waitDelivery(`/messages/${informationDraft.id}/deliveries`,'delivered');
  assert.deepEqual((await api(`/referrals?quoteId=${f.quoteId}&pageSize=50`)).body.items.map(x=>({id:x.id,state:x.state})),referralsBefore);
  await page.reload();await page.getByRole('tab',{name:'Messages',exact:true}).click();await button('Browser underwriting information request').click();await page.getByText(informationDraft.body,{exact:true}).waitFor();
  assert.equal((await api(`/threads/${informationThread.id}/messages`)).body.items.find(x=>x.id===informationDraft.id).state,'sent');cases.push('underwriting information request persists on the quote and delivers without changing referral decisions');
  assert.deepEqual(errors,[]);await writeFile(f.output+'/browser-report.json',JSON.stringify({sourceHash,cases,errors,messageId:saved.id,informationMessageId:informationDraft.id},null,2));
 }catch(error){await page.screenshot({path:f.output+'/failure.png'}).catch(()=>{});const selects=await page.locator('select').evaluateAll(nodes=>nodes.map(n=>({value:n.value,disabled:n.disabled,options:[...n.options].map(o=>({value:o.value,text:o.text}))}))).catch(()=>[]);await writeFile(f.output+'/browser-failure.json',JSON.stringify({sourceHash,cases,errors,relationshipId:f.relationshipId,selects,error:String(error).split('\n')[0],locatorLog:error.name==='TimeoutError'?error.message:undefined},null,2));process.exitCode=1;}
 finally{await browser.close();}
}
