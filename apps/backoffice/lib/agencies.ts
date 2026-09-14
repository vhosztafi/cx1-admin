export type AgencyJson = { [key: string]: string | number | AgencyJson };
export type AgencyProduct = { id?: string; productVersionId: string; effectiveFrom: string; brokerCommissionBasisPoints: number; productCode?: string };
export type AgencyDraft = { id: string; reference: string; state: string; onboardingStep: number; details: AgencyJson; validation: { valid: boolean; items: { code: string; path: string; stage: number; state: string; message: string }[] }; unavailableSections: { kind: string; message: string }[] };
export type CatalogProduct = { productVersionId: string; productCode: string; name: string; capacityProviderName: string; ratingReady: boolean; distributionEligible: boolean };
export type AgencySummary = { id: string; reference: string; legalName?: string; state: string; onboardingStep: number; relationshipManagerName?: string; mainContactName?: string; productCodes: string[]; lastActivityAt?: string };
export type Field = { path: string; label: string; stage: number; type?: 'date' | 'email' | 'money' | 'percent' | 'days'; max?: number; options?: Record<string, string | number> };
export const stages = ['Agency & regulatory','Contacts','Products & commission','Agreement & compliance','Accounts','Review & activate'];
export const canReadAgencies = (roles: string[]) => roles.some(x => ['agency-admin','system-admin','underwriter','senior-underwriter'].includes(x));
export const canWriteAgencies = (roles: string[]) => roles.some(x => ['agency-admin','system-admin'].includes(x));
export class AgencyError extends Error {
  status: number;
  constructor(status: number) { super(status === 401 ? 'Your session has ended. Sign in again.' : status === 403 ? 'Your current access does not allow this action.' : status === 412 || status === 428 ? 'This agency has changed. Your entries are retained; reload the saved draft before editing again.' : status === 422 || status === 400 || status === 413 ? 'Check the agency fields, selected products and value formats.' : status === 404 ? 'This agency is unavailable.' : status === 409 ? 'This change conflicts with the current agency or command. Reload the saved record.' : 'The service could not confirm the result. Retry the same action.'); this.status = status; }
}
export async function agencyFetch<T>(url: string, init: RequestInit = {}): Promise<{ data: T; etag: string | null }> {
  const response = await fetch(url,{...init,cache:'no-store',signal:init.signal ?? AbortSignal.timeout(15_000)});
  if (!response.ok) { if(response.status === 401 && typeof window !== 'undefined') window.location.replace('/login'); throw new AgencyError(response.status); }
  return {data:await response.json() as T,etag:response.headers.get('ETag')};
}
export function flattenDraft(input: AgencyJson, prefix = '', result: Record<string,string> = {}): Record<string,string> {
  for(const [key,value] of Object.entries(input)) { const path = prefix ? `${prefix}.${key}` : key; if(typeof value === 'object') flattenDraft(value,path,result); else result[path] = typeof value === 'number' && path.endsWith('BasisPoints') ? (value / 100).toFixed(2) : String(value); }
  return result;
}
export function exactMoney(value: string): string {
  if(!/^(0|[1-9]\d{0,16})(\.\d{1,2})?$/.test(value)) throw new Error('Enter a nonnegative amount with at most two decimal places.');
  const [whole,fraction = ''] = value.split('.'); return `${whole}.${fraction.padEnd(2,'0')}`;
}
export function basisPoints(value: string): number {
  const money = exactMoney(value); const [whole,fraction] = money.split('.');
  if(whole.length > 3) throw new Error('Commission must be between 0 and 100%.');
  const bps = Number(whole) * 100 + Number(fraction); if(bps > 10000) throw new Error('Commission must be between 0 and 100%.'); return bps;
}
export function draftPayload(values: Record<string,string>, fields: Field[]): AgencyJson {
  const result: AgencyJson = {};
  for(const field of fields) {
    const value = values[field.path]?.trim(); if(!value) continue;
    const parts = field.path.split('.'); let owner = result;
    for(const part of parts.slice(0,-1)) owner = (owner[part] ??= {}) as AgencyJson;
    owner[parts.at(-1)!] = field.type === 'percent' ? basisPoints(value) : field.type === 'money' ? exactMoney(value) : field.type === 'days' ? Number(value) : value;
  }
  return result;
}
