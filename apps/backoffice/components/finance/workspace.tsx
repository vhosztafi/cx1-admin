'use client';
import Link from 'next/link';
import {useRouter} from 'next/navigation';
import {useState} from 'react';
import {EmptyState,Panel,SectionTabs} from '../primitives';
import {routeForFinanceTab} from '../../lib/finance-workspace-api';
import {FinanceReceipts} from './receipts';
import {FinanceAccounts} from './accounts';
import {FinanceOverview} from './overview';
import {FinanceTransactions} from './transactions';
import {FinanceReconciliation} from './reconciliation';
import {FinanceBordereaux} from './bordereaux';
import {FinanceRefunds} from './refunds';

const validId=(value:string)=>/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
const tabs=[['overview','Overview'],['transactions','Transactions'],['accounts','Broker accounts'],
 ['payments','Payments'],['reconciliation','Reconciliation'],['bordereaux','Bordereaux'],['refunds','Refunds']];
export function FinanceWorkspace({agencyId,tab,receiptId,statementId,reconciliationId,batchId,refundId,
 periodId,transactionId}: {agencyId?:string;tab:string;receiptId?:string;statementId?:string;
 reconciliationId?:string;batchId?:string;refundId?:string;periodId?:string;transactionId?:string}){
 const router=useRouter();const [agencyInput,setAgencyInput]=useState(agencyId??'');const [error,setError]=useState('');
 const selected=tabs.some(([key])=>key===tab)?tab:'overview';
 const items=tabs.map(([key,label])=>({key,label,href:routeForFinanceTab(key,{agencyId})}));
 return <><div className="page-heading"><div><h1>Accounting</h1>
  <p>Posted evidence, saved balances and exact reporting versions</p></div>
  <Link className="button" href="/agents">Agency directory</Link></div>
  <SectionTabs active={selected} items={items} label="Accounting sections"/>
  <Panel title="Selected agency" note="Current finance permissions are checked on every read and command"><form className="finance-agency-picker" onSubmit={event=>{
   event.preventDefault();const id=agencyInput.trim();if(!validId(id)){setError('Enter a saved agency ID.');return;}
   setError('');router.push(routeForFinanceTab(selected,{agencyId:id}));}}>
   <label>Agency ID<input value={agencyInput} onChange={event=>setAgencyInput(event.target.value)} placeholder="Agency record ID"/></label>
   <button className="button" type="submit">Open agency account</button></form>
   {error&&<p role="alert" className="finance-alert">{error}</p>}
   <p className="finance-pad client-help">Open from an agency record or enter its saved ID. Every selected record remains in the URL.</p>
  </Panel>
  {selected==='overview'?<FinanceOverview agencyId={agencyId} initialPeriodId={periodId}/>
   :selected==='bordereaux'?<FinanceBordereaux agencyId={agencyId} initialBatchId={batchId} periodId={periodId}/>
   :!agencyId?<Panel title="Choose an agency"><EmptyState title="No account selected">Select a saved agency to inspect this accounting section.</EmptyState></Panel>
   :selected==='transactions'?<FinanceTransactions agencyId={agencyId} initialTransactionId={transactionId}/>
   :selected==='accounts'?<FinanceAccounts key={`${agencyId}:${statementId??''}`} agencyId={agencyId} initialStatementId={statementId}/>
   :selected==='payments'?<FinanceReceipts key={`${agencyId}:${receiptId??''}`} agencyId={agencyId} initialReceiptId={receiptId}/>
   :selected==='reconciliation'?<FinanceReconciliation agencyId={agencyId} initialReconciliationId={reconciliationId}/>
   :<FinanceRefunds agencyId={agencyId} initialRefundId={refundId}/>}
 </>;
}
