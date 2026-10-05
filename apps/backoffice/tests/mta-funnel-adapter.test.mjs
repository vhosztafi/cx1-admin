import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
import {captureToMta,funnelToMta,mtaToFunnel} from '../lib/mta-funnel-adapter.ts';
const read=async path=>JSON.parse(await readFile(new URL(`../../../contracts/${path}`,import.meta.url),'utf8'));
const references=await read('reference-data/motor-trade-capture.json'),questions=await read('quote-question-catalogue.json');const catalogue={...references,mappings:questions.mappings};
const ajv=new Ajv({strict:true,allErrors:true});addFormats(ajv);const validate=ajv.compile(await read('schemas/servicing.schema.json'));
const id=at=>`10000000-0000-4000-8000-${String(at).padStart(12,'0')}`;
const base={schemaVersion:'1.0',productCode:'motor-trade-road-risks',insured:{legalName:'Fictional client',entityType:'sole-trader'},termIntent:{kind:'annual',timeZone:'Europe/London',localStartDate:'2026-09-15',localStartTime:'09:00',utcOffsetMinutes:60},risk:{business:{description:'Original'},drivers:[],vehicles:[{id:id(4),registration:'AB12 CDE',make:'Example',model:'Car',ownership:'business-owned',value:'1000.00'}],premises:[]},cover:{}};
const current=()=>({schemaVersion:'1.0',baseVersionId:id(1),reason:'Fictional adjustment request',requestedBy:{kind:'broker',name:'Fictional broker'},commonEffectiveIntent:{localDate:'2026-10-05',localTime:'10:00',timeZone:'Europe/London',utcOffsetMinutes:60},changes:[]});
const editor=(proposal=base)=>({draftId:id(2),revisionId:id(3),clientId:id(5),assessment:{base,proposed:proposal,changes:[],slices:[],readinessIssues:[]}});
test('funnel changes retain stable target/change identities and dated cover intent',()=>{
 const draft=current();draft.dateBasis='per-cover-change';draft.changes=[{changeId:id(7),kind:'cover',riskItemId:id(6),operation:'update',payload:{},effectiveIntent:{...draft.commonEffectiveIntent,localDate:'2026-10-07'}}];
 const next=structuredClone(base);next.risk.vehicles[0].value='1100.00';next.risk.materialFacts='Fictional new disclosure';
 const result=captureToMta(next,draft,editor(),id(6));assert.ok(validate(result),JSON.stringify(validate.errors));
 assert.equal(result.changes[0].changeId,id(7));assert.equal(result.changes[0].effectiveIntent.localDate,'2026-10-07');assert.equal(result.changes.find(row=>row.kind==='vehicle').riskItemId,id(4));assert.equal(result.changes.find(row=>row.kind==='risk-details').payload.materialFacts,'Fictional new disclosure');
 const repeated=captureToMta(next,result,editor(next),id(6));assert.deepEqual(repeated,result);assert.equal(base.risk.vehicles[0].value,'1000.00');
});
test('MTA source date becomes effective intent while the issued term stays fixed',()=>{
 const draft=current();const raw=mtaToFunnel(draft,editor(),catalogue);assert.equal(raw.isMta,true);assert.equal(raw.proposerPolicyStartDate,'2026-10-05');raw.proposerPolicyStartDate='2026-10-06';
 const result=funnelToMta({version:1,step:1,formData:raw},draft,editor(),id(6),catalogue);assert.equal(result.commonEffectiveIntent.localDate,'2026-10-06');assert.equal(result.commonEffectiveIntent.utcOffsetMinutes,60);assert.ok(validate(result),JSON.stringify(validate.errors));assert.equal(base.termIntent.localStartDate,'2026-09-15');
 raw.isShortTerm=true;assert.throws(()=>funnelToMta({version:1,step:1,formData:raw},draft,editor(),id(6),catalogue),/duration/);
});
test('multiple dated target edits and existing risk reordering fail explicitly',()=>{
 const draft=current();draft.changes=[1,2].map(at=>({changeId:id(10+at),kind:'vehicle',riskItemId:id(4),operation:'update',payload:{value:'1000.00'}}));const next=structuredClone(base);next.risk.vehicles[0].value='1200.00';assert.throws(()=>captureToMta(next,draft,editor(),id(6)),/multiple dates/);
 const original=structuredClone(base);original.risk.vehicles.push({...original.risk.vehicles[0],id:id(9)});const reversed=structuredClone(original);reversed.risk.vehicles.reverse();assert.throws(()=>captureToMta(reversed,current(),editor(original),id(6)),/reorder/);
});
test('unchanged complete issued capture does not create phantom MTA changes',async()=>{
 const proposal=(await read('examples/underwriting-demo.json')).proposals['motor-trade-road-risks'];
 const snapshot={...editor(proposal),assessment:{...editor(proposal).assessment,base:proposal}};
 const raw=mtaToFunnel(current(),snapshot,catalogue);
 const result=funnelToMta({version:1,step:1,formData:raw},current(),snapshot,id(6),catalogue);
 assert.deepEqual(result.changes,[]);
});
