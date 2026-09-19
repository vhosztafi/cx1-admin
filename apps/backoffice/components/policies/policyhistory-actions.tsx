'use client';
import Link from 'next/link';
import {useRef,useState} from 'react';
import {Panel,DataTable,Status} from '../primitives';
import {useQuoteResource,LoadFeedback} from '../quotes/shared';
import {quoteFetch,uncertainQuoteFailure} from '../../lib/quotes';
import {servicingCommand,type ServicingCommand} from '../../lib/servicing-api';
import {sendPolicyHistory,type PolicyCloneTerms,type PolicyReconstruction,type PolicyHistoryView} from '../../lib/policy-history';
import type {PolicyView} from '../../lib/policies-api';

export function PolicyHistoryActions({policy}:{policy:PolicyView}) {
 const root=`/api/v1/policies/${policy.id}`;
 const history=useQuoteResource<PolicyHistoryView>(root+'/history');
 const requests=useQuoteResource<PolicyReconstruction[]>(root+'/reconstructions');
 const [terms,setTerms]=useState<PolicyCloneTerms|null>(null),[reason,setReason]=useState(''),[confirmed,setConfirmed]=useState(false);
 const [busy,setBusy]=useState(false),[uncertain,setUncertain]=useState(false),[error,setError]=useState(''),[quoteId,setQuoteId]=useState(''),[requestId,setRequestId]=useState('');
 const pending=useRef<ServicingCommand|null>(null),sending=useRef(false);
 async function prepareClone() {
  if(sending.current)return;sending.current=true;setBusy(true);setError('');
  try{const result=await quoteFetch<PolicyCloneTerms>(root+`/clone-terms?versionId=${policy.versionId}&relationshipId=${policy.relationshipId}`);
   if(result.data.policyId!==policy.id||result.data.versionId!==policy.versionId||result.data.relationshipId!==policy.relationshipId||result.etag!==result.data.policyEtag)throw new Error('Reload the policy.');
   setTerms(result.data);setConfirmed(false);
  }catch{setError('Current agency terms are unavailable. Refresh the policy before cloning.');}
  finally{sending.current=false;setBusy(false);}
 }
 async function submit(kind:'clone'|'reconstruct') {
  if(sending.current)return;sending.current=true;setBusy(true);setError('');
  try{
   if(!pending.current){
    if(reason.trim().length<10||reason.length>2000)throw new Error('Enter a reason of 10–2,000 characters.');
    if(kind==='clone'){
     if(!terms||!confirmed)throw new Error('Review and confirm the current agency terms.');
     pending.current=servicingCommand(root+'/clone','POST',terms.policyEtag,{versionId:policy.versionId,relationshipId:policy.relationshipId,confirmedTermsId:terms.termsId,reason});
    }else{
     if(!history.etag)throw new Error('Reload the current policy before requesting a reconstruction.');
     pending.current=servicingCommand(`/api/v1/terms/${policy.termId}/as-at/export`,'POST',history.etag,{effectiveAt:policy.effectiveCutoff,knownAt:policy.knownCutoff,versionId:policy.versionId,contentHash:policy.contentHash,reason});
    }
   }
   const receipt=await sendPolicyHistory(pending.current);pending.current=null;setUncertain(false);
   if(receipt.quoteId)setQuoteId(receipt.quoteId);
   if(receipt.requestId){setRequestId(receipt.requestId);requests.refresh();}
  }catch(failure){const retry=pending.current!==null&&uncertainQuoteFailure(failure);setUncertain(retry);if(!retry){pending.current=null;history.refresh();setTerms(null);setConfirmed(false);}
   setError(retry?'The result is unconfirmed. Retry the same saved request.':failure instanceof Error?failure.message:'The request could not be saved.');
  }finally{sending.current=false;setBusy(false);}
 }
 return <Panel title="Clone and reconstruct" note="Based on the selected issued version"><div className="quote-rail-body policy-history-actions">
  <p>Term {policy.termNumber} · Version {policy.versionSequence}. Reconstruction uses the effective and known-at cutoffs shown above.</p>
  <fieldset disabled={busy||uncertain}><label className="quote-form-label">Reason for clone or reconstruction<textarea aria-label="Reason for clone or reconstruction" required minLength={10} maxLength={2000} value={reason} onChange={event=>setReason(event.target.value)}/></label>
   <div className="quote-row-actions"><button className="button" onClick={()=>void prepareClone()}>Clone to New Quote</button><button className="button" disabled={!history.etag} onClick={()=>void submit('reconstruct')}>Request reconstruction</button></div>
   {terms?<section aria-label="Confirm policy clone"><h3>New incomplete quote</h3><p>The new quote keeps this client and agency, uses current agency terms version {terms.termsVersion}, and needs new term dates and underwriting.</p>
    <label><input type="checkbox" checked={confirmed} onChange={event=>setConfirmed(event.target.checked)}/>I confirm the current agency terms and selected policy version.</label>
    <button className="button button-primary" disabled={!confirmed} onClick={()=>void submit('clone')}>Create incomplete quote</button></section>:null}
  </fieldset>
  {uncertain?<button className="button" disabled={busy} onClick={()=>void submit(pending.current?.url.endsWith('/clone')?'clone':'reconstruct')}>Retry saved request</button>:null}
  {error?<p role="alert">{error}</p>:null}{busy?<p role="status">Saving…</p>:null}
  {quoteId?<p role="status">New incomplete quote created. <Link href={`/quotes/${quoteId}`}>Open new quote</Link></p>:null}
  {requestId?<p role="status">Reconstruction request saved. Document rendering is pending.</p>:null}
  {!requests.data?<LoadFeedback error={requests.error} retry={requests.refresh}/>:requests.data.length?<DataTable caption="Saved policy reconstruction requests" columns={['Requested view','Status']}>
   {requests.data.map(item=><tr key={item.id}><th scope="row">Effective {new Date(item.effectiveAt).toLocaleString('en-GB',{timeZone:'Europe/London'})}<p>Known {new Date(item.knownAt).toLocaleString('en-GB',{timeZone:'Europe/London'})} · London</p><p>{item.reason}</p></th><td><Status tone="info">{item.state==='pending'?'Awaiting rendering':item.state}</Status><p>{item.coverageState}</p></td></tr>)}
  </DataTable>:<p>No reconstruction requests saved.</p>}
 </div></Panel>;
}
