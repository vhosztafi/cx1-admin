'use client';
import Link from 'next/link';
import { useState } from 'react';
import { DataTable, EmptyState, Panel, Status } from '../primitives';
import { clientDate, type Page } from '../../lib/clients';
import { LoadFeedback, Paging, useAgencyResource } from './shared';
import { policyStateLabels } from '../policies/policy-list';
type SharedPolicy = { id: string; reference: string; clientName: string; productCode: string; state: string; startsAt: string; endsAt: string };
export function AgencySharedPolicies({ agencyId, canOpenInternal }: { agencyId: string; canOpenInternal: boolean }) {
  const [input, setInput] = useState(''), [search, setSearch] = useState(''), [cursors, setCursors] = useState<string[]>([]), [selected, setSelected] = useState('');
  const result = useAgencyResource<Page<SharedPolicy>>(`/api/v1/agencies/${agencyId}/sharing/policies?pageSize=10${search ? '&q=' + encodeURIComponent(search) : ''}${cursors.at(-1) ? '&cursor=' + encodeURIComponent(cursors.at(-1)!) : ''}`);
  const refresh = () => { setCursors([]); setSelected(''); result.refresh(); };
  return <><Panel title="Their policies" note="Shared summaries for current agency relationships">
    <form className="agency-sharing-search" onSubmit={event => { event.preventDefault(); setSearch(input.trim()); refresh(); }}><label htmlFor="shared-policy-search">Search shared policy summaries</label><div><input id="shared-policy-search" value={input} maxLength={200} placeholder="Policy reference or client name" onChange={event => setInput(event.target.value)} /><button className="button button-primary" type="submit">Search</button><button className="button" type="button" onClick={() => { setInput(''); setSearch(''); refresh(); }}>Clear search</button></div></form>
    {!result.data ? <LoadFeedback error={result.error} retry={refresh} /> : !result.data.items.length ? <EmptyState title="No shared policies match">Only issued policies belonging to current agency relationships are shown.</EmptyState> : <DataTable caption="Shared policy summaries" columns={['Policy', 'Client', 'Product', 'Status', 'Inception', 'Expiry']}>
      {result.data.items.map(row => <tr key={row.id}><td><button className="button" aria-pressed={selected === row.id} onClick={() => setSelected(row.id)}>{row.reference}</button></td><td>{row.clientName}</td><td>{row.productCode === 'motor-trade-combined' ? 'Motor Trade Combined' : 'Motor Trade Road Risks'}</td><td><Status>{policyStateLabels[row.state]}</Status></td><td>{clientDate(row.startsAt, true)}</td><td>{clientDate(row.endsAt, true)}</td></tr>)}
    </DataTable>}
    <Paging total={result.data?.totalCount} previous={cursors.length ? () => { setCursors(x => x.slice(0, -1)); setSelected(''); } : undefined} next={result.data?.nextCursor ? () => { setCursors(x => [...x, result.data!.nextCursor!]); setSelected(''); } : undefined} />
    <p className="match-copy">Internal underwriting, risk declarations, registrations and accounting entries are not shared here.</p>
  </Panel>{selected && <SharedPolicyDetail key={selected} agencyId={agencyId} policyId={selected} canOpenInternal={canOpenInternal} />}</>;
}
function SharedPolicyDetail({ agencyId, policyId, canOpenInternal }: { agencyId: string; policyId: string; canOpenInternal: boolean }) {
  const result = useAgencyResource<SharedPolicy>(`/api/v1/agencies/${agencyId}/sharing/policies/${policyId}`);
  return <Panel title="Shared policy reference">{!result.data ? <LoadFeedback error={result.error} retry={result.refresh} /> : <div className="quote-rail-body"><h3>{result.data.reference}</h3><p>{result.data.clientName} · {policyStateLabels[result.data.state]}</p><p>{clientDate(result.data.startsAt, true)} to {clientDate(result.data.endsAt, true)}</p>{canOpenInternal && <Link className="button" href={`/policies/${policyId}`}>Open internal policy record</Link>}</div>}</Panel>;
}
