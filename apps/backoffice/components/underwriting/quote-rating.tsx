'use client';
import Link from 'next/link';
import { useEffect, useState } from 'react';
import type { QuoteView } from '../../lib/quotes';
import { formatGbp, quoteStateLabel, type UnderwritingAction, type UnderwritingAssessment, type UnderwritingRating, type RatingHistoryItem } from '../../lib/underwriting-api';
import { Panel, Status, DataTable } from '../primitives';
import { LoadFeedback, Paging, useQuoteResource } from '../quotes/shared';
import { QuoteProposalDetails } from '../quotes/quote-history';
import { UnderwritingActionDialog, actionLabels } from './underwriting-action';

export function QuoteRating({ quote, actorId, refresh, questionLabels, openUnderwriting, openQuotation }: { quote: QuoteView; actorId: string; refresh: () => void; questionLabels: Record<string, string>; openUnderwriting: () => void; openQuotation: () => void }) {
  const now = useDisplayClock();
  const assessment = useQuoteResource<UnderwritingAssessment>(`/api/v1/quotes/${quote.id}/underwriting`);
  const [action, setAction] = useState<UnderwritingAction>();
  const rating = useQuoteResource<UnderwritingRating>(assessment.data?.ratingId ? `/api/v1/ratings/${assessment.data.ratingId}` : null);
  const job = useQuoteResource<{ state: string; attempts: number; attemptLimit: number; retryAllowed: boolean; errorCode?: string }>(assessment.data?.jobId ? `/api/v1/jobs/${assessment.data.jobId}` : null);
  useEffect(() => {
    if (action || !assessment.data) return;
    const changed = assessment.data.state !== quote.state;
    if (!changed && (assessment.data.state !== 'rating-pending' || assessment.data.blockers.some(item => item.code === 'quote-rating-failed'))) return;
    const timer = window.setTimeout(refresh, 4000); return () => window.clearTimeout(timer);
  }, [action, assessment.data, quote.state, refresh]);
  if (!assessment.data) return <Panel title="Quote overview"><LoadFeedback error={assessment.error} retry={assessment.refresh} /></Panel>;
  const current = assessment.data, capabilities = current.capabilities;
  const coherent = current.quoteId === quote.id && current.state === quote.state && (!current.context || current.context.revisionId === quote.revisionId);
  const failed = current.blockers.some(item => item.code === 'quote-rating-failed');
  const allow: Record<UnderwritingAction, boolean> = { rate: capabilities.canRate, submit: capabilities.canSubmit, 'return-to-draft': capabilities.canRevise, 'underwriting/refresh': current.refreshOptions.length > 0 };
  return <><div className="underwriting-layout"><div className="underwriting-main">
    <Panel title="Quote overview" note={`Revision ${quote.revisionNumber} · ${quote.agencyName}`}><div className="quote-rail-body">
      <Status tone={failed ? 'error' : current.state === 'rated' ? 'success' : 'info'}>{failed ? 'Rating failed' : quoteStateLabel(current.state, rating.data?.expiresAt, now)}</Status>
      <dl className="quote-saved-details"><div><dt>Client</dt><dd><Link href={`/clients/${quote.clientId}`}>{quote.clientName}</Link></dd></div><div><dt>Product</dt><dd>{quote.productCode === 'motor-trade-combined' ? 'Motor Trade Combined' : 'Motor Trade Road Risks'}</dd></div><div><dt>Requested start</dt><dd>{quote.proposal.termIntent?.localStartDate ?? 'Not recorded'} {quote.proposal.termIntent?.localStartTime ?? ''} · London</dd></div><div><dt>Underwriting team</dt><dd>{current.assignedTeamLabel ?? 'Not submitted'}</dd></div></dl>
      {current.submissionId && <p role="status">Submitted for underwriting. Outstanding requirements remain below.</p>}
      <dl className="quote-saved-details"><div><dt>Created</dt><dd>{new Date(current.createdAt).toLocaleString('en-GB', { timeZone: 'Europe/London' })} · London</dd></div><div><dt>Assigned underwriter</dt><dd>{current.assignedUserLabel ?? 'Not individually assigned'}</dd></div></dl>
      <BusinessSnapshot quote={quote} />
      {!coherent && <p role="alert">The quote changed while loading. Reload before taking an action.</p>}
    </div></Panel>
    <Panel title="Premium" note="GBP · calculated from the retained saved proposal"><div className="quote-rail-body">
      {current.ratingId ? !rating.data ? <LoadFeedback error={rating.error} retry={rating.refresh} /> : <RatingDetail rating={rating.data} /> : <>
        <p role="status">{failed ? 'Rating could not complete. Review the attempt and retry.' : current.state === 'rating-pending' ? 'Rating requested. A premium will appear when the saved result is available.' : 'No rating yet.'}</p>
        <p>{failed ? 'Return to draft to review the proposal and request a new rating. An authorised operator can recover a retryable job.' : 'Complete the pricing requirements, then rate this quote.'}</p>
      </>}
      {current.jobId && <details><summary>Rating request</summary>{job.data ? <p>State: {job.data.state} · Attempts {job.data.attempts} of {job.data.attemptLimit}</p> : <LoadFeedback error={job.error} retry={job.refresh} />}<p className="client-help">Fictional demo rating provider. No external submission.</p><p className="underwriting-provenance">Request <code>{current.jobId}</code></p></details>}
    </div></Panel>
    <Panel title="Outstanding requirements" note="Pricing, referrals and supporting proof are assessed independently"><div className="quote-rail-body">{current.blockers.length ? <ul className="underwriting-requirements">{current.blockers.map((item, index) => <li key={`${item.code}:${item.targetId ?? ''}:${index}`}><strong>{item.message}</strong>{item.dimension && <span>{item.dimension.replaceAll('-', ' ')}</span>}{item.path && <span className="client-help">{item.path}</span>}</li>)}</ul> : <p>No outstanding referrals. Later proof, terms and issue checks still apply.</p>}</div></Panel>
    <RatingHistory quoteId={quote.id} currentRatingId={current.ratingId} questionLabels={questionLabels} />
  </div><aside className="underwriting-rail" aria-label="Quote next actions"><Panel title="Next actions"><div className="quote-rail-body underwriting-action-list">
    <button className="button" onClick={openUnderwriting}>Review referrals</button>
    {(['rate', 'submit', 'return-to-draft', 'underwriting/refresh'] as const).map(name => <button key={name} className={`button ${name === 'rate' && allow[name] ? 'button-primary' : ''}`} disabled={!coherent || !allow[name]} onClick={() => setAction(name)}>{name === 'rate' && current.ratingId ? 'Re-rate quote' : actionLabels[name]}</button>)}
    {quote.capabilities.canSave && <Link className="button" href={`/quotes/${quote.id}/edit`}>Edit quote draft</Link>}
    <p className="client-help">Available actions follow current access and saved quote checks. Refresh choices require a complete term and approved published versions. Return to draft before editing progressed risk.</p>
    <button className="button" onClick={openQuotation}>Prepare quotation / send quote to agency</button><button className="button" disabled>Issue policy</button><p className="client-help">Policy issue is not available yet. Quotation preparation, delivery and acceptance are on the Quotation tab.</p>
    <button className="button" onClick={refresh}>Reload saved quote</button>
  </div></Panel><Panel title="Scheme and basis"><div className="quote-rail-body"><dl className="underwriting-provenance"><div><dt>Capacity provider</dt><dd>{current.providerLabel}</dd></div><div><dt>Scheme</dt><dd>{current.productLabel} · {current.productVersionLabel}</dd></div><div><dt>Basis</dt><dd>{quote.proposal.termIntent?.kind === 'annual' ? 'Annual cover' : quote.proposal.termIntent?.kind === 'short-period' ? 'Short-period cover' : 'Not recorded'}</dd></div><div><dt>Material edits</dt><dd>{quote.capabilities.canSave ? 'Edit the draft and save a new revision.' : 'Return to draft before editing the risk.'} Earlier results remain in history.</dd></div><div><dt>Assumptions</dt><dd>Calculated from the saved declarations and selected cover. Review the exact input in rating history.</dd></div><div><dt>Endorsements offered</dt><dd>{current.appliedEndorsements.length ? current.appliedEndorsements.map(item => <p key={item.decisionId + item.code}><strong>{item.code}</strong> · {item.wording}</p>) : 'No underwriting endorsements applied.'}</dd></div><div><dt>Licence check due again</dt><dd>{current.proofRequirements.some(item => ['photocard-both-sides', 'driving-record'].includes(item.code)) ? current.proofRequirements.filter(item => ['photocard-both-sides', 'driving-record'].includes(item.code)).every(item => item.satisfied) ? 'At renewal' : 'Current driver proof needs underwriting review.' : 'No current named-driver proof assessment.'}</dd></div></dl><Link className="button" href="/quotes">Back to quotes</Link></div></Panel></aside></div>
    {action && <UnderwritingActionDialog action={action} quote={quote} assessment={current} actorId={actorId} close={() => setAction(undefined)} completed={() => { setAction(undefined); refresh(); }} />}
  </>;
}

