import type { QuoteObject, QuoteProposal, QuoteValue } from './quotes';
function fieldValue(proposal: QuoteProposal, path: string): QuoteValue | undefined { const [root, name] = path.split('.'); return (proposal[root as 'risk' | 'cover'] as QuoteObject | undefined)?.[name]; }
function changeField(proposal: QuoteProposal, path: string, value: QuoteValue): QuoteProposal { const next = structuredClone(proposal), [root, name] = path.split('.'); const key = root as 'risk' | 'cover'; next[key] ??= {}; next[key]![name] = structuredClone(value); return next; }

export type SectionRows = 'premises' | 'annualEuropeanCover' | 'temporaryEuropeanCover';
export const sectionRowPath = (group: SectionRows) => `${group === 'premises' ? 'risk' : 'cover'}.${group}`;
const same = (value: QuoteValue | undefined, id: string) => typeof value === 'string' && value.toLowerCase() === id.toLowerCase();
export function sectionRows(proposal: QuoteProposal, group: SectionRows): QuoteObject[] {
  const rows = fieldValue(proposal, sectionRowPath(group)); if (rows === undefined) return [];
  if (!Array.isArray(rows) || rows.some(row => !row || typeof row !== 'object' || Array.isArray(row) || typeof row.id !== 'string')) throw new Error('The saved section cannot be edited.'); return rows as QuoteObject[];
}
function position(rows: QuoteObject[], id: string) { const index = rows.findIndex(row => same(row.id, id)); if (index < 0) throw new Error('The selected section row has changed.'); return index; }
export function addSectionRow(proposal: QuoteProposal, group: SectionRows, id = crypto.randomUUID()) {
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id) || id === '00000000-0000-0000-0000-000000000000') throw new Error('A distinct row identity is required.');
  function contains(value: unknown): boolean { if (!value || typeof value !== 'object') return false; if (Array.isArray(value)) return value.some(contains); return same((value as QuoteObject).id, id) || Object.values(value).some(contains); }
  if (contains(proposal)) throw new Error('A distinct row identity is required.'); const rows = sectionRows(proposal, group);
  if (rows.length >= 1000) throw new Error('The section row limit has been reached.'); return changeField(proposal, sectionRowPath(group), [...rows, { id }]);
}
export function removeSectionRow(proposal: QuoteProposal, group: SectionRows, id: string) {
  const rows = [...sectionRows(proposal, group)]; const index = position(rows, id);
  if (group === 'premises' && ((proposal.risk?.drivers ?? []) as QuoteObject[]).some(driver => ((driver.losses ?? []) as QuoteObject[]).some(loss => same(loss.riskItemId, id)))) throw new Error('Update the retained loss reference before removing these premises.');
  rows.splice(index, 1); return changeField(proposal, sectionRowPath(group), rows);
}
export function moveSectionRow(proposal: QuoteProposal, group: SectionRows, id: string, direction: -1 | 1) {
  if (direction !== -1 && direction !== 1) throw new Error('Choose an adjacent row position.');
  const rows = [...sectionRows(proposal, group)]; const index = position(rows, id), target = index + direction;
  if (target >= 0 && target < rows.length) [rows[index], rows[target]] = [rows[target], rows[index]];
  return changeField(proposal, sectionRowPath(group), rows);
}
export function changeSectionRow(proposal: QuoteProposal, group: SectionRows, id: string, path: string, value: QuoteValue | undefined) {
  const parts = path.split('.'); if (parts.some(part => !/^[a-zA-Z][a-zA-Z0-9]*$/.test(part) || ['id', 'constructor', 'prototype', '__proto__'].includes(part))) throw new Error('Invalid section field path.');
  const rows = structuredClone(sectionRows(proposal, group)); let row = rows[position(rows, id)];
  for (const part of parts.slice(0, -1)) { const child = row[part]; if (child !== undefined && (!child || typeof child !== 'object' || Array.isArray(child))) throw new Error('Cannot replace a saved field container.'); if (child === undefined) row[part] = {}; row = row[part] as QuoteObject; }
  const name = parts.at(-1)!; if (value === undefined) delete row[name]; else row[name] = structuredClone(value);
  return changeField(proposal, sectionRowPath(group), rows);
}
export function setTripDriver(proposal: QuoteProposal, tripId: string, driverId: string, selected: boolean) {
  if (!((proposal.risk?.drivers ?? []) as QuoteObject[]).some(driver => same(driver.id, driverId))) throw new Error('Choose a driver in this proposal.');
  const rows = sectionRows(proposal, 'temporaryEuropeanCover'); const row = rows[position(rows, tripId)], ids = (row.driverIds ?? []) as QuoteValue[];
  return changeSectionRow(proposal, 'temporaryEuropeanCover', tripId, 'driverIds', selected ? ids.some(id => same(id, driverId)) ? ids : [...ids, driverId] : ids.filter(id => !same(id, driverId)));
}
