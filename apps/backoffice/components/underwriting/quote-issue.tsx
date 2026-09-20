'use client';
import { useState } from 'react';
import { formatGbp, type UnderwritingAssessment, type QuotationTerms } from '../../lib/underwriting-api';
import { issuePolicyCommand, quotedIssueAmount } from '../../lib/policies-api';
import type { DecisionRequest } from './decision-command';
import type { QuoteView, QuoteCaptureProposal } from '../../lib/quotes';

export function QuoteIssue({ quote, assessment, terms, run }: { quote: QuoteView<QuoteCaptureProposal>; assessment: UnderwritingAssessment; terms?: QuotationTerms; run: (request: DecisionRequest) => void }) {
  const [reason, setReason] = useState(''), [error, setError] = useState('');
  function review() {
    try {
      if (!assessment.capabilities.canIssue || !terms || terms.id !== assessment.termsVersionId) throw new Error('Refresh the current accepted quotation before issuing.');
      const command = issuePolicyCommand(assessment.quoteId, assessment.quoteEtag, { cycleId: assessment.context?.cycleId, ratingId: assessment.ratingId,
        acceptanceId: assessment.acceptanceId, termsHash: assessment.termsHash, assuranceHash: assessment.assuranceHash, reason });
      const term = terms.rating.input.termIntent;
      if (!term) throw new Error('Reload the accepted quotation to review the cover period.');
      run({ command, kind: 'issue', label: 'Issue policy', description: `${quote.clientName} · ${quote.agencyName}. Issue ${assessment.productLabel} from accepted terms version ${terms.number}. Cover starts ${term.localStartDate} at ${term.localStartTime} London time (${term.kind} term${term.localEndDate ? `, ending ${term.localEndDate} at ${term.localEndTime}` : ''}). Opening amount due: ${formatGbp(quotedIssueAmount(terms))}, payable by the ${terms.settlement.collector === 'agency' ? 'agency' : 'client'}. This requests the policy documents. No payment is collected.` }); setError('');
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'Review the accepted quotation.'); }
  }
  return <fieldset className="quote-reference-fields"><legend>Issue policy</legend>
    <p>Issue the accepted new-business cover and opening financial balance.</p>
    {assessment.capabilities.canIssue ? <><label>Issue reason<textarea aria-label="Issue reason" maxLength={1000} value={reason} onChange={event => setReason(event.target.value)} /></label><button className="button button-primary" onClick={review}>Review policy issue</button></> : <p className="client-help">A currently authorised underwriter can issue once the quotation, delivery, acceptance and proof are current. Refresh after resolving the outstanding requirements.</p>}
    {error && <p role="alert" className="error-message">{error}</p>}
  </fieldset>;
}
