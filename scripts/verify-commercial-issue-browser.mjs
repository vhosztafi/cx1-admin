import assert from 'node:assert/strict';
import {writeFile} from 'node:fs/promises';
import {underwritingResponseValidator} from './validate-underwriting-response.mjs';

export async function commercialIssueJourney({page,f,quoteId,checks}) {
  await page.getByLabel('Issue reason',{exact:true}).fill('Issue the reviewed fictional Commercial Combined policy');
  await page.getByRole('button',{name:'Review policy issue',exact:true}).click();
  const pending=page.waitForResponse(response=>response.url().endsWith(`/quotes/${quoteId}/issue`)&&response.request().method()==='POST');
  await page.getByRole('button',{name:'Confirm action',exact:true}).click();
  const response=await pending;assert.equal(response.status(),201,await response.text());
  const receipt=await response.json(), validateReceipt=await underwritingResponseValidator('UnderwritingIssueResult');
  assert.ok(validateReceipt(receipt),JSON.stringify(validateReceipt.errors));assert.match(receipt.policyReference,/^PL-CC-\d{10}$/);
  await page.waitForURL(new RegExp(`/policies/${receipt.policyId}$`));
  await page.getByRole('heading',{name:receipt.policyReference,exact:true}).waitFor();
  async function read() {const result=await page.request.get(f.apiOrigin+`/api/v1/policies/${receipt.policyId}`);assert.equal(result.status(),200,await result.text());return result.json();}
  const policy=await read(),validate=await underwritingResponseValidator('CommercialFirstPolicyView');
  assert.ok(validate(policy),JSON.stringify(validate.errors));assert.equal(policy.snapshot.productCode,'commercial-combined');
  assert.equal(policy.commercialExposureDecisionId,receipt.commercialExposureDecisionId);
  assert.equal(policy.versionId,receipt.versionId);assert.equal(policy.sourceQuoteId,quoteId);
  assert.deepEqual(policy.documentRequests.map(x=>x.id).sort(),receipt.documentRequestIds.slice().sort());
  assert.equal(policy.documentRequests.filter(x=>x.kind==='policy-certificate').length,policy.snapshot.cover.sections.some(x=>x.code==='employers-liability')?1:0);
  assert.equal('vehicles' in policy.snapshot.risk,false);
  assert.equal(await page.getByRole('button',{name:'Make a policy change',exact:true}).count(),1);
  assert.equal(await page.getByRole('button',{name:/Cancel policy|Renew policy/}).count(),0);
  await page.getByRole('tab',{name:'Property schedule',exact:true}).click();
  await page.getByRole('heading',{name:'Locations and sums insured',exact:true}).waitFor();
  await page.getByRole('tab',{name:'Cover',exact:true}).click();
  await page.getByRole('heading',{name:'Selected commercial cover',exact:true}).waitFor();
  await page.getByRole('tab',{name:'Documents',exact:true}).click();
  await page.getByRole('rowheader',{name:'Employers’ liability certificate',exact:true}).waitFor();
  const requestRows=page.getByRole('table',{name:'Policy document requests',exact:true}).locator('tbody tr');
  assert.deepEqual((await requestRows.evaluateAll(rows=>rows.map(row=>row.getAttribute('data-request-id')))).sort(),policy.documentRequests.map(x=>x.id).sort());
  assert.ok((await requestRows.evaluateAll(rows=>rows.map(row=>row.getAttribute('data-version-id')))).every(id=>id===policy.versionId));
  await page.reload();await page.getByRole('heading',{name:receipt.policyReference,exact:true}).waitFor();
  assert.deepEqual(await read(),policy);
  const {commercialPolicyJourney}=await import('./verify-commercial-policy-browser.mjs');
  await commercialPolicyJourney({page,f,policy,checks});
  await page.screenshot({path:f.output+'/issued-commercial-desktop.png',fullPage:true});
  await page.setViewportSize({width:390,height:844});
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),true);
  await page.screenshot({path:f.output+'/issued-commercial-mobile.png',fullPage:true});
  await page.screenshot({path:f.output+'/issued-commercial-mobile-viewport.png'});
  await page.setViewportSize({width:1480,height:980});
  if(f.renewal){const {commercialRenewalJourney}=await import('./verify-commercial-renewal-browser.mjs');await commercialRenewalJourney({page,f,policy,checks});}
  else if(f.servicing){const {commercialServicingJourney}=await import('./verify-commercial-servicing-editor-browser.mjs');await commercialServicingJourney({page,f,policy,checks});}
  await page.goto(f.webOrigin+`/quotes/${quoteId}`);await page.getByRole('link',{name:'Open issued policy',exact:true}).waitFor();
  await writeFile(f.output+'/issued-commercial.json',JSON.stringify({receipt,policy},null,2));
  checks.push('Actual UI Commercial Combined issue; validated receipt and immutable policy API; exposure decision identity, selected EL documents and source quote link; persisted reload and desktop/390px views');
}

if(process.argv[1]?.endsWith('verify-commercial-issue-browser.mjs')) {
  process.argv.push('--stage','issue');await import('./verify-commercial-capture-browser.mjs');
}
