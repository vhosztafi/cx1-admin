'use client';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useEffect, useRef, useState } from 'react';
import { csrfToken, type Actor } from '../../lib/auth';
import { quoteFetch, sendQuoteCommand, staleQuoteFailure, uncertainQuoteFailure, validQuoteEtag, type PendingQuoteCommand, type QuoteView } from '../../lib/quotes';
import { funnelToProposal, proposalToFunnel, type FunnelCatalogue, type FunnelState } from '../../lib/funnel-adapter';
import { funnelOrigin, localFunnelParent } from '../../lib/funnel-origin';
import { Panel } from '../primitives';
import { LoadFeedback, useQuoteResource } from './shared';

export function QuoteFunnel({quoteId,actorId,catalogue}:{quoteId:string;actorId:string;catalogue:FunnelCatalogue}) {
  const record=useQuoteResource<QuoteView>(`/api/v1/quotes/${quoteId}`);
  if(!record.data) return <Panel title="Motor Trade risk capture"><LoadFeedback error={record.error} retry={record.refresh}/></Panel>;
  if(!record.data.capabilities.canSave || !validQuoteEtag(record.etag) || !['motor-trade-road-risks','motor-trade-combined'].includes(record.data.productCode)) return <Panel title="Capture unavailable"><p className="match-copy">This quote cannot currently be edited.</p><Link className="button" href={`/quotes/${quoteId}`}>Open saved quote</Link></Panel>;
  return <ConnectedFunnel key={`${quoteId}:${actorId}`} quote={record.data} initialEtag={record.etag} actorId={actorId} catalogue={catalogue}/>;
}
function ConnectedFunnel({quote,initialEtag,actorId,catalogue}:{quote:QuoteView;initialEtag:string;actorId:string;catalogue:FunnelCatalogue}) {
  const router=useRouter(); const frame=useRef<HTMLIFrameElement>(null); const [nonce]=useState(()=>crypto.randomUUID()); const origin=funnelOrigin();
  const saved=useRef({quote,etag:initialEtag}); const command=useRef<PendingQuoteCommand | undefined>(undefined); const lock=useRef(false); const completion=useRef(false);
  const dirty=useRef(false);
  const [error,setError]=useState('');const [busy,setBusy]=useState(false);const [uncertain,setUncertain]=useState(false);const [stale,setStale]=useState(false);const [ready,setReady]=useState(false);const [notice,setNotice]=useState('');
  const [parentOrigin,setParentOrigin]=useState('');
  useEffect(()=>{queueMicrotask(()=>{if(localFunnelParent(window.location.origin))setParentOrigin(window.location.origin);else setError('This back-office address is not configured for risk capture.');});},[]);
  useEffect(()=>{
    const unload=(event:BeforeUnloadEvent)=>{if(lock.current || command.current || dirty.current)event.preventDefault();};
    const click=(event:MouseEvent)=>{if((lock.current || command.current || dirty.current) && (event.target as Element).closest?.('a[href]')){event.preventDefault();event.stopPropagation();setError(command.current?'Confirm the pending save before leaving.':'Save the current funnel answers before leaving.');}};
    const navigation=(window as Window & {navigation?:EventTarget}).navigation;
    const navigate=(event:Event)=>{if((lock.current||command.current||dirty.current)&&event.cancelable&&!(event as Event & {hashChange?:boolean}).hashChange){event.preventDefault();setError('Save and confirm the current funnel answers before leaving.');}};
    window.addEventListener('beforeunload',unload);document.addEventListener('click',click,true);
    navigation?.addEventListener('navigate',navigate);
    return()=>{window.removeEventListener('beforeunload',unload);document.removeEventListener('click',click,true);navigation?.removeEventListener('navigate',navigate);};
  },[]);
  useEffect(()=>{
    function post(message:Record<string,unknown>){frame.current?.contentWindow?.postMessage({...message,nonce},origin);}
    async function save(state?:FunnelState,complete=false) {
      if(lock.current || stale) return;
      if(!command.current && state) {
        try {
          if(state.version!==1 || !Number.isInteger(state.step) || state.step<1 || state.step>14 || !state.formData || typeof state.formData!=='object' || Array.isArray(state.formData)) throw new Error('The funnel returned an invalid draft.');
          const proposal=funnelToProposal(state,saved.current.quote.proposal,catalogue);
          command.current=Object.freeze({method:'PUT',url:`/api/v1/quotes/${quote.id}/proposal`,body:JSON.stringify({proposal,funnelStateJson:JSON.stringify(state),reason:'Saved from Motor Trade funnel'}),key:crypto.randomUUID(),etag:saved.current.etag,expectedId:quote.id}); completion.current=complete;
        } catch(failure){const message=failure instanceof Error?failure.message:'Review the captured answers.';setError(message);post({type:'cx1:funnel-error',message});return;}
      }
      if(!command.current)return;
      lock.current=true;setBusy(true);setError('');let attempted=false;
      try {
        const csrf=await csrfToken();const actor=await quoteFetch<Actor>('/api/v1/account');if(actor.data.id!==actorId)throw new Error('The signed-in account changed. Sign in again before saving.');
        attempted=true;await sendQuoteCommand(command.current,csrf);
        const current=await quoteFetch<QuoteView>(`/api/v1/quotes/${quote.id}`);
        if(!validQuoteEtag(current.etag))throw new Error('Reload the quote to confirm its saved version.');
        saved.current={quote:current.data,etag:current.etag};command.current=undefined;setUncertain(false);setNotice('Draft saved.');
        dirty.current=false;
        const url=`${window.location.origin}/quotes/${quote.id}`;post({type:'cx1:funnel-saved',url,revisionId:current.data.revisionId,state:current.data.funnelStateJson?JSON.parse(current.data.funnelStateJson):undefined,readiness:current.data.readiness});if(completion.current)router.replace(`/quotes/${quote.id}`);
      } catch(failure){const pending=uncertain||(attempted&&uncertainQuoteFailure(failure));setUncertain(pending);setStale(!pending&&staleQuoteFailure(failure));if(!pending)command.current=undefined;const message=failure instanceof Error?failure.message:'The saved result could not be confirmed.';setError(message);post({type:'cx1:funnel-error',message,pending});}
      finally {lock.current=false;setBusy(false);}
    }
    function receive(event:MessageEvent){
      if(!localFunnelParent(window.location.origin) || event.origin!==origin || event.source!==frame.current?.contentWindow || !event.data || typeof event.data!=='object' || event.data.nonce!==nonce)return;
      if(event.data.type==='cx1:funnel-ready') {
        let state:FunnelState;try {state=saved.current.quote.funnelStateJson ? JSON.parse(saved.current.quote.funnelStateJson) : {version:1,step:1,formData:proposalToFunnel(saved.current.quote.proposal,catalogue)};}catch {setError('The saved capture state could not be opened.');return;}
        post({type:'cx1:funnel-open',state,pending:Boolean(command.current),context:{quoteId:quote.id,revisionId:saved.current.quote.revisionId,reference:quote.reference,clientName:quote.clientName,agencyName:quote.agencyName,productCode:quote.productCode,captureLimits:catalogue.captureLimits}});setReady(true);
      }
      if(event.data.type==='cx1:funnel-save' && !uncertain)void save(event.data.state,event.data.complete===true);
      if(event.data.type==='cx1:funnel-dirty')dirty.current=true;
      if(event.data.type==='cx1:funnel-retry' && uncertain)void save();
      if(event.data.type==='cx1:funnel-return' && !lock.current && !command.current)router.replace(`/quotes/${quote.id}`);
    }
    window.addEventListener('message',receive);return()=>window.removeEventListener('message',receive);
  },[quote,actorId,origin,nonce,catalogue,router,stale,uncertain]);
  return <><div className="page-heading"><div><h1>{quote.reference} · Motor Trade risk capture</h1><p>{quote.clientName} · {quote.agencyName}</p></div><button type="button" className="button" disabled={busy||uncertain} onClick={()=>{if(ready)frame.current?.contentWindow?.postMessage({type:'cx1:funnel-save-return',nonce},origin);else router.replace(`/quotes/${quote.id}`);}}>Save and return to quote</button></div>
    {error&&<div className="error-message" role="alert">{error}</div>}{notice&&<div className="notice" role="status">{notice}</div>}
    {uncertain&&<button className="button" disabled={busy} onClick={()=>frame.current?.contentWindow?.postMessage({type:'cx1:funnel-retry-request',nonce},origin)}>Retry same save</button>}
    {stale&&<p>Your funnel answers are retained in its local session. Reload the current quote before reconciling changes.</p>}
    {parentOrigin&&!ready&&<p role="status">Connecting the saved quote to the Motor Trade funnel…</p>}
    {parentOrigin&&<iframe ref={frame} title="Motor Trade quote funnel" className="quote-funnel-frame" src={`${origin}/?cx1Host=backoffice&parentOrigin=${encodeURIComponent(parentOrigin)}&nonce=${encodeURIComponent(nonce)}`} />}
  </>;
}
