'use client';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useEffect, useRef, useState } from 'react';
import { csrfToken, type Actor } from '../../lib/auth';
import { amountInput, changeField, fieldValue, percentageInput } from '../../lib/quote-form';
import { QuoteError, quoteFetch, saveQuoteCommand, sendQuoteCommand, staleQuoteFailure, uncertainQuoteFailure, validQuoteEtag, type PendingQuoteCommand, type QuoteValue, type QuoteView } from '../../lib/quotes';
import { readinessTarget, type ReadinessTarget } from '../../lib/quote-readiness';
import { termFeedback } from '../../lib/quote-term';
import { QuoteDrivers } from './quote-drivers';
import { QuoteTermFields } from './quote-term-fields';
import { QuoteSourceBusiness } from './quote-source-business';
import { QuoteBusinessAnswers } from './quote-business-answers';
import { QuoteActivities } from './quote-activities';
import { QuoteProposerReferences } from './quote-proposer-references';
import type { QuoteFormCatalogue } from '../../lib/quote-catalogue';
import { Panel } from '../primitives';
import { LoadFeedback, useQuoteResource } from './shared';

export function QuoteWizard({ actorId, quoteId, catalogue }: { actorId: string; quoteId: string; catalogue: QuoteFormCatalogue }) {
  const record = useQuoteResource<QuoteView>(`/api/v1/quotes/${quoteId}`);
  if (!record.data || !validQuoteEtag(record.etag)) return <Panel title="Edit quote"><LoadFeedback error={record.error ?? (record.data ? 'The saved version could not be confirmed.' : undefined)} retry={record.refresh} /></Panel>;
  return <Editor actorId={actorId} initial={record.data} initialEtag={record.etag} catalogue={catalogue} />;
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
  { path: 'insured.title', label: 'Proposer title' }, { path: 'insured.declaredCompanyType', label: 'Company category' }, { path: 'insured.responses', label: 'Quotation and marketing answers' }, { path: 'insured.entityType', label: 'Legal entity' }, { path: 'insured.proposerNames', label: 'Full proposer names' },
  { path: 'risk.drivers', label: 'Drivers and history' }, { path: 'risk.responses', label: 'Driver basis and restrictions' },
  { path: 'risk.business.responses', label: 'Business answers' }, { path: 'risk.business.activities', label: 'Motor Trade occupations' }, { path: 'risk.business.description', label: 'Business description' }, { path: 'risk.business.startedOn', label: 'Business start date' },
  { path: 'risk.business.turnover', label: 'Annual turnover' }, { path: 'risk.business.wageRoll', label: 'Annual wage roll' },
  ...['kind', 'localStartDate', 'localStartTime', 'utcOffsetMinutes', 'localEndDate', 'localEndTime', 'endUtcOffsetMinutes'].map(name => ({ path: `termIntent.${name}`, label: `Requested term: ${name}` })),
  ...splitFields.map(([name, label]) => ({ path: `risk.business.declaredActivitySplit.${name}`, label: `${label} (basis points)` }))];
const display = (value: QuoteValue | undefined, sourceLabels: Record<string, string> = {}): string => value === undefined ? 'Not recorded' : Array.isArray(value) ? value.map(item => display(item, sourceLabels)).join('; ') : typeof value === 'object' ? typeof value.id === 'string' && ('code' in value || 'turnoverBasisPoints' in value) ? `${display(value.code, sourceLabels)} · ${value.turnoverBasisPoints === undefined ? 'Share not recorded' : `${Number(value.turnoverBasisPoints) / 100}%`}` : typeof value.label === 'string' ? value.label : Array.isArray(value.answers) ? value.answers.map(answer => { const item = answer as Record<string, QuoteValue>; const labels: Record<string, string> = { 'MTS-01-Q01': 'Quotation data consent', 'MTS-01-Q02': 'Marketing consent', 'MTS-01-Q03': 'Contact methods', 'MTS-03-Q04': 'Trade association membership', 'MTS-03-Q05': 'Trade association name', 'MTS-03-Q06': 'VAT registered', 'MTS-03-Q07': 'VAT number', 'MTS-03-Q08': 'Vehicles handled per year', 'MTS-03-Q09': 'MIPD vehicle limit' }; return `${labels[String(item.questionId)] ?? sourceLabels[String(item.questionId)] ?? 'Other saved answer'}: ${display(item.value, sourceLabels)}`; }).join('; ') : Object.entries(value).filter(([key]) => key !== 'id').map(([key, item]) => `${key.replace(/([A-Z])/g, ' $1')}: ${display(item, sourceLabels)}`).join('; ') : String(value);

