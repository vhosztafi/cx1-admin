'use client';
import { useEffect, useRef, useState } from 'react';
import type { QuoteFormCatalogue } from '../../lib/quote-catalogue';
import type { QuoteObject, QuoteProposal } from '../../lib/quotes';
import type { ServicingEditor, ServicingProposal } from '../../lib/servicing-api';
import { projectServicingCapture, putServicingChange } from '../../lib/servicing-change-form';
import { changeField, fieldValue } from '../../lib/quote-form';
import { QuoteProposerReferences } from '../quotes/quote-proposer-references';
const identityFields = [['firstName','First name',100],['surname','Surname',100],['legalName','Company or partnership name',50],['tradingName','Trading name',200],['companyNumber','Company number',30],['contact.email','Email address',254],['contact.telephone','Telephone',11],['contact.mobile','Mobile',11],['address.houseNumber','House number or name',50],['address.street','Street',50],['address.town','Town',50],['address.city','City',50],['address.county','County',50],['address.postcode','Postcode',10]] as const;
export function ServicingPolicyholderEditor({ proposal, editor, policyId, catalogue, disabled, change }: {
 proposal: ServicingProposal; editor: ServicingEditor; policyId: string; catalogue: QuoteFormCatalogue; disabled: boolean; change: (proposal: ServicingProposal) => void;
}) {
 const [capture,setCapture]=useState<QuoteProposal|null>(null),[changeId,setChangeId]=useState(''),[error,setError]=useState('');
 const [buffers,setBuffers]=useState<Record<string,string>>({}),[invalid,setInvalid]=useState<Record<string,string>>({});
 const dialog=useRef<HTMLDialogElement>(null),heading=useRef<HTMLHeadingElement>(null),trigger=useRef<HTMLButtonElement>(null),resume=useRef<HTMLButtonElement>(null);
 const hasSession=capture!==null;
 useEffect(()=>{if(hasSession){dialog.current?.showModal();heading.current?.focus();}},[hasSession]);
 function close(){dialog.current?.close();setCapture(null);requestAnimationFrame(()=>trigger.current?.focus());}
 function keep(){dialog.current?.close();resume.current?.focus();}
 function begin(){
  if(disabled)return;
  try{setCapture(projectServicingCapture(editor.assessment.base,proposal,policyId,editor.clientId));setChangeId(proposal.changes.find(item=>item.kind==='policyholder')?.changeId??crypto.randomUUID());setBuffers({});setInvalid({});setError('');}
  catch(failure){setError(failure instanceof Error?failure.message:'The proposal could not be prepared.');}
 }
 function apply(){
  if(!capture||disabled||Object.keys(invalid).length)return;
  try{change(putServicingChange(proposal,{changeId,riskItemId:editor.clientId,kind:'policyholder',operation:'update',payloadMode:'replace',payload:structuredClone((capture.insured??{}) as QuoteObject)}));close();}
  catch(failure){setError(failure instanceof Error?failure.message:'The change could not be prepared.');}
 }
 const controls=capture?{proposal:capture,versions:editor.captureVersions,catalogue,replace:setCapture,buffers,
  setBuffer:(key:string,value:string|undefined)=>setBuffers(current=>{const next={...current};if(value===undefined)delete next[key];else next[key]=value;return next;}),
  validity:(key:string,value?:string)=>setInvalid(current=>{const next={...current};if(value)next[key]=value;else delete next[key];return next;})}:null;
 return <section aria-label="Policyholder corrections"><h3>Policyholder</h3><div className="operations-actions"><button ref={trigger} className="button" type="button" disabled={disabled||hasSession} onClick={begin}>Correct policyholder details</button>{hasSession&&<button ref={resume} className="button" type="button" onClick={()=>{dialog.current?.showModal();heading.current?.focus();}}>Resume policyholder form</button>}</div>
 {!hasSession&&error&&<p role="alert">{error}</p>}
 <dialog className="agency-dialog agency-terms-dialog servicing-change-dialog" ref={dialog} aria-labelledby="servicing-policyholder-title" onCancel={event=>{event.preventDefault();keep();}}>
 {capture&&controls&&<form noValidate onSubmit={event=>{event.preventDefault();apply();}}><h2 id="servicing-policyholder-title" tabIndex={-1} ref={heading}>Correct policyholder details</h2><p>Apply these details, then save the draft. Issued cover remains unchanged. Incomplete declarations can be saved for review.</p>
 {disabled&&<p role="alert">Editing ownership changed. These form values are retained; reacquire the draft before applying them.</p>}
 <fieldset disabled={disabled}><p>These are contractual policyholder details for this policy. Client ownership and agency relationships cannot be transferred here.</p><div className="quote-form-grid"><label>Legal entity<select aria-label="Legal entity" value={String(capture.insured?.entityType??'')} onChange={event=>setCapture(changeField(capture,'insured.entityType',event.target.value||undefined))}><option value="">Not answered</option><option value="sole-trader">Sole trader</option><option value="partnership">Partnership</option><option value="limited-company">Limited company</option><option value="llp">Limited liability partnership</option></select></label>
 {identityFields.map(([path,label,max])=><label key={path}>{label}<input aria-label={label} maxLength={max} value={String(fieldValue(capture,`insured.${path}`)??'')} onChange={event=>setCapture(changeField(capture,`insured.${path}`,event.target.value||undefined))}/></label>)}</div>
 <h3>Full proposer names</h3><div className="quote-form-grid">{[0,1,2].map(index=>{const names=(capture.insured?.proposerNames??[]) as string[];return <label key={index}>Proposer {index+1} full name<input aria-label={`Proposer ${index+1} full name`} maxLength={200} value={names[index]??''} onChange={event=>{const next=Array.from({length:3},(_,position)=>names[position]??'');next[index]=event.target.value;while(next.length&&next.at(-1)==='')next.pop();setCapture(changeField(capture,'insured.proposerNames',next));controls.validity('names',next.some(name=>!name)?'Remove empty proposer slots or enter each full name.':undefined);}}/></label>;})}</div>
 <button className="button" type="button" onClick={()=>{setCapture(changeField(capture,'insured.proposerNames',((capture.insured?.proposerNames??[]) as string[]).filter(Boolean)));controls.validity('names');}}>Remove empty proposer slots</button>
 <QuoteProposerReferences proposal={capture} versions={editor.captureVersions} catalogue={catalogue} replace={setCapture} change={(path,value)=>setCapture(changeField(capture,path,value))}/></fieldset>
 {Object.values(invalid).map((message,index)=><p key={index} role="alert">{message}</p>)}{error&&<p role="alert">{error}</p>}
 <div className="operations-actions"><button className="button button-primary" disabled={disabled||Object.keys(invalid).length>0}>Apply to draft</button><button className="button" type="button" onClick={keep}>Keep form and return</button><button className="button" type="button" onClick={close}>Discard form</button></div></form>}
 </dialog></section>;
}
