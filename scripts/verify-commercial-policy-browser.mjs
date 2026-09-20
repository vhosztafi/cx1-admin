import assert from 'node:assert/strict';
import {writeFile} from 'node:fs/promises';
import {underwritingResponseValidator} from './validate-underwriting-response.mjs';

export async function commercialPolicyJourney({page,f,policy,checks}) {
 const tabs=['Overview','Risk details','Cover','Property schedule','Liability & employees','Business interruption','Transactions','Documents','History'];
 const money=value=>'£'+Number(value).toLocaleString('en-GB',{minimumFractionDigits:2,maximumFractionDigits:2});
 const panel=page.getByRole('tabpanel');
 for(const width of [1480,390]) {
  await page.setViewportSize({width,height:width===390?844:980});
  for(const name of tabs) {
   await page.getByRole('tab',{name,exact:true}).click();
   assert.equal(await page.getByRole('tab',{name,exact:true}).getAttribute('aria-selected'),'true');
   if(name==='Property schedule') {
    await page.getByRole('table',{name:'Commercial property schedule',exact:true}).waitFor();
    for(const location of policy.snapshot.risk.locations) {
     const row=page.getByRole('table',{name:'Commercial property schedule',exact:true}).getByRole('row').filter({hasText:location.reference});
     assert.ok((await row.innerText()).includes(location.address.postcode));assert.ok((await row.innerText()).includes(money(location.buildings)));
    }
   }
   if(name==='Liability & employees') for(const wages of policy.snapshot.risk.wages) assert.ok((await panel.innerText()).includes(money(wages.employees)));
   if(name==='Business interruption') {assert.ok((await panel.innerText()).includes(money(policy.snapshot.risk.businessInterruption.sumInsured)));assert.ok((await panel.innerText()).includes(policy.snapshot.risk.businessInterruption.indemnityMonths+' months'));}
   if(name==='History') await page.getByRole('button',{name:'Open version 1',exact:true}).waitFor();
   assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),true,`${name} ${width}px page overflow`);
   if(width===390&&['Property schedule','History','Liability & employees'].includes(name)){await page.evaluate(()=>scrollTo(0,0));await page.screenshot({path:f.output+`/policy-${name.split(' ')[0].toLowerCase()}-390.png`,fullPage:true});}
  }
 }
 assert.equal(await page.getByRole('tab',{name:/Drivers|Vehicles|MID/}).count(),0);
 await page.getByRole('button',{name:'Open version 1',exact:true}).click();
 await page.getByRole('heading',{name:'New-business transaction',exact:true}).waitFor();
 await page.getByRole('button',{name:'View current policy',exact:true}).click();
 const base=`/api/v1/policies/${policy.id}`,validate=await underwritingResponseValidator('CommercialCaptureExposure');
 const observations=[];
 for(const [effectiveAt,knownAt,label] of [[f.clockNow,f.clockNow,'Inception scheduled'],[policy.snapshot.term.startsAt,f.clockNow,'In force'],[policy.snapshot.term.endsAt,f.clockNow,'Term ended']]) {
  await page.getByLabel('Effective date and time (UTC)',{exact:true}).fill(new Date(effectiveAt).toISOString().slice(0,16));
  await page.getByLabel('Known-at date and time (UTC)',{exact:true}).fill(new Date(knownAt).toISOString().slice(0,16));
  await page.getByRole('button',{name:'View selected dates',exact:true}).click();
  await page.getByRole('region',{name:'Issued policy'}).getByText(label,{exact:true}).waitFor();
  await page.getByRole('tab',{name:'Property schedule',exact:true}).click();
  await page.getByRole('table',{name:'District exposure at selected dates',exact:true}).waitFor();
  const query=new URLSearchParams({effectiveAt,knownAt});
  const response=await page.request.get(f.apiOrigin+base+'/commercial-exposure?'+query);
  assert.equal(response.status(),200,await response.text());assert.equal(response.headers()['cache-control'],'no-store');
  const exposure=await response.json();assert.ok(validate(exposure),JSON.stringify(validate.errors));
  assert.equal(exposure.source.id,policy.versionId);assert.equal(exposure.source.hash,policy.contentHash);
  for(const row of exposure.districts)assert.equal(row.ownProposedSumInsured=== '0.00',label!=='In force');
  const retained=await page.request.get(f.apiOrigin+base+'/as-at?'+query);assert.equal(retained.status(),200);
  assert.deepEqual((await retained.json()).snapshot,policy.snapshot);observations.push(exposure);
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),true);
 }
 await page.getByLabel('Known-at date and time (UTC)',{exact:true}).fill(new Date(Date.parse(policy.issuedAt)-60000).toISOString().slice(0,16));
 await page.getByRole('button',{name:'View selected dates',exact:true}).click();
 await page.getByRole('heading',{name:'No cover recorded at these dates',exact:true}).waitFor();
 await page.getByRole('button',{name:'View current policy',exact:true}).click();
 await page.getByRole('heading',{name:policy.reference,exact:true}).waitFor();
 await writeFile(f.output+'/commercial-policy-observations.json',JSON.stringify(observations,null,2));
 checks.push('All nine Commercial Combined tabs at desktop and390px; exact saved location/wage/BI amounts; issued history selection; actual contracted E/K exposure source and scheduled/active/expired/no-cover views');
}
if(process.argv[1]?.endsWith('verify-commercial-policy-browser.mjs')) {
 process.argv.push('--stage','issue','--policy');await import('./verify-commercial-capture-browser.mjs');
}
