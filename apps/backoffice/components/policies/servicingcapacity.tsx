'use client';
import Link from 'next/link';
import { useState } from 'react';
import { Status } from '../primitives';
import { PageButtons, useProofRead } from './servicing-proof-read';
import type { QuoteCaptureProposal } from '../../lib/quotes';
import type { ServicingEditor } from '../../lib/servicing-api';
import type { ProofAssociation, ProofRequirement } from '../../lib/servicing-proof';
import type { ServicingReferral } from '../../lib/servicing-referrals';
import { carrierDate, carrierLabel, type CarrierCases, type CarrierDetail, type CarrierRun } from '../../lib/servicing-capacity';
import { CarrierCorrespondence, CarrierResolutionPanel } from './servicing-capacity-history';
import { CarrierSend, CarrierSuppliedResponse } from './servicing-capacity-forms';

export function ServicingCapacity({draftId,etag,cycleId,referral,active,paused,requirements,evidence,editor,run}:{
  draftId:string;etag:string;cycleId:string;referral:ServicingReferral;active:boolean;paused:boolean;requirements:ProofRequirement[];evidence:ProofAssociation[];editor:ServicingEditor<QuoteCaptureProposal>|null;run:CarrierRun;
}) {
 const [open,setOpen]=useState(false),[reason,setReason]=useState(''),[selected,setSelected]=useState(''),[page,setPage]=useState({etag,cursor:''});
 const cursor=page.etag===etag?page.cursor:'';const base=`/api/v1/drafts/${draftId}`;
 const cases=useProofRead<CarrierCases>(open?`${base}/capacity?referralId=${referral.id}&pageSize=50${cursor?'&cursor='+encodeURIComponent(cursor):''}`:null,etag,paused);
 const matching=cases.data?.items.filter(item=>item.referralId===referral.id)??[];
 const chosen=matching.find(item=>item.id===selected)??matching[0];
 const read=useProofRead<CarrierDetail>(open&&chosen?`${base}/capacity/${chosen.id}`:null,etag,paused);
 const current=active&&cases.current&&read.current&&!!read.data?.current&&read.data.case.cycleId===cycleId;
 return <details open={open} onToggle={event=>setOpen(event.currentTarget.open)} className="servicing-capacity"><summary>Carrier capacity request</summary>
  <p className="client-help">Obtain a carrier response for this rated change. Internal approval, required proof and issue remain separate checks.</p>
  {cases.error&&<p role="status">{cases.error}</p>}
  {cases.data&&!matching.length&&<><p>No carrier request for this referral on this page.</p><fieldset disabled={!active||!cases.current||!!cursor||!!cases.data.nextCursor} className="quote-reference-fields"><legend>Create a carrier request</legend>
   <label>Capacity request reason<textarea aria-label="Capacity request reason" value={reason} maxLength={2000} onChange={event=>setReason(event.target.value)}/></label>
   <button className="button" disabled={reason.trim().length<10} onClick={()=>run('/capacity',{cycleId,referralId:referral.id,referralEtag:referral.etag,reason})}>Create carrier request</button>
  </fieldset></>}
  {matching.length>1&&<label>Saved carrier request<select value={chosen?.id??''} onChange={event=>setSelected(event.target.value)}>{matching.map(item=><option key={item.id} value={item.id}>{carrierDate(item.raisedAt)} · {carrierLabel(item.state)}</option>)}</select></label>}
  {cases.data&&<PageButtons cursor={cursor} next={cases.data.nextCursor} disabled={paused} change={value=>setPage({etag,cursor:value})}/>}
  {cases.error&&<button className="button" disabled={paused} onClick={()=>setPage({etag,cursor:''})}>Restart capacity pages</button>}
  {read.error&&<p role="status">{read.error}</p>}
  {read.data&&chosen&&<CarrierWorkspace key={chosen.id} view={read.data} etag={etag} active={current&&read.data.canWrite} paused={paused} requirements={requirements} evidence={evidence} editor={editor} run={run}/>}
 </details>;
}

