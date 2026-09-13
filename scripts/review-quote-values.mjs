import {readFile,writeFile} from 'node:fs/promises';
const inventory=JSON.parse(await readFile('docs/design/control-inventory.json','utf8'));
const file='docs/design/reviewed-api-controls.json';const reviews=JSON.parse(await readFile(file,'utf8'));
const questions=[];
// Explicit source field meanings; units belong to the catalog, never inferred at runtime.
const fields={
 '3fd9edd7e66e':['text','risk.business.responses'], '3e4fdf2b682e':['text','risk.business.responses'],
 '909e1c6eff8c':['text','risk.business.responses'],'2d662a3ec81d':['text','risk.business.responses'],
 ec9fcabe7952:['count','risk.business.responses'], '4500a357b44b':['count','risk.business.responses'],
 '35a0dd933924':['count','risk.business.responses'],'636aa0774e1c':['count','risk.business.responses'],
 '315960b57ab1':['boolean','risk.business.responses'],
 '0b9bf2ac4b8f':['percentage','risk.business.responses'],c2b0372b4040:['percentage','risk.business.responses'],
 b99b3a5b3004:['text','risk.declarations'],'7adfff3ade76':['text','risk.declarations'],
 '59c91e8db152':['text','risk.declarations'],acace76b1053:['text','risk.declarations'],
 '5fcb5378a1fd':['text','risk.declarations'],'4799b8daa1ca':['text','risk.declarations'],
 '5743fa7db272':['money','cover.responses'],fa63248f9ae1:['money','cover.responses'],
 '92094fbe9a4b':['count','risk.business.responses'],'433ec08d2c67':['text','risk.business.responses'],
 '4479e664ad2f':['text','risk.business.responses'],a0e5d1b910f8:['text','risk.declarations']
};
function put(c,row){const index=reviews.findIndex(r=>r.controlId===c.id);const value={controlId:c.id,status:'reviewed',...row};if(index<0)reviews.push(value);else reviews[index]=value;}
for(const [suffix,[kind,targetContainer]] of Object.entries(fields)){
 const c=inventory.controls.find(c=>c.id===`CTL-${suffix}`);if(!c)throw new Error(suffix);
 const questionId=`prototype.quote-value.${suffix}`;
 questions.push({questionId,label:c.label,kind,targetContainer,sourceControlId:c.id,sourceOptions:kind==='boolean'?['Yes','No']:[],unit:kind==='percentage'?'basis-points':kind==='money'?'GBP':kind==='count'?'count':'none'});
 put(c,{disposition:'edit-then-save',operationIds:['saveQuoteProposal'],fieldBindings:[{targetPath:`${targetContainer}.answers`,questionId}],reason:'Explicitly reviewed field uses a pinned typed question. Percentages convert exactly to basis points; money uses two decimal GBP; counts are nonnegative integers. Free-text distance retains supplied units without guessing. Applicability and cross-field totals require product validation.'});
}
const activityIds=['e4214dae194f','a6d4124b2312','bfa3798e8803','1932e55ba1f3','0e2bd5ea6a0e','f7d84fa890d0','b3c56b8b3eff'];
for(const suffix of activityIds){const c=inventory.controls.find(c=>c.id===`CTL-${suffix}`);put(c,{disposition:'edit-then-save',operationIds:['saveQuoteProposal'],fieldBindings:[{targetPath:'risk.business.activities[].turnoverBasisPoints'}],activityCode:['vehicle-sales','vehicle-servicing','mechanical-repair','breakdown-recovery','body-repair','vehicle-valeting','other'][activityIds.indexOf(suffix)],reason:'Bind to the stable activity code, convert percent to basis points without truncation, and persist proposal. Rating requires total exactly 10000 and other description when other is positive; incomplete totals may save as draft.'});}
for(const suffix of ['009dd2bb8754','4f3f78fac1b0']){const c=inventory.controls.find(c=>c.id===`CTL-${suffix}`);put(c,{disposition:'client-only',operationIds:[],reason:'Read-only derived count of the two vehicle registers; MID count selects reportable vehicle answers. Never persist a separately editable count that can disagree with the register. Actual submissions use the separately reviewed MID workflow.'});}
for(const [suffix,targetPath] of Object.entries({'58b5bc3f356b':'risk.tradePlates','7fd9eb9a4c55':'risk.businessInterruption.basis'})){const c=inventory.controls.find(c=>c.id===`CTL-${suffix}`);put(c,{disposition:'edit-then-save',operationIds:['saveQuoteProposal'],fieldBindings:[{targetPath}],reason:suffix==='58b5bc3f356b'?'Parse one plate per entry, normalise case/spacing, reject duplicate or invalid plates, retain stable item IDs and persist the proposal. Held No with nonempty plates is a validation conflict.':'Map Gross profit/Gross revenue/Increased cost/Estimated gross profit to explicit BI basis enums; do not collapse estimated gross profit into gross profit.'});}
const removals={
 d3ad8f8ae9bf:['risk.drivers'],fd8ac93bd9a3:['risk.drivers'],'05c2fcf290fc':['risk.drivers'],
 fcefa12ff7d2:['risk.drivers[].convictions','risk.losses'],
 '95722586bd61':['risk.vehicles'],'8619e0fbace4':['risk.vehicles'],
 f0cca3137127:['risk.locations'],d5ab49275b15:['risk.locations'],
 d3599fa8e33d:['risk.wages'],a9582c5204a4:['risk.wages'],b616086b2b64:['risk.wages']
};
for(const [suffix,paths] of Object.entries(removals)){
 const c=inventory.controls.find(c=>c.id===`CTL-${suffix}`);
 put(c,{disposition:'edit-then-save',operationIds:['saveQuoteProposal'],fieldBindings:paths.map(targetPath=>({targetPath})),reason:'Remove the selected stable child ID from the enclosing draft collection, then persist with If-Match; never delete issued history. Reject dangling risk/owner/specification references and require explicit reassignment or removal. Driver removal includes its nested convictions/losses only after explicit confirmation; location removal requires loss reassignment. Shared source row is driver conviction for MT and loss for CC, selected by product/stage, not row index. Recompute totals and invalidate rating/acceptance.'});
}
await writeFile(file,JSON.stringify(reviews,null,2)+'\n');
await writeFile('contracts/examples/prototype-quote-value-questions.json',JSON.stringify({version:'prototype-quote-value-1',questions},null,2)+'\n');
console.log(`${reviews.length} reviewed controls`);
