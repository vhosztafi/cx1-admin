'use client';
import {useRef,useState,type FormEvent} from 'react';
import {DataTable,EmptyState,Panel,Status} from '../primitives';
import {ClientError,clientFetch,clientDate,uncertainFailure,type Page,type Relationship} from '../../lib/clients';
import type {Contact} from '../../lib/contacts';
import {flagCategories,flagConsents,flagPayload,flagTypes,type FlagHistory,type SafeInstruction,type SupportFlag} from '../../lib/support-flags';
import {csrfToken} from '../../lib/auth';
import {LoadFeedback,Paging,useClientResource} from './shared';

type Action = {kind:'create'|'edit'|'review'|'end'|'history';id?:string};
export function SupportFlags({relationship,canSupport,disabled,onLock}:{relationship:Relationship;canSupport:boolean;disabled:boolean;onLock:(value:boolean)=>void}) {
  const [preview,setPreview] = useState(false); const [revision,setRevision] = useState(0);
  return <>{canSupport && <PersonFlags relationship={relationship} disabled={disabled} onLock={onLock} onChanged={() => setRevision(x => x + 1)} />}
    <Panel title="Agency-safe instructions" note={`${relationship.agencyName} · only instructions explicitly shared with this relationship`}>
      <div className="panel-footer"><button className="button" disabled={disabled} onClick={() => setPreview(x => !x)}>{preview ? 'Close agency preview' : 'Preview agency instructions'}</button></div>
      {preview && <SafePreview key={relationship.id} relationshipId={relationship.id} revision={revision} />}
    </Panel></>;
}
function SafePreview({relationshipId,revision}:{relationshipId:string;revision:number}) {
  const [history,setHistory] = useState(['']);const cursor = history.at(-1)!;
  const resource = useClientResource<Page<SafeInstruction>>(`/api/v1/relationships/${relationshipId}/support-instructions/preview?pageSize=15${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`,revision);
  return <>{!resource.data ? <LoadFeedback error={resource.error} retry={resource.refresh} /> : !resource.data.items.length ? <EmptyState title="No instructions shared">There are no current instructions shared with this agency relationship.</EmptyState> : <DataTable caption="Agency-safe instructions" columns={['Instruction','Review date']}>
    {resource.data.items.map(row => <tr key={row.id}><td className="support-wording">{row.instruction}</td><td>{row.reviewOn}</td></tr>)}
  </DataTable>}<div className="panel-footer operations-actions"><button className="button" onClick={resource.refresh}>Refresh preview</button><button className="button" disabled={history.length < 2} onClick={() => setHistory(x => x.slice(0,-1))}>Previous instructions</button><button className="button" disabled={!resource.data?.nextCursor} onClick={() => setHistory(x => [...x,resource.data!.nextCursor!])}>Next instructions</button></div></>;
}
function PersonFlags({relationship,disabled,onLock,onChanged}:{relationship:Relationship;disabled:boolean;onLock:(value:boolean)=>void;onChanged:()=>void}) {
  const [person,setPerson] = useState<Contact | null>(null);const [locked,setLocked] = useState(false);const [history,setHistory] = useState(['']);const cursor = history.at(-1)!;
  const contacts = useClientResource<Page<Contact>>(`/api/v1/relationships/${relationship.id}/contacts?includeEnded=true&pageSize=15${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
  function lock(value:boolean) {setLocked(value);onLock(value);}
  return <><Panel title="Customer flags" note="Held against the person · internal support details stay out of rating and insurer reports">
    {!contacts.data ? <LoadFeedback error={contacts.error} retry={contacts.refresh} /> : <div className="operations-toolbar"><label>Contact for support flags<select disabled={locked || disabled} value={person?.id ?? ''} onChange={event => setPerson(contacts.data!.items.find(x => x.id === event.target.value) ?? null)}><option value="">Choose a contact</option>{person && !contacts.data.items.some(x => x.id === person.id) && <option value={person.id}>{person.fullName}</option>}{contacts.data.items.map(row => <option key={row.id} value={row.id}>{row.fullName}{row.endedAt ? ' · Ended contact' : ''}</option>)}</select></label></div>}
    <Paging total={contacts.data?.totalCount} previous={!locked && !disabled && history.length > 1 ? () => setHistory(x => x.slice(0,-1)) : undefined} next={!locked && !disabled && contacts.data?.nextCursor ? () => setHistory(x => [...x,contacts.data!.nextCursor!]) : undefined} />
  </Panel>{person && <FlagList key={person.id} relationship={relationship} contact={person} disabled={disabled} onLock={lock} onChanged={onChanged} />}</>;
}
function FlagList({relationship,contact,disabled,onLock,onChanged}:{relationship:Relationship;contact:Contact;disabled:boolean;onLock:(value:boolean)=>void;onChanged:()=>void}) {
  const [history,setHistory] = useState(['']);const cursor = history.at(-1)!;const [action,setAction] = useState<Action | null>(null);const [notice,setNotice] = useState('');
  const base = `/api/v1/relationships/${relationship.id}/people/${contact.personId}/flags`;
  const resource = useClientResource<Page<SupportFlag>>(`${base}?pageSize=15${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
  function open(value:Action) {setNotice('');setAction(value);onLock(true);}
  function close() {setAction(null);onLock(false);}
  function saved() {close();setNotice('Support record saved.');resource.refresh();setHistory(['']);onChanged();}
  return <>{notice && <div className="notice" role="status">{notice}</div>}{action && <FlagAction action={action} relationship={relationship} contact={contact} base={base} onClose={close} onSaved={saved} />}
    <Panel title={`Flags for ${contact.fullName}`} note="Internal category, recording basis and history are restricted">
      <div className="panel-footer"><button className="button button-primary" disabled={disabled || !!action || !!contact.endedAt || relationship.state !== 'active'} onClick={() => open({kind:'create'})}>Add flag</button></div>
      {!resource.data ? <LoadFeedback error={resource.error} retry={resource.refresh} /> : !resource.data.items.length ? <EmptyState title="No customer flags">Record functional support needs only when a valid consent or authority basis is available.</EmptyState> : <DataTable caption="Customer flags" columns={['Contact','Type','Category','Support need (internal)','Shown to agency','Review','Status','Action']}>
        {resource.data.items.map(row => <tr key={row.id}><td>{contact.fullName}</td><td>{flagTypes[row.typeCode]}</td><td>{flagCategories[row.internalCategory]}</td><td className="support-wording">{row.internalInstruction}</td><td className="support-wording">{row.visibleRelationshipIds.length ? <>{row.agencyInstruction}<div className="client-help">Shared with {row.visibleRelationshipIds.length} selected relationship(s)</div></> : 'Internal only'}</td><td>{row.reviewOn}</td><td><Status tone={row.endedAt ? 'muted' : 'warning'}>{row.endedAt ? 'Ended' : 'Active'}</Status></td><td><div className="operations-actions">{!row.endedAt && <><button className="button" disabled={disabled || !!action} onClick={() => open({kind:'edit',id:row.id})}>Amend</button><button className="button" disabled={disabled || !!action} onClick={() => open({kind:'review',id:row.id})}>Review</button><button className="button" disabled={disabled || !!action} onClick={() => open({kind:'end',id:row.id})}>End flag</button></>}<button className="button" disabled={disabled || !!action} onClick={() => open({kind:'history',id:row.id})}>History</button></div></td></tr>)}
      </DataTable>}
      <Paging total={resource.data?.totalCount} previous={!action && history.length > 1 ? () => setHistory(x => x.slice(0,-1)) : undefined} next={!action && resource.data?.nextCursor ? () => setHistory(x => [...x,resource.data!.nextCursor!]) : undefined} />
    </Panel></>;
}
function FlagAction({action,relationship,contact,base,onClose,onSaved}:{action:Action;relationship:Relationship;contact:Contact;base:string;onClose:()=>void;onSaved:()=>void}) {
  const resource = useClientResource<SupportFlag | Relationship>(action.kind === 'create' ? `/api/v1/relationships/${relationship.id}` : `/api/v1/flags/${action.id}`);
  const title = action.kind === 'create' ? 'Add a customer flag' : action.kind === 'end' ? 'End customer flag' : action.kind === 'review' ? 'Review customer flag' : action.kind === 'history' ? 'Support flag history' : 'Amend customer flag';
  return <Panel title={title}>{action.kind === 'history' ? <FlagHistoryView flagId={action.id!} onClose={onClose} /> : !resource.data ? <><LoadFeedback error={resource.error} retry={resource.refresh} /><div className="panel-footer"><button className="button" onClick={onClose}>Cancel flag</button></div></> : <FlagForm key={resource.etag} action={action} base={base} clientId={relationship.clientId} etag={resource.etag!} flag={action.kind === 'create' ? undefined : resource.data as SupportFlag} contactName={contact.fullName} onClose={onClose} onSaved={onSaved} onReload={resource.refresh} />}</Panel>;
}
function FlagHistoryView({flagId,onClose}:{flagId:string;onClose:()=>void}) {
  const [history,setHistory] = useState(['']);const cursor = history.at(-1)!;
  const resource = useClientResource<Page<FlagHistory>>(`/api/v1/flags/${flagId}/history?pageSize=10${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
  return <>{!resource.data ? <LoadFeedback error={resource.error} retry={resource.refresh} /> : <DataTable caption="Restricted support history" columns={['When','By','Action','Reason','Saved version']}>
    {resource.data.items.map(row => <tr key={row.id}><td>{clientDate(row.occurredAt,true)}</td><td>{row.actorLabel}</td><td>{row.action}</td><td className="support-wording">{row.reason}</td><td><details><summary>View saved version</summary><dl className="support-snapshot"><dt>Type / category</dt><dd>{flagTypes[row.snapshot.typeCode]} · {flagCategories[row.snapshot.internalCategory]}</dd><dt>Internal instruction</dt><dd>{row.snapshot.internalInstruction}</dd><dt>Agency wording</dt><dd>{row.snapshot.agencyInstruction ?? 'Not supplied'}</dd><dt>Sharing</dt><dd>{row.snapshot.visibleRelationshipIds.length} explicit relationship(s)</dd><dt>Recording basis</dt><dd>{flagConsents[row.snapshot.consentBasis]}</dd><dt>Review date</dt><dd>{row.snapshot.reviewOn}</dd></dl></details></td></tr>)}
  </DataTable>}<Paging total={resource.data?.totalCount} previous={history.length > 1 ? () => setHistory(x => x.slice(0,-1)) : undefined} next={resource.data?.nextCursor ? () => setHistory(x => [...x,resource.data!.nextCursor!]) : undefined} /><div className="panel-footer"><button className="button" onClick={onClose}>Close flag history</button></div></>;
}
function Sharing({clientId,selected,onChange,disabled}:{clientId:string;selected:string[];onChange:(ids:string[])=>void;disabled:boolean}) {
  const [history,setHistory] = useState(['']);const cursor = history.at(-1)!;
  const resource = useClientResource<Page<Relationship>>(`/api/v1/clients/${clientId}/relationships?pageSize=10${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
  return <div className="support-sharing"><h3>Share functional wording</h3><p className="client-help">Select only relationships where this person is an active contact. Leave all unchecked for internal only.</p>
    {!resource.data ? <LoadFeedback error={resource.error} retry={resource.refresh} /> : resource.data.items.map(row => <label className="contact-check" key={row.id}><input type="checkbox" checked={selected.includes(row.id)} disabled={disabled || (row.state !== 'active' && !selected.includes(row.id))} onChange={event => onChange(event.target.checked ? [...selected,row.id] : selected.filter(id => id !== row.id))} />{row.agencyName} · {row.agencyReference} · {row.state}</label>)}
    <div className="operations-actions"><span>{selected.length} selected</span><button className="button" type="button" disabled={disabled || history.length < 2} onClick={() => setHistory(x => x.slice(0,-1))}>Previous sharing options</button><button className="button" type="button" disabled={disabled || !resource.data?.nextCursor} onClick={() => setHistory(x => [...x,resource.data!.nextCursor!])}>Next sharing options</button><button className="button" type="button" disabled={disabled} onClick={() => onChange([])}>Clear sharing</button></div>
  </div>;
}
function FlagForm({action,base,clientId,etag,flag,contactName,onClose,onSaved,onReload}:{action:Action;base:string;clientId:string;etag:string;flag?:SupportFlag;contactName:string;onClose:()=>void;onSaved:()=>void;onReload:()=>void}) {
  const [consent,setConsent] = useState(flag?.consentBasis ?? 'verbal-consent');const [instruction,setInstruction] = useState(flag?.internalInstruction ?? '');const [agency,setAgency] = useState(flag?.agencyInstruction ?? '');const [reason,setReason] = useState('');const [category,setCategory] = useState(flag?.internalCategory ?? 'health');const [grants,setGrants] = useState(flag?.visibleRelationshipIds ?? []);
  const [busy,setBusy] = useState(false);const [uncertain,setUncertain] = useState(false);const [stale,setStale] = useState(false);const [error,setError] = useState('');const lock = useRef(false);
  const receipt = useRef<{body:string;key:string;etag:string} | null>(null);const declined = consent === 'declined';const end = action.kind === 'end';
  function changeConsent(value:string) {setConsent(value);if(value === 'declined'){setInstruction('');setAgency('');setReason('');setCategory('');setGrants([]);receipt.current = null;setError('');}}
  async function save(event:FormEvent<HTMLFormElement>) {
    event.preventDefault();if(lock.current || (!end && declined))return;setError('');
    try {
      if(!uncertain || !receipt.current){const form = new FormData(event.currentTarget);receipt.current = {body:JSON.stringify(end ? {reason:String(form.get('reason') ?? '').trim()} : flagPayload(form,grants)),key:crypto.randomUUID(),etag};}
      lock.current = true;setBusy(true);const retained = receipt.current!;
      await clientFetch<{id:string}>(action.kind === 'create' ? base : `/api/v1/flags/${action.id}${end ? '/end' : ''}`,{method:action.kind === 'create' || end ? 'POST' : 'PUT',headers:{'Content-Type':'application/json','X-CSRF-Token':await csrfToken(),'Idempotency-Key':retained.key,'If-Match':retained.etag},body:retained.body});
      receipt.current = null;setUncertain(false);onSaved();
    }catch(failure){setUncertain(uncertainFailure(failure));setStale(failure instanceof ClientError && [409,412,428].includes(failure.status));setError(failure instanceof ClientError && failure.status === 404 ? 'The support record or selected active contact relationship is unavailable. Check the sharing selection.' : failure instanceof ClientError && [409,412,428].includes(failure.status) ? 'This support record has changed or ended. Reload the saved record before continuing.' : failure instanceof Error ? failure.message : 'The save could not be confirmed.');}
    finally{lock.current = false;setBusy(false);}
  }
  return <form className="client-form support-form" onSubmit={event => void save(event)}><p className="client-help">{contactName} · {end ? 'Resolve this flag with a reason. Restricted history will be retained.' : 'Describe the action staff should take, not a diagnosis. Internal detail never belongs in agency wording.'}</p>
    <fieldset disabled={busy || uncertain || stale}><legend className="sr-only">Support flag</legend>{!end && <label className="support-consent">Consent to record *<select name="consentBasis" value={consent} onChange={event => changeConsent(event.target.value)}>{Object.entries(flagConsents).map(([key,label]) => <option value={key} key={key}>{label}</option>)}</select></label>}
      {!end && declined && <div className="notice" role="status">Consent declined. Draft sensitive details have been cleared. No flag has been saved. Existing history, if any, is retained; use End flag to resolve an existing instruction.</div>}
      {!end && <fieldset disabled={declined}><legend className="sr-only">Sensitive instruction details</legend><div className="client-field-grid">
        <label>Flag type *<select name="typeCode" defaultValue={flag?.typeCode ?? 'vulnerability'}>{Object.entries(flagTypes).map(([key,label]) => <option key={key} value={key}>{label}</option>)}</select></label>
        <label>Category (internal) *<select name="internalCategory" required value={category} onChange={event => setCategory(event.target.value)}><option value="">Choose category</option>{Object.entries(flagCategories).map(([key,label]) => <option key={key} value={key}>{label}</option>)}</select></label>
        <label>Support need (internal) *<textarea name="internalInstruction" required maxLength={2000} rows={4} value={instruction} onChange={event => setInstruction(event.target.value)} /></label>
        <label>Shown to the agency<textarea name="agencyInstruction" required={grants.length > 0} maxLength={1000} rows={4} value={agency} onChange={event => setAgency(event.target.value)} /></label>
        <label>Review date *<input name="reviewOn" type="date" required defaultValue={flag?.reviewOn ?? ''} /></label>
      </div><Sharing clientId={clientId} selected={grants} onChange={setGrants} disabled={busy || uncertain || stale || declined} /></fieldset>}
      <label className="contact-reason">{end ? 'Reason for ending *' : 'Reason for recording or change *'}<textarea name="reason" required disabled={!end && declined} maxLength={1000} rows={3} value={reason} onChange={event => setReason(event.target.value)} /></label>
    </fieldset>{error && <div className="error-message" role="alert">{error}</div>}{uncertain && <p className="client-help" role="status">The request is retained. Retry the same save to confirm the result.</p>}
    <div className="operations-actions"><button className="button button-primary" type="submit" disabled={busy || stale || (!end && declined)}>{busy ? 'Saving…' : uncertain ? 'Retry same flag save' : end ? 'Confirm end flag' : action.kind === 'create' ? 'Save flag' : action.kind === 'review' ? 'Save review' : 'Save amendment'}</button><button className="button" type="button" disabled={busy || uncertain} onClick={onClose}>Cancel flag</button>{stale && <button className="button" type="button" onClick={onReload}>Reload saved flag</button>}</div>
  </form>;
}
