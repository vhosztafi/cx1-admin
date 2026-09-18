'use client';
import {useState} from 'react';
import {Status} from '../primitives';
import type {ServicingSubmissionPage} from '../../lib/servicing-submission';
import {PageButtons,useProofRead} from './servicing-proof-read';

export function ServicingSubmission({draftId,revisionId,cycleId,etag,active,paused,submit}:{draftId:string;revisionId:string;cycleId:string|null;etag:string;active:boolean;paused:boolean;submit:(reason:string)=>void}) {
 const [reason,setReason]=useState(''),[page,setPage]=useState({etag,cursor:''});
 const cursor=page.etag===etag?page.cursor:'';
 const history=useProofRead<ServicingSubmissionPage>(`/api/v1/drafts/${draftId}/submissions?pageSize=20${cursor?'&cursor='+encodeURIComponent(cursor):''}`,etag,paused);
 const current=history.data?.current;
 const fresh=history.current&&history.data?.draftId===draftId&&history.data.draftEtag===etag;
 const submitted=!!(fresh&&current?.cycleId===cycleId&&current?.revisionId===revisionId);
 return <section aria-label="Underwriting submission" className="quote-reference-fields">
  <h3>Submit to underwriting</h3>
  <p className="client-help">Send the saved rated change for review. Outstanding proof and referral decisions remain separate from submission.</p>
  {current&&<p><Status tone={fresh&&current.applicable?'info':'warning'}>{fresh&&current.applicable?'Submitted for underwriting':'Retained submission'}</Status> · {new Date(current.submittedAt).toLocaleString('en-GB')}</p>}
  {history.error&&<p role="status">Submission history is unavailable. Your reason is retained while it refreshes.</p>}
  {!active&&!submitted&&<p>Save and rate the change, then acquire its editing lease to submit.</p>}
  <fieldset disabled={!active||paused||!fresh||submitted}>
   <label>Submission reason<textarea aria-label="Submission reason" value={reason} minLength={10} maxLength={2000} onChange={event=>setReason(event.target.value)} /></label>
   <button className="button button-primary" disabled={reason.trim().length<10} onClick={()=>submit(reason)}>Submit to underwriting</button>
  </fieldset>
  <a className="button" href="#servicing-referrals">Review submitted referrals</a>
  <details><summary>Submission history</summary>
   {history.data?.items.length===0&&<p>No underwriting submissions recorded.</p>}
   {history.data?.items.map(item=><div key={item.id} data-submission-id={item.id} className="quote-reference-fields">
    <p><strong>{fresh&&item.applicable?'Current submission':'Retained submission'}</strong> · {new Date(item.submittedAt).toLocaleString('en-GB')}</p><p>{item.reason}</p>
    <details><summary>Submission provenance</summary><p>Submission: {item.id}</p><p>Revision: {item.revisionId}</p><p>Cycle: {item.cycleId}</p><p>Submitted by: {item.submittedBy}</p></details>
   </div>)}
   <PageButtons cursor={cursor} next={history.data?.nextCursor??null} disabled={paused} change={next=>setPage({etag,cursor:next})} />
  </details>
 </section>;
}
