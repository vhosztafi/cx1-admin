import type { QuoteProposal } from './quotes';

export type TermsDocument = { template: { title: string; notice: string }; expiresAt: string; effectiveDates: string[];
  slices: { effectiveAt: string; proposal: QuoteProposal }[];
  price: { currency: string; premium: string; tax: string; fee: string; brokerCommission: string; grossPayable: string; netDue: string };
  conditions: { id: string; code: string; kind: string; effectiveDates: string[]; definition: Record<string, unknown> }[] };
export type TermsSnapshot = { id: string; sequence: number; ratingId: string; templateVersionId: string; termsHash: string; preparedAt: string; document: TermsDocument; applicable: boolean };
export type TermsRecipient = { id: string; name: string; email: string };
export type TermsDocumentHistory = {termsVersionId:string;title:string;version:number;preparedAt:string;deliveryId:string|null;deliveryState:string|null;sentAt:string|null;recipients:TermsRecipient[]};
export type TermsView = { draftId: string; cycleId: string; revisionId: string; ratingId: string; draftEtag: string; applicable: boolean; assuranceHash: string;
  canPrepare: boolean; canSend: boolean; blockingCode: string | null; sendBlockingCode: string | null; acceptanceApplicable: boolean; terms: TermsSnapshot | null;
  delivery: { id: string; termsVersionId: string; state: string; jobState: string; queuedAt: string; completedAt: string | null; outcomeCode: string | null; recipients: TermsRecipient[] } | null;
  acceptance: { id: string; termsVersionId: string; deliveryId: string; accepterLabel: string; channel: string; acceptedAt: string; recordedAt: string; evidenceAssociationId: string } | null;
  templates: { id: string; code: string; version: number; title: string }[]; recipientOptions: TermsRecipient[] };
export type TermsHistoryItem = { id: string; cycleId: string; revisionId: string; ratingId: string; termsVersionId: string; deliveryId: string | null; recordedAt: string; state: string; label: string };
