'use client';
import { useState } from 'react';
import type { OpsIncident, OpsIncidentDraftWrite, OpsOccurrenceResolution } from '../../lib/incidents-api';
import type { OpsIncidentRevision } from '../../../../contracts/generated/operations';
import { useQuoteResource, LoadFeedback } from '../quotes/shared';
import { Panel, DataTable } from '../primitives';
import { IncidentEditor } from './incident-editor';
import { incidentLabel } from './incident-fields';
import { IncidentFacts } from './incident-facts';

type Page<T>={items:T[];totalCount:number;nextCursor?:string};
export function RecordIncidents({policyId,productCode}:{policyId:string;productCode:OpsIncidentDraftWrite['productCode']}){
  const [cursor,setCursor]=useState(''),[selected,setSelected]=useState<string>();
  const list=useQuoteResource<Page<OpsIncident>>(`/api/v1/incidents?policyId=${policyId}&pageSize=20${cursor?`&cursor=${encodeURIComponent(cursor)}`:''}`);
  if(selected==='new')return <IncidentEditor policyId={policyId} productCode={productCode} saved={list.refresh} cancel={()=>{setSelected(undefined);list.refresh();}}/>;
  if(selected)return <IncidentDetail id={selected} policyId={policyId} productCode={productCode} saved={list.refresh} cancel={()=>{setSelected(undefined);list.refresh();}}/>;
  return <Panel title="Claims and incidents" note="Saved reports and their historical policy context"><div className="quote-rail-body">
    <button className="button primary" onClick={()=>setSelected('new')}>Log an incident</button><p className="client-help">Reports can be saved before all facts are known. Logging here does not send them to a claims administrator.</p>
    {!list.data&&<LoadFeedback error={list.error} retry={list.refresh}/>}
    {list.data&&<><DataTable caption="Saved incidents" columns={['Reference','Occurrence','Type','Status']}>{list.data.items.map(item=><tr key={item.id}><th scope="row"><button className="button" onClick={()=>setSelected(item.id)}>{item.reference}</button></th><td>{item.draft.occurrence?.occurredOn??'Not recorded'}</td><td>{item.draft.kind?incidentLabel(item.draft.kind):'Not recorded'}</td><td>{item.state==='logged'?'Logged, unsent':incidentLabel(item.state)}</td></tr>)}</DataTable>{!list.data.items.length&&<p>No incidents recorded.</p>}<p>{list.data.totalCount} saved reports</p>{cursor&&<button className="button" onClick={()=>setCursor('')}>First page</button>}{list.data.nextCursor&&<button className="button" onClick={()=>setCursor(list.data!.nextCursor!)}>Next page</button>}</>}
  </div></Panel>;
}
function IncidentDetail({id,policyId,productCode,cancel,saved}:{id:string;policyId:string;productCode:OpsIncidentDraftWrite['productCode'];cancel:()=>void;saved:()=>void}){
  const record=useQuoteResource<OpsIncident>(`/api/v1/incidents/${id}`),[cursor,setCursor]=useState('');
  const history=useQuoteResource<Page<OpsIncidentRevision>>(`/api/v1/incidents/${id}/revisions?pageSize=10${cursor?`&cursor=${encodeURIComponent(cursor)}`:''}`);
  return <>{!record.data&&<LoadFeedback error={record.error} retry={record.refresh}/>}
    {record.data&&record.etag&&<IncidentEditor key={id} initial={record.data} initialEtag={record.etag} policyId={policyId} productCode={productCode} cancel={cancel} saved={()=>{history.refresh();saved();}}/>}
    <Panel title="Report history" note="Earlier revisions remain unchanged"><div className="quote-rail-body">{!history.data&&<LoadFeedback error={history.error} retry={history.refresh}/>}
      {history.data?.items.map(item=><details key={item.id}><summary>Revision {item.number} · {item.reason} · {item.authorLabel}</summary><p>{new Date(item.createdAt).toLocaleString('en-GB')}</p><IncidentFacts draft={item.draft}/></details>)}
      {cursor&&<button className="button" onClick={()=>setCursor('')}>Newest revisions</button>}{history.data?.nextCursor&&<button className="button" onClick={()=>setCursor(history.data!.nextCursor!)}>Older revisions</button>}
    </div></Panel><IncidentResolutionHistory key={`${id}:${history.data?.totalCount??0}`} id={id}/></>;
}
function IncidentResolutionHistory({id}:{id:string}){
  const [cursor,setCursor]=useState('');const checks=useQuoteResource<Page<OpsOccurrenceResolution>>(`/api/v1/incidents/${id}/occurrence-resolutions?pageSize=10${cursor?`&cursor=${encodeURIComponent(cursor)}`:''}`);
  return <Panel title="Historical cover checks" note="Retained checks remain available after report corrections"><div className="quote-rail-body">{!checks.data?<LoadFeedback error={checks.error} retry={checks.refresh}/>:<>{checks.data.items.map(item=><details key={item.id}><summary>{incidentLabel(item.state)} · known at {new Date(item.knownAt).toLocaleString('en-GB')}</summary>{item.window&&<p>{item.window.isExact?'Confirmed instant':'Applicability window'}: {item.window.from} to {item.window.to}</p>}<ul>{item.candidates.map((source,index)=><li key={index}>{source.label} · {source.from} to {source.to}</li>)}</ul></details>)}{!checks.data.items.length&&<p>No historical checks recorded yet.</p>}{checks.data.nextCursor&&<button className="button" onClick={()=>setCursor(checks.data!.nextCursor!)}>Older cover checks</button>}<button className="button" onClick={checks.refresh}>Refresh cover checks</button></>}</div></Panel>;
}
