import { QuoteError } from './quotes.ts';
import { validTaskId } from './tasks-api.ts';
import type { OpsDocumentVersion, OpsFileUpload, OpsDocumentGenerationChoice, OpsDocumentGenerationOptions } from '../../../contracts/generated/operations.ts';

export type DocumentVersion = OpsDocumentVersion;
export type FileUpload = OpsFileUpload;
export type DocumentChoice = OpsDocumentGenerationChoice;
export type DocumentOptions = OpsDocumentGenerationOptions;
export type DocumentSource = DocumentChoice['source'];
export const maximumDocumentBytes = 20 * 1024 * 1024;
export const documentKinds = [
  ['evidence', 'Evidence'], ['quotation', 'Quotation'], ['statement-of-fact', 'Statement of fact'],
  ['policy-schedule', 'Policy schedule'], ['policy-certificate', 'Certificate'], ['endorsement', 'Endorsements'],
  ['renewal-invitation', 'Renewal invitation'], ['cancellation-notice', 'Cancellation notice'],
] as const;
const mediaTypes = ['application/pdf', 'image/png', 'image/jpeg'];
type UploadProof = Pick<FileUpload, 'id' | 'subjectRecordId' | 'name' | 'mediaType' | 'byteLength' | 'sha256' | 'state'>;
export type PendingDocumentCommand = Readonly<{ action: 'generate' | 'upload'; url: string; body: string; key: string; upload?: Readonly<UploadProof> }>;
export type PendingFileUpload = Readonly<{ url: string; subjectId: string; file: File; mediaType: string; sha256: string; key: string }>;

export class DocumentError extends QuoteError {
  constructor(status: number) {
    super(status);
    this.message = status === 401 ? 'Your session has ended. Sign in again; your selected file and details are retained.'
      : status === 403 ? 'Your current access does not allow this document action.'
      : status === 404 ? 'This document or linked record is no longer available.'
      : [409, 412, 428].includes(status) ? 'The document is not available for this action. Refresh its saved status before continuing.'
      : [400, 413, 415, 422].includes(status) ? 'Check the document details and choose a supported file of up to 20 MiB. Your inputs are retained.'
      : 'The service could not confirm the result. Retry the same action.';
  }
}
export async function documentFetch<T>(url: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(url, { ...init, cache: 'no-store', signal: init.signal ?? AbortSignal.timeout(15_000) });
  if (!response.ok) {
    if (response.status === 409) {
      let code: unknown; try { code = (await response.json() as { code?: unknown }).code; } catch { /* No server diagnostics in UI copy. */ }
      if (code === 'command-busy') throw new Error('The service is still confirming this action. Retry the same action.');
    }
    throw new DocumentError(response.status);
  }
  return await response.json() as T;
}
function object(value: unknown): value is Record<string, unknown> { return !!value && typeof value === 'object' && !Array.isArray(value); }
function text(value: unknown, max: number): value is string { return typeof value === 'string' && !!value.trim() && value.length <= max && !/[\p{Cc}]/u.test(value); }
function keys(value: Record<string, unknown>, required: string[], optional: string[] = []) {
  if (required.some(key => !Object.hasOwn(value, key)) || Object.keys(value).some(key => !required.includes(key) && !optional.includes(key))) throw new Error('Review the selected document details.');
}
function key(value: string) { if (!text(value, 200) || value.length < 16 || value !== value.trim()) throw new Error('Prepare this action again.'); return value; }
function sameId(a: unknown, b: unknown) { return validTaskId(a) && validTaskId(b) && a.toLowerCase() === b.toLowerCase(); }
const unconfirmed = () => new Error('The saved document result could not be confirmed. Retry the same action.');

export function legacyEvidenceUrl(kind: 'quote' | 'agency' | 'servicing-draft', parentId: string, fileId?: string): string {
  if (!['quote', 'agency', 'servicing-draft'].includes(kind) || !validTaskId(parentId) || fileId !== undefined && !validTaskId(fileId)) throw new Error('Choose an original evidence file and record.');
  const family = kind === 'quote' ? 'quotes' : kind === 'agency' ? 'agencies' : 'drafts';
  return `/api/v1/${family}/${parentId.toLowerCase()}/evidence-files${fileId ? `/${fileId.toLowerCase()}/content` : ''}`;
}

