import test from 'node:test';
import assert from 'node:assert/strict';
import {emptyCommercialProposal,changeCommercialField,addCommercialRow,commercialRows,setCommercialResponse,sumCommercialMoney,clearCommercialSection,commercialQuestionApplies,commercialDecimalInput,commercialPostcodeInput,commercialInputStage} from '../lib/commercial-capture.ts';

const question=(id,kind='boolean',stage=6)=>({id,kind,stage,label:id,container:'risk.declarations',options:[]});
test('commercial totals use exact pennies and never treat unknown as zero',()=>{
 assert.equal(sumCommercialMoney(['0.10','0.20']), '0.30');
 assert.equal(sumCommercialMoney(['9999999999999.99','9999999999999.99']), '19999999999999.98');
 assert.equal(sumCommercialMoney(['0.00','0.00']), '0.00');
 assert.equal(sumCommercialMoney(['1.00',undefined]), undefined);assert.equal(sumCommercialMoney([]),undefined);
 assert.throws(()=>sumCommercialMoney(['1.001']));assert.throws(()=>sumCommercialMoney(['-1.00']));
});
test('clearing deselected EL or BI details preserves other sections and stable identities',()=>{
 let p=addCommercialRow(emptyCommercialProposal(),'locations');const locationId=commercialRows(p,'locations')[0].id;
 p=addCommercialRow(p,'wages');p=changeCommercialField(p,'risk.liability',{employersLimit:'10000000.00',publicLimit:'5000000.00',employersReferenceNumber:'DEMO'});
 p=changeCommercialField(p,'risk.businessInterruption',{basis:'gross-profit',sumInsured:'10.00'});
 const clearEl=clearCommercialSection(p,'el');assert.deepEqual(commercialRows(clearEl,'wages'),[]);
 assert.equal(clearEl.risk.liability.employersReferenceNumber,undefined);assert.equal(clearEl.risk.liability.publicLimit,'5000000.00');
 assert.equal(clearEl.risk.businessInterruption.sumInsured,'10.00');assert.equal(commercialRows(clearEl,'locations')[0].id,locationId);
 const clearBi=clearCommercialSection(clearEl,'bi');assert.equal(clearBi.risk.businessInterruption,undefined);
 assert.equal(p.risk.liability.employersReferenceNumber,'DEMO');assert.equal(commercialRows(p,'wages').length,1);
});
test('subsidence questions follow explicit cover selection; flood Yes reference requires details',()=>{
 const selected=question('prototype.quote.be47c08f530f');const signs=question('prototype.quote.aaccb5c97c33');
 const history=question('prototype.quote.ade0f3f0df5e','reference');const details=question('prototype.quote-value.4799b8daa1ca','text');
 const catalogue={questions:[selected,signs,history,details]};let p=setCommercialResponse(emptyCommercialProposal(),selected,undefined,false);
 assert.equal(commercialQuestionApplies(p,signs,catalogue),false);assert.equal(commercialQuestionApplies(p,details,catalogue),false);
 p=setCommercialResponse(p,history,undefined,{collection:history.id,value:2,label:'Yes — at the location',version:'commercial-reference-1'});
 assert.equal(commercialQuestionApplies(p,details,catalogue),true);p=setCommercialResponse(p,selected,undefined,true);
 assert.equal(commercialQuestionApplies(p,signs,catalogue),true);
});
test('height and postcode inputs reject incomplete or invented values without defaulting to zero',()=>{
 assert.deepEqual(commercialDecimalInput(''),{});assert.deepEqual(commercialDecimalInput('0'),{value:0});assert.deepEqual(commercialDecimalInput('2.05'),{value:2.05});
 for(const input of ['1e2','2.001','1000.01','-2','NaN'])assert.ok(commercialDecimalInput(input).error);
 assert.deepEqual(commercialPostcodeInput(''),{});assert.deepEqual(commercialPostcodeInput('s9 2qt'),{value:'s9 2qt'});assert.ok(commercialPostcodeInput('ZZ9 9ZZ').error);
});
test('validation returns to the owning stage and stable dependency instead of the proposer',()=>{
 const p=addCommercialRow(emptyCommercialProposal(),'dependencies');const id=commercialRows(p,'dependencies')[0].id;const catalogue={questions:[]};
 assert.equal(commercialInputStage(p,catalogue,'risk.liability.maximumHeightMetres'),7);
 assert.equal(commercialInputStage(p,catalogue,'risk.businessInterruption.sumInsured'),6);
 assert.equal(commercialInputStage(p,catalogue,`${id}:limit`),6);
 assert.equal(commercialInputStage(p,catalogue,'cover.contractWorks.excess'),9);
 assert.equal(commercialInputStage(p,catalogue,'risk.business.turnover'),1);
});
