'use client';
import { QuoteLookupControl } from './quote-lookup';
import { matchingQuoteCatalogue, type QuoteFormCatalogue } from '../../lib/quote-catalogue';
import { amountInput, changeAnswer, changeResponses, countInput, selectedReference } from '../../lib/quote-form';
import { addQuoteDriver, addQuoteDriverHistory, changeQuoteDriverField, changeQuoteDriverHistoryField, moveQuoteDriver, moveQuoteDriverHistory, quoteDriverHistory, quoteDrivers, removeQuoteDriver, removeQuoteDriverHistory, type DriverHistory } from '../../lib/quote-driver-form';
import { driverOptionStates, resolvedDriverField } from '../../lib/quote-driver-options';
import type { DriverField } from '../../lib/quote-driver-fields';
import type { QuoteObject, QuoteProposal, QuoteValue, QuoteView } from '../../lib/quotes';

type Props = {
  proposal: QuoteProposal; versions: QuoteView['captureVersions']; catalogue: QuoteFormCatalogue; history?: boolean; targetId?: string;
  replace: (proposal: QuoteProposal) => void; buffers: Record<string, string>;
  setBuffer: (key: string, value: string | undefined) => void; validity: (key: string, error?: string) => void;
};
const histories: { key: DriverHistory; label: string }[] = [
  { key: 'occupations', label: 'Additional occupations' }, { key: 'convictions', label: 'Motoring convictions' },
  { key: 'losses', label: 'Accidents and claims' }, { key: 'criminalConvictions', label: 'Criminal convictions' },
  { key: 'countyCourtJudgments', label: 'County court judgments' },
];
const objectValue = (row: QuoteObject, path: string): QuoteValue | undefined => {
  let value: QuoteValue | undefined = row;
  for (const part of path.split('.')) value = value && typeof value === 'object' && !Array.isArray(value) ? value[part] : undefined;
  return value;
};
const answers = (row: QuoteObject) => (row.responses && typeof row.responses === 'object' && !Array.isArray(row.responses) ? row.responses.answers ?? [] : []) as QuoteObject[];

