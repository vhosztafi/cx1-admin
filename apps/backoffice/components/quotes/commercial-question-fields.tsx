'use client';
import {amountInput, countInput, percentageInput} from '../../lib/quote-form';
import {commercialResponse, commercialReferenceVersion, commercialQuestionApplies, commercialDecimalInput, commercialPostcodeInput, setCommercialResponse, type CommercialCatalogue, type CommercialProposal, type CommercialQuestion} from '../../lib/commercial-capture';
import type {QuoteValue} from '../../lib/quotes';

export type CommercialFormProps = {
  proposal: CommercialProposal; catalogue: CommercialCatalogue; replace: (proposal: CommercialProposal) => void;
  buffers: Record<string, string>; setBuffer: (key: string, value: string) => void; validity: (key: string, error?: string) => void;
};
export function CommercialInput({form, inputKey, label, kind = 'text', value, change, maxLength = 200}: {
  form: CommercialFormProps; inputKey: string; label: string; kind?: 'text' | 'textarea' | 'date' | 'money' | 'percentage' | 'count' | 'decimal' | 'postcode';
  value: QuoteValue | undefined; change: (value: QuoteValue | undefined) => void; maxLength?: number;
}) {
  const numeric = kind === 'money' || kind === 'percentage' || kind === 'count' || kind === 'decimal';
  const validated = numeric || kind === 'postcode';
  const text = form.buffers[inputKey] ?? (value === undefined ? '' : kind === 'percentage' ? String(Number(value) / 100) : String(value));
  const parse = (input: string) => kind === 'money' ? amountInput(input) : kind === 'percentage' ? percentageInput(input) : kind === 'decimal' ? commercialDecimalInput(input) : kind === 'postcode' ? commercialPostcodeInput(input) : countInput(input);
  const error = validated ? parse(text).error : undefined;
  function update(input: string) {
    form.setBuffer(inputKey, input);
    if (validated) {const parsed = parse(input); form.validity(inputKey, parsed.error); if (!parsed.error) change(parsed.value);}
    else change(input === '' ? undefined : input);
  }
  const props = {id: `cc-${inputKey}`, 'aria-label': label, value: text, 'aria-invalid': Boolean(error), 'aria-describedby': error ? `cc-${inputKey}-error` : undefined,
    onChange: (event: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => update(event.target.value), maxLength};
  return <label>{label}{kind === 'textarea' ? <textarea {...props} /> : <input {...props} type={kind === 'date' ? 'date' : 'text'} inputMode={numeric ? kind === 'count' ? 'numeric' : 'decimal' : undefined} />}
    {error && <small id={`cc-${inputKey}-error`}>{error}</small>}</label>;
}
export function CommercialQuestionFields({form, questions, itemId, subjectLabel}: {form: CommercialFormProps; questions: CommercialQuestion[]; itemId?: string; subjectLabel?: string}) {
  const applicable = questions.filter(q => commercialQuestionApplies(form.proposal, q, form.catalogue));
  const retained = questions.filter(q => !applicable.includes(q) && (commercialResponse(form.proposal, q, itemId) !== undefined || Boolean(form.buffers[`${q.id}:${itemId ?? 'proposal'}`])));
  const readable = (q: CommercialQuestion) => {
    const answer = commercialResponse(form.proposal, q, itemId);
    const buffered = form.buffers[`${q.id}:${itemId ?? 'proposal'}`];
    if (buffered !== undefined) return buffered;
    return typeof answer === 'boolean' ? answer ? 'Yes' : 'No' : answer && typeof answer === 'object' && !Array.isArray(answer) ? String(answer.label ?? 'Retained answer') : String(answer ?? 'Not answered');
  };
  return <><div className="quote-form-grid">{applicable.map(q => {
    const value = commercialResponse(form.proposal, q, itemId); const key = `${q.id}:${itemId ?? 'proposal'}`;
    const label = subjectLabel ? `${q.label} — ${subjectLabel}` : q.label;
    const change = (next: QuoteValue | undefined) => form.replace(setCommercialResponse(form.proposal, q, itemId, next));
    if (q.kind === 'boolean') return <label key={key}>{label}<select id={`cc-${key}`} aria-label={label} value={value === true ? 'yes' : value === false ? 'no' : ''} onChange={event => change(event.target.value === '' ? undefined : event.target.value === 'yes')}>
      <option value="">Not answered</option><option value="yes">Yes</option><option value="no">No</option></select></label>;
    if (q.kind === 'reference') {
      const selected = value && typeof value === 'object' && !Array.isArray(value) ? value.value : undefined;
      return <label key={key}>{label}<select id={`cc-${key}`} aria-label={label} value={selected === undefined ? '' : String(selected)} onChange={event => {
        const option = q.options.find(x => String(x.value) === event.target.value);
        change(option ? {collection: q.id, value: option.value, label: option.label, version: commercialReferenceVersion} : undefined);
      }}><option value="">Not answered</option>{q.options.map(x => <option key={x.value} value={String(x.value)}>{x.label}</option>)}</select></label>;
    }
    return <CommercialInput key={key} form={form} inputKey={key} label={label} value={value} kind={q.kind === 'text' ? 'textarea' : q.kind} maxLength={q.kind === 'text' ? 4000 : 30} change={change} />;
  })}</div>{retained.length > 0 && <details className="quote-reference-fields"><summary>Retained answers that are not currently applicable ({retained.length})</summary><p>These retained answers do not select cover. Review or explicitly clear them.</p>{retained.map(q => <div key={q.id}><strong>{q.label}</strong><p>{readable(q)}</p><button className="button" type="button" onClick={() => {form.replace(setCommercialResponse(form.proposal, q, itemId, undefined)); form.setBuffer(`${q.id}:${itemId ?? 'proposal'}`, ''); form.validity(`${q.id}:${itemId ?? 'proposal'}`);}}>Clear retained answer: {q.label}</button></div>)}</details>}</>;
}
