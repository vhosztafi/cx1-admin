'use client';
import { useState } from 'react';
import { Status } from '../primitives';
import { PageButtons, useProofRead } from './servicing-proof-read';
import { conditionProof } from '../../lib/servicing-referrals';
import type { ProofAssociation, ProofPage, ProofRequirement } from '../../lib/servicing-proof';
import { carrierDate, carrierLabel, type CarrierCondition, type CarrierMessage, type CarrierResponse, type CarrierResolution, type CarrierRun, type CarrierSubmission } from '../../lib/servicing-capacity';

export function CarrierCorrespondence({base,etag,paused}:{base:string;etag:string;paused:boolean}) {
 const [kind,setKind]=useState('messages'),[page,setPage]=useState({etag,kind:'messages',cursor:''});
 const cursor=page.etag===etag&&page.kind===kind?page.cursor:'';
 const history=useProofRead<ProofPage<CarrierMessage|CarrierResponse|CarrierSubmission>>(`${base}/${kind}?pageSize=10${cursor?'&cursor='+encodeURIComponent(cursor):''}`,etag,paused);
 return <section aria-label="Carrier correspondence"><h4>Retained correspondence</h4>
  <label>Carrier history<select aria-label="Carrier history" value={kind} onChange={event=>setKind(event.target.value)}><option value="messages">Requests, chases and replies</option><option value="responses">Carrier responses</option><option value="submissions">Submitted versions</option></select></label>
  {history.error&&<p role="status">{history.error}</p>}
  {history.error&&cursor&&<button className="button" disabled={paused} onClick={()=>setPage({etag,kind,cursor:''})}>Restart carrier history</button>}
  {history.data&&<>{!history.data.items.length&&<p>No saved entries.</p>}{history.data.items.map(row=><article className="capacity-message" key={row.id}>
   <strong>{'outcome' in row?carrierLabel(row.outcome):'kind' in row?carrierLabel(row.kind):`Submission ${row.sequence}`}</strong>
   <p className="client-help">{carrierDate('submittedAt' in row?row.submittedAt:row.recordedAt)}{'recordedByLabel' in row&&` · ${row.recordedByLabel}`}</p><p className="capacity-correspondence">{row.body}</p>
   {'provenance' in row&&<><p>{carrierLabel(row.provenance)} · {row.applicationState==='superseded'?'Retained historical response':'Applied when recorded; current applicability is shown above'}</p><p>{row.providerUnderwriter} · {row.providerReference} · Received {carrierDate(row.receivedAt)}</p>
    {row.definition.validFrom&&<p>Valid {carrierDate(row.definition.validFrom)} to {carrierDate(row.definition.validTo!)}</p>}
    {row.definition.authorisedLimits.map((limit,index)=><p key={index}>{carrierLabel(limit.dimension)}: {limit.maximumAmount?`GBP ${limit.maximumAmount}`:limit.dimension==='driver-age'?`${limit.minimumAge}–${limit.maximumAge} years`:`Permission for ${limit.questionId}`}</p>)}
    {row.definition.conditions.map((condition,index)=><p key={index}>{carrierLabel(condition.definition.code)} · {condition.effectiveDates.map(carrierDate).join('; ')}</p>)}</>}
   {'jobState' in row&&<><p>Demo processing: {carrierLabel(row.jobState)} · {row.attempts} attempts · Due {carrierDate(row.responseDueAt)}</p><p>{row.reason}</p><details><summary>Submission provenance</summary><p>Reference: {row.id}</p><p className="client-help">Immutable context: {row.contextHash}</p><p>{row.evidenceIds.length} selected documents</p></details></>}
  </article>)}<PageButtons cursor={cursor} next={history.data.nextCursor} disabled={paused} change={value=>setPage({etag,kind,cursor:value})}/></>}
 </section>;
}

export function CarrierResolutionPanel({condition,base,etag,cycleId,active,paused,requirements,evidence,run}:{condition:CarrierCondition;base:string;etag:string;cycleId:string;active:boolean;paused:boolean;requirements:ProofRequirement[];evidence:ProofAssociation[];run:CarrierRun}) {
 const [proof,setProof]=useState(''),[reason,setReason]=useState(''),[outcome,setOutcome]=useState('satisfied'),[open,setOpen]=useState(false),[page,setPage]=useState({etag,cursor:''});
 const cursor=page.etag===etag?page.cursor:'';
 const options=evidence.filter(item=>requirements.some(required=>conditionProof({...condition,clauses:condition.effectiveDates.map(effectiveAt=>({effectiveAt,wording:condition.wording,endorsementCode:null,targetIds:[]}))},required,item,outcome)));
 const history=useProofRead<ProofPage<CarrierResolution>>(open?`${base}/conditions/${condition.id}/resolutions?pageSize=10${cursor?'&cursor='+encodeURIComponent(cursor):''}`:null,etag,paused);
 return <section data-carrier-condition-id={condition.id}><h4>{condition.wording}</h4><Status tone={condition.satisfied?'success':'warning'}>{condition.satisfied?'Carrier condition satisfied':'Carrier condition outstanding'}</Status>
  <p>Applies on {condition.effectiveDates.map(carrierDate).join('; ')}</p><p className="client-help">Upload, attach and review current-purpose proof in Supporting information, then record the explicit resolution here.</p>
  <fieldset className="quote-reference-fields" disabled={!active}><legend>Resolve carrier condition</legend>
   <label>Carrier resolution outcome<select aria-label="Carrier resolution outcome" value={outcome} onChange={event=>setOutcome(event.target.value)}><option value="satisfied">Satisfied</option><option value="rejected">Rejected</option></select></label>
   <label>Reviewed carrier condition proof<select aria-label="Reviewed carrier condition proof" value={proof} onChange={event=>setProof(event.target.value)}><option value="">Choose current reviewed proof</option>{options.map(item=><option key={item.id} value={item.id}>{item.fileName} · {item.reviewOutcome}</option>)}</select></label>
   <label>Carrier resolution reason<textarea aria-label="Carrier resolution reason" value={reason} maxLength={2000} onChange={event=>setReason(event.target.value)}/></label>
   <button className="button" disabled={reason.trim().length<10||!options.some(item=>item.id===proof)} onClick={()=>run(`/capacity-conditions/${condition.id}/resolutions`,{cycleId,conditionEtag:condition.etag,evidenceAssociationId:proof,outcome,reason})}>Record carrier condition resolution</button>
  </fieldset>
  <details open={open} onToggle={event=>setOpen(event.currentTarget.open)}><summary>Carrier condition resolution history</summary>{history.error&&<p role="status">{history.error}</p>}
   {history.error&&cursor&&<button className="button" onClick={()=>setPage({etag,cursor:''})}>Restart resolution history</button>}
   {history.data&&<>{!history.data.items.length&&<p>No explicit resolution recorded.</p>}{history.data.items.map(item=><p key={item.id}>{carrierLabel(item.outcome)} · {carrierDate(item.recordedAt)}<br/>{item.reason}</p>)}<PageButtons cursor={cursor} next={history.data.nextCursor} disabled={paused} change={value=>setPage({etag,cursor:value})}/></>}
  </details>
 </section>;
}
