import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {chromium} from 'playwright';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const output='.local/browser-evidence/renewal-preparation';await mkdir(output,{recursive:true});
const fixtures=JSON.parse(await readFile('.local/browser-evidence/underwriting-issue/report.json','utf8')).journeys;
const password=(await readFile('.local/demo-password.txt','utf8')).trim();
const browser=await chromium.launch({channel:'chrome',headless:true});
let context=await browser.newContext({viewport:{width:1560,height:1000}}),page=await context.newPage(),path;
const errors=[],report={startedAt:new Date().toISOString(),journeys:[]};
function watch(){page.setDefaultTimeout(45000);page.on('pageerror',e=>errors.push(e.message));}watch();
const button=name=>page.getByRole('button',{name,exact:true}),field=name=>page.getByLabel(name,{exact:true});
async function get(route){const response=await page.request.get(origin+route);assert.equal(response.status(),200,await response.text());assert.match(response.headers()['cache-control']??'',/no-store/);return response.json();}
async function until(route,predicate){for(let i=0;i<120;i++){const data=await get(route);if(predicate(data))return data;await page.waitForTimeout(250);}throw new Error('Saved state did not arrive: '+route);}
async function login(email){await page.goto(origin+'/login');await field('Email address').fill(email);await field('Password').fill(password);await button('Sign in').click();await page.waitForURL(origin+'/');}
async function renewLease(){const draft=await get(path);if(Date.parse(draft.lease?.expiresAt??'')<Date.now()+60000){const reply=page.waitForResponse(r=>r.url().endsWith(path+'/lease')&&r.request().method()==='PUT');await button('Renew editing lease').click();assert.equal((await reply).status(),200);}}
async function command(suffix,name,method='POST',status=201){await renewLease();const response=page.waitForResponse(r=>r.url().endsWith(path+suffix)&&r.request().method()===method);await button(name).click();const saved=await response;assert.equal(saved.status(),status,await saved.text());await page.getByText('Renewal information saved.',{exact:true}).waitFor();return saved.json();}
async function abandon(){await field('Takeover or abandonment reason').fill('Fictional renewal browser proof complete; preserve all history');await field('I confirm this draft should be abandoned.').check();await button('Abandon draft').click();await page.getByText('Abandoned',{exact:true}).waitFor();}
try{
 await login('underwriter@cover.example');
 const prior=await readFile(output+'/active.json','utf8').then(JSON.parse).catch(e=>{if(e.code==='ENOENT')return null;throw e;});
 if(prior?.createdBy==='renewal preparation browser verification'&&fixtures.some(f=>f.policyId===prior.policyId)){
  path=`/api/v1/drafts/${prior.draftId}`;const old=await get(path);assert.equal(old.policyId,prior.policyId);assert.equal(old.proposal.reason,'Fictional renewal preparation browser journey');
  if(old.state==='draft'){await page.goto(origin+`/drafts/${prior.draftId}`);if(old.lease?.active&&Date.parse(old.lease.expiresAt)>Date.now()){await field('Takeover or abandonment reason').fill('Resume owned fictional renewal browser proof');await button('Take over editing').click();}else await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();await abandon();}
 }
 for(const [index,fixture] of fixtures.entries()){
  const before=await get(`/api/v1/policies/${fixture.policyId}`);
  await page.goto(origin+`/policies/${fixture.policyId}`);await field('Draft type').selectOption('renewal');assert.equal(await field('Requested effective date').count(),0);
  await field('Reason for draft').fill('Fictional renewal preparation browser journey');await button('Create servicing draft').click();await page.waitForURL(/\/drafts\//);
  const draftId=page.url().split('/').at(-1);path=`/api/v1/drafts/${draftId}`;await writeFile(output+'/active.json',JSON.stringify({draftId,policyId:fixture.policyId,createdBy:'renewal preparation browser verification'}));
  await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
  const months=index===0?'6':'12';await field('Renewal term').selectOption(months);await command('/renewal/preparation','Save renewal preparation');
  let preparation=await get(path+'/renewal/preparation');assert.equal(preparation.current,true);assert.equal(preparation.preparation.termMonths,Number(months));assert.equal(Date.parse(preparation.preparation.term.startsAt),Date.parse(preparation.expiringEndsAt));
  assert.equal(preparation.eligibility.fairValueSatisfied,true);assert.equal(preparation.eligibility.brokerArrearsState,'unavailable');
  assert.equal(await field('Effective date basis').count(),0);assert.equal((await get(path+'/renewal/experience')).experience,null);
  if(index===1){await button('Correct policyholder details').click();await field('Trading name').fill('Fictional renewal risk comparison');await button('Apply to draft').click();await button('Save draft').click();await page.getByText('Draft action saved.',{exact:true}).waitFor();assert.ok((await get(path+'/editor')).assessment.changes.length>0);}
  await button('Rate saved renewal').click();await until(path+'/ratings',x=>x.current?.result?.outcome==='rated');
  let referrals=await get(path+'/referrals');assert.equal(referrals.items.find(x=>x.ruleCode==='UW-31-information').decisionReady,false);
  const bytes=Buffer.from('Fictional claims statement: one claim, GBP600 paid, GBP0 outstanding, GBP1000 earned.');
  await field('Claims experience evidence').setInputFiles({name:'renewal-claims.txt',mimeType:'text/plain',buffer:bytes});
  const uploadReply=page.waitForResponse(r=>r.url().endsWith(path+'/renewal/experience/uploads')&&r.request().method()==='POST');await button('Upload experience evidence').click();assert.equal((await uploadReply).status(),201);await page.getByText('New evidence uploaded. Save the experience figures to attach it to this version.',{exact:true}).waitFor();
  for(const [name,value] of [['Observation starts','2025-09-16'],['Observation ends (exclusive)','2026-09-16'],['Number of claims','1'],['Claims paid (£)','600.00'],['Outstanding claims (£)','0.00'],['Earned premium (£)','1000.00'],['Source reference','Fictional supplied renewal statement']])await field(name).fill(value);
  await command('/renewal/experience','Save supplied experience','PUT');let experience=await get(path+'/renewal/experience');assert.equal(experience.experience.paid,'600.00');assert.equal(experience.review,null);
  await page.reload();await field('Claims paid (£)').waitFor();assert.equal(await field('Claims paid (£)').inputValue(),'600.00');
  const download=await page.request.get(origin+`/api/v1/drafts/${draftId}/evidence-files/${experience.evidenceFileId}/content`);assert.equal(download.status(),200);assert.deepEqual(await download.body(),bytes);
  await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
  await field('Experience review reason').fill('Verified the fictional supplied claims statement and earned premium');await command(`/renewal/experience/${experience.experience.id}/reviews`,'Record experience review');
  await button('Rate saved renewal').click();const rated=await until(path+'/ratings',x=>x.current?.result?.outcome==='rated'&&x.current.applicable);
  assert.ok(Number(rated.current.result.premium)>0);assert.equal(rated.current.result.fee,'35.00');referrals=await get(path+'/referrals');const referral=referrals.items.find(x=>x.ruleCode==='UW-31');assert.ok(referral);assert.ok(!referrals.items.some(x=>x.ruleCode==='UW-31-information'));
  await page.getByRole('navigation',{name:'Renewal stages'}).getByRole('link',{name:'3. Issue invitation',exact:true}).click();assert.equal((await get(path)).state,'draft');assert.equal((await get(path+'/renewal/preparation')).preparation.id,preparation.preparation.id);
  await context.close();context=await browser.newContext({viewport:{width:1560,height:1000}});page=await context.newPage();watch();await login('senior-underwriter@cover.example');await page.goto(origin+`/drafts/${draftId}`);
  await field('Takeover or abandonment reason').fill('Senior review of fictional renewal claims experience');await button('Take over editing').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
  const card=page.locator(`[data-referral-id="${referral.id}"]`);await card.getByRole('checkbox').check();await field('Decision reason').fill('Senior approval of the reviewed fictional renewal loss ratio');
  const decisionReply=page.waitForResponse(r=>r.url().endsWith(path+`/referrals/${referral.id}/decisions`)&&r.request().method()==='POST');await button('Record decision for 1 selected').click();assert.equal((await decisionReply).status(),200);await page.getByText('Supporting information saved.',{exact:true}).waitFor();
  referrals=await get(path+'/referrals');assert.equal(referrals.items.find(x=>x.id===referral.id).decisionReady,true);
  await page.locator('#renewal-review-risk').scrollIntoViewIfNeeded();await page.screenshot({path:output+'/'+fixture.productCode+'-desktop.png'});
  await page.setViewportSize({width:390,height:844});await writeFile(output+'/overflow.json',JSON.stringify(await page.evaluate(()=>Array.from(document.querySelectorAll('*')).map(e=>({tag:e.tagName,cls:e.className,w:e.getBoundingClientRect().width,right:e.getBoundingClientRect().right,text:e.textContent?.slice(0,70)})).filter(e=>e.right>innerWidth+1)),null,2));assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.locator('#renewal-review-risk').scrollIntoViewIfNeeded();await page.screenshot({path:output+'/'+fixture.productCode+'-mobile.png'});await page.setViewportSize({width:1560,height:1000});
  const after=await get(`/api/v1/policies/${fixture.policyId}`);const retained=({effectiveCutoff,knownCutoff,...record})=>record;assert.deepEqual(retained(after),retained(before),'Preparation and rating preserve issued policy');
  report.journeys.push({productCode:fixture.productCode,policyId:fixture.policyId,draftId,termMonths:Number(months),preparationId:preparation.preparation.id,experienceId:experience.experience.id,cycleId:rated.current.id,referralId:referral.id,premium:rated.current.result.premium,checks:['prepared term','missing experience unresolved','real uploaded bytes','saved experience reload','reviewed experience rating','senior UW-31 decision','four stage navigation','responsive layout','issued policy unchanged']});
  await abandon();await writeFile(output+'/report.json',JSON.stringify(report,null,2));
  if(index<fixtures.length-1){await context.close();context=await browser.newContext({viewport:{width:1560,height:1000}});page=await context.newPage();watch();await login('underwriter@cover.example');}
 }
 assert.deepEqual(errors,[]);report.finishedAt=new Date().toISOString();await writeFile(output+'/report.json',JSON.stringify(report,null,2));console.log(JSON.stringify({journeys:report.journeys.length,errors}));
}catch(error){await page.screenshot({path:output+'/failure.png'}).catch(()=>{});await writeFile(output+'/failure.txt',await page.locator('body').innerText().catch(()=>''));throw error;}finally{await browser.close();}
