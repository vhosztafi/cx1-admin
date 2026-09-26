export function addReportingContracts({schemas:s,ref,operation:op,paths,id,instant,text:t,object:o,array:a}) {
 const nullable = schema => ({anyOf:[schema,{type:'null'}]});
 s.DashboardRow=o({id,kind:t(40),reference:t(200),label:t(300),state:t(40),dueOn:nullable({type:'string',format:'date'}),href:t(300)});
 s.DashboardQueue=o({code:t(60),title:t(200),definition:t(2000),count:nullable({type:'integer'}),overdue:nullable({type:'integer'}),items:a(ref('DashboardRow')),nextOffset:nullable({type:'integer'})});
 s.Dashboard=o({asOf:instant,scope:{enum:['mine','team']},queues:a(ref('DashboardQueue')),workload:a(o({type:t(60),count:{type:'integer'}})),activity:a(o({id,taskId:id,kind:t(60),createdAt:instant}))});
 delete paths['/dashboard'];op('get','/dashboard','getDashboard','current-family-read',{output:ref('Dashboard')});paths['/dashboard'].get.parameters=[{name:'scope',in:'query',schema:{enum:['mine','team']}},{name:'queue',in:'query',schema:t(60)},{name:'offset',in:'query',schema:{type:'integer',minimum:0,maximum:10000}}];
 s.Notification=o({id,kind:{enum:['task','security']},text:t(300),createdAt:instant,href:t(200),read:{type:'boolean'}});
 delete paths['/notifications'];op('get','/notifications','listNotifications','self',{output:a(ref('Notification'))});
 delete paths['/notifications/{notificationId}/read'];op('post','/notifications/{notificationId}/read','markNotificationRead','self',{status:204,idempotent:false});
 s.SearchHit=o({id,kind:{enum:['client','quote','policy','agency','match']},reference:t(100),label:t(200),status:t(40),productCode:nullable(t(60)),href:t(200)});
 s.SearchFilters=o({q:t(200),kind:{enum:['all','client','quote','policy','agency','match']},status:t(40),productCode:t(60),agencyId:id,providerId:id,underwriterId:id,from:{type:'string',format:'date'},to:{type:'string',format:'date'},reference:t(100),offset:{type:'integer',minimum:0,maximum:10000},version:t(100),asOf:instant},[]);
 s.SearchResult=o({filters:ref('SearchFilters'),asOf:instant,version:t(100),total:{type:'integer'},nextOffset:nullable({type:'integer'}),availableKinds:a(s.SearchHit.properties.kind),items:a(ref('SearchHit'))});
 delete paths['/search'];
 op('get','/search','searchRecords','current-family-read',{output:ref('SearchResult')});
 paths['/search'].get.parameters=Object.entries(s.SearchFilters.properties).map(([name,schema])=>({name,in:'query',required:false,schema}));
 for(const name of ['status','productCode','agencyId','providerId','underwriterId','from','to','reference','version','asOf'])s.SearchFilters.properties[name]=nullable(s.SearchFilters.properties[name]);
 op('get','/search/options','getSearchOptions','current-family-read',{output:o({products:a(o({code:t(60),name:t()})),agencies:a(o({id,name:t()})),providers:a(o({id,name:t()})),underwriters:a(o({id,name:t()})),kinds:a(s.SearchHit.properties.kind)})});
}

