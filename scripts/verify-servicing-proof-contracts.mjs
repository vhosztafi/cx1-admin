import {readFile,readdir,stat} from 'node:fs/promises';
import {resolve,join} from 'node:path';
import assert from 'node:assert/strict';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';

const [directory,notBefore,mode='reads']=process.argv.slice(2);
assert.ok(['reads','commands','authority','submissions','referral-work','capacity','terms'].includes(mode),'Choose reads, commands, authority, submissions, referral-work, capacity or terms.');
assert.ok(directory&&Number.isFinite(Date.parse(notBefore)),'Provide a fresh response directory and earliest UTC timestamp.');
const read=async path=>JSON.parse(await readFile(path,'utf8'));
const document=await read(new URL('../contracts/openapi.json',import.meta.url));
const ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);ajv.addFormat('binary',true);
const root='https://contracts.cover-mga.example/servicing-proof-runtime';const external=new Map();
for(const name of ['policy','policy-draft','quote-draft','issued-policy']) {
 const schema=await read(new URL(`../contracts/schemas/${name}.schema.json`,import.meta.url));ajv.addSchema(schema);external.set(`./schemas/${name}.schema.json`,schema.$id);
}
function relocate(value) {
 if(Array.isArray(value))return value.map(relocate);
 if(value&&typeof value==='object')return Object.fromEntries(Object.entries(value).map(([key,item])=>[key,key==='$ref'?external.get(item)??item.replace('#/components/schemas/',`${root}#/$defs/`):relocate(item)]));
 return value;
}
ajv.addSchema({$id:root,$defs:relocate(document.components.schemas)});
const expected=mode==='capacity'?38:mode==='terms'?24:mode==='commands'?14:mode==='authority'?4:mode==='referral-work'?2:12;
const files=(await readdir(resolve(directory))).filter(x=>x.endsWith('.json'));assert.equal(files.length,expected,'Expect fresh HTTP responses from both Motor Trade products.');
const names=new Map();
for(const file of files) {
 const path=join(resolve(directory),file);assert.ok((await stat(path)).mtimeMs>=Date.parse(notBefore),`Stale response: ${file}`);
 const {schema,data}=await read(path);const validate=ajv.getSchema(`${root}#/$defs/${schema}`);assert.ok(validate,`Unknown response schema ${schema}`);
 assert.ok(validate(data),`${file}: ${JSON.stringify(validate.errors)}`);
 assert.equal(validate({...data,unrecognisedProperty:true}),false,`${schema} must be closed`);
 names.set(schema,(names.get(schema)??0)+1);
}
if(mode==='capacity') {assert.equal(names.size,8);for(const name of ['ServicingCapacityReceipt','ServicingCapacityQueuedReceipt','ServicingCapacityDetail','ServicingCapacityPage','ServicingCapacitySubmissionsPage','ServicingCapacityMessagesPage','ServicingCapacityResponsesPage','ServicingCapacityResolutionsPage'])assert.ok(names.get(name)>=2,name);}
else if(mode==='terms') {assert.equal(names.size,4);assert.equal(names.get('ServicingTermsReceipt'),6);assert.equal(names.get('ServicingTermsView'),6);assert.equal(names.get('ServicingTermsHistoryPage'),10);assert.equal(names.get('ServicingTermsSnapshot'),2);}
else if(mode==='commands') {assert.equal(names.size,1);assert.equal(names.get('ServicingProofReceipt'),14);}
else if(mode==='authority') {assert.equal(names.size,1);assert.equal(names.get('ServicingCurrentAuthorityPage'),4);}
else if(mode==='submissions') {assert.equal(names.size,2);assert.equal(names.get('ServicingSubmissionReceipt'),4);assert.equal(names.get('ServicingSubmissionPage'),8);}
else if(mode==='referral-work') {assert.equal(names.size,1);assert.equal(names.get('ServicingReferralWork'),2);}
else {assert.equal(names.size,6);for(const count of names.values())assert.equal(count,2);}
console.log(`Validated ${expected} fresh persisted HTTP responses and rejected unknown response properties.`);
