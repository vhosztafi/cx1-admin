'use client';
import { useState } from 'react';
import { communicationCommand, type NoteView } from '../../lib/communications-api';
import { CommunicationCommand, type CommunicationConfirmation } from './communication-command';
import { documentDate } from './document-shared';
import { LoadFeedback, Paging, useCommunicationResource, type CommunicationPage } from './communication-shared';

export function Notes({ subjectId, actorId }: { subjectId: string; actorId: string }) {
  const [body, setBody] = useState(''), [error, setError] = useState(''), [notice, setNotice] = useState('');
  const [request, setRequest] = useState<CommunicationConfirmation>(), [pages, setPages] = useState(['']);
  const cursor = pages.at(-1)!;
  const read = useCommunicationResource<CommunicationPage<NoteView>>(`/api/v1/records/${subjectId}/notes?pageSize=10${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
  return <div className="quote-rail-body communication-form">
    <form onSubmit={event => { event.preventDefault(); setError(''); try { setRequest({ command: communicationCommand('note', subjectId, { body }), label: 'Add internal note', description: 'Save this note with your name and the current time. It remains visible to internal users only.' }); } catch (failure) { setError((failure as Error).message); } }}>
      <label>Internal note<textarea value={body} maxLength={8000} rows={4} required disabled={!!request} onChange={event => setBody(event.target.value)} /></label>
      {error && <p role="alert">{error}</p>}<button className="button button-primary" disabled={!!request}>Add internal note</button>
    </form>
    {notice && <p role="status">{notice}</p>}
    <div className="quote-row-actions"><h3>Saved notes</h3><button className="button" onClick={read.refresh}>Refresh notes</button></div>
    {read.data ? <>{!read.data.items.length && <p>No internal notes recorded.</p>}{read.data.items.map(note => <article key={note.id} className="quote-driver-card"><p><strong>{note.authorLabel}</strong> · {documentDate(note.createdAt)}</p><p style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{note.body}</p></article>)}
      <Paging total={read.data.totalCount} previous={pages.length > 1 ? () => setPages(x => x.slice(0, -1)) : undefined} next={read.data.nextCursor ? () => setPages(x => [...x, read.data!.nextCursor!]) : undefined} />
    </> : <LoadFeedback error={read.error} retry={read.refresh} />}
    {request && <CommunicationCommand request={request} actorId={actorId} close={() => setRequest(undefined)} saved={() => { setRequest(undefined); setBody(''); setNotice('Internal note saved.'); setPages(['']); read.refresh(); }} />}
  </div>;
}
