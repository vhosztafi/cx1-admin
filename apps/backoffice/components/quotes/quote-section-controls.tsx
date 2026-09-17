'use client';
import { QuoteLookupControl } from './quote-lookup';
import { coverExcessOptions } from '../../lib/quote-cover-options';
import { matchingQuoteCatalogue, type QuoteFormCatalogue } from '../../lib/quote-catalogue';
import { changeField, changeResponses, fieldValue } from '../../lib/quote-form';
import { addSectionRow, changeSectionRow, moveSectionRow, removeSectionRow, sectionRows, setTripDriver, type SectionRows } from '../../lib/quote-section-form';
import type { SectionField, SectionGroup } from '../../lib/quote-section-fields';
import type { QuoteObject, QuoteProposal, QuoteValue, QuoteView } from '../../lib/quotes';
import { QuoteCaptureControl } from './quote-vehicles';

export type QuoteSectionProps = { targetId?: string; proposal: QuoteProposal; versions: QuoteView['captureVersions']; catalogue: QuoteFormCatalogue; replace: (proposal: QuoteProposal) => void; buffers: Record<string, string>; setBuffer: (key: string, value: string | undefined) => void; validity: (key: string, error?: string) => void };
const get = (row: QuoteObject, path: string): QuoteValue | undefined => { let value: QuoteValue | undefined = row; for (const part of path.split('.')) value = value && typeof value === 'object' && !Array.isArray(value) ? value[part] : undefined; return value; };
const answerValue = (responses: QuoteValue | undefined, id: string) => responses && typeof responses === 'object' && !Array.isArray(responses) ? ((responses.answers ?? []) as QuoteObject[]).find(answer => answer.questionId === id)?.value : undefined;
function Control({ field: source, value, props, label, buffer, change }: { field: SectionField; value: QuoteValue | undefined; props: QuoteSectionProps; label: string; buffer: string; change: (value: QuoteValue | undefined) => void }) {
  let field = source;
  if (source.dynamic) {
    const catalogue = props.catalogue.driverOptions;
    const { active: eligible, collection, choices } = coverExcessOptions(props.proposal, catalogue?.version === props.catalogue.version ? catalogue : undefined);
    if (!eligible || !collection) return <div><p>{field.label}</p><p className="client-help">Select compatible cover and own-vehicle limits to choose an excess. {value === undefined ? 'Not recorded.' : 'The saved excess is retained.'}</p>{value !== undefined && <button className="button" type="button" aria-label={`Clear ${label}`} onClick={() => change(undefined)}>Clear saved excess</button>}</div>;
    field = { ...source, collection, choices };
  }
  return <QuoteCaptureControl field={field} label={label} value={value} bufferKey={buffer} props={props} change={change} />;
}
export function QuoteSectionControls({ group, prefix, ...props }: QuoteSectionProps & { group: 'cover' | 'insurance' | 'declarations'; prefix: string }) {
  const fields = props.catalogue.sectionFields;
  if (!matchingQuoteCatalogue(props.versions, props.catalogue) || !fields) return <p role="alert">This section needs the catalogue matching the saved quote. Existing answers are retained.</p>;
  return <div className="quote-form-grid">{fields.filter(field => field.group === group && field.products.includes(props.proposal.productCode)).map(field => {
    const container = field.questionId ? fieldValue(props.proposal, field.container!) as QuoteObject | undefined : undefined;
    if (container?.questionSetVersion && container.questionSetVersion !== props.versions.questionSetVersion) return <p role="alert" key={field.id}>Existing answers need their matching question version.</p>;
    const value = field.questionId ? answerValue(container, field.questionId) : fieldValue(props.proposal, field.path);
    return <Control key={field.id} field={field} value={value} props={props} label={`${prefix} · ${field.label}`} buffer={`section/${group}/${field.id}`} change={value => {
      try { props.replace(changeField(props.proposal, field.questionId ? field.container! : field.path, field.questionId ? changeResponses(container, props.versions.questionSetVersion, field.questionId, field.kind, value) : value)); props.validity(`section-${group}`); }
      catch (error) { props.validity(`section-${group}`, error instanceof Error ? error.message : 'The section change could not be applied.'); }
    }} />;
  })}</div>;
}
export function QuoteRepeatedSection({ group, singular, title, ...props }: QuoteSectionProps & { group: SectionRows; singular: string; title: string }) {
  const fields = props.catalogue.sectionFields;
  if (!matchingQuoteCatalogue(props.versions, props.catalogue) || !fields) return <p role="alert">This section needs the catalogue matching the saved quote.</p>;
  const rows = sectionRows(props.proposal, group);
  const attempt = (action: () => QuoteProposal) => { try { props.replace(action()); props.validity(`section-${group}`); } catch (error) { props.validity(`section-${group}`, error instanceof Error ? error.message : 'The section change could not be applied.'); } };
  const clear = (prefix: string) => { for (const key of Object.keys(props.buffers).filter(key => key.startsWith(prefix))) { props.setBuffer(key, undefined); props.validity(key); } };
  return <div className="quote-driver-section"><h3>{title}</h3><p className="client-help">Recorded rows remain when a controlling answer changes. Review them and remove only the rows that no longer apply.</p>{!props.targetId && <button className="button" type="button" onClick={() => attempt(() => addSectionRow(props.proposal, group))}>Add {singular.toLowerCase()}</button>}
    {rows.map((row, index) => {
      if (props.targetId && String(row.id).toLowerCase() !== props.targetId.toLowerCase()) return null;
      const id = String(row.id), prefix = `${singular} ${index + 1}`, buffer = `section/${group}/${id}`;
      return <details className="quote-driver-card" key={id} open={!!props.targetId || index === 0}><summary>{prefix} · {String(row.registration ?? get(row, 'address.postcode') ?? 'Details not entered')}</summary>
        {!props.targetId && <div className="quote-row-actions"><button type="button" className="button" aria-label={`Move ${prefix.toLowerCase()} up`} disabled={index === 0} onClick={() => attempt(() => moveSectionRow(props.proposal, group, id, -1))}>Move up</button><button type="button" className="button" aria-label={`Move ${prefix.toLowerCase()} down`} disabled={index === rows.length - 1} onClick={() => attempt(() => moveSectionRow(props.proposal, group, id, 1))}>Move down</button><button type="button" className="button" aria-label={`Remove ${prefix.toLowerCase()}`} onClick={() => attempt(() => { const next = removeSectionRow(props.proposal, group, id); clear(buffer + '/'); return next; })}>Remove {singular.toLowerCase()}</button></div>}
        <div className="quote-form-grid">{fields.filter(field => field.group === (group as SectionGroup) && field.products.includes(props.proposal.productCode)).map(field => {
          const container = field.questionId ? get(row, field.container!) as QuoteObject | undefined : undefined;
          if (container?.questionSetVersion && container.questionSetVersion !== props.versions.questionSetVersion) return <p role="alert" key={field.id}>Existing answers need their matching question version.</p>;
          return <Control key={field.id} field={field} value={field.questionId ? answerValue(container, field.questionId) : get(row, field.path)} label={`${prefix} · ${field.label}`} buffer={`${buffer}/${field.id}`} props={props} change={value => attempt(() => changeSectionRow(props.proposal, group, id, field.questionId ? field.container! : field.path, field.questionId ? changeResponses(container, props.versions.questionSetVersion, field.questionId, field.kind, value) : field.path === 'registration' && typeof value === 'string' ? value.toUpperCase() : value))} />;
        })}</div>
        {group === 'premises' && <QuoteLookupControl kind="address" scope="premises" riskItemId={id} label={`${prefix} address`} />}
        {group === 'temporaryEuropeanCover' && <fieldset className="quote-reference-fields"><legend>Trip drivers</legend>{((props.proposal.risk?.drivers ?? []) as QuoteObject[]).map((driver, position) => <label key={String(driver.id)}><input type="checkbox" aria-label={`${prefix} · Trip driver ${position + 1}`} checked={((row.driverIds ?? []) as string[]).some(value => value.toLowerCase() === String(driver.id).toLowerCase())} onChange={event => attempt(() => setTripDriver(props.proposal, id, String(driver.id), event.target.checked))} /> {String(driver.fullName ?? `Driver ${position + 1}`)}</label>)}</fieldset>}
      </details>;
    })}
  </div>;
}
