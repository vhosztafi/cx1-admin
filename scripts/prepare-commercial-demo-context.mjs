import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir,open,unlink} from 'node:fs/promises';
import {resolve,sep} from 'node:path';
import {chromium} from 'playwright';
import {openDemoJournal} from './demo-command-journal.mjs';

// Explicit adoption by two existing agency operators. No authority grant,
// activation bypass, configuration rewrite or external invitation is performed.
const [agencyId,productVersionId]=process.argv.slice(2);
for(const id of [agencyId,productVersionId])assert.match(id??'',/^[a-f0-9-]{36}$/i);
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const directory=process.env.COVER_COMMERCIAL_DEMO_DIRECTORY??'.local/commercial-lifecycle-demo-v1';assert.ok(resolve(directory).startsWith(resolve('.local')+sep));await mkdir(directory,{recursive:true});
const lock=await open(directory+'/context-running.lock','wx');let browser;
try{
 await lock.writeFile(String(process.pid));const selected={agencyId,productVersionId};
 try{assert.deepEqual(JSON.parse(await readFile(directory+'/context-selection.json','utf8')),selected);}catch(error){if(error.code!=='ENOENT')throw error;await writeFile(directory+'/context-selection.json',JSON.stringify(selected),{flag:'wx'});}
 const journal=await openDemoJournal(directory+'/context-commands.json',origin);
 browser=await chromium.launch({channel:'chrome',headless:true});
 const password=process.env.COVER_COMMERCIAL_DEMO_PASSWORD??(await readFile('.local/demo-password.txt','utf8')).trim();
 async function login(email){const page=await browser.newPage();await page.goto(origin+'/login');await page.getByLabel('Email address',{exact:true}).fill(email);await page.getByLabel('Password',{exact:true}).fill(password);await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');return page;}
 async function get(page,path){const response=await page.request.get(origin+path);assert.equal(response.status(),200,path);return{data:await response.json(),etag:response.headers().etag};}
 async function command(page,name,path,data,scope){
  const current=await get(page,scope);
  return journal.command(name,async()=>({path,data,etag:current.etag}),async(request,key)=>{
   const csrf=(await get(page,'/api/v1/auth/csrf')).data.requestToken;
   const response=await page.request.post(origin+request.path,{headers:{'X-CSRF-Token':csrf,'Idempotency-Key':key,...(request.etag?{'If-Match':request.etag}:{})},data:request.data});
   if(!response.ok()){let code='request-failed';try{code=(await response.json()).code??code;}catch{}throw Error(`${request.path}: HTTP ${response.status()} ${code}`);}return response.json();
  });
 }
 const author=await login('agency-admin@cover.example'),reviewer=await login('agency-reviewer@cover.example'),party=await login('servicing@cover.example');
 const route='/api/v1/agencies/'+agencyId,agency=(await get(author,route)).data;
 assert.equal(agency.state,'active');assert.match(agency.details.legalName,/^Fictional /);
 const product=(await get(author,'/api/v1/agency-product-catalog')).data.items.find(x=>x.productVersionId===productVersionId);
 assert.equal(product?.productCode,'commercial-combined');assert.equal(product.distributionEligible,true);
 const before=(await get(author,route+'/terms')).data;assert.ok(!before.nextCursor);
 const current=before.items.find(x=>x.status==='current');assert.ok(current);
 if(!current.products.some(x=>x.productVersionId===productVersionId)){
  const today=new Intl.DateTimeFormat('en-CA',{timeZone:'Europe/London',year:'numeric',month:'2-digit',day:'2-digit'}).format(new Date());
  assert.ok(current.effectiveFrom<today,'A later current-day terms version needs explicit scheduling review.');
  const data=Object.fromEntries(['commercialTerms','settlement','paymentTermsDays','creditLimit','products'].map(key=>[key,structuredClone(current[key])]));
  data.effectiveFrom=today;data.commercialTerms.effectiveFrom=today;data.reason='Adopt commercial v3 for the fictional business demonstration, retaining prior agreed terms';
  data.products=data.products.map(x=>({...x,effectiveFrom:x.effectiveFrom<today?today:x.effectiveFrom}));
  data.products.push({productVersionId,effectiveFrom:today,brokerCommissionBasisPoints:1250});
  const proposed=await command(author,'terms:propose',route+'/terms-requests',data,route);
  const requestRoute='/api/v1/agency-terms-requests/'+proposed.id;
  await command(reviewer,'terms:approve',requestRoute+'/decision',{outcome:'approve',reason:'Independent review of fictional commercial product adoption and unchanged prior product commission'},requestRoute);
 }
 const after=(await get(author,route+'/terms')).data;assert.ok(after.items.find(x=>x.status==='current').products.some(x=>x.productVersionId===productVersionId));
 for(const original of before.items){const retained=after.items.find(x=>x.id===original.id);assert.ok(retained);for(const key of ['version','effectiveFrom','commercialTerms','settlement','paymentTermsDays','creditLimit','products'])assert.deepEqual(retained[key],original[key]);}
 const client=await command(party,'client:create','/api/v1/clients',{legalName:'Fictional Commercial Demonstration',entityType:'sole-trader',address:{line1:'1 Fictional Works',town:'Sheffield',postcode:'S9 2QT',country:'GB'}},'/api/v1/clients');
 const clientRoute='/api/v1/clients/'+client.id;
 const relationship=await command(party,'relationship:create',clientRoute+'/relationships',{agencyId},clientRoute);
 const relationshipRoute='/api/v1/relationships/'+relationship.id;
 const contact=await command(party,'contact:create',relationshipRoute+'/contacts',{fullName:'Fictional Commercial Demo Customer',role:'proprietor',isPrimary:true,email:'commercial-demo@example.invalid',
  marketingConsent:{state:'not-asked',email:false,telephone:false,recordedAt:new Date().toISOString(),source:'Explicit fictional demo setup'}},relationshipRoute);
 const senior=await login('senior-underwriter@cover.example');
 const offer=(await get(senior,'/api/v1/quote-products?relationshipId='+relationship.id)).data.items.find(x=>x.productVersionId===productVersionId);assert.equal(offer?.captureEligible,true);
 const result={agencyId,agencyReference:agency.reference,clientId:client.id,clientReference:client.reference,relationshipId:relationship.id,contactId:contact.id,productVersionId};
 await writeFile(directory+'/context.json',JSON.stringify(result,null,2));console.log(JSON.stringify(result));
}finally{await browser?.close();await lock.close();await unlink(directory+'/context-running.lock');}
