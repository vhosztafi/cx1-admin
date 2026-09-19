'use client';
import { useEffect, useRef, useState } from 'react';
import { DataTable, Panel, Status } from '../primitives';
import { quoteFetch } from '../../lib/quotes';
import { formatGbp } from '../../lib/underwriting-api';
import { revisedTermPremium, servicingRatingDisplayState, type ServicingRatingCycle, type ServicingRatingHistory } from '../../lib/servicing-rating';
const date = (value: string) => new Date(value).toLocaleString('en-GB', { timeZone: 'Europe/London' });
const labels: Record<string, string> = { unrated: 'Not rated', 'rating-pending': 'Rating requested', rated: 'Rated', failed: 'Rating failed', expired: 'Rating expired', stale: 'Re-rate required', superseded: 'Historical rating' };
function RatingDetails({ cycle, kind, baseTermPremium }: { cycle: ServicingRatingCycle; kind: 'adjustment' | 'renewal'; baseTermPremium?: string }) {
  const result = cycle.result;
  const revised = result ? revisedTermPremium(baseTermPremium, result.premium) : null;
  return <><p>Requested {date(cycle.requestedAt)} · London. Attempt {cycle.attempts} of {cycle.attemptLimit}.</p>
    {cycle.nextAttemptAt && cycle.jobState === 'pending' ? <p>Next attempt {date(cycle.nextAttemptAt)} · London.</p> : null}
    {result?.detailsAvailable && result.outcome === 'rated' ? <>
      {kind === 'adjustment' && <dl className="underwriting-provenance"><div><dt>Issued term premium before adjustment</dt><dd>{baseTermPremium ? formatGbp(baseTermPremium) : 'Unavailable'}</dd></div><div><dt>Proposed revised term premium</dt><dd>{revised === null ? 'Unavailable' : formatGbp(revised)}</dd></div></dl>}
      <dl className="underwriting-provenance">{[['Base annual premium', result.baseAnnualPremium], [kind === 'renewal' ? 'Renewal term premium' : 'Premium movement', result.premium], ['Insurance premium tax', result.tax], [kind === 'renewal' ? 'One renewal fee' : 'One adjustment fee', result.fee], ['Broker commission', result.brokerCommission], ['Gross payable / credit', result.grossPayable], ['Net due / credit', result.netDue]].map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{formatGbp(value)}</dd></div>)}</dl>
      <p>Expires {date(result.expiresAt)} · London. These amounts do not record a payment or change issued cover.</p>
      <DataTable caption="Dated rating slices" columns={['Effective · London', 'Annual premium', 'Annual movement', 'Days / year', 'Premium', 'Tax', 'Commission']}>
        {result.slices.map(slice => <tr key={slice.effectiveAt}><th scope="row">{date(slice.effectiveAt)}</th><td>{formatGbp(slice.annualPremium)}</td><td>{formatGbp(slice.annualDelta)}</td><td>{slice.remainingDays} / {slice.annualDays}</td><td>{formatGbp(slice.premium)}</td><td>{formatGbp(slice.tax)}</td><td>{formatGbp(slice.brokerCommission)}</td></tr>)}
      </DataTable><p>{kind === 'renewal' ? 'The renewal fee applies once to the full new term. Annual movement is shown for comparison.' : 'The fee applies once to the adjustment, across all effective dates.'}</p>
    </> : result ? <p>{result.outcome === 'rejected' ? 'The rating request was rejected. Review the saved proposal before requesting another rating.' : 'Detailed results are unavailable. Re-rate before continuing.'}</p> : null}
    <details><summary>Rating provenance</summary><p>Rating cycle: {cycle.id}</p><p>Saved revision: {cycle.revisionId}</p><p>Issued base: {cycle.baseVersionId}</p><p>Rating rule: {cycle.ruleVersionId}</p><p>Agency terms: {cycle.agencyTermsVersionId}</p><p>Input hash: {cycle.inputHash}</p><p>Job: {cycle.workId} · {cycle.jobState}</p>{result ? <p>Result hash: {result.resultHash}</p> : null}</details>
  </>;
}
export function ServicingRating({ draftId, draftEtag, canRate, dirty, busy, rate, kind = 'adjustment', baseTermPremium }: { kind?: 'adjustment' | 'renewal'; baseTermPremium?: string; draftId: string; draftEtag: string; canRate: boolean; dirty: boolean; busy: boolean; rate: () => void }) {
  const [history, setHistory] = useState<ServicingRatingHistory | null>(null), [error, setError] = useState('');
  const [older, setOlder] = useState<ServicingRatingCycle[]>([]), [cursor, setCursor] = useState<string | null>(null), [loading, setLoading] = useState(false);
  const [now, setNow] = useState(0); const paging = useRef(false), generation = useRef(0);
  const url = `/api/v1/drafts/${draftId}/ratings?pageSize=10`;
  useEffect(() => {
    const controller = new AbortController(); let timer: ReturnType<typeof setTimeout>;
    generation.current++; let first = true;
    async function refresh() {
      try {
        const response = await quoteFetch<ServicingRatingHistory>(url, { signal: controller.signal });
        if (controller.signal.aborted) return;
        if (first) { setOlder([]); setCursor(null); first = false; }
        setHistory(response.data); setError(''); setNow(Date.now());
      } catch { if (!controller.signal.aborted) setError('Rating history could not be refreshed. Refresh before continuing.'); }
      if (!controller.signal.aborted) timer = setTimeout(() => void refresh(), 3000);
    }
    void refresh(); const clock = setInterval(() => setNow(Date.now()), 1000);
    return () => { controller.abort(); clearTimeout(timer); clearInterval(clock); };
  }, [url, draftEtag]);
  async function more() {
    const next = older.length ? cursor : history?.nextCursor;
    if (!next || paging.current) return; paging.current = true; setLoading(true); const currentGeneration = generation.current;
    try {
      const response = await quoteFetch<ServicingRatingHistory>(url + '&cursor=' + encodeURIComponent(next));
      if (currentGeneration !== generation.current) return;
      setOlder(previous => [...previous, ...response.data.items].filter((item, index, all) => all.findIndex(value => value.id === item.id) === index)); setCursor(response.data.nextCursor);
    } catch { if (currentGeneration === generation.current) setError('Older ratings could not be loaded. The draft may have changed; refresh its history.'); }
    finally { paging.current = false; setLoading(false); }
  }
  const state = servicingRatingDisplayState(history?.current ?? null, history?.draftEtag ?? '', draftEtag, now);
  const items = [...(history?.items ?? []), ...older].filter((item, index, all) => item.id !== history?.currentCycleId && all.findIndex(value => value.id === item.id) === index);
  return <Panel title={kind === 'renewal' ? 'Rate renewal' : 'Review & rate'} note={`Saved ${kind}`}><div className="quote-rail-body">
    <p role="status"><Status tone={state === 'rated' && !error ? 'success' : state === 'failed' ? 'error' : 'info'}>{error ? 'Rating status unconfirmed' : labels[state] ?? state}</Status></p>
    {error ? <p role="alert">{error}</p> : null}
    {dirty ? <p>Save your local changes before rating. The results below describe the saved proposal.</p> : null}
    <button type="button" className="button button-primary" disabled={!canRate || busy || !!error || history?.draftEtag !== draftEtag} onClick={rate}>{`${history?.current ? 'Re-rate' : 'Rate'} saved ${kind}`}</button>
    {!canRate && !dirty ? <p>{kind === 'renewal' ? 'Acquire an editing lease and save valid renewal preparation before rating.' : 'Acquire an editing lease and save a valid adjustment with at least one change before rating.'}</p> : null}
    {state === 'expired' ? <p>This rating has expired. Re-rate before continuing.</p> : null}
    {state === 'stale' ? <p>The saved draft or rating configuration has changed. Re-rate the current proposal.</p> : null}
    {state === 'failed' ? <p>The rating could not be completed. Review the proposal and re-rate, or ask an authorised operator to review its job.</p> : null}
    <p>Rating does not grant underwriting approval or acceptance.</p>
    {history?.current ? <RatingDetails cycle={history.current} kind={kind} baseTermPremium={baseTermPremium} /> : <p>No saved rating results.</p>}
    <h3>Rating history</h3>{items.length ? items.map(cycle => <details key={cycle.id}><summary>Rating {cycle.sequence} · {labels[cycle.state] ?? cycle.state} · {date(cycle.requestedAt)}</summary><RatingDetails cycle={cycle} kind={kind} baseTermPremium={baseTermPremium}/></details>) : <p>No earlier ratings.</p>}
    {(older.length ? cursor : history?.nextCursor) ? <button className="button" type="button" disabled={loading} onClick={() => void more()}>{loading ? 'Loading…' : 'Load earlier ratings'}</button> : null}
  </div></Panel>;
}
