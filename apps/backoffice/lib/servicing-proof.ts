import { quoteFetch, validQuoteEtag } from './quotes.ts';

export type ProofRequirement = { code: string; label: string; path: string; riskItemId: string | null; effectiveDates: string[]; inputFingerprint: string;
  capacitySubmissionId?: string | null;
  context: { draftId: string; cycleId: string; revisionId: string; ratingId: string } };
export type ProofRequirements = { draftId: string; cycleId: string; draftEtag: string; applicable: boolean; requirements: { requirement: ProofRequirement; satisfied: boolean }[] };
export type ProofFile = { id: string; fileName: string; contentType: string; byteLength: number; screeningState: string; screeningMethod: string; createdAt: string };
export type ProofAssociation = { id: string; cycleId: string; revisionId: string; ratingId: string; fileId: string; fileName: string; code: string; riskItemId: string | null;
  capacitySubmissionId?: string | null;
  inputFingerprint: string; reason: string; etag: string; latestReviewId: string | null; reviewOutcome: string | null; withdrawn: boolean; createdAt: string };
export type ProofEvent = { id: string; sequence: number; kind: string; outcome: string | null; reason: string; actorId: string; authorityVersionId: string | null; recordedAt: string };
export type ProofPage<T> = { items: T[]; nextCursor: string | null; draftEtag: string };
export type ProofScope = Readonly<{ draftId: string; cycleId: string; revisionId: string; etag: string; fence: string }>;
export type ProofCommand = Readonly<{ scope: ProofScope; url: string; key: string; body?: string; file?: File }>;
export type ProofReceipt = { id: string; draftId: string; revisionId: string; cycleId: string; draftEtag: string };
const id = (value: string) => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000';

export function proofReadState<T>(read: {url:string; etag:string; data:T} | null, url:string | null, etag:string, error:string) {
  // Retain same-page forms while refreshing a version (including lease renewal),
  // but never authorize writes with the retained stale result.
  return {data:read?.url===url?read.data:null,current:!!read && read.url===url && read.etag===etag && !error};
}

export function currentProof(association: ProofAssociation, requirement: ProofRequirement): boolean {
  return !association.withdrawn && association.cycleId === requirement.context.cycleId && association.revisionId === requirement.context.revisionId &&
    association.ratingId === requirement.context.ratingId && association.code === requirement.code && association.riskItemId === requirement.riskItemId &&
    association.inputFingerprint === requirement.inputFingerprint && (association.capacitySubmissionId ?? null) === (requirement.capacitySubmissionId ?? null);
}

// Snapshot every retry-sensitive value, including the immutable File. Never rebuild
// this object from refreshed draft data after an uncertain response.
export function proofCommand(scope: ProofScope, path: string, body?: unknown, file?: File): ProofCommand {
  if (![scope.draftId, scope.cycleId, scope.revisionId, scope.fence].every(id) || !validQuoteEtag(scope.etag)) throw new Error('Refresh the saved draft and acquire its editing lease.');
  const child = '[0-9a-f-]{36}';
  if (!new RegExp(`^/(submit|evidence/uploads|evidence|evidence/${child}/(reviews|withdraw)|referrals/decisions|referrals/${child}/decisions|conditions/${child}/resolutions)$`, 'i').test(path)) throw new Error('Invalid servicing action.');
  if (path === '/evidence/uploads') {
    if (!file || body !== undefined || !file.size || file.size > 10 * 1024 * 1024 || !['application/pdf', 'image/png', 'image/jpeg', 'text/plain'].includes(file.type))
      throw new Error('Choose a PDF, PNG, JPEG or text file up to 10 MiB.');
  } else if (file || !body || typeof body !== 'object' || !('cycleId' in body) || body.cycleId !== scope.cycleId) throw new Error('The command must belong to the current rating cycle.');
  if (path === '/submit' && (!body || typeof body !== 'object' || !('revisionId' in body) || body.revisionId !== scope.revisionId)) throw new Error('Submit the exact saved revision.');
  return Object.freeze({ scope: Object.freeze({ ...scope }), url: `/api/v1/drafts/${scope.draftId}${path}`, key: crypto.randomUUID(), body: body === undefined ? undefined : JSON.stringify(body), file });
}

export function confirmProofReceipt(command: ProofCommand, data: ProofReceipt, etag: string | null): ProofReceipt {
  if (!data || !id(data.id) || !validQuoteEtag(etag) || data.draftEtag !== etag || data.draftId !== command.scope.draftId ||
    data.revisionId !== command.scope.revisionId || data.cycleId !== command.scope.cycleId)
    throw new Error('The saved result could not be confirmed. Retry the same action.');
  return data;
}

export async function sendProof(command: ProofCommand): Promise<ProofReceipt> {
  const { data: csrf } = await quoteFetch<{ requestToken: string }>('/api/v1/auth/csrf');
  let body: BodyInit | undefined = command.body;
  if (command.file) {
    const form = new FormData(); form.set('file', command.file, command.file.name); form.set('fileName', command.file.name); form.set('contentType', command.file.type); body = form;
  }
  const result = await quoteFetch<ProofReceipt>(command.url, { method: 'POST', body, headers: {
    ...(command.file ? {} : { 'Content-Type': 'application/json' }), 'X-CSRF-Token': csrf.requestToken, 'Idempotency-Key': command.key,
    'If-Match': command.scope.etag, 'X-Edit-Lease': command.scope.fence } });
  return confirmProofReceipt(command, result.data, result.etag);
}
