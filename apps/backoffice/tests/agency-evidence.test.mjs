import test from 'node:test';
import assert from 'node:assert/strict';
import {evidenceLabel,uploadContentType} from '../lib/agency-evidence.ts';
test('evidence display distinguishes fresh results, stale inputs and missing verification',()=>{
 assert.equal(evidenceLabel('satisfied'),'Current demo evidence');assert.equal(evidenceLabel('stale'),'Out of date');assert.equal(evidenceLabel('expired'),'Expired');assert.equal(evidenceLabel(undefined),'Evidence needed');assert.equal(evidenceLabel('verified'),'Unavailable');assert.equal(evidenceLabel('satisfied',true),'Save changes to assess current evidence');
});
test('upload metadata uses the allowlisted extension and rejects inconsistent MIME or excess bytes',()=>{
 assert.equal(uploadContentType({name:'proof.TXT',size:20,type:''}),'text/plain');assert.equal(uploadContentType({name:'proof.pdf',size:10*1024*1024,type:'application/pdf'}),'application/pdf');
 for(const file of [{name:'proof.svg',size:10,type:'image/svg+xml'},{name:'proof.pdf',size:20,type:'text/plain'},{name:'proof.txt',size:0,type:'text/plain'},{name:'proof.txt',size:10*1024*1024+1,type:'text/plain'}])assert.throws(()=>uploadContentType(file));
});
