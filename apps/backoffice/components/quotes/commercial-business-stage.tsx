'use client';
import {addCommercialRow, changeCommercialField, commercialField, commercialRows, removeCommercialRow, updateCommercialRow} from '../../lib/commercial-capture';
import type {QuoteValue} from '../../lib/quotes';
import {CommercialInput, CommercialQuestionFields, type CommercialFormProps} from './commercial-question-fields';

const entityLabels: Record<string, string> = {'sole-trader': 'Sole trader', partnership: 'Partnership', 'limited-company': 'Limited company', llp: 'LLP', 'charity-or-trust': 'Charity or trust'};
export function CommercialBusinessStage({form}: {form: CommercialFormProps}) {
  const set = (path: string, value: QuoteValue | undefined) => form.replace(changeCommercialField(form.proposal, path, value));
  const field = (path: string, label: string, kind: 'text' | 'textarea' | 'date' | 'money' = 'text', maxLength = 200) =>
    <CommercialInput key={path} form={form} inputKey={path} label={label} kind={kind} maxLength={maxLength} value={commercialField(form.proposal, path)} change={value => set(path, value)} />;
  const names = commercialField(form.proposal, 'insured.proposerNames');
  const nameText = form.buffers['insured.proposerNames'] ?? (Array.isArray(names) ? names.join('\n') : '');
  const activities = commercialRows(form.proposal, 'activities');
  return <>
    <fieldset className="quote-reference-fields"><legend>The proposer</legend><div className="quote-form-grid">
      {field('insured.legalName', 'Legal name')}
      <label>Proposer full name(s)<textarea aria-label="Proposer full name(s)" value={nameText} onChange={event => {
        const text = event.target.value; form.setBuffer('insured.proposerNames', text); const values = text.split('\n').map(x => x.trim()).filter(Boolean);
        const invalid = values.length > 3 || values.some(x => x.length > 200); form.validity('insured.proposerNames', invalid ? 'Enter up to three proposer names, each up to 200 characters.' : undefined);
        if (!invalid) set('insured.proposerNames', values);
      }} /><span className="client-help">One name per line, up to three names.</span></label>
      {field('insured.tradingName', 'Trading as')}
      <label>Entity type<select aria-label="Entity type" value={String(commercialField(form.proposal, 'insured.entityType') ?? '')} onChange={event => set('insured.entityType', event.target.value || undefined)}><option value="">Not answered</option>{form.catalogue.entityTypes.map(x => <option key={x} value={x}>{entityLabels[x] ?? x}</option>)}</select></label>
      {field('insured.companyNumber', 'Company number', 'text', 30)}
      {field('risk.liability.employersReferenceNumber', 'Employers reference number (ERN)', 'text', 100)}
      {field('insured.address.line1', 'Correspondence address line 1')}{field('insured.address.line2', 'Correspondence address line 2')}
      {field('insured.address.town', 'Town', 'text', 100)}{field('insured.address.postcode', 'Correspondence postcode', 'text', 10)}
      <label>Country<select aria-label="Country" value={String(commercialField(form.proposal, 'insured.address.country') ?? '')} onChange={event => set('insured.address.country', event.target.value || undefined)}><option value="">Not answered</option><option value="GB">United Kingdom</option></select></label>
      {field('insured.contact.telephone', 'Telephone', 'text', 50)}{field('insured.contact.email', 'Email', 'text', 254)}
    </div></fieldset>
    <fieldset className="quote-reference-fields"><legend>Business and trading history</legend><div className="quote-form-grid">
      {field('risk.business.description', 'Business or trade description', 'textarea', 4000)}
      {field('risk.business.startedOn', 'Business start date', 'date')}{field('risk.business.turnover', 'Estimated annual turnover', 'money', 30)}
      <label>VAT registered<select aria-label="VAT registered" value={commercialField(form.proposal, 'risk.business.vatRegistered') === true ? 'yes' : commercialField(form.proposal, 'risk.business.vatRegistered') === false ? 'no' : ''} onChange={event => set('risk.business.vatRegistered', event.target.value === '' ? undefined : event.target.value === 'yes')}><option value="">Not answered</option><option value="yes">Yes</option><option value="no">No</option></select></label>
    </div><CommercialQuestionFields form={form} questions={form.catalogue.questions.filter(x => x.stage === 2)} /></fieldset>
    <fieldset className="quote-reference-fields"><legend>Business activities</legend><p className="client-help">Describe the activities and their share of turnover. Shares must total 100% before progression.</p>
      {activities.map((row, index) => <fieldset className="quote-reference-fields" key={row.id}><legend>Activity {index + 1}</legend><div className="quote-form-grid">
        {(['code', 'description', 'percentageBasisPoints'] as const).map(path => <CommercialInput key={path} form={form} inputKey={`${row.id}:${path}`} label={`${path === 'code' ? 'Activity code' : path === 'description' ? 'Activity description' : 'Turnover share (%)'} ${index + 1}`} value={row[path]} kind={path === 'percentageBasisPoints' ? 'percentage' : 'text'} maxLength={path === 'description' ? 500 : 100} change={value => form.replace(updateCommercialRow(form.proposal, 'activities', row.id, path, value))} />)}
      </div><button className="button" type="button" onClick={() => form.replace(removeCommercialRow(form.proposal, 'activities', row.id))}>Remove activity {index + 1}</button></fieldset>)}
      <button className="button" type="button" disabled={activities.length >= 30} onClick={() => form.replace(addCommercialRow(form.proposal, 'activities'))}>Add activity</button>
    </fieldset>
  </>;
}
