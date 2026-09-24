'use client';
import Link from 'next/link';
import {useRouter} from 'next/navigation';
import {useEffect,useState} from 'react';
import {DataTable,EmptyState,Panel,Status} from '../primitives';
import {csrfToken} from '../../lib/auth';
import {accountSummary,combinedBalance,FinanceCommandError,ledgerAll,moneyLabel,
  sendFinanceCommand,statementCommand,statementDetail,statementEquation,statementList,
  type FinanceAccountSummary,type FinanceLedgerRow,type FinanceStatementView} from '../../lib/finance-api';
import type {FinanceStatementListItem} from '../../../../contracts/generated/finance-statements.ts';
import type {FinanceCommand} from '../../lib/finance-api';

async function readAccountState(agencyId:string){
 const [summary,page,rows]=await Promise.all([accountSummary(agencyId),statementList(agencyId),ledgerAll(agencyId)]);
 return {summary,page,rows};
}

export function FinanceAccounts({agencyId,initialStatementId}: {agencyId:string;initialStatementId?:string}) {
 const router=useRouter();
 const [account,setAccount]=useState<FinanceAccountSummary|null>(null);
 const [statements,setStatements]=useState<FinanceStatementListItem[]>([]);
 const [movements,setMovements]=useState<FinanceLedgerRow[]>([]);
 const [selected,setSelected]=useState<FinanceStatementView|null>(null);
 const [statementId,setStatementId]=useState(initialStatementId??'');
 const [from,setFrom]=useState('');const [to,setTo]=useState('');
 const [pending,setPending]=useState<FinanceCommand|null>(null);
 const [error,setError]=useState('');const [busy,setBusy]=useState(false);
 async function load(){
  try{const state=await readAccountState(agencyId);
   setAccount(state.summary);setStatements(state.page.items);setMovements(state.rows);setError('');}
  catch(cause){if(cause instanceof FinanceCommandError&&cause.denied){setAccount(null);setStatements([]);setMovements([]);setSelected(null);}
   setError(cause instanceof Error?cause.message:'Account could not be loaded.');}
 }
 useEffect(()=>{let active=true;
  readAccountState(agencyId).then(state=>{if(!active)return;
   setAccount(state.summary);setStatements(state.page.items);setMovements(state.rows);setError('');
  }).catch(cause=>{if(!active)return;
   if(cause instanceof FinanceCommandError&&cause.denied){setAccount(null);setStatements([]);setMovements([]);setSelected(null);}
   setError(cause instanceof Error?cause.message:'Account could not be loaded.');});
  return()=>{active=false;};},[agencyId]);
 useEffect(()=>{if(!statementId)return;let active=true;
  statementDetail(statementId).then(value=>{if(active){setSelected(value);setError('');}}).catch(cause=>{if(active){setSelected(null);
   setError(cause instanceof Error?cause.message:'Statement could not be loaded.');}});
  return()=>{active=false;};},[statementId]);
 async function send(command:FinanceCommand){setPending(command);setBusy(true);setError('');try{
  const saved=await sendFinanceCommand(command,await csrfToken());setPending(null);await load();
  if(typeof saved.id==='string'){setStatementId(saved.id);router.push(`/accounting?tab=accounts&agencyId=${agencyId}&statementId=${saved.id}`);}
  setFrom('');setTo('');
 }catch(cause){setError(cause instanceof Error?cause.message:'Statement could not be confirmed.');
  if(cause instanceof FinanceCommandError&&!cause.uncertain)setPending(null);
 }finally{setBusy(false);}}
 async function download(){if(!selected)return;setBusy(true);setError('');try{
  // Current authority is checked by the server again before any bytes are returned.
  const response=await fetch(`/api/v1/finance/statements/${selected.id}/download`,{cache:'no-store',signal:AbortSignal.timeout(15000)});
  if(!response.ok)throw new FinanceCommandError(response.status);
  if(response.headers.get('ETag')!==`"${selected.contentHash}"`)throw new FinanceCommandError(0,true);
  const blob=await response.blob(),url=URL.createObjectURL(blob),anchor=document.createElement('a');
  anchor.href=url;anchor.download=`statement-${selected.id}.csv`;anchor.click();URL.revokeObjectURL(url);
 }catch(cause){if(cause instanceof FinanceCommandError&&cause.denied)setSelected(null);
  setError(cause instanceof Error?cause.message:'Download could not be confirmed.');}finally{setBusy(false);}}
 const balance=account?combinedBalance(account.agencyReceivable,account.relationshipReceivable):null;
 return <div className="finance-stack"><Panel title="Broker account" note={`Saved agency ${agencyId}`}>
  {error&&<p role="alert" className="finance-alert">{error}</p>}
  {!account?<p className="finance-pad">No account data is available under your current access.</p>:<>
   <div className="finance-measures"><div><span>Agency receivable</span><strong>{moneyLabel(account.agencyReceivable)}</strong></div>
    <div><span>Relationship receivable</span><strong>{moneyLabel(account.relationshipReceivable)}</strong></div>
    <div><span>Combined balance</span><strong>{balance!.label}</strong><small>{balance!.credit?'Credit balance':'Receivable'}</small></div>
    <div><span>Provider payable</span><strong>{moneyLabel(account.providerPayable)}</strong></div></div>
   <p className="finance-pad client-help">Posting basis · {account.movementCount} saved ledger movements. Receipt cash enters suspense once; applications change the debtor balance without adding cash again.</p>
  </>}
 </Panel>
 <Panel title="Statements" note="Saved versions and exact generated periods"><div className="finance-pad">
  <form className="finance-form" onSubmit={event=>{event.preventDefault();try{void send(statementCommand(agencyId,from,to));}catch(cause){setError(cause instanceof Error?cause.message:'Review statement dates.');}}}>
   <label>From<input type="date" value={from} onChange={event=>setFrom(event.target.value)}/></label>
   <label>To (exclusive)<input type="date" value={to} onChange={event=>setTo(event.target.value)}/></label>
   <button className="button" disabled={busy||!!pending}>Generate statement</button></form>
  {pending&&<div role="status" className="finance-recovery"><p>The statement result is awaiting confirmation. Your dates and command key are retained.</p>
   <button className="button button-primary" onClick={()=>void send(pending)} disabled={busy}>Retry same action</button></div>}
  </div>{statements.length?<DataTable caption="Saved agency statements" columns={['Version','Period','Opening','Closing','Open']}>
   {statements.map(x=><tr key={x.id}><td>v{x.version}</td><td>{x.from} to {x.to}</td><td>{moneyLabel(x.opening)}</td><td>{moneyLabel(x.closing)}</td><td><button className="button" onClick={()=>{setStatementId(x.id);router.push(`/accounting?tab=accounts&agencyId=${agencyId}&statementId=${x.id}`);}}>View statement</button></td></tr>)}
  </DataTable>:<EmptyState title="No saved statements">Generate a statement for a period to inspect its exact saved version.</EmptyState>}
  {selected&&<div className="finance-pad"><h3>Statement v{selected.version} · {selected.from} to {selected.to}</h3>
   <p>Source cutoff {new Date(selected.sourceCutoff).toLocaleString('en-GB')} · saved hash {selected.contentHash.slice(0,12)}…</p>
   <div className="finance-equation">{[['Opening',selected.opening],['Debits',selected.debits],['Credits',selected.credits],['Closing',selected.closing]].map(([label,value])=><div key={label}><span>{label}</span><strong>{moneyLabel(value)}</strong></div>)}</div>
   {!statementEquation(selected).balanced&&<p role="alert">Saved statement equation does not balance. Contact finance support before using this version.</p>}
   <button className="button" onClick={()=>void download()} disabled={busy}>Download statement</button>
   <DataTable caption="Saved statement lines" columns={['Date','Source','Debit','Credit','Running balance','Due']}>
    {selected.rows.map((row,index)=>{const source=selected.sources.find(x=>x.sourceKey===row.sourceKey);return <tr key={`${row.sourceKey}-${index}`}>
     <td>{row.postingDate}</td><td>{source?.policyId?<Link href={`/policies/${source.policyId}`}>{row.sourceKey}</Link>:row.sourceKey}</td>
     <td>{moneyLabel(row.debit)}</td><td>{moneyLabel(row.credit)}</td><td>{moneyLabel(row.runningBalance)}</td>
     <td>{row.dueDate??'—'} {row.overdue&&<Status tone="warning">Overdue at period end</Status>}</td></tr>;})}</DataTable></div>}
 </Panel>
 <Panel title="Linked transactions" note="Posted movements; payment labels require cash application evidence">
  {movements.length?<DataTable caption="Agency finance movements" columns={['Posted','Source','Policy','Debtor change','Due','Status']}>
   {movements.map(x=><tr key={x.sourceKey}><td>{x.postingDate}</td><td>{x.sourceKind}</td><td>{x.policyId?<Link href={`/policies/${x.policyId}`}>{x.policyId}</Link>:'—'}</td>
    <td>{moneyLabel(x.debtorDelta)}</td><td>{x.dueDate??'—'}</td><td>{x.sourceKind==='insurance'?'Posted invoice':x.sourceKind==='receipt-application'?'Cash applied':x.sourceKind==='receipt-application-reversal'?'Application reversed':'Posted movement'}</td></tr>)}
  </DataTable>:<EmptyState title="No movements">This agency has no posted finance movements in the current ledger.</EmptyState>}
 </Panel></div>;
}