function RatingDetail({ rating }: { rating: UnderwritingRating }) {
  const now = useDisplayClock();
  return <>{!rating.applicable && <p role="status" className="error-message">This is a historical or expired rating. It cannot support current progression.</p>}{Date.parse(rating.expiresAt) <= now && <p>This rating has expired. Re-rate before sending or issuing.</p>}<p className="client-help">Valid until {new Date(rating.expiresAt).toLocaleString('en-GB')} · Completed {new Date(rating.completedAt).toLocaleString('en-GB')}</p>
    <dl className="underwriting-premium">{[['Annual premium', rating.annualPremium], ['Term premium (GWP)', rating.termPremium], ['Insurance Premium Tax', rating.tax], ['Fee', rating.fee], ['Total payable', rating.grossPayable], ['Broker commission', rating.brokerCommission]].map(([label, amount]) => <div key={label} className={label === 'Total payable' ? 'underwriting-total' : ''}><dt>{label}</dt><dd>{formatGbp(amount)}</dd></div>)}</dl>
    <details><summary>Rating factors and provenance</summary><DataTable caption="Saved rating factors" columns={['Factor', 'Basis', 'Adjustment']}>{rating.factors.map((factor, index) => <tr key={`${factor.code}:${index}`}><td>{factor.label}{factor.basisPoints !== undefined && <p className="client-help">{factor.basisPoints / 100}% of basis</p>}</td><td>{factor.basisAmount ? formatGbp(factor.basisAmount) : '—'}</td><td>{factor.direction === 'discount' ? '−' : '+'}{formatGbp(factor.amount)}</td></tr>)}</DataTable>
      <dl className="underwriting-provenance"><div><dt>Rating reference</dt><dd><code>{rating.id}</code></dd></div><div><dt>Input revision</dt><dd><code>{rating.revisionId}</code></dd></div><div><dt>Rating rule version</dt><dd><code>{rating.ruleVersionId}</code></dd></div><div><dt>Agency terms version</dt><dd><code>{rating.agencyTermsVersionId}</code></dd></div><div><dt>Pricing input hash</dt><dd><code>{rating.pricingInputHash}</code></dd></div></dl></details>
  </>;
}
function useDisplayClock() {
  const [now, setNow] = useState(Date.now);
  useEffect(() => { const timer = window.setInterval(() => setNow(Date.now()), 30_000); return () => window.clearInterval(timer); }, []);
  return now;
}
function BusinessSnapshot({ quote }: { quote: QuoteView }) {
  const business = quote.proposal.risk?.business;
  const saved = business && typeof business === 'object' && !Array.isArray(business) ? business : {};
  const contact = quote.proposal.insured?.contact;
  const communication = contact && typeof contact === 'object' && !Array.isArray(contact) ? contact : {};
  const text = (value: unknown) => typeof value === 'string' && value ? value : 'Not recorded';
  return <dl className="quote-saved-details"><div><dt>Business description</dt><dd>{text(saved.description)}</dd></div><div><dt>Trading since</dt><dd>{text(saved.startedOn)}</dd></div><div><dt>Entity type</dt><dd>{text(quote.proposal.insured?.entityType).replaceAll('-', ' ')}</dd></div><div><dt>Turnover</dt><dd>{typeof saved.turnover === 'string' ? formatGbp(saved.turnover) : 'Not recorded'}</dd></div><div><dt>Contact email</dt><dd>{text(communication.email)}</dd></div><div><dt>Contact telephone</dt><dd>{text(communication.mobile)}</dd></div></dl>;
}
function RatingHistory({ quoteId, currentRatingId, questionLabels }: { quoteId: string; currentRatingId?: string; questionLabels: Record<string, string> }) {
  const [open, setOpen] = useState(false), [cursors, setCursors] = useState<string[]>([]), [selected, setSelected] = useState<RatingHistoryItem>();
  const history = useQuoteResource<{ items: RatingHistoryItem[]; nextCursor?: string }>(open ? `/api/v1/quotes/${quoteId}/ratings?pageSize=10${cursors.at(-1) ? `&cursor=${encodeURIComponent(cursors.at(-1)!)}` : ''}` : null);
  const detail = useQuoteResource<UnderwritingRating>(selected ? `/api/v1/ratings/${selected.id}` : null);
  return <Panel title="Rating history"><div className="quote-rail-body"><button className="button" aria-expanded={open} onClick={() => setOpen(value => !value)}>{open ? 'Hide rating history' : 'View rating history'}</button>{open && <>
    {!history.data ? <LoadFeedback error={history.error} retry={() => { setCursors([]); history.refresh(); }} /> : !history.data.items.length ? <p>No completed rating results.</p> : <DataTable caption="Retained quote rating results" columns={['Saved result', 'Revision', 'Outcome', 'Total payable']}>{history.data.items.map(item => <tr key={item.id}><td><button className="button" onClick={() => setSelected(item)}>{new Date(item.completedAt).toLocaleString('en-GB')}</button>{item.id === currentRatingId && <p>Current result</p>}</td><td>{item.revisionNumber}</td><td>{item.outcome}</td><td>{item.outcome === 'rated' ? formatGbp(item.grossPayable) : 'No price'}</td></tr>)}</DataTable>}
    <Paging previous={cursors.length ? () => setCursors(value => value.slice(0, -1)) : undefined} next={history.data?.nextCursor ? () => setCursors(value => [...value, history.data!.nextCursor!]) : undefined} />
    {selected && (!detail.data ? <LoadFeedback error={detail.error} retry={detail.refresh} /> : <section aria-label="Selected historical rating"><h3>Selected saved rating</h3>{selected.outcome === 'rated' ? <RatingDetail rating={detail.data} /> : <p>This request was rejected. No premium was returned.</p>}<details><summary>Exact saved input</summary><QuoteProposalDetails value={detail.data.input} proposal={detail.data.input} questionLabels={questionLabels} /></details></section>)}
  </>}</div></Panel>;
}
