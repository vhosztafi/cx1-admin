'use client';
import { useRef, useState, type FormEvent } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { csrfToken } from '../../lib/auth';
import { ClientError, clientFetch, entityTypes, identityPayload, uncertainFailure, type Client, type ClientWrite } from '../../lib/clients';

export function IdentityForm({ client, etag, onSaved, onCancel, onReload }: { client?: Client; etag?: string | null; onSaved?: (client: Client) => void; onCancel?: () => void; onReload?: () => void }) {
  const router = useRouter(); const [busy, setBusy] = useState(false); const lock = useRef(false);
  const [error, setError] = useState(''); const [uncertain, setUncertain] = useState(false); const [stale, setStale] = useState(false);
  const receipt = useRef<{ body: ClientWrite; intent: string; key: string } | null>(null);
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (lock.current) return;
    const body = uncertain && receipt.current ? receipt.current.body : identityPayload(new FormData(event.currentTarget));
    const intent = JSON.stringify(body);
    if (receipt.current?.intent !== intent) receipt.current = { body, intent, key: crypto.randomUUID() };
    lock.current = true; setBusy(true); setError('');
    try {
      const { data } = await clientFetch<Client>(client ? `/api/v1/clients/${client.id}` : '/api/v1/clients', {
        method: client ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': await csrfToken(), 'Idempotency-Key': receipt.current.key, ...(etag && { 'If-Match': etag }) }, body: JSON.stringify(body),
      });
      receipt.current = null; setUncertain(false);
      if (onSaved) onSaved(data); else router.push(`/clients/${data.id}`);
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'The save could not be confirmed. Retry the same action.');
      setUncertain(uncertainFailure(failure)); setStale(failure instanceof ClientError && [412, 428].includes(failure.status));
    } finally { lock.current = false; setBusy(false); }
  }
  const fields = [['legalName','Legal business name',200,true],['companyNumber','Company number',30,false],['line1','Address line 1',200,true],['line2','Address line 2',200,false],['town','Town or city',100,true],['county','County',100,false],['postcode','Postcode',20,true]] as const;
  function initial(key: string) { return key === 'legalName' ? client?.legalName : key === 'companyNumber' ? client?.companyNumber : client?.address[key as keyof Client['address']]; }
  return <form className="client-form" onSubmit={event => void save(event)}>
    <p className="client-help">Enter the business identity and correspondence address. Required fields are marked *.</p>
    <fieldset disabled={busy || uncertain}><legend className="sr-only">Client identity</legend><div className="client-field-grid">
      {fields.map(([key,label,max,required]) => <div className="field" key={key}><label htmlFor={`client-${key}`}>{label}{required ? ' *' : ''}</label><input id={`client-${key}`} name={key} required={required} maxLength={max} defaultValue={initial(key) ?? ''} autoComplete="off" /></div>)}
      <div className="field"><label htmlFor="client-entityType">Entity type *</label><select id="client-entityType" name="entityType" defaultValue={client?.entityType ?? 'limited-company'}>{Object.entries(entityTypes).map(([key,label]) => <option value={key} key={key}>{label}</option>)}</select></div>
      <div className="field"><label htmlFor="client-country">Country</label><input id="client-country" value="United Kingdom" readOnly /></div>
    </div></fieldset>
    {error && <div className="error-message" role="alert">{error}</div>}
    {uncertain && <p className="client-help" role="status">Your entries are retained. Retry this save to confirm its result before making further changes.</p>}
    <div className="operations-actions"><button className="button button-primary" disabled={busy || stale} type="submit">{busy ? 'Saving…' : uncertain ? 'Retry same save' : client ? 'Save identity' : 'Create client'}</button>
      {onCancel ? <button className="button" type="button" disabled={busy || uncertain} onClick={onCancel}>Cancel</button> : <Link className="button" href="/clients">Back to clients</Link>}
      {stale && onReload && <button className="button" type="button" onClick={onReload}>Replace my edits with saved identity</button>}
    </div>
  </form>;
}
