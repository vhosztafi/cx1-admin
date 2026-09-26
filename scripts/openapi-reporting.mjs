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
