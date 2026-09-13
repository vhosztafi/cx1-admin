import { readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';

const read = async p => JSON.parse(await readFile(new URL(`../${p}`,import.meta.url),'utf8'));
const evidence = await read('docs/design/source/prototype-evidence.json');
const render = await read('docs/design/source/prototype-render-data.json');
const template = await readFile(new URL('../docs/design/source/prototype-template.txt',import.meta.url),'utf8');
const profiles = {
  shell:['FND-03',2,'authenticated','Navigation, layout, filtering and notifications; no domain mutation from navigation.'],
  clients:['CLI-02',3,'servicing','Persist account and agency-scoped contact changes; flags require restricted access and never enter rating.'],
  match:['CLI-04',3,'underwriting','Record duplicate decision and reason; preserve other agency confidentiality and original policy answers.'],
  agencies:['AGY-01',4,'agency-admin','Persist onboarding and evidence; activate only when prerequisites pass; audit suspension/access changes.'],
  quote:['QUO-02',5,'servicing','Persist draft revision with validation; save may be incomplete; rate/issue require complete risk and current dependencies.'],
  underwriting:['UWR-03',6,'underwriting','Persist version-bound decisions; actor authority, outstanding evidence and capacity conditions gate issue.'],
  policy:['POL-01',7,'servicing','Read immutable applicable policy version; edits create drafts rather than overwrite issued cover.'],
  adjustment:['POL-04',7,'servicing/underwriting','Validate effective date, base version and lease; edits invalidate rating/approval/acceptance; issue atomically.'],
  renewal:['POL-07',7,'servicing/underwriting','Prepare next term, rate, refer, invite, accept and issue; lapse requires reason and retained history.'],
  cancellation:['POL-09',7,'servicing/underwriting','Review date/notice/reason and return premium; issue closes relevant drafts/tasks and creates refund/document/MID work.'],
  cc:['CC-04',8,'servicing/underwriting','Use CC risk sections and configurable authority; shared issue/history invariants apply.'],
  tasks:['OPS-01',9,'servicing','Persist linked task status, ownership, due date, notes and checklist; completion reasons and workflow identity audited.'],
  incident:['OPS-07',9,'servicing','Persist draft or submitted incident, linked risk objects, provider handoff attempts and returned summaries.'],
  finance:['FIN-01',10,'finance','Posted movements are immutable; allocation, journal, refund, reconciliation and batch transitions require explicit commands.'],
  reporting:['RPT-03',12,'reporting','Query scoped real records; distinguish each named report; export with same field/agency restrictions.'],
  admin:['ADM-03',11,'system-admin','Version configuration; validate approvals/effective dates and audit changes; historic policy/config snapshots remain unchanged.'],
  account:['ADM-05',11,'self','Profile/password/MFA/session controls have real local-auth effects; identity changes follow approval and revoke affected sessions.'],
  history:['POL-05',7,'servicing','Query effective and processing dates independently, excluding drafts; export actual selected version and documents.'],
};
const methodProfile={pDashboard:'reporting',pSearch:'reporting',pRisks:'quote',pClients:'clients',pClient:'clients',pMatch:'match',flagSecs:'clients',flagTable:'clients',pAgents:'agencies',pAgency:'agencies',pNewAgency:'agencies',pPortal:'agencies',pNewQuote:'quote',pQuote:'underwriting',quoteMenu:'underwriting',AUTHORITY:'underwriting',DIMENSION_INFO:'underwriting',pEscalation:'underwriting',escSend:'underwriting',pPolicy:'policy',policyTab:'policy',policyMenu:'policy',pMta:'adjustment',rateMta:'adjustment',submitMta:'adjustment',dateRules:'adjustment',pRenewal:'renewal',pCancelReview:'cancellation',pIssued:'policy',pVehicle:'policy',pDriver:'policy',pAsAt:'history',VERSIONS:'history',pCcPolicy:'cc',PRODUCT_TABS:'cc',pTasks:'tasks',pTask:'tasks',pLogClaim:'incident',pClaim:'incident',logClaim:'incident',claimDefaults:'incident',pAccounting:'finance',pReporting:'reporting',pAdmin:'admin',pAccount:'account',faWizard:'account',faOff:'account',faOn:'account',pLogout:'account'};
const modalProfile={cancel:'cancellation',task:'tasks',picker:'adjustment',newquote:'quote',adddriver:'quote',addconv:'quote',addinc:'quote',addveh:'quote',addloc:'cc',addwage:'cc',addloss:'cc',addprem:'quote',addcontact:'clients',escoutcome:'underwriting',aguser:'agencies',flag:'clients',chgemail:'admin',codes:'account',fadisable:'account',edit:'adjustment',invite:'agencies'};
let modalKind='';
const sourceRecords=evidence.evidence.map(e=>{
 const modalMatch=e.text.match(/m\.kind\s*===\s*'([^']+)'/);if(modalMatch)modalKind=modalMatch[1];
 const profile=e.method==='modalVals'?(modalProfile[modalKind]||'shell'):(methodProfile[e.method]||'shell');
 return {id:`SRC-${String(e.line).padStart(5,'0')}`,line:e.line,method:e.method,profile,source:e.text,disposition:/onClick|onConfirm|this\.act\(|disabled|confirmLabel/.test(e.text)?'interaction-evidence':/tabs:|title:|subnav:/.test(e.text)?'presentation-or-route-evidence':'supporting-evidence'};
});
const controls=[];
for(const item of render.items){
 const handlers=Object.fromEntries(Object.entries(item).filter(([k,v])=>/^on[A-Z]/.test(k)&&typeof v==='string'));
 if(!Object.keys(handlers).length&&!item.confirmLabel&&!item.actionLabel)continue;
 const label=item.label||item.confirmLabel||item.actionLabel||item.sendLabel||item.title||item.text||item.path;
 let profile=item.method==='modalVals'?(modalProfile[item.tabs[0]]||'shell'):(methodProfile[item.method]||'shell');
 if(item.method==='pNewQuote'&&item.tabs.some(t=>t.startsWith('Commercial Combined:')))profile='cc';
 const [requirement,phase,permission,contract]=profiles[profile];
 const serialized=JSON.stringify(item);
 const id='CTL-'+createHash('sha256').update(serialized).digest('hex').slice(0,12);
 const fn=Object.values(handlers).join('\n');
 const navigation=/this\.go\(|this\.setTab\(|next\(/.test(fn)&&!/(toast\(|addTo|removeFrom|rateMta|submitMta|logClaim|escSend)/.test(fn);
 const input=Object.keys(handlers).some(k=>['onChange','onInput'].includes(k));
 controls.push({id,method:item.method,path:item.path,label,tabs:item.tabs,scenarios:item.scenarios,kind:input?'input':navigation?'navigation':'action',requirement,phase,profile,permission,contract,validation:item.error||item.hint||'Use the enclosing field/workflow rules in source; API enforces the domain contract.',persistence:input?'Persist via the enclosing save/command, not on display.':navigation?'No domain write.':contract,failure:'Show validation/domain conflict or authorised retry; preserve saved data. Never report successful storage solely from a toast.',acceptance:`AT-${id.slice(4)}`,handlers});
}
const inputs=[...template.matchAll(/this\.input\(\s*'([^']*)'\s*,\s*'([^']+)'/g)].map(m=>({label:m[1],key:m[2],line:template.slice(0,m.index).split('\n').length}));
const routes=[...template.matchAll(/(?:^|[,\s])([a-z]+):'(p[A-Z]\w+)'/gm)].map(m=>({route:m[1],method:m[2]}));
await writeFile(new URL('../docs/design/control-inventory.json',import.meta.url),JSON.stringify({version:1,sourceSha256:evidence.sha256,review:'Method/feature contracts reviewed; exact handlers and source evidence retained. Render probes cover default/ready plus all wizard steps, not every possible input combination. Fine-grained state cases are verified during each owning implementation phase.',profiles,routes,modalKinds:render.modals,sourceRecords,inputs,controls},null,2)+'\n');

const files=['src/contracts/questions.ts','src/domain/business.ts','src/domain/cover.ts','src/domain/driver-workflow.ts','src/domain/premises.ts','src/domain/occupations.ts','src/domain/trade-plates.ts','src/domain/additional-information.ts','src/contracts/driver/questions.json','src/contracts/vehicle/questions.json','src/contracts/vehicle/specified-questions.json','src/contracts/extras/questions.json','src/contracts/declaration/pairs.json','src/contracts/vehicle-types/categories.json'];
const mappings=[];
function area(path,file){if(file.includes('/driver/'))return 'risk.motorTrade.drivers[]';if(file.includes('/vehicle/'))return 'risk.motorTrade.vehicles[]';if(file.includes('/extras/'))return 'cover.extras';if(file.includes('/declaration/'))return 'risk.declarations';if(path.startsWith('proposerPolicy')||path==='isShortTerm')return 'term';if(path.startsWith('proposer')||path.includes('onsent')||path==='marketingMethod')return 'insured';if(path.startsWith('business'))return 'risk.business';if(path.startsWith('cover')||path.includes('Cover'))return 'cover.motorTrade';if(path.startsWith('aad')||path.startsWith('driverPlan')||path.startsWith('young'))return 'risk.motorTrade.driverBasis';if(path.startsWith('tradePlate'))return 'risk.motorTrade.tradePlates';return 'risk.motorTrade';}
for(const file of files){
 const content=await readFile(new URL(`../frontend-code/${file}`,import.meta.url),'utf8');
 let rows=[];
 if(file.endsWith('.json')){const data=JSON.parse(content);const visit=x=>{if(!x||typeof x!=='object')return;if(x.path&&x.owner)rows.push(x);for(const v of Object.values(x))if(v&&typeof v==='object')if(Array.isArray(v))v.forEach(visit);else visit(v);};visit(data);}
 else rows=[...content.matchAll(/\{\s*owner:\s*'([^']+)'\s*,\s*path:\s*'([^']+)'([\s\S]*?)\}/g)].map(m=>({owner:m[1],path:m[2],label:m[3].match(/label:\s*'([^']*)'/)?.[1]||m[2],options:m[3].match(/options:\s*'([^']*)'/)?.[1]}));
 for(const row of rows){if(!row.path)continue;const section=area(row.path,file);mappings.push({owner:row.owner||'declaration-pair',source:`frontend-code/${file}`,rawPath:row.path,label:row.label||row.path,optionCollection:row.options||null,proposedSection:section,representation:row.options||/ID$|Id$/.test(row.path)?'Typed reference: preserve legacy ID value and type, collection, label and reference-data version.':/Date|Birth/.test(row.path)?'ISO date; contractual time handled separately, never silently shifted.':/Turnover|Wage|Value|value|paid|Limit/.test(row.path)?'Explicit decimal/units, derived from actual field semantics.':'Preserve raw value separately from any calculated/display projection.',status:'reference-mapped; new API schema owns validation, no funnel integration in v1'});}
}
await writeFile(new URL('../docs/design/funnel-field-mapping.json',import.meta.url),JSON.stringify({version:1,note:'Reference mapping, not an assertion that canonical unknowns are resolved or a promise to reproduce the customer funnel UI.',mappings},null,2)+'\n');
console.log(JSON.stringify({sourceEvidence:sourceRecords.length,controls:controls.length,inputs:inputs.length,routes:routes.length,funnelFields:mappings.length}));
