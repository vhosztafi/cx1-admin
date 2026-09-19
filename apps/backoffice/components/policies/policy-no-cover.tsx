'use client';
import Link from 'next/link';
import {useRef,useState} from 'react';
import {Panel,DataTable} from '../primitives';
import {useQuoteResource,LoadFeedback} from '../quotes/shared';
import {uncertainQuoteFailure} from '../../lib/quotes';
import {servicingCommand,type ServicingCommand} from '../../lib/servicing-api';
import {sendPolicyHistory,type PolicyHistoryView} from '../../lib/policy-history';

export function PolicyNoCover({policyId,effectiveAt,knownAt}:{policyId:string;effectiveAt:string;knownAt:string}) {
 const history=useQuoteResource<PolicyHistoryView>(`/api/v1/policies/${policyId}/history?${new URLSearchParams({effectiveAt,knownAt})}`);
 const [termId,setTermId]=useState(''),[reason,setReason]=useState(''),[busy,setBusy]=useState(false),[uncertain,setUncertain]=useState(false),[error,setError]=useState(''),[saved,setSaved]=useState(false);
 const pending=useRef<ServicingCommand|null>(null),sending=useRef(false);
 async function request(){if(sending.current)return;sending.current=true;setBusy(true);setError('');
  try{if(!pending.current){if(!termId||reason.trim().length<10||!history.etag)throw new Error('Choose a policy term and enter a reason of at least 10 characters.');
   pending.current=servicingCommand(`/api/v1/terms/${termId}/as-at/export`,'POST',history.etag,{effectiveAt,knownAt,reason});}
   await sendPolicyHistory(pending.current);pending.current=null;setUncertain(false);setSaved(true);
  }catch(failure){const retry=pending.current!==null&&uncertainQuoteFailure(failure);setUncertain(retry);if(!retry)pending.current=null;setError(retry?'The result is unconfirmed. Retry the same request.':failure instanceof Error?failure.message:'Unable to retain this reconstruction.');}
  finally{sending.current=false;setBusy(false);}
 }
 return <Panel title="No cover recorded at these dates" note="The known-at cutoff excludes later issued records"><div className="quote-rail-body"><p>No issued policy version was known and applicable to this selection. The table explains the excluded versions.</p>
  {!history.data?<LoadFeedback error={history.error} retry={history.refresh}/>:<>
   <DataTable caption="Excluded issued versions" columns={['Version','Why excluded','Open']}>
    {history.data.versions.map(item=><tr key={item.id}><th scope="row">Term {item.termNumber} · v{item.versionSequence}</th><td>{item.applicability==='not-yet-known'?'Recorded after the known-at cutoff':item.applicability.replaceAll('-',' ')}</td><td><Link href={`/policies/${policyId}?termId=${item.termId}&versionId=${item.id}&tab=Transactions`}>Open issued version</Link></td></tr>)}
   </DataTable>
   <form onSubmit={event=>{event.preventDefault();void request();}}><fieldset disabled={busy||uncertain||saved}><div className="quote-form-grid"><label>Reconstruction term<select aria-label="Reconstruction term" required value={termId} onChange={event=>setTermId(event.target.value)}><option value="">Choose policy term</option>{[...new Map(history.data.versions.map(item=>[item.termId,item.termNumber])).entries()].map(([id,number])=><option key={id} value={id}>Term {number}</option>)}</select></label>
    <label>Reason for no-cover reconstruction<textarea aria-label="Reason for no-cover reconstruction" required minLength={10} maxLength={2000} value={reason} onChange={event=>setReason(event.target.value)}/></label></div></fieldset>
    <button className="button" disabled={busy||saved} type="submit">{uncertain?'Retry reconstruction request':'Retain no-cover reconstruction'}</button></form>
  </>}{error?<p role="alert">{error}</p>:null}{saved?<p role="status">No-cover reconstruction saved. Rendering is pending.</p>:null}
 </div></Panel>;
}
