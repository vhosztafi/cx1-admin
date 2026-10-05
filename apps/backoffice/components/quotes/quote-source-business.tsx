'use client';
import { matchingQuoteCatalogue, type QuoteFormCatalogue } from '../../lib/quote-catalogue';
import { changeAnswer, fieldValue, selectedReference } from '../../lib/quote-form';
import type { QuoteObject, QuoteProposal, QuoteValue, QuoteView } from '../../lib/quotes';

export function QuoteSourceBusiness({ proposal, versions, catalogue, stage, replace }: {
  proposal: QuoteProposal; versions: QuoteView['captureVersions']; catalogue: QuoteFormCatalogue; stage: 1 | 2;
  replace: (proposal: QuoteProposal) => void;
}) {
  const responses = fieldValue(proposal, 'risk.business.responses') as QuoteObject | undefined;
  if (!matchingQuoteCatalogue(versions, catalogue) || (responses?.questionSetVersion && responses.questionSetVersion !== versions.questionSetVersion)) return <p role="alert">Source business questions need the catalogue matching this saved quote. Existing answers are retained.</p>;
  const answers = (responses?.answers ?? []) as QuoteObject[];
  const questions = catalogue.businessQuestions.filter(question => question.stage === stage && question.products.includes(proposal.productCode));
  return <fieldset className="quote-reference-fields"><legend>{stage === 1 ? 'Motor trader details' : 'Trade declarations'}</legend>
    <p className="client-help">Record each answer independently. Changing an answer retains related details for review; clear a detail explicitly when it no longer applies.</p>
    {stage === 1 ? <p className="client-help">Part-time traders need a main occupation and applicable employment status. For full-time traders, main-occupation details remain visible until cleared.</p> : <p className="client-help">Describe other activity when its share is above zero, and provide details for any Yes answers below. Trading location can require premises details in a later section.</p>}
    <div className="quote-form-grid">{questions.map(question => {
      const value = answers.find(item => item.questionId === question.id)?.value;
      const update = (next: QuoteValue | undefined) => replace(changeAnswer(proposal, 'risk.business.responses', versions.questionSetVersion, question.id, question.kind, next));
      if (question.kind === 'text') return <label className="quote-form-label" key={question.id}>{question.label}<textarea aria-label={question.label} maxLength={4000} value={String(value ?? '')} onChange={event => update(event.target.value || undefined)} /></label>;
      if (question.kind === 'boolean') return <label key={question.id}>{question.label}<select aria-label={question.label} value={value === undefined ? '' : String(value)} onChange={event => update(event.target.value === '' ? undefined : event.target.value === 'true')}><option value="">Not answered</option><option value="true">Yes</option><option value="false">No</option></select></label>;
      if(question.kind==='count')return <label key={question.id}>{question.label}<input aria-label={question.label} type="number" min={0} max={1000000} step={1} value={typeof value==='number'?value:''} onChange={event=>{const text=event.target.value;if(!text)update(undefined);else if(/^\d+$/.test(text)&&Number(text)<=1000000)update(Number(text));}}/></label>;
      const saved = value as QuoteObject | undefined;
      const selected = question.choices.findIndex(choice => saved?.collection === question.collection && saved?.version === catalogue.version && saved?.value === choice.value && saved?.label === choice.text);
      return <label key={question.id}>{question.label}<select aria-label={question.label} value={value === undefined ? '' : String(selected)} onChange={event => update(event.target.value === '' ? undefined : selectedReference(question.collection!, catalogue.version, question.choices, question.choices[Number(event.target.value)].value))}>
        <option value="">Not answered</option>{selected < 0 && value !== undefined && <option value="-1" disabled>Saved selection unavailable</option>}{question.choices.map((choice, index) => <option key={index} value={index}>{choice.text}</option>)}
      </select></label>;
    })}</div>
  </fieldset>;
}
