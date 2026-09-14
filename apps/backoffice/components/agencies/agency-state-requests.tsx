'use client';
import {useEffect,useRef,useState} from 'react';
import {Panel,DataTable} from '../primitives';
import {agencyFetch,AgencyError} from '../../lib/agencies';
import {csrfToken} from '../../lib/auth';
import {userReason} from '../../lib/agency-users';
import {stateAction,canDecideState,type StateRequest} from '../../lib/agency-state';
import {clientDate,type Page} from '../../lib/clients';
import {LoadFeedback,Paging,useAgencyResource} from './shared';

type Selection={title:string;url:string;etag:string;outcome?:'approve'|'reject';request?:StateRequest};
export function AgencyStateRequests({id,actorId,agencyState,etag,disabled=false,onSaved,onRefresh,onGuardChange}:{id:string;actorId:string;agencyState:string;etag:string;disabled?:boolean;onSaved:()=>void;onRefresh?:()=>void;onGuardChange?:(value:boolean)=>void}) {
  const [cursors,setCursors]=useState<string[]>([]);
  const requests=useAgencyResource<Page<StateRequest>>(`/api/v1/agencies/${id}/state-requests?pageSize=10${cursors.at(-1)?`&cursor=${encodeURIComponent(cursors.at(-1)!)}`:''}`);
  const [selection,setSelection]=useState<Selection>();const [reason,setReason]=useState('');
  const [busy,setBusy]=useState(false);const [uncertain,setUncertain]=useState(false);const [stale,setStale]=useState(false);
  const [error,setError]=useState('');const [notice,setNotice]=useState('');
  const dialog=useRef<HTMLDialogElement>(null);const input=useRef<HTMLInputElement>(null);const trigger=useRef<HTMLElement|null>(null);const lock=useRef(false);
  const command=useRef<{url:string;etag:string;key:string;body:string}>(undefined);
  const action=stateAction(agencyState);
  useEffect(()=>{if(selection){dialog.current?.showModal();input.current?.focus();}},[selection]);
  useEffect(()=>{onGuardChange?.(!!selection);return()=>onGuardChange?.(false);},[selection,onGuardChange]);
  useEffect(()=>{
    if(!selection)return;
    const unload=(event:BeforeUnloadEvent)=>event.preventDefault();
    const navigate=(event:MouseEvent)=>{if((event.target as Element).closest?.('a[href]')){event.preventDefault();event.stopPropagation();}};
    window.addEventListener('beforeunload',unload);document.addEventListener('click',navigate,true);
    return()=>{window.removeEventListener('beforeunload',unload);document.removeEventListener('click',navigate,true);};
  },[selection]);
  function begin(value:Selection){if(disabled||lock.current||selection)return;trigger.current=document.activeElement as HTMLElement;setReason('');setError('');setNotice('');setStale(false);setSelection(value);}
  function close(){if(lock.current||uncertain)return;dialog.current?.close();setSelection(undefined);command.current=undefined;requestAnimationFrame(()=>trigger.current?.focus());}
  async function submit(){
    if(lock.current||!selection||stale)return;
    if(!uncertain){try{command.current={url:selection.url,etag:selection.etag,key:crypto.randomUUID(),body:JSON.stringify({reason:userReason(reason),...(selection.outcome?{outcome:selection.outcome}:{})})};}catch(failure){setError((failure as Error).message);return;}}
    lock.current=true;setBusy(true);setError('');const pending=command.current!;
    try{
      await agencyFetch(pending.url,{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-Token':await csrfToken(),'If-Match':pending.etag,'Idempotency-Key':pending.key},body:pending.body});
    }catch(failure){
      const unknown=!(failure instanceof AgencyError)||failure.status>=500;setUncertain(unknown);
      setStale(!unknown&&failure instanceof AgencyError&&[409,412,428].includes(failure.status));
      setError(unknown?'The result is uncertain. Retry the same request to recover it.':failure instanceof AgencyError&&[409,412,428].includes(failure.status)?'The agency or request changed. Your reason is retained. Close this dialog and refresh before reviewing the current record.':(failure as Error).message);
      lock.current=false;setBusy(false);return;
    }
    // A refresh failure must never turn a confirmed command into an uncertain command.
    const applied=selection.outcome==='approve';setUncertain(false);lock.current=false;setBusy(false);dialog.current?.close();setSelection(undefined);command.current=undefined;requestAnimationFrame(()=>trigger.current?.focus());
    setNotice(selection.outcome?`Decision saved: ${applied?'approved and applied':'rejected'}.`:'Request saved. A different administrator must review and countersign.');
    requests.refresh();if(applied)onSaved();
  }
  return <Panel title="Agency approvals"><p className="match-copy">Changes require a reason and approval by a different administrator. The agency changes only after approval.</p>
    <div className="operations-actions">{action&&<button className="button" disabled={disabled||!!selection} onClick={()=>begin({title:action.label,url:`/api/v1/agencies/${id}/${action.path}`,etag})}>{action.label}</button>}<button className="button secondary" disabled={disabled||!!selection} onClick={()=>{requests.refresh();onRefresh?.();}}>Refresh approvals</button></div>
    {disabled&&<p className="match-copy">Save or resolve the draft before requesting approval.</p>}
    {!requests.data?<LoadFeedback error={requests.error} retry={requests.refresh}/>:<><DataTable caption="Agency approval history" columns={['Request','Requested by','Reason','Status','Decision','Actions']}>
      {requests.data.items.map(row=><tr key={row.id}><td>{row.requestKind}<br/>{clientDate(row.createdAt,true)}</td><td>{row.requestedByLabel}</td><td>{row.reason}</td><td>{row.state}</td><td>{row.decisionByLabel?<>{row.decisionByLabel}<br/>{row.decisionReason}<br/>{row.decidedAt&&clientDate(row.decidedAt,true)}</>:'Awaiting decision'}</td><td>{canDecideState(row,actorId)?<div className="operations-actions">{(['approve','reject'] as const).map(outcome=><button key={outcome} className="button secondary" disabled={disabled||!!selection} onClick={()=>begin({title:`${outcome==='approve'?'Approve':'Reject'} ${row.requestKind}`,url:`/api/v1/agency-state-requests/${row.id}/decision`,etag:row.etag,outcome,request:row})}>{outcome==='approve'?'Approve':'Reject'}</button>)}</div>:row.state==='pending'?'Another administrator must decide.':'Closed'}</td></tr>)}
    </DataTable>{!requests.data.items.length&&<p className="match-copy">No approval requests yet.</p>}<Paging total={requests.data.totalCount} previous={!selection&&cursors.length?()=>setCursors(x=>x.slice(0,-1)):undefined} next={!selection&&requests.data.nextCursor?()=>setCursors(x=>[...x,requests.data!.nextCursor!]):undefined}/></>}
    <dialog ref={dialog} className="agency-dialog" aria-labelledby="agency-state-title" onCancel={event=>{event.preventDefault();close();}}><form onSubmit={event=>{event.preventDefault();void submit();}}><h2 id="agency-state-title">{selection?.title}</h2><p className="match-copy">{selection?.outcome==='reject'?'The request will close without changing the agency.':stateAction(selection?.request?({activation:'draft',suspension:'active',reactivation:'suspended'}[selection.request.requestKind]):agencyState)?.effect}</p>{selection?.request&&<p>Requested by {selection.request.requestedByLabel}: {selection.request.reason}</p>}<label>Reason<input ref={input} required maxLength={1000} value={reason} disabled={busy||uncertain} onChange={event=>setReason(event.target.value)}/></label>{error&&<p className="error-message" role="alert">{error}</p>}<div className="operations-actions"><button className="button button-primary" disabled={busy||stale}>{busy?'Saving…':uncertain?'Retry same request':'Confirm'}</button><button type="button" className="button secondary" disabled={busy||uncertain} onClick={close}>Cancel</button></div></form></dialog><p className="match-copy" role="status">{notice}</p>
  </Panel>;
}
