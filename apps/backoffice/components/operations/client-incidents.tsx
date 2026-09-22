'use client';
import {useState} from 'react';
import Link from 'next/link';
import {Panel,EmptyState} from '../primitives';
import {LoadFeedback,Paging,useQuoteResource} from '../quotes/shared';
import {RecordIncidents} from './incident-detail';
import type {OpsIncidentDraftWrite} from '../../lib/incidents-api';

type Policy={id:string;reference:string;agencyName:string;productCode:OpsIncidentDraftWrite['productCode']};
type Page={items:Policy[];totalCount:number;nextCursor?:string};
export function ClientIncidents({clientId}:{clientId:string}){
  const [input,setInput]=useState(''),[search,setSearch]=useState(''),[cursors,setCursors]=useState<string[]>([]),[selected,setSelected]=useState<Policy>();
  const query=new URLSearchParams({clientId,pageSize:'10',sort:'reference',direction:'asc'});
  if(search)query.set('q',search);if(cursors.length)query.set('cursor',cursors.at(-1)!);
  const policies=useQuoteResource<Page>(`/api/v1/policies?${query}`);
  return <>
    <Panel title="Client claims history" note="Choose an accessible policy to read its saved reports and administrator summaries">
      <form className="task-filters" onSubmit={event=>{event.preventDefault();setSearch(input.trim());setCursors([]);setSelected(undefined);}}><label>Find policy for claims<input type="search" maxLength={200} value={input} onChange={event=>setInput(event.target.value)}/></label><button className="button" type="submit">Find policy</button></form>
      {!policies.data?<LoadFeedback error={policies.error} retry={policies.refresh}/>:!policies.data.items.length?<EmptyState title="No accessible policies match">Clear the search or choose another client.</EmptyState>:<div className="quote-rail-body">{policies.data.items.map(policy=><p key={policy.id}><button className="button" aria-pressed={selected?.id===policy.id} onClick={()=>setSelected(policy)}>Claims for {policy.reference}</button> · {policy.agencyName}</p>)}</div>}
      <Paging total={policies.data?.totalCount} previous={cursors.length?()=>setCursors(x=>x.slice(0,-1)):undefined} next={policies.data?.nextCursor?()=>setCursors(x=>[...x,policies.data!.nextCursor!]):undefined}/>
    </Panel>
    {selected&&<><div className="quote-row-actions"><h2>{selected.reference}</h2><Link className="button" href={`/policies/${selected.id}?tab=Claims`}>Open policy claims</Link></div><RecordIncidents key={selected.id} policyId={selected.id} productCode={selected.productCode}/></>}
  </>;
}
