import {readFile,writeFile} from 'node:fs/promises';
const inventory=JSON.parse(await readFile('docs/design/control-inventory.json','utf8'));
const rendered=JSON.parse(await readFile('docs/design/source/prototype-render-data.json','utf8'));
const file='docs/design/reviewed-api-controls.json';const reviews=JSON.parse(await readFile(file,'utf8'));
const catalog=[];
const direct={
 fb92851f2483:'insured.proposerNames', '54cd86bd3577':'insured.proposerNames',ad6422c180bc:'insured.proposerNames',ceac3f42ee3f:'insured.tradingName','212ce9265e52':'insured.entityType',f6a27f05ee04:'insured.companyNumber','21ebcb37d76a':'insured.address',d29cf683b746:'insured.contact.telephone','07d3f2a4c5de':'insured.contact.email',b68cb9b7bb57:'risk.business.startedOn',
 ac167725da1f:'risk.previousInsurance.insurer','6ca7d23ce712':'risk.previousInsurance.expiresOn',ba4a89b86e80:'risk.previousInsurance.noClaimsYears',
 '0fabfac70c54':'insured.proposerNames','485a887e9c09':'insured.tradingName','013d2d83308a':'insured.entityType','244d1e602d04':'insured.companyNumber',b8c835b4be7d:'risk.liability.employersReferenceNumber','58ce136bec6e':'insured.address',d5d492a9e1b4:'risk.business.turnover',
 a27790bacfc5:'risk.businessInterruption.sumInsured',be3540a2ad2e:'risk.businessInterruption.indemnityMonths','473e38b699c4':'risk.liability.maximumHeightMetres',d7068d9c43bd:'risk.liability.employersLimit','092ac5f51b35':'risk.liability.publicLimit','631d675e32e2':'risk.liability.productsLimit'
};
const selection={
 '3ba462f99580':['listAgencies','createQuote'], '0f8fc7e35665':['listContacts'],f90bbda7baf4:['listProductVersions','createQuote'],'5b72b50e96bc':['listProductVersions','createQuote'],'2dd04027e52d':['listProductVersions','createQuote'],'74a9d7f987c5':['listClients','createQuote']
};
function put(row){const i=reviews.findIndex(x=>x.controlId===row.controlId);if(i<0)reviews.push(row);else reviews[i]=row;}
for(const c of inventory.controls.filter(x=>x.method==='pNewQuote'&&x.kind==='input')){
 const suffix=c.id.slice(4);
 if(selection[suffix]){put({controlId:c.id,status:'reviewed',disposition:'read-selection',operationIds:selection[suffix],reason:'Reviewed agency/contact/product/client selector resolves authorised stable identities. A displayed option label never grants scope, and changing an established quote relationship requires an explicit supported re-scope or new quote.'});continue;}
 if(direct[suffix]){put({controlId:c.id,status:'reviewed',disposition:'edit-then-save',operationIds:['saveQuoteProposal'],fieldBindings:[{targetPath:direct[suffix]}],reason:'Reviewed capture field writes this typed proposal path. Names stay as entered; monetary strings, structured addresses and ISO dates are normalised explicitly. Multiple proposer controls preserve array order; months/metres are explicit units.'});continue;}
 // Only source-rendered option inputs are classified here. Free text, dates,
 // numbers and combined composer callbacks stay pending for explicit mapping.
 const item=rendered.items.find(x=>x.method===c.method&&x.path===c.path&&x.label===c.label&&Array.isArray(x.options)&&x.options.length);
 if(!item||['Date insurance to start','Term','Vehicles on the register','Vehicles to report to the motor insurance database','Trade plates held','Trade plate numbers','Evidence received','Proof of discount received','Claims experience document held','Basis'].includes(c.label))continue;
 const steps=item.tabs.filter(t=>t.includes(':step-'));
 if(!steps.length)continue;
 // Reviewed stage boundaries: product-specific capture may have different
 // numbers, but each retains the original stage and scope in this catalog.
 const isCc=steps.every(t=>t.startsWith('Commercial Combined:'));
 const isCover=steps.every(t=>isCc?/:step-(7|10)$/.test(t):t.startsWith('Motor Trade Road Risks:')?/:step-8$/.test(t):/:step-9$/.test(t));
 const previous=steps.every(t=>t.startsWith('Motor Trade Road Risks:step-7')||t.startsWith('Motor Trade Combined:step-8'));
 const container=isCover?'cover.responses':previous?'risk.previousInsurance.responses':isCc?'risk.declarations':'risk.business.responses';
 const yesNo=item.options.length===2&&item.options.some(v=>/^Yes(?:\b|$)/.test(v))&&item.options.some(v=>/^No(?:\b|$)/.test(v));
 const kind=yesNo?'boolean':'reference';const questionId=`prototype.quote.${suffix}`;
 const question={questionId,label:c.label,kind,targetContainer:container,sourceControlId:c.id,sourceOptions:item.options,stages:steps,referenceValues:yesNo?[]:item.options.map((label,index)=>({value:index+1,label})),requiredWhenApplicable:true};
 catalog.push(question);
 put({controlId:c.id,status:'reviewed',disposition:'edit-then-save',operationIds:['saveQuoteProposal'],fieldBindings:[{targetPath:`${container}.answers`,questionId}],reason:'Reviewed source dropdown retains every option in the pinned question catalog. Binary yes/no becomes boolean; categorical choices retain versioned ordinal value and label. Declaration/risk/cover scope and original capture stage are recorded; no free-text type guessed.'});
}
await writeFile(file,JSON.stringify(reviews,null,2)+'\n');
await writeFile('contracts/examples/prototype-quote-questions.json',JSON.stringify({version:'prototype-quote-1',questions:catalog},null,2)+'\n');
console.log(`${reviews.length} controls reviewed; ${catalog.length} quote option questions.`);
