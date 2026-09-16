'use client';
import { matchingQuoteCatalogue, type QuoteFormCatalogue } from '../../lib/quote-catalogue';
import { changeAnswer, countInput, fieldValue } from '../../lib/quote-form';
import type { QuoteObject, QuoteProposal, QuoteValue, QuoteView } from '../../lib/quotes';

export function QuoteBusinessAnswers({ proposal, versions, catalogue, replace, buffers, setBuffer, validity }: {
  proposal: QuoteProposal; versions: QuoteView['captureVersions']; catalogue: QuoteFormCatalogue;
  replace: (proposal: QuoteProposal) => void; buffers: Record<string, string>;
  setBuffer: (key: string, value: string) => void; validity: (key: string, error?: string) => void;
}) {
  const responses = fieldValue(proposal, 'risk.business.responses') as QuoteObject | undefined;
  if (!matchingQuoteCatalogue(versions, catalogue) || (responses?.questionSetVersion && responses.questionSetVersion !== versions.questionSetVersion)) return <p role="alert">Business answers need the catalogue matching this saved quote. Existing answers are retained.</p>;
  const answers = (responses?.answers ?? []) as QuoteObject[];
  const answer = (id: string) => answers.find(item => item.questionId === id)?.value;
  const update = (id: string, kind: string, value: QuoteValue | undefined) => replace(changeAnswer(proposal, 'risk.business.responses', versions.questionSetVersion, id, kind, value));
  return <fieldset className="quote-reference-fields"><legend>Business details</legend>
    <div className="quote-form-grid">
      {(['MTS-03-Q04', 'MTS-03-Q06'] as const).map(id => {
        const label = id === 'MTS-03-Q04' ? 'Are you a member of any trade associations?' : 'Is the company VAT registered?';
        return <label key={id}>{label}<select aria-label={label} value={answer(id) === undefined ? '' : String(answer(id))} onChange={event => update(id, 'boolean', event.target.value === '' ? undefined : event.target.value === 'true')}><option value="">Not answered</option><option value="true">Yes</option><option value="false">No</option></select></label>;
      })}
      {(['MTS-03-Q05', 'MTS-03-Q07'] as const).map(id => {
        const association = id === 'MTS-03-Q05'; const label = association ? 'Trade association name' : 'VAT number';
        const active = answer(association ? 'MTS-03-Q04' : 'MTS-03-Q06') === true;
        return <label key={id}>{label}<input aria-label={label} maxLength={association ? 4000 : 20} value={String(answer(id) ?? '')} onChange={event => update(id, 'text', event.target.value || undefined)} />
          <small className="client-help">{active ? association ? 'Required for readiness when membership is Yes.' : 'Optional VAT number.' : 'Applies when the answer above is Yes. Existing details are retained until you clear them.'}</small></label>;
      })}
      {(['MTS-03-Q08', 'MTS-03-Q09'] as const).map(id => {
        const label = id === 'MTS-03-Q08' ? 'Number of vehicles handled per year' : 'Motor Insurance Policy Database (MIPD) vehicle limit';
        const key = `business-answer-${id}`; const text = buffers[key] ?? String(answer(id) ?? ''); const error = countInput(text).error;
        return <label key={id}>{label}<input aria-label={label} inputMode="numeric" value={text} aria-invalid={Boolean(error)} aria-describedby={error ? `${key}-error` : undefined} onChange={event => {
          setBuffer(key, event.target.value); const parsed = countInput(event.target.value); validity(key, parsed.error);
          if (!parsed.error) update(id, 'count', parsed.value);
        }} />{error && <small id={`${key}-error`}>{error}</small>}{id === 'MTS-03-Q08' && <small className="client-help">At least one vehicle is required for readiness. Zero can be saved as an incomplete declaration.</small>}</label>;
      })}
    </div>
  </fieldset>;
}
