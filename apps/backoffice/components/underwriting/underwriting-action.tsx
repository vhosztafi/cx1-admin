'use client';
import { useEffect, useRef, useState } from 'react';
import { csrfToken, type Actor } from '../../lib/auth';
import { quoteFetch, QuoteError, uncertainQuoteFailure, staleQuoteFailure, type PendingQuoteCommand, type QuoteView } from '../../lib/quotes';
import { underwritingCommand, sendUnderwritingCommand, type UnderwritingAction, type UnderwritingAssessment } from '../../lib/underwriting-api';

export const actionLabels: Record<UnderwritingAction, string> = { rate: 'Rate quote', submit: 'Submit for underwriting', 'return-to-draft': 'Return to draft', 'underwriting/refresh': 'Refresh published versions' };
export function UnderwritingActionDialog({ action, quote, assessment, actorId, close, completed }: {
  action: UnderwritingAction; quote: Pick<QuoteView, 'id' | 'revisionId' | 'reference' | 'clientName' | 'revisionNumber'>; assessment: UnderwritingAssessment; actorId: string; close: () => void; completed: () => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null), pending = useRef<PendingQuoteCommand | null>(null), guard = useRef({ busy: false, uncertain: false });
  const closeRef = useRef(close);
  useEffect(() => { closeRef.current = close; }, [close]);
  const [reason, setReason] = useState(''), [selection, setSelection] = useState(''), [confirmed, setConfirmed] = useState(false);
  const [busy, setBusy] = useState(false), [uncertain, setUncertain] = useState(false), [stale, setStale] = useState(false), [error, setError] = useState('');
  const [latest, setLatest] = useState<UnderwritingAssessment>();
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null, node = dialog.current;
    node?.showModal(); node?.querySelector<HTMLTextAreaElement>('textarea')?.focus();
    const blocked = () => guard.current.busy || guard.current.uncertain;
    const warn = () => setError('Use Retry same action to confirm the pending result before leaving.');
    const cancel = (event: Event) => { event.preventDefault(); if (blocked()) warn(); else closeRef.current(); };
    const keyboard = (event: KeyboardEvent) => {
      if (event.key !== 'Tab' || !node) return;
      const controls = Array.from(node.querySelectorAll<HTMLElement>('button, input, select, textarea, a[href], [tabindex]'))
        .filter(element => !element.matches(':disabled,[tabindex="-1"]') && element.getClientRects().length > 0);
      const first = controls[0], last = controls.at(-1);
      if (!first || !last) { event.preventDefault(); return; }
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    const unload = (event: BeforeUnloadEvent) => { if (blocked()) { event.preventDefault(); event.returnValue = ''; } };
    const click = (event: MouseEvent) => { if (blocked() && (event.target as Element).closest?.('a[href], .account-dropdown button')) { event.preventDefault(); event.stopPropagation(); warn(); } };
    const navigation = (window as Window & { navigation?: EventTarget }).navigation;
    const navigate = (event: Event) => { if (blocked() && event.cancelable) { event.preventDefault(); warn(); } };
    const url = window.location.href, state: unknown = window.history.state;
    const pop = (event: PopStateEvent) => { if (!navigation && blocked()) { event.stopImmediatePropagation(); window.history.pushState(state, '', url); warn(); } };
    node?.addEventListener('cancel', cancel); node?.addEventListener('keydown', keyboard); window.addEventListener('beforeunload', unload); document.addEventListener('click', click, true);
    navigation?.addEventListener('navigate', navigate); window.addEventListener('popstate', pop, true);
    return () => { node?.removeEventListener('cancel', cancel); node?.removeEventListener('keydown', keyboard); window.removeEventListener('beforeunload', unload); document.removeEventListener('click', click, true);
      navigation?.removeEventListener('navigate', navigate); window.removeEventListener('popstate', pop, true); previous?.focus(); };
  }, []);
  useEffect(() => { if (uncertain) dialog.current?.querySelector<HTMLButtonElement>('[data-retry-action]')?.focus(); }, [uncertain]);
  async function submit() {
    if (guard.current.busy || stale) return;
    const recovering = guard.current.uncertain; let attempted = false;
    guard.current.busy = true; setBusy(true); setError('');
    try {
      if (!recovering) {
        const option = assessment.refreshOptions.find(item => item.productVersionId === selection);
        if (action === 'underwriting/refresh' && (!option || !confirmed)) throw new Error('Select and confirm the approved version and terms.');
        const body = action === 'rate' ? { revisionId: quote.revisionId, reason }
          : action === 'underwriting/refresh' ? { revisionId: quote.revisionId, productVersionId: option!.productVersionId, confirmedTermsVersionId: option!.agencyTermsVersionId, reason }
            : { cycleId: assessment.context?.cycleId, reason };
        pending.current = underwritingCommand(quote.id, action, assessment.quoteEtag, body);
      }
      const [csrf, account] = await Promise.all([csrfToken(), quoteFetch<Actor>('/api/v1/account')]);
      if (account.data.id !== actorId) throw new QuoteError(403);
      attempted = true; await sendUnderwritingCommand(pending.current!, csrf);
      pending.current = null; guard.current = { busy: false, uncertain: false }; completed();
    } catch (failure) {
      const retain = recovering || attempted && uncertainQuoteFailure(failure);
      guard.current.uncertain = retain; setUncertain(retain);
      if (!retain) { pending.current = null; setStale(staleQuoteFailure(failure)); }
      setError(failure instanceof Error ? failure.message : 'The action could not be confirmed.');
    } finally { guard.current.busy = false; setBusy(false); }
  }
  const frozen = busy || uncertain || stale;
  return <dialog ref={dialog} className="agency-dialog agency-terms-dialog" aria-labelledby="underwriting-action-title">
    <h2 id="underwriting-action-title">{actionLabels[action]}</h2><p>{quote.reference} · {quote.clientName} · Revision {quote.revisionNumber}</p>
    <p>{action === 'return-to-draft' ? 'Return this quote to draft? Current rating and acceptance will no longer apply. History will be retained.'
      : action === 'rate' ? 'Request a rating for this saved revision. Risk editing closes while the request is processed.'
        : action === 'submit' ? 'Send this saved rating and its outstanding requirements to the configured underwriting team.' : 'Create a new revision using the selected published product and approved agency terms. Earlier revisions will be retained.'}</p>
    <fieldset disabled={frozen}>
      {action === 'underwriting/refresh' && <><label>Published version<select aria-label="Published version" value={selection} onChange={event => { setSelection(event.target.value); setConfirmed(false); }}><option value="">Select approved version</option>{assessment.refreshOptions.map(item => <option key={item.productVersionId} value={item.productVersionId}>{item.displayName} {item.versionLabel} · Agency terms {item.termsVersion} from {item.effectiveFrom}</option>)}</select></label>
        <label className="contact-check"><input type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} />I confirm the selected published version and approved agency terms.</label></>}
      <label>Reason<textarea aria-label="Underwriting action reason" maxLength={action === 'rate' || action === 'underwriting/refresh' ? 1000 : 2000} value={reason} onChange={event => setReason(event.target.value)} /></label>
    </fieldset>
    {error && <p role="alert" className="error-message">{error}</p>}{uncertain && <p role="status">The result is unconfirmed. Retry the same action to recover its saved outcome.</p>}
    {stale && <div><p>Your reason is retained. The quote changed; review its current state before starting another action.</p><p>Submitted revision: <code>{quote.revisionId}</code></p>{latest && <p>Current state: {latest.state}. Current rating revision: <code>{latest.context?.revisionId ?? 'No current rating'}</code></p>}<button className="button" disabled={busy} onClick={() => { void quoteFetch<UnderwritingAssessment>(`/api/v1/quotes/${quote.id}/underwriting`).then(result => setLatest(result.data), () => setError('Current state could not be loaded. Your reason is retained.')); }}>Read current state</button></div>}
    <div className="quote-row-actions"><button className="button" disabled={busy || uncertain} onClick={close}>Cancel</button><button data-retry-action className="button button-primary" disabled={busy || stale || !uncertain && (!reason.trim() || action === 'underwriting/refresh' && (!selection || !confirmed))} onClick={() => void submit()}>{busy ? 'Confirming…' : uncertain ? 'Retry same action' : actionLabels[action]}</button></div>
  </dialog>;
}
