'use client';
import { useState } from 'react';
import { communicationCommand, communicationFetch, type MessageView, type ThreadView } from '../../lib/communications-api';
import type { DocumentVersion } from '../../lib/documents-api';
import { Status } from '../primitives';
import { CommunicationCommand, type CommunicationConfirmation } from './communication-command';
import { DocumentPreview } from './document-preview';
import { DeliveryHistory } from './delivery-history';
import { AgencyResponseTracking } from './agency-response-tracking';
import { documentDate } from './document-shared';
import { LoadFeedback, Paging, useCommunicationResource, type CommunicationPage } from './communication-shared';

type Option = { id: string; label: string; email?: string };
type Options<T> = { items: T[]; nextCursor?: string };
export function Threads({ subjectId, actorId, initialAttachment }: { subjectId: string; actorId: string; initialAttachment?: {version:DocumentVersion;relationshipId:string} }) {
  const [subject, setSubject] = useState(initialAttachment ? `Document: ${initialAttachment.version.originalName}`.slice(0,300) : ''), [visibility, setVisibility] = useState<'internal' | 'agency'>('agency'), [relationshipId, setRelationshipId] = useState(initialAttachment?.relationshipId ?? '');
  const [pages, setPages] = useState(['']), [relationPages, setRelationPages] = useState(['']), [selected, setSelected] = useState('');
  const [request, setRequest] = useState<CommunicationConfirmation>(), [error, setError] = useState('');
  const cursor = pages.at(-1)!, relationCursor = relationPages.at(-1)!;
  const read = useCommunicationResource<CommunicationPage<ThreadView>>(`/api/v1/records/${subjectId}/threads?pageSize=10${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
  const relationships = useCommunicationResource<Options<Option>>(visibility === 'agency' ? `/api/v1/records/${subjectId}/thread-relationships?pageSize=25${relationCursor ? `&cursor=${encodeURIComponent(relationCursor)}` : ''}` : null);
  const current = read.data?.items.find(x => x.id === selected);
  return <div className="quote-rail-body communication-form">
    <form onSubmit={event => { event.preventDefault(); setError(''); try { setRequest({ command: communicationCommand('thread', subjectId, { visibility, subject, ...(visibility === 'agency' ? { relationshipId } : {}) }), label: 'Create conversation', description: `Save a new ${visibility === 'agency' ? 'agency' : 'internal'} conversation for this record.` }); } catch (failure) { setError((failure as Error).message); } }}>
      <fieldset className="quote-reference-fields" disabled={!!request}><legend>New conversation</legend>
        <label>Audience<select disabled={!!initialAttachment} value={visibility} onChange={event => setVisibility(event.target.value as 'internal' | 'agency')}><option value="agency">Agency conversation</option><option value="internal">Internal conversation</option></select></label>
        {visibility === 'agency' && <><label>Client relationship<select required value={relationshipId} onChange={event => setRelationshipId(event.target.value)}><option value="">Choose an owned client relationship</option>{relationships.data?.items.map(x => <option key={x.id} value={x.id}>{x.label}</option>)}</select></label>
          {!relationships.data && <LoadFeedback error={relationships.error} retry={relationships.refresh} />}<OptionPaging pages={relationPages} setPages={setRelationPages} next={relationships.data?.nextCursor} /></>}
        <label>Conversation subject<input required maxLength={300} value={subject} onChange={event => setSubject(event.target.value)} /></label>
        <button className="button" type="submit">Create conversation</button>
      </fieldset>
    </form>{error && <p role="alert">{error}</p>}
    <div className="quote-row-actions"><h3>Conversations</h3><button className="button" onClick={read.refresh}>Refresh conversations</button></div>
    {read.data ? <>{!read.data.items.length && <p>No conversations recorded.</p>}{read.data.items.map(thread => <article className="quote-driver-card" key={thread.id}>
      <div className="quote-row-actions"><button className="button" onClick={() => setSelected(thread.id)}>{thread.subject}</button><Status tone="info">{thread.visibility === 'agency' ? 'Agency' : 'Internal only'}</Status></div>
      <p>{thread.authorLabel} · {documentDate(thread.createdAt)}</p>
    </article>)}<Paging total={read.data.totalCount} previous={pages.length > 1 ? () => { setSelected(''); setPages(x => x.slice(0, -1)); } : undefined} next={read.data.nextCursor ? () => { setSelected(''); setPages(x => [...x, read.data!.nextCursor!]); } : undefined} /></> : <LoadFeedback error={read.error} retry={read.refresh} />}
    {initialAttachment && <p>Selected file: {initialAttachment.version.originalName} · version {initialAttachment.version.number}. The recipient relationship must match this file’s audience.</p>}
    {current && <ThreadWorkspace key={current.id} thread={current} actorId={actorId} initialAttachment={initialAttachment?.version} />}
    {request && <CommunicationCommand request={request} actorId={actorId} close={() => setRequest(undefined)} saved={id => { setRequest(undefined); setSubject(''); setPages(['']); setSelected(id ?? ''); read.refresh(); }} />}
  </div>;
}
function ThreadWorkspace({ thread, actorId, initialAttachment }: { thread: ThreadView; actorId: string; initialAttachment?:DocumentVersion }) {
  const [send,setSend]=useState<CommunicationConfirmation>(),[sendError,setSendError]=useState('');
  const [pages, setPages] = useState(['']), [editing, setEditing] = useState<MessageView>(), [compose, setCompose] = useState(!!initialAttachment), [notice, setNotice] = useState(''), [revision, setRevision] = useState(0);
  const cursor = pages.at(-1)!;
  const read = useCommunicationResource<CommunicationPage<MessageView>>(`/api/v1/threads/${thread.id}/messages?pageSize=10${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
  return <section aria-label="Conversation messages" className="quote-driver-card"><h3>{thread.subject}</h3><p>{thread.visibility === 'agency' ? 'Agency conversation' : 'Visible to internal users only'}</p>
    {notice && <p role="status">{notice}</p>}
    <div className="quote-row-actions"><button className="button" disabled={compose} onClick={() => { setEditing(undefined); setCompose(true); }}>New message draft</button><button className="button" onClick={read.refresh}>Refresh messages</button></div>
    {read.data ? <>{!read.data.items.length && <p>No messages in this thread. Choose recipients and write the first message.</p>}{read.data.items.map(message => <article key={message.id} className="quote-driver-card"><p><strong>{message.authorLabel}</strong> · {documentDate(message.createdAt)} · <Status tone="info">{message.state === 'draft' ? 'Draft' : message.state}</Status></p><p style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{message.body || 'Empty draft'}</p>
      <p>{message.recipientContactIds.length} selected recipients · {message.attachmentVersionIds.length} exact file versions</p>
      {message.state === 'draft' && <button className="button" disabled={compose} onClick={() => { setEditing(message); setCompose(true); }}>Edit draft</button>}
      {message.state === 'draft' && thread.visibility==='agency' && <button className="button button-primary" disabled={compose||!!send||!message.body.trim()||!message.recipientContactIds.length} onClick={()=>{try{setSendError('');setSend({command:communicationCommand('send-message',message.id,{},message.etag),label:'Send to agency',description:`Queue the saved message with ${message.recipientContactIds.length} recipients and ${message.attachmentVersionIds.length} exact file versions. Delivery status will be recorded separately.`});}catch(error){setSendError((error as Error).message);}}}>Send to agency</button>}
      {message.state!=='draft'&&<DeliveryHistory id={message.id} kind="message" actorId={actorId}/>}
      {message.state==='sent'&&thread.visibility==='agency'&&<AgencyResponseTracking messageId={message.id} actorId={actorId}/>}
    </article>)}<Paging total={read.data.totalCount} previous={pages.length > 1 ? () => setPages(x => x.slice(0, -1)) : undefined} next={read.data.nextCursor ? () => setPages(x => [...x, read.data!.nextCursor!]) : undefined} /></> : <LoadFeedback error={read.error} retry={read.refresh} />}
    {compose && <DraftComposer key={`${editing?.id ?? 'new'}:${revision}`} thread={thread} actorId={actorId} initial={editing} initialAttachment={initialAttachment} saved={() => { setCompose(false); setEditing(undefined); setRevision(x => x + 1); setNotice('Draft saved.'); setPages(['']); read.refresh(); }} />}
    {sendError&&<p role="alert">{sendError}</p>}
    {send&&<CommunicationCommand request={send} actorId={actorId} close={()=>setSend(undefined)} saved={()=>{setSend(undefined);setNotice('Delivery queued. Refresh delivery history to view its outcome.');read.refresh();}}/>}
  </section>;
}
function DraftComposer({ thread, actorId, initial, initialAttachment, saved }: { thread: ThreadView; actorId: string; initial?: MessageView; initialAttachment?:DocumentVersion; saved: () => void }) {
  const templates=useCommunicationResource<{items:{id:string;name:string;body:string}[]}>('/api/v1/communication/templates');
  const [body, setBody] = useState(initial?.body ?? ''), [recipients, setRecipients] = useState(initial?.recipientContactIds ?? []), [attachments, setAttachments] = useState(initial?.attachmentVersionIds ?? (initialAttachment ? [initialAttachment.id] : []));
  const [etag, setEtag] = useState(initial?.etag), [latest, setLatest] = useState<MessageView>(), [error, setError] = useState(''), [request, setRequest] = useState<CommunicationConfirmation>();
  const [preview, setPreview] = useState<string>();
  async function reviewCurrent() {
    try { const result = await communicationFetch<MessageView>(`/api/v1/messages/${initial!.id}`); setLatest(result.data); setError(''); } catch (failure) { setError((failure as Error).message); }
  }
  return <section aria-label="Message draft"><h4>{initial ? 'Edit saved draft' : 'New message draft'}</h4>
    <form onSubmit={event => { event.preventDefault(); setError(''); try { setRequest({ command: communicationCommand(initial ? 'update-draft' : 'create-draft', initial?.id ?? thread.id, { body, recipientContactIds: recipients, attachmentVersionIds: attachments }, etag), label: 'Save draft', description: `Save this draft with ${recipients.length} recipients and ${attachments.length} selected file versions.` }); } catch (failure) { setError((failure as Error).message); } }}>
      <fieldset className="quote-reference-fields" disabled={!!request}><legend>Draft contents</legend>
        {!!templates.data?.items.length&&<label>Insert message template (replaces draft text)<select value="" onChange={e=>{const template=templates.data!.items.find(x=>x.id===e.target.value);if(template)setBody(template.body);}}><option value="">Choose a template</option>{templates.data.items.map(x=><option key={x.id} value={x.id}>{x.name}</option>)}</select></label>}
        <label>Message text<textarea rows={6} maxLength={8000} value={body} onChange={event => setBody(event.target.value)} /></label>
        {thread.visibility === 'agency' && <ChoiceList<Option> url={`/api/v1/threads/${thread.id}/recipient-options`} label="Recipients" selected={recipients} change={setRecipients} maximum={50} describe={x => `${x.label} · ${x.email}`} />}
        <ChoiceList<DocumentVersion> url={`/api/v1/threads/${thread.id}/attachment-options`} label="Attachments" selected={attachments} change={setAttachments} maximum={20} describe={x => `${x.originalName} · file version ${x.number} · ${x.sourceLabel ?? 'Saved source'}`} preview={setPreview} />
        <button className="button button-primary" type="submit">Save draft</button>
        {initial && <button className="button" type="button" onClick={() => void reviewCurrent()}>Review current saved draft</button>}
      </fieldset>
    </form>{error && <p role="alert">{error}</p>}
    {latest && <section className="quote-driver-card" aria-label="Current saved draft"><h4>Current saved draft</h4><p style={{ whiteSpace: 'pre-wrap' }}>{latest.body || 'Empty draft'}</p><p>Saved {documentDate(latest.updatedAt)} · {latest.state}</p>
      {latest.state === 'draft' ? <button className="button" onClick={() => { setEtag(latest.etag); setLatest(undefined); }}>Keep my text and use this version for the next save</button> : <p>This message can no longer be edited. Your local text is retained above.</p>}
    </section>}
    {preview && <DocumentPreview versionId={preview} close={() => setPreview(undefined)} />}
    {request && <CommunicationCommand request={request} actorId={actorId} close={() => setRequest(undefined)} saved={() => { setRequest(undefined); saved(); }} />}
  </section>;
}
export function ChoiceList<T extends { id: string }>({ url, label, selected, change, maximum, describe, preview }: { url: string; label: string; selected: string[]; change: (ids: string[]) => void; maximum: number; describe: (item: T) => string; preview?: (id: string) => void }) {
  const [pages, setPages] = useState(['']); const cursor = pages.at(-1)!;
  const read = useCommunicationResource<Options<T>>(`${url}?pageSize=25${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
  return <section aria-label={label}><h5>{label}</h5><p>{selected.length} selected · maximum {maximum}</p>
    {read.data ? <>{!read.data.items.length && <p>No eligible choices on this page.</p>}{read.data.items.map(item => <div className="quote-driver-card" key={item.id}><label className="communication-choice"><input type="checkbox" checked={selected.includes(item.id)} disabled={!selected.includes(item.id) && selected.length >= maximum} onChange={event => change(event.target.checked ? [...selected, item.id] : selected.filter(id => id !== item.id))} /><span>{describe(item)}</span></label>{preview && <button className="button" type="button" onClick={() => preview(item.id)}>Preview file version</button>}</div>)}</> : <LoadFeedback error={read.error} retry={read.refresh} />}
    {selected.filter(id => !read.data?.items.some(x => x.id === id)).map(id => <p key={id}>Saved selection {id.slice(0, 8)} · not on this page <button type="button" className="button" onClick={() => change(selected.filter(x => x !== id))}>Remove selection</button></p>)}
    <OptionPaging pages={pages} setPages={setPages} next={read.data?.nextCursor} />
  </section>;
}
function OptionPaging({ pages, setPages, next }: { pages: string[]; setPages: (value: string[]) => void; next?: string }) {
  return <div className="quote-row-actions">{pages.length > 1 && <button type="button" className="button" onClick={() => setPages(pages.slice(0, -1))}>Previous choices</button>}{next && <button type="button" className="button" onClick={() => setPages([...pages, next])}>More choices</button>}</div>;
}