export function documentOptionsUrl(subjectId: string, source: DocumentSource, cursor?: string): string {
  // Validate the closed source union without choosing a template or a latest version.
  const sourceId = source.kind === 'policy-version' ? source.policyVersionId : source.kind === 'quote-revision' ? source.quoteRevisionId : source.termsVersionId;
  documentCommand('generate', subjectId, { kind: 'statement-of-fact', source, templateVersionId: sourceId, visibility: 'internal', reason: 'Select an applicable document template' });
  if (cursor !== undefined && !text(cursor, 2048)) throw new Error('Refresh the available document templates.');
  const query = new URLSearchParams({ sourceKind: source.kind, sourceId, pageSize: '25' });
  if (source.kind === 'quote-revision' && source.quoteTermsVersionId) query.set('quoteTermsVersionId', source.quoteTermsVersionId);
  if (cursor) query.set('cursor', cursor);
  return `/api/v1/records/${subjectId.toLowerCase()}/documents/options?${query}`;
}

export function documentCommand(action: 'generate' | 'upload', subjectId: string, input: Record<string, unknown>, commandKey = crypto.randomUUID(), upload?: UploadProof): PendingDocumentCommand {
  if (!validTaskId(subjectId) || !['generate', 'upload'].includes(action) || !object(input)) throw new Error('Choose a saved document and linked record.');
  keys(input, ['kind', 'visibility', 'reason', ...(action === 'generate' ? ['source', 'templateVersionId'] : ['uploadId'])], ['documentId', 'relationshipId']);
  if (!documentKinds.some(([kind]) => kind === input.kind) || action === 'generate' && input.kind === 'evidence' || !text(input.reason, 1000)) throw new Error('Choose a document kind and enter a reason of up to 1,000 characters.');
  if (input.documentId !== undefined && !validTaskId(input.documentId)) throw new Error('Choose a saved document for the new version.');
  if (!(input.visibility === 'agency' ? validTaskId(input.relationshipId) : ['internal', 'insurer'].includes(String(input.visibility)) && input.relationshipId === undefined)) throw new Error('Choose a valid document audience.');
  if (action === 'generate') {
    const source = input.source;
    if (!validTaskId(input.templateVersionId) || !object(source)) throw new Error('Choose a saved source and template.');
    if (source.kind === 'policy-version') {
      keys(source, ['kind', 'policyVersionId']);
      if (!validTaskId(source.policyVersionId) || !['statement-of-fact', 'policy-schedule', 'policy-certificate', 'endorsement', 'cancellation-notice'].includes(String(input.kind))) throw new Error('Choose an applicable policy document.');
    } else if (source.kind === 'quote-revision') {
      keys(source, ['kind', 'quoteRevisionId'], ['quoteTermsVersionId']);
      if (!validTaskId(source.quoteRevisionId) || source.quoteTermsVersionId !== undefined && !validTaskId(source.quoteTermsVersionId) ||
        !(input.kind === 'statement-of-fact' || input.kind === 'quotation' && validTaskId(source.quoteTermsVersionId))) throw new Error('Choose exact saved quotation terms or a statement of fact.');
    } else if (source.kind === 'servicing-terms') {
      keys(source, ['kind', 'termsVersionId']);
      if (!validTaskId(source.termsVersionId) || !['quotation', 'statement-of-fact', 'renewal-invitation'].includes(String(input.kind))) throw new Error('Choose applicable saved servicing terms.');
    } else throw new Error('Choose a supported saved document source.');
  } else if (!upload || !sameId(input.uploadId, upload.id) || !sameId(subjectId, upload.subjectRecordId) || upload.state === 'quarantined') throw new Error('Confirm the uploaded file for this record before attaching it.');
  return Object.freeze({ action, url: `/api/v1/records/${subjectId.toLowerCase()}/documents/${action}`, body: JSON.stringify(input), key: key(commandKey), ...(upload ? { upload: Object.freeze({ ...upload }) } : {}) });
}

