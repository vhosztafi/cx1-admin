import test from 'node:test';
import assert from 'node:assert/strict';
import {resumablePrefix} from '../scripts/acceptance-resume.mjs';
const stages=['capture','review','issue'].map(name=>({name,script:name+'.mjs',args:[]}));
function report(){return {origin:'http://localhost',passed:false,finishedAt:'2026-09-21T06:00:00Z',stages:stages.map((s,i)=>({...s,status:['passed','failed','not-run'][i],...(i===0?{exitCode:0,scriptSha256:'source',logSha256:'evidence',logPath:'capture.log'}:{})}))};}
test('acceptance resumes only the failed stage and its unexecuted suffix',()=>assert.equal(resumablePrefix(report(),stages,'http://localhost'),1));
test('acceptance cannot resume active or successful reports',()=>{
 for(const changed of [{finishedAt:null},{passed:true}])assert.throws(()=>resumablePrefix({...report(),...changed},stages,'http://localhost'));
});
test('acceptance cannot resume against another origin or changed inventory',()=>{
 assert.throws(()=>resumablePrefix(report(),stages,'http://another'));
 assert.throws(()=>resumablePrefix(report(),stages.slice(1),'http://localhost'));
});
test('acceptance rejects missing proof, false success, or noncontiguous execution',()=>{
 for(const mutate of [r=>delete r.stages[0].logSha256,r=>r.stages[0].exitCode=1,r=>r.stages[0].status='not-run',r=>r.stages[2].status='passed']){
  const r=report();mutate(r);assert.throws(()=>resumablePrefix(r,stages,'http://localhost'));
 }
});
