import { QuoteError, validQuoteEtag } from './quotes.ts';
import type { OpsTask, OpsAssignment, OpsSubject } from '../../../contracts/generated/operations.ts';

export type TaskView = OpsTask;
export type TaskAssignment = OpsAssignment;
export type TaskSubject = OpsSubject;
export const taskTypes = [
  ['servicing', 'Servicing'], ['underwriting', 'Underwriting'], ['underwriting-referral', 'Underwriting referral'],
  ['authority-referral', 'Authority referral'], ['renewal', 'Renewal'], ['data-exception', 'Data exception'],
  ['agency-onboarding', 'Agency onboarding'], ['complaint', 'Complaint'],
] as const;
export const taskStates = [['open', 'Open'], ['in-progress', 'In progress'], ['awaiting-information', 'Awaiting information'], ['blocked', 'Blocked'], ['completed', 'Completed'], ['cancelled', 'Cancelled']] as const;
export const taskPriorities = [['low', 'Low'], ['normal', 'Medium'], ['high', 'High'], ['urgent', 'Urgent']] as const;
export type TaskPage = { items: TaskView[]; totalCount: number; nextCursor?: string };
export type TaskAction = 'register' | 'create' | 'update' | 'transition' | 'checklist' | 'comment';
export type PendingTaskCommand = Readonly<{ method: 'POST' | 'PUT'; url: string; body: string; key: string; etag?: string; action: TaskAction | 'bulk'; expectedId?: string; selectedIds?: readonly string[] }>;

export class TaskError extends QuoteError {
  constructor(status: number) {
    super(status);
    this.message = status === 401 ? 'Your session has ended. Sign in again.'
      : status === 403 ? 'Your current access does not allow this task action.'
      : status === 404 ? 'This task or linked record is no longer available.'
      : [409, 412, 428].includes(status) ? 'This record changed after you opened it. Review the saved version before applying your changes.'
      : [400, 413, 415, 422].includes(status) ? 'Check the task details. Your inputs are retained.'
      : 'The service could not confirm the result. Retry the same action.';
  }
}
export async function taskFetch<T>(url: string, init: RequestInit = {}): Promise<{ data: T; etag: string | null }> {
  const response = await fetch(url, { ...init, cache: 'no-store', signal: init.signal ?? AbortSignal.timeout(15_000) });
  if (!response.ok) {
    if (response.status === 409) {
      let code: unknown; try { code = (await response.json() as { code?: unknown }).code; } catch { /* Never show arbitrary server diagnostics. */ }
      if (code === 'command-busy') throw new Error('The service is still confirming this action. Retry the same action.');
    }
    throw new TaskError(response.status);
  }
  return { data: await response.json() as T, etag: response.headers.get('ETag') };
}

export const validTaskId = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000';
function text(value: unknown, max: number): value is string { return typeof value === 'string' && !!value.trim() && value.length <= max; }
function object(value: unknown): value is Record<string, unknown> { return !!value && typeof value === 'object' && !Array.isArray(value); }
function keys(body: Record<string, unknown>, required: string[], optional: string[] = []) {
  if (required.some(key => !Object.hasOwn(body, key)) || Object.keys(body).some(key => !required.includes(key) && !optional.includes(key))) throw new Error('Review the task fields before saving.');
}
function assignment(value: unknown) {
  if (!object(value)) throw new Error('Choose an eligible owner or team.');
  const expected = value.kind === 'user' ? ['kind', 'ownerId'] : value.kind === 'team' ? ['kind', 'teamId'] : value.kind === 'unassigned' ? ['kind'] : [];
  if (!expected.length) throw new Error('Choose an eligible owner or team.');
  keys(value, expected); if (value.kind !== 'unassigned' && !validTaskId(value[value.kind === 'user' ? 'ownerId' : 'teamId'])) throw new Error('Choose an eligible owner or team.');
}
function date(value: unknown) {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value) || !Number.isFinite(Date.parse(value)) || new Date(value).toISOString().slice(0, 10) !== value) throw new Error('Enter a valid due date.');
}
function reason(value: unknown) { if (!text(value, 1000)) throw new Error('Enter a reason of up to 1,000 characters.'); }
function commandKey(value: string) { if (!text(value, 200) || value.length < 16 || value !== value.trim() || /[\u0000-\u001f]/.test(value)) throw new Error('The save cannot be prepared. Retry from the form.'); return value; }

