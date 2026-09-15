import {readFile,writeFile} from 'node:fs/promises';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const ownership=await read('quote-control-ownership.json'),questions=await read('quote-question-catalogue.json');
const replacements={
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
await writeFile(new URL('../contracts/quote-prototype-bindings.json',import.meta.url),JSON.stringify({sourceSha256:ownership.sourceSha256,questionVersion:questions.version,status:'capture path contract; control value semantics and runtime remain separate gates',controls:bindings},null,2)+'\n');
