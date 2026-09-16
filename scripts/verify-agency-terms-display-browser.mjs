import {chromium} from 'playwright';
import assert from 'node:assert/strict';
import {readFile,mkdir} from 'node:fs/promises';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';
if(!['localhost','127.0.0.1'].includes(new URL(origin).hostname))throw Error('Local demo only');
const password=(await readFile('.local/demo-password.txt','utf8')).trim();
const {stdout}=await promisify(execFile)('dotnet',['backend/src/BackOffice.Api/bin/Debug/net10.0/BackOffice.Api.dll','--seed-agency-notification-demo'],{env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',DOTNET_ENVIRONMENT:'Development'},windowsHide:true,maxBuffer:8*1024*1024});
const id=JSON.parse(stdout.trim().split(/\r?\n/).at(-1)).agencyId;
await mkdir('.local/browser-evidence',{recursive:true});
const browser=await chromium.launch({channel:'chrome',headless:true});const page=await browser.newPage({viewport:{width:1560,height:1000}});page.setDefaultTimeout(20000);const errors=[];page.on('pageerror',e=>errors.push(e.message));
try{
 await page.goto(origin+'/login');await page.getByLabel('Email address',{exact:true}).fill('underwriter@cover.example');await page.getByLabel('Password',{exact:true}).fill(password);await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');
 await page.goto(`${origin}/agents/${id}?tab=Accounts`);await page.getByText('No approved terms have been published for this agency.',{exact:true}).waitFor();
 const catalog=await page.evaluate(async()=>{const response=await fetch('/api/v1/agency-product-catalog');if(!response.ok)throw Error('Catalogue unavailable');return(await response.json()).items;});assert.ok(catalog.length);
 // Presentation fixtures exercise all statuses without pretending to publish real terms.
 const snapshot={commercialTerms:{commissionBasis:'per-product',feeSharing:'none',volumeCommitmentMode:'none',minimumPremiumOverrideMode:'none',referralRouting:'standard-internal-underwriting',effectiveFrom:'2026-09-01'},settlement:{statementCycle:'monthly',method:'bank-transfer',premiumCollection:'agency',commissionSettlement:'net-remittance'},paymentTermsDays:60,creditLimit:'99999999999999999.99',products:[{productVersionId:catalog[0].productVersionId,effectiveFrom:'2026-09-01',brokerCommissionBasisPoints:1250}]};
 const row=(version,status,date)=>({...snapshot,products:snapshot.products.map(product=>({...product,effectiveFrom:date})),id:crypto.randomUUID(),agencyId:id,version,status,effectiveFrom:date,commercialTerms:{...snapshot.commercialTerms,effectiveFrom:date},createdAt:'2026-09-01T10:00:00Z',approvedRequestId:crypto.randomUUID(),approvedRequestKind:version===1?'activation':'terms'});
 const scheduled=row(3,'scheduled','2026-12-01');const current={...row(2,'current','2026-09-01'),effectiveTo:'2026-12-01'};const historical={...row(1,'historical','2026-08-01'),effectiveTo:'2026-09-01'};let fail=false;
 await page.route(`**/api/v1/agencies/${id}/terms?**`,async route=>{if(fail){await route.fulfill({status:503,json:{detail:'private database stack must not render'}});return;}const older=new URL(route.request().url()).searchParams.has('cursor');await route.fulfill({status:200,headers:{ETag:'"fixture"'},json:{items:older?[historical]:[scheduled,current],totalCount:3,asOf:'2026-09-14T12:00:00Z',...(older?{}:{nextCursor:'fixture-next-page'})}});});
 await page.getByRole('button',{name:'Refresh approved terms',exact:true}).click();await page.getByRole('heading',{name:'Version 3 · scheduled',exact:true}).waitFor();await page.getByRole('heading',{name:'Version 2 · current',exact:true}).waitFor();assert.equal(await page.getByText('£99,999,999,999,999,999.99',{exact:true}).count(),2);await page.getByText('60 days',{exact:true}).first().waitFor();
 await page.getByRole('button',{name:'Next page',exact:true}).click();await page.getByRole('heading',{name:'Version 1 · historical',exact:true}).waitFor();await page.getByRole('button',{name:'Previous page',exact:true}).click();await page.getByRole('heading',{name:'Version 2 · current',exact:true}).waitFor();
 await page.goto(`${origin}/agents/${id}?tab=Products`);const table=page.getByRole('table',{name:'Products in terms version 2',exact:true});await table.getByText(catalog[0].name,{exact:true}).waitFor();await table.getByText('12.50%',{exact:true}).waitFor();await page.screenshot({path:'.local/browser-evidence/agency-terms-desktop.png',fullPage:true});
 await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.screenshot({path:'.local/browser-evidence/agency-terms-mobile.png',fullPage:true});
 fail=true;await page.getByRole('button',{name:'Refresh approved terms',exact:true}).click();await page.getByRole('alert').filter({hasText:'The service could not confirm'}).waitFor();assert.equal(await page.getByText('private database stack must not render',{exact:true}).count(),0);fail=false;await page.getByRole('button',{name:'Refresh approved terms',exact:true}).click();await page.getByRole('heading',{name:'Version 2 · current',exact:true}).waitFor();assert.deepEqual(errors,[]);
 console.log('Terms display browser passed: real read-only login and empty SQL history; intercepted display fixtures for current/scheduled/historical, exact money, products, pagination, safe error recovery and390px. Publication is not exercised by this script.');
}finally{await browser.close();}
