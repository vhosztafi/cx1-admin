'use client';
import { useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import type { QuoteView } from '../../lib/quotes';
import { formatGbp, type UnderwritingAssessment, type UnderwritingEvidence as Evidence, type QuotationHistory, type QuotationTerms, type QuotationDelivery } from '../../lib/underwriting-api';
import { underwritingWrite } from '../../lib/underwriting-decisions';
import { quotationCommand } from '../../lib/quotation';
import { Panel, Status } from '../primitives';
import { LoadFeedback, useQuoteResource } from '../quotes/shared';
import { QuoteProposalDetails } from '../quotes/quote-history';
import { UnderwritingEvidence } from './underwriting-evidence';
import { DecisionCommand, type DecisionRequest } from './decision-command';
import { QuoteAcceptance } from './quote-acceptance';
import { QuoteIssue } from './quote-issue';
export function QuoteTerms({ quote, actorId, refresh, questionLabels }: { quote: QuoteView; actorId: string; refresh: () => void; questionLabels: Record<string, string> }) {
  const router = useRouter();
  const assessment = useQuoteResource<UnderwritingAssessment>(`/api/v1/quotes/${quote.id}/underwriting`);
  const [cursors, setCursors] = useState({ terms: '', deliveries: '', acceptances: '' }), [evidenceCursor, setEvidenceCursor] = useState('');
  const query = new URLSearchParams({ pageSize: '20', ...Object.fromEntries(Object.entries(cursors).filter(([, value]) => value).map(([key, value]) => [`${key}Cursor`, value])) });
  const history = useQuoteResource<QuotationHistory>(`/api/v1/quotes/${quote.id}/terms?${query}`);
  const evidence = useQuoteResource<{ items: Evidence[]; nextCursor?: string }>(`/api/v1/quotes/${quote.id}/underwriting/evidence?pageSize=100${evidenceCursor ? '&cursor=' + encodeURIComponent(evidenceCursor) : ''}`);
  const [template, setTemplate] = useState(''), [recipients, setRecipients] = useState<string[]>([]), [request, setRequest] = useState<DecisionRequest>(), [error, setError] = useState('');
  if (!assessment.data || !history.data || !evidence.data) return <Panel title="Quotation"><LoadFeedback error={assessment.error || history.error || evidence.error} retry={refresh} /></Panel>;
  const current = assessment.data, data = history.data;
  const coherent = current.quoteId === quote.id && current.state === quote.state && current.context?.revisionId === quote.revisionId;
  const terms = data.terms.find(x => x.id === current.termsVersionId);
  function prepare() {
    try {
      if (!data.templates.some(x => x.id === template)) throw new Error('Select a currently available quotation template.');
      setRequest({ command: quotationCommand(quote.id, 'terms/prepare', current.quoteEtag, { cycleId: current.context!.cycleId, ratingId: current.ratingId!, templateVersionId: template }), label: 'Prepare quotation', description: 'Retain the current rating, cover, warranties and settlement as an exact quotation version. Existing signed proof must match that version.' }); setError('');
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'Review the preparation details.'); }
  }
  function send() {
    try {
      if (recipients.some(id => !data.recipientOptions.some(x => x.id === id))) throw new Error('Review the current contact choices.');
      setRequest({ command: quotationCommand(quote.id, 'terms', current.quoteEtag, { termsVersionId: terms!.id, recipientContactIds: recipients }), label: 'Send quote to agency', description: `Terms version ${terms!.number} · ${data.recipientOptions.filter(x => recipients.includes(x.id)).map(x => `${x.name} (${x.email})`).join(', ')}. Queues a persisted demo delivery; no real email is sent.` }); setError('');
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'Review the recipient choices.'); }
  }
  function paging(kind: 'terms' | 'deliveries' | 'acceptances', next?: string) {
    return <div className="quote-row-actions"><button className="button" disabled={!cursors[kind]} onClick={() => setCursors({ ...cursors, [kind]: '' })}>Latest {kind}</button><button className="button" disabled={!next} onClick={() => setCursors({ ...cursors, [kind]: next! })}>Older {kind}</button></div>;
  }
  return <div className="underwriting-workspace"><div className="underwriting-layout"><div className="underwriting-main">
    <Panel title="Quotation" note="Versioned terms, signed proof and retained delivery"><div className="quote-rail-body">
      {!coherent && <p role="alert">The quote changed while loading. Refresh quotation before taking an action.</p>}
      {current.acceptanceId ? <Status tone="success">Current acceptance recorded</Status> : data.acceptances.length > 0 ? <p role="status">The quote has changed since acceptance. Review the current terms and record fresh acceptance.</p> : <p>No current acceptance recorded.</p>}
      {!terms && <p>{current.termsVersionId ? 'Current terms are on the latest history page.' : 'Prepare the current quotation once the pricing and underwriting requirements are complete.'}</p>}
      {terms && <p>Terms version {terms.number} · {current.capabilities.canSend ? 'Required signing proof reviewed' : 'Review current requirements and exact-version proof'}</p>}
      <p className="client-help">Prepared terms are available below as structured information. Generated quotation documents are not available yet.</p>
    </div></Panel>
    {data.terms.map(item => <Panel key={item.id} title={`Terms version ${item.number}`} note={item.id === current.termsVersionId ? 'Current prepared version' : 'Historical version · retained unchanged'}><div className="quote-rail-body"><TermsPreview terms={item} questionLabels={questionLabels} /></div></Panel>)}
    {paging('terms', data.nextTermsCursor)}
    <Panel title="Delivery history" note="Persisted demo delivery · no external transmission"><div className="quote-rail-body">
      {!data.deliveries.length && <p>No delivery requested.</p>}{data.deliveries.map(item => <Delivery key={item.id} item={item} quoteId={quote.id} run={setRequest} />)}{paging('deliveries', data.nextDeliveriesCursor)}
    </div></Panel>
    <Panel title="Acceptance history"><div className="quote-rail-body">{!data.acceptances.length && <p>No acceptance has been recorded.</p>}{data.acceptances.map(item => <article className="quote-driver-card" key={item.id}><h3>{item.accepterLabel}</h3><Status tone={item.id === current.acceptanceId ? 'success' : 'warning'}>{item.id === current.acceptanceId ? 'Current acceptance' : 'Historical acceptance'}</Status><p>{item.channel} · {new Date(item.acceptedAt).toLocaleString('en-GB', { timeZone: 'Europe/London' })} · London</p><dl className="underwriting-provenance"><div><dt>Terms version</dt><dd><code>{item.termsVersionId}</code></dd></div><div><dt>Delivered quotation</dt><dd><code>{item.deliveryId}</code></dd></div><div><dt>Reviewed evidence</dt><dd><code>{item.evidenceAssociationId}</code></dd></div></dl></article>)}{paging('acceptances', data.nextAcceptancesCursor)}</div></Panel>
    {coherent && <><UnderwritingEvidence quote={quote} assessment={{ ...current, proofRequirements: current.proofRequirements.filter(x => !!x.termsVersionId) }} evidence={evidence.data.items.filter(x => !!x.termsVersionId)} run={setRequest} /><div className="quote-row-actions"><button className="button" disabled={!evidenceCursor} onClick={() => setEvidenceCursor('')}>Latest evidence</button><button className="button" disabled={!evidence.data.nextCursor} onClick={() => setEvidenceCursor(evidence.data!.nextCursor!)}>Older evidence</button></div></>}
  </div><aside className="underwriting-rail" aria-label="Quotation actions"><Panel title="Next actions"><div className="quote-rail-body">
    <button className="button" onClick={refresh}>Refresh quotation</button>
    <fieldset className="quote-reference-fields" disabled={!coherent || !current.capabilities.canPrepareTerms}><legend>Prepare quotation</legend><label>Quotation template<select aria-label="Quotation template" value={template} onChange={event => setTemplate(event.target.value)}><option value="">Select template</option>{data.templates.map(x => <option key={x.id} value={x.id}>{x.title} · v{x.version}</option>)}</select></label><button className="button" onClick={prepare}>Prepare exact terms</button></fieldset>
    <fieldset className="quote-reference-fields" disabled={!coherent || !terms || !current.capabilities.canSend}><legend>Send quote to agency</legend><p>Select current contacts for this client and agency relationship.</p>{!data.recipientOptions.length && <p>No active contacts with email are available. Add a contact on the client record.</p>}{data.recipientOptions.map(x => <label className="contact-check" key={x.id}><input type="checkbox" checked={recipients.includes(x.id)} onChange={event => setRecipients(event.target.checked ? [...recipients, x.id] : recipients.filter(id => id !== x.id))} />{x.name} · {x.email}</label>)}<button className="button button-primary" disabled={!recipients.length} onClick={send}>Review quotation delivery</button></fieldset>
    {error && <p role="alert" className="error-message">{error}</p>}
    {coherent && terms && <QuoteAcceptance key={terms.id} assessment={current} terms={terms} evidence={evidence.data.items} run={setRequest} />}
    {!current.capabilities.canAccept && <p className="client-help">Acceptance needs completed current delivery and reviewed acceptance evidence. Upload acceptance proof after delivery completes.</p>}
    {quote.boundPolicyId ? <Link className="button button-primary" href={`/policies/${quote.boundPolicyId}`}>Open issued policy</Link> : coherent && <QuoteIssue quote={quote} assessment={current} terms={terms} run={setRequest} />}
  </div></Panel><Panel title="Outstanding requirements"><div className="quote-rail-body">{current.blockers.length ? <ul>{current.blockers.map((x, index) => <li key={index}>{x.message}</li>)}</ul> : <p>No outstanding underwriting blockers.</p>}</div></Panel></aside></div>
    {request && <DecisionCommand request={request} actorId={actorId} close={() => setRequest(undefined)} completed={policyId => { setRequest(undefined); if (policyId) router.push(`/policies/${policyId}`); else refresh(); }} />}
  </div>;
}
function TermsPreview({ terms, questionLabels }: { terms: QuotationTerms; questionLabels: Record<string, string> }) {
  const rating = terms.rating;
  return <><p>Prepared {new Date(terms.preparedAt).toLocaleString('en-GB', { timeZone: 'Europe/London' })} · London. Valid until {new Date(rating.expiresAt).toLocaleString('en-GB', { timeZone: 'Europe/London' })} · London.</p>
    {!rating.applicable && <p role="status">Historical or expired rating — review the current quotation.</p>}
    <dl className="underwriting-premium">{[['Annual premium', rating.annualPremium], ['Term premium', rating.termPremium], ['Insurance Premium Tax', rating.tax], ['Fee', rating.fee], ['Total payable', rating.grossPayable], ['Broker commission', rating.brokerCommission]].map(([label, value]) => <div key={label} className={label === 'Total payable' ? 'underwriting-total' : ''}><dt>{label}</dt><dd>{formatGbp(value)}</dd></div>)}</dl>
    <div className="table-scroll" role="region" aria-label={`Cover for terms version ${terms.number}`} tabIndex={0}><table><thead><tr><th>Cover</th><th>Limit</th><th>Excess</th></tr></thead><tbody>{terms.cover.map((x, index) => <tr key={`${x.code}:${index}`}><th scope="row">{x.code.replaceAll('-', ' ')}</th><td>{formatGbp(x.limit)}</td><td>{formatGbp(x.excess)}</td></tr>)}</tbody></table></div>
    <h3>Endorsements</h3>{terms.endorsements.length ? terms.endorsements.map(x => <p key={x.decisionId + x.code}><strong>{x.code} · v{x.version}</strong> {x.wording}</p>) : <p>No endorsements applied.</p>}
    <h3>Contract conditions</h3>{terms.conditions.length ? terms.conditions.map(x => <p key={x.id}>{x.definition.code.replaceAll('-', ' ')} · {x.state}</p>) : <p>No contractual conditions.</p>}
    <p>Premium collector: {terms.settlement.collector} · Commission settlement: {terms.settlement.mode} · Commission {terms.settlement.commissionRateBps / 100}% · Fee share {terms.settlement.feeShareBps / 100}%</p>
    <details><summary>Exact proposal, rating factors and provenance</summary><QuoteProposalDetails value={rating.input} proposal={rating.input} questionLabels={questionLabels} />{rating.factors.map((x, index) => <p key={index}>{x.label} · {x.direction === 'discount' ? '−' : '+'}{formatGbp(x.amount)}</p>)}<dl className="underwriting-provenance">{[['Terms version', terms.id], ['Terms hash', terms.termsHash], ['Rating', terms.ratingId], ['Revision', terms.revisionId], ['Template version', terms.templateVersionId], ['Agency terms', terms.agencyTermsVersionId]].map(([label, value]) => <div key={label}><dt>{label}</dt><dd><code>{value}</code></dd></div>)}</dl></details>
  </>;
}
function Delivery({ item, quoteId, run }: { item: QuotationDelivery; quoteId: string; run: (request: DecisionRequest) => void }) {
  const job = useQuoteResource<{ state: string; attempts: number; attemptLimit: number; retryAllowed: boolean }>(`/api/v1/jobs/${item.jobId}`);
  const [reason, setReason] = useState('');
  const labels: Record<string, string> = { queued: 'Demo delivery queued', delivered: 'Demo delivery completed', failed: 'Demo delivery failed', superseded: 'Delivery superseded — context changed' };
  return <article className="quote-driver-card" data-delivery-id={item.id}><h3>{labels[item.state] ?? 'Delivery status unavailable'}</h3><p>{item.recipients.map(x => `${x.name} (${x.email})`).join(', ')}</p><p>Requested {new Date(item.queuedAt).toLocaleString('en-GB')}{item.completedAt && ` · Completed ${new Date(item.completedAt).toLocaleString('en-GB')}`}</p><p className="underwriting-provenance">Terms <code>{item.termsVersionId}</code></p>{item.errorCode && <p>{item.errorCode.replaceAll('-', ' ')}</p>}<details><summary>Delivery attempts ({item.attempts.length})</summary>{item.attempts.map(x => <p key={x.number}>Attempt {x.number}: {x.outcome} · {new Date(x.startedAt).toLocaleString('en-GB')}{x.errorCode && ` · ${x.errorCode}`}</p>)}</details>{!job.data ? <LoadFeedback error={job.error} retry={job.refresh} /> : <><p>Job {job.data.state} · Attempts {job.data.attempts} of {job.data.attemptLimit}</p>{job.data.retryAllowed && job.etag && <fieldset className="quote-reference-fields"><legend>Recover original delivery</legend><label>Delivery recovery reason<textarea value={reason} maxLength={1000} onChange={event => setReason(event.target.value)} /></label><button className="button" disabled={!reason.trim()} onClick={() => run({ command: underwritingWrite(quoteId, `/api/v1/jobs/${item.jobId}/retry`, job.etag!, { reason }), jobId: item.jobId, jobKind: 'quote-delivery', label: 'Recover original delivery', description: 'Extend the bounded retry budget for this exact retained quotation and recipient snapshot.' })}>Review delivery recovery</button></fieldset>}</>}</article>;
}
