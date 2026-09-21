import test from 'node:test';
import assert from 'node:assert/strict';
import { documentCommand, sendDocumentCommand, prepareFileUpload, sendFileUpload, documentContentUrl, documentOptionsUrl } from '../lib/documents-api.ts';
import { uncertainQuoteFailure } from '../lib/quotes.ts';
const id=n=>`aaaaaaaa-0000-4000-8000-${String(n).padStart(12,'0')}`;
const input=()=>({kind:'policy-schedule',source:{kind:'policy-version',policyVersionId:id(2)},templateVersionId:id(3),visibility:'internal',reason:'Generate the selected historical cover'});
const receipt=()=>({id:id(4),documentId:id(5),number:1,kind:'policy-schedule',state:'pending',originalName:'schedule.pdf',bytes:0,contentType:'application/pdf',sourceVersionId:id(2),templateVersionId:id(3),createdAt:'2026-09-21T12:00:00Z'});

test('template picker URL retains explicit historical source and opaque cursor',()=>{
 const url=new URL(documentOptionsUrl(id(1),{kind:'quote-revision',quoteRevisionId:id(2),quoteTermsVersionId:id(3)},'a+b/=c'),'https://local.example');
 assert.equal(url.searchParams.get('sourceId'),id(2));assert.equal(url.searchParams.get('quoteTermsVersionId'),id(3));assert.equal(url.searchParams.get('cursor'),'a+b/=c');
 assert.throws(()=>documentOptionsUrl(id(1),{kind:'policy-version',policyVersionId:id(2),quoteTermsVersionId:id(3)}));
 assert.throws(()=>documentOptionsUrl(id(1),{kind:'policy-version',policyVersionId:'latest'}));
 assert.throws(()=>documentOptionsUrl(id(1),{kind:'quote-revision',quoteRevisionId:id(2)},'x'.repeat(2049)));
});

test('document commands pin exact source/template and immutable retry bytes',()=>{
 const values=input(),command=documentCommand('generate',id(1),values);
 values.source.policyVersionId=id(6);values.templateVersionId=id(7);
 assert.equal(JSON.parse(command.body).source.policyVersionId,id(2));assert.equal(JSON.parse(command.body).templateVersionId,id(3));assert.ok(Object.isFrozen(command));
 assert.throws(()=>documentCommand('generate',id(1),{...input(),createdBy:id(8)}));
 assert.throws(()=>documentCommand('generate',id(1),{...input(),source:{kind:'policy-version',policyVersionId:id(2),quoteRevisionId:id(6)}}));
 assert.throws(()=>documentCommand('generate',id(1),{...input(),reason:'\n'}));
 assert.throws(()=>documentCommand('generate',id(1),{...input(),visibility:'agency'}));
 assert.throws(()=>documentCommand('generate',id(1),{...input(),relationshipId:id(6)}));
 assert.throws(()=>documentCommand('generate',id(1),{...input(),kind:'quotation',source:{kind:'quote-revision',quoteRevisionId:id(2)}}));
});

test('document retry requires matching source, template and target version receipt',async()=>{
 const original=globalThis.fetch,calls=[],command=documentCommand('generate',id(1),{...input(),documentId:id(5)});
 try{
  globalThis.fetch=async(url,init)=>{calls.push([url,init]);return new Response(JSON.stringify(receipt()));};
  await sendDocumentCommand(command,'csrf');await sendDocumentCommand(command,'csrf');
  assert.equal(calls[0][1].body,calls[1][1].body);assert.equal(calls[0][1].headers['Idempotency-Key'],calls[1][1].headers['Idempotency-Key']);
  for(const change of [{sourceVersionId:id(9)},{templateVersionId:id(9)},{documentId:id(9)},{kind:'quotation'},{id:'https://untrusted.example/file'},{state:'ready',bytes:0}]){
   globalThis.fetch=async()=>new Response(JSON.stringify({...receipt(),...change}));
   await assert.rejects(()=>sendDocumentCommand(command,'csrf'),error=>uncertainQuoteFailure(error));
  }
 }finally{globalThis.fetch=original;}
});

