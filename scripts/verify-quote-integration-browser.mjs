import {chromium} from 'playwright';
import assert from 'node:assert/strict';
import {readFile,mkdir,writeFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const output='.local/browser-evidence/quote-integration';await mkdir(output,{recursive:true});
const password=(await readFile('.local/demo-password.txt','utf8')).trim(),spec=JSON.parse(await readFile('contracts/openapi.json','utf8'));
const ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);const summary=ajv.compile(spec.components.schemas.QuoteDiscoverySummary),shared=ajv.compile(spec.components.schemas.AgencySharedQuote);
const browser=await chromium.launch({channel:'chrome',headless:true}),context=await browser.newContext({viewport:{width:1560,height:1000}}),page=await context.newPage();
page.setDefaultTimeout(25000);const errors=[],report={};page.on('pageerror',error=>errors.push(error.message));
const button=name=>page.getByRole('button',{name,exact:true});
async function login(target,role){await target.goto(`${origin}/login`);await target.getByLabel('Email address').fill(`${role}@cover.example`);await target.getByLabel('Password',{exact:true}).fill(password);await target.getByRole('button',{name:'Sign in',exact:true}).click();await target.waitForURL(`${origin}/`);}
async function get(path){const response=await page.request.get(origin+path);assert.equal(response.status(),200,await response.text());return {data:await response.json(),etag:response.headers().etag};}
async function write(method,path,data,etag){const csrf=(await get('/api/v1/auth/csrf')).data.requestToken;const response=await page.request.fetch(origin+path,{method,headers:{'X-CSRF-Token':csrf,'Idempotency-Key':crypto.randomUUID(),...(etag?{'If-Match':etag}:{})},data});assert.ok(response.ok(),await response.text());return {data:await response.json(),etag:response.headers().etag};}
async function decide(label,reason){await button(label).click();await page.getByLabel('Decision reason *').fill(reason);await button('Record decision').click();await page.getByLabel('Decision reason *').waitFor({state:'detached'});}
async function capture(name){await page.screenshot({path:`${output}/${name}-desktop.png`,fullPage:true});await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.screenshot({path:`${output}/${name}-mobile.png`,fullPage:true});await page.setViewportSize({width:1560,height:1000});}
try{
 await login(page,'underwriter');const marker=Date.now().toString(36),name=`Fictional Integration Traders ${marker}`;
 const baseRelationship=(await get('/api/v1/relationships/51000000-0000-4000-8000-000000000003')).data,agencyId=baseRelationship.agencyId;
 const products=(await get(`/api/v1/quote-products?relationshipId=${baseRelationship.id}`)).data.items.filter(product => product.captureEligible);assert.equal(products.length,2);
 const clients=[];
 for(let index=0;index<2;index++){
  const created=await write('POST','/api/v1/clients',{legalName:name,entityType:'sole-trader',address:{line1:'1 Fictional Workshop Lane',town:'Sheffield',postcode:'S1 1AA',country:'GB'}});
  const client=(await get(`/api/v1/clients/${created.data.id}`)).data;
  const relationship=await write('POST',`/api/v1/clients/${client.id}/relationships`,{agencyId},created.etag);clients.push({...client,relationshipId:relationship.data.id});
 }
 await page.goto(`${origin}/clients/${clients[1].id}`);await page.getByRole('link',{name:'New quote',exact:true}).click();await page.getByText(clients[1].reference,{exact:true}).first().waitFor();
 await page.getByRole('radio',{name:new RegExp(baseRelationship.agencyName)}).check();await page.getByRole('radio',{name:/Motor Trade Road Risks/}).and(page.locator(':enabled')).check();
 let first=true;const attempts=[];await page.route('**/api/v1/quotes',async route=>{if(route.request().method()!=='POST')return route.continue();attempts.push({body:route.request().postData(),key:route.request().headers()['idempotency-key']});if(first){first=false;const response=await route.fetch();assert.equal(response.status(),201,await response.text());await route.abort('failed');}else await route.continue();});
 await button('Create quote draft').click();await button('Retry same creation').click();await page.waitForURL(url=>/^\/quotes\/[0-9a-f-]+$/.test(url.pathname));await button('Withdraw quote').waitFor();await page.unroute('**/api/v1/quotes');assert.equal(attempts.length,2);assert.deepEqual(attempts[0],attempts[1]);
 const quoteId=page.url().split('/').at(-1),original=(await get(`/api/v1/quotes/${quoteId}`)).data;assert.ok(original.matchReviewId);assert.ok(original.readiness.issues.some(x=>x.category==='matching'));
 await page.getByRole('link',{name:'Open account matching review',exact:true}).click();await page.getByRole('heading',{name:'Signals compared',exact:true}).waitFor();
 assert.equal(Math.round((await page.locator('.match-rail').boundingBox()).width),314);
 const matchId=original.matchReviewId;await decide('Not a duplicate',`Fictional separate account ${marker}`);
 const separated=(await get(`/api/v1/quotes/${quoteId}`)).data;assert.equal(separated.revisionNumber,2);assert.notEqual(separated.clientId,original.clientId);
 assert.equal((await get(`/api/v1/quotes/${quoteId}/revisions/${original.revisionId}`)).data.clientId,original.clientId);
 await decide('Reopen match review',`Fictional reassociation check ${marker}`);const match=(await get(`/api/v1/matches/${matchId}`)).data;
 await page.getByRole('button',{name:new RegExp(`^Link to ${clients[0].reference}$`)}).click();await page.getByLabel('Decision reason *').fill(`Fictional stale decision ${marker}`);
 const current=await get(`/api/v1/quotes/${quoteId}`),registration=`QI${marker.slice(-5).toUpperCase()}`;
 const proposal={...current.data.proposal,insured:{firstName:'Concurrent fictional edit'},risk:{vehicles:[{id:crypto.randomUUID(),registration:`${registration}A`},{id:crypto.randomUUID(),registration:`${registration}B`}]}};
 await write('PUT',`/api/v1/quotes/${quoteId}/proposal`,{proposal,reason:`Fictional concurrent save ${marker}`},current.etag);
 await button('Record decision').click();await button('Reload saved review').waitFor();await button('Reload saved review').click();await page.getByLabel('Decision reason *').waitFor({state:'detached'});
 await decide(`Link to ${clients[0].reference}`,`Fictional reviewed association ${marker}`);const linked=(await get(`/api/v1/quotes/${quoteId}`)).data;assert.equal(linked.clientId,clients[0].id);assert.equal(linked.revisionNumber,4);assert.ok(!linked.readiness.issues.some(x=>x.category==='matching'));
 await capture('matching');await page.getByRole('link',{name:'Open attached quote',exact:true}).click();await page.getByRole('link',{name:'Back to quotes',exact:true}).click();
 await page.getByLabel('Search quotes',{exact:true}).fill(registration);await button('Search').click();await page.getByRole('link',{name:linked.reference,exact:true}).waitFor();
 const found=(await get(`/api/v1/quotes?q=${registration}`)).data;assert.equal(found.totalCount,1);assert.ok(summary(found.items[0]),JSON.stringify(summary.errors));
 await page.getByLabel('Product',{exact:true}).selectOption('motor-trade-combined');await page.getByRole('heading',{name:'No quotes match these filters',exact:true}).waitFor();
 await page.getByLabel('Product',{exact:true}).selectOption('motor-trade-road-risks');await page.getByRole('link',{name:linked.reference,exact:true}).waitFor();
 await page.getByLabel('Sort by',{exact:true}).selectOption('updated');await page.getByLabel('Order',{exact:true}).selectOption('desc');await page.getByRole('link',{name:linked.reference,exact:true}).waitFor();await capture('discovery');
 await page.getByRole('link',{name:name,exact:true}).click();await page.getByLabel('Client sections',{exact:true}).getByRole('link',{name:'Quotes',exact:true}).click();await page.getByRole('link',{name:linked.reference,exact:true}).waitFor();await capture('client-quotes');
 await page.getByRole('link',{name:'Activity',exact:true}).click();await page.getByRole('link',{name:'Quote saved.',exact:true}).first().click();await button('Withdraw quote').waitFor();
 await page.goto(`${origin}/quotes`);const table=page.getByRole('region',{name:'Saved quotes',exact:true});await table.waitFor();const initialRef=await table.locator('tbody tr').first().innerText();await button('Next page').last().click();await page.waitForFunction(old=>{const row=document.querySelector('table tbody tr');return row&&row.innerText!==old;},initialRef);await table.waitFor();
 await page.goto(`${origin}/agents/${agencyId}/sharing`);await page.getByLabel('Search shared quote summaries',{exact:true}).fill(linked.reference);await page.locator('form').filter({has:page.getByLabel('Search shared quote summaries',{exact:true})}).getByRole('button',{name:'Search',exact:true}).click();await page.getByRole('region',{name:'Shared quote summaries',exact:true}).getByText(linked.reference,{exact:true}).waitFor();
 const sharedQuotes=(await get(`/api/v1/agencies/${agencyId}/sharing/quotes?q=${linked.reference}`)).data;assert.equal(sharedQuotes.totalCount,1);assert.ok(shared(sharedQuotes.items[0]),JSON.stringify(shared.errors));assert.ok(!JSON.stringify(sharedQuotes).includes(registration));await capture('agency-sharing');
 const combined=products.find(x=>x.productCode==='motor-trade-combined');const combinedCreate=await write('POST','/api/v1/quotes',{relationshipId:baseRelationship.id,productVersionId:combined.productVersionId,proposal:{schemaVersion:'1.0',productCode:combined.productCode}});
 const combinedRead=(await get(`/api/v1/quotes/${combinedCreate.data.id}`)).data;const combinedList=(await get(`/api/v1/quotes?q=${combinedRead.reference}&productCode=motor-trade-combined`)).data;assert.equal(combinedList.totalCount,1);assert.ok(summary(combinedList.items[0]),JSON.stringify(summary.errors));
 const limitedContext=await browser.newContext(),limited=await limitedContext.newPage();await login(limited,'agency-admin');await limited.goto(`${origin}/quotes`);await limited.getByRole('heading',{name:'Quotes are restricted',exact:true}).waitFor();assert.equal((await limited.request.get(`${origin}/api/v1/quotes`)).status(),403);await limitedContext.close();
 Object.assign(report,{quoteId,reference:linked.reference,matchId,originalClientId:original.clientId,separateClientId:separated.clientId,currentClientId:linked.clientId,revision:linked.revisionNumber,combinedId:combinedRead.id,combinedReference:combinedRead.reference,checks:['client entry and pinned selection','lost creation response exact replay','automatic duplicate review and matching readiness','separate and link preserve historical identity','stale quote version blocks match decision','current registration search without duplicates','product and sort filters and pagination','real client quote and activity links','safe agency summaries','restricted role denial','314px rail and390px containment']});
 assert.deepEqual(errors,[]);await writeFile(`${output}/report.json`,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
}catch(error){await page.screenshot({path:`${output}/failure.png`,fullPage:true}).catch(()=>{});await writeFile(`${output}/failure.txt`,await page.locator('main').innerText()).catch(()=>{});throw error;}finally{await browser.close();}
