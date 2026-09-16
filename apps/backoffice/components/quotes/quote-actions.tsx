'use client';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useEffect, useRef, useState } from 'react';
import { csrfToken, type Actor } from '../../lib/auth';
import { quoteFetch, QuoteError, sendQuoteCommand, staleQuoteFailure, uncertainQuoteFailure, type PendingQuoteCommand, type QuoteView } from '../../lib/quotes';
import { ClientChoice, RelationshipChoice } from './quote-create';
import { LoadFeedback, useQuoteResource } from './shared';

type Terms = { agencyTermsVersionId: string; version: number; effectiveFrom: string; confirmationRequired: boolean };
export function QuoteActions({ actorId, quote, etag, sourceRevisionId, refresh }: { actorId: string; quote: QuoteView; etag: string; sourceRevisionId: string; refresh: () => void }) {
  const [action, setAction] = useState<'clone' | 'withdraw'>();
  return <><div className="quote-row-actions">
    <button className="button" disabled={!quote.capabilities.canClone} onClick={() => setAction('clone')}>Clone selected revision</button>
    <button className="button" disabled={!quote.capabilities.canWithdraw} onClick={() => setAction('withdraw')}>Withdraw quote</button>
  </div>{action && <ActionDialog actorId={actorId} quote={quote} etag={etag} sourceRevisionId={sourceRevisionId} action={action} close={() => setAction(undefined)} refresh={refresh} />}</>;
}

