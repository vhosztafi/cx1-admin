'use client';
import {useState} from 'react';
import Link from 'next/link';
import type {OpsCancellationConsequences} from '../../../../contracts/generated/operations';
import type {Actor} from '../../lib/auth';
import {useQuoteResource,LoadFeedback} from '../quotes/shared';
import {Panel} from '../primitives';
import {DocumentPreview} from './document-preview';
import {DeliveryDetails} from './delivery-history';
import {MidSubmissions} from './mid-submissions';
const date=(value:string)=>new Date(value).toLocaleString('en-GB',{timeZone:'Europe/London'});
const label=(value:string)=>({'notice':'Cancellation notice','certificate-withdrawal':'Certificate withdrawal','task-close':'Renewal task closure','mid-removal':'MID removal','legacy-demo-delivered':'Legacy demo delivery receipt','legacy-demo-no-recipient':'Legacy notice without recipients','delivery-delivered':'Delivered by demo adapter','delivery-queued':'Delivery queued','delivery-failed':'Delivery failed','delivery-superseded':'Delivery access or source changed','eligible-tasks-closed':'Eligible renewal tasks closed','certificate-withdrawn':'Certificate withdrawn','scheduled':'Scheduled for effective date','no-recipient':'No eligible recipients'}[value]??value.replaceAll('-',' '));
export function CancellationConsequences({versionId}:{versionId:string}) {
 const read=useQuoteResource<OpsCancellationConsequences>(`/api/v1/versions/${versionId}/cancellation-consequences`);
 const actor=useQuoteResource<Actor>('/api/v1/account');
 const [preview,setPreview]=useState<string>(),[delivery,setDelivery]=useState<string>(),[mid,setMid]=useState(false);
 return <Panel title="Cancellation operations"><div className="quote-rail-body">
  <p>Withdrawal and renewal task closure take effect at the recorded cancellation time. A posted credit is not a cash refund.</p>
  <button className="button" onClick={read.refresh}>Refresh cancellation operations</button>
  {!read.data?<LoadFeedback error={read.error} retry={read.refresh}/>:!read.data.items.length?<p>No cancellation operations belong to this version.</p>:read.data.items.map(row=><article className="quote-driver-card" key={row.id}>
   <h3>{label(row.kind)}</h3><p>{row.kind==='task-close'&&row.state==='eligible-tasks-closed'&&!row.closedTaskIds.length?'No eligible renewal tasks to close':label(row.state)} · Effective {date(row.effectiveAt)} · London</p>
   {row.appliedAt&&<p>Recorded {date(row.appliedAt)}</p>}{row.errorCode&&<p role="status">{label(row.errorCode)}</p>}
   {row.legacyReceiptId&&<p>Historical receipt retained. It does not prove delivery by the current adapter and has not been resent.</p>}
   {row.kind==='certificate-withdrawal'&&<p>Historical certificate files remain available with their withdrawal date.</p>}
   {row.documentVersionId&&<button className="button" onClick={()=>setPreview(row.documentVersionId!)}>View cancellation notice</button>}
   {row.deliveryId&&<button className="button" onClick={()=>setDelivery(row.deliveryId!)}>View notice delivery</button>}
   {row.midSubmissionId&&<button className="button" onClick={()=>setMid(x=>!x)}>View MID removal</button>}
   {row.exceptionTaskId&&<p><Link href={`/tasks/${row.exceptionTaskId}`}>Open cancellation exception task</Link></p>}
   {row.closedTaskIds.map(id=><p key={id}><Link href={`/tasks/${id}`}>View closed renewal task</Link></p>)}
  </article>)}
  {preview&&<DocumentPreview versionId={preview} close={()=>setPreview(undefined)}/>}
  {delivery&&actor.data&&<DeliveryDetails id={delivery} kind="document" actorId={actor.data.id} changed={read.refresh} allowResend={false}/>}
  {delivery&&!actor.data&&<LoadFeedback error={actor.error} retry={actor.refresh}/>}
  {mid&&<MidSubmissions versionId={versionId}/>}
 </div></Panel>;
}
