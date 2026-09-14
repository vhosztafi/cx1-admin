'use client';
import {useEffect,useRef,useState} from 'react';
import {Panel,DataTable} from '../primitives';
import {csrfToken} from '../../lib/auth';
import {agencyFetch,AgencyError} from '../../lib/agencies';
import {clientDate,type Page} from '../../lib/clients';
import {LoadFeedback,Paging,useAgencyResource} from './shared';

type Notification = {id:string;kind:string;state:string;attempts:number;createdAt:string;completedAt?:string;lastResultCode?:string;retryAllowed:boolean;etag:string};
const labels:Record<string,string> = {queued:'Queued',processing:'Processing','demo-delivered':'Demo delivered',rejected:'Rejected',exhausted:'Attempts exhausted',superseded:'Superseded'};
export function AgencyNotifications({id,onSaved}: {id:string;onSaved?:()=>void}) {
  const [cursors,setCursors]=useState<string[]>([]);
  const resource=useAgencyResource<Page<Notification>>(`/api/v1/agencies/${id}/notifications?pageSize=10${cursors.at(-1)?`&cursor=${encodeURIComponent(cursors.at(-1)!)}`:''}`);
  const [selected,setSelected]=useState<Notification>();const [reason,setReason]=useState('');
  const [busy,setBusy]=useState(false);const [uncertain,setUncertain]=useState(false);const [stale,setStale]=useState(false);
  const [error,setError]=useState('');const [notice,setNotice]=useState('');const locked=useRef(false);
  const reasonInput=useRef<HTMLTextAreaElement>(null);const refreshButton=useRef<HTMLButtonElement>(null);
  useEffect(()=>{reasonInput.current?.focus();},[selected?.id]);
  const command=useRef<{id:string;body:string;etag:string;key:string}|undefined>(undefined);
  const guard=!!selected;
  useEffect(()=>{
    if(!guard)return;
    const unload=(event:BeforeUnloadEvent)=>{event.preventDefault();};
    const navigate=(event:MouseEvent)=>{if((event.target as Element).closest('a[href]')){event.preventDefault();event.stopPropagation();setError('Finish or cancel this notification retry before leaving.');}};
    window.addEventListener('beforeunload',unload);document.addEventListener('click',navigate,true);
    return()=>{window.removeEventListener('beforeunload',unload);document.removeEventListener('click',navigate,true);};
  },[guard]);
  async function submit() {
    if(locked.current||stale||!selected)return;
    if(!uncertain){if(!reason.trim())return;command.current={id:selected.id,body:JSON.stringify({reason:reason.trim()}),etag:selected.etag,key:crypto.randomUUID()};}
    const pending=command.current!;locked.current=true;setBusy(true);setError('');setNotice('');
    try{
      await agencyFetch(`/api/v1/agencies/${id}/notifications/${pending.id}/retry`,{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-Token':await csrfToken(),'Idempotency-Key':pending.key,'If-Match':pending.etag},body:pending.body});
      setSelected(undefined);setReason('');setUncertain(false);command.current=undefined;setNotice('Retry queued for demo delivery. Delivery has not yet been confirmed.');resource.refresh();onSaved?.();requestAnimationFrame(()=>refreshButton.current?.focus());
    }catch(failure){
      if(!(failure instanceof AgencyError)||failure.status>=500){setUncertain(true);setError('The service could not confirm the retry. Retry the same request to recover its result.');}
      else{setUncertain(false);command.current=undefined;setStale([409,412,428].includes(failure.status));setError([409,412,428].includes(failure.status)?'This notification changed. Your reason is retained; reload its current status.':failure.message);}
    }finally{locked.current=false;setBusy(false);}
  }
  async function reload(){if(!selected||busy||uncertain)return;setBusy(true);try{const result=await agencyFetch<Notification>(`/api/v1/agencies/${id}/notifications/${selected.id}`);setSelected(result.data);setStale(false);setError(result.data.retryAllowed?'':'This notification is no longer eligible for retry.');resource.refresh();}catch{setError('Unable to reload this notification. Your reason is retained.');}finally{setBusy(false);}}
  return <Panel title="Demo delivery">
    <p className="match-copy">Persisted notification delivery history. No external email is sent. Invitation acceptance is tracked separately.</p>
    <div className="operations-actions notification-controls"><button ref={refreshButton} className="button secondary" disabled={!!selected} onClick={resource.refresh}>Refresh delivery status</button></div>
    {!resource.data?<LoadFeedback error={resource.error} retry={resource.refresh}/>:<><DataTable caption="Agency demo notifications" columns={['Created','Notification','Delivery status','Attempts','Completed','Action']}>
      {resource.data.items.map(row=><tr key={row.id}><td>{clientDate(row.createdAt,true)}</td><td>{row.kind==='activation'?'Agency activation':'Agency invitation'}</td><td>{labels[row.state]??row.state}{row.lastResultCode&&<small className="match-copy"> {row.lastResultCode.replaceAll('-',' ')}</small>}</td><td>{row.attempts}</td><td>{row.completedAt?clientDate(row.completedAt,true):'Not completed'}</td><td>{row.retryAllowed?<button className="button secondary" disabled={!!selected} onClick={()=>{setSelected(row);setError('');setNotice('');setStale(false);}}>Retry delivery</button>:row.state==='rejected'?'Rejected; retry unavailable':'—'}</td></tr>)}
    </DataTable>{resource.data.items.length===0&&<p className="match-copy">No demo notifications recorded for this agency.</p>}<Paging total={resource.data.totalCount} previous={!selected&&cursors.length?()=>setCursors(x=>x.slice(0,-1)):undefined} next={!selected&&resource.data.nextCursor?()=>setCursors(x=>[...x,resource.data!.nextCursor!]):undefined}/></>}
    {selected&&<form className="client-form" onSubmit={event=>{event.preventDefault();void submit();}}><h3>Retry demo delivery</h3><p className="match-copy">Retrying preserves the original message and delivery history.</p><label htmlFor="notification-retry-reason">Reason for retry</label><textarea ref={reasonInput} id="notification-retry-reason" required maxLength={1000} value={reason} disabled={busy||uncertain} onChange={event=>setReason(event.target.value)}/><div className="operations-actions"><button className="button" disabled={busy||stale||!selected.retryAllowed||!reason.trim()}>{busy?'Queuing…':uncertain?'Retry same request':'Queue retry'}</button>{stale&&<button className="button secondary" type="button" disabled={busy} onClick={()=>void reload()}>Reload notification status</button>}<button type="button" className="button secondary" disabled={busy||uncertain} onClick={()=>{setSelected(undefined);setReason('');setError('');setStale(false);requestAnimationFrame(()=>refreshButton.current?.focus());}}>Cancel retry</button></div></form>}
    {error&&<p role="alert" className="form-error">{error}</p>}<p role="status" className="match-copy">{notice}</p>
  </Panel>;
}
