'use client';
import Link from 'next/link';
import { useState } from 'react';
import { DataTable, EmptyState, Panel, Status } from '../primitives';
import { clientDate, type Agency, type Page } from '../../lib/clients';
import { LoadFeedback, Paging, useQuoteResource } from './shared';

type QuoteRow = { id: string; reference: string; clientId: string; clientName: string; clientReference: string; agencyId: string; agencyName: string; productCode: string; state: string; revisionNumber: number; updatedAt: string; startDate: string | null };
const products: Record<string, string> = { 'motor-trade-road-risks': 'Motor Trade Road Risks', 'motor-trade-combined': 'Motor Trade Combined' };

export function QuoteList({ clientId, agencyId: initialAgency, embedded = false }: { clientId?: string; agencyId?: string; embedded?: boolean }) {
  const [search, setSearch] = useState(''); const [query, setQuery] = useState(''); const [product, setProduct] = useState('');
  const [agency, setAgency] = useState(initialAgency ?? ''); const [status, setStatus] = useState('');
  const [sort, setSort] = useState('reference'); const [direction, setDirection] = useState('asc'); const [cursors, setCursors] = useState<string[]>([]);
  const params = new URLSearchParams({ pageSize: '10', sort, direction });
  if (query) params.set('q', query); if (product) params.set('productCode', product); if (agency) params.set('agencyId', agency);
  if (status) params.set('status', status); if (clientId) params.set('clientId', clientId); if (cursors.at(-1)) params.set('cursor', cursors.at(-1)!);
  const resource = useQuoteResource<Page<QuoteRow>>(`/api/v1/quotes?${params}`);
  const refresh = () => { setCursors([]); resource.refresh(); };
  const newQuote = `/quotes/new${clientId ? `?clientId=${encodeURIComponent(clientId)}` : ''}`;
  return <>{!embedded && <div className="page-heading"><div><h1>Quotes</h1><p>Saved Motor Trade drafts and their retained history</p></div><Link className="button button-primary" href={newQuote}>New quote</Link></div>}
    <Panel title={embedded ? 'Client quotes' : 'Quotes'} note="Search current registrations, quote references, clients and agencies">
      {embedded && <div className="panel-footer"><Link className="button button-primary" href={newQuote}>New quote for this client</Link></div>}
      <form className="operations-toolbar quote-list-filters" onSubmit={event => { event.preventDefault(); setQuery(search.trim()); refresh(); }}>
        <label>Search quotes<input type="search" value={search} maxLength={200} placeholder="Reference, business or registration" onChange={event => setSearch(event.target.value)} /></label>
        <label>Product<select aria-label="Product" value={product} onChange={event => { setProduct(event.target.value); refresh(); }}><option value="">All Motor Trade products</option>{Object.entries(products).map(([code, label]) => <option key={code} value={code}>{label}</option>)}</select></label>
        <label>Status<select aria-label="Status" value={status} onChange={event => { setStatus(event.target.value); refresh(); }}><option value="">All statuses</option><option value="draft">Draft</option><option value="withdrawn">Withdrawn</option></select></label>
        <label>Sort by<select aria-label="Sort by" value={sort} onChange={event => { setSort(event.target.value); refresh(); }}><option value="reference">Quote reference</option><option value="updated">Last updated</option><option value="start">Requested start date</option></select></label>
        <label>Order<select aria-label="Order" value={direction} onChange={event => { setDirection(event.target.value); refresh(); }}><option value="asc">Ascending</option><option value="desc">Descending</option></select></label>
        <button className="button button-primary" type="submit">Search</button><button className="button" type="button" onClick={() => { setSearch(''); setQuery(''); setProduct(''); setStatus(''); setAgency(initialAgency ?? ''); setSort('reference'); setDirection('asc'); refresh(); }}>Clear filters</button>
      </form>
      {!initialAgency && <AgencyFilter value={agency} change={value => { setAgency(value); refresh(); }} />}
      {!resource.data ? <LoadFeedback error={resource.error} retry={refresh} /> : !resource.data.items.length ? <EmptyState title={query || product || status || agency ? 'No quotes match these filters' : 'No saved quotes yet'}>{query || product || status || agency ? 'Change the search or clear the filters.' : 'Create a quote draft to start capturing a Motor Trade proposal.'}</EmptyState> : <DataTable caption="Saved quotes" columns={['Quote','Client','Agency','Product','Status','Requested start','Last updated']}>
        {resource.data.items.map(row => <tr key={row.id}><td><Link href={`/quotes/${row.id}`}>{row.reference}</Link><p className="client-help">Revision {row.revisionNumber}</p></td><td><Link href={`/clients/${row.clientId}`}>{row.clientName}</Link><p className="client-help">{row.clientReference}</p></td><td>{row.agencyName}</td><td>{products[row.productCode] ?? row.productCode}</td><td><Status tone={row.state === 'draft' ? 'warning' : 'muted'}>{row.state === 'draft' ? 'Draft' : 'Withdrawn'}</Status></td><td>{row.startDate ? clientDate(row.startDate) : 'Not recorded'}</td><td>{clientDate(row.updatedAt, true)}</td></tr>)}
      </DataTable>}
      <Paging total={resource.data?.totalCount} previous={cursors.length ? () => setCursors(value => value.slice(0, -1)) : undefined} next={resource.data?.nextCursor ? () => setCursors(value => [...value, resource.data!.nextCursor!]) : undefined} />
    </Panel></>;
}

function AgencyFilter({ value, change }: { value: string; change: (id: string) => void }) {
  const [cursors, setCursors] = useState<string[]>([]);
  const resource = useQuoteResource<Page<Agency>>(`/api/v1/relationship-agencies?pageSize=25${cursors.at(-1) ? `&cursor=${encodeURIComponent(cursors.at(-1)!)}` : ''}`);
  return <div className="quote-agency-filter"><label>Agency<select aria-label="Filter by agency" value={value} onChange={event => change(event.target.value)}><option value="">All agencies</option>{value && !resource.data?.items.some(x => x.id === value) && <option value={value}>Selected agency (another page)</option>}{resource.data?.items.map(agency => <option key={agency.id} value={agency.id}>{agency.legalName} · {agency.reference}</option>)}</select></label>
    {resource.error && <LoadFeedback error={resource.error} retry={() => { setCursors([]); resource.refresh(); }} />}
    {(cursors.length > 0 || resource.data?.nextCursor) && <Paging previous={cursors.length ? () => setCursors(x => x.slice(0, -1)) : undefined} next={resource.data?.nextCursor ? () => setCursors(x => [...x, resource.data!.nextCursor!]) : undefined} />}
  </div>;
}
