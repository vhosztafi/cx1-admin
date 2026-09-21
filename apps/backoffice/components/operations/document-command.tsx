'use client';
import { useEffect, useId, useRef, useState } from 'react';
import { csrfToken, type Actor } from '../../lib/auth';
import { staleQuoteFailure, uncertainQuoteFailure } from '../../lib/quotes';
import { documentFetch, DocumentError, type DocumentVersion } from '../../lib/documents-api';

export type DocumentConfirmation = { execute: (csrf: string) => Promise<DocumentVersion>; label: string; description: string };
export function DocumentCommand({ request, actorId, close, saved }: { request: DocumentConfirmation; actorId: string; close: () => void; saved: (id?: string) => void }) {
  const titleId = useId(), dialog = useRef<HTMLDialogElement>(null);
  const original = useRef({ request, actorId }), callbacks = useRef({ close, saved }), guard = useRef({ busy: false, uncertain: false });
  const [busy, setBusy] = useState(false), [uncertain, setUncertain] = useState(false), [stale, setStale] = useState(false), [error, setError] = useState('');
  useEffect(() => { callbacks.current = { close, saved }; }, [close, saved]);
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null, node = dialog.current;
    node?.showModal(); node?.querySelector<HTMLButtonElement>('button')?.focus();
    const blocked = () => guard.current.busy || guard.current.uncertain;
    const warn = () => setError('Retry the same action to confirm the saved result before leaving.');
    const cancel = (event: Event) => { event.preventDefault(); if (blocked()) warn(); else callbacks.current.close(); };
    const keyboard = (event: KeyboardEvent) => {
      if (event.key !== 'Tab' || !node) return;
      const items = Array.from(node.querySelectorAll<HTMLElement>('button, a[href], input, textarea, select')).filter(x => !x.matches(':disabled') && x.getClientRects().length);
      const first = items[0], last = items.at(-1); if (!first || !last) { event.preventDefault(); return; }
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
    return () => { node?.removeEventListener('cancel', cancel); node?.removeEventListener('keydown', keyboard); window.removeEventListener('beforeunload', unload); document.removeEventListener('click', click, true); navigation?.removeEventListener('navigate', navigate); window.removeEventListener('popstate', pop, true); previous?.focus(); };
  }, []);
  useEffect(() => { if (uncertain) dialog.current?.querySelector<HTMLButtonElement>('[data-retry-document]')?.focus(); }, [uncertain]);
  async function submit() {
    if (guard.current.busy || stale) return;
    const recovering = guard.current.uncertain; let attempted = false;
    guard.current.busy = true; setBusy(true); setError('');
    try {
      const [csrf, account] = await Promise.all([csrfToken(), documentFetch<Actor>('/api/v1/account')]);
      if (account.id !== original.current.actorId) throw new DocumentError(403);
      attempted = true;
      const receipt = await original.current.request.execute(csrf);
      guard.current = { busy: false, uncertain: false }; callbacks.current.saved(receipt.id);
    } catch (failure) {
      const retain = recovering || attempted && uncertainQuoteFailure(failure);
      guard.current.uncertain = retain; setUncertain(retain); if (!retain) setStale(staleQuoteFailure(failure));
      setError(failure instanceof Error ? failure.message : 'The saved document result could not be confirmed.');
    } finally { guard.current.busy = false; setBusy(false); }
  }
  return <dialog ref={dialog} className="agency-dialog agency-terms-dialog" aria-labelledby={titleId}>
    <h2 id={titleId}>{request.label}</h2><p>{request.description}</p>
    {error && <p role="alert" className="error-message">{error}</p>}
    {uncertain && <p role="status">The result is unconfirmed. Retry the same action to recover its saved outcome.</p>}
    {stale && <p>Your form and selection are retained. Return to the form, read the saved version and review your changes before trying again.</p>}
    <div className="quote-row-actions"><button className="button" disabled={busy || uncertain} onClick={() => callbacks.current.close()}>Back to form</button>
      <button data-retry-document className="button button-primary" disabled={busy || stale} onClick={() => void submit()}>{busy ? 'Confirming…' : uncertain ? 'Retry same action' : request.label}</button></div>
  </dialog>;
}
