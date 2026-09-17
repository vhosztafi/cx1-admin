import { londonCandidates } from './quote-term.ts';
import { validQuoteEtag } from './quotes.ts';
import type { ServicingEffectiveIntent, ServicingProposal } from './servicing-api';

export function servicingDateFeedback(intent: ServicingEffectiveIntent): { instant?: number; error?: string } {
  const choices = londonCandidates(intent.localDate, intent.localTime);
  if (!choices.length) return { error: 'Enter a valid London date and time. The clocks skip some times in spring.' };
  if (choices.length > 1 && intent.utcOffsetMinutes === undefined) return { error: 'This time occurs twice. Choose GMT or British Summer Time.' };
  const chosen = intent.utcOffsetMinutes === undefined ? choices[0] : choices.find(value => value.offset === intent.utcOffsetMinutes);
  return chosen ? { instant: chosen.instant } : { error: 'The selected offset does not match this London date and time.' };
}
export function changeServicingDate(intent: ServicingEffectiveIntent, field: 'localDate' | 'localTime', value: string): ServicingEffectiveIntent {
  const next = { ...intent, [field]: value }; delete next.utcOffsetMinutes; return next;
}
export function setServicingDateBasis(proposal: ServicingProposal, dateBasis: 'shared' | 'per-cover-change'): ServicingProposal {
  if (dateBasis === 'shared' && proposal.changes.some(change => change.effectiveIntent)) throw new Error('Remove the individual cover dates before choosing one shared date.');
  return { ...proposal, dateBasis };
}
export function setCoverEffectiveIntent(proposal: ServicingProposal, changeId: string, intent: ServicingEffectiveIntent | undefined): ServicingProposal {
  const current = proposal.changes.find(change => change.changeId === changeId);
  if (!current || current.kind !== 'cover') throw new Error('Only an existing cover change can use an individual date.');
  if (intent && proposal.dateBasis !== 'per-cover-change') throw new Error('Choose individual cover dates first.');
  return { ...proposal, changes: proposal.changes.map(change => {
    if (change.changeId !== changeId) return change;
    const next = { ...change }; if (intent) next.effectiveIntent = { ...intent }; else delete next.effectiveIntent; return next;
  }) };
}
export function matchingServicingEditor(view: { etag: string; data: { id: string; revisionId: string } }, editor: { etag: string | null; data: { draftId: string; revisionId: string } } | null): boolean {
  return !!(validQuoteEtag(editor?.etag) && editor!.etag === view.etag && editor!.data.draftId === view.data.id && editor!.data.revisionId === view.data.revisionId);
}
