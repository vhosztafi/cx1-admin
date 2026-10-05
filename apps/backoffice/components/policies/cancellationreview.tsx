'use client';
import {useEffect,useRef,useState} from 'react';
import {Panel,Status,DataTable} from '../primitives';
import {quoteFetch,uncertainQuoteFailure} from '../../lib/quotes';
import {formatCancellationMoney as formatGbp,cancellationBlocker,cancellationCommand,sendCancellation,type CancellationCommand,type CancellationView,type CancellationEvidencePage} from '../../lib/cancellation-review';

const purposes=[['cancellation-request','Cancellation request'],['cancellation-notice','Cancellation notice'],['cancellation-reason','Cancellation reason evidence'],['insurer-instruction','Insurer instruction']];
const date=(value:string)=>new Date(value).toLocaleString('en-GB',{timeZone:'Europe/London'});

export function CancellationReview({draftId,etag,fence,editable,blocked,dirty,canReview,pendingChanged,saved}:{
  draftId:string;etag:string;fence:string|null;editable:boolean;blocked:boolean;dirty:boolean;canReview:boolean;
  pendingChanged:(value:boolean)=>void;saved:()=>Promise<void>;
}) {
  const [read,setRead]=useState<{view:CancellationView;evidence:CancellationEvidencePage;etag:string}|null>(null);
  const [error,setError]=useState(''),[notice,setNotice]=useState(''),[sending,setSending]=useState(false),[uncertain,setUncertain]=useState(false);
  const [file,setFile]=useState<File>(),[purpose,setPurpose]=useState('cancellation-request'),[delivered,setDelivered]=useState('');
  const [evidenceId,setEvidenceId]=useState(''),[reviewOutcome,setReviewOutcome]=useState('accepted'),[reviewReason,setReviewReason]=useState(''),[approvalReason,setApprovalReason]=useState('');
  const [issueReason,setIssueReason]=useState(''),[confirmedHash,setConfirmedHash]=useState<string|null>(null);
  const pending=useRef<CancellationCommand|null>(null),sendingRef=useRef(false),lastHash=useRef<string|null>(null);
  const root=`/api/v1/drafts/${draftId}`;
  useEffect(()=>{
    if(blocked||uncertain)return;const controller=new AbortController();let loading=false;
    async function refresh(){if(loading)return;loading=true;
      try{
        const [view,evidence]=await Promise.all([quoteFetch<CancellationView>(root+'/cancellation-preview',{signal:controller.signal}),quoteFetch<CancellationEvidencePage>(root+'/cancellation-evidence',{signal:controller.signal})]);
        if(controller.signal.aborted)return;
        if(view.etag!==etag||evidence.etag!==etag||view.data.draftId!==draftId||evidence.data.draftId!==draftId)return;
        if(lastHash.current&&lastHash.current!==view.data.previewHash)setNotice('The cancellation preview has changed. Review the new amounts and obtain fresh approval.');
        lastHash.current=view.data.previewHash;setRead({view:view.data,evidence:evidence.data,etag});setError('');
      }catch{if(!controller.signal.aborted){setRead(null);setError('Cancellation review is unavailable. Refresh before continuing.');}}
      finally{loading=false;}
    }
    void refresh();const timer=setInterval(()=>void refresh(),5000);return()=>{controller.abort();clearInterval(timer);};
  },[root,draftId,etag,blocked,uncertain]);
  const current=read?.etag===etag,active=!!(current&&editable&&fence&&!blocked&&!dirty&&!uncertain&&!sending);
  const view=read?.view,posting=view?.amounts?.posting;
  async function execute(path?:string,body?:unknown,upload?:{file:File;purpose:string;delivered?:string}){
    if(sendingRef.current||(!pending.current&&!active))return;sendingRef.current=true;setSending(true);setError('');setNotice('');
    try{
      if(!pending.current){pending.current=cancellationCommand(draftId,etag,fence!,path!,body,upload);pendingChanged(true);}
      await sendCancellation(pending.current);await saved();pending.current=null;setUncertain(false);pendingChanged(false);setConfirmedHash(null);setNotice('Cancellation action saved.');
    }catch(failure){
      if(!uncertain&&!uncertainQuoteFailure(failure)){pending.current=null;pendingChanged(false);setUncertain(false);setRead(null);}
      else setUncertain(true);
      setError(pending.current?'The result is unconfirmed. Retry the same action before continuing.':'The action could not be applied. Refresh the draft and check current authority and editing ownership.');
    }finally{sendingRef.current=false;setSending(false);}
  }
  return <section aria-label="Cancellation review" id="cancellation-review" className="cancellation-review">
    <Panel title="Cancellation review" note="Original posted amounts · GBP">
      <div className="quote-rail-body">
        <Status tone={view?.approvalId&&current?'success':'warning'}>{view?.approvalId&&current?'Cancellation approved':view?.previewId&&current?'Awaiting approval':'Proposal review'}</Status>
        <p>Review the credit and evidence before approving cancellation. Approval does not end cover or pay a refund.</p>
        {dirty?<p role="status">Save the proposal to calculate its current cancellation preview.</p>:null}
        {notice?<p role="status">{notice}</p>:null}{error?<p role="alert">{error}</p>:null}
        {uncertain?<button className="button button-primary" disabled={sending} onClick={()=>void execute()}>Retry same cancellation action</button>:null}
        {view?<><p>Effective {date(view.effectiveAt)} · Latest supported change boundary {date(view.supportedFrom)}</p>
          <p>Days on cover: {view.daysOnCover} of {view.termDays}. Basis: pro-rata by local calendar day, using each original posted component.</p>
          {view.noticeEffectiveFrom?<p>Earliest date after notice: {view.noticeEffectiveFrom}</p>:null}
          {view.blockers.length?<ul aria-label="Cancellation blockers">{view.blockers.map(code=><li key={code}>{cancellationBlocker(code)}</li>)}</ul>:null}</>:<p role="status">Loading the saved cancellation review…</p>}
      </div>
      {posting?<><DataTable caption="Cancellation financial components" columns={['Original component','Return / reversal','Coverage from','Coverage to']}>
        {posting.movements.map(row=><tr key={row.originalComponentId}><th scope="row">{row.code.replaceAll('-',' ')} {row.ordinal}</th><td>{formatGbp(row.amount)}</td><td>{date(row.startsAt)}</td><td>{date(row.endsAt)}</td></tr>)}
      </DataTable><div className="quote-rail-body"><dl className="quote-form-grid">
        <div><dt>Premium charged to date</dt><dd>{view!.premiumChargedToDate === null ? 'Unavailable' : formatGbp(view!.premiumChargedToDate)}</dd></div>
        <div><dt>Premium movement</dt><dd>{formatGbp(posting.premium)}</dd></div><div><dt>Tax movement</dt><dd>{formatGbp(posting.tax)}</dd></div>
        <div><dt>Commission reversal</dt><dd>{formatGbp(posting.commission)}</dd></div><div><dt>Debtor credit / charge</dt><dd>{formatGbp(posting.invoiceDue)}</dd></div>
        <div><dt>Economic net movement</dt><dd>{formatGbp(posting.netDue)}</dd></div><div><dt>Separate broker remuneration movement</dt><dd>{formatGbp(posting.brokerPayable)}</dd></div>
        <div><dt>Retained fees</dt><dd>{formatGbp(view!.amounts!.retainedFee)}</dd></div><div><dt>Retained broker fee share</dt><dd>{formatGbp(view!.amounts!.retainedFeeShare)}</dd></div>
      </dl><p>Negative amounts reduce the original charge. Retained fees are not charged again. Cash paid: £0.00. Any eventual refund depends on the debtor balance and separate Finance approval.</p></div></>:null}
      <div className="quote-rail-body"><a className="button" href="#cancellation-proposal">Amend reason or date</a><button className="button" disabled={!active||!view||view.blockers.length>0||!!view.previewId} onClick={()=>void execute('cancellation-preview',{previewHash:view!.previewHash})}>Retain reviewed preview</button>
      {canReview?<><label className="quote-form-label">Cancellation approval reason<textarea aria-label="Cancellation approval reason" minLength={10} maxLength={2000} disabled={!active} value={approvalReason} onChange={e=>setApprovalReason(e.target.value)} /></label>
        <button className="button button-primary" disabled={!active||!view?.previewId||!view.canApprove||!!view.approvalId||approvalReason.trim().length<10} onClick={()=>void execute('cancellation-approvals',{previewId:view!.previewId,previewHash:view!.previewHash,reason:approvalReason})}>Approve cancellation</button></>:null}
      <p className="client-help">Non-payment, non-disclosure and insurer instruction require a current senior approver different from the draft requester. The approver must acquire the editing lease.</p></div>
    </Panel>
    {canReview?<Panel title="Issue cancellation" note="Final confirmation"><div className="quote-rail-body">
      <p>Issuing ends cover at the approved effective time and records the reviewed charge or credit. Cash paid by this action: £0.00.</p>
      {view&&posting?<p>Effective {date(view.effectiveAt)} · Debtor movement {formatGbp(posting.invoiceDue)}</p>:null}
      <fieldset disabled={!active||!view?.approvalId||view.blockers.length>0}>
        <label>Cancellation issue reason<textarea aria-label="Cancellation issue reason" minLength={10} maxLength={2000} value={issueReason} onChange={e=>setIssueReason(e.target.value)}/></label>
        <label><input type="checkbox" checked={!!view&&confirmedHash===view.previewHash} onChange={e=>setConfirmedHash(e.target.checked?view!.previewHash:null)}/> I confirm the cancellation effective time and reviewed financial movement.</label>
        <button className="button button-primary" disabled={!view||confirmedHash!==view.previewHash||issueReason.trim().length<10} onClick={()=>void execute('cancellation-issue',{previewId:view!.previewId,approvalId:view!.approvalId,previewHash:view!.previewHash,reason:issueReason})}>Issue approved cancellation</button>
      </fieldset></div></Panel>:null}
    <Panel title="Cancellation evidence" note="Applies to this saved revision">
      <div className="quote-rail-body"><fieldset disabled={!active} className="quote-form-grid">
        <label>Evidence purpose<select aria-label="Cancellation evidence purpose" value={purpose} onChange={e=>setPurpose(e.target.value)}>{purposes.map(([code,label])=><option value={code} key={code}>{label}</option>)}</select></label>
        <label>Evidence file<input aria-label="Cancellation evidence file" type="file" accept=".pdf,.png,.jpg,.jpeg,.txt" onChange={e=>setFile(e.target.files?.[0])} /></label>
        {purpose==='cancellation-notice'?<label>Notice delivered at (UTC)<input aria-label="Notice delivered at (UTC)" type="datetime-local" value={delivered} onChange={e=>setDelivered(e.target.value)} /></label>:null}
      </fieldset><button className="button" disabled={!active||!file} onClick={()=>void execute('cancellation-evidence/uploads',undefined,{file:file!,purpose,delivered:purpose==='cancellation-notice'&&delivered?delivered+':00Z':undefined})}>Upload cancellation evidence</button></div>
      <DataTable caption="Cancellation evidence history" columns={['Document','Purpose','Review','Notice delivery']}>
        {read?.evidence.items.map(item=><tr key={item.id}><td><a href={`${root}/evidence-files/${item.fileId}/content`} target="_blank" rel="noreferrer">{item.fileName}</a></td><td>{item.purpose.replaceAll('-',' ')}</td><td>{item.reviewState}</td><td>{item.noticeDeliveredAt?date(item.noticeDeliveredAt):'Not recorded'}</td></tr>)}
      </DataTable>
      {canReview?<div className="quote-rail-body"><fieldset disabled={!active} className="quote-form-grid">
        <label>Evidence to review<select aria-label="Cancellation evidence to review" value={evidenceId} onChange={e=>setEvidenceId(e.target.value)}><option value="">Choose evidence</option>{read?.evidence.items.map(item=><option value={item.id} key={item.id}>{item.fileName} · {item.reviewState}</option>)}</select></label>
        <label>Review outcome<select aria-label="Cancellation evidence outcome" value={reviewOutcome} onChange={e=>setReviewOutcome(e.target.value)}><option value="accepted">Accepted</option><option value="rejected">Rejected</option></select></label>
        <label>Review reason<textarea aria-label="Cancellation evidence review reason" minLength={10} maxLength={2000} value={reviewReason} onChange={e=>setReviewReason(e.target.value)} /></label>
      </fieldset><button className="button" disabled={!active||!evidenceId||reviewReason.trim().length<10} onClick={()=>void execute(`cancellation-evidence/${evidenceId}/reviews`,{outcome:reviewOutcome,reason:reviewReason})}>Save cancellation evidence review</button></div>:null}
    </Panel>
  </section>;
}
