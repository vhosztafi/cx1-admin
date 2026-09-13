import {readFile,writeFile} from 'node:fs/promises';
const inventory=JSON.parse(await readFile('docs/design/control-inventory.json','utf8'));
const render=JSON.parse(await readFile('docs/design/source/prototype-render-data.json','utf8'));
const file='docs/design/reviewed-api-controls.json';const reviews=JSON.parse(await readFile(file,'utf8'));
const catalog=[];
// Explicitly reviewed modal field targets. q: values are typed product questions
// in the stated child's Responses container; they are not arbitrary JSON keys.
const groups={
 adddriver:{base:'risk.drivers[]',fields:{'Full name':'fullName','Date of birth':'dateOfBirth','Years living in the UK':'q:count:residency-years','Years full UK licence held':'q:count:licence-years',Occupation:'q:text:occupation',Status:'relationship','Full or part time in the motor trade':'q:reference:trade-employment','Other occupation if part time':'q:text:other-occupation','Use required':'usage'}},
 addconv:{base:'risk.drivers[].convictions[]',fields:{'Date of conviction':'occurredOn','Offence code':'code',Fine:'fine','Penalty points':'points','Disqualification period':'banMonths','Pending or convicted':'q:reference:prosecution-status'}},
 addinc:{base:'risk.drivers[].losses[]',fields:{'Date of incident':'occurredOn',Type:'type','Cost of damage to own vehicle':'q:money:own-damage','Cost to third party':'q:money:third-party-damage','At fault':'fault','Was a claim made':'q:boolean:claim-made',Description:'description'}},
 addveh:{base:'risk.vehicles[]',fields:{Registration:'registration','Make and model':'make+model','Body type':'body','Year of make':'manufactureYear','CC or gross vehicle weight':'engineCc+grossWeightKg','Date of purchase':'purchasedOn','Price paid':'purchasePrice','Estimated present trade value':'value','Modified, left-hand drive, imported or adapted':'q:reference:special-characteristics','Report to the motor insurance database':'q:boolean:report-mid'}},
 addloc:{base:'risk.locations[]',fields:{Address:'address',Use:'occupancy','Sole occupier':'q:boolean:sole-occupier','Detached building':'q:boolean:detached','Approximate year built':'q:count:year-built','Number of storeys':'q:reference:storeys','Construction of external walls':'q:reference:wall-construction','Construction of roof':'q:reference:roof-construction','Flat roof':'q:reference:flat-roof','Composite panels present':'q:reference:composite-panels','Timber framed':'q:boolean:timber-frame','Heritage listed':'q:reference:heritage-listed','Method of heating':'q:reference:heating','Basement or cellar':'q:reference:basement','Intruder alarm':'q:reference:intruder-alarm','Level 1 police response':'q:boolean:police-response','Fire alarm':'q:reference:fire-alarm','Buildings sum insured':'buildings','Contents sum insured':'contents','Stock sum insured':'stock'}},
 addwage:{base:'risk.wages[]',fields:{Category:'category','Own employees':'employees','Labour only sub-contractors':'labourOnlySubcontractors','Bona fide sub-contractors':'bonaFideSubcontractors','Number of people in this category':'q:count:headcount'}},
 addloss:{base:'risk.losses[]',fields:{'Date of loss':'occurredOn',Type:'type','Location affected':'riskItemId',Amount:'amount','Was it insured':'q:boolean:insured',Status:'status',Circumstances:'description'}},
 addprem:{base:'risk.premises[]',fields:{Address:'address',Use:'use',Security:'security','Buildings sum insured':'buildings','Contents sum insured':'contents','Vehicles kept overnight':'q:boolean:overnight-vehicles','Open to the public':'q:boolean:public-access'}}
};
for(const c of inventory.controls.filter(x=>x.method==='modalVals'&&x.kind==='input')){
 const targets=c.tabs.filter(tab=>groups[tab]);
 if(!targets.length)continue;
 const fields=targets.map(tab=>({tab,...groups[tab],field:groups[tab].fields[c.label]}));
 if(fields.some(x=>!x.field))continue;
 const bindings=fields.map(({tab,base,field})=>{
  if(!field.startsWith('q:'))return {modal:tab,targetPath:`${base}.${field}`};
  const [,kind,name]=field.split(':');const questionId=`prototype.${tab}.${name}`;
  const source=render.items.find(item=>item.method===c.method&&item.path===c.path&&item.label===c.label&&item.tabs.some(t=>c.tabs.includes(t)));
  const entry={questionId,label:c.label,kind,targetContainer:`${base}.responses`,sourceControlId:c.id,sourceOptions:source?.options??[],unit:kind==='money'?'GBP':kind==='count'?(name.includes('years')?'years':name==='year-built'?'calendar-year':'count'):'none'};
  catalog.push(entry);return {modal:tab,targetPath:`${base}.responses.answers`,questionId};
 });
 reviews.push({controlId:c.id,status:'reviewed',disposition:'edit-then-save',operationIds:['saveQuoteProposal'],fieldBindings:bindings,reason:'Reviewed quote detail input is stored in its typed child item or explicitly defined versioned question. Stable parent/child IDs preserve linkage. Displayed names, combined make/model and CC/weight need explicit UI subfields; never silently split an ambiguous string.'});
}
// Re-running is deterministic and does not duplicate reviewed IDs.
const unique=[...new Map(reviews.map(x=>[x.controlId,x])).values()];
await writeFile(file,JSON.stringify(unique,null,2)+'\n');
await writeFile('contracts/examples/prototype-detail-questions.json',JSON.stringify({version:'prototype-detail-1',questions:catalog},null,2)+'\n');
console.log(`${unique.length} controls reviewed; ${catalog.length} typed detail questions.`);
