import assert from 'node:assert/strict';
import {readFile,writeFile,readdir} from 'node:fs/promises';
import {chromium} from 'playwright';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const renewal=process.argv.includes('--renewal');
const output=renewal?'.local/browser-evidence/renewal-issue':'.local/browser-evidence/servicing-issue';
const report=JSON.parse(await readFile(output+'/report.json','utf8'));assert.equal(report.journeys.length,2);assert.ok(report.completedAt??report.finishedAt);
const api=JSON.parse(await readFile('contracts/openapi.json','utf8')),ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);ajv.addFormat('binary',true);
for(const name of ['policy','policy-draft','quote-draft','issued-policy','issued-servicing']) {
 const schema=JSON.parse(await readFile(`contracts/schemas/${name}.schema.json`,'utf8'));
 ajv.addSchema(schema,`https://contracts.cover-mga.example/schemas/${name}.schema.json`);
}
const root='https://contracts.cover-mga.example/api-schemas';
function rewrite(value){if(Array.isArray(value))return value.map(rewrite);if(!value||typeof value!=='object')return value;return Object.fromEntries(Object.entries(value).map(([k,v])=>[k,k==='$ref'?v.replace('#/components/schemas/',root+'#/$defs/'):rewrite(v)]));}
ajv.addSchema({$id:root,$defs:rewrite(api.components.schemas)});
function valid(schema,data){const check=ajv.getSchema(root+'#/$defs/'+schema);assert.ok(check(data),JSON.stringify(check.errors));}
const browser=await chromium.launch({channel:'chrome',headless:true}),page=await browser.newPage({viewport:{width:1560,height:1000}});const errors=[];page.on('pageerror',e=>errors.push(e.message));
try {
 await page.goto(origin+'/login');await page.getByLabel('Email address',{exact:true}).fill('underwriter@cover.example');await page.getByLabel('Password',{exact:true}).fill((await readFile('.local/demo-password.txt','utf8')).trim());await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');
 const results=[];
 for(const journey of report.journeys){
  const receipt=journey.receipt;valid('ServicingIssueResult',receipt);
  const response=await page.request.get(origin+`/api/v1/policies/${journey.policyId}/terms/${receipt.termId}/versions/${receipt.versionId}`);assert.equal(response.status(),200);assert.match(response.headers()['cache-control'],/no-store/);
  const data=await response.json();valid('ServicingPolicyView',data);if(journey.contentHash)assert.equal(data.contentHash,journey.contentHash);assert.equal(data.snapshot.provenance.servicingIssueDecisionId,receipt.decisionId);assert.equal(data.financials.journalId,receipt.journalId);assert.equal(data.financials.amountDue,receipt.netAmount);
  if(renewal){assert.equal(data.transactionSequence,1);assert.ok(data.termNumber>1);assert.equal(data.snapshot.premium.termPremium,journey.premium);}
  const draft=await page.request.get(origin+`/api/v1/drafts/${journey.draftId}`);assert.equal(draft.status(),200);const draftData=await draft.json();assert.equal(draftData.state,'issued');
  if(renewal){
   const lifecycle=await page.request.get(origin+`/api/v1/terms/${draftData.baseTermId}/renewal-lifecycle`);assert.equal(lifecycle.status(),200);const saved=await lifecycle.json();valid('RenewalLifecycleView',saved);assert.equal(saved.state,'issued');assert.equal(saved.canLapse,false);
   const documentsUrl=origin+`/api/v1/drafts/${journey.draftId}/terms/documents?pageSize=10`;
   const documentsResponse=await page.request.get(documentsUrl);assert.equal(documentsResponse.status(),200);assert.match(documentsResponse.headers()['cache-control'],/no-store/);const documentsData=await documentsResponse.json();valid('ServicingDocumentHistoryPage',documentsData);assert.ok(documentsData.items.length>0);assert.equal(documentsData.items[0].deliveryState,'delivered');
   assert.equal((await page.request.get(documentsUrl+'&forged=true')).status(),400);
   const anonymous=await browser.newContext();assert.equal((await anonymous.request.get(documentsUrl)).status(),401);await anonymous.close();
   await page.goto(origin+`/drafts/${journey.draftId}`);const documents=page.getByRole('region',{name:'Renewal invitation documents',exact:true});await documents.waitFor();assert.equal(await documents.getByText('Not delivered',{exact:true}).count(),0);assert.match(await documents.innerText(),/@/);
   await documents.getByRole('button').first().click();await page.getByRole('region',{name:'Renewal invitation and acceptance',exact:true}).getByText('Renewal term premium',{exact:true}).waitFor();await documents.scrollIntoViewIfNeeded();await page.screenshot({path:output+'/'+journey.productCode+'-documents.png'});
   await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await documents.scrollIntoViewIfNeeded();await page.screenshot({path:output+'/'+journey.productCode+'-documents-mobile.png'});await page.setViewportSize({width:1560,height:1000});
  }
  await page.goto(origin+`/policies/${journey.policyId}?termId=${receipt.termId}&versionId=${receipt.versionId}`);
  await page.getByText('Viewing a specific issued change. Its effective date may be in the future.',{exact:true}).waitFor();
  await page.getByText(receipt.versionId,{exact:true}).waitFor({state:'attached'});
  await page.goto(origin+`/policies/${journey.policyId}?termId=${receipt.termId}&versionId=${receipt.versionId}&tab=Transactions`);
  await page.getByRole('heading',{name:renewal?'Renewal transaction':'Adjustment transaction',exact:true}).waitFor();
  assert.equal(await page.getByRole('tab',{name:'Transactions',exact:true}).getAttribute('aria-selected'),'true');
  await page.screenshot({path:output+'/'+journey.productCode+'-issued-version.png',fullPage:true});
  await page.getByRole('button',{name:'View current policy',exact:true}).click();
  await page.getByText('Viewing a specific issued change. Its effective date may be in the future.',{exact:true}).waitFor({state:'detached'});
  results.push({policyId:journey.policyId,versionId:receipt.versionId,contentHash:data.contentHash,checks:['exact persisted snapshot hash','signed financial and issue response schemas','issued version UI link','current-policy navigation']});
 }
 const responses=process.env.COVER_ISSUE_RESPONSES_DIRECTORY;
 if(responses){const files=(await readdir(responses)).filter(x=>x.endsWith('.json'));assert.ok(files.length>=2);for(const file of files){const item=JSON.parse(await readFile(responses+'/'+file,'utf8'));valid(item.schema,item.data);}}
 assert.deepEqual(errors,[]);await writeFile(output+'/restart-readback.json',JSON.stringify({completedAt:new Date().toISOString(),note:'Legacy evidence filename retained; this script verifies persisted readback but does not restart a process.',results},null,2));console.log('Both product persisted issue graphs, UI links and response contracts pass. Actual process restart is verified separately.');
}finally{await browser.close();}
