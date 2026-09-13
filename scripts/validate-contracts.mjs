import {readFile,writeFile} from 'node:fs/promises';
import {spawnSync} from 'node:child_process';
import {pathToFileURL} from 'node:url';
export function validateCoverage(inventory,map,spec,conditional,source){
 const operations=new Map(Object.values(spec.paths).flatMap(path=>Object.values(path).filter(o=>o.operationId).map(o=>[o.operationId,o])));
 if(map.sourceSha256!==inventory.sourceSha256)throw new Error('Source hash mismatch');
 const rows=new Map(map.controls.map(c=>[c.controlId,c]));
 if(rows.size!==map.controls.length||rows.size!==inventory.controls.length)throw new Error('Missing or duplicate control mapping');
 for(const c of inventory.controls){const row=rows.get(c.id);
  if(!row||row.status!=='reviewed')throw new Error(`Unreviewed control ${c.id}`);
  if(!row.operationIds.length&&row.disposition!=='client-only')throw new Error(`Missing operation ${c.id}`);
  for(const id of row.operationIds)if(!operations.has(id))throw new Error(`Broken operation reference ${id}`);
  if(!c.requirement||!c.acceptance||!row.reason)throw new Error(`Missing acceptance ownership ${c.id}`);
 }
 for(const c of conditional.controls){if(!source.includes(c.sourceKey))throw new Error(`Conditional source missing ${c.id}`);if(!operations.has(c.operationId))throw new Error(`Conditional operation missing ${c.id}`);}
 return {controls:rows.size,conditionalRules:conditional.controls.length,operations:operations.size};
}
const read=async path=>JSON.parse(await readFile(path,'utf8'));
export async function validateAndBuildMatrix(){
 const [inventory,map,spec,conditional,source]=await Promise.all([read('docs/design/control-inventory.json'),read('docs/design/api-control-map.json'),read('contracts/openapi.json'),read('contracts/examples/conditional-capture.json'),readFile('docs/design/source/prototype-template.txt','utf8')]);
 const result=validateCoverage(inventory,map,spec,conditional,source);
 const byId=new Map(map.controls.map(c=>[c.controlId,c]));
 const byOperation=new Map(Object.values(spec.paths).flatMap(path=>Object.values(path).filter(o=>o.operationId).map(o=>[o.operationId,o])));
 const clean=s=>String(s).replaceAll('|','\\|').replaceAll('\n',' ');
 const lines=['# Prototype acceptance matrix','','Design traceability only. Every row remains **runtime pending** until its feature phase verifies the browser action, API permission, persistence/reload, failure path and audit. Product fixture datasets supply fictional source records; never send real messages/payments.','', '| Scenario | Source control | Requirement / phase | API operation(s) | Design behavior and acceptance basis |','|---|---|---|---|---|'];
 for(const c of inventory.controls){const m=byId.get(c.id);const permissions=[...new Set(m.operationIds.map(id=>byOperation.get(id)['x-permission']))].join(', ')||'Local presentation';lines.push(`| ${c.acceptance} | ${c.id} ${clean(c.method)}: ${clean(c.label)} | ${c.requirement} / ${c.phase} | ${m.operationIds.join(', ')||'Client only'}; permission: ${clean(permissions)} | ${clean(m.reason)} Data: ${clean((m.fieldBindings??[]).map(b=>b.targetPath).join(', ')||c.persistence)} Validate: ${clean(c.validation)} Failure: ${clean(c.failure)} |`);}
 lines.push('','## Conditional source branches','','These supplement render variants and must be exercised in the relevant product phase.','');
 for(const c of conditional.controls)lines.push(`- **${c.id}** (${c.when}), ${c.operationId}: ${c.acceptance}`);
 lines.push('','## Required cross-cutting checks','','For each persisted command test allowed actor, denied actor/agency, stale ETag, successful reload and repeat submission. Issue/payment tests use SQL transaction boundaries and restart faults. Read controls test scope before counts/pagination and download-time reauthorisation. Source field bindings and exact question catalogs are in api-control-map.json and contracts/examples; the API security extension and PERMISSIONS.md define actual authority. Human UAT is not covered by design tests.','');
 await writeFile('docs/design/ACCEPTANCE-MATRIX.md',lines.join('\n'));
 return result;
}
if(process.argv[1]&&import.meta.url===pathToFileURL(process.argv[1]).href){
 for(const args of [['scripts/lint-openapi.mjs'],['--test','tests/*.test.mjs']]){const run=spawnSync(process.execPath,args,{stdio:'inherit'});if(run.status!==0)process.exit(run.status??1);}
 console.log(JSON.stringify(await validateAndBuildMatrix()));
}
