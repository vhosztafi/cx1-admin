import {readFile,writeFile} from 'node:fs/promises';
const inventory=JSON.parse(await readFile('docs/design/control-inventory.json','utf8'));
const file='docs/design/reviewed-api-controls.json';
const reviews=JSON.parse(await readFile(file,'utf8'));
const evidence={
 ede6aab29eed:[['photocard-both-sides','driving-record'],'driver'],f635873d20cd:['no-claims-proof','proposal'],
 '9fff96d5eae7':['photocard-both-sides','driver'],'9fe3e32b1d3d':['driving-record','driver'],
 '894165a378c1':['no-claims-proof','proposal'],'107ada5f5bfe':['motor-trader-proof','proposal'],
 e48b5c40a127:['five-year-claims','proposal'],'73b23541e556':['five-year-claims','proposal'],
 '8edb6a187769':['electrical-inspection-remediation','location'],'98e734d0c0dc':['alarm-maintenance','location'],
 de020621bc82:['health-safety-assessments','proposal'],edc0a8d5978f:['subsidence-survey','location']
};
const catalog=[];
for(const [suffix,[requirementCode,scope]] of Object.entries(evidence)){
 const c=inventory.controls.find(c=>c.id===`CTL-${suffix}`);if(!c)throw new Error(suffix);
 catalog.push({controlId:c.id,label:c.label,requirementCodes:Array.isArray(requirementCode)?requirementCode:[requirementCode],scope});
 const row={controlId:c.id,status:'reviewed',disposition:'evidence-workflow',operationIds:['listQuoteEvidence','attachQuoteEvidence','withdrawQuoteEvidence'],reason:'Received/checked opens a scoped document-version picker and persists evidence association; per-driver/location requirements need stable riskItemId for every applicable item. Uncheck withdraws associations with reason, never deletes document bytes. Received means attached, not underwriting accepted. Satisfaction derives from current applicable evidence and decisions; preserve errors and refresh after success.'};
 const index=reviews.findIndex(r=>r.controlId===row.controlId);if(index<0)reviews.push(row);else reviews[index]=row;
}
await writeFile(file,JSON.stringify(reviews,null,2)+'\n');
await writeFile('contracts/examples/prototype-evidence-requirements.json',JSON.stringify({version:'prototype-evidence-1',requirements:catalog},null,2)+'\n');
console.log(`${reviews.length} reviewed controls`);
