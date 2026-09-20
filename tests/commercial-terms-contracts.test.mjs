import test from 'node:test';
import assert from 'node:assert/strict';
import {underwritingResponseValidator} from '../scripts/validate-underwriting-response.mjs';
const id='aaaaaaaa-0000-4000-8000-000000000001';
test('quote carrier contract permits closed CC extents without widening Motor Trade servicing',async()=>{
 const quote=await underwritingResponseValidator('QuoteCapacityExtension');
 const motor=await underwritingResponseValidator('UnderwritingCapacityExtension');
 const extent={dimension:'single-location',maximumAmount:'3000000.00',riskItemId:id};
 assert.equal(quote(extent),true,JSON.stringify(quote.errors));assert.equal(motor(extent),false);
 for(const bad of [{dimension:'single-location',maximumAmount:'3000000.00'}, {...extent,dimension:'district-property'}, {...extent,maximumAmount:'1e6'}, {...extent,extra:true}])assert.equal(quote(bad),false);
 assert.equal(quote({dimension:'public-liability',maximumAmount:'10000000.00'}),true);
 assert.equal(quote({dimension:'public-liability',maximumAmount:'10000000.00',riskItemId:id}),false);
});
test('commercial terms represent absent excess and glass basis without inventing monetary limits',async()=>{
 const validate=await underwritingResponseValidator('UnderwritingTermCover');
 assert.equal(validate({code:'property',limit:'3000000.00',targetIds:[id]}),true);
 assert.equal(validate({code:'glass',basis:'Included for declared premises',targetIds:[id]}),true);
 assert.equal(validate({code:'named-suppliers',limit:'250000.00',targetIds:[]}),true);
 assert.equal(validate({code:'glass',limit:'0.00',targetIds:[id]}),false);
 assert.equal(validate({code:'road-risks',limit:'50000.00',targetIds:[]}),false);
 assert.equal(validate({code:'road-risks',limit:'50000.00',excess:'250.00',targetIds:[]}),true);
});
