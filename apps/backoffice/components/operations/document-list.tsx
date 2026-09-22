'use client';
import { useEffect, useState } from 'react';
import { csrfToken, type Actor } from '../../lib/auth';
import { documentContentUrl, documentFetch, documentKinds, validDocumentVersion, type DocumentSource, type DocumentVersion } from '../../lib/documents-api';
import { sendTaskCommand, taskCommand } from '../../lib/tasks-api';
import type { OpsDocument } from '../../../../contracts/generated/operations';
import { DataTable, Panel, Status } from '../primitives';
import type { TaskParent } from './task-create';
import { DocumentGenerate } from './document-generate';
import { DocumentUpload } from './document-upload';
import { LegacyEvidence } from './document-legacy-evidence';
import { DocumentMetadata, DocumentPreview } from './document-preview';
import { documentDate, documentSize, LoadFeedback, Paging, useDocumentResource } from './document-shared';

type Page<T> = { items: T[]; totalCount: number; nextCursor?: string };
export function RecordDocuments({ parent, source, relationshipId }: { parent: TaskParent; source?: DocumentSource; relationshipId?: string }) {
  const [revision, setRevision] = useState(0), key = `${parent.kind}:${parent.id}:${revision}`;
  const [result, setResult] = useState<{ key: string; subjectId?: string; actorId?: string; error?: string }>({ key: '' });
  useEffect(() => {
    let active = true;
    async function load() {
      try {
        const [csrf, account] = await Promise.all([csrfToken(), documentFetch<Actor>('/api/v1/account')]);
        const command = taskCommand('register', parent.id, null, { kind: parent.kind }, `document-subject:${parent.kind}:${parent.id}`);
        const subject = await sendTaskCommand(command, csrf);
        if (active) setResult({ key, subjectId: subject.id, actorId: account.id });
      } catch (failure) { if (active) setResult({ key, error: failure instanceof Error ? failure.message : 'Documents could not be loaded.' }); }
    }
    void load(); return () => { active = false; };
  }, [key, parent.kind, parent.id]);
  return <Panel title="Documents" note="Saved files and immutable version history. Generating a file does not send it.">
    {result.key === key && result.subjectId && result.actorId ? <DocumentList key={`${result.actorId}:${result.subjectId}:${JSON.stringify(source)}`} subjectId={result.subjectId} actorId={result.actorId} source={source} relationshipId={relationshipId} />
      : <LoadFeedback error={result.key === key ? result.error : undefined} retry={() => setRevision(x => x + 1)} />}
    {['quote', 'agency', 'servicing-draft'].includes(parent.kind) && <LegacyEvidence key={`${parent.kind}:${parent.id}`} kind={parent.kind as 'quote' | 'agency' | 'servicing-draft'} parentId={parent.id} />}
  </Panel>;
}
export function DocumentList({ subjectId, actorId, source, relationshipId }: { subjectId: string; actorId: string; source?: DocumentSource; relationshipId?: string }) {
  const [cursor, setCursor] = useState<string>(), [previous, setPrevious] = useState<(string | undefined)[]>([]), [generation, setGeneration] = useState(0);
  const [action, setAction] = useState<'generate' | 'upload'>(), [history, setHistory] = useState<OpsDocument>(), [preview, setPreview] = useState<string>(), [message, setMessage] = useState('');
  const [replacement, setReplacement] = useState<OpsDocument>();
  const url = `/api/v1/records/${subjectId}/documents?pageSize=10${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`;
  const documents = useDocumentResource<Page<OpsDocument>>(url, generation);
  function refresh() { setGeneration(x => x + 1); }
  function saved() { setAction(undefined); setReplacement(undefined); setHistory(undefined); setCursor(undefined); setPrevious([]); setMessage('Document saved. Storage or generation may still be pending.'); refresh(); }
  function closeForm() { setAction(undefined); setReplacement(undefined); }
  return <><div className="quote-rail-body"><div className="quote-row-actions">
    {source && <button className="button button-primary" disabled={!!action} onClick={() => setAction('generate')}>Generate document</button>}
    <button className="button" disabled={!!action} onClick={() => setAction('upload')}>Upload evidence</button><button className="button" onClick={refresh}>Refresh documents</button></div>
    {message && <p role="status">{message}</p>}{source && <p className="client-help">Generation uses the source selected in this record. The list retains files from every saved version.</p>}</div>
    {action === 'generate' && source && <DocumentGenerate key={replacement?.id ?? 'new'} subjectId={subjectId} actorId={actorId} source={source} relationshipId={replacement?.relationshipId ?? relationshipId} document={replacement} saved={saved} close={closeForm} />}
    {action === 'upload' && <DocumentUpload key={replacement?.id ?? 'new'} subjectId={subjectId} actorId={actorId} relationshipId={replacement?.relationshipId ?? relationshipId} documentId={replacement?.id} kind={replacement?.kind} initialVisibility={replacement?.visibility} saved={saved} close={closeForm} />}
    {documents.data ? <>{documents.data.items.length ? <DataTable caption="Saved documents" columns={['Document', 'Source', 'File', 'Availability', 'Actions']}>
      {documents.data.items.map(document => { const version = document.currentVersion; return <tr key={document.id} data-document-id={document.id}><th scope="row">{documentKinds.find(([kind]) => kind === document.kind)?.[1] ?? document.kind}<small className="document-secondary">{document.visibility}</small></th>
        <td>{version?.sourceLabel ?? 'Source unavailable'}{version?.sourceDate && <small className="document-secondary">{documentDate(version.sourceDate)} · London</small>}</td>
        <td>{version?.originalName ?? 'Awaiting file'}{version && <small className="document-secondary">Version {version.number} · {documentSize(version.bytes)} · {version.contentType}<br />Created {documentDate(version.createdAt)}</small>}</td>
        <td><DocumentAvailability version={version} /></td><td><div className="quote-row-actions"><VersionActions version={version} preview={setPreview} /><button className="button" onClick={() => setHistory(document)}>Version history</button>
          {(document.kind === 'evidence' || source) && <button className="button" disabled={!!action} onClick={() => { setReplacement(document); setAction(document.kind === 'evidence' ? 'upload' : 'generate'); }}>New version</button>}
        </div></td></tr>; })}
    </DataTable> : <div className="quote-rail-body"><p>No documents available yet.</p><p>Generate an applicable document or upload evidence.</p></div>}
      <Paging total={documents.data.totalCount} previous={previous.length ? () => { setCursor(previous.at(-1)); setPrevious(x => x.slice(0, -1)); } : undefined} next={documents.data.nextCursor ? () => { setPrevious(x => [...x, cursor]); setCursor(documents.data!.nextCursor); } : undefined} />
    </> : <LoadFeedback error={documents.error} retry={documents.refresh} />}
    {history && <DocumentHistory key={history.id} document={history} preview={setPreview} close={() => setHistory(undefined)} />}
    {preview && <DocumentPreview key={preview} versionId={preview} close={() => setPreview(undefined)} />}
  </>;
}
function DocumentAvailability({ version }: { version?: DocumentVersion }) {
  const state = version?.state ?? 'pending';
  return <Status tone={state === 'ready' ? 'success' : state === 'pending' ? 'info' : 'error'}>{version?.withdrawnEffectiveAt ? 'Withdrawn · retained file' : state === 'ready' ? 'Ready to download' : state === 'pending' ? 'Pending' : state === 'quarantined' ? 'File unavailable' : 'Failed'}</Status>;
}
function VersionActions({ version, preview }: { version?: DocumentVersion; preview: (id: string) => void }) {
  return version?.state === 'ready' && validDocumentVersion(version) ? <><button className="button" onClick={() => preview(version.id)}>Preview v{version.number}</button><a className="button" href={documentContentUrl(version)} download>Download v{version.number}</a></> : null;
}
function DocumentHistory({ document, preview, close }: { document: OpsDocument; preview: (id: string) => void; close: () => void }) {
  const [cursor, setCursor] = useState<string>(), [previous, setPrevious] = useState<(string | undefined)[]>([]);
  const versions = useDocumentResource<Page<DocumentVersion>>(`/api/v1/documents/${document.id}/versions?pageSize=10${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
  return <section className="quote-rail-body" aria-label="Document version history"><div className="quote-row-actions"><h3>Version history</h3><button className="button" onClick={close}>Close history</button><button className="button" onClick={versions.refresh}>Refresh history</button></div>
    {versions.data ? <>{versions.data.items.map(version => <article key={version.id} className="document-history-version"><h4>File version {version.number}</h4><DocumentAvailability version={version} /><DocumentMetadata version={version} /><div className="quote-row-actions"><VersionActions version={version} preview={preview} /></div></article>)}
      <Paging total={versions.data.totalCount} previous={previous.length ? () => { setCursor(previous.at(-1)); setPrevious(x => x.slice(0, -1)); } : undefined} next={versions.data.nextCursor ? () => { setPrevious(x => [...x, cursor]); setCursor(versions.data!.nextCursor); } : undefined} />
    </> : <LoadFeedback error={versions.error} retry={versions.refresh} />}
  </section>;
}
