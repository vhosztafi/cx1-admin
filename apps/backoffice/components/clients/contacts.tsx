'use client';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { DataTable, EmptyState, Panel, Status } from '../primitives';
import { ClientError, clientFetch, clientDate, uncertainFailure, type Page, type Relationship } from '../../lib/clients';
import { contactPayload, contactRoles, type Contact } from '../../lib/contacts';
import { SupportFlags } from './support-flags';
import { csrfToken } from '../../lib/auth';
import { LoadFeedback, Paging, useClientResource } from './shared';

type Action = {kind:'create'|'update'|'make-primary'|'end'; id?:string};
function contactError(error: unknown) {
  if (!(error instanceof ClientError)) return 'The service could not confirm the result. Retry the same action.';
  return error.status === 404 ? 'This contact or agency relationship is unavailable.' : [412,428].includes(error.status) ? 'The contact or relationship has changed. Reload the saved record before continuing.' :
    error.status === 409 ? 'Check the current primary contact. Make another active contact primary before ending or demoting a primary contact. Ended contacts cannot be edited.' : error.message;
}
export function Contacts({clientId,canWrite,canSupport,onSummary,generation}:{clientId:string;canWrite:boolean;canSupport:boolean;onSummary:(value:string)=>void;generation:number}) {
  const [selection,setSelected] = useState<Relationship | null>(); const [locked,setLocked] = useState(false);
  const [history,setHistory] = useState(['']); const cursor = history.at(-1)!;
  const relationships = useClientResource<Page<Relationship>>(`/api/v1/clients/${clientId}/relationships?pageSize=15${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`,generation);
  const page = relationships.data;
  const selected = selection === undefined && page && !page.nextCursor && !cursor && page.items.length === 1 && page.items[0].state === 'active' ? page.items[0] : selection;
  useEffect(() => {if (!selected) onSummary('Choose an agency relationship');},[selected,onSummary]);
  return <><Panel title="Agency relationship" note="Choose the relationship whose contacts you want to service">
    {!relationships.data ? <LoadFeedback error={relationships.error} retry={relationships.refresh} /> : !relationships.data.items.length ? <EmptyState title="No agency relationships">Add an agency relationship on the Overview tab before adding contacts.</EmptyState> : <div className="operations-toolbar"><label>Selected agency relationship<select disabled={locked} value={selected?.id ?? ''} onChange={event => setSelected(relationships.data!.items.find(x => x.id === event.target.value) ?? null)}><option value="">Choose an agency relationship</option>{selected && !relationships.data.items.some(x => x.id === selected.id) && <option value={selected.id}>{selected.agencyName} · {selected.agencyReference}</option>}{relationships.data.items.map(row => <option key={row.id} value={row.id}>{row.agencyName} · {row.agencyReference} · {row.state}</option>)}</select></label></div>}
    <Paging total={relationships.data?.totalCount} previous={!locked && history.length > 1 ? () => setHistory(x => x.slice(0,-1)) : undefined} next={!locked && relationships.data?.nextCursor ? () => setHistory(x => [...x,relationships.data!.nextCursor!]) : undefined} />
  </Panel>{selected ? <RelationshipContacts key={selected.id} relationship={selected} canWrite={canWrite} canSupport={canSupport} onLock={setLocked} onSummary={onSummary} /> : <Panel title="Contacts"><EmptyState title="Choose an agency relationship">Contact details and marketing consent are recorded separately for each relationship.</EmptyState></Panel>}</>;
}
function RelationshipContacts({relationship,canWrite,canSupport,onLock,onSummary}:{relationship:Relationship;canWrite:boolean;canSupport:boolean;onLock:(locked:boolean)=>void;onSummary:(value:string)=>void}) {
  const [history,setHistory] = useState(['']); const [ended,setEnded] = useState(false); const [action,setAction] = useState<Action | null>(null); const [notice,setNotice] = useState('');
  const [supportBusy,setSupportBusy] = useState(false);const [contactRevision,setContactRevision] = useState(0);
  const cursor = history.at(-1)!; const base = `/api/v1/relationships/${relationship.id}/contacts`;
  const resource = useClientResource<Page<Contact>>(`${base}?pageSize=15&includeEnded=${ended}${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
  const primary = resource.data?.items.find(row => row.isPrimary);
  const summary = primary ? primary.fullName + ' · ' + relationship.agencyReference : !resource.data ? (resource.error ? 'Contact unavailable' : 'Loading contact…') : resource.data.nextCursor || cursor ? 'View contact list · ' + relationship.agencyReference : 'No active primary · ' + relationship.agencyReference;
  useEffect(() => onSummary(summary),[summary,onSummary]);
  function open(value:Action) {setNotice('');setAction(value);onLock(true);}
  function close() {setAction(null);onLock(false);}
  function saved() {close();setNotice('Contact saved.');setHistory(['']);resource.refresh();setContactRevision(x => x + 1);}
  return <>{notice && <div className="notice" role="status">{notice}</div>}
    {action && <ContactAction key={`${action.kind}:${action.id ?? ''}`} base={base} action={action} onSaved={saved} onClose={close} />}
    <Panel title="Contacts" note={`${relationship.agencyName} · ${relationship.agencyReference}`}>
      <div className="operations-toolbar">{canWrite && relationship.state === 'active' && <button className="button button-primary" disabled={!!action || supportBusy} onClick={() => open({kind:'create'})}>Add contact</button>}
        <label className="contact-check"><input type="checkbox" checked={ended} disabled={!!action || supportBusy} onChange={event => {setEnded(event.target.checked);setHistory(['']);}} />Include ended contacts</label>
      </div>
      {!resource.data ? <LoadFeedback error={resource.error} retry={resource.refresh} /> : !resource.data.items.length ? <EmptyState title="No contacts">Add the first contact for this relationship. The first active contact will be primary.</EmptyState> : <DataTable caption="Relationship contacts" columns={['Name','Role','Email','Telephone','Primary','Marketing consent','Actions']}>
        {resource.data.items.map(row => <tr key={row.id}><td><strong>{row.fullName}</strong>{row.endedAt && <div><Status>Ended {clientDate(row.endedAt)}</Status></div>}</td><td>{row.role}</td><td>{row.email ?? 'Not supplied'}</td><td>{row.telephone ?? 'Not supplied'}</td><td>{row.isPrimary ? <Status tone="info">Primary</Status> : '—'}</td><td>{row.marketingConsent.state === 'given' ? 'Given' : row.marketingConsent.state === 'withheld' ? 'Withheld' : 'Not asked'}{row.marketingConsent.state === 'given' && <div className="client-help">{[row.marketingConsent.email && 'Email',row.marketingConsent.telephone && 'Telephone'].filter(Boolean).join(' · ')}</div>}</td><td>{canWrite && !row.endedAt && relationship.state === 'active' ? <div className="operations-actions"><button className="button" disabled={!!action || supportBusy} onClick={() => open({kind:'update',id:row.id})}>Edit</button>{!row.isPrimary && <button className="button" disabled={!!action || supportBusy} onClick={() => open({kind:'make-primary',id:row.id})}>Make primary</button>}<button className="button" disabled={!!action || supportBusy} onClick={() => open({kind:'end',id:row.id})}>End</button></div> : row.endedAt ? 'History retained' : 'View only'}</td></tr>)}
      </DataTable>}
      <Paging total={resource.data?.totalCount} previous={!action && !supportBusy && history.length > 1 ? () => setHistory(x => x.slice(0,-1)) : undefined} next={!action && !supportBusy && resource.data?.nextCursor ? () => setHistory(x => [...x,resource.data!.nextCursor!]) : undefined} />
    </Panel><SupportFlags key={contactRevision} relationship={relationship} canSupport={canSupport} disabled={!!action} onLock={value => {setSupportBusy(value);onLock(value);}} /></>;
}
function ContactAction({base,action,onSaved,onClose}:{base:string;action:Action;onSaved:()=>void;onClose:()=>void}) {
  const url = action.kind === 'create' ? base.replace(/\/contacts$/,'') : `${base}/${action.id}`;
  const resource = useClientResource<Contact | Relationship>(url);
  const title = action.kind === 'create' ? 'Add a contact' : action.kind === 'update' ? 'Edit contact' : action.kind === 'end' ? 'End contact' : 'Change primary contact';
  return <Panel title={title}>{!resource.data ? <><LoadFeedback error={resource.error} retry={resource.refresh} /><div className="panel-footer"><button className="button" onClick={onClose}>Cancel</button></div></> : <ContactForm key={resource.etag} base={base} action={action} contact={action.kind === 'create' ? undefined : resource.data as Contact} etag={resource.etag!} onSaved={onSaved} onClose={onClose} onReload={resource.refresh} />}</Panel>;
}
function ContactForm({base,action,contact,etag,onSaved,onClose,onReload}:{base:string;action:Action;contact?:Contact;etag:string;onSaved:()=>void;onClose:()=>void;onReload:()=>void}) {
  const [busy,setBusy] = useState(false); const [error,setError] = useState(''); const [uncertain,setUncertain] = useState(false); const [stale,setStale] = useState(false);
  const [consent,setConsent] = useState(contact?.marketingConsent.state ?? 'not-asked'); const lock = useRef(false);
  const receipt = useRef<{key:string;body:string | undefined;etag:string} | null>(null);
  const editing = action.kind === 'create' || action.kind === 'update';
  async function save(event:FormEvent<HTMLFormElement>) {
    event.preventDefault();if(lock.current)return;
    setError('');
    try {
      if (!uncertain || !receipt.current) {
        const form = new FormData(event.currentTarget);
        const body = editing ? contactPayload(form,contact) : action.kind === 'end' ? {reason:String(form.get('reason') ?? '').trim()} : undefined;
        if (editing && 'marketingConsent' in body! && body.marketingConsent.state === 'given' && !body.marketingConsent.email && !body.marketingConsent.telephone) {setError('Choose at least one permitted marketing channel for Given consent.');return;}
        receipt.current = {key:crypto.randomUUID(),body:body ? JSON.stringify(body) : undefined,etag};
      }
      lock.current = true;setBusy(true);
      const retained = receipt.current!;
      await clientFetch<Contact>(base + (action.id ? `/${action.id}` : '') + (editing ? '' : `/${action.kind}`), {method:action.kind === 'update' ? 'PUT' : 'POST',
        headers:{'Content-Type':'application/json','X-CSRF-Token':await csrfToken(),'Idempotency-Key':retained.key,'If-Match':retained.etag},body:retained.body});
      receipt.current = null;setUncertain(false);onSaved();
    } catch(failure) {setError(contactError(failure));setUncertain(uncertainFailure(failure));setStale(failure instanceof ClientError && [409,412,428].includes(failure.status));}
    finally {lock.current = false;setBusy(false);}
  }
  return <form className="client-form" onSubmit={event => void save(event)}>
    <p className="client-help">{editing ? 'Save the declared details for this agency relationship. Required fields are marked *.' : action.kind === 'end' ? `End ${contact?.fullName}. Their contact history will be retained. If this is the primary contact and others remain, choose a replacement first.` : `Make ${contact?.fullName} the primary contact for this relationship. The existing primary will become an additional contact.`}</p>
    <fieldset disabled={busy || uncertain || stale}><legend className="sr-only">Contact details</legend>{editing ? <div className="client-field-grid">
      <label>Full name *<input name="fullName" required maxLength={200} defaultValue={contact?.fullName} /></label>
      <label>Role *<select name="role" required defaultValue={contact?.role ?? 'Director'}>{contact?.role && !contactRoles.includes(contact.role) && <option>{contact.role}</option>}{contactRoles.map(role => <option key={role}>{role}</option>)}</select></label>
      <label>Email<input name="email" type="email" maxLength={254} defaultValue={contact?.email} /></label>
      <label>Telephone<input name="telephone" type="tel" maxLength={50} defaultValue={contact?.telephone} /></label>
      <label>Primary contact<select name="primary" defaultValue={contact?.isPrimary ? 'yes' : 'no'}><option value="no">No</option><option value="yes">Yes</option></select></label>
      <label>Marketing consent *<select name="consent" value={consent} onChange={event => setConsent(event.target.value as typeof consent)}><option value="not-asked">Not asked</option><option value="withheld">Withheld</option><option value="given">Given</option></select></label>
      <div><span>Permitted marketing channels</span><label className="contact-check"><input name="consentEmail" type="checkbox" disabled={consent !== 'given'} defaultChecked={contact?.marketingConsent.email} />Email</label><label className="contact-check"><input name="consentTelephone" type="checkbox" disabled={consent !== 'given'} defaultChecked={contact?.marketingConsent.telephone} />Telephone</label></div>
      <label>Consent evidence time (UTC) *<input name="recordedAt" type="datetime-local" step="1" required defaultValue={(contact?.marketingConsent.recordedAt ?? new Date().toISOString()).slice(0,19)} /></label>
      <label>Consent source *<input name="source" maxLength={200} required defaultValue={contact?.marketingConsent.source ?? 'Back office contact record'} /></label>
    </div> : action.kind === 'end' ? <label className="contact-reason">Reason for ending *<textarea name="reason" required maxLength={1000} rows={3} /></label> : null}</fieldset>
    {error && <div className="error-message" role="alert">{error}</div>}{uncertain && <p className="client-help" role="status">Your request is retained. Retry the same action to confirm its result before making further changes.</p>}
    <div className="operations-actions"><button className="button button-primary" type="submit" disabled={busy || stale}>{busy ? 'Saving…' : uncertain ? 'Retry same save' : action.kind === 'end' ? 'Confirm end contact' : action.kind === 'make-primary' ? 'Confirm primary contact' : action.kind === 'create' ? 'Add contact' : 'Save contact'}</button><button className="button" type="button" disabled={busy || uncertain} onClick={onClose}>Cancel</button>{stale && <button className="button" type="button" onClick={onReload}>Reload saved contact</button>}</div>
  </form>;
}
