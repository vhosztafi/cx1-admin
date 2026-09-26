'use client';
import Link from 'next/link';
import {useEffect,useState} from 'react';
import {DataTable,EmptyState,Panel,Status} from './primitives';
import {csrfToken} from '../lib/auth';
type Row={id:string;reference:string;label:string;state:string;dueOn?:string;href:string};
type Queue={code:string;title:string;definition:string;count:number|null;overdue:number|null;items:Row[];nextOffset:number|null};
type Dashboard={asOf:string;scope:string;queues:Queue[];workload:{type:string;count:number}[];activity:{id:string;taskId:string;kind:string;createdAt:string}[]};
type Notice={id:string;kind:string;text:string;createdAt:string;href:string;read:boolean};
export function DashboardWorkspace({name,canQuote}:{name:string;canQuote:boolean}) {
 const [scope,setScope]=useState('mine'),[query,setQuery]=useState(''),[refresh,setRefresh]=useState(0);
 const [data,setData]=useState<Dashboard|null>(null),[error,setError]=useState('');
 useEffect(()=>{const c=new AbortController();fetch('/api/v1/dashboard?scope='+scope+query,{signal:c.signal}).then(async r=>{if(!r.ok)throw Error('Dashboard could not be loaded. Please retry.');return r.json();}).then(setData).catch(e=>{if(!c.signal.aborted)setError(e.message);});return()=>c.abort();},[scope,query,refresh]);
 const change=(next:string)=>{setData(null);setError('');setScope(next);setQuery('');};
 return <><div className="page-heading"><div><h1>Welcome, {name}</h1><p>Your back office workspace</p></div>{canQuote&&<Link href="/quotes/new" className="button button-primary">New Quote</Link>}</div>
 <div className="report-filter-grid"><label>Assignment scope<select aria-label="Assignment scope" value={scope} onChange={e=>change(e.target.value)}><option value="mine">My assigned work</option><option value="team">My team’s work</option></select></label><div><button className="button" onClick={()=>{setData(null);setError('');setRefresh(n=>n+1);}}>Refresh</button> <Link href="/search">Advanced Search</Link> · <Link href="/tasks">View My Tasks</Link></div></div>
 {error&&<p role="alert">{error}</p>}{!data&&!error&&<p role="status">Loading your saved work…</p>}
 {data&&<><p className="muted">As of {new Date(data.asOf).toLocaleString('en-GB',{timeZone:'Europe/London'})} London</p><div className="kpi-grid">{data.queues.map(q=><a className="kpi" href={'#queue-'+q.code} key={q.code}><p>{q.title}</p><strong>{q.count??'—'}</strong><span>{q.count===null?'Not available with your role':q.overdue!==null?`${q.overdue} overdue`:'Saved records'}</span></a>)}</div>
 <div className="dashboard-grid">{data.queues.filter(q=>q.count!==null).map(q=><div id={'queue-'+q.code} key={q.code}><Panel title={q.title} note={q.definition}>{q.items.length?<DataTable caption={q.title} columns={['Reference','Work','Status','Due']}>{q.items.map(r=><tr key={r.id}><td><Link prefetch={false} href={r.href}>{r.reference}</Link></td><td>{r.label}</td><td><Status>{r.state}</Status></td><td>{r.dueOn??'—'}</td></tr>)}</DataTable>:<EmptyState title="No work in this queue">There are no matching saved records.</EmptyState>}{q.nextOffset!=null&&<button className="button" onClick={()=>{setData(null);setQuery('&queue='+q.code+'&offset='+q.nextOffset);}}>Next 25 in {q.title}</button>}</Panel></div>)}</div>
 <Panel title="Workload by task type" note="Open tasks in the selected assignment scope"><DataTable caption="Workload" columns={['Task type','Open tasks']}>{data.workload.map(x=><tr key={x.type}><td>{x.type}</td><td>{x.count}</td></tr>)}</DataTable></Panel>
 <Panel title="My recent task activity" note="Your recorded actions on tasks in the selected scope">{data.activity.length?<ul>{data.activity.map(x=><li key={x.id}><Link href={'/tasks/'+x.taskId}>{x.kind}</Link> · {new Date(x.createdAt).toLocaleString('en-GB',{timeZone:'Europe/London'})}</li>)}</ul>:<EmptyState title="No recent activity">Your saved task actions will appear here.</EmptyState>}</Panel></>}
 </>;
}
export function AlertsWorkspace(){
 const [data,setData]=useState<Notice[]|null>(null),[error,setError]=useState(''),[refresh,setRefresh]=useState(0),[busy,setBusy]=useState<string|null>(null);
 useEffect(()=>{const c=new AbortController();fetch('/api/v1/notifications',{signal:c.signal}).then(async r=>{if(!r.ok)throw Error('Alerts could not be loaded.');return r.json();}).then(setData).catch(e=>{if(!c.signal.aborted)setError(e.message);});return()=>c.abort();},[refresh]);
 async function read(id:string){setBusy(id);setError('');try{const r=await fetch('/api/v1/notifications/'+id+'/read',{method:'POST',headers:{'X-CSRF-Token':await csrfToken()}});if(!r.ok)throw Error('This alert could not be marked read.');setRefresh(n=>n+1);}catch(e){setError((e as Error).message);}finally{setBusy(null);}}
 return <><div className="page-heading"><div><h1>My alerts</h1><p>Your latest 50 open tasks and 50 account security notices.</p></div></div>{error&&<p role="alert">{error}</p>}<Panel title="Task and security notices">{data===null?<p role="status">Loading alerts…</p>:data.length?<DataTable caption="My alerts" columns={['Notice','Type','Recorded','Read status']}>{data.map(n=><tr key={n.id}><td><Link href={n.href}>{n.text}</Link></td><td>{n.kind}</td><td>{new Date(n.createdAt).toLocaleString('en-GB',{timeZone:'Europe/London'})}</td><td>{n.read?<Status>Read</Status>:<button className="button" disabled={busy!==null} onClick={()=>read(n.id)}>Mark read</button>}</td></tr>)}</DataTable>:<EmptyState title="You have no alerts">No open assigned tasks or account notices are recorded.</EmptyState>}</Panel></>;
}
