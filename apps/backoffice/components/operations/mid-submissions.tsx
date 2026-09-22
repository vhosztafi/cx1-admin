'use client';
import {useEffect,useRef,useState} from 'react';
import Link from 'next/link';
import type {OpsMidSubmission} from '../../../../contracts/generated/operations';
import {csrfToken,type Actor} from '../../lib/auth';
import {incidentFetch} from '../../lib/incidents-api';
import {midRetryCommand,sendMidRetry,type MidRetry} from '../../lib/mid-api';
import {uncertainQuoteFailure} from '../../lib/quotes';
import {LoadFeedback,useQuoteResource} from '../quotes/shared';
import {DataTable,Panel} from '../primitives';
const date=(value:string)=>new Date(value).toLocaleString('en-GB',{timeZone:'Europe/London'});
const label=(value:string)=>({'not-required':'No reportable changes','new-business':'Initial issue','provider-unavailable':'Provider unavailable','provider-timeout':'Provider response timed out','attempts-exhausted':'Automatic attempts exhausted','mid-context-unavailable':'Original access or source unavailable','demo-provider-rejected':'Demo provider rejected the submission','no-reportable-changes':'No reportable changes'}[value]??value.replaceAll('-',' ').replace(/^./,x=>x.toUpperCase()));
export function MidSubmissions({versionId,riskItemId}:{versionId:string;riskItemId?:string}){
 const [cursor,setCursor]=useState(''),[reason,setReason]=useState(''),[pending,setPending]=useState<MidRetry>(),[busy,setBusy]=useState(false),[error,setError]=useState(''),[notice,setNotice]=useState('');
 const root=useRef<HTMLDivElement>(null);
 const history=useQuoteResource<{items:OpsMidSubmission[];nextCursor:string|null}>(`/api/v1/versions/${versionId}/mid-submissions?pageSize=25${cursor?`&cursor=${encodeURIComponent(cursor)}`:''}`);
 useEffect(()=>{
  if(!pending)return;
  const warn=()=>setError('Confirm the same MID retry before leaving this submission.');
  const unload=(event:BeforeUnloadEvent)=>{event.preventDefault();event.returnValue='';};
  const outside=(event:Event)=>{if(!root.current?.contains(event.target as Node)){event.preventDefault();event.stopPropagation();warn();}};
  const click=(event:MouseEvent)=>{if((event.target as Element).closest?.('a[href],button,[role="tab"]'))outside(event);};
  const navigation=(window as Window&{navigation?:EventTarget}).navigation;const navigate=(event:Event)=>{if(event.cancelable){event.preventDefault();warn();}};
  window.addEventListener('beforeunload',unload);document.addEventListener('click',click,true);document.addEventListener('submit',outside,true);navigation?.addEventListener('navigate',navigate);
  return()=>{window.removeEventListener('beforeunload',unload);document.removeEventListener('click',click,true);document.removeEventListener('submit',outside,true);navigation?.removeEventListener('navigate',navigate);};
 },[pending]);
 async function retry(row?:OpsMidSubmission,existing?:MidRetry){
  setBusy(true);setError('');setNotice('');
  try{const account=await incidentFetch<Actor>('/api/v1/account');const command=existing??midRetryCommand(row!.id,row!.jobId,account.data.id,row!.etag,reason);setPending(command);await sendMidRetry(command,await csrfToken(),account.data.id);setPending(undefined);setNotice('Submission queued for retry.');history.refresh();}
  catch(caught){setError(caught instanceof Error?caught.message:'The result is unconfirmed. Retry the same action.');if(!existing&&!uncertainQuoteFailure(caught))setPending(undefined);}finally{setBusy(false);}
 }
 const rows=history.data?.items.filter(row=>!riskItemId||row.items.some(item=>item.riskItemId===riskItemId));
 return <Panel title="MID submissions" note="Persistent demo reporting for this exact issued version"><div ref={root} className="quote-rail-body mid-submissions">
  {error&&<p role="alert">{error}</p>}{notice&&<p role="status">{notice}</p>}
  {pending&&!busy&&<button className="button" onClick={()=>void retry(undefined,pending)}>Retry unconfirmed MID action</button>}
  <button className="button" disabled={busy||!!pending} onClick={history.refresh}>Check MID status</button>
  {!history.data?<LoadFeedback error={history.error} retry={history.refresh}/>:!rows?.length?<p>No saved submission for this {riskItemId?'vehicle at this ':''}version. Queued reporting may still be preparing.</p>:rows.map(row=><article key={row.id} aria-label="Saved MID submission">
   <h3>{label(row.purpose)} · {label(row.state)}</h3><p>Effective {date(row.effectiveAt)} · London</p><p>{row.providerReference??(row.state==='not-required'?'No provider submission required':'Awaiting provider reference')}</p>
   {row.reasonCodes.map(code=><p key={code}>{label(code)}</p>)}
   <DataTable caption="Submitted MID items" columns={['Registration','Kind','Action','Effective']}>
    {row.items.filter(item=>!riskItemId||item.riskItemId===riskItemId).map(item=><tr key={`${item.riskItemId}:${item.action}`}><th scope="row">{item.registration}</th><td>{label(item.kind)}</td><td>{label(item.action)}</td><td>{date(item.effectiveAt)}</td></tr>)}
   </DataTable>
   <details><summary>Submission and attempt history</summary><p>Policy version: {row.policyVersionId}</p><p>Submitted source hash: {row.contentHash}</p><p>Recorded {date(row.createdAt)} · London</p>
    {!row.attempts.length&&<p>No attempt yet.</p>}{row.attempts.map(attempt=><p key={attempt.id}>Attempt {attempt.number}: {label(attempt.outcome)}{attempt.errorCode?` · ${label(attempt.errorCode)}`:''} · {date(attempt.startedAt)}</p>)}
   </details>
   {row.exceptionTaskId&&<Link className="button" href={`/tasks/${row.exceptionTaskId}`} onClick={event=>{if(pending)event.preventDefault();}}>Open exception task</Link>}
   {row.retryAllowed&&<fieldset disabled={busy||!!pending}><label>Reason to retry<input value={reason} maxLength={1000} onChange={event=>setReason(event.target.value)}/></label><button className="button" disabled={!reason.trim()} onClick={()=>void retry(row)}>Retry data submission</button></fieldset>}
  </article>)}
  {history.data?.nextCursor&&<button className="button" disabled={busy||!!pending} onClick={()=>setCursor(history.data!.nextCursor!)}>Older submissions</button>}{cursor&&<button className="button" disabled={busy||!!pending} onClick={()=>setCursor('')}>Newest submissions</button>}
 </div></Panel>;
}
