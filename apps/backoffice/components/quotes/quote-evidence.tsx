'use client';
import { useEffect, useState } from 'react';
import { quoteFetch, type PendingQuoteCommand, type QuoteView } from '../../lib/quotes';
import { Panel } from '../primitives';

type EvidenceFile = { id: string; fileName: string; contentType: string; length: number; sha256: string; uploadedAt: string };
type Requirement = { code: string; path: string; riskItemId: string | null; label: string; inputFingerprint: string; state: 'missing' | 'current' };
type Evidence = { id: string; revisionId: string; requirementCode: string; riskItemId: string | null; state: 'current' | 'stale' | 'withdrawn'; etag: string;
  reason: string; createdAt: string; createdByLabel: string; file: EvidenceFile; withdrawnAt: string | null; withdrawalReason: string | null };
type Snapshot = { revisionId: string; requirements: Requirement[]; items: Evidence[] };

export function QuoteEvidence({ saved, etag, disabled, dirty, refreshToken, run }: {
  saved: QuoteView; etag: string; disabled: boolean; dirty: boolean; refreshToken: number; run: (command: PendingQuoteCommand) => void;
}) {
  const [loaded, setLoaded] = useState<{ key: string; snapshot: Snapshot; files: EvidenceFile[] }>();
  const [reload, setReload] = useState(0); const [error, setError] = useState(''); const [file, setFile] = useState<File>();
  const key = `${saved.id}:${saved.revisionId}:${refreshToken}:${reload}`;
  useEffect(() => {
    const abort = new AbortController();
    const signal = AbortSignal.any([abort.signal, AbortSignal.timeout(15_000)]);
    Promise.all([quoteFetch<Snapshot>(`/api/v1/quotes/${saved.id}/evidence?revisionId=${saved.revisionId}`, { signal }),
      quoteFetch<{ items: EvidenceFile[] }>(`/api/v1/quotes/${saved.id}/evidence-files`, { signal })]).then(([snapshot, files]) => {
      if (!abort.signal.aborted) { setLoaded({ key, snapshot: snapshot.data, files: files.data.items }); setError(''); }
    }, () => { if (!abort.signal.aborted) setError('Evidence could not be loaded. Refresh to check saved requirements.'); });
    return () => abort.abort();
  }, [saved.id, saved.revisionId, key]);
  const current = loaded?.key === key ? loaded : undefined;
  const unavailable = disabled || dirty || !saved.capabilities.canAttachEvidence || !current;
  const submit = (suffix: string, body: object, version = etag, upload?: File) => {
    if (unavailable) return;
    run(Object.freeze({ method: 'POST', url: `/api/v1/quotes/${saved.id}/${suffix}`, body: JSON.stringify(body),
      key: crypto.randomUUID(), etag: version, ...(upload ? { upload } : {}) }));
  };
  return <Panel title="Proposal evidence"><div className="quote-rail-body" data-testid="quote-evidence">
    <p>Attach proof to saved revision {saved.revisionNumber}. Changes to the relevant proposer or risk details make earlier proof stale.</p>
    <p className="client-help">Demo screening checks file type and size only. It does not scan for malware or verify document contents. Use fictional documents.</p>
    {dirty && <p>Save your changes before uploading, attaching or withdrawing evidence.</p>}
    {error && <p role="alert">{error}</p>}
    <button type="button" className="button" disabled={disabled} onClick={() => setReload(value => value + 1)}>Refresh evidence</button>
    {!current && !error && <p role="status">Loading saved evidence…</p>}
    <fieldset className="quote-reference-fields" disabled={unavailable}><legend>Upload a document</legend>
      <label className="quote-form-label">Evidence file<input type="file" accept=".pdf,.png,.jpg,.jpeg,.txt" onChange={event => {
        const selected = event.target.files?.[0];
        if (selected && (selected.size === 0 || selected.size > 10 * 1024 * 1024)) { setError('Choose a non-empty file of at most 10 MiB.'); setFile(undefined); }
        else { setError(''); setFile(selected); }
      }} /></label>
      <p className="client-help">PDF, PNG, JPEG or plain text · maximum 10 MiB. Upload first, then select the saved document for a requirement.</p>
      <button type="button" className="button" disabled={!file} onClick={() => file && submit('evidence-files', {}, etag, file)}>Upload evidence file</button>
    </fieldset>
    {current?.snapshot.requirements.map(requirement => <RequirementCard key={`${requirement.code}:${requirement.riskItemId ?? ''}`} requirement={requirement}
      files={current.files} disabled={unavailable} attach={(fileId, reason) => submit('evidence', { revisionId: current.snapshot.revisionId,
        requirementCode: requirement.code, ...(requirement.riskItemId ? { riskItemId: requirement.riskItemId } : {}),
        inputFingerprint: requirement.inputFingerprint, fileId, reason })} />)}
    <details><summary>Saved documents ({current?.files.length ?? 0})</summary>
      {current?.files.map(item => <p key={item.id}><a href={`/api/v1/quotes/${saved.id}/evidence-files/${item.id}/content`} download>{item.fileName}</a> · {item.length.toLocaleString('en-GB')} bytes</p>)}
    </details>
    <details open><summary>Evidence history ({current?.snapshot.items.length ?? 0})</summary>
      {current?.snapshot.items.length === 0 && <p>No evidence attached yet.</p>}
      {current?.snapshot.items.map(item => <HistoryCard key={item.id} item={item} quoteId={saved.id} disabled={unavailable}
        withdraw={reason => submit(`evidence/${item.id}/withdraw`, { reason }, item.etag)} />)}
    </details>
  </div></Panel>;
}

