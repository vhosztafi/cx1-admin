import {readFile,writeFile} from 'node:fs/promises';
const read=async p=>JSON.parse(await readFile(new URL(p,import.meta.url),'utf8'));
const [ownership,catalogue,inventory,render]=await Promise.all([
 read('../contracts/quote-control-ownership.json'),read('../contracts/quote-question-catalogue.json'),
 read('../docs/design/control-inventory.json'),read('../docs/design/source/prototype-render-data.json')]);
const conditional={
 'prototype.quote.9608e47f7b80':'Electrical inspection confirmed; unknown is not rectified',
 'prototype.quote.aaccb5c97c33':'Subsidence cover requested',
 'prototype.quote.8293bc041f81':'Subsidence cover requested',
 'prototype.quote.02d9c6be528a':'Subsidence cover requested',
 'prototype.quote.a6a4579c177e':'Subsidence cover requested',
 'prototype.quote.7a732bc99a5f':'Subsidence cover requested',
 'prototype.quote.fcc7e22c33ea':'Subsidence cover requested',
 'prototype.quote.923e600eb3a4':'Subsidence cover requested',
 'prototype.quote.7bd824326d4c':'Business interruption selected',
 'prototype.quote.9b4688f28580':'Business interruption selected',
 'prototype.quote.72cb884b6df1':'Business interruption selected',
 'prototype.quote.a2f1dd35162a':'Business interruption selected',
 'prototype.quote.ab908171e40b':'Business interruption selected',
 'prototype.quote-value.59c91e8db152':'At least one location is not sole occupier; identify the relevant location in details',
 'prototype.quote-value.acace76b1053':'At least one location is detached; identify the relevant location in details',
 'prototype.quote-value.5fcb5378a1fd':'Mortgage or charge to be noted',
 'prototype.quote-value.b99b3a5b3004':'At least one adverse proposer/history answer is Yes',
 'prototype.quote-value.4799b8daa1ca':'At least one adverse flood/subsidence answer is Yes',
 'prototype.quote-value.a0e5d1b910f8':'At least one health/safety answer is No or outstanding',
 'prototype.quote.4a288359be03':'Subsidence cover requested',
 'prototype.addloc.police-response':'Location intruder alarm selected',
};
const questions=catalogue.deferredQuestions.map(q=>({
 ...q,targetPath:`${q.targetContainer}.answers`,
 subjectScope:q.targetContainer.includes('locations[]')?'location':q.targetContainer.includes('wages[]')?'wage':q.targetContainer.includes('losses[]')?'loss':'proposal',
 applicability:conditional[q.questionId]??'When the owning source section or item is applicable; missing remains unanswered',
 ownerPlan:q.stages.some(x=>/:step-[23]$/.test(x))||q.questionId.startsWith('prototype.addloss.')?'08-03':'08-04',
 runtimeStatus:'pending',verification:[]
}));
const questionsByControl=new Map(questions.map(x=>[x.sourceControlId,x]));
const stages=['Agency & product','Proposer & history','Claims & losses','Locations & occupancy','Construction & protections','Flood & subsidence','Sums insured & BI','Liability & wage roll','Health & safety','Cover & declarations'];
const evidence=new Set(['CTL-e48b5c40a127','CTL-73b23541e556','CTL-8edb6a187769','CTL-98e734d0c0dc','CTL-de020621bc82','CTL-edc0a8d5978f']);
const captureControls=ownership.controls.filter(x=>x.featurePhase===8).map(c=>{
 const q=questionsByControl.get(c.controlId),bindings=c.sourceFieldBindings;
 const fields=bindings.map(x=>x.targetPath).join(';');
 const navigation=stages.includes(c.label),selection=['CTL-edd4bf8b620d','CTL-2dd04027e52d'].includes(c.controlId);
 const business=fields.startsWith('insured.')||fields.startsWith('risk.business.')||fields.startsWith('risk.losses');
 return {controlId:c.controlId,method:c.method,path:c.path,label:c.label,sourceModalKinds:c.sourceModalKinds,sourceFieldBindings:bindings,
  ownerPlan:evidence.has(c.controlId)?'08-06':selection?'08-02':q?q.ownerPlan:navigation?'08-03':business?'08-03':'08-04',
  operation:evidence.has(c.controlId)?'Existing scoped quote evidence attachment/review commands':selection?'POST /api/v1/quotes':navigation?'Client stage navigation; GET /api/v1/quotes/{quoteId}':'PUT /api/v1/quotes/{quoteId}/proposal',
  disposition:evidence.has(c.controlId)?'persistent-evidence':selection?'product-selection':navigation?'stage-navigation':bindings.length?'persisted-field-or-collection':'item-add-dialog',
  targetPaths:bindings.map(x=>x.targetPath),questionId:q?.questionId??null,runtimeStatus:'pending',verification:[]};
});
const policyControls=inventory.controls.filter(x=>x.method==='pCcPolicy').map(c=>{
 const fallback=/motor trade/i.test(c.label),future=c.id==='CTL-94f70d0cd3fa'||c.id==='CTL-04e2a11eab51',config=c.id==='CTL-5b3f0138f6c3';
 return {controlId:c.id,label:c.label,path:c.path,tabs:c.tabs,scenarios:c.scenarios,
  ownerPhase:future?9:config?11:8,ownerPlan:future?null:config?null:/Adjustment|Propose a change/.test(c.label)?'08-11':'08-10',
  disposition:future?'shared-operations-later-phase':config?'configuration-administration-later-phase':fallback?'invalid-tab-fallback':'cc-policy-action',
  contract:future?'Phase8 supplies exact-version CC payloads; Phase9 owns real document/incident action.':config?'Retain honest configuration context; Phase11 owns administration link.':fallback?'Source renders the global tab set against CC; remove invalid MT fallback from CC routes. Use scoped actual product navigation only.':'Bind actual scoped CC policy data/action; never execute prototype toast as success.',
  runtimeStatus:'pending',verification:[]};
});
const referralRows=[
 ['PR-04','Flood at a location or within250m'],['PR-05','Advised potential flood area'],['PR-08','Subsidence selected with movement/history outside appetite'],
 ['PR-11','Composite panels'],['PR-12','Timber frame'],['PR-14','Listed building'],['PR-17','Waste burning outside appetite'],['PR-18','Recycling trade outside appetite'],['PR-21','Unoccupied30days'],
 ['AU-05','Single-location buildings+contents+stock greater than2500000.00'],['AU-06','Largest-location demo MEL greater than2000000.00'],
 ['LI-03','Heat away'],['LI-05','Work above2metres'],['LI-08','Refinery/offshore/aviation/railway/airport work'],['LI-11','Products sourced outside accepted territories'],['LI-12','USA/Canada turnover'],['LI-15','No formal written health/safety policy'],['LI-18','Prohibition/prosecution'],['UW-18','Declined/cancelled/special insurance terms'],['UW-20','Insolvency/CCJ'],['UW-30','More than2 losses in5years']
];
const branches=referralRows.map(([id,condition])=>({id,sourceRule:id.replace('-',''),condition,ownerPlan:'08-06',verification:'Unit branch/boundary and SQL current decision/proof tests; pending',runtimeStatus:'pending'}));
for(const [id,condition,ownerPlan] of [
 ['CC-READY','Proposer/trade description/turnover, at least1 positive-SI location, applicable ERN/wages, electrical confirmation and consistent loss declaration','08-02'],
 ['CC-DISTRICT-CAPACITY','Whole-book property total at every affected effective breakpoint is within its published applicable book limit; own risk replaced, not double-counted','08-09'],
 ['CC-EL-CERTIFICATE','Request EL certificate only when EL selected in exact issued version','08-09'],
 ['CC-RENEWAL-BASE','Use expiring term-end winner, preserve current term until inception','08-13'],
 ['CC-CANCELLATION-RELEASE','Cancellation zero header releases exposure only at effective time','08-14'],
 ['CC-SOURCE-MT-FALLTHROUGH','Do not apply prototype unconditional MT checks after its CC branch','08-06']
])branches.push({id,condition,ownerPlan,verification:'Independent positive/negative temporal or product-boundary tests; pending',runtimeStatus:'pending'});
const supplementalFields=[
 ['CC-POL-VAT','VAT registered','risk.business.vatRegistered','boolean','08-03'],
 ['CC-POL-CONTRACT-WORKS','Contract works','cover.contractWorks','selected/sumInsured/excess','08-04'],
 ['CC-POL-HOT-WORKS','Hot works procedures','risk.liability.hotWorksProcedures','text with separate evidence','08-04'],
 ['CC-POL-BI-DECLARATION','Declaration linked','risk.businessInterruption.declarationLinked','boolean','08-04'],
 ['CC-POL-BI-DEPENDENCIES','Suppliers extension','risk.businessInterruption.dependencies[]','stable ID/kind/name/limit','08-04'],
 ['CC-POL-SPRINKLERS','Sprinklers','risk.locations[].sprinklers','boolean','08-04'],
 ['CC-POL-FLOOD-ZONE','Flood zone','risk.locations[].floodZone','declared demo zone1/2/3/unknown, not a geographic lookup','08-04']
].map(([id,label,targetPath,kind,ownerPlan])=>({id,label,targetPath,kind,ownerPlan,sourceMethod:'pCcPolicy',reason:'Persist source policy display fact absent from the109 capture-question definitions; do not invent a fixed sample value.',runtimeStatus:'pending',verification:[]}));
const policyDisplayItems=render.items.filter(x=>x.method==='pCcPolicy').map((x,index)=>{
 const claims=x.tabs.length===1&&x.tabs[0]==='Claims',cash=x.label==='Outstanding balance',later=claims||cash;
 return {sourceOccurrence:index,source:x,ownerPhase:claims?9:cash?10:8,ownerPlan:later?null:'08-10',disposition:later?'shared-operations-later-phase':'cc-display-or-source-fallback',runtimeStatus:'pending'};
});
const result={format:'commercial-source-ledger-1',sourceSha256:inventory.sourceSha256,status:'design-mapped-runtime-pending',
 denominators:{captureControls:166,questions:109,policyControls:60,policyDisplayItems:policyDisplayItems.length,supplementalFields:supplementalFields.length},captureControls,questions,supplementalFields,policyControls,branches,policyDisplayItems};
// Runtime evidence is authored only after checks pass. Keep it separate from
// source extraction so regeneration cannot erase verification or invent it.
let runtimeEvidence;
try { runtimeEvidence=await read('../.planning/phases/08-commercial-combined-back-office/08-SOURCE-EVIDENCE.json'); }
catch(error){if(error.code!=='ENOENT')throw error;}
if(runtimeEvidence){
 if(runtimeEvidence.sourceSha256!==result.sourceSha256)throw Error('Commercial source evidence belongs to a different prototype revision.');
 result.status=runtimeEvidence.status;
 for(const [group,key] of [['captureControls','controlId'],['questions','questionId'],['branches','id']]){
  for(const [identity,proof] of Object.entries(runtimeEvidence[group]??{})){
   const row=result[group].find(x=>x[key]===identity);if(!row)throw Error(`Unknown commercial source evidence identity: ${identity}`);
   row.runtimeStatus=proof.runtimeStatus;row.verification=proof.verification;
  }
 }
}
await writeFile(new URL('../.planning/phases/08-commercial-combined-back-office/08-SOURCE-INVENTORY.json',import.meta.url),JSON.stringify(result,null,2)+'\n');
console.log(JSON.stringify(result.denominators));
