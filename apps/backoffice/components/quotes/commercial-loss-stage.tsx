'use client';
import {useEffect, useRef, useState} from 'react';
import {addCommercialRow, changeCommercialField, commercialField, commercialRows, commercialReferenceVersion, removeCommercialRow, sumCommercialMoney, updateCommercialRow, type CommercialProposal} from '../../lib/commercial-capture';
import type {QuoteValue} from '../../lib/quotes';
import {CommercialInput, CommercialQuestionFields, type CommercialFormProps} from './commercial-question-fields';

const optionLabel = (value: QuoteValue | undefined) => value && typeof value === 'object' && !Array.isArray(value) && typeof value.label === 'string' ? value.label : 'Not entered';
const money = (value: QuoteValue | undefined) => typeof value === 'string' && /^(0|[1-9][0-9]*)\.[0-9]{2}$/.test(value) ? `£${value.replace(/\B(?=(\d{3})+(?!\d))/g, ',')}` : 'Not entered';
export function CommercialLossStage({form}: {form: CommercialFormProps}) {
  const [edit, setEdit] = useState<{id: string; proposal: CommercialProposal; isNew: boolean}>();
  const rows = commercialRows(form.proposal, 'losses'); const locations = commercialRows(form.proposal, 'locations');
  return <>
    <fieldset className="quote-reference-fields"><legend>Loss history</legend><p className="client-help">Declare loss or damage in the last five years, including uninsured losses and known circumstances.</p>
      <CommercialQuestionFields form={form} questions={form.catalogue.questions.filter(x => x.stage === 3 && !x.container.includes('[]'))} />
      <p className="client-help">Claims experience evidence is handled separately from the declared loss details.</p>
    </fieldset>
    <div className="quote-row-actions"><h3>Losses declared</h3><button className="button button-primary" type="button" disabled={rows.length >= 100} onClick={() => {
      const proposal = addCommercialRow(form.proposal, 'losses'); const id = commercialRows(proposal, 'losses').at(-1)!.id; setEdit({id, proposal, isNew: true});
    }}>Add loss</button></div>
    {rows.length === 0 ? <p className="empty-state">No losses recorded. Confirm the loss declaration or add the relevant loss details.</p> :
      <div className="table-scroll" role="region" aria-label="Declared Commercial Combined losses" tabIndex={0}><table><thead><tr><th>Date</th><th>Type</th><th>Location</th><th>Amount</th><th>Status</th><th>Circumstances</th><th>Actions</th></tr></thead><tbody>
        {rows.map((row, index) => <tr key={row.id}><td>{String(row.occurredOn ?? 'Not entered')}</td><td>{optionLabel(row.type)}</td><td>{row.riskItemId ? String(locations.find(x => x.id === row.riskItemId)?.reference ?? 'Location') : 'No specific location'}</td><td className="num">{money(row.amount)}</td><td>{String(row.status ?? 'Not entered')}</td><td>{String(row.description ?? 'Not entered')}</td><td><div className="quote-row-actions">
          <button className="button" type="button" onClick={() => setEdit({id: row.id, proposal: structuredClone(form.proposal), isNew: false})}>Edit loss {index + 1}</button>
          <button className="button" type="button" onClick={() => {if (window.confirm(`Remove loss ${index + 1} from this draft? Save the quote to record the removal.`)) form.replace(removeCommercialRow(form.proposal, 'losses', row.id));}}>Remove loss {index + 1}</button>
        </div></td></tr>)}
      </tbody></table></div>}
    {rows.length > 0 && <p>Total declared loss amount: <strong>{money(sumCommercialMoney(rows.map(row => row.amount)))}</strong></p>}
    <div className="quote-form-grid"><CommercialInput form={form} inputKey="risk.materialFacts" label="Additional loss circumstances" kind="textarea" maxLength={10000} value={commercialField(form.proposal, 'risk.materialFacts')} change={value => form.replace(changeCommercialField(form.proposal, 'risk.materialFacts', value))} /></div>
    {edit && <LossDialog key={edit.id} initial={edit} catalogue={form.catalogue} close={() => setEdit(undefined)} apply={proposal => {form.replace(proposal); setEdit(undefined);}} />}
  </>;
}
function LossDialog({initial, catalogue, close, apply}: {initial: {id: string; proposal: CommercialProposal; isNew: boolean}; catalogue: CommercialFormProps['catalogue']; close: () => void; apply: (proposal: CommercialProposal) => void}) {
  const dialog = useRef<HTMLDialogElement>(null); const [proposal, setProposal] = useState(initial.proposal);
  const [buffers, setBuffers] = useState<Record<string, string>>({}); const [invalid, setInvalid] = useState<Record<string, string>>({});
  useEffect(() => {const node = dialog.current; node?.showModal(); return () => node?.close();}, []);
  const form: CommercialFormProps = {proposal, catalogue, replace: setProposal, buffers, setBuffer: (key, value) => setBuffers(x => ({...x, [key]: value})),
    validity: (key, error) => setInvalid(x => {const next = {...x}; if (error) next[key] = error; else delete next[key]; return next;})};
  const row = commercialRows(proposal, 'losses').find(x => x.id === initial.id)!;
  const change = (path: string, value: QuoteValue | undefined) => setProposal(x => updateCommercialRow(x, 'losses', initial.id, path, value));
  const type = row.type && typeof row.type === 'object' && !Array.isArray(row.type) ? String(row.type.value) : '';
  return <dialog ref={dialog} className="agency-dialog agency-terms-dialog" aria-labelledby="commercial-loss-title" onCancel={event => {event.preventDefault(); close();}}>
    <h2 id="commercial-loss-title">{initial.isNew ? 'Add loss' : 'Edit loss'}</h2><p>Enter the known details. Apply them to your draft, then save the quote to record the revision.</p>
    <div className="quote-form-grid"><CommercialInput form={form} inputKey="loss.occurredOn" label="Loss date" kind="date" value={row.occurredOn} change={value => change('occurredOn', value)} />
      <label>Loss type<select aria-label="Loss type" value={type} onChange={event => {const choice = catalogue.lossTypes.find(x => String(x.value) === event.target.value); change('type', choice ? {collection: 'cc-loss-type', value: choice.value, label: choice.label, version: commercialReferenceVersion} : undefined);}}><option value="">Not answered</option>{catalogue.lossTypes.map(x => <option key={x.value} value={x.value}>{x.label}</option>)}</select></label>
      <label>Loss location<select aria-label="Loss location" value={String(row.riskItemId ?? '')} onChange={event => change('riskItemId', event.target.value || undefined)}><option value="">No specific location</option>{commercialRows(proposal, 'locations').map((x, index) => <option key={x.id} value={x.id}>{String(x.reference ?? `Location ${index + 1}`)}</option>)}</select></label>
      {(['amount', 'paid', 'reserve'] as const).map(path => <CommercialInput key={path} form={form} inputKey={`loss.${path}`} label={path === 'amount' ? 'Loss amount' : path === 'paid' ? 'Paid amount' : 'Outstanding reserve'} kind="money" maxLength={30} value={row[path]} change={value => change(path, value)} />)}
      <label>Loss status<select aria-label="Loss status" value={String(row.status ?? '')} onChange={event => change('status', event.target.value || undefined)}><option value="">Not answered</option>{['open', 'settled', 'repudiated', 'withdrawn'].map(x => <option key={x} value={x}>{x[0].toUpperCase() + x.slice(1)}</option>)}</select></label>
      <CommercialInput form={form} inputKey="loss.description" label="Loss circumstances" kind="textarea" maxLength={4000} value={row.description} change={value => change('description', value)} />
    </div><CommercialQuestionFields form={form} questions={catalogue.questions.filter(x => x.container === 'risk.losses[].responses')} itemId={initial.id} />
    {Object.values(invalid).map((message, index) => <p role="alert" key={index}>{message}</p>)}
    <div className="quote-row-actions"><button type="button" className="button button-primary" disabled={Object.keys(invalid).length > 0} onClick={() => apply(proposal)}>Apply loss details</button><button type="button" className="button" onClick={close}>Discard loss edits</button></div>
  </dialog>;
}
