import type {CommercialProposal} from './commercial-capture';
import type {QuoteObject,QuoteValue} from './quotes';
import type {ServicingChange,ServicingDraft,ServicingEditor,ServicingProposal} from './servicing-api';
import {servicingDateFeedback} from './servicing-proposal.ts';

export type CommercialChangeKind='commercial-location'|'commercial-property'|'commercial-business'|'commercial-bi'|'commercial-liability'|'commercial-wage'|'commercial-loss'|'commercial-cover'|'commercial-insured'|'commercial-declarations';
export type CommercialServicingChange=Omit<ServicingChange,'kind'|'specifiedVehicle'>&{kind:CommercialChangeKind};
export type CommercialServicingProposal=Omit<ServicingProposal,'changes'>&{changes:CommercialServicingChange[]};
export type CommercialServicingDraft=ServicingDraft<CommercialServicingProposal>;
export type CommercialServicingEditor=ServicingEditor<CommercialProposal>;
const obj=(value:unknown):QuoteObject=>value&&typeof value==='object'&&!Array.isArray(value)?value as QuoteObject:{};
const rows=(value:unknown):QuoteObject[]=>Array.isArray(value)?value.map(obj):[];
const collection:Partial<Record<CommercialChangeKind,string>>={'commercial-location':'locations','commercial-property':'locations','commercial-wage':'wages','commercial-loss':'losses'};
const singleton:Partial<Record<CommercialChangeKind,string>>={'commercial-business':'business','commercial-bi':'businessInterruption','commercial-liability':'liability','commercial-cover':'cover','commercial-insured':'insured'};
function merge(target:QuoteObject,payload:QuoteObject) {
 for(const [key,value] of Object.entries(payload)) {
  if(value&&typeof value==='object'&&!Array.isArray(value)&&target[key]&&typeof target[key]==='object'&&!Array.isArray(target[key]))merge(target[key] as QuoteObject,value);
  else target[key]=structuredClone(value);
 }
}
const ordered=(proposal:CommercialServicingProposal)=>proposal.changes.map((change,index)=>({change,index,instant:servicingDateFeedback(change.effectiveIntent??proposal.commonEffectiveIntent).instant??0})).sort((a,b)=>a.instant-b.instant||a.index-b.index);
export function commercialServicingCapture(base:CommercialProposal,proposal:CommercialServicingProposal):CommercialProposal {
 if(base.productCode!=='commercial-combined')throw new Error('The commercial issued base is unavailable.');
 const result=structuredClone(base),root=result as unknown as QuoteObject;root.risk??={};
 const risk=obj(root.risk);
 for(const {change} of ordered(proposal)) {
  const path=collection[change.kind],payload=change.payload??{};
  if(path) {
   risk[path]??=[];const items=risk[path] as QuoteObject[],index=items.findIndex(x=>x.id===change.riskItemId);
   if(change.operation==='add') {if(index>=0)throw new Error('This commercial item already exists.');items.push({...structuredClone(payload),id:change.riskItemId});}
   else {
    if(index<0)throw new Error('The changed item is not in this commercial base.');
    if(change.operation==='remove')items.splice(index,1);
    else if(change.kind==='commercial-property') {if(change.payloadMode==='replace')for(const field of ['buildings','contents','stock','maximumEstimatedLoss'])delete items[index][field];merge(items[index],payload);}
    else {if(change.payloadMode==='replace')items[index]={id:change.riskItemId};merge(items[index],payload);}
   }
  } else if(change.kind==='commercial-declarations') {
   if(change.payloadMode==='replace'){delete risk.declarations;delete risk.materialFacts;}merge(risk,payload);
  } else {
   const name=singleton[change.kind];if(!name||change.operation!=='update')throw new Error('This change does not belong to Commercial Combined.');
   const owner=change.kind==='commercial-cover'||change.kind==='commercial-insured'?root:risk;
   if(change.payloadMode==='replace'||!owner[name])owner[name]={};merge(obj(owner[name]),payload);
  }
 }
 return result;
}
function normalized(value:unknown):unknown {
 if(Array.isArray(value))return value.map(normalized);
 if(value&&typeof value==='object')return Object.fromEntries(Object.entries(value).filter(([,v])=>v!==undefined).sort(([a],[b])=>a.localeCompare(b)).map(([key,item])=>[key,normalized(item)]));
 return value;
}
const equal=(left:unknown,right:unknown)=>JSON.stringify(normalized(left))===JSON.stringify(normalized(right));
const withoutId=(value:QuoteObject):QuoteObject=>Object.fromEntries(Object.entries(value).filter(([key])=>key!=='id'));
export function updateCommercialServicing(base:CommercialProposal,proposal:CommercialServicingProposal,next:CommercialProposal,policyId:string,clientId:string):CommercialServicingProposal {
 const current=commercialServicingCapture(base,proposal);
 if(next.productCode!==base.productCode||next.format!==base.format||!equal(next.termIntent,base.termIntent))throw new Error('Product and issued term cannot be changed here.');
 const result=structuredClone(proposal);
 function put(kind:CommercialChangeKind,id:string,value:QuoteObject|undefined,sourceExists:boolean) {
  const related=(x:CommercialServicingChange)=>x.riskItemId===id&&(x.kind===kind||kind==='commercial-location'&&x.kind==='commercial-property');
  const previous=ordered(result).filter(x=>related(x.change)).at(-1)?.change;
  if(value===undefined) {
   result.changes=result.changes.filter(x=>!related(x));
   if(sourceExists)result.changes.push({changeId:previous?.changeId??crypto.randomUUID(),riskItemId:id,kind,operation:'remove'});
   return;
  }
  const change:CommercialServicingChange={changeId:previous?.changeId??crypto.randomUUID(),riskItemId:id,kind,operation:sourceExists?'update':'add',payload:structuredClone(value),
   ...(sourceExists?{payloadMode:'replace' as const}:{}),...(previous?.effectiveIntent?{effectiveIntent:previous.effectiveIntent}:{})};
  if(previous)result.changes[result.changes.findIndex(x=>x.changeId===previous.changeId)]=change;else result.changes.push(change);
 }
 for(const [kind,path] of [['commercial-location','locations'],['commercial-wage','wages'],['commercial-loss','losses']] as const) {
  const before=rows(current.risk?.[path]),after=rows(next.risk?.[path]),source=rows(base.risk?.[path]);
  if(new Set(after.map(x=>x.id)).size!==after.length||after.some(x=>typeof x.id!=='string'))throw new Error('Each commercial item needs a unique identity.');
  for(const id of new Set([...before,...after].map(x=>x.id as string))) {
   const old=before.find(x=>x.id===id),value=after.find(x=>x.id===id);
   if(!equal(old,value))put(kind,id,value?withoutId(value):undefined,source.some(x=>x.id===id));
  }
 }
 const entries:[CommercialChangeKind,QuoteValue|undefined,QuoteValue|undefined,string][]=[
  ['commercial-insured',current.insured,next.insured,clientId],['commercial-business',current.risk?.business,next.risk?.business,policyId],
  ['commercial-bi',current.risk?.businessInterruption,next.risk?.businessInterruption,policyId],['commercial-liability',current.risk?.liability,next.risk?.liability,policyId],
  ['commercial-cover',current.cover,next.cover,policyId],['commercial-declarations',
   {...(current.risk?.declarations?{declarations:current.risk.declarations}:{}),...(current.risk?.materialFacts!==undefined?{materialFacts:current.risk.materialFacts}:{})},
   {...(next.risk?.declarations?{declarations:next.risk.declarations}:{}),...(next.risk?.materialFacts!==undefined?{materialFacts:next.risk.materialFacts}:{})},policyId]];
 for(const [kind,before,after,id] of entries)if(!equal(before,after))put(kind,id,obj(after),true);
 return result;
}
export function removeCommercialChange(proposal:CommercialServicingProposal,changeId:string):CommercialServicingProposal {
 const target=proposal.changes.find(x=>x.changeId===changeId);if(!target)throw new Error('This proposed change is no longer present.');
 return {...proposal,changes:proposal.changes.filter(x=>target.operation==='add'?x.riskItemId!==target.riskItemId:x.changeId!==changeId)};
}
