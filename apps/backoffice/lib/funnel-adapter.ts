import type { QuoteProposal } from './quotes';
import { annualEndDate, londonCandidates } from './quote-term.ts';
import { selectQuoteDynamicOptions, type DynamicCatalogue } from '../../../scripts/quote-dynamic-options.mjs';

type ObjectValue = Record<string, unknown>;
export type FunnelMapping = { owner: string; rawPath?: string; canonicalPath: string; contractKind: string; answerKind?: string; optionCollection?: string | null; products?: string[] };
export type FunnelCatalogue = DynamicCatalogue & { mappings: FunnelMapping[]; bindings: { owner?: string; questionId?: string; canonicalPath?: string; collections: string[] }[]; captureLimits?:{driverLimit:number;pageSize:number} };
export type FunnelState = { version: 1; formData: ObjectValue; step: number; pendingForms?: Record<string, ObjectValue>; routeState?: ObjectValue };
const object = (value: unknown): value is ObjectValue => value !== null && typeof value === 'object' && !Array.isArray(value);
const read = (value: unknown, path: string): unknown => path.split('.').reduce<unknown>((item, key) => object(item) ? item[key] : undefined, value);
function write(target: ObjectValue, path: string, value: unknown) {
  const parts = path.split('.');
  if (parts.some(key => !/^[a-zA-Z][a-zA-Z0-9]*$/.test(key) || ['constructor', 'prototype'].includes(key))) throw new Error('Unsupported funnel field.');
  let parent = target;
  for (const key of parts.slice(0, -1)) { if (!object(parent[key])) parent[key] = {}; parent = parent[key] as ObjectValue; }
  if (value === undefined) delete parent[parts.at(-1)!]; else parent[parts.at(-1)!] = value;
}
function exactDecimal(value: unknown, scale: number) {
  if (typeof value !== 'string' && typeof value !== 'number') throw new Error('A numeric answer could not be converted.');
  const text = String(value); const match = /^(0|[1-9]\d*)(?:\.(\d{1,2}))?$/.exec(text);
  if (!match) throw new Error('Use a non-negative number with at most two decimal places.');
  const minor = Number(match[1]) * 100 + Number((match[2] ?? '').padEnd(2, '0'));
  if (!Number.isSafeInteger(minor)) throw new Error('This amount is too large.');
  return scale === 100 ? minor : `${match[1]}.${(match[2] ?? '').padEnd(2, '0')}`;
}
function collectionsFor(row: FunnelMapping, catalogue: FunnelCatalogue) {
  return row.optionCollection ? [row.optionCollection] : (catalogue.bindings.find(binding => binding.owner === row.owner || binding.questionId === row.owner) ?? catalogue.bindings.find(binding=>!binding.owner&&!binding.questionId&&binding.canonicalPath===row.canonicalPath))?.collections ?? [];
}
function reference(value: unknown, row: FunnelMapping, catalogue: FunnelCatalogue) {
  // Raw ID equality remains strict. Numeric-looking strings are not silently
  // promoted to valid source options or granted reference authority.
  for (const collection of collectionsFor(row, catalogue)) {
    const option = catalogue.collections[collection]?.find(option => option.value === value);
    if (option) return { collection, value: option.value, label: option.text, version: catalogue.version };
  }
  throw new Error(`Select a supported option for ${row.rawPath ?? row.owner}.`);
}
function convert(value: unknown, row: FunnelMapping, catalogue: FunnelCatalogue, reverse: boolean): unknown {
  if (value === undefined || value === null || value === '') return undefined;
  const kind = row.answerKind ?? row.contractKind;
  if (kind.toLowerCase() === 'reference') return reverse ? object(value) ? value.value : undefined : reference(value, row, catalogue);
  if (kind === 'references') return Array.isArray(value) ? value.map(item => reverse ? object(item) ? item.value : undefined : reference(item, row, catalogue)) : undefined;
  if (row.canonicalPath === 'termIntent.kind') return reverse ? value === 'short-period' : value === true ? 'short-period' : 'annual';
  if (kind === 'Amount' || kind === 'money') return reverse ? value : exactDecimal(value, 1);
  if (kind === 'percentage' || row.canonicalPath.endsWith('turnoverBasisPoints')) return reverse ? Number(value) / 100 : exactDecimal(value, 100);
  if (kind === 'Date' || kind === 'date') {
    const text = value instanceof Date ? value.toISOString().slice(0, 10) : String(value).slice(0, 10);
    if (!/^\d{4}-\d{2}-\d{2}$/.test(text) || new Date(`${text}T00:00:00Z`).toISOString().slice(0, 10) !== text) throw new Error('An entered date is invalid.');
    return text;
  }
  if (['integer', 'number', 'count'].includes(kind)) {
    if (!/^\d+(?:\.\d+)?$/.test(String(value)) || !Number.isFinite(Number(value)) || (kind !== 'number' && !Number.isSafeInteger(Number(value)))) throw new Error(`Enter a valid number for ${row.rawPath}.`);
    return Number(value);
  }
  if (kind === 'boolean' && typeof value !== 'boolean') throw new Error(`Answer yes or no for ${row.rawPath}.`);
  return structuredClone(value);
}

