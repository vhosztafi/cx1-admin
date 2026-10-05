import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {chromium} from 'playwright';
const origin='http://127.0.0.1:3193',out='output/playwright/v1.1';
const fixture=JSON.parse(await readFile('.local/phase11-fixture/fixture.json','utf8'));
assert.match(fixture.database,/^CoverMGA_Test_Phase11_[a-f0-9]{32}$/);
const password=(await readFile('.local/phase11-fixture/password.txt','utf8')).trim();
const ids=JSON.parse(await readFile('.local/phase11-fixture/reporting.json','utf8'));
const mta=JSON.parse(await readFile(out+'/mta-funnel-fixture.json','utf8'));
const browser=await chromium.launch({channel:'chrome',headless:true});
const page=await browser.newPage({viewport:{width:1560,height:1000}}),checks=[],errors=[];
page.on('pageerror',e=>errors.push(e.message));
async function login(actor){await page.goto('about:blank');await page.context().clearCookies();await page.goto(origin+'/login');await page.getByLabel('Email address').fill(actor+'@cover.example');await page.getByLabel('Password',{exact:true}).fill(password);await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');}
try{
 await login('underwriter');
 const sections=[['dashboard','/','Dashboard'],['clients','/clients/'+ids.clientId,'Fictional reporting customer'],['matching','/matches/3a000000-0000-4000-8000-000000000001','Signals compared'],['agency','/agents/'+ids.agencyId,'Fictional Phase 12 reporting agency'],['quote','/quotes/'+mta.quoteId,'Fictional reporting customer'],['mta','/drafts/'+mta.draftId,'Fictional reporting customer'],['policy','/policies/'+mta.policyId,'Fictional reporting customer'],['tasks','/tasks','My tasks'],['reporting','/reporting','Reporting']];
 for(const [name,path,heading] of sections){await page.setViewportSize({width:1560,height:1000});await page.goto(origin+path);await page.getByRole('heading',{name:name==='dashboard'?/^Good /:heading,exact:true}).first().waitFor();await page.waitForTimeout(800);await page.screenshot({path:out+'/visual-'+name+'-desktop.png'});await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true,name+' mobile containment');await page.screenshot({path:out+'/visual-'+name+'-mobile.png'});checks.push(name+': saved desktop presentation and 390px contained layout');}
 await login('agency-admin');await page.goto(origin+'/agents/'+ids.agencyId);await page.getByRole('link',{name:'Invite user',exact:true}).waitFor();await page.getByRole('link',{name:'Suspend agency',exact:true}).waitFor();await page.getByRole('link',{name:'Preview portal',exact:true}).click();await page.getByRole('heading',{name:'Agency portal — data reference',exact:true}).waitFor();await page.getByRole('table',{name:'Agency sharing boundaries',exact:true}).waitFor();checks.push('agency administrator contextual invite/suspension routes and loaded internal sharing boundary table');
 await login('finance');await page.goto(origin+'/accounting');await page.getByRole('heading',{name:'Accounting',exact:true}).waitFor();await page.waitForTimeout(800);await page.screenshot({path:out+'/visual-accounting-mobile.png'});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 await login('system-admin');await page.goto(origin+'/admin');await page.getByRole('heading',{name:'Administration areas',exact:true}).waitFor();await page.waitForTimeout(400);await page.screenshot({path:out+'/visual-admin-mobile.png'});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 await page.goto(origin+'/account');await page.getByLabel('Job title',{exact:true}).waitFor();await page.screenshot({path:out+'/visual-account-mobile.png'});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);checks.push('accounting, administration and loaded account profile mobile containment');
 assert.deepEqual(errors,[]);await writeFile(out+'/section-visual-report.json',JSON.stringify({passed:true,checks},null,2));console.log(JSON.stringify({passed:true,checks}));
}finally{await browser.close();}
