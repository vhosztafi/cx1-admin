import test from 'node:test';
import assert from 'node:assert/strict';
import {agreedTermsPayload,visibleTermsFields} from '../lib/agencies.ts';
import {agencyFields} from '../lib/agency-fields.ts';
function fixture(){const values={};for(const field of agencyFields.filter(x=>[3,5].includes(x.stage))){if(field.options)values[field.path]=String(Object.values(field.options)[0]);}Object.assign(values,{'commercialTerms.effectiveFrom':'2026-12-01',creditLimit:'99999999999999999.99'});return values;}
const products=[{productVersionId:'5c4ddf81-19d8-4809-9dc5-012345678901',effectiveFrom:'2026-12-01',commission:'12.50'}];
test('complete terms proposal preserves exact money and omits inactive conditional answers',()=>{
 const values={...fixture(),'commercialTerms.flatCommissionBasisPoints':'99.99','commercialTerms.feeShareBasisPoints':'50','commercialTerms.volumeCommitment':'1','commercialTerms.minimumPremiumOverride':'2'};
 const payload=agreedTermsPayload(values,agencyFields,products,' Reviewed full terms ','2026-11-01');
 assert.equal(payload.creditLimit,'99999999999999999.99');assert.equal(payload.products[0].brokerCommissionBasisPoints,1250);assert.equal(payload.reason,'Reviewed full terms');
 for(const key of ['flatCommissionBasisPoints','feeShareBasisPoints','volumeCommitment','minimumPremiumOverride'])assert.equal(Object.hasOwn(payload.commercialTerms,key),false);
});
test('terms proposal requires conditional facts, complete fields and later valid dates',()=>{
 const values={...fixture(),'commercialTerms.commissionBasis':'flat-rate'};
 assert.ok(visibleTermsFields(values,agencyFields).some(x=>x.path==='commercialTerms.flatCommissionBasisPoints'));
 assert.throws(()=>agreedTermsPayload(values,agencyFields,products,'Review','2026-11-01'));
 for(const date of ['2026-11-01','2026-02-30','invalid'])assert.throws(()=>agreedTermsPayload({...fixture(),'commercialTerms.effectiveFrom':date},agencyFields,products,'Review','2026-11-01'));
 for(const value of ['', 'unknown'])assert.throws(()=>agreedTermsPayload({...fixture(),'settlement.method':value},agencyFields,products,'Review','2026-11-01'));
 for(const reason of ['', 'line\nbreak'])assert.throws(()=>agreedTermsPayload(fixture(),agencyFields,products,reason,'2026-11-01'));
});
test('terms products require an immediate grant, unique selections and exact bounded rates',()=>{
 for(const rows of [[],[...products,...products],[{...products[0],effectiveFrom:'2026-11-30'}],[{...products[0],effectiveFrom:'2026-12-02'}],[{...products[0],commission:'100.01'}]])assert.throws(()=>agreedTermsPayload(fixture(),agencyFields,rows,'Review','2026-11-01'));
});
