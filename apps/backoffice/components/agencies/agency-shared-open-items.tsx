'use client';
import {useState} from 'react';
import {Panel,DataTable,Status} from '../primitives';
import {clientDate,type Page} from '../../lib/clients';
import {LoadFeedback,Paging,useAgencyResource} from './shared';
type OpenItem={id:string;reference:string;kind:string;subject:string;instruction:string;state:string;createdAt:string};
export function AgencySharedOpenItems({agencyId}:{agencyId:string}) {
  const [input,setInput]=useState(''),[search,setSearch]=useState(''),[cursors,setCursors]=useState<string[]>([]);
  const resource=useAgencyResource<Page<OpenItem>>(`/api/v1/agencies/${agencyId}/sharing/open-items?pageSize=10${search?`&q=${encodeURIComponent(search)}`:''}${cursors.at(-1)?`&cursor=${encodeURIComponent(cursors.at(-1)!)}`:''}`);
  const refresh=()=>{setCursors([]);resource.refresh();};
  return <Panel title="Their open items" note="Delivered requests and current terms awaiting acceptance">
    <form className="agency-sharing-search" onSubmit={event=>{event.preventDefault();setSearch(input.trim());refresh();}}><label htmlFor="shared-open-item-search">Search shared open items</label><div><input id="shared-open-item-search" maxLength={200} value={input} onChange={event=>setInput(event.target.value)}/><button className="button button-primary" type="submit">Search open items</button><button className="button" type="button" onClick={()=>{setInput('');setSearch('');refresh();}}>Clear search</button><button className="button" type="button" onClick={refresh}>Refresh open items</button></div></form>
    {!resource.data?<LoadFeedback error={resource.error} retry={refresh}/>:<><DataTable caption="Shared open items" columns={['Reference','Requested action','Status','Created']}>{resource.data.items.map(item=><tr key={item.id}><td className="mono">{item.reference}</td><td><strong>{item.subject}</strong><p style={{whiteSpace:'pre-wrap',overflowWrap:'anywhere'}}>{item.instruction}</p></td><td><Status tone="warning">{item.state==='awaiting-response'?'Awaiting response':'Awaiting acceptance'}</Status></td><td>{clientDate(item.createdAt)}</td></tr>)}</DataTable>{!resource.data.items.length&&<p className="match-copy">No shared open items match this search.</p>}<Paging total={resource.data.totalCount} previous={cursors.length?()=>setCursors(x=>x.slice(0,-1)):undefined} next={resource.data.nextCursor?()=>setCursors(x=>[...x,resource.data!.nextCursor!]):undefined}/></>}
  </Panel>;
}
