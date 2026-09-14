'use client';
import { useRef, useState } from 'react';
import { agencyFetch, AgencyError } from '../../lib/agencies';
import { csrfToken } from '../../lib/auth';
export function AbandonAgency({id,etag,disabled,onSaved}: {id: string; etag: string; disabled?: boolean; onSaved: () => void}) {
  const dialog = useRef<HTMLDialogElement>(null); const [reason,setReason] = useState(''); const [busy,setBusy] = useState(false); const [error,setError] = useState(''); const [uncertain,setUncertain] = useState(false);
  const command = useRef<{key: string; reason: string; etag: string} | null>(null); const lock = useRef(false);
  async function abandon() {
    if(lock.current) return; if(!uncertain) command.current = {key:crypto.randomUUID(),reason:reason.trim(),etag};
    lock.current = true;setBusy(true);setError('');
    try { const pending = command.current!;await agencyFetch(`/api/v1/agencies/${id}/abandon`,{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-Token':await csrfToken(),'Idempotency-Key':pending.key,'If-Match':pending.etag},body:JSON.stringify({reason:pending.reason})});setUncertain(false);dialog.current?.close();onSaved(); }
    catch(failure) {setError(failure instanceof Error ? failure.message : 'Unable to confirm abandonment.');setUncertain(!(failure instanceof AgencyError) || failure.status >= 500);}
    finally {lock.current = false;setBusy(false);}
  }
  return <><button className="button match-danger" disabled={disabled} onClick={() => {setReason('');setError('');dialog.current?.showModal();}}>Abandon draft</button><dialog className="agency-dialog" ref={dialog} aria-labelledby={`abandon-${id}`} onCancel={event => {if(busy || uncertain) event.preventDefault();}}><form onSubmit={event => {event.preventDefault();void abandon();}}><h2 id={`abandon-${id}`}>Abandon agency draft</h2><p>The agency and its saved history will be retained. Onboarding will close.</p><label>Reason<textarea autoFocus required maxLength={1000} value={reason} disabled={busy || uncertain} onChange={event => setReason(event.target.value)} /></label>{error && <div className="error-message" role="alert">{error}</div>}<div className="operations-actions"><button className="button match-danger" disabled={busy || !reason.trim()} type="submit">{busy ? 'Saving…' : uncertain ? 'Retry same action' : 'Abandon draft'}</button><button className="button" type="button" disabled={busy || uncertain} onClick={() => dialog.current?.close()}>Keep draft</button></div></form></dialog></>;
}
