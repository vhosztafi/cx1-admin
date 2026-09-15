// Executable contract checks for the future .NET capture boundary. These are
// design validators, not a running API. Run JSON Schema validation before the
// semantic checks; neither semantic helper certifies full quote readiness.
export const maxQuoteBytes=1024*1024;
export const maxQuoteDepth=64;

export function quoteMappingsForProduct(catalogue,productCode) {
  if(!catalogue.products.includes(productCode))throw new Error('unsupported-capture-product');
  return catalogue.mappings.filter(row=>row.products.includes(productCode));
}

export function parseQuoteJson(text) {
  if(typeof text!=='string')throw new TypeError('Expected JSON text');
  if(Buffer.byteLength(text,'utf8')>maxQuoteBytes)throw new Error('quote-json-too-large');
  // Let the standard parser reject invalid grammar. A second, bounded scan is
  // necessary: JSON.parse otherwise silently keeps the last duplicate key.
  const parsed=JSON.parse(text);
  let at=0;
  const whitespace=()=>{while(/\s/.test(text[at]??'')&&at<text.length)at++;};
  const string=()=>{
    const start=at++;
    while(at<text.length) {
      if(text[at]==='\\'){at+=2;continue;}
      if(text[at++]==='"')return JSON.parse(text.slice(start,at));
    }
    throw new Error('invalid-json-string');
  };
  const value=depth=>{
    whitespace();
    if(text[at]==='{') {
      if(depth>=maxQuoteDepth)throw new Error('quote-json-too-deep');
      at++;whitespace();const keys=new Set();
      if(text[at]==='}'){at++;return;}
      while(true) {
        whitespace();const key=string();
        if(keys.has(key))throw new Error('duplicate-json-key');
        keys.add(key);whitespace();at++;value(depth+1);whitespace();
        if(text[at++]==='}')return;
      }
    }
    if(text[at]==='[') {
      if(depth>=maxQuoteDepth)throw new Error('quote-json-too-deep');
      at++;whitespace();if(text[at]===']'){at++;return;}
      while(true){value(depth+1);whitespace();if(text[at++]===']')return;}
    }
    if(text[at]==='"'){string();return;}
    while(at<text.length&&!/[\s,\]}]/.test(text[at]))at++;
  };
  value(0);return parsed;
}

const normalizedId=id=>id.toLowerCase();
const emptyId='00000000-0000-0000-0000-000000000000';
const pointerPart=key=>key.replaceAll('~','~0').replaceAll('/','~1');
function visit(value,action,path='',canonical='') {
  if(Array.isArray(value))value.forEach((item,index)=>visit(item,action,`${path}/${index}`,`${canonical}[]`));
  else if(value&&typeof value==='object') {
    action(value,path,canonical);
    for(const [key,item] of Object.entries(value))visit(item,action,`${path}/${pointerPart(key)}`,canonical?`${canonical}.${key}`:key);
  }
}

export function validateQuoteIdentity(proposal) {
  const issues=[];const ids=new Set();
  const issue=(code,path)=>issues.push({code,path});
  visit(proposal,(item,path)=>{
    if(typeof item.id!=='string')return;
    const id=normalizedId(item.id);
    if(id===emptyId){issue('invalid-item-id',`${path}/id`);return;}
    if(ids.has(id))issue('duplicate-item-id',`${path}/id`);
    ids.add(id);
  });
  const risk=proposal.risk??{};
  const idSet=items=>new Set((items??[]).map(item=>normalizedId(item.id)).filter(id=>id!==emptyId));
  const drivers=idSet(risk.drivers),vehicles=idSet(risk.vehicles);
  const riskItems=new Set([...drivers,...vehicles,...idSet(risk.premises)]);
  const link=(id,allowed,path)=>{if(normalizedId(id)===emptyId)issue('invalid-item-id',path);else if(!allowed.has(normalizedId(id)))issue('unknown-item-reference',path);};
  const links=(items,allowed,path)=>{
    const seen=new Set();
    (items??[]).forEach((id,index)=>{
      const normalized=normalizedId(id);
      if(seen.has(normalized))issue('duplicate-item-reference',`${path}/${index}`);
      seen.add(normalized);link(id,allowed,`${path}/${index}`);
    });
  };
  links(risk.specifiedVehicleIds,vehicles,'/risk/specifiedVehicleIds');
  (risk.vehicles??[]).forEach((vehicle,index)=>{
    if(vehicle.ownerDriverId)link(vehicle.ownerDriverId,drivers,`/risk/vehicles/${index}/ownerDriverId`);
  });
  (proposal.cover?.temporaryEuropeanCover??[]).forEach((trip,index)=>links(trip.driverIds,drivers,`/cover/temporaryEuropeanCover/${index}/driverIds`));
  (risk.drivers??[]).forEach((driver,d)=>(driver.losses??[]).forEach((loss,l)=>{
    if(loss.riskItemId)link(loss.riskItemId,riskItems,`/risk/drivers/${d}/losses/${l}/riskItemId`);
  }));
  return issues;
}

