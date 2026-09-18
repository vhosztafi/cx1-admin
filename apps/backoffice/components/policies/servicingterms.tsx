'use client';
import { useState } from 'react';
import { Status } from '../primitives';
import { currentProof, type ProofAssociation, type ProofPage, type ProofRequirement } from '../../lib/servicing-proof';
import { quoteFetch } from '../../lib/quotes';
import type { TermsDocument, TermsHistoryItem, TermsSnapshot, TermsView } from '../../lib/servicing-terms';
import { PageButtons, useProofRead } from './servicing-proof-read';
import { QuoteProposalDetails } from '../quotes/quote-history';

const date = (value: string) => new Date(value).toLocaleString('en-GB');
const money = (value: string) => new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP' }).format(Number(value));
const blocker = (code: string | null) => ({
  'servicing-proof-review-required': 'Complete and review the supporting proof before continuing.',
  'servicing-signature-required': 'Attach and review the signed statement for these terms.',
  'servicing-condition-outstanding': 'Resolve the outstanding underwriting conditions.',
  'servicing-referral-outstanding': 'An underwriting decision is still required.',
  'servicing-capacity-outstanding': 'Current carrier permission is still required.',
  'servicing-terms-required': 'Prepare the current terms before sending.',
  'servicing-rating-expired': 'The rating has expired. Rate the saved changes again.',
}[code ?? ''] ?? 'Review the current rating, proof and underwriting decisions before continuing.');

