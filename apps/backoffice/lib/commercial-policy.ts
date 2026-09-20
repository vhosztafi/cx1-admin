import type {CommercialProposal} from './commercial-capture';
import type {IssuedPolicySnapshot, PolicyTemporalView, PolicyView} from './policies-api';

export type CommercialIssuedSnapshot = Omit<CommercialProposal, 'format' | 'termIntent' | 'insured' | 'risk' | 'cover'> & {
  snapshotFormat: 'issued-commercial-1';
  insured: NonNullable<CommercialProposal['insured']>;
  risk: NonNullable<CommercialProposal['risk']>;
  cover: NonNullable<CommercialProposal['cover']>;
  term: IssuedPolicySnapshot['term']; premium: IssuedPolicySnapshot['premium'];
  provenance: {source: string; quoteRevisionId: string; authorityVersionId: string};
};
export type CommercialPolicyView = Omit<PolicyView, 'snapshot'> & {snapshot: CommercialIssuedSnapshot; commercialExposureDecisionId: string};
export type AnyPolicyTemporalView = PolicyTemporalView | CommercialPolicyView;
export function isCommercialPolicy(policy: PolicyView | CommercialPolicyView): policy is CommercialPolicyView {
  return policy.snapshot.productCode === 'commercial-combined';
}
