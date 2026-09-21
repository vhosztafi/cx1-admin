'use client';
import { useState } from 'react';
import { documentCommand, documentOptionsUrl, sendDocumentCommand, type DocumentOptions, type DocumentSource } from '../../lib/documents-api';
import { DocumentCommand, type DocumentConfirmation } from './document-command';
import { DocumentAudience } from './document-upload';
import { documentDate, LoadFeedback, useDocumentResource } from './document-shared';

export function DocumentGenerate({ subjectId, actorId, source, relationshipId, document, saved, close }: {
  subjectId: string; actorId: string; source: DocumentSource; relationshipId?: string; document?: { id: string; kind: string; visibility: string }; saved: () => void; close: () => void;
}) {
  const [cursor, setCursor] = useState<string>(), [selection, setSelection] = useState(''), [reason, setReason] = useState(''), [visibility, setVisibility] = useState(document?.visibility ?? 'internal');
  const [pending, setPending] = useState<DocumentConfirmation>(), [error, setError] = useState('');
  const options = useDocumentResource<DocumentOptions>(documentOptionsUrl(subjectId, source, cursor));
  const choice = options.data?.items.find(item => `${item.kind}:${item.templateVersionId}` === selection);
  function prepare() {
    try {
      if (!choice) throw new Error('Choose an applicable document and template.');
      const command = documentCommand('generate', subjectId, { kind: choice.kind, source: choice.source, templateVersionId: choice.templateVersionId, visibility, reason, ...(visibility === 'agency' ? { relationshipId } : {}), ...(document ? { documentId: document.id } : {}) });
      setPending({ label: 'Generate document', description: `${choice.label} · ${options.data!.sourceLabel} · ${choice.templateLabel}. This queues generation of a PDF.`, execute: csrf => sendDocumentCommand(command, csrf) }); setError('');
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'Review the selected document.'); }
  }
  return <div className="quote-rail-body"><h3>Generate document</h3>{options.data ? <>
    <p>{options.data.sourceLabel} · {documentDate(options.data.sourceDate)} · London</p>
    <form onSubmit={event => { event.preventDefault(); prepare(); }}><fieldset className="document-fields" disabled={!!pending}>
      <label>Document and template<select aria-label="Document and template" required value={selection} onChange={event => setSelection(event.target.value)}><option value="">Choose a document</option>{options.data.items.filter(item => !document || item.kind === document.kind).map(item => <option key={`${item.kind}:${item.templateVersionId}`} value={`${item.kind}:${item.templateVersionId}`}>{item.label} · {item.templateLabel}</option>)}</select></label>
      {!options.data.items.length && <p>No applicable templates on this page.</p>}
      <div className="quote-row-actions">{cursor && <button type="button" className="button" onClick={() => { setCursor(undefined); setSelection(''); }}>First template page</button>}{options.data.nextCursor && <button type="button" className="button" onClick={() => { setCursor(options.data!.nextCursor); setSelection(''); }}>More templates</button>}</div>
      {document ? <p>New version of the selected {document.kind} · {visibility} audience. Existing files remain available.</p> : <DocumentAudience value={visibility} setValue={setVisibility} relationshipId={relationshipId} />}
      <label>Reason<textarea required maxLength={1000} value={reason} onChange={event => setReason(event.target.value)} /></label>
      <div className="quote-row-actions"><button type="button" className="button" onClick={close}>Cancel generation</button><button className="button button-primary" disabled={!choice}>Review generation</button></div>
    </fieldset></form></> : <><LoadFeedback error={options.error} retry={options.refresh} /><button className="button" onClick={close}>Cancel generation</button></>}
    {error && <p role="alert" className="error-message">{error}</p>}{pending && <DocumentCommand request={pending} actorId={actorId} close={() => setPending(undefined)} saved={saved} />}
  </div>;
}
