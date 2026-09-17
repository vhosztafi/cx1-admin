'use client';
import Link from 'next/link';
import { useState } from 'react';
import { DataTable, EmptyState, Panel, Status } from '../primitives';
import { clientDate, type ClientSummary, type Page } from '../../lib/clients';
import { LoadFeedback, Paging, useQuoteResource } from '../quotes/shared';
import { AgencyFilter } from '../quotes/quote-list';

type PolicyRow = { id: string; reference: string; clientId: string; clientName: string; agencyName: string; productCode: string; state: string; startsAt: string; endsAt: string; issuedAt: string };
export const policyStateLabels: Record<string, string> = { scheduled: 'Inception scheduled', active: 'In force', expired: 'Term ended', cancelled: 'Cancelled' };
const products: Record<string, string> = { 'motor-trade-road-risks': 'Motor Trade Road Risks', 'motor-trade-combined': 'Motor Trade Combined' };
export function PolicyList({ clientId, embedded = false }: { clientId?: string; embedded?: boolean }) {
  const [input, setInput] = useState(''), [search, setSearch] = useState(''), [product, setProduct] = useState(''), [state, setState] = useState('');
  const [agency, setAgency] = useState(''), [client, setClient] = useState(clientId ?? ''), [from, setFrom] = useState(''), [to, setTo] = useState('');
  const [sort, setSort] = useState('reference'), [direction, setDirection] = useState('asc'), [cursors, setCursors] = useState<string[]>([]);
  const params = new URLSearchParams({ pageSize: '10', sort, direction });
  for (const [key, value] of Object.entries({ q: search, productCode: product, state, agencyId: agency, clientId: client, inceptionFrom: from, inceptionTo: to, cursor: cursors.at(-1) })) if (value) params.set(key, value);
  const result = useQuoteResource<Page<PolicyRow>>(`/api/v1/policies?${params}`);
  const refresh = () => { setCursors([]); result.refresh(); };
  function clear() { setInput(''); setSearch(''); setProduct(''); setState(''); setAgency(''); setClient(clientId ?? ''); setFrom(''); setTo(''); setSort('reference'); setDirection('asc'); refresh(); }
  return <>{!embedded && <div className="page-heading"><div><h1>Policies</h1><p>Issued risks, current terms and retained policy records</p></div><Link className="button" href="/quotes">Open quotes</Link></div>}
    <Panel title={embedded ? 'Client policies' : 'Issued policies'} note="Search policy references, clients, agencies and issued registrations">
      <form className="operations-toolbar quote-list-filters" onSubmit={event => { event.preventDefault(); setSearch(input.trim()); refresh(); }}>
        <label>Search policies<input type="search" value={input} maxLength={200} placeholder="Reference, business or registration" onChange={event => setInput(event.target.value)} /></label>
        <label>Product<select aria-label="Product" value={product} onChange={event => { setProduct(event.target.value); refresh(); }}><option value="">All Motor Trade products</option>{Object.entries(products).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
        <label>Status<select aria-label="Status" value={state} onChange={event => { setState(event.target.value); refresh(); }}><option value="">All statuses</option>{Object.entries(policyStateLabels).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
        <label>Inception from<input type="date" value={from} max={to || undefined} onChange={event => { setFrom(event.target.value); refresh(); }} /></label>
        <label>Inception to<input type="date" value={to} min={from || undefined} onChange={event => { setTo(event.target.value); refresh(); }} /></label>
        <label>Sort by<select aria-label="Sort by" value={sort} onChange={event => { setSort(event.target.value); refresh(); }}><option value="reference">Policy reference</option><option value="inception">Inception</option><option value="issued">Issued</option></select></label>
        <label>Order<select aria-label="Order" value={direction} onChange={event => { setDirection(event.target.value); refresh(); }}><option value="asc">Ascending</option><option value="desc">Descending</option></select></label>
        <button className="button button-primary" type="submit">Search</button><button className="button" type="button" onClick={clear}>Clear filters</button>
      </form>
      <div className="policy-discovery-parties"><AgencyFilter value={agency} change={value => { setAgency(value); refresh(); }} />
      {!clientId && <ClientFilter value={client} change={value => { setClient(value); refresh(); }} />}</div>
      {!result.data ? <LoadFeedback error={result.error} retry={refresh} /> : !result.data.items.length ? <EmptyState title="No policies match these filters">Clear filters or open an accepted quote to review issue eligibility.</EmptyState> : <DataTable caption="Issued policies" columns={['Policy', 'Client', 'Agency', 'Product', 'Status', 'Inception', 'Expiry']}>
        {result.data.items.map(row => <tr key={row.id}><td><Link href={`/policies/${row.id}`}>{row.reference}</Link></td><td><Link href={`/clients/${row.clientId}`}>{row.clientName}</Link></td><td>{row.agencyName}</td><td>{products[row.productCode] ?? row.productCode}</td><td><Status tone={row.state === 'active' ? 'success' : 'info'}>{policyStateLabels[row.state] ?? row.state}</Status></td><td>{clientDate(row.startsAt, true)}</td><td>{clientDate(row.endsAt, true)}</td></tr>)}
      </DataTable>}
      <Paging total={result.data?.totalCount} previous={cursors.length ? () => setCursors(x => x.slice(0, -1)) : undefined} next={result.data?.nextCursor ? () => setCursors(x => [...x, result.data!.nextCursor!]) : undefined} />
    </Panel></>;
}
function ClientFilter({ value, change }: { value: string; change: (id: string) => void }) {
  const [cursors, setCursors] = useState<string[]>([]);
  const result = useQuoteResource<Page<ClientSummary>>(`/api/v1/clients?pageSize=25${cursors.at(-1) ? '&cursor=' + encodeURIComponent(cursors.at(-1)!) : ''}`);
  return <div className="quote-agency-filter"><label>Client<select aria-label="Filter by client" value={value} onChange={event => change(event.target.value)}><option value="">All clients</option>{value && !result.data?.items.some(x => x.id === value) && <option value={value}>Selected client (another page)</option>}{result.data?.items.map(x => <option key={x.id} value={x.id}>{x.legalName} · {x.reference}</option>)}</select></label>
    {result.error && <LoadFeedback error={result.error} retry={() => { setCursors([]); result.refresh(); }} />}
    {(cursors.length > 0 || result.data?.nextCursor) && <Paging previous={cursors.length ? () => setCursors(x => x.slice(0, -1)) : undefined} next={result.data?.nextCursor ? () => setCursors(x => [...x, result.data!.nextCursor!]) : undefined} />}
  </div>;
}
