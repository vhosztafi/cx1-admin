import type { ReferenceChoice } from './quote-form';

export type VehicleField = { id: string; group: 'vehicle' | 'modification' | 'portfolio' | 'plates'; path: string; label: string; kind: 'text' | 'date' | 'boolean' | 'money' | 'count' | 'decimal' | 'percentage' | 'reference' | 'enum'; questionId?: string; scope?: 'risk' | 'business'; collection?: string; choices: ReferenceChoice[]; max?: number };
type Mapping = { owner: string; questionId?: string; label?: string; answerKind?: string; contractKind: string; canonicalPath: string };
type Binding = { owner: string; selectionRule: string; collections: string[] };
export function sourceVehicleFields(mappings: Mapping[], bindings: Binding[], collections: Record<string, ReferenceChoice[]>, labels: Record<string, string>): VehicleField[] {
  const selected = mappings.filter(row => !row.owner.startsWith('MTS-09-') && (row.canonicalPath.startsWith('risk.vehicles[].') || row.owner.startsWith('MTS-10-') || ['MTS-07-Q01', 'MTS-07-Q02', 'prototype.quote-value.315960b57ab1'].includes(row.owner)));
  const fields: VehicleField[] = selected.map(row => {
    const relative = row.canonicalPath.replace(/^risk\.vehicles\[\]\./, '');
    const group = row.owner.startsWith('MTS-10-') ? 'portfolio' : !row.canonicalPath.startsWith('risk.vehicles') ? 'plates' : relative.startsWith('modifications') ? 'modification' : 'vehicle';
    const kind = row.answerKind ?? ({ string: 'text', Date: 'date', Reference: 'reference', Amount: 'money', integer: 'count', number: 'decimal', boolean: 'boolean' } as Record<string, VehicleField['kind']>)[row.contractKind];
    if (!['text', 'date', 'reference', 'money', 'count', 'decimal', 'percentage', 'boolean'].includes(kind)) throw new Error('Unsupported vehicle control.');
    const binding = bindings.find(item => item.owner === row.owner); const collection = binding?.collections[0];
    if (kind === 'reference' && (!collection || binding?.selectionRule !== 'fixed' || !collections[collection])) throw new Error('Vehicle reference binding is missing.');
    const label = (row.label ?? labels[row.owner] ?? '').trim().replace(/:$/, ''); if (!label) throw new Error('Vehicle field label is missing.');
    return { id: row.owner, group, path: row.questionId ? 'responses' : group === 'modification' ? 'code' : relative, label, kind: kind as VehicleField['kind'], questionId: row.questionId,
      scope: row.canonicalPath.startsWith('risk.business') ? 'business' : 'risk', ...(collection ? { collection } : {}), choices: collection ? collections[collection].map(({ value, text }) => ({ value, text })) : [] };
  });
  const extra = (path: string, label: string, kind: VehicleField['kind'], values: string[] = [], max?: number): VehicleField => ({ id: `vehicle.${path}`, group: 'vehicle', path, label, kind, choices: values.map(value => ({ value, text: value.replaceAll('-', ' ') })), max });
  fields.unshift(extra('register', 'Vehicle register', 'enum', ['owned-not-for-sale', 'held-for-sale']), extra('ownership', 'Ownership purpose', 'enum', ['business-owned', 'stock-for-sale', 'driver-personal', 'customer']));
  fields.push(extra('manufactureYear', 'Year manufactured', 'count', [], 2200), extra('engineCc', 'Engine capacity (cc)', 'count', [], 1000000), extra('purchasePrice', 'Purchase price', 'money'));
  if (!collections['prototype.vehicle-body']) throw new Error('Vehicle body reference binding is missing.');
  fields.push({ ...extra('body', 'Declared body category', 'reference'), collection: 'prototype.vehicle-body', choices: collections['prototype.vehicle-body'].map(({ value, text }) => ({ value, text })) });
  const bounds: Record<string, number> = { registration: 12, make: 100, model: 100, abiCode: 100, bodyDescription: 200, fuelType: 100, transmission: 100, declaredEngineSize: 100, registrationYear: 2200, leaseLengthYears: 100, grossWeightKg: 1000000, abiGroup: 1000000, seats: 1000000, doors: 1000000 };
  const overrides: Record<string, string> = { grossWeightKg: 'Gross vehicle weight (kg)', declaredEngineSize: 'Declared engine size', registeredOn: 'First registration date', purchasedOn: 'Purchase date', keptOvernightAddress: 'Overnight postcode' };
  return fields.map(field => ({ ...field, max: bounds[field.path] ?? (field.kind === 'text' ? 4000 : field.max), label: overrides[field.path] ?? field.label }));
}
