import {writeFile} from 'node:fs/promises';

const object=(properties,required=Object.keys(properties))=>({type:'object',additionalProperties:false,properties,required});
const ref=name=>({$ref:`#/$defs/${name}`});

// This is a capture-write contract, not a serialized policy with its required
// fields removed. Ownership, UTC term derivation and premiums are server-owned.
export async function writeQuoteSchemas(policy) {
  const defs=structuredClone(policy.$defs);
  const insured=structuredClone(policy.properties.insured);
  for(const key of ['clientId','clientAgencyRelationshipId']) delete insured.properties[key];
  insured.required=insured.required.filter(key=>!['clientId','clientAgencyRelationshipId'].includes(key));
  // Evidence is attached via a scoped command, never by placing arbitrary file
  // identifiers in proposal JSON.
  delete defs.PreviousInsurance.properties.evidenceDocumentIds;
  defs.PreviousInsurance.required=defs.PreviousInsurance.required.filter(key=>key!=='evidenceDocumentIds');
  const termIntent=object({
    kind:{type:'string',enum:['annual','short-period']},
    localStartDate:ref('Date'),localStartTime:{type:'string',pattern:'^(?:[01][0-9]|2[0-3]):[0-5][0-9]$'},
    timeZone:{const:'Europe/London'},
    utcOffsetMinutes:{type:'integer',enum:[0,60]},
    localEndDate:ref('Date'),localEndTime:{type:'string',pattern:'^(?:[01][0-9]|2[0-3]):[0-5][0-9]$'},
    endUtcOffsetMinutes:{type:'integer',enum:[0,60]},
  },['kind','localStartDate','localStartTime','timeZone']);
  const ready={
    $schema:policy.$schema,$id:'https://schemas.cover-mga.example/quote-ready/1.0',
    title:'Motor Trade capture write shape; semantic readiness is assessed separately',
    ...object({schemaVersion:{const:'1.0'},productCode:{enum:['motor-trade-road-risks','motor-trade-combined']},insured,termIntent,risk:ref('RoadRisk'),cover:ref('Cover')}),
    $defs:defs,
  };
  // Domain validation must also check chronology/DST, but a structurally complete
  // short-period intent cannot omit its entered end date and time.
  ready.properties.termIntent.allOf=[{if:{properties:{kind:{const:'short-period'}},required:['kind']},then:{properties:{localEndDate:ref('Date'),localEndTime:termIntent.properties.localEndTime},required:['localEndDate','localEndTime']}}];
  function incomplete(value) {
    if(Array.isArray(value))return value.map(incomplete);
    if(!value||typeof value!=='object')return value;
    const result={};
    for(const [key,item] of Object.entries(value)) {
      if(key==='required'||key==='allOf')continue;
      if(key==='anyOf'&&item.every(branch=>branch.required))continue;
      result[key==='oneOf'?'anyOf':key]=key==='minItems'?0:incomplete(item);
    }
    // Persisted child rows must be identifiable even while incomplete.
    if(value.properties?.id) result.required=['id'];
    if(value.properties?.kind?.const!==undefined) result.required=[...(result.required??[]),'kind'];
    return result;
  }
  const draft=incomplete(ready);
  draft.$id='https://schemas.cover-mga.example/quote-draft/1.0';
  draft.title='Incomplete Motor Trade capture write shape';
  draft.required=['schemaVersion','productCode'];
  // An unanswered question is omitted, not an untyped answer object. Reference
  // selections remain complete and versioned even in otherwise partial drafts.
  draft.$defs.Answer=structuredClone(defs.Answer);
  draft.$defs.Reference=structuredClone(defs.Reference);
  const output=new URL('../contracts/schemas/',import.meta.url);
  await writeFile(new URL('quote-draft.schema.json',output),JSON.stringify(draft,null,2)+'\n');
  await writeFile(new URL('quote-ready.schema.json',output),JSON.stringify(ready,null,2)+'\n');
}
