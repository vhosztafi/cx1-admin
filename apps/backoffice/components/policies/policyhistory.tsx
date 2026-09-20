'use client';
import Link from 'next/link';
import {useState} from 'react';
import {Panel,Status,DataTable} from '../primitives';
import {LoadFeedback,useQuoteResource} from '../quotes/shared';
import {QuoteProposalDetails} from '../quotes/quote-history';
import type {CommercialPolicyView} from '../../lib/commercial-policy';
import type {PolicyView} from '../../lib/policies-api';
import type {PolicyHistoryView,PolicyComparison} from '../../lib/policy-history';
import {formatCancellationMoney} from '../../lib/cancellation-review';

const date=(value:string)=>new Date(value).toLocaleString('en-GB',{timeZone:'Europe/London'});
const names:Record<string,string>={'new-business':'New business',adjustment:'Adjustment',renewal:'Renewal',cancellation:'Cancellation',
 selected:'Selected cover','not-yet-known':'Recorded after known-at cutoff','not-yet-effective':'Effective after selected date','different-term':'Another policy term',superseded:'Superseded by later issued cover'};
export function PolicyHistory({policy,questionLabels,cutoffs,onSelect}:{policy:PolicyView|CommercialPolicyView;questionLabels:Record<string,string>;cutoffs:string;onSelect:(value:{termId:string;versionId:string},tab?:'Transactions'|'Documents')=>void}) {
 const history=useQuoteResource<PolicyHistoryView>(`/api/v1/policies/${policy.id}/history${cutoffs?'?'+cutoffs:''}`);
 const [before,setBefore]=useState(''),[after,setAfter]=useState(''),[comparison,setComparison]=useState('');
 const [includeDrafts,setIncludeDrafts]=useState(false);
 const drafts=useQuoteResource<{items:{id:string;kind:string;state:string}[]}>(includeDrafts?`/api/v1/terms/${policy.termId}/drafts`:null);
 const compared=useQuoteResource<PolicyComparison>(comparison?`/api/v1/policies/${policy.id}/compare?${comparison}`:null);
 return <Panel title="Transactions and versions" note="Issued history · saved drafts do not change cover">
  {!history.data?<LoadFeedback error={history.error} retry={history.refresh}/>:<>
   <div className="quote-rail-body"><p>Effective at {date(history.data.effectiveAt)} · Known at {date(history.data.knownAt)} · London</p>
    <p>Agency: <Link href={`/agencies/${policy.agencyId}`}>{history.data.agencyName}</Link></p>
    <p>Each row explains whether that issued version contributes to the selected cover. Draft proposals are available separately under Servicing drafts.</p></div>
   <div className="quote-rail-body"><label><input type="checkbox" checked={includeDrafts} onChange={event=>setIncludeDrafts(event.target.checked)}/>Include draft transactions</label>
    {includeDrafts?<section aria-label="Drafts excluded from policy cover"><h3>Draft proposals for term {policy.termNumber}</h3><p>These proposals are not counted in the selected cover, comparison or reconstruction.</p>
     {!drafts.data?<LoadFeedback error={drafts.error} retry={drafts.refresh}/>:drafts.data.items.some(item=>item.state==='draft')?<ul>{drafts.data.items.filter(item=>item.state==='draft').map(item=><li key={item.id}><Link href={`/drafts/${item.id}`}>Open {item.kind} draft</Link></li>)}</ul>:<p>No active draft proposals for this term.</p>}
    </section>:null}</div>
   <DataTable caption="Policy transactions and versions" columns={['Transaction / version','Effective / recorded','Selection and reason','Open']}>
    {history.data.versions.map(item=><tr key={item.id}><th scope="row">{names[item.kind]??item.kind}<p>Term {item.termNumber} · v{item.versionSequence} · slice {item.sliceOrdinal}</p><p>Insurer: {item.providerName}</p><p>Decision binder: {item.decisionBinder}</p><p>Authority: {item.decisionAuthority}</p></th>
     <td>{date(item.effectiveAt)}<p>Recorded {date(item.processedAt)}</p><p>{item.actorLabel}</p></td><td><Status tone={item.applicability==='selected'?'success':'info'}>{names[item.applicability]??item.applicability}</Status><p>{item.reason}</p><p>Transaction due / credit: {formatCancellationMoney(item.amountDue)}</p><p>{item.documentRequests.length} document requests retained</p></td>
     <td><button className="button" onClick={()=>onSelect({termId:item.termId,versionId:item.id})}>Open version {item.versionSequence}</button>
      <button className="button" onClick={()=>onSelect({termId:item.termId,versionId:item.id},'Documents')}>View document requests</button>
      {item.sourceDraftId?<Link href={`/drafts/${item.sourceDraftId}`}>Open originating draft and decisions</Link>:<Link href={`/quotes/${policy.sourceQuoteId}`}>Open source quote and decisions</Link>}
      <Link href={`/policies/${policy.id}?termId=${item.termId}&versionId=${item.id}&tab=Transactions`}>Transaction {item.transactionSequence}</Link></td></tr>)}
   </DataTable>
   <form className="quote-rail-body" onSubmit={event=>{event.preventDefault();setComparison(new URLSearchParams({beforeVersionId:before,afterVersionId:after}).toString());}}>
    <div className="quote-form-grid">{([['Compare from',before,setBefore],['Compare to',after,setAfter]] as const).map(([label,value,select])=><label key={label}>{label}<select aria-label={label} required value={value} onChange={event=>select(event.target.value)}><option value="">Choose issued version</option>{history.data!.versions.map(item=><option key={item.id} value={item.id}>Term {item.termNumber} · v{item.versionSequence} · {names[item.kind]??item.kind} · {date(item.effectiveAt)}</option>)}</select></label>)}</div>
    <button className="button" type="submit">Compare selected versions</button>
   </form>
   {comparison?<section className="quote-rail-body" aria-label="Policy version comparison"><h3>Version comparison</h3>{!compared.data?<LoadFeedback error={compared.error} retry={compared.refresh}/>:compared.data.changes.length?compared.data.changes.map((change,index)=><details key={index}><summary>{change.kind} · {change.path.replaceAll('/',' · ')}</summary><div className="quote-form-grid">{(['before','after'] as const).map(side=><div key={side}><h4>{side==='before'?'Before':'After'}</h4>{change[side]?<QuoteProposalDetails proposal={policy.snapshot} questionLabels={questionLabels} value={JSON.parse(change[side]!.json)}/>:<p>Not recorded</p>}</div>)}</div></details>):<p>No declaration changes between these versions.</p>}</section>:null}
  </>}
 </Panel>;
}
