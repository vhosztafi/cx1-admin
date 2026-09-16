'use client';
import { matchingQuoteCatalogue, type QuoteFormCatalogue } from '../../lib/quote-catalogue';
import { amountInput, changeField, changeResponses, countInput, percentageInput, selectedReference } from '../../lib/quote-form';
import { addVehicleRow, changeVehicleModification, changeVehicleRow, moveVehicleRow, removeVehicleRow, setSpecifiedVehicle, vehicleModifications, vehicleRows, type VehicleRows } from '../../lib/quote-vehicle-form';
import type { VehicleField } from '../../lib/quote-vehicle-fields';
import type { QuoteObject, QuoteProposal, QuoteValue, QuoteView } from '../../lib/quotes';

type Props = { proposal: QuoteProposal; versions: QuoteView['captureVersions']; catalogue: QuoteFormCatalogue; replace: (proposal: QuoteProposal) => void; buffers: Record<string, string>; setBuffer: (key: string, value: string | undefined) => void; validity: (key: string, error?: string) => void };
const answers = (row: QuoteObject) => ((row.responses as QuoteObject | undefined)?.answers ?? []) as QuoteObject[];
const valueOf = (row: QuoteObject, field: VehicleField) => field.questionId ? answers(row).find(answer => answer.questionId === field.questionId)?.value : row[field.path];
export function QuoteVehicles(props: Props) {
  const { proposal, versions, catalogue, replace } = props; const fields = catalogue.vehicleFields;
  if (!matchingQuoteCatalogue(versions, catalogue) || !fields) return <p role="alert">Vehicle details need the catalogue matching this saved quote. Existing details are retained.</p>;
  const risk = proposal.risk ?? {}, vehicles = vehicleRows(proposal), drivers = (risk.drivers ?? []) as QuoteObject[];
  const specified = (risk.specifiedVehicleIds ?? []) as string[];
  const attempt = (action: () => QuoteProposal) => { try { replace(action()); props.validity('vehicle-action'); } catch (error) { props.validity('vehicle-action', error instanceof Error ? error.message : 'The vehicle change could not be applied.'); } };
  const clear = (prefix: string) => { for (const key of Object.keys(props.buffers).filter(key => key.startsWith(prefix))) { props.setBuffer(key, undefined); props.validity(key); } };
  const versionReady = (row: QuoteObject) => !(row.responses as QuoteObject | undefined)?.questionSetVersion || (row.responses as QuoteObject).questionSetVersion === versions.questionSetVersion;
  const controls = (row: QuoteObject, group: VehicleField['group'], prefix: string, key: string, change: (path: string, value: QuoteValue | undefined) => void, choose = (field: VehicleField) => field) => fields.filter(field => field.group === group).map(source => {
    const field = choose(source); if (field.questionId && !versionReady(row)) return <p key={field.id} role="alert">{prefix} answers need their matching question version.</p>;
    return <VehicleControl key={field.id} field={field} label={`${prefix} · ${field.label}`} value={valueOf(row, field)} bufferKey={`${key}/${field.id}`} props={props} change={value => change(field.questionId ? 'responses' : field.path, field.questionId ? changeResponses(row.responses, versions.questionSetVersion, field.questionId, field.kind, value) : value)} />;
  });
  const questionControls = (group: 'portfolio' | 'plates') => fields.filter(field => field.group === group).map(field => {
    const row = field.scope === 'business' ? risk.business as QuoteObject ?? {} : risk;
    return versionReady(row) ? <VehicleControl key={field.id} field={field} label={`${group === 'portfolio' ? 'Vehicle portfolio' : 'Trade plates'} · ${field.label}`} value={valueOf(row, field)} bufferKey={`vehicle/${group}/${field.id}`} props={props} change={value => attempt(() => changeField(proposal, field.scope === 'business' ? 'risk.business.responses' : 'risk.responses', changeResponses(row.responses, versions.questionSetVersion, field.questionId!, field.kind, value)))} /> : <p role="alert" key={field.id}>The saved answers need their matching question version.</p>;
  });
  const personal = (driver: QuoteObject) => answers(driver).some(answer => answer.questionId === 'MTS-06-Q31' && answer.value === true);
  const postcode = (row: QuoteObject | undefined) => (row?.address as QuoteObject | undefined)?.postcode;
  const overnight = [...new Set([postcode(proposal.insured), ...((risk.premises ?? []) as QuoteObject[]).map(postcode), ...drivers.filter(personal).map(postcode)].filter(value => typeof value === 'string' && value.trim()) as string[])];
  const company = (proposal.insured?.declaredCompanyType as QuoteObject | undefined)?.value;
  return <div className="quote-driver-section">
    <p className="client-help">Enter vehicle details manually. No registration lookup, licence check or MID submission has been performed. Changing a declaration retains existing details for review.</p>
    <fieldset className="quote-reference-fields"><legend>Specified vehicles</legend><div className="quote-form-grid"><label>Are specified vehicles required?<select aria-label="Are specified vehicles required?" value={risk.specifiedVehiclesRequested === undefined ? '' : String(risk.specifiedVehiclesRequested)} onChange={event => attempt(() => changeField(proposal, 'risk.specifiedVehiclesRequested', event.target.value === '' ? undefined : event.target.value === 'true'))}><option value="">Not answered</option><option value="true">Yes</option><option value="false">No</option></select></label></div><p className="client-help">Select individual vehicles below. The specified selection, vehicle register and legal owner are separate declarations.</p></fieldset>
    <p className="client-help">Owned, not for sale: {vehicles.filter(row => row.register === 'owned-not-for-sale').length} · Held for sale: {vehicles.filter(row => row.register === 'held-for-sale').length} · Register not recorded: {vehicles.filter(row => !row.register).length}</p>
    <button type="button" className="button" onClick={() => attempt(() => addVehicleRow(proposal, 'vehicles'))}>Add vehicle</button>
    {vehicles.map((vehicle, index) => {
      const id = String(vehicle.id), prefix = `Vehicle ${index + 1}`, buffer = `vehicle/${id}`, isSpecified = specified.some(value => value.toLowerCase() === id.toLowerCase());
      const owners = drivers.filter(driver => personal(driver) && (isSpecified || company !== 1 || (driver.relationship as QuoteObject | undefined)?.value !== 3));
      const choose = (field: VehicleField): VehicleField => field.path === 'keptOvernightAddress' ? { ...field, kind: 'enum', choices: overnight.map(value => ({ value, text: value })) } : field.path === 'declaredOwnerType' ? { ...field, choices: field.choices.filter(choice => (company === 1 ? [1, 4] : company === 4 ? [2, 4] : company === undefined ? [] : [2, 3, 4]).includes(Number(choice.value))) } : field;
      return <details className="quote-driver-card" key={id} open={index === 0}><summary>{prefix} · {String(vehicle.registration ?? 'Registration not entered')}</summary>
        <div className="quote-row-actions"><button className="button" type="button" aria-label={`Move vehicle ${index + 1} up`} disabled={index === 0} onClick={() => attempt(() => moveVehicleRow(proposal, 'vehicles', id, -1))}>Move up</button><button className="button" type="button" aria-label={`Move vehicle ${index + 1} down`} disabled={index === vehicles.length - 1} onClick={() => attempt(() => moveVehicleRow(proposal, 'vehicles', id, 1))}>Move down</button><button className="button" type="button" aria-label={`Remove vehicle ${index + 1}`} onClick={() => attempt(() => { const next = removeVehicleRow(proposal, 'vehicles', id); clear(buffer + '/'); return next; })}>Remove vehicle</button></div>
        <label><input type="checkbox" aria-label={`${prefix} · Specifically insured`} checked={isSpecified} onChange={event => attempt(() => setSpecifiedVehicle(proposal, id, event.target.checked))} /> Specifically insured</label>
        <div className="quote-form-grid">{controls(vehicle, 'vehicle', prefix, buffer, (field, value) => attempt(() => changeVehicleRow(proposal, 'vehicles', id, field, value)), choose)}
          <label>Named driver owner<select aria-label={`${prefix} · Named driver owner`} value={String(vehicle.ownerDriverId ?? '')} onChange={event => attempt(() => changeVehicleRow(proposal, 'vehicles', id, 'ownerDriverId', event.target.value || undefined))}><option value="">Not recorded</option>{vehicle.ownerDriverId && !owners.some(driver => driver.id === vehicle.ownerDriverId) && <option value={String(vehicle.ownerDriverId)} disabled>Saved owner is not currently eligible</option>}{owners.map((driver, position) => <option key={String(driver.id)} value={String(driver.id)}>{String(driver.fullName ?? ([driver.firstName, driver.surname].filter(Boolean).join(' ') || `Driver ${position + 1}`))}</option>)}</select></label>
        </div>
        <fieldset className="quote-reference-fields"><legend>Modifications</legend><button type="button" className="button" aria-label={`Add modification for vehicle ${index + 1}`} onClick={() => attempt(() => changeVehicleModification(proposal, id, 'add', crypto.randomUUID()))}>Add modification</button>
          {vehicleModifications(proposal, id).map((row, child) => <div className="quote-form-grid" key={String(row.id)}>{controls(row, 'modification', `${prefix} · Modification ${child + 1}`, `${buffer}/modification/${row.id}`, (_path, value) => attempt(() => changeVehicleModification(proposal, id, 'change', String(row.id), value)))}<button type="button" className="button" aria-label={`Remove modification ${child + 1} for vehicle ${index + 1}`} onClick={() => attempt(() => changeVehicleModification(proposal, id, 'remove', String(row.id)))}>Remove modification</button></div>)}
        </fieldset>
      </details>;
    })}
    <details className="quote-driver-card"><summary>Vehicle portfolio and proportions</summary><p className="client-help">Enter percentages of the vehicles handled. Selected categories must total 100%. Conditional details remain recorded until explicitly cleared.</p><div className="quote-form-grid">{questionControls('portfolio')}</div></details>
    <details className="quote-driver-card"><summary>Trade plates</summary><div className="quote-form-grid">{questionControls('plates')}</div>
      {(['heldTradePlates', 'tradePlates'] as VehicleRows[]).map(group => { const label = group === 'heldTradePlates' ? 'Held trade plates' : 'Covered trade plates'; return <fieldset className="quote-reference-fields" key={group}><legend>{label}</legend><button type="button" className="button" onClick={() => attempt(() => addVehicleRow(proposal, group))}>Add {label.toLowerCase()}</button>{vehicleRows(proposal, group).map((row, index) => <div className="quote-form-grid" key={String(row.id)}><label>Plate number<input aria-label={`${label} ${index + 1} · Plate number`} maxLength={12} value={String(row.number ?? '')} onChange={event => attempt(() => changeVehicleRow(proposal, group, String(row.id), 'number', event.target.value.toUpperCase() || undefined))} /></label><button type="button" className="button" aria-label={`Remove ${label.toLowerCase()} ${index + 1}`} onClick={() => attempt(() => removeVehicleRow(proposal, group, String(row.id)))}>Remove plate</button></div>)}</fieldset>; })}
    </details>
  </div>;
}

