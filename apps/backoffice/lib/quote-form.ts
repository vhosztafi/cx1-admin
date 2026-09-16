import type { QuoteObject, QuoteProposal, QuoteValue } from './quotes';

export type ParsedInput<T> = { value: T | undefined; error?: never } | { value?: never; error: string };

// Keep in-progress input text in the control. A failed conversion must never
// replace the saved value with zero or silently round business information.
export function amountInput(text: string): ParsedInput<string> {
  if (text === '') return { value: undefined };
  if (!/^(0|[1-9][0-9]{0,12})(\.[0-9]{1,2})?$/.test(text)) return { error: 'Enter a non-negative GBP amount with up to two decimal places.' };
  const [whole, fraction = ''] = text.split('.');
  return { value: `${whole}.${fraction.padEnd(2, '0')}` };
}

export function percentageInput(text: string): ParsedInput<number> {
  if (text === '') return { value: undefined };
  if (!/^(0|[1-9][0-9]{0,2})(\.[0-9]{1,2})?$/.test(text)) return { error: 'Enter a percentage from 0 to 100 with up to two decimal places.' };
  const [whole, fraction = ''] = text.split('.');
  const value = Number(whole) * 100 + Number(fraction.padEnd(2, '0'));
  return value <= 10000 ? { value } : { error: 'The percentage cannot exceed 100.' };
}

export function countInput(text: string): ParsedInput<number> {
  if (text === '') return { value: undefined };
  if (!/^(0|[1-9][0-9]{0,6})$/.test(text) || Number(text) > 1000000) return { error: 'Enter a whole number from 0 to 1,000,000.' };
  return { value: Number(text) };
}

const object = (value: QuoteValue | undefined): value is QuoteObject => Boolean(value) && typeof value === 'object' && !Array.isArray(value);
function segments(path: string) {
  const parts = path.split('.');
  if (parts.some(part => !/^[a-zA-Z][a-zA-Z0-9]*$/.test(part) || ['constructor', 'prototype', '__proto__'].includes(part))) throw new Error('Invalid form field path.');
  return parts;
}

export function fieldValue(proposal: QuoteProposal, path: string): QuoteValue | undefined {
  let value: QuoteValue | undefined = proposal as unknown as QuoteObject;
  for (const part of segments(path)) value = object(value) ? value[part] : undefined;
  return value;
}

// Only object fields are addressed here. Arrays must be edited as complete
// stable-ID collections, never by an index captured before a reorder.
export function changeField(proposal: QuoteProposal, path: string, value: QuoteValue | undefined): QuoteProposal {
  const parts = segments(path);
  if (!['insured', 'risk', 'cover', 'termIntent'].includes(parts[0])) throw new Error('Quote identity is not editable.');
  const next = structuredClone(proposal) as unknown as QuoteObject;
  let current = next;
  for (const part of parts.slice(0, -1)) {
    const child = current[part];
    if (child !== undefined && !object(child)) throw new Error('Cannot replace a non-object form container.');
    if (child === undefined) current[part] = {};
    current = current[part] as QuoteObject;
  }
  const name = parts.at(-1)!;
  if (value === undefined) delete current[name]; else current[name] = structuredClone(value);
  return next as unknown as QuoteProposal;
}

export function changeAnswer(proposal: QuoteProposal, scope: string, version: string, questionId: string, kind: string, value: QuoteValue | undefined): QuoteProposal {
  if (!version || !questionId || !scope.endsWith('.responses')) throw new Error('Question identity is required.');
  return changeField(proposal, scope, changeResponses(fieldValue(proposal, scope), version, questionId, kind, value));
}

export function changeResponses(existing: QuoteValue | undefined, version: string, questionId: string, kind: string, value: QuoteValue | undefined): QuoteObject {
  if (!version || !questionId) throw new Error('Question identity is required.');
  if (existing !== undefined && !object(existing)) throw new Error('Invalid saved answer container.');
  const responses: QuoteObject = existing === undefined ? {} : structuredClone(existing as QuoteObject);
  if (responses.questionSetVersion !== undefined && responses.questionSetVersion !== version) throw new Error('The saved question version is different. Reload its matching form.');
  if (responses.answers !== undefined && !Array.isArray(responses.answers)) throw new Error('Invalid saved answer list.');
  const answers = (responses.answers ?? []) as QuoteValue[];
  const indices = answers.flatMap((answer, index) => object(answer) && answer.questionId === questionId ? [index] : []);
  if (indices.length > 1) throw new Error('Duplicate saved question identity.');
  const index = indices[0];
  if (value === undefined) { if (index !== undefined) answers.splice(index, 1); }
  else {
    validateAnswer(kind, value);
    const answer: QuoteObject = { questionId, kind, value: structuredClone(value) };
    if (kind === 'money') answer.currency = 'GBP';
    if (kind === 'percentage') answer.unit = 'basis-points';
    if (index === undefined) answers.push(answer); else answers[index] = answer;
  }
  responses.questionSetVersion = version; responses.answers = answers;
  return responses;
}

