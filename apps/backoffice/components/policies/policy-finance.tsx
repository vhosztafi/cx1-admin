'use client';
import Link from 'next/link';
import {useEffect,useState} from 'react';
import {DataTable,EmptyState,Panel,Status} from '../primitives';
import {financeFetch,moneyLabel} from '../../lib/finance-api';
import type {FinanceLedgerPage} from '../../../../contracts/generated/finance.ts';

export function PolicyFinance({policyId}:{policyId:string}){
 const [page,setPage]=useState<FinanceLedgerPage|null>(null),[error,setError]=useState('');
 useEffect(()=>{let active=true;financeFetch<FinanceLedgerPage>(`/api/v1/policies/${policyId}/finance`)
  .then(result=>{if(active){setPage(result);setError('');}})
  .catch(cause=>{if(active){setPage(null);setError(cause instanceof Error?cause.message:'Policy finance could not be loaded.');}});
  return()=>{active=false;};},[policyId]);
 return <Panel title="Policy finance" note="Posted movements linked to this policy under current policy scope">
  {error&&<p role="alert" className="finance-alert">{error}</p>}
  {!page?<EmptyState title="Policy finance unavailable">The current policy finance read could not be confirmed.</EmptyState>
   :page.items.length===0?<EmptyState title="No posted finance movements">No saved movement is linked to this policy.</EmptyState>
   :<><p className="finance-pad">{page.total} saved movement{page.total===1?'':'s'}. These are posted amounts; payment state is held on the broker account.</p>
    <DataTable caption="Policy finance movements" columns={['Posting date','Source','Debtor movement','Provider movement','Due','State']}>
     {page.items.map(row=><tr key={row.sourceKey}><td>{row.postingDate}</td><th scope="row">{row.movementKind}</th>
      <td>{moneyLabel(row.debtorDelta)}</td><td>{moneyLabel(row.providerDelta)}</td><td>{row.dueDate??'—'}</td><td><Status>{row.status}</Status></td></tr>)}</DataTable>
    <Link className="button" href={`/accounting?tab=accounts&agencyId=${page.items[0].agencyId}`}>Open broker account</Link></>}
 </Panel>;
}
