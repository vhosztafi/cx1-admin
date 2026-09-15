import {readFile,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
const read=async path=>JSON.parse(await readFile(new URL(`../${path}`,import.meta.url),'utf8'));
const names={'Motor Trade Road Risks':'motor-trade-road-risks','Motor Trade Combined':'motor-trade-combined','Commercial Combined':'commercial-combined'};
const products=['motor-trade-road-risks','motor-trade-combined'];
const sourceMappings=(await read('contracts/quote-field-mapping.json')).mappings;
const controls=(await read('docs/design/control-inventory.json')).controls;
const rendered=(await read('docs/design/source/prototype-render-data.json')).items;
const sources=['prototype-quote-questions','prototype-detail-questions','prototype-quote-value-questions'];
const active=[],deferred=[];
for(const name of sources) {
  const source=await read(`contracts/examples/${name}.json`);
  for(const question of source.questions) {
    const control=controls.find(row=>row.id===question.sourceControlId);
    if(!control)throw new Error(`Missing source control ${question.sourceControlId}`);
    let stages=question.stages??[];
    let applicable;
    if(name==='prototype-detail-questions') {
      applicable=/^risk\.(locations|wages|losses)\[\]/.test(question.targetContainer)?['commercial-combined']:
        question.targetContainer==='risk.premises[].responses'?['motor-trade-combined']:products;
    } else {
      if(!stages.length)stages=rendered.filter(row=>row.method===control.method&&row.path===control.path&&row.label===control.label).flatMap(row=>row.tabs??[]).filter(tab=>tab.includes(':step-'));
      applicable=[...new Set(stages.map(stage=>names[stage.split(':step-')[0]]))];
      if(!applicable.length||applicable.some(product=>!product))throw new Error(`Unknown source stages ${question.questionId}`);
    }
    const entry={...question,stages:[...new Set(stages)],products:applicable,sourceCatalogue:source.version,
      disposition:applicable.some(product=>products.includes(product))?'phase-05-capture':'phase-08-commercial-combined'};
    if(entry.disposition==='phase-08-commercial-combined'){deferred.push(entry);continue;}
    active.push({...entry,owner:question.questionId,canonicalPath:`${question.targetContainer}.answers[]`,contractKind:'Answer',answerKind:question.kind});
  }
}
// Conditional composers are not present in the default render inventory.
// Preserve the separately audited source control instead of losing its answer.
const conditional=await read('contracts/examples/conditional-capture.json');
const reason=conditional.controls.find(row=>row.id==='COND-NCD-REASON');
if(!reason?.question)throw new Error('missing-ncd-reason-contract');
active.push({...reason.question,label:'Reason no discount is being claimed',owner:reason.question.questionId,
 sourceControlId:reason.id,sourceKey:reason.sourceKey,sourceCatalogue:conditional.version,
 targetContainer:'risk.previousInsurance.responses',canonicalPath:'risk.previousInsurance.responses.answers[]',
 contractKind:'Answer',answerKind:'text',products:['motor-trade-road-risks'],disposition:'phase-05-capture',
 requiredWhen:{questionId:'prototype.quote.1bdc05ff8b3d',equals:false},
 stages:['Motor Trade Road Risks:step-7']});
const mappings=[...sourceMappings.map(row=>({...row,products})),...active];
const sourceReference=await read('contracts/reference-data/motor-trade-source.json');
const references=structuredClone(sourceReference);
for(const question of active.filter(row=>row.kind==='reference')) {
  const name=question.questionId;
  const options=question.referenceValues??question.sourceOptions.map((label,index)=>({value:index+1,label}));
  if(!options.length)throw new Error(`Missing prototype options ${name}`);
  references.collections[name]=options.map(row=>({value:row.value,text:row.label}));
  references.bindings.push({owner:name,questionId:name,canonicalPath:question.canonicalPath,selectionRule:'fixed',collections:[name]});
}
const directReferenceFields=[];
for(const [controlId,modal,collection,canonicalPath,applicable] of [
 ['CTL-6c4f6c3ca8c9','addveh','prototype.vehicle-body','risk.vehicles[].body',products],
 ['CTL-196064c77a8f','addprem','prototype.premises-use','risk.premises[].declaredUse',['motor-trade-combined']],
 ['CTL-7390fe5a19a7','addprem','prototype.premises-security','risk.premises[].security',['motor-trade-combined']],
]) {
 const control=controls.find(row=>row.id===controlId);
 const renderedControl=rendered.find(row=>row.method===control?.method&&row.path===control.path&&row.label===control.label&&row.tabs?.includes(modal));
 if(!renderedControl?.options?.length)throw new Error(`missing-direct-options:${controlId}`);
 const field={controlId,collection,canonicalPath,products:applicable,sourceOptions:renderedControl.options};
 directReferenceFields.push(field);
 references.collections[collection]=field.sourceOptions.map((text,index)=>({value:index+1,text}));
 references.bindings.push({owner:controlId,canonicalPath,selectionRule:'fixed',collections:[collection]});
}
const digest=createHash('sha256').update(JSON.stringify({mappings,deferred,directReferenceFields,sourceReferenceVersion:sourceReference.version})).digest('hex');
const version=`mt-capture-${digest.slice(0,16)}`;
references.sourceReferenceVersion=sourceReference.version;references.version=version;
references.status='Combined identity catalogue; prototype and funnel eligibility/conflict reconciliation still requires product validation.';
const catalogue={version,status:'Question identity and product ownership catalogue; conditional readiness, overlapping source answers and control audit remain pending.',products,mappings,deferredQuestions:deferred,directReferenceFields};
await writeFile(new URL('../contracts/quote-question-catalogue.json',import.meta.url),JSON.stringify(catalogue,null,2)+'\n');
await writeFile(new URL('../contracts/reference-data/motor-trade-capture.json',import.meta.url),JSON.stringify(references,null,2)+'\n');
console.log(JSON.stringify({version,sourceFields:sourceMappings.length,prototypeQuestions:active.length,deferredQuestions:deferred.length,referenceBindings:references.bindings.length}));