export function addReportExecutionContracts({schemas:s,ref:r,operation:op,paths,id,instant,text:t,object:o,array:a}){
 const nil=x=>({anyOf:[x,{type:'null'}]}),number={type:'integer'},date={type:'string',format:'date'};
 s.ReportMeasure=o({code:t(60),label:t(200),definition:t(2000),unit:{enum:['count','GBP','percent','hours']},numerator:nil(t(60)),denominator:nil(t(60))});
 s.ReportDefinition=o({id,code:t(60),category:{enum:['underwriting','portfolio','renewal','finance','agency','compliance-exceptions']},title:t(200),description:t(2000),capability:t(100),defaultBasis:{enum:['effective','processed']},bases:a({enum:['effective','processed']}),filters:a({enum:['agencyId','providerId','productCode','underwriterId']}),measures:a(r('ReportMeasure'))});
 s.ReportFilters=o({from:date,to:date,basis:{enum:['effective','processed']},agencyId:nil(id),providerId:nil(id),productCode:nil(t(60)),underwriterId:nil(id),offset:{type:'integer',minimum:0,maximum:10000}},['from','to','basis']);
 s.ReportValue=o({code:t(60),value:nil(t(100)),denominator:nil(t(100))});
 s.ReportSourceRow=o({recordId:id,kind:t(40),reference:t(200),label:t(300),state:t(40),href:t(300),sourceId:nil(id),sourceRuleId:nil(id),agencyId:nil(id),productCode:nil(t(60)),at:instant,basisDate:nil(date),values:{type:'object',additionalProperties:nil(t(100))}});
 s.ReportResult=o({reportId:id,generatedAt:instant,filters:r('ReportFilters'),definition:t(2000),totalRows:number,measures:a(r('ReportValue')),rows:a(r('ReportSourceRow')),nextOffset:nil(number),groups:a(o({agencyId:nil(id),month:t(30),productCode:nil(t(60)),measures:a(r('ReportValue'))}))});
 for(const path of ['/reports','/reports/{reportId}','/reports/{reportId}/run'])delete paths[path];
 op('get','/reports','listReports','current-report-capability',{output:a(r('ReportDefinition'))});
 op('get','/reports/{reportId}','getReportDefinition','current-report-capability',{output:r('ReportDefinition')});
 op('post','/reports/{reportId}/run','runReport','current-report-capability',{input:r('ReportFilters'),output:r('ReportResult'),idempotent:false});
 op('get','/reports/{reportId}/options','getReportOptions','current-report-capability',{output:o({products:a(o({code:t(60),name:t()})),agencies:a(o({id,name:t()})),providers:a(o({id,name:t()})),underwriters:a(o({id,name:t()}))})});
}

export function addSavedReportingContracts({schemas:s,ref:r,operation:op,paths,id,instant,text:t,object:o,array:a}){
 for(const path of ['/account/saved-reports','/account/saved-reports/{savedReportId}','/reports/{reportId}/export'])delete paths[path];
 s.SavedReport=o({id,reportId:id,name:t(100),filters:r('ReportFilters')});
 s.RecentReport=o({reportId:id,filters:r('ReportFilters'),ranAt:instant,totalRows:{type:'integer'}});
 s.ReportPreferences=o({version:{type:'integer'},favourites:a(r('SavedReport')),recent:a(r('RecentReport'))});
 s.SaveReportInput=o({version:{type:'integer',minimum:0},reportId:id,name:{type:'string',minLength:1,maxLength:100},filters:r('ReportFilters')});
 op('get','/reports/preferences','listSavedReports','self-current-report-capability',{output:r('ReportPreferences')});
 op('post','/reports/favourites','saveReport','self-current-report-capability',{input:r('SaveReportInput'),output:r('SavedReport'),idempotent:false});
 op('put','/reports/favourites/{savedReportId}','updateSavedReport','self-current-report-capability',{input:r('SaveReportInput'),output:r('SavedReport'),idempotent:false});
 op('delete','/reports/favourites/{savedReportId}','deleteSavedReport','self-current-report-capability',{status:204,idempotent:false});
 paths['/reports/favourites/{savedReportId}'].delete.parameters.push({name:'version',in:'query',required:true,schema:{type:'integer',minimum:0}});
 op('post','/reports/{reportId}/export','exportReport','current-report-capability',{input:o({filters:r('ReportFilters'),format:{enum:['csv']}}),idempotent:false});
 paths['/reports/{reportId}/export'].post.responses['200']={description:'UTF-8 CSV containing all matching source rows, maximum 10,000. Current permission and filter checks; oversize is 422. Formula-prefixed text is neutralised.',content:{'text/csv':{schema:{type:'string'}}}};
}
export async function writeReportingTypes(schemas){
 const {writeFile}=await import('node:fs/promises');
 function type(s){if(s.$ref)return s.$ref.split('/').at(-1);if(s.anyOf)return s.anyOf.map(type).join(' | ');if(s.enum)return s.enum.map(x=>JSON.stringify(x)).join(' | ');if(s.type==='null')return 'null';if(s.type==='array')return '('+type(s.items)+')[]';if(s.type==='integer'||s.type==='number')return 'number';if(s.type==='boolean')return 'boolean';if(s.type==='object'){if(s.additionalProperties)return 'Record<string, '+type(s.additionalProperties)+'>';return '{ '+Object.entries(s.properties??{}).map(([k,v])=>k+(s.required?.includes(k)?'':'?')+': '+type(v)).join('; ')+' }';}return 'string';}
 const names=['ReportMeasure','ReportDefinition','ReportFilters','ReportValue','ReportSourceRow','ReportResult','SavedReport','RecentReport','ReportPreferences','SaveReportInput'];
 await writeFile('contracts/generated/reporting.ts','// Generated from the reporting OpenAPI schemas.\n'+names.map(n=>'export type '+n+' = '+type(schemas[n])+';').join('\n')+'\n');
}
