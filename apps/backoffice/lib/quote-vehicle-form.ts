import type { QuoteObject, QuoteProposal, QuoteValue } from './quotes';

export type VehicleRows = 'vehicles' | 'tradePlates' | 'heldTradePlates';
const same = (value: QuoteValue | undefined, id: string) => typeof value === 'string' && value.toLowerCase() === id.toLowerCase();
export function vehicleRows(proposal: QuoteProposal, group: VehicleRows = 'vehicles'): QuoteObject[] {
  const value = proposal.risk?.[group];
  if (value === undefined) return [];
  if (!Array.isArray(value) || value.some(row => !row || typeof row !== 'object' || Array.isArray(row) || typeof row.id !== 'string')) throw new Error('Saved vehicle rows cannot be edited.');
  return value as QuoteObject[];
}
function position(rows: QuoteObject[], id: string) { const index = rows.findIndex(row => same(row.id, id)); if (index < 0) throw new Error('The selected vehicle row has changed.'); return index; }
function unique(proposal: QuoteProposal, id: string) {
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id) || id === '00000000-0000-0000-0000-000000000000') throw new Error('A distinct row identity is required.');
  function contains(value: unknown): boolean { if (!value || typeof value !== 'object') return false; if (Array.isArray(value)) return value.some(contains); return same((value as QuoteObject).id, id) || Object.values(value).some(contains); }
  if (contains(proposal)) throw new Error('A distinct row identity is required.');
}
function replace(proposal: QuoteProposal, group: VehicleRows, rows: QuoteObject[]) { const next = structuredClone(proposal); next.risk ??= {}; next.risk[group] = structuredClone(rows); return next; }
export function addVehicleRow(proposal: QuoteProposal, group: VehicleRows, id = crypto.randomUUID()) {
  unique(proposal, id); const rows = vehicleRows(proposal, group); if (rows.length >= (group === 'tradePlates' ? 150 : 1000)) throw new Error('The row limit has been reached.');
  return replace(proposal, group, [...rows, { id }]);
}
export function removeVehicleRow(proposal: QuoteProposal, group: VehicleRows, id: string) {
  const rows = [...vehicleRows(proposal, group)]; const index = position(rows, id);
  if (group === 'vehicles') {
    if ((proposal.risk?.specifiedVehicleIds as QuoteValue[] | undefined)?.some(value => same(value, id))) throw new Error('Clear the specified selection before removing this vehicle.');
    const drivers = proposal.risk?.drivers as QuoteObject[] | undefined;
    if (drivers?.some(driver => (driver.losses as QuoteObject[] | undefined)?.some(loss => same(loss.riskItemId, id)))) throw new Error('Update the retained loss reference before removing this vehicle.');
  }
  rows.splice(index, 1); return replace(proposal, group, rows);
}
export function moveVehicleRow(proposal: QuoteProposal, group: VehicleRows, id: string, direction: -1 | 1) {
  if (direction !== -1 && direction !== 1) throw new Error('Choose an adjacent row position.');
  const rows = [...vehicleRows(proposal, group)]; const index = position(rows, id), target = index + direction;
  if (target >= 0 && target < rows.length) [rows[index], rows[target]] = [rows[target], rows[index]];
  return replace(proposal, group, rows);
}
export function changeVehicleRow(proposal: QuoteProposal, group: VehicleRows, id: string, field: string, value: QuoteValue | undefined) {
  if (!/^[a-zA-Z][a-zA-Z0-9]*$/.test(field) || ['id', 'constructor', 'prototype', '__proto__', 'modifications'].includes(field)) throw new Error('Invalid vehicle field.');
  const rows = structuredClone(vehicleRows(proposal, group)), row = rows[position(rows, id)];
  if (value === undefined) delete row[field]; else row[field] = structuredClone(value); return replace(proposal, group, rows);
}
export function setSpecifiedVehicle(proposal: QuoteProposal, id: string, selected: boolean) {
  position(vehicleRows(proposal), id); const next = structuredClone(proposal); next.risk ??= {};
  const ids = (next.risk.specifiedVehicleIds ?? []) as QuoteValue[];
  next.risk.specifiedVehicleIds = selected ? ids.some(value => same(value, id)) ? ids : [...ids, id] : ids.filter(value => !same(value, id)); return next;
}
export function vehicleModifications(proposal: QuoteProposal, id: string) { const rows = vehicleRows(proposal); return (rows[position(rows, id)].modifications ?? []) as QuoteObject[]; }
export function changeVehicleModification(proposal: QuoteProposal, vehicleId: string, action: 'add' | 'remove' | 'change', id: string, value?: QuoteValue) {
  if (!['add', 'remove', 'change'].includes(action)) throw new Error('Unknown modification action.');
  const rows = structuredClone(vehicleRows(proposal)); const vehicle = rows[position(rows, vehicleId)]; const children = (vehicle.modifications ?? []) as QuoteObject[];
  if (action === 'add') { unique(proposal, id); if (children.length >= 1000) throw new Error('The modification row limit has been reached.'); children.push({ id }); }
  else { const index = position(children, id); if (action === 'remove') children.splice(index, 1); else if (value === undefined) delete children[index].code; else children[index].code = structuredClone(value); }
  vehicle.modifications = children; return replace(proposal, 'vehicles', rows);
}
