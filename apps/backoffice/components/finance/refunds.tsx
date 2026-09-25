'use client';
import Link from 'next/link';
import {useRouter} from 'next/navigation';
import {useEffect,useRef,useState} from 'react';
import {EmptyState,Panel,Status} from '../primitives';
import {FinanceCommandError} from '../../lib/finance-api';
import {financeCommand,financeRequest,financeSend,routeForFinanceTab,type WorkspaceCommand} from '../../lib/finance-workspace-api';
import type {FinanceRefund} from '../../../../contracts/generated/finance-refunds.ts';
import type {FinanceRefundPage} from '../../../../contracts/generated/finance-refunds.ts';
import type {FinancePayment} from '../../../../contracts/generated/finance-payments.ts';

const validId=(value:string)=>/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
type Pending={command:WorkspaceCommand;label:string};
export function FinanceRefunds({agencyId,initialRefundId}:{agencyId:string;initialRefundId?:string}){
 const router=useRouter();
 const [refundId,setRefundId]=useState(initialRefundId??''),[refund,setRefund]=useState<FinanceRefund|null>(null);
 const [paymentId,setPaymentId]=useState(''),[payment,setPayment]=useState<FinancePayment|null>(null);
 const [creditId,setCreditId]=useState(''),[allocationId,setAllocationId]=useState(''),[sourceAmount,setSourceAmount]=useState('');
 const [amount,setAmount]=useState(''),[reason,setReason]=useState(''),[decision,setDecision]=useState<'approve'|'reject'>('approve');
 const [pending,setPending]=useState<Pending|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState(''),[notice,setNotice]=useState('');
 const [queue,setQueue]=useState<FinanceRefundPage|null>(null),[queueScope,setQueueScope]=useState('');
 const queueGeneration=useRef(0);
 const detailGeneration=useRef(0);
 async function loadQueue(page=1){const generation=++queueGeneration.current;setQueue(null);setQueueScope('');try{
  const saved=await financeRequest<FinanceRefundPage>(`/api/v1/finance/agencies/${agencyId}/refunds?page=${page}&pageSize=50`);
  if(generation!==queueGeneration.current)return;setQueue(saved);setQueueScope(agencyId);setError('');
 }catch(cause){if(generation!==queueGeneration.current)return;setQueue(null);setQueueScope('');setError(cause instanceof Error?cause.message:'Saved refunds could not be loaded.');}}
 async function load(id=refundId){const generation=++detailGeneration.current;try{if(!validId(id))throw Error('Enter a saved refund ID.');
  const saved=await financeRequest<FinanceRefund>(`/api/v1/finance/refunds/${id}`);
  if(generation!==detailGeneration.current)return;
  if(saved.agencyId!==agencyId)throw Error('Refund is outside the selected agency.');
  setRefund(saved);setRefundId(id);setError('');if(validId(paymentId))await loadPayment(paymentId);
 }catch(cause){if(generation!==detailGeneration.current)return;setRefund(null);setPayment(null);setError(cause instanceof Error?cause.message:'Refund could not be loaded.');}}
 async function loadPayment(id:string){const generation=detailGeneration.current;const saved=await financeRequest<FinancePayment>(`/api/v1/finance/payments/${id}`);
  if(generation!==detailGeneration.current)return;
  if(saved.agencyId!==agencyId||saved.refundRequestId!==refundId)throw Error('Payment is outside this refund.');
  setPayment(saved);setPaymentId(id);}
 useEffect(()=>{const generation=++detailGeneration.current;void Promise.resolve().then(()=>{
  if(generation!==detailGeneration.current)return;
  setRefund(null);setPayment(null);setPaymentId('');setRefundId(initialRefundId??'');
  if(initialRefundId)void load(initialRefundId); // Read after render.
 });
 // eslint-disable-next-line react-hooks/exhaustive-deps
 },[agencyId,initialRefundId]);
 useEffect(()=>{void Promise.resolve().then(()=>loadQueue());
 // eslint-disable-next-line react-hooks/exhaustive-deps
 },[agencyId]);
 async function send(next:Pending){setPending(next);setBusy(true);setError('');setNotice('');try{
  const saved=await financeSend(next.command);const nextRefund=String(saved.refundId||saved.id||refundId);
  if(next.label==='Payment queue'&&validId(String(saved.paymentId)))setPaymentId(String(saved.paymentId));
  if(validId(nextRefund)){await load(nextRefund);router.push(routeForFinanceTab('refunds',{agencyId,refundId:nextRefund}));}
  await loadQueue(queue?.page??1);
  setNotice(`${next.label} saved. Refresh the record to inspect its current state.`);setPending(null);
 }catch(cause){setError(cause instanceof Error?cause.message:'Saved outcome is uncertain.');
  if(!(cause instanceof FinanceCommandError&&cause.uncertain))setPending(null);}finally{setBusy(false);}}
 function request(){if(!validId(creditId)||!validId(allocationId)){setError('Use saved credit and receipt allocation IDs.');return;}
  void send({label:'Refund request',command:financeCommand(`/api/v1/finance/credits/${creditId}/refunds`,
   {amount,sources:[{allocationId,amount:sourceAmount}],reason})});}
 function decide(){if(!refund)return;void send({label:'Approval decision',command:financeCommand(
  `/api/v1/finance/refunds/${refund.id}/decisions`,{kind:decision,reason},{etag:refund.etag})});}
 return <div className="finance-stack"><Panel title="Refunds" note="A negative posted credit and collected same-payee allocation are required">
  {error&&<p role="alert" className="finance-alert">{error}</p>}{notice&&<p role="status" className="finance-pad">{notice}</p>}
  {pending&&<div role="status" className="finance-recovery">Awaiting confirmation. The same command key and version are retained.
   <button className="button" disabled={busy} onClick={()=>void send(pending)}>Retry same action</button>
   <button className="button" disabled={busy} onClick={()=>{setPending(null);void load();}}>Review saved refund</button></div>}
  <div className="finance-pad"><h3>Saved refunds</h3>
   {queueScope===agencyId&&queue?<><p>{queue.total} saved refund{queue.total===1?'':'s'} for this agency</p>
    {queue.items.length?<ul>{queue.items.map(item=><li key={item.id}>
     <Link href={routeForFinanceTab('refunds',{agencyId,refundId:item.id})}>{item.id}</Link>
     {' · '}{item.state} · £{item.amount} · {item.approvalCount}/{item.requiredApprovals} approvals
    </li>)}</ul>:<p>No saved refunds for this agency.</p>}
    <div className="operations-actions"><button className="button" disabled={queue.page<=1} onClick={()=>void loadQueue(queue.page-1)}>Previous refunds</button>
     <button className="button" disabled={queue.page*queue.pageSize>=queue.total} onClick={()=>void loadQueue(queue.page+1)}>Next refunds</button></div></>
   :<p>Loading saved refunds.</p>}</div>
  <div className="finance-pad finance-form"><label>Refund ID<input value={refundId} onChange={e=>setRefundId(e.target.value.trim())}/></label>
   <button className="button" disabled={!validId(refundId)} onClick={()=>void load().then(()=>router.push(routeForFinanceTab('refunds',{agencyId,refundId})))}>Open saved refund</button></div>
  {!refund?<div className="finance-pad"><h3>Request against saved credit</h3><p>Use the credit obligation from a posted negative insurance movement and a collected allocation for the same payer.</p>
   <div className="finance-form"><label>Credit obligation ID<input value={creditId} onChange={e=>setCreditId(e.target.value.trim())}/></label>
    <label>Collected allocation ID<input value={allocationId} onChange={e=>setAllocationId(e.target.value.trim())}/></label>
    <label>Source amount GBP<input value={sourceAmount} onChange={e=>setSourceAmount(e.target.value)}/></label>
    <label>Refund amount GBP<input value={amount} onChange={e=>setAmount(e.target.value)}/></label>
    <label>Reason<input value={reason} onChange={e=>setReason(e.target.value)}/></label></div>
   <button className="button button-primary" disabled={busy} onClick={request}>Request refund</button></div>:<>
   <div className="finance-pad"><h3>Refund {refund.id}</h3><p><Status>{refund.state}</Status> · £{refund.amount} GBP · payer {refund.debtorKind} {refund.debtorId}</p>
    <p>Credit {refund.creditObligationId} · policy <Link href={`/policies/${refund.policyId}`}>{refund.policyId}</Link></p>
    <p>Rule v{refund.ruleVersion} · threshold £{refund.approvalThreshold} · {refund.requiredApprovals} independent approval{refund.requiredApprovals===1?'':'s'} required</p>
    <p>Requested by {refund.requestedBy} at {refund.requestedAt} · {refund.reason}</p>
    <h4>Reserved collected sources</h4>{refund.sources.map(source=><p key={source.allocationId}>Allocation {source.allocationId} · receipt {source.receiptId} · £{source.amount}</p>)}
    <h4>Approval audit</h4>{refund.decisions.length?refund.decisions.map(item=><p key={item.id}>{item.kind} · {item.actorId} · {item.decidedAt} · {item.reason}</p>):<p>No decisions yet.</p>}
    {refund.state==='pending'&&<div className="finance-form"><label>Decision<select value={decision} onChange={e=>setDecision(e.target.value as 'approve'|'reject')}><option value="approve">Approve</option><option value="reject">Reject</option></select></label>
     <label>Decision reason<input value={reason} onChange={e=>setReason(e.target.value)}/></label>
     <button className="button button-primary" disabled={busy} onClick={decide}>Save independent decision</button></div>}
    {refund.state==='approved'&&<div className="finance-form"><label>Payment review reason<input value={reason} onChange={e=>setReason(e.target.value)}/></label>
     <button className="button button-primary" disabled={busy} onClick={()=>void send({label:'Payment queue',command:financeCommand(`/api/v1/finance/refunds/${refund.id}/payments`,{reason},{etag:refund.etag})})}>Queue approved payment</button></div>}
   </div>
   <div className="finance-pad"><h3>Payment result</h3><label>Payment ID<input value={paymentId} onChange={e=>setPaymentId(e.target.value.trim())}/></label>
    <button className="button" disabled={!validId(paymentId)} onClick={()=>void loadPayment(paymentId).catch(cause=>setError(String(cause)))}>Inspect saved payment</button>
    {payment?<><p><Status>{payment.state}</Status> · £{payment.amount} · operation {payment.providerOperationId??'not yet sent'}</p>
     <p>Provider {payment.providerState??'pending'} · applied {payment.appliedAt??'pending'} · linked prior payment {payment.priorPaymentId??'none'}</p>
     {payment.state==='failed'&&payment.providerState===null&&<button className="button" disabled={busy} onClick={()=>void send({label:'Audited payment resume',command:financeCommand(`/api/v1/finance/payments/${payment.id}/resume`,{reason},{etag:payment.etag})})}>Resume same payment work</button>}</>:<EmptyState title="No payment selected">A queued payment remains linked to the saved refund.</EmptyState>}
   </div>
  </>}
 </Panel></div>;
}
