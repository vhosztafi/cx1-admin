import { QuoteError, validQuoteEtag } from './quotes.ts';
import { validTaskId } from './tasks-api.ts';
import type { OpsNote, OpsThread, OpsMessage } from '../../../contracts/generated/operations.ts';

export type NoteView = OpsNote;
export type ThreadView = OpsThread;
export type MessageView = OpsMessage;
export type CommunicationAction = 'note' | 'thread' | 'create-draft' | 'update-draft' | 'send-message' | 'send-pack' | 'retry-message' | 'resend-message' | 'retry-document' | 'resend-document' | 'track-response' | 'resolve-response';
export type PendingCommunicationCommand = Readonly<{ action: CommunicationAction; method: 'POST' | 'PUT'; url: string; body: string; key: string; expectedId: string; etag?: string }>;
export class CommunicationError extends QuoteError {
  constructor(status: number) {
    super(status);
    this.message = status === 401 ? 'Your session has ended. Sign in again.' : status === 403 ? 'Your current access does not allow this action.'
      : status === 404 ? 'This record, recipient or attachment is no longer available.' : [409, 412, 428].includes(status) ? 'The saved draft changed. Review the current version before saving your retained text.'
      : [400, 413, 422].includes(status) ? 'Check the text, recipients and attachments. Your inputs are retained.' : 'The result is unconfirmed. Retry the same action.';
  }
}
export async function communicationFetch<T>(url: string, init: RequestInit = {}): Promise<{ data: T; etag: string | null }> {
  const response = await fetch(url, { ...init, cache: 'no-store', signal: init.signal ?? AbortSignal.timeout(15_000) });
  if (!response.ok) {
    if (response.status === 409) { let code: unknown; try { code = (await response.json() as { code?: unknown }).code; } catch { /* Safe fixed errors only. */ }
      if (code === 'command-busy') throw new Error('This action is still being confirmed. Retry the same action.'); }
    throw new CommunicationError(response.status);
  }
  return { data: await response.json() as T, etag: response.headers.get('ETag') };
}
function text(value: unknown, maximum: number, blank = false): value is string { return typeof value === 'string' && value.length <= maximum && (blank || !!value.trim()); }
function ids(value: unknown, maximum: number): value is string[] { return Array.isArray(value) && value.length <= maximum && value.every(validTaskId) && new Set(value.map(x => x.toLowerCase())).size === value.length; }
export function communicationCommand(action: CommunicationAction, id: string, input: Record<string, unknown>, etag?: string, key = crypto.randomUUID()): PendingCommunicationCommand {
  if (!validTaskId(id) || !text(key, 200) || key.length < 16 || key !== key.trim()) throw new Error('Choose a saved record before continuing.');
  let fields: string[], url: string;
  if (action === 'note') { if (!text(input.body, 8000)) throw new Error('Enter an internal note of up to 8,000 characters.'); fields = ['body']; url = `/api/v1/records/${id}/notes`; }
  else if (action === 'thread') {
    if (!text(input.subject, 300) || !['internal', 'agency'].includes(String(input.visibility)) || input.visibility === 'agency' && !validTaskId(input.relationshipId)) throw new Error('Choose an audience and enter a subject.');
    fields = input.visibility === 'agency' ? ['visibility', 'subject', 'relationshipId'] : ['visibility', 'subject']; url = `/api/v1/records/${id}/threads`;
  } else if (action === 'create-draft' || action === 'update-draft') {
    if (!text(input.body, 8000, true) || !ids(input.recipientContactIds, 50) || !ids(input.attachmentVersionIds, 20)) throw new Error('Review the message text and selected recipients and versions.');
    if (action === 'update-draft' && !validQuoteEtag(etag)) throw new Error('Read the current saved draft before editing.');
    fields = ['body', 'recipientContactIds', 'attachmentVersionIds']; url = action === 'create-draft' ? `/api/v1/threads/${id}/messages` : `/api/v1/messages/${id}`;
  } else if (action === 'send-message') {
    if (!validQuoteEtag(etag)) throw new Error('Read the current saved draft before sending.');
    fields = []; url = `/api/v1/messages/${id}/send`;
  } else if (action === 'send-pack') {
    if (!text(input.subject,300) || !text(input.body,8000) || !ids(input.recipientContactIds,50) || !input.recipientContactIds.length || !ids(input.documentVersionIds,20) || !input.documentVersionIds.length) throw new Error('Enter a subject and message, and select recipients and ready file versions.');
    fields = ['subject','body','recipientContactIds','documentVersionIds']; url = `/api/v1/records/${id}/document-deliveries`;
  } else if (['retry-message','resend-message','retry-document','resend-document'].includes(action)) {
    if (!validQuoteEtag(etag) || !text(input.reason,1000)) throw new Error('Read the current delivery and enter a reason.');
    const [operation,kind] = action.split('-'); fields = ['reason']; url = `/api/v1/${kind}-deliveries/${id}/${operation}`;
  } else if (action === 'track-response' || action === 'resolve-response') {
    if (!text(input.reason,2000) || input.reason.trim().length < 10) throw new Error('Enter a reason of 10 to 2,000 characters.');
    if (action === 'resolve-response' && (!validQuoteEtag(etag) || !['response-received','withdrawn'].includes(String(input.outcome)))) throw new Error('Refresh the saved request and choose a closure outcome.');
    fields = action === 'track-response' ? ['reason'] : ['outcome','reason'];
    url = action === 'track-response' ? `/api/v1/messages/${id}/agency-response` : `/api/v1/agency-responses/${id}/resolve`;
  } else throw new Error('Choose a supported communication action.');
  if (Object.keys(input).some(x => !fields.includes(x)) || fields.some(x => !Object.hasOwn(input, x))) throw new Error('Review the communication fields.');
  return Object.freeze({ action, method: action === 'update-draft' ? 'PUT' : 'POST', url, body: JSON.stringify(input), key, expectedId: id, ...(etag ? { etag } : {}) });
}
export async function sendCommunicationCommand(command: PendingCommunicationCommand, csrf: string): Promise<{ id: string }> {
  if (!csrf) throw new Error('The security token is unavailable. Retry this save.');
  const result = await communicationFetch<Record<string, unknown>>(command.url, { method: command.method, body: command.body,
    headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf, 'Idempotency-Key': command.key, ...(command.etag ? { 'If-Match': command.etag } : {}) } });
  const saved = result.data, input = JSON.parse(command.body) as Record<string, unknown>;
  const unconfirmed = () => new Error('The saved communication could not be confirmed. Retry the same action.');
  if (command.action === 'track-response' || command.action === 'resolve-response') {
    if (!saved || !validTaskId(saved.id) || !validQuoteEtag(result.etag) || saved.etag !== result.etag || !text(saved.reference,40) || !text(saved.instruction,8000) ||
      (command.action === 'track-response' ? saved.messageId !== command.expectedId || saved.state !== 'awaiting-response' || saved.reason !== String(input.reason).trim() : saved.id !== command.expectedId || saved.state !== input.outcome || saved.resolutionReason !== String(input.reason).trim())) throw unconfirmed();
    return {id:saved.id};
  }
  if (command.action.startsWith('send-') || command.action.startsWith('retry-') || command.action.startsWith('resend-')) {
    if (!saved || !validTaskId(saved.id) || saved.kind !== 'operational-delivery' || saved.state !== 'pending' || !Number.isInteger(saved.attempts) || !validQuoteEtag(result.etag)) throw unconfirmed();
    return { id: saved.id };
  }
  if (!saved || !validTaskId(saved.id) || !text(saved.authorLabel, 300) || typeof saved.createdAt !== 'string' || !Number.isFinite(Date.parse(saved.createdAt))) throw unconfirmed();
  if (command.action === 'note' || command.action === 'thread') {
    if (saved.subjectRecordId !== command.expectedId || Object.entries(input).some(([key, value]) => saved[key] !== value)) throw unconfirmed();
  } else {
    if (!validQuoteEtag(result.etag) || saved.etag !== result.etag || saved.state !== 'draft' || saved.body !== input.body ||
      (command.action === 'create-draft' ? saved.threadId : saved.id) !== command.expectedId) throw unconfirmed();
    for (const field of ['recipientContactIds', 'attachmentVersionIds']) if (!ids(saved[field], field === 'recipientContactIds' ? 50 : 20) ||
      JSON.stringify([...saved[field] as string[]].sort()) !== JSON.stringify([...(input[field] as string[])].sort())) throw unconfirmed();
  }
  return { id: saved.id };
}
