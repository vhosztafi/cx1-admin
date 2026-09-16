import { selectQuoteDynamicOptions, type DynamicCatalogue, type DriverOptionState } from '../../../scripts/quote-dynamic-options.mjs';
import type { DriverField } from './quote-driver-fields';
import type { QuoteProposal } from './quotes';

export function driverOptionStates(proposal: QuoteProposal, catalogue: DynamicCatalogue | undefined): DriverOptionState[] {
  return catalogue ? selectQuoteDynamicOptions(proposal, catalogue, { includeDriverOptions: true }).driverOptions ?? [] : [];
}
export function resolvedDriverField(field: DriverField, driverIndex: number, states: DriverOptionState[]): DriverField {
  if (!/^MTS-06-Q(58|59|60|61|62)$/.test(field.id)) return field;
  const state = states.find(item => item.driverIndex === driverIndex && item.questionId === field.id);
  return { ...field, unavailable: state?.active !== true, unavailableReason: state?.active === false ? 'inactive' : 'context',
    ...(state?.collection ? { collection: state.collection } : {}), choices: state?.choices ?? [] };
}
