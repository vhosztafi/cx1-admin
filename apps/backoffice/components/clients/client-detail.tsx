'use client';
import { RecordCommunications } from '../operations/communication-shared';
import { RecordDocuments } from '../operations/document-list';
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
import { PolicyList } from '../policies/policy-list';
import { TaskCreateEntry } from '../operations/task-create-entry';

const tabs = ['Overview','Policies','Quotes','Contacts','Notes','Messages','Activity'];
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
    facts={[{label:'Client reference',value:client.reference},{label:'Client since',value:clientDate(client.createdAt)},{label:'Address',value:`${client.address.town} ${client.address.postcode}`},{label:'Agency relationships',value:agencyNames},{label:'Primary contact',value:contactSummary},{label:'Records',value:canWrite?'Quotes and policies available':'Quote access restricted'}]}
    tabs={tabs.map(label => ({label,href:`/clients/${clientId}?tab=${label}`}))} active={tab}
    actions={canWrite && <button className="button" disabled={linking} onClick={() => {setEditing(true); setNotice('');}}>Edit identity</button>} />
    {notice && <div className="notice" role="status">{notice}</div>}
    {linking && <Panel title="Add agency relationship"><AddRelationship clientId={clientId} etag={resource.etag} onSaved={relationshipSaved} onClose={() => setLinking(false)} onReload={() => {setLinking(false); resource.refresh(); summary.refresh();}} /></Panel>}
    {editing && <Panel title="Edit client identity"><IdentityForm client={client} etag={resource.etag} onCancel={() => setEditing(false)} onReload={resource.refresh} onSaved={() => {setEditing(false); setNotice('Client identity saved.'); resource.refresh();}} /></Panel>}
    {tab === 'Notes' || tab === 'Messages' ? <ClientCommunications clientId={clientId} mode={tab==='Notes'?'notes':'messages'}/> : tab === 'Overview' ? <><div className="notice"><Status tone="info">Business identity</Status><span>This account holds the business identity. Policy declarations and documents belong to their individual records.</span></div><div className="client-overview-grid"><Panel title="Business details"><dl className="account-facts"><div><dt>Legal business name</dt><dd>{client.legalName}</dd></div><div><dt>Entity type</dt><dd>{entityTypes[client.entityType]}</dd></div><div><dt>Company number</dt><dd>{client.companyNumber ?? 'Not supplied'}</dd></div><div><dt>Correspondence address</dt><dd>{[client.address.line1,client.address.line2,client.address.town,client.address.county,client.address.postcode,'United Kingdom'].filter(Boolean).join(', ')}</dd></div></dl></Panel><Panel title="Business records">{canWrite && <p className="match-copy"><Link href={`/clients/${clientId}?tab=Quotes`}>View this client’s quotes</Link> · <Link href={`/quotes/new?clientId=${clientId}`}>New quote</Link></p>}{canWrite && <p className="match-copy"><Link href={`/clients/${clientId}?tab=Policies`}>View this client’s policies</Link></p>}</Panel></div><Relationships clientId={clientId} onAdd={canWrite && !editing && !linking ? () => setLinking(true) : undefined} /><ClientActivity clientId={clientId} /></> : tab === 'Activity' ? <ClientActivity clientId={clientId} /> : tab === 'Quotes' ? canWrite ? <QuoteList clientId={clientId} embedded /> : <Unavailable title="Quote access" description="Your current role cannot read quotes." /> : tab === 'Policies' ? canWrite ? <PolicyList clientId={clientId} embedded /> : <Unavailable title="Policy access" description="Your current role cannot read policies." /> : tab === 'Contacts' ? null : <Unavailable title={tab} description={`${tab} are not available yet. Policy records will appear after issuing is implemented.`} />}
    <div hidden={tab !== 'Contacts'}><Contacts clientId={clientId} canWrite={canWriteContacts} canSupport={canSupport} onSummary={setContactSummary} generation={contactGeneration} /></div>
  </>;
}
function Unavailable({ title, description }: { title: string; description: string }) { return <Panel title={title}><EmptyState title={`${title} are not available yet`}>{description}</EmptyState></Panel>; }
function ClientCommunications({clientId,mode}:{clientId:string;mode:'notes'|'messages'}) {
  const [pages,setPages]=useState(['']),[selected,setSelected]=useState<Relationship>();const cursor=pages.at(-1)!;
  const read=useClientResource<Page<Relationship>>(`/api/v1/clients/${clientId}/relationships?pageSize=15${cursor?`&cursor=${encodeURIComponent(cursor)}`:''}`);
  return <><Panel title="Choose agency relationship" note="Notes and messages remain within the selected relationship"><div className="quote-rail-body">
    {!read.data?<LoadFeedback error={read.error} retry={read.refresh}/>:read.data.items.filter(x=>x.state==='active').map(row=><button className="button" key={row.id} onClick={()=>setSelected(row)}>{row.agencyReference} · {row.agencyName}</button>)}
    <Paging total={read.data?.totalCount} previous={pages.length>1?()=>setPages(x=>x.slice(0,-1)):undefined} next={read.data?.nextCursor?()=>setPages(x=>[...x,read.data!.nextCursor!]):undefined}/>
  </div></Panel>{selected&&<RecordCommunications key={`${selected.id}:${mode}`} parent={{kind:'relationship',id:selected.id,label:selected.agencyReference}} mode={mode}/>}</>;
}
function Relationships({ clientId, onAdd }: { clientId: string; onAdd?: () => void }) {
  const [documentRelationship, setDocumentRelationship] = useState<Relationship>();
  const [history,setHistory] = useState(['']); const cursor = history.at(-1)!;
  const resource = useClientResource<Page<Relationship>>(`/api/v1/clients/${clientId}/relationships?pageSize=15${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
  return <Panel title="Agency relationships" note="Contacts and servicing instructions belong to each relationship">
    {onAdd && <div className="panel-footer"><button className="button" onClick={onAdd}>Add agency relationship</button></div>}
    {!resource.data ? <LoadFeedback error={resource.error} retry={resource.refresh} /> : !resource.data.items.length ? <EmptyState title="No agency relationships">Agency relationships will appear here when this client is associated with an agency.</EmptyState> : <DataTable caption="Agency relationships" columns={['Agency','Reference','Relationship status']}>
      {resource.data.items.map(row => <tr key={row.id}><td>{row.agencyName}</td><td>{row.agencyReference}</td><td><Status tone={row.state === 'active' ? 'success' : 'muted'}>{row.state}</Status>{row.state === 'active' && <><TaskCreateEntry parent={{kind: 'relationship', id: row.id, label: row.agencyReference + ' · ' + row.agencyName}} /><button className="button" onClick={() => setDocumentRelationship(row)}>Relationship documents</button></>}</td></tr>)}
    </DataTable>}
    <Paging total={resource.data?.totalCount} previous={history.length > 1 ? () => setHistory(x => x.slice(0,-1)) : undefined} next={resource.data?.nextCursor ? () => setHistory(x => [...x,resource.data!.nextCursor!]) : undefined} />
    {documentRelationship && <div className="quote-rail-body"><h3>{documentRelationship.agencyReference} · {documentRelationship.agencyName}</h3><button className="button" onClick={() => setDocumentRelationship(undefined)}>Close relationship documents</button><RecordDocuments key={documentRelationship.id} parent={{kind:'relationship',id:documentRelationship.id,label:documentRelationship.agencyReference}} relationshipId={documentRelationship.id}/></div>}
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
