'use client';
import {useEffect,useRef,useState} from 'react';
import {Panel,Status} from '../primitives';
import {quoteFetch,uncertainQuoteFailure} from '../../lib/quotes';
import {renewalLapseCommand,sendRenewalLapse,type RenewalLifecycle,type RenewalLapseCommand} from '../../lib/renewal-lifecycle';

const labels:Record<RenewalLifecycle['state'],string>={'not-due':'Invitation not yet due',due:'Invitation due',overdue:'Renewal overdue',invited:'Invitation delivered',accepted:'Acceptance recorded',issued:'New term issued',lapsed:'Renewal lapsed',cancelled:'Term cancelled'};
const date=(value:string)=>new Date(value).toLocaleString('en-GB',{timeZone:'Europe/London',dateStyle:'medium',timeStyle:'short'});
export function RenewalLifecyclePanel({termId,blocked=false}:{termId:string;blocked?:boolean}) {
 const [view,setView]=useState<RenewalLifecycle|null>(null),[reason,setReason]=useState(''),[confirmed,setConfirmed]=useState(false);
 const [busy,setBusy]=useState(false),[retry,setRetry]=useState(false),[error,setError]=useState(''),[current,setCurrent]=useState(false);
 const [readError,setReadError]=useState(false);
 const pending=useRef<RenewalLapseCommand|null>(null),sending=useRef(false);const path=`/api/v1/terms/${termId}/renewal-lifecycle`;
 useEffect(()=>{
  const abort=new AbortController();
  async function refresh(){if(sending.current||pending.current)return;try{const result=await quoteFetch<RenewalLifecycle>(path,{signal:abort.signal});if(!abort.signal.aborted){setView(result.data);setCurrent(result.etag===result.data.etag);setReadError(false);}}
   catch{if(!abort.signal.aborted){setCurrent(false);setReadError(true);}}}
  void refresh();const interval=setInterval(()=>void refresh(),5000);return()=>{abort.abort();clearInterval(interval);};
 },[path]);
 async function lapse(){
  if(sending.current||!view||(!pending.current&&(!current||!confirmed||!view.canLapse||blocked)))return;
  sending.current=true;setBusy(true);setError('');
  try{
   pending.current??=renewalLapseCommand(termId,view.etag,reason);
   const result=await sendRenewalLapse(pending.current);const read=await quoteFetch<RenewalLifecycle>(path);
   if(read.data.lapseEventId!==result.id||read.data.state!=='lapsed')throw new Error('Lapse readback remains unconfirmed.');
   setView(read.data);setCurrent(read.etag===read.data.etag);pending.current=null;setRetry(false);setConfirmed(false);
  }catch(failure){const uncertain=uncertainQuoteFailure(failure);setRetry(uncertain);if(!uncertain){pending.current=null;setCurrent(false);}setError(uncertain?'The lapse result is unconfirmed. Retry the same action.':'Lapse was refused. Refresh the current renewal and review its acceptance or issue status.');}
  finally{sending.current=false;setBusy(false);}
 }
 return <Panel title="Renewal timeline" note="Dates shown in London time"><section className="quote-rail-body" aria-label="Renewal lifecycle">
  {!view?<p role="status">{readError?'Renewal lifecycle information is unavailable. Retrying shortly.':'Renewal lifecycle information is loading.'}</p>:<>
   <Status tone={view.state==='lapsed'||view.state==='overdue'?'warning':'info'}>{labels[view.state]}</Status>
   <dl className="underwriting-premium"><div><dt>Invitation due</dt><dd>{date(view.timeline.invitationDueAt)}</dd></div><div><dt>Current term ends</dt><dd>{date(view.timeline.expiringEnd)}</dd></div><div><dt>Proposed renewal starts</dt><dd>{date(view.timeline.renewalInception)}</dd></div><div><dt>Automatic lapse deadline</dt><dd>{date(view.timeline.autoLapseAt)}</dd></div></dl>
   {!current&&<p role="status">Current renewal status could not be confirmed. Actions are paused while refreshing.</p>}
   {view.lapseEventId?<><p>Recorded {date(view.recordedAt!)} · {view.lapseMode==='automatic'?'Automatic lapse':'Manual lapse'}</p><p>{view.lapseReason}</p>
    <p>Existing cover ends at its original expiry. Lapse creates no new policy term.</p><p>Demo notification: {view.notificationState==='succeeded'?'Delivered':view.notificationState==='failed'?'Failed — review required':view.notificationState==='leased'?'Processing':'Queued'}. No email is sent.</p>
    <details><summary>Notification attempts ({view.notificationAttempts.length})</summary>{view.notificationAttempts.map(attempt=><p key={attempt.number}>Attempt {attempt.number} · {date(attempt.startedAt)} · {attempt.outcome.replaceAll('-',' ')}{attempt.errorCode&&` · ${attempt.errorCode}`}</p>)}</details></>:
    <><p>Only a successfully delivered invitation is marked invited. Acceptance is recorded separately; an accepted or issued renewal cannot lapse.</p>
     <fieldset className="quote-reference-fields" disabled={!current||!view.canLapse||blocked||busy||retry}><legend>Record lapse</legend>
      <label>Lapse reason<textarea aria-label="Lapse reason" minLength={10} maxLength={1000} value={reason} onChange={event=>setReason(event.target.value)}/></label>
      <label><input type="checkbox" checked={confirmed} onChange={event=>setConfirmed(event.target.checked)}/> I confirm this renewal will not proceed.</label>
      <p>Lapse takes effect at the current term’s expiry, even when recorded early.</p>
      <button className="button" disabled={!confirmed||reason.trim().length<10} onClick={()=>void lapse()}>{busy?'Recording lapse…':'Record renewal lapse'}</button>
     </fieldset></>}
  </>}
  {error&&<p role="alert">{error}</p>}{retry&&<button className="button" disabled={busy} onClick={()=>void lapse()}>Retry same lapse</button>}
 </section></Panel>;
}
