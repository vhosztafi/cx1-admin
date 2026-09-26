'use client';
import {useEffect,useRef,useState} from 'react';
import {AdminError,adminRead,adminIntent,adminSend,type AdminIntent,type Catalogue,type ProductVersion,type Provider} from '../../lib/admin-api';
import {DataTable,Panel,Status} from '../primitives';

export function ProductCatalogue(){
 const [data,setData]=useState<Catalogue>(),[error,setError]=useState(''),[notice,setNotice]=useState(''),[revision,setRevision]=useState(0),[selected,setSelected]=useState<ProductVersion>(),[provider,setProvider]=useState<Provider>(),[newProvider,setNewProvider]=useState(false),[busy,setBusy]=useState(false),[retry,setRetry]=useState<AdminIntent>();
 const lock=useRef(false);
 useEffect(()=>{const c=new AbortController();adminRead<Catalogue>('/admin/catalogue',c.signal).then(setData,e=>{if(!c.signal.aborted)setError(e.message);});return()=>c.abort();},[revision]);
 function refresh(){setError('');setSelected(undefined);setProvider(undefined);setNewProvider(false);setRevision(x=>x+1);}
 async function send(intent:AdminIntent){if(lock.current)return;lock.current=true;setBusy(true);setError('');try{const saved=await adminSend<ProductVersion|Provider>(intent);setRetry(undefined);setNotice('Saved. The configuration below reflects the persisted record.');setRevision(x=>x+1);if('productId' in saved)setSelected(saved);else{setProvider(saved);setNewProvider(false);}}catch(e){setError(e instanceof Error?e.message:'Unable to confirm this change.');setRetry(e instanceof AdminError&&e.status<500?undefined:intent);}finally{lock.current=false;setBusy(false);}}
 return <div className="operations-stack">
  <div aria-live="polite">{notice&&<p className="notice">{notice}</p>}{error&&<p role="alert" className="error-message">{error}</p>}{retry&&<button className="button" disabled={busy} onClick={()=>void send(retry)}>Retry same action</button>}</div>
  <Panel title="Products & schemes" note="Published versions stay fixed. Create a draft successor to change a scheme.">
   <div className="operations-toolbar"><button className="button" onClick={refresh} disabled={busy||!!retry}>Reload catalogue</button></div>
   {!data?<p role="status">Loading catalogue…</p>:<DataTable caption="Product versions" columns={['Product','Version','Status','Effective from','Capacity provider','Action']}>
    {data.versions.map(v=><tr key={v.id}><td>{v.productName}</td><td>{v.version}</td><td><Status tone={v.state==='published'?'success':'info'}>{v.state}</Status></td><td>{v.effectiveFrom.slice(0,10)}</td><td>{data.providers.find(p=>p.id===v.providerId)?.name}</td><td><button className="button" disabled={busy||!!retry} onClick={()=>{setSelected(v);setError('');setNotice('');}}>Open version {v.version}</button></td></tr>)}
   </DataTable>}
  </Panel>
  {selected&&data&&<VersionForm key={selected.id+selected.etag} value={selected} providers={data.providers} locked={busy||!!retry} send={send}/>}
  <Panel title="Capacity providers" note="Provider codes are permanent; names and availability can be maintained.">
   <div className="operations-toolbar"><button className="button" disabled={busy||!!retry} onClick={()=>{setProvider(undefined);setNewProvider(true);}}>Add capacity provider</button></div>
   {data&&<DataTable caption="Capacity providers" columns={['Code','Name','Status','Action']}>{data.providers.map(p=><tr key={p.id}><td>{p.code}</td><td>{p.name}</td><td>{p.state}</td><td><button className="button" disabled={busy||!!retry} onClick={()=>{setProvider(p);setNewProvider(false);}}>Edit {p.name}</button></td></tr>)}</DataTable>}
   {(provider||newProvider)&&<form key={provider?.id??'new'} className="task-form" onSubmit={e=>{e.preventDefault();const f=new FormData(e.currentTarget);void send(adminIntent('/admin/providers'+(provider?'/'+provider.id:''),provider?'PUT':'POST',{code:f.get('code'),name:f.get('name'),state:f.get('state'),reason:f.get('reason')},provider?.etag));}}>
    <label>Provider code<input name="code" defaultValue={provider?.code} readOnly={!!provider} required maxLength={50} disabled={busy||!!retry}/></label><label>Provider name<input name="name" defaultValue={provider?.name} required maxLength={200} disabled={busy||!!retry}/></label>
    <label>Availability<select name="state" defaultValue={provider?.state??'active'} disabled={busy||!!retry}><option value="active">Active</option><option value="inactive">Inactive</option></select></label><label>Change reason<textarea name="reason" required maxLength={1000} disabled={busy||!!retry}/></label><button className="button button-primary" disabled={busy||!!retry}>Save provider</button>
   </form>}
  </Panel>
 </div>;
}

