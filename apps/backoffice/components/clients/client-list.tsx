'use client';
import { useState } from 'react';
import Link from 'next/link';
import { DataTable, EmptyState, Panel } from '../primitives';
import { entityTypes, type ClientSummary, type Page } from '../../lib/clients';
import { LoadFeedback, Paging, useClientResource } from './shared';

export function ClientList({ canWrite }: { canWrite: boolean }) {
  const [search, setSearch] = useState(''); const [query, setQuery] = useState(''); const [entity, setEntity] = useState(''); const [history, setHistory] = useState<string[]>(['']);
  const cursor = history.at(-1)!;
  const resource = useClientResource<Page<ClientSummary>>(`/api/v1/clients?pageSize=15${query ? `&q=${encodeURIComponent(query)}` : ''}${entity ? `&entityType=${entity}` : ''}${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
  function reset() { setHistory(['']); resource.refresh(); }
  return <><div className="page-heading"><div><h1>Clients</h1><p>Business identities and agency relationships</p></div>{canWrite && <Link className="button button-primary" href="/clients/new">Create client</Link>}</div>
    <Panel title="Client accounts">
      <form className="operations-toolbar" onSubmit={event => { event.preventDefault(); setQuery(search.trim()); reset(); }}>
        <label>Search clients<input type="search" placeholder="Business, agency, reference or company number" value={search} maxLength={200} onChange={event => setSearch(event.target.value)} /></label>
        <label>Entity type<select value={entity} onChange={event => {setEntity(event.target.value); reset();}}><option value="">All entity types</option>{Object.entries(entityTypes).map(([key,label]) => <option key={key} value={key}>{label}</option>)}</select></label>
        <button className="button" type="submit">Search</button><button className="button" type="button" onClick={() => {setSearch(''); setQuery(''); setEntity(''); reset();}}>Clear filters</button>
      </form>
      {!resource.data ? <LoadFeedback error={resource.error} retry={reset} /> : resource.data.items.length === 0 ? <EmptyState title="No clients match your search">Try another business name or clear the filters.</EmptyState> : <DataTable caption="Client accounts" columns={['Reference','Business','Main contact','Entity type','Trade activities','Agencies with records','Records']}>
        {resource.data.items.map(client => <tr key={client.id}><td className="client-reference"><Link href={`/clients/${client.id}`} prefetch={false}>{client.reference}</Link></td><td><Link className="client-name" href={`/clients/${client.id}`} prefetch={false}>{client.legalName}</Link></td><td>{client.primaryContactName ?? 'Not available yet'}</td><td>{entityTypes[client.entityType] ?? client.entityType}</td><td>{client.tradeActivities?.join(', ') ?? 'Not available yet'}</td><td>{client.agencies.length ? client.agencies.map(x => x.name).join(' · ') : 'No agency relationships'}</td><td>{client.records.state === 'available' ? `${client.records.policyCount} policies · ${client.records.quoteCount} quotes` : 'Not available yet'}</td></tr>)}
      </DataTable>}
      <Paging total={resource.data?.totalCount} previous={history.length > 1 ? () => setHistory(x => x.slice(0,-1)) : undefined} next={resource.data?.nextCursor ? () => setHistory(x => [...x, resource.data!.nextCursor!]) : undefined} />
    </Panel>
  </>;
}
