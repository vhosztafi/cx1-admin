import assert from 'node:assert/strict';
import {commercialServicingIssueJourney} from './verify-commercial-servicing-issue-browser.mjs';

export async function commercialRenewalJourney({page,f,policy,checks}) {
 const button=name=>page.getByRole('button',{name,exact:true}),field=name=>page.getByLabel(name,{exact:true});
 async function action(suffix,method,click,status=201){
  const response=page.waitForResponse(r=>new URL(r.url()).pathname.endsWith(suffix)&&r.request().method()===method);
  await click();const result=await response;assert.equal(result.status(),status,await result.text());return result.json();
 }
 await page.goto(f.webOrigin+`/policies/${policy.id}`);await button('Make a policy change').click();
 await field('Draft type').selectOption('renewal');await field('Reason for draft').fill('Fictional commercial renewal browser verification');
 const created=await action(`/terms/${policy.termId}/drafts`,'POST',()=>button('Create servicing draft').click());
 const draftId=created.id;assert.ok(draftId);await page.waitForURL(`**/drafts/${draftId}`);
 await page.getByRole('heading',{name:'Commercial policy renewal',exact:true}).waitFor();
 await action('/lease','POST',()=>button('Acquire editing lease').click(),200);
 await field('Renewal term').selectOption('12');
 await action('/renewal/preparation','POST',()=>button('Save renewal preparation').click());
 await page.getByRole('region',{name:'Commercial experience scope'}).waitFor();
 await field('Claims experience evidence').setInputFiles({name:'fictional-commercial-renewal.txt',mimeType:'text/plain',buffer:Buffer.from('Fictional whole commercial risk zero-loss experience for business demonstration.')});
 await action('/renewal/experience/uploads','POST',()=>button('Upload experience evidence').click());
 for(const [name,value] of [['Observation starts','2025-01-01'],['Observation ends (exclusive)','2026-01-01'],['Number of claims','0'],['Claims paid (£)','0.00'],['Outstanding claims (£)','0.00'],['Earned premium (£)','1000.00'],['Source reference','Fictional commercial renewal experience']])await field(name).fill(value);
 const experience=await action('/renewal/experience','PUT',()=>button('Save supplied experience').click());
 await field('Experience review reason').fill('Reviewed fictional whole-risk commercial renewal experience');
 await action(`/renewal/experience/${experience.resourceId}/reviews`,'POST',()=>button('Record experience review').click());
 const storedResponse=await page.request.get(f.apiOrigin+`/api/v1/drafts/${draftId}/renewal/experience`);assert.equal(storedResponse.status(),200);
 const stored=await storedResponse.json();assert.equal(stored.review.outcome,'accepted');assert.equal(stored.experience.claimCount,0);
 assert.deepEqual(stored.experience.commercialSubjects,stored.currentCommercialSubjects);
 await commercialServicingIssueJourney({page,f,policy,draftId,checks,renewal:true,leaseOwned:true});
 const current=await (await page.request.get(f.apiOrigin+`/api/v1/policies/${policy.id}`)).json();assert.equal(current.versionId,policy.versionId);
 checks.push('Commercial renewal actual UI creation, full-term preparation, owned zero-loss experience review and early issue preserve current policy');
}

if(process.argv[1]?.endsWith('verify-commercial-renewal-browser.mjs')){process.argv.push('--stage','issue','--renewal');await import('./verify-commercial-capture-browser.mjs');}
