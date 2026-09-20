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
 return <ExposureObservation url={`/api/v1/policies/${policy.id}/commercial-exposure?${query}`} sourceId={policy.versionId} sourceHash={policy.contentHash}/>;
}
export function CommercialDraftExposurePanel({draftId,revisionId,dirty}:{draftId:string;revisionId:string;dirty:boolean}) {
 return <ExposureObservation url={`/api/v1/drafts/${draftId}/commercial-exposure`} sourceId={revisionId} draft dirty={dirty}/>;
}
function ExposureObservation({url,sourceId,sourceHash,draft=false,dirty=false}:{url:string;sourceId:string;sourceHash?:string;draft?:boolean;dirty?:boolean}) {
 const resource=useQuoteResource<Exposure>(url),data=resource.data;
 const matches=data?.source?.id===sourceId && (!sourceHash||data?.source?.hash===sourceHash);
 return <Panel title="Dated district exposure" note="Advisory observation · capacity is rechecked when cover is issued">
 {!data?<LoadFeedback error={resource.error} retry={resource.refresh}/>:<><div className="quote-rail-body">
 <Status tone={data.outcome==='within-capacity'?'success':'info'}>{outcomes[data.outcome]??'Assessment unavailable'}</Status>
 <p>Effective {date(data.effectiveAt)} · Known at {date(data.knownAt)} · Observed {date(data.observedAt)} · London</p>
 <p>Scheduled, expired and cancelled cover contributes zero outside its active interval.</p>{draft?<p>{dirty?"This assessment describes the saved proposal. Save local changes to refresh it.":"Each interval includes the saved changes and the current issued book. This observation does not reserve capacity."}</p>:null}
 {!matches?<p role="status">{draft?"The saved proposal assessment is unavailable or describes an earlier revision. Refresh after saving a complete proposal.":"This observation describes cover selected at these dates, not the specific version open above. Select its effective and known-at dates to align the observation."}</p>:null}
 </div><DataTable caption="District exposure at selected dates" columns={['District','Interval','Own property contribution',...(data.audience==='internal'?['Book total','Policies','Limit','Headroom']:[]),'Outcome']}>
 {data.districts.map((row,index)=><tr key={`${row.district}:${index}`}><th scope="row">{row.district}</th><td>{date(row.interval.startsAt)}<p>Until {date(row.interval.endsAt)}</p></td><td>{money(row.ownProposedSumInsured)}</td>
 {data.audience==='internal'?<><td>{row.bookSumInsured===undefined?'Unavailable':money(row.bookSumInsured)}</td><td>{row.policyCount??'Unavailable'}</td><td>{row.limit===undefined?'Unavailable':money(row.limit)}</td><td>{row.headroom===undefined?'Unavailable':money(row.headroom)}</td></>:null}<td>{outcomes[row.outcome]??'Unavailable'}</td></tr>)}
 </DataTable>{!data.districts.length?<p className="quote-rail-body">No district assessment is available for these dates.</p>:null}{data.truncated?<p className="quote-rail-body">Only the first 1,000 intervals are shown. The outcome includes every assessed interval.</p>:null}</>}
 </Panel>;
}
