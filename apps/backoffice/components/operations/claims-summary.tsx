'use client';
import { useEffect, useState } from 'react';
import type { OpsIncident, OpsClaimsHandoff, OpsClaimsRequest, OpsClaimsSummary, OpsClaimsAdministrators } from '../../../../contracts/generated/operations';
import { csrfToken, type Actor } from '../../lib/auth';
import { incidentFetch } from '../../lib/incidents-api';
import { claimsCommand, sendClaimsCommand, type ClaimsCommand, type ClaimsAction } from '../../lib/claims-api';
import { uncertainQuoteFailure } from '../../lib/quotes';
import { useQuoteResource, LoadFeedback } from '../quotes/shared';
import { Panel } from '../primitives';
import { IncidentFacts } from './incident-facts';
import { incidentLabel } from './incident-fields';
type Page<T>={items:T[];totalCount:number;nextCursor?:string};
export function ClaimsSummaryPanel({record,etag,disabled,updated,blocked,view}:{record:OpsIncident;etag:string;disabled:boolean;updated:(value:OpsIncident,etag:string)=>void;blocked:(value:boolean)=>void;view?:'detail'|'summary'}){
 const [tab,setTab]=useState<'detail'|'summary'>('detail'),[provider,setProvider]=useState(''),[body,setBody]=useState(''),[reason,setReason]=useState('');
 const [busy,setBusy]=useState(false),[pending,setPending]=useState<ClaimsCommand>(),[error,setError]=useState(''),[notice,setNotice]=useState('');
 const [summaryCursor,setSummaryCursor]=useState(''),[handoffCursor,setHandoffCursor]=useState(''),[requestCursor,setRequestCursor]=useState('');
 const base=`/api/v1/incidents/${record.id}`;
 const admins=useQuoteResource<OpsClaimsAdministrators>(`${base}/administrators`);
 const handoffs=useQuoteResource<Page<OpsClaimsHandoff>>(`${base}/handoffs?pageSize=10${handoffCursor?`&cursor=${encodeURIComponent(handoffCursor)}`:''}`);
 const summaries=useQuoteResource<Page<OpsClaimsSummary>>(`${base}/summaries?pageSize=10${summaryCursor?`&cursor=${encodeURIComponent(summaryCursor)}`:''}`);
 const requests=useQuoteResource<Page<OpsClaimsRequest>>(`${base}/requests?pageSize=10${requestCursor?`&cursor=${encodeURIComponent(requestCursor)}`:''}`);
 const selected=provider||admins.data?.items[0]?.id;
 useEffect(()=>{
  if(!pending)return;
  const unload=(event:BeforeUnloadEvent)=>{event.preventDefault();event.returnValue='';};
  const click=(event:MouseEvent)=>{if((event.target as Element).closest?.('a[href], [role="tab"], .account-dropdown button')){event.preventDefault();event.stopPropagation();setError('Confirm the pending claims action before leaving.');}};
  const navigation=(window as Window&{navigation?:EventTarget}).navigation;const navigate=(event:Event)=>{if(event.cancelable)event.preventDefault();};
  window.addEventListener('beforeunload',unload);document.addEventListener('click',click,true);navigation?.addEventListener('navigate',navigate);
  return()=>{window.removeEventListener('beforeunload',unload);document.removeEventListener('click',click,true);navigation?.removeEventListener('navigate',navigate);};
 },[pending]);
 async function act(action:ClaimsAction,retry?:OpsClaimsRequest,existing?:ClaimsCommand){
  setBusy(true);blocked(true);setError('');setNotice('');let command=existing;
  try{
   const actor=await incidentFetch<Actor>('/api/v1/account');
   command??=claimsCommand(action,record.id,actor.data.id,retry?.etag??etag,action==='contact'?{body}:action==='retry'?{reason}:action==='refresh'?{}:{revisionId:record.revisionId,resolutionId:record.resolution?.id,providerId:selected},retry?.id);
   setPending(command);await sendClaimsCommand(command,await csrfToken(),actor.data.id);
   const current=await incidentFetch<OpsIncident>(base);if(!current.etag)throw new Error('Reload the incident to confirm the request.');
   updated(current.data,current.etag);setPending(undefined);blocked(false);setNotice(action==='contact'?'Administrator contact queued.':action==='refresh'?'Administrator summary refresh queued.':current.data.state==='handed-off'?'Handoff acknowledged by the administrator.':'Claims request queued. Awaiting acknowledgement.');
   if(action==='contact')setBody('');handoffs.refresh();requests.refresh();summaries.refresh();
  }catch(caught){setError(caught instanceof Error?caught.message:'The claims result is unconfirmed.');if(!command||!existing&&!uncertainQuoteFailure(caught)){setPending(undefined);blocked(false);}}
  finally{setBusy(false);}
 }
 async function refresh(){setBusy(true);try{const current=await incidentFetch<OpsIncident>(base);if(current.etag)updated(current.data,current.etag);handoffs.refresh();requests.refresh();summaries.refresh();}catch(caught){setError(caught instanceof Error?caught.message:'Unable to refresh claims status.');}finally{setBusy(false);}}
 return <Panel title="Claims administrator" note="Persistent fictional handoff and external demo summaries"><div className="quote-rail-body incident-editor"><p>Logging records the report locally. Hand off the saved report to request administrator handling; acknowledgement is shown separately. Claims liability, reserves, recovery and settlement remain with the administrator. Received summaries are read-only.</p><p className="client-help">Demo requests use a persistent fictional adapter. Contact administrator queues a message here; it does not send a real email or phone call. Summary values are provider-reported and are not an underwriting decision or a portfolio loss-ratio calculation.</p>
  {error&&<p role="alert">{error}</p>}{notice&&<p role="status">{notice}</p>}
  {pending&&!busy&&<button className="button" onClick={()=>void act(pending.action,undefined,pending)}>Retry unconfirmed claims action</button>}
  {!view&&<div role="tablist" aria-label="Incident detail views" onKeyDown={event=>{if(!pending&&['ArrowLeft','ArrowRight','Home','End'].includes(event.key)){event.preventDefault();const next=event.key==='Home'?'detail':event.key==='End'?'summary':tab==='detail'?'summary':'detail';setTab(next);document.getElementById(`claims-${record.id}-${next}`)?.focus();}}}><button className="button" id={`claims-${record.id}-detail`} aria-controls={`claims-${record.id}-panel`} tabIndex={tab==='detail'?0:-1} role="tab" aria-selected={tab==='detail'} disabled={!!pending} onClick={()=>setTab('detail')}>Logged detail</button><button className="button" id={`claims-${record.id}-summary`} aria-controls={`claims-${record.id}-panel`} tabIndex={tab==='summary'?0:-1} role="tab" aria-selected={tab==='summary'} disabled={!!pending} onClick={()=>setTab('summary')}>Summary from administrator</button></div>}
  <fieldset disabled={disabled||busy||!!pending}>
   {(record.state==='draft'||record.state==='logged')&&<><label>Claims administrator<select aria-label="Claims administrator" value={selected??''} onChange={event=>setProvider(event.target.value)}>{admins.data?.items.map(x=><option key={x.id} value={x.id}>{x.label}</option>)}</select></label>
   {admins.error&&<LoadFeedback error={admins.error} retry={admins.refresh}/>}<p>Handoff sends this saved revision and its historical source. Only insurer-visible evidence is disclosed; internal and agency-only documents remain local.</p>
   <button className="button primary" disabled={!selected||!record.resolution||record.missing.length>0} onClick={()=>void act(record.state==='logged'?'handoff':'log-and-handoff')}>Log and hand off</button></>}
   {record.state==='handed-off'&&<><button className="button" onClick={()=>void act('refresh')}>Refresh from administrator</button><label>Message to administrator<textarea aria-label="Message to administrator" maxLength={8000} value={body} onChange={event=>setBody(event.target.value)}/></label><button className="button" disabled={!body.trim()} onClick={()=>void act('contact')}>Contact administrator</button></>}
  </fieldset>
  <button className="button" disabled={busy||!!pending} onClick={()=>void refresh()}>Check claims status</button>
  {(view??tab)==='summary'?<section id={`claims-${record.id}-panel`} role="tabpanel" tabIndex={0} aria-label="Administrator summaries">
   {!summaries.data?<LoadFeedback error={summaries.error} retry={summaries.refresh}/>:<>{!summaries.data.items.length&&<p>No administrator summary received yet.</p>}{summaries.data.items.map(item=><article key={item.id}><h3>{item.providerReference} · {incidentLabel(item.status)}</h3><p>External demo summary · reported as of {new Date(item.asOf).toLocaleString('en-GB')} · received {new Date(item.receivedAt).toLocaleString('en-GB')}</p><dl><dt>Paid</dt><dd>{item.paid===null?'Not advised':`GBP ${item.paid}`}</dd><dt>Reserve</dt><dd>{item.reserved===null?'Not advised':`GBP ${item.reserved}`}</dd><dt>Liability</dt><dd>{item.liability??'Not advised'}</dd><dt>Incurred</dt><dd>{item.incurred==null?'Not advised':`GBP ${item.incurred}`}</dd><dt>Recovery expected</dt><dd>{item.recoveryExpected??'Not advised'}</dd><dt>Excess applied</dt><dd>{item.excessApplied==null?'Not advised':`GBP ${item.excessApplied}`}</dd><dt>Latest movement</dt><dd>{item.movementNote??'Not advised'}</dd></dl></article>)}<p>Summaries are ordered by administrator as-of time. Later receipt does not make an older update current.</p>{summaries.data.nextCursor&&<button className="button" onClick={()=>setSummaryCursor(summaries.data!.nextCursor!)}>Older summaries</button>}{summaryCursor&&<button className="button" onClick={()=>setSummaryCursor('')}>Newest summaries</button>}</>}
  </section>:<section id={`claims-${record.id}-panel`} role="tabpanel" tabIndex={0} aria-label="Submitted incident details">
   {!handoffs.data?<LoadFeedback error={handoffs.error} retry={handoffs.refresh}/>:<>{!handoffs.data.items.length&&<p>No handoff submitted. The report remains local.</p>}{handoffs.data.items.map(item=><details key={item.id} open={handoffs.data?.items.length===1}><summary>{incidentLabel(item.state)} · {item.submitted.administratorName} · {item.providerReference??'Awaiting reference'}</summary><p>Submitted {new Date(item.createdAt).toLocaleString('en-GB')} · historical source {item.submitted.resolution.candidates[0]?.label??'Saved policy version'}</p><IncidentFacts draft={item.submitted.facts}/><p>{item.submitted.evidence.length} insurer-visible evidence files included. {item.submitted.withheldEvidenceCount} local files withheld.</p>{item.submitted.evidence.map(file=><p key={file.versionId}>{file.name}</p>)}{item.outcomeCode&&<p>{incidentLabel(item.outcomeCode)}</p>}{item.state==='rejected'&&<p>The administrator rejected this submission. Correct the report and submit a new revision with a new request.</p>}</details>)}{handoffs.data.nextCursor&&<button className="button" onClick={()=>setHandoffCursor(handoffs.data!.nextCursor!)}>Older handoffs</button>}{handoffCursor&&<button className="button" onClick={()=>setHandoffCursor('')}>Newest handoffs</button>}</>}
  </section>}
  <h3>Requests and attempts</h3>{!requests.data?<LoadFeedback error={requests.error} retry={requests.refresh}/>:<>{requests.data.items.map(item=><details key={item.id}><summary>{incidentLabel(item.purpose)} · {incidentLabel(item.state)} · {new Date(item.createdAt).toLocaleString('en-GB')}</summary>{item.body&&<p>{item.body}</p>}{item.attempts.map(attempt=><p key={attempt.id}>Attempt {attempt.number}: {incidentLabel(attempt.outcome)}{attempt.errorCode?` · ${incidentLabel(attempt.errorCode)}`:''}</p>)}{item.retryAllowed&&<><label>Reason to retry<input value={reason} maxLength={1000} onChange={event=>setReason(event.target.value)}/></label><button className="button" disabled={busy||!!pending||!reason.trim()} onClick={()=>void act('retry',item)}>Retry this claims request</button></>}</details>)}{requests.data.nextCursor&&<button className="button" onClick={()=>setRequestCursor(requests.data!.nextCursor!)}>Older requests</button>}{requestCursor&&<button className="button" onClick={()=>setRequestCursor('')}>Newest requests</button>}</>}
 </div></Panel>;
}
