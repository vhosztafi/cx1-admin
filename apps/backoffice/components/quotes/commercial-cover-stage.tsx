'use client';
import {addCommercialRow,changeCommercialField,clearCommercialSection,commercialBiQuestions,commercialField,commercialRows,commercialResponse,sumCommercialMoney,removeCommercialRow,updateCommercialRow,type CommercialProposal,type CommercialCatalogue} from '../../lib/commercial-capture';
import type {QuoteValue} from '../../lib/quotes';
import {CommercialInput,CommercialQuestionFields,type CommercialFormProps} from './commercial-question-fields';
import {CommercialLocationStage,commercialMoney} from './commercial-location-stage';

export function clearSectionInputs(form:CommercialFormProps,section:'el'|'bi'|'contract-works'){
 const prefixes=section==='el'?['risk.liability.employers','risk.wages']:section==='bi'?['risk.businessInterruption',...commercialBiQuestions]:['cover.contractWorks'];
 form.replace(clearCommercialSection(form.proposal,section));
 for(const key of Object.keys(form.buffers))if(prefixes.some(prefix=>key.startsWith(prefix))){form.setBuffer(key,'');form.validity(key);}
}
export function CommercialCoverStage({form,stage}:{form:CommercialFormProps;stage:7|10}){
 const set=(path:string,value:QuoteValue|undefined)=>form.replace(changeCommercialField(form.proposal,path,value));
 const field=(path:string,label:string,kind:'text'|'textarea'|'money'='text',maxLength=200)=><CommercialInput key={path} form={form} inputKey={path} label={label} kind={kind} maxLength={maxLength} value={commercialField(form.proposal,path)} change={value=>set(path,value)}/>;
 const biQuestion=form.catalogue.questions.find(q=>q.id==='prototype.quote.7660fc5eb42e')!;
 const biSelected=(form.proposal.cover?.responses as {answers?:{questionId:string;value:QuoteValue}[]}|undefined)?.answers?.find(x=>x.questionId===biQuestion.id)?.value;
 const bi=form.proposal.risk?.businessInterruption;
 const retainedBi=Boolean(bi&&Object.keys(bi).length)||Object.entries(form.buffers).some(([key,value])=>value&&key.startsWith('risk.businessInterruption'));
 const contractWorksSelected=commercialField(form.proposal,'cover.contractWorks.selected');
 const retainedWorks=commercialField(form.proposal,'cover.contractWorks.sumInsured')!==undefined||commercialField(form.proposal,'cover.contractWorks.excess')!==undefined||Object.entries(form.buffers).some(([key,value])=>value&&key.startsWith('cover.contractWorks'));
 const dependencies=commercialRows(form.proposal,'dependencies');
 return stage===7?<>
   <CommercialLocationStage form={form} stage={-1}/>
   <fieldset className="quote-reference-fields"><legend>Property basis and additional cover</legend><p>Machinery and computers are a breakdown of contents, not additional property sum insured.</p><CommercialQuestionFields form={form} questions={form.catalogue.questions.filter(q=>q.stage===7&&q.id!==biQuestion.id&&!commercialBiQuestions.includes(q.id))}/></fieldset>
   <fieldset className="quote-reference-fields"><legend>Business interruption</legend><CommercialQuestionFields form={form} questions={[biQuestion]}/>
   {biSelected!==true&&retainedBi&&<div><p role="alert">Retained business interruption details need review. Select the cover or clear these details explicitly.</p><button className="button" type="button" onClick={()=>{if(window.confirm('Clear retained business interruption sums, basis, extensions and dependencies? Save the quote to record this change.'))clearSectionInputs(form,'bi');}}>Clear retained BI details</button></div>}
   {(biSelected===true||retainedBi)&&<><div className="quote-form-grid">
    <label>Business interruption basis<select aria-label="Business interruption basis" value={String(bi?.basis??'')} onChange={event=>set('risk.businessInterruption.basis',event.target.value||undefined)}><option value="">Not answered</option><option value="gross-profit">Gross profit</option><option value="gross-revenue">Gross revenue</option><option value="increased-cost-of-working">Increased cost of working only</option><option value="estimated-gross-profit">Estimated gross profit</option></select></label>
    {field('risk.businessInterruption.sumInsured','Business interruption sum insured','money',30)}
    <label>Indemnity period<select aria-label="Indemnity period" value={String(bi?.indemnityMonths??'')} onChange={event=>set('risk.businessInterruption.indemnityMonths',event.target.value?Number(event.target.value):undefined)}><option value="">Not answered</option>{[12,18,24,36].map(value=><option key={value} value={value}>{value} months</option>)}</select></label>
    <label>BI declaration linked<select aria-label="BI declaration linked" value={bi?.declarationLinked===true?'yes':bi?.declarationLinked===false?'no':''} onChange={event=>set('risk.businessInterruption.declarationLinked',event.target.value===''?undefined:event.target.value==='yes')}><option value="">Not answered</option><option value="yes">Yes</option><option value="no">No</option></select></label>
   </div><CommercialQuestionFields form={form} questions={form.catalogue.questions.filter(q=>commercialBiQuestions.includes(q.id))}/>
   <h3>Named suppliers and customers</h3>{dependencies.map((row,index)=><fieldset key={row.id} className="quote-reference-fields"><legend>Dependency {index+1}</legend><div className="quote-form-grid">
    <label>Dependency type {index+1}<select aria-label={`Dependency type ${index+1}`} value={String(row.kind??'')} onChange={event=>form.replace(updateCommercialRow(form.proposal,'dependencies',row.id,'kind',event.target.value||undefined))}><option value="">Not answered</option><option value="supplier">Supplier</option><option value="customer">Customer</option></select></label>
    <CommercialInput form={form} inputKey={`${row.id}:name`} label={`Dependency name ${index+1}`} value={row.name} change={value=>form.replace(updateCommercialRow(form.proposal,'dependencies',row.id,'name',value))}/>
    <CommercialInput form={form} inputKey={`${row.id}:limit`} label={`Dependency limit ${index+1}`} kind="money" maxLength={30} value={row.limit} change={value=>form.replace(updateCommercialRow(form.proposal,'dependencies',row.id,'limit',value))}/>
   </div><button className="button" type="button" onClick={()=>form.replace(removeCommercialRow(form.proposal,'dependencies',row.id))}>Remove dependency {index+1}</button></fieldset>)}
   <button className="button" type="button" disabled={dependencies.length>=50} onClick={()=>form.replace(addCommercialRow(form.proposal,'dependencies'))}>Add dependency</button></>}
   </fieldset>
  </>:<>
   <CommercialSections proposal={form.proposal} catalogue={form.catalogue}/>
   <fieldset className="quote-reference-fields"><legend>Contract works</legend><div className="quote-form-grid"><label>Contract works required<select aria-label="Contract works required" value={contractWorksSelected===true?'yes':contractWorksSelected===false?'no':''} onChange={event=>set('cover.contractWorks.selected',event.target.value===''?undefined:event.target.value==='yes')}><option value="">Not answered</option><option value="yes">Yes</option><option value="no">No</option></select></label>
    {(contractWorksSelected===true||retainedWorks)&&<>{field('cover.contractWorks.sumInsured','Contract works sum insured','money',30)}{field('cover.contractWorks.excess','Contract works excess','money',30)}</>}
   </div>{contractWorksSelected!==true&&retainedWorks&&<button className="button" type="button" onClick={()=>{if(window.confirm('Clear retained contract works amounts?'))clearSectionInputs(form,'contract-works');}}>Clear retained contract works details</button>}</fieldset>
   <fieldset className="quote-reference-fields"><legend>Excesses and declarations</legend><CommercialQuestionFields form={form} questions={form.catalogue.questions.filter(q=>q.stage===10)}/><div className="quote-form-grid">{field('risk.materialFacts','Other material facts','textarea',10000)}</div></fieldset>
   <p>Evidence, rating and terms use the saved proposal and are separate underwriting steps. Capture answers do not confirm receipt or acceptance of an enclosure.</p>
  </>;
}
function CommercialSections({proposal,catalogue}:{proposal:CommercialProposal;catalogue:CommercialCatalogue}){
 const answer=(id:string)=>{const q=catalogue.questions.find(x=>x.id===id);return q?commercialResponse(proposal,q):undefined;};
 const label=(value:QuoteValue|undefined)=>value&&typeof value==='object'&&!Array.isArray(value)?String(value.label??'Not answered'):value===undefined?'Not answered':String(value);
 const yes=(value:QuoteValue|undefined)=>value===true?'Yes':value===false?'No':'Not answered';
 const positive=(value:QuoteValue|undefined)=>typeof value==='string'?value==='0.00'?'No':'Yes':'Not answered';
 const amount=(path:string)=>commercialField(proposal,path);
 const property=sumCommercialMoney(commercialRows(proposal,'locations').flatMap(row=>[row.buildings,row.contents,row.stock]));
 const rows:[string,string,string,string][]=[
  ['Property damage',positive(property),commercialMoney(property),label(answer('prototype.quote.17b8e4af6aef'))],
  ['Business interruption',yes(answer('prototype.quote.7660fc5eb42e')),commercialMoney(amount('risk.businessInterruption.sumInsured')),label(answer('prototype.quote.7bd824326d4c'))],
  ['Employers’ liability',yes(answer('prototype.quote.36ef01068295')),commercialMoney(amount('risk.liability.employersLimit')),'Assessed in underwriting'],
  ['Public liability',positive(amount('risk.liability.publicLimit')),commercialMoney(amount('risk.liability.publicLimit')),'Assessed in underwriting'],
  ['Products liability',positive(amount('risk.liability.productsLimit')),commercialMoney(amount('risk.liability.productsLimit')),'Assessed in underwriting'],
  ['Goods in transit','See selected option',label(answer('prototype.quote.480819d83a08')),'Assessed in underwriting'],
  ['Money','See selected option',label(answer('prototype.quote.c8c59a389166')),'Assessed in underwriting'],
  ['Glass','See selected option',label(answer('prototype.quote.1638a58efb48')),'Assessed in underwriting'],
  ['Contract works',yes(amount('cover.contractWorks.selected')),commercialMoney(amount('cover.contractWorks.sumInsured')),commercialMoney(amount('cover.contractWorks.excess'))]
 ];
 return <div className="table-scroll" role="region" aria-label="Captured commercial cover amounts" tabIndex={0}><table><thead><tr><th>Section</th><th>Required</th><th>Captured amount or option</th><th>Captured excess</th></tr></thead><tbody>{rows.map(([section,required,value,excess])=><tr key={section}><th>{section}</th><td>{required}</td><td className="num">{value}</td><td>{excess}</td></tr>)}</tbody></table><p>Captured amounts may include retained details for deselected sections. Review saved readiness before requesting a rating.</p></div>;
}