function validateAnswer(kind: string, value: QuoteValue) {
  const valid = kind === 'boolean' ? typeof value === 'boolean'
    : kind === 'text' ? typeof value === 'string' && value.length <= 4000
    : kind === 'date' ? typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)
    : kind === 'count' ? Number.isInteger(value) && typeof value === 'number' && value >= 0 && value <= 1000000
    : kind === 'percentage' ? Number.isInteger(value) && typeof value === 'number' && value >= 0 && value <= 10000
    : kind === 'money' ? typeof value === 'string' && /^(0|[1-9][0-9]{0,12})\.[0-9]{2}$/.test(value)
    : kind === 'reference' ? object(value)
    : kind === 'references' ? Array.isArray(value) && value.every(object)
    : false;
  if (!valid) throw new Error('The answer does not match its question type.');
}

export type ReferenceChoice = { value: number | string; text: string };
export function selectedReference(collection: string, version: string, choices: readonly ReferenceChoice[], selected: number | string): QuoteObject {
  const matches = choices.filter(choice => choice.value === selected);
  if (!collection || !version || matches.length !== 1 || !matches[0].text) throw new Error('Choose an option from the matching saved catalogue.');
  return { collection, version, value: matches[0].value, label: matches[0].text };
}

export function quoteActivities(proposal: QuoteProposal): QuoteObject[] {
  const value = fieldValue(proposal, 'risk.business.activities');
  if (value === undefined) return [];
  if (!Array.isArray(value) || value.some(row => !row || typeof row !== 'object' || Array.isArray(row) || typeof row.id !== 'string')) throw new Error('The saved occupation rows cannot be edited.');
  const rows = value as QuoteObject[];
  if (new Set(rows.map(row => String(row.id).toLowerCase())).size !== rows.length) throw new Error('Occupation row identities must be distinct.');
  return rows;
}

function indexOf(rows: QuoteObject[], id: string) {
  const index = rows.findIndex(row => row.id === id);
  if (index < 0) throw new Error('The occupation row has changed.');
  return index;
}

export function addQuoteActivity(proposal: QuoteProposal, id = crypto.randomUUID()): QuoteProposal {
  const rows = quoteActivities(proposal);
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id) || id === '00000000-0000-0000-0000-000000000000' || rows.some(row => String(row.id).toLowerCase() === id.toLowerCase()) || rows.length >= 1000) throw new Error('A distinct occupation row is required.');
  return changeField(proposal, 'risk.business.activities', [...rows, { id }]);
}

export function changeQuoteActivity(proposal: QuoteProposal, id: string, field: 'code' | 'turnoverBasisPoints', value: QuoteValue | undefined): QuoteProposal {
  if (!['code', 'turnoverBasisPoints'].includes(field)) throw new Error('Occupation row identity is not editable.');
  const rows = structuredClone(quoteActivities(proposal)); const row = rows[indexOf(rows, id)];
  if (value === undefined) delete row[field]; else row[field] = structuredClone(value);
  return changeField(proposal, 'risk.business.activities', rows);
}

export function removeQuoteActivity(proposal: QuoteProposal, id: string): QuoteProposal {
  const rows = [...quoteActivities(proposal)]; rows.splice(indexOf(rows, id), 1);
  return changeField(proposal, 'risk.business.activities', rows);
}

export function moveQuoteActivity(proposal: QuoteProposal, id: string, direction: -1 | 1): QuoteProposal {
  if (direction !== -1 && direction !== 1) throw new Error('Choose an adjacent occupation position.');
  const rows = [...quoteActivities(proposal)]; const from = indexOf(rows, id); const to = from + direction;
  if (to < 0 || to >= rows.length) return proposal;
  [rows[from], rows[to]] = [rows[to], rows[from]];
  return changeField(proposal, 'risk.business.activities', rows);
}
