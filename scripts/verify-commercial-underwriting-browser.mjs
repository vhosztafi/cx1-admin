import assert from 'node:assert/strict';
import {randomUUID} from 'node:crypto';
import {writeFile} from 'node:fs/promises';
import {underwritingResponseValidator} from './validate-underwriting-response.mjs';

export async function commercialTermsJourney({page,f,quoteId,checks}) {
  const root='/api/v1', quote=`${root}/quotes/${quoteId}`;
  async function read(path) { const response=await page.request.get(f.apiOrigin+path);assert.equal(response.status(),200,await response.text());return response.json(); }
  const assess=()=>read(quote+'/underwriting');
  async function post(path,data,status=200,multipart) {
    const current=await assess(),csrf=await read(root+'/auth/csrf');
    const response=await page.request.post(f.apiOrigin+path,{headers:{'If-Match':current.quoteEtag,'Idempotency-Key':randomUUID(),'X-CSRF-Token':csrf.requestToken},...(multipart?{multipart}:{data})});
    assert.equal(response.status(),status,await response.text());return response.json();
  }
  const file=await post(quote+'/underwriting/evidence-files',null,201,{fileName:'fictional-commercial-proof.txt',contentType:'text/plain',file:{name:'fictional-commercial-proof.txt',mimeType:'text/plain',buffer:Buffer.from('Fictional commercial evidence for browser acceptance')}});
  async function proof(purpose) {
    const current=await assess();
    const attached=await post(quote+'/underwriting/evidence',{cycleId:current.context.cycleId,fileId:file.id,requirementCode:purpose.code,inputFingerprint:purpose.inputFingerprint,
      ...Object.fromEntries(['riskItemId','conditionId','termsVersionId','capacitySubmissionId'].filter(x=>purpose[x]).map(x=>[x,purpose[x]])),reason:'Exact Commercial Combined purpose'},201);
    const evidence=(await read(quote+'/underwriting/evidence?pageSize=100')).items.find(x=>x.id===attached.id);assert.ok(evidence);
    await post(quote+`/underwriting/evidence/${attached.id}/reviews`,{cycleId:current.context.cycleId,associationEtag:evidence.etag,outcome:'accepted',expectedFingerprint:purpose.inputFingerprint,reason:'Independent review of the current supplied proof'});
    return attached.id;
  }
  for(const requirement of (await assess()).proofRequirements.filter(x=>x.code.startsWith('cc-'))) await proof(requirement);
  const referrals=()=>read(root+`/referrals?quoteId=${quoteId}&pageSize=100`);
  const carrierRows=(await referrals()).items.filter(x=>['single-location','maximum-estimated-loss'].includes(x.dimension));assert.equal(carrierRows.length,2);
  async function confirm(path,status=200) {
    const pending=page.waitForResponse(r=>r.url().includes(path)&&r.request().method()==='POST');
    await page.getByRole('button',{name:'Confirm action',exact:true}).click();
    const response=await pending;assert.equal(response.status(),status,await response.text());await page.getByRole('dialog').waitFor({state:'hidden'});return response.json();
  }
  for(const referral of carrierRows) {
    const current=await assess();
    const created=await post(root+`/referrals/${referral.id}/escalations`,{cycleId:current.context.cycleId,referralEtag:referral.etag,providerId:current.providerId,reason:'Exact location carrier exception'},201);
    await page.goto(f.webOrigin+`/escalations/${created.id}`);
    await page.getByLabel('Capacity submission message',{exact:true}).fill('Review this saved commercial location and its exact extent.');
    await page.getByLabel('Capacity demo scenario',{exact:true}).selectOption({label:'Demo: cc conditional proof · v1'});
    await page.getByRole('button',{name:'Review submission',exact:true}).click();await confirm(`/escalations/${created.id}/send`,202);
    let stored;
    for(let attempt=0;attempt<120;attempt++) {stored=await read(root+`/escalations/${created.id}`);if(stored.state==='conditional')break;assert.notEqual(stored.state,'failed');await new Promise(resolve=>setTimeout(resolve,250));}
    assert.equal(stored.state,'conditional');assert.equal(stored.riskItemId,referral.targetId);
    const valid=await underwritingResponseValidator('UnderwritingEscalationView');assert.ok(valid(stored),JSON.stringify(valid.errors));
    await page.reload();await page.getByRole('heading',{name:'Capacity escalation',exact:true}).waitFor();await page.getByText('Request subject: LOC-1',{exact:true}).waitFor();
    await page.screenshot({path:f.output+`/carrier-${referral.dimension}.png`,fullPage:true});
    await writeFile(f.output+`/carrier-${referral.dimension}.json`,JSON.stringify(stored,null,2));
  }
  assert.equal((await assess()).capabilities.canPrepareTerms,false);
  for(const purpose of (await assess()).proofRequirements.filter(x=>x.conditionId)) {
    const evidenceId=await proof(purpose);
    const rows=(await referrals()).items;
    const referral=rows.find(x=>x.conditions.some(c=>c.id===purpose.conditionId));assert.ok(referral);
    const condition=referral.conditions.find(x=>x.id===purpose.conditionId);
    await post(root+`/referrals/${referral.id}/conditions/${condition.id}/resolutions`,{cycleId:(await assess()).context.cycleId,conditionEtag:condition.etag,evidenceAssociationId:evidenceId,outcome:'satisfied',reason:'Resolve exact carrier condition'});
  }
  const current=await assess();
  await post(quote+'/referral-decisions',{cycleId:current.context.cycleId,decisions:(await referrals()).items.map(x=>({referralId:x.id,etag:x.etag,outcome:'approve',reason:'Separate internal Commercial Combined decision'}))});
  async function quotation() {await page.goto(f.webOrigin+`/quotes/${quoteId}`);await page.getByRole('tab',{name:'Quotation',exact:true}).click();await page.getByLabel('Quotation template',{exact:true}).waitFor();}
  await quotation();await page.getByLabel('Quotation template',{exact:true}).selectOption({index:1});
  await page.getByRole('button',{name:'Prepare exact terms',exact:true}).click();await confirm('/terms/prepare',201);
  let assessed=await assess();assert.ok(assessed.termsVersionId);assert.equal(assessed.capabilities.canAccept,false);
  await proof(assessed.proofRequirements.find(x=>x.code==='signed-statement'&&!x.conditionId));
  await quotation();await page.getByRole('checkbox',{name:/Fictional CC browser customer/}).check();
  await page.getByRole('button',{name:'Review quotation delivery',exact:true}).click();await confirm('/terms',202);
  let history;
  for(let attempt=0;attempt<120;attempt++) {history=await read(quote+'/terms');if(history.deliveries.some(x=>x.state==='delivered'))break;await new Promise(resolve=>setTimeout(resolve,250));}
  assert.ok(history.deliveries.some(x=>x.state==='delivered'));
  assessed=await assess();const acceptanceProof=await proof(assessed.proofRequirements.find(x=>x.code==='acceptance-proof'));
  await quotation();await page.getByLabel('Accepted by',{exact:true}).fill('Fictional CC browser customer');
  await page.getByLabel('Acceptance received at',{exact:true}).fill(new Date(f.clockNow).toISOString().slice(0,19)+'Z');
  await page.getByLabel('Reviewed acceptance evidence',{exact:true}).selectOption(acceptanceProof);
  await page.getByRole('button',{name:'Review acceptance',exact:true}).click();await confirm('/acceptances',201);
  await quotation();await page.getByText('Current acceptance recorded',{exact:true}).waitFor();
  assessed=await assess();assert.equal(assessed.state,'accepted');assert.equal(assessed.capabilities.canIssue,false);
  history=await read(quote+'/terms');
  const valid=await underwritingResponseValidator('UnderwritingTermsView');for(const terms of history.terms)assert.ok(valid(terms),JSON.stringify(valid.errors));
  await page.screenshot({path:f.output+'/accepted-terms-desktop.png',fullPage:true});
  await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),true);
  await page.screenshot({path:f.output+'/accepted-terms-mobile.png',fullPage:true});await page.evaluate(()=>window.scrollTo(0,0));await page.screenshot({path:f.output+'/accepted-terms-mobile-viewport.png'});await page.setViewportSize({width:1480,height:980});
  await writeFile(f.output+'/accepted-terms.json',JSON.stringify({assessment:assessed,history},null,2));
  checks.push('Actual completed CC capture -> exact location demo carrier submission through UI -> conditional response -> scoped API proof/review and independent internal decision -> UI prepared, delivered and accepted terms; API schemas and desktop/390px readback; issue remains unavailable');
}

if(process.argv[1]?.endsWith('verify-commercial-underwriting-browser.mjs')) {
  process.argv.push('--stage','terms');await import('./verify-commercial-capture-browser.mjs');
}