export function QuoteDrivers(props: Props) {
  const { proposal, versions, catalogue, replace, history = false } = props;
  const fields = catalogue.driverFields;
  if (!matchingQuoteCatalogue(versions, catalogue) || !fields) return <p role="alert">Driver details need the catalogue matching this saved quote. Existing details are retained.</p>;
  const drivers = quoteDrivers(proposal);
  const optionStates = driverOptionStates(proposal, catalogue.driverOptions?.version === catalogue.version ? catalogue.driverOptions : undefined);
  const attempt = (action: () => QuoteProposal) => { try { replace(action()); props.validity('driver-action'); } catch (error) { props.validity('driver-action', error instanceof Error ? error.message : 'The driver change could not be applied.'); } };
  const clearBuffers = (prefix: string) => { for (const key of Object.keys(props.buffers).filter(key => key.startsWith(prefix))) { props.setBuffer(key, undefined); props.validity(key); } };
  const controls = (row: QuoteObject, group: DriverField['group'], prefix: string, bufferPrefix: string, update: (path: string, value: QuoteValue | undefined) => void, driverIndex = -1) => fields.filter(field => field.group === group).map(sourceField => {
    const field = group === 'driver' ? resolvedDriverField(sourceField, driverIndex, optionStates) : sourceField;
    const responses = row.responses as QuoteObject | undefined;
    if (field.questionId && responses?.questionSetVersion && responses.questionSetVersion !== versions.questionSetVersion) return <p role="alert" key={field.id}>{prefix} answers need their matching question version. Existing answers are retained.</p>;
    const value = field.questionId ? answers(row).find(answer => answer.questionId === field.questionId)?.value : objectValue(row, field.path);
    return <DriverControl key={field.id} field={field} label={`${prefix} · ${field.label}`} value={value} bufferKey={`${bufferPrefix}/${field.id}`} props={props}
      change={next => { if (field.questionId) update('responses', changeResponses(row.responses, versions.questionSetVersion, field.questionId, field.kind, next)); else update(field.path, next); }} />;
  });
  return <div className="quote-driver-section">
    <p className="client-help">Save incomplete details at any time. Changing a declaration retains existing rows for review. Remove a row explicitly when it no longer applies.</p>
    {!history && !props.targetId && <fieldset className="quote-reference-fields"><legend>Driver basis and restrictions</legend><div className="quote-form-grid">{controls(proposal.risk ?? {}, 'plan', 'Driver basis', 'drivers/plan', (_path, value) => attempt(() => ({ ...proposal, risk: { ...proposal.risk, responses: value! } })))}</div></fieldset>}
    {history && !props.targetId && <fieldset className="quote-reference-fields"><legend>Proposer and named driver declarations</legend><p className="client-help">These declarations cover the proposer as well as all named drivers. A Yes answer does not create a history row.</p><div className="quote-form-grid">{[
      ['ef70e80708bb', 'Motoring convictions in the last five years or pending prosecutions'], ['36da21d3c935', 'Accidents, claims or losses in the last three years'],
      ['922ca15dc9ed', 'County court judgments in the last five years'], ['46414cc10100', 'Criminal convictions or pending prosecutions'],
    ].map(([suffix, label]) => {
      const id = `prototype.quote.${suffix}`; const business = proposal.risk?.business as QuoteObject | undefined;
      const value = business ? answers(business).find(answer => answer.questionId === id)?.value : undefined;
      return <label key={id}>{label}<select aria-label={label} value={value === undefined ? '' : String(value)} onChange={event => attempt(() => changeAnswer(proposal, 'risk.business.responses', versions.questionSetVersion, id, 'boolean', event.target.value === '' ? undefined : event.target.value === 'true'))}><option value="">Not answered</option><option value="true">Yes</option><option value="false">No</option></select></label>;
    })}</div></fieldset>}
    {!history && !props.targetId && <button type="button" className="button" onClick={() => attempt(() => addQuoteDriver(proposal))}>Add driver</button>}
    {drivers.length === 0 && <p>No named drivers recorded. Choose the appropriate driver basis and add named drivers when required.</p>}
    {drivers.map((driver, driverIndex) => {
      const driverId = String(driver.id); if (props.targetId && driverId !== props.targetId) return null; const prefix = `Driver ${driverIndex + 1}`; const bufferPrefix = `drivers/${driverId}`;
      const name = String(driver.fullName ?? ([driver.firstName, driver.surname].filter(Boolean).join(' ') || 'Name not recorded'));
      return <details className="quote-driver-card" key={driverId} open={!!props.targetId || driverIndex === 0}><summary>{prefix} · {name}</summary>
        {!history && <>{!props.targetId && <div className="quote-row-actions"><button type="button" className="button" aria-label={`Move ${prefix.toLowerCase()} up`} disabled={driverIndex === 0} onClick={() => attempt(() => moveQuoteDriver(proposal, driverId, -1))}>Move up</button><button type="button" className="button" aria-label={`Move ${prefix.toLowerCase()} down`} disabled={driverIndex === drivers.length - 1} onClick={() => attempt(() => moveQuoteDriver(proposal, driverId, 1))}>Move down</button><button type="button" className="button" aria-label={`Remove ${prefix.toLowerCase()}`} onClick={() => attempt(() => { const next = removeQuoteDriver(proposal, driverId); clearBuffers(bufferPrefix + '/'); return next; })}>Remove driver</button></div>}
          <p className="client-help">{props.targetId ? "Full name and separate names are independent declarations. Licence issue date and driving test date are also distinct. These proposed details are captured for review." : "Full name and separate names are independent declarations. Licence issue date and driving test date are also distinct. Demo licence lookups do not verify entitlement. Attach licence and driving record documents in Proposal evidence below."}</p>
          <div className="quote-form-grid">{controls(driver, 'driver', prefix, bufferPrefix, (path, value) => attempt(() => changeQuoteDriverField(proposal, driverId, path, value)), driverIndex)}</div><QuoteLookupControl kind="address" scope="driver" riskItemId={driverId} label={`${prefix} address`} /><QuoteLookupControl kind="licence" scope="driver" riskItemId={driverId} label={`${prefix} licence`} /></>}
        {histories.filter(group => history ? group.key !== 'occupations' : group.key === 'occupations').map(group => {
          const rows = quoteDriverHistory(proposal, driverId, group.key);
          return <fieldset className="quote-reference-fields" key={group.key}><legend>{group.label}</legend><button type="button" className="button" onClick={() => attempt(() => addQuoteDriverHistory(proposal, driverId, group.key))}>Add {group.label.toLowerCase()} for {prefix.toLowerCase()}</button>
            {rows.length === 0 && <p className="client-help">No rows recorded.</p>}
            {rows.map((row, index) => {
              const id = String(row.id); const itemPrefix = `${prefix} · ${group.label} ${index + 1}`; const itemBuffer = `${bufferPrefix}/${group.key}/${id}`;
              const update = (path: string, value: QuoteValue | undefined) => attempt(() => changeQuoteDriverHistoryField(proposal, driverId, group.key, id, path, value));
              return <details className="quote-driver-card" key={id} open><summary>{group.label} {index + 1}</summary><div className="quote-row-actions">
                <button type="button" className="button" aria-label={`Move ${itemPrefix.toLowerCase()} up`} disabled={index === 0} onClick={() => attempt(() => moveQuoteDriverHistory(proposal, driverId, group.key, id, -1))}>Move up</button>
                <button type="button" className="button" aria-label={`Move ${itemPrefix.toLowerCase()} down`} disabled={index === rows.length - 1} onClick={() => attempt(() => moveQuoteDriverHistory(proposal, driverId, group.key, id, 1))}>Move down</button>
                <button type="button" className="button" aria-label={`Remove ${itemPrefix.toLowerCase()}`} onClick={() => attempt(() => { const next = removeQuoteDriverHistory(proposal, driverId, group.key, id); clearBuffers(itemBuffer + '/'); return next; })}>Remove row</button></div>
                <div className="quote-form-grid">{controls(row, group.key, itemPrefix, itemBuffer, update)}
                  {group.key === 'losses' && <label>Related risk item<select aria-label={`${itemPrefix} · Related risk item`} value={String(row.riskItemId ?? '')} onChange={event => update('riskItemId', event.target.value || undefined)}><option value="">Not recorded</option>{(['drivers', 'vehicles', 'premises'] as const).flatMap(kind => Array.isArray(proposal.risk?.[kind]) ? (proposal.risk[kind] as QuoteObject[]).map((item, position) => <option key={String(item.id)} value={String(item.id)}>{kind} {position + 1} · {String(item.fullName ?? item.registration ?? item.firstName ?? 'Recorded item')}</option>) : [])}</select></label>}
                </div></details>;
            })}</fieldset>;
        })}</details>;
    })}
  </div>;
}

