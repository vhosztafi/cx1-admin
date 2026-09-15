import {readFile,writeFile} from 'node:fs/promises';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const ownership=await read('quote-control-ownership.json'),questions=await read('quote-question-catalogue.json');
const references=await read('reference-data/motor-trade-capture.json');
const activitySplitControls={'CTL-e4214dae194f':'sales','CTL-a6d4124b2312':'servicing','CTL-bfa3798e8803':'mechanicalRepair','CTL-1932e55ba1f3':'breakdownRecovery','CTL-0e2bd5ea6a0e':'bodyRepairs','CTL-f7d84fa890d0':'valeting','CTL-b3c56b8b3eff':'other'};
const proposerSlots={'CTL-fb92851f2483':0,'CTL-54cd86bd3577':1,'CTL-ad6422c180bc':2};
const referenceSelections={
 'CTL-e7838b90abda':{collection:'driverRelationshipsPolicyHolder',values:{Proprietor:3,'Business partner':4,Director:1,Spouse:5,Employee:2}},
 'CTL-1624517d8aa7':{collection:'driverUsages',values:{'Motor trade only':1,'Motor trade + SD&P':2}},
};
const offenceLabels=['SP30 — exceeding statutory speed limit','SP50 — exceeding speed limit on a motorway','CU80 — using a mobile telephone','IN10 — using a vehicle uninsured','TS10 — failing to comply with traffic light signals','MS90 — failure to give driver information','DR10 — driving with excess alcohol','AC10 — failing to stop after an accident','CD10 — driving without due care and attention'];
referenceSelections['CTL-7b5a86aac53f']={collection:'driverMTConvictionCodes',placeholderOptions:['Select a code'],values:Object.fromEntries(offenceLabels.map(label=>{
 const code=label.split(' — ')[0],matches=references.collections.driverMTConvictionCodes.filter(row=>row.text.startsWith(`${code} - `));
 if(matches.length!==1)throw new Error(`unresolved-prototype-offence:${code}`);
 return [label,matches[0].value];
}))};
const replacements={
 'risk.tradePlates':{paths:['risk.heldTradePlates'],rule:'Capture plates held as inventory with stable row IDs. Source risk.tradePlates remains the separately selected covered list; ownership does not imply cover.'},
 'insured.entityType':{paths:['insured.entityType'],rule:'Preserve the legal entity choice. Capture public/private company subtype separately; LLP uses the source partnership relationship rules without changing its legal entity.',values:{'Sole trader':'sole-trader',Partnership:'partnership','Limited company':'limited-company',LLP:'llp'}},
 'risk.drivers[].convictions[].banMonths':{paths:['risk.drivers[].convictions[].declaredBanPeriod','risk.drivers[].convictions[].banMonths'],rule:'Preserve the selected band separately; collect exact months for a disqualification before readiness, never invent a band midpoint.',values:{None:'none','Under 3 months':'under-3-months','3 to 6 months':'3-to-6-months','6 to 12 months':'6-to-12-months','Over 12 months':'over-12-months'}},
 'risk.drivers[].losses[].fault':{paths:['risk.drivers[].losses[].fault'],rule:'Preserve split liability as distinct from fault, non-fault and not yet determined.',values:{Yes:'fault',No:'non-fault','Split liability':'split','Not yet determined':'unknown'}},
 'term.startsAt':{paths:['termIntent.localStartDate','termIntent.localStartTime','termIntent.timeZone','termIntent.utcOffsetMinutes'],rule:'Capture local date/time with London zone and explicit offset only when needed; UTC is server-derived.'},
 'term.kind+endsAt':{paths:['termIntent.kind','termIntent.localEndDate','termIntent.localEndTime','termIntent.endUtcOffsetMinutes'],rule:'Annual end is derived; short-period end has explicit local date/time and disambiguation.'},
 'risk.vehicles[].make+model':{paths:['risk.vehicles[].make','risk.vehicles[].model'],rule:'Use separately labelled make and model fields in the compound control; never split free text heuristically.'},
 'risk.vehicles[].engineCc+grossWeightKg':{paths:['risk.vehicles[].declaredEngineSize','risk.vehicles[].grossWeightKg'],rule:'Use explicit engine-size and gross-weight fields by trusted vehicle category, never infer units from free text.'},
 'risk.previousInsurance.noClaimsYears+noClaimsYearsBasis':{paths:['risk.previousInsurance.noClaimsYears','risk.previousInsurance.noClaimsYearsBasis'],rule:'Capture years and the declared basis separately; reconcile with NCB source answers before readiness.'},
 'risk.losses':{paths:['risk.drivers[].losses'],rule:'Driver incident removal targets the selected driver and stable loss ID; no root RoadRisk.losses collection exists.'},
};
const bindings=ownership.controls.filter(control=>control.featurePhase===5&&control.sourceFieldBindings.length).map(control=>({
 controlId:control.controlId,method:control.method,sourcePath:control.path,label:control.label,products:control.products,
 bindings:control.sourceFieldBindings.map(binding=>{
  if(Object.hasOwn(proposerSlots,control.controlId))return {kind:'field-or-collection',paths:['insured.proposerNames'],arrayIndex:proposerSlots[control.controlId],captureRule:'Preserve the ordered full name in this proposer slot; omit unused trailing slots, not empty names. Do not split names or conflate co-proposers with the source contact person.'};
  if(activitySplitControls[control.controlId])return {kind:'field-or-collection',paths:[`risk.business.declaredActivitySplit.${activitySplitControls[control.controlId]}`],unit:'basis-points',captureRule:'Preserve this prototype percentage independently of source occupation rows. Seven declared shares must total 10000 basis points; do not invent servicing/repair allocations from a combined source occupation.'};
  const selection=referenceSelections[control.controlId];
  if(selection) {
   const referenceMapping=Object.fromEntries(Object.entries(selection.values).map(([label,value])=>{
    const row=references.collections[selection.collection].find(row=>row.value===value);
    if(!row)throw new Error(`unresolved-prototype-reference:${control.controlId}:${label}`);
    return [label,{collection:selection.collection,value:row.value,label:row.text,version:references.version}];
   }));
   return {kind:'field-or-collection',paths:[binding.targetPath],referenceMapping,...(selection.placeholderOptions?{placeholderOptions:selection.placeholderOptions}:{}),captureRule:'Translate the explicit source label to this pinned reference; validate current company/driver eligibility separately. Placeholder options never become saved references.'};
  }
  const direct=questions.directReferenceFields?.find(field=>field.controlId===control.controlId);
  if(direct)return {kind:'field-or-collection',paths:[direct.canonicalPath],optionCollection:direct.collection,products:direct.products,captureRule:'Preserve the pinned prototype selection; premises activity/use is separate from source physical premise type.'};
  if(binding.questionId) {
   const question=questions.mappings.find(row=>row.questionId===binding.questionId);
   if(!question)throw new Error(`unmapped-prototype-question:${binding.questionId}`);
   return {kind:'answer',paths:[question.canonicalPath],questionId:question.questionId,answerKind:question.answerKind,products:question.products};
  }
  const replacement=replacements[binding.targetPath];
  return {kind:replacement?.paths.length>1?'compound':'field-or-collection',paths:replacement?.paths??[binding.targetPath],...(replacement?{captureRule:replacement.rule,...(replacement.values?{valueMapping:replacement.values}:{})}:{}),...(binding.modal?{modal:binding.modal}:{})};
 }),
}));
const collectionViews=[
 {sourceCollection:'qVehOwn',addControlId:'CTL-9387056d9fac',modalMode:'own',collectionPath:'risk.vehicles',discriminatorPath:'register',discriminatorValue:'owned-not-for-sale'},
 {sourceCollection:'qVehSale',addControlId:'CTL-49edfbf20fae',modalMode:'sale',collectionPath:'risk.vehicles',discriminatorPath:'register',discriminatorValue:'held-for-sale'},
];
await writeFile(new URL('../contracts/quote-prototype-bindings.json',import.meta.url),JSON.stringify({sourceSha256:ownership.sourceSha256,questionVersion:questions.version,status:'capture path contract; control value semantics and runtime remain separate gates',controls:bindings,collectionViews},null,2)+'\n');
