'use client';
import { useRef, useState } from 'react';
import { documentCommand, prepareFileUpload, sendFileUpload, sendDocumentCommand, type FileUpload } from '../../lib/documents-api';
import { DocumentCommand, type DocumentConfirmation } from './document-command';
import { documentSize } from './document-shared';

export function DocumentAudience({ value, setValue, relationshipId }: { value: string; setValue: (value: string) => void; relationshipId?: string }) {
  return <label>Audience<select value={value} onChange={event => setValue(event.target.value)}><option value="internal">Internal</option><option value="insurer">Insurer</option>{relationshipId && <option value="agency">Linked agency relationship</option>}</select></label>;
}
export function DocumentUpload({ subjectId, actorId, relationshipId, documentId, kind = 'evidence', initialVisibility = 'internal', saved, close }: {
  subjectId: string; actorId: string; relationshipId?: string; documentId?: string; kind?: string; initialVisibility?: string; saved: () => void; close: () => void;
}) {
  const [file, setFile] = useState<File>(), [reason, setReason] = useState(''), [visibility, setVisibility] = useState(initialVisibility);
  const [pending, setPending] = useState<DocumentConfirmation>(), [error, setError] = useState(''), [preparing, setPreparing] = useState(false);
  const preparingRef = useRef(false);
  async function prepare() {
    if (preparingRef.current || !file) return;
    preparingRef.current = true; setPreparing(true); setError('');
    try {
      if (!reason.trim()) throw new Error('Enter a reason for uploading this evidence.');
      const upload = await prepareFileUpload(subjectId, file), attachKey = crypto.randomUUID();
      const input = { kind, visibility, reason, ...(visibility === 'agency' ? { relationshipId } : {}), ...(documentId ? { documentId } : {}) };
      let receipt: FileUpload | undefined;
      setPending({ label: documentId ? 'Save new file version' : 'Upload evidence', description: `${file.name} · ${documentSize(file.size)}. The original file will be retained.`,
        execute: async csrf => {
          receipt ??= await sendFileUpload(upload, csrf);
          return sendDocumentCommand(documentCommand('upload', subjectId, { ...input, uploadId: receipt.id }, attachKey, receipt), csrf);
        } });
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'The file could not be prepared.'); }
    finally { preparingRef.current = false; setPreparing(false); }
  }
  return <div className="quote-rail-body"><h3>{documentId ? 'Upload a new file version' : 'Upload evidence'}</h3>
    <form onSubmit={event => { event.preventDefault(); void prepare(); }}><fieldset disabled={preparing || !!pending} className="document-fields">
      <label>File (PDF, PNG or JPEG; up to 20 MiB)<input type="file" accept="application/pdf,image/png,image/jpeg,.pdf,.png,.jpg,.jpeg" required onChange={event => setFile(event.target.files?.[0])} /></label>
      {file && <p>{file.name} · {documentSize(file.size)}</p>}
      {!documentId && <DocumentAudience value={visibility} setValue={setVisibility} relationshipId={relationshipId} />}
      <label>Reason<textarea required maxLength={1000} value={reason} onChange={event => setReason(event.target.value)} /></label>
      <div className="quote-row-actions"><button type="button" className="button" onClick={close}>Cancel upload</button><button className="button button-primary" disabled={!file}>{preparing ? 'Checking file…' : 'Review upload'}</button></div>
    </fieldset></form>{error && <p role="alert" className="error-message">{error}</p>}
    {pending && <DocumentCommand request={pending} actorId={actorId} close={() => setPending(undefined)} saved={saved} />}
  </div>;
}
