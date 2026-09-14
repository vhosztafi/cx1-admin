import {chromium} from 'playwright';
import assert from 'node:assert/strict';
import {readFile,mkdir} from 'node:fs/promises';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';
if(!['localhost','127.0.0.1'].includes(new URL(origin).hostname))throw Error('Local demo only.');
const run=promisify(execFile);
async function fixture(){const {stdout}=await run('dotnet',['backend/src/BackOffice.Api/bin/Debug/net10.0/BackOffice.Api.dll','--seed-agency-invitation-demo'],{env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',DOTNET_ENVIRONMENT:'Development'},windowsHide:true,maxBuffer:8*1024*1024});return JSON.parse(stdout.trim().split(/\r?\n/).at(-1)).invitationId;}
const password=(await readFile('.local/demo-password.txt','utf8')).trim();const output='.local/browser-evidence';await mkdir(output,{recursive:true});
const browser=await chromium.launch({channel:'chrome',headless:true});const admin=await browser.newContext();const context=await browser.newContext({viewport:{width:1440,height:960}});const page=await context.newPage();page.setDefaultTimeout(20000);let errors=0;page.on('pageerror',()=>errors++);
let stage='login';
try{
 const login=await admin.newPage();await login.goto(origin+'/login');await login.getByLabel('Email address',{exact:true}).fill('agency-admin@cover.example');await login.getByLabel('Password',{exact:true}).fill(password);await login.getByRole('button',{name:'Sign in',exact:true}).click();await login.waitForURL(origin+'/');
 async function reveal(){const id=await fixture();const csrf=await(await admin.request.get(origin+'/api/v1/auth/csrf')).json();const response=await admin.request.post(`${origin}/api/v1/invitations/${id}/demo-link`,{headers:{'X-CSRF-Token':csrf.requestToken},data:{}});assert.equal(response.status(),200);return(await response.json()).invitationToken;}
 stage='reveal and prepare'; const raw=await reveal();await page.goto(origin+'/invitations/accept#'+raw);await page.getByLabel('New password',{exact:true}).waitFor();assert.ok(!new URL(page.url()).hash);
 assert.ok(!((await page.content()).includes(raw)));assert.equal(await page.locator('meta[name="referrer"]').getAttribute('content'),'no-referrer');
 assert.ok(await page.evaluate(value=>!JSON.stringify({...localStorage,...sessionStorage}).includes(value),raw));
 stage='password mismatch'; await page.getByLabel('New password',{exact:true}).fill('A fictional browser passphrase!');await page.getByLabel('Confirm password',{exact:true}).fill('Mismatch fictional phrase!');await page.getByRole('button',{name:'Set password',exact:true}).click();await page.getByText('The passwords do not match.',{exact:true}).waitFor();
 stage='successful acceptance'; await page.getByLabel('Confirm password',{exact:true}).fill('A fictional browser passphrase!');await page.getByRole('button',{name:'Set password',exact:true}).click();await page.getByRole('heading',{name:'Password saved',exact:true}).waitFor();assert.equal(await page.locator('input[type=password]').count(),0);assert.equal((await context.request.get(origin+'/api/v1/account')).status(),401);
 stage='success screenshot'; await page.screenshot({path:`${output}/invitation-accepted-desktop.png`,fullPage:true});await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth),true);await page.screenshot({path:`${output}/invitation-accepted-mobile.png`,fullPage:true});
 stage='reused invitation'; await page.goto(origin+'/invitations/accept#'+raw);await page.getByLabel('New password',{exact:true}).fill('A fictional replacement phrase!');await page.getByLabel('Confirm password',{exact:true}).fill('A fictional replacement phrase!');await page.getByRole('button',{name:'Set password',exact:true}).click();await page.getByRole('heading',{name:'Invitation unavailable',exact:true}).waitFor();
 await page.goto(origin+'/invitations/accept');await page.getByRole('heading',{name:'Invitation unavailable',exact:true}).waitFor();
 stage='uncertain response'; const lost=await reveal();await page.goto(origin+'/invitations/accept#'+lost);await page.getByLabel('New password',{exact:true}).fill('A fictional uncertain phrase!');await page.getByLabel('Confirm password',{exact:true}).fill('A fictional uncertain phrase!');
 await page.route('**/api/v1/auth/invitations/accept',async route=>{const response=await route.fetch();assert.equal(response.status(),200);await route.abort('failed');});
 await page.getByRole('button',{name:'Set password',exact:true}).click();await page.getByText(/We could not confirm password setup/).waitFor();assert.equal(await page.getByLabel('New password',{exact:true}).inputValue(),'');assert.equal(await page.getByLabel('Confirm password',{exact:true}).inputValue(),'');
 assert.equal(errors,0);console.log('Invitation browser passed: URL cleanup, secret-free storage/markup, password confirmation, one-time acceptance, no automatic session, mobile layout and uncertain-response handling.');
}catch{throw Error(`Invitation browser verification failed at ${stage}; secret-bearing details intentionally omitted.`);}
finally{await context.close();await admin.close();await browser.close();}