function VehicleControl({ field, label, value, bufferKey, props, change }: { field: VehicleField; label: string; value: QuoteValue | undefined; bufferKey: string; props: Props; change: (value: QuoteValue | undefined) => void }) {
  if (field.kind === 'reference' || field.kind === 'enum') {
    const reference = value as QuoteObject | undefined; const selected = field.choices.findIndex(choice => field.kind === 'enum' ? choice.value === value : reference?.collection === field.collection && reference?.version === props.catalogue.version && reference?.value === choice.value && reference?.label === choice.text);
    return <label>{field.label}<select aria-label={label} value={value === undefined ? '' : String(selected)} onChange={event => change(event.target.value === '' ? undefined : field.kind === 'enum' ? field.choices[Number(event.target.value)].value : selectedReference(field.collection!, props.catalogue.version, field.choices, field.choices[Number(event.target.value)].value))}><option value="">Not answered</option>{selected < 0 && value !== undefined && <option value="-1" disabled>Saved selection unavailable</option>}{field.choices.map((choice, index) => <option key={index} value={index}>{choice.text}</option>)}</select></label>;
  }
  if (field.kind === 'boolean') return <label>{field.label}<select aria-label={label} value={value === undefined ? '' : String(value)} onChange={event => change(event.target.value === '' ? undefined : event.target.value === 'true')}><option value="">Not answered</option><option value="true">Yes</option><option value="false">No</option></select></label>;
  if (['money', 'count', 'decimal', 'percentage'].includes(field.kind)) {
    const text = props.buffers[bufferKey] ?? (value === undefined ? '' : String(field.kind === 'percentage' ? Number(value) / 100 : value));
    const parse = (input: string) => {
      const parsed = field.kind === 'percentage' ? percentageInput(input) : field.kind === 'count' ? countInput(input) : field.kind === 'decimal' ? input === '' ? { value: undefined } : /^(0|[1-9]\d*)(\.\d+)?$/.test(input) && Number.isFinite(Number(input)) ? { value: Number(input) } : { error: 'Enter a positive number or zero.' } : amountInput(input);
      return !parsed.error && typeof parsed.value === 'number' && parsed.value > (field.max ?? (field.kind === 'count' ? 1000000 : Infinity)) ? { error: `Enter a value no greater than ${field.max ?? 1000000}.` } : parsed;
    }; const parsed = parse(text);
    return <label>{field.label}{field.kind === 'money' ? ' (GBP)' : field.kind === 'percentage' ? ' (%)' : ''}<input aria-label={label} inputMode={field.kind === 'count' ? 'numeric' : 'decimal'} value={text} aria-invalid={Boolean(parsed.error)} onChange={event => { props.setBuffer(bufferKey, event.target.value); const next = parse(event.target.value); props.validity(bufferKey, next.error); if (!next.error) change(next.value); }} />{parsed.error && <small>{parsed.error}</small>}</label>;
  }
  if (field.kind === 'text' && (field.max ?? 4000) > 200) return <label>{field.label}<textarea aria-label={label} maxLength={field.max ?? 4000} value={String(value ?? '')} onChange={event => change(event.target.value || undefined)} /></label>;
  return <label>{field.label}<input aria-label={label} type={field.kind === 'date' ? 'date' : 'text'} maxLength={field.max} value={String(value ?? '')} onChange={event => change((field.path === 'registration' ? event.target.value.toUpperCase() : event.target.value) || undefined)} /></label>;
}
