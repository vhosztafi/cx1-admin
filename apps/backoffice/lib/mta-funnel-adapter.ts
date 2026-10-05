import type {QuoteObject,QuoteProposal} from './quotes';
import type {ServicingChange,ServicingEditor,ServicingProposal} from './servicing-api';
import {funnelToProposal,proposalToFunnel,type FunnelCatalogue,type FunnelState} from './funnel-adapter.ts';
import {londonCandidates} from './quote-term.ts';
const object=(value:unknown):value is QuoteObject=>!!value&&typeof value==='object'&&!Array.isArray(value);
function normalized(value:unknown,key=''):unknown{
 if(Array.isArray(value)){const rows=value.map(item=>normalized(item));if(!rows.length)return undefined;if(key==='answers')rows.sort((a,b)=>String((a as QuoteObject).questionId).localeCompare(String((b as QuoteObject).questionId)));return rows;}
 if(object(value)){const entries=Object.entries(value).map(([name,item])=>[name,normalized(item,name)] as const).filter(([,item])=>item!==undefined);return entries.length?Object.fromEntries(entries.sort(([a],[b])=>a.localeCompare(b))):undefined;}
 return value;
}
function stable(value:unknown):string{return JSON.stringify(normalized(value))??'undefined';}
const same=(a:unknown,b:unknown)=>stable(a)===stable(b);
const excluded=['business','drivers','vehicles','premises','specifiedVehicleIds','specifiedVehiclesRequested'];
function details(proposal:QuoteProposal):QuoteObject{return Object.fromEntries(Object.entries(proposal.risk??{}).filter(([key])=>!excluded.includes(key)));}
export function mtaToFunnel(current:ServicingProposal,editor:ServicingEditor,catalogue:FunnelCatalogue){
 const term=editor.assessment.base.termIntent;const start=term?.localStartDate;
 let end=term?.localEndDate;
 if(!end&&start){const [year,month,day]=start.split('-').map(Number);const anniversary=new Date(Date.UTC(year+1,month-1,day));if(anniversary.getUTCMonth()!==month-1)anniversary.setUTCDate(0);end=anniversary.toISOString().slice(0,10);}
 return {...proposalToFunnel(editor.assessment.proposed,catalogue),isMta:true,minPolicyStartDate:start,maxPolicyStartDate:end,proposerPolicyStartDate:current.commonEffectiveIntent.localDate,proposerPolicyStartTime:current.commonEffectiveIntent.localTime};
}
export function funnelToMta(state:FunnelState,current:ServicingProposal,editor:ServicingEditor,policyId:string,catalogue:FunnelCatalogue){
 const date=state.formData.proposerPolicyStartDate;const localDate=date instanceof Date?date.toISOString().slice(0,10):typeof date==='string'?date.slice(0,10):current.commonEffectiveIntent.localDate;
 const localTime=typeof state.formData.proposerPolicyStartTime==='string'?state.formData.proposerPolicyStartTime:current.commonEffectiveIntent.localTime;
 const common={...current.commonEffectiveIntent,localDate,localTime};if(localDate!==current.commonEffectiveIntent.localDate||localTime!==current.commonEffectiveIntent.localTime){delete common.utcOffsetMinutes;const candidates=londonCandidates(localDate,localTime);if(candidates.length===1)common.utcOffsetMinutes=candidates[0].offset;}
 if(state.formData.isShortTerm!==undefined&&state.formData.isShortTerm!==(editor.assessment.proposed.termIntent?.kind==='short-period'))throw new Error('An MTA cannot change the issued policy duration.');
 const bound=editor.assessment.proposed.termIntent;const source={...state,formData:{...state.formData,proposerPolicyStartDate:bound?.localStartDate,proposerPolicyStartTime:bound?.localStartTime,isShortTerm:bound?.kind==='short-period'}};
 const capture=funnelToProposal(source,editor.assessment.proposed,catalogue);capture.termIntent=structuredClone(bound);
 const previousVehicles=editor.assessment.proposed.risk?.vehicles;
 if(Array.isArray(capture.risk?.vehicles)&&Array.isArray(previousVehicles))for(const next of capture.risk.vehicles){if(!object(next))continue;const previous=previousVehicles.find(item=>object(item)&&item.id===next.id);if(object(previous)&&same(next.declaredOwnerType,previous.declaredOwnerType)){if(previous.ownership===undefined)delete next.ownership;else next.ownership=previous.ownership;}}
 return captureToMta(capture,{...current,commonEffectiveIntent:common},editor,policyId);
}
export function captureToMta(capture:QuoteProposal,current:ServicingProposal,editor:ServicingEditor,policyId:string):ServicingProposal{
 const before=editor.assessment.proposed,base=editor.assessment.base;
 if(capture.productCode!==before.productCode)throw new Error('The MTA product cannot change.');
 const term=(proposal:QuoteProposal)=>Object.fromEntries(Object.entries(proposal.termIntent??{}).filter(([key])=>!['utcOffsetMinutes','endUtcOffsetMinutes'].includes(key)));
 if(!same(term(capture),term(before)))throw new Error('An MTA cannot change the policy term. Restore the issued start and duration; use the MTA effective date for changes.');
 const result=structuredClone(current);
 function put(kind:ServicingChange['kind'],id:string,next:QuoteObject|undefined,previous:QuoteObject|undefined,original:QuoteObject|undefined,vehicle?:ServicingChange['specifiedVehicle']){
  if(same(next,previous)&&!vehicle)return;
  const existing=result.changes.filter(change=>change.kind===kind&&change.riskItemId.toLowerCase()===id.toLowerCase());
  if(existing.length>1)throw new Error('This item has changes on multiple dates. Review its individual changes before editing it in the funnel.');
  result.changes=result.changes.filter(change=>!existing.includes(change));
  if(same(next,original)&&!vehicle)return;
  const old=existing[0];const payload=next?structuredClone(next):undefined;if(payload)delete payload.id;
  const operation=next?original?'update':'add':'remove';
  if(!next&&!original)return;
  result.changes.push({changeId:old?.changeId??crypto.randomUUID(),riskItemId:id,kind,operation,...(payload?{payload,...(operation==='update'?{payloadMode:'replace' as const}:{})}:{}),...(old?.effectiveIntent?{effectiveIntent:old.effectiveIntent}:{}),...(vehicle?{specifiedVehicle:vehicle}:{})});
 }
 for(const [kind,key] of [['driver','drivers'],['vehicle','vehicles'],['premises','premises']] as const){
  const rows=(proposal:QuoteProposal)=>(proposal.risk?.[key]??[]) as QuoteObject[];const oldRows=rows(before),newRows=rows(capture),baseRows=rows(base);
  const commonIds=oldRows.filter(row=>newRows.some(next=>next.id===row.id)).map(row=>row.id);const newCommonIds=newRows.filter(row=>oldRows.some(old=>old.id===row.id)).map(row=>row.id);
  if(!same(commonIds,newCommonIds))throw new Error('The MTA cannot reorder existing risk records. Restore their order before saving.');
  for(const id of new Set([...oldRows,...newRows].map(row=>String(row.id)))){
   const next=newRows.find(row=>row.id===id),previous=oldRows.find(row=>row.id===id),original=baseRows.find(row=>row.id===id);
   const selected=(proposal:QuoteProposal)=>((proposal.risk?.specifiedVehicleIds??[]) as string[]).includes(id);
   const required=(proposal:QuoteProposal)=>proposal.risk?.specifiedVehiclesRequested===true;
   const selectionChanged=kind==='vehicle'&&(selected(capture)!==selected(before)||required(capture)!==required(before));
   const vehicle=kind==='vehicle'&&(selectionChanged||!same(next,previous))?{selected:!!next&&selected(capture),required:required(capture)}:undefined;
   put(kind,id,next,previous,original,vehicle);
  }
 }
 for(const [kind,next,previous,original,id] of [
  ['policyholder',capture.insured,before.insured,base.insured,editor.clientId],
  ['business',capture.risk?.business,before.risk?.business,base.risk?.business,policyId],
  ['cover',capture.cover,before.cover,base.cover,policyId],
  ['risk-details',details(capture),details(before),details(base),policyId],
 ] as const){if(!same(next,previous)){if(kind==='cover'&&current.changes.some(change=>change.kind==='cover'&&change.riskItemId!==policyId))throw new Error('This MTA contains individual cover changes. Review those dated cover changes before editing all cover in the funnel.');put(kind,id,(next??{}) as QuoteObject,(previous??{}) as QuoteObject,(original??{}) as QuoteObject);}}
 if(result.changes.length>100)throw new Error('This MTA exceeds the supported 100 changes.');
 return result;
}
