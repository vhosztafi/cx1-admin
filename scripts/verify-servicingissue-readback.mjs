import assert from 'node:assert/strict';
import {readFile,writeFile,readdir} from 'node:fs/promises';
import {chromium} from 'playwright';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const output='.local/browser-evidence/servicing-issue';
const report=JSON.parse(await readFile(output+'/report.json','utf8'));assert.equal(report.journeys.length,2);assert.ok(report.completedAt);
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
  const data=await response.json();valid('ServicingPolicyView',data);assert.equal(data.contentHash,journey.contentHash);assert.equal(data.snapshot.provenance.servicingIssueDecisionId,receipt.decisionId);assert.equal(data.financials.journalId,receipt.journalId);assert.equal(data.financials.amountDue,receipt.netAmount);
  const draft=await page.request.get(origin+`/api/v1/drafts/${journey.draftId}`);assert.equal(draft.status(),200);assert.equal((await draft.json()).state,'issued');
  await page.goto(origin+`/policies/${journey.policyId}?termId=${receipt.termId}&versionId=${receipt.versionId}`);
  await page.getByText('Viewing a specific issued change. Its effective date may be in the future.',{exact:true}).waitFor();
  await page.getByText(receipt.versionId,{exact:true}).waitFor({state:'attached'});
  await page.goto(origin+`/policies/${journey.policyId}?termId=${receipt.termId}&versionId=${receipt.versionId}&tab=Transactions`);
  await page.getByRole('heading',{name:'Adjustment transaction',exact:true}).waitFor();
  assert.equal(await page.getByRole('tab',{name:'Transactions',exact:true}).getAttribute('aria-selected'),'true');
  await page.screenshot({path:output+'/'+journey.productCode+'-issued-version.png',fullPage:true});
  await page.getByRole('button',{name:'View current policy',exact:true}).click();
  await page.getByText('Viewing a specific issued change. Its effective date may be in the future.',{exact:true}).waitFor({state:'detached'});
  results.push({policyId:journey.policyId,versionId:receipt.versionId,contentHash:data.contentHash,checks:['post-restart exact snapshot hash','signed financial and issue response schemas','issued version UI link','current-policy navigation']});
 }
 const responses=process.env.COVER_ISSUE_RESPONSES_DIRECTORY;
 if(responses){const files=(await readdir(responses)).filter(x=>x.endsWith('.json'));assert.ok(files.length>=2);for(const file of files){const item=JSON.parse(await readFile(responses+'/'+file,'utf8'));valid(item.schema,item.data);}}
 assert.deepEqual(errors,[]);await writeFile(output+'/restart-readback.json',JSON.stringify({completedAt:new Date().toISOString(),results},null,2));console.log('Both product issue graphs and UI links survived restart; response contracts pass.');
}finally{await browser.close();}
