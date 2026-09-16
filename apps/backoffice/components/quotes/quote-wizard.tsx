'use client';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useEffect, useRef, useState } from 'react';
import { csrfToken, type Actor } from '../../lib/auth';
import { amountInput, changeField, fieldValue, percentageInput } from '../../lib/quote-form';
import { QuoteError, quoteFetch, saveQuoteCommand, sendQuoteCommand, staleQuoteFailure, uncertainQuoteFailure, validQuoteEtag, type PendingQuoteCommand, type QuoteValue, type QuoteView } from '../../lib/quotes';
import { Panel } from '../primitives';
import { LoadFeedback, useQuoteResource } from './shared';

export function QuoteWizard({ actorId, quoteId }: { actorId: string; quoteId: string }) {
  const record = useQuoteResource<QuoteView>(`/api/v1/quotes/${quoteId}`);
  if (!record.data || !validQuoteEtag(record.etag)) return <Panel title="Edit quote"><LoadFeedback error={record.error ?? (record.data ? 'The saved version could not be confirmed.' : undefined)} retry={record.refresh} /></Panel>;
  return <Editor actorId={actorId} initial={record.data} initialEtag={record.etag} />;
}

const identityFields = [
  ['insured.firstName', 'First name', 100], ['insured.surname', 'Surname', 100],
  ['insured.legalName', 'Company or partnership name', 50], ['insured.tradingName', 'Trading name', 200], ['insured.companyNumber', 'Company number', 30],
  ['insured.contact.email', 'Email address', 254], ['insured.contact.telephone', 'Telephone', 11], ['insured.contact.mobile', 'Mobile', 11],
  ['insured.address.houseNumber', 'House number or name', 50], ['insured.address.street', 'Street', 50],
  ['insured.address.town', 'Town', 50], ['insured.address.city', 'City', 50], ['insured.address.county', 'County', 50], ['insured.address.postcode', 'Postcode', 10],
] as const;
const splitFields = [['sales', 'Vehicle sales'], ['servicing', 'Servicing'], ['mechanicalRepair', 'Mechanical repair'], ['breakdownRecovery', 'Breakdown and recovery'], ['bodyRepairs', 'Body repairs'], ['valeting', 'Valeting'], ['other', 'Other activities']] as const;
const comparisonFields = [...identityFields.map(([path, label]) => ({ path, label })),
  { path: 'insured.entityType', label: 'Legal entity' }, { path: 'insured.proposerNames', label: 'Full proposer names' },
  { path: 'risk.business.description', label: 'Business description' }, { path: 'risk.business.startedOn', label: 'Business start date' },
  { path: 'risk.business.turnover', label: 'Annual turnover' }, { path: 'risk.business.wageRoll', label: 'Annual wage roll' },
  ...splitFields.map(([name, label]) => ({ path: `risk.business.declaredActivitySplit.${name}`, label: `${label} (basis points)` }))];
const display = (value: QuoteValue | undefined): string => value === undefined ? 'Not recorded' : Array.isArray(value) ? value.map(display).join('; ') : typeof value === 'object' ? 'Structured saved value' : String(value);

