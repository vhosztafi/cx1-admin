'use client';
import {useEffect,useRef,useState} from 'react';
import {Panel,DataTable} from '../primitives';
import {agencyFetch,AgencyError} from '../../lib/agencies';
import {csrfToken} from '../../lib/auth';
import {userReason} from '../../lib/agency-users';
import {canDecidePermission,type PermissionRequest,type PermissionGrant} from '../../lib/agency-permissions';
import {clientDate,type Page} from '../../lib/clients';
import {LoadFeedback,Paging,useAgencyResource} from './shared';

type Selection={title:string;url:string;etag:string;kind:'request'|'decision'|'revoke';outcome?:'approve'|'reject';request?:PermissionRequest};
export function AgencyPermissions({id,actorId,agencyState,etag,onSaved}:{id:string;actorId:string;agencyState:string;etag:string;onSaved:()=>void}) {
  const [cursors,setCursors]=useState<string[]>([]);const [grantCursors,setGrantCursors]=useState<string[]>([]);
  const base=`/api/v1/agencies/${id}`;
  const requests=useAgencyResource<Page<PermissionRequest>>(`${base}/permission-requests?pageSize=10${cursors.at(-1)?`&cursor=${encodeURIComponent(cursors.at(-1)!)}`:''}`);
  const grants=useAgencyResource<Page<PermissionGrant>>(`${base}/permission-grants?pageSize=10${grantCursors.at(-1)?`&cursor=${encodeURIComponent(grantCursors.at(-1)!)}`:''}`);
  const [selection,setSelection]=useState<Selection>();const [reason,setReason]=useState('');
  const [busy,setBusy]=useState(false);const [uncertain,setUncertain]=useState(false);const [stale,setStale]=useState(false);
  const [error,setError]=useState('');const [notice,setNotice]=useState('');
  const dialog=useRef<HTMLDialogElement>(null);const input=useRef<HTMLInputElement>(null);const trigger=useRef<HTMLElement|null>(null);const lock=useRef(false);
  const command=useRef<{url:string;etag:string;key:string;body:string}>(undefined);

  useEffect(()=>{if(selection){dialog.current?.showModal();input.current?.focus();}},[selection]);
  useEffect(()=>{
    if(!selection)return;
    const unload=(event:BeforeUnloadEvent)=>event.preventDefault();
    const navigate=(event:MouseEvent)=>{if((event.target as Element).closest?.('a[href]')){event.preventDefault();event.stopPropagation();}};
    window.addEventListener('beforeunload',unload);document.addEventListener('click',navigate,true);
    return()=>{window.removeEventListener('beforeunload',unload);document.removeEventListener('click',navigate,true);};
  },[selection]);
  function begin(value:Selection){if(lock.current||selection)return;trigger.current=document.activeElement as HTMLElement;setReason('');setError('');setNotice('');setStale(false);setSelection(value);}
  function close(){if(lock.current||uncertain)return;dialog.current?.close();setSelection(undefined);command.current=undefined;requestAnimationFrame(()=>trigger.current?.focus());}
  async function submit(){
    if(lock.current||!selection||stale)return;
    if(!uncertain){try{command.current={url:selection.url,etag:selection.etag,key:crypto.randomUUID(),body:JSON.stringify({reason:userReason(reason),...(selection.kind==='request'?{permission:'bordereau-download'}:{}),...(selection.outcome?{outcome:selection.outcome}:{})})};}catch(failure){setError((failure as Error).message);return;}}
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
    setUncertain(false);lock.current=false;setBusy(false);dialog.current?.close();setSelection(undefined);command.current=undefined;requestAnimationFrame(()=>trigger.current?.focus());
    setNotice('Permission change saved.');
    setCursors([]);setGrantCursors([]);requests.refresh();grants.refresh();onSaved();
  }
  function refresh(){setCursors([]);setGrantCursors([]);requests.refresh();grants.refresh();onSaved();}
  return <><Panel title="Permissions & access"><p className="match-copy">Bordereau download access requires approval by a different administrator. Downloads are not available yet, including for approved agencies.</p><div className="operations-actions"><button className="button button-primary" disabled={agencyState!=='active'||!!selection} onClick={()=>begin({title:'Request bordereau access',kind:'request',url:base+'/permission-requests',etag})}>Request bordereau access</button><button className="button" disabled={!!selection} onClick={refresh}>Refresh permissions</button></div>{agencyState!=='active'&&<p className="match-copy">Requests and decisions require an active agency. Existing grants can be revoked while suspended.</p>}</Panel>
    <Panel title="Permission requests">{!requests.data?<LoadFeedback error={requests.error} retry={refresh}/>:<><DataTable caption="Permission requests" columns={['Permission','Requested by','Reason','Status','Decision','Actions']}>{requests.data.items.map(row=><tr key={row.id}><td>Bordereau downloads<br/>{clientDate(row.createdAt,true)}</td><td>{row.requestedByLabel}</td><td>{row.reason}</td><td>{row.state}</td><td>{row.decisionByLabel?<>{row.decisionByLabel}<br/>{row.decisionReason}<br/>{row.decidedAt&&clientDate(row.decidedAt,true)}</>:'Awaiting decision'}</td><td>{canDecidePermission(row,actorId,agencyState)?<div className="operations-actions">{(['approve','reject'] as const).map(outcome=><button className="button" key={outcome} disabled={!!selection} onClick={()=>begin({title:`${outcome==='approve'?'Approve':'Reject'} bordereau access`,kind:'decision',outcome,url:base+`/permission-requests/${row.id}/decision`,etag:row.etag,request:row})}>{outcome==='approve'?'Approve':'Reject'}</button>)}</div>:row.state==='pending'?'An independent administrator must review while the agency is active.':'Closed'}</td></tr>)}</DataTable>{!requests.data.items.length&&<p className="match-copy">No permission requests yet.</p>}<Paging total={requests.data.totalCount} previous={!selection&&cursors.length?()=>setCursors(x=>x.slice(0,-1)):undefined} next={!selection&&requests.data.nextCursor?()=>setCursors(x=>[...x,requests.data!.nextCursor!]):undefined}/></>}</Panel>
    <Panel title="Permission grants">{!grants.data?<LoadFeedback error={grants.error} retry={refresh}/>:<><DataTable caption="Permission grants" columns={['Permission','Approved by','Status','Revocation','Actions']}>{grants.data.items.map(row=><tr key={row.id}><td>Bordereau downloads</td><td>{row.grantedByLabel}<br/>{clientDate(row.grantedAt,true)}</td><td>{row.revokedAt?'Revoked':agencyState==='active'?'Granted · downloads unavailable':'Agency access suspended'}</td><td>{row.revokedAt?<>{row.revokedByLabel}<br/>{row.revocationReason}<br/>{clientDate(row.revokedAt,true)}</>:'Not revoked'}</td><td>{!row.revokedAt&&['active','suspended'].includes(agencyState)&&<button className="button" disabled={!!selection} onClick={()=>begin({title:'Revoke bordereau access',kind:'revoke',url:base+`/permission-grants/${row.id}/revoke`,etag:row.etag})}>Revoke</button>}</td></tr>)}</DataTable>{!grants.data.items.length&&<p className="match-copy">No permission grants yet.</p>}<Paging total={grants.data.totalCount} previous={!selection&&grantCursors.length?()=>setGrantCursors(x=>x.slice(0,-1)):undefined} next={!selection&&grants.data.nextCursor?()=>setGrantCursors(x=>[...x,grants.data!.nextCursor!]):undefined}/></>}</Panel>
    <dialog ref={dialog} className="agency-dialog" aria-labelledby="agency-permission-title" onCancel={event=>{event.preventDefault();close();}}><form onSubmit={event=>{event.preventDefault();void submit();}}><h2 id="agency-permission-title">{selection?.title}</h2><p className="match-copy">{selection?.kind==='request'?'A different administrator must review this request.':selection?.kind==='revoke'?'This withdraws the permission and invalidates existing agency user sessions.':'The decision is recorded with your identity and reason. Approval does not make downloads available yet.'}</p>{selection?.request&&<p>Requested by {selection.request.requestedByLabel}: {selection.request.reason}</p>}<label>Reason<input ref={input} required maxLength={1000} value={reason} disabled={busy||uncertain} onChange={event=>setReason(event.target.value)}/></label>{error&&<p className="error-message" role="alert">{error}</p>}<div className="operations-actions"><button className="button button-primary" disabled={busy||stale}>{busy?'Saving…':uncertain?'Retry same request':'Confirm'}</button><button type="button" className="button" disabled={busy||uncertain} onClick={close}>Cancel</button></div></form></dialog><p className="match-copy" role="status">{notice}</p>
  </>;
}
