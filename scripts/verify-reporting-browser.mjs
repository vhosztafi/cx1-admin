import {chromium} from 'playwright';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import assert from 'node:assert/strict';
const fixture=JSON.parse(await readFile('.local/phase11-fixture/fixture.json','utf8'));
assert.match(fixture.database,/^CoverMGA_Test_Phase11_[a-f0-9]{32}$/);
const origin='http://localhost:3193',password=(await readFile('.local/phase11-fixture/password.txt','utf8')).trim();
const browser=await chromium.launch({channel:'chrome',headless:true});const page=await browser.newPage({viewport:{width:1440,height:1000}});page.setDefaultTimeout(60000);
const evidence={passed:false,checks:[]};
try {
 await page.goto(origin+'/login');await page.getByLabel('Email address',{exact:true}).fill('underwriter@cover.example');await page.getByLabel('Password',{exact:true}).fill(password);await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');
 await page.goto(origin+'/search');await page.getByRole('table',{name:'Search results',exact:true}).waitFor();
 const all=await page.evaluate(async()=>{const r=await fetch('/api/v1/search');if(!r.ok)throw Error(String(r.status));return r.json();});assert.ok(all.items.length);assert.ok(all.total>=all.items.length);
 await page.getByLabel('Record type',{exact:true}).selectOption('client');const applied=page.waitForResponse(r=>r.url().includes('/api/v1/search?')&&r.url().includes('kind=client'));await page.getByRole('button',{name:'Apply filters',exact:true}).click();const response=await applied;assert.equal(response.status(),200);const filtered=await response.json();assert.ok(filtered.items.length,JSON.stringify({url:response.url(),filtered,all}));assert.ok(filtered.items.every(x=>x.kind==='client'));
 await page.getByRole('table',{name:'Search results',exact:true}).getByRole('link').first().click();await page.waitForURL('**/clients/*');assert.equal(new URL(page.url()).pathname,filtered.items[0].href);evidence.checks.push('saved client search, kind filter and authorised detail link');
 await page.goto(origin+'/search?q='+encodeURIComponent(filtered.items[0].reference));await page.getByRole('table',{name:'Search results',exact:true}).waitFor();await page.reload();await page.getByRole('link',{name:filtered.items[0].reference,exact:true}).first().waitFor();evidence.checks.push('reference search survives reload');
 await mkdir('output/playwright',{recursive:true});await page.screenshot({path:'output/playwright/phase12-search.png',fullPage:true});evidence.passed=true;
} finally {await mkdir('.local/phase12-tests',{recursive:true});await writeFile('.local/phase12-tests/search-browser.json',JSON.stringify(evidence,null,2));await browser.close();}
console.log(JSON.stringify(evidence));
