import type {QuoteObject, QuoteTermIntent, QuoteValue, QuoteView} from './quotes';

export const commercialQuestionVersion = 'commercial-questions-1';
export const commercialReferenceVersion = 'commercial-reference-1';
export const commercialStages = ['Agency & product', 'Proposer & history', 'Claims & losses', 'Locations & occupancy', 'Construction & protections', 'Flood & subsidence', 'Sums insured & BI', 'Liability & wage roll', 'Health & safety', 'Cover & declarations'] as const;
export type CommercialItem = QuoteObject & {id: string};
export type CommercialProposal = {
  schemaVersion: '1.0'; format: 'commercial-combined-capture-1'; productCode: 'commercial-combined';
  insured?: QuoteObject; termIntent?: QuoteTermIntent;
  risk?: {business?: QuoteObject; locations?: CommercialItem[]; wages?: CommercialItem[]; losses?: CommercialItem[];
    liability?: QuoteObject; businessInterruption?: QuoteObject; declarations?: QuoteObject; materialFacts?: string};
  cover?: QuoteObject;
};
export type CommercialQuoteView = QuoteView<CommercialProposal>;
export type CommercialOption = {value: string | number; label: string};
export type CommercialQuestion = {id: string; label: string; kind: 'boolean' | 'reference' | 'text' | 'count' | 'money' | 'percentage'; container: string; stage: number; options: CommercialOption[]};
export type CommercialCatalogue = {questionVersion: string; referenceVersion: string; questions: CommercialQuestion[]; lossTypes: CommercialOption[]; wageCategories: CommercialOption[]; occupancy: string[]; entityTypes: string[]};
export type CommercialCollection = 'locations' | 'wages' | 'losses' | 'activities' | 'dependencies';
const paths: Record<CommercialCollection, string> = {locations: 'risk.locations', wages: 'risk.wages', losses: 'risk.losses', activities: 'risk.business.activities', dependencies: 'risk.businessInterruption.dependencies'};
const limits: Record<CommercialCollection, number> = {locations: 100, wages: 100, losses: 100, activities: 30, dependencies: 50};
const object = (value: unknown): value is QuoteObject => value !== null && typeof value === 'object' && !Array.isArray(value);
const idPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const sameId = (left: string, right: string) => left.toLowerCase() === right.toLowerCase();
function fieldParts(path: string) {
  const parts = path.split('.');
  if (parts.some(x => !/^[a-zA-Z][a-zA-Z0-9]*$/.test(x) || ['constructor', 'prototype', '__proto__'].includes(x))) throw new Error('Invalid commercial field path.');
  return parts;
}
function read(value: unknown, path: string): QuoteValue | undefined {
  for (const part of fieldParts(path)) value = object(value) ? value[part] : undefined;
  return value as QuoteValue | undefined;
}
function set(owner: QuoteObject, path: string, value: QuoteValue | undefined) {
  const parts = fieldParts(path); let current = owner;
  for (const part of parts.slice(0, -1)) {
    if (current[part] !== undefined && !object(current[part])) throw new Error('The field container has changed.');
    if (current[part] === undefined) current[part] = {};
    current = current[part] as QuoteObject;
  }
  if (value === undefined) delete current[parts.at(-1)!]; else current[parts.at(-1)!] = structuredClone(value);
}
export const emptyCommercialProposal = (): CommercialProposal => ({schemaVersion: '1.0', format: 'commercial-combined-capture-1', productCode: 'commercial-combined'});
export function commercialField(proposal: CommercialProposal, path: string) {return read(proposal, path);}
export function changeCommercialField(proposal: CommercialProposal, path: string, value: QuoteValue | undefined): CommercialProposal {
  if (!['insured', 'risk', 'cover', 'termIntent'].includes(fieldParts(path)[0])) throw new Error('Quote identity cannot be edited.');
  const next = structuredClone(proposal); set(next as unknown as QuoteObject, path, value); return next;
}
export function commercialRows(proposal: CommercialProposal, collection: CommercialCollection): CommercialItem[] {
  const rows = read(proposal, paths[collection]);
  if (rows === undefined) return [];
  if (!Array.isArray(rows) || rows.some(x => !object(x) || typeof x.id !== 'string')) throw new Error('The saved item collection is invalid.');
  return rows as CommercialItem[];
}
export function addCommercialRow(proposal: CommercialProposal, collection: CommercialCollection, id = crypto.randomUUID()): CommercialProposal {
  if (!idPattern.test(id) || id === '00000000-0000-0000-0000-000000000000') throw new Error('A new stable item identity is required.');
  if ((Object.keys(paths) as CommercialCollection[]).some(kind => commercialRows(proposal, kind).some(x => sameId(x.id, id)))) throw new Error('This item identity already exists.');
  const rows = commercialRows(proposal, collection); if (rows.length >= limits[collection]) throw new Error('The supported item limit has been reached.');
  return changeCommercialField(proposal, paths[collection], [...rows, {id}]);
}
export function updateCommercialRow(proposal: CommercialProposal, collection: CommercialCollection, id: string, path: string, value: QuoteValue | undefined): CommercialProposal {
  if (fieldParts(path)[0] === 'id') throw new Error('Item identity cannot be edited.');
  const rows = commercialRows(proposal, collection); const index = rows.findIndex(x => sameId(x.id, id));
  if (index < 0) throw new Error('This item is no longer part of the proposal.');
  if (collection === 'losses' && path === 'riskItemId' && value !== undefined &&
      (typeof value !== 'string' || !commercialRows(proposal, 'locations').some(x => sameId(x.id, value)))) throw new Error('Choose a location in this proposal.');
  const next = structuredClone(rows); set(next[index], path, value); return changeCommercialField(proposal, paths[collection], next);
}
export function removeCommercialRow(proposal: CommercialProposal, collection: CommercialCollection, id: string): CommercialProposal {
  const rows = commercialRows(proposal, collection);
  if (!rows.some(x => sameId(x.id, id))) throw new Error('This item is no longer part of the proposal.');
  if (collection === 'locations' && commercialRows(proposal, 'losses').some(x => typeof x.riskItemId === 'string' && sameId(x.riskItemId, id)))
    throw new Error('Clear or change the linked loss locations before removing this location.');
  return changeCommercialField(proposal, paths[collection], rows.filter(x => !sameId(x.id, id)));
}
function subject(proposal: CommercialProposal, question: CommercialQuestion, itemId?: string) {
  const split = question.container.indexOf('[]');
  if (split < 0) {
    if (itemId !== undefined) throw new Error('This question belongs to the whole proposal.');
    return {responses: commercialField(proposal, question.container), collection: undefined, path: question.container};
  }
  const collection = (Object.keys(paths) as CommercialCollection[]).find(x => paths[x] === question.container.slice(0, split));
  if (!collection || !itemId) throw new Error('This question needs its saved item identity.');
  const row = commercialRows(proposal, collection).find(x => sameId(x.id, itemId));
  if (!row) throw new Error('The question subject is not part of this proposal.');
  const path = question.container.slice(split + 3); return {responses: read(row, path), collection, path};
}
export function commercialResponse(proposal: CommercialProposal, question: CommercialQuestion, itemId?: string): QuoteValue | undefined {
  const {responses} = subject(proposal, question, itemId);
  const answers = object(responses) && Array.isArray(responses.answers) ? responses.answers as QuoteObject[] : [];
  return answers.find(x => x.questionId === question.id)?.value;
}
export function setCommercialResponse(proposal: CommercialProposal, question: CommercialQuestion, itemId: string | undefined, value: QuoteValue | undefined): CommercialProposal {
  const current = subject(proposal, question, itemId);
  if (object(current.responses) && current.responses.questionSetVersion !== commercialQuestionVersion) throw new Error('The saved question version is unavailable.');
  const answers = object(current.responses) && Array.isArray(current.responses.answers) ? current.responses.answers as QuoteObject[] : [];
  const next = answers.filter(x => x.questionId !== question.id);
  if (value !== undefined) next.push({questionId: question.id, kind: question.kind, value,
    ...(question.kind === 'money' ? {currency: 'GBP'} : {}), ...(question.kind === 'percentage' ? {unit: 'basis-points'} : {})});
  const responses = {questionSetVersion: commercialQuestionVersion, answers: next};
  return current.collection ? updateCommercialRow(proposal, current.collection, itemId!, current.path, responses) : changeCommercialField(proposal, current.path, responses);
}
export function commercialVersionsMatch(view: CommercialQuoteView, catalogue: CommercialCatalogue) {
  return view.proposal.format === 'commercial-combined-capture-1' && view.captureVersions.schemaVersion === '1.0' &&
    view.captureVersions.questionSetVersion === catalogue.questionVersion && catalogue.questionVersion === commercialQuestionVersion &&
    view.captureVersions.referenceDataVersion === catalogue.referenceVersion && catalogue.referenceVersion === commercialReferenceVersion;
}
