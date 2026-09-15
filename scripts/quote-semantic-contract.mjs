// Executable contract checks for the future .NET capture boundary. These are
// design validators, not a running API. Run JSON Schema validation before the
// semantic checks; neither semantic helper certifies full quote readiness.
export const maxQuoteBytes=1024*1024;
export const maxQuoteDepth=64;

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
    if(ids.has(id))issue('duplicate-item-id',`${path}/id`);
    ids.add(id);
  });
  const risk=proposal.risk??{};
  const idSet=items=>new Set((items??[]).map(item=>normalizedId(item.id)));
  const drivers=idSet(risk.drivers),vehicles=idSet(risk.vehicles);
  const riskItems=new Set([...drivers,...vehicles,...idSet(risk.premises)]);
  const link=(id,allowed,path)=>{if(!allowed.has(normalizedId(id)))issue('unknown-item-reference',path);};
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
