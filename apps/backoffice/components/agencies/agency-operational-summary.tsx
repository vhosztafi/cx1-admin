'use client';
import Link from 'next/link';
import {Panel,DataTable} from '../primitives';
import {LoadFeedback,useAgencyResource} from './shared';
type Measure={code:string;value:string|null};
type Summary={from:string;to:string;definition:string;current:Measure[];previous:Measure[]};
const measures=[['count','Quotes created'],['bound','Policies bound'],['conversion','Conversion'],['premium','Bound GWP']];
function display(code:string,value:string|null|undefined){if(value==null)return '—';const amount=Number(value);return code==='premium'?amount.toLocaleString('en-GB',{style:'currency',currency:'GBP'}):amount.toLocaleString('en-GB',{maximumFractionDigits:2})+(code==='conversion'?'%':'');}
function change(code:string,current:string|null|undefined,previous:string|null|undefined){if(current==null||previous==null)return '—';const now=Number(current),before=Number(previous);if(code==='conversion')return `${now-before>=0?'+':''}${(now-before).toFixed(2)}pp`;if(before===0)return '—';const delta=(now-before)/Math.abs(before)*100;return `${delta>=0?'+':''}${delta.toFixed(1)}%`;}
export function AgencyOperationalSummary({id}:{id:string}){
 const resource=useAgencyResource<Summary>(`/api/v1/agencies/${id}/operational-summary`);
 return <Panel title="Operational summary — last 90 days" note={resource.data?`${resource.data.from} – ${resource.data.to} · compared with preceding 90 days`:'Saved quote cohorts'}>{!resource.data?<LoadFeedback error={resource.error} retry={resource.refresh}/>:<><DataTable caption="Agency operational summary" columns={['Measure','Value','Change']}>{measures.map(([code,label])=>{const current=resource.data!.current.find(item=>item.code===code)?.value,previous=resource.data!.previous.find(item=>item.code===code)?.value;return <tr key={code}><th scope="row">{label}</th><td>{display(code,current)}</td><td>{change(code,current,previous)}</td></tr>;})}</DataTable><p className="panel-footer">{resource.data.definition} <Link href={`/reporting?reportId=12000000-0000-4000-8000-000000000001`}>Open underwriting report</Link></p></>}</Panel>;
}
