'use client';
import { useState } from 'react';
import type { OpsDocument, OpsTaskAttachments } from '../../../../contracts/generated/operations';
import { documentContentUrl, validDocumentVersion } from '../../lib/documents-api';
import { taskCommand, type TaskView } from '../../lib/tasks-api';
import { Panel, Status } from '../primitives';
import { DocumentPreview } from './document-preview';
import { RecordDocuments } from './document-list';
import { documentDate, documentSize, LoadFeedback, useDocumentResource } from './document-shared';
import { TaskCommand, type TaskConfirmation } from './task-command';

export function TaskAttachments({ task, actorId, saved }: { task: TaskView; actorId: string; saved: () => void }) {
  const [picking, setPicking] = useState(false), [manage, setManage] = useState(false), [cursor, setCursor] = useState<string>();
  const [selected, setSelected] = useState(''), [reason, setReason] = useState(''), [pending, setPending] = useState<TaskConfirmation>(), [preview, setPreview] = useState<string>(), [error, setError] = useState('');
  const attachments = useDocumentResource<OpsTaskAttachments>(`/api/v1/tasks/${task.id}/attachments`);
  const choices = useDocumentResource<{ items: OpsDocument[]; nextCursor?: string }>(picking ? `/api/v1/records/${task.subjectRecordId}/documents?pageSize=20${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}` : null);
  const terminal = task.state === 'completed' || task.state === 'cancelled';
  const available = choices.data?.items.map(x => x.currentVersion).filter(x => x?.state === 'ready' && validDocumentVersion(x)) ?? [];
  function prepare(attachmentId?: string) {
    try {
      if (!attachmentId && !available.some(x => x?.id === selected)) throw new Error('Choose an available exact file version.');
      const command = attachmentId ? taskCommand('remove-attachment', task.id, task.etag, { attachmentId, reason }) : taskCommand('attach-document', task.id, task.etag, { documentVersionId: selected, reason });
      setPending({ command, label: attachmentId ? 'Remove attachment' : 'Attach file version', description: attachmentId ? 'Remove this task association. The original file and attachment history remain available.' : 'Attach this exact saved version to the task. A later file version will not replace it.' }); setError('');
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'Review the selected file and reason.'); }
  }
  return <Panel title="Attachments" note="Exact saved versions linked to this task"><div className="quote-rail-body task-attachments">
    {attachments.data ? <>{!attachments.data.items.length && <p>No files attached to this task.</p>}{attachments.data.items.map(link => {
      const version = link.version, ready = version.state === 'ready' && validDocumentVersion(version);
      return <article key={link.id} className="document-history-version"><h3>{version.originalName}</h3><Status tone={ready ? 'success' : 'warning'}>{version.state}</Status>
        <p>File version {version.number} · {documentSize(version.bytes)} · {version.contentType}</p><p>{version.sourceLabel}{version.sourceDate ? ` · ${documentDate(version.sourceDate)}` : ''}</p>
        <p>Attached by {link.authorLabel} · {documentDate(link.createdAt)}</p><p>{link.reason}</p>
        <div className="quote-row-actions">{ready && <><button className="button" onClick={() => setPreview(version.id)}>Preview attachment</button><a className="button" href={documentContentUrl(version)} download>Download attachment</a></>}
          {!terminal && <button className="button" onClick={() => prepare(link.id)}>Remove attachment</button>}</div></article>;
    })}</> : <LoadFeedback error={attachments.error} retry={attachments.refresh} />}
    {!terminal ? <><label>Attachment reason<textarea aria-label="Attachment reason" maxLength={1000} value={reason} onChange={event => setReason(event.target.value)} /></label>
      <div className="quote-row-actions"><button className="button" onClick={() => setPicking(x => !x)}>{picking ? 'Close file picker' : 'Choose file version'}</button><button className="button" onClick={() => setManage(x => !x)}>{manage ? 'Close record documents' : 'Open record documents'}</button></div>
      {picking && (choices.data ? <div className="document-fields"><label>Saved file version<select aria-label="Saved file version" value={selected} onChange={event => setSelected(event.target.value)}><option value="">Choose a ready file on this page</option>{available.map(version => version && <option key={version.id} value={version.id}>{version.originalName} · v{version.number} · {version.sourceLabel}</option>)}</select></label>
        {!available.length && <p>No ready files on this page. Open record documents to upload evidence.</p>}
        <div className="quote-row-actions"><button className="button" disabled={!selected || !reason.trim()} onClick={() => prepare()}>Attach file version</button><button className="button" onClick={choices.refresh}>Refresh file choices</button>
          {cursor && <button className="button" onClick={() => { setCursor(undefined); setSelected(''); }}>First file page</button>}{choices.data.nextCursor && <button className="button" onClick={() => { setCursor(choices.data!.nextCursor); setSelected(''); }}>More files</button>}</div>
      </div> : <LoadFeedback error={choices.error} retry={choices.refresh} />)}
    </> : <p>Reopen the task to change its attachments.</p>}
    {manage && <RecordDocuments parent={{kind:task.subject.kind,id:task.subject.parentId,label:task.subject.label}} />}
    {error && <p role="alert" className="error-message">{error}</p>}
    {preview && <DocumentPreview versionId={preview} close={() => setPreview(undefined)} />}
    {pending && <TaskCommand request={pending} actorId={actorId} close={() => setPending(undefined)} saved={() => { setPending(undefined); saved(); }} />}
  </div></Panel>;
}
