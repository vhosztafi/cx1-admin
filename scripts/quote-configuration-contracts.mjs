import {writeFile,readFile} from 'node:fs/promises';
import {pathToFileURL} from 'node:url';
import Ajv2020 from 'ajv/dist/2020.js';

const text={type:'string',minLength:1};
const integer={type:'integer',minimum:0};
const bool={type:'boolean'};
const list=(items,minItems=0)=>({type:'array',items,minItems});
const strings={...list(text),uniqueItems:true};
const object=(properties,required=Object.keys(properties))=>({type:'object',additionalProperties:false,properties,required});
const path={type:'string',pattern:'^[A-Za-z][A-Za-z0-9]*(?:\\[\\])?(?:\\.[A-Za-z][A-Za-z0-9]*(?:\\[\\])?)*$'};
const products={...list({enum:['motor-trade-road-risks','motor-trade-combined']},1),uniqueItems:true};
const kinds={enum:['reference','references','boolean','date','text','count','percentage','money']};
const version={type:'string',pattern:'^mt-capture-[a-f0-9]{16}$'};
const condition=object({questionId:text,equals:bool});
const prototypeProperties={questionId:text,label:text,kind:kinds,targetContainer:path,sourceControlId:text,sourceOptions:list(text),stages:strings,referenceValues:list(object({value:integer,label:text})),requiredWhenApplicable:bool,products,sourceCatalogue:{enum:['prototype-quote-1','prototype-detail-1','prototype-quote-value-1','prototype-conditional-1']},disposition:{enum:['phase-05-capture','phase-08-commercial-combined']},unit:{enum:['years','none','GBP','count','calendar-year','basis-points']}};
const mapping=object({...prototypeProperties,owner:text,rawPath:path,canonicalPath:path,contractKind:{enum:['string','Reference','Date','Id','Answer','boolean','Amount','integer','number']},mappingStatus:text,runtimeValidation:text,optionCollection:{anyOf:[text,{type:'null'}]},answerKind:kinds,sourceVehicleSet:text,requiredWhen:condition,conversion:text,sourceKey:text},['owner','canonicalPath','contractKind','products']);
mapping.allOf=[{if:{properties:{contractKind:{const:'Answer'}},required:['contractKind']},then:{properties:{questionId:text,answerKind:kinds},required:['questionId','answerKind']}}];
const deferred=object({...prototypeProperties,products:{...list({const:'commercial-combined'},1),uniqueItems:true},disposition:{const:'phase-08-commercial-combined'}},Object.keys(prototypeProperties).filter(k=>!['unit','referenceValues','requiredWhenApplicable'].includes(k)));
const header=(name,title)=>({$schema:'https://json-schema.org/draft/2020-12/schema',$id:`https://schemas.cover-mga.example/${name}/1.0`,title});
export const questionConfigurationSchema={...header('quote-question-configuration','Versioned Motor Trade question configuration'),...object({version,status:text,products,mappings:list(mapping,1),deferredQuestions:list(deferred),directReferenceFields:list(object({controlId:text,collection:text,canonicalPath:path,products,sourceOptions:list(text,1)}))})};

const optionProperties={value:integer,text,isComprehensive:bool,abiGroup:integer,maxGVW:integer,maxCC:{anyOf:[integer,{type:'null'}]},relationshipOptions:{...list(integer),uniqueItems:true},numericValue:integer,gvwLimitDecimal:{type:'number',minimum:0},isSocialDomesticPleasure:bool,excesses:list({$ref:'#/$defs/Option'}),customerLOI:bool,requireShunterRadius:bool,requireCarJockeyRadius:bool,requireNCBIntroRenewal:bool,category:text,requiresQuadBikes:bool,requiresRallyTrackKitCarsTrikes:bool,requiresABICheckForCommercialVehicle:bool,countries:text,isDefault:bool};
const options=list({$ref:'#/$defs/Option'},1);
export const referenceConfigurationSchema={...header('quote-reference-configuration','Versioned Motor Trade reference configuration'),...object({version,sourcePath:text,sourceSha256:{type:'string',pattern:'^[a-f0-9]{64}$'},status:text,collections:{type:'object',minProperties:1,propertyNames:text,additionalProperties:options},youngDriverConfiguration:list(object({ageFrom:{anyOf:[integer,{type:'null'}]},ageTo:{anyOf:[integer,{type:'null'}]},defaultCc:text,cCs:options,defaultAdditionalIndemnity:text,indemnities:options,additionalExcess:text}),1),bindings:list(object({owner:text,canonicalPath:path,questionId:text,selectionRule:{enum:['fixed','driver-age-indemnity','driver-age-cc','own-indemnity-excess']},collections:{...list(text,1),uniqueItems:true}},['owner','canonicalPath','selectionRule','collections']),1),sourceReferenceVersion:{type:'string',pattern:'^mt-source-[a-f0-9]{16}$'}}),$defs:{Option:object(optionProperties,['value','text'])}};

