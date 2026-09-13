import {readFile,writeFile} from 'node:fs/promises';
import vm from 'node:vm';
const template=await readFile('docs/design/source/prototype-template.txt','utf8');
const logic=template.match(/<script type="text\/x-dc"[^>]*>([\s\S]*?)<\/script>/)[1];
const sandbox=vm.createContext({});
vm.runInContext(`class DCLogic { props={}; setState(p){Object.assign(this.state,typeof p==='function'?p(this.state):p);} }\n${logic}\nglobalThis.create=()=>new Component();`,sandbox,{timeout:5000});
const inventory=JSON.parse(await readFile('docs/design/control-inventory.json','utf8'));
const file='docs/design/reviewed-api-controls.json';const rows=JSON.parse(await readFile(file,'utf8'));
const captures=[];
// Execute only this inspected pure-navigation closure, with go replaced by a
// recorder. No browser, filesystem, network or timers exist inside the VM.
for(const c of inventory.controls.filter(c=>c.handlers.onClick==='() => this.go(o.go, o.tab)')){
 const routes=[];
 for(const tab of c.tabs.filter(t=>!t.includes(':step-'))){
  sandbox.method=c.method;sandbox.tab=tab;sandbox.parts=c.path.match(/[A-Za-z_][A-Za-z_0-9]*|\d+/g);
  const result=vm.runInContext(`(()=>{const x=create();x.state.tab=tab;let v=x[method]();for(const part of parts)v=v?.[part];if(v?.onClick?.toString()!=='() => this.go(o.go, o.tab)')return null;let result;x.go=(route,tab)=>{result={route,tab:tab??null}};v.onClick();return result;})()`,sandbox,{timeout:1000});
  if(result&&!routes.some(r=>r.route===result.route&&r.tab===result.tab))routes.push(result);
 }
 captures.push({controlId:c.id,routes});
}
await writeFile('docs/design/source/dynamic-navigation.json',JSON.stringify({sourceSha256:inventory.sourceSha256,captures},null,2)+'\n');
console.log(JSON.stringify([...new Map(captures.flatMap(c=>c.routes).map(r=>[JSON.stringify(r),r])).values()]));
const destinations={task:['getTask'],tasks:['listTasks'],mta:['getPolicyDraft'],quote:['getQuote'],policy:['getPolicy','getPolicyAsAt'],agency:['getAgency'],vehicle:['getPolicyVersion'],escalation:['getReferral','getEscalation'],ccpolicy:['getPolicy','getPolicyAsAt'],match:['getMatchReview'],client:['getClient'],risks:['listQuotes']};
const policyTabs={Transactions:['listPolicyVersions'],Vehicles:['getPolicyAsAt'],Messages:['listThreads'],Claims:['listIncidents']};
const accountingTabs={'Broker accounts':['listAgencyStatements'],Payments:['listReceipts'],Reconciliation:['listBankLines'],Bordereaux:['listBordereaux'],Refunds:['listRefunds']};
for(const capture of captures){
 if(!capture.routes.length)continue;
 const operationIds=[...new Set(capture.routes.flatMap(({route,tab})=>{
  if(route==='accounting'){if(!accountingTabs[tab])throw new Error(`Unknown accounting ${tab}`);return accountingTabs[tab];}
  if(route==='agency'&&tab==='Broker accounts')return ['getAgencyStatement'];
  if(route==='policy'&&policyTabs[tab])return policyTabs[tab];
  if(!destinations[route])throw new Error(`Unknown route ${route}`);return destinations[route];
 }))];
 const row={controlId:capture.controlId,status:'reviewed',disposition:'navigation-read',operationIds,reason:'Inspected navigation-only closure with go intercepted in an isolated VM; resolved destination is recorded in dynamic-navigation.json. Read selected authorised record ID and target tab; never use prototype fixed references. Navigation creates no domain mutation.'};
 const index=rows.findIndex(r=>r.controlId===row.controlId);if(index<0)rows.push(row);else rows[index]=row;
}
await writeFile(file,JSON.stringify(rows,null,2)+'\n');
