'use client';
import { changeRequestedCover, requestedCoverCodes, requestedCoverLabels, requestedCoverRows, type RequestedCoverCode } from '../../lib/requested-cover';
import type { QuoteObject } from '../../lib/quotes';
import type { QuoteSectionProps } from './quote-section-controls';

export function RequestedCover(props: QuoteSectionProps & { onlyCode?: RequestedCoverCode }) {
  return <section aria-label="Requested cover sections"><h3>Requested cover sections</h3><p className="client-help">Choose Selected or Not selected for every available section. Limits and excesses must be recorded explicitly.</p>{requestedCoverCodes(props.proposal).filter(code => !props.onlyCode || props.onlyCode === code).map(code => <RequestedSection key={code} code={code} {...props} />)}</section>;
}
function RequestedSection({ code, ...props }: QuoteSectionProps & { code: RequestedCoverCode }) {
  const existing = requestedCoverRows(props.proposal).find(row => row.code === code), prefix = `requested-cover/${code}/`;
  const error = props.buffers[prefix + 'error'] ?? '';
  const choice = props.buffers[prefix + 'selected'] ?? (existing?.selected === true ? 'true' : existing?.selected === false ? 'false' : '');
  const fields = code === 'stock-custody' ? ['limit', 'excess', 'anyOneVehicleLimit'] : ['limit', 'excess'];
  const value = (field: string) => props.buffers[prefix + field] ?? String(existing?.[field] ?? '');
  const premises = Array.isArray(props.proposal.risk?.premises) ? props.proposal.risk.premises as QuoteObject[] : [];
  const targets: string[] = props.buffers[prefix + 'premisesIds'] ? JSON.parse(props.buffers[prefix + 'premisesIds']) : (existing?.premisesIds as string[] | undefined) ?? [];
  function change(field: string, input: string) {
    props.setBuffer(prefix + field, input);
    const selected = field === 'selected' ? input : choice;
    const id = String(existing?.id ?? props.buffers[prefix + 'id'] ?? crypto.randomUUID()); props.setBuffer(prefix + 'id', id);
    const values = Object.fromEntries(fields.map(name => [name, field === name ? input : value(name)]));
    try {
      if (!selected) throw new Error('Choose Selected or Not selected.');
      const next = changeRequestedCover(props.proposal, code, id, selected === 'true', values, field === 'premisesIds' ? JSON.parse(input) : targets);
      props.replace(next); props.validity(prefix); props.setBuffer(prefix + 'error', undefined);
      if (selected === 'false') for (const name of [...fields, 'premisesIds']) props.setBuffer(prefix + name, undefined);
    } catch (failure) {
      const message = failure instanceof Error ? failure.message : 'Review the requested cover.'; props.validity(prefix, message); props.setBuffer(prefix + 'error', message);
    }
  }
  return <fieldset className="requested-cover-card"><legend>{requestedCoverLabels[code]}</legend>
    <label>{requestedCoverLabels[code]} choice<select aria-label={`${requestedCoverLabels[code]} choice`} aria-describedby={error ? `cover-error-${code}` : undefined} value={choice} onChange={event => change('selected', event.target.value)}><option value="">Not recorded</option><option value="false">Not selected</option><option value="true">Selected</option></select></label>
    {choice === 'true' && <><div className="quote-form-grid">{fields.map(field => <label key={field}>{field === 'anyOneVehicleLimit' ? 'Any one vehicle limit' : field === 'limit' ? 'Section limit' : 'Excess'} (£)<input aria-label={`${requestedCoverLabels[code]} ${field === 'anyOneVehicleLimit' ? 'any one vehicle limit' : field} (£)`} inputMode="decimal" maxLength={16} value={value(field)} aria-describedby={error ? `cover-error-${code}` : undefined} onChange={event => change(field, event.target.value)} /></label>)}</div>
      {code === 'premises' && <fieldset><legend>Covered trading premises</legend>{!premises.length && <p>Add a trading premises before selecting this cover.</p>}{premises.map((premise, index) => <label key={String(premise.id)} className="contact-check"><input type="checkbox" checked={targets.includes(String(premise.id))} onChange={event => change('premisesIds', JSON.stringify(event.target.checked ? [...targets, String(premise.id)] : targets.filter(id => id !== premise.id)))} />Premises {index + 1} · {String((premise.address as QuoteObject | undefined)?.postcode ?? 'Address not recorded')}</label>)}</fieldset>}
    </>}{error && <p id={`cover-error-${code}`} role="alert" className="error-message">{error}</p>}
  </fieldset>;
}
