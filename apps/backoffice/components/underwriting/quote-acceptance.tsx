'use client';
import { useState } from 'react';
import type { UnderwritingAssessment, UnderwritingEvidence, QuotationTerms } from '../../lib/underwriting-api';
import { acceptanceInstant, acceptanceProofs, quotationCommand } from '../../lib/quotation';
import type { DecisionRequest } from './decision-command';

export function QuoteAcceptance({ assessment, terms, evidence, run }: { assessment: UnderwritingAssessment; terms: QuotationTerms; evidence: UnderwritingEvidence[]; run: (request: DecisionRequest) => void }) {
  const [name, setName] = useState(''), [received, setReceived] = useState(''), [channel, setChannel] = useState('email'), [proof, setProof] = useState(''), [error, setError] = useState('');
  const options = acceptanceProofs(evidence, assessment);
  function review() {
    try {
      if (!options.some(x => x.id === proof)) throw new Error('Select reviewed acceptance evidence for these terms.');
      const instant = acceptanceInstant(received);
      const command = quotationCommand(assessment.quoteId, 'acceptances', assessment.quoteEtag, { cycleId: assessment.context!.cycleId, ratingId: assessment.ratingId!, termsVersionId: terms.id, termsHash: terms.termsHash, assuranceHash: assessment.assuranceHash!, accepterLabel: name.trim(), acceptedAt: instant, channel, evidenceAssociationId: proof });
      setError(''); run({ command, label: 'Record acceptance', description: `${name.trim()} · Terms version ${terms.number} · ${channel} · ${instant}. This records the supplied acceptance of the delivered terms.` });
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'Review the acceptance details.'); }
  }
  return <fieldset className="quote-reference-fields" disabled={!assessment.capabilities.canAccept}><legend>Record acceptance · Terms version {terms.number}</legend>
    <p>Record the person who accepted the delivered quotation and their actual acceptance evidence.</p>
    <label>Accepted by<input value={name} maxLength={200} onChange={event => setName(event.target.value)} autoComplete="off" /></label>
    <label>Acceptance received at<input value={received} onChange={event => setReceived(event.target.value)} placeholder="2026-09-17T10:30:00+01:00" aria-describedby="acceptance-time-help" /></label>
    <p id="acceptance-time-help" className="client-help">Include seconds and UTC offset: +01:00 for British Summer Time, +00:00 for GMT, or Z for UTC. Use the actual received time after delivery.</p>
    <label>Acceptance channel<select aria-label="Acceptance channel" value={channel} onChange={event => setChannel(event.target.value)}><option value="email">Email</option><option value="written">Written</option><option value="telephone">Telephone</option></select></label>
    <label>Reviewed acceptance evidence<select aria-label="Reviewed acceptance evidence" value={proof} onChange={event => setProof(event.target.value)}><option value="">Select reviewed proof</option>{options.map(x => <option key={x.id} value={x.id}>{x.fileName}</option>)}</select></label>
    {error && <p role="alert" className="error-message">{error}</p>}
    <button className="button button-primary" onClick={review}>Review acceptance</button>
  </fieldset>;
}
