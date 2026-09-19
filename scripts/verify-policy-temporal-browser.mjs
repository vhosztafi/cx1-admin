import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {chromium} from 'playwright';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';
assert.ok(['127.0.0.1','localhost'].includes(new URL(origin).hostname));
const output='.local/browser-evidence/policy-temporal';await mkdir(output,{recursive:true});
const fixtures=JSON.parse(await readFile(process.env.COVER_POLICY_FIXTURES??'.local/browser-evidence/underwriting-issue/report.json','utf8')).journeys;
const password=(await readFile('.local/demo-password.txt','utf8')).trim();
const browser=await chromium.launch({channel:'chrome',headless:true});
const context=await browser.newContext({viewport:{width:1560,height:1000}});
const page=await context.newPage();page.setDefaultTimeout(30000);
const errors=[];page.on('pageerror',error=>errors.push(error.message));
const report={journeys:[]};
async function get(path){const response=await page.request.get(origin+path);assert.equal(response.status(),200,`${path}: ${await response.text()}`);assert.match(response.headers()['cache-control']??'',/no-store/);return response.json();}
const input=instant=>new Date(instant).toISOString().slice(0,16);
try {
 await page.goto(origin+'/login');await page.getByLabel('Email address').fill('underwriter@cover.example');await page.getByLabel('Password',{exact:true}).fill(password);
 await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');
 for(const fixture of fixtures){
  const current=await get(`/api/v1/policies/${fixture.policyId}`);
  assert.equal(current.versionId,fixture.versionId);
  // The UI accepts minute precision. Round knowledge forward so a policy
  // issued seconds ago is included by the same API and UI cutoff.
  const effective=new Date(current.snapshot.term.startsAt).toISOString(),known=new Date(Math.ceil(Date.now()/60000)*60000).toISOString();
  const params=new URLSearchParams({effectiveAt:effective,knownAt:known});
  const at=await get(`/api/v1/policies/${fixture.policyId}/as-at?${params}`);
  assert.equal(at.contentHash,current.contentHash);assert.deepEqual(at.snapshot,current.snapshot);assert.equal(at.coverageState,'active');
  const term=await get(`/api/v1/terms/${current.termId}/as-at?${params}`);assert.equal(term.versionId,at.versionId);
  for(const invalid of ['effectiveAt=2026-09-17&knownAt='+encodeURIComponent(known),params+'&extra=1',params+'&knownAt='+encodeURIComponent(known)])
   assert.equal((await page.request.get(origin+`/api/v1/policies/${fixture.policyId}/as-at?${invalid}`)).status(),400);
  assert.equal((await page.request.get(origin+`/api/v1/terms/${crypto.randomUUID()}/as-at?${params}`)).status(),404);
  await page.goto(origin+`/policies/${fixture.policyId}`);await page.getByRole('heading',{name:current.reference,exact:true}).waitFor();
  await page.getByLabel('Effective date and time (UTC)',{exact:true}).fill(input(effective));
  await page.getByLabel('Known-at date and time (UTC)',{exact:true}).fill(input(known));
  await page.getByRole('button',{name:'View selected dates',exact:true}).click();await page.getByText('In force',{exact:true}).waitFor();
  await page.getByLabel('Known-at date and time (UTC)',{exact:true}).fill(input(Date.parse(current.issuedAt)-1000));
  await page.getByRole('button',{name:'View selected dates',exact:true}).click();await page.getByRole('heading',{name:'No cover recorded at these dates',exact:true}).waitFor();
  assert.equal(await page.getByRole('heading',{name:current.reference,exact:true}).count(),0);
  await page.getByRole('button',{name:'View current policy',exact:true}).click();await page.getByRole('heading',{name:current.reference,exact:true}).waitFor();
  report.journeys.push({productCode:fixture.productCode,policyId:fixture.policyId,versionId:at.versionId,contentHash:at.contentHash,checks:['same issued bytes','policy and scoped term cutoffs','strict query rejection','unknown term denial','UI historical cover','no-known-version empty result','return to current']});
 }
 await page.screenshot({path:output+'/desktop.png',fullPage:true});
 await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 await page.screenshot({path:output+'/mobile.png',fullPage:true});assert.deepEqual(errors,[]);
 await writeFile(output+'/report.json',JSON.stringify(report,null,2)+'\n');
 console.log(`${report.journeys.length} persisted policies: temporal API/UI checks passed; no policy data changed.`);
} catch(error){await page.screenshot({path:output+'/failure.png',fullPage:true}).catch(()=>{});throw error;}
finally{await context.close();await browser.close();}