function Editor({ actorId, initial, initialEtag }: { actorId: string; initial: QuoteView; initialEtag: string }) {
  const router = useRouter();
  const [saved, setSaved] = useState(initial); const [etag, setEtag] = useState(initialEtag); const [proposal, setProposal] = useState(initial.proposal);
  const [stage, setStage] = useState(1); const [busy, setBusy] = useState(false); const [uncertain, setUncertain] = useState(false);
  const [error, setError] = useState(''); const [status, setStatus] = useState(''); const [invalid, setInvalid] = useState<Record<string, string>>({});
  const [conflict, setConflict] = useState(false); const [comparison, setComparison] = useState<{ data: QuoteView; etag: string }>();
  const [formRevision, setFormRevision] = useState(0);
  const [buffers, setBuffers] = useState<Record<string, string>>({});
  const command = useRef<PendingQuoteCommand | null>(null); const action = useRef<'stay' | 'continue' | 'exit'>('stay');
  const navigationState = useRef({ busy: false, uncertain: false, dirty: false });
  const dirty = JSON.stringify(proposal) !== JSON.stringify(saved.proposal) || Object.keys(invalid).length > 0;
  useEffect(() => { navigationState.current.dirty = dirty; }, [dirty]);
  const frozen = busy || uncertain || conflict || !saved.capabilities.canSave;
  const stages = saved.productCode === 'motor-trade-combined'
    ? ['Agency & product', 'Proposer', 'Trade activities', 'Premises', 'Drivers', 'Claims & convictions', 'Vehicles & trade plates', 'Cover & excess', 'Declarations & review']
    : ['Agency & product', 'Proposer', 'Trade activities', 'Drivers', 'Claims & convictions', 'Vehicles & trade plates', 'Previous insurance & NCD', 'Cover & excess', 'Declarations & review'];

  useEffect(() => {
    const allow = () => {
      const state = navigationState.current;
      if (state.busy || state.uncertain) { setError('Confirm the pending save with Retry same save before leaving.'); return false; }
      if (state.dirty && !window.confirm('Discard your unsaved quote changes and leave?')) return false;
      return true;
    };
    const unload = (event: BeforeUnloadEvent) => { const state = navigationState.current; if (state.busy || state.uncertain || state.dirty) { event.preventDefault(); event.returnValue = ''; } };
    let approvedUrl: string | undefined;
    const click = (event: MouseEvent) => {
      if (!(event.target as Element).closest?.('a[href]:not([href^="#"]), .account-dropdown button')) return;
      if (!allow()) { event.preventDefault(); event.stopPropagation(); }
      else { approvedUrl = (event.target as Element).closest<HTMLAnchorElement>('a[href]')?.href; }
    };
    const navigation = (window as Window & { navigation?: EventTarget }).navigation;
    // Link clicks are checked in capture phase; programmatic/history navigation
    // is checked here. An allowed dirty link clears only the guard, not data.
    const navigate = (event: Event) => { if ((event as Event & { hashChange?: boolean }).hashChange) return; const destination = (event as Event & { destination?: { url: string } }).destination?.url; if (approvedUrl && destination === approvedUrl) { approvedUrl = undefined; return; } if (event.cancelable && !allow()) event.preventDefault(); };
    const retainedUrl = window.location.href; const retainedState: unknown = window.history.state;
    const pop = (event: PopStateEvent) => { if (!navigation && !allow()) { event.stopImmediatePropagation(); window.history.pushState(retainedState, '', retainedUrl); } };
    window.addEventListener('beforeunload', unload); window.addEventListener('popstate', pop, true); document.addEventListener('click', click, true); navigation?.addEventListener('navigate', navigate);
    return () => { window.removeEventListener('beforeunload', unload); window.removeEventListener('popstate', pop, true); document.removeEventListener('click', click, true); navigation?.removeEventListener('navigate', navigate); };
  }, []);

  function change(path: string, value: QuoteValue | undefined) { navigationState.current.dirty = true; setProposal(current => changeField(current, path, value)); setStatus(''); }
  function validity(path: string, message?: string) { setInvalid(current => { const next = { ...current }; if (message) next[path] = message; else delete next[path]; return next; }); }
  async function compare() {
    if (navigationState.current.busy) return;
    navigationState.current.busy = true; setBusy(true); setError('');
    try { const latest = await quoteFetch<QuoteView>(`/api/v1/quotes/${saved.id}`); if (!validQuoteEtag(latest.etag)) throw new Error('The saved version could not be confirmed.'); setComparison({ data: latest.data, etag: latest.etag }); }
    catch (failure) { setError(failure instanceof Error ? failure.message : 'Unable to load the saved quote.'); }
    finally { navigationState.current.busy = false; setBusy(false); }
  }
  function replaceWithSaved() {
    if (!comparison || navigationState.current.uncertain || navigationState.current.busy) return;
    setSaved(comparison.data); setProposal(comparison.data.proposal); setEtag(comparison.etag); setInvalid({}); setBuffers({}); setFormRevision(value => value + 1);
    setComparison(undefined); setConflict(false); setError(''); setStatus('Loaded the saved revision.'); navigationState.current.dirty = false;
  }
  async function save(nextAction: 'stay' | 'continue' | 'exit') {
    if (navigationState.current.busy || conflict || !saved.capabilities.canSave || Object.keys(invalid).length) return;
    const recovering = navigationState.current.uncertain;
    if (!recovering) { command.current = saveQuoteCommand(saved.id, etag, proposal); action.current = nextAction; }
    if (!command.current) return;
    navigationState.current.busy = true; setBusy(true); setError(''); setStatus(''); let attempted = false; let acknowledged = false;
    try {
      const csrf = await csrfToken(); const account = await quoteFetch<Actor>('/api/v1/account');
      if (account.data.id !== actorId) throw new QuoteError(403);
      attempted = true; const receipt = await sendQuoteCommand(command.current, csrf); acknowledged = true;
      const latest = await quoteFetch<QuoteView>(`/api/v1/quotes/${saved.id}`);
      if (!validQuoteEtag(latest.etag)) throw new Error('The saved version could not be confirmed.');
      navigationState.current.uncertain = false; setUncertain(false); command.current = null;
      if (latest.etag !== receipt.etag) { setComparison({ data: latest.data, etag: latest.etag }); setConflict(true); throw new QuoteError(412); }
      setSaved(latest.data); setProposal(latest.data.proposal); setEtag(latest.etag); navigationState.current.dirty = false;
      setStatus(`Draft saved · Revision ${latest.data.revisionNumber}`);
      navigationState.current.busy = false;
      if (action.current === 'exit') router.push(`/quotes/${saved.id}`);
      else if (action.current === 'continue') setStage(value => Math.min(value + 1, 2));
    } catch (failure) {
      const pending = command.current !== null && (recovering || acknowledged || (attempted && uncertainQuoteFailure(failure)));
      navigationState.current.uncertain = pending; setUncertain(pending);
      if (!pending && staleQuoteFailure(failure)) setConflict(true);
      if (!pending) command.current = null;
      setError(failure instanceof Error ? failure.message : 'The save could not be confirmed.');
    } finally { navigationState.current.busy = false; setBusy(false); }
  }

  return <><div className="page-heading"><div><h1>Edit {saved.reference}</h1><p>Saved revision {saved.revisionNumber} · {dirty ? 'Unsaved changes' : 'No unsaved changes'}</p></div><Link className="button" href={`/quotes/${saved.id}`}>View saved quote</Link></div>
    <div className="agency-layout"><div>
      <Panel title={stages[stage]} note="Incomplete answers can be saved. Saving does not confirm readiness.">
        <fieldset disabled={frozen} className="quote-selection" key={formRevision}><legend className="sr-only">{stages[stage]} details</legend>
          {stage === 0 ? <><p>{saved.clientName} · {saved.agencyName}</p><p>{saved.productCode === 'motor-trade-road-risks' ? 'Motor Trade Road Risks' : 'Motor Trade Combined'}</p><p className="client-help">The saved relationship and product cannot be changed. Policy term controls are not available yet.</p></> : stage === 1 ? <>
            <div className="quote-form-grid"><label>Legal entity<select value={String(fieldValue(proposal, 'insured.entityType') ?? '')} onChange={event => change('insured.entityType', event.target.value || undefined)}><option value="">Not answered</option><option value="sole-trader">Sole trader</option><option value="partnership">Partnership</option><option value="limited-company">Limited company</option><option value="llp">Limited liability partnership</option></select></label>
              {identityFields.map(([path, label, max]) => <label key={path}>{label}<input maxLength={max} value={String(fieldValue(proposal, path) ?? '')} onChange={event => change(path, event.target.value || undefined)} /></label>)}
            </div><h3>Full proposer names</h3><p className="client-help">Record complete names in order. Separate first name and surname fields above remain independent.</p>
            <div className="quote-form-grid">{[0, 1, 2].map(index => { const names = (fieldValue(proposal, 'insured.proposerNames') ?? []) as string[]; return <label key={index}>Proposer {index + 1} full name<input maxLength={200} value={names[index] ?? ''} onChange={event => { const next = Array.from({ length: 3 }, (_, position) => names[position] ?? ''); next[index] = event.target.value; while (next.length && next.at(-1) === '') next.pop(); change('insured.proposerNames', next); validity('insured.proposerNames', next.some(name => !name) ? 'Remove empty proposer slots or enter each full name.' : undefined); }} /></label>; })}</div>
            <button className="button" type="button" onClick={() => { change('insured.proposerNames', ((fieldValue(proposal, 'insured.proposerNames') ?? []) as string[]).filter(Boolean)); validity('insured.proposerNames'); }}>Remove empty proposer slots</button>
            <p className="client-help">Title, source company category and contact declarations will be added next. Existing saved answers are preserved.</p>
          </> : <><label className="quote-form-label">Business description<textarea aria-label="Business description" maxLength={4000} value={String(fieldValue(proposal, 'risk.business.description') ?? '')} onChange={event => change('risk.business.description', event.target.value || undefined)} /></label>
            <div className="quote-form-grid"><label>Business start date<input type="date" value={String(fieldValue(proposal, 'risk.business.startedOn') ?? '')} onChange={event => change('risk.business.startedOn', event.target.value || undefined)} /></label>
              {(['turnover', 'wageRoll'] as const).map(name => <DecimalField key={name} label={name === 'turnover' ? 'Annual turnover (GBP)' : 'Annual wage roll (GBP)'} path={`risk.business.${name}`} initial={fieldValue(proposal, `risk.business.${name}`)} kind="amount" buffers={buffers} setBuffer={(path, value) => setBuffers(current => ({ ...current, [path]: value }))} change={change} validity={validity} />)}</div>
            <h3>Activity split</h3><p className="client-help">Enter percentages adding to 100%. Occupation selections are a separate requirement and will be added next.</p><div className="quote-form-grid">{splitFields.map(([name, label]) => <DecimalField key={name} label={`${label} (%)`} path={`risk.business.declaredActivitySplit.${name}`} initial={fieldValue(proposal, `risk.business.declaredActivitySplit.${name}`)} kind="percentage" buffers={buffers} setBuffer={(path, value) => setBuffers(current => ({ ...current, [path]: value }))} change={change} validity={validity} />)}</div>
            <p role="status">Total activity split: {splitFields.reduce((sum, [name]) => sum + Number(fieldValue(proposal, `risk.business.declaredActivitySplit.${name}`) ?? 0), 0) / 100}%</p>
          </>}
        </fieldset>
      </Panel>
      {conflict && <Panel title="Saved version changed"><div className="quote-rail-body"><p>Your draft is retained. Load the saved revision to compare before deciding to replace your edits.</p><button className="button" disabled={busy} onClick={() => void compare()}>Load saved comparison</button>
        {comparison && <><p>Current saved revision: {comparison.data.revisionNumber}. Other sections also remain as recorded in that revision.</p><div className="table-scroll" role="region" aria-label="Quote changes" tabIndex={0}><table><thead><tr><th>Field</th><th>Your draft</th><th>Saved revision</th></tr></thead><tbody>{comparisonFields.filter(field => JSON.stringify(fieldValue(proposal, field.path)) !== JSON.stringify(fieldValue(comparison.data.proposal, field.path))).map(field => <tr key={field.path}><th>{field.label}</th><td>{display(fieldValue(proposal, field.path))}</td><td>{display(fieldValue(comparison.data.proposal, field.path))}</td></tr>)}</tbody></table></div><button className="button" disabled={busy || uncertain} onClick={replaceWithSaved}>Discard my edits and load saved revision</button></>}
      </div></Panel>}
    </div><aside className="quote-create-rail"><Panel title="Quote progress"><div className="quote-rail-body"><nav className="agency-steps" aria-label="Quote stages">{stages.map((name, index) => <button className="button" key={name} type="button" disabled={index > 2 || busy || uncertain} aria-current={index === stage ? 'step' : undefined} onClick={() => setStage(index)}><span>{index + 1}</span>{name}{index > 2 ? ' · unavailable' : ''}</button>)}</nav>
      <p className="client-help">This capture form is being completed. Missing sections remain incomplete; the quote cannot progress to rating or issue.</p>
      {error && <p className="error-message" role="alert">{error}</p>}{Object.entries(invalid).map(([path, message]) => <p key={path} role="alert">{message}</p>)}
      {status && <p role="status">{status}</p>}{uncertain && <p className="quote-pending" role="status">Save result unconfirmed. Your draft is locked until the original save is confirmed.</p>}
      {!saved.capabilities.canSave && <p role="alert">This quote is currently closed to editing.</p>}
      <div className="quote-save-actions"><button className="button button-primary" disabled={busy || conflict || !saved.capabilities.canSave || Object.keys(invalid).length > 0} onClick={() => void save('stay')}>{busy ? 'Saving…' : uncertain ? 'Retry same save' : 'Save draft'}</button>
        <button className="button" disabled={frozen || stage >= 2 || Object.keys(invalid).length > 0} onClick={() => void save('continue')}>Save and continue</button>
        <button className="button" disabled={frozen || Object.keys(invalid).length > 0} onClick={() => void save('exit')}>Save and exit</button></div>
    </div></Panel></aside></div></>;
}

function DecimalField({ label, path, initial, kind, buffers, setBuffer, change, validity }: { label: string; path: string; initial: QuoteValue | undefined; kind: 'amount' | 'percentage'; buffers: Record<string, string>; setBuffer: (path: string, value: string) => void; change: (path: string, value: QuoteValue | undefined) => void; validity: (path: string, error?: string) => void }) {
  const text = buffers[path] ?? (initial === undefined ? '' : kind === 'percentage' ? String(Number(initial) / 100) : String(initial));
  const error = (kind === 'amount' ? amountInput(text) : percentageInput(text)).error;
  return <label>{label}<input aria-label={label} inputMode="decimal" value={text} aria-invalid={Boolean(error)} aria-describedby={error ? `${path}-error` : undefined} onChange={event => { const value = event.target.value; setBuffer(path, value); const parsed = kind === 'amount' ? amountInput(value) : percentageInput(value); validity(path, parsed.error); if (!parsed.error) change(path, parsed.value); }} />{error && <small id={`${path}-error`}>{error}</small>}</label>;
}