test('busy or uncertain document results preserve the command without exposing diagnostics',async()=>{
 const original=globalThis.fetch,command=documentCommand('generate',id(1),input());
 try{
  globalThis.fetch=async()=>new Response(JSON.stringify({code:'command-busy',detail:'private SQL path'}),{status:409});
  await assert.rejects(()=>sendDocumentCommand(command,'csrf'),error=>uncertainQuoteFailure(error)&&!error.message.includes('private'));
  globalThis.fetch=async()=>new Response(JSON.stringify({detail:'private SQL path'}),{status:403});
  await assert.rejects(()=>sendDocumentCommand(command,'csrf'),error=>!uncertainQuoteFailure(error)&&!error.message.includes('private'));
 }finally{globalThis.fetch=original;}
});

test('uploads preserve exact file bytes, checksum and key across uncertain retry',async()=>{
 const original=globalThis.fetch,file=new File(['%PDF-1.7\nfictional\n%%EOF'],'evidence.pdf',{type:'application/pdf'});
 const command=await prepareFileUpload(id(1),file),calls=[];
 const proof={id:id(7),subjectRecordId:id(1),name:file.name,mediaType:file.type,byteLength:file.size,sha256:command.sha256,state:'pending',createdAt:'2026-09-21T12:00:00Z',verifiedAt:null,failureCode:null};
 try{
  globalThis.fetch=async(url,init)=>{calls.push([url,init]);return new Response(JSON.stringify(proof));};
  await sendFileUpload(command,'csrf');await sendFileUpload(command,'csrf');
  assert.equal(calls[0][1].body,file);assert.equal(calls[1][1].body,file);assert.equal(calls[0][1].headers['Idempotency-Key'],calls[1][1].headers['Idempotency-Key']);
  for(const change of [{sha256:'f'.repeat(64)},{subjectRecordId:id(8)},{name:'another.pdf'},{byteLength:file.size+1}]){
   globalThis.fetch=async()=>new Response(JSON.stringify({...proof,...change}));await assert.rejects(()=>sendFileUpload(command,'csrf'),error=>uncertainQuoteFailure(error));
  }
 }finally{globalThis.fetch=original;}
 await assert.rejects(()=>prepareFileUpload(id(1),new File(['x'],'unsafe.svg',{type:'image/svg+xml'})));
 await assert.rejects(()=>prepareFileUpload(id(1),new File(['x'],'../unsafe.pdf',{type:'application/pdf'})));
 await assert.rejects(()=>prepareFileUpload(id(1),new File([],'empty.pdf',{type:'application/pdf'})));
});

test('document attachment binds the confirmed public upload and preserves replacement target',()=>{
 const proof={id:id(7),subjectRecordId:id(1),name:'evidence.png',mediaType:'image/png',byteLength:100,sha256:'a'.repeat(64),state:'pending'};
 const values={kind:'evidence',uploadId:proof.id,visibility:'internal',reason:'Save supplied evidence',documentId:id(5)};
 const command=documentCommand('upload',id(1),values,crypto.randomUUID(),proof);
 assert.equal(JSON.parse(command.body).uploadId,id(7));assert.equal(JSON.parse(command.body).documentId,id(5));
 assert.throws(()=>documentCommand('upload',id(1),{...values,uploadId:id(9)},crypto.randomUUID(),proof));
 assert.throws(()=>documentCommand('upload',id(1),{...values,fileObjectId:id(9)},crypto.randomUUID(),proof));
 assert.throws(()=>documentCommand('upload',id(2),values,crypto.randomUUID(),proof));
});

test('preview and download URLs require exact ready versions and never accept arbitrary links',()=>{
 const ready={...receipt(),state:'ready',bytes:100,sha256:'a'.repeat(64)};
 assert.equal(documentContentUrl(ready,true),`/api/v1/document-versions/${id(4)}/preview`);
 assert.equal(documentContentUrl(ready),`/api/v1/document-versions/${id(4)}/content`);
 for(const state of ['pending','failed','quarantined'])assert.throws(()=>documentContentUrl({...ready,state},true));
 assert.throws(()=>documentContentUrl({...ready,id:'../other'},true));
 assert.throws(()=>documentContentUrl({...ready,contentType:'text/html'},true));
});
