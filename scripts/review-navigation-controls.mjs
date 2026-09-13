// Reviewed literal navigation forms only. Dynamic callbacks, wizard transitions
// and compound handlers remain for separate semantic review.
import {readFile,writeFile} from 'node:fs/promises';
const inventory=JSON.parse(await readFile('docs/design/control-inventory.json','utf8'));
const path='docs/design/reviewed-api-controls.json';
const rows=JSON.parse(await readFile(path,'utf8'));
const routes={
 search:['searchRecords'],tasks:['listTasks'],mta:['listPolicyDrafts','getPolicyDraft'],renewal:['listPolicyTerms','listPolicyDrafts'],
 logclaim:['getPolicyVersion'],asat:['getPolicyAsAt'],driver:['getPolicyVersion'],policy:['getPolicy','getPolicyAsAt'],
 escalation:['getReferral','getEscalation'],agents:['listAgencies'],agency:['getAgency'],newagency:[],portal:[],
 accounting:['listJournals','listReceipts'],dashboard:['getDashboard'],task:['getTask'],account:['getAccount'],
 admin:['listProducts'],client:['getClient']
};
const commonTabs={Notes:['listNotes'],Documents:['listDocuments'],Tasks:['listTasks'],Transactions:['listPolicyVersions'],Claims:['listIncidents'],Messages:['listThreads']};
const tabs={
 pPolicy:commonTabs,
 pQuote:{Underwriting:['listReferrals'],'Risk Details':['getQuote'],History:['listQuoteRevisions']},
 pTask:{Task:['getTask','listNotes']},
 pAccounting:{'Batch BX-2026-08':['getBordereau','listBordereauRows'],Bordereaux:['listBordereaux']},
 pReporting:{'Underwriting performance':['listReports','getReportDefinition'],'Report library':['listReports']},
 pAdmin:{'Users & roles':['listUsers'],'Client matching':['listMatchingRules'],'Products & schemes':['listProducts'],'Delegated authority':['listAuthorityVersions'],Workflow:['listWorkflowRules'],Templates:['listTemplateVersions'],Integrations:['listIntegrationSettings'],'Audit log':['listAuditEvents'],Settings:['getGeneralSettings']},
 pCcPolicy:{...commonTabs,'Property schedule':['getPolicyAsAt'],'Liability & employees':['getPolicyAsAt']}
};
let count=0;
for(const c of inventory.controls){
 if(rows.some(r=>r.controlId===c.id)||Object.values(c.handlers).length!==1)continue;
 const handler=Object.values(c.handlers)[0];
 const go=/^\(\) => this\.go\('([^']+)'(?:, '([^']+)')?\)$/.exec(handler);
 const tab=/^\(\) => this\.setTab\('([^']+)'\)$/.exec(handler);
 let operationIds,reason;
 if(go&&Object.hasOwn(routes,go[1])){
  operationIds=routes[go[1]];
  if(go[1]==='policy'&&go[2]&&commonTabs[go[2]])operationIds=commonTabs[go[2]];
  if(go[1]==='account'&&go[2]==='Sessions & activity')operationIds=['listSessions'];
  if(go[1]==='accounting'&&go[2]==='Broker accounts')operationIds=['getAgencyStatement'];
  if(go[1]==='accounting'&&go[2]==='Bordereaux')operationIds=['listBordereaux'];
  reason=`Literal navigation to ${go[1]}${go[2]?` / ${go[2]}`:''}; display only. Record IDs come from selected scoped row, not fixed prototype IDs. Opening capture does not save, issue or send; its confirmed commands are separately reviewed.`;
  if(go[1]==='portal')reason='Navigate to static internal sharing reference; prototype explicitly excludes a separate broker portal. No domain mutation.';
 }else if(tab&&tabs[c.method]?.[tab[1]]){
  operationIds=tabs[c.method][tab[1]];
  reason=`Literal ${c.method} tab selection ${tab[1]}; read current authorised record collection only. Render/add/edit commands within the tab are independently mapped.`;
 }
 if(operationIds){rows.push({controlId:c.id,status:'reviewed',disposition:operationIds.length?'navigation-read':'client-only',operationIds,reason});count++;}
}
await writeFile(path,JSON.stringify(rows,null,2)+'\n');
console.log(`Added ${count} reviewed literal navigation controls; total ${rows.length}.`);
