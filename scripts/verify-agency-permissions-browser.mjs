import {chromium} from 'playwright';
import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';
if(!['localhost','127.0.0.1'].includes(new URL(origin).hostname))throw Error('Local demo required.');
const agencyId=process.env.COVER_PERMISSION_AGENCY_ID??JSON.parse(await readFile('.local/browser-evidence/agency-lifecycle-result.json','utf8')).agencyId;
const password=(await readFile('.local/demo-password.txt','utf8')).trim();
const output='.local/browser-evidence';await mkdir(output,{recursive:true});
const browser=await chromium.launch({channel:'chrome',headless:true});const errors=[];
const url=origin+`/agents/${agencyId}?tab=Permissions%20%26%20access`;const base=`/api/v1/agencies/${agencyId}`;
async function login(role){const context=await browser.newContext({viewport:{width:1560,height:1000}});const page=await context.newPage();page.setDefaultTimeout(15000);page.on('pageerror',e=>errors.push(e.message));await page.goto(origin+'/login');await page.getByLabel('Email address').fill(role+'@cover.example');await page.getByLabel('Password',{exact:true}).fill(password);await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');await page.goto(url);return page;}
async function rows(page,path){const r=await page.request.get(origin+base+path);assert.equal(r.status(),200);return(await r.json()).items;}
try{
 const admin=await login('agency-admin');const reviewer=await login('agency-reviewer');
 // Recover only unfinished fictional grants from this same demo verifier.
 const history=await rows(admin,'/permission-requests');
 for(const grant of await rows(admin,'/permission-grants')) {
  if(grant.revokedAt||!history.some(r=>r.id===grant.requestId&&r.reason.startsWith('Fictional permission browser ')))continue;
  const csrf=(await(await admin.request.get(origin+'/api/v1/auth/csrf')).json()).requestToken;
  const recovered=await admin.request.post(origin+base+`/permission-grants/${grant.id}/revoke`,{headers:{'X-CSRF-Token':csrf,'Idempotency-Key':crypto.randomUUID(),'If-Match':grant.etag},data:{reason:'Fictional browser recovery'}});assert.equal(recovered.status(),200);
 }
 await admin.reload();await reviewer.reload();
 const reason='Fictional permission browser '+Date.now();
 await admin.getByRole('button',{name:'Request bordereau access',exact:true}).click();await admin.getByRole('dialog').waitFor();await admin.keyboard.press('Escape');assert.equal(await admin.getByRole('dialog').count(),0);
 const keys=[];let lost=false;
 await admin.route('**'+base+'/permission-requests',async route=>{if(route.request().method()!=='POST'){await route.continue();return;}keys.push(route.request().headers()['idempotency-key']);if(!lost){lost=true;const response=await route.fetch();assert.equal(response.status(),202);await route.abort('failed');}else await route.continue();});
 await admin.getByRole('button',{name:'Request bordereau access',exact:true}).click();await admin.getByRole('dialog').getByLabel('Reason',{exact:true}).fill(reason);await admin.getByRole('dialog').getByRole('button',{name:'Confirm',exact:true}).click();
 await admin.getByRole('button',{name:'Retry same request',exact:true}).waitFor();assert.equal(await admin.getByRole('dialog').getByLabel('Reason',{exact:true}).isDisabled(),true);assert.equal(await admin.getByRole('dialog').getByRole('button',{name:'Cancel',exact:true}).isDisabled(),true);
 await admin.getByRole('button',{name:'Retry same request',exact:true}).click();await admin.getByRole('region',{name:'Permission requests',exact:true}).getByText(reason,{exact:true}).waitFor();assert.equal(keys.length,2);assert.equal(keys[0],keys[1]);await admin.unroute('**'+base+'/permission-requests');
 let request=(await rows(admin,'/permission-requests')).find(x=>x.reason===reason);assert.equal(request.state,'pending');
 const ownRow=admin.getByRole('region',{name:'Permission requests',exact:true}).getByRole('row').filter({hasText:reason});assert.equal(await ownRow.getByRole('button',{name:'Approve',exact:true}).count(),0);
 await reviewer.getByRole('button',{name:'Refresh permissions',exact:true}).click();
 let reviewRow=reviewer.getByRole('region',{name:'Permission requests',exact:true}).getByRole('row').filter({hasText:reason});await reviewRow.getByRole('button',{name:'Reject',exact:true}).click();await reviewer.getByRole('dialog').getByLabel('Reason',{exact:true}).fill('Fictional independent rejection');await reviewer.getByRole('dialog').getByRole('button',{name:'Confirm',exact:true}).click();await reviewRow.getByText('rejected',{exact:true}).waitFor();
 await admin.getByRole('button',{name:'Refresh permissions',exact:true}).click();await admin.getByRole('button',{name:'Request bordereau access',exact:true}).click();await admin.getByRole('dialog').getByLabel('Reason',{exact:true}).fill(reason+' approved');await admin.getByRole('dialog').getByRole('button',{name:'Confirm',exact:true}).click();await admin.getByRole('region',{name:'Permission requests',exact:true}).getByText(reason+' approved',{exact:true}).waitFor();
 await reviewer.getByRole('button',{name:'Refresh permissions',exact:true}).click();reviewRow=reviewer.getByRole('region',{name:'Permission requests',exact:true}).getByRole('row').filter({hasText:reason+' approved'});await reviewRow.getByRole('button',{name:'Approve',exact:true}).click();await reviewer.getByRole('dialog').getByLabel('Reason',{exact:true}).fill('Fictional independent approval');await reviewer.getByRole('dialog').getByRole('button',{name:'Confirm',exact:true}).click();await reviewRow.getByText('granted',{exact:true}).waitFor();
 await admin.getByRole('button',{name:'Refresh permissions',exact:true}).click();await admin.getByRole('region',{name:'Permission grants',exact:true}).getByText('Granted · downloads unavailable',{exact:true}).waitFor();
 await admin.evaluate(()=>{document.activeElement?.blur();window.scrollTo(0,0);});await admin.screenshot({path:output+'/agency-permissions-desktop.png',fullPage:true});
 await admin.getByRole('region',{name:'Permission grants',exact:true}).getByRole('button',{name:'Revoke',exact:true}).click();await admin.getByRole('dialog').getByLabel('Reason',{exact:true}).fill(reason+' revoked');await admin.getByRole('dialog').getByRole('button',{name:'Confirm',exact:true}).click();await admin.getByRole('region',{name:'Permission grants',exact:true}).getByRole('row').filter({hasText:reason+' revoked'}).waitFor();
 const grants=await rows(admin,'/permission-grants');const grant=grants.find(x=>x.revocationReason===reason+' revoked');assert.ok(grant.revokedAt);assert.ok(grant.grantedByLabel);assert.ok(grant.revokedByLabel);
 await admin.setViewportSize({width:390,height:844});assert.ok(await admin.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));await admin.screenshot({path:output+'/agency-permissions-mobile.png',fullPage:true});
 const limited=await login('underwriter');await limited.getByText('Permission administration requires agency administrator access. Use Preview shared data to view the agency’s sharing reference.',{exact:true}).waitFor();assert.equal(await limited.getByRole('button',{name:'Request bordereau access',exact:true}).count(),0);
 assert.deepEqual(errors,[]);await writeFile(output+'/agency-permissions-result.json',JSON.stringify({agencyId,requestId:request.id,grantId:grant.id,passed:true,checks:['real persisted request/reject/approve/revoke','actual actor labels','lost response immutable retry','own approval hidden','read-only role','modal escape','390px contained tables']},null,2));console.log('Agency permission browser checks passed.');
}finally{await browser.close();}
