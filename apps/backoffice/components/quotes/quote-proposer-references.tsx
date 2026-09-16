'use client';
import { matchingQuoteCatalogue, type QuoteFormCatalogue } from '../../lib/quote-catalogue';
import { changeAnswer, fieldValue, selectedReference, type ReferenceChoice } from '../../lib/quote-form';
import type { QuoteObject, QuoteProposal, QuoteValue, QuoteView } from '../../lib/quotes';

export function QuoteProposerReferences({ proposal, versions, catalogue, change, replace }: {
  proposal: QuoteProposal; versions: QuoteView['captureVersions']; catalogue: QuoteFormCatalogue;
  change: (path: string, value: QuoteValue | undefined) => void; replace: (proposal: QuoteProposal) => void;
}) {
  const responses = fieldValue(proposal, 'insured.responses') as QuoteObject | undefined;
  const supported = matchingQuoteCatalogue(versions, catalogue) && (!responses?.questionSetVersion || responses.questionSetVersion === catalogue.version);
  const answers = (responses?.answers ?? []) as QuoteObject[];
  const answer = (id: string) => answers.find(item => item.questionId === id)?.value;
  function update(id: string, kind: string, value: QuoteValue | undefined) {
    if (supported) replace(changeAnswer(proposal, 'insured.responses', versions.questionSetVersion, id, kind, value));
  }
  if (!supported) return <p role="alert">These selections need the catalogue matching this saved quote. Existing answers are retained; reference editing is unavailable.</p>;
  return <fieldset className="quote-reference-fields" disabled={!supported}><legend>Source proposer selections</legend>
    <div className="quote-form-grid">
      <ReferenceSelect label="Proposer title" value={fieldValue(proposal, 'insured.title')} collection="proposerTitles" catalogue={catalogue} change={value => change('insured.title', value)} />
      <ReferenceSelect label="Company category" value={fieldValue(proposal, 'insured.declaredCompanyType')} collection="companyTypes" catalogue={catalogue} change={value => change('insured.declaredCompanyType', value)} />
      <label>Consent to collect quotation data<select aria-label="Consent to collect quotation data" value={answer('MTS-01-Q01') === undefined ? '' : String(answer('MTS-01-Q01'))} onChange={event => update('MTS-01-Q01', 'boolean', event.target.value === '' ? undefined : event.target.value === 'true')}><option value="">Not answered</option><option value="true">Yes</option><option value="false">No</option></select></label>
    </div>
    <p className="client-help">Record the proposer’s answer about collecting relevant personal data for this quotation and any resulting policy. These quote answers do not change the client’s contact record.</p>
    <ReferenceChecks label="Marketing consent" questionId="MTS-01-Q02" collection="marketingConsents" catalogue={catalogue} value={answer('MTS-01-Q02')} update={update} />
    <ReferenceChecks label="Marketing contact methods" questionId="MTS-01-Q03" collection="marketingMethods" catalogue={catalogue} value={answer('MTS-01-Q03')} update={update} />
    <p className="client-help">Contact methods apply when marketing consent is recorded. Retained choices remain visible until you change or clear them; no messages are sent.</p>
  </fieldset>;
}

function ReferenceSelect({ label, value, collection, catalogue, change }: { label: string; value: QuoteValue | undefined; collection: 'proposerTitles' | 'companyTypes'; catalogue: QuoteFormCatalogue; change: (value: QuoteValue | undefined) => void }) {
  const choices = catalogue.collections[collection]; const saved = value as QuoteObject | undefined;
  const index = choices.findIndex(choice => saved?.collection === collection && choice.value === saved.value && choice.text === saved.label && saved.version === catalogue.version);
  return <label>{label}<select aria-label={label} value={value === undefined ? '' : String(index)} onChange={event => change(event.target.value === '' ? undefined : selectedReference(collection, catalogue.version, choices, choices[Number(event.target.value)].value))}>
    <option value="">Not answered</option>{index < 0 && value !== undefined && <option value="-1" disabled>Saved selection unavailable</option>}
    {choices.map((choice, position) => <option key={`${typeof choice.value}:${choice.value}`} value={position}>{choice.text}</option>)}
  </select></label>;
}

function ReferenceChecks({ label, questionId, collection, catalogue, value, update }: { label: string; questionId: string; collection: 'marketingConsents' | 'marketingMethods'; catalogue: QuoteFormCatalogue; value: QuoteValue | undefined; update: (id: string, kind: string, value: QuoteValue | undefined) => void }) {
  const selected = (value ?? []) as QuoteObject[];
  const matches = (reference: QuoteObject, choice: ReferenceChoice) => reference.collection === collection && reference.value === choice.value;
  return <fieldset className="quote-reference-checks"><legend>{label}</legend>
    <p className="client-help">{value === undefined ? 'Not answered' : selected.length === 0 ? 'Explicitly recorded: no selections' : `${selected.length} selected`}</p>
    {catalogue.collections[collection].map(choice => <label key={`${typeof choice.value}:${choice.value}`}><input type="checkbox" checked={selected.some(reference => matches(reference, choice))} onChange={event => {
      const remaining = selected.filter(reference => !matches(reference, choice));
      update(questionId, 'references', event.target.checked ? [...remaining, selectedReference(collection, catalogue.version, catalogue.collections[collection], choice.value)] : remaining);
    }} />{choice.text}</label>)}
    <div className="operations-actions"><button className="button" type="button" onClick={() => update(questionId, 'references', [])}>Record no {collection === 'marketingConsents' ? 'marketing consent' : 'contact methods'}</button><button className="button" type="button" onClick={() => update(questionId, 'references', undefined)}>Clear {collection === 'marketingConsents' ? 'marketing consent' : 'contact methods'} answer</button></div>
  </fieldset>;
}
