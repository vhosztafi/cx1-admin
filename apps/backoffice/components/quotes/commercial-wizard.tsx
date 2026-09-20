'use client';
import Link from 'next/link';
import {useRouter} from 'next/navigation';
import {useEffect, useRef, useState} from 'react';
import {csrfToken, type Actor} from '../../lib/auth';
import {changeCommercialField, commercialInputStage, commercialRows, commercialStages, commercialVersionsMatch, type CommercialCollection, type CommercialCatalogue, type CommercialQuoteView, type CommercialProposal} from '../../lib/commercial-capture';
import {QuoteError, quoteFetch, saveQuoteCommand, sendQuoteCommand, staleQuoteFailure, uncertainQuoteFailure, validQuoteEtag, type PendingQuoteCommand, type QuoteIssue, type QuoteValue} from '../../lib/quotes';
import {termFeedback} from '../../lib/quote-term';
import {Panel} from '../primitives';
import {CommercialBusinessStage} from './commercial-business-stage';
import {CommercialLossStage} from './commercial-loss-stage';
import {CommercialLocationStage} from './commercial-location-stage';
import {CommercialCoverStage} from './commercial-cover-stage';
import {CommercialLiabilityStage} from './commercial-liability-stage';
import {CommercialQuestionFields} from './commercial-question-fields';
import type {CommercialFormProps} from './commercial-question-fields';
import {QuoteTermFields} from './quote-term-fields';
import {QuoteProposalDetails} from './quote-history';

