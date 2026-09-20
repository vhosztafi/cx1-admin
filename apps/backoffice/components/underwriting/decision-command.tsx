'use client';
import { useEffect, useRef, useState } from 'react';
import { csrfToken, type Actor } from '../../lib/auth';
import { quoteFetch, QuoteError, uncertainQuoteFailure, staleQuoteFailure, type PendingQuoteCommand } from '../../lib/quotes';
import { sendUnderwritingCommand, type UnderwritingAssessment } from '../../lib/underwriting-api';
import { sendCapacityRecovery } from '../../lib/capacity';
import { sendPolicyIssue, type PolicyIssueExpectation } from '../../lib/policies-api';

export type DecisionRequest = { command: PendingQuoteCommand; label: string; description: string; kind?: 'issue'; issueExpectation?: PolicyIssueExpectation; jobId?: string; jobKind?: 'capacity-escalation' | 'quote-delivery' };
export function DecisionCommand({ request, actorId, close, completed }: { request: DecisionRequest; actorId: string; close: () => void; completed: (policyId?: string) => void }) {
  const dialog = useRef<HTMLDialogElement>(null), guard = useRef({ busy: false, uncertain: false }), closeRef = useRef(close);
  const [busy, setBusy] = useState(false), [uncertain, setUncertain] = useState(false), [stale, setStale] = useState(false), [error, setError] = useState('');
  const [latest, setLatest] = useState<UnderwritingAssessment>();
  useEffect(() => { closeRef.current = close; }, [close]);
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null, node = dialog.current;
    node?.showModal(); node?.querySelector<HTMLButtonElement>('button')?.focus();
    const blocked = () => guard.current.busy || guard.current.uncertain;
    const warn = () => setError('Retry the same action to confirm the saved result before leaving.');
    const cancel = (event: Event) => { event.preventDefault(); if (blocked()) warn(); else closeRef.current(); };
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
  useEffect(() => { if (uncertain) dialog.current?.querySelector<HTMLButtonElement>('[data-retry-action]')?.focus(); }, [uncertain]);
  async function submit() {
    if (guard.current.busy || stale) return;
    const recovering = guard.current.uncertain; let attempted = false;
    guard.current.busy = true; setBusy(true); setError('');
    try {
      const [csrf, account] = await Promise.all([csrfToken(), quoteFetch<Actor>('/api/v1/account')]);
      if (account.data.id !== actorId) throw new QuoteError(403);
      attempted = true;
      let policyId: string | undefined;
      if (request.kind === 'issue') policyId = (await sendPolicyIssue(request.command, csrf, request.issueExpectation)).policyId;
      else if (request.jobId) await sendCapacityRecovery(request.command, request.jobId, csrf, request.jobKind); else await sendUnderwritingCommand(request.command, csrf);
      guard.current = { busy: false, uncertain: false }; completed(policyId);
    } catch (failure) {
      const retain = recovering || attempted && uncertainQuoteFailure(failure);
      guard.current.uncertain = retain; setUncertain(retain); if (!retain) setStale(staleQuoteFailure(failure));
      setError(failure instanceof Error ? failure.message : 'The result could not be confirmed.');
    } finally { guard.current.busy = false; setBusy(false); }
  }
  return <dialog ref={dialog} className="agency-dialog agency-terms-dialog" aria-labelledby="decision-command-title">
    <h2 id="decision-command-title">{request.label}</h2><p>{request.description}</p><p>This records an audited action against the reviewed quote and child versions.</p>
    {request.command.upload && <p>{request.command.upload.name} · {request.command.upload.size.toLocaleString('en-GB')} bytes</p>}
    {error && <p role="alert" className="error-message">{error}</p>}{uncertain && <p role="status">The result is unconfirmed. Retry the same action to recover its saved outcome.</p>}
    {stale && <><p>Your form is retained. The quote changed; read its current state before closing this confirmation and reviewing your inputs.</p>
      <button className="button" onClick={() => void quoteFetch<UnderwritingAssessment>(`/api/v1/quotes/${request.command.expectedId}/underwriting`).then(x => setLatest(x.data), () => setError('Current state could not be loaded.'))}>Read current state</button>
      {latest && <p>Current state: {latest.state}. Current revision: <code>{latest.context?.revisionId ?? 'No active cycle'}</code></p>}</>}
    <div className="quote-row-actions"><button className="button" disabled={busy || uncertain} onClick={close}>Back to form</button><button data-retry-action className="button button-primary" disabled={busy || stale} onClick={() => void submit()}>{busy ? 'Confirming…' : uncertain ? 'Retry same action' : 'Confirm action'}</button></div>
  </dialog>;
}