const ajv=new Ajv2020({strict:true,allErrors:true});
const questionShape=ajv.compile(questionConfigurationSchema),referenceShape=ajv.compile(referenceConfigurationSchema);
// Configuration errors are deployment/design errors, never user proposal issues.
// Source occurrences may repeat a question at the same path for separate vehicle
// sets. Its definition must still agree within each product and answer container.
export function validateQuoteConfiguration(questions,references) {
 const issues=[];
 for(const [name,validate,value] of [['questions',questionShape,questions],['references',referenceShape,references]]) {
  if(!validate(value))issues.push(...validate.errors.map(e=>`${name}${e.instancePath}: ${e.keyword}`));
 }
 if(issues.length)return issues;
 if(questions.version!==references.version)issues.push('Question/reference version mismatch');
 const signature=row=>JSON.stringify([row.answerKind,row.requiredWhen??null,row.optionCollection??null]);
 for(const product of questions.products) {
  const scoped=questions.mappings.filter(row=>row.products.includes(product));
  const definitions=new Map();
  for(const row of scoped.filter(row=>row.questionId)) {
   const key=`${row.canonicalPath}:${row.questionId}`,prior=definitions.get(key);
   if(prior&&signature(prior)!==signature(row))issues.push(`Conflicting question definition: ${key}`);
   definitions.set(key,row);
  }
  for(const row of definitions.values())if(row.requiredWhen) {
   const parent=definitions.get(`${row.canonicalPath}:${row.requiredWhen.questionId}`);
   if(!parent||parent.answerKind!=='boolean')issues.push(`Invalid conditional parent: ${row.questionId}`);
   const visited=new Set([row.questionId]);let current=row;
   while(current?.requiredWhen) {
    if(visited.has(current.requiredWhen.questionId)){issues.push(`Cyclic question condition: ${row.questionId}`);break;}
    visited.add(current.requiredWhen.questionId);
    current=definitions.get(`${row.canonicalPath}:${current.requiredWhen.questionId}`);
   }
  }
 }
 function checkOptions(rows,name) {
  const ids=new Set();
  for(const row of rows){if(ids.has(row.value))issues.push(`Duplicate option identity: ${name}/${row.value}`);ids.add(row.value);if(row.excesses)checkOptions(row.excesses,`${name}/${row.value}/excesses`);}
 }
 for(const [name,rows] of Object.entries(references.collections))checkOptions(rows,name);
 const bindingKeys=new Set();
 const bindingDefinitions=new Map();
 for(const binding of references.bindings) {
  const key=`${binding.canonicalPath}:${binding.questionId??''}:${binding.owner}`;
  if(bindingKeys.has(key))issues.push(`Duplicate reference binding: ${key}`);bindingKeys.add(key);
  const scope=`${binding.canonicalPath}:${binding.questionId??''}`,definition=JSON.stringify([binding.selectionRule,binding.collections]);
  if(bindingDefinitions.has(scope)&&bindingDefinitions.get(scope)!==definition)issues.push(`Conflicting reference binding: ${scope}`);
  bindingDefinitions.set(scope,definition);
  if(binding.selectionRule==='fixed'&&binding.collections.length!==1)issues.push(`Fixed reference binding needs one collection: ${key}`);
  for(const collection of binding.collections)if(!references.collections[collection])issues.push(`Unknown collection: ${collection}`);
  if(binding.questionId&&!questions.mappings.some(row=>row.questionId===binding.questionId&&row.canonicalPath===binding.canonicalPath&&['reference','references'].includes(row.answerKind)))issues.push(`Unbound reference question: ${binding.questionId}`);
 }
 for(const row of questions.mappings) {
  if(row.questionId&&['reference','references'].includes(row.answerKind)&&!references.bindings.some(b=>b.questionId===row.questionId&&b.canonicalPath===row.canonicalPath))issues.push(`Missing question reference binding: ${row.questionId}`);
 }
 references.youngDriverConfiguration.forEach((band,index)=>{
  const prior=references.youngDriverConfiguration[index-1];
  if((index===0&&band.ageFrom!==null)||(index>0&&(band.ageFrom===null||prior.ageTo===null||band.ageFrom!==prior.ageTo+1))||(band.ageTo!==null&&band.ageTo<(band.ageFrom??0))||(index===references.youngDriverConfiguration.length-1&&band.ageTo!==null))issues.push(`Invalid age band: ${index}`);
  for(const key of ['cCs','indemnities']) {
   checkOptions(band[key],`youngDriverConfiguration/${index}/${key}`);
   if(JSON.stringify(band[key])!==JSON.stringify(references.collections[`youngDriverConfiguration/${index}/${key}`]))issues.push(`Age band collection mismatch: ${index}/${key}`);
  }
  if(!band.cCs.some(row=>row.text===band.defaultCc)||!band.indemnities.some(row=>row.text===band.defaultAdditionalIndemnity))issues.push(`Unknown age band default: ${index}`);
 });
 for(const field of questions.directReferenceFields) {
  const rows=references.collections[field.collection];
  if(!rows||JSON.stringify(rows.map(row=>row.text))!==JSON.stringify(field.sourceOptions))issues.push(`Direct reference options mismatch: ${field.controlId}`);
  if(!references.bindings.some(row=>row.owner===field.controlId&&row.canonicalPath===field.canonicalPath&&row.collections.includes(field.collection)))issues.push(`Unbound direct reference: ${field.controlId}`);
 }
 return [...new Set(issues)];
}

export async function writeQuoteConfigurationSchemas() {
 for(const [name,schema] of [['quote-question-configuration',questionConfigurationSchema],['quote-reference-configuration',referenceConfigurationSchema]])await writeFile(new URL(`../contracts/schemas/${name}.schema.json`,import.meta.url),JSON.stringify(schema,null,2)+'\n');
}
if(process.argv[1]&&import.meta.url===pathToFileURL(process.argv[1]).href) {
 const read=async name=>JSON.parse(await readFile(new URL(`../contracts/${name}`,import.meta.url),'utf8'));
 const issues=validateQuoteConfiguration(await read('quote-question-catalogue.json'),await read('reference-data/motor-trade-capture.json'));
 if(issues.length)throw new Error(issues.join('\n'));
 await writeQuoteConfigurationSchemas();
}

