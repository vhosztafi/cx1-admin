'use client';
import { useState } from 'react';
import type { DocumentVersion } from '../../lib/documents-api';
import { communicationCommand } from '../../lib/communications-api';
import { CommunicationCommand, type CommunicationConfirmation } from './communication-command';
import { ChoiceList } from './thread';
import { DocumentPreview } from './document-preview';

export type PackSelection={version:DocumentVersion;relationshipId:string};
export function DocumentPack({subjectId,actorId,selection,remove,saved}:{subjectId:string;actorId:string;selection:PackSelection[];remove:(id:string)=>void;saved:()=>void}) {
  const [subject,setSubject]=useState('Your policy documents'),[body,setBody]=useState('Please find the selected documents attached.'),[recipients,setRecipients]=useState<string[]>([]);
  const [request,setRequest]=useState<CommunicationConfirmation>(),[error,setError]=useState(''),[preview,setPreview]=useState<string>();
  const relationship=selection[0]?.relationshipId;
  if(!relationship)return null;
  return <section className="quote-rail-body communication-form" aria-label="Document pack"><h3>Send document pack</h3>
    <form onSubmit={event=>{event.preventDefault();try{setError('');setRequest({command:communicationCommand('send-pack',subjectId,{subject,body,recipientContactIds:recipients,documentVersionIds:selection.map(x=>x.version.id)}),label:'Send document pack',description:`Queue ${selection.length} selected file versions for ${recipients.length} recipients. The exact files listed here will be retained with the delivery.`});}catch(failure){setError((failure as Error).message);}}}>
      <fieldset className="quote-reference-fields" disabled={!!request}><legend>Review selected files and recipients</legend>
        {selection.map(item=><article key={item.version.id} className="quote-driver-card"><p>{item.version.originalName} · version {item.version.number}</p><div className="quote-row-actions"><button type="button" className="button" onClick={()=>setPreview(item.version.id)}>Preview selected file</button><button type="button" className="button" onClick={()=>remove(item.version.id)}>Remove from pack</button></div></article>)}
        <ChoiceList<{id:string;label:string;email:string}> url={`/api/v1/records/${subjectId}/document-delivery-recipients/${relationship}`} label="Pack recipients" selected={recipients} change={setRecipients} maximum={50} describe={item=>`${item.label} · ${item.email}`}/>
        <label>Pack subject<input aria-label="Pack subject" required maxLength={300} value={subject} onChange={event=>setSubject(event.target.value)}/></label>
        <label>Pack message<textarea aria-label="Pack message" required rows={4} maxLength={8000} value={body} onChange={event=>setBody(event.target.value)}/></label>
        <button className="button button-primary" type="submit" disabled={!recipients.length}>Review and send pack</button>
      </fieldset>
    </form>{error&&<p role="alert">{error}</p>}{preview&&<DocumentPreview versionId={preview} close={()=>setPreview(undefined)}/>}
    {request&&<CommunicationCommand request={request} actorId={actorId} close={()=>setRequest(undefined)} saved={()=>{setRequest(undefined);saved();}}/>}
  </section>;
}
