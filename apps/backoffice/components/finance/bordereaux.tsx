'use client';
import Link from 'next/link';
import {useRouter} from 'next/navigation';
import {useEffect,useState} from 'react';
import {EmptyState,Panel,Status} from '../primitives';
import {FinanceCommandError} from '../../lib/finance-api';
import {financeCommand,financeDownload,financeRequest,financeSend,routeForFinanceTab,type WorkspaceCommand} from '../../lib/finance-workspace-api';
import type {BordereauBatchItem,BordereauPage,BordereauVersion,BordereauMember} from '../../../../contracts/generated/finance-bordereaux.ts';
import type {FinancePeriodReview} from '../../../../contracts/generated/finance-periods.ts';

const validId=(value:string)=>/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
const etag=(id:string)=>`"${id.replaceAll('-','')}"`;
type Pending={command:WorkspaceCommand;label:string};
type Draft={policyReference:string;providerProductCode:string;agencyReference:string;reason:string};
const blank:Draft={policyReference:'',providerProductCode:'',agencyReference:'',reason:''};
export function FinanceBordereaux({agencyId,initialBatchId,periodId:initialPeriodId}:{agencyId?:string;initialBatchId?:string;periodId?:string}){
 const router=useRouter();
 const [providerId,setProviderId]=useState(''),[periodId,setPeriodId]=useState(initialPeriodId??'');
 const [periods,setPeriods]=useState<FinancePeriodReview[]>([]),[batches,setBatches]=useState<BordereauBatchItem[]>([]);
 const [batchId,setBatchId]=useState(initialBatchId??''),[current,setCurrent]=useState<BordereauVersion|null>(null);
 const [history,setHistory]=useState<BordereauVersion[]>([]),[submittedIds,setSubmittedIds]=useState<string[]>([]);
 const [drafts,setDrafts]=useState<Record<string,Draft>>({});
 const [submission,setSubmission]=useState<{state:string;providerOperationId?:string}|null>(null);
 const [pending,setPending]=useState<Pending|null>(null),[error,setError]=useState(''),[notice,setNotice]=useState(''),[busy,setBusy]=useState(false);
 async function loadBatch(id:string){
  const version=await financeRequest<BordereauVersion>(`/api/v1/finance/bordereaux/${id}`);
  if(providerId&&version.providerId!==providerId)throw Error('Selected batch is outside this provider.');
  setCurrent(version);setBatchId(id);
  const chain:BordereauVersion[]=[version];let parent=version.parentVersionId;
  while(parent&&chain.length<30){const older=await financeRequest<BordereauVersion>(`/api/v1/finance/bordereaux/${id}?versionId=${parent}`);
   chain.push(older);parent=older.parentVersionId;}
  setHistory(chain);
  const submitted=await Promise.all(chain.slice(1).filter(item=>item.state==='valid'&&item.contentHash).map(async item=>{
   try{const saved=await financeRequest<{state:string}>(`/api/v1/finance/bordereaux/${id}/versions/${item.id}/submission`);
    return saved.state==='submitted'?item.id:null;}
   catch(cause){if(cause instanceof FinanceCommandError&&cause.status===404)return null;throw cause;}}));
  setSubmittedIds(submitted.filter((item):item is string=>item!==null));
  try{const saved=await financeRequest<{state:string;providerOperationId?:string}>(`/api/v1/finance/bordereaux/${id}/versions/${version.id}/submission`);
   setSubmission(saved);}catch(cause){if(cause instanceof FinanceCommandError&&cause.status===404)setSubmission(null);else throw cause;}
 }
 async function load(){try{const list=await financeRequest<FinancePeriodReview[]>('/api/v1/finance/periods');setPeriods(list);
  setPeriodId(value=>value||list.find(item=>item.state==='open')?.id||list[0]?.id||'');
  if(providerId&&validId(providerId)){const page=await financeRequest<BordereauPage>(`/api/v1/finance/providers/${providerId}/bordereaux?page=1&pageSize=50`);
   setBatches(page.items);if(batchId)await loadBatch(batchId);}else if(batchId)await loadBatch(batchId);
  setError('');}catch(cause){setError(cause instanceof Error?cause.message:'Bordereaux could not be loaded.');}}
 useEffect(()=>{void Promise.resolve().then(load); // Initial saved read follows render.
 // eslint-disable-next-line react-hooks/exhaustive-deps
 },[providerId]);
 async function send(next:Pending){setPending(next);setBusy(true);setError('');setNotice('');try{const saved=await financeSend(next.command);
  const selected=String(saved.batchId||saved.id||batchId);
  if(validId(selected)){await loadBatch(selected);router.push(routeForFinanceTab('bordereaux',{agencyId,batchId:selected,periodId}));}else await load();
  setNotice(`${next.label} saved. Review the version and validation before export or submission.`);setPending(null);
 }catch(cause){setError(cause instanceof Error?cause.message:'Saved outcome is uncertain.');
  if(!(cause instanceof FinanceCommandError&&cause.uncertain))setPending(null);}finally{setBusy(false);}}
 function update(id:string,field:keyof Draft,value:string){setDrafts(all=>({...all,[id]:{...(all[id]??blank),[field]:value}}));}
 function correction(member:BordereauMember){if(!current)return;const draft=drafts[member.sourceJournalId]??blank;
  const values=Object.fromEntries((['policyReference','providerProductCode','agencyReference'] as const)
   .filter(field=>draft[field].trim()).map(field=>[field,draft[field].trim()]));
  if(!Object.keys(values).length||draft.reason.trim().length<10){setError('Enter a mapping value and a reason of at least 10 characters.');return;}
  void send({label:`Correction for ${member.sourceJournalId}`,command:financeCommand(
   `/api/v1/finance/bordereaux/${batchId}/members/${member.sourceJournalId}/corrections`,
   {...values,reason:draft.reason.trim()},{etag:etag(current.id)})});}
 function exclusion(member:BordereauMember){if(!current)return;const reason=(drafts[member.sourceJournalId]??blank).reason.trim();
  if(reason.length<10){setError('Give this excluded member a reason of at least 10 characters.');return;}
  void send({label:`Exclusion for ${member.sourceJournalId}`,command:financeCommand(
   `/api/v1/finance/bordereaux/${batchId}/members/${member.sourceJournalId}/exclusions`,{reason},{etag:etag(current.id)})});}
 return <div className="finance-stack"><Panel title="Insurer bordereaux" note="Exact saved provider, period, batch and version">
  {error&&<p role="alert" className="finance-alert">{error}</p>}{notice&&<p role="status" className="finance-pad">{notice}</p>}
  {pending&&<div className="finance-recovery" role="status">The result is uncertain. The exact version and command key remain available.
   <button className="button" disabled={busy} onClick={()=>void send(pending)}>Retry same action</button>
   <button className="button" disabled={busy} onClick={()=>{setPending(null);void load();}}>Review saved result</button></div>}
  <div className="finance-pad finance-form"><label>Provider ID<input value={providerId} onChange={e=>setProviderId(e.target.value.trim())} placeholder="Saved insurer provider ID"/></label>
   <label>Accounting period<select value={periodId} onChange={e=>setPeriodId(e.target.value)}>{periods.map(period=><option key={period.id} value={period.id}>{period.from} to {period.to} · {period.state}</option>)}</select></label>
   <button className="button button-primary" disabled={busy||!validId(providerId)||!validId(periodId)} onClick={()=>void send({label:'Batch generation',command:financeCommand(`/api/v1/finance/providers/${providerId}/periods/${periodId}/bordereaux`,null)})}>Generate saved batch</button>
   <button className="button" onClick={()=>void load()}>Refresh saved batches</button></div>
  <div className="finance-pad"><label>Batch ID<input value={batchId} onChange={e=>setBatchId(e.target.value.trim())} placeholder="Saved batch ID"/></label>
   <button className="button" disabled={!validId(batchId)} onClick={()=>void loadBatch(batchId).then(()=>router.push(routeForFinanceTab('bordereaux',{agencyId,batchId,periodId}))).catch(cause=>setError(String(cause)))}>Open batch</button></div>
  {batches.length>0&&<div className="finance-pad"><h3>Saved batches</h3>{batches.map(batch=><p key={batch.id}><button className="button button-quiet" onClick={()=>void loadBatch(batch.id).then(()=>router.push(routeForFinanceTab('bordereaux',{agencyId,batchId:batch.id,periodId}))).catch(cause=>setError(String(cause)))}>{batch.id} · v{batch.version} · {batch.state}</button></p>)}</div>}
  {!current?<EmptyState title="Open a saved batch">A provider and accounting period identify its source window.</EmptyState>:<>
   <div className="finance-pad"><Link className="button" href={routeForFinanceTab('bordereaux',{agencyId})}>Back to bordereaux</Link><h3>Batch {current.batchId}</h3><p>Version {current.number} · <Status>{current.state}</Status> · cutoff {current.sourceCutoff} · {current.members.length} saved members</p>
    <p>Source hash {current.sourceHash} · members hash {current.membersHash}</p>
    {current.validation.map((issue,index)=><p key={`${issue.sourceJournalId}-${index}`} role="alert">{issue.sourceJournalId??'Batch'} · {issue.field}: {issue.code}</p>)}
    <button className="button" disabled={busy} onClick={()=>void send({label:'Validation',command:financeCommand(`/api/v1/finance/bordereaux/${batchId}/validations`,null,{etag:etag(current.id)})})}>Re-run validation</button>
    {current.state==='valid'&&current.contentHash&&<><button className="button" onClick={()=>void financeDownload(`/api/v1/finance/bordereaux/${batchId}/versions/${current.id}/download`,`bordereau-${current.id}.csv`,current.contentHash??undefined).catch(cause=>setError(String(cause)))}>Export CSV</button>
     <button className="button button-primary" disabled={busy||Boolean(submission)} onClick={()=>void send({label:'Submission',command:financeCommand(`/api/v1/finance/bordereaux/${batchId}/versions/${current.id}/submissions`,{contentHash:current.contentHash},{etag:etag(current.id)})})}>Submit exact valid version</button></>}
    {submission&&<p role="status">Submission: {submission.state} · {submission.providerOperationId??'provider result pending'}</p>}
   </div>
   <div className="finance-pad"><h3>Member validation and correction</h3>{current.members.length===0?<p>No saved members.</p>:current.members.map(member=>{
    const issues=current.validation.filter(issue=>issue.sourceJournalId?.toLowerCase()===member.sourceJournalId.toLowerCase());
    const draft=drafts[member.sourceJournalId]??blank;
    return <section key={member.sourceJournalId} className="finance-record" data-source-journal-id={member.sourceJournalId}><h4>{member.policyReference||member.sourceJournalId}</h4>
     <p>Posted {member.postingDate} · premium {member.premium} · tax {member.tax} · fee {member.fee} · net {member.netDue}</p>
     <Link href={`/policies/${member.policyId}`}>Open policy finance</Link>{' · '}
     <Link href={routeForFinanceTab('accounts',{agencyId:member.agencyId})}>Open broker account</Link>
     {issues.map((issue,index)=><p key={index} role="alert">{issue.field}: {issue.code}</p>)}
     {!member.exclusionReason&&<><div className="finance-form"><label>Policy reference<input value={draft.policyReference} onChange={e=>update(member.sourceJournalId,'policyReference',e.target.value)}/></label>
      <label>Provider product code<input value={draft.providerProductCode} onChange={e=>update(member.sourceJournalId,'providerProductCode',e.target.value)}/></label>
      <label>Agency reference<input value={draft.agencyReference} onChange={e=>update(member.sourceJournalId,'agencyReference',e.target.value)}/></label>
      <label>Reason<input value={draft.reason} onChange={e=>update(member.sourceJournalId,'reason',e.target.value)}/></label></div>
      <button className="button" disabled={busy} onClick={()=>correction(member)}>Correct mapping</button>
      <button className="button" disabled={busy} onClick={()=>exclusion(member)}>Exclude row</button></>}
     {member.correctionReason&&<p>Corrected: {member.correctionReason}</p>}{member.exclusionReason&&<p>Excluded: {member.exclusionReason}</p>}
    </section>;})}</div>
   <div className="finance-pad"><h3>Saved versions</h3>{history.map(version=><p key={version.id}>v{version.number} · {version.state} · {version.id} {version.state==='valid'&&version.contentHash&&(version.id===current.id||submittedIds.includes(version.id))&&<button className="button" onClick={()=>void financeDownload(`/api/v1/finance/bordereaux/${batchId}/versions/${version.id}/download`,`bordereau-${version.id}.csv`,version.contentHash??undefined).catch(cause=>setError(String(cause)))}>Download exact saved CSV</button>}</p>)}</div>
  </>}
 </Panel></div>;
}
