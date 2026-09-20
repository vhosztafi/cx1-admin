'use client';
import {useEffect, useRef, useState, type ReactNode} from 'react';
import type {CommercialProposal, CommercialCatalogue} from '../../lib/commercial-capture';
import type {CommercialFormProps} from './commercial-question-fields';

export function CommercialItemDialog({title, initial, catalogue, close, apply, children}: {title: string; initial: CommercialProposal; catalogue: CommercialCatalogue; close: () => void; apply: (proposal: CommercialProposal) => void; children: (form: CommercialFormProps) => ReactNode}) {
  const dialog = useRef<HTMLDialogElement>(null); const [proposal, setProposal] = useState(initial);
  const [buffers, setBuffers] = useState<Record<string,string>>({}); const [invalid, setInvalid] = useState<Record<string,string>>({});
  useEffect(() => {const node = dialog.current; node?.showModal(); return () => node?.close();}, []);
  const form: CommercialFormProps = {proposal, catalogue, replace: setProposal, buffers, setBuffer: (key,value) => setBuffers(x => ({...x,[key]:value})),
    validity: (key,message) => setInvalid(x => {const next = {...x}; if (message) next[key] = message; else delete next[key]; return next;})};
  return <dialog className="agency-dialog agency-terms-dialog" ref={dialog} aria-labelledby="commercial-item-title" onCancel={event => {event.preventDefault(); close();}}>
    <h2 id="commercial-item-title">{title}</h2><p>Apply the known details to your draft, then save the quote to record the revision.</p>
    {children(form)}
    {Object.entries(invalid).map(([key,message]) => <p key={key} role="alert">{message}</p>)}
    <div className="quote-row-actions"><button className="button button-primary" type="button" disabled={Object.keys(invalid).length>0} onClick={() => apply(proposal)}>Apply {title.toLowerCase().includes('location') ? 'location' : 'wage'} details</button><button className="button" type="button" onClick={close}>Discard item edits</button></div>
  </dialog>;
}
