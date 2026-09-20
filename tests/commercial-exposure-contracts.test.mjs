import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
const id='aaaaaaaa-0000-4000-8000-000000000001',at='2027-01-01T00:00:00Z';
async function validator(){const schema=JSON.parse(await readFile('contracts/schemas/commercial-combined.schema.json','utf8'));const ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);ajv.addSchema(schema);return ajv.compile({$ref:schema.$id+'#/$defs/Exposure'});}
const row={district:'S9',interval:{startsAt:at,endsAt:'2027-02-01T00:00:00Z'},ownProposedSumInsured:'100.00',outcome:'within-capacity'};
const base={format:'commercial-exposure-1',audience:'agency',observedAt:at,effectiveAt:at,knownAt:at,advisory:true,coverageState:'active',outcome:'within-capacity',truncated:false,source:{kind:'policy-version',id,hash:'a'.repeat(64)},districts:[row]};
test('commercial observed exposure retains exact selected source and cannot disclose book details to agency audience',async()=>{
 const valid=await validator();assert.ok(valid(base),JSON.stringify(valid.errors));
 for(const field of ['bookSumInsured','policyCount','limit','headroom','bookId','limitVersionId','limitHash','policyIds']){
  assert.equal(valid({...base,districts:[{...row,[field]:field==='policyCount'?2:field==='policyIds'?[id]:'100.00'}]}),false,field);
 }
 assert.equal(valid({...base,foreignPolicyIds:[id]}),false);
});
test('internal missing-limit observation is explicit and large property aggregates keep exact pennies',async()=>{
 const valid=await validator();const body={...base,audience:'internal',outcome:'unavailable',districts:[{...row,outcome:'unavailable',bookSumInsured:'2999999999999999.99',policyCount:3,bookId:id,blocker:'commercial-exposure-limit-missing'}]};
 assert.ok(valid(body),JSON.stringify(valid.errors));
 assert.equal(valid({...body,districts:[{...body.districts[0],policyIds:[id]}]}),false);
 assert.ok(valid({...base,audience:'internal',districts:[{...row,bookSumInsured:'200.00',policyCount:2,bookId:id,limit:'150.00',headroom:'-50.00',limitVersionId:id,limitHash:'a'.repeat(64),outcome:'exceeds-capacity',blocker:'commercial-district-capacity-exceeded'}]}),JSON.stringify(valid.errors));
});
