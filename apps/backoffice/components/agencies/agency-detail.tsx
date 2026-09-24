'use client';
import { TaskCreateEntry } from '../operations/task-create-entry';
import { RecordCommunications } from '../operations/communication-shared';
import { RecordDocuments } from '../operations/document-list';
import Link from 'next/link';
import { useState } from 'react';
import { Panel, DataTable } from '../primitives';
import { RecordHeader } from '../record-header';
import { agencyFetch, flattenDraft, type AgencyDraft, type AgencyProduct, type CatalogProduct } from '../../lib/agencies';
import { agencyFields } from '../../lib/agency-fields';
import { clientDate, type Page } from '../../lib/clients';
import { LoadFeedback, Paging, useAgencyResource } from './shared';
import { AbandonAgency } from './abandon-agency';
import { AgencyNotifications } from './agency-notifications';
import { AgencyPermissions } from './agency-permissions';
import { AgencyUsers } from './agency-users';
import { AgencyStateRequests } from './agency-state-requests';
import { AgencyTermsHistory } from './agency-terms-history';
import { AgencyTermsProposal } from './agency-terms-proposal';
import { AgencyTermsRequests } from './agency-terms-requests';
const tabs = ['Overview','Users','Permissions & access','Products','Accounts','Documents','Notes','Messages','Activity'];
export function AgencyDetail({id,actorId,canWrite,canFinance=false,tab: requested}: {id: string; actorId:string; canWrite: boolean; canFinance?:boolean; tab: string}) {
  const [updatedCounts,setUpdatedCounts]=useState<{id:string;users?:number;invited?:number}>();
  const [activityRevision,setActivityRevision]=useState(0);
  const [termsRevision,setTermsRevision]=useState(0);
  const resource = useAgencyResource<AgencyDraft>(`/api/v1/agencies/${id}`); const products = useAgencyResource<Page<AgencyProduct>>(`/api/v1/agencies/${id}/products`); const catalog = useAgencyResource<Page<CatalogProduct>>('/api/v1/agency-product-catalog');
  const managers = useAgencyResource<Page<{id: string; displayName: string}>>('/api/v1/agency-relationship-managers?pageSize=100');
  if(!resource.data) return <Panel title="Agency"><LoadFeedback error={resource.error} retry={resource.refresh} /></Panel>;
  const agency = resource.data; const values = flattenDraft(agency.details); const tab = tabs.includes(requested) ? requested : 'Overview';
  return <><Link className="client-back" href="/agents">← All agencies</Link><RecordHeader type="Wholesale agency" title={values.legalName ?? 'Unnamed agency draft'} subtitle={values.tradingName ?? 'Agency onboarding'} status={agency.state === 'draft' ? 'Onboarding' : agency.state}
    facts={[{label:'Agency reference',value:agency.reference},{label:'FCA reference',value:values.regulatoryReference ?? 'Not entered'},{label:'Main contact',value:values['mainContact.name'] ?? 'Not entered'},{label:'Users',value:updatedCounts?.id===id?(updatedCounts.users===undefined?'Unavailable':`${updatedCounts.users} / ${updatedCounts.invited} awaiting acceptance`):`${agency.userCount} / ${agency.invitedUserCount} awaiting acceptance`}]} tabs={tabs.map(label => ({label,href:`/agents/${id}?tab=${encodeURIComponent(label)}`}))} active={tab} actions={agency.state === 'active' ? <Link className="button" href={"/agents/"+id+"/sharing"}>Preview shared data</Link> : canWrite && agency.state === 'draft' ? <Link className="button" href={`/agents/${id}/onboarding`}>Resume onboarding</Link> : undefined} />
    <div className="operations-actions"><TaskCreateEntry parent={{kind: "agency", id, label: agency.reference}} /></div>{tab === 'Overview' && <>{canWrite && <AgencyStateRequests id={id} actorId={actorId} agencyState={agency.state} etag={resource.etag!} onRefresh={resource.refresh} onSaved={()=>{resource.refresh();products.refresh();setActivityRevision(x=>x+1);}} />}<Panel title="Agency details"><dl className="agency-review">{agencyFields.filter(x => x.stage <= 2 && values[x.path]).map(field => <div key={field.path}><dt>{field.label}</dt><dd>{field.path === 'relationshipManagerId' ? managers.data?.items.find(x => x.id === values[field.path])?.displayName ?? 'Saved manager (currently unavailable)' : field.options ? Object.entries(field.options).find(([,value]) => String(value) === values[field.path])?.[0] ?? values[field.path] : values[field.path]}</dd></div>)}</dl></Panel>{agency.state === 'draft' && <Panel title="Activation checklist"><ul className="agency-checklist">{agency.validation.items.map(x => <li key={x.code}><strong>{({satisfied:'Satisfied',missing:'Missing',pending:'Pending',failed:'Needs attention',stale:'Out of date',expired:'Expired',unavailable:'Unavailable'} as Record<string,string>)[x.state] ?? 'Unavailable'}</strong><span>{x.message}</span>{canWrite && agency.state === 'draft' && x.state === 'missing' && <Link href={`/agents/${id}/onboarding`}>Resume onboarding</Link>}</li>)}</ul></Panel>}{canWrite && agency.state === 'draft' && <div className="operations-actions"><AbandonAgency id={id} etag={resource.etag!} onSaved={resource.refresh} /><Link className="button" href="/agents/new">Create another agency</Link></div>}</>}
    {tab === 'Accounts'&&canFinance&&<div className="operations-actions"><Link className="button button-primary" href={`/accounting?tab=accounts&agencyId=${id}`}>Open in Accounting</Link></div>}
    {(tab === 'Notes' || tab === 'Messages') && <RecordCommunications parent={{kind:'agency',id,label:agency.reference}} mode={tab==='Notes'?'notes':'messages'} />}
    {tab === 'Products' && <>{canWrite && ['active','suspended'].includes(agency.state) && <><AgencyTermsProposal id={id} onSaved={()=>{setActivityRevision(x=>x+1);setTermsRevision(x=>x+1);}} /><AgencyTermsRequests key={termsRevision} id={id} actorId={actorId} onApplied={()=>{resource.refresh();products.refresh();setActivityRevision(x=>x+1);}} /></>}<Panel title={agency.state === 'draft' || agency.state === 'abandoned' ? "Draft product selections" : "Current approved products"}>{!products.data ? <LoadFeedback error={products.error} retry={products.refresh} /> : <DataTable caption="Agency products" columns={['Product','Provider','Commission','Effective from','Access']}>
      {products.data.items.map(x => {const definition = catalog.data?.items.find(p => p.productVersionId === x.productVersionId);return <tr key={x.id}><td>{definition?.name ?? x.productCode?.replaceAll('-',' ')}</td><td>{definition?.capacityProviderName ?? 'Unavailable'}</td><td>{(x.brokerCommissionBasisPoints / 100).toFixed(2)}%</td><td>{x.effectiveFrom}</td><td>{agency.state === 'active' ? 'Approved product grant' : agency.state === 'suspended' ? 'Agency access suspended' : 'Draft selection; access not granted'}</td></tr>;})}</DataTable>}{products.data?.items.length === 0 && <p className="match-copy">No products selected.</p>}{catalog.error && <LoadFeedback error={catalog.error} retry={catalog.refresh} />}</Panel>{['active','suspended'].includes(agency.state) && <AgencyTermsHistory id={id} />}</>}
    {tab === 'Accounts' && (['active','suspended'].includes(agency.state) ? <AgencyTermsHistory id={id} accounts /> : <Panel title="Credit and settlement"><dl className="agency-review">{agencyFields.filter(x => x.stage === 5 && values[x.path]).map(field => <div key={field.path}><dt>{field.label}</dt><dd>{field.options ? Object.entries(field.options).find(([,value]) => String(value) === values[field.path])?.[0] ?? values[field.path] : values[field.path]}</dd></div>)}</dl><p className="match-copy">{canFinance?'Open Accounting for saved balances and statements.':'Finance balances require an accounting role.'}</p></Panel>)}
    {tab === 'Users' && (canWrite ? <AgencyUsers id={id} agencyState={agency.state} onSaved={async()=>{setActivityRevision(x=>x+1);try{const fresh=await agencyFetch<AgencyDraft>(`/api/v1/agencies/${id}`);setUpdatedCounts({id,users:fresh.data.userCount,invited:fresh.data.invitedUserCount});}catch{setUpdatedCounts({id});}}}/> : <Panel title="Agency users"><p className="match-copy">User administration requires agency administrator access.</p></Panel>)}
    {tab === 'Permissions & access' && (canWrite?<AgencyPermissions id={id} actorId={actorId} agencyState={agency.state} etag={resource.etag!} onSaved={()=>{resource.refresh();setActivityRevision(x=>x+1);}}/>:<Panel title={tab}><p className="match-copy">Permission administration requires agency administrator access. Use Preview shared data to view the agency’s sharing reference.</p></Panel>)}
    {tab === 'Documents' && <RecordDocuments parent={{kind:'agency',id,label:agency.reference}} />}
    {tab === 'Activity' && <><AgencyActivity key={activityRevision} id={id} />{canWrite && <AgencyNotifications id={id} onSaved={()=>setActivityRevision(x=>x+1)}/>}</>}
  </>;
}
function AgencyActivity({id}: {id: string}) {
  const [cursors,setCursors] = useState<string[]>([]);
  const cursor = cursors.at(-1);
  const resource = useAgencyResource<Page<{id: string; occurredAt: string; actorLabel: string; summary: string}>>(`/api/v1/agencies/${id}/activity?pageSize=15${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
  return <Panel title="Agency activity">{!resource.data ? <LoadFeedback error={resource.error} retry={resource.refresh} /> : <><DataTable caption="Agency activity" columns={['When','Staff member','Activity']}>{resource.data.items.map(x => <tr key={x.id}><td>{clientDate(x.occurredAt,true)}</td><td>{x.actorLabel}</td><td>{x.summary}</td></tr>)}</DataTable><Paging total={resource.data.totalCount} previous={cursors.length ? () => setCursors(x => x.slice(0,-1)) : undefined} next={resource.data.nextCursor ? () => setCursors(x => [...x,resource.data!.nextCursor!]) : undefined} /></>}{resource.data?.items.length === 0 && <p className="match-copy">No activity recorded yet.</p>}</Panel>;
}
