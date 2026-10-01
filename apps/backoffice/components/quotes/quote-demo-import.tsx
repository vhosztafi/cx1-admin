'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { csrfToken, type Actor } from '../../lib/auth';
import type { Client } from '../../lib/clients';
import { canCaptureQuotes, createQuoteCommand, quoteFetch, sendQuoteCommand, type QuoteProduct, type QuoteProposal } from '../../lib/quotes';

const FRONTEND_ORIGIN = 'https://cx1-dev.gyongyos.co.uk';
const CLIENT_ID = '4d5690a9-3120-4ef2-8798-787eae64cdae';
const RELATIONSHIP_ID = '40196fa6-7a0e-4143-b4e3-dd758542739c';
const PRODUCT_CODE = 'motor-trade-road-risks';

type Handoff = { type: 'cx1:quote-handoff'; version: 1; key: string; proposal: QuoteProposal; snapshot: string };

function isHandoff(value: unknown): value is Handoff {
  if (!value || typeof value !== 'object') return false;
  const data = value as Record<string, unknown>;
  if (data.type !== 'cx1:quote-handoff' || data.version !== 1 || typeof data.key !== 'string' ||
      !/^[0-9a-f-]{36}$/i.test(data.key) || typeof data.snapshot !== 'string' ||
      new TextEncoder().encode(data.snapshot).length > 1024 * 1024 ||
      !data.proposal || typeof data.proposal !== 'object' || Array.isArray(data.proposal)) return false;
  const proposal = data.proposal as Record<string, unknown>;
  return proposal.schemaVersion === '1.0' && proposal.productCode === PRODUCT_CODE &&
    JSON.stringify(proposal).length < 1024 * 1024;
}

export function QuoteDemoImport() {
  const [status, setStatus] = useState('Checking your back-office session…');
  const [error, setError] = useState('');
  const [savedUrl, setSavedUrl] = useState('');
  const [createdId, setCreatedId] = useState('');
  const [hasHandoff, setHasHandoff] = useState(false);
  const [working, setWorking] = useState(false);
  const actor = useRef<Actor | null>(null);
  const handoff = useRef<Handoff | null>(null);
  const created = useRef<{ id: string; etag: string } | null>(null);
  const busy = useRef(false);
  const completed = useRef(false);

  const save = useCallback(async (data: Handoff) => {
    if (busy.current || completed.current) return;
    busy.current = true; setWorking(true); setError('');
    try {
      if (!actor.current) throw new Error('Sign in with an internal quote role first.');
      const account = await quoteFetch<Actor>('/api/v1/account');
      if (account.data.id !== actor.current.id || account.data.scope !== 'internal' || !canCaptureQuotes(account.data.roles))
        throw new Error('Your current account cannot create this quote.');
      const csrf = await csrfToken();
      if (!created.current) {
        const [client, available] = await Promise.all([
          quoteFetch<Client>(`/api/v1/clients/${CLIENT_ID}`),
          quoteFetch<{ items: QuoteProduct[] }>(`/api/v1/quote-products?relationshipId=${RELATIONSHIP_ID}`),
        ]);
        const product = available.data.items.find(item => item.productCode === PRODUCT_CODE && item.captureEligible);
        if (!product) throw new Error('The review relationship has no available Motor Trade Road Risks product.');
        const proposal: QuoteProposal = { ...data.proposal, insured: {
          ...(data.proposal.insured ?? {}), legalName: client.data.legalName, entityType: client.data.entityType,
        } };
        const command = createQuoteCommand(RELATIONSHIP_ID, product.productVersionId, proposal, data.key);
        created.current = await sendQuoteCommand(command, csrf);
        setCreatedId(created.current.id);
        setStatus('Quote draft saved. Attaching the full funnel answers…');
      }
      const saved = created.current;
      const file = new File([data.snapshot], 'cx1-funnel-answers.txt', { type: 'text/plain' });
      await sendQuoteCommand({ method: 'POST', url: `/api/v1/quotes/${saved.id}/evidence-files`, body: '',
        key: `${data.key}:answers`, etag: saved.etag, expectedId: saved.id, upload: file }, csrf);
      const url = `${window.location.origin}/quotes/${saved.id}`;
      completed.current = true;
      setSavedUrl(url); setStatus('The draft and completed funnel answers are saved.');
      window.opener?.postMessage({ type: 'cx1:quote-created', url }, FRONTEND_ORIGIN);
    } catch (failure) {
      const message = failure instanceof Error ? failure.message : 'The demo quote could not be saved.';
      setError(message);
      setStatus(created.current ? 'The draft was created, but the answers still need attaching.' : 'The quote was not confirmed.');
      window.opener?.postMessage({ type: 'cx1:quote-error' }, FRONTEND_ORIGIN);
    } finally { busy.current = false; setWorking(false); }
  }, []);

  useEffect(() => {
    let closed = false;
    let readyTimer: number | undefined;
    const receive = (event: MessageEvent) => {
      if (event.origin !== FRONTEND_ORIGIN || event.source !== window.opener || !isHandoff(event.data)) return;
      if (handoff.current && handoff.current.key !== event.data.key) return;
      handoff.current = event.data;
      setHasHandoff(true);
      void save(event.data);
    };
    window.addEventListener('message', receive);
    void quoteFetch<Actor>('/api/v1/account').then(result => {
      if (closed) return;
      if (result.data.scope !== 'internal' || !canCaptureQuotes(result.data.roles)) {
        setStatus('Your account needs an internal quote role.'); return;
      }
      actor.current = result.data;
      setStatus('Waiting for the completed funnel answers…');
      window.opener?.postMessage({ type: 'cx1:quote-ready' }, FRONTEND_ORIGIN);
      readyTimer = window.setInterval(() => {
        if (!handoff.current) window.opener?.postMessage({ type: 'cx1:quote-ready' }, FRONTEND_ORIGIN);
      }, 1000);
    }, failure => {
      if (closed) return;
      if (failure && typeof failure === 'object' && 'status' in failure && failure.status === 401)
        window.location.replace('/login?returnTo=%2Fquote-import');
      else setError('The back-office session could not be checked. Reload this window to retry.');
    });
    return () => { closed = true; if (readyTimer) window.clearInterval(readyTimer); window.removeEventListener('message', receive); };
  }, [save]);

  return <div aria-live="polite"><p>{status}</p>{error && <p className="error-message">{error}</p>}
    {createdId && !savedUrl && <p><a href={`/quotes/${createdId}`} target="_blank" rel="noopener noreferrer">Open the draft already created</a></p>}
    {hasHandoff && error && <button type="button" className="button button-primary" disabled={working} onClick={() => { if (handoff.current) void save(handoff.current); }}>Retry same save</button>}
    {savedUrl && <p><a className="button button-primary" href={savedUrl}>Open saved quote</a></p>}
  </div>;
}
