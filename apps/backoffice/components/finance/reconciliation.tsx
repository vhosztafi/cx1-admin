'use client';
import {useRouter} from 'next/navigation';
import {useEffect,useState} from 'react';
import {DataTable,EmptyState,Panel} from '../primitives';
import {FinanceCommandError,minorUnits,moneyLabel} from '../../lib/finance-api';
import {financeCommand,financeRequest,financeSend,routeForFinanceTab,type WorkspaceCommand} from '../../lib/finance-workspace-api';
import type {FinanceBankLinePage,FinanceReconciliation} from '../../../../contracts/generated/finance-reconciliation.ts';

export function FinanceReconciliation({agencyId,initialReconciliationId}: {agencyId:string;initialReconciliationId?:string}){
 const router=useRouter();const [selectedId,setSelectedId]=useState(initialReconciliationId??'');
 const [bank,setBank]=useState<FinanceBankLinePage|null>(null),[detail,setDetail]=useState<FinanceReconciliation|null>(null);
 const [from,setFrom]=useState(''),[to,setTo]=useState(''),[importKey,setImportKey]=useState('');
 const [valueDate,setValueDate]=useState(''),[reference,setReference]=useState(''),[signedAmount,setSignedAmount]=useState('');
 const [lineId,setLineId]=useState(''),[postingId,setPostingId]=useState(''),[amount,setAmount]=useState('');
 const [duplicateId,setDuplicateId]=useState(''),[evidence,setEvidence]=useState('');
 const [reason,setReason]=useState(''),[pending,setPending]=useState<WorkspaceCommand|null>(null);
 const [busy,setBusy]=useState(false),[conflict,setConflict]=useState(false),[error,setError]=useState(''),[notice,setNotice]=useState('');
 async function load(id=selectedId){try{const [lines,recon]=await Promise.all([
  financeRequest<FinanceBankLinePage>(`/api/v1/finance/agencies/${agencyId}/bank-lines?page=1&pageSize=50`),
  id?financeRequest<FinanceReconciliation>(`/api/v1/finance/reconciliations/${id}`):Promise.resolve(null)]);
  if(recon&&recon.agencyId.toLowerCase()!==agencyId.toLowerCase())throw new FinanceCommandError(404);
  setBank(lines);setDetail(recon);setError('');}
 catch(cause){if(cause instanceof FinanceCommandError&&cause.denied){setBank(null);setDetail(null);}
  setError(cause instanceof Error?cause.message:'Reconciliation could not be loaded.');}}
 useEffect(()=>{void Promise.resolve().then(()=>load()); // Initial saved read follows render.
 // eslint-disable-next-line react-hooks/exhaustive-deps
 },[agencyId,selectedId]);
 async function run(command:WorkspaceCommand){setPending(command);setBusy(true);setConflict(false);setError('');setNotice('');try{
  const saved=await financeSend(command);setPending(null);
  if(command.url.endsWith('/reconciliations')&&typeof saved.id==='string'){
   setSelectedId(saved.id);router.push(routeForFinanceTab('reconciliation',{agencyId,reconciliationId:saved.id}));
   await load(saved.id);
  }else await load();setNotice(`Saved reconciliation action ${String(saved.id??selectedId)}.`);
 }catch(cause){setError(cause instanceof Error?cause.message:'The saved result could not be confirmed.');
  if(cause instanceof FinanceCommandError){if(cause.conflict)setConflict(true);if(!cause.uncertain)setPending(null);}
 }finally{setBusy(false);}}
 function create(){if(!from||!to||from>=to){setError('Choose a valid bank reconciliation window.');return;}
  void run(financeCommand(`/api/v1/finance/agencies/${agencyId}/reconciliations`,{from,to}));}
 function importLine(){try{if(!importKey.trim()||!valueDate||!reference.trim()||minorUnits(signedAmount)===0)
   throw Error('Enter original bank import key, date, reference and signed amount.');
  void run(financeCommand(`/api/v1/finance/agencies/${agencyId}/bank-lines`,
   {importKey:importKey.trim(),valueDate,reference:reference.trim(),signedAmount,currency:'GBP',raw:{source:'manual-finance-ui'}}));
 }catch(cause){setError(cause instanceof Error?cause.message:'Review bank line.');}}
 const line=detail?.lines.find(x=>x.bankLineId===lineId);
 return <div className="finance-stack"><Panel title="Reconciliation" note={`Signed bank and posted cash evidence for agency ${agencyId}`}>
  {error&&<p role="alert" className="finance-alert">{error}</p>}{notice&&<p role="status" className="finance-pad">{notice}</p>}
  {pending&&<div role="status" className="finance-recovery"><p>Awaiting confirmation. Bank selection and the exact command key are retained.</p>
   <button className="button button-primary" disabled={busy} onClick={()=>void run(pending)}>Retry same action</button></div>}
  {conflict&&<div className="finance-recovery"><p>Saved bank or cash residual changed. Review current evidence; typed values remain.</p>
   <button className="button" onClick={()=>{setConflict(false);void load();}}>Review saved version</button></div>}
  <form className="finance-form finance-pad" onSubmit={event=>{event.preventDefault();create();}}>
   <label>From<input type="date" value={from} onChange={event=>setFrom(event.target.value)} required/></label>
   <label>To (exclusive)<input type="date" value={to} onChange={event=>setTo(event.target.value)} required/></label>
   <button className="button button-primary" disabled={busy||!!pending}>Open reconciliation</button></form>
  <p className="finance-pad client-help">The same agency and exact window reopen the saved reconciliation. A bank value date can differ from its cash posting date.</p>
 </Panel>
 <Panel title="Import bank line" note="Manual local evidence · no bank connection"><form className="finance-form finance-pad"
  onSubmit={event=>{event.preventDefault();importLine();}}>
  <label>Original import ID<input value={importKey} onChange={event=>setImportKey(event.target.value)} required/></label>
  <label>Value date<input type="date" value={valueDate} onChange={event=>setValueDate(event.target.value)} required/></label>
  <label>Bank reference<input value={reference} onChange={event=>setReference(event.target.value)} required/></label>
  <label>Signed amount (GBP)<input inputMode="decimal" value={signedAmount} onChange={event=>setSignedAmount(event.target.value)} placeholder="0.00" required/></label>
  <button className="button" disabled={busy||!!pending}>Import bank lines</button></form>
  {bank?.items.length?<DataTable caption="Saved imported bank lines" columns={['Value date','Import ID','Reference','Signed amount','Duplicate candidates']}>
   {bank.items.map(item=><tr key={item.id}><td>{item.valueDate}</td><td>{item.importKey}</td><td>{item.reference}</td>
    <td>{moneyLabel(item.signedAmount)}</td><td>{item.duplicateCandidateIds.length}</td></tr>)}
  </DataTable>:<EmptyState title="No imported bank lines">Import an original bank ID to retain a distinct line even when its facts match another.</EmptyState>}
 </Panel>
 {detail&&<><Panel title="Saved reconciliation" note={`${detail.from} to ${detail.to} · ${detail.id}`}>
  <div className="finance-measures"><div><span>Net variance</span><strong>{moneyLabel(detail.netVariance)}</strong></div>
   <div><span>Absolute residual</span><strong>{moneyLabel(detail.absoluteVariance)}</strong></div>
   <div><span>Unaddressed</span><strong>{detail.unaddressedCount}</strong></div>
   <div><span>State</span><strong>{detail.completedAt?'Completed':'Open'}</strong></div></div>
  <p className="finance-pad client-help">An explanation remains visible as an audit exception; it does not create cash or reduce the actual variance.</p>
  <DataTable caption="Bank line residuals" columns={['Date','Import ID','Signed','Residual','Evidence','Action']}>
   {detail.lines.map(item=><tr key={item.bankLineId}><td>{item.valueDate}</td><td>{item.importKey}</td>
    <td>{moneyLabel(item.signedAmount)}</td><td>{moneyLabel(item.residual)}</td>
    <td>{item.excluded?'Excluded duplicate':item.explanation??(item.addressed?'Matched':'Unaddressed')}</td>
    <td><button className="button" disabled={!!detail.completedAt} onClick={()=>{setLineId(item.bankLineId);setAmount(item.residual);}}>Resolve</button></td></tr>)}
  </DataTable>
  <DataTable caption="Posted cash source residuals" columns={['Posted','Source','Signed','Residual','Explanation']}>
   {detail.targets.map(item=><tr key={item.financePostingId}><td>{item.postingDate}</td><td>{item.sourceKind}</td>
    <td>{moneyLabel(item.signedAmount)}</td><td>{moneyLabel(item.residual)}</td><td>{item.explanation??(item.addressed?'Matched':'Unaddressed')}</td></tr>)}
  </DataTable>
  <div className="finance-pad operations-actions"><button className="button button-primary" disabled={busy||!!pending||!!detail.completedAt||detail.unaddressedCount!==0}
   onClick={()=>void run(financeCommand(`/api/v1/finance/reconciliations/${detail.id}/complete`,{}))}>Complete reconciliation</button>
   <button className="button" onClick={()=>void load()}>Refresh residuals</button></div>
 </Panel>
 {line&&<Panel title="Resolve bank line" note={`Saved line ${line.bankLineId}`}><div className="finance-pad">
  <form className="finance-form" onSubmit={event=>{event.preventDefault();void run(financeCommand(
   `/api/v1/finance/reconciliations/${detail.id}/matches`,{bankLineId:lineId,financePostingId:postingId,signedAmount:amount,reason}));}}>
   <label>Posted cash source<select value={postingId} onChange={event=>setPostingId(event.target.value)} required>
    <option value="">Choose source with matching sign and GBP</option>{detail.targets.filter(item=>
     minorUnits(item.residual)!==0&&Math.sign(minorUnits(item.residual))===Math.sign(minorUnits(line.residual)))
     .map(item=><option key={item.financePostingId} value={item.financePostingId}>{item.sourceKind} · {item.postingDate} · residual {moneyLabel(item.residual)}</option>)}</select></label>
   <label>Signed amount to match<input inputMode="decimal" value={amount} onChange={event=>setAmount(event.target.value)} required/></label>
   <label>Reason<input value={reason} onChange={event=>setReason(event.target.value)} minLength={10} required/></label>
   <button className="button button-primary" disabled={busy||!!pending||!!detail.completedAt}>Match line</button></form>
  <div className="finance-form"><label>Duplicate of bank line<select value={duplicateId} onChange={event=>setDuplicateId(event.target.value)}><option value="">Choose comparison</option>
   {detail.lines.filter(item=>item.bankLineId!==lineId).map(item=><option key={item.bankLineId} value={item.bankLineId}>{item.importKey}</option>)}</select></label>
   <label>Evidence reference<input value={evidence} onChange={event=>setEvidence(event.target.value)}/></label>
   <button className="button" disabled={!duplicateId||!evidence||!reason||!!detail.completedAt} onClick={()=>void run(financeCommand(
    `/api/v1/finance/reconciliations/${detail.id}/exclusions`,{bankLineId:lineId,duplicateOfBankLineId:duplicateId,evidenceReference:evidence,reason}))}>Exclude duplicate</button>
   <button className="button" disabled={!reason||!!detail.completedAt} onClick={()=>void run(financeCommand(
    `/api/v1/finance/reconciliations/${detail.id}/variances`,{bankLineId:lineId,reason}))}>Explain variance</button></div>
  <p>Resolve documents the line. Exclusion requires another retained original import ID and comparison evidence.</p>
 </div></Panel>}
 <Panel title="Match and source history" note="Append-only reversals and reasoned target exceptions">
  <DataTable caption="Saved matches" columns={['Bank line','Cash source','Signed amount','State','Action']}>
   {detail.matches.filter(item=>!item.reversalOfId).map(item=><tr key={item.id}><td>{item.bankLineId}</td>
    <td>{item.financePostingId}</td><td>{moneyLabel(item.signedAmount)}</td>
    <td>{detail.matches.some(other=>other.reversalOfId===item.id)?'Reversed':'Matched'}</td><td>
     {!detail.completedAt&&!detail.matches.some(other=>other.reversalOfId===item.id)&&<button className="button" disabled={!reason}
      onClick={()=>void run(financeCommand(`/api/v1/finance/reconciliation-matches/${item.id}/reversals`,{reason}))}>Unmatch</button>}</td></tr>)}
  </DataTable><div className="finance-pad finance-form"><label>Cash source to explain<select value={postingId} onChange={event=>setPostingId(event.target.value)}>
   <option value="">Choose source</option>{detail.targets.filter(item=>minorUnits(item.residual)!==0).map(item=><option key={item.financePostingId} value={item.financePostingId}>{item.sourceKind} · {moneyLabel(item.residual)}</option>)}</select></label>
   <button className="button" disabled={!postingId||!reason||!!detail.completedAt} onClick={()=>void run(financeCommand(
    `/api/v1/finance/reconciliations/${detail.id}/target-variances`,{financePostingId:postingId,reason}))}>Explain cash residual</button></div>
 </Panel></>}
 </div>;
}
