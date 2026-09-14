'use client';
import {useState} from 'react';
import Link from 'next/link';
import {Panel,DataTable,EmptyState,Status} from '../primitives';
import {clientDate,type Page} from '../../lib/clients';
import {matchStates,type MatchReview} from '../../lib/matches';
import {LoadFeedback,Paging,useClientResource} from './shared';
export function MatchList(){
  const [state,setState]=useState('');const [history,setHistory]=useState(['']);const cursor=history.at(-1)!;
  const resource=useClientResource<Page<MatchReview>>(`/api/v1/matches?pageSize=15${state?`&state=${state}`:''}${cursor?`&cursor=${encodeURIComponent(cursor)}`:''}`);
  return <><Link href="/clients" className="client-back">← All clients</Link><div className="page-heading"><div><h1>Match reviews</h1><p>Review submitted identities against existing client accounts</p></div></div>
    <Panel title="Duplicate reviews" note="Saved intake examples · internal underwriting evidence"><div className="operations-toolbar"><label>Review status<select value={state} onChange={event=>{setState(event.target.value);setHistory(['']);}}><option value="">All statuses</option>{Object.entries(matchStates).map(([key,label])=><option key={key} value={key}>{label}</option>)}</select></label><button className="button" onClick={()=>{setHistory(['']);resource.refresh();}}>Refresh reviews</button></div>
    {!resource.data?<LoadFeedback error={resource.error} retry={resource.refresh}/>:!resource.data.items.length?<EmptyState title="No reviews match this status">Choose another status or refresh the list.</EmptyState>:<DataTable caption="Match reviews" columns={['Intake','Submitted business','Submitting agency','Confidence','Status','Received']}>
      {resource.data.items.map(row=><tr key={row.id}><td><Link className="client-reference" href={`/matches/${row.id}`} prefetch={false}>{row.submission.reference}</Link></td><td>{row.submission.identity.legalName}</td><td>{row.submission.agencyName}</td><td className="match-capitalize">{row.confidence}</td><td><Status tone={row.state==='pending'||row.state==='queried'?'warning':row.state==='linked'?'success':'muted'}>{matchStates[row.state]}</Status></td><td>{clientDate(row.submission.createdAt)}</td></tr>)}
    </DataTable>}<Paging total={resource.data?.totalCount} previous={history.length>1?()=>setHistory(x=>x.slice(0,-1)):undefined} next={resource.data?.nextCursor?()=>setHistory(x=>[...x,resource.data!.nextCursor!]):undefined}/></Panel></>;
}
