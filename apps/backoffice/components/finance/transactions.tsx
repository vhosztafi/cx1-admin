'use client';
import Link from 'next/link';
import {useEffect,useState} from 'react';
import {DataTable,EmptyState,Panel,Status} from '../primitives';
import {FinanceCommandError,ledgerAll,moneyLabel,type FinanceLedgerRow} from '../../lib/finance-api';
import type {FinanceTransactionDetail} from '../../../../contracts/generated/finance.ts';
import {financeRequest,routeForFinanceTab} from '../../lib/finance-workspace-api';

export function FinanceTransactions({agencyId,initialTransactionId}: {agencyId:string;initialTransactionId?:string}){
 const [rows,setRows]=useState<FinanceLedgerRow[]>([]),[selected,setSelected]=useState<FinanceTransactionDetail|null>(null);
 const [transactionId,setTransactionId]=useState(initialTransactionId??''),[from,setFrom]=useState(''),[to,setTo]=useState('');
 const [kind,setKind]=useState('all'),[status,setStatus]=useState('all'),[error,setError]=useState('');
 useEffect(()=>{let active=true;ledgerAll(agencyId).then(value=>{if(active){setRows(value);setError('');}})
  .catch(cause=>{if(!active)return;setRows([]);setSelected(null);setError(cause instanceof Error?cause.message:'Transactions could not be loaded.');});
  return()=>{active=false;};},[agencyId]);
 useEffect(()=>{if(!transactionId)return;let active=true;
  financeRequest<FinanceTransactionDetail>(`/api/v1/finance/transactions/${transactionId}`).then(value=>{
   if(!active)return;if(value.agencyId.toLowerCase()!==agencyId.toLowerCase())throw new FinanceCommandError(404);
   setSelected(value);setError('');}).catch(cause=>{if(active){setSelected(null);setError(cause instanceof Error?cause.message:'Transaction unavailable.');}});
  return()=>{active=false;};},[agencyId,transactionId]);
 const filtered=rows.filter(row=>(!from||row.postingDate>=from)&&(!to||row.postingDate<to)&&
  (kind==='all'||row.sourceKind===kind)&&(status==='all'||row.status===status));
 return <div className="finance-stack"><Panel title="Transactions" note={`Saved posted movements for agency ${agencyId}`}>
  {error&&<p role="alert" className="finance-alert">{error}</p>}
  <div className="finance-form finance-pad"><label>Posted from<input type="date" value={from} onChange={event=>setFrom(event.target.value)}/></label>
   <label>Posted to (exclusive)<input type="date" value={to} onChange={event=>setTo(event.target.value)}/></label>
   <label>Movement type<select value={kind} onChange={event=>setKind(event.target.value)}><option value="all">All posted types</option>
    {[...new Set(rows.map(row=>row.sourceKind))].map(value=><option key={value} value={value}>{value}</option>)}</select></label>
   <label>Posting state<select value={status} onChange={event=>setStatus(event.target.value)}><option value="all">All saved states</option>
    {[...new Set(rows.map(row=>row.status))].map(value=><option key={value} value={value}>{value}</option>)}</select></label></div>
  {filtered.length?<DataTable caption="Scoped posted transactions" columns={['Posted','Effective','Source','Policy','Debtor','Provider','Due','State','Open']}>
   {filtered.map(row=><tr key={row.sourceKey}><td>{row.postingDate}</td><td>{new Date(row.effectiveAt).toLocaleDateString('en-GB')}</td>
    <td>{row.sourceKind}</td><td>{row.policyId?<Link href={`/policies/${row.policyId}`}>{row.policyId}</Link>:'—'}</td>
    <td>{moneyLabel(row.debtorDelta)}</td><td>{moneyLabel(row.providerDelta)}</td><td>{row.dueDate??'—'}</td>
    <td><Status tone={row.status==='credit'?'warning':'muted'}>{row.status==='outstanding'?'Posted receivable':row.status}</Status></td>
    <td>{row.transactionId?<Link href={routeForFinanceTab('transactions',{agencyId,transactionId:row.transactionId})}
      onClick={()=>setTransactionId(row.transactionId!)}>View</Link>:row.sourceKey}</td></tr>)}</DataTable>
   :<EmptyState title="No posted movements">This filter has no saved entries. Try another posting window or type.</EmptyState>}
  <p className="finance-pad client-help">Posted date determines the accounting period. Effective date describes coverage. A posted receivable is not marked paid without saved cash application evidence.</p>
 </Panel>
 {selected&&<Panel title="Selected transaction" note={`Saved journal ${selected.journalId}`}><div className="finance-pad">
  <p>Policy <Link href={`/policies/${selected.policyId}`}>{selected.policyId}</Link> · agency {selected.agencyId}</p>
  <p>Effective {new Date(selected.effectiveAt).toLocaleString('en-GB')} · posted {selected.postingDate} · captured {new Date(selected.postedAt).toLocaleString('en-GB')}</p>
  <div className="finance-measures"><div><span>Invoice due</span><strong>{moneyLabel(selected.invoiceDue)}</strong></div>
   <div><span>Broker payable</span><strong>{moneyLabel(selected.brokerPayable)}</strong></div>
   <div><span>Debtor</span><strong>{selected.debtorKind}</strong></div>
   <div><span>Settlement</span><strong>{selected.settlement}</strong></div></div>
  <p>Agency terms version {selected.agencyTermsVersionId} · accounting period {selected.accountingPeriodId??'legacy London posting date'}</p>
  <Link className="button" href={routeForFinanceTab('overview',{agencyId,periodId:selected.accountingPeriodId??undefined})}>Review period</Link>
 </div></Panel>}
 </div>;
}
