import type { QuoteObject, QuoteProposal, QuoteValue } from './quotes';

export type DriverHistory = 'occupations' | 'convictions' | 'losses' | 'criminalConvictions' | 'countyCourtJudgments';
const limits: Record<DriverHistory, number> = { occupations: 5, convictions: 1000, losses: 1000, criminalConvictions: 100, countyCourtJudgments: 100 };
const sameId = (left: QuoteValue | undefined, right: string) => typeof left === 'string' && left.toLowerCase() === right.toLowerCase();
function rows(value: QuoteValue | undefined): QuoteObject[] {
  if (value === undefined) return [];
  if (!Array.isArray(value) || value.some(row => !row || typeof row !== 'object' || Array.isArray(row) || typeof row.id !== 'string')) throw new Error('Saved driver rows cannot be edited.');
  const result = value as QuoteObject[];
  if (new Set(result.map(row => String(row.id).toLowerCase())).size !== result.length) throw new Error('Row identities must be distinct.');
  return result;
}
function index(items: QuoteObject[], id: string) { const result = items.findIndex(row => sameId(row.id, id)); if (result < 0) throw new Error('The selected driver row has changed.'); return result; }
export function quoteDrivers(proposal: QuoteProposal) { return rows(proposal.risk?.drivers); }
function distinctId(proposal: QuoteProposal, id: string) {
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id) || id === '00000000-0000-0000-0000-000000000000') throw new Error('A distinct row identity is required.');
  function contains(value: unknown): boolean {
    if (!value || typeof value !== 'object') return false;
    if (Array.isArray(value)) return value.some(contains);
    const object = value as QuoteObject;
    return sameId(object.id, id) || Object.values(object).some(contains);
  }
  if (contains(proposal)) throw new Error('A distinct row identity is required.');
}
function replace(proposal: QuoteProposal, drivers: QuoteObject[]): QuoteProposal { const next = structuredClone(proposal); next.risk ??= {}; next.risk.drivers = structuredClone(drivers); return next; }
export function addQuoteDriver(proposal: QuoteProposal, id = crypto.randomUUID()) {
  distinctId(proposal, id); const drivers = quoteDrivers(proposal);
  if (drivers.length >= 1000) throw new Error('The driver row limit has been reached.');
  return replace(proposal, [...drivers, { id }]);
}
export function removeQuoteDriver(proposal: QuoteProposal, id: string) {
  const drivers = [...quoteDrivers(proposal)]; const selected = index(drivers, id);
  if (rows(proposal.risk?.vehicles).some(vehicle => sameId(vehicle.ownerDriverId, id))) throw new Error('Update the retained vehicle owner before removing this driver.');
  if (rows(proposal.cover?.temporaryEuropeanCover).some(trip => Array.isArray(trip.driverIds) && trip.driverIds.some(driver => sameId(driver, id)))) throw new Error('Update the retained trip drivers before removing this driver.');
  if (drivers.some((driver, position) => position !== selected && rows(driver.losses).some(loss => sameId(loss.riskItemId, id)))) throw new Error('Update the retained loss reference before removing this driver.');
  drivers.splice(selected, 1); return replace(proposal, drivers);
}
function move(items: QuoteObject[], id: string, direction: -1 | 1) {
  if (direction !== -1 && direction !== 1) throw new Error('Choose an adjacent row position.');
  const next = [...items]; const from = index(next, id); const to = from + direction;
  if (to >= 0 && to < next.length) [next[from], next[to]] = [next[to], next[from]];
  return next;
}
export function moveQuoteDriver(proposal: QuoteProposal, id: string, direction: -1 | 1) { return replace(proposal, move(quoteDrivers(proposal), id, direction)); }
export function quoteDriverHistory(proposal: QuoteProposal, driverId: string, group: DriverHistory) {
  if (!Object.hasOwn(limits, group)) throw new Error('Unknown driver history group.');
  const drivers = quoteDrivers(proposal); return rows(drivers[index(drivers, driverId)][group]);
}
function replaceHistory(proposal: QuoteProposal, driverId: string, group: DriverHistory, history: QuoteObject[]) {
  const drivers = structuredClone(quoteDrivers(proposal)); drivers[index(drivers, driverId)][group] = history; return replace(proposal, drivers);
}
export function addQuoteDriverHistory(proposal: QuoteProposal, driverId: string, group: DriverHistory, id = crypto.randomUUID()) {
  distinctId(proposal, id); const history = quoteDriverHistory(proposal, driverId, group);
  if (history.length >= limits[group]) throw new Error('The history row limit has been reached.');
  return replaceHistory(proposal, driverId, group, [...history, { id }]);
}
export function removeQuoteDriverHistory(proposal: QuoteProposal, driverId: string, group: DriverHistory, id: string) {
  const history = [...quoteDriverHistory(proposal, driverId, group)]; history.splice(index(history, id), 1); return replaceHistory(proposal, driverId, group, history);
}
export function moveQuoteDriverHistory(proposal: QuoteProposal, driverId: string, group: DriverHistory, id: string, direction: -1 | 1) {
  return replaceHistory(proposal, driverId, group, move(quoteDriverHistory(proposal, driverId, group), id, direction));
}

// Resolve identities at the moment of editing: captured array positions become
// stale after reorder. Never expose identity or whole history arrays as fields.
function editRow(row: QuoteObject, path: string, value: QuoteValue | undefined): QuoteObject {
  const parts = path.split('.');
  if (parts.some(part => !/^[a-zA-Z][a-zA-Z0-9]*$/.test(part) || ['id', 'constructor', 'prototype', '__proto__'].includes(part)) || Object.hasOwn(limits, parts[0])) throw new Error('Invalid driver field path.');
  const next = structuredClone(row); let current = next;
  for (const part of parts.slice(0, -1)) {
    const child = current[part];
    if (child !== undefined && (!child || typeof child !== 'object' || Array.isArray(child))) throw new Error('Cannot replace a saved field container.');
    if (child === undefined) current[part] = {};
    current = current[part] as QuoteObject;
  }
  const name = parts.at(-1)!;
  if (value === undefined) delete current[name]; else current[name] = structuredClone(value);
  return next;
}
export function changeQuoteDriverField(proposal: QuoteProposal, driverId: string, path: string, value: QuoteValue | undefined) {
  const drivers = [...quoteDrivers(proposal)]; const selected = index(drivers, driverId);
  drivers[selected] = editRow(drivers[selected], path, value); return replace(proposal, drivers);
}
export function changeQuoteDriverHistoryField(proposal: QuoteProposal, driverId: string, group: DriverHistory, id: string, path: string, value: QuoteValue | undefined) {
  const history = [...quoteDriverHistory(proposal, driverId, group)]; const selected = index(history, id);
  history[selected] = editRow(history[selected], path, value); return replaceHistory(proposal, driverId, group, history);
}
