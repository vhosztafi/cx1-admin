'use client';
import {useEffect,useRef,useState} from 'react';
import {Panel,DataTable} from '../primitives';
import {agencyFetch,AgencyError,type CatalogProduct} from '../../lib/agencies';
import type {AgencyTermsRequest} from '../../lib/agency-terms';
import {canDecideState} from '../../lib/agency-state';
import {userReason} from '../../lib/agency-users';
import {csrfToken} from '../../lib/auth';
import {clientDate,type Page} from '../../lib/clients';
import {LoadFeedback,Paging,useAgencyResource} from './shared';
import {TermsSnapshotDetails} from './agency-terms-history';

export function AgencyTermsRequests({id,actorId,onApplied}:{id:string;actorId:string;onApplied:()=>void}) {
  const [cursors,setCursors]=useState<string[]>([]);const [selected,setSelected]=useState<AgencyTermsRequest>();
  const requests=useAgencyResource<Page<AgencyTermsRequest>>(`/api/v1/agencies/${id}/terms-requests?pageSize=10${cursors.at(-1)?`&cursor=${encodeURIComponent(cursors.at(-1)!)}`:''}`);
  const catalog=useAgencyResource<Page<CatalogProduct>>('/api/v1/agency-product-catalog');
  const [reason,setReason]=useState('');const [busy,setBusy]=useState(false);const [uncertain,setUncertain]=useState(false);const [stale,setStale]=useState(false);const [error,setError]=useState('');const [notice,setNotice]=useState('');
  const dialog=useRef<HTMLDialogElement>(null);const heading=useRef<HTMLHeadingElement>(null);const trigger=useRef<HTMLElement|null>(null);const refresh=useRef<HTMLButtonElement>(null);const lock=useRef(false);
  const command=useRef<{id:string;body:string;key:string;etag:string;outcome:'approve'|'reject'}>(undefined);
  useEffect(()=>{if(selected){dialog.current?.showModal();heading.current?.focus();}},[selected]);
  useEffect(()=>{if(!selected)return;const unload=(event:BeforeUnloadEvent)=>event.preventDefault();const navigate=(event:MouseEvent)=>{if((event.target as Element).closest?.('a[href]')){event.preventDefault();event.stopPropagation();}};window.addEventListener('beforeunload',unload);document.addEventListener('click',navigate,true);return()=>{window.removeEventListener('beforeunload',unload);document.removeEventListener('click',navigate,true);};},[selected]);
  function restoreFocus(){requestAnimationFrame(()=>{if(trigger.current?.isConnected)trigger.current.focus();else refresh.current?.focus();});}
  function close(){if(lock.current||uncertain)return;dialog.current?.close();setSelected(undefined);command.current=undefined;setError('');restoreFocus();}
  async function review(row:AgencyTermsRequest){if(lock.current||selected)return;trigger.current=document.activeElement as HTMLElement;lock.current=true;setBusy(true);setError('');setNotice('');
    try{const fresh=await agencyFetch<AgencyTermsRequest>(`/api/v1/agency-terms-requests/${row.id}`);if(fresh.data.agencyId!==id||!fresh.etag)throw Error('The terms request is unavailable.');setReason('');setStale(false);setUncertain(false);setSelected({...fresh.data,etag:fresh.etag});}
    catch(failure){setError((failure as Error).message);}finally{lock.current=false;setBusy(false);}}
  async function decide(outcome:'approve'|'reject'){
    if(lock.current||!selected||stale||!canDecideState(selected,actorId))return;
    if(!uncertain){try{command.current={id:selected.id,etag:selected.etag,key:crypto.randomUUID(),body:JSON.stringify({outcome,reason:userReason(reason)}),outcome};}catch(failure){setError((failure as Error).message);return;}}
    lock.current=true;setBusy(true);setError('');const pending=command.current!;
    try{await agencyFetch(`/api/v1/agency-terms-requests/${pending.id}/decision`,{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-Token':await csrfToken(),'If-Match':pending.etag,'Idempotency-Key':pending.key},body:pending.body});}
    catch(failure){const unknown=!(failure instanceof AgencyError)||failure.status>=500;setUncertain(unknown);setStale(!unknown&&failure instanceof AgencyError&&[409,412,428].includes(failure.status));setError(unknown?'The decision result is uncertain. Retry the same decision before leaving.':failure instanceof AgencyError&&[409,412,428].includes(failure.status)?'The request or agency changed. Your reason is retained. Close and reopen the request to review its current version.':(failure as Error).message);lock.current=false;setBusy(false);return;}
    lock.current=false;setBusy(false);setUncertain(false);dialog.current?.close();setSelected(undefined);command.current=undefined;setNotice(pending.outcome==='approve'?'Terms approved and published. They take effect on their agreed date.':'Terms proposal rejected. Existing approved versions are unchanged.');requests.refresh();restoreFocus();if(pending.outcome==='approve')onApplied();
  }
  return <Panel title="Terms proposals and decisions"><p className="match-copy">Review the full proposed replacement before deciding. A requester cannot approve or reject their own proposal.</p><button ref={refresh} className="button secondary" disabled={busy||!!selected} onClick={()=>{setCursors([]);requests.refresh();}}>Refresh terms proposals</button>
    {!requests.data?<LoadFeedback error={requests.error} retry={requests.refresh}/>:<><DataTable caption="Terms proposal history" columns={['Effective from','Requested by','Reason','Status','Decision','Actions']}>
      {requests.data.items.map(row=><tr key={row.id}><td>{row.effectiveFrom}</td><td>{row.requestedByLabel}<br/>{clientDate(row.createdAt,true)}</td><td>{row.reason}</td><td>{row.state}</td><td>{row.decisionByLabel?<>{row.decisionByLabel}<br/>{row.decisionReason}<br/>{row.decidedAt&&clientDate(row.decidedAt,true)}</>:row.state==='pending'?'Awaiting independent review':'No decision recorded'}</td><td><button className="button secondary" disabled={busy||!!selected} onClick={()=>void review(row)}>Review proposal</button></td></tr>)}
    </DataTable>{!requests.data.items.length&&<p className="match-copy">No terms proposals yet.</p>}<Paging total={requests.data.totalCount} previous={!selected&&cursors.length?()=>setCursors(x=>x.slice(0,-1)):undefined} next={!selected&&requests.data.nextCursor?()=>setCursors(x=>[...x,requests.data!.nextCursor!]):undefined}/></>}
    <dialog ref={dialog} className="agency-dialog agency-terms-dialog" aria-labelledby="terms-review-title" onCancel={event=>{event.preventDefault();close();}}><h2 id="terms-review-title" ref={heading} tabIndex={-1}>Review proposed terms</h2>{selected&&<><p>Requested by {selected.requestedByLabel} on {clientDate(selected.createdAt,true)}. Status: {selected.state}. Effective from {selected.effectiveFrom}.</p><p>{selected.reason}</p><TermsSnapshotDetails snapshot={selected} catalog={catalog.data?.items??[]} caption="Proposed products"/>{catalog.error&&<LoadFeedback error={catalog.error} retry={catalog.refresh}/>}
      {canDecideState(selected,actorId)?<><p>Approval publishes this complete version from the stated date. Rejection closes this proposal without changing existing terms.</p><label>Decision reason<input maxLength={1000} value={reason} disabled={busy||uncertain} onChange={event=>setReason(event.target.value)}/></label><div className="operations-actions">{uncertain?<button className="button button-primary" disabled={busy} onClick={()=>void decide(command.current!.outcome)}>Retry same decision</button>:<><button className="button button-primary" disabled={busy||stale||!catalog.data} onClick={()=>void decide('approve')}>Approve terms</button><button className="button secondary" disabled={busy||stale} onClick={()=>void decide('reject')}>Reject terms</button></>}</div></>:<p>{selected.state==='pending'?'Another administrator must decide this proposal.':`Decision: ${selected.decisionByLabel??'No reviewer recorded'} — ${selected.decisionReason??'No decision reason recorded'}`}</p>}</>}
      {error&&<p className="error-message" role="alert">{error}</p>}<button className="button secondary" disabled={busy||uncertain} onClick={close}>Close review</button>
    </dialog>{!selected&&error&&<p className="error-message" role="alert">{error}</p>}<p className="match-copy" role="status">{notice}</p>
  </Panel>;
}
