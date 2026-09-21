'use client';
import { TaskCreateEntry } from '../operations/task-create-entry';
import Link from 'next/link';
import { useEffect, useRef, useState } from 'react';
import { Panel, Status } from '../primitives';
import { quoteFetch, uncertainQuoteFailure } from '../../lib/quotes';
import { sendServicing, sendServicingRating, servicingCommand, type ServicingCommand, type ServicingDraft, type ServicingEditor, type ServicingProposal } from '../../lib/servicing-api';
import { matchingServicingEditor, setCoverEffectiveIntent, setServicingDateBasis } from '../../lib/servicing-proposal';
import { ServicingEffectiveFields } from './servicing-effective-fields';
import { ServicingSavedReview } from './servicing-saved-review';
import { ServicingDriverEditor } from './servicing-driver-editor';
import { ServicingVehicleEditor } from './servicing-vehicle-editor';
import { ServicingPremisesEditor } from './servicing-premises-editor';
import { ServicingBusinessEditor } from './servicing-business-editor';
import { ServicingPolicyholderEditor } from './servicing-policyholder-editor';
import { ServicingRating } from './servicingrating';
import { RenewalPreparation } from './renewalpreparation';
import { RenewalLifecyclePanel } from './renewallifecycle';
import { CancellationReview } from './cancellationreview';
import { CancellationIssued } from './cancellationissued';
import { cancellationReasons, type CancellationReason } from '../../lib/cancellation-review';
import { ServicingEvidence } from './servicing-evidence';
import { ServicingCoverEditor } from './servicing-cover-editor';
import type { QuoteFormCatalogue } from '../../lib/quote-catalogue';
import type { CommercialCatalogue } from '../../lib/commercial-capture';
import type { CommercialServicingDraft } from '../../lib/commercial-servicing';
import { CommercialServicingWorkspace } from './commercial-servicing-workspace';
import { useQuoteResource, LoadFeedback } from '../quotes/shared';

