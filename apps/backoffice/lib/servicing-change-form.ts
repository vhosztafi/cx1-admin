import type { QuoteObject, QuoteProposal, QuoteValue } from './quotes';
import type { ServicingChange, ServicingEffectiveIntent, ServicingProposal } from './servicing-api';
import { servicingDateFeedback } from './servicing-proposal.ts';
export const sameServicingId = (left: unknown, right: string) => typeof left === 'string' && left.toLowerCase() === right.toLowerCase();

export function projectServicingCaptureAt(base: QuoteProposal, proposal: ServicingProposal, policyId: string, clientId: string, intent: ServicingEffectiveIntent): QuoteProposal {
  const instant = servicingDateFeedback(intent).instant;
  if (instant === undefined) throw new Error('Resolve the effective date and clock offset before editing cover.');
  const changes = proposal.changes.filter(change => {
    const effective = servicingDateFeedback(change.effectiveIntent ?? proposal.commonEffectiveIntent).instant;
    if (effective === undefined) throw new Error('Resolve all change dates before editing cover.');
    return effective <= instant;
  });
  return projectServicingCapture(base, { ...proposal, changes }, policyId, clientId);
}

export function putServicingChange(proposal: ServicingProposal, change: ServicingChange): ServicingProposal {
  const exists = proposal.changes.some(item => sameServicingId(item.changeId, change.changeId));
  if (!exists && proposal.changes.length >= 100) throw new Error('A draft can contain at most 100 changes.');
  return { ...proposal, changes: exists ? proposal.changes.map(item => sameServicingId(item.changeId, change.changeId) ? structuredClone(change) : item) : [...proposal.changes, structuredClone(change)] };
}
// The dialog explicitly states that this requirement applies to all vehicle changes.
export function putServicingVehicleChange(proposal: ServicingProposal, change: ServicingChange): ServicingProposal {
  if (change.kind !== 'vehicle' || !change.specifiedVehicle) throw new Error('A vehicle declaration is required.');
  const required = change.specifiedVehicle.required;
  return putServicingChange({ ...proposal, changes: proposal.changes.map(item => item.kind === 'vehicle' && item.specifiedVehicle
    ? { ...item, specifiedVehicle: { ...item.specifiedVehicle, required } } : item) }, change);
}
const object = (value: QuoteValue | undefined): QuoteObject => value && typeof value === 'object' && !Array.isArray(value) ? value : {};
function merge(target: QuoteObject, payload: QuoteObject) {
  for (const [key, value] of Object.entries(payload)) {
    if (['__proto__', 'constructor', 'prototype'].includes(key)) throw new Error('Unsupported field.');
    if (typeof value === 'object' && !Array.isArray(value) && typeof target[key] === 'object' && !Array.isArray(target[key])) merge(object(target[key]), value);
    else target[key] = structuredClone(value);
  }
}
// Local form context only. The server independently validates typed shape,
// ownership, dependencies, dates and readiness before persisting any proposal.
export function projectServicingCapture(base: QuoteProposal, proposal: ServicingProposal, policyId: string, clientId: string): QuoteProposal {
  const result = structuredClone(base); result.risk ??= {}; result.cover ??= {}; result.insured ??= {};
  const ordered = proposal.changes.map((change, index) => ({ change, index, instant: servicingDateFeedback(change.effectiveIntent ?? proposal.commonEffectiveIntent).instant ?? 0 }))
    .sort((a, b) => a.instant - b.instant || a.index - b.index);
  function update(target: QuoteObject, payload: QuoteObject, replace: boolean) {
    if (replace) for (const key of Object.keys(target)) if (key !== 'id') delete target[key];
    merge(target, payload);
  }
  let specifiedRequired: boolean | undefined;
  for (const { change } of ordered) {
    if (change.specifiedVehicle) {
      const declaration = change.specifiedVehicle;
      if (change.kind !== 'vehicle' || typeof declaration.selected !== 'boolean' || typeof declaration.required !== 'boolean') throw new Error('Specified vehicle declarations require a vehicle and both choices.');
      if (change.operation === 'remove' && declaration.selected) throw new Error('A removed vehicle cannot remain specified.');
      if (specifiedRequired !== undefined && specifiedRequired !== declaration.required) throw new Error('Vehicle changes must agree whether specified vehicles are required.');
      specifiedRequired = declaration.required;
      const references = (result.risk.specifiedVehicleIds ?? []) as string[];
      result.risk.specifiedVehicleIds = declaration.selected
        ? references.some(id => sameServicingId(id, change.riskItemId)) ? [...references] : [...references, change.riskItemId]
        : references.filter(id => !sameServicingId(id, change.riskItemId));
      result.risk.specifiedVehiclesRequested = declaration.required;
    }
    const payload = structuredClone(change.payload ?? {});
    if(change.kind==='risk-details'){
      if(change.operation!=='update'||!sameServicingId(change.riskItemId,policyId))throw new Error('Risk declarations do not belong to this policy.');
      const retained=['business','drivers','vehicles','premises','specifiedVehicleIds','specifiedVehiclesRequested'];
      if(Object.keys(payload).some(key=>retained.includes(key)))throw new Error('Risk declarations cannot replace independently owned risk records.');
      if(change.payloadMode==='replace')for(const key of Object.keys(result.risk))if(!retained.includes(key))delete result.risk[key];
      merge(result.risk,payload);continue;
    }
    if (change.kind === 'business' || change.kind === 'policyholder' || change.kind === 'cover' && sameServicingId(change.riskItemId, policyId)) {
      if (change.operation !== 'update' || !sameServicingId(change.riskItemId, change.kind === 'policyholder' ? clientId : policyId)) throw new Error('This change does not belong to the selected policy.');
      const target = change.kind === 'business' ? (result.risk.business ??= {}) as QuoteObject : change.kind === 'policyholder' ? result.insured : result.cover;
      update(target, payload, change.payloadMode === 'replace'); continue;
    }
    const collection = { driver: 'drivers', vehicle: 'vehicles', premises: 'premises', cover: 'requestedSections' }[change.kind];
    const owner = change.kind === 'cover' ? result.cover : result.risk;
    const rows = (owner[collection] ??= []) as QuoteObject[];
    const index = rows.findIndex(row => sameServicingId(row.id, change.riskItemId));
    const values = change.kind === 'cover' && change.operation !== 'remove' ? structuredClone((payload.requestedSections as QuoteObject[])[0]) : payload;
    delete values.id;
    if (change.operation === 'add') {
      if (index >= 0) throw new Error('This item is already present. Edit its proposed change.');
      rows.push({ ...values, id: change.riskItemId });
    } else {
      if (index < 0) throw new Error('The selected item is no longer present in this proposal.');
      if (change.operation === 'remove') rows.splice(index, 1); else update(rows[index], values, change.payloadMode === 'replace');
    }
  }
  return result;
}
