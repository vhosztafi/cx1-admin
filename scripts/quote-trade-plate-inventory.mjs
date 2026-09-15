import {quoteMappingsForProduct} from './quote-semantic-contract.mjs';

export function validateQuoteTradePlateInventory(proposal,questions) {
 quoteMappingsForProduct(questions,proposal.productCode);
 const risk=proposal.risk??{},id='prototype.quote-value.315960b57ab1';
 const held=risk.business?.responses?.answers?.find(a=>a.questionId===id)?.value;
 const covered=risk.responses?.answers?.find(a=>a.questionId==='MTS-07-Q01')?.value;
 const rows=risk.heldTradePlates??[],issues=[],numbers=new Set();
 const normalize=value=>value.replaceAll(' ','').toUpperCase();
 if(held===undefined)issues.push({code:'trade-plates-held-answer-required',path:'/risk/business/responses/answers',questionId:id});
 if(held===true&&!rows.length)issues.push({code:'held-trade-plate-inventory-required',path:'/risk/heldTradePlates'});
 if(held!==true&&rows.length)issues.push({code:'inactive-held-trade-plates',path:'/risk/heldTradePlates'});
 rows.forEach((row,index)=>{
  const path=`/risk/heldTradePlates/${index}/number`;
  if(typeof row.number!=='string'||!row.number.trim()){issues.push({code:'held-trade-plate-number-required',path});return;}
  const number=normalize(row.number);if(numbers.has(number))issues.push({code:'duplicate-held-trade-plate',path});numbers.add(number);
 });
 if(covered===true&&held!==true)issues.push({code:'covered-trade-plates-require-held-declaration',path:'/risk/responses/answers'});
 (risk.tradePlates??[]).forEach((row,index)=>{
  if(typeof row.number==='string'&&!numbers.has(normalize(row.number)))issues.push({code:'covered-trade-plate-not-held',path:`/risk/tradePlates/${index}/number`});
 });
 return issues;
}
