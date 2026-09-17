'use client';
import { useEffect, useRef, useState } from 'react';
import type { QuoteFormCatalogue } from '../../lib/quote-catalogue';
import type { QuoteObject, QuoteProposal } from '../../lib/quotes';
import type { ServicingChange, ServicingEditor, ServicingProposal } from '../../lib/servicing-api';
import { projectServicingCapture, putServicingVehicleChange, sameServicingId } from '../../lib/servicing-change-form';
import { QuoteVehicles } from '../quotes/quote-vehicles';
import { changeField } from '../../lib/quote-form';
import { setSpecifiedVehicle } from '../../lib/quote-vehicle-form';

type Session = { operation: 'add' | 'update' | 'remove'; targetId: string; changeId: string; prior?: ServicingChange; capture: QuoteProposal };
export function ServicingVehicleEditor({ proposal, editor, policyId, catalogue, disabled, change }: {
  proposal: ServicingProposal; editor: ServicingEditor; policyId: string; catalogue: QuoteFormCatalogue; disabled: boolean; change: (proposal: ServicingProposal) => void;
}) {
  const [selected, setSelected] = useState(''), [session, setSession] = useState<Session | null>(null), [error, setError] = useState('');
  const [buffers, setBuffers] = useState<Record<string, string>>({}), [invalid, setInvalid] = useState<Record<string, string>>({});
  const dialog = useRef<HTMLDialogElement>(null), heading = useRef<HTMLHeadingElement>(null), trigger = useRef<HTMLElement | null>(null);
  const resume = useRef<HTMLButtonElement>(null);
  let current: QuoteProposal | undefined; let contextError = '';
  try { current = projectServicingCapture(editor.assessment.base, proposal, policyId, editor.clientId); }
  catch (failure) { contextError = failure instanceof Error ? failure.message : 'The local proposal could not be prepared.'; }
  const vehicles = (current?.risk?.vehicles ?? []) as QuoteObject[];
  const target = vehicles.some(row => row.id === selected) ? selected : String(vehicles[0]?.id ?? '');
  const hasSession = session !== null;
  useEffect(() => { if (hasSession) { dialog.current?.showModal(); heading.current?.focus(); } }, [hasSession]);
  function close() { dialog.current?.close(); setSession(null); requestAnimationFrame(() => trigger.current?.focus()); }
  function keepForm() { dialog.current?.close(); resume.current?.focus(); }
  function begin(operation: Session['operation']) {
    if (!current || disabled || operation !== 'add' && !target) return;
    trigger.current = document.activeElement as HTMLElement; setError(''); setBuffers({}); setInvalid({});
    const targetId = operation === 'add' ? crypto.randomUUID() : target;
    const capture = structuredClone(current);
    if (operation === 'add') { capture.risk ??= {}; capture.risk.vehicles = [...((capture.risk.vehicles ?? []) as QuoteObject[]), { id: targetId }]; }
    const prior = proposal.changes.find(item => item.kind === 'vehicle' && sameServicingId(item.riskItemId, targetId));
    setSession({ operation, targetId, changeId: prior?.changeId ?? crypto.randomUUID(), prior, capture });
  }
  function apply() {
    if (!session || disabled || Object.keys(invalid).length) return;
    try {
      if (session.operation === 'remove' && session.prior?.operation === 'add') change({ ...proposal, changes: proposal.changes.filter(item => item.changeId !== session.prior!.changeId) });
      else {
        const required = session.capture.risk?.specifiedVehiclesRequested;
        const selected = ((session.capture.risk?.specifiedVehicleIds ?? []) as string[]).some(id => sameServicingId(id, session.targetId));
        if (typeof required !== 'boolean') throw new Error('Choose whether specified vehicles are required.');
        if (session.operation === 'remove' && selected) throw new Error('Explicitly clear the specified selection before removing this vehicle.');
        const operation = session.operation === 'update' && session.prior?.operation === 'add' ? 'add' : session.operation;
        const item: ServicingChange = { changeId: session.changeId, riskItemId: session.targetId, kind: 'vehicle', operation, specifiedVehicle: { selected, required } };
        if (operation !== 'remove') {
          const row = ((session.capture.risk?.vehicles ?? []) as QuoteObject[]).find(value => value.id === session.targetId);
          if (!row) throw new Error('The vehicle is no longer present. Reopen the editor.');
          item.payload = structuredClone(row); delete item.payload.id;
          if (operation === 'update') item.payloadMode = 'replace';
        }
        change(putServicingVehicleChange(proposal, item));
      }
      close();
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'The change could not be prepared.'); }
  }
  const controls = session ? { proposal: session.capture, targetId: session.targetId, versions: editor.captureVersions, catalogue,
    replace: (capture: QuoteProposal) => setSession(value => value ? { ...value, capture } : null), buffers,
    setBuffer: (key: string, value: string | undefined) => setBuffers(existing => { const next = { ...existing }; if (value === undefined) delete next[key]; else next[key] = value; return next; }),
    validity: (key: string, value?: string) => setInvalid(existing => { const next = { ...existing }; if (value) next[key] = value; else delete next[key]; return next; }) } : null;
  return <section aria-label="Vehicle changes"><h3>Vehicles</h3>
    {contextError ? <p role="alert">{contextError}</p> : null}
    <div className="operations-actions">{session ? <button ref={resume} type="button" className="button" onClick={() => { dialog.current?.showModal(); heading.current?.focus(); }}>Resume vehicle form</button> : null}<button type="button" className="button" disabled={disabled || !!contextError || !!session} onClick={() => begin('add')}>Add vehicle</button>
      <label>Vehicle to change<select aria-label="Vehicle to change" disabled={disabled} value={target} onChange={event => setSelected(event.target.value)}><option value="">Select a vehicle</option>{vehicles.map((vehicle, index) => <option key={String(vehicle.id)} value={String(vehicle.id)}>{String(vehicle.registration || `Vehicle ${index + 1}`)}</option>)}</select></label>
      <button type="button" className="button" disabled={disabled || !target || !!contextError || !!session} onClick={() => begin('update')}>Edit vehicle</button>
      <button type="button" className="button" disabled={disabled || !target || !!contextError || !!session} onClick={() => begin('remove')}>Remove vehicle</button></div>
    <dialog className="agency-dialog agency-terms-dialog servicing-change-dialog" ref={dialog} aria-labelledby="servicing-vehicle-title" onCancel={event => { event.preventDefault(); keepForm(); }}>
      {session && controls ? <form noValidate onSubmit={event => { event.preventDefault(); apply(); }}><h2 id="servicing-vehicle-title" tabIndex={-1} ref={heading}>{session.operation === 'remove' ? 'Remove vehicle' : session.operation === 'add' ? 'Add vehicle' : 'Edit vehicle'}</h2>
        <p>Apply this proposal, then save the draft. Issued cover remains unchanged. Incomplete details can be saved for review. Your specified-vehicle requirement choice applies to all vehicle changes in this draft.</p>
        {disabled ? <p role="alert">Editing ownership changed. These form values are retained; reacquire the draft before applying them.</p> : null}
        <fieldset disabled={disabled}>{session.operation === 'remove' && session.prior?.operation === 'add' ? <p>This cancels the proposed vehicle addition and its selection declaration. Other saved changes are retained.</p> : session.operation === 'remove' ? <><p>The selected vehicle will be removed from the proposal. Explicitly clear its specified selection. Other dependent references must be corrected before saving.</p><label><input type="checkbox" aria-label="Vehicle remains specifically insured" checked={((session.capture.risk?.specifiedVehicleIds ?? []) as string[]).some(id => sameServicingId(id, session.targetId))} onChange={event => controls.replace(setSpecifiedVehicle(session.capture, session.targetId, event.target.checked))} /> Vehicle remains specifically insured</label><label>Are specified vehicles required?<select aria-label="Are specified vehicles required?" value={session.capture.risk?.specifiedVehiclesRequested === undefined ? '' : String(session.capture.risk.specifiedVehiclesRequested)} onChange={event => controls.replace(changeField(session.capture, 'risk.specifiedVehiclesRequested', event.target.value === '' ? undefined : event.target.value === 'true'))}><option value="">Not answered</option><option value="true">Yes</option><option value="false">No</option></select></label></> : <QuoteVehicles {...controls} />}</fieldset>
        {Object.values(invalid).map((message, index) => <p role="alert" key={index}>{message}</p>)}{error ? <p role="alert">{error}</p> : null}
        <div className="operations-actions"><button className="button button-primary" disabled={disabled || Object.keys(invalid).length > 0}>Apply to draft</button><button className="button" type="button" onClick={keepForm}>Keep form and return</button><button className="button" type="button" onClick={close}>Discard form</button></div>
      </form> : null}
    </dialog>
  </section>;
}