export function ServicingTerms({ draftId, revisionId, cycleId, etag, active, paused, requirements, evidence, run }: {
  draftId: string; revisionId: string; cycleId: string | null; etag: string; active: boolean; paused: boolean;
  requirements: ProofRequirement[]; evidence: ProofAssociation[]; run: (path: string, body: unknown) => void;
}) {
  const base = `/api/v1/drafts/${draftId}/terms`;
  const read = useProofRead<TermsView>(cycleId ? base : null, etag, paused);
  const view = read.data;
  const [templateId, setTemplateId] = useState(''), [recipients, setRecipients] = useState<string[]>([]);
  const [accepter, setAccepter] = useState(''), [acceptedAt, setAcceptedAt] = useState(''), [channel, setChannel] = useState('email'), [proofId, setProofId] = useState('');
  const [observedAt, setObservedAt] = useState(0);
  const enabled = active && read.current && view?.cycleId === cycleId && view?.revisionId === revisionId && view.applicable;
  const template = templateId || view?.templates[0]?.id || '';
  const terms = view?.terms;
  const proof = requirements.find(x => x.code === 'acceptance-proof' && x.termsVersionId === terms?.id);
  const proofs = proof ? evidence.filter(x => currentProof(x, proof) && x.reviewOutcome === 'accepted') : [];
  const instant = acceptedAt ? new Date(acceptedAt).getTime() : NaN;
  const received = Number.isFinite(instant) && instant <= observedAt && !!view?.delivery?.completedAt && instant >= Date.parse(view.delivery.completedAt);
  const delivered = terms?.applicable && view?.delivery?.termsVersionId === terms.id && view.delivery.state === 'delivered';
  return <section aria-label="Servicing terms and acceptance" className="servicing-terms" id="servicing-terms">
    <h3>Terms and acceptance</h3><p>Prepare the agreed change, review the signed statement, then send the terms and record the customer’s acceptance.</p>
    {read.error && <p role="status">{read.error}</p>}
    {view && <>
      <fieldset className="quote-reference-fields" disabled={!enabled || !view.canPrepare}><legend>Prepare terms</legend>
        <label>Terms template<select aria-label="Terms template" value={template} onChange={e => setTemplateId(e.target.value)}>{view.templates.map(x => <option key={x.id} value={x.id}>{x.title} · Version {x.version}</option>)}</select></label>
        <button className="button" disabled={!view.templates.some(x => x.id === template)} onClick={() => run('/terms/prepare', { cycleId, ratingId: view.ratingId, templateVersionId: template })}>Prepare servicing terms</button>
      </fieldset>
      {view.blockingCode && <p role="status">{blocker(view.blockingCode)}</p>}
      {terms && <><Status tone={read.current && terms.applicable ? 'success' : 'warning'}>{read.current && terms.applicable ? `Prepared terms · Version ${terms.sequence}` : 'Previous terms — prepare the current change'}</Status><Contract document={terms.document} />
        <p className="client-help">Attach and review the signed-statement proof in Supporting information. It must relate to this exact version.</p>
        <fieldset className="quote-reference-fields" disabled={!enabled || !view.canSend}><legend>Send prepared terms</legend>
          {view.recipientOptions.map(x => <label key={x.id}><input type="checkbox" checked={recipients.includes(x.id)} onChange={e => setRecipients(old => e.target.checked ? [...old, x.id] : old.filter(id => id !== x.id))} /> {x.name} · {x.email}</label>)}
          {!view.recipientOptions.length && <p>No current email contacts are available for this customer relationship.</p>}
          <button className="button button-primary" disabled={!recipients.length || recipients.length > 20 || recipients.some(id => !view.recipientOptions.some(x => x.id === id))} onClick={() => run('/terms/send', { cycleId, termsVersionId: terms.id, recipientContactIds: recipients })}>Send servicing terms</button>
          <p className="client-help">Demo delivery records processing and its outcome. No email is sent.</p>
        </fieldset>
        {view.sendBlockingCode && <p role="status">{blocker(view.sendBlockingCode)}</p>}
      </>}
      {view.delivery && <article className="quote-driver-card" aria-label="Terms delivery"><h4>Terms delivery</h4><Status tone={delivered ? 'success' : 'warning'}>{view.delivery.state}</Status>
        <p>Queued {date(view.delivery.queuedAt)}{view.delivery.completedAt && ` · Completed ${date(view.delivery.completedAt)}`}</p>
        <p>{view.delivery.recipients.map(x => `${x.name} (${x.email})`).join(', ')}</p>
        {view.delivery.state === 'queued' && <p role="status">Delivery is processing. Acceptance is available only after delivery succeeds.</p>}
        {['failed', 'superseded'].includes(view.delivery.state) && <p>Review current terms, proof and recipients, then send again. This attempt remains in history.</p>}
      </article>}
      <fieldset className="quote-reference-fields" disabled={!enabled || !delivered || !view.canSend}><legend>Record customer acceptance</legend>
        <label>Accepted by<input aria-label="Accepted by" value={accepter} maxLength={200} onChange={e => setAccepter(e.target.value)} /></label>
        <label>Acceptance received at<input aria-label="Acceptance received at" type="datetime-local" step="1" value={acceptedAt} onChange={e => {setAcceptedAt(e.target.value);setObservedAt(Date.now());}} /></label>
        <p className="client-help">Enter your local time, after successful delivery. The recorded instant is retained in UTC.</p>
        <label>Acceptance channel<select aria-label="Acceptance channel" value={channel} onChange={e => setChannel(e.target.value)}><option value="email">Email</option><option value="written">Written</option><option value="telephone">Telephone</option></select></label>
        <label>Reviewed acceptance proof<select aria-label="Reviewed acceptance proof" value={proofId} onChange={e => setProofId(e.target.value)}><option value="">Choose accepted proof on this evidence page</option>{proofs.map(x => <option key={x.id} value={x.id}>{x.fileName}</option>)}</select></label>
        {!proofs.length && <p>Attach and review acceptance-proof for this version in Supporting information.</p>}
        <button className="button button-primary" disabled={!accepter.trim() || !received || !proofs.some(x => x.id === proofId)} onClick={() => run('/acceptances', { cycleId, ratingId: view.ratingId, termsVersionId: terms!.id, deliveryId: view.delivery!.id, termsHash: terms!.termsHash, assuranceHash: view.assuranceHash, accepterLabel: accepter, acceptedAt: new Date(instant).toISOString(), channel, evidenceAssociationId: proofId })}>Record servicing acceptance</button>
      </fieldset>
      {view.acceptance && <article className="quote-driver-card" aria-label="Recorded acceptance"><h4>Recorded acceptance</h4><Status tone={read.current && view.acceptanceApplicable ? 'success' : 'warning'}>{read.current && view.acceptanceApplicable ? 'Current acceptance' : 'Historical acceptance — fresh confirmation required'}</Status>
        <p>{view.acceptance.accepterLabel} · {view.acceptance.channel} · {date(view.acceptance.acceptedAt)}</p><p>Acceptance is recorded separately from delivery. Final issue checks still apply.</p></article>}
    </>}
    <TermsHistory base={base} etag={etag} paused={paused} />
  </section>;
}

