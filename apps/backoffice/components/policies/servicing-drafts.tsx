'use client';
import Link from 'next/link';
import { useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { Panel } from '../primitives';
import { useQuoteResource, LoadFeedback } from '../quotes/shared';
import { uncertainQuoteFailure } from '../../lib/quotes';
import { sendServicing, servicingCommand, type ServicingCommand } from '../../lib/servicing-api';

export function ServicingDrafts({ termId, baseVersionId }: { termId: string; baseVersionId: string }) {
  const list = useQuoteResource<{ items: { id: string; kind: string; state: string }[] }>(`/api/v1/terms/${termId}/drafts`);
  const [kind, setKind] = useState('adjustment'), [date, setDate] = useState(''), [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false), [error, setError] = useState(''), [retry, setRetry] = useState(false);
  const pending = useRef<ServicingCommand | null>(null), router = useRouter();
  async function create() {
    if (busy || !list.etag) return; setBusy(true); setError('');
    try {
      pending.current ??= servicingCommand(`/api/v1/terms/${termId}/drafts`, 'POST', list.etag, { kind, baseVersionId,
        commonEffectiveIntent: { localDate: date, localTime: '00:00', timeZone: 'Europe/London' }, reason });
      const saved = await sendServicing(pending.current); pending.current = null; setRetry(false); router.push(`/drafts/${saved.data.id}`);
    } catch (failure) {
      const uncertain = uncertainQuoteFailure(failure); setRetry(uncertain); if (!uncertain) { pending.current = null; list.refresh(); }
      setError(uncertain ? 'The result is unconfirmed. Retry the same creation.' : 'Unable to create this draft. Check the date and reason, or resume an existing draft.');
    } finally { setBusy(false); }
  }
  return <Panel title="Servicing drafts" note="Saved proposals are separate from issued cover"><div className="quote-rail-body">
    {!list.data ? <LoadFeedback error={list.error} retry={list.refresh} /> : <>
      {list.data.items.length ? <ul>{list.data.items.map(item => <li key={item.id}><Link href={`/drafts/${item.id}`}>Resume {item.kind} · {item.state}</Link>{item.kind === 'adjustment' && item.state === 'draft' && <> · <Link href={`/drafts/${item.id}#servicing-referrals`}>Open referrals</Link></>}</li>)}</ul> : <p>No servicing drafts saved for this term.</p>}
      <form onSubmit={event => { event.preventDefault(); void create(); }}><fieldset disabled={busy || retry}><div className="quote-form-grid">
        <label>Draft type<select aria-label="Draft type" value={kind} onChange={event => setKind(event.target.value)}><option value="adjustment">Policy adjustment</option><option value="renewal">Renewal</option><option value="cancellation">Cancellation</option></select></label>
        <label>Requested effective date<input type="date" required value={date} onChange={event => setDate(event.target.value)} /></label>
        <label>Reason for draft<textarea required minLength={10} maxLength={2000} value={reason} onChange={event => setReason(event.target.value)} /></label>
      </div></fieldset><p className="client-help">Dates use London time. Saving a draft does not change cover.</p>
        <button className="button button-primary" disabled={busy || !list.etag} type="submit">{busy ? 'Creating…' : retry ? 'Retry draft creation' : 'Create servicing draft'}</button>
      </form></>}
    {error ? <p role="alert">{error}</p> : null}
  </div></Panel>;
}
