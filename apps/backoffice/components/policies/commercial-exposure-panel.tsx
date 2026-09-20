'use client';
import type {CommercialPolicyView} from '../../lib/commercial-policy';
import {formatCommercialMoney as money} from '../../lib/commercial-policy';
import {DataTable,Panel,Status} from '../primitives';
import {LoadFeedback,useQuoteResource} from '../quotes/shared';

type Exposure = {observedAt:string;effectiveAt:string;knownAt:string;audience:'agency'|'internal';coverageState:string;outcome:string;truncated:boolean;
 source?:{kind:string;id:string;hash:string};districts:{district:string;interval:{startsAt:string;endsAt:string};ownProposedSumInsured:string;outcome:string;
 bookSumInsured?:string;policyCount?:number;limit?:string;headroom?:string}[]};
const date=(value:string)=>new Date(value).toLocaleString('en-GB',{timeZone:'Europe/London'});
const outcomes:Record<string,string>={'within-capacity':'Within capacity','exceeds-capacity':'Capacity exceeded',unavailable:'Assessment unavailable'};
export function CommercialExposurePanel({policy}:{policy:CommercialPolicyView}) {
 const query=new URLSearchParams({effectiveAt:policy.effectiveCutoff,knownAt:policy.knownCutoff});
 const resource=useQuoteResource<Exposure>(`/api/v1/policies/${policy.id}/commercial-exposure?${query}`),data=resource.data;
 const matches=data?.source?.id===policy.versionId && data?.source?.hash===policy.contentHash;
 return <Panel title="Dated district exposure" note="Advisory observation · capacity is rechecked when cover is issued">
 {!data?<LoadFeedback error={resource.error} retry={resource.refresh}/>:<><div className="quote-rail-body">
 <Status tone={data.outcome==='within-capacity'?'success':'info'}>{outcomes[data.outcome]??'Assessment unavailable'}</Status>
 <p>Effective {date(data.effectiveAt)} · Known at {date(data.knownAt)} · Observed {date(data.observedAt)} · London</p>
 <p>Scheduled, expired and cancelled cover contributes zero outside its active interval.</p>
 {!matches?<p role="status">This observation describes cover selected at these dates, not the specific version open above. Select its effective and known-at dates to align the observation.</p>:null}
 </div><DataTable caption="District exposure at selected dates" columns={['District','Interval','Own property contribution',...(data.audience==='internal'?['Book total','Policies','Limit','Headroom']:[]),'Outcome']}>
 {data.districts.map((row,index)=><tr key={`${row.district}:${index}`}><th scope="row">{row.district}</th><td>{date(row.interval.startsAt)}<p>Until {date(row.interval.endsAt)}</p></td><td>{money(row.ownProposedSumInsured)}</td>
 {data.audience==='internal'?<><td>{row.bookSumInsured===undefined?'Unavailable':money(row.bookSumInsured)}</td><td>{row.policyCount??'Unavailable'}</td><td>{row.limit===undefined?'Unavailable':money(row.limit)}</td><td>{row.headroom===undefined?'Unavailable':money(row.headroom)}</td></>:null}<td>{outcomes[row.outcome]??'Unavailable'}</td></tr>)}
 </DataTable>{!data.districts.length?<p className="quote-rail-body">No district assessment is available for these dates.</p>:null}{data.truncated?<p className="quote-rail-body">Only the first 1,000 intervals are shown. The outcome includes every assessed interval.</p>:null}</>}
 </Panel>;
}
