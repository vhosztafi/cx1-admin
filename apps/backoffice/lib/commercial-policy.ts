import type {CommercialProposal} from './commercial-capture';
import type {QuoteObject} from './quotes';
import type {IssuedPolicySnapshot, PolicyTemporalView, PolicyView} from './policies-api';

export type CommercialIssuedSnapshot = Omit<CommercialProposal, 'format' | 'termIntent' | 'insured' | 'risk' | 'cover'> & {
  snapshotFormat: 'issued-commercial-1' | 'issued-commercial-servicing-1' | 'issued-commercial-cancellation-1';
  insured: NonNullable<CommercialProposal['insured']>;
  risk: NonNullable<CommercialProposal['risk']>;
  cover: NonNullable<CommercialProposal['cover']>;
  term: IssuedPolicySnapshot['term']; premium: IssuedPolicySnapshot['premium'];
  provenance: {source: string; quoteRevisionId?: string; authorityVersionId?: string; revisionId?: string; servicingIssueDecisionId?: string};
};
export type CommercialPolicyView = Omit<PolicyView, 'snapshot'> & {snapshot: CommercialIssuedSnapshot; commercialExposureDecisionId: string};
export type AnyPolicyTemporalView = PolicyTemporalView | CommercialPolicyView;
export function isCommercialPolicy(policy: PolicyView | CommercialPolicyView): policy is CommercialPolicyView {
  return policy.snapshot.productCode === 'commercial-combined';
}
export const commercialPolicyCoverage = (state: CommercialPolicyView['coverageState']) => ({active:'In force',scheduled:'Inception scheduled',expired:'Term ended',cancelled:'Cancelled'})[state];
export function formatCommercialMoney(value:string):string {
  if(!/^-?(0|[1-9][0-9]{0,17})\.[0-9]{2}$/.test(value))return 'Unavailable';
  const negative=value.startsWith('-'),[whole,fraction]=(negative?value.slice(1):value).split('.');
  return `${negative?'−':''}£${whole.replace(/\B(?=(\d{3})+(?!\d))/g,',')}.${fraction}`;
}
export function commercialPropertySummary(locations: QuoteObject[]) {
  const totals = locations.map(location => {
    const amounts = [location.buildings, location.contents, location.stock];
    if (amounts.some(x => typeof x !== 'string' || !/^(0|[1-9][0-9]{0,12})\.[0-9]{2}$/.test(x))) return undefined;
    return amounts.reduce<bigint>((sum,x) => sum + BigInt((x as string).replace('.', '')), BigInt(0));
  });
  const money = (value: bigint) => `${value/BigInt(100)}.${String(value%BigInt(100)).padStart(2,'0')}`;
  const complete = totals.length > 0 && totals.every(x => x !== undefined);
  return {total:complete?money(totals.reduce<bigint>((sum,x)=>sum+x!,BigInt(0))):undefined,
    demoMel:complete?money(totals.reduce<bigint>((max,x)=>x!>max?x!:max,BigInt(0))):undefined,
    locationTotals:totals.map(x=>x===undefined?undefined:money(x))};
}