const evidenceLabel = (code: string) => ({ 'motor-trader-proof': 'business proof', 'photocard-both-sides': 'photocard licence', 'driving-record': 'driving record', 'no-claims-proof': 'no-claims proof' })[code] ?? 'proof';

function RequirementCard({ requirement, files, disabled, attach }: { requirement: Requirement; files: EvidenceFile[]; disabled: boolean; attach: (id: string, reason: string) => void }) {
  const [fileId, setFileId] = useState(''); const [reason, setReason] = useState('');
  const label = `${/^\/risk\/drivers\/\d+$/.test(requirement.path) ? `Driver ${Number(requirement.path.split('/').at(-1)) + 1} · ` : ''}${requirement.label}`;
  return <fieldset className="quote-reference-fields" disabled={disabled}><legend>{label}</legend>
    <p role="status">{requirement.state === 'current' ? 'Current evidence attached' : 'Evidence required'}</p>
    <div className="quote-form-grid"><label>Saved document<select aria-label={`${label} · Saved document`} value={fileId} onChange={event => setFileId(event.target.value)}>
      <option value="">Select a document</option>{files.map(item => <option key={item.id} value={item.id}>{item.fileName} · {new Date(item.uploadedAt).toLocaleString('en-GB')}</option>)}
    </select></label><label>Attachment reason<textarea aria-label={`${label} · Attachment reason`} maxLength={1000} value={reason} onChange={event => setReason(event.target.value)} /></label></div>
    <button type="button" className="button" disabled={!files.some(item => item.id === fileId) || !reason.trim()} onClick={() => attach(fileId, reason.trim())}>Attach {evidenceLabel(requirement.code)}</button>
  </fieldset>;
}

function HistoryCard({ item, quoteId, disabled, withdraw }: { item: Evidence; quoteId: string; disabled: boolean; withdraw: (reason: string) => void }) {
  const [reason, setReason] = useState('');
  return <div className="quote-driver-card" data-evidence-id={item.id}>
    <p><strong>{evidenceLabel(item.requirementCode)} · {item.state}</strong></p>
    <p><a href={`/api/v1/quotes/${quoteId}/evidence-files/${item.file.id}/content`} download>{item.file.fileName}</a> · {item.createdByLabel} · {new Date(item.createdAt).toLocaleString('en-GB')}</p>
    <p>{item.reason}</p>{item.riskItemId && <p className="client-help">Evidence relates to the named driver recorded in its source revision.</p>}
    {item.state === 'stale' && <p>The relevant saved answers changed. Review and attach proof against the current requirement.</p>}
    {item.withdrawnAt ? <p>Withdrawn {new Date(item.withdrawnAt).toLocaleString('en-GB')} · {item.withdrawalReason}</p> : <>
      <label className="quote-form-label">Withdrawal reason<textarea aria-label={`Withdrawal reason for ${item.file.fileName}`} maxLength={1000} value={reason} disabled={disabled} onChange={event => setReason(event.target.value)} /></label>
      <button className="button" type="button" disabled={disabled || !reason.trim()} onClick={() => withdraw(reason.trim())}>Withdraw attachment</button>
    </>}
  </div>;
}
