'use client';
import { useRef, useState, type FormEvent } from 'react';
import { csrfToken } from '../../lib/auth';
import { ClientError, clientFetch, uncertainFailure, type Agency, type Page, type Relationship } from '../../lib/clients';
import { LoadFeedback, useClientResource } from './shared';

export function AddRelationship({ clientId, etag, onSaved, onReload, onClose }: { clientId: string; etag?: string | null; onSaved: () => void; onReload: () => void; onClose: () => void }) {
  const [history,setHistory] = useState(['']); const cursor = history.at(-1)!;
  const agencies = useClientResource<Page<Agency>>(`/api/v1/relationship-agencies?pageSize=15${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
  const [agencyId,setAgencyId] = useState(''); const [busy,setBusy] = useState(false); const locked = useRef(false);
  const [error,setError] = useState(''); const [uncertain,setUncertain] = useState(false); const [stale,setStale] = useState(false);
  const receipt = useRef<{ agencyId: string; key: string } | null>(null);
  async function save(event: FormEvent) {
    event.preventDefault(); if (locked.current || !agencyId) return;
    if (receipt.current?.agencyId !== agencyId) receipt.current = { agencyId, key: crypto.randomUUID() };
    locked.current = true; setBusy(true); setError('');
    try {
      await clientFetch<Relationship>(`/api/v1/clients/${clientId}/relationships`, { method: 'POST', headers: {
        'Content-Type':'application/json','X-CSRF-Token':await csrfToken(),'Idempotency-Key':receipt.current.key,...(etag && {'If-Match':etag}),
      }, body: JSON.stringify({agencyId:receipt.current.agencyId}) });
      receipt.current = null; onSaved();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'The result could not be confirmed. Retry the same action.');
      setUncertain(uncertainFailure(failure)); setStale(failure instanceof ClientError && [412,428].includes(failure.status));
    } finally {locked.current = false; setBusy(false);}
  }
  return <form className="client-form client-relationship-form" onSubmit={event => void save(event)}>
    <p className="client-help">Choose the agency for this business relationship. Draft agencies still need to complete onboarding.</p>
    {!agencies.data ? <LoadFeedback error={agencies.error} retry={() => {setHistory(['']); agencies.refresh();}} /> : <fieldset disabled={busy || uncertain}><legend className="sr-only">Choose agency</legend>
      <div className="field"><label htmlFor="relationship-agency">Agency *</label><select id="relationship-agency" required value={agencyId} onChange={event => setAgencyId(event.target.value)}><option value="">Select an agency</option>
        {agencies.data.items.map(agency => <option key={agency.id} value={agency.id} disabled={agency.state === 'suspended' || agency.state === 'abandoned'}>{agency.legalName} · {agency.reference} · {agency.state}</option>)}
      </select></div>
      <div className="operations-actions client-agency-paging"><span>{agencies.data.totalCount ?? 'Available'} agencies</span><button className="button" type="button" disabled={history.length < 2} onClick={() => {setAgencyId(''); setHistory(x => x.slice(0,-1));}}>Previous agencies</button><button className="button" type="button" disabled={!agencies.data.nextCursor} onClick={() => {setAgencyId(''); setHistory(x => [...x,agencies.data!.nextCursor!]);}}>Next agencies</button></div>
    </fieldset>}
    {error && <div className="error-message" role="alert">{error}</div>}
    {uncertain && <p className="client-help" role="status">The selected agency is retained. Retry this same link to confirm the result.</p>}
    <div className="operations-actions"><button className="button button-primary" type="submit" disabled={busy || stale || !agencyId}>{busy ? 'Linking agency…' : uncertain ? 'Retry same link' : 'Link agency'}</button><button className="button" type="button" disabled={busy || uncertain} onClick={onClose}>Cancel relationship</button>{stale && <button className="button" type="button" onClick={onReload}>Reload client before linking</button>}</div>
  </form>;
}
