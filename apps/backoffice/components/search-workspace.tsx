'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { DataTable, EmptyState, Panel, Status } from './primitives';

type Options = {products:{code:string;name:string}[];agencies:{id:string;name:string}[];providers:{id:string;name:string}[];underwriters:{id:string;name:string}[];kinds:string[]};
type Hit = {id:string;kind:string;reference:string;label:string;status:string;productCode?:string;href:string};
type Results = {items:Hit[];total:number;nextOffset:number|null;version:string;asOf:string;availableKinds:string[]};
export function SearchWorkspace({initialQuery = ''}:{initialQuery?:string}) {
  const [options,setOptions] = useState<Options|null>(null);
  const [refresh,setRefresh] = useState(0);
  const [query,setQuery] = useState(initialQuery);
  const [result,setResult] = useState<Results|null>(null);
  const [error,setError] = useState('');
  const [busy,setBusy] = useState(true);
  useEffect(() => { const c=new AbortController();fetch('/api/v1/search/options',{signal:c.signal}).then(r=>{if(!r.ok)throw new Error('Filter options could not be loaded.');return r.json();}).then(setOptions).catch(e=>{if(!c.signal.aborted)setError(e.message);});return ()=>c.abort();},[]);
  useEffect(() => {
    const controller = new AbortController();
    fetch(`/api/v1/search?${query}`,{signal:controller.signal}).then(async response => {
      if (!response.ok) throw new Error(response.status === 403 ? 'Your current role cannot search these records.' : response.status === 409 ? 'Records changed. Apply the filters again to refresh the results.' : 'Search failed. Check the filters and try again.');
      return response.json() as Promise<Results>;
    }).then(setResult).catch(e => {if (!controller.signal.aborted) setError(e.message);}).finally(() => {if (!controller.signal.aborted) setBusy(false);});
    return () => controller.abort();
  },[query,refresh]);
  function run(next:string) {window.history.replaceState(null,'','/search?'+next);setError('');setResult(null);setBusy(true);setRefresh(n=>n+1);setQuery(next);}
  const initial = new URLSearchParams(initialQuery);
  return <><div className="page-heading"><div><h1>Advanced Search</h1><p>Find saved records across the back office.</p></div></div>
    <Panel title="Search records" note="Results follow your current access. Registration searches use the selected policy version; dates filter inception.">
      <form className="report-filter-grid" onSubmit={event => {event.preventDefault(); const params=new URLSearchParams();new FormData(event.currentTarget).forEach((v,k)=>{if(String(v).trim())params.set(k,k==='asOf'?String(v)+'Z':String(v).trim());});run(params.toString());}}>
        <label>Search<input name="q" defaultValue={initial.get('q')??''} maxLength={200} placeholder="Name, reference or registration"/></label>
        <label>Record type<select aria-label="Record type" name="kind" defaultValue={initial.get('kind')??'all'}><option value="all">All accessible records</option>{(options?.kinds??[]).map(k=><option key={k} value={k}>{k}</option>)}</select></label>
        <label>Status<input name="status" defaultValue={initial.get('status')??''} maxLength={40}/></label>
        <label>Product<select aria-label="Product" name="productCode" defaultValue={initial.get('productCode')??''}><option value="">All products</option>{options?.products.map(p=><option key={p.code} value={p.code}>{p.name}</option>)}</select></label>
        <label>Agency<select aria-label="Agency" name="agencyId" defaultValue={initial.get('agencyId')??''}><option value="">All agencies</option>{options?.agencies.map(p=><option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
        <label>Insurer<select aria-label="Insurer" name="providerId" defaultValue={initial.get('providerId')??''}><option value="">All insurers</option>{options?.providers.map(p=><option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
        <label>Assigned underwriter<select aria-label="Assigned underwriter" name="underwriterId" defaultValue={initial.get('underwriterId')??''}><option value="">All underwriters</option>{options?.underwriters.map(p=><option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
        <label>Inception from<input type="date" name="from" defaultValue={initial.get('from')??''}/></label>
        <label>Inception to<input type="date" name="to" defaultValue={initial.get('to')??''}/></label>
        <label>Reference<input name="reference" defaultValue={initial.get('reference')??''} maxLength={100}/></label>
        <label>Policy effective at (UTC, optional)<input type="datetime-local" name="asOf"/></label>
        <div><button className="button button-primary" disabled={busy}>Apply filters</button> <Link href="/search" className="button">Reset</Link></div>
      </form>
    </Panel>
    {busy&&<p role="status">Searching saved records…</p>}{error&&<p role="alert">{error}</p>}
    {result&&<Panel title={`${result.total} results`} note={`Ordered by record type, reference and ID. Policy basis: ${new Date(result.asOf).toLocaleString('en-GB',{timeZone:'Europe/London'})} London.`}>
      {result.items.length?<DataTable caption="Search results" columns={['Reference','Type','Account / name','Product','Status']}>{result.items.map(h=><tr key={`${h.kind}-${h.id}`}><td><Link prefetch={false} href={h.href}>{h.reference}</Link></td><td>{h.kind}</td><td>{h.label}</td><td>{h.productCode??'—'}</td><td><Status>{h.status}</Status></td></tr>)}</DataTable>:<EmptyState title="No matching records">Try broader filters. Restricted records are excluded.</EmptyState>}
      {result.nextOffset!=null&&<button className="button" onClick={()=>{const params=new URLSearchParams(query);params.set('offset',String(result.nextOffset));params.set('version',result.version);if(!params.get('kind')||['all','policy'].includes(params.get('kind')!))params.set('asOf',result.asOf);run(params.toString());}}>Next 25 results</button>}
    </Panel>}
  </>;
}
