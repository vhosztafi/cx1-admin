'use client';
import { matchingQuoteCatalogue, type QuoteFormCatalogue } from '../../lib/quote-catalogue';
import { addQuoteActivity, changeQuoteActivity, moveQuoteActivity, percentageInput, quoteActivities, removeQuoteActivity, selectedReference } from '../../lib/quote-form';
import type { QuoteObject, QuoteProposal, QuoteView } from '../../lib/quotes';

export function QuoteActivities({ proposal, versions, catalogue, replace, buffers, setBuffer, validity }: {
  proposal: QuoteProposal; versions: QuoteView['captureVersions']; catalogue: QuoteFormCatalogue;
  replace: (proposal: QuoteProposal) => void; buffers: Record<string, string>;
  setBuffer: (path: string, value: string | undefined) => void; validity: (path: string, error?: string) => void;
}) {
  if (!matchingQuoteCatalogue(versions, catalogue)) return <p role="alert">Occupation editing needs the catalogue matching this saved quote. Existing rows are retained.</p>;
  const rows = quoteActivities(proposal); const choices = catalogue.collections.mtOccupations;
  const total = rows.reduce((sum, row) => sum + Number(row.turnoverBasisPoints ?? 0), 0);
  const requiresOnlyActivity = rows.some(row => choices.some(choice => choice.requireCarJockeyRadius && choice.value === (row.code as QuoteObject | undefined)?.value));
  const codes = rows.filter(row => row.code !== undefined).map(row => `${typeof (row.code as QuoteObject).value}:${(row.code as QuoteObject).value}`);
  return <section className="quote-occupations" aria-label="Motor Trade occupations"><h3>Motor Trade occupations</h3>
    <p className="client-help">Record each occupation and its share of turnover. Each occupation needs at least 1%, with a total of 100%, for readiness. Occupation shares and the activity split above are separate declarations; incomplete rows can be saved.</p>
    {rows.length === 0 && <p>No occupations recorded.</p>}
    {rows.map((row, position) => {
      const id = String(row.id); const key = `occupation-share-${id}`; const reference = row.code as QuoteObject | undefined;
      const selected = choices.findIndex(choice => reference?.collection === 'mtOccupations' && choice.value === reference.value && choice.text === reference.label && reference.version === catalogue.version);
      const text = buffers[key] ?? (row.turnoverBasisPoints === undefined ? '' : String(Number(row.turnoverBasisPoints) / 100));
      const inputError = percentageInput(text).error;
      return <fieldset className="quote-occupation" key={id} data-activity-id={id}><legend>Occupation {position + 1}</legend>
        <div className="quote-form-grid"><label>Occupation<select aria-label={`Occupation ${position + 1}`} value={row.code === undefined ? '' : String(selected)} onChange={event => {
          const choice = event.target.value === '' ? undefined : choices[Number(event.target.value)];
          replace(changeQuoteActivity(proposal, id, 'code', choice ? selectedReference('mtOccupations', catalogue.version, choices, choice.value) : undefined));
        }}><option value="">Not answered</option>{selected < 0 && row.code !== undefined && <option value="-1" disabled>Saved selection unavailable</option>}{choices.map((choice, index) => <option key={`${typeof choice.value}:${choice.value}`} value={index}>{choice.text}</option>)}</select></label>
          <label>Turnover share (%)<input aria-label={`Occupation ${position + 1} turnover share (%)`} inputMode="decimal" value={text} aria-invalid={Boolean(inputError)} aria-describedby={inputError ? `${key}-error` : undefined} onChange={event => {
            setBuffer(key, event.target.value); const parsed = percentageInput(event.target.value); validity(key, parsed.error);
            if (!parsed.error) replace(changeQuoteActivity(proposal, id, 'turnoverBasisPoints', parsed.value));
          }} />{inputError && <small id={`${key}-error`}>{inputError}</small>}</label></div>
        <div className="operations-actions"><button className="button" type="button" aria-label={`Move occupation ${position + 1} up`} disabled={position === 0} onClick={() => replace(moveQuoteActivity(proposal, id, -1))}>Move up</button><button className="button" type="button" aria-label={`Move occupation ${position + 1} down`} disabled={position === rows.length - 1} onClick={() => replace(moveQuoteActivity(proposal, id, 1))}>Move down</button><button className="button" type="button" aria-label={`Remove occupation ${position + 1}`} onClick={() => { replace(removeQuoteActivity(proposal, id)); setBuffer(key, undefined); validity(key); }}>Remove occupation</button></div>
      </fieldset>;
    })}
    <button className="button" type="button" disabled={rows.length >= 1000} onClick={() => replace(addQuoteActivity(proposal))}>Add occupation</button>
    <p role="status">Total occupation share: {total / 100}%</p>
    {rows.length > 0 && (total !== 10000 || rows.some(row => row.code === undefined || Number(row.turnoverBasisPoints ?? 0) < 100)) && <p className="client-help">Complete each occupation and adjust the shares to meet the readiness rules. You can still save an incomplete draft.</p>}
    {new Set(codes).size !== codes.length && <p role="alert">An occupation is selected more than once. Keep one row per occupation for readiness.</p>}
    {requiresOnlyActivity && rows.length > 1 && <p role="alert">The selected car-jockey occupation must be the only occupation for readiness.</p>}
  </section>;
}
