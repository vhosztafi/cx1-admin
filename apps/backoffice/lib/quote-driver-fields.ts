import type { ReferenceChoice } from './quote-form';
import type { DriverHistory } from './quote-driver-form';

export type DriverFieldGroup = 'plan' | 'driver' | DriverHistory;
export type DriverField = { id: string; group: DriverFieldGroup; path: string; label: string; kind: 'text' | 'date' | 'boolean' | 'money' | 'count' | 'reference' | 'enum'; questionId?: string; collection?: string; choices: ReferenceChoice[]; unavailable?: boolean; unavailableReason?: 'context' | 'inactive'; max?: number };
type Mapping = { owner: string; questionId?: string; label?: string; answerKind?: string; contractKind: string; canonicalPath: string };
type Binding = { owner: string; selectionRule: string; collections: string[] };

// Server-only projection of explicit source bindings. Conditional young-driver
// selections stay unavailable until their cover-dependent options are resolved.
export function sourceDriverFields(mappings: Mapping[], bindings: Binding[], collections: Record<string, ReferenceChoice[]>, labels: Record<string, string>): DriverField[] {
  const fields: DriverField[] = mappings.filter(row => row.owner.startsWith('MTS-06-') || row.canonicalPath.startsWith('risk.drivers[].')).map(row => {
    const relative = row.canonicalPath.replace(/^risk\.drivers\[\]\./, '');
    const nested = /^(occupations|convictions|losses|criminalConvictions|countyCourtJudgments)\[\]\.(.+)$/.exec(relative);
    const group = row.canonicalPath.startsWith('risk.responses') ? 'plan' : nested ? nested[1] as DriverHistory : 'driver';
    const kind = row.answerKind ?? ({ string: 'text', Date: 'date', Reference: 'reference', Amount: 'money', integer: 'count', boolean: 'boolean' } as Record<string, DriverField['kind']>)[row.contractKind];
    if (!['text', 'date', 'reference', 'money', 'count', 'boolean'].includes(kind)) throw new Error('Unsupported driver control.');
    const binding = bindings.find(item => item.owner === row.owner);
    const unavailable = kind === 'reference' && binding?.selectionRule !== 'fixed';
    const collection = binding?.collections[0];
    if (kind === 'reference' && (!collection || !collections[collection])) throw new Error('Driver reference binding is missing.');
    const label = (row.label ?? labels[row.owner] ?? '').trim().replace(/:$/, '');
    if (!label) throw new Error('Driver field label is missing.');
    return { id: row.owner, group, path: row.questionId ? 'responses' : nested ? nested[2] : relative, label,
      kind: kind as DriverField['kind'], questionId: row.questionId,
      ...(collection ? { collection } : {}), choices: collection && !unavailable ? collections[collection].map(({ value, text }) => ({ value, text })) : [], ...(unavailable ? { unavailable: true } : {}) };
  });
  const extra = (group: DriverFieldGroup, path: string, label: string, kind: DriverField['kind'], choices: ReferenceChoice[] = [], max?: number): DriverField => ({ id: `${group}.${path}`, group, path, label, kind, choices, ...(max ? { max } : {}) });
  fields.unshift(extra('driver', 'fullName', 'Full name', 'text', [], 200));
  fields.push(extra('driver', 'licence.number', 'Driving licence number', 'text', [], 40), extra('driver', 'licence.testDate', 'Driving test date', 'date'));
  const enums = (values: string[]) => values.map(value => ({ value, text: value.replaceAll('-', ' ') }));
  fields.push(extra('convictions', 'declaredBanPeriod', 'Declared ban period', 'enum', enums(['none', 'under-3-months', '3-to-6-months', '6-to-12-months', 'over-12-months'])),
    extra('losses', 'status', 'Recorded claim status', 'enum', enums(['open', 'closed', 'notification-only'])),
    extra('losses', 'fault', 'Fault involvement', 'enum', enums(['fault', 'non-fault', 'unknown', 'split'])));
  const incidentTypes = collections['prototype.incident-type'];
  if (!incidentTypes) throw new Error('Incident reference binding is missing.');
  fields.push({ ...extra('losses', 'declaredType', 'Declared incident type', 'reference', incidentTypes.map(({ value, text }) => ({ value, text }))), collection: 'prototype.incident-type' });
  const overrides: Record<string, string> = { 'MTS-06-Q22': 'Date UK residency began', 'MTS-06-Q58': 'Remove the age-related additional excess', 'MTS-06-Q59': 'Young driver indemnity limit', 'MTS-06-Q60': 'Young driver engine capacity limit', 'MTS-06-Q61': 'Inexperienced driver excess', 'MTS-06-Q62': 'Inexperienced driver engine capacity limit' };
  const bounds: Record<string, number> = { 'MTS-06-Q09': 100, 'MTS-06-Q10': 100, 'MTS-06-Q13': 10, 'MTS-06-Q14': 50, 'MTS-06-Q15': 50, 'MTS-06-Q16': 50, 'MTS-06-Q17': 50, 'MTS-06-Q18': 50, 'MTS-06-Q37': 100, 'MTS-06-Q39': 1200, 'MTS-06-Q47': 50, 'MTS-06-Q51': 100, 'MTS-06-Q52': 11 };
  return fields.map(field => ({ ...field, label: overrides[field.id] ?? field.label, max: bounds[field.id] ?? field.max }));
}
