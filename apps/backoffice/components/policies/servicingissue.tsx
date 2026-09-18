'use client';
import { useRef, useState } from 'react';
import { Status } from '../primitives';
import { quoteFetch, uncertainQuoteFailure } from '../../lib/quotes';
import type { PolicyView } from '../../lib/policies-api';
import { adjustmentIssueCommand, sendAdjustmentIssue, type AdjustmentIssueCommand, type AdjustmentIssueReceipt } from '../../lib/servicing-issue';
import type { ProofScope } from '../../lib/servicing-proof';
import type { TermsView } from '../../lib/servicing-terms';
import { useProofRead } from './servicing-proof-read';

export function ServicingIssue({scope,active,paused,pendingChanged,saved}:{scope:ProofScope;active:boolean;paused:boolean;pendingChanged:(value:boolean)=>void;saved:()=>Promise<void>}) {
  const read=useProofRead<TermsView>(`/api/v1/drafts/${scope.draftId}/terms`,scope.etag,paused);
  const [reason,setReason]=useState(''),[confirmed,setConfirmed]=useState(false),[busy,setBusy]=useState(false),[uncertain,setUncertain]=useState(false),[error,setError]=useState('');
  const [receipt,setReceipt]=useState<AdjustmentIssueReceipt|null>(null);
  const [issuedView,setIssuedView]=useState<PolicyView|null>(null);
  const pending=useRef<AdjustmentIssueCommand|null>(null),sending=useRef(false);
  const view=read.data;
  const ready=active && read.current && view?.cycleId===scope.cycleId && view.revisionId===scope.revisionId && view.acceptanceApplicable && view.terms?.applicable;
  async function issue() {
    if(sending.current || (!pending.current && (!ready || !confirmed)))return;
    sending.current=true;setBusy(true);setError('');
    try {
      if(!pending.current) {
        pending.current=adjustmentIssueCommand(scope,{cycleId:scope.cycleId,ratingId:view!.ratingId,termsVersionId:view!.terms!.id,
          acceptanceId:view!.acceptance!.id,termsHash:view!.terms!.termsHash,assuranceHash:view!.assuranceHash,reason});
        pendingChanged(true);
      }
      const result=await sendAdjustmentIssue(pending.current);
      setReceipt(result);
      const {data:issued}=await quoteFetch<PolicyView>(`/api/v1/policies/${result.policyId}/terms/${result.termId}/versions/${result.versionIds.at(-1)}`);
      if(issued.transactionId!==result.transactionId || issued.versionId!==result.versionIds.at(-1) || issued.financials.journalId!==result.journalId)
        throw new Error('Issued policy readback is unconfirmed.');
      setIssuedView(issued);
      await saved();
      pending.current=null;setUncertain(false);pendingChanged(false);
    } catch(failure) {
      const retain=!!pending.current && uncertainQuoteFailure(failure);
      setUncertain(retain);
      if(!retain){pending.current=null;pendingChanged(false);}
      setError(retain?'The issue result is unconfirmed. Retry the same action before making further changes.':'Issue was refused. Refresh the draft and review the saved acceptance, editing lease and your current authority.');
    } finally {sending.current=false;setBusy(false);}
  }
  return <section aria-label="Issue policy adjustment" className="servicing-terms">
    <h3>Issue adjustment</h3>
    {receipt && <article aria-label="Adjustment issue receipt" className="quote-driver-card"><Status tone="success">Adjustment issued</Status>
      <p>{receipt.policyReference} · Issued {new Date(receipt.processedAt).toLocaleString('en-GB')}</p>
      <p>Amount due £{receipt.amountDue} · Credit £{receipt.amountCredit} · Posting date {receipt.postingDate}</p>
      {issuedView && <><p>Transaction {issuedView.transactionSequence} · Latest issued version {issuedView.versionSequence}</p>
        <p>Adjustment premium £{issuedView.financials.premium} · Revised term premium £{issuedView.snapshot.premium.termPremium}</p>
        <p>Payments collected by this action: £0.00. The charge or credit is recorded for settlement.</p></>}
      {view?.terms?.document.effectiveDates.map(value=><p key={value}>Changes effective {new Date(value).toLocaleString('en-GB')}</p>)}
      <p>{receipt.versionIds.length} policy {receipt.versionIds.length===1?'version':'versions'} saved. Document requests and MID updates are queued.</p>
      <a href={`/policies/${receipt.policyId}`}>View current policy</a>
      <p><a href={`/policies/${receipt.policyId}?termId=${receipt.termId}&versionId=${receipt.versionId}&tab=Transactions`}>View issued transaction</a></p>
      <ul>{receipt.versionIds.map((id,index)=><li key={id}><a href={`/policies/${receipt.policyId}?termId=${receipt.termId}&versionId=${id}`}>View issued change {index+1}</a></li>)}</ul>
    </article>}
    {error && <p role="alert">{error}</p>}
    {uncertain && <button className="button button-primary" disabled={busy} onClick={issue}>Retry same issue</button>}
    {!receipt && !uncertain && <fieldset className="quote-reference-fields" disabled={!ready || busy}><legend>Confirm accepted changes</legend>
      <p>Issuing records all effective changes and their charge or credit. Future changes take effect on their scheduled dates.</p>
      {!ready && <p>Save and rate the change, complete the required proof and decisions, and record current acceptance before issuing.</p>}
      {view?.terms?.document.effectiveDates.map(value=><p key={value}>Effective {new Date(value).toLocaleString('en-GB')}</p>)}
      {view?.terms && <p>Accepted net movement £{view.terms.document.price.netDue}</p>}
      <label>Issue reason<textarea aria-label="Issue reason" maxLength={1000} value={reason} onChange={event=>setReason(event.target.value)}/></label>
      <label><input type="checkbox" checked={confirmed} onChange={event=>setConfirmed(event.target.checked)}/> I confirm the accepted changes and effective dates.</label>
      <button className="button button-primary" disabled={!confirmed || reason.trim().length<10} onClick={issue}>{busy?'Issuing…':'Issue accepted adjustment'}</button>
    </fieldset>}
  </section>;
}
