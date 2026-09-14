'use client';
import { useEffect, useRef, useState } from 'react';
import { Panel, DataTable } from '../primitives';
import { csrfToken } from '../../lib/auth';
import { agencyFetch, AgencyError, type AgencyDraft } from '../../lib/agencies';
import { attestationLabels, checkLabels, evidenceLabel, uploadContentType } from '../../lib/agency-evidence';
import type { Page } from '../../lib/clients';
import { LoadFeedback, Paging, useAgencyResource } from './shared';

type EvidenceFile = {id: string; fileName: string; contentType: string; byteLength: number; screeningState: string};
type Evidence = {id: string; kind: string; state: string; fileId?: string; recordedAt: string; resultCode: string};
type Props = {id: string; etag: string; stage: number; disabled: boolean; validation: AgencyDraft['validation']; onGuardChange: (guard: boolean) => void; onSaved: (etag: string,validation: AgencyDraft['validation']) => void; onStale: () => void};
export function AgencyEvidencePanel({id,etag,stage,disabled,validation,onGuardChange,onSaved,onStale}: Props) {
  const [fileCursors,setFileCursors] = useState<string[]>([]);
  const files = useAgencyResource<Page<EvidenceFile>>(`/api/v1/agencies/${id}/evidence-files?pageSize=25${fileCursors.at(-1) ? `&cursor=${encodeURIComponent(fileCursors.at(-1)!)}` : ''}`);
  const [cursor,setCursor] = useState<string[]>([]); const history = useAgencyResource<Page<Evidence>>(`/api/v1/agencies/${id}/evidence?pageSize=10${cursor.at(-1) ? `&cursor=${encodeURIComponent(cursor.at(-1)!)}` : ''}`);
  const [file,setFile] = useState<File>(); const input = useRef<HTMLInputElement>(null);
  const [kind,setKind] = useState('toba'); const [fileId,setFileId] = useState(''); const [notes,setNotes] = useState(''); const [expiresOn,setExpiresOn] = useState('');
  const [busy,setBusy] = useState(false); const [uncertain,setUncertain] = useState(false); const [stale,setStale] = useState(false); const [error,setError] = useState(''); const [notice,setNotice] = useState('');
  const lock = useRef(false); const command = useRef<{suffix: string; body: BodyInit; json: boolean; key: string; etag: string} | null>(null);
  const guard = !!file || !!fileId || !!notes || !!expiresOn || busy || uncertain;
  useEffect(() => {onGuardChange(guard);},[guard,onGuardChange]);
  useEffect(() => () => onGuardChange(false),[onGuardChange]);
  function clear() {setFile(undefined);if(input.current)input.current.value='';setFileId('');setNotes('');setExpiresOn('');setError('');}
  async function execute(suffix?: string,body?: BodyInit,json = true) {
    if(lock.current || stale) return;
    if(!uncertain) {if(disabled || !suffix || body === undefined)return;command.current={suffix,body,json,key:crypto.randomUUID(),etag};}
    const pending = command.current!;lock.current=true;setBusy(true);setError('');setNotice('');
    try {
      const result = await agencyFetch<{id: string}>(`/api/v1/agencies/${id}/${pending.suffix}`,{method:'POST',headers:{'X-CSRF-Token':await csrfToken(),'If-Match':pending.etag,'Idempotency-Key':pending.key,...(pending.json ? {'Content-Type':'application/json'} : {})},body:pending.body});
      const fresh = await agencyFetch<AgencyDraft>(`/api/v1/agencies/${id}`);
      if(!result.etag || result.etag !== fresh.etag) throw new AgencyError(412);
      onSaved(result.etag,fresh.data.validation);command.current=null;setUncertain(false);clear();setFileCursors([]);setCursor([]);files.refresh();history.refresh();setNotice('Evidence operation saved. The current result is shown below.');
    } catch(failure) {setError(failure instanceof Error ? failure.message : 'The evidence operation could not be confirmed.');const conflict=failure instanceof AgencyError && [409,412,428].includes(failure.status);setStale(conflict);if(conflict)onStale();setUncertain(!(failure instanceof AgencyError) || failure.status >= 500);}
    finally {lock.current=false;setBusy(false);}
  }
  function upload() {
    try {if(!file)return;const type=uploadContentType(file);const body=new FormData();body.append('file',new Blob([file],{type}),file.name);body.append('fileName',file.name);body.append('contentType',type);void execute('evidence-files',body,false);}
    catch(failure){setError(failure instanceof Error?failure.message:'Check the evidence file.');}
  }
  const frozen = disabled || busy || uncertain || stale;
  const checks = Object.entries(checkLabels).filter(([key]) => stage === 1 ? key === 'fca' : key !== 'fca');
  return <Panel title={stage === 1 ? 'FCA register check' : 'Compliance evidence'} note="Fictional demo checks and recorded staff attestations; no external provider is contacted.">
    {disabled && <p className="notice">Save the agency answers before checking or recording evidence.</p>}
    <div className="agency-evidence-body client-form"><div className="agency-check-grid">{checks.map(([key,label]) => {const result=validation.items.find(x=>x.code===`evidence-${key}`);return <section className="agency-check" key={key}><h3>{label}</h3><strong>{evidenceLabel(result?.state,disabled)}</strong><p>{result?.message ?? 'No check recorded.'}</p><button type="button" className="button" disabled={frozen || !!file || !!fileId || !!notes || !!expiresOn} onClick={() => void execute('checks',JSON.stringify({kind:key}))}>Run demo {label.toLowerCase()} check</button></section>;})}</div>
    {stage === 4 && <><h3>Upload supporting evidence</h3><p className="client-help">PDF, PNG, JPEG or plain text, up to 10 MiB. Uploading a file does not verify it. Screening is a demo check.</p>
      <div className="operations-actions"><label>Evidence file<input ref={input} type="file" accept=".pdf,.png,.jpg,.jpeg,.txt" disabled={frozen || !!fileId || !!notes || !!expiresOn} onChange={event => {setFile(event.target.files?.[0]);setNotice('');}} /></label><button type="button" className="button" disabled={frozen || !file} onClick={upload}>Upload evidence file</button></div>
      <h3>Record a staff attestation</h3><p className="client-help">Review the uploaded document and explain what it evidences. Current saved declarations and demo thresholds are checked by the server.</p>
      <fieldset disabled={frozen || !!file}><legend className="sr-only">Evidence attestation</legend><div className="client-field-grid"><div className="field"><label htmlFor="evidence-kind">Evidence type</label><select id="evidence-kind" value={kind} onChange={event => setKind(event.target.value)}>{Object.entries(attestationLabels).map(([key,label])=><option key={key} value={key}>{label}</option>)}</select></div>
      <div className="field"><label htmlFor="evidence-document">Uploaded document</label><select id="evidence-document" value={fileId} onChange={event=>{setFileId(event.target.value);setNotice('');}}><option value="">Choose a document</option>{fileId && !files.data?.items.some(x=>x.id===fileId) && <option value={fileId}>Previously selected document</option>}{files.data?.items.filter(x=>x.screeningState==='demo-cleared').map(x=><option key={x.id} value={x.id}>{x.fileName}</option>)}</select></div><div className="field"><label htmlFor="evidence-expiry">Evidence expiry date</label><input id="evidence-expiry" type="date" value={expiresOn} onChange={event=>setExpiresOn(event.target.value)} /></div><div className="field"><label htmlFor="evidence-notes">Attestation notes</label><textarea id="evidence-notes" maxLength={1000} value={notes} onChange={event=>{setNotes(event.target.value);setNotice('');}} /></div></div><p className="client-help">For professional indemnity, enter the exact expiry date saved in the agency declarations.</p><button type="button" className="button button-primary" disabled={!fileId || !notes.trim()} onClick={()=>void execute('evidence',JSON.stringify({kind,fileId,notes,...(expiresOn?{expiresOn}:{})}))}>Record attestation</button></fieldset>
      {!files.data && <LoadFeedback error={files.error} retry={files.refresh} />}{files.data && <div aria-label="Uploaded document pages"><Paging total={files.data.totalCount} previous={fileCursors.length && !busy && !uncertain ? ()=>setFileCursors(x=>x.slice(0,-1)):undefined} next={files.data.nextCursor && !busy && !uncertain ? ()=>setFileCursors(x=>[...x,files.data!.nextCursor!]):undefined} /></div>}
      <h3>Documents required</h3><DataTable caption="Required agency evidence" columns={['Document','Required from','Current status']}>{Object.entries(attestationLabels).map(([key,label])=>{const result=validation.items.find(x=>x.code===`evidence-${key}`);return <tr key={key}><td>{label}</td><td>Agency</td><td>{result ? evidenceLabel(result.state,disabled) : 'Not currently required'}</td></tr>;})}</DataTable>
      <h3>Evidence history</h3>{!history.data ? <LoadFeedback error={history.error} retry={history.refresh} /> : <><ul className="agency-evidence-history">{history.data.items.map(row=><li key={row.id}><strong>{checkLabels[row.kind] ?? attestationLabels[row.kind]}</strong><span>{new Date(row.recordedAt).toLocaleString('en-GB')} · Recorded result: {row.resultCode.replaceAll('-',' ')}</span>{row.fileId && <a href={`/api/v1/agencies/${id}/evidence-files/${row.fileId}/content`}>Download supporting file</a>}</li>)}</ul><Paging total={history.data.totalCount} previous={cursor.length?()=>setCursor(x=>x.slice(0,-1)):undefined} next={history.data.nextCursor?()=>setCursor(x=>[...x,history.data!.nextCursor!]):undefined} /></>}
    </>}
    {guard && !busy && !uncertain && !stale && <button type="button" className="button" onClick={clear}>Discard unsaved evidence inputs</button>}{uncertain && <button type="button" className="button button-primary" disabled={busy} onClick={()=>void execute()}>Retry same evidence operation</button>}
    {busy && <p role="status">Saving evidence operation…</p>}{notice && <p className="notice" role="status">{notice}</p>}{error && <p className="error-message" role="alert">{error}</p>}
    </div>
  </Panel>;
}
