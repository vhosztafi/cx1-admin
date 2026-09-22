'use client';
import { useEffect, useState } from 'react';
import { csrfToken } from '../../lib/auth';
import { sendTaskCommand, taskCommand } from '../../lib/tasks-api';
import type { OpsDocument, OpsDocumentVersion } from '../../../../contracts/generated/operations';
import { useQuoteResource, LoadFeedback } from '../quotes/shared';
type Page<T>={items:T[];nextCursor?:string};
export function IncidentEvidence({policyId,selected,change}:{policyId:string;selected:string[];change:(ids:string[])=>void}){
  const [subject,setSubject]=useState<string>(),[error,setError]=useState(''),[retry,setRetry]=useState(0);
  useEffect(()=>{let active=true;void (async()=>{try{const result=await sendTaskCommand(taskCommand('register',policyId,null,{kind:'policy'},`incident-evidence:${policyId}`),await csrfToken());if(active){setSubject(result.id);setError('');}}catch(failure){if(active)setError(failure instanceof Error?failure.message:'Evidence is unavailable.');}})();return()=>{active=false;};},[policyId,retry]);
  return <details><summary>Supporting evidence ({selected.length} selected)</summary><p className="client-help">Choose ready files from this policy. Each selection retains its exact version. Upload files in the policy Documents tab.</p>
    {subject?<EvidenceDocuments subjectId={subject} selected={selected} change={change}/>:<LoadFeedback error={error||undefined} retry={()=>setRetry(x=>x+1)}/>}
    {selected.map(id=><SelectedEvidence key={id} id={id} remove={()=>change(selected.filter(x=>x!==id))}/>)}
  </details>;
}
function SelectedEvidence({id,remove}:{id:string;remove:()=>void}){
  const version=useQuoteResource<OpsDocumentVersion>(`/api/v1/document-versions/${id}`);
  return <div className="quote-row-actions"><a href={`/api/v1/document-versions/${id}/content`} target="_blank" rel="noreferrer">{version.data?`${version.data.originalName} · version ${version.data.number}`:'Selected evidence'}</a><button className="button" onClick={remove}>Remove selected evidence</button>{version.error&&<p role="alert">This selected version is unavailable. Review it before saving.</p>}</div>;
}
function EvidenceDocuments({subjectId,selected,change}:{subjectId:string;selected:string[];change:(ids:string[])=>void}){
  const [cursor,setCursor]=useState(''),[document,setDocument]=useState<string>();
  const documents=useQuoteResource<Page<OpsDocument>>(`/api/v1/records/${subjectId}/documents?pageSize=10${cursor?`&cursor=${encodeURIComponent(cursor)}`:''}`);
  return <>{!documents.data?<LoadFeedback error={documents.error} retry={documents.refresh}/>:<>{documents.data.items.map(item=><div key={item.id}><button className="button" onClick={()=>setDocument(item.id)}>{item.currentVersion?.originalName??item.kind} · choose a version</button></div>)}{!documents.data.items.length&&<p>No policy documents yet.</p>}{documents.data.nextCursor&&<button className="button" onClick={()=>setCursor(documents.data!.nextCursor!)}>More documents</button>}{cursor&&<button className="button" onClick={()=>setCursor('')}>First documents</button>}</>}
    {document&&<EvidenceVersions key={document} id={document} selected={selected} change={change}/>}
  </>;
}
function EvidenceVersions({id,selected,change}:{id:string;selected:string[];change:(ids:string[])=>void}){
  const [cursor,setCursor]=useState('');const versions=useQuoteResource<Page<OpsDocumentVersion>>(`/api/v1/documents/${id}/versions?pageSize=10${cursor?`&cursor=${encodeURIComponent(cursor)}`:''}`);
  return <fieldset><legend>Document versions</legend>{!versions.data?<LoadFeedback error={versions.error} retry={versions.refresh}/>:<>{versions.data.items.map(item=><label className="incident-evidence-choice" key={item.id}><input type="checkbox" checked={selected.includes(item.id)} disabled={item.state!=='ready'||!selected.includes(item.id)&&selected.length>=20} onChange={event=>change(event.target.checked?[...selected,item.id]:selected.filter(x=>x!==item.id))}/>{item.originalName} · version {item.number} · {item.state}</label>)}{versions.data.nextCursor&&<button className="button" onClick={()=>setCursor(versions.data!.nextCursor!)}>Older file versions</button>}</>}</fieldset>;
}