export function taskCommand(action: TaskAction, recordId: string, etag: string | null, input: Record<string, unknown>, key = crypto.randomUUID()): PendingTaskCommand {
  if (!['register', 'create', 'update', 'transition', 'checklist', 'comment'].includes(action)) throw new Error('Choose a supported task action.');
  if (!validTaskId(recordId)) throw new Error('Choose a saved linked record.');
  const id = recordId.toLowerCase(); let body = { ...input };
  if (action === 'create' || action === 'update') {
    keys(body, ['typeCode', 'title', 'priority', 'assignment'], ['dueOn']);
    if (!taskTypes.some(([code]) => code === body.typeCode) || !taskPriorities.some(([code]) => code === body.priority) || !text(body.title, 300)) throw new Error('Choose a task type, priority and title.');
    assignment(body.assignment); if (body.dueOn !== undefined) date(body.dueOn);
    if (action === 'create') body = { ...body, subjectRecordId: id };
  } else if (action === 'transition') {
    keys(body, ['state', 'reason']); reason(body.reason);
    if (!taskStates.some(([state]) => state === body.state)) throw new Error('Choose a task status.');
  } else if (action === 'comment') { keys(body, ['body']); if (!text(body.body, 8000)) throw new Error('Enter a comment of up to 8,000 characters.'); }
  else if (action === 'checklist') {
    keys(body, ['items', 'reason']); reason(body.reason);
    if (!Array.isArray(body.items) || !body.items.length || body.items.length > 100) throw new Error('Review the checklist selection.');
    const ids = new Set<string>();
    for (const item of body.items) {
      if (!object(item)) throw new Error('Review the checklist selection.'); keys(item, ['id', 'completed']);
      if (!validTaskId(item.id) || typeof item.completed !== 'boolean' || ids.has(item.id.toLowerCase())) throw new Error('Review the checklist selection.'); ids.add(item.id.toLowerCase());
    }
  } else {
    keys(body, ['kind']); if (!['agency', 'relationship', 'quote', 'policy', 'servicing-draft'].includes(String(body.kind))) throw new Error('Choose a supported linked record.');
    body = { ...body, parentId: id };
  }
  if (action !== 'create' && action !== 'register' && !validQuoteEtag(etag)) throw new Error('Reload the saved task version before saving.');
  const url = action === 'register' ? '/api/v1/operational-subjects' : action === 'create' ? '/api/v1/tasks' : `/api/v1/tasks/${id}${action === 'update' ? '' : action === 'comment' ? '/comments' : `/${action}`}`;
  return Object.freeze({ action, method: action === 'update' || action === 'checklist' ? 'PUT' : 'POST', url, body: JSON.stringify(body), key: commandKey(key),
    ...(etag && action !== 'create' && action !== 'register' ? { etag } : {}), expectedId: id });
}

export function taskBulkCommand(action: 'assign' | 'due' | 'complete', selection: { id: string; etag: string }[], input: Record<string, unknown>, key = crypto.randomUUID()): PendingTaskCommand {
  if (!['assign', 'due', 'complete'].includes(action)) throw new Error('Choose a supported bulk task action.');
  if (selection.length < 1 || selection.length > 100 || selection.some(x => !validTaskId(x.id) || !validQuoteEtag(x.etag))) throw new Error('Select up to 100 saved tasks.');
  const tasks = selection.map(x => ({ id: x.id.toLowerCase(), etag: x.etag }));
  if (new Set(tasks.map(x => x.id)).size !== tasks.length) throw new Error('Each task can be selected once.');
  keys(input, action === 'assign' ? ['assignment', 'reason'] : action === 'due' ? ['dueOn', 'reason'] : ['reason']); reason(input.reason);
  if (action === 'assign') assignment(input.assignment); if (action === 'due') date(input.dueOn);
  const suffix = action === 'assign' ? 'assignment' : action === 'due' ? 'due-date' : 'completion';
  return Object.freeze({ action: 'bulk', method: 'POST', url: `/api/v1/tasks/bulk-${suffix}`, body: JSON.stringify({ tasks, ...input }), key: commandKey(key), selectedIds: Object.freeze(tasks.map(x => x.id)) });
}

export async function sendTaskCommand(command: PendingTaskCommand, csrf: string): Promise<{ id?: string; etag?: string; updatedIds?: string[] }> {
  if (!csrf) throw new Error('The security token is unavailable. Retry this save.');
  const result = await taskFetch<Record<string, unknown>>(command.url, { method: command.method, body: command.body,
    headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf, 'Idempotency-Key': command.key, ...(command.etag ? { 'If-Match': command.etag } : {}) } });
  const saved = result.data, input = JSON.parse(command.body) as Record<string, unknown>;
  const unconfirmed = () => new Error('The saved task result could not be confirmed. Retry the same action.');
  if (!object(saved)) throw unconfirmed();
  if (command.action === 'bulk') {
    const ids = saved.updatedIds;
    if (!Array.isArray(ids) || ids.some(x => !validTaskId(x)) || ids.length !== command.selectedIds?.length || new Set(ids).size !== ids.length || ids.some(x => !command.selectedIds!.includes(x))) throw unconfirmed();
    return { updatedIds: ids as string[] };
  }
  if (!validTaskId(saved.id)) throw unconfirmed();
  if (command.action === 'register') {
    if (saved.parentId !== command.expectedId || saved.kind !== input.kind) throw unconfirmed();
    return { id: saved.id };
  }
  if (!validQuoteEtag(result.etag)) throw unconfirmed();
  if (command.action === 'create' ? saved.subjectRecordId !== command.expectedId : command.action === 'comment' ? saved.taskId !== command.expectedId : saved.id !== command.expectedId) throw unconfirmed();
  if (command.action !== 'comment' && (saved.etag !== result.etag || command.action === 'transition' && saved.state !== input.state)) throw unconfirmed();
  return { id: saved.id, etag: result.etag };
}
