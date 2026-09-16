export type Address = { line1: string; line2?: string; town: string; county?: string; postcode: string; country: 'GB' };
export type ClientWrite = { legalName: string; entityType: string; companyNumber?: string; address: Address };
export type Client = ClientWrite & { id: string; reference: string; createdAt: string; identityState: string };
export type ClientSummary = Client & { agencies: { id: string; name: string; reference: string }[]; records: { state: 'unavailable' } | { state: 'partial'; quoteCount: number; policyState: 'unavailable' } | { state: 'available'; policyCount: number; quoteCount: number }; primaryContactName?: string; tradeActivities?: string[] };
export type Relationship = { id: string; clientId: string; agencyId: string; state: string; agencyName: string; agencyReference: string };
export type Agency = { id: string; reference: string; legalName: string; state: string };
export type Activity = { id: string; occurredAt: string; actorLabel: string; summary: string; recordId?: string; recordKind?: string };
export type Page<T> = { items: T[]; totalCount?: number; nextCursor?: string };
export const entityTypes: Record<string, string> = { 'limited-company': 'Limited company', 'sole-trader': 'Sole trader', partnership: 'Partnership', llp: 'LLP' };
export const canReadClients = (roles: string[]) => roles.some(role => ['servicing', 'underwriter', 'senior-underwriter', 'agency-admin'].includes(role));
export const canWriteClients = (roles: string[]) => roles.some(role => ['servicing', 'underwriter', 'senior-underwriter'].includes(role));
export class ClientError extends Error {
  status: number;
  constructor(status: number) {
    super(status === 401 ? 'Your session has ended. Sign in again.' : status === 403 ? 'Your current access does not allow this action.' : status === 404 ? 'This client record is unavailable.' : status === 412 || status === 428 ? 'This client has changed. Reload the saved identity before editing again.' : status === 422 ? 'Check the required fields and their maximum lengths.' : status === 409 ? 'This change conflicts with an existing record or command. Refresh the record and check its current state.' : status === 400 ? 'The request or page cursor is invalid. Refresh and try again.' : 'The service could not confirm the result. Retry the same action.');
    this.status = status;
  }
}
export async function clientFetch<T>(url: string, init: RequestInit = {}): Promise<{ data: T; etag: string | null }> {
  const response = await fetch(url, { ...init, cache: 'no-store', signal: init.signal ?? AbortSignal.timeout(15_000) });
  if (!response.ok) {
    if (response.status === 401 && typeof window !== 'undefined') window.location.replace('/login');
    throw new ClientError(response.status);
  }
  return { data: await response.json() as T, etag: response.headers.get('ETag') };
}
export const uncertainFailure = (error: unknown) => !(error instanceof ClientError) || error.status >= 500;
export function identityPayload(form: FormData): ClientWrite {
  const value = (key: string) => String(form.get(key) ?? '').trim();
  return { legalName: value('legalName'), entityType: value('entityType'), ...(value('companyNumber') && { companyNumber: value('companyNumber') }), address: {
    line1: value('line1'), town: value('town'), postcode: value('postcode'), country: 'GB',
    ...(value('line2') && { line2: value('line2') }), ...(value('county') && { county: value('county') }),
  } };
}
export const clientDate = (value: string, time = false) => new Intl.DateTimeFormat('en-GB', { dateStyle: 'short', ...(time && { timeStyle: 'short' }), timeZone: 'Europe/London' }).format(new Date(value));
