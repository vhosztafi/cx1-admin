'use client';
import Link from 'next/link';
import {useRouter} from 'next/navigation';
import {useState} from 'react';
import {EmptyState,Panel,SectionTabs} from '../primitives';
import {FinanceReceipts} from './receipts';
import {FinanceAccounts} from './accounts';

const validId=(value:string)=>/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
export function FinanceWorkspace({agencyId,tab,receiptId,statementId}: {agencyId?:string;tab:string;receiptId?:string;statementId?:string}){
 const router=useRouter();const [agencyInput,setAgencyInput]=useState(agencyId??'');const [error,setError]=useState('');
 const selected=tab==='accounts'?'accounts':'payments';
 const tabs=[{key:'payments',label:'Payments',href:`/accounting?tab=payments${agencyId?`&agencyId=${agencyId}`:''}`},
  {key:'accounts',label:'Broker accounts',href:`/accounting?tab=accounts${agencyId?`&agencyId=${agencyId}`:''}`}];
 return <><div className="page-heading"><div><h1>Accounting</h1><p>Saved cash, invoice applications and agency balances</p></div>
  <Link className="button" href="/agents">Agency directory</Link></div>
  <SectionTabs active={selected} items={tabs} label="Accounting sections"/>
  <Panel title="Selected agency" note="Current finance permissions are checked on every read and command"><form className="finance-agency-picker" onSubmit={event=>{
   event.preventDefault();const id=agencyInput.trim();if(!validId(id)){setError('Enter a saved agency ID.');return;}
   setError('');router.push(`/accounting?tab=${selected}&agencyId=${id}`);}}>
   <label>Agency ID<input value={agencyInput} onChange={event=>setAgencyInput(event.target.value)} placeholder="Agency record ID"/></label>
   <button className="button" type="submit">Open agency account</button></form>
   {error&&<p role="alert" className="finance-alert">{error}</p>}
   <p className="finance-pad client-help">Open this page from an agency record or enter its saved ID. The selected agency stays in the URL; receipt and statement views use exact saved IDs.</p>
  </Panel>
  {!agencyId?<Panel title="Choose an agency"><EmptyState title="No account selected">Select a saved agency to inspect receipts, allocations and statements.</EmptyState></Panel>
   :selected==='payments'?<FinanceReceipts key={`${agencyId}:${receiptId??''}`} agencyId={agencyId} initialReceiptId={receiptId}/>
   :<FinanceAccounts key={`${agencyId}:${statementId??''}`} agencyId={agencyId} initialStatementId={statementId}/>}
 </>;
}
