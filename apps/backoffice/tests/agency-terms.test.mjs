import test from 'node:test';
import assert from 'node:assert/strict';
import {termsFieldValue} from '../lib/agency-terms.ts';
test('approved money renders without losing SQL decimal precision or cents',()=>{
  const field={path:'creditLimit',label:'Credit limit',stage:5,type:'money'};
  assert.equal(termsFieldValue(field,'99999999999999999.99'),'£99,999,999,999,999,999.99');
  assert.equal(termsFieldValue(field,'0.01'),'£0.01');
  assert.equal(termsFieldValue({...field,type:'percent'},'12.50'),'12.50%');
});
test('approved choice labels retain their actual stored value',()=>{
  const field={path:'paymentTermsDays',label:'Credit terms',stage:5,options:{'30 days':30,'60 days':60}};
  assert.equal(termsFieldValue(field,'60'),'60 days');
  assert.equal(termsFieldValue(field,'45'),'45');
});
