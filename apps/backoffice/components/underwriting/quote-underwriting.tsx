'use client';
import { useState } from 'react';
import type { QuoteView, QuoteCaptureProposal } from '../../lib/quotes';
import type { UnderwritingAssessment, Referral, UnderwritingEvidence as Evidence } from '../../lib/underwriting-api';
import { Panel } from '../primitives';
import { LoadFeedback, useQuoteResource } from '../quotes/shared';
import { ReferralDecisions } from './referral-decisions';
import { UnderwritingEvidence } from './underwriting-evidence';
import { DecisionCommand, type DecisionRequest } from './decision-command';
import { riskTargetLabel } from '../../lib/underwriting-decisions';

export function QuoteUnderwriting({ quote, actorId, refresh, openQuotation }: { quote: QuoteView<QuoteCaptureProposal>; actorId: string; refresh: () => void; openQuotation: () => void }) {
  const assessment = useQuoteResource<UnderwritingAssessment>(`/api/v1/quotes/${quote.id}/underwriting`);
  const [referralCursor, setReferralCursor] = useState(''), [evidenceCursor, setEvidenceCursor] = useState(''), [request, setRequest] = useState<DecisionRequest>();
  const referrals = useQuoteResource<{ items: Referral[]; nextCursor?: string }>(`/api/v1/referrals?quoteId=${quote.id}&pageSize=50${referralCursor ? '&cursor=' + encodeURIComponent(referralCursor) : ''}`);
  const evidence = useQuoteResource<{ items: Evidence[]; nextCursor?: string }>(`/api/v1/quotes/${quote.id}/underwriting/evidence?pageSize=100${evidenceCursor ? '&cursor=' + encodeURIComponent(evidenceCursor) : ''}`);
  if (!assessment.data) return <Panel title="Underwriting"><LoadFeedback error={assessment.error} retry={assessment.refresh} /></Panel>;
  const current = assessment.data;
  const coherent = current.quoteId === quote.id && current.state === quote.state && (!current.context || current.context.revisionId === quote.revisionId);
  const ready = coherent && !!referrals.data && !!evidence.data;
  return <div className="underwriting-workspace">
    <Panel title="Authority and assignment" note={`${current.assignedTeamLabel ?? 'Not submitted'} · ${current.assignedUserLabel ?? 'No individually assigned underwriter'}`}><div className="quote-rail-body">
      {!coherent && <p role="alert">The quote changed while loading. Reload before taking an action.</p>}
      {!current.context && <p>Rate the saved proposal from Overview to start an underwriting cycle.</p>}
      <div className="quote-row-actions"><button className="button" onClick={() => {assessment.refresh(); referrals.refresh(); evidence.refresh(); refresh();}}>Reload underwriting</button><button className="button" onClick={openQuotation}>Send quote to agency</button></div>
      {current.authorityViews.map((view, index) => <details key={view.authorityVersionId ?? 'none'} open><summary>{view.hasCurrentGrant ? `Your current authority ${index + 1}` : 'No current underwriting grant'}</summary>
        <p className="client-help">All dimensions must be covered by one current grant and the binder. Proof requirements remain independent. Applied warranties are assessed by the decision service.</p>
        <div className="table-scroll" role="region" aria-label={`Authority dimensions ${index + 1}`} tabIndex={0}><table><thead><tr><th>Dimension</th><th>Requested</th><th>Your limit</th><th>Binder limit</th></tr></thead><tbody>{view.rows.map(row => <tr key={row.code}><th scope="row">{row.label}{row.code.includes(':') && <span className="client-help"> · {riskTargetLabel(quote.proposal, row.code.split(':')[1])}</span>}</th><td>{row.requested}</td><td>{row.actorLimit}{!row.actorAllows && <span className="client-help"> · Requires further authority or an applicable condition</span>}</td><td>{row.binderLimit}{!row.binderAllows && <span className="client-help"> · {row.code === 'district' ? 'Pending whole-book check' : 'Outside binder extent'}</span>}</td></tr>)}</tbody></table></div>
      </details>)}
      {current.blockers.length > 0 && <details><summary>Outstanding requirements ({current.blockers.length})</summary><ul>{current.blockers.map((x, index) => <li key={index}>{x.message}</li>)}</ul></details>}
    </div></Panel>
    {!referrals.data && <LoadFeedback error={referrals.error} retry={referrals.refresh} />}{!evidence.data && <LoadFeedback error={evidence.error} retry={evidence.refresh} />}
    {ready && <><div className="underwriting-layout"><ReferralDecisions quote={quote} assessment={current} referrals={referrals.data!.items} evidence={evidence.data!.items} run={setRequest} /></div>
      <div className="quote-row-actions"><button className="button" disabled={!referralCursor} onClick={() => setReferralCursor('')}>Latest referrals</button><button className="button" disabled={!referrals.data!.nextCursor} onClick={() => setReferralCursor(referrals.data!.nextCursor!)}>Older referrals</button></div>
      <UnderwritingEvidence quote={quote} assessment={current} evidence={evidence.data!.items} run={setRequest} />
      <div className="quote-row-actions"><button className="button" disabled={!evidenceCursor} onClick={() => setEvidenceCursor('')}>Latest evidence</button><button className="button" disabled={!evidence.data!.nextCursor} onClick={() => setEvidenceCursor(evidence.data!.nextCursor!)}>Older evidence</button></div>
    </>}
    {request && <DecisionCommand request={request} actorId={actorId} close={() => setRequest(undefined)} completed={() => { setRequest(undefined); assessment.refresh(); referrals.refresh(); evidence.refresh(); refresh(); }} />}
  </div>;
}
