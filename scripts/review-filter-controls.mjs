import {readFile,writeFile} from 'node:fs/promises';
const inventory=JSON.parse(await readFile('docs/design/control-inventory.json','utf8'));
const file='docs/design/reviewed-api-controls.json';const rows=JSON.parse(await readFile(file,'utf8'));
const queries={
 f219d18ccb4a:[['listPolicies'],'state'],'7e3297dc0fdf':[['listQuotes'],'status'],
 '536ecaf3079e':[['listPolicies','listQuotes'],'productCode'],e9415f15b16e:[['listPolicies','listQuotes'],'agencyId'],
 '547b106bce55':[['listClients'],'entityType'],'6ffa74bf0063':[['listAgencies'],'state'],db272aea89f2:[['listAgencies'],'relationshipManagerId'],
 d6cec7d66670:[['listTasks'],'priority'],ed74b3103b7c:[['listTasks'],'kind'],'429bddccb3a4':[['listTasks'],'dueWindow'],
 '19d3f582abf2':[['searchRecords'],'kind'],'9bebc3693170':[['searchRecords'],'status'],ba0bd4946547:[['searchRecords'],'productCode'],
 '8c99b56d1a25':[['searchRecords'],'agencyId'],'86419581e107':[['searchRecords'],'providerId'],'4c406f9de95c':[['searchRecords'],'underwriterId'],
 '56825612fb50':[['searchRecords'],'inceptionFrom'],b057804ffcf4:[['searchRecords'],'reference']
};
const finance=['listJournals','listAgencyStatements','listReceipts','listBankLines','listBordereaux','listRefunds'];
Object.assign(queries,{'740ea920a861':[finance,'from'],d29d6b03b3a0:[finance,'agencyId'],'51b829721081':[finance,'filterStatus']});
const fields={'26a1d0f6ca7e':['runReport','from'],'0722bfd85b0e':['runReport','productCode'],'66ec47549c4e':['runReport','agencyId'],e9bb1df559d8:['runReport','underwriterId'],'55e62661c427':['updateMatchingRule','duplicateQuotePolicy'],'99ab620eb2d4':['updateMatchingRule','brokerOfRecordDays'],'89449214fd17':['createAuthorityVersion','effectiveFrom'],'261936502b06':['createAuthorityVersion','binderVersionId']};
for(const c of inventory.controls){const key=c.id.slice(4);let row;
 if(queries[key]){const [operationIds,parameter]=queries[key];row={disposition:'query-filter',operationIds,queryFields:operationIds.map(operationId=>({operationId,parameter})),reason:'Filter the complete authorised server-side collection, reset pagination and retain filters in URL. All/Any omits the parameter. Names resolve to stable IDs; dates use Europe/London. Period selects both from/to. Finance status is a documented tab-specific projection, not a write or cross-tab state enum.'};}
 if(fields[key]){const [operationId,field]=fields[key];row={disposition:'edit-then-command',operationIds:[operationId],apiFields:[{operationId,field}],reason:'Explicit source field maps to typed command field. Report periods resolve both from/to (Custom requires dates); reports run with saved filters. Configuration edits remain drafts until explicit save/publish/independent approval; display labels map to enum values or stable binder IDs.'};}
 if(key==='5f57ef7459cf')row={disposition:'query-filter',operationIds:['listAuthorityVersions','listProductVersions'],queryFields:[{operationId:'listAuthorityVersions',parameter:'productVersionId'}],reason:'Authority-for selects a published product version and loads its authority matrix. Product selection alone does not grant authority or change a binder.'};
 if(row){const result={controlId:c.id,status:'reviewed',...row};const index=rows.findIndex(r=>r.controlId===c.id);if(index<0)rows.push(result);else rows[index]=result;}
}
await writeFile(file,JSON.stringify(rows,null,2)+'\n');console.log(`${rows.length} controls reviewed`);
