import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { sourceSectionFields } from '../lib/quote-section-fields.ts';
import { addSectionRow, changeSectionRow, moveSectionRow, removeSectionRow, setTripDriver } from '../lib/quote-section-form.ts';
import { coverExcessOptions } from '../lib/quote-cover-options.ts';
import { readinessTarget } from '../lib/quote-readiness.ts';
const read=async path=>JSON.parse(await readFile(new URL(`../../../${path}`,import.meta.url),'utf8'));
const q=await read('contracts/quote-question-catalogue.json'),r=await read('contracts/reference-data/motor-trade-capture.json'),labels=Object.fromEntries((await read('docs/design/funnel-field-mapping.json')).mappings.map(row=>[row.owner,row.label]));
const fields=sourceSectionFields(q.mappings,r.bindings,r.collections,labels),id=n=>`72000000-0000-4000-8000-${String(n).padStart(12,'0')}`;
const reference=(collection,value)=>({collection,value,version:r.version,label:r.collections[collection].find(row=>row.value===value).text});
test('remaining sections cover all source mappings and retain product-specific prototype questions',()=>{
 const expected=q.mappings.filter(row=>row.canonicalPath.startsWith('cover.')||row.canonicalPath.startsWith('risk.previousInsurance.')||row.canonicalPath.startsWith('risk.premises[].')||row.canonicalPath.startsWith('risk.declarations.')||row.owner.startsWith('MTS-13-'));
 for(const row of expected.filter(row=>row.contractKind!=='Id'))assert.equal(fields.filter(field=>field.id===row.owner).length,1,row.owner);
 for(const field of fields.filter(field=>field.kind==='reference'&&!field.dynamic))assert.deepEqual(field.choices,r.collections[field.collection].map(({value,text})=>({value,text})));
 assert.equal(fields.filter(field=>field.dynamic).length,1);assert.equal(fields.find(field=>field.dynamic).id,'MTS-05-Q04');
 assert.equal(fields.find(field=>field.id==='MTS-12-Q02').container,'risk.declarations');
 assert.deepEqual(fields.find(field=>field.id==='prototype.no-claims.reason').products,['motor-trade-road-risks']);
 for(const path of ['declaredUse','security','buildings','contents'])assert.deepEqual(fields.find(field=>field.group==='premises'&&field.path===path).products,['motor-trade-combined']);
 assert.ok(fields.some(field=>field.path==='risk.previousInsurance.noClaimsYearsBasis'));assert.ok(fields.some(field=>field.path==='risk.previousInsurance.expiresOn'));
 for(const product of ['motor-trade-road-risks','motor-trade-combined']){const names=fields.filter(field=>field.products.includes(product)).map(field=>`${field.group}:${field.label}`);assert.equal(new Set(names).size,names.length,'Every actual control must have a distinct accessible label');}
});
test('premises and trip edits use stable identities and protect actual references',()=>{
 const original={schemaVersion:'1.0',productCode:'motor-trade-combined',risk:{drivers:[{id:id(1)}]}};
 let p=addSectionRow(original,'premises',id(2));p=addSectionRow(p,'premises',id(3));p=changeSectionRow(p,'premises',id(2),'address.postcode','AB1 2CD');p=moveSectionRow(p,'premises',id(2),1);assert.equal(p.risk.premises[1].address.postcode,'AB1 2CD');
 p=addSectionRow(p,'temporaryEuropeanCover',id(4));p=setTripDriver(p,id(4),id(1),true);p=setTripDriver(p,id(4),id(1),true);assert.deepEqual(p.cover.temporaryEuropeanCover[0].driverIds,[id(1)]);
 assert.throws(()=>setTripDriver(p,id(4),id(99),true),/proposal/);assert.throws(()=>addSectionRow(p,'annualEuropeanCover',id(2)),/distinct/);
 p.risk.drivers[0].losses=[{id:id(5),riskItemId:id(2)}];assert.throws(()=>removeSectionRow(p,'premises',id(2)),/loss reference/);
 p=removeSectionRow(p,'temporaryEuropeanCover',id(4));assert.deepEqual(p.cover.temporaryEuropeanCover,[]);assert.equal(p.risk.premises.length,2);assert.equal(original.risk.premises,undefined);
 assert.throws(()=>changeSectionRow(p,'premises',id(2),'address.__proto__','bad'));
});
test('excess options reconcile independent cover declarations without modifying the proposal',()=>{
 const p={cover:{responses:{answers:[{questionId:'MTS-05-Q01',kind:'reference',value:reference('coverLevels',1)},{questionId:'prototype.quote.d9dd069a314c',kind:'reference',value:reference('prototype.quote.d9dd069a314c',2)}]}}};
 const before=structuredClone(p),result=coverExcessOptions(p,r);assert.equal(result.active,true);assert.equal(result.collection,'indemnityOwnVehicles/number:5/excesses');assert.deepEqual(result.choices,r.collections[result.collection].map(({value,text})=>({value,text})));assert.deepEqual(p,before);
 p.cover.responses.answers.push({questionId:'MTS-05-Q02',kind:'reference',value:reference('indemnityOwnVehicles',2)});assert.equal(coverExcessOptions(p,r).active,false);
 assert.equal(coverExcessOptions(p,undefined).active,false);
});
test('readiness points to reachable Combined insurance, Road Risks premises and retained excess clearing',()=>{
 const p={productCode:'motor-trade-combined',risk:{premises:[{id:id(1)}]},cover:{responses:{answers:[{questionId:'MTS-05-Q04',value:{}}]}}};
 const target=(issue,active=false)=>readinessTarget(issue,p,[],[],[],[],fields,active);
 assert.deepEqual(target({path:'/risk/previousInsurance/noClaimsBonusExpiresOn',questionId:'MTS-05-Q12'}),{stage:7,label:'Previous insurance · No-claims bonus expiry'});
 assert.deepEqual(target({path:'/cover/responses/answers',questionId:'MTS-05-Q04'}),{stage:7,label:'Clear Cover · Own-vehicle excess'});
 assert.deepEqual(target({path:'/cover/responses/answers',questionId:'MTS-05-Q04'},true),{stage:7,label:'Cover · Own-vehicle excess'});
 assert.deepEqual(target({path:'/risk/materialFacts',questionId:'prototype.quote.example'}),{stage:8,label:'Declarations · Material facts and additional information'});
 p.productCode='motor-trade-road-risks';assert.deepEqual(target({path:'/risk/premises/0/address/postcode'}),{stage:2,label:`Premises 1 · ${fields.find(field=>field.id==='MTS-02-Q02').label}`});
 assert.equal(target({path:'/risk/premises/0/security'}),undefined);
});
