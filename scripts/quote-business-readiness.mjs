import {quoteMappingsForProduct,validateQuoteAnswerConditions} from './quote-semantic-contract.mjs';

// Source-backed section readiness, not full quote readiness. Strict shape,
// identity, question and reference validation must precede these checks.
// Sources: frontend-code/src/domain/{proposer,business,premises,declaration}.ts.
const ids=(section,numbers)=>numbers.map(n=>`MTS-${section}-Q${String(n).padStart(2,'0')}`);
export const businessReadinessOwners={
  proposer:ids('01',[6,7,8,9,12,15,16,17,20]),
  business:ids('03',[1,2,3,4,6,8,9]),
  premises:ids('02',[2,3,4,5,6,7,10]),
  declarations:ids('12',[1,3,5,7,9,11,13,15,17,19]),
};
const present=value=>value!==undefined&&value!==null&&(typeof value!=='string'||value.trim().length>0);
const at=(value,path)=>path.split('.').reduce((item,key)=>item?.[key],value);

export function validateQuoteBusinessReadiness(proposal,questions,references) {
  const mappings=quoteMappingsForProduct(questions,proposal.productCode);
  const issues=[];
  const add=(code,path,questionId)=>issues.push({code,path,...(questionId?{questionId}:{})});
  const rowFor=owner=>{
    const row=mappings.find(row=>row.owner===owner);
    if(!row)throw new Error(`missing-readiness-mapping:${owner}`);
    return row;
  };
  const read=(owner)=>{
    const row=rowFor(owner);
    if(row.contractKind==='Answer') {
      const scope=row.canonicalPath.replace(/\.answers\[\]$/,'');
      const answers=at(proposal,scope)?.answers??[];
      const index=answers.findIndex(answer=>answer.questionId===row.questionId);
      return {value:answers[index]?.value,path:`/${scope.replaceAll('.','/')}/answers${index<0?'':`/${index}/value`}`};
    }
    return {value:at(proposal,row.canonicalPath),path:`/${row.canonicalPath.replaceAll('.','/')}`};
  };
  const requireOwner=owner=>{const {value,path}=read(owner);if(!present(value))add('required-capture-field',path,owner);};
  for(const owner of [...businessReadinessOwners.proposer,...businessReadinessOwners.business,...businessReadinessOwners.declarations,'MTS-02-Q01'])requireOwner(owner);
  // Domain decisions only use a pinned reference, never a caller's numeric ID.
  const trusted=(reference,collection)=>{
    if(reference?.collection!==collection||reference.version!==references.version)return undefined;
    return references.collections[collection]?.find(row=>row.value===reference.value&&row.text===reference.label)?.value;
  };
  const company=trusted(read('MTS-01-Q06').value,'companyTypes');
  if([2,3,4].includes(company))requireOwner('MTS-01-Q11');
  const telephone=read('MTS-01-Q13'),mobile=read('MTS-01-Q14');
  if(!present(telephone.value)&&!present(mobile.value))add('contact-number-required','/insured/contact');
  for(const field of [telephone,mobile])if(present(field.value)&&field.value.length>11)add('contact-number-too-long',field.path);
  if(present(mobile.value)&&!mobile.value.startsWith('07'))add('mobile-prefix-invalid',mobile.path);
  const tradingFrom=trusted(read('MTS-02-Q01').value,'tradingFroms');
  const premises=proposal.risk?.premises??[];
  if([3,4].includes(tradingFrom)) {
    if(!premises.length)add('premises-required','/risk/premises');
    premises.forEach((premise,index)=>{
      for(const owner of businessReadinessOwners.premises) {
        const path=rowFor(owner).canonicalPath.replace('risk.premises[].','');
        if(!present(at(premise,path)))add('required-capture-field',`/risk/premises/${index}/${path.replaceAll('.','/')}`,owner);
      }
    });
  } else if(premises.length) add(tradingFrom===undefined?'premises-context-required':'inactive-premises-retained','/risk/premises');
  const handled=read('MTS-03-Q08');
  if(handled.value!==undefined&&handled.value<1)add('vehicles-handled-minimum',handled.path,'MTS-03-Q08');
  const started=read('MTS-03-Q01');
  if(started.value&&started.value<'1900-01-01')add('business-start-too-early',started.path,'MTS-03-Q01');
  // Conditional details retain their own exact question/path issues.
  issues.push(...validateQuoteAnswerConditions(proposal,mappings.filter(row=>/^MTS-(03|12)-/.test(row.owner??''))));
  return issues;
}