function DriverControl({ field, label, value, bufferKey, props, change }: { field: DriverField; label: string; value: QuoteValue | undefined; bufferKey: string; props: Props; change: (value: QuoteValue | undefined) => void }) {
  if (field.unavailable) return <div><p>{field.label}</p><p className="client-help">{field.unavailableReason === 'inactive' ? 'Does not apply for the current age, licence experience and cover. ' : 'Complete the policy term, age, licence and cover context to select an eligible option. '}{value === undefined ? 'Not recorded.' : 'The saved selection is retained.'}</p>{value !== undefined && <button className="button" type="button" aria-label={`Clear ${label}`} onClick={() => change(undefined)}>Clear saved selection</button>}</div>;
  if (field.kind === 'reference' || field.kind === 'enum') {
    const reference = value as QuoteObject | undefined;
    const selected = field.choices.findIndex(choice => field.kind === 'enum' ? choice.value === value : reference?.collection === field.collection && reference?.version === props.catalogue.version && reference?.value === choice.value && reference?.label === choice.text);
    return <label>{field.label}<select aria-label={label} value={value === undefined ? '' : String(selected)} onChange={event => change(event.target.value === '' ? undefined : field.kind === 'enum' ? field.choices[Number(event.target.value)].value : selectedReference(field.collection!, props.catalogue.version, field.choices, field.choices[Number(event.target.value)].value))}><option value="">Not answered</option>{selected < 0 && value !== undefined && <option value="-1" disabled>Saved selection unavailable</option>}{field.choices.map((choice, index) => <option key={index} value={index}>{choice.text}</option>)}</select></label>;
  }
  if (field.kind === 'boolean') return <label>{field.label}<select aria-label={label} value={value === undefined ? '' : String(value)} onChange={event => change(event.target.value === '' ? undefined : event.target.value === 'true')}><option value="">Not answered</option><option value="true">Yes</option><option value="false">No</option></select></label>;
  if (field.kind === 'money' || field.kind === 'count') {
    const text = props.buffers[bufferKey] ?? String(value ?? ''); const parse = (input: string) => {
      const result = field.kind === 'money' ? amountInput(input) : countInput(input);
      return !result.error && field.kind === 'count' && typeof result.value === 'number' && field.max !== undefined && result.value > field.max ? { error: `Enter a whole number from 0 to ${field.max}.` } : result;
    }; const parsed = parse(text);
    return <label>{field.label}{field.kind === 'money' ? ' (GBP)' : ''}<input aria-label={label} inputMode={field.kind === 'money' ? 'decimal' : 'numeric'} value={text} aria-invalid={Boolean(parsed.error)} onChange={event => { props.setBuffer(bufferKey, event.target.value); const next = parse(event.target.value); props.validity(bufferKey, next.error); if (!next.error) change(next.value); }} />{parsed.error && <small>{parsed.error}</small>}</label>;
  }
  if (field.kind === 'text' && (field.max ?? 4000) > 200) return <label className="quote-form-label">{field.label}<textarea aria-label={label} maxLength={field.max ?? 4000} value={String(value ?? '')} onChange={event => change(event.target.value || undefined)} /></label>;
  return <label>{field.label}<input aria-label={label} type={field.kind === 'date' ? 'date' : 'text'} maxLength={field.max ?? (field.kind === 'text' ? 4000 : undefined)} value={String(value ?? '')} onChange={event => change(event.target.value || undefined)} /></label>;
}