function Editor({ actorId, initial, initialEtag, catalogue }: { actorId: string; initial: QuoteView; initialEtag: string; catalogue: QuoteFormCatalogue }) {
  const router = useRouter();
  const [saved, setSaved] = useState(initial); const [etag, setEtag] = useState(initialEtag); const [proposal, setProposal] = useState(initial.proposal);
  const [stage, setStage] = useState(1); const [busy, setBusy] = useState(false); const [uncertain, setUncertain] = useState(false);
  const [error, setError] = useState(''); const [status, setStatus] = useState(''); const [invalid, setInvalid] = useState<Record<string, string>>({});
  const [conflict, setConflict] = useState(false); const [comparison, setComparison] = useState<{ data: QuoteView; etag: string }>();
  const [formRevision, setFormRevision] = useState(0);
  const [focusTarget, setFocusTarget] = useState<ReadinessTarget>();
  useEffect(() => {
    if (!focusTarget) return;
    const frame = requestAnimationFrame(() => {
    const controls = Array.from(document.querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement | HTMLButtonElement>('input,select,textarea,button'));
    const control = controls.find(item => (item.getAttribute('aria-label') ?? (item instanceof HTMLButtonElement ? item.textContent?.trim() : Array.from(item.labels?.[0]?.childNodes ?? []).filter(node => node.nodeType === Node.TEXT_NODE).map(node => node.textContent).join('').trim())) === focusTarget.label);
    if (control && !control.disabled) { let ancestor = control.parentElement; while (ancestor) { if (ancestor instanceof HTMLDetailsElement) ancestor.open = true; ancestor = ancestor.parentElement; } control.focus(); control.scrollIntoView({ block: 'center', behavior: 'instant' }); }
    else setError('This field is not currently available. Review the section and its controlling answers.');
    });
    return () => cancelAnimationFrame(frame);
  }, [focusTarget]);
  const [buffers, setBuffers] = useState<Record<string, string>>({});
  const command = useRef<PendingQuoteCommand | null>(null); const action = useRef<'stay' | 'continue' | 'exit'>('stay');
  const navigationState = useRef({ busy: false, uncertain: false, dirty: false });
  const term = termFeedback(proposal.termIntent); const inputErrors = Object.keys(invalid).length + term.errors.length;
  const dirty = JSON.stringify(proposal) !== JSON.stringify(saved.proposal) || inputErrors > 0;
  useEffect(() => { navigationState.current.dirty = dirty; }, [dirty]);
  const frozen = busy || uncertain || conflict || !saved.capabilities.canSave;
  const stages = saved.productCode === 'motor-trade-combined'
    ? ['Agency & product', 'Proposer', 'Trade activities', 'Premises', 'Drivers', 'Claims & convictions', 'Vehicles & trade plates', 'Cover & excess', 'Declarations & review']
    : ['Agency & product', 'Proposer', 'Trade activities', 'Drivers', 'Claims & convictions', 'Vehicles & trade plates', 'Previous insurance & NCD', 'Cover & excess', 'Declarations & review'];

  const driverStage = saved.productCode === 'motor-trade-combined' ? 4 : 3;
  const historyStage = driverStage + 1;
  const editableStages = [0, 1, 2, driverStage, historyStage];

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
    if (navigationState.current.busy || conflict || !saved.capabilities.canSave || inputErrors) return;
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
      else if (action.current === 'continue') setStage(value => editableStages.find(next => next > value) ?? value);
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
          {stage === 0 ? <><p>{saved.clientName} · {saved.agencyName}</p><p>{saved.productCode === 'motor-trade-road-risks' ? 'Motor Trade Road Risks' : 'Motor Trade Combined'}</p><p className="client-help">The saved relationship and product cannot be changed.</p><QuoteTermFields intent={proposal.termIntent} change={change} /></> : stage === 1 ? <>
            <div className="quote-form-grid"><label>Legal entity<select aria-label="Legal entity" value={String(fieldValue(proposal, 'insured.entityType') ?? '')} onChange={event => change('insured.entityType', event.target.value || undefined)}><option value="">Not answered</option><option value="sole-trader">Sole trader</option><option value="partnership">Partnership</option><option value="limited-company">Limited company</option><option value="llp">Limited liability partnership</option></select></label>
              {identityFields.map(([path, label, max]) => <label key={path}>{label}<input maxLength={max} value={String(fieldValue(proposal, path) ?? '')} onChange={event => change(path, event.target.value || undefined)} /></label>)}
            </div><h3>Full proposer names</h3><p className="client-help">Record complete names in order. Separate first name and surname fields above remain independent.</p>
            <div className="quote-form-grid">{[0, 1, 2].map(index => { const names = (fieldValue(proposal, 'insured.proposerNames') ?? []) as string[]; return <label key={index}>Proposer {index + 1} full name<input maxLength={200} value={names[index] ?? ''} onChange={event => { const next = Array.from({ length: 3 }, (_, position) => names[position] ?? ''); next[index] = event.target.value; while (next.length && next.at(-1) === '') next.pop(); change('insured.proposerNames', next); validity('insured.proposerNames', next.some(name => !name) ? 'Remove empty proposer slots or enter each full name.' : undefined); }} /></label>; })}</div>
            <button className="button" type="button" onClick={() => { change('insured.proposerNames', ((fieldValue(proposal, 'insured.proposerNames') ?? []) as string[]).filter(Boolean)); validity('insured.proposerNames'); }}>Remove empty proposer slots</button>
            <label className="quote-form-label">Business description<textarea aria-label="Business description" maxLength={4000} value={String(fieldValue(proposal, 'risk.business.description') ?? '')} onChange={event => change('risk.business.description', event.target.value || undefined)} /></label>
<div className="quote-form-grid"><label>Business start date<input type="date" value={String(fieldValue(proposal, 'risk.business.startedOn') ?? '')} onChange={event => change('risk.business.startedOn', event.target.value || undefined)} /></label></div>
            <QuoteSourceBusiness proposal={proposal} versions={saved.captureVersions} catalogue={catalogue} stage={1} replace={next => { navigationState.current.dirty = true; setProposal(next); setStatus(''); }} />
            <QuoteProposerReferences proposal={proposal} versions={saved.captureVersions} catalogue={catalogue} change={change} replace={next => { navigationState.current.dirty = true; setProposal(next); setStatus(''); }} />
          </> : stage === 2 ? <><div className="quote-form-grid">
              {(['turnover', 'wageRoll'] as const).map(name => <DecimalField key={name} label={name === 'turnover' ? 'Annual turnover (GBP)' : 'Annual wage roll (GBP)'} path={`risk.business.${name}`} initial={fieldValue(proposal, `risk.business.${name}`)} kind="amount" buffers={buffers} setBuffer={(path, value) => setBuffers(current => ({ ...current, [path]: value }))} change={change} validity={validity} />)}</div>
            <QuoteBusinessAnswers proposal={proposal} versions={saved.captureVersions} catalogue={catalogue} buffers={buffers} setBuffer={(path, value) => setBuffers(current => ({ ...current, [path]: value }))} validity={validity} replace={next => { navigationState.current.dirty = true; setProposal(next); setStatus(''); }} />
            <h3>Activity split</h3><p className="client-help">Enter percentages adding to 100%. Occupation selections below are a separate declaration.</p><div className="quote-form-grid">{splitFields.map(([name, label]) => <DecimalField key={name} label={`${label} (%)`} path={`risk.business.declaredActivitySplit.${name}`} initial={fieldValue(proposal, `risk.business.declaredActivitySplit.${name}`)} kind="percentage" buffers={buffers} setBuffer={(path, value) => setBuffers(current => ({ ...current, [path]: value }))} change={change} validity={validity} />)}</div>
            <p role="status">Total activity split: {splitFields.reduce((sum, [name]) => sum + Number(fieldValue(proposal, `risk.business.declaredActivitySplit.${name}`) ?? 0), 0) / 100}%</p>
            <QuoteSourceBusiness proposal={proposal} versions={saved.captureVersions} catalogue={catalogue} stage={2} replace={next => { navigationState.current.dirty = true; setProposal(next); setStatus(''); }} />
            <QuoteActivities proposal={proposal} versions={saved.captureVersions} catalogue={catalogue} buffers={buffers} setBuffer={(path, value) => setBuffers(current => { const next = { ...current }; if (value === undefined) delete next[path]; else next[path] = value; return next; })} validity={validity} replace={next => { navigationState.current.dirty = true; setProposal(next); setStatus(''); }} />
          </> : <QuoteDrivers proposal={proposal} versions={saved.captureVersions} catalogue={catalogue} history={stage === historyStage} buffers={buffers} setBuffer={(key, value) => setBuffers(current => { const next = { ...current }; if (value === undefined) delete next[key]; else next[key] = value; return next; })} validity={validity} replace={next => { navigationState.current.dirty = true; setProposal(next); setStatus(''); }} />}
        </fieldset>
      </Panel>
      <Panel title="Saved draft readiness"><div className="quote-rail-body">
        <p>Checks apply to saved revision {saved.revisionNumber}. {dirty ? 'Save your changes to refresh this guidance.' : 'Review the fields below; incomplete drafts can still be saved.'}</p>
        <p className="client-help">Later sections and complete quote assessment remain unavailable. This list does not confirm readiness to rate or issue.</p>
        <ul className="quote-readiness-list">{saved.readiness.issues.map((issue, index) => {
          const target = readinessTarget(issue, saved.proposal, catalogue.businessQuestions, catalogue.driverFields);
          return target ? <li key={`${issue.code}-${issue.path}-${index}`}><button type="button" className="button" disabled={dirty || frozen} onClick={() => { setStage(target.stage); setFocusTarget({ ...target }); }}>Review {target.label}</button><span>{issue.message}</span></li> : null;
        })}</ul>
        <p className="client-help">{saved.readiness.issues.filter(issue => !readinessTarget(issue, saved.proposal, catalogue.businessQuestions, catalogue.driverFields)).length} other checks concern later sections or quote-level requirements.</p>
      </div></Panel>
      {conflict && <Panel title="Saved version changed"><div className="quote-rail-body"><p>Your draft is retained. Load the saved revision to compare before deciding to replace your edits.</p><button className="button" disabled={busy} onClick={() => void compare()}>Load saved comparison</button>
        {comparison && <><p>Current saved revision: {comparison.data.revisionNumber}. Other sections also remain as recorded in that revision.</p><div className="table-scroll" role="region" aria-label="Quote changes" tabIndex={0}><table><thead><tr><th>Field</th><th>Your draft</th><th>Saved revision</th></tr></thead><tbody>{comparisonFields.filter(field => JSON.stringify(fieldValue(proposal, field.path)) !== JSON.stringify(fieldValue(comparison.data.proposal, field.path))).map(field => <tr key={field.path}><th>{field.label}</th><td>{display(fieldValue(proposal, field.path), Object.fromEntries([...catalogue.businessQuestions, ...(catalogue.driverFields ?? [])].map(question => [question.id, question.label])))}</td><td>{display(fieldValue(comparison.data.proposal, field.path), Object.fromEntries([...catalogue.businessQuestions, ...(catalogue.driverFields ?? [])].map(question => [question.id, question.label])))}</td></tr>)}</tbody></table></div><button className="button" disabled={busy || uncertain} onClick={replaceWithSaved}>Discard my edits and load saved revision</button></>}
      </div></Panel>}
    </div><aside className="quote-create-rail"><Panel title="Quote progress"><div className="quote-rail-body"><nav className="agency-steps" aria-label="Quote stages">{stages.map((name, index) => <button className="button" key={name} type="button" disabled={!editableStages.includes(index) || busy || uncertain} aria-current={index === stage ? 'step' : undefined} onClick={() => setStage(index)}><span>{index + 1}</span>{name}{!editableStages.includes(index) ? ' · unavailable' : ''}</button>)}</nav>
      <p className="client-help">This capture form is being completed. Missing sections remain incomplete; the quote cannot progress to rating or issue.</p>
      {error && <p className="error-message" role="alert">{error}</p>}{term.errors.map(message => <p key={message} role="alert">{message}</p>)}{Object.entries(invalid).map(([path, message]) => <p key={path} role="alert">{message}</p>)}
      {status && <p role="status">{status}</p>}{uncertain && <p className="quote-pending" role="status">Save result unconfirmed. Your draft is locked until the original save is confirmed.</p>}
      {!saved.capabilities.canSave && <p role="alert">This quote is currently closed to editing.</p>}
      <div className="quote-save-actions"><button className="button button-primary" disabled={busy || conflict || !saved.capabilities.canSave || inputErrors > 0} onClick={() => void save('stay')}>{busy ? 'Saving…' : uncertain ? 'Retry same save' : 'Save draft'}</button>
        <button className="button" disabled={frozen || stage >= historyStage || inputErrors > 0} onClick={() => void save('continue')}>Save and continue</button>
        <button className="button" disabled={frozen || inputErrors > 0} onClick={() => void save('exit')}>Save and exit</button></div>
    </div></Panel></aside></div></>;
}

function DecimalField({ label, path, initial, kind, buffers, setBuffer, change, validity }: { label: string; path: string; initial: QuoteValue | undefined; kind: 'amount' | 'percentage'; buffers: Record<string, string>; setBuffer: (path: string, value: string) => void; change: (path: string, value: QuoteValue | undefined) => void; validity: (path: string, error?: string) => void }) {
  const text = buffers[path] ?? (initial === undefined ? '' : kind === 'percentage' ? String(Number(initial) / 100) : String(initial));
  const error = (kind === 'amount' ? amountInput(text) : percentageInput(text)).error;
  return <label>{label}<input aria-label={label} inputMode="decimal" value={text} aria-invalid={Boolean(error)} aria-describedby={error ? `${path}-error` : undefined} onChange={event => { const value = event.target.value; setBuffer(path, value); const parsed = kind === 'amount' ? amountInput(value) : percentageInput(value); validity(path, parsed.error); if (!parsed.error) change(path, parsed.value); }} />{error && <small id={`${path}-error`}>{error}</small>}</label>;
}


