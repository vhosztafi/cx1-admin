'use client';
import {useState} from 'react';
import {communicationCommand} from '../../lib/communications-api';
import {CommunicationCommand,type CommunicationConfirmation} from './communication-command';
import {LoadFeedback,useCommunicationResource} from './communication-shared';
import {documentDate} from './document-shared';
import type {OpsAgencyResponse} from '../../../../contracts/generated/operations';

type ResponseRequest=OpsAgencyResponse;
export function AgencyResponseTracking({messageId,actorId}:{messageId:string;actorId:string}) {
  const read=useCommunicationResource<ResponseRequest|null>(`/api/v1/messages/${messageId}/agency-response`);
  const [reason,setReason]=useState(''),[outcome,setOutcome]=useState('response-received'),[confirmation,setConfirmation]=useState<CommunicationConfirmation>(),[error,setError]=useState('');
  const saved=read.data;
  return <section aria-label="Agency response tracking"><h4>Agency response</h4>
    <button className="button" disabled={!!confirmation} onClick={read.refresh}>Refresh response status</button>
    {saved===undefined?<LoadFeedback error={read.error} retry={read.refresh}/>:saved&&saved.state!=='awaiting-response'?<><p>{saved.reference} · {saved.state==='response-received'?'Response received':'Request withdrawn'} · {documentDate(saved.resolvedAt!)}</p><p>{saved.resolutionReason}</p></>:<>
      <p>{saved?`${saved.reference} · Awaiting agency response`:'Track this delivered message when it asks the agency for a response.'}</p>
      <p>Closing a response request does not approve underwriting or change policy cover.</p>
      <form onSubmit={event=>{event.preventDefault();setError('');try{setConfirmation({command:saved?communicationCommand('resolve-response',saved.id,{outcome,reason},saved.etag):communicationCommand('track-response',messageId,{reason}),label:saved?'Close response request':'Track requested response',description:saved?'Save this correspondence outcome and retain the original delivered request.':'Show this delivered message as an open item in the agency sharing reference.'});}catch(failure){setError((failure as Error).message);}}}>
        <fieldset className="quote-reference-fields" disabled={!!confirmation}>
          {saved&&<label>Outcome<select value={outcome} onChange={event=>setOutcome(event.target.value)}><option value="response-received">Response received</option><option value="withdrawn">Request withdrawn</option></select></label>}
          <label>Internal tracking reason<textarea required minLength={10} maxLength={2000} value={reason} onChange={event=>setReason(event.target.value)}/></label>
          <button className="button" type="submit">{saved?'Close response request':'Track requested response'}</button>
        </fieldset>
      </form></>}
    {error&&<p role="alert">{error}</p>}
    {confirmation&&<CommunicationCommand request={confirmation} actorId={actorId} close={()=>setConfirmation(undefined)} saved={()=>{setConfirmation(undefined);setReason('');read.refresh();}}/>}
  </section>;
}