function CarrierWorkspace({view,etag,active,paused,requirements,evidence,editor,run}:{view:CarrierDetail;etag:string;active:boolean;paused:boolean;requirements:ProofRequirement[];evidence:ProofAssociation[];editor:ServicingEditor<QuoteCaptureProposal>|null;run:CarrierRun}) {
 const item=view.case;const [tab,setTab]=useState('request'),[action,setAction]=useState(''),[reason,setReason]=useState(''),[senior,setSenior]=useState('');
 const base=`/api/v1/drafts/${item.draftId}/capacity/${item.id}`;
 const withdraw=['queued','sent','queried','failed'].includes(item.state),reopen=['approved','conditional','declined'].includes(item.state);
 const common={cycleId:item.cycleId,caseEtag:item.etag};
 return <section aria-label="Servicing carrier case" data-capacity-id={item.id} className="capacity-workspace">
  <div className="capacity-message-heading"><h4>{item.providerLabel} · {carrierLabel(item.dimension)}</h4><Status tone={view.ready?'success':item.state==='declined'?'error':'warning'}>{carrierLabel(item.state)}</Status></div>
  <p>{item.reason}</p><p><Link href={`/drafts/${item.draftId}`}>Parent servicing draft</Link> · Raised {carrierDate(item.raisedAt)}</p>
  <p role="status">{!view.current?'Historical carrier request. Its response cannot authorise the current change.':view.ready?'Current carrier extent and condition proof are satisfied. Overall issue checks still apply.':'Carrier permission or its condition proof remains outstanding.'}</p>
  {item.assignedUserId&&<p>Senior review: {view.seniors.find(x=>x.id===item.assignedUserId)?.label??'Previously assigned staff member'}</p>}
  {view.submission&&<p>Submitted {carrierDate(view.submission.submittedAt)} · Response due {carrierDate(view.submission.responseDueAt)}<br/>Demo processing: {carrierLabel(view.submission.jobState)} · {view.submission.attempts} attempts{view.submission.errorCode&&` · ${carrierLabel(view.submission.errorCode)}`}</p>}
  <div className="quote-row-actions" aria-label="Carrier case sections"><button className="button" aria-pressed={tab==='request'} onClick={()=>setTab('request')}>Request and correspondence</button><button className="button" aria-pressed={tab==='authority'} onClick={()=>setTab('authority')}>Carrier authority context</button></div>
  {tab==='authority'?<><p>Permission applies only to the current submission and the recorded extent below. Review your current authority and binder limits in the referral section.</p>
   {view.response?<><h4>{carrierLabel(view.response.outcome)}</h4><p>{view.response.body}</p>{view.response.definition.validFrom&&<p>Valid {carrierDate(view.response.definition.validFrom)} to {carrierDate(view.response.definition.validTo!)}</p>}
    {view.response.definition.authorisedLimits.map((limit,index)=><p key={index}>{carrierLabel(limit.dimension)}: {limit.maximumAmount?`GBP ${limit.maximumAmount}`:limit.dimension==='driver-age'?`${limit.minimumAge}–${limit.maximumAge} years`:`Permission for ${limit.questionId}`}</p>)}</>:<p>No carrier response recorded.</p>}
   <h4>Similar cases on this policy</h4><p className="client-help">Same provider, binder and referral rule. Previous outcomes are context only and confer no permission.</p>
   {view.similarCases.length?view.similarCases.map(other=><p key={other.id}><Link href={`/drafts/${other.draftId}#servicing-referrals`}>{carrierDate(other.raisedAt)} · {carrierLabel(other.state)}</Link> · {other.reason}</p>):<p>No matching prior cases.</p>}
  </>:<><CarrierCorrespondence base={base} etag={etag} paused={paused}/>
   <CarrierSend view={view} active={active} evidence={evidence} requirements={requirements} run={run}/>
   <CarrierSuppliedResponse view={view} active={active} evidence={evidence} requirements={requirements} editor={editor} run={run}/>
  </>}
  {view.conditions.map(condition=><CarrierResolutionPanel key={condition.id} condition={condition} base={base} etag={etag} cycleId={item.cycleId} active={active&&item.state==='conditional'} paused={paused} requirements={requirements} evidence={evidence} run={run}/>)}
  <fieldset className="quote-reference-fields" disabled={!active}><legend>Manage carrier request</legend>
   <label>Carrier request action<select aria-label="Carrier request action" value={action} onChange={event=>setAction(event.target.value)}><option value="">Select an action</option>{withdraw&&<option value="withdraw">Withdraw request</option>}{reopen&&<option value="reopen">Reopen request</option>}<option value="assign">Assign senior review</option></select></label>
   {action==='assign'&&<label>Senior reviewer<select aria-label="Senior reviewer" value={senior} onChange={event=>setSenior(event.target.value)}><option value="">Choose an eligible senior</option>{view.seniors.map(option=><option key={option.id} value={option.id}>{option.label}</option>)}</select></label>}
   {action&&<p>{action==='assign'?'Assignment routes internal work. It does not extend authority.':'This returns the request to draft and removes its permission. All correspondence remains in history; a new submission is required.'}</p>}
   <label>Carrier action reason<textarea aria-label="Carrier action reason" value={reason} maxLength={2000} onChange={event=>setReason(event.target.value)}/></label>
   <button className="button" disabled={reason.trim().length<10||!action||(action==='assign'?!senior:action==='withdraw'?!withdraw:!reopen)} onClick={()=>run(`/capacity/${item.id}/${action==='assign'?'assignment':'actions'}`,{...common,reason,...(action==='assign'?{assignedUserId:senior}:{action})})}>{action==='assign'?'Assign senior review':action==='withdraw'?'Confirm withdrawal':action==='reopen'?'Confirm reopen':'Save carrier action'}</button>
  </fieldset>
 </section>;
}
