'use client';
import { useState } from 'react';
import type { OpsDelivery, OpsDeliveryAttempts } from '../../../../contracts/generated/operations';
import { communicationCommand } from '../../lib/communications-api';
import { CommunicationCommand, type CommunicationConfirmation } from './communication-command';
import { LoadFeedback, Paging, useCommunicationResource, type CommunicationPage } from './communication-shared';
import { documentDate } from './document-shared';
import { DocumentPreview } from './document-preview';

export function DeliveryHistory({ id, kind, actorId }: { id: string; kind: 'message' | 'document'; actorId: string }) {
  const [pages,setPages]=useState(['']),[selected,setSelected]=useState('');
  const cursor=pages.at(-1)!;
  const base=kind==='message'?`/api/v1/messages/${id}/deliveries`:`/api/v1/records/${id}/document-deliveries`;
  const read=useCommunicationResource<CommunicationPage<OpsDelivery>>(`${base}?pageSize=10${cursor?`&cursor=${encodeURIComponent(cursor)}`:''}`);
  return <section aria-label="Delivery history"><div className="quote-row-actions"><h4>Delivery history</h4><button className="button" onClick={read.refresh}>Refresh deliveries</button></div>
    {read.data?<>{!read.data.items.length&&<p>No deliveries recorded.</p>}{read.data.items.map(row=><article className="quote-driver-card" key={row.id}>
      <p><strong>{row.state}</strong> · {documentDate(row.createdAt)}{row.resendOfId?' · Requested resend':''}</p>
      <p>{row.recipientLabels.join('; ')}</p><button className="button" onClick={()=>setSelected(row.id)}>View delivery and attempts</button>
    </article>)}<Paging total={read.data.totalCount} previous={pages.length>1?()=>setPages(x=>x.slice(0,-1)):undefined} next={read.data.nextCursor?()=>setPages(x=>[...x,read.data!.nextCursor!]):undefined}/></>:<LoadFeedback error={read.error} retry={read.refresh}/>}
    {selected&&<DeliveryDetails key={selected} id={selected} kind={kind} actorId={actorId} changed={read.refresh}/>}
  </section>;
}
function DeliveryDetails({id,kind,actorId,changed}:{id:string;kind:'message'|'document';actorId:string;changed:()=>void}) {
  const [reason,setReason]=useState(''),[error,setError]=useState(''),[preview,setPreview]=useState<string>(),[request,setRequest]=useState<CommunicationConfirmation>();
  const base=`/api/v1/${kind}-deliveries/${id}`;
  const read=useCommunicationResource<OpsDelivery>(base),attempts=useCommunicationResource<OpsDeliveryAttempts>(`${base}/attempts`);
  if(!read.data)return <LoadFeedback error={read.error} retry={read.refresh}/>;
  const row=read.data;
  function recover(resend:boolean) {
    try {setError('');setRequest({command:communicationCommand(`${resend?'resend':'retry'}-${kind}`,id,{reason},row.etag),label:resend?'Resend original delivery':'Retry delivery',description:resend?'Create a new delivery using the original recipients, message and exact file versions.':'Continue the same delivery. An already completed provider effect will not be repeated.'});}
    catch(failure){setError((failure as Error).message);}
  }
  return <section className="quote-driver-card communication-form" aria-label="Delivery details"><h4>{row.subject}</h4>
    <p>Status: {row.state}{row.completedAt?` · ${documentDate(row.completedAt)}`:''}</p>
    {row.state==='superseded'&&row.providerOutcome==='delivered'&&<p>The demo provider completed delivery before access changed. This receipt preserves that outcome; local application was stopped.</p>}
    <p>{row.recipientLabels.join('; ')}</p><p style={{whiteSpace:'pre-wrap',overflowWrap:'anywhere'}}>{row.body}</p>
    {row.documentVersionIds.map((version,index)=><button key={version} className="button" onClick={()=>setPreview(version)}>View original file {index+1}</button>)}
    {attempts.data?<ol>{attempts.data.items.map(attempt=><li key={attempt.id}>Attempt {attempt.number}: {attempt.outcome} · {documentDate(attempt.startedAt)}</li>)}</ol>:<LoadFeedback error={attempts.error} retry={attempts.refresh}/>}
    <button className="button" onClick={()=>{read.refresh();attempts.refresh();changed();}}>Refresh delivery details</button>
    {row.state!=='queued'&&<fieldset disabled={!!request}><legend>Delivery recovery</legend><label>Reason<input maxLength={1000} value={reason} onChange={event=>setReason(event.target.value)}/></label>
      {row.retryAllowed&&<button className="button" onClick={()=>recover(false)}>Retry delivery</button>}<button className="button" onClick={()=>recover(true)}>Resend original delivery</button>
      <p>A resend creates a new delivery and preserves this receipt. Current recipient and file access will be checked again.</p></fieldset>}
    {error&&<p role="alert">{error}</p>}{preview&&<DocumentPreview versionId={preview} close={()=>setPreview(undefined)}/>}
    {request&&<CommunicationCommand request={request} actorId={actorId} close={()=>setRequest(undefined)} saved={()=>{setRequest(undefined);setReason('');read.refresh();attempts.refresh();changed();}}/>}
  </section>;
}
