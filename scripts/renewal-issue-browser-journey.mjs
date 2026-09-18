import assert from 'node:assert/strict';

// Called only after the real renewal preparation/rating/review UI journey.
export async function issueRenewal({page,origin,path,get,until,renewLease,output,productCode}) {
 const button=name=>page.getByRole('button',{name,exact:true}),field=name=>page.getByLabel(name,{exact:true});
 async function command(suffix,click,status=201){
  await renewLease();const current=await page.request.get(origin+path);
  await page.waitForFunction(etag=>document.querySelector('[data-servicing-draft-etag]')?.getAttribute('data-servicing-draft-etag')===etag,current.headers().etag);
  const pending=page.waitForResponse(r=>r.url().endsWith(path+suffix)&&r.request().method()==='POST');
  await click();const response=await pending;assert.equal(response.status(),status,await response.text());
  await page.getByText('Supporting information saved.',{exact:true}).waitFor();return response.json();
 }
 await field('Supporting document').setInputFiles({name:'fictional-renewal-contract.txt',mimeType:'text/plain',buffer:Buffer.from('Fictional renewal supporting proof, signed statement and evidenced acceptance. Demo only.')});
 const upload=await command('/evidence/uploads',()=>button('Upload document').click());
 async function proof(required){
  const group=page.locator(`[data-requirement-code="${required.code}"][data-risk-item-id="${required.riskItemId??''}"]`);
  await group.getByLabel('Saved document',{exact:true}).selectOption(upload.id);
  await group.getByLabel('Attachment reason',{exact:true}).fill('Fictional evidence for this exact renewal requirement');
  const association=await command('/evidence',()=>group.getByRole('button',{name:'Attach proof',exact:true}).click());
  const card=page.locator(`[data-evidence-id="${association.id}"]`);
  await card.getByLabel('Review or withdrawal reason',{exact:true}).fill('Verified fictional renewal document against the retained requirement');
  await command(`/evidence/${association.id}/reviews`,()=>card.getByRole('button',{name:'Record review',exact:true}).click(),200);
  return association.id;
 }
 for(const item of (await get(path+'/evidence/requirements')).requirements)await proof(item.requirement);
 const prepared=await command('/terms/prepare',()=>button('Prepare renewal invitation').click());
 let view=await get(path+'/terms');assert.equal(view.terms.id,prepared.id);assert.equal(view.terms.document.format,'renewal-contract-1');assert.equal(view.acceptance,null);
 await proof((await get(path+'/evidence/requirements')).requirements.find(x=>x.requirement.code==='signed-statement').requirement);
 const panel=page.getByRole('region',{name:'Renewal invitation and acceptance',exact:true});
 await panel.getByRole('group',{name:'Send prepared terms',exact:true}).getByRole('checkbox').first().check();
 await command('/terms/send',()=>button('Send renewal invitation').click(),202);
 view=await until(path+'/terms',x=>x.delivery?.state==='delivered');assert.equal(view.acceptance,null);
 const acceptanceProof=await proof((await get(path+'/evidence/requirements')).requirements.find(x=>x.requirement.code==='acceptance-proof').requirement);
 await field('Accepted by').fill('Fictional renewal customer');
 const local=new Date();local.setMinutes(local.getMinutes()-local.getTimezoneOffset());
 await field('Acceptance received at').fill(local.toISOString().slice(0,19));await field('Reviewed acceptance proof').selectOption(acceptanceProof);
 const acceptance=await command('/acceptances',()=>button('Record renewal acceptance').click());
 view=await get(path+'/terms');assert.equal(view.acceptance.id,acceptance.id);assert.equal(view.acceptanceApplicable,true);
 await panel.evaluate(el=>window.scrollTo(0,el.getBoundingClientRect().top+scrollY-90));await page.screenshot({path:output+'/'+productCode+'-invitation.png'});
 await field('Issue reason').fill('Issue the accepted fictional renewal with current cover preserved');
 await field('I confirm the accepted changes and effective dates.').check();
 const response=page.waitForResponse(r=>r.url().endsWith(path+'/issue')&&r.request().method()==='POST');
 await button('Issue accepted renewal').click();const result=await response;assert.equal(result.status(),201,await result.text());const receipt=await result.json();
 await page.getByText('Renewal issued',{exact:true}).waitFor();
 const issued=await get(`/api/v1/policies/${receipt.policyId}/terms/${receipt.termId}/versions/${receipt.versionId}`);
 assert.equal(issued.transactionId,receipt.transactionId);assert.equal(issued.financials.journalId,receipt.journalId);
 assert.equal(issued.snapshot.premium.termPremium,view.terms.document.price.premium);
 assert.equal((await get(path)).state,'issued');
 await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 await page.getByRole('article',{name:'Renewal issue receipt',exact:true}).scrollIntoViewIfNeeded();await page.screenshot({path:output+'/'+productCode+'-issued-mobile.png'});
 await page.setViewportSize({width:1560,height:1000});
 return {receipt,termsId:prepared.id,deliveryId:view.delivery.id,acceptanceId:acceptance.id};
}