export function CommercialWizard({actorId, initial, initialEtag, catalogue}: {actorId: string; initial: CommercialQuoteView; initialEtag: string; catalogue: CommercialCatalogue}) {
  const router = useRouter(); const [saved, setSaved] = useState(initial); const [proposal, setProposal] = useState(initial.proposal); const [etag, setEtag] = useState(initialEtag);
  const [stage, setStage] = useState(1); const [buffers, setBuffers] = useState<Record<string, string>>({}); const [invalid, setInvalid] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false); const [uncertain, setUncertain] = useState(false); const [conflict, setConflict] = useState(false); const [accessLost, setAccessLost] = useState(false);
  const [error, setError] = useState(''); const [status, setStatus] = useState('');
  const [comparison, setComparison] = useState<{data: CommercialQuoteView; etag: string}>();
  const [reviewTarget, setReviewTarget] = useState<{id: string}>();
  useEffect(() => {
    if (!reviewTarget) return;
    const target = document.getElementById(reviewTarget.id);
    target?.focus(); target?.scrollIntoView({block: 'center', behavior: 'smooth'});
  }, [reviewTarget, stage]);
  const command = useRef<PendingQuoteCommand | null>(null); const action = useRef<'stay' | 'continue' | 'exit'>('stay');
  const guard = useRef({busy: false, uncertain: false, dirty: false});
  const term = termFeedback(proposal.termIntent); const inputErrors = Object.keys(invalid).length + term.errors.length;
  const dirty = JSON.stringify(proposal) !== JSON.stringify(saved.proposal) || inputErrors > 0;
  useEffect(() => {guard.current.dirty = dirty;}, [dirty]);
  useEffect(() => {
    const allow = () => {
      if (guard.current.busy || guard.current.uncertain) {setError('Confirm the pending save with Retry same save before leaving.'); return false;}
      return !guard.current.dirty || window.confirm('Discard your unsaved quote changes and leave?');
    };
    const unload = (event: BeforeUnloadEvent) => {if (guard.current.busy || guard.current.uncertain || guard.current.dirty) {event.preventDefault(); event.returnValue = '';}};
    let approvedUrl: string | undefined;
    const click = (event: MouseEvent) => {
      const link = (event.target as Element).closest?.('a[href]:not([href^="#"]), .account-dropdown button'); if (!link) return;
      if (!allow()) {event.preventDefault(); event.stopPropagation();} else approvedUrl = (link as HTMLAnchorElement).href;
    };
    const navigation = (window as Window & {navigation?: EventTarget}).navigation;
    const navigate = (event: Event) => {
      if ((event as Event & {hashChange?: boolean}).hashChange) return;
      const destination = (event as Event & {destination?: {url: string}}).destination?.url;
      if (approvedUrl && destination === approvedUrl) {approvedUrl = undefined; return;}
      if (event.cancelable && !allow()) event.preventDefault();
    };
    const retainedUrl = location.href; const retainedState: unknown = history.state;
    const pop = (event: PopStateEvent) => {if (!navigation && !allow()) {event.stopImmediatePropagation(); history.pushState(retainedState, '', retainedUrl);}};
    window.addEventListener('beforeunload', unload); window.addEventListener('popstate', pop, true); document.addEventListener('click', click, true); navigation?.addEventListener('navigate', navigate);
    return () => {window.removeEventListener('beforeunload', unload); window.removeEventListener('popstate', pop, true); document.removeEventListener('click', click, true); navigation?.removeEventListener('navigate', navigate);};
  }, []);
  function replace(next: CommercialProposal) {
    const collections: CommercialCollection[] = ['activities', 'locations', 'wages', 'losses', 'dependencies'];
    const removed = collections.flatMap(kind => commercialRows(proposal, kind).filter(row => !commercialRows(next, kind).some(value => value.id.toLowerCase() === row.id.toLowerCase())).map(row => row.id.toLowerCase()));
    if (removed.length) {
      const retain = (values: Record<string, string>) => Object.fromEntries(Object.entries(values).filter(([key]) => !removed.some(id => key.toLowerCase().split(':').includes(id))));
      setBuffers(retain); setInvalid(retain);
    }
    guard.current.dirty = true; setProposal(next); setStatus('');
  }
  function change(path: string, value: QuoteValue | undefined) {replace(changeCommercialField(proposal, path, value));}
  function accept(latest: {data: CommercialQuoteView; etag: string}) {
    setSaved(latest.data); setProposal(latest.data.proposal); setEtag(latest.etag); setBuffers({}); setInvalid({});
    setConflict(false); setComparison(undefined); setAccessLost(false); guard.current.dirty = false;
  }
  async function readCurrent() {
    const account = await quoteFetch<Actor>('/api/v1/account');
    if (account.data.id !== actorId) throw new QuoteError(403);
    const latest = await quoteFetch<CommercialQuoteView>(`/api/v1/quotes/${saved.id}`);
    if (!validQuoteEtag(latest.etag) || latest.data.productCode !== 'commercial-combined' || !commercialVersionsMatch(latest.data, catalogue)) throw new Error('The current commercial quote version could not be confirmed.');
    return {data: latest.data, etag: latest.etag};
  }
  async function compare() {
    if (guard.current.busy) return; guard.current.busy = true; setBusy(true);
    try {setComparison(await readCurrent()); setError('');}
    catch (failure) {setError(failure instanceof Error ? failure.message : 'Unable to load the saved revision.'); if (failure instanceof QuoteError && [401, 403, 404].includes(failure.status)) setAccessLost(true);}
    finally {guard.current.busy = false; setBusy(false);}
  }
  async function save(next: 'stay' | 'continue' | 'exit') {
    if (guard.current.busy || conflict || !saved.capabilities.canSave || inputErrors > 0) return;
    const recovering = guard.current.uncertain;
    if (!recovering) {command.current = saveQuoteCommand(saved.id, etag, proposal, 'Commercial Combined capture'); action.current = next;}
    if (!command.current) return;
    guard.current.busy = true; setBusy(true); setError(''); let attempted = false; let acknowledged = false;
    try {
      const csrf = await csrfToken(); const account = await quoteFetch<Actor>('/api/v1/account');
      if (account.data.id !== actorId) throw new QuoteError(403);
      attempted = true; await sendQuoteCommand(command.current, csrf); acknowledged = true;
      const latest = await readCurrent(); accept(latest); command.current = null; guard.current.uncertain = false; setUncertain(false);
      setStatus(`Draft saved. Revision ${latest.data.revisionNumber}.`); guard.current.busy = false;
      if (action.current === 'exit') router.push(`/quotes/${saved.id}`); else if (action.current === 'continue') setStage(value => Math.min(value + 1, 9));
    } catch (failure) {
      const pending = command.current !== null && (recovering || acknowledged || attempted && uncertainQuoteFailure(failure));
      guard.current.uncertain = pending; setUncertain(pending);
      if (!pending) {command.current = null; if (staleQuoteFailure(failure)) setConflict(true);}
      if (failure instanceof QuoteError && [401, 403, 404].includes(failure.status)) setAccessLost(true);
      setError(failure instanceof Error ? failure.message : 'The save could not be confirmed.');
    } finally {guard.current.busy = false; setBusy(false);}
  }
  const form: CommercialFormProps = {proposal, catalogue, replace, buffers,
    setBuffer: (key, value) => {guard.current.dirty = true; setBuffers(x => ({...x, [key]: value}));},
    validity: (key, message) => setInvalid(x => {const next = {...x}; if (message) next[key] = message; else delete next[key]; return next;})};
  const matching = commercialVersionsMatch(saved, catalogue); const frozen = busy || uncertain || conflict || !saved.capabilities.canSave || !matching;
  const labels = Object.fromEntries(catalogue.questions.map(x => [x.id, x.label]));
  const stageFor = (issue: QuoteIssue) => issue.questionId ? (catalogue.questions.find(x => x.id === issue.questionId)?.stage ?? 10) - 1 :
    issue.path.startsWith('/termIntent') ? 0 : issue.path.startsWith('/risk/losses') ? 2 : issue.path.startsWith('/risk/businessInterruption') ? 6 : issue.path.startsWith('/insured') || issue.path.startsWith('/risk/business') ? 1 : issue.path.startsWith('/risk/locations') ? 3 : issue.path.startsWith('/risk/wages') || issue.path.startsWith('/risk/liability') ? 7 : 9;
  function review(issue: QuoteIssue) {
    setStage(stageFor(issue));
    setReviewTarget({id: issue.questionId ? `cc-${issue.questionId}:proposal` : `cc-${issue.path.replace(/^\//, '').replaceAll('/', '.')}`});
  }
  if (accessLost) return <Panel title="Quote access changed"><p role="alert">{error || 'Current access must be confirmed before this quote can be shown.'}</p><p>Your unsaved work remains in this window.</p><button className="button" disabled={busy} onClick={() => {
    guard.current.busy = true; setBusy(true); void readCurrent().then(value => {if (!value.data.capabilities.canSave) throw new Error('Editing remains unavailable.'); setAccessLost(false); setConflict(!uncertain); setComparison(value);}).catch(failure => setError(failure instanceof Error ? failure.message : 'Access could not be confirmed.')).finally(() => {guard.current.busy = false; setBusy(false);});
  }}>Check current access</button></Panel>;
  return <><div className="page-heading"><div><h1>Edit {saved.reference}</h1><p>Commercial Combined · Saved revision {saved.revisionNumber} · {dirty ? 'Unsaved changes' : 'No unsaved changes'}</p></div><Link className="button" href={`/quotes/${saved.id}`}>View saved quote</Link></div>
    <div className="agency-layout"><div><Panel title={commercialStages[stage]} note="Incomplete answers can be saved. Saving does not request a rating.">
      <fieldset disabled={frozen} className="quote-selection"><legend className="sr-only">{commercialStages[stage]} details</legend>
        {stage === 0 ? <><p>{saved.clientName} · {saved.agencyName}</p><p>Commercial Combined</p><QuoteTermFields intent={proposal.termIntent} change={change} /></> : stage === 1 ? <CommercialBusinessStage form={form} /> : stage === 2 ? <CommercialLossStage form={form} /> : stage >= 3 && stage <= 5 ? <CommercialLocationStage form={form} stage={stage+1}/> : stage === 6 ? <CommercialCoverStage form={form} stage={7}/> : stage === 7 ? <CommercialLiabilityStage form={form}/> : stage === 8 ? <><p>Record the actual health and safety position. Unknown answers remain unanswered; adverse answers require details.</p><CommercialQuestionFields form={form} questions={catalogue.questions.filter(q=>q.stage===9)}/></> : <CommercialCoverStage form={form} stage={10}/>}
      </fieldset></Panel>
      <Panel title="Saved readiness" note={dirty ? 'This assessment describes the saved revision. Save your changes to refresh it.' : 'Capture readiness is separate from underwriting approval.'}><div className="quote-rail-body">
        <ul className="quote-readiness-list">{saved.readiness.issues.map((issue, index) => <li key={`${issue.code}:${index}`}><span>{issue.message}</span><button type="button" className="button" disabled={frozen} onClick={() => review(issue)}>Review {commercialStages[stageFor(issue)]}</button></li>)}</ul>
        {saved.matchReviewId && <Link href={`/matches/${saved.matchReviewId}`}>Open account matching review</Link>}
      </div></Panel>
      {conflict && <Panel title="Saved version changed"><div className="quote-rail-body"><p>This draft changed after you opened it. Your edits are retained. Compare the saved version before discarding them.</p><button className="button" disabled={busy} onClick={() => void compare()}>Load saved comparison</button>
        {comparison && <><h3>Your retained draft</h3><QuoteProposalDetails proposal={proposal} value={proposal} questionLabels={labels} /><h3>Saved revision {comparison.data.revisionNumber}</h3><QuoteProposalDetails proposal={comparison.data.proposal} value={comparison.data.proposal} questionLabels={labels} /><button className="button" disabled={busy || uncertain} onClick={() => {accept(comparison); setError(''); setStatus('Loaded the saved revision.');}}>Discard my edits and load saved revision</button></>}
      </div></Panel>}
    </div><aside className="quote-create-rail"><Panel title="Quote progress"><div className="quote-rail-body"><nav className="agency-steps" aria-label="Quote stages">{commercialStages.map((name, index) => <button key={name} type="button" className="button" aria-current={stage === index ? 'step' : undefined} disabled={busy || uncertain} onClick={() => setStage(index)}><span>{index + 1}</span>{name}</button>)}</nav>
      {!matching && <p role="alert">The catalogue matching this saved quote is unavailable. Existing answers are retained.</p>}
      {error && <p role="alert" className="error-message">{error}</p>}{Object.entries(invalid).map(([key, value]) => <p role="alert" key={key}><button className="button" type="button" onClick={() => {setStage(commercialInputStage(proposal, catalogue, key)); setReviewTarget({id: `cc-${key}`});}}>{value}</button></p>)}{term.errors.map(value => <p role="alert" key={value}>{value}</p>)}
      {status && <p role="status">{status}</p>}{uncertain && <p role="status">The result of this save is not yet confirmed. Retry the same request.</p>}
      <div className="quote-save-actions"><button className="button button-primary" type="button" disabled={busy || conflict || !matching || !saved.capabilities.canSave || inputErrors > 0} onClick={() => void save('stay')}>{busy ? 'Saving…' : uncertain ? 'Retry same save' : 'Save quote draft'}</button>
        <button className="button" type="button" disabled={frozen || inputErrors > 0 || stage >= 9} onClick={() => void save('continue')}>Save and continue</button><button className="button" type="button" disabled={frozen || inputErrors > 0} onClick={() => void save('exit')}>Save and exit</button></div>
    </div></Panel></aside></div></>;
}
