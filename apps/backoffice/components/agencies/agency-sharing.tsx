'use client';
import Link from 'next/link';
import {useState} from 'react';
import {Panel,DataTable,Status} from '../primitives';
import {clientDate,type Page} from '../../lib/clients';
import {LoadFeedback,Paging,useAgencyResource} from './shared';
import {AgencySharedQuotes} from './agency-shared-quotes';

type Sharing={agency:{id:string;reference:string;legalName:string;state:string};products:{productCode:string;name:string;effectiveFrom:string;effectiveTo?:string;available:boolean}[];permissions:{permission:string;granted:boolean;available:boolean}[]};
type SharedClient={id:string;relationshipId:string;reference:string;legalName:string};
type SharedContact={id:string;personId:string;fullName:string;role:string;email?:string;telephone?:string;isPrimary:boolean};
type SharedInstruction={id:string;personId:string;contactName:string;instruction:string;reviewOn:string};

export function AgencySharing({id}:{id:string}) {
  const resource=useAgencyResource<Sharing>(`/api/v1/agencies/${id}/sharing`);
  const [revision,setRevision]=useState(0);
  const refresh=()=>{resource.refresh();setRevision(x=>x+1);};
  return <>
    <div className="page-heading"><div><h1>Agency portal — data reference</h1><p>{resource.data?`What ${resource.data.agency.legalName} can see`:'Current shared agency data'} · internal reference only</p></div><Link className="button secondary" href={`/agents/${id}`}>Back to agency</Link></div>
    <section className="agency-sharing-banner" aria-label="Separate application"><Status tone="info">Separate application</Status><p>The agency portal is a separate application. This reference shows which records and fields are shared with the agency, and which stay inside the back office.</p></section>
    <div className="operations-actions agency-sharing-tools"><button className="button" onClick={refresh}>Refresh shared data</button>{resource.data&&<span className="match-copy">{resource.data.agency.reference} · {resource.data.agency.legalName}</span>}</div>
    {!resource.data?<Panel title="Shared agency data"><LoadFeedback error={resource.error} retry={refresh}/>{resource.error&&<p className="match-copy">Shared data is available for active agencies with the appropriate staff access. Return to the agency to check its current status.</p>}</Panel>:<>
      <SharedClients key={`${id}:${revision}`} id={id}/>
      <Panel title="Approved products" note="Current agreed product selection"><DataTable caption="Products in the sharing reference" columns={['Product','Effective from','Effective until','Availability']}>{resource.data.products.map(product=><tr key={product.productCode}><td>{product.name}</td><td>{clientDate(product.effectiveFrom)}</td><td>{product.effectiveTo?`${clientDate(product.effectiveTo)} (exclusive)`:'No scheduled end'}</td><td><Status tone={product.available?'success':'muted'}>{product.available?'Available':'Not available yet'}</Status></td></tr>)}</DataTable>{!resource.data.products.length&&<p className="match-copy">No approved product terms are in effect.</p>}</Panel>
      <Panel title="Their policies"><p className="match-copy">Shared policy information is not available yet.</p></Panel>
      <AgencySharedQuotes key={`quotes:${id}:${revision}`} agencyId={id}/><Panel title="Their open items"><p className="match-copy">Shared tasks are not available yet.</p></Panel>
      <Panel title="Hidden from agency users" note="Shown here for internal reference only"><DataTable caption="Agency sharing boundaries" columns={['Area','Visible to agency','Reason']}>
        <tr><td>Internal notes</td><td><Status>No</Status></td><td>Underwriting commentary stays inside the back office.</td></tr>
        <tr><td>Referral rules and authority limits</td><td><Status>No</Status></td><td>Commercially sensitive internal rules.</td></tr>
        <tr><td>Other agencies’ records</td><td><Status>No</Status></td><td>Records are limited to the selected agency’s relationships.</td></tr>
        <tr><td>Accounting ledger</td><td><Status tone="warning">Own statement only</Status></td><td>Statements are not available yet.</td></tr>
        <tr><td>Bordereaux</td><td><Status tone={resource.data.permissions.some(x=>x.permission==='bordereau-download'&&x.granted)?'success':'warning'}>{resource.data.permissions.some(x=>x.permission==='bordereau-download'&&x.granted)?'Approved':'Approval required'}</Status></td><td>{resource.data.permissions.some(x=>x.permission==='bordereau-download'&&x.available)?'Approved download access.':'Downloads are not available yet.'}</td></tr>
        <tr><td>Admin configuration</td><td><Status>No</Status></td><td>Internal administration only.</td></tr>
      </DataTable></Panel>
    </>}
  </>;
}