function ActionDialog({ actorId, quote, etag, sourceRevisionId, action, close, refresh }: { actorId: string; quote: QuoteView; etag: string; sourceRevisionId: string; action: 'clone' | 'withdraw'; close: () => void; refresh: () => void }) {
  const router = useRouter(); const dialog = useRef<HTMLDialogElement>(null);
  const [client, setClient] = useState({ id: quote.clientId, name: quote.clientName });
  const [relationship, setRelationship] = useState({ id: quote.relationshipId, agencyId: quote.agencyId, name: quote.agencyName });
  const [chooseClient, setChooseClient] = useState(false); const [chooseAgency, setChooseAgency] = useState(false);
  const [reason, setReason] = useState(''); const [confirm, setConfirm] = useState(false);
  const [busy, setBusy] = useState(false); const [uncertain, setUncertain] = useState(false); const [error, setError] = useState(''); const [stale, setStale] = useState(false);
  const pending = useRef<PendingQuoteCommand | null>(null); const guard = useRef({ busy: false, uncertain: false });
  const terms = useQuoteResource<Terms>(action === 'clone' && relationship.id ? `/api/v1/quotes/${quote.id}/clone-terms?sourceRevisionId=${sourceRevisionId}&relationshipId=${relationship.id}` : null);
  useEffect(() => {
    dialog.current?.showModal(); const node = dialog.current;
    const blocked = () => guard.current.busy || guard.current.uncertain;
    const warn = () => setError('Confirm the pending action with Retry same action before leaving.');
    const cancel = (event: Event) => { event.preventDefault(); if (blocked()) warn(); else close(); };
    const unload = (event: BeforeUnloadEvent) => { if (blocked()) { event.preventDefault(); event.returnValue = ''; } };
    const click = (event: MouseEvent) => { if (blocked() && (event.target as Element).closest?.('a[href], .account-dropdown button')) { event.preventDefault(); event.stopPropagation(); warn(); } };
    const navigation = (window as Window & { navigation?: EventTarget }).navigation;
    const navigate = (event: Event) => { if (blocked() && event.cancelable) { event.preventDefault(); warn(); } };
    const url = window.location.href; const state: unknown = window.history.state;
    const pop = (event: PopStateEvent) => { if (!navigation && blocked()) { event.stopImmediatePropagation(); window.history.pushState(state, '', url); warn(); } };
    node?.addEventListener('cancel', cancel); window.addEventListener('beforeunload', unload); document.addEventListener('click', click, true);
    navigation?.addEventListener('navigate', navigate); window.addEventListener('popstate', pop, true);
    return () => { node?.removeEventListener('cancel', cancel); window.removeEventListener('beforeunload', unload); document.removeEventListener('click', click, true);
      navigation?.removeEventListener('navigate', navigate); window.removeEventListener('popstate', pop, true); };
  }, [close]);
  const frozen = busy || uncertain || stale;
  async function submit() {
    if (guard.current.busy || stale) return;
    const recovering = guard.current.uncertain;
    if (!recovering) {
      if (!reason.trim() || (action === 'clone' && (!terms.data || (terms.data.confirmationRequired && !confirm)))) return;
      pending.current = Object.freeze({ method: 'POST', url: `/api/v1/quotes/${quote.id}/${action}`, key: crypto.randomUUID(), etag,
        body: JSON.stringify(action === 'withdraw' ? { reason: reason.trim() } : { sourceRevisionId, relationshipId: relationship.id, reason: reason.trim(),
          ...(terms.data?.confirmationRequired ? { confirmedTermsId: terms.data.agencyTermsVersionId } : {}) }), ...(action === 'withdraw' ? { expectedId: quote.id } : {}) });
    }
    if (!pending.current) return;
    guard.current.busy = true; setBusy(true); setError(''); let attempted = false;
    try {
      const csrf = await csrfToken(); const account = await quoteFetch<Actor>('/api/v1/account');
      if (account.data.id !== actorId) throw new QuoteError(403);
      attempted = true; const receipt = await sendQuoteCommand(pending.current, csrf);
      guard.current = { busy: false, uncertain: false }; pending.current = null; setUncertain(false);
      close(); if (action === 'clone') router.push(`/quotes/${receipt.id}`); else refresh();
    } catch (failure) {
      const retain = recovering || (attempted && uncertainQuoteFailure(failure)); guard.current.uncertain = retain; setUncertain(retain);
      if (!retain) { pending.current = null; setStale(staleQuoteFailure(failure)); }
      setError(failure instanceof Error ? failure.message : 'The action could not be confirmed.');
    } finally { guard.current.busy = false; setBusy(false); }
  }
  return <dialog className="agency-dialog agency-terms-dialog" ref={dialog} aria-labelledby="quote-action-title">
    <h2 id="quote-action-title">{action === 'clone' ? 'Clone quote revision' : 'Withdraw this quote'}</h2>
    <p>{action === 'clone' ? 'Create a separate draft from the selected saved revision. Supporting documents, lookup decisions and progression do not transfer.' : 'Withdrawal closes this quote permanently. Its current rating no longer applies. Saved revisions and evidence remain available in its history.'}</p>
    <fieldset disabled={frozen}>
      {action === 'clone' && <><p>Destination: <strong>{client.name} · {relationship.name || 'Choose an agency'}</strong></p>
        <div className="quote-row-actions"><button type="button" className="button" onClick={() => setChooseClient(value => !value)}>Choose another client</button><button type="button" className="button" onClick={() => setChooseAgency(value => !value)}>Choose agency relationship</button></div>
        {chooseClient && <ClientChoice onSelect={item => { setClient({ id: item.id, name: item.legalName }); setRelationship({ id: '', agencyId: '', name: '' }); setConfirm(false); setChooseClient(false); setChooseAgency(true); }} />}
        {chooseAgency && <RelationshipChoice clientId={client.id} selected={relationship.id} onSelect={item => { setRelationship({ id: item.id, agencyId: item.agencyId, name: item.agencyName }); setConfirm(false); setChooseAgency(false); }} />}
        {!terms.data ? relationship.id && <LoadFeedback error={terms.error} retry={terms.refresh} /> : <>
          <p>Destination terms: version {terms.data.version}, effective {terms.data.effectiveFrom}. <Link href={`/agencies/${relationship.agencyId}`} target="_blank" rel="noopener noreferrer">Review agency terms</Link></p>
          {terms.data.confirmationRequired && <label className="contact-check"><input type="checkbox" checked={confirm} onChange={event => setConfirm(event.target.checked)} />I have reviewed and accept these different destination terms.</label>}
        </>}
      </>}
      <label>Reason<textarea aria-label={action === 'clone' ? 'Clone reason' : 'Withdrawal reason'} maxLength={1000} value={reason} onChange={event => setReason(event.target.value)} /></label>
    </fieldset>
    {error && <p role="alert" className="error-message">{error}</p>}{uncertain && <p role="status">The result is unconfirmed. Retry the same action to recover its saved outcome.</p>}
    {stale && <p>The saved quote or terms changed. Close this dialog and reload the quote before reviewing the action again.</p>}
    <div className="quote-row-actions"><button type="button" className="button" disabled={busy || uncertain} onClick={close}>Cancel</button>
      <button type="button" className="button button-primary" disabled={busy || stale || (!uncertain && (!reason.trim() || (action === 'clone' && (!terms.data || (terms.data.confirmationRequired && !confirm)))))} onClick={() => void submit()}>{busy ? 'Confirming…' : uncertain ? 'Retry same action' : action === 'clone' ? 'Create cloned draft' : 'Confirm withdrawal'}</button></div>
  </dialog>;
}
