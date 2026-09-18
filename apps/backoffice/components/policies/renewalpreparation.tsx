'use client';
import {useEffect,useRef,useState} from 'react';
import {Panel,Status,DataTable} from '../primitives';
import {QuoteError,quoteFetch,uncertainQuoteFailure} from '../../lib/quotes';
import {formatGbp} from '../../lib/underwriting-api';
import {renewalCommand,sendRenewal,renewalExperienceDisplay,type RenewalCommand,type RenewalWorkspace,type RenewalExperienceView,type RenewalExperienceFacts} from '../../lib/renewal-preparation';
import type {ServicingRatingHistory} from '../../lib/servicing-rating';
import type {ServicingEditor} from '../../lib/servicing-api';

const date=(value:string)=>new Date(value).toLocaleString('en-GB',{timeZone:'Europe/London'});
const emptyFacts={observationStartsOn:'',observationEndsOn:'',claimCount:Number.NaN,paid:'',outstanding:'',earnedPremium:'',sourceCode:'agency' as const,sourceReference:'',evidenceAssociationId:''};
const blockers:Record<string,string>={
  'renewal-preparation-required':'Choose and save the renewal term before rating.',
  'renewal-preparation-stale':'Published terms or the policy have changed. Review and save fresh preparation.',
  'renewal-experience-review-stale':'The evidence review no longer has current authority. Obtain a fresh underwriting review.',
  'renewal-base-stale':'The issued risk has changed. Start a renewal from the current term-end risk.',
  'renewal-draft-closed':'This renewal draft is closed.',
  'renewal-configuration-unavailable':'Renewal configuration is unavailable. Ask an administrator to review it.'
};

function useRenewalRead<T>(url:string,etag:string,paused:boolean) {
  const [read,setRead]=useState<{url:string;etag:string;data:T}|null>(null),[error,setError]=useState('');
  useEffect(()=>{
    if(paused)return;const controller=new AbortController();let loading=false;
    async function refresh(){if(loading)return;loading=true;try{
      const result=await quoteFetch<T&{draftEtag?:string}>(url,{signal:controller.signal});
      if(controller.signal.aborted)return;
      if((result.data.draftEtag??result.etag)!==etag)throw new Error('Draft changed');
      setRead({url,etag,data:result.data});setError('');
    }catch(failure){if(!controller.signal.aborted){if(failure instanceof QuoteError&&[401,403].includes(failure.status))setRead(null);
      setError('Saved renewal information is unavailable. Refresh before continuing.');}}
    finally{loading=false;}}
    void refresh();const timer=setInterval(()=>void refresh(),5000);return()=>{controller.abort();clearInterval(timer);};
  },[url,etag,paused]);
  return {data:read?.url===url?read.data:null,current:!!read&&read.url===url&&read.etag===etag&&!error,error};
}

