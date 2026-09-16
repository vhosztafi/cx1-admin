'use client';
import { useState } from 'react';
import Link from 'next/link';
import { DataTable, EmptyState, Panel, Status } from '../primitives';
import { clientDate, entityTypes, type Activity, type Client, type Page, type Relationship } from '../../lib/clients';
import { LoadFeedback, Paging, useClientResource } from './shared';
import { IdentityForm } from './identity-form';
import { RecordHeader } from '../record-header';
import { AddRelationship } from './add-relationship';
import { Contacts } from './contacts';
import { QuoteList } from '../quotes/quote-list';

const tabs = ['Overview','Policies','Quotes','Contacts','Activity'];
export function ClientDetail({ clientId, tab: requestedTab, canWrite, canWriteContacts, canSupport }: { clientId: string; tab: string; canWrite: boolean; canWriteContacts: boolean; canSupport: boolean }) {
  const resource = useClientResource<Client>(`/api/v1/clients/${clientId}`);
  const summary = useClientResource<Page<Relationship>>(`/api/v1/clients/${clientId}/relationships?pageSize=3`);
  const [editing, setEditing] = useState(false); const [notice, setNotice] = useState('');
  const [contactGeneration,setContactGeneration] = useState(0);
  const [contactSummary,setContactSummary] = useState('Choose an agency relationship');
  const [linking,setLinking] = useState(false);
  const tab = tabs.includes(requestedTab) ? requestedTab : 'Overview';
  if (!resource.data) return <Panel title="Client account"><LoadFeedback error={resource.error} retry={resource.refresh} /></Panel>;
  const client = resource.data;
  const agencyNames = summary.data ? (summary.data.items.map(x => x.agencyName).join(' · ') || 'None linked') + (summary.data.nextCursor ? ` · ${summary.data.totalCount ?? 'More'} relationships in total` : '') : summary.error ? 'Unavailable — see relationships below' : 'Loading…';
  function relationshipSaved() {setContactGeneration(x => x + 1);setLinking(false); setNotice('Agency relationship saved.'); resource.refresh(); summary.refresh();}
  return <><Link className="client-back" href="/clients">← All clients</Link><RecordHeader type="Client account" title={client.legalName}
    subtitle={`${entityTypes[client.entityType]}${client.companyNumber ? ` · Company no. ${client.companyNumber}` : ''}`} status={client.identityState === 'active' ? 'Active client' : 'Inactive client'}
    facts={[{label:'Client reference',value:client.reference},{label:'Client since',value:clientDate(client.createdAt)},{label:'Address',value:`${client.address.town} ${client.address.postcode}`},{label:'Agency relationships',value:agencyNames},{label:'Primary contact',value:contactSummary},{label:'Records',value:canWrite?'Quotes available · policies pending':'Quote access restricted'}]}
    tabs={tabs.map(label => ({label,href:`/clients/${clientId}?tab=${label}`}))} active={tab}
    actions={canWrite && <button className="button" disabled={linking} onClick={() => {setEditing(true); setNotice('');}}>Edit identity</button>} />
    {notice && <div className="notice" role="status">{notice}</div>}
    {linking && <Panel title="Add agency relationship"><AddRelationship clientId={clientId} etag={resource.etag} onSaved={relationshipSaved} onClose={() => setLinking(false)} onReload={() => {setLinking(false); resource.refresh(); summary.refresh();}} /></Panel>}
    {editing && <Panel title="Edit client identity"><IdentityForm client={client} etag={resource.etag} onCancel={() => setEditing(false)} onReload={resource.refresh} onSaved={() => {setEditing(false); setNotice('Client identity saved.'); resource.refresh();}} /></Panel>}
    {tab === 'Overview' ? <><div className="notice"><Status tone="info">Business identity</Status><span>This account holds the business identity. Policy declarations and documents belong to their individual records.</span></div><div className="client-overview-grid"><Panel title="Business details"><dl className="account-facts"><div><dt>Legal business name</dt><dd>{client.legalName}</dd></div><div><dt>Entity type</dt><dd>{entityTypes[client.entityType]}</dd></div><div><dt>Company number</dt><dd>{client.companyNumber ?? 'Not supplied'}</dd></div><div><dt>Correspondence address</dt><dd>{[client.address.line1,client.address.line2,client.address.town,client.address.county,client.address.postcode,'United Kingdom'].filter(Boolean).join(', ')}</dd></div></dl></Panel><Panel title="Business records">{canWrite && <p className="match-copy"><Link href={`/clients/${clientId}?tab=Quotes`}>View this client’s quotes</Link> · <Link href={`/quotes/new?clientId=${clientId}`}>New quote</Link></p>}<p className="match-copy">Policy records are not available yet.</p></Panel></div><Relationships clientId={clientId} onAdd={canWrite && !editing && !linking ? () => setLinking(true) : undefined} /><ClientActivity clientId={clientId} /></> : tab === 'Activity' ? <ClientActivity clientId={clientId} /> : tab === 'Quotes' ? canWrite ? <QuoteList clientId={clientId} embedded /> : <Unavailable title="Quote access" description="Your current role cannot read quotes." /> : tab === 'Contacts' ? null : <Unavailable title={tab} description={`${tab} are not available yet. Policy records will appear after issuing is implemented.`} />}
    <div hidden={tab !== 'Contacts'}><Contacts clientId={clientId} canWrite={canWriteContacts} canSupport={canSupport} onSummary={setContactSummary} generation={contactGeneration} /></div>
  </>;
}
function Unavailable({ title, description }: { title: string; description: string }) { return <Panel title={title}><EmptyState title={`${title} are not available yet`}>{description}</EmptyState></Panel>; }
function Relationships({ clientId, onAdd }: { clientId: string; onAdd?: () => void }) {
  const [history,setHistory] = useState(['']); const cursor = history.at(-1)!;
  const resource = useClientResource<Page<Relationship>>(`/api/v1/clients/${clientId}/relationships?pageSize=15${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
  return <Panel title="Agency relationships" note="Contacts and servicing instructions belong to each relationship">
    {onAdd && <div className="panel-footer"><button className="button" onClick={onAdd}>Add agency relationship</button></div>}
    {!resource.data ? <LoadFeedback error={resource.error} retry={resource.refresh} /> : !resource.data.items.length ? <EmptyState title="No agency relationships">Agency relationships will appear here when this client is associated with an agency.</EmptyState> : <DataTable caption="Agency relationships" columns={['Agency','Reference','Relationship status']}>
      {resource.data.items.map(row => <tr key={row.id}><td>{row.agencyName}</td><td>{row.agencyReference}</td><td><Status tone={row.state === 'active' ? 'success' : 'muted'}>{row.state}</Status></td></tr>)}
    </DataTable>}
    <Paging total={resource.data?.totalCount} previous={history.length > 1 ? () => setHistory(x => x.slice(0,-1)) : undefined} next={resource.data?.nextCursor ? () => setHistory(x => [...x,resource.data!.nextCursor!]) : undefined} />
  </Panel>;
}
function ClientActivity({ clientId }: { clientId: string }) {
  const [history,setHistory] = useState(['']); const cursor = history.at(-1)!;
  const resource = useClientResource<Page<Activity>>(`/api/v1/clients/${clientId}/activity?pageSize=10${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
  return <Panel title="Activity" note="Saved client servicing events">
    {!resource.data ? <LoadFeedback error={resource.error} retry={resource.refresh} /> : !resource.data.items.length ? <EmptyState title="No activity yet">Saved servicing actions will appear here.</EmptyState> : <DataTable caption="Client activity" columns={['When','Activity','By']}>
      {resource.data.items.map(row => <tr key={row.id}><td>{clientDate(row.occurredAt,true)}</td><td>{row.recordKind === 'client' && row.recordId === clientId ? <Link href={`/clients/${clientId}`}>{row.summary}</Link> : row.recordKind === 'quote' && row.recordId ? <Link href={`/quotes/${row.recordId}`}>{row.summary}</Link> : row.recordKind === 'match' && row.recordId ? <Link href={`/matches/${row.recordId}`}>{row.summary}</Link> : row.summary}</td><td>{row.actorLabel}</td></tr>)}
    </DataTable>}
    <Paging total={resource.data?.totalCount} previous={history.length > 1 ? () => setHistory(x => x.slice(0,-1)) : undefined} next={resource.data?.nextCursor ? () => setHistory(x => [...x,resource.data!.nextCursor!]) : undefined} />
  </Panel>;
}