export function validateQuoteQuestions(proposal,mappings,questionSetVersion) {
  if(typeof questionSetVersion!=='string'||!questionSetVersion)throw new TypeError('Pinned question version required');
  const allowed=new Map();
  for(const row of mappings.filter(row=>row.contractKind==='Answer')) {
    const scope=row.canonicalPath.replace(/\.answers\[\]$/,'');
    if(!allowed.has(scope))allowed.set(scope,new Map());
    allowed.get(scope).set(row.questionId,row.answerKind);
  }
  const issues=[];
  visit(proposal,(item,path,canonical)=>{
    if(!Array.isArray(item.answers))return;
    if(item.questionSetVersion!==questionSetVersion)issues.push({code:'question-version-mismatch',path:`${path}/questionSetVersion`});
    const seen=new Set();
    item.answers.forEach((answer,index)=>{
      const answerPath=`${path}/answers/${index}`;
      if(seen.has(answer.questionId))issues.push({code:'duplicate-question-id',path:`${answerPath}/questionId`});
      seen.add(answer.questionId);
      const kind=allowed.get(canonical)?.get(answer.questionId);
      if(!kind)issues.push({code:'unknown-question-id',path:`${answerPath}/questionId`});
      else if(answer.kind!==kind)issues.push({code:'question-kind-mismatch',path:`${answerPath}/kind`});
    });
  });
  return issues;
}

// selectedCollections is keyed by the exact reference JSON pointer, so two
// drivers may have different age bands. It is trusted context from the proposal
// and pinned product metadata, never a request field. Dynamic options fail
// closed until that selector has run. Numeric limits/eligibility still require
// their own domain checks; catalogue membership alone is insufficient.
export function validateQuoteReferences(proposal,catalogue,selectedCollections={}) {
  const issues=[];
  const validate=(reference,binding,path)=>{
    if(!binding){issues.push({code:'unbound-reference',path});return;}
    const selected=binding.selectionRule==='fixed'?binding.collections:selectedCollections[path];
    if(!Array.isArray(selected)||!selected.length||selected.some(name=>!binding.collections.includes(name))) {
      issues.push({code:'reference-context-required',path});return;
    }
    if(reference.version!==catalogue.version){issues.push({code:'reference-version-mismatch',path:`${path}/version`});return;}
    if(!selected.includes(reference.collection)){issues.push({code:'reference-collection-mismatch',path:`${path}/collection`});return;}
    const row=catalogue.collections[reference.collection]?.find(row=>row.value===reference.value);
    if(!row){issues.push({code:'unknown-reference-value',path:`${path}/value`});return;}
    if(row.text!==reference.label)issues.push({code:'reference-label-mismatch',path:`${path}/label`});
  };
  visit(proposal,(item,path,canonical)=>{
    if(['reference','references'].includes(item.kind)&&typeof item.questionId==='string') {
      const binding=catalogue.bindings.find(row=>row.questionId===item.questionId&&row.canonicalPath===canonical);
      if(item.kind==='reference')validate(item.value,binding,`${path}/value`);
      else {
        const seen=new Set();
        item.value.forEach((reference,index)=>{
          const identity=JSON.stringify([reference.collection,reference.value]);
          if(seen.has(identity))issues.push({code:'duplicate-reference-selection',path:`${path}/value/${index}`});
          seen.add(identity);validate(reference,binding,`${path}/value/${index}`);
        });
      }
    } else if(typeof item.collection==='string'&&!canonical.includes('.answers[].value')) {
      const binding=catalogue.bindings.find(row=>!row.questionId&&row.canonicalPath===canonical);
      validate(item,binding,path);
    }
  });
  return issues;
}

// Readiness checks for mapped conditional scalar answers. These do not require
// unanswered independent questions: the complete product question catalogue
// owns that later check. Retained inactive child answers are never auto-cleared.
export function validateQuoteAnswerConditions(proposal,mappings) {
  const rules=mappings.filter(row=>row.requiredWhen);
  const issues=[];
  visit(proposal,(item,path,canonical)=>{
    if(!Array.isArray(item.answers))return;
    const answers=new Map(item.answers.map((answer,index)=>[answer.questionId,{answer,index}]));
    for(const rule of rules.filter(row=>row.canonicalPath===`${canonical}.answers[]`)) {
      const parent=answers.get(rule.requiredWhen.questionId);
      const child=answers.get(rule.questionId);
      const childPath=child?`${path}/answers/${child.index}/value`:`${path}/answers`;
      if(parent?.answer.value===rule.requiredWhen.equals) {
        if(!child||child.answer.kind==='text'&&!child.answer.value.trim())
          issues.push({code:'conditional-answer-required',path:childPath,questionId:rule.questionId});
      } else if(child) {
        issues.push({code:parent?'inactive-answer-retained':'controlling-answer-required',path:childPath,questionId:rule.questionId});
      }
    }
  });
  return issues;
}

export function validateQuotePortfolio(proposal,mappings) {
  const rules=mappings.filter(row=>row.owner.startsWith('MTS-10-')&&row.answerKind==='percentage');
  const answers=proposal.risk?.responses?.answers??[];
  const byId=new Map(answers.map((answer,index)=>[answer.questionId,{answer,index}]));
  const selected=rules.filter(rule=>byId.get(rule.requiredWhen.questionId)?.answer.value===true);
  if(!selected.length)return [{code:'vehicle-category-required',path:'/risk/responses/answers'}];
  const issues=[];let total=0;
  for(const rule of selected) {
    const entry=byId.get(rule.questionId);
    const value=entry?.answer.value;
    if(!Number.isInteger(value)||value<=0||value>10000)
      issues.push({code:'positive-portfolio-percentage-required',path:entry?`/risk/responses/answers/${entry.index}/value`:'/risk/responses/answers',questionId:rule.questionId});
    else total+=value;
  }
  if(!issues.length&&total!==10000)issues.push({code:'portfolio-total-must-equal-100-percent',path:'/risk/responses/answers'});
  return issues;
}
