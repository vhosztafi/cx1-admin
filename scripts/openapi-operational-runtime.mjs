import {operationalDefinitions,relocateOperational} from './operations-contracts.mjs';
// Applied after legacy form modifiers. These routes remain contract-only until their owning Phase9 plan runs.
export function addOperationalRuntimeContracts({schemas:s,ref:r,operation:op,list,paths,object:o,id,text:t}){
 Object.assign(s,relocateOperational(operationalDefinitions()));
 const input=(path,method,name)=>{paths[path][method].requestBody={required:true,content:{'application/json':{schema:r(name)}}};paths[path][method]['x-runtime-status']='phase-9-contract-only';};
 const output=(path,method,name)=>{const x=paths[path][method];for(const [code,response]of Object.entries(x.responses))if(Number(code)>=200&&Number(code)<300&&response.content?.['application/json'])response.content['application/json'].schema=r(name);x['x-runtime-status']='phase-9-contract-only';};
 for(const [path,method,name]of[['/tasks','post','OpsTaskWrite'],['/tasks/{taskId}','put','OpsTaskUpdateWrite'],['/tasks/{taskId}/transition','post','OpsTaskTransition'],['/tasks/bulk-assignment','post','OpsTaskBulkAssignment'],['/records/{recordId}/threads','post','OpsThreadWrite'],['/threads/{threadId}/messages','post','OpsMessageWrite'],['/messages/{messageId}','put','OpsMessageWrite'],['/records/{recordId}/document-deliveries','post','OpsPackWrite'],['/records/{recordId}/documents/generate','post','OpsDocumentGenerate'],['/incidents','post','OpsIncidentDraftWrite'],['/incidents/{incidentId}','put','OpsIncidentDraftWrite'],['/incidents/{incidentId}/handoff','post','OpsIncidentHandoff']])input(path,method,name);
 for(const [path,method,name]of[['/tasks','post','OpsTask'],['/tasks/{taskId}','get','OpsTask'],['/tasks/{taskId}','put','OpsTask'],['/tasks/{taskId}/transition','post','OpsTask'],['/incidents','post','OpsIncident'],['/incidents/{incidentId}','get','OpsIncident'],['/incidents/{incidentId}','put','OpsIncident'],['/incidents/{incidentId}/log','post','OpsIncident'],['/records/{recordId}/documents/upload','post','OpsDocumentVersion']])output(path,method,name);
 for(const [path,name]of[['/tasks','OpsTask'],['/incidents','OpsIncident'],['/incidents/{incidentId}/summaries','OpsClaimsSummary'],['/documents/{documentId}/versions','OpsDocumentVersion'],['/versions/{versionId}/mid-submissions','OpsMidSubmission']]){paths[path].get.responses[200].content['application/json'].schema.properties.items.items=r(name);paths[path].get['x-runtime-status']='phase-9-contract-only';}
 const taskQuery=paths['/tasks'].get.parameters;taskQuery.find(x=>x.name==='state').schema=s.OpsTask.properties.state;taskQuery.push({name:'view',in:'query',schema:{type:'string',enum:['my-open','team','created-by-me','completed']}},{name:'typeCode',in:'query',schema:s.OpsTask.properties.typeCode},{name:'q',in:'query',schema:t(300)});
 input('/incidents/validate','post','OpsIncidentDraftWrite');
 for(const [path,method,name]of[['/records/{recordId}/threads','post','OpsThread'],['/threads/{threadId}/messages','post','OpsMessage'],['/messages/{messageId}','put','OpsMessage']])output(path,method,name);
 for(const [path,name]of[['/records/{recordId}/threads','OpsThread'],['/threads/{threadId}/messages','OpsMessage']])paths[path].get.responses[200].content['application/json'].schema.properties.items.items=r(name);
 const receipt=o({updatedIds:{type:'array',items:id,minItems:1,maxItems:100,uniqueItems:true}});
 op('post','/records/{recordId}/file-uploads','stageFileUpload','document-upload',{query:[['name',t(255)]],output:r('OpsFileUpload'),status:202});
 const fileUpload=paths['/records/{recordId}/file-uploads'].post;
 fileUpload.parameters.find(x=>x.name==='name').required=true;
 fileUpload.requestBody={required:true,content:Object.fromEntries(['application/pdf','image/png','image/jpeg'].map(type=>[type,{schema:{type:'string',format:'binary',maxLength:20971520}}]))};
 op('get','/file-uploads/{uploadId}','getFileUpload','document-read',{output:r('OpsFileUpload')});
 op('get','/file-uploads/{uploadId}/content','downloadFileUpload','document-download',{output:{type:'string',format:'binary'}});
 const fileDownload=paths['/file-uploads/{uploadId}/content'].get.responses[200];
 fileDownload.content=Object.fromEntries(['application/pdf','image/png','image/jpeg'].map(type=>[type,{schema:{type:'string',format:'binary'}}]));
 fileDownload.headers['Content-Disposition']={description:'Safe attachment filename; verified original bytes.',schema:t(500)};
 for(const operation of [fileUpload,paths['/file-uploads/{uploadId}'].get,paths['/file-uploads/{uploadId}/content'].get]){
  operation['x-runtime-status']='phase-9-05-api-verified';
  for(const [code,response]of Object.entries(operation.responses))if(Number(code)>=200&&Number(code)<300){response.headers??={};response.headers['Cache-Control']={description:'Private, no-store.',schema:{type:'string'}};response.headers['X-Content-Type-Options']={description:'nosniff',schema:{type:'string',enum:['nosniff']}};}
 }
 op('get','/tasks/summary','getTaskSummary','task-read',{output:o({open:{type:'integer',minimum:0},dueToday:{type:'integer',minimum:0},overdue:{type:'integer',minimum:0},awaitingOthers:{type:'integer',minimum:0},completedSevenDays:{type:'integer',minimum:0},asOf:{type:'string',format:'date-time'}})});
 paths['/tasks/summary'].get['x-runtime-status']='phase-9-03-api-verified';
 op('get','/task-assignees','listTaskAssignees','task-assign',{query:[['subjectRecordId',{type:'array',items:id,minItems:1,maxItems:100,uniqueItems:true}],['kind',{type:'string',enum:['user','team']}],['q',t(100)]],output:o({items:{type:'array',maxItems:50,items:o({id,label:t(300)})},hasMore:{type:'boolean'}})});
 for(const parameter of paths['/task-assignees'].get.parameters){if(['subjectRecordId','kind'].includes(parameter.name))parameter.required=true;if(parameter.name==='subjectRecordId'){parameter.style='form';parameter.explode=true;}}
 paths['/task-assignees'].get['x-runtime-status']='phase-9-03-api-verified';
 op('post','/operational-subjects','registerOperationalSubject','subject-read',{input:r('OpsSubjectWrite'),output:r('OpsSubject'),status:201});
 op('post','/tasks/bulk-due-date','changeTaskDueDates','task-assign',{input:r('OpsTaskBulkDue'),output:receipt});
 op('post','/tasks/bulk-completion','completeTasks','task-write',{input:r('OpsTaskBulkComplete'),output:receipt});
 op('put','/tasks/{taskId}/checklist','updateTaskChecklist','task-write',{existing:true,input:r('OpsTaskChecklistWrite'),output:r('OpsTask')});
 op('post','/tasks/{taskId}/comments','addTaskComment','task-write',{existing:true,input:r('OpsTextWrite'),output:r('OpsComment'),status:201});
 list('/tasks/{taskId}/comments','listTaskComments','task-read',r('OpsComment'));
 list('/tasks/{taskId}/events','listTaskEvents','task-read',r('OpsTaskEvent'));
 op('post','/incidents/{incidentId}/occurrence','clarifyIncidentOccurrence','incident-write',{existing:true,input:r('OpsOccurrenceClarification'),output:r('OpsIncident')});
 op('post','/incidents/{incidentId}/occurrence-resolution','resolveIncidentOccurrence','incident-write',{existing:true,input:o({}),output:r('OpsOccurrenceResolution')});
 op('post','/incidents/{incidentId}/log-and-handoff','logAndHandoffIncident','incident-handoff',{existing:true,input:r('OpsIncidentHandoff'),output:r('Job'),status:202});
 op('post','/incidents/{incidentId}/contact','contactClaimsAdministrator','incident-handoff',{existing:true,input:r('OpsTextWrite'),output:r('Job'),status:202});
 list('/messages/{messageId}/deliveries','listMessageDeliveries','message-read',r('OpsDelivery'));
 list('/records/{recordId}/document-deliveries','listDocumentDeliveries','document-read',r('OpsDelivery'));
 op('get','/document-deliveries/{deliveryId}','getDocumentDelivery','document-read',{output:r('OpsDelivery')});
 list('/document-deliveries/{deliveryId}/attempts','listDocumentDeliveryAttempts','document-read',r('OpsDeliveryAttempt'));
 op('post','/document-deliveries/{deliveryId}/retry','retryDocumentDelivery','document-send',{existing:true,input:o({reason:t(1000)}),output:r('Job'),status:202});
 op('post','/document-deliveries/{deliveryId}/resend','resendDocumentPack','document-send',{existing:true,input:o({reason:t(1000)}),output:r('Job'),status:202});
 op('post','/message-deliveries/{deliveryId}/retry','retryMessageDelivery','message-send',{existing:true,input:o({reason:t(1000)}),output:r('Job'),status:202});
 op('get','/document-versions/{versionId}/preview','previewDocumentVersion','document-download',{output:{type:'string',format:'binary'}});
 const preview=paths['/document-versions/{versionId}/preview'].get.responses[200];preview.content={'application/pdf':{schema:{type:'string',format:'binary'}},'image/png':{schema:{type:'string',format:'binary'}},'image/jpeg':{schema:{type:'string',format:'binary'}}};
 preview.headers['Content-Disposition']={description:'Safe inline filename; exact authorized immutable version only.',schema:t(500)};
 preview.headers['Cache-Control']={description:'Private, no-store.',schema:t(100)};
 const newIds=new Set(['registerOperationalSubject','changeTaskDueDates','completeTasks','updateTaskChecklist','addTaskComment','listTaskComments','listTaskEvents','clarifyIncidentOccurrence','resolveIncidentOccurrence','logAndHandoffIncident','contactClaimsAdministrator','listMessageDeliveries','listDocumentDeliveries','getDocumentDelivery','listDocumentDeliveryAttempts','retryDocumentDelivery','resendDocumentPack','retryMessageDelivery','previewDocumentVersion']);
 for(const methods of Object.values(paths))for(const operation of Object.values(methods))if(newIds.has(operation.operationId))operation['x-runtime-status']='phase-9-contract-only';
 const taskRuntime=new Set(['registerOperationalSubject','createTask','getTask','updateTask','listTasks','transitionTask','assignTasks','changeTaskDueDates','completeTasks','updateTaskChecklist','addTaskComment','listTaskComments','listTaskEvents']);
 for(const methods of Object.values(paths))for(const operation of Object.values(methods))if(taskRuntime.has(operation.operationId))operation['x-runtime-status']='phase-9-02-api-verified';
}
