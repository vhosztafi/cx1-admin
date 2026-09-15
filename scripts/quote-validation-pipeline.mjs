import {readFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
import {parseQuoteJson,quoteMappingsForProduct,validateQuoteIdentity,validateQuoteQuestions,validateQuoteReferences} from './quote-semantic-contract.mjs';
import {selectQuoteDynamicOptions} from './quote-dynamic-options.mjs';
import {validateQuoteTerm} from './quote-term-contract.mjs';
import {validateQuoteSourceRules,validateQuoteTextBounds} from './quote-source-rules.mjs';
import {validateQuoteBusinessReadiness} from './quote-business-readiness.mjs';
import {validateQuotePrototypeBusiness} from './quote-prototype-business.mjs';
import {validateQuotePrototypeVehicles} from './quote-prototype-vehicles.mjs';
import {validateQuotePrototypeDetails} from './quote-prototype-details.mjs';
import {validateQuoteDriverReadiness} from './quote-driver-readiness.mjs';
import {validateQuoteDriverEligibility} from './quote-driver-eligibility.mjs';
import {validateQuoteDriverPlan} from './quote-driver-plan.mjs';
import {reconcileQuoteDriverDeclarations} from './quote-driver-reconciliation.mjs';
import {reconcileQuoteHistory} from './quote-history-reconciliation.mjs';
import {validateQuoteVehicleReadiness} from './quote-vehicle-readiness.mjs';
import {validateQuoteVehicleOwnership,validateQuoteVehicleOvernight} from './quote-vehicle-ownership.mjs';
import {validateQuoteVehicleLimits} from './quote-vehicle-limits.mjs';
import {validateQuoteInsuranceReadiness} from './quote-insurance-readiness.mjs';
import {validateQuotePrototypeInsurance} from './quote-prototype-insurance.mjs';
import {reconcileQuoteInsuranceDeclarations} from './quote-insurance-reconciliation.mjs';
import {validateQuoteCoverReadiness} from './quote-cover-readiness.mjs';
import {reconcileQuoteCoverDeclarations} from './quote-cover-declarations.mjs';
import {validateQuoteExtrasReadiness} from './quote-extras-readiness.mjs';
import {validateQuoteAdditionalReadiness,validateQuoteActivityReadiness} from './quote-additional-readiness.mjs';
import {validateQuotePortfolioReadiness} from './quote-portfolio-readiness.mjs';

// Executable integration design, not a live API. "section-checks-pass" is not
// full readiness: prototype completeness, evidence and current authority remain
// separate gates. Caller supplies trusted as-of and persisted vehicle modes.
export async function createQuoteValidationPipeline() {
 const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
 const [schema,questions,references]=await Promise.all([read('schemas/quote-draft.schema.json'),read('quote-question-catalogue.json'),read('reference-data/motor-trade-capture.json')]);
 const ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);const shape=ajv.compile(schema);
 return (text,{asOfDate,vehicleModes={}}={})=>{
  let proposal;
  try{proposal=parseQuoteJson(text);}catch(error){return {status:'invalid-draft',issues:[{stage:'json',code:error instanceof SyntaxError?'invalid-json':error.message,path:''}]};}
  if(!shape(proposal))return {status:'invalid-draft',issues:shape.errors.map(error=>({stage:'schema',code:error.keyword,path:error.instancePath}))};
  const mappings=quoteMappingsForProduct(questions,proposal.productCode);
  const identity=validateQuoteIdentity(proposal),questionIssues=validateQuoteQuestions(proposal,mappings,questions.version);
  if(identity.length||questionIssues.length)return {status:'invalid-draft',issues:[...identity.map(i=>({stage:'identity',...i})),...questionIssues.map(i=>({stage:'questions',...i}))]};
  const dynamic=selectQuoteDynamicOptions(proposal,references,{requireDriverAnswers:true});
  const issues=[];
  const collect=(stage,rows)=>issues.push(...rows.map(issue=>({stage,...issue})));
  // References can require context that incomplete drafts have not captured.
  collect('references',validateQuoteReferences(proposal,references,dynamic.selectedCollections));
  collect('dynamic',dynamic.issues);
  collect('term',validateQuoteTerm(proposal.termIntent).issues);
  collect('source',validateQuoteSourceRules(proposal,asOfDate));
  collect('history-reconciliation',reconcileQuoteHistory(proposal,questions,references,asOfDate));
  collect('text',validateQuoteTextBounds(proposal));
  for(const [stage,validate] of [
   ['business',validateQuoteBusinessReadiness],['prototype-business',validateQuotePrototypeBusiness],['prototype-details',validateQuotePrototypeDetails],['driver',validateQuoteDriverReadiness],
   ['driver-eligibility',validateQuoteDriverEligibility],['driver-plan',validateQuoteDriverPlan],
   ['driver-reconciliation',reconcileQuoteDriverDeclarations],['vehicle-owner',validateQuoteVehicleOwnership],
   ['vehicle-limits',validateQuoteVehicleLimits],['prototype-vehicles',validateQuotePrototypeVehicles],['insurance',validateQuoteInsuranceReadiness],['prototype-insurance',validateQuotePrototypeInsurance],
   ['insurance-reconciliation',reconcileQuoteInsuranceDeclarations],['cover-declarations',reconcileQuoteCoverDeclarations],['extras',validateQuoteExtrasReadiness],
   ['additional',validateQuoteAdditionalReadiness],['activity',validateQuoteActivityReadiness],['portfolio',validateQuotePortfolioReadiness],
  ])collect(stage,validate(proposal,questions,references));
  collect('vehicle',validateQuoteVehicleReadiness(proposal,questions,references,vehicleModes));
  collect('overnight',validateQuoteVehicleOvernight(proposal,questions));
  collect('cover',validateQuoteCoverReadiness(proposal,questions,references).issues);
  return {status:issues.length?'incomplete':'section-checks-pass',issues};
 };
}
