'use client';
import { useEffect, useRef, useState } from 'react';
import type { QuoteFormCatalogue } from '../../lib/quote-catalogue';
import type { QuoteObject, QuoteProposal } from '../../lib/quotes';
import type { ServicingEditor, ServicingProposal } from '../../lib/servicing-api';
import { projectServicingCapture, putServicingChange } from '../../lib/servicing-change-form';
import { changeField, fieldValue } from '../../lib/quote-form';
import { QuoteActivities } from '../quotes/quote-activities';
import { QuoteSourceBusiness } from '../quotes/quote-source-business';
import { QuoteBusinessAnswers } from '../quotes/quote-business-answers';
import { QuoteCaptureControl } from '../quotes/quote-vehicles';
const splits = [['sales','Vehicle sales'],['servicing','Servicing'],['mechanicalRepair','Mechanical repair'],['breakdownRecovery','Breakdown and recovery'],['bodyRepairs','Body repairs'],['valeting','Valeting'],['other','Other activities']] as const;
export function ServicingBusinessEditor({ proposal, editor, policyId, catalogue, disabled, change }: {
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
  try{setCapture(projectServicingCapture(editor.assessment.base,proposal,policyId,editor.clientId));setChangeId(proposal.changes.find(item=>item.kind==='business')?.changeId??crypto.randomUUID());setBuffers({});setInvalid({});setError('');}
  catch(failure){setError(failure instanceof Error?failure.message:'The proposal could not be prepared.');}
 }
 function apply(){
  if(!capture||disabled||Object.keys(invalid).length)return;
  try{change(putServicingChange(proposal,{changeId,riskItemId:policyId,kind:'business',operation:'update',payloadMode:'replace',payload:structuredClone((capture.risk?.business??{}) as QuoteObject)}));close();}
  catch(failure){setError(failure instanceof Error?failure.message:'The change could not be prepared.');}
 }
 const controls=capture?{proposal:capture,versions:editor.captureVersions,catalogue,replace:setCapture,buffers,
  setBuffer:(key:string,value:string|undefined)=>setBuffers(current=>{const next={...current};if(value===undefined)delete next[key];else next[key]=value;return next;}),
  validity:(key:string,value?:string)=>setInvalid(current=>{const next={...current};if(value)next[key]=value;else delete next[key];return next;})}:null;
 return <section aria-label="Trade activity changes"><h3>Trade activities</h3><div className="operations-actions"><button ref={trigger} className="button" type="button" disabled={disabled||hasSession} onClick={begin}>Amend trade activities</button>{hasSession&&<button ref={resume} className="button" type="button" onClick={()=>{dialog.current?.showModal();heading.current?.focus();}}>Resume trade activity form</button>}</div>
 {!hasSession&&error&&<p role="alert">{error}</p>}
 <dialog className="agency-dialog agency-terms-dialog servicing-change-dialog" ref={dialog} aria-labelledby="servicing-business-title" onCancel={event=>{event.preventDefault();keep();}}>
 {capture&&controls&&<form noValidate onSubmit={event=>{event.preventDefault();apply();}}><h2 id="servicing-business-title" tabIndex={-1} ref={heading}>Amend trade activities</h2><p>Apply these details, then save the draft. Issued cover remains unchanged. Incomplete declarations can be saved for review.</p>
 {disabled&&<p role="alert">Editing ownership changed. These form values are retained; reacquire the draft before applying them.</p>}
 <fieldset disabled={disabled}><QuoteSourceBusiness {...controls} stage={1}/><div className="quote-form-grid">{(['turnover','wageRoll'] as const).map(name=><QuoteCaptureControl key={name} field={{id:name,path:name,label:name==='turnover'?'Annual turnover':'Annual wage roll',kind:'money',choices:[]}} label={name==='turnover'?'Annual turnover (GBP)':'Annual wage roll (GBP)'} value={fieldValue(capture,`risk.business.${name}`)} bufferKey={`business-${name}`} props={controls} change={value=>setCapture(changeField(capture,`risk.business.${name}`,value))}/>)}</div>
 <QuoteBusinessAnswers {...controls}/><h3>Activity split</h3><p>Enter percentages adding to 100%. Occupation shares are a separate declaration.</p><div className="quote-form-grid">{splits.map(([name,label])=><QuoteCaptureControl key={name} field={{id:name,path:name,label,kind:'percentage',choices:[]}} label={`${label} (%)`} value={fieldValue(capture,`risk.business.declaredActivitySplit.${name}`)} bufferKey={`split-${name}`} props={controls} change={value=>setCapture(changeField(capture,`risk.business.declaredActivitySplit.${name}`,value))}/>)}</div>
 <p role="status">Total activity split: {splits.reduce((sum,[name])=>sum+Number(fieldValue(capture,`risk.business.declaredActivitySplit.${name}`)??0),0)/100}%</p><QuoteSourceBusiness {...controls} stage={2}/><QuoteActivities {...controls}/></fieldset>
 {Object.values(invalid).map((message,index)=><p key={index} role="alert">{message}</p>)}{error&&<p role="alert">{error}</p>}
 <div className="operations-actions"><button className="button button-primary" disabled={disabled||Object.keys(invalid).length>0}>Apply to draft</button><button className="button" type="button" onClick={keep}>Keep form and return</button><button className="button" type="button" onClick={close}>Discard form</button></div></form>}
 </dialog></section>;
}
