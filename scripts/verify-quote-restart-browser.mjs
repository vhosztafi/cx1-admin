import {chromium} from 'playwright';
import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
const mode=process.argv[2];assert.ok(['capture','verify'].includes(mode));assert.equal(process.argv.length,3);
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const file='.local/browser-evidence/quote-ready/restart.json',records=JSON.parse(await readFile('.local/browser-evidence/quote-ready/report.json','utf8'));
assert.equal(records.length,2);assert.equal(new Set(records.map(x=>x.productCode)).size,2);
const password=(await readFile('.local/demo-password.txt','utf8')).trim(),browser=await chromium.launch({channel:'chrome',headless:true}),page=await browser.newPage();
const digest=value=>createHash('sha256').update(value).digest('hex');
try{
 await page.goto(`${origin}/login`);await page.getByLabel('Email address').fill('underwriter@cover.example');await page.getByLabel('Password',{exact:true}).fill(password);await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(`${origin}/`);
 const snapshot=[];
 for(const record of records){
  const route=`/api/v1/quotes/${record.id}`,hashes={};
  for(const suffix of ['',`/revisions/${record.originalRevisionId}`,`/revisions/${record.revisionId}`,'/revisions','/evidence','/evidence-files','/lookups',`/evidence-files/${record.fileId}/content`]){
   const response=await page.request.get(origin+route+suffix);assert.equal(response.status(),200);const bytes=await response.body();hashes[suffix]=digest(bytes);
   if(suffix===''){const data=JSON.parse(bytes);assert.equal(data.revisionId,record.revisionId);assert.equal(data.readiness.ready,true);}
   if(suffix==='/lookups'){const items=JSON.parse(bytes).items;assert.deepEqual(items.map(x=>x.id),record.lookupIds);assert.ok(items.every(x=>x.attempts>0&&x.state==='no-match'));}
   if(suffix.endsWith('/content'))assert.equal(hashes[suffix],record.evidenceSha256);
  }
  await page.goto(`${origin}/quotes/${record.id}`);await page.getByRole('heading',{name:record.reference,exact:true}).waitFor();await page.getByText('Capture checks passed for this saved revision.',{exact:true}).waitFor();
  await page.getByRole('tab',{name:'History and comparison',exact:true}).click();await page.getByRole('region',{name:'Saved quote revisions',exact:true}).waitFor();
  snapshot.push({id:record.id,hashes});
 }
 if(mode==='capture')await writeFile(file,JSON.stringify({capturedAt:new Date().toISOString(),snapshot},null,2));
 else{const prior=JSON.parse(await readFile(file,'utf8'));assert.deepEqual(snapshot,prior.snapshot);await writeFile(file,JSON.stringify({...prior,verifiedAt:new Date().toISOString(),passed:true},null,2));}
 console.log(`${mode}: both product quotes, original/current revisions, evidence bytes and lookup attempts verified through fresh authentication.`);
}finally{await browser.close();}