export function validDocumentVersion(value: unknown): value is DocumentVersion {
  if (!object(value) || !validTaskId(value.id) || !validTaskId(value.documentId) || !Number.isSafeInteger(value.number) || Number(value.number) < 1 ||
    !documentKinds.some(([kind]) => kind === value.kind) || !['pending', 'ready', 'failed', 'quarantined'].includes(String(value.state)) ||
    !text(value.originalName, 255) || !mediaTypes.includes(String(value.contentType)) || !Number.isSafeInteger(value.bytes) || Number(value.bytes) < 0 || Number(value.bytes) > maximumDocumentBytes ||
    typeof value.createdAt !== 'string' || !Number.isFinite(Date.parse(value.createdAt))) return false;
  if (value.sourceVersionId !== undefined && value.sourceVersionId !== null && !validTaskId(value.sourceVersionId) || value.templateVersionId !== undefined && value.templateVersionId !== null && !validTaskId(value.templateVersionId)) return false;
  return value.state !== 'ready' || Number(value.bytes) > 0 && typeof value.sha256 === 'string' && /^[a-f0-9]{64}$/.test(value.sha256);
}
export async function sendDocumentCommand(command: PendingDocumentCommand, csrf: string): Promise<DocumentVersion> {
  if (!csrf) throw new Error('The security token is unavailable. Retry this action.');
  const saved = await documentFetch<unknown>(command.url, { method: 'POST', body: command.body,
    headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf, 'Idempotency-Key': command.key } });
  const input = JSON.parse(command.body) as Record<string, unknown>;
  if (!validDocumentVersion(saved) || saved.kind !== input.kind || input.documentId !== undefined && !sameId(saved.documentId, input.documentId)) throw unconfirmed();
  if (command.action === 'generate') {
    const source = input.source as Record<string, unknown>;
    if (!sameId(saved.templateVersionId, input.templateVersionId) || !sameId(saved.sourceVersionId, source.policyVersionId ?? source.quoteRevisionId ?? source.termsVersionId) || saved.contentType !== 'application/pdf') throw unconfirmed();
  } else if (!command.upload || saved.originalName !== command.upload.name || saved.bytes !== command.upload.byteLength || saved.sha256 !== command.upload.sha256 || saved.contentType !== command.upload.mediaType) throw unconfirmed();
  return saved;
}

export async function prepareFileUpload(subjectId: string, file: File, commandKey = crypto.randomUUID()): Promise<PendingFileUpload> {
  const retainedKey = key(commandKey);
  if (!validTaskId(subjectId) || !(file instanceof File) || file.size < 1 || file.size > maximumDocumentBytes) throw new Error('Choose a non-empty PDF, PNG or JPEG of up to 20 MiB.');
  if (!text(file.name, 255) || file.name !== file.name.trim() || file.name.startsWith('.') || file.name.endsWith('.') || /[\p{Cf}\p{Cs}/\\:<>"|?*]/u.test(file.name)) throw new Error('Choose a file with a plain, readable filename.');
  const mediaType = file.type || (/\.pdf$/i.test(file.name) ? 'application/pdf' : /\.png$/i.test(file.name) ? 'image/png' : /\.jpe?g$/i.test(file.name) ? 'image/jpeg' : '');
  if (!mediaTypes.includes(mediaType)) throw new Error('Choose a PDF, PNG or JPEG file.');
  const sha256 = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', await file.arrayBuffer())), byte => byte.toString(16).padStart(2, '0')).join('');
  return Object.freeze({ url: `/api/v1/records/${subjectId.toLowerCase()}/file-uploads?name=${encodeURIComponent(file.name)}`, subjectId: subjectId.toLowerCase(), file, mediaType, sha256, key: retainedKey });
}
export async function sendFileUpload(command: PendingFileUpload, csrf: string): Promise<FileUpload> {
  if (!csrf) throw new Error('The security token is unavailable. Retry this upload.');
  const saved = await documentFetch<unknown>(command.url, { method: 'POST', body: command.file, signal: AbortSignal.timeout(120_000),
    headers: { 'Content-Type': command.mediaType, 'X-CSRF-Token': csrf, 'Idempotency-Key': command.key } });
  if (!object(saved) || !validTaskId(saved.id) || !sameId(saved.subjectRecordId, command.subjectId) || saved.name !== command.file.name || saved.mediaType !== command.mediaType ||
    saved.byteLength !== command.file.size || saved.sha256 !== command.sha256 || !['pending', 'ready', 'quarantined'].includes(String(saved.state)) ||
    typeof saved.createdAt !== 'string' || !Number.isFinite(Date.parse(saved.createdAt))) throw unconfirmed();
  return saved as FileUpload;
}
export function documentContentUrl(version: DocumentVersion, preview = false) {
  if (!validDocumentVersion(version) || version.state !== 'ready') throw new Error('This exact document version is not ready to download.');
  return `/api/v1/document-versions/${version.id.toLowerCase()}/${preview ? 'preview' : 'content'}`;
}