function Contract({ document }: { document: TermsDocument }) {
  return <details className="quote-driver-card" open><summary>{document.template.title}</summary><p>{document.template.notice}</p>
    <dl className="underwriting-premium">{Object.entries(document.price).filter(([key]) => key !== 'currency').map(([key, value]) => <div key={key} className={key === 'grossPayable' ? 'underwriting-total' : ''}><dt>{({ premium: 'Premium change', tax: 'Insurance premium tax', fee: 'Adjustment fee', brokerCommission: 'Broker commission', grossPayable: 'Total payable', netDue: 'Net due' } as Record<string, string>)[key]}</dt><dd>{money(value)}</dd></div>)}</dl>
    <p>Terms expire {date(document.expiresAt)}</p><h4>Effective changes</h4>{document.effectiveDates.map(value => <p key={value}>From {date(value)}</p>)}
    {document.slices.map(slice => <details key={slice.effectiveAt}><summary>Exact prepared risk from {date(slice.effectiveAt)}</summary><QuoteProposalDetails value={slice.proposal} proposal={slice.proposal} questionLabels={{}} /></details>)}
    {document.conditions.length > 0 && <><h4>Conditions and endorsements</h4>{document.conditions.map(x => <p key={x.id}>{x.code.replaceAll('-', ' ')} · {x.effectiveDates.map(date).join(', ')}</p>)}</>}
  </details>;
}

function TermsHistory({ base, etag, paused }: { base: string; etag: string; paused: boolean }) {
  const [kind, setKind] = useState('terms'), [open, setOpen] = useState(false), [page, setPage] = useState({ etag, cursor: '' });
  const [document, setDocument] = useState<TermsSnapshot | null>(null), [error, setError] = useState('');
  const cursor = page.etag === etag ? page.cursor : '';
  const read = useProofRead<ProofPage<TermsHistoryItem>>(open ? `${base}/history/${kind}?pageSize=10${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}` : null, etag, paused);
  async function show(id: string) { try { const result = await quoteFetch<TermsSnapshot>(`${base}/history/terms/${id}`); setDocument(result.data); setError(''); } catch { setError('This retained contract could not be loaded.'); } }
  return <details onToggle={e => setOpen(e.currentTarget.open)}><summary>Terms, delivery and acceptance history</summary>
    <p>Retained records show what happened. Historical acceptance does not grant current issue permission.</p>
    <label>Terms history type<select aria-label="Terms history type" disabled={paused} value={kind} onChange={e => { setKind(e.target.value); setPage({ etag, cursor: '' }); setDocument(null); }}><option value="terms">Prepared terms</option><option value="deliveries">Delivery attempts</option><option value="acceptances">Recorded acceptances</option></select></label>
    {read.error && <p role="status">{read.error}</p>}{error && <p role="alert">{error}</p>}
    {read.data?.items.map(x => <article key={x.id} className="quote-driver-card"><p>{x.label} · {x.state} · {date(x.recordedAt)}</p><button className="button" onClick={() => void show(x.termsVersionId)}>View retained contract</button></article>)}
    {read.data && <PageButtons cursor={cursor} next={read.data.nextCursor} disabled={paused} change={value => setPage({ etag, cursor: value })} />}
    {document && <><p>Historical contract prepared {date(document.preparedAt)}.</p><Contract document={document.document} /></>}
  </details>;
}
