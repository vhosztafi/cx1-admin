'use client';
import { useEffect, useState } from 'react';
import { csrfToken, type Actor } from '../../lib/auth';
import { uncertainQuoteFailure } from '../../lib/quotes';
import { incidentCommand, incidentFetch, sendIncidentCommand, IncidentError, type IncidentCommand, type OpsIncident, type OpsIncidentDraftWrite, type IncidentOptions, type IncidentAction } from '../../lib/incidents-api';
import { Panel } from '../primitives';
import { IncidentFields, incidentLabel } from './incident-fields';
import { useQuoteResource } from '../quotes/shared';
import { IncidentEvidence } from './incident-evidence';
import { ClaimsSummaryPanel } from './claims-summary';
import type { OpsClaimsHandoff } from '../../../../contracts/generated/operations';

export function IncidentEditor({initial,initialEtag,policyId,productCode,cancel,saved}:{initial?:OpsIncident;initialEtag?:string;policyId:string;productCode:OpsIncidentDraftWrite['productCode'];cancel:()=>void;saved:()=>void}){
  const [draft,setDraft]=useState<OpsIncidentDraftWrite>(initial?.draft??{policyId,productCode});
  const [record,setRecord]=useState(initial),[etag,setEtag]=useState(initialEtag),[pending,setPending]=useState<IncidentCommand>(),[busy,setBusy]=useState(false),[error,setError]=useState(''),[notice,setNotice]=useState(''),[stale,setStale]=useState(false);
  const [day,setDay]=useState(draft.occurrence?.occurredOn??''),[precision,setPrecision]=useState(draft.occurrence?.precision??'date'),[approximate,setApproximate]=useState(draft.occurrence?.precision==='approximate'?draft.occurrence.approximateLocalTime:''),[exact,setExact]=useState(draft.occurrence?.precision==='exact'?draft.occurrence.occurredAt:''),[reason,setReason]=useState('');
  const [versionChoice,setVersionChoice]=useState(''),[claimsBlocked,setClaimsBlocked]=useState(false);
  useEffect(()=>{
    if(!pending)return;
    const warn=()=>setError('Retry the same action to confirm its result before leaving.');
    const unload=(event:BeforeUnloadEvent)=>{event.preventDefault();event.returnValue='';};
    const click=(event:MouseEvent)=>{if((event.target as Element).closest?.('a[href], [role="tab"], .account-dropdown button')){event.preventDefault();event.stopPropagation();warn();}};
    const navigation=(window as Window&{navigation?:EventTarget}).navigation;
    const navigate=(event:Event)=>{if(event.cancelable){event.preventDefault();warn();}};
    window.addEventListener('beforeunload',unload);document.addEventListener('click',click,true);navigation?.addEventListener('navigate',navigate);
    return()=>{window.removeEventListener('beforeunload',unload);document.removeEventListener('click',click,true);navigation?.removeEventListener('navigate',navigate);};
  },[pending]);
  const failedHandoffs=useQuoteResource<{items:OpsClaimsHandoff[]}>(record?.state==='failed'?`/api/v1/incidents/${record.id}/handoffs?pageSize=10`:null);
  const unresolvedFailure=record?.state==='failed'&&!failedHandoffs.data?.items.some(x=>x.revisionId===record.revisionId&&x.state==='rejected');
  const source=versionChoice||record?.resolution?.candidates[0]?.versionId;
  const options=useQuoteResource<IncidentOptions>(record?.resolution&&source?`/api/v1/incidents/${record.id}/subject-options?versionId=${source}`:null);
  const dirty=JSON.stringify(draft)!==JSON.stringify(record?.draft)||day!==(record?.draft.occurrence?.occurredOn??'')||precision!==(record?.draft.occurrence?.precision??'date')||precision==='approximate'&&approximate!==(record?.draft.occurrence?.precision==='approximate'?record.draft.occurrence.approximateLocalTime:'')||precision==='exact'&&exact!==(record?.draft.occurrence?.precision==='exact'?record.draft.occurrence.occurredAt:'');
  const observed=()=>{
    if(!day)return undefined;
    if(precision==='approximate'&&!approximate)throw new Error('Enter the approximate time, or choose date only.');
    if(precision==='exact'&&(!exact||!/(Z|[+-]\d{2}:\d{2})$/.test(exact)))throw new Error('Enter the confirmed time with its UTC offset, for example 2026-09-16T12:30:00+01:00.');
    return precision==='date'?{occurredOn:day,timeZone:'Europe/London' as const,precision}:{occurredOn:day,timeZone:'Europe/London' as const,...(precision==='approximate'?{precision,approximateLocalTime:approximate}:{precision,occurredAt:exact})};
  };
  async function act(action:IncidentAction,existing?:IncidentCommand){
    setBusy(true);setError('');setNotice('');
    try{
      const account=await incidentFetch<Actor>('/api/v1/account');
      let command=existing;
      if(!command){
        const occurrence=['create','update','occurrence'].includes(action)?observed():undefined,body=action==='create'||action==='update'?{...draft,occurrence}:action==='description'?{description:draft.description??''}:action==='occurrence'?{occurrence,reason}:{};
        command=incidentCommand(action,record?.id??policyId,body,etag,account.data.id);setPending(command);
      }
      const response=await sendIncidentCommand(command,await csrfToken(),account.data.id);
      const current='draft' in response.data?response:await incidentFetch<OpsIncident>(`/api/v1/incidents/${record!.id}`);
      const value=current.data as OpsIncident;setRecord(value);setEtag(current.etag??undefined);setPending(undefined);setStale(false);setVersionChoice('');
      if(command.action==='occurrence'){setDraft(currentDraft=>({...currentDraft,occurrence:value.draft.occurrence}));}
      else if(command.action!=='description'){setDraft(value.draft);setDay(value.draft.occurrence?.occurredOn??'');setPrecision(value.draft.occurrence?.precision??'date');setApproximate(value.draft.occurrence?.precision==='approximate'?value.draft.occurrence.approximateLocalTime:'');setExact(value.draft.occurrence?.precision==='exact'?value.draft.occurrence.occurredAt:'');}
      setNotice(command.action==='log'?'Incident logged without sending.':command.action==='description'?'Description saved. Other edits remain in this form.':command.action==='occurrence-resolution'?'Historical cover checked.':'Incident draft saved.');saved();
    }catch(caught){setError(caught instanceof Error?caught.message:'The result is unconfirmed. Retry the same action.');if(!existing&&!uncertainQuoteFailure(caught)){setPending(undefined);if(caught instanceof IncidentError&&[409,412,428].includes(caught.status))setStale(true);}}
    finally{setBusy(false);}
  }
  async function review(){
    if(!record)return;setBusy(true);try{const current=await incidentFetch<OpsIncident>(`/api/v1/incidents/${record.id}`);setRecord(current.data);setEtag(current.etag??undefined);setStale(false);setError('');setNotice('Latest saved version loaded below. Your form edits are retained; review them before saving.');}catch(caught){setError(caught instanceof Error?caught.message:'Unable to load the latest version.');}finally{setBusy(false);}
  }
  return <Panel title={record?`${record.reference} · ${record.state==='logged'?'Logged, unsent':incidentLabel(record.state)}`:'Log an incident'} note="Capture the report before sending to the claims administrator."><div className="quote-rail-body incident-editor">
    {error&&<p role="alert" className="form-error">{error}</p>}{notice&&<p role="status">{notice}</p>}
    {unresolvedFailure&&<p role="status">This submission has no definitive rejection. Keep its saved facts and retry the existing claims request below.</p>}
    {stale&&<button className="button" disabled={busy} onClick={()=>void review()}>Review latest saved version</button>}
    {pending&&!busy&&<button className="button" onClick={()=>void act(pending.action,pending)}>Retry unconfirmed action</button>}
    <fieldset disabled={busy||!!pending||claimsBlocked||stale||unresolvedFailure||record?.state==='queued'||record?.state==='handed-off'}>
      <fieldset className="quote-reference-fields"><legend>When did it happen?</legend>
        <label>Occurrence date<input aria-label="Occurrence date" type="date" value={day} onChange={event=>setDay(event.target.value)}/></label>
        <label>Time precision<select aria-label="Time precision" value={precision} onChange={event=>setPrecision(event.target.value as typeof precision)}><option value="date">Date only</option><option value="approximate">Approximate time</option><option value="exact">Confirmed exact time</option></select></label>
        {precision==='approximate'&&<label>Approximate time<input aria-label="Approximate time" type="time" value={approximate} onChange={event=>setApproximate(event.target.value)}/></label>}
        {precision==='exact'&&<label>Confirmed time with UTC offset<input aria-label="Confirmed time with UTC offset" value={exact} placeholder="2026-09-16T12:30:00+01:00" onChange={event=>setExact(event.target.value)}/></label>}
        <p className="client-help">Dates and approximate times use Europe/London. Approximate times preserve the full day when checking historical cover.</p>
      </fieldset>
      <IncidentFields draft={draft} change={setDraft} options={options.data?.revisionId===record?.revisionId?options.data:undefined}/>
      <IncidentEvidence policyId={policyId} selected={draft.evidenceDocumentVersionIds??[]} change={ids=>setDraft({...draft,evidenceDocumentVersionIds:ids})}/>
      <div className="quote-row-actions"><button className="button primary" onClick={()=>void act(record?'update':'create')}>Save draft</button><button className="button" onClick={()=>void act(record?'update':'create')}>Save without sending</button><button className="button" disabled={!record||!draft.description?.trim()} onClick={()=>void act('description')}>Save description</button></div>
      {record&&<><div className="quote-row-actions"><button className="button" disabled={dirty} onClick={()=>void act('occurrence-resolution')}>Check historical cover</button><button className="button" disabled={dirty||record.missing.length>0} onClick={()=>void act('log')}>Log without sending</button></div><p className="client-help">Save edits before checking cover or logging.</p>
        <details><summary>Clarify the occurrence</summary><label>Reason for clarification<textarea aria-label="Occurrence clarification reason" value={reason} maxLength={1000} onChange={event=>setReason(event.target.value)}/></label><button className="button" disabled={!day||!reason.trim()} onClick={()=>void act('occurrence')}>Save occurrence clarification</button></details></>}
    </fieldset>
    {record&&<><h3>Readiness</h3>{record.missing.length?<ul>{record.missing.map(x=><li key={x}>{incidentLabel(x)} required</li>)}</ul>:<p>{record.state==='queued'?'Handoff queued; awaiting acknowledgement.':record.state==='handed-off'?'Acknowledged by the claims administrator.':record.state==='failed'?'Review the claims outcome before correcting or retrying.':'Ready to log. Nothing has been sent.'}</p>}
      {record.resolution&&<><p>Historical cover: {incidentLabel(record.resolution.state)}</p>{record.resolution.state==='ambiguous'&&<p>More than one version applies to the reported day. Clarify the confirmed occurrence before logging; selecting a version here only changes the choices shown.</p>}
        {!!record.resolution.candidates.length&&<label>Historical choices<select aria-label="Historical choices" value={source??''} onChange={event=>setVersionChoice(event.target.value)}>{[...new Map(record.resolution.candidates.map(x=>[x.versionId,x])).values()].map(x=><option key={x.versionId} value={x.versionId}>{x.label}</option>)}</select></label>}
      </>}
      <details><summary>Latest saved report</summary><p>{record.draft.description??'No description recorded'}</p><p>Updated {new Date(record.updatedAt).toLocaleString('en-GB')}</p></details></>}
    {record&&etag&&<ClaimsSummaryPanel record={record} etag={etag} disabled={busy||!!pending||dirty||stale} blocked={setClaimsBlocked} updated={(value,nextEtag)=>{setRecord(value);setEtag(nextEtag);saved();}}/>}
    <button className="button" disabled={busy||!!pending||claimsBlocked} onClick={cancel}>Cancel / back to claims</button>
  </div></Panel>;
}
