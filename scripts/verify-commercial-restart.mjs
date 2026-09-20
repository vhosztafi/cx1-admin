import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {resolve,sep} from 'node:path';
import {chromium} from 'playwright';

const [mode,evidence]=process.argv.slice(2);assert.ok(['capture','compare'].includes(mode)&&evidence);
const directory=resolve(evidence);assert.ok(directory.startsWith(resolve('.local')+sep));
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
if(mode==='capture')await mkdir(directory,{recursive:false});
const prior=mode==='compare'?JSON.parse(await readFile(directory+'/before.json','utf8')):null;
if(prior)assert.equal(prior.origin,origin);
let sources=prior?.sources;
if(!sources){
 const motor=JSON.parse(await readFile(process.env.COVER_MOTOR_RESTART_BASELINE??'.local/phase8-15-preupgrade-api/policy-graph-before.json','utf8'));
 const folder=process.env.COVER_COMMERCIAL_DEMO_DIRECTORY??'.local/commercial-lifecycle-demo-v1';
 const commercial=JSON.parse(await readFile(folder+'/references.json','utf8'));
 const renewed=JSON.parse(await readFile(folder+'/renewal.json','utf8'));
 const adjusted=JSON.parse(await readFile(folder+'/adjustment.json','utf8'));
 sources={policies:[...motor.policies.map(x=>({policyId:x.policyId,productCode:x.productCode})),{policyId:commercial.policy.policyId,productCode:'commercial-combined'}],
  commercialVersions:[commercial.policy.versionId,...commercial.stages.adjustment.versionIds,...commercial.stages.renewal.versionIds,commercial.stages.cancellation.versionId],
  exposurePoints:[adjusted.issued.snapshot.term.startsAt,adjusted.issued.snapshot.provenance.effectiveAt,renewed.issued.snapshot.term.startsAt,
   new Date(Date.parse(commercial.stages.cancellation.effectiveAt)-1).toISOString(),commercial.stages.cancellation.effectiveAt]};
}
assert.equal(new Set(sources.policies.map(x=>x.productCode)).size,3);assert.ok(sources.exposurePoints.every(x=>Number.isFinite(Date.parse(x))));
const knownAt=prior?.knownAt??new Date().toISOString(),browser=await chromium.launch({channel:'chrome',headless:true});
try{
 const page=await browser.newPage();await page.goto(origin+'/login');await page.getByLabel('Email address',{exact:true}).fill('senior-underwriter@cover.example');
 await page.getByLabel('Password',{exact:true}).fill((await readFile('.local/demo-password.txt','utf8')).trim());await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');
 async function get(path){const response=await page.request.get(origin+path);assert.equal(response.status(),200,path);assert.match(response.headers()['cache-control']??'',/no-store/);return response.json();}
 const policies=[];
 for(const source of sources.policies){
  const root='/api/v1/policies/'+source.policyId,query=new URLSearchParams({effectiveAt:knownAt,knownAt});
  const history=await get(root+'/history?'+query),versions=[];assert.ok(history.versions.length);
  for(const row of history.versions){
   const version=await get(`${root}/terms/${row.termId}/versions/${row.id}`);assert.equal(version.contentHash,row.contentHash);assert.equal(version.snapshot.productCode,source.productCode);
   for(const field of ['effectiveCutoff','knownCutoff']){assert.ok(Number.isFinite(Date.parse(version[field])));delete version[field];}versions.push(version);
  }
  const exposure=[];
  if(source.productCode==='commercial-combined'){
   for(const id of sources.commercialVersions)assert.ok(versions.some(x=>x.versionId===id),'Missing commercial lifecycle version.');
   for(const effectiveAt of sources.exposurePoints){const value=await get(root+'/commercial-exposure?'+new URLSearchParams({effectiveAt,knownAt}));assert.ok(Number.isFinite(Date.parse(value.observedAt)));delete value.observedAt;exposure.push(value);}
   assert.equal(exposure.at(-1).coverageState,'cancelled');assert.ok(exposure.at(-1).districts.every(x=>x.ownProposedSumInsured==='0.00'));
   assert.ok(exposure.at(-2).districts.some(x=>x.ownProposedSumInsured!=='0.00'));
  }
  policies.push({...source,history,versions,exposure});
 }
 const result={origin,knownAt,sources,policies};if(prior)assert.deepEqual(result,prior,'Retained commercial/Motor Trade policy graphs or pinned exposure changed.');
 await writeFile(directory+`/${mode==='capture'?'before':'after'}.json`,JSON.stringify(result,null,2));
 const report={passed:true,mode,completedAt:new Date().toISOString(),policyCount:policies.length,versionCount:policies.reduce((n,x)=>n+x.versions.length,0),sha256:createHash('sha256').update(JSON.stringify(result)).digest('hex'),
  boundary:'Fresh-login exact API readback. Actual process restart and all-table initialization evidence must be recorded separately.'};
 await writeFile(directory+`/${mode}-report.json`,JSON.stringify(report,null,2));console.log(`${mode}: ${report.policyCount} policy graphs and commercial exposure boundaries verified.`);
}finally{await browser.close();}
