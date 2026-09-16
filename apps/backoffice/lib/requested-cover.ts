import type { QuoteObject, QuoteProposal } from './quotes';
import { amountInput } from './quote-form.ts';

export const requestedCoverLabels = { 'stock-custody': 'Stock and custody', premises: 'Premises', 'tools-equipment': 'Tools and equipment' };
export type RequestedCoverCode = keyof typeof requestedCoverLabels;
export const requestedCoverCodes = (proposal: QuoteProposal): RequestedCoverCode[] => proposal.productCode === 'motor-trade-combined' ? ['stock-custody', 'premises', 'tools-equipment'] : ['tools-equipment'];
export function requestedCoverRows(proposal: QuoteProposal): QuoteObject[] { return Array.isArray(proposal.cover?.requestedSections) ? proposal.cover.requestedSections as QuoteObject[] : []; }
export function changeRequestedCover(proposal: QuoteProposal, code: RequestedCoverCode, id: string, selected: boolean, fields: Record<string, string>, premisesIds: string[]): QuoteProposal {
  if (!requestedCoverCodes(proposal).includes(code)) throw new Error('This section is not available for this product.');
  if (!/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(id)) throw new Error('The section identity is unavailable.');
  const rows = requestedCoverRows(proposal);
  if (rows.filter(row => row.code === code).length > 1) throw new Error('Duplicate saved sections must be reviewed.');
  const existing = rows.find(row => row.code === code);
  const section: QuoteObject = { id: existing?.id ?? id, code, selected };
  if (selected) {
    for (const field of code === 'stock-custody' ? ['limit', 'excess', 'anyOneVehicleLimit'] : ['limit', 'excess']) {
      const parsed = amountInput(fields[field] ?? '');
      if (parsed.error || parsed.value === undefined || field !== 'excess' && parsed.value === '0.00') throw new Error(`${requestedCoverLabels[code]}: enter ${field === 'anyOneVehicleLimit' ? 'any one vehicle limit' : field} in GBP${field === 'excess' ? ' (zero is allowed)' : ' greater than zero'}.`);
      section[field] = parsed.value;
    }
    if (code === 'premises') {
      const current = Array.isArray(proposal.risk?.premises) ? proposal.risk.premises as QuoteObject[] : [];
      if (!premisesIds.length || new Set(premisesIds).size !== premisesIds.length || premisesIds.some(target => !current.some(row => row.id === target))) throw new Error('Select at least one current trading premises.');
      section.premisesIds = [...premisesIds];
    }
  }
  return { ...structuredClone(proposal), cover: { ...structuredClone(proposal.cover ?? {}), requestedSections: existing ? rows.map(row => structuredClone(row.code === code ? section : row)) : [...structuredClone(rows), section] } };
}