function SharedClients({id}:{id:string}) {
  const [input,setInput]=useState(''); const [search,setSearch]=useState(''); const [cursors,setCursors]=useState<string[]>([]); const [selected,setSelected]=useState<SharedClient>();
  const url=`/api/v1/agencies/${id}/sharing/clients?pageSize=10${search?`&q=${encodeURIComponent(search)}`:''}${cursors.at(-1)?`&cursor=${encodeURIComponent(cursors.at(-1)!)}`:''}`;
  const resource=useAgencyResource<Page<SharedClient>>(url);
  const refresh=()=>{setCursors([]);setSelected(undefined);resource.refresh();};
  return <><Panel title="Their clients" note="Only current agency relationships are shown">
    <form className="agency-sharing-search" onSubmit={event=>{event.preventDefault();setSearch(input.trim());setCursors([]);setSelected(undefined);resource.refresh();}}><label htmlFor="shared-client-search">Search shared clients</label><div><input id="shared-client-search" value={input} maxLength={200} onChange={event=>setInput(event.target.value)}/><button className="button button-primary" type="submit">Search</button><button className="button secondary" type="button" onClick={()=>{setInput('');setSearch('');refresh();}}>Clear search</button></div></form>
    {!resource.data?<LoadFeedback error={resource.error} retry={refresh}/>:<><DataTable caption="Shared clients" columns={['Reference','Client','Contacts and instructions']}>{resource.data.items.map(client=><tr key={client.relationshipId}><td className="mono">{client.reference}</td><td>{client.legalName}</td><td><button className="button secondary" aria-pressed={selected?.relationshipId===client.relationshipId} onClick={()=>setSelected(client)}>View contacts<span className="sr-only"> for {client.legalName}</span></button></td></tr>)}</DataTable>{!resource.data.items.length&&<p className="match-copy">No shared clients match this search.</p>}<Paging total={resource.data.totalCount} previous={cursors.length?()=>{setCursors(x=>x.slice(0,-1));setSelected(undefined);}:undefined} next={resource.data.nextCursor?()=>{setCursors(x=>[...x,resource.data!.nextCursor!]);setSelected(undefined);}:undefined}/></>}
  </Panel>{selected&&<SharedRelationship key={selected.relationshipId} id={id} client={selected}/>}</>;
}

function SharedRelationship({id,client}:{id:string;client:SharedClient}) {
  const [contactCursors,setContactCursors]=useState<string[]>([]); const [instructionCursors,setInstructionCursors]=useState<string[]>([]);
  const base=`/api/v1/agencies/${id}/sharing/relationships/${client.relationshipId}`;
  const contacts=useAgencyResource<Page<SharedContact>>(`${base}/contacts?pageSize=10${contactCursors.at(-1)?`&cursor=${encodeURIComponent(contactCursors.at(-1)!)}`:''}`);
  const instructions=useAgencyResource<Page<SharedInstruction>>(`${base}/instructions?pageSize=10${instructionCursors.at(-1)?`&cursor=${encodeURIComponent(instructionCursors.at(-1)!)}`:''}`);
  return <div className="agency-shared-relationship" aria-label={`Shared contact details for ${client.legalName}`}>
    <Panel title={`Contacts · ${client.legalName}`}><p className="match-copy">Contact details recorded for this agency relationship.</p>{!contacts.data?<LoadFeedback error={contacts.error} retry={()=>{setContactCursors([]);contacts.refresh();}}/>:<><DataTable caption="Shared contacts" columns={['Name','Role','Email','Telephone','Primary']}>{contacts.data.items.map(contact=><tr key={contact.id}><td>{contact.fullName}</td><td>{contact.role}</td><td>{contact.email??'Not recorded'}</td><td>{contact.telephone??'Not recorded'}</td><td>{contact.isPrimary?'Yes':'No'}</td></tr>)}</DataTable>{!contacts.data.items.length&&<p className="match-copy">No current contacts are shared for this relationship.</p>}<Paging total={contacts.data.totalCount} previous={contactCursors.length?()=>setContactCursors(x=>x.slice(0,-1)):undefined} next={contacts.data.nextCursor?()=>setContactCursors(x=>[...x,contacts.data!.nextCursor!]):undefined}/></>}</Panel>
    <Panel title="Shared support instructions" note="Only explicitly shared current instructions appear here">{!instructions.data?<LoadFeedback error={instructions.error} retry={()=>{setInstructionCursors([]);instructions.refresh();}}/>:<><DataTable caption="Shared support instructions" columns={['Contact','Instruction','Review date']}>{instructions.data.items.map(instruction=><tr key={instruction.id}><td>{instruction.contactName}</td><td>{instruction.instruction}</td><td>{clientDate(instruction.reviewOn)}</td></tr>)}</DataTable>{!instructions.data.items.length&&<p className="match-copy">No support instructions are shared for this relationship.</p>}<Paging total={instructions.data.totalCount} previous={instructionCursors.length?()=>setInstructionCursors(x=>x.slice(0,-1)):undefined} next={instructions.data.nextCursor?()=>setInstructionCursors(x=>[...x,instructions.data!.nextCursor!]):undefined}/></>}</Panel>
  </div>;
}
