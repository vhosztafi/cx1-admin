import type { ReferenceChoice } from './quote-form';

export type SourceQuestion = { id: string; label: string; kind: 'boolean' | 'text' | 'reference' | 'count'; stage: 1 | 2; products: string[]; collection?: string; choices: ReferenceChoice[] };
type Mapping = { questionId?: string; label?: string; answerKind?: string; canonicalPath: string; stages?: string[]; products: string[] };
type Binding = { questionId?: string; canonicalPath: string; selectionRule: string; collections: string[] };

// Server projection: source stage ownership wins over the JSON storage section.
// Only the initial two editable source stages are shipped to this form.
export function sourceBusinessQuestions(mappings: Mapping[], bindings: Binding[], collections: Record<string, ReferenceChoice[]>): SourceQuestion[] {
  return mappings.filter(row => row.questionId && row.canonicalPath === 'risk.business.responses.answers[]' &&
    (row.questionId === 'MTS-02-Q01' || row.stages?.some(stage => /^Motor Trade (Road Risks|Combined):step-[23]$/.test(stage))))
    .map(row => {
      const kind = row.answerKind;
      if (kind !== 'boolean' && kind !== 'text' && kind !== 'reference' && kind !== 'count') throw new Error('Unsupported business question control.');
      const binding = bindings.find(item => item.questionId === row.questionId && item.canonicalPath === row.canonicalPath);
      const collection = binding?.collections[0];
      if (kind === 'reference' && (!collection || binding?.selectionRule !== 'fixed' || binding.collections.length !== 1 || !collections[collection])) throw new Error('Business question reference binding is missing.');
      return { id: row.questionId!, label: row.label ?? 'Where are you trading from?', kind,
        stage: row.stages?.some(stage => stage.startsWith('Motor Trade') && stage.endsWith(':step-2')) ? 1 : 2,
        products: row.products.filter(product => product.startsWith('motor-trade-')),
        ...(collection ? { collection } : {}), choices: collection ? collections[collection].map(({ value, text }) => ({ value, text })) : [] };
    });
}
