'use client';
import Link from 'next/link';
import {useEffect,useRef,useState} from 'react';
import {DataTable,EmptyState,Panel,Status} from '../primitives';
import {accountSummary,decimalMoney,FinanceCommandError,invoiceCandidates,ledgerAll,minorUnits,receiptList,
 statementDetail,type FinanceAccountSummary,type FinanceLedgerRow,type FinanceReceiptPage} from '../../lib/finance-api';
import {correctionCommand,exactSum,financeCommand,financeDownload,financeRequest,financeSend,
 periodCloseCommand,routeForFinanceTab,type WorkspaceCommand} from '../../lib/finance-workspace-api';
import type {FinancePeriodReview} from '../../../../contracts/generated/finance-periods.ts';
import type {FinanceRefundPage} from '../../../../contracts/generated/finance-refunds.ts';

type Pending={kind:'close'|'correction'|'export';command:WorkspaceCommand};
const sourceId=(key:string)=>{const raw=key.split('/')[1];return raw&&/^[0-9a-f]{32}$/i.test(raw)?
 `${raw.slice(0,8)}-${raw.slice(8,12)}-${raw.slice(12,16)}-${raw.slice(16,20)}-${raw.slice(20)}`:'';};
async function receipts(agencyId:string){
 const first=await receiptList(agencyId),pages=Array.from({length:Math.ceil(first.total/50)-1},(_,i)=>i+2);
 const later=await Promise.all(pages.map(page=>receiptList(agencyId,page)));
 return [first,...later].flatMap((page:FinanceReceiptPage)=>page.items);
}
export function FinanceOverview({agencyId,initialPeriodId}: {agencyId?:string;initialPeriodId?:string}){
 const [periods,setPeriods]=useState<FinancePeriodReview[]>([]),[periodId,setPeriodId]=useState(initialPeriodId??'');
 const [rows,setRows]=useState<FinanceLedgerRow[]>([]),[account,setAccount]=useState<FinanceAccountSummary|null>(null);
 const [cash,setCash]=useState<FinanceReceiptPage['items']>([]);
 const [refunds,setRefunds]=useState<FinanceRefundPage|null>(null),[refundScope,setRefundScope]=useState('');
 const loadGeneration=useRef(0);
 const [source,setSource]=useState(''),[debtor,setDebtor]=useState(''),[provider,setProvider]=useState('');
 const [cashDelta,setCashDelta]=useState(''),[internalDelta,setInternalDelta]=useState('');
 const [reason,setReason]=useState(''),[closeReason,setCloseReason]=useState('');
 const [pending,setPending]=useState<Pending|null>(null),[error,setError]=useState(''),[notice,setNotice]=useState('');
 const [busy,setBusy]=useState(false);
 async function load(){
  const generation=++loadGeneration.current;
  setRefunds(null);setRefundScope('');
  try{const [list,ledger,summary,received,pendingRefunds]=await Promise.all([
   financeRequest<FinancePeriodReview[]>('/api/v1/finance/periods'),
   agencyId?ledgerAll(agencyId):Promise.resolve([]),
   agencyId?accountSummary(agencyId):Promise.resolve(null),
   agencyId?receipts(agencyId):Promise.resolve([]),
   agencyId?financeRequest<FinanceRefundPage>(`/api/v1/finance/agencies/${agencyId}/refunds?state=pending&page=1&pageSize=50`):Promise.resolve(null)]);
   if(generation!==loadGeneration.current)return;
   setPeriods(list);setRows(ledger);setAccount(summary);setCash(received);setRefunds(pendingRefunds);setRefundScope(agencyId??'');
   setPeriodId(current=>current||list.find(item=>item.state==='open')?.id||list[0]?.id||'');setError('');}
  catch(cause){if(generation!==loadGeneration.current)return;setRefunds(null);setRefundScope('');if(cause instanceof FinanceCommandError&&cause.denied){setPeriods([]);setRows([]);setAccount(null);setCash([]);}
   setError(cause instanceof Error?cause.message:'Accounting overview could not be loaded.');}
 }
 useEffect(()=>{void Promise.resolve().then(load); // Initial saved read follows render.
 // eslint-disable-next-line react-hooks/exhaustive-deps
 },[agencyId]);
 const period=periods.find(item=>item.id===periodId);
 const inWindow=(date:string)=>period&&date>=period.from&&date<period.to;
 const issued=rows.filter(row=>row.sourceKind==='insurance'&&inWindow(row.postingDate));
 const beforeClose=rows.filter(row=>period&&row.postingDate<period.to);
 const invoices=period?invoiceCandidates(beforeClose):[];
 const age=(dueDate:string|null)=>period&&dueDate?Math.max(0,Math.floor((Date.parse(period.to)-Date.parse(dueDate))/86400000)-1):0;
 const aged=(lower:number,upper:number)=>exactSum(invoices.filter(row=>{
  const days=age(row.dueDate);return days>=lower&&days<=upper;}).map(row=>row.residual));
 const unmatched=cash.filter(item=>minorUnits(item.residual)>0);
 const scopedRefunds=refundScope===agencyId?refunds:null;
 const eligible=rows.filter(row=>row.sourceKind==='insurance'&&sourceId(row.sourceKey)&&
  periods.some(p=>p.state==='closed'&&row.postingDate>=p.from&&row.postingDate<p.to));
 async function run(next:Pending){setPending(next);setBusy(true);setError('');setNotice('');try{
  const saved=await financeSend(next.command);
  if(next.kind==='export'){
   const statement=await statementDetail(String(saved.id));
   await financeDownload(`/api/v1/finance/statements/${statement.id}/download`,
    `statement-${statement.id}.csv`,statement.contentHash);
   setNotice(`Exported saved statement v${statement.version} for ${statement.from} to ${statement.to}.`);
  }else if(next.kind==='close')setNotice(`Closed saved period ${String(saved.periodId)}.`);
  else setNotice(`Posted linked correction ${String(saved.id)} in ${String(saved.accountingPeriodId)}.`);
  setPending(null);await load();
 }catch(cause){setError(cause instanceof Error?cause.message:'The saved result could not be confirmed.');
  if(!(cause instanceof FinanceCommandError&&cause.uncertain))setPending(null);
 }finally{setBusy(false);}}
 function postCorrection(){try{if(!source)throw Error('Choose a saved posted insurance source.');
  void run({kind:'correction',command:correctionCommand('insurance',source,{debtorDelta:debtor,providerDelta:provider,
   cashDelta,internalDelta,effectiveAt:new Date().toISOString(),reason})});
 }catch(cause){setError(cause instanceof Error?cause.message:'Review correction details.');}}
 return <div className="finance-stack"><Panel title="Accounting overview" note="Select an annual accounting period and a saved agency">
  {error&&<p role="alert" className="finance-alert">{error}</p>}{notice&&<p role="status" className="finance-pad">{notice}</p>}
  {pending&&<div role="status" className="finance-recovery"><p>Awaiting confirmation. The exact request and command key are retained.</p>
   <button className="button button-primary" disabled={busy} onClick={()=>void run(pending)}>Retry same action</button>
   <button className="button" disabled={busy} onClick={()=>{setPending(null);void load();}}>Review saved records</button></div>}
  <div className="finance-pad finance-form"><label>Accounting period<select value={periodId} onChange={event=>setPeriodId(event.target.value)}>
   {periods.map(item=><option key={item.id} value={item.id}>{item.from} to {item.to} · {item.state}</option>)}</select></label>
   <button className="button" onClick={()=>void load()}>Refresh saved evidence</button></div>
  {!period?<EmptyState title="No saved period">The accounting period list is unavailable under your current access.</EmptyState>:<>
   <p className="finance-pad client-help">Posting basis {period.from} inclusive to {period.to} exclusive · view refreshed from saved SQL evidence. Balance uses posted debtor movements before period end; cash received uses receipt date.</p>
   {agencyId?<><div className="finance-measures">
    <div><span>Written premium</span><strong>{exactSum(issued.flatMap(row=>[row.grossDue??'0.00',decimalMoney(-minorUnits(row.tax??'0.00')),decimalMoney(-minorUnits(row.fee??'0.00'))]))}</strong></div>
    <div><span>Tax</span><strong>{exactSum(issued.map(row=>row.tax??'0.00'))}</strong></div>
    <div><span>Fees</span><strong>{exactSum(issued.map(row=>row.fee??'0.00'))}</strong></div>
    <div><span>Commission</span><strong>{exactSum(issued.map(row=>row.commission??'0.00'))}</strong></div>
    <div><span>Net insurer payable</span><strong>{exactSum(issued.map(row=>row.netDue??'0.00'))}</strong></div>
    <div><span>Cash received</span><strong>{exactSum(cash.filter(item=>inWindow(item.receivedOn)).map(item=>item.amount))}</strong></div>
    <div><span>Closing debtor balance</span><strong>{exactSum(beforeClose.map(row=>row.debtorDelta))}</strong></div>
    <div><span>Past-due invoice residual</span><strong>{exactSum(invoices.filter(row=>age(row.dueDate)>0).map(row=>row.residual))}</strong></div>
   </div><div className="finance-measures" aria-label="Invoice residual ageing at period end">
    <div><span>Current or no due date</span><strong>{aged(0,0)}</strong></div>
    <div><span>1–30 days overdue</span><strong>{aged(1,30)}</strong></div>
    <div><span>31–60 days overdue</span><strong>{aged(31,60)}</strong></div>
    <div><span>61+ days overdue</span><strong>{aged(61,Number.MAX_SAFE_INTEGER)}</strong></div>
   </div><p className="finance-pad client-help">Current scoped account: {account?.movementCount??0} saved movements. Invoice residual and ageing include saved receipt applications before the period end. Earned premium requires coverage earning evidence and is not inferred from written premium.</p></>
   :<p className="finance-pad">Choose an agency for scoped money and exact statement export.</p>}
   <div className="finance-pad operations-actions">
    <button className="button" disabled={!agencyId||busy||!!pending} onClick={()=>agencyId&&void run({kind:'export',
     command:financeCommand(`/api/v1/finance/agencies/${agencyId}/statements`,{from:period.from,to:period.to})})}>Export period</button>
    <Link className="button" href={routeForFinanceTab('transactions',{agencyId})}>Inspect transactions</Link>
   </div></>}
 </Panel>
 {period&&agencyId&&<Panel title="Items needing attention" note="Saved receipt residuals and period checklist; no estimated figures">
  <DataTable caption="Saved accounting attention" columns={['Item','Saved evidence','Action']}>
   <tr><th scope="row">Unmatched receipt</th><td>{unmatched.length} receipt{unmatched.length===1?'':'s'} · {exactSum(unmatched.map(item=>item.residual))} residual</td>
    <td><Link href={routeForFinanceTab('payments',{agencyId,receiptId:unmatched[0]?.id})}>{unmatched[0]?'Open first saved receipt':'Open payments'}</Link></td></tr>
   <tr><th scope="row">Bank reconciliation</th><td>{period.blockers.some(item=>item.includes('reconciliation'))?'Evidence still required':'No period close blocker reported'}</td>
    <td><Link href={routeForFinanceTab('reconciliation',{agencyId})}>Review reconciliation</Link></td></tr>
   <tr><th scope="row">Bordereau validation</th><td>{period.blockers.some(item=>item.includes('bordereau'))?'Evidence still required':'No period close blocker reported'}</td>
    <td><Link href={routeForFinanceTab('bordereaux',{agencyId,periodId:period.id})}>Open bordereaux</Link></td></tr>
   <tr><th scope="row">Refund approval</th><td>{scopedRefunds?`${scopedRefunds.total} pending refund${scopedRefunds.total===1?'':'s'}`:'Loading saved refunds'}</td>
    <td><Link href={routeForFinanceTab('refunds',{agencyId,refundId:scopedRefunds?.items[0]?.id})}>{scopedRefunds?.items[0]?'Open first saved refund':'Review refunds'}</Link></td></tr>
  </DataTable></Panel>}
 {period&&<Panel title="Period close" note={`Saved state: ${period.state} · source cutoff ${period.sourceCutoff??'not closed'}`}>
  {period.blockers.length?<DataTable caption="Current close blockers" columns={['Evidence still required']}>
   {period.blockers.map(blocker=><tr key={blocker}><td><Status tone="warning">{blocker.replaceAll('-',' ')}</Status></td></tr>)}
  </DataTable>:<p className="finance-pad">No close blockers are currently reported for this period.</p>}
  {period.state==='open'?<form className="finance-form finance-pad" onSubmit={event=>{event.preventDefault();try{
   void run({kind:'close',command:periodCloseCommand(period,closeReason)});
  }catch(cause){setError(cause instanceof Error?cause.message:'Review close evidence.');}}}>
   <label>Reason for close<input value={closeReason} onChange={event=>setCloseReason(event.target.value)} minLength={10} required/></label>
   <button className="button button-primary" disabled={busy||!!pending||period.blockers.length>0}>Close period</button></form>
   :<p className="finance-pad">Closed by {period.closedBy} at {period.closedAt}. {period.closeReason}</p>}
 </Panel>}
 {agencyId&&<Panel title="Post journal" note="A correction links a closed source and posts separately in the next eligible open period">
  <form className="finance-form finance-pad" onSubmit={event=>{event.preventDefault();postCorrection();}}>
   <label>Original posted insurance source<select value={source} onChange={event=>setSource(event.target.value)} required><option value="">Choose saved source</option>
    {eligible.map(row=><option key={row.sourceKey} value={sourceId(row.sourceKey)}>{row.postingDate} · {row.policyId??row.sourceKey}</option>)}</select></label>
   {([['Debtor change',debtor,setDebtor],['Provider change',provider,setProvider],['Cash change',cashDelta,setCashDelta],
    ['Internal change',internalDelta,setInternalDelta]] as const).map(([label,value,set])=><label key={label}>{label} (GBP)
     <input inputMode="decimal" value={value} onChange={event=>set(event.target.value)} placeholder="0.00" required/></label>)}
   <label>Correction reason<input value={reason} onChange={event=>setReason(event.target.value)} minLength={10} required/></label>
   <button className="button button-primary" disabled={busy||!!pending}>Post correction</button></form>
  {eligible.length===0&&<p className="finance-pad client-help">A closed insurance source is required before posting a later correction.</p>}
 </Panel>}
 </div>;
}
