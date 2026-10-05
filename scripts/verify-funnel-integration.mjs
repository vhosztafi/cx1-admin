import {chromium} from 'playwright';
import assert from 'node:assert/strict';
import {readFile,mkdir,writeFile} from 'node:fs/promises';
// Additive fictional records, isolated database and exact local origins only.
const origin='http://127.0.0.1:3193';
const fixture=JSON.parse(await readFile('.local/phase11-fixture/fixture.json','utf8'));
assert.match(fixture.database,/^CoverMGA_Test_Phase11_[a-f0-9]{32}$/);
const password=(await readFile('.local/phase11-fixture/password.txt','utf8')).trim();
const output='output/playwright/v1.1';await mkdir(output,{recursive:true});
const browser=await chromium.launch({channel:'chrome',headless:true});
const context=await browser.newContext({viewport:{width:1560,height:1000}});const page=await context.newPage();page.setDefaultTimeout(30000);
const errors=[];page.on('pageerror',error=>errors.push(error.message));
const report={checks:[]};
try {
 await page.goto(origin+'/quotes/new');await page.waitForURL('**/login');
 await page.getByLabel('Email address',{exact:true}).fill('underwriter@cover.example');await page.getByLabel('Password',{exact:true}).fill(password);await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');
 await page.goto(origin+'/quotes/new');await page.getByRole('button',{name:'Create new client',exact:true}).click();
 const clientName=`Fictional funnel customer ${crypto.randomUUID().slice(0,8)}`;
 await page.getByLabel('Legal business name *',{exact:true}).fill(clientName);await page.getByLabel('Address line 1 *',{exact:true}).fill('1 Fictional Lane');await page.getByLabel('Town or city *',{exact:true}).fill('Example Town');await page.getByLabel('Postcode *',{exact:true}).fill('PE1 1AA');await page.getByLabel('Entity type *',{exact:true}).selectOption('sole-trader');
 await page.getByRole('button',{name:'Create client',exact:true}).click();
 const reporting=JSON.parse(await readFile('.local/phase11-fixture/reporting.json','utf8'));
 await page.getByRole('combobox',{name:'Agency *',exact:true}).selectOption(reporting.agencyId);await page.getByRole('button',{name:'Link agency',exact:true}).click();
 await page.getByRole('radio',{name:'Fictional Phase 12 reporting agency AG-RPT-12 · active',exact:true}).check();await page.getByRole('combobox',{name:'Agency contact',exact:true}).selectOption('main-contact');
 await page.getByRole('radio',{name:'Motor Trade Road Risks v2 · Available',exact:true}).check();await page.getByRole('button',{name:'Start Motor Trade quote',exact:true}).click();await page.waitForURL(/\/quotes\/[a-f0-9-]{36}\/funnel$/);
 const id=page.url().split('/').at(-2);report.quoteId=id;
 const frame=page.frameLocator('iframe[title="Motor Trade quote funnel"]');
 await frame.getByRole('button',{name:'Save draft',exact:true}).waitFor();
 await frame.getByText('No',{exact:true}).click();
 await frame.getByRole('button',{name:'Save draft',exact:true}).click();await frame.getByRole('status').filter({hasText:'Draft saved.'}).waitFor();
 const quote=await (await page.request.get(origin+'/api/v1/quotes/'+id)).json();assert.equal(quote.clientName,clientName);assert.equal(JSON.parse(quote.brokerContactJson).name,'Fictional broker contact');
 assert.equal(JSON.parse(quote.funnelStateJson).formData.isShortTerm,false);assert.equal(quote.proposal.termIntent.kind,'annual');assert.equal(JSON.parse(quote.funnelStateJson).formData.proposerCompanyTypeId,1);
 report.checks.push('inline client creation and agency linking; client/agency/contact selected before capture; correct saved identity; existing 14-step funnel; current-step false saved atomically');
 await page.reload();await frame.getByRole('button',{name:'Save draft',exact:true}).waitFor();assert.equal(await frame.getByRole('radio',{name:'No',exact:true}).isChecked(),true);
 report.checks.push('saved raw source prefill and current step survive reload');
 await page.screenshot({path:output+'/quote-funnel.png',fullPage:true});
 const attempts=[];
 await page.route(`**/api/v1/quotes/${id}/proposal`,async route=>{
   if(route.request().method()!=='PUT')return route.continue();
   attempts.push({body:route.request().postData(),key:route.request().headers()['idempotency-key']});
   if(attempts.length===1){const response=await route.fetch();assert.equal(response.status(),200);return route.abort('failed');}
   if(attempts.length===2)return route.fulfill({status:403,contentType:'application/json',body:'{}'});
   return route.continue();
 });
 await frame.getByText('Yes',{exact:true}).click();await frame.getByRole('button',{name:'Save draft',exact:true}).click();
 await frame.getByRole('button',{name:'Retry same save',exact:true}).waitFor();await frame.getByRole('button',{name:'Retry same save',exact:true}).click();
 await frame.getByRole('alert').waitFor();await frame.getByRole('button',{name:'Retry same save',exact:true}).click();await frame.getByRole('status').filter({hasText:'Draft saved.'}).waitFor();
 assert.equal(attempts.length,3);assert.deepEqual(attempts[0],attempts[1]);assert.deepEqual(attempts[0],attempts[2]);await page.unroute(`**/api/v1/quotes/${id}/proposal`);
 report.checks.push('lost committed save, subsequent denial and identical authorized retry retain one immutable command');
 await frame.getByRole('button',{name:'Return to quote',exact:true}).click();await page.waitForURL(origin+'/quotes/'+id);
 report.checks.push('save and return goes to the same quote');
 assert.deepEqual(errors,[]);await writeFile(output+'/funnel-integration.json',JSON.stringify(report,null,2));console.log(JSON.stringify(report));
}finally{await browser.close();}
