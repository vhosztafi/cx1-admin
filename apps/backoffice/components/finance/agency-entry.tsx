'use client';
import Link from 'next/link';
import {useEffect,useState} from 'react';
import {EmptyState,Panel} from '../primitives';
import {accountSummary,moneyLabel,type FinanceAccountSummary} from '../../lib/finance-api';
import {routeForFinanceTab} from '../../lib/finance-workspace-api';

export function FinanceAgencyEntry({agencyId}:{agencyId:string}){
 const [account,setAccount]=useState<FinanceAccountSummary|null>(null),[error,setError]=useState('');
 useEffect(()=>{let active=true;accountSummary(agencyId).then(value=>{if(active){setAccount(value);setError('');}})
  .catch(cause=>{if(active){setAccount(null);setError(cause instanceof Error?cause.message:'Broker account unavailable.');}});
  return()=>{active=false;};},[agencyId]);
 return <Panel title="Agency broker account" note="Finance scope and current role checked against saved account">
  {error&&<p role="alert" className="finance-alert">{error}</p>}
  {account?<div className="finance-pad"><p>Agency {account.agencyId} · {account.movementCount} saved movements</p>
   <p>Agency receivable {moneyLabel(account.agencyReceivable)} · provider payable {moneyLabel(account.providerPayable)}</p>
   <Link className="button button-primary" href={routeForFinanceTab('accounts',{agencyId:account.agencyId})}>Open in Accounting</Link></div>
   :<EmptyState title="Broker account unavailable">Review the current agency scope before opening accounting.</EmptyState>}
 </Panel>;
}
