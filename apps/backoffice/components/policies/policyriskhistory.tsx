'use client';
import {useState} from 'react';
import Link from 'next/link';
import {MidSubmissions} from '../operations/mid-submissions';
import {DriverTasks} from '../operations/driver-tasks';
import {Panel,DataTable,Status} from '../primitives';
import {LoadFeedback,useQuoteResource} from '../quotes/shared';
import {QuoteProposalDetails} from '../quotes/quote-history';
import type {PolicyView} from '../../lib/policies-api';
import type {QuoteObject} from '../../lib/quotes';

type ItemHistory={policyId:string;kind:string;itemId:string;versions:{versionId:string;termId:string;versionSequence:number;effectiveAt:string;processedAt:string;contentHash:string;item:QuoteObject|null;transactionId:string;kind:string;reason:string;actorLabel:string;ageAtEffectiveDate:number|null;licenceYearsAtEffectiveDate:number|null;cover:QuoteObject;driverBasis:QuoteObject|null}[]};
export function PolicyRiskHistory({policy,kind,questionLabels}:{policy:PolicyView;kind:'drivers'|'vehicles';questionLabels:Record<string,string>}) {
 const [selected,setSelected]=useState('');
 const list=policy.snapshot.risk[kind];
 const items=Array.isArray(list)?list.filter((value):value is QuoteObject=>!!value&&typeof value==='object'&&!Array.isArray(value)):[];
 const history=useQuoteResource<ItemHistory>(selected?`/api/v1/policies/${policy.id}/risk/${kind}/${selected}/history`:null);
 const label=(value:unknown)=>typeof value==='object'&&value!==null&&'label' in value?String(value.label):typeof value==='string'?value:'Not recorded';
 const current=items.find(item=>item.id===selected);
 const title=(item:QuoteObject)=>String(item.fullName||item.registration||[item.firstName,item.surname].filter(Boolean).join(' ')||'Unnamed record');
 return <Panel title={kind==='drivers'?'Driver records':'Vehicle register'} note="Select an item to follow its stable identity through issued versions"><div className="quote-rail-body">
  {items.length?<DataTable caption={kind==='drivers'?'Named drivers at selected policy version':'Vehicle register at selected policy version'} columns={kind==='drivers'?['Driver','Date of birth','Relationship','Use','Record']:['Registration','Make / model','Declared value','Ownership','Record']}>
   {items.map(item=>typeof item.id==='string'?<tr key={item.id}><th scope="row">{title(item)}</th><td>{kind==='drivers'?label(item.dateOfBirth):[label(item.make),label(item.model)].join(' · ')}</td><td>{kind==='drivers'?label(item.relationship):typeof item.value==='string'?`£${item.value}`:'Not recorded'}</td><td>{kind==='drivers'?label(item.usage):label(item.declaredOwnerType??item.ownership)}</td><td><button className="button" data-risk-item-id={item.id} aria-pressed={selected===item.id} onClick={()=>setSelected(item.id as string)}>{kind==='drivers'?'Open driver record':'Open vehicle record'}: {title(item)}</button></td></tr>:null)}
  </DataTable>:<p>No {kind} recorded in this version.</p>}
  {current&&<section className="quote-saved-banner" aria-label={kind==='drivers'?'Selected driver':'Selected vehicle'}><div><span className="quote-step-label">{kind==='drivers'?'Driver':'Vehicle'}</span><h3>{title(current)}</h3><p>Policy {policy.reference} · Term {policy.termNumber} · Version {policy.versionSequence}</p><p>Effective {new Date(policy.effectiveAt).toLocaleString('en-GB',{timeZone:'Europe/London'})} · Recorded {new Date(policy.issuedAt).toLocaleString('en-GB',{timeZone:'Europe/London'})} · London</p></div><Status tone="info">{policy.coverageState} policy version</Status></section>}
  {selected?<section aria-label="Risk item history"><h3>{kind==='drivers'?'Driver':'Vehicle'} history</h3>{!history.data?<LoadFeedback error={history.error} retry={history.refresh}/>:history.data.versions.map(item=><details key={item.versionId} open={item.versionId===policy.versionId}><summary>Version {item.versionSequence} · Effective {new Date(item.effectiveAt).toLocaleString('en-GB',{timeZone:'Europe/London'})} · London{item.item?'':' · Not present'}</summary>
   <p>Recorded {new Date(item.processedAt).toLocaleString('en-GB',{timeZone:'Europe/London'})} · London</p>
   <p>{item.actorLabel} · {item.kind} · {item.reason}</p><Link href={`/policies/${policy.id}?termId=${item.termId}&versionId=${item.versionId}&tab=Transactions`}>Open originating transaction</Link>
   <p>Underwriting decisions and supporting evidence are available from the originating quote or draft in that transaction. Details not shown were not recorded in this version.</p>
   {kind==='drivers'&&item.item?<dl className="underwriting-provenance"><div><dt>Age at version effective date</dt><dd>{item.ageAtEffectiveDate??'Not recorded'}</dd></div><div><dt>Licence held at version effective date</dt><dd>{item.licenceYearsAtEffectiveDate===null?'Not recorded':`${item.licenceYearsAtEffectiveDate} complete years`}</dd></div></dl>:null}
   {item.item?<><QuoteProposalDetails value={item.item} proposal={policy.snapshot} questionLabels={questionLabels}/>{kind==='vehicles'?<dl className="underwriting-provenance"><div><dt>VIN</dt><dd>Not recorded in this issued version</dd></div></dl>:null}</>:<p>This item is not present in this issued version.</p>}
   {item.item?<section aria-label="Cover context at this version"><h4>Policy cover at this version</h4><p>{item.kind==='cancellation'?'This version records cancellation; the retained cover terms describe the cover before cancellation.':'Read these policy terms together with the declarations and endorsements for this item.'}</p><QuoteProposalDetails value={{cover:item.cover,permittedDriverBasis:item.driverBasis??'Not recorded'}} proposal={policy.snapshot} questionLabels={questionLabels}/></section>:null}
   {kind==='vehicles'&&<MidSubmissions versionId={item.versionId} riskItemId={selected}/>}
  </details>)}{kind==='drivers'&&history.data&&<DriverTasks key={selected} policyId={policy.id} driverId={selected}/>}</section>:null}
 </div></Panel>;
}
