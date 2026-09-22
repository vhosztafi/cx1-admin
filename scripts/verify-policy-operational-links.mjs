import assert from 'node:assert/strict';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import {resolve,sep} from 'node:path';
import {chromium} from 'playwright';

// Read-only navigation regression against saved fictional demo policies.
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';
assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const output=process.argv[2];assert.ok(output&&resolve(output).startsWith(resolve('.local')+sep));
await mkdir(output,{recursive:true});
const fixtures=JSON.parse(await readFile('.local/operational-incidents-demo-v1/fixtures.json','utf8'));
assert.equal(fixtures.length,2);
const browser=await chromium.launch({headless:true});
const report={passed:false,checkedAt:new Date().toISOString(),checks:[],errors:[]};
try{
 const page=await browser.newPage({viewport:{width:1440,height:1000}});page.setDefaultTimeout(30000);
 page.on('pageerror',error=>report.errors.push(error.message));
 await page.goto(origin+'/login');await page.getByLabel('Email address',{exact:true}).fill('senior-underwriter@cover.example');
 await page.getByLabel('Password',{exact:true}).fill((await readFile('.local/demo-password.txt','utf8')).trim());
 await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');
 for(const fixture of fixtures){
  assert.match(fixture.policyId,/^[a-f0-9-]{36}$/i);
  const response=await page.request.get(origin+'/api/v1/policies/'+fixture.policyId);assert.equal(response.status(),200);
  const policy=await response.json();assert.ok(policy.reference&&policy.snapshot.productCode);
  for(const tab of ['Messages','Notes','Tasks','Claims']){
   await page.goto(origin+'/policies/'+fixture.policyId+'?tab='+tab);
   await page.getByRole('heading',{name:policy.reference,exact:true}).waitFor();
   const selected=await page.getByRole('tab',{name:tab,exact:true}).getAttribute('aria-selected');
   report.checks.push({policyId:fixture.policyId,productCode:policy.snapshot.productCode,tab,selected});
   await page.screenshot({path:output+'/'+policy.snapshot.productCode+'-'+tab+'.png'});
   assert.equal(selected,'true',policy.snapshot.productCode+' deep link must select '+tab);
   await page.getByRole('heading',{name:tab==='Notes'?'Internal notes':tab==='Tasks'?'Tasks linked to this policy':tab==='Claims'?'Claims and incidents':'Messages',exact:true}).waitFor();
  }
  await page.goto(origin+'/clients/'+policy.clientId+'?tab=Claims');
  await page.getByLabel('Find policy for claims',{exact:true}).fill(policy.reference);
  await page.getByRole('button',{name:'Find policy',exact:true}).click();
  await page.getByRole('button',{name:'Claims for '+policy.reference,exact:true}).click();
  const incidents=await page.request.get(origin+'/api/v1/incidents?policyId='+fixture.policyId+'&pageSize=20');assert.equal(incidents.status(),200);
  const saved=(await incidents.json()).items;assert.ok(saved.length);
  await page.getByRole('button',{name:saved[0].reference,exact:true}).waitFor();
  await page.getByRole('link',{name:'Open policy claims',exact:true}).click();
  await page.waitForURL(origin+'/policies/'+fixture.policyId+'?tab=Claims');
  assert.equal(await page.getByRole('tab',{name:'Claims',exact:true}).getAttribute('aria-selected'),'true');
  report.checks.push({policyId:fixture.policyId,clientId:policy.clientId,incidentId:saved[0].id,from:'client',to:'policy-claims'});
  for(let index=0;index<2;index++){
   await page.goto(origin+'/policies/'+fixture.policyId);
   const links=page.getByRole('link',{name:'Log an incident',exact:true});await links.first().waitFor();assert.equal(await links.count(),2);
   await links.nth(index).click();await page.getByRole('button',{name:'Save draft',exact:true}).waitFor();
   assert.equal(await page.getByRole('tab',{name:'Claims',exact:true}).getAttribute('aria-selected'),'true');
   await page.getByRole('button',{name:'Cancel / back to claims',exact:true}).click();
   await page.getByRole('button',{name:saved[0].reference,exact:true}).waitFor();
   report.checks.push({policyId:fixture.policyId,from:index===0?'heading':'next-actions',to:'new-incident',cancelled:true});
  }
  const after=await page.request.get(origin+'/api/v1/incidents?policyId='+fixture.policyId+'&pageSize=20');assert.equal(after.status(),200);
  assert.deepEqual((await after.json()).items.map(x=>x.id).sort(),saved.map(x=>x.id).sort(),'Opening and cancelling the editor must not save an incident');
  if(policy.snapshot.productCode.startsWith('motor-trade')){
   await page.goto(origin+'/policies/'+fixture.policyId);
   await page.getByRole('button',{name:'View vehicles and MID submissions',exact:true}).click();
   assert.equal(await page.getByRole('tab',{name:'Vehicles',exact:true}).getAttribute('aria-selected'),'true');
   await page.getByRole('heading',{name:'Vehicle register',exact:true}).waitFor();
   report.checks.push({policyId:fixture.policyId,from:'next-actions',to:'vehicle-register'});
  }
 }
 assert.deepEqual(report.errors,[]);report.passed=true;
}catch(error){report.failure=String(error);throw error;}
finally{await writeFile(output+'/report.json',JSON.stringify(report,null,2));await browser.close();}
