import {readFile,writeFile} from 'node:fs/promises';
const inventory=JSON.parse(await readFile('docs/design/control-inventory.json','utf8'));
const file='docs/design/reviewed-api-controls.json';
const reviews=JSON.parse(await readFile(file,'utf8'));
function put(control,operationIds,reason,fieldBindings){
 const row={controlId:control.id,status:'reviewed',disposition:operationIds.length?'command-or-navigation':'client-only',operationIds,reason,...(fieldBindings?{fieldBindings}:{})};
 const index=reviews.findIndex(r=>r.controlId===control.id);if(index<0)reviews.push(row);else reviews[index]=row;
}
const exact={
 b05c609fb19a:[['saveQuoteProposal','listQuotes'],'Save incomplete typed proposal with If-Match; navigate only after success. Retain edits and show errors on failure.'],
 bced6e519228:[['saveQuoteProposal'],'Persist incomplete proposal; replace toast reference with returned quote identity and revision.'],
 '36b43c0519b3':[['listPolicies','clonePolicyToQuote'],'Select authorised issued version and relationship; create a distinct draft with lineage and no inherited acceptance/rating/decisions. Do not silently overwrite the current draft.'],
 '34fef62548c5':[['withdrawQuote','listQuotes'],'Record withdrawal reason and retain audit/history. Navigate only after successful withdrawal.'],
 '27261fc05988':[['saveQuoteProposal','validateQuote','rateQuote'],'Save first, validate the returned revision, then queue rating for that same revision. Server revalidates; missing answers link to their stages. Display actual persisted rating job/result.'],
 ee3368ee8a32:[[],'Progress indicator style is local presentation; changing it never marks questions complete or changes persisted risk.']
};
const composers={e29a40c53e65:'risk.business.description','82ebf22fee24':'risk.business.description',fbcf28cc54fc:'risk.materialFacts','6a617e77cc8e':'risk.materialFacts'};
const modalKinds=new Set(['adddriver','addconv','addinc','addveh','addprem','addloss','addloc','addwage']);
for(const c of inventory.controls.filter(c=>c.method==='pNewQuote')){
 const key=c.id.slice(4);
 if(exact[key]){put(c,...exact[key]);continue;}
 if(composers[key]){put(c,['saveQuoteProposal'],'Composer changes edit the typed proposal; onSend persists the whole current proposal using If-Match. A toast alone does not count as saving.',[{targetPath:composers[key]}]);continue;}
 if(['() => onJump(n)','() => next(step + 1)','() => next(step - 1)'].includes(c.handlers.onClick)){
  put(c,['saveQuoteProposal','getQuote'],'Save dirty incomplete proposal before moving to the bounded product-specific step; keep current step/edits on failure. Completion derives from required answers, not visited steps. Resume from persisted answers; route step is presentation state.');continue;
 }
 const modal=c.handlers.onAction?.match(/modal:\{kind:'([^']+)'/);
 if(modal&&modalKinds.has(modal[1]))put(c,[],'Open the reviewed risk-detail modal. Opening/cancelling writes nothing; its separately mapped confirmation validates and saves through saveQuoteProposal. Preserve own-versus-sale vehicle mode.');
}
await writeFile(file,JSON.stringify(reviews,null,2)+'\n');
console.log(`${reviews.length} reviewed controls`);
