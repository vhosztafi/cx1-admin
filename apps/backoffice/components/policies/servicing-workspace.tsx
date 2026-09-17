'use client';
import Link from 'next/link';
import { useEffect, useRef, useState } from 'react';
import { Panel, Status } from '../primitives';
import { quoteFetch, uncertainQuoteFailure } from '../../lib/quotes';
import { sendServicing, servicingCommand, type ServicingCommand, type ServicingDraft, type ServicingProposal } from '../../lib/servicing-api';

export function ServicingWorkspace({ draftId, actorId, canTakeover }: { draftId: string; actorId: string; canTakeover: boolean }) {
  const [view, setView] = useState<{ data: ServicingDraft; etag: string } | null>(null), [proposal, setProposal] = useState<ServicingProposal | null>(null);
  const [fence, setFence] = useState<string | null>(null), [busy, setBusy] = useState(false), [error, setError] = useState(''), [notice, setNotice] = useState('');
  const [reason, setReason] = useState(''), [retry, setRetry] = useState(false), [dirty, setDirty] = useState(false);
  const [confirmAbandon, setConfirmAbandon] = useState(false);
  const [observedAt, setObservedAt] = useState(0);
  const dirtyRef = useRef(false), pending = useRef<ServicingCommand | null>(null), busyRef = useRef(false);
  const url = `/api/v1/drafts/${draftId}`;
  useEffect(() => {
    const controller = new AbortController();
    async function refresh() {
      setObservedAt(Date.now());
      if (busyRef.current) return;
      try {
        const result = await quoteFetch<ServicingDraft>(url, { signal: controller.signal });
        if (controller.signal.aborted || busyRef.current || !result.etag) return;
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
  async function run(action: 'acquire' | 'takeover' | 'renew' | 'release' | 'save' | 'abandon') {
    if (!view || !proposal || busyRef.current) return; busyRef.current = true; setBusy(true); setError(''); setNotice('');
    try {
      if (!pending.current) {
        const path = action === 'save' ? '/proposal' : action === 'abandon' ? '/abandon' : '/lease';
        const method = action === 'release' ? 'DELETE' : action === 'renew' || action === 'save' ? 'PUT' : 'POST';
        const body = action === 'save' ? proposal : action === 'abandon' ? { reason } : action === 'acquire' ? { mode: 'acquire' } : action === 'takeover' ? { mode: 'takeover', reason } : undefined;
        pending.current = servicingCommand(url + path, method, view.etag, body, fence ?? undefined);
      }
      const sent = pending.current; const result = await sendServicing(sent); setView(result);
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
  if (!view || !proposal) return <Panel title="Servicing draft"><div className="quote-rail-body"><p role="status">{error || 'Loading saved draft…'}</p></div></Panel>;
  return <><div className="page-heading"><div><h1>{view.data.kind === 'adjustment' ? 'Policy adjustment' : view.data.kind === 'renewal' ? 'Renewal draft' : 'Cancellation draft'}</h1><p>Saved proposal · Issued cover remains unchanged</p></div><Link className="button" href={`/policies/${view.data.policyId}`}>Back to policy</Link></div>
    <section className="quote-saved-banner" aria-label="Editing lease"><div><h2>{editing ? 'You are editing this draft' : 'Read-only draft'}</h2><p>{view.data.state !== 'draft' ? 'This draft is closed. Its saved history remains available.' : otherEditor ? 'Another editor holds this draft. Your local edits are retained.' : editing ? `Lease expires ${new Date(lease!.expiresAt).toLocaleTimeString('en-GB')}. Renew to keep editing.` : 'Acquire an editing lease to make changes.'}</p></div><Status tone={editing ? 'success' : 'info'}>{view.data.state === 'draft' ? dirty ? 'Local changes retained' : 'Saved draft' : 'Abandoned'}</Status></section>
    {error ? <p role="alert">{error}</p> : null}{notice ? <p role="status">{notice}</p> : null}
    <div className="underwriting-layout"><div className="underwriting-main"><Panel title="Requested change" note="Draft details"><form className="quote-rail-body" onSubmit={event => { event.preventDefault(); void run('save'); }}>
      <fieldset disabled={!editing || busy || retry}><div className="quote-form-grid">
        <label>Reason for change<textarea aria-label="Reason for change" minLength={10} maxLength={2000} required value={proposal.reason} onChange={event => change({ ...proposal, reason: event.target.value })} /></label>
        <label>Effective date (London)<input type="date" required value={proposal.commonEffectiveIntent.localDate} onChange={event => change({ ...proposal, commonEffectiveIntent: { ...proposal.commonEffectiveIntent, localDate: event.target.value } })} /></label>
        <label>Effective time (London)<input type="time" required value={proposal.commonEffectiveIntent.localTime} onChange={event => change({ ...proposal, commonEffectiveIntent: { ...proposal.commonEffectiveIntent, localTime: event.target.value } })} /></label>
      </div></fieldset><p>{proposal.changes.length} proposed risk changes saved. Saved draft details do not alter issued cover.</p>
      <button className="button button-primary" type="submit" disabled={!editing || busy || retry}>Save draft</button>
    </form></Panel></div><aside className="underwriting-rail" aria-label="Draft actions"><Panel title="Editing controls"><div className="quote-rail-body servicing-controls">
      {retry ? <button className="button button-primary" disabled={busy} onClick={() => void run('save')}>Retry same action</button> : null}
      <button className="button" disabled={busy || retry || editing || otherEditor || view.data.state !== 'draft'} onClick={() => void run('acquire')}>Acquire editing lease</button>
      <button className="button" disabled={busy || retry || !editing} onClick={() => void run('renew')}>Renew editing lease</button>
      <button className="button" disabled={busy || retry || !editing} onClick={() => void run('release')}>Release editing lease</button>
      <label className="quote-form-label">Takeover or abandonment reason<textarea aria-label="Takeover or abandonment reason" maxLength={2000} value={reason} onChange={event => setReason(event.target.value)} /></label>
      {canTakeover ? <button className="button" disabled={busy || retry || !otherEditor || reason.trim().length < 10} onClick={() => void run('takeover')}>Take over editing</button> : null}
      <label><input type="checkbox" checked={confirmAbandon} onChange={event => setConfirmAbandon(event.target.checked)} /> I confirm this draft should be abandoned.</label>
      <button className="button" disabled={busy || retry || !editing || !confirmAbandon || reason.trim().length < 10} onClick={() => void run('abandon')}>Abandon draft</button>
      <p className="client-help">Taking over stops the previous editor from saving. Abandonment closes this draft and retains its saved history.</p>
    </div></Panel><Panel title="Saved history"><div className="quote-rail-body"><p>Last saved {new Date(view.data.updatedAt).toLocaleString('en-GB')}</p><details><summary>Draft provenance</summary><p>Draft: {view.data.id}</p><p>Base version: {view.data.baseVersionId}</p><p>Revision: {view.data.revisionId}</p></details></div></Panel></aside></div>
  </>;
}