export function RenewalPreparation({draftId,policyId,editor,etag,fence,editable,blocked,dirty,canReview,pendingChanged,saved,readyChanged}:{
  policyId:string;editor:ServicingEditor|null;
  draftId:string;etag:string;fence:string|null;editable:boolean;blocked:boolean;dirty:boolean;canReview:boolean;
  pendingChanged:(value:boolean)=>void;saved:()=>Promise<void>;readyChanged:(value:{etag:string;ready:boolean})=>void;
}) {
  const [sending,setSending]=useState(false),[pendingState,setPendingState]=useState(false),[error,setError]=useState(''),[notice,setNotice]=useState('');
  const pending=useRef<RenewalCommand|null>(null),sendingRef=useRef(false);
  const [months,setMonths]=useState(''),[offset,setOffset]=useState(''),[file,setFile]=useState<File>();
  const [facts,setFacts]=useState<RenewalExperienceFacts>(emptyFacts),[factsEdited,setFactsEdited]=useState(false),[association,setAssociation]=useState('');
  const [reviewReason,setReviewReason]=useState(''),[reviewOutcome,setReviewOutcome]=useState<'accepted'|'rejected'>('accepted');
  const root=`/api/v1/drafts/${draftId}/renewal`,paused=blocked||pendingState;
  const workspace=useRenewalRead<RenewalWorkspace>(root+'/preparation',etag,paused);
  const experience=useRenewalRead<RenewalExperienceView>(root+'/experience',etag,paused);
  const ratings=useRenewalRead<ServicingRatingHistory>(`/api/v1/drafts/${draftId}/ratings`,etag,paused);
  const current=workspace.current&&experience.current;
  const active=editable&&!!fence&&!blocked&&!pendingState&&!dirty&&current;
  const ready=!!(current&&workspace.data?.current);
  useEffect(()=>{readyChanged({etag,ready});},[etag,ready,readyChanged]);
  const recorded=experience.data?.experience;
  const form=factsEdited?facts:recorded??emptyFacts;
  const selectedMonths=months||String(workspace.data?.preparation?.termMonths??workspace.data?.defaultTermMonths??'');
  const display=renewalExperienceDisplay(experience.data??{experience:null,review:null},ready);
  const price=ratings.current&&ratings.data?.current?.applicable?ratings.data.current.result:null;
  function update<K extends keyof RenewalExperienceFacts>(key:K,value:RenewalExperienceFacts[K]){setFacts({...form,[key]:value});setFactsEdited(true);}
  async function execute(path?:string,body?:unknown,upload?:File){
    if(sendingRef.current||(!pending.current&&!active))return;sendingRef.current=true;setSending(true);setError('');setNotice('');
    try {
      if(!pending.current){pending.current=renewalCommand({draftId,etag,fence:fence!},path!,body,upload);pendingChanged(true);setPendingState(true);}
      const command=pending.current;const result=await sendRenewal(command);
      await saved();
      if(command.file)setAssociation(result.data.resourceId);
      if(command.method==='PUT'){setFactsEdited(false);setAssociation('');}
      pending.current=null;pendingChanged(false);setPendingState(false);setNotice(command.file?'Evidence uploaded. Save the supplied experience to associate these bytes.':'Renewal information saved.');
    }catch(failure){
      if(!uncertainQuoteFailure(failure)){pending.current=null;pendingChanged(false);setPendingState(false);}
      setError(pending.current?'The result is unconfirmed. Retry the same action before continuing.':failure instanceof QuoteError?'The renewal action could not be applied. Refresh the draft and check its editing lease.':failure instanceof Error?failure.message:'Unable to save renewal information.');
    }finally{sendingRef.current=false;setSending(false);}
  }
  const state=workspace.data;
  const insured=editor?.assessment.base.insured;
  const insuredName=typeof insured?.legalName==='string'?insured.legalName:[insured?.firstName,insured?.surname].filter(value=>typeof value==='string').join(' ');
  return <section className="renewal-preparation" aria-label="Renewal preparation" id="renewal-review-risk">
    <nav className="quote-row-actions" aria-label="Renewal stages">{[['Review risk','#renewal-review-risk'],['Rate renewal','#renewal-rate'],['Issue invitation','#renewal-invitation'],['Await acceptance','#renewal-acceptance']].map(([label,href],index)=><a className="button" key={href} href={href}>{index+1}. {label}</a>)}</nav>
    <Panel title="Renewal summary" note="Issued cover remains unchanged"><div className="quote-rail-body">
      <Status tone={ready?'info':'warning'}>{ready?'In preparation':'Review required'}</Status>
      {workspace.error||experience.error?<p role="alert">{workspace.error||experience.error}</p>:null}
      {state?<><dl className="underwriting-provenance">
        <div><dt>Policy</dt><dd><a href={`/policies/${policyId}`}>Open issued policy</a></dd></div>
        <div><dt>Insured</dt><dd>{insuredName||'Waiting for saved insured details'}</dd></div>
        <div><dt>Expiring term · London</dt><dd>{date(state.expiringStartsAt)} – {date(state.expiringEndsAt)} (exclusive end)</dd></div>
        <div><dt>Saved renewal term · London</dt><dd>{state.preparation?`${date(state.preparation.term.startsAt)} – ${date(state.preparation.term.endsAt)} (exclusive end)`:'Not prepared'}</dd></div>
        <div><dt>Expiring annual premium</dt><dd>{formatGbp(state.expiringAnnualPremium)}</dd></div>
        <div><dt>Renewal term premium</dt><dd>{price?formatGbp(price.premium):'Pending current rating'}</dd></div>
        <div><dt>Annual premium movement</dt><dd>{price?.slices[0]?formatGbp(price.slices[0].annualDelta):'Pending current rating'}</dd></div>
      </dl>{state.blockers.map(code=><p key={code}>{blockers[code]??'Renewal eligibility requires review before continuing.'}</p>)}</>:<p>Loading saved renewal…</p>}
      <form onSubmit={event=>{event.preventDefault();void execute('/preparation',{termMonths:Number(selectedMonths),...(offset?{endUtcOffsetMinutes:Number(offset)}:{})});}}>
        <fieldset disabled={!active||sending}><div className="quote-form-grid">
          <label>Renewal term<select aria-label="Renewal term" required value={selectedMonths} onChange={event=>setMonths(event.target.value)}>
            <option value="">Choose term</option>{state?.allowedTermMonths.map(value=><option value={value} key={value}>{value} months</option>)}</select></label>
          <label>Anniversary clock offset<select aria-label="Anniversary clock offset" value={offset} onChange={event=>setOffset(event.target.value)}><option value="">Automatic when unambiguous</option><option value="0">GMT · UTC+00:00</option><option value="60">BST · UTC+01:00</option></select></label>
        </div><button className="button" disabled={!selectedMonths} type="submit">Save renewal preparation</button></fieldset>
      </form><p className="client-help">The new term starts at the expiring term’s exclusive end. Changes require a fresh rating and acceptance.</p>
    </div></Panel>
    <Panel title="Supplied claims experience" note="Observed figures and reviewed evidence"><div className="quote-rail-body">
      <p>{display.label}</p>
      <form onSubmit={event=>{event.preventDefault();void execute('/experience/uploads',undefined,file);}}><fieldset disabled={!active||sending}>
        <label>Claims experience evidence<input aria-label="Claims experience evidence" type="file" accept=".pdf,.png,.jpg,.jpeg,.txt" required onChange={event=>setFile(event.target.files?.[0])}/></label>
        <button className="button" disabled={!file} type="submit">Upload experience evidence</button>
      </fieldset></form>
      {association?<p role="status">New evidence uploaded. Save the experience figures to attach it to this version.</p>:null}
      {experience.data?.evidenceFileId?<a className="button" href={`/api/v1/drafts/${draftId}/evidence-files/${experience.data.evidenceFileId}/content`}>Download saved experience evidence</a>:null}
      <form onSubmit={event=>{event.preventDefault();void execute('/experience',{observationStartsOn:form.observationStartsOn,observationEndsOn:form.observationEndsOn,
        claimCount:form.claimCount,paid:form.paid,outstanding:form.outstanding,earnedPremium:form.earnedPremium,sourceCode:form.sourceCode,
        sourceReference:form.sourceReference,evidenceAssociationId:association||form.evidenceAssociationId});}}><fieldset disabled={!active||sending}><div className="quote-form-grid">
        <label>Observation starts<input aria-label="Observation starts" type="date" required value={form.observationStartsOn} onChange={event=>update('observationStartsOn',event.target.value)}/></label>
        <label>Observation ends (exclusive)<input aria-label="Observation ends (exclusive)" type="date" required value={form.observationEndsOn} onChange={event=>update('observationEndsOn',event.target.value)}/></label>
        <label>Number of claims<input aria-label="Number of claims" type="number" required min={0} max={100000} step={1} value={Number.isFinite(form.claimCount)?form.claimCount:''} onChange={event=>update('claimCount',event.target.valueAsNumber)}/></label>
        {([['paid','Claims paid (£)'],['outstanding','Outstanding claims (£)'],['earnedPremium','Earned premium (£)']] as const).map(([key,label])=><label key={key}>{label}<input aria-label={label} required inputMode="decimal" pattern="(0|[1-9][0-9]{0,12})\.[0-9]{2}" placeholder="0.00" value={form[key]} onChange={event=>update(key,event.target.value)}/></label>)}
        <label>Experience source<select aria-label="Experience source" value={form.sourceCode} onChange={event=>update('sourceCode',event.target.value as RenewalExperienceFacts['sourceCode'])}><option value="agency">Agency</option><option value="insured">Insured</option><option value="administrator">Administrator</option></select></label>
        <label>Source reference<input aria-label="Source reference" required maxLength={200} value={form.sourceReference} onChange={event=>update('sourceReference',event.target.value)}/></label>
      </div><button className="button button-primary" type="submit" disabled={!association&&!form.evidenceAssociationId}>Save supplied experience</button></fieldset></form>
      {recorded?<p>Experience version {recorded.sequence} · Recorded {date(recorded.recordedAt)} · London. {experience.data?.review?`Review: ${experience.data.review.outcome}. ${experience.data.review.reason}`:'Awaiting underwriting review.'}</p>:null}
      {canReview&&recorded?<form onSubmit={event=>{event.preventDefault();void execute(`/experience/${recorded.id}/reviews`,{outcome:reviewOutcome,reason:reviewReason});}}><fieldset disabled={!active||sending||factsEdited||!!association}><div className="quote-form-grid">
        <label>Experience review<select aria-label="Experience review" value={reviewOutcome} onChange={event=>setReviewOutcome(event.target.value as 'accepted'|'rejected')}><option value="accepted">Accept evidence</option><option value="rejected">Reject evidence</option></select></label>
        <label>Experience review reason<textarea aria-label="Experience review reason" minLength={10} maxLength={2000} required value={reviewReason} onChange={event=>setReviewReason(event.target.value)}/></label>
      </div><button className="button" type="submit">Record experience review</button></fieldset></form>:null}
    </div></Panel>
    <Panel title="Renewal checks"><DataTable caption="Current renewal checks" columns={['Check','Result','Source']}>
      <tr><th scope="row">Loss ratio</th><td>{display.label}</td><td>Saved experience and its exact review</td></tr>
      <tr><th scope="row">Delegated authority</th><td>{price?'Review current referrals below':'Awaiting current rating'}</td><td>Published binder and current underwriting grants</td></tr>
      <tr><th scope="row">Fair value assessment</th><td>{workspace.current&&state?.eligibility?state.eligibility.fairValueSatisfied?'Pass':'Review required':'Unconfirmed'}</td><td>{state?.eligibility?.fairValueAssessmentId?<details><summary>Stored assessment</summary><p>{state.eligibility.fairValueAssessmentId}</p><p>Outcome: {state.eligibility.fairValueState}</p></details>:'No applicable assessment'}</td></tr>
      <tr><th scope="row">Broker account</th><td>{workspace.current&&state?.eligibility?'Agency and approved terms eligible':'Eligibility unconfirmed'}</td><td>Arrears check unavailable</td></tr>
    </DataTable></Panel>
    {error?<p role="alert">{error}</p>:null}{notice?<p role="status">{notice}</p>:null}
    {pendingState?<button className="button button-primary" disabled={sending} onClick={()=>void execute()}>Retry same renewal action</button>:null}
    {dirty?<p>Save risk edits before changing renewal preparation or evidence.</p>:null}
  </section>;
}
