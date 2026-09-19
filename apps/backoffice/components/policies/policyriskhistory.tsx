'use client';
import {useState} from 'react';
import {Panel} from '../primitives';
import {LoadFeedback,useQuoteResource} from '../quotes/shared';
import {QuoteProposalDetails} from '../quotes/quote-history';
import type {PolicyView} from '../../lib/policies-api';
import type {QuoteObject} from '../../lib/quotes';

type ItemHistory={policyId:string;kind:string;itemId:string;versions:{versionId:string;termId:string;versionSequence:number;effectiveAt:string;processedAt:string;contentHash:string;item:QuoteObject|null}[]};
export function PolicyRiskHistory({policy,kind,questionLabels}:{policy:PolicyView;kind:'drivers'|'vehicles';questionLabels:Record<string,string>}) {
 const [selected,setSelected]=useState('');
 const list=policy.snapshot.risk[kind];
 const items=Array.isArray(list)?list.filter((value):value is QuoteObject=>!!value&&typeof value==='object'&&!Array.isArray(value)):[];
 const history=useQuoteResource<ItemHistory>(selected?`/api/v1/policies/${policy.id}/risk/${kind}/${selected}/history`:null);
 return <Panel title={kind==='drivers'?'Driver records':'Vehicle register'} note="Select an item to follow its stable identity through issued versions"><div className="quote-rail-body">
  {items.length?<div className="quote-row-actions">{items.map((item,index)=>typeof item.id==='string'?<button className="button" key={item.id} aria-pressed={selected===item.id} onClick={()=>setSelected(item.id as string)}>
   {kind==='drivers'?'Open driver record':'Open vehicle record'}: {String(item.fullName||item.registration||[item.firstName,item.surname].filter(Boolean).join(' ')||index+1)}
  </button>:null)}</div>:<p>No {kind} recorded in this version.</p>}
  {selected?<section aria-label="Risk item history"><h3>{kind==='drivers'?'Driver':'Vehicle'} history</h3>{!history.data?<LoadFeedback error={history.error} retry={history.refresh}/>:history.data.versions.map(item=><details key={item.versionId} open={item.versionId===policy.versionId}><summary>Version {item.versionSequence} · Effective {new Date(item.effectiveAt).toLocaleString('en-GB',{timeZone:'Europe/London'})} · London{item.item?'':' · Not present'}</summary>
   <p>Recorded {new Date(item.processedAt).toLocaleString('en-GB',{timeZone:'Europe/London'})} · London</p>
   {item.item?<QuoteProposalDetails value={item.item} proposal={policy.snapshot} questionLabels={questionLabels}/>:<p>This item is not present in this issued version.</p>}
  </details>)}</section>:null}
 </div></Panel>;
}
