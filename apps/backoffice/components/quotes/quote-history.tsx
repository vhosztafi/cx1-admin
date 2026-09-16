'use client';
import { createContext, useContext, useState } from 'react';
import type { Page } from '../../lib/clients';
import type { QuoteProposal, QuoteView } from '../../lib/quotes';
import { Panel } from '../primitives';
import { LoadFeedback, Paging, useQuoteResource } from './shared';

type Revision = { id: string; number: number; savedAt: string; savedByLabel: string; reason?: string; proposal: QuoteProposal; proposalHash: string };
type Side = { path: string; json: string };
type Change = { kind: 'added' | 'removed' | 'changed' | 'reordered'; path: string; before?: Side; after?: Side };
type Comparison = { changes: Change[]; totalChanges: number; nextCursor?: string };
const QuestionLabels = createContext<Record<string, string>>({});
const label = (path: string) => path.split('/').filter(Boolean).filter(value => value !== 'risk').map(value => /^\d+$/.test(value) ? String(Number(value) + 1) :
  value.replaceAll('~1', '/').replaceAll('~0', '~').replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, first => first.toUpperCase())).join(' · ') || 'Proposal';

export function QuoteHistory({ quote, selected, select, questionLabels }: { quote: QuoteView; selected: string; select: (id: string) => void; questionLabels: Record<string, string> }) {
  const [pages, setPages] = useState(['']); const [left, setLeft] = useState(''); const [right, setRight] = useState(quote.revisionId);
  const cursor = pages.at(-1)!;
  const history = useQuoteResource<Page<Revision>>(`/api/v1/quotes/${quote.id}/revisions?pageSize=10${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
  return <QuestionLabels.Provider value={questionLabels}><Panel title="Revision history" note="Saved snapshots remain unchanged; select a revision before cloning">
    {!history.data ? <LoadFeedback error={history.error} retry={() => { setPages(['']); history.refresh(); }} /> : <>
      <div className="table-scroll" role="region" aria-label="Saved quote revisions" tabIndex={0}><table><thead><tr><th>Revision</th><th>Saved by</th><th>Saved at</th><th>Reason</th><th>Actions</th></tr></thead>
        <tbody>{history.data.items.map(item => <tr key={item.id}><th>{item.number}{item.id === quote.revisionId ? ' · current' : ''}{item.id === selected ? ' · selected' : ''}</th>
          <td>{item.savedByLabel}</td><td>{new Date(item.savedAt).toLocaleString('en-GB')}</td><td>{item.reason ?? 'Initial capture or draft save'}</td>
          <td><div className="quote-row-actions"><button type="button" className="button" aria-pressed={selected === item.id} onClick={() => select(item.id)}>Select revision {item.number}</button>
            <button type="button" className="button" aria-pressed={left === item.id} onClick={() => setLeft(item.id)}>Compare from revision {item.number}</button>
            <button type="button" className="button" aria-pressed={right === item.id} onClick={() => setRight(item.id)}>Compare to revision {item.number}</button></div></td></tr>)}</tbody></table></div>
      <Paging total={history.data.totalCount} previous={pages.length > 1 ? () => setPages(value => value.slice(0, -1)) : undefined}
        next={history.data.nextCursor ? () => setPages(value => [...value, history.data!.nextCursor!]) : undefined} />
      <Snapshot key={selected} quoteId={quote.id} revisionId={selected} />
      {left && right && <Compare key={`${left}:${right}`} quoteId={quote.id} left={left} right={right} />}
    </>}
  </Panel></QuestionLabels.Provider>;
}

function Snapshot({ quoteId, revisionId }: { quoteId: string; revisionId: string }) {
  const revision = useQuoteResource<Revision>(`/api/v1/quotes/${quoteId}/revisions/${revisionId}`);
  return <details className="quote-rail-body"><summary>Selected revision details</summary>{!revision.data ? <LoadFeedback error={revision.error} retry={revision.refresh} /> :
    <><p>Revision {revision.data.number} · {revision.data.savedByLabel} · {new Date(revision.data.savedAt).toLocaleString('en-GB')}</p><Value value={revision.data.proposal} /></>}</details>;
}
function Compare({ quoteId, left, right }: { quoteId: string; left: string; right: string }) {
  const [pages, setPages] = useState(['']); const cursor = pages.at(-1)!;
  const result = useQuoteResource<Comparison>(`/api/v1/quotes/${quoteId}/compare?leftRevisionId=${left}&rightRevisionId=${right}&pageSize=25${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
  return <section className="quote-rail-body" aria-label="Revision comparison"><h2>Revision comparison</h2>
    {!result.data ? <LoadFeedback error={result.error} retry={result.refresh} /> : <><p>{result.data.totalChanges} {result.data.totalChanges === 1 ? 'change' : 'changes'}. Reordered entries are matched to the same saved risk.</p>
      {result.data.changes.map((item, index) => <details className="quote-driver-card" key={`${item.path}:${index}`} open><summary>{label(item.path)} · {item.kind}</summary>
        <div className="quote-form-grid"><div><h3>Before</h3>{item.before ? <Value value={JSON.parse(item.before.json)} /> : <p>Not recorded</p>}</div>
          <div><h3>After</h3>{item.after ? <Value value={JSON.parse(item.after.json)} /> : <p>Not recorded</p>}</div></div></details>)}
      <Paging total={result.data.totalChanges} previous={pages.length > 1 ? () => setPages(value => value.slice(0, -1)) : undefined}
        next={result.data.nextCursor ? () => setPages(value => [...value, result.data!.nextCursor!]) : undefined} /></>}
  </section>;
}
function Value({ value }: { value: unknown }) {
  const labels = useContext(QuestionLabels);
  if (value === null) return <span>Explicitly empty</span>;
  if (typeof value === 'boolean') return <span>{value ? 'Yes' : 'No'}</span>;
  if (Array.isArray(value)) return <ol>{value.map((item, index) => <li key={index}><Value value={item} /></li>)}</ol>;
  if (typeof value === 'object' && 'questionId' in value && typeof value.questionId === 'string') {
    const answer = value as Record<string, unknown>;
    return <div><strong>{labels[value.questionId] ?? 'Recorded declaration'}</strong><Value value={answer.value ?? Object.fromEntries(Object.entries(answer).filter(([key]) => !['questionId', 'kind'].includes(key)))} /></div>;
  }
  if (typeof value === 'object' && 'label' in value && typeof value.label === 'string') return <span>{value.label}</span>;
  if (typeof value === 'object') return <dl className="quote-saved-details">{Object.entries(value).filter(([key]) => !['id', 'schemaVersion', 'questionSetVersion', 'referenceVersion'].includes(key)).map(([key, item]) => <div key={key}><dt>{label(key)}</dt><dd><Value value={item} /></dd></div>)}</dl>;
  return <span style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{String(value)}</span>;
}
