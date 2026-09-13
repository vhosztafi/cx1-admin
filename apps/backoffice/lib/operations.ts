export type Job = { id: string; kind: string; state: 'pending' | 'leased' | 'succeeded' | 'failed'; attempts: number; attemptLimit?: number; retryAllowed?: boolean; nextAttemptAt?: string; completedAt?: string; errorCode?: string; resultResourceId?: string };
export type Integration = { id: string; scenario: string; version: number; enabled: boolean; maxAttempts: number; retrySeconds: number[] };
export type AuditEntry = { id: string; actorLabel: string; eventType: string; occurredAt: string; subjectRecordId?: string; summary: string; correlationId: string };
export type Page<T> = { items: T[]; totalCount?: number; nextCursor?: string };

export function operationError(status: number): string {
  if (status === 401) return 'Your session has ended. Sign in again to continue.';
  if (status === 403) return 'This action is unavailable for your current access. Refresh the page and try again.';
  if (status === 400) return 'The request or page cursor is no longer valid. Refresh the list and try again.';
  if (status === 404) return 'This record or operation is unavailable.';
  if (status === 409) return 'This recovery cannot be applied. Refresh the jobs and check their current status.';
  if (status === 412 || status === 428) return 'A selected job changed. Refresh the jobs and select again.';
  if (status === 422) return 'Check the selection and enter a reason of up to 1000 characters.';
  return 'The service could not confirm the result. Refresh the list or retry the same action.';
}

export async function operationalFetch<T>(url: string, init: RequestInit = {}): Promise<{ data: T; etag: string | null }> {
  const response = await fetch(url, { ...init, cache: 'no-store', signal: init.signal ?? AbortSignal.timeout(15_000) });
  if (!response.ok) throw new Error(operationError(response.status));
  return { data: await response.json() as T, etag: response.headers.get('ETag') };
}
export function jobTone(state: Job['state']): 'success' | 'error' | 'warning' | 'info' {
  return state === 'succeeded' ? 'success' : state === 'failed' ? 'error' : state === 'leased' ? 'warning' : 'info';
}
