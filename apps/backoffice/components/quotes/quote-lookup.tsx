'use client';
import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { quoteFetch, type PendingQuoteCommand, type QuoteObject, type QuoteView } from '../../lib/quotes';

type Target = { kind: 'address' | 'vehicle' | 'licence'; scope: 'insured' | 'driver' | 'vehicle' | 'premises'; riskItemId?: string };
type Lookup = Target & { id: string; revisionId: string; inputFingerprint: string; scenario: string; state: string; attempts: number; workState: string;
  createdAt: string; completedAt: string | null; source: string | null; selectedRevisionId: string | null; candidates: { id: string; label: string; patch: QuoteObject }[] };
type Context = { saved: QuoteView; etag: string; disabled: boolean; dirty: boolean; items: Lookup[]; error: string; refresh: () => void; run: (command: PendingQuoteCommand, kind: 'lookup-request' | 'lookup-select') => void };
const LookupContext = createContext<Context | null>(null);
export function QuoteLookupProvider({ children, refreshToken, ...props }: Omit<Context, 'items' | 'error' | 'refresh'> & { children: ReactNode; refreshToken: number }) {
  const [items, setItems] = useState<Lookup[]>([]); const [error, setError] = useState(''); const [reload, setReload] = useState(0);
  useEffect(() => {
    let active = true; let timer: ReturnType<typeof setTimeout> | undefined; const abort = new AbortController();
    async function load() {
      try {
        const result = await quoteFetch<{ items: Lookup[] }>(`/api/v1/quotes/${props.saved.id}/lookups`, { signal: abort.signal });
        if (!active) return;
        setItems(result.data.items); setError('');
        if (result.data.items.some(item => item.state === 'pending')) timer = setTimeout(() => void load(), 2000);
      } catch { if (active) setError('Lookup history could not be loaded. Refresh to check the saved outcome.'); }
    }
    void load(); return () => { active = false; abort.abort(); if (timer) clearTimeout(timer); };
  }, [props.saved.id, props.saved.revisionId, refreshToken, reload]);
  return <LookupContext.Provider value={{ ...props, items, error, refresh: () => setReload(value => value + 1) }}>{children}</LookupContext.Provider>;
}

const scenarios = [['success', 'One match'], ['multiple', 'Multiple matches'], ['no-match', 'No match'], ['reject', 'Rejected'], ['fail-once', 'Retry once'], ['timeout-after-success', 'Timeout after completion']] as const;
export function QuoteLookupControl({ label, ...target }: Target & { label: string }) {
  const context = useContext(LookupContext); const [scenario, setScenario] = useState('success'); const [reason, setReason] = useState('');
  const [historyId, setHistoryId] = useState('');
  if (!context) return null;
  const { saved, etag, dirty, disabled, run } = context;
  const history = context.items.filter(item => item.kind === target.kind && item.scope === target.scope && (item.riskItemId ?? '').toLowerCase() === (target.riskItemId ?? '').toLowerCase());
  const lookup = history.find(item => item.id === historyId) ?? history[0];
  const current = lookup?.revisionId === saved.revisionId && !lookup.selectedRevisionId;
  const unavailable = disabled || dirty;
  const submit = (selection?: { candidateId: string } | { manualReason: string }) => {
    if (unavailable || (selection && !current)) return;
    run(Object.freeze({ method: 'POST', url: `/api/v1/quotes/${saved.id}/${selection ? 'lookup-selections' : 'lookups'}`,
      body: JSON.stringify(selection ? { lookupId: lookup!.id, revisionId: saved.revisionId, inputFingerprint: lookup!.inputFingerprint, ...selection }
        : { revisionId: saved.revisionId, ...target, scenario }), key: crypto.randomUUID(), etag,
      ...(selection ? { expectedId: saved.id } : {}) }), selection ? 'lookup-select' : 'lookup-request');
  };
  return <fieldset className="quote-reference-fields"><legend>{label} lookup</legend>
    <p className="client-help">Fictional demo results. Lookup uses the saved {target.kind === 'address' ? 'postcode' : target.kind === 'vehicle' ? 'registration' : 'licence number'}. {target.kind === 'licence' ? 'This does not verify entitlement or replace licence evidence.' : 'Your declared details change only when you select a result.'}</p>
    {dirty && <p>Save your changes before requesting or selecting a lookup.</p>}
    <div className="quote-form-grid"><label>Demo outcome<select aria-label={`${label} · Demo outcome`} value={scenario} disabled={disabled} onChange={event => setScenario(event.target.value)}>{scenarios.map(([value, text]) => <option key={value} value={value}>{text}</option>)}</select></label></div>
    <div className="quote-row-actions"><button className="button" type="button" disabled={unavailable} onClick={() => submit()}>Look up {label.toLowerCase()}</button><button className="button" type="button" disabled={disabled} onClick={context.refresh}>Refresh {label.toLowerCase()} lookup</button></div>
    {context.error && <p role="alert">{context.error}</p>}
    {history.length > 1 && <div className="quote-form-grid"><label>Previous requests<select aria-label={`${label} · Previous requests`} value={lookup?.id ?? ''} onChange={event => setHistoryId(event.target.value)}>{history.map(item => <option key={item.id} value={item.id}>{new Date(item.createdAt).toLocaleString('en-GB')} · {item.scenario} · {item.state}</option>)}</select></label></div>}
    {lookup && <div aria-label={`${label} lookup outcome`}>
      <p role="status">{lookup.state === 'pending' ? `Lookup queued or retrying · ${lookup.attempts} attempts. No result confirmed.` : lookup.state === 'no-match' ? 'No matching demo result.' : lookup.state === 'rejected' ? 'The demo lookup was rejected.' : lookup.state === 'failed' ? 'Lookup failed. You can record a manual decision.' : `${lookup.candidates.length} demo ${lookup.candidates.length === 1 ? 'match' : 'matches'} returned.`}</p>
      {lookup.completedAt && <p className="client-help">Completed {new Date(lookup.completedAt).toLocaleString('en-GB')} · {lookup.source ?? 'Demo lookup'}</p>}
      {!current && <p>{lookup.selectedRevisionId ? 'This decision has been recorded in quote history.' : 'The saved inputs have changed. Request a lookup for the current revision.'}</p>}
      {lookup.candidates.map((candidate, index) => <div className="quote-driver-card" key={candidate.id}><p>{candidate.label}</p><p className="client-help">{Object.values(candidate.patch).flatMap(value => typeof value === 'object' && !Array.isArray(value) ? Object.values(value) : [value]).filter(value => typeof value === 'string').join(', ')}</p><button className="button" type="button" disabled={unavailable || !current} onClick={() => submit({ candidateId: candidate.id })}>Use {label.toLowerCase()} match {index + 1}</button></div>)}
      <label className="quote-form-label">Manual decision reason<textarea aria-label={`${label} · Manual decision reason`} maxLength={1000} value={reason} disabled={disabled || !current} onChange={event => setReason(event.target.value)} /></label>
      <button className="button" type="button" disabled={unavailable || !current || !reason.trim()} onClick={() => submit({ manualReason: reason.trim() })}>Record manual {label.toLowerCase()} decision</button>
    </div>}
  </fieldset>;
}
