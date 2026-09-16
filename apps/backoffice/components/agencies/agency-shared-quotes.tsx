'use client';
import { useState } from 'react';
import { DataTable, EmptyState, Panel, Status } from '../primitives';
import { clientDate, type Page } from '../../lib/clients';
import { LoadFeedback, Paging, useAgencyResource } from './shared';

type SharedQuote = { id: string; reference: string; clientName: string; productCode: string; state: string; updatedAt: string; startDate?: string };
export function AgencySharedQuotes({ agencyId }: { agencyId: string }) {
  const [input, setInput] = useState(''); const [search, setSearch] = useState(''); const [cursors, setCursors] = useState<string[]>([]);
  const resource = useAgencyResource<Page<SharedQuote>>(`/api/v1/agencies/${agencyId}/sharing/quotes?pageSize=10${search ? `&q=${encodeURIComponent(search)}` : ''}${cursors.at(-1) ? `&cursor=${encodeURIComponent(cursors.at(-1)!)}` : ''}`);
  const refresh = () => { setCursors([]); resource.refresh(); };
  return <Panel title="Their quotes" note="Shared summaries for current agency relationships">
    <form className="agency-sharing-search" onSubmit={event => { event.preventDefault(); setSearch(input.trim()); refresh(); }}><label htmlFor="shared-quote-search">Search shared quote summaries</label><div><input id="shared-quote-search" value={input} maxLength={200} placeholder="Quote reference or client name" onChange={event => setInput(event.target.value)} /><button className="button button-primary" type="submit">Search</button><button className="button" type="button" onClick={() => { setInput(''); setSearch(''); refresh(); }}>Clear search</button></div></form>
    {!resource.data ? <LoadFeedback error={resource.error} retry={refresh} /> : !resource.data.items.length ? <EmptyState title="No shared quotes match">Only saved quote summaries belonging to this agency are shown.</EmptyState> : <DataTable caption="Shared quote summaries" columns={['Quote','Client','Product','Status','Requested start','Updated']}>
      {resource.data.items.map(quote => <tr key={quote.id}><td className="mono">{quote.reference}</td><td>{quote.clientName}</td><td>{quote.productCode === 'motor-trade-combined' ? 'Motor Trade Combined' : 'Motor Trade Road Risks'}</td><td><Status tone={quote.state === 'draft' ? 'warning' : 'muted'}>{quote.state === 'draft' ? 'Draft' : 'Withdrawn'}</Status></td><td>{quote.startDate ? clientDate(quote.startDate) : 'Not recorded'}</td><td>{clientDate(quote.updatedAt, true)}</td></tr>)}
    </DataTable>}
    <Paging total={resource.data?.totalCount} previous={cursors.length ? () => setCursors(x => x.slice(0, -1)) : undefined} next={resource.data?.nextCursor ? () => setCursors(x => [...x, resource.data!.nextCursor!]) : undefined} />
    <p className="match-copy">Risk declarations, documents and internal matching evidence are not part of this shared summary.</p>
  </Panel>;
}
