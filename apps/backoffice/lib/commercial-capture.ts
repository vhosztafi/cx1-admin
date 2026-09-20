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

export function sumCommercialMoney(values: (QuoteValue | undefined)[]): string | undefined {
  if (values.length === 0 || values.some(value => value === undefined)) return undefined;
  let pennies = BigInt(0);
  for (const value of values) {
    if (typeof value !== 'string' || !/^(0|[1-9][0-9]{0,12})\.[0-9]{2}$/.test(value)) throw new Error('A complete non-negative amount is required.');
    pennies += BigInt(value.replace('.', ''));
  }
  return `${pennies / BigInt(100)}.${String(pennies % BigInt(100)).padStart(2, '0')}`;
}
export function commercialDecimalInput(input: string): {value?: number; error?: string} {
  if (input === '') return {};
  if (!/^(0|[1-9][0-9]*)(\.[0-9]{1,2})?$/.test(input) || Number(input) > 1000) return {error: 'Enter a height from 0 to 1,000 metres with at most two decimal places.'};
  return {value: Number(input)};
}
export function commercialPostcodeInput(input: string): {value?: string; error?: string} {
  if (input === '') return {};
  const compact = input.trim().toUpperCase().replaceAll(' ', '');
  if (!/^(?:GIR0AA|(?:[A-PR-UWYZ][0-9][0-9]?|[A-PR-UWYZ][A-HK-Y][0-9][0-9]?|[A-PR-UWYZ][0-9][A-HJKPSTUW]|[A-PR-UWYZ][A-HK-Y][0-9][ABEHMNPRVWXY])[0-9][ABD-HJLNP-UW-Z]{2})$/.test(compact)) return {error: 'Enter a complete UK postcode or leave it unanswered.'};
  return {value: input};
}
export function commercialInputStage(proposal: CommercialProposal, catalogue: Pick<CommercialCatalogue, 'questions'>, key: string): number {
  const question = catalogue.questions.find(q => key.startsWith(`${q.id}:`));
  if (question) return question.stage - 1;
  if (key.startsWith('risk.businessInterruption') || commercialRows(proposal, 'dependencies').some(row => key.startsWith(`${row.id}:`))) return 6;
  if (key.startsWith('risk.liability')) return 7;
  if (key.startsWith('cover.') || key === 'risk.materialFacts') return 9;
  return 1;
}
export const commercialBiQuestions = ['prototype.quote.7bd824326d4c', 'prototype.quote.9b4688f28580', 'prototype.quote.72cb884b6df1', 'prototype.quote.a2f1dd35162a', 'prototype.quote.ab908171e40b'];
export const commercialSubsidenceQuestions = ['prototype.quote.aaccb5c97c33', 'prototype.quote.8293bc041f81', 'prototype.quote.02d9c6be528a', 'prototype.quote.a6a4579c177e', 'prototype.quote.7a732bc99a5f', 'prototype.quote.fcc7e22c33ea', 'prototype.quote.923e600eb3a4'];
export function clearCommercialSection(proposal: CommercialProposal, section: 'el' | 'bi' | 'contract-works'): CommercialProposal {
  let next = structuredClone(proposal);
  if (section === 'el') {
    next = changeCommercialField(next, 'risk.wages', []);
    next = changeCommercialField(next, 'risk.liability.employersLimit', undefined);
    return changeCommercialField(next, 'risk.liability.employersReferenceNumber', undefined);
  }
  if (section === 'contract-works') {
    next = changeCommercialField(next, 'cover.contractWorks.sumInsured', undefined);
    return changeCommercialField(next, 'cover.contractWorks.excess', undefined);
  }
  next = changeCommercialField(next, 'risk.businessInterruption', undefined);
  const responses = commercialField(next, 'cover.responses');
  if (object(responses) && Array.isArray(responses.answers))
    next = changeCommercialField(next, 'cover.responses.answers', responses.answers.filter(answer => !object(answer) || !commercialBiQuestions.includes(String(answer.questionId))));
  return next;
}
export function commercialQuestionApplies(proposal: CommercialProposal, question: CommercialQuestion, catalogue: Pick<CommercialCatalogue, 'questions'>): boolean {
  const value = (id: string) => {const q = catalogue.questions.find(x => x.id === id); return q ? commercialResponse(proposal, q) : undefined;};
  const yes = (id: string) => value(id) === true;
  const choice = (id: string) => {const answer = value(id); return object(answer) ? answer.value : undefined;};
  const any = (stage: number, answer: boolean, exclude: string[] = []) => catalogue.questions.some(q => q.stage === stage && q.container === 'risk.declarations' && !exclude.includes(q.id) && value(q.id) === answer);
  if (commercialSubsidenceQuestions.includes(question.id) || question.id === 'prototype.quote.4a288359be03') return yes('prototype.quote.be47c08f530f');
  if (commercialBiQuestions.includes(question.id)) return yes('prototype.quote.7660fc5eb42e');
  switch (question.id) {
    case 'prototype.quote-value.b99b3a5b3004': return any(2, true, ['prototype.quote.36ef01068295']);
    case 'prototype.quote-value.4799b8daa1ca': return [2, 3].includes(Number(choice('prototype.quote.ade0f3f0df5e'))) || yes('prototype.quote.c08c9ebaf825') || yes('prototype.quote.a6ff9fdbe769') || yes('prototype.quote.be47c08f530f') && commercialSubsidenceQuestions.some(yes);
    case 'prototype.quote-value.a0e5d1b910f8': return any(9, false) || [2,3].includes(Number(choice('prototype.quote.00fa2758dd8c'))) || choice('prototype.quote.6d9a54d9e464') === 2 || [2,3].includes(Number(choice('prototype.quote.956ebc71fded'))) || choice('prototype.quote.992fa2724da2') === 2 || choice('prototype.quote.ca115cf242f7') === 2 || choice('prototype.quote.978fde66afb5') === 3;
    case 'prototype.quote-value.5fcb5378a1fd': return yes('prototype.quote.c70fcdf94e4e');
    case 'prototype.quote-value.59c91e8db152':
    case 'prototype.quote-value.acace76b1053': {
      const detached = question.id === 'prototype.quote-value.acace76b1053';
      const q = catalogue.questions.find(x => x.id === (detached ? 'prototype.addloc.detached' : 'prototype.addloc.sole-occupier'));
      return Boolean(q && commercialRows(proposal, 'locations').some(row => commercialResponse(proposal, q, row.id) === detached));
    }
    default: return true;
  }
}