type WorkspaceProps = { draftId: string; actorId: string; canTakeover: boolean; catalogue: QuoteFormCatalogue };
export function ServicingWorkspace(props: WorkspaceProps & { commercialCatalogue: CommercialCatalogue }) {
  const record = useQuoteResource<ServicingDraft<unknown>>(`/api/v1/drafts/${props.draftId}`);
  if (!record.data || !record.etag) return <Panel title="Servicing draft"><LoadFeedback error={record.error} retry={record.refresh} /></Panel>;
  if (record.data.context?.productCode === 'commercial-combined') return <CommercialServicingWorkspace draftId={props.draftId} actorId={props.actorId} canTakeover={props.canTakeover} catalogue={props.commercialCatalogue} initial={{data: record.data as CommercialServicingDraft, etag: record.etag}} />;
  if (['motor-trade-road-risks', 'motor-trade-combined'].includes(record.data.context?.productCode ?? '')) return <MotorServicingWorkspace {...props} />;
  return <Panel title="Servicing draft"><p>This product does not have an available servicing editor.</p></Panel>;
}
function MotorServicingWorkspace({ draftId, actorId, canTakeover, catalogue }: WorkspaceProps) {
  const [view, setView] = useState<{ data: ServicingDraft; etag: string } | null>(null), [proposal, setProposal] = useState<ServicingProposal | null>(null);
  const [fence, setFence] = useState<string | null>(null), [busy, setBusy] = useState(false), [error, setError] = useState(''), [notice, setNotice] = useState('');
  const [reason, setReason] = useState(''), [retry, setRetry] = useState(false), [dirty, setDirty] = useState(false);
  const [confirmAbandon, setConfirmAbandon] = useState(false);
  const [proofPending, setProofPending] = useState(false);
  const [renewalReady, setRenewalReady] = useState({etag: '', ready: false});
  const proofPendingRef = useRef(false);
  const [observedAt, setObservedAt] = useState(0);
  const [editor, setEditor] = useState<{ data: ServicingEditor; etag: string | null } | null>(null);
  const [editorReadOk, setEditorReadOk] = useState(false);
  const dirtyRef = useRef(false), pending = useRef<ServicingCommand | null>(null), busyRef = useRef(false);
  const url = `/api/v1/drafts/${draftId}`;
  useEffect(() => {
    const controller = new AbortController();
    async function refresh() {
      setObservedAt(Date.now());
      if (busyRef.current || proofPendingRef.current) return;
      try {
        const [draftRead, editorRead] = await Promise.allSettled([
          quoteFetch<ServicingDraft>(url, { signal: controller.signal }), quoteFetch<ServicingEditor>(url + '/editor', { signal: controller.signal })]);
        if (controller.signal.aborted || busyRef.current || proofPendingRef.current) return;
        if (draftRead.status === 'rejected') throw draftRead.reason;
        const result = draftRead.value; if (!result.etag) return;
        setEditorReadOk(editorRead.status === 'fulfilled');
        if (editorRead.status === 'fulfilled') setEditor(editorRead.value);
        setView({ data: result.data, etag: result.etag }); if (!dirtyRef.current) setProposal(result.data.proposal);
      } catch { if (!controller.signal.aborted) setError('Unable to refresh ownership. Your local edits are retained.'); }
    }
    void refresh(); const timer = setInterval(() => void refresh(), 5000);
    return () => { controller.abort(); clearInterval(timer); };
  }, [url]);
  const lease = view?.data.lease;
  const editing = !!(view?.data.state === 'draft' && fence && lease?.active && lease.holderId === actorId && lease.leaseToken === fence && Date.parse(lease.expiresAt) > observedAt);
  const otherEditor = !!(lease?.active && lease.holderId !== actorId && Date.parse(lease.expiresAt) > observedAt);
  function change(next: ServicingProposal) { dirtyRef.current = true; setDirty(true); setProposal(next); }
  function attempt(action: () => ServicingProposal) { try { change(action()); setError(''); } catch (failure) { setError(failure instanceof Error ? failure.message : 'This draft change could not be applied.'); } }
  async function run(action: 'acquire' | 'takeover' | 'renew' | 'release' | 'save' | 'abandon' | 'rate') {
    if (!view || !proposal || busyRef.current || proofPendingRef.current) return; busyRef.current = true; setBusy(true); setError(''); setNotice('');
    try {
      if (!pending.current) {
        const path = action === 'rate' ? '/rate' : action === 'save' ? '/proposal' : action === 'abandon' ? '/abandon' : '/lease';
        const method = action === 'release' ? 'DELETE' : action === 'renew' || action === 'save' ? 'PUT' : 'POST';
        const body = action === 'rate' ? { revisionId: view.data.revisionId, reason: proposal.reason } : action === 'save' ? proposal : action === 'abandon' ? { reason } : action === 'acquire' ? { mode: 'acquire' } : action === 'takeover' ? { mode: 'takeover', reason } : undefined;
        pending.current = servicingCommand(url + path, method, view.etag, body, fence ?? undefined);
      }
      const sent = pending.current;
      if (sent.url.endsWith('/rate')) {
        await sendServicingRating(sent);
        const refreshed = await quoteFetch<ServicingDraft>(url);
        if (!refreshed.etag) throw new Error('Draft refresh required.');
        setView({ data: refreshed.data, etag: refreshed.etag });
        pending.current = null; setRetry(false); setNotice('Rating requested. Results will appear when processing finishes.'); return;
      }
      const result = await sendServicing(sent); setView(result);
      if (sent.url.endsWith('/lease') && sent.method === 'POST') setFence(result.data.lease?.leaseToken ?? null);
      if (sent.method === 'DELETE' || result.data.state !== 'draft') setFence(null);
      if (sent.url.endsWith('/proposal')) { dirtyRef.current = false; setDirty(false); setProposal(result.data.proposal); }
      pending.current = null; setRetry(false); setNotice('Draft action saved.');
    } catch (failure) {
      const uncertain = uncertainQuoteFailure(failure); setRetry(uncertain);
      if (!uncertain) { pending.current = null; setFence(null); }
      setError(uncertain ? 'The result is unconfirmed. Retry the same action before continuing.' : 'This action could not be applied. Your local edits are retained; refresh ownership and acquire the current lease.');
    } finally { busyRef.current = false; setBusy(false); }
  }
  function proofPendingChanged(value: boolean) { proofPendingRef.current = value; setProofPending(value); }
  async function proofSaved() {
    const [draftRead, editorRead] = await Promise.all([quoteFetch<ServicingDraft>(url), quoteFetch<ServicingEditor>(url + '/editor')]);
    if (!draftRead.etag || draftRead.data.id !== draftId || !matchingServicingEditor({ data: draftRead.data, etag: draftRead.etag }, editorRead)) throw new Error('Saved draft readback is unconfirmed.');
    setView({ data: draftRead.data, etag: draftRead.etag }); setEditor(editorRead); setEditorReadOk(true);
    if (!dirtyRef.current) setProposal(draftRead.data.proposal);
  }
  if (!view || !proposal) return <Panel title="Servicing draft"><div className="quote-rail-body"><p role="status">{error || 'Loading saved draft…'}</p></div></Panel>;
  const insured = editorReadOk && matchingServicingEditor(view, editor) ? editor!.data.assessment.base.insured : undefined;
  const insuredLabel = typeof insured?.legalName === 'string' ? insured.legalName : [insured?.firstName, insured?.surname].filter(value => typeof value === 'string').join(' ');
  return <><div className="page-heading"><div><h1>{view.data.kind === 'adjustment' ? 'Policy adjustment' : view.data.kind === 'renewal' ? 'Renewal draft' : 'Cancellation draft'}</h1><p>{view.data.state === 'issued' ? view.data.kind === 'cancellation' ? 'Cancellation issued · Decision saved' : view.data.kind === 'renewal' ? 'Renewal issued · New term saved' : 'Issued adjustment · Effective changes saved' : 'Saved proposal · Issued cover remains unchanged'}</p></div><Link className="button" href={`/policies/${view.data.policyId}`}>Back to policy</Link><TaskCreateEntry parent={{kind: "servicing-draft", id: draftId, label: view.data.kind + " draft"}} /></div>
    <dl className="underwriting-provenance" aria-label="Draft context">
      <div><dt>Policy</dt><dd>{view.data.context ? <Link href={`/policies/${view.data.policyId}`}>{view.data.context.policyReference}</Link> : 'Policy reference unavailable'}</dd></div>
      <div><dt>Insured on issued base</dt><dd>{insuredLabel || 'Insured details unavailable'}</dd></div>
      <div><dt>Prepared by</dt><dd>{view.data.context?.preparedBy.label ?? 'Preparer unavailable'}</dd></div>
      <div><dt>Prepared</dt><dd>{new Date(view.data.createdAt).toLocaleString('en-GB', { timeZone: 'Europe/London' })} · London</dd></div>
    </dl>
    <section className="quote-saved-banner" aria-label="Editing lease"><div><h2>{editing ? 'You are editing this draft' : 'Read-only draft'}</h2><p>{view.data.state !== 'draft' ? 'This draft is closed. Its saved history remains available.' : otherEditor ? 'Another editor holds this draft. Your local edits are retained.' : editing ? `Lease expires ${new Date(lease!.expiresAt).toLocaleTimeString('en-GB')}. Renew to keep editing.` : 'Acquire an editing lease to make changes.'}</p></div><Status tone={editing ? 'success' : 'info'}>{view.data.state === 'draft' ? dirty ? 'Local changes retained' : 'Saved draft' : view.data.state === 'issued' ? 'Issued' : view.data.state === 'lapsed' ? 'Lapsed' : 'Abandoned'}</Status></section>
    {error ? <p role="alert">{error}</p> : null}{notice ? <p role="status">{notice}</p> : null}
    {view.data.kind === 'renewal' ? <RenewalLifecyclePanel termId={view.data.baseTermId} blocked={busy || retry || proofPending || dirty} /> : null}
    <div className="underwriting-layout"><div className="underwriting-main">{view.data.kind === 'renewal' ? <RenewalPreparation policyId={view.data.policyId} editor={editorReadOk && matchingServicingEditor(view, editor) ? editor!.data : null} draftId={draftId} etag={view.etag} fence={fence} editable={editing} blocked={busy || retry || proofPending} dirty={dirty} canReview={canTakeover} pendingChanged={proofPendingChanged} saved={proofSaved} readyChanged={setRenewalReady} /> : null}<Panel title="Requested change" note="Draft details"><form id="cancellation-proposal" className="quote-rail-body" onSubmit={event => { event.preventDefault(); void run('save'); }}>
      <fieldset disabled={!editing || busy || retry || proofPending}><div className="quote-form-grid">
        {view.data.kind === 'cancellation' ? <label>Cancellation reason<select aria-label="Cancellation reason" required value={proposal.cancellationReasonCode ?? ''} onChange={event => change({ ...proposal, cancellationReasonCode: event.target.value as CancellationReason })}><option value="" disabled>Select a reason</option>{cancellationReasons.map(([code, label]) => <option key={code} value={code}>{label}</option>)}</select></label> : null}
        <label>Reason for change<textarea aria-label="Reason for change" minLength={10} maxLength={2000} required value={proposal.reason} onChange={event => change({ ...proposal, reason: event.target.value })} /></label>
        <label>Requested by<select aria-label="Requested by" value={proposal.requestedBy.kind} onChange={event => {
          const kind = event.target.value as ServicingProposal['requestedBy']['kind']; change({ ...proposal, requestedBy: kind === 'internal' ? { kind } : { kind, name: proposal.requestedBy.name ?? '' } });
        }}><option value="internal">Internal request</option><option value="insured">Insured</option><option value="broker">Broker</option></select></label>
        {proposal.requestedBy.kind !== 'internal' ? <label>Requester name<input aria-label="Requester name" required maxLength={200} value={proposal.requestedBy.name ?? ''} onChange={event => change({ ...proposal, requestedBy: { ...proposal.requestedBy, name: event.target.value } })} /></label> : null}
        {view.data.kind !== 'renewal' ? <><ServicingEffectiveFields intent={proposal.commonEffectiveIntent} change={intent => change({ ...proposal, commonEffectiveIntent: intent })} />
        {view.data.kind !== 'cancellation' ? <label>Effective date basis<select aria-label="Effective date basis" value={proposal.dateBasis ?? 'shared'} onChange={event => attempt(() => setServicingDateBasis(proposal, event.target.value as 'shared' | 'per-cover-change'))}><option value="shared">One shared date</option><option value="per-cover-change">Individual dates for cover changes</option></select></label> : null}</> : null}
      </div><p className="client-help">{view.data.kind === 'renewal' ? 'All renewal risk changes apply at the prepared renewal inception.' : 'Driver changes cannot be backdated. Other backdated changes require current senior authority. Cover dates must be on or after the shared date and within the policy term.'}</p>
      {proposal.changes.map((item, index) => <fieldset className="quote-reference-fields" key={item.changeId}><legend>Change {index + 1} · {item.operation} {item.kind}</legend>
        {item.kind === 'cover' && proposal.dateBasis === 'per-cover-change' ? <><label><input type="checkbox" checked={!!item.effectiveIntent} onChange={event => attempt(() => setCoverEffectiveIntent(proposal, item.changeId, event.target.checked ? proposal.commonEffectiveIntent : undefined))} /> Use an individual cover date</label>
          {item.effectiveIntent ? <div className="quote-form-grid"><ServicingEffectiveFields prefix={`Cover change ${index + 1}`} intent={item.effectiveIntent} change={intent => attempt(() => setCoverEffectiveIntent(proposal, item.changeId, intent))} /></div> : null}</> : null}
        <button type="button" className="button" onClick={() => change({ ...proposal, changes: proposal.changes.filter(current => current.changeId !== item.changeId) })}>Remove proposed change {index + 1}</button>
      </fieldset>)}
      </fieldset><p>{proposal.changes.length} proposed risk changes{dirty ? ' including local edits' : ' saved'}. Saved draft details do not alter issued cover.</p>
      <button className="button button-primary" type="submit" disabled={!editing || busy || retry || proofPending}>Save draft</button>
    </form></Panel>{view.data.kind === 'cancellation' && view.data.state === 'issued' ? <CancellationIssued draftId={draftId} /> : view.data.kind === 'cancellation' ? <CancellationReview draftId={draftId} etag={view.etag} fence={fence} editable={editing} blocked={busy || retry || proofPending} dirty={dirty} canReview={canTakeover} pendingChanged={proofPendingChanged} saved={proofSaved} /> : null}{view.data.kind !== 'cancellation' && editor?.data.draftId === draftId ? <Panel title="Risk changes"><div className="quote-rail-body"><ServicingDriverEditor proposal={proposal} editor={editor.data} policyId={view.data.policyId} catalogue={catalogue} disabled={!editing || busy || retry || proofPending || !editorReadOk || !matchingServicingEditor(view, editor)} change={change} /><ServicingVehicleEditor proposal={proposal} editor={editor.data} policyId={view.data.policyId} catalogue={catalogue} disabled={!editing || busy || retry || proofPending || !editorReadOk || !matchingServicingEditor(view, editor)} change={change} /><ServicingPremisesEditor proposal={proposal} editor={editor.data} policyId={view.data.policyId} catalogue={catalogue} disabled={!editing || busy || retry || proofPending || !editorReadOk || !matchingServicingEditor(view, editor)} change={change} /><ServicingBusinessEditor proposal={proposal} editor={editor.data} policyId={view.data.policyId} catalogue={catalogue} disabled={!editing || busy || retry || proofPending || !editorReadOk || !matchingServicingEditor(view, editor)} change={change} /><ServicingPolicyholderEditor proposal={proposal} editor={editor.data} policyId={view.data.policyId} catalogue={catalogue} disabled={!editing || busy || retry || proofPending || !editorReadOk || !matchingServicingEditor(view, editor)} change={change} /><ServicingCoverEditor proposal={proposal} editor={editor.data} policyId={view.data.policyId} catalogue={catalogue} disabled={!editing || busy || retry || proofPending || !editorReadOk || !matchingServicingEditor(view, editor)} change={change} /></div></Panel> : null}<ServicingSavedReview editor={editorReadOk && matchingServicingEditor(view, editor) ? editor!.data : null} dirty={dirty} />{['adjustment','renewal'].includes(view.data.kind) ? <div id="renewal-rate"><ServicingRating baseTermPremium={view.data.context?.baseTermPremium} kind={view.data.kind === 'renewal' ? 'renewal' : 'adjustment'} draftId={draftId} draftEtag={view.etag} dirty={dirty} busy={busy || retry || proofPending} canRate={editing && !dirty && editorReadOk && matchingServicingEditor(view, editor) && editor!.data.assessment.readinessIssues.length === 0 && (view.data.kind === 'renewal' ? renewalReady.etag === view.etag && renewalReady.ready : proposal.changes.length > 0)} rate={() => void run('rate')} /></div> : null}{['adjustment','renewal'].includes(view.data.kind) ? <ServicingEvidence kind={view.data.kind === 'renewal' ? 'renewal' : 'adjustment'} draftId={draftId} revisionId={view.data.revisionId} etag={view.etag} fence={fence} editable={editing} blocked={busy || retry || proofPending} dirty={dirty} canReview={canTakeover} editor={editorReadOk && matchingServicingEditor(view, editor) ? editor!.data : null} pendingChanged={proofPendingChanged} saved={proofSaved} /> : null}</div><aside className="underwriting-rail" aria-label="Draft actions"><Panel title="Editing controls"><div className="quote-rail-body servicing-controls">
      {retry ? <button className="button button-primary" disabled={busy} onClick={() => void run('save')}>Retry same action</button> : null}
      <button className="button" disabled={busy || retry || proofPending || editing || otherEditor || view.data.state !== 'draft'} onClick={() => void run('acquire')}>Acquire editing lease</button>
      <button className="button" disabled={busy || retry || proofPending || !editing} onClick={() => void run('renew')}>Renew editing lease</button>
      <button className="button" disabled={busy || retry || proofPending || !editing} onClick={() => void run('release')}>Release editing lease</button>
      {['adjustment','renewal'].includes(view.data.kind) && <Link className="button" href="#servicing-referrals">Review referrals</Link>}
      {view.data.kind === 'adjustment' && <Link className="button" href="#servicing-referrals">Refer to capacity provider</Link>}
      <label className="quote-form-label">Takeover or abandonment reason<textarea aria-label="Takeover or abandonment reason" maxLength={2000} value={reason} onChange={event => setReason(event.target.value)} /></label>
      {canTakeover ? <button className="button" disabled={busy || retry || proofPending || !otherEditor || reason.trim().length < 10} onClick={() => void run('takeover')}>Take over editing</button> : null}
      <label><input type="checkbox" checked={confirmAbandon} onChange={event => setConfirmAbandon(event.target.checked)} /> I confirm this draft should be abandoned.</label>
      <button className="button" disabled={busy || retry || proofPending || !editing || !confirmAbandon || reason.trim().length < 10} onClick={() => void run('abandon')}>Abandon draft</button>
      <p className="client-help">Taking over stops the previous editor from saving. Abandonment closes this draft and retains its saved history.</p>
    </div></Panel><Panel title="Saved history"><div className="quote-rail-body"><p>Last saved {new Date(view.data.updatedAt).toLocaleString('en-GB')}</p><details><summary>Draft provenance</summary><p>Draft: {view.data.id}</p><p>Base version: {view.data.baseVersionId}</p><p>Revision: {view.data.revisionId}</p></details></div></Panel></aside></div>
  </>;
}
