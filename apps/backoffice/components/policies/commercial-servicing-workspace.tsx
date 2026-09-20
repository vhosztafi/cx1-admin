'use client';
import Link from 'next/link';
import {useEffect,useRef,useState} from 'react';
import type {CommercialCatalogue} from '../../lib/commercial-capture';
import {removeCommercialChange,type CommercialServicingDraft,type CommercialServicingEditor,type CommercialServicingProposal} from '../../lib/commercial-servicing';
import {quoteFetch,uncertainQuoteFailure} from '../../lib/quotes';
import {matchingServicingEditor} from '../../lib/servicing-proposal';
import {sendServicing,sendServicingRating,servicingCommand,type ServicingCommand} from '../../lib/servicing-api';
import {Panel,Status} from '../primitives';
import {CommercialChangeEditors} from './commercial-change-editors';
import {ServicingEffectiveFields} from './servicing-effective-fields';
import {ServicingSavedReview} from './servicing-saved-review';
import {ServicingRating} from './servicingrating';
import {ServicingEvidence} from './servicing-evidence';
import {CommercialDraftExposurePanel} from './commercial-exposure-panel';

type View={data:CommercialServicingDraft;etag:string};
export function CommercialServicingWorkspace({draftId,actorId,canTakeover,catalogue,initial}:{draftId:string;actorId:string;canTakeover:boolean;catalogue:CommercialCatalogue;initial:View}) {
 const [view,setView]=useState(initial),[proposal,setProposal]=useState(initial.data.proposal);
 const [editor,setEditor]=useState<{data:CommercialServicingEditor;etag:string|null}|null>(null),[editorOk,setEditorOk]=useState(false);
 const [fence,setFence]=useState<string|null>(null),[observedAt,setObservedAt]=useState(0),[busy,setBusy]=useState(false),[dirty,setDirty]=useState(false),[invalid,setInvalid]=useState(false);
 const [uncertain,setUncertain]=useState(false),[error,setError]=useState(''),[notice,setNotice]=useState(''),[reason,setReason]=useState(''),[confirmAbandon,setConfirmAbandon]=useState(false),[epoch,setEpoch]=useState(0);
 const [localRevision,setLocalRevision]=useState<string|null>(null);
 const readGeneration=useRef(0);
 const [proofPending,setProofPending]=useState(false),proofPendingRef=useRef(false);
 const dirtyRef=useRef(false),busyRef=useRef(false),pending=useRef<ServicingCommand|null>(null);const url=`/api/v1/drafts/${draftId}`;
 useEffect(()=>{
  const controller=new AbortController();let loading=false;
  async function refresh() {
   setObservedAt(Date.now());if(loading||busyRef.current||proofPendingRef.current)return;const generation=readGeneration.current;loading=true;
   try {
   const [draftRead,editorRead]=await Promise.allSettled([quoteFetch<CommercialServicingDraft>(url,{signal:controller.signal}),quoteFetch<CommercialServicingEditor>(url+'/editor',{signal:controller.signal})]);
   if(controller.signal.aborted||busyRef.current||proofPendingRef.current||generation!==readGeneration.current)return;
   if(draftRead.status==='fulfilled'&&draftRead.value.etag){setView({data:draftRead.value.data,etag:draftRead.value.etag});if(!dirtyRef.current)setProposal(draftRead.value.data.proposal);}
   else {setEditorOk(false);setError('Unable to refresh current access. Local edits are retained.');return;}
   setEditorOk(editorRead.status==='fulfilled');if(editorRead.status==='fulfilled')setEditor(editorRead.value);
   } finally {loading=false;}
  }
  void refresh();const timer=setInterval(()=>void refresh(),5000);return()=>{controller.abort();clearInterval(timer);};
 },[url]);
 useEffect(()=>{const warn=(event:BeforeUnloadEvent)=>{if(dirtyRef.current||busyRef.current||pending.current){event.preventDefault();event.returnValue='';}};const navigate=(event:MouseEvent)=>{const anchor=(event.target as Element).closest('a[href]');if(anchor&&(dirtyRef.current||busyRef.current||pending.current)&&!window.confirm('Leave this draft and discard unsaved local changes?')){event.preventDefault();event.stopPropagation();}};window.addEventListener('beforeunload',warn);document.addEventListener('click',navigate,true);return()=>{window.removeEventListener('beforeunload',warn);document.removeEventListener('click',navigate,true);};},[]);
 const lease=view.data.lease,editing=!!(view.data.state==='draft'&&fence&&lease?.active&&lease.holderId===actorId&&lease.leaseToken===fence&&Date.parse(lease.expiresAt)>observedAt);
 const otherEditor=!!(lease?.active&&lease.holderId!==actorId&&Date.parse(lease.expiresAt)>observedAt),matched=editorOk&&matchingServicingEditor(view,editor);
 function change(value:CommercialServicingProposal){setLocalRevision(before=>before??view.data.revisionId);dirtyRef.current=true;setDirty(true);setProposal(value);}
 function editedBuffer(){setLocalRevision(before=>before??view.data.revisionId);dirtyRef.current=true;setDirty(true);}
 function attempt(action:()=>CommercialServicingProposal){try{change(action());setError('');}catch(failure){setError(failure instanceof Error?failure.message:'Unable to apply this local change.');}}
 async function run(action:'acquire'|'takeover'|'renew'|'release'|'save'|'abandon'|'rate') {
  const recovering=uncertain;
  if(busyRef.current||proofPendingRef.current||(action==='save'&&invalid&&!pending.current))return;readGeneration.current++;busyRef.current=true;setBusy(true);setError('');setNotice('');
  try {
   if(!pending.current){const path=action==='rate'?'/rate':action==='save'?'/proposal':action==='abandon'?'/abandon':'/lease';const method=action==='release'?'DELETE':action==='renew'||action==='save'?'PUT':'POST';
    const body=action==='rate'?{revisionId:view.data.revisionId,reason:proposal.reason}:action==='save'?proposal:action==='abandon'?{reason}:action==='acquire'?{mode:'acquire'}:action==='takeover'?{mode:'takeover',reason}:undefined;
    pending.current=servicingCommand(url+path,method,view.etag,body,fence??undefined);}
   const sent=pending.current;
   if(sent.url.endsWith('/rate')){await sendServicingRating(sent);await proofSaved();pending.current=null;setUncertain(false);setNotice('Rating requested. Results will appear when processing finishes.');return;}
   const result=await sendServicing<CommercialServicingProposal>(sent);setView(result);
   if(sent.url.endsWith('/lease')&&sent.method==='POST')setFence(result.data.lease?.leaseToken??null);
   if(sent.method==='DELETE'||result.data.state!=='draft')setFence(null);
   if(sent.url.endsWith('/proposal')){setLocalRevision(null);dirtyRef.current=false;setDirty(false);setInvalid(false);setProposal(result.data.proposal);setEpoch(x=>x+1);}
   pending.current=null;setUncertain(false);setEditorOk(false);setNotice('Draft action saved. Issued cover is unchanged.');
   const read=await quoteFetch<CommercialServicingEditor>(url+'/editor').catch(()=>null);if(read){setEditor(read);setEditorOk(true);}
  }catch(failure){const retry=recovering||uncertainQuoteFailure(failure);setUncertain(retry);if(!retry){pending.current=null;setFence(null);}
   setError(retry?'The result is unconfirmed. Restore current access if needed, then retry the same action before continuing.':'This action was not applied. Local edits are retained; refresh ownership and acquire the current editing lease.');
  }finally{busyRef.current=false;setBusy(false);}
 }
 function proofPendingChanged(value:boolean){readGeneration.current++;proofPendingRef.current=value;setProofPending(value);}
 async function proofSaved(){
  const [draftRead,editorRead]=await Promise.all([quoteFetch<CommercialServicingDraft>(url),quoteFetch<CommercialServicingEditor>(url+'/editor')]);
  if(!draftRead.etag||draftRead.data.id!==draftId||!matchingServicingEditor({data:draftRead.data,etag:draftRead.etag},editorRead))throw new Error('Saved draft readback is unconfirmed.');
  readGeneration.current++;setView({data:draftRead.data,etag:draftRead.etag});setEditor(editorRead);setEditorOk(true);if(!dirtyRef.current)setProposal(draftRead.data.proposal);
 }
 const conflict=dirty&&localRevision!==view.data.revisionId;
 const disabled=!editing||busy||uncertain||proofPending||!matched||conflict,labels=Object.fromEntries(catalogue.questions.map(x=>[x.id,x.label]));
 return <><div className="page-heading"><div><h1>Commercial policy adjustment</h1><p>{view.data.state==='issued'?'Issued adjustment · effective changes saved':'Saved proposal · issued cover and exposure stay unchanged'}</p></div><Link className="button" href={`/policies/${view.data.policyId}`}>Back to policy</Link></div>
 <section className="quote-saved-banner" aria-label="Editing lease"><div><h2>{editing?'You are editing this draft':'Read-only draft'}</h2><p>{view.data.context?.policyReference} · {view.data.state!=='draft'?'This draft is closed; its saved history remains available.':otherEditor?'Another editor holds the lease.':editing?'Renew the lease to keep editing.':'Acquire an editing lease to make changes.'}</p></div><Status tone={editing?'success':'info'}>{dirty?'Local changes retained':view.data.state==='draft'?'Saved draft':view.data.state}</Status></section>
 {conflict?<p role="alert">Another revision was saved while you were editing. Your local edits are retained. Review the saved comparison, then discard local changes to load that revision before editing again.</p>:null}
 {error?<p role="alert">{error}</p>:null}{notice?<p role="status">{notice}</p>:null}
 <div className="underwriting-layout"><div className="underwriting-main"><Panel title="Requested change"><form className="quote-rail-body" onSubmit={event=>{event.preventDefault();void run('save');}}><fieldset disabled={disabled}><div className="quote-form-grid">
 <label>Reason for change<textarea aria-label="Reason for change" required minLength={10} maxLength={2000} value={proposal.reason} onChange={event=>change({...proposal,reason:event.target.value})}/></label>
 <label>Requested by<select aria-label="Requested by" value={proposal.requestedBy.kind} onChange={event=>{const kind=event.target.value as CommercialServicingProposal['requestedBy']['kind'];change({...proposal,requestedBy:kind==='internal'?{kind}:{kind,name:proposal.requestedBy.name??''}});}}><option value="internal">Internal request</option><option value="insured">Insured</option><option value="broker">Broker</option></select></label>
 {proposal.requestedBy.kind!=='internal'?<label>Requester name<input aria-label="Requester name" required maxLength={200} value={proposal.requestedBy.name??''} onChange={event=>change({...proposal,requestedBy:{...proposal.requestedBy,name:event.target.value}})}/></label>:null}
 <ServicingEffectiveFields intent={proposal.commonEffectiveIntent} change={intent=>change({...proposal,commonEffectiveIntent:intent})}/>
 <label>Effective date basis<select aria-label="Effective date basis" value={proposal.dateBasis??'shared'} onChange={event=>attempt(()=>{const dateBasis=event.target.value as 'shared'|'per-cover-change';if(dateBasis==='shared'&&proposal.changes.some(x=>x.effectiveIntent))throw new Error('Remove individual cover dates before choosing a shared date.');return {...proposal,dateBasis};})}><option value="shared">One shared date</option><option value="per-cover-change">Individual dates for cover changes</option></select></label></div>
 <p>Dates must be within the term and no earlier than the latest issued change. Backdating requires current senior authority.</p>
 {proposal.changes.map((item,index)=><fieldset className="quote-reference-fields" key={item.changeId}><legend>Change {index+1} · {item.operation} {item.kind.replace('commercial-','')}</legend>
 {item.kind==='commercial-cover'&&proposal.dateBasis==='per-cover-change'?<><label><input type="checkbox" checked={!!item.effectiveIntent} onChange={event=>change({...proposal,changes:proposal.changes.map(x=>{if(x.changeId!==item.changeId)return x;const next={...x};if(event.target.checked)next.effectiveIntent=proposal.commonEffectiveIntent;else delete next.effectiveIntent;return next;})})}/>Use an individual cover date</label>
 {item.effectiveIntent?<ServicingEffectiveFields prefix={`Cover change ${index+1}`} intent={item.effectiveIntent} change={intent=>change({...proposal,changes:proposal.changes.map(x=>x.changeId===item.changeId?{...x,effectiveIntent:intent}:x)})}/>:null}</>:null}
 <button className="button" type="button" onClick={()=>{attempt(()=>removeCommercialChange(proposal,item.changeId));setInvalid(false);setEpoch(x=>x+1);}}>Remove proposed change {index+1}</button></fieldset>)}
 </fieldset><p>{proposal.changes.length} proposed risk changes{dirty?' including local edits':' saved'}.</p><button className="button button-primary" type="submit" disabled={disabled||invalid}>Save draft</button></form></Panel>
 {editor?.data.draftId===draftId?<CommercialChangeEditors key={`${view.data.revisionId}:${epoch}`} proposal={proposal} editor={editor.data} policyId={view.data.policyId} catalogue={catalogue} disabled={disabled} change={change} bufferChanged={editedBuffer} invalidChanged={setInvalid}/>:<Panel title="Commercial risk changes"><p className="quote-rail-body">Waiting for the retained commercial base and current revision.</p></Panel>}
 <ServicingSavedReview editor={matched?editor!.data:null} dirty={dirty} questionLabels={labels}/>
 <CommercialDraftExposurePanel key={`${view.data.revisionId}:${view.data.state}`} draftId={draftId} revisionId={view.data.revisionId} dirty={dirty}/>
 <ServicingRating kind="adjustment" draftId={draftId} draftEtag={view.etag} baseTermPremium={view.data.context?.baseTermPremium} dirty={dirty} busy={busy||uncertain||proofPending} canRate={!disabled&&!dirty&&!invalid&&proposal.changes.length>0&&editor!.data.assessment.readinessIssues.length===0} rate={()=>void run('rate')}/>
 <ServicingEvidence draftId={draftId} revisionId={view.data.revisionId} etag={view.etag} fence={fence} editable={editing} blocked={busy||uncertain||proofPending} dirty={dirty} canReview={canTakeover} editor={matched?editor!.data:null} pendingChanged={proofPendingChanged} saved={proofSaved}/>
 </div><aside className="underwriting-rail" aria-label="Draft actions"><Panel title="Editing controls"><div className="quote-rail-body servicing-controls">
 {uncertain?<button className="button button-primary" disabled={busy} onClick={()=>void run('save')}>Retry same action</button>:null}
 <button className="button" disabled={busy||uncertain||proofPending||editing||otherEditor||view.data.state!=='draft'} onClick={()=>void run('acquire')}>Acquire editing lease</button>
 <button className="button" disabled={busy||uncertain||proofPending||!editing} onClick={()=>void run('renew')}>Renew editing lease</button><button className="button" disabled={busy||uncertain||proofPending||!editing} onClick={()=>void run('release')}>Release editing lease</button>
 <button className="button" disabled={busy||uncertain||proofPending||!dirty} onClick={()=>{if(window.confirm('Discard local changes and use the currently saved draft?')){setLocalRevision(null);dirtyRef.current=false;setDirty(false);setInvalid(false);setProposal(view.data.proposal);setEpoch(x=>x+1);}}}>Discard local changes</button>
 <label>Takeover or abandonment reason<textarea aria-label="Takeover or abandonment reason" minLength={10} maxLength={2000} value={reason} onChange={event=>setReason(event.target.value)}/></label>
 {canTakeover?<button className="button" disabled={busy||uncertain||proofPending||!otherEditor||reason.trim().length<10} onClick={()=>void run('takeover')}>Take over editing</button>:null}
 <label><input type="checkbox" checked={confirmAbandon} onChange={event=>setConfirmAbandon(event.target.checked)}/>I confirm this draft should be abandoned.</label><button className="button" disabled={busy||uncertain||proofPending||!editing||!confirmAbandon||reason.trim().length<10} onClick={()=>void run('abandon')}>Abandon draft</button>
 <p>Takeover prevents the previous editor from saving. Abandonment retains saved proposal history.</p></div></Panel>
 <Panel title="Saved source"><div className="quote-rail-body"><p>Last saved {new Date(view.data.updatedAt).toLocaleString('en-GB',{timeZone:'Europe/London'})} · London</p><details><summary>Draft provenance</summary><p>Base version: {view.data.baseVersionId}</p><p>Revision: {view.data.revisionId}</p></details></div></Panel></aside></div></>;
}
