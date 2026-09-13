import {readFile,writeFile} from 'node:fs/promises';
const inventory=JSON.parse(await readFile('docs/design/control-inventory.json','utf8'));
const reviewed=JSON.parse(await readFile('docs/design/reviewed-api-controls.json','utf8'));
const spec=JSON.parse(await readFile('contracts/openapi.json','utf8'));
const ops=new Set(Object.values(spec.paths).flatMap(p=>Object.values(p).map(op=>op.operationId)));
const byId=new Map(reviewed.map(row=>[row.controlId,row]));
if(byId.size!==reviewed.length)throw new Error('Duplicate reviewed control');
for(const row of reviewed){
 if(!inventory.controls.some(c=>c.id===row.controlId))throw new Error(`Unknown control ${row.controlId}`);
 for(const operationId of row.operationIds)if(!ops.has(operationId))throw new Error(`Unknown operation ${operationId}`);
}
const controls=inventory.controls.map(c=>({controlId:c.id,method:c.method,label:c.label,requirement:c.requirement,acceptanceId:c.acceptance,...(byId.get(c.id)??{status:'pending',operationIds:[],reason:'Semantic review required; not inferred from source method or handler keywords.'})}));
const result={sourceSha256:inventory.sourceSha256,reviewed:reviewed.length,total:controls.length,complete:reviewed.length===controls.length,controls};
await writeFile('docs/design/api-control-map.json',JSON.stringify(result,null,2)+'\n');
console.log(`${result.reviewed}/${result.total} controls reviewed; complete=${result.complete}`);
if(process.argv.includes('--complete')&&!result.complete)process.exitCode=1;
