import {readFile,writeFile} from 'node:fs/promises';
const read=async path=>JSON.parse(await readFile(path,'utf8'));
const dir='.planning/phases/09-tasks-documents-communication-and-incidents';
const [inventory,mapping,seven,eight,render]=await Promise.all([read('docs/design/control-inventory.json'),read('docs/design/api-control-map.json'),read('.planning/phases/07-policy-lifecycle-and-history/07-SOURCE-INVENTORY.json'),read('.planning/phases/08-commercial-combined-back-office/08-SOURCE-INVENTORY.json'),read('docs/design/source/prototype-render-data.json')]);
const mapped=new Map(mapping.controls.map(x=>[x.controlId,x]));
const ops=new Set(['listTasks','getTask','createTask','updateTask','transitionTask','assignTasks','listNotes','addNote','listThreads','createThread','listMessages','createMessageDraft','updateMessageDraft','sendMessage','listDocuments','listDocumentVersions','generateDocument','uploadDocument','downloadDocumentVersion','sendDocumentPack','resendDocumentPack','createIncidentDraft','updateIncidentDraft','getIncident','listIncidents','logIncident','handoffIncident','listClaimSummaries','refreshClaimSummary','listMidSubmissions','retryMidSubmission']);
const inherited=new Set([...seven.controls.filter(x=>x.ownerPhase===9).map(x=>x.id),...eight.policyControls.filter(x=>x.ownerPhase===9).map(x=>x.controlId)]);
function owner(source,operationIds=[]){
 if(source.method==='pReporting')return{ownerPhase:12,ownerPlan:'phase-12',disposition:'report-export-consumer-future-owner'};
 if(source.method==='pAdmin')return{ownerPhase:11,ownerPlan:'phase-11',disposition:'template-administration-future-owner'};
 if(source.method==='pDashboard')return{ownerPhase:12,ownerPlan:'phase-12',disposition:'dashboard-consumer-future-owner-task-destination-phase9'};
 const joined=operationIds.join(' ');
 let plan='09-16';
 if(source.method==='pClaim')plan='09-13';
 else if(source.method==='pLogClaim')plan='09-12';
 else if(['pTasks','pTask'].includes(source.method)||source.method==='modalVals'&&source.tabs.includes('task'))plan='09-03';
 else if(/MidSubmission/.test(joined)||source.method==='pVehicle')plan='09-14';
 else if(/DocumentPack/.test(joined)||/Send Documents/.test(source.label??''))plan='09-10';
 else if(/Document/.test(joined))plan='09-08';
 else if(/Incident/.test(joined)||/Incident|incident/.test(source.label??''))plan='09-12';
 else if(/Message|Thread/.test(joined))plan=source.method==='pMatch'?'09-16':'09-10';
 else if(/Note/.test(joined))plan='09-09';
 else if(/Task/.test(joined)||/task|Task/.test(source.label??''))plan='09-03';
 return{ownerPhase:9,ownerPlan:plan,disposition:'implement-owned-saved-workflow'};
}
let prior;try{prior=await read(dir+'/09-SOURCE-INVENTORY.json');}catch(e){if(e.code!=='ENOENT')throw e;}
const priorControls=new Map(prior?.controls.map(x=>[x.controlId,x])??[]);
const priorClaims=new Map(prior?.commercialClaimsOccurrences.map(x=>[x.sourceOccurrence,x])??[]);
const selected=inventory.controls.filter(x=>Number(x.phase)===9||inherited.has(x.id)||(mapped.get(x.id)?.operationIds??[]).some(id=>ops.has(id)));
const controls=selected.map(source=>{const map=mapped.get(source.id);const previous=priorControls.get(source.id);return{controlId:source.id,source:{method:source.method,path:source.path,label:source.label,tabs:source.tabs,scenarios:source.scenarios,handlers:source.handlers??{}},originalPhase:source.phase,origin:Number(source.phase)===9?'direct-phase9':inherited.has(source.id)?'explicit-phase7-8-handoff':'mapped-operational-consumer',operationIds:map?.operationIds??[],fieldBindings:map?.apiFields??[],contractReason:map?.reason??source.contract,...owner(source,map?.operationIds),...(source.id==='CTL-f483a775b1ff'?{ownerPlan:'09-17',disposition:'invalid-commercial-motor-fallback-already-removed-verify-no-reintroduction'}:{}),runtimeStatus:previous?.runtimeStatus??'not-implemented-in-phase9',verification:previous?.verification??[]};});
const methods=new Set(['pTasks','pTask','pLogClaim','pClaim']),shared=new Set(['pPolicy','pQuote','pClient','pAgency','pCcPolicy']),tabs=new Set(['Notes','Documents','Tasks','Messages','Claims']);
const displayOccurrences=render.items.flatMap((source,sourceOccurrence)=>{
 const direct=methods.has(source.method),modal=source.method==='modalVals'&&source.tabs.includes('task'),sharedTab=shared.has(source.method)&&source.tabs.length===1&&tabs.has(source.tabs[0]);
 if(!direct&&!modal&&!sharedTab)return[];
 const tab=source.tabs[0];const ownerPlan=direct?source.method==='pClaim'?'09-13':source.method==='pLogClaim'?'09-12':'09-03':modal?'09-03':({Notes:'09-09',Documents:'09-08',Tasks:'09-03',Messages:'09-10',Claims:'09-13'})[tab];
 const previous=prior?.displayOccurrences.find(x=>x.sourceOccurrence===sourceOccurrence);
 return[{sourceOccurrence,source,ownerPlan,runtimeStatus:previous?.runtimeStatus??'not-implemented-in-phase9',verification:previous?.verification??[]}];
});
const rawBranches=[
 ['TASK-REOPEN','Reopen task','09-03','Reasoned explicit reopen retains previous completion and never approves linked referral.'],
 ['TASK-BULK-COMPLETE',"'Complete ' + sel.length",'09-03','Validate100-or-fewer unique scoped selections and ETags; atomic completion with reasons/checklist.'],
 ['TASK-BULK-REASSIGN','Reassign selected','09-03','Only currently eligible assignee/team; whole batch rollback on conflict.'],
 ['TASK-BULK-DUE','change due date or complete them together','09-03','Source-implied bulk due edit is a real atomic audited command.'],
 ['INCIDENT-VEHICLE',"'lcVeh'",'09-12','Select owned vehicle at historical occurrence or preserve explicit not-on-register declaration.'],
 ['INCIDENT-DRIVER',"'lcDriver'",'09-12','Owned historical driver or explicit not-a-named-driver; never imply cover.'],
 ['INCIDENT-DRIVABLE',"'lcDrivable'",'09-12','Preserve Yes/No recovered/Unknown without coercing unknown tofalse.'],
 ['INCIDENT-HANDOFF-READY','onClick: () => this.logClaim()','09-13','Validated logged revision queues one exact handoff; status follows saved provider outcome.'],
 ['DOCUMENT-STATEMENT-OF-FACT','Statement of fact','09-06','Render exact original/current source statement of fact and keep historical versions.'],
 ['DOCUMENT-ENDORSEMENT','Endorsement E-14','09-06','Render applicable selected endorsement content pinned to source/template.']
];
const branches=rawBranches.map(([id,sourceNeedle,ownerPlan,expectedBehavior])=>({id,sourceNeedle,ownerPlan,expectedBehavior,runtimeStatus:prior?.branches.find(x=>x.id===id)?.runtimeStatus??'not-implemented-in-phase9'}));
const result={format:'operations-source-1',sourceSha256:inventory.sourceSha256,status:prior?.status??'extracted-pending-detailed-source-review',denominators:{directControls:62,explicitInheritedControls:inherited.size,selectedControlUnion:controls.length,displayOccurrences:displayOccurrences.length,commercialClaimsOccurrences:33,supplementalBranches:branches.length},controls,displayOccurrences,commercialClaimsOccurrences:eight.policyDisplayItems.filter(x=>x.ownerPhase===9).map(x=>({sourceOccurrence:x.sourceOccurrence,source:x.source,ownerPlan:'09-13',runtimeStatus:priorClaims.get(x.sourceOccurrence)?.runtimeStatus??'not-implemented-in-phase9',verification:priorClaims.get(x.sourceOccurrence)?.verification??[]})),branches};
await writeFile(dir+'/09-SOURCE-INVENTORY.json',JSON.stringify(result,null,2)+'\n');
console.log(JSON.stringify(result.denominators));
