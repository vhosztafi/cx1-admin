import { reconcileQuoteCover } from '../../../scripts/quote-cover-reconciliation.mjs';
import type { DynamicCatalogue } from '../../../scripts/quote-dynamic-options.mjs';
import type { QuoteProposal } from './quotes';
export function coverExcessOptions(proposal: QuoteProposal, catalogue: DynamicCatalogue | undefined) {
  const facts = catalogue ? reconcileQuoteCover(proposal, catalogue).facts : {};
  const own = catalogue?.collections.indemnityOwnVehicles?.find(row => typeof row.numericValue === 'number' && row.numericValue.toFixed(2) === facts.ownVehicleLimit);
  const collection = own ? `indemnityOwnVehicles/number:${own.value}/excesses` : undefined;
  const active = Boolean(facts.coverLevel !== undefined && facts.coverLevel !== 'third-party-only' && own && (facts.coverLevel !== 'third-party-fire-theft' || Number(facts.ownVehicleLimit) <= 15000));
  return { active, collection, choices: active && collection ? (catalogue?.collections[collection] ?? []).map(({ value, text }) => ({ value, text })) : [] };
}
