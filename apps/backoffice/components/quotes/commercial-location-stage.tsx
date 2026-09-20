'use client';
import {useState} from 'react';
import {addCommercialRow, commercialRows, commercialResponse, removeCommercialRow, updateCommercialRow, sumCommercialMoney, type CommercialProposal} from '../../lib/commercial-capture';
import type {QuoteObject,QuoteValue} from '../../lib/quotes';
import {CommercialInput,CommercialQuestionFields,type CommercialFormProps} from './commercial-question-fields';
import {CommercialItemDialog} from './commercial-item-dialog';

export const commercialMoney = (value: QuoteValue | undefined) => typeof value === 'string' ? `£${value.replace(/\B(?=(\d{3})+(?!\d))/g, ',')}` : 'Not complete';
export function CommercialLocationStage({form,stage=4}: {form:CommercialFormProps;stage?:number}) {
  const [edit,setEdit]=useState<{id:string;proposal:CommercialProposal;isNew:boolean}>();const [error,setError]=useState('');
  const rows=commercialRows(form.proposal,'locations');
  const totals=rows.map(row=>sumCommercialMoney([row.buildings,row.contents,row.stock]));
  const total=sumCommercialMoney(rows.flatMap(row=>[row.buildings,row.contents,row.stock]));
  const largest=totals.length && totals.every(x=>x!==undefined) ? totals.reduce((a,b)=>BigInt(a!.replace('.',''))>BigInt(b!.replace('.',''))?a:b) : undefined;
  return <>
    <CommercialQuestionFields form={form} questions={form.catalogue.questions.filter(q=>q.stage===stage&&!q.container.includes('[]'))}/>
    <div className="quote-row-actions"><h3>{stage===5?'Construction and protections':stage===6?'Location flood details':'Property locations'}</h3><button className="button button-primary" type="button" disabled={rows.length>=100} onClick={()=>{const proposal=addCommercialRow(form.proposal,'locations');setEdit({id:commercialRows(proposal,'locations').at(-1)!.id,proposal,isNew:true});}}>Add location</button></div>
    <p>Construction, protection and declared flood details belong to each location. Edit a location to review its answers.</p>
    {stage===5&&rows.length>0&&<ConstructionSummary form={form}/>}
    {rows.length===0?<p className="empty-state">No locations added. Add a location to record property and construction details.</p>:<div className="table-scroll" role="region" aria-label="Commercial property locations" tabIndex={0}><table><thead><tr><th>Reference</th><th>Address</th><th>Use</th><th>Buildings</th><th>Contents</th><th>Stock</th><th>Total</th><th>Actions</th></tr></thead><tbody>{rows.map((row,index)=><tr key={row.id}><th>{String(row.reference??`Location ${index+1}`)}</th><td>{String((row.address as QuoteObject|undefined)?.line1??'Not entered')}<br/>{String((row.address as QuoteObject|undefined)?.postcode??'')}</td><td>{String(row.occupancy??'Not answered')}</td><td className="num">{commercialMoney(row.buildings)}</td><td className="num">{commercialMoney(row.contents)}</td><td className="num">{commercialMoney(row.stock)}</td><td className="num">{commercialMoney(totals[index])}</td><td><div className="quote-row-actions"><button className="button" type="button" onClick={()=>setEdit({id:row.id,proposal:structuredClone(form.proposal),isNew:false})}>Edit location {index+1}</button><button className="button" type="button" onClick={()=>{if(!window.confirm(`Remove location ${index+1} from this draft? Save the draft to record the removal.`))return;try{form.replace(removeCommercialRow(form.proposal,'locations',row.id));setError('');}catch(failure){setError(failure instanceof Error?failure.message:'Unable to remove location.');}}}>Remove location {index+1}</button></div></td></tr>)}</tbody></table></div>}
    <p>Total property sum insured: <strong>{commercialMoney(total)}</strong> · Demo MEL — largest complete location sum insured: <strong>{commercialMoney(largest)}</strong></p><p className="client-help">The demo MEL is separate from any supplied loss estimate. Book capacity is assessed later during underwriting.</p>{error&&<p role="alert">{error}</p>}
    {edit&&<CommercialItemDialog key={edit.id} title={edit.isNew?'Add location':'Edit location'} initial={edit.proposal} catalogue={form.catalogue} close={()=>setEdit(undefined)} apply={proposal=>{form.replace(proposal);setEdit(undefined);}}>{local=><LocationFields form={local} id={edit.id}/>}</CommercialItemDialog>}
  </>;
}
function ConstructionSummary({form}:{form:CommercialFormProps}){
 const groups=[{name:'Construction by location',ids:['year-built','storeys','wall-construction','roof-construction','flat-roof','composite-panels','timber-frame','heritage-listed','heating','basement']},{name:'Protections by location',ids:['intruder-alarm','police-response','fire-alarm']}];
 const rows=commercialRows(form.proposal,'locations');
 return <>{groups.map(group=>{const questions=group.ids.map(id=>form.catalogue.questions.find(q=>q.id===`prototype.addloc.${id}`)!);return <div key={group.name}><h3>{group.name}</h3><div className="table-scroll" role="region" aria-label={group.name} tabIndex={0}><table><thead><tr><th>Location</th>{questions.map(q=><th key={q.id}>{q.label}</th>)}</tr></thead><tbody>{rows.map((row,index)=><tr key={row.id}><th>{String(row.reference??`Location ${index+1}`)}</th>{questions.map(q=>{const answer=commercialResponse(form.proposal,q,row.id);const value=answer&&typeof answer==='object'&&!Array.isArray(answer)?answer.label:answer===true?'Yes':answer===false?'No':answer??'Not answered';return <td key={q.id}>{String(value)}</td>;})}</tr>)}</tbody></table></div></div>;})}</>;
}
function LocationFields({form,id}:{form:CommercialFormProps;id:string}){
 const row=commercialRows(form.proposal,'locations').find(x=>x.id===id)!;
 const change=(path:string,value:QuoteValue|undefined)=>form.replace(updateCommercialRow(form.proposal,'locations',id,path,value));
 const read=(path:string):QuoteValue|undefined=>path.split('.').reduce<QuoteValue|undefined>((value,key)=>value&&typeof value==='object'&&!Array.isArray(value)?value[key]:undefined,row);
 const field=(path:string,label:string,kind:'text'|'money'|'postcode'='text',maxLength=200)=><CommercialInput key={path} form={form} inputKey={`${id}:${path}`} label={label} value={read(path)} kind={kind} maxLength={maxLength} change={value=>change(path,value)}/>;
 return <><div className="quote-form-grid">
  {field('reference','Location reference','text',50)}{field('address.line1','Location address line 1')}{field('address.line2','Location address line 2')}{field('address.town','Location town','text',100)}{field('address.county','Location county','text',100)}{field('address.postcode','Location postcode','postcode',10)}
  <label>Location country<select aria-label="Location country" value={String(read('address.country')??'')} onChange={event=>change('address.country',event.target.value||undefined)}><option value="">Not answered</option><option value="GB">United Kingdom</option></select></label>
  <label>Location use<select aria-label="Location use" value={String(row.occupancy??'')} onChange={event=>change('occupancy',event.target.value||undefined)}><option value="">Not answered</option>{form.catalogue.occupancy.map(value=><option key={value}>{value}</option>)}</select></label>
  <label>Sprinklers present<select aria-label="Sprinklers present" value={row.sprinklers===true?'yes':row.sprinklers===false?'no':''} onChange={event=>change('sprinklers',event.target.value===''?undefined:event.target.value==='yes')}><option value="">Not answered</option><option value="yes">Yes</option><option value="no">No</option></select></label>
  <label>Declared flood zone<select aria-label="Declared flood zone" value={String(row.floodZone??'')} onChange={event=>change('floodZone',event.target.value||undefined)}><option value="">Not answered</option><option value="unknown">Unknown</option>{['1','2','3'].map(value=><option key={value} value={value}>Zone {value}</option>)}</select></label>
  {field('buildings','Buildings sum insured','money',30)}{field('contents','Contents sum insured','money',30)}{field('stock','Stock sum insured','money',30)}{field('maximumEstimatedLoss','Supplied maximum loss estimate','money',30)}
 </div><h3>Construction and protections for this location</h3><CommercialQuestionFields form={form} questions={form.catalogue.questions.filter(q=>q.container==='risk.locations[].responses')} itemId={id}/></>;
}
