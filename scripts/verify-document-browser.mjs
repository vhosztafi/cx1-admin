import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {chromium} from 'playwright';

const sources=['scripts/verify-document-browser.mjs','backend/tests/BackOffice.IntegrationTests/OperationalDocumentBrowserTests.cs','apps/backoffice/app/globals.css','apps/backoffice/lib/documents-api.ts',...['document-list','document-preview','document-shared','document-command','document-generate','document-upload'].map(x=>`apps/backoffice/components/operations/${x}.tsx`),...['policy-record','commercial-policy-record'].map(x=>`apps/backoffice/components/policies/${x}.tsx`)];
const hash=createHash('sha256');for(const path of sources){hash.update(path);hash.update(await readFile(path));}const sourceHash=hash.digest('hex');
if(!process.argv.includes('--worker')){
 const latest=JSON.parse(await readFile('.local/phase9-08-browser/latest.json','utf8'));
 const report=JSON.parse(await readFile(latest.output+'/browser-report.json','utf8')),sql=JSON.parse(await readFile(latest.output+'/sql-readback.json','utf8'));
 assert.equal(latest.passed,true);assert.equal(sql.passed,true);assert.equal(report.sourceHash,sourceHash);assert.ok(report.cases.length>=12);assert.deepEqual(report.errors,[]);
 console.log(`Document policy browser: ${report.cases.length} checks with SQL readback. ${latest.output}`);
}else{
 const f=JSON.parse(process.env.COVER_DOCUMENT_BROWSER_FIXTURE);
 assert.equal(new URL(f.apiOrigin).hostname,'127.0.0.1');assert.notEqual(new URL(f.apiOrigin).port,'5000');assert.equal(new URL(f.webOrigin).hostname,'127.0.0.1');
 const browser=await chromium.launch({channel:'chrome',headless:true}),page=await browser.newPage({viewport:{width:1480,height:980}});page.setDefaultTimeout(30000);
 const errors=[],cases=[],commands=[];let loseGeneration=true;
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/api/v1/**',async route=>{
  const request=route.request(),url=new URL(request.url()),response=await route.fetch({url:f.apiOrigin+url.pathname+url.search});
  if(url.pathname.endsWith('/documents/generate')&&request.method()==='POST'){
   commands.push({body:request.postData(),key:request.headers()['idempotency-key'],status:response.status()});
   if(loseGeneration&&response.status()===202){loseGeneration=false;await route.abort('failed');return;}
  }
  await route.fulfill({response});
 });
 const button=name=>page.getByRole('button',{name,exact:true}),row=id=>page.locator(`[data-document-id="${id}"]`);
 async function api(path){return page.evaluate(async path=>{const r=await fetch('/api/v1'+path);return{status:r.status,body:await r.json()};},path);}
 async function ready(id){for(let n=0;n<100;n++){const r=await api('/document-versions/'+id);assert.equal(r.status,200);if(r.body.state==='ready')return r.body;assert.equal(r.body.state,'pending');await page.waitForTimeout(300);}throw new Error('Document did not become ready');}
 async function bytes(id){return page.evaluate(async id=>{const r=await fetch('/api/v1/document-versions/'+id+'/content');return{status:r.status,bytes:Array.from(new Uint8Array(await r.arrayBuffer()))};},id);}
 async function confirm(name,path){const response=page.waitForResponse(r=>r.request().method()==='POST'&&new URL(r.url()).pathname.endsWith(path));await page.getByRole('dialog').last().getByRole('button',{name,exact:true}).click();const result=await response;assert.equal(result.status(),202);const body=await result.json();await page.getByRole('dialog').waitFor({state:'hidden'});return body;}
 async function chooseSchedule(reason){const select=page.getByLabel('Document and template',{exact:true});await select.waitFor();const value=await select.locator('option').evaluateAll(xs=>xs.find(x=>x.value.startsWith('policy-schedule:'))?.value);assert.ok(value);await select.selectOption(value);await page.getByLabel('Reason',{exact:true}).fill(reason);await button('Review generation').click();}
 async function upload(reason,content){await page.getByLabel('File (PDF, PNG or JPEG; up to 20 MiB)',{exact:true}).setInputFiles({name:'fictional-evidence.png',mimeType:'image/png',buffer:content});await page.getByLabel('Reason',{exact:true}).fill(reason);await button('Review upload').click();return confirm(reason==='Browser original evidence'?'Upload evidence':'Save new file version','/documents/upload');}
 try{
  await page.goto(f.webOrigin+'/login');await page.getByLabel('Email address',{exact:true}).fill('underwriter@cover.example');await page.getByLabel('Password',{exact:true}).fill(process.env.COVER_DOCUMENT_BROWSER_PASSWORD);await button('Sign in').click();await page.waitForURL(f.webOrigin+'/');
  const policyUrl=f.webOrigin+`/policies/${f.policyId}?termId=${f.termId}&versionId=${f.versionId}&tab=Documents`;
  await page.goto(policyUrl);await button('Generate document').waitFor();await button('Generate document').click();await chooseSchedule('Browser historical schedule');
  await page.getByRole('dialog').getByRole('button',{name:'Generate document',exact:true}).click();await button('Retry same action').waitFor();
  await page.keyboard.press('Escape');assert.equal(await page.getByRole('dialog').count(),1);
  const generated=await confirm('Retry same action','/documents/generate');assert.equal(commands.length,2);assert.equal(commands[0].body,commands[1].body);assert.equal(commands[0].key,commands[1].key);cases.push('lost response retries identical command and guards leaving');
  assert.equal(generated.sourceVersionId,f.versionId);const original=await ready(generated.id),originalFile=await bytes(original.id);assert.equal(originalFile.status,200);assert.ok(Buffer.from(originalFile.bytes).subarray(0,5).equals(Buffer.from('%PDF-')));assert.equal(createHash('sha256').update(Buffer.from(originalFile.bytes)).digest('hex'),original.sha256);await writeFile(f.output+'/generated-schedule.pdf',Buffer.from(originalFile.bytes));cases.push('actual PDF bytes match stored hash and exact source');
  await button('Refresh documents').click();await row(original.documentId).getByRole('button',{name:'Preview v1',exact:true}).waitFor();await row(original.documentId).getByRole('button',{name:'Preview v1',exact:true}).click();await page.getByRole('dialog').locator('iframe').waitFor();assert.ok((await page.getByRole('dialog').locator('iframe').getAttribute('src')).includes(original.id+'/preview'));await page.keyboard.press('Escape');assert.equal(await row(original.documentId).getByRole('button',{name:'Preview v1',exact:true}).evaluate(x=>document.activeElement===x),true);cases.push('exact authenticated preview restores trigger focus');
  await row(original.documentId).getByRole('button',{name:'New version',exact:true}).click();await chooseSchedule('Browser replacement schedule');const replacement=await confirm('Generate document','/documents/generate');await ready(replacement.id);assert.equal(replacement.documentId,original.documentId);assert.equal(replacement.number,2);assert.deepEqual((await bytes(original.id)).bytes,originalFile.bytes);cases.push('new generated version leaves original PDF bytes stable');
  await button('Upload evidence').click();const png=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a1XcAAAAASUVORK5CYII=','base64');
  const evidence=await upload('Browser original evidence',png);await ready(evidence.id);assert.deepEqual(Buffer.from((await bytes(evidence.id)).bytes),png);cases.push('actual uploaded evidence round trips unchanged');
  await button('Refresh documents').click();await row(evidence.documentId).getByRole('button',{name:'New version',exact:true}).click();
  const png2=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=','base64');const changedEvidence=await upload('Browser replacement evidence',png2);await ready(changedEvidence.id);assert.equal(changedEvidence.documentId,evidence.documentId);assert.equal(changedEvidence.number,2);assert.deepEqual(Buffer.from((await bytes(evidence.id)).bytes),png);assert.deepEqual(Buffer.from((await bytes(changedEvidence.id)).bytes),png2);cases.push('replacement evidence appends version and retains old bytes');
  await page.reload();await row(original.documentId).getByRole('button',{name:'Version history',exact:true}).click();const history=page.getByRole('region',{name:'Document version history'});await history.getByRole('heading',{name:'File version 1',exact:true}).waitFor();await history.getByRole('heading',{name:'File version 2',exact:true}).waitFor();cases.push('history survives reload with both immutable versions');
  assert.equal((await api('/document-versions/aaaaaaaa-0000-4000-8000-000000000001')).status,404);cases.push('foreign metadata is unavailable');
  await page.screenshot({path:f.output+'/documents-desktop.png',fullPage:true});cases.push('desktop document list and history captured');
  await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.screenshot({path:f.output+'/documents-mobile.png',fullPage:true});cases.push('390px list and history fit viewport');
  await history.getByRole('button',{name:'Preview v1',exact:true}).click();await page.getByRole('dialog').locator('iframe').waitFor();assert.equal(await page.getByRole('dialog').evaluate(x=>x.getBoundingClientRect().width<=innerWidth),true);await page.screenshot({path:f.output+'/preview-mobile.png',fullPage:true});await button('Close preview').click();cases.push('390px exact-version preview and close');
  const head=(await api('/documents/'+original.documentId+'/versions')).body;assert.equal(head.items.length,2);assert.equal(head.items[1].sha256,original.sha256);cases.push('saved API history readback agrees with downloaded original');assert.deepEqual(errors,[]);
  await writeFile(f.output+'/browser-report.json',JSON.stringify({sourceHash,cases,errors,generatedId:original.id,replacementId:replacement.id,evidenceId:evidence.id},null,2));
 }catch(error){await page.screenshot({path:f.output+'/failure.png',fullPage:true}).catch(()=>{});await writeFile(f.output+'/browser-failure.json',JSON.stringify({sourceHash,cases,errors,error:String(error)},null,2));throw error;}
 finally{await browser.close();}
}
