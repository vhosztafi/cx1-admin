'use client';
import { useEffect, useRef, useState } from 'react';
import type { QuoteFormCatalogue } from '../../lib/quote-catalogue';
import type { QuoteObject, QuoteProposal } from '../../lib/quotes';
import type { ServicingChange, ServicingEditor, ServicingProposal } from '../../lib/servicing-api';
import { projectServicingCapture, putServicingChange, sameServicingId } from '../../lib/servicing-change-form';
import { QuoteDrivers } from '../quotes/quote-drivers';
import { DriverTasks } from '../operations/driver-tasks';

type Session = { operation: 'add' | 'update' | 'remove'; targetId: string; changeId: string; prior?: ServicingChange; capture: QuoteProposal };
export function ServicingDriverEditor({ proposal, editor, policyId, catalogue, disabled, change }: {
  proposal: ServicingProposal; editor: ServicingEditor; policyId: string; catalogue: QuoteFormCatalogue; disabled: boolean; change: (proposal: ServicingProposal) => void;
}) {
  const [selected, setSelected] = useState(''), [session, setSession] = useState<Session | null>(null), [error, setError] = useState('');
  const [buffers, setBuffers] = useState<Record<string, string>>({}), [invalid, setInvalid] = useState<Record<string, string>>({});
  const dialog = useRef<HTMLDialogElement>(null), heading = useRef<HTMLHeadingElement>(null), trigger = useRef<HTMLElement | null>(null);
  const resume = useRef<HTMLButtonElement>(null);
  let current: QuoteProposal | undefined; let contextError = '';
  try { current = projectServicingCapture(editor.assessment.base, proposal, policyId, editor.clientId); }
  catch (failure) { contextError = failure instanceof Error ? failure.message : 'The local proposal could not be prepared.'; }
  const drivers = (current?.risk?.drivers ?? []) as QuoteObject[];
  const target = drivers.some(row => row.id === selected) ? selected : String(drivers[0]?.id ?? '');
  const hasSession = session !== null;
  useEffect(() => { if (hasSession) { dialog.current?.showModal(); heading.current?.focus(); } }, [hasSession]);
  function close() { dialog.current?.close(); setSession(null); requestAnimationFrame(() => trigger.current?.focus()); }
  function keepForm() { dialog.current?.close(); resume.current?.focus(); }
  function begin(operation: Session['operation']) {
    if (!current || disabled || operation !== 'add' && !target) return;
    trigger.current = document.activeElement as HTMLElement; setError(''); setBuffers({}); setInvalid({});
    const targetId = operation === 'add' ? crypto.randomUUID() : target;
    const capture = structuredClone(current);
    if (operation === 'add') { capture.risk ??= {}; capture.risk.drivers = [...((capture.risk.drivers ?? []) as QuoteObject[]), { id: targetId }]; }
    const prior = proposal.changes.find(item => item.kind === 'driver' && sameServicingId(item.riskItemId, targetId));
    setSession({ operation, targetId, changeId: prior?.changeId ?? crypto.randomUUID(), prior, capture });
  }
  function apply() {
    if (!session || disabled || Object.keys(invalid).length) return;
    try {
      if (session.operation === 'remove' && session.prior?.operation === 'add') change({ ...proposal, changes: proposal.changes.filter(item => item.changeId !== session.prior!.changeId) });
      else {
        const operation = session.operation === 'update' && session.prior?.operation === 'add' ? 'add' : session.operation;
        const item: ServicingChange = { changeId: session.changeId, riskItemId: session.targetId, kind: 'driver', operation };
        if (operation !== 'remove') {
          const row = ((session.capture.risk?.drivers ?? []) as QuoteObject[]).find(value => value.id === session.targetId);
          if (!row) throw new Error('The driver is no longer present. Reopen the editor.');
          item.payload = structuredClone(row); delete item.payload.id;
          if (operation === 'update') item.payloadMode = 'replace';
        }
        change(putServicingChange(proposal, item));
      }
      close();
    } catch (failure) { setError(failure instanceof Error ? failure.message : 'The change could not be prepared.'); }
  }
  const controls = session ? { proposal: session.capture, targetId: session.targetId, versions: editor.captureVersions, catalogue,
    replace: (capture: QuoteProposal) => setSession(value => value ? { ...value, capture } : null), buffers,
    setBuffer: (key: string, value: string | undefined) => setBuffers(existing => { const next = { ...existing }; if (value === undefined) delete next[key]; else next[key] = value; return next; }),
    validity: (key: string, value?: string) => setInvalid(existing => { const next = { ...existing }; if (value) next[key] = value; else delete next[key]; return next; }) } : null;
  return <section aria-label="Named driver changes"><h3>Named drivers</h3>
    {contextError ? <p role="alert">{contextError}</p> : null}
    <div className="operations-actions">{session ? <button ref={resume} type="button" className="button" onClick={() => { dialog.current?.showModal(); heading.current?.focus(); }}>Resume driver form</button> : null}<button type="button" className="button" disabled={disabled || !!contextError || !!session} onClick={() => begin('add')}>Add named driver</button>
      <label>Named driver to change<select aria-label="Named driver to change" disabled={!!contextError} value={target} onChange={event => setSelected(event.target.value)}><option value="">Select a named driver</option>{drivers.map((driver, index) => <option key={String(driver.id)} value={String(driver.id)}>{String(driver.fullName ?? ([driver.firstName, driver.surname].filter(Boolean).join(' ') || `Driver ${index + 1}`))}</option>)}</select></label>
      <button type="button" className="button" disabled={disabled || !target || !!contextError || !!session} onClick={() => begin('update')}>Edit named driver</button>
      <button type="button" className="button" disabled={disabled || !target || !!contextError || !!session} onClick={() => begin('remove')}>Remove named driver</button></div>
    {target&&<DriverTasks key={target} policyId={policyId} driverId={target}/>}
    <dialog className="agency-dialog agency-terms-dialog servicing-change-dialog" ref={dialog} aria-labelledby="servicing-driver-title" onCancel={event => { event.preventDefault(); keepForm(); }}>
      {session && controls ? <form noValidate onSubmit={event => { event.preventDefault(); apply(); }}><h2 id="servicing-driver-title" tabIndex={-1} ref={heading}>{session.operation === 'remove' ? 'Remove named driver' : session.operation === 'add' ? 'Add named driver' : 'Edit named driver'}</h2>
        <p>Apply this proposal, then save the draft. Issued cover remains unchanged. Incomplete details can be saved for review.</p>
        {disabled ? <p role="alert">Editing ownership changed. These form values are retained; reacquire the draft before applying them.</p> : null}
        <fieldset disabled={disabled}>{session.operation === 'remove' ? <p>The selected driver will be removed from the proposal. Any vehicle ownership, trip or loss references must be explicitly corrected before saving.</p> : <><QuoteDrivers {...controls} /><QuoteDrivers {...controls} history /></>}</fieldset>
        {Object.values(invalid).map((message, index) => <p role="alert" key={index}>{message}</p>)}{error ? <p role="alert">{error}</p> : null}
        <div className="operations-actions"><button className="button button-primary" disabled={disabled || Object.keys(invalid).length > 0}>Apply to draft</button><button className="button" type="button" onClick={keepForm}>Keep form and return</button><button className="button" type="button" onClick={close}>Discard form</button></div>
      </form> : null}
    </dialog>
  </section>;
}
