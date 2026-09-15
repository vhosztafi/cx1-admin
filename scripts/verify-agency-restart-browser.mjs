import {chromium} from 'playwright';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';

const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';
if(!['localhost','127.0.0.1'].includes(new URL(origin).hostname)) throw Error('Local demo only.');
const result=JSON.parse(await readFile('.local/browser-evidence/agency-lifecycle-result.json','utf8'));
assert.equal(result.passed,true);
const password=(await readFile('.local/demo-password.txt','utf8')).trim();
const browser=await chromium.launch({channel:'chrome',headless:true});
try {
  const page=await browser.newPage({viewport:{width:1560,height:1000}});
  await page.goto(origin+'/login');
  await page.getByLabel('Email address',{exact:true}).fill('agency-admin@cover.example');
  await page.getByLabel('Password',{exact:true}).fill(password);
  await page.getByRole('button',{name:'Sign in',exact:true}).click();
  await page.waitForURL(origin+'/');
  const path=`${origin}/api/v1/agencies/${result.agencyId}`;
  const agency=await page.request.get(path);assert.equal(agency.status(),200);
  const stored=await agency.json();assert.equal(stored.reference,result.reference);assert.equal(stored.state,'active');
  const response=await page.request.get(path+'/terms');assert.equal(response.status(),200);
  const terms=(await response.json()).items;
  assert.equal(terms.length,2);
  assert.equal(terms.some(x=>x.id===result.initialVersionId&&x.status==='historical'),true);
  assert.equal(terms.some(x=>x.id===result.currentVersionId&&x.status==='current'&&x.creditLimit==='123456.78'),true);
  await page.goto(`${origin}/agents/${result.agencyId}?tab=Accounts`);
  await page.getByText('£123,456.78',{exact:true}).waitFor();
  await page.reload();await page.getByText('£123,456.78',{exact:true}).waitFor();
  await page.screenshot({path:'.local/browser-evidence/agency-restart-accounts.png',fullPage:true});
  console.log('Fresh post-restart login, persisted agency identity/state, exact current/historical terms and Accounts reload passed.');
} finally {await browser.close();}
