'use client';
import Link from 'next/link';
import {useRouter} from 'next/navigation';
import {useEffect,useState} from 'react';
import {DataTable,EmptyState,Panel,Status} from '../primitives';
import {csrfToken} from '../../lib/auth';
import {decimalMoney,FinanceCommandError,invoiceCandidates,ledgerAll,minorUnits,moneyLabel,
 receiptCommand,receiptDetail,receiptList,recordReceiptCommand,sendFinanceCommand,transactionDetail,
 type FinanceCommand,type FinanceReceipt,type FinanceReceiptPage,type InvoiceCandidate} from '../../lib/finance-api';

async function readReceiptState(agencyId:string,selectedId:string){
 const [page,rows,detail]=await Promise.all([receiptList(agencyId),ledgerAll(agencyId),
  selectedId?receiptDetail(selectedId):Promise.resolve(null)]);
 if(detail&&detail.agencyId.toLowerCase()!==agencyId.toLowerCase())throw new FinanceCommandError(404);
 return {page,candidates:invoiceCandidates(rows),detail};
}

export function FinanceReceipts({agencyId,initialReceiptId}: {agencyId:string;initialReceiptId?:string}) {
 const router=useRouter();
 const [page,setPage]=useState<FinanceReceiptPage|null>(null);
 const [receipt,setReceipt]=useState<FinanceReceipt|null>(null);
 const [candidateRows,setCandidateRows]=useState<InvoiceCandidate[]>([]);
 const [selectedId,setSelectedId]=useState(initialReceiptId??'');
 const [error,setError]=useState('');const [conflict,setConflict]=useState(false);const [busy,setBusy]=useState(false);
 const [pending,setPending]=useState<FinanceCommand|null>(null);
 const [amount,setAmount]=useState('');const [invoiceTransactionId,setInvoiceTransactionId]=useState('');
 const [payerKind,setPayerKind]=useState<'unidentified'|'agency'|'relationship'>('agency');
 const [payerId,setPayerId]=useState(agencyId);const [reason,setReason]=useState('');
 const [reversalId,setReversalId]=useState('');const [reversalReason,setReversalReason]=useState('');
 const [recordAmount,setRecordAmount]=useState('');const [receivedOn,setReceivedOn]=useState('');
 const [bankReference,setBankReference]=useState('');
 async function load(){try{
  const state=await readReceiptState(agencyId,selectedId);
  setPage(state.page);setCandidateRows(state.candidates);setReceipt(state.detail);setError('');
  if(state.detail){setPayerKind(state.detail.payerKind);setPayerId(state.detail.payerId??agencyId);}
 }catch(cause){if(cause instanceof FinanceCommandError&&cause.denied){setPage(null);setReceipt(null);setCandidateRows([]);}
  setError(cause instanceof Error?cause.message:'Receipts could not be loaded.');}}
 useEffect(()=>{let active=true;
  readReceiptState(agencyId,selectedId).then(state=>{if(!active)return;
   setPage(state.page);setCandidateRows(state.candidates);setReceipt(state.detail);setError('');
   if(state.detail){setPayerKind(state.detail.payerKind);setPayerId(state.detail.payerId??agencyId);}
  }).catch(cause=>{if(!active)return;
   if(cause instanceof FinanceCommandError&&cause.denied){setPage(null);setReceipt(null);setCandidateRows([]);}
   setError(cause instanceof Error?cause.message:'Receipts could not be loaded.');});
  return()=>{active=false;};},[agencyId,selectedId]);
 async function run(command:FinanceCommand){setPending(command);setBusy(true);setConflict(false);setError('');try{
  const saved=await sendFinanceCommand(command,await csrfToken());setPending(null);
  if(command.action==='record'&&typeof saved.id==='string'){
   setSelectedId(saved.id);router.push(`/accounting?tab=payments&agencyId=${agencyId}&receiptId=${saved.id}`);}
  await load();if(command.action==='record'){setRecordAmount('');setBankReference('');}
  if(command.action==='allocate')setAmount('');if(command.action==='assign')setReason('');
  if(command.action==='reverse'){setReversalId('');setReversalReason('');}
 }catch(cause){setError(cause instanceof Error?cause.message:'Action could not be confirmed.');
  if(cause instanceof FinanceCommandError){if(cause.conflict){setConflict(true);setPending(null);}
   else if(!cause.uncertain)setPending(null);
   if(cause.denied){setPage(null);setReceipt(null);setCandidateRows([]);}}
 }finally{setBusy(false);}}
 async function allocate(){try{
  const candidate=candidateRows.find(x=>x.transactionId===invoiceTransactionId);
  if(!candidate)throw Error('Choose an outstanding saved invoice.');
  const applied=minorUnits(amount),available=minorUnits(candidate.residual);
  if(applied<=0||applied>available)throw Error('Amount must be within the saved invoice residual.');
  const invoice=await transactionDetail(candidate.transactionId);
  if(invoice.agencyId!==agencyId||invoice.policyId!==candidate.policyId)throw Error('Invoice scope changed. Review the saved account.');
  await run(receiptCommand('allocate',receipt!,{invoiceId:invoice.obligationId,amount:decimalMoney(applied)}));
 }catch(cause){setError(cause instanceof Error?cause.message:'Review the saved invoice.');}}
 const eligible=candidateRows.filter(x=>receipt&&x.debtorKind===receipt.payerKind&&x.debtorId===receipt.payerId);
 const active=receipt?.allocations.filter(x=>!x.reversalOfId&&!receipt.allocations.some(y=>y.reversalOfId?.toLowerCase()===x.id.toLowerCase()))??[];
 return <div className="finance-stack"><Panel title="Record receipt" note={`Cash received for saved agency ${agencyId}`}>
  <form className="finance-form finance-pad" onSubmit={event=>{event.preventDefault();try{void run(recordReceiptCommand(agencyId,
   {amount:decimalMoney(minorUnits(recordAmount)),receivedOn,bankReference,payerKind:'unidentified',payerId:null}));}
   catch(cause){setError(cause instanceof Error?cause.message:'Review receipt details.');}}}>
   <label>Amount received (GBP)<input inputMode="decimal" value={recordAmount} onChange={event=>setRecordAmount(event.target.value)} placeholder="0.00" required/></label>
   <label>Received on<input type="date" value={receivedOn} onChange={event=>setReceivedOn(event.target.value)} required/></label>
   <label>Bank reference<input value={bankReference} onChange={event=>setBankReference(event.target.value)} maxLength={200} required/></label>
   <button className="button button-primary" disabled={busy||!!pending}>Record receipt</button></form>
  <p className="finance-pad client-help">A new receipt starts unidentified. Assign its payer before applying it to an invoice. Recording cash does not mark any invoice paid.</p>
 </Panel>
 <Panel title="Receipts" note="Saved cash, payer and remaining amount">
  {error&&<p role="alert" className="finance-alert">{error}</p>}
  {pending&&<div role="status" className="finance-recovery"><p>Awaiting confirmation. The exact command key, saved selection and typed values are retained.</p>
   <button className="button button-primary" onClick={()=>void run(pending)} disabled={busy}>Retry same action</button></div>}
  {conflict&&<div className="finance-recovery"><p>The saved record changed. Review its current version before making a new command. Your entries remain in the form.</p>
   <button className="button" onClick={()=>{setConflict(false);void load();}}>Review saved version</button></div>}
  {!page?<p className="finance-pad">No receipt list is available under your current access.</p>:page.items.length?<DataTable caption="Saved receipts" columns={['Received','Bank reference','Cash','Residual','Payer','Open']}>
   {page.items.map(x=><tr key={x.id}><td>{x.receivedOn}</td><td>{x.bankReference}</td><td>{moneyLabel(x.amount)}</td>
    <td>{moneyLabel(x.residual)}</td><td>{x.payerKind==='unidentified'?<Status tone="warning">Unidentified</Status>:x.payerKind}</td>
    <td><Link href={`/accounting?tab=payments&agencyId=${agencyId}&receiptId=${x.id}`} onClick={()=>setSelectedId(x.id)}>View</Link></td></tr>)}
  </DataTable>:<EmptyState title="No saved receipts">Record a receipt to create a cash fact for this agency.</EmptyState>}
  {page&&<p className="finance-pad client-help">{page.total} scoped saved receipt{page.total===1?'':'s'} · showing {page.items.length} on this page</p>}
 </Panel>
 {receipt&&<Panel title="Receipt detail" note={`Saved receipt ${receipt.id}`}><div className="finance-pad">
  <div className="finance-measures"><div><span>Cash received</span><strong>{moneyLabel(receipt.amount)}</strong></div>
   <div><span>Allocated</span><strong>{moneyLabel(decimalMoney(minorUnits(receipt.amount)-minorUnits(receipt.residual)))}</strong></div>
   <div><span>Residual</span><strong>{moneyLabel(receipt.residual)}</strong></div>
   <div><span>Payer</span><strong>{receipt.payerKind==='unidentified'?'Unidentified':receipt.payerKind}</strong></div></div>
  <p>Bank reference {receipt.bankReference} · received {receipt.receivedOn} · posted {receipt.postingDate}</p>
  <p className="client-help">Assignment version {receipt.assignmentOrdinal} · {receipt.assignmentId}</p>
  <form className="finance-form" onSubmit={event=>{event.preventDefault();try{void run(receiptCommand('assign',receipt,
   {payerKind,payerId:payerKind==='unidentified'?null:payerKind==='agency'?agencyId:payerId,reason}));}
   catch(cause){setError(cause instanceof Error?cause.message:'Review payer assignment.');}}}>
   <div className="finance-field"><label htmlFor="receipt-payer-type">Payer type</label><select id="receipt-payer-type" value={payerKind} onChange={event=>setPayerKind(event.target.value as typeof payerKind)}>
    <option value="unidentified">Unidentified</option><option value="agency">Agency</option><option value="relationship">Client relationship</option></select></div>
   {payerKind==='relationship'&&<label>Relationship ID<input value={payerId} onChange={event=>setPayerId(event.target.value)} required/></label>}
   <label>Reason for payer assignment<input value={reason} onChange={event=>setReason(event.target.value)} minLength={10} maxLength={1000} required/></label>
   <button className="button" disabled={busy||!!pending}>Assign payer</button></form>
  <h3>Apply to invoice</h3><form className="finance-form" onSubmit={event=>{event.preventDefault();void allocate();}}>
   <div className="finance-field"><label htmlFor="receipt-invoice-candidate">Invoice candidate</label><select id="receipt-invoice-candidate" value={invoiceTransactionId} onChange={event=>setInvoiceTransactionId(event.target.value)} required>
    <option value="">Choose outstanding invoice</option>{eligible.map(x=><option key={x.transactionId} value={x.transactionId}>
     {x.transactionId} · {x.paymentState} · residual {moneyLabel(x.residual)} · due {x.dueDate??'not set'}
    </option>)}</select></div>
   <label>Amount to apply (GBP)<input inputMode="decimal" value={amount} onChange={event=>setAmount(event.target.value)} placeholder="0.00" required/></label>
   <button className="button button-primary" disabled={busy||!!pending||receipt.payerKind==='unidentified'||minorUnits(receipt.residual)<=0}>Allocate amount</button></form>
  {receipt.payerKind==='unidentified'&&<p role="status">Assign a payer before allocation.</p>}
  {eligible.length===0&&receipt.payerKind!=='unidentified'&&<p>No outstanding invoices match this payer in the saved ledger.</p>}
  </div><DataTable caption="Saved receipt allocations" columns={['Invoice obligation','Applied','Date','State','Action']}>
   {receipt.allocations.filter(x=>!x.reversalOfId).map(x=><tr key={x.id}><td>{x.invoiceId}</td><td>{moneyLabel(x.amount)}</td>
    <td>{x.postingDate}</td><td>{active.some(y=>y.id===x.id)?'Applied':'Reversed'}</td><td>{active.some(y=>y.id===x.id)&&<button className="button" disabled={busy||!!pending}
     onClick={()=>setReversalId(x.id)}>Reverse allocation</button>}</td></tr>)}
  </DataTable>{reversalId&&<form className="finance-form finance-pad" onSubmit={event=>{event.preventDefault();try{void run(receiptCommand('reverse',receipt,
   {allocationId:reversalId,reason:reversalReason}));}catch(cause){setError(cause instanceof Error?cause.message:'Enter a reversal reason.');}}}>
   <label>Reason for reversal<input value={reversalReason} onChange={event=>setReversalReason(event.target.value)} minLength={10} maxLength={1000} required/></label>
   <button className="button button-primary" disabled={busy||!!pending}>Confirm reversal</button>
   <button className="button" type="button" onClick={()=>{setReversalId('');setReversalReason('');}}>Cancel</button></form>}
  </Panel>}
 </div>;
}