const groups = [
  ['risk.drivers[]', 'drivers'], ['risk.vehicles[]', 'vehicles'], ['risk.premises[]', 'tradingPremises'],
  ['risk.business.activities[]', 'occupations'], ['cover.annualEuropeanCover[]', 'europeanCoverAnnualExtras'], ['cover.temporaryEuropeanCover[]', 'europeanCoverTemporaryExtras'],
] as const;
function field(row: FunnelMapping, raw: ObjectValue, canonical: ObjectValue, catalogue: FunnelCatalogue, reverse: boolean) {
  if (!row.rawPath) return;
  const rawPath = row.rawPath; const target = row.canonicalPath;
  if (rawPath.includes('[]')) {
    const [rawRoot, ...rawRest] = rawPath.split('[].'); const [targetRoot, ...targetRest] = target.split('[].');
    const values = read(reverse ? canonical : raw, reverse ? targetRoot : rawRoot);
    if (!Array.isArray(values) || !targetRest.length) return;
    const output: ObjectValue[] = values.map((item, index) => {
      const source = object(item) ? item : {};
      const destinationList = read(reverse ? raw : canonical, reverse ? rawRoot : targetRoot);
      const destination = Array.isArray(destinationList) && object(destinationList[index]) ? structuredClone(destinationList[index]) : {};
      const nested = { ...row, rawPath: rawRest.join('[].'), canonicalPath: targetRest.join('[].') };
      field(nested, reverse ? destination : source, reverse ? source : destination, catalogue, reverse);
      if (reverse) destination.guid = source.id;
      else if (!('id' in destination)) destination.id = typeof source.guid === 'string' ? source.guid : (source.guid = crypto.randomUUID());
      return destination;
    });
    write(reverse ? raw : canonical, reverse ? rawRoot : targetRoot, output); return;
  }
  if (row.contractKind === 'Answer') {
    const scope = target.replace(/\.answers\[\]$/, '');
    const responses = read(canonical, scope); const answers = object(responses) && Array.isArray(responses.answers) ? responses.answers : [];
    const existing = answers.find(answer => object(answer) && answer.questionId === row.owner);
    const value = convert(reverse ? object(existing) ? existing.value : undefined : read(raw, rawPath), row, catalogue, reverse);
    if (reverse) write(raw, rawPath, value);
    else {
      const retained = answers.filter(answer => !object(answer) || answer.questionId !== row.owner);
      if (value !== undefined) retained.push({ questionId: row.owner, kind: row.answerKind, value,...(row.answerKind==='percentage'?{unit:'basis-points'}:{}),...(row.answerKind==='money'?{currency:'GBP'}:{}) });
      if (retained.length) write(canonical, scope, { questionSetVersion: catalogue.version, answers: retained });
      else write(canonical, scope, undefined);
    }
    return;
  }
  const directTarget = target.endsWith('[]') ? target.slice(0,-2) : target;
  const value = convert(read(reverse ? canonical : raw, reverse ? directTarget : rawPath), row, catalogue, reverse);
  write(reverse ? raw : canonical, reverse ? rawPath : directTarget, value);
}
function map(raw: ObjectValue, proposal: ObjectValue, catalogue: FunnelCatalogue, reverse: boolean) {
  const rows = catalogue.mappings.filter(row => row.rawPath && (!row.products || row.products.includes(String(proposal.productCode))));
  for (const row of rows.filter(row => !groups.some(([prefix]) => row.canonicalPath.startsWith(prefix)))) {
    if (row.canonicalPath.includes('[]') && row.contractKind !== 'Answer') continue;
    field(row, raw, proposal, catalogue, reverse);
  }
  for (const [prefix, rawKey] of groups) {
    const groupRows = rows.filter(row => row.canonicalPath.startsWith(`${prefix}.`) && !(rawKey==='vehicles' && row.owner.startsWith('MTS-09')));
    if (!groupRows.length) continue;
    const targetKey = prefix.slice(0, -2); const items = reverse ? read(proposal, targetKey) : raw[rawKey];
    if (!Array.isArray(items)) continue;
    const mapped = items.map((item) => {
      if (!object(item)) throw new Error(`Invalid ${rawKey} row.`);
      const currentRows = reverse ? raw[rawKey] : read(proposal, targetKey);
      const identity=reverse ? item.id : item.guid;
      const existing=Array.isArray(currentRows) ? currentRows.find(candidate=>object(candidate) && (reverse ? candidate.guid : candidate.id)===identity) : undefined;
      const destination = object(existing) ? structuredClone(existing) : {};
      const rawItem = reverse ? destination : item; const canonicalItem = reverse ? item : destination;
      if (reverse) rawItem.guid = canonicalItem.id;
      else canonicalItem.id = typeof rawItem.guid === 'string' ? rawItem.guid : (rawItem.guid = crypto.randomUUID());
      for (const row of groupRows) {
        let rawPath = row.rawPath!;
        if (rawPath.startsWith(`${rawKey}[].`)) rawPath = rawPath.slice(rawKey.length + 3);
        field({ ...row, rawPath, canonicalPath: row.canonicalPath.slice(prefix.length + 1) }, rawItem, canonicalItem, catalogue, reverse);
      }
      return destination;
    });
    write(reverse ? raw : proposal, reverse ? rawKey : targetKey, mapped);
  }
}
export function proposalToFunnel(proposal: QuoteProposal, catalogue: FunnelCatalogue): ObjectValue {
  const raw: ObjectValue = {}; map(raw, structuredClone(proposal) as unknown as ObjectValue, catalogue, true);
  if(proposal.termIntent?.localStartTime)raw.proposerPolicyStartTime=proposal.termIntent.localStartTime;
  const risk=proposal.risk;
  if(Array.isArray(risk?.tradePlates))raw.tradePlateNumbers=risk.tradePlates.map(row=>object(row)?{guid:row.id,plateNumber:row.number}:{});
  if(Array.isArray(raw.vehicles) && Array.isArray(risk?.specifiedVehicleIds)) {
    const ids=risk.specifiedVehicleIds;
    raw.specifiedVehicles=raw.vehicles.filter(row=>object(row)&&ids.includes(row.guid as never));
    raw.vehicles=raw.vehicles.filter(row=>object(row)&&!ids.includes(row.guid as never));
  }
  for(const key of ['vehicles','specifiedVehicles'])if(Array.isArray(raw[key]))for(const item of raw[key]){
    if(!object(item))continue;const saved=Array.isArray(risk?.vehicles)?risk.vehicles.find(row=>object(row)&&row.id===item.guid):undefined;
    if(object(saved)&&typeof saved.ownerDriverId==='string')item.ownerGuid=saved.ownerDriverId;
  }
  if(proposal.insured?.entityType && raw.proposerCompanyTypeId===undefined){
    const labels:Record<string,string>={'sole-trader':'sole','partnership':'partnership'};
    const label=labels[String(proposal.insured.entityType)];
    if(label)raw.proposerCompanyTypeId=catalogue.collections.companyTypes?.find(row=>row.text.toLowerCase().includes(label))?.value;
  }
  return raw;
}
export function funnelToProposal(state: FunnelState, base: QuoteProposal, catalogue: FunnelCatalogue): QuoteProposal {
  const proposal = structuredClone(base) as unknown as ObjectValue;
  const raw=state.formData;
  const specified=Array.isArray(raw.specifiedVehicles)?raw.specifiedVehicles:[];
  const vehicles=raw.vehicles;
  for(const item of specified)if(object(item)&&typeof item.guid!=='string')item.guid=crypto.randomUUID();
  const normalized=specified.map(item=>object(item)?{...item,vehicleModifications:Array.isArray(item.modifications)?item.modifications.map(row=>object(row)?{...row,modificationId:row.modificationID}:row):undefined}:item);
  if(specified.length)raw.vehicles=[...(Array.isArray(vehicles)?vehicles:[]),...normalized];
  try { map(raw, proposal, catalogue, false); } finally {
    normalized.forEach((item,index)=>{const original=specified[index];if(!object(item)||!object(original)||!Array.isArray(item.vehicleModifications)||!Array.isArray(original.modifications))return;const originalRows=original.modifications;item.vehicleModifications.forEach((row,at)=>{const target=originalRows[at];if(object(row)&&object(target)&&typeof row.guid==='string')target.guid=row.guid;});});
    raw.vehicles=vehicles;
  }
  // The source clears company name for sole traders. The selected client remains
  // the legal identity; personal proposer answers do not replace that identity.
  if(raw.proposerCompanyTypeId===1 && object(proposal.insured) && !proposal.insured.legalName && base.insured?.legalName)proposal.insured.legalName=base.insured.legalName;
  const risk=object(proposal.risk)?proposal.risk:{};
  if(Array.isArray(raw.tradePlateNumbers))risk.tradePlates=raw.tradePlateNumbers.map(row=>{if(!object(row))throw new Error('Invalid trade plate.');const id=typeof row.guid==='string'?row.guid:(row.guid=crypto.randomUUID());return {id,...(row.plateNumber?{number:row.plateNumber}:{})};});
  if(Array.isArray(risk.vehicles)) {
    const all=[...(Array.isArray(vehicles)?vehicles:[]),...specified];
    risk.specifiedVehicleIds=specified.map(row=>object(row)?row.guid:undefined).filter(Boolean);
    risk.vehicles.forEach((item,index)=>{if(!object(item))return;const source=all[index];if(!object(source))return;
      const owners:Record<string,string>={'1':'business-owned','2':'business-owned','3':'driver-personal','4':'driver-personal'};
      if(source.ownerTypeID!==undefined)item.ownership=owners[String(source.ownerTypeID)];
      if(item.ownership==='driver-personal'&&typeof source.ownerGuid==='string')item.ownerDriverId=source.ownerGuid;else delete item.ownerDriverId;
    });
  }
  if(Object.keys(risk).length)proposal.risk=risk;
  if(object(proposal.insured) && object(proposal.insured.declaredCompanyType)) {
    const entity:Record<string,string>={'1':'sole-trader','2':'limited-company','3':'limited-company','4':'partnership'};
    const selected=entity[String(proposal.insured.declaredCompanyType.value)];if(selected)proposal.insured.entityType=selected;
  }
  const term = object(proposal.termIntent) ? proposal.termIntent : {};
  term.timeZone = 'Europe/London';
  if ('proposerPolicyStartTime' in raw) {
    if(typeof raw.proposerPolicyStartTime==='string' && /^(?:[01]\d|2[0-3]):[0-5]\d$/.test(raw.proposerPolicyStartTime))term.localStartTime=raw.proposerPolicyStartTime;
    else if(raw.proposerPolicyStartTime===null||raw.proposerPolicyStartTime==='')delete term.localStartTime;
    else throw new Error('Enter a valid start time.');
  }
  if(term.kind==='annual'){delete term.localEndDate;delete term.localEndTime;}
  else if(term.kind==='short-period' && typeof term.localStartDate==='string'){
    const start=new Date(`${term.localStartDate}T00:00:00Z`);const day=start.getUTCDate();start.setUTCDate(1);start.setUTCMonth(start.getUTCMonth()+6);const month=start.getUTCMonth();start.setUTCDate(day);if(start.getUTCMonth()!==month)start.setUTCDate(0);
    term.localEndDate=start.toISOString().slice(0,10);if(term.localStartTime)term.localEndTime=term.localStartTime;else delete term.localEndTime;
  }
  if(typeof term.localStartDate==='string' && typeof term.localStartTime==='string') {
    const start=londonCandidates(term.localStartDate,term.localStartTime);
    if(start.length===1)term.utcOffsetMinutes=start[0].offset;else delete term.utcOffsetMinutes;
    const endDate=term.kind==='annual'?annualEndDate(term.localStartDate):term.localEndDate;
    const endTime=term.kind==='annual'?term.localStartTime:term.localEndTime;
    if(typeof endDate==='string'&&typeof endTime==='string'){const end=londonCandidates(endDate,endTime);if(end.length===1)term.endUtcOffsetMinutes=end[0].offset;else delete term.endUtcOffsetMinutes;}
  }
  proposal.termIntent = term;
  // IDs in dependent source families can repeat. Resolve the family using the
  // same pinned current-risk selector used by back-office readiness checks.
  const dynamic=selectQuoteDynamicOptions(proposal,catalogue,{includeDriverOptions:true});
  for(const [pointer,collections] of Object.entries(dynamic.selectedCollections)){
    const keys=pointer.slice(1).split('/');let parent:unknown=proposal;
    for(const key of keys.slice(0,-1))parent=object(parent)||Array.isArray(parent)?(parent as Record<string,unknown>)[key]:undefined;
    if(!object(parent))continue;const key=keys.at(-1)!;const value=parent[key];if(!object(value))continue;
    const selected=collections.map(collection=>({collection,row:catalogue.collections[collection]?.find(row=>row.value===value.value)})).find(item=>item.row);
    if(!selected?.row)throw new Error('Review a cover or driver option for the current risk.');
    parent[key]={collection:selected.collection,value:selected.row.value,label:selected.row.text,version:catalogue.version};
  }
  for(const option of dynamic.driverOptions??[])if(option.active===false){
    const drivers=risk.drivers;if(!Array.isArray(drivers))continue;const driver=drivers[option.driverIndex];if(!object(driver)||!object(driver.responses)||!Array.isArray(driver.responses.answers))continue;
    driver.responses.answers=driver.responses.answers.filter(answer=>!object(answer)||answer.questionId!==option.questionId);
  }
  return proposal as unknown as QuoteProposal;
}
