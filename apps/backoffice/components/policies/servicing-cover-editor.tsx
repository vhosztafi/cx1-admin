'use client';
import { useEffect, useRef, useState } from 'react';
import type { QuoteFormCatalogue } from '../../lib/quote-catalogue';
import type { QuoteObject, QuoteProposal } from '../../lib/quotes';
import type { ServicingChange, ServicingEditor, ServicingEffectiveIntent, ServicingProposal } from '../../lib/servicing-api';
import { projectServicingCaptureAt, putServicingChange, sameServicingId } from '../../lib/servicing-change-form';
import { requestedCoverCodes, requestedCoverLabels, requestedCoverRows, type RequestedCoverCode } from '../../lib/requested-cover';
import { RequestedCover } from '../quotes/requested-cover';
import { QuoteRepeatedSection, QuoteSectionControls } from '../quotes/quote-section-controls';
type Selection = 'all' | RequestedCoverCode;
type Session = { capture: QuoteProposal; changeId: string; prior?: ServicingChange; selection: Selection; intent: ServicingEffectiveIntent };
export function ServicingCoverEditor({ proposal, editor, policyId, catalogue, disabled, change }: {
 proposal: ServicingProposal; editor: ServicingEditor; policyId: string; catalogue: QuoteFormCatalogue; disabled: boolean; change: (proposal: ServicingProposal) => void;
}) {
 const [selection,setSelection]=useState<Selection>('all'),[session,setSession]=useState<Session|null>(null),[error,setError]=useState('');
 const [buffers,setBuffers]=useState<Record<string,string>>({}),[invalid,setInvalid]=useState<Record<string,string>>({});
 const dialog=useRef<HTMLDialogElement>(null),heading=useRef<HTMLHeadingElement>(null),trigger=useRef<HTMLButtonElement>(null),resume=useRef<HTMLButtonElement>(null);
 const hasSession=session!==null;
 useEffect(()=>{if(hasSession){dialog.current?.showModal();heading.current?.focus();}},[hasSession]);
 function close(){dialog.current?.close();setSession(null);requestAnimationFrame(()=>trigger.current?.focus());}
 function keep(){dialog.current?.close();resume.current?.focus();}
 function begin(){
  if(disabled)return;
  try{
   const baseRow=requestedCoverRows(editor.assessment.base).find(row=>row.code===selection);
   const prior=proposal.changes.find(item=>item.kind==='cover'&&(selection==='all'?sameServicingId(item.riskItemId,policyId):!sameServicingId(item.riskItemId,policyId)&&((baseRow&&sameServicingId(item.riskItemId,String(baseRow.id)))||((item.payload?.requestedSections??[]) as QuoteObject[]).some(row=>row.code===selection))));
   const intent=prior?.effectiveIntent??proposal.commonEffectiveIntent;
   const capture=projectServicingCaptureAt(editor.assessment.base,proposal,policyId,editor.clientId,intent);
   setSession({capture,changeId:prior?.changeId??crypto.randomUUID(),prior,selection,intent:structuredClone(intent)});setBuffers({});setInvalid({});setError('');
  }catch(failure){setError(failure instanceof Error?failure.message:'The cover proposal could not be prepared.');}
 }
 function apply(){
  if(!session||disabled||Object.keys(invalid).length)return;
  try{
   const prior=proposal.changes.find(item=>item.changeId===session.changeId);
   if(JSON.stringify(prior?.effectiveIntent??proposal.commonEffectiveIntent)!==JSON.stringify(session.intent))throw new Error('The effective date changed while this form was open. Restore that date or reopen the form before applying.');
   let item:ServicingChange;
   if(session.selection==='all')item={changeId:session.changeId,riskItemId:policyId,kind:'cover',operation:'update',payloadMode:'replace',payload:structuredClone(session.capture.cover??{})};
   else{
    const row=requestedCoverRows(session.capture).find(value=>value.code===session.selection);
    if(!row)throw new Error('Record the section choice before applying.');
    const earlier=projectServicingCaptureAt(editor.assessment.base,{...proposal,changes:proposal.changes.filter(value=>value.changeId!==session.changeId)},policyId,editor.clientId,session.intent);
    const exists=requestedCoverRows(earlier).some(value=>sameServicingId(value.id,String(row.id)));
    item={changeId:session.changeId,riskItemId:String(row.id),kind:'cover',operation:exists?'update':'add',payload:{requestedSections:[structuredClone(row)]},...(exists?{payloadMode:'replace' as const}:{})};
   }
   if(prior?.effectiveIntent)item.effectiveIntent=structuredClone(prior.effectiveIntent);
   change(putServicingChange(proposal,item));close();
  }catch(failure){setError(failure instanceof Error?failure.message:'The cover change could not be prepared.');}
 }
 const controls=session?{proposal:session.capture,versions:editor.captureVersions,catalogue,replace:(capture:QuoteProposal)=>setSession(current=>current?{...current,capture}:null),buffers,
  setBuffer:(key:string,value:string|undefined)=>setBuffers(current=>{const next={...current};if(value===undefined)delete next[key];else next[key]=value;return next;}),
  validity:(key:string,value?:string)=>setInvalid(current=>{const next={...current};if(value)next[key]=value;else delete next[key];return next;})}:null;
 return <section aria-label="Cover changes"><h3>Cover, limits and excesses</h3><div className="operations-actions"><label>Cover to change<select aria-label="Cover to change" disabled={disabled||hasSession} value={selection} onChange={event=>setSelection(event.target.value as Selection)}><option value="all">All cover details and European trips</option>{requestedCoverCodes(editor.assessment.base).map(code=><option key={code} value={code}>{requestedCoverLabels[code]}</option>)}</select></label><button ref={trigger} className="button" type="button" disabled={disabled||hasSession} onClick={begin}>Amend cover</button>{hasSession&&<button ref={resume} className="button" type="button" onClick={()=>{dialog.current?.showModal();heading.current?.focus();}}>Resume cover form</button>}</div>
 {!hasSession&&error&&<p role="alert">{error}</p>}<dialog className="agency-dialog agency-terms-dialog servicing-change-dialog" ref={dialog} aria-labelledby="servicing-cover-title" onCancel={event=>{event.preventDefault();keep();}}>
 {session&&controls&&<form noValidate onSubmit={event=>{event.preventDefault();apply();}}><h2 id="servicing-cover-title" tabIndex={-1} ref={heading}>Amend cover</h2><p>Editing {session.selection==='all'?'all cover details':requestedCoverLabels[session.selection]} at {session.intent.localDate} {session.intent.localTime} Europe/London. Later changes remain separate. Apply, then save the draft; issued cover remains unchanged.</p><p>To assign a later date, apply the change, choose individual cover dates in the draft and edit its date. Reopening this form retains that date.</p>
 {disabled&&<p role="alert">Editing ownership changed. These form values are retained; reacquire the draft before applying them.</p>}
 <fieldset disabled={disabled}><RequestedCover {...controls} onlyCode={session.selection==='all'?undefined:session.selection}/>{session.selection==='all'&&<><QuoteSectionControls {...controls} group="cover" prefix="Cover"/><QuoteRepeatedSection {...controls} group="annualEuropeanCover" singular="Annual European vehicle" title="Annual European cover"/><QuoteRepeatedSection {...controls} group="temporaryEuropeanCover" singular="European trip" title="Temporary European cover"/></>}</fieldset>
 {Object.values(invalid).map((message,index)=><p key={index} role="alert">{message}</p>)}{error&&<p role="alert">{error}</p>}<div className="operations-actions"><button className="button button-primary" disabled={disabled||Object.keys(invalid).length>0}>Apply to draft</button><button className="button" type="button" onClick={keep}>Keep form and return</button><button className="button" type="button" onClick={close}>Discard form</button></div></form>}
 </dialog></section>;
}