function VersionForm({value:v,providers,locked,send}:{value:ProductVersion;providers:Provider[];locked:boolean;send:(intent:AdminIntent)=>Promise<void>}){
 const draft=v.state==='draft';
 const covers=v.productCode==='commercial-combined'?[['commercial-combined','Commercial Combined core cover'],['contract-works','Contract works']]:[['road-risks','Road risks'],...(v.productCode==='motor-trade-combined'?[['stock-custody','Stock and custody'],['premises','Premises']]:[]),['tools-equipment','Tools and equipment']];
 return <Panel title={`${v.productName} · version ${v.version}`} note={draft?'Edit this draft, then publish it once its dates and covers are correct.':'This saved version is immutable. The form creates a new draft.'}>
  <form className="task-form" onSubmit={e=>{e.preventDefault();const f=new FormData(e.currentTarget);const from=String(f.get('from')),to=String(f.get('to'));void send(adminIntent(`/admin/product-versions/${v.id}${draft?'':'/clone'}`,draft?'PUT':'POST',{providerId:f.get('provider'),effectiveFrom:from+'T00:00:00Z',effectiveTo:to?to+'T00:00:00Z':null,coverSections:[covers[0][0],...f.getAll('covers')],reason:f.get('reason')},v.etag));}}>
   <label>Capacity provider<select name="provider" defaultValue={v.providerId} disabled={locked}>{providers.filter(p=>p.state==='active'||p.id===v.providerId).map(p=><option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
   <label>Effective from (UTC)<input name="from" type="date" defaultValue={(draft?v.effectiveFrom:v.effectiveTo??v.effectiveFrom).slice(0,10)} required disabled={locked}/></label><label>Effective to, exclusive (UTC)<input name="to" type="date" defaultValue={draft?v.effectiveTo?.slice(0,10):''} disabled={locked}/></label>
   <fieldset disabled={locked}><legend>Offered cover sections</legend><p>{covers[0][1]} is included.</p>{covers.slice(1).map(([code,label])=><label key={code}><input type="checkbox" name="covers" value={code} defaultChecked={v.coverSections.includes(code)}/>{label}</label>)}</fieldset><label>Change reason<textarea name="reason" required maxLength={1000} disabled={locked}/></label>
   <button className="button button-primary" disabled={locked}>{draft?'Save draft':'Create draft successor'}</button>
  </form>
  {draft&&<form className="operations-toolbar" onSubmit={e=>{e.preventDefault();void send(adminIntent(`/admin/product-versions/${v.id}/publish`,'POST',{reason:new FormData(e.currentTarget).get('reason')},v.etag));}}><label>Publication reason<input name="reason" required maxLength={1000} disabled={locked}/></label><button className="button" disabled={locked}>Publish saved draft</button><p>Publication uses the saved draft. Agency access requires its own approved grant.</p></form>}
 </Panel>;
}
