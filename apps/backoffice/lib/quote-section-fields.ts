import type { VehicleField } from './quote-vehicle-fields';
import type { ReferenceChoice } from './quote-form';

export type SectionGroup = 'premises' | 'insurance' | 'cover' | 'declarations' | 'annualEuropeanCover' | 'temporaryEuropeanCover';
export type SectionField = Omit<VehicleField, 'group' | 'scope'> & { group: SectionGroup; products: string[]; container?: string; dynamic?: boolean };
type Mapping = { owner: string; questionId?: string; label?: string; answerKind?: string; contractKind: string; canonicalPath: string; products: string[] };
type Binding = { owner: string; selectionRule: string; collections: string[] };
const allProducts = ['motor-trade-road-risks', 'motor-trade-combined'];
export function sourceSectionFields(mappings: Mapping[], bindings: Binding[], collections: Record<string, ReferenceChoice[]>, labels: Record<string, string>): SectionField[] {
  const selected = mappings.filter(row => row.canonicalPath.startsWith('cover.') || row.canonicalPath.startsWith('risk.previousInsurance.') || row.canonicalPath.startsWith('risk.premises[].') || row.canonicalPath.startsWith('risk.declarations.') || row.owner.startsWith('MTS-13-'));
  const fields: SectionField[] = selected.filter(row => row.contractKind !== 'Id').map(row => {
    const path = row.canonicalPath;
    const group: SectionGroup = path.startsWith('risk.premises') ? 'premises' : path.startsWith('risk.previousInsurance') ? 'insurance' : path.startsWith('cover.annual') ? 'annualEuropeanCover' : path.startsWith('cover.temporary') ? 'temporaryEuropeanCover' : path.startsWith('cover.') ? 'cover' : 'declarations';
    const repeated = ['premises', 'annualEuropeanCover', 'temporaryEuropeanCover'].includes(group);
    const relative = repeated ? path.replace(/^(risk|cover)\.[a-zA-Z]+\[\]\./, '') : path;
    const kind = row.answerKind ?? ({ string: 'text', Date: 'date', Reference: 'reference', Amount: 'money', integer: 'count', number: 'decimal', boolean: 'boolean' } as Record<string, SectionField['kind']>)[row.contractKind];
    if (!['text', 'date', 'reference', 'money', 'count', 'decimal', 'percentage', 'boolean'].includes(kind)) throw new Error('Unsupported capture section control.');
    const binding = bindings.find(item => item.owner === row.owner); const collection = binding?.collections[0];
    if (kind === 'reference' && (!collection || !collections[collection])) throw new Error('Section reference binding is missing.');
    const dynamic = kind === 'reference' && binding?.selectionRule !== 'fixed';
    if (dynamic && row.owner !== 'MTS-05-Q04') throw new Error('Unsupported dynamic section reference.');
    const label = (row.label ?? labels[row.owner] ?? '').trim().replace(/:$/, ''); if (!label) throw new Error('Section field label is missing.');
    return { id: row.owner, group, path: row.questionId ? 'responses' : relative, container: row.questionId ? (repeated ? relative : path).replace(/\.answers\[\]$/, '') : undefined,
      label, kind: kind as SectionField['kind'], questionId: row.questionId, products: row.products,
      ...(collection ? { collection } : {}), dynamic, choices: collection && !dynamic ? collections[collection].map(({ value, text }) => ({ value, text })) : [] };
  });
  const extra = (group: SectionGroup, path: string, label: string, kind: SectionField['kind'], max?: number, products = allProducts): SectionField => ({ id: `${group}.${path}`, group, path, label, kind, choices: [], products, max });
  fields.push(extra('insurance', 'risk.previousInsurance.insurer', 'Previous insurer', 'text', 200), extra('insurance', 'risk.previousInsurance.policyNumber', 'Previous policy number', 'text', 100), extra('insurance', 'risk.previousInsurance.policyholderName', 'Previous policyholder name', 'text', 200), extra('insurance', 'risk.previousInsurance.expiresOn', 'Previous policy expiry', 'date'), extra('insurance', 'risk.previousInsurance.noClaimsYears', 'Declared no-claims years', 'count', 100),
    { ...extra('insurance', 'risk.previousInsurance.noClaimsYearsBasis', 'Declared years basis', 'enum'), choices: [{ value: 'exact', text: 'Exact years' }, { value: 'at-least', text: 'At least these years' }] },
    extra('premises', 'buildings', 'Buildings sum insured', 'money', undefined, ['motor-trade-combined']), extra('premises', 'contents', 'Contents sum insured', 'money', undefined, ['motor-trade-combined']));
  for (const [path, label, collection] of [['declaredUse', 'Declared premises use', 'prototype.premises-use'], ['security', 'Security', 'prototype.premises-security']]) {
    if (!collections[collection]) throw new Error('Premises reference binding is missing.');
    fields.push({ ...extra('premises', path, label, 'reference', undefined, ['motor-trade-combined']), collection, choices: collections[collection].map(({ value, text }) => ({ value, text })) });
  }
  const labelsById: Record<string, string> = { 'MTS-05-Q04': 'Own-vehicle excess', 'MTS-05-Q12': 'No-claims bonus expiry', 'MTS-11-Q01': 'Demonstration cover required', 'MTS-13-Q01': 'Material facts and additional information',
    'MTS-12-Q02': 'Insurance refusal or special terms details', 'MTS-12-Q04': 'Refused claim details', 'MTS-12-Q06': 'Court judgment details', 'MTS-12-Q08': 'Insolvency or creditor arrangement details', 'MTS-12-Q10': 'Unreported disability details', 'MTS-12-Q12': 'Criminal conviction or prosecution details', 'MTS-12-Q14': 'Other directorship or business details', 'MTS-12-Q16': 'Vehicle rental, leasing or claims business details', 'MTS-12-Q18': 'Director disqualification details', 'MTS-12-Q20': 'Company liquidation or dissolution details' };
  return fields.map(field => ({ ...field, max: field.id === 'MTS-05-Q16' ? 50 : field.path === 'risk.materialFacts' ? 1000 : field.path === 'address.postcode' ? 10 : field.path.startsWith('address.') ? 50 : field.path === 'registration' ? 12 : field.kind === 'text' ? field.max ?? 4000 : field.max,
    label: labelsById[field.id] ?? field.label }));
}
