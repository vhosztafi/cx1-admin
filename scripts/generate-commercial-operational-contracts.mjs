import {readFile,writeFile} from 'node:fs/promises';
import {pathToFileURL} from 'node:url';

export async function writeCommercialOperationalContracts(){
 const issued=JSON.parse(await readFile(new URL('../contracts/schemas/commercial-combined-issued.schema.json',import.meta.url),'utf8'));
 const obj=properties=>({type:'object',additionalProperties:false,properties,required:Object.keys(properties)});
 const ref=name=>({$ref:`#/$defs/${name}`});
 const defs=issued.$defs;
 const base={format:{const:'commercial-document-1'},policyId:ref('Id'),versionId:ref('Id'),sourceContentHash:{type:'string',pattern:'^[a-f0-9]{64}$'},
  effectiveAt:ref('Instant'),insured:ref('Insured'),term:issued.properties.term,
  endorsements:defs.Cover.properties.endorsements,warranties:defs.Cover.properties.warranties};
 const schedule=obj({...base,kind:{const:'policy-schedule'},sections:defs.Cover.properties.sections,locations:defs.Risk.properties.locations,
  business:ref('Business'),wages:defs.Risk.properties.wages,liability:ref('Liability'),premium:ref('Premium')});
 schedule.properties.businessInterruption=ref('BusinessInterruption');
 const statement=obj({...base,kind:{const:'policy-statement'},declarations:ref('Risk'),cover:ref('Cover')});
 const certificate=obj({...base,kind:{const:'policy-certificate'},section:{allOf:[defs.Cover.properties.sections.items,
  {type:'object',properties:{code:{const:'employers-liability'}},required:['code']}]}});
 certificate.properties.employersReferenceNumber=defs.Liability.properties.employersReferenceNumber;
 // Bundle only transitive local definitions; no remote schema resolution at runtime.
 async function write(name,root){
  const selected={};
  function visit(value){if(!value||typeof value!=='object')return;
   if(value.$ref){const name=value.$ref.split('/').at(-1);if(!selected[name]){if(!defs[name])throw Error(`Unknown definition ${name}`);selected[name]=defs[name];visit(defs[name]);}}
   for(const child of Object.values(value))visit(child);
  }
  visit(root);
  const schema={$schema:'https://json-schema.org/draft/2020-12/schema',$id:`https://schemas.cover-mga.example/${name}/1`,...root,$defs:selected};
  await writeFile(new URL(`../contracts/schemas/${name}.schema.json`,import.meta.url),JSON.stringify(schema,null,2)+'\n');
 }
 await write('commercial-incident',defs.IncidentPayload);
 await write('commercial-document',{oneOf:[schedule,statement,certificate]});
}
if(process.argv[1]&&import.meta.url===pathToFileURL(process.argv[1]).href)await writeCommercialOperationalContracts();
