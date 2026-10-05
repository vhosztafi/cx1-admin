'use client';
import Link from 'next/link';
import { DataTable, EmptyState, Panel, Status } from '../primitives';
import { clientDate, type Page } from '../../lib/clients';
import { quoteStateLabel } from '../../lib/underwriting-api';
import { policyStateLabels } from '../policies/policy-list';
import { LoadFeedback, useClientResource } from './shared';

type RecordRow = { id: string; reference: string; productCode: string; agencyName: string; state: string; startDate?: string; startsAt?: string; endsAt?: string };
const products: Record<string, string> = { 'motor-trade-road-risks': 'Motor Trade Road Risks', 'motor-trade-combined': 'Motor Trade Combined' };

// The two lists have independent server cursors. This preview never implies that
// an initial page is a complete portfolio; full lists remain linked below.
export function ClientRecords({ clientId, title = 'Policies and quotes' }: { clientId: string; title?: string }) {
  const policies = useClientResource<Page<RecordRow>>(`/api/v1/policies?clientId=${clientId}&pageSize=10&sort=reference&direction=asc`);
  const quotes = useClientResource<Page<RecordRow>>(`/api/v1/quotes?clientId=${clientId}&pageSize=10&sort=reference&direction=asc`);
  const rows = [...(policies.data?.items ?? []).map(row => ({ ...row, kind: 'Policy' })), ...(quotes.data?.items ?? []).map(row => ({ ...row, kind: 'Quote' }))]
    .filter(row => row.productCode in products);
  return <Panel title={title} note="Records held by each agency for this business">
    {!policies.data && <LoadFeedback error={policies.error} retry={policies.refresh} />}
    {!quotes.data && <LoadFeedback error={quotes.error} retry={quotes.refresh} />}
    {rows.length ? <DataTable caption="Client policies and quotes" columns={['Reference', 'Type', 'Product', 'Agency', 'Status', 'Period']}>
      {rows.map(row => <tr key={`${row.kind}:${row.id}`}><td className="client-reference"><Link href={`/${row.kind === 'Policy' ? 'policies' : 'quotes'}/${row.id}`}>{row.reference}</Link></td><td>{row.kind}</td><td>{products[row.productCode]}</td><td>{row.agencyName}</td><td><Status>{row.kind === 'Policy' ? policyStateLabels[row.state] ?? row.state : quoteStateLabel(row.state)}</Status></td><td>{row.startsAt && row.endsAt ? `${clientDate(row.startsAt)} – ${clientDate(row.endsAt)}` : row.startDate ? `Requested ${clientDate(row.startDate)}` : 'Not specified'}</td></tr>)}
    </DataTable> : policies.data && quotes.data && <EmptyState title="No Motor Trade records in this preview">Open the full lists or start a new quote for this client.</EmptyState>}
    <div className="panel-footer operations-actions"><span>{policies.data?.nextCursor || quotes.data?.nextCursor ? 'More records available in the full lists' : 'Motor Trade records'}</span><Link href={`/clients/${clientId}?tab=Policies`}>All policies</Link><Link href={`/clients/${clientId}?tab=Quotes`}>All quotes</Link></div>
  </Panel>;
}
