import {readFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
export async function underwritingResponseValidator(name) {
 // Decimal JSON numbers such as 2.05 are not exact binary floats. Keep the
 // schema's two-decimal rule while allowing only floating-point division noise.
 const ajv = new Ajv2020({strict:true,allErrors:true,multipleOfPrecision:8}); addFormats(ajv); ajv.addFormat('binary',true);
 const rootId = 'https://contracts.cover-mga.example/rating-validation';
 const document = JSON.parse(await readFile(new URL('../contracts/openapi.json',import.meta.url),'utf8'));
 const refs = {};
 for(const file of ['policy','policy-draft','quote-draft','issued-policy','issued-servicing','issued-cancellation']) {
  const schema = JSON.parse(await readFile(new URL(`../contracts/schemas/${file}.schema.json`,import.meta.url),'utf8'));
  refs[`./schemas/${file}.schema.json`] = schema.$id; ajv.addSchema(schema);
 }
 function relocate(value) {
  if(Array.isArray(value)) return value.map(relocate);
  if(value && typeof value==='object') return Object.fromEntries(Object.entries(value).map(([key,item])=>[key,key==='$ref' ? Object.entries(refs).reduce((ref,[from,to])=>ref.replace(from,to),item.replace('#/components/schemas/',rootId+'#/$defs/')) : relocate(item)]));
  return value;
 }
 ajv.addSchema({$id:rootId,$defs:relocate(document.components.schemas)});
 return ajv.compile({$ref:rootId+'#/$defs/'+name});
}
