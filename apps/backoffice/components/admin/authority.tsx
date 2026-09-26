'use client';
import {useState} from 'react';
import {Panel,DataTable,Status} from '../primitives';
import {adminIntent,type Catalogue} from '../../lib/admin-api';
import {useAdminResource} from './use-admin-resource';

import type {AuthorityLimits as Limits,AdministrationAuthority as Authority,AuthorityAdministrationView as Data} from '../../../../contracts/generated/administration';
const label=(key:string)=>key.replace(/([a-z])([A-Z])/g,'$1 $2').replaceAll('-',' ').replace(/^./,c=>c.toUpperCase());

export function AuthorityAdministration(){
 const r=useAdminResource<Data>('/admin/authority'),catalogue=useAdminResource<Catalogue>('/admin/catalogue');
 const [selected,setSelected]=useState<string>();
 const source=r.data?.versions.find(x=>x.id===selected);
 return <div className="operations-stack">
  <div aria-live="polite">{r.notice&&<p className="notice">{r.notice}</p>}{(r.error||catalogue.error)&&<p role="alert" className="error-message">{r.error||catalogue.error}</p>}{r.retry&&<button className="button" disabled={r.busy} onClick={()=>void r.send(r.retry!)}>Retry same action</button>}</div>
  <Panel title="Delegated authority" note="Published limits remain fixed. New limits and referral routing take effect only after another administrator approves them.">
   <button className="button" disabled={r.locked} onClick={r.reload}>Reload authority</button>
   {!r.data?<p role="status">Loading authority…</p>:<DataTable caption="Authority versions" columns={['Product','Version','Effective dates','Status','Action']}>{r.data.versions.map(x=><tr key={x.id}><td>{x.productName}</td><td>{x.version}</td><td>{x.effectiveFrom.slice(0,10)} to {x.effectiveTo.slice(0,10)}</td><td><Status tone="info">{x.state}</Status></td><td><button className="button" disabled={r.locked||x.state!=='published'} onClick={()=>setSelected(x.id)}>Propose from {x.version}</button></td></tr>)}</DataTable>}
  </Panel>
  {source&&r.data&&catalogue.data&&<AuthorityForm key={source.id+source.etag} source={source} data={r.data} catalogue={catalogue.data} locked={r.locked} send={r.send}/>}
  <Panel title="Approval requests" note="Review the proposed limits, scope and routing. The requester cannot approve their own request.">
   {r.data?.requests.length===0&&<p>No authority requests.</p>}
   {r.data?.requests.map(x=><details key={x.id}><summary>{x.state} · {x.proposal.input.effectiveFrom.slice(0,10)} · {x.proposal.input.reason}</summary>
    <p>Requested by {r.data!.users.find(u=>u.id===x.requestedBy)?.displayName??x.requestedBy}. Product version: {catalogue.data?.versions.find(v=>v.id===x.proposal.input.productVersionId)?.productName} · {catalogue.data?.versions.find(v=>v.id===x.proposal.input.productVersionId)?.version}.</p>
    <p>Effective to {x.proposal.input.effectiveTo.slice(0,10)}. Referral team: {r.data!.teams.find(t=>t.id===x.proposal.input.routingTeamId)?.name}. Grants: {x.proposal.input.userIds.map(id=>r.data!.users.find(u=>u.id===id)?.displayName??id).join(', ')||'No staff grants requested'}.</p>
    <LimitSummary limits={x.proposal.input.limits}/>{x.decisionReason&&<p>Decision: {x.decisionReason}</p>}
    {x.state==='pending'&&<form className="task-form" onSubmit={e=>{e.preventDefault();const f=new FormData(e.currentTarget);void r.send(adminIntent(`/admin/authority/requests/${x.id}/decision`,'POST',{approve:f.get('decision')==='approve',reason:f.get('reason')},x.etag));}}><label>Decision<select name="decision" disabled={r.locked}><option value="reject">Reject</option><option value="approve">Approve and publish</option></select></label><label>Decision reason<textarea name="reason" required maxLength={1000} disabled={r.locked}/></label><button className="button" disabled={r.locked}>Record decision</button></form>}
   </details>)}
  </Panel>
  <Panel title="Staff authority grants" note="Grants apply alongside the user's role permissions. Revoke an overlapping grant before requesting its replacement.">
   {r.data&&<DataTable caption="Authority grants" columns={['User','Authority','Effective dates','Status','Revoke']}>{r.data.grants.map(x=><tr key={x.id}><td>{r.data!.users.find(u=>u.id===x.userId)?.displayName??x.userId}</td><td>{r.data!.versions.find(v=>v.id===x.authorityVersionId)?.version}</td><td>{x.effectiveFrom.slice(0,10)} to {x.effectiveTo.slice(0,10)}</td><td>{x.revokedAt?'Revoked':'Active'}</td><td>{!x.revokedAt&&<form onSubmit={e=>{e.preventDefault();void r.send(adminIntent(`/admin/authority/grants/${x.id}/revoke`,'POST',{reason:new FormData(e.currentTarget).get('reason')},x.etag));}}><input aria-label="Revocation reason" name="reason" required maxLength={1000} disabled={r.locked}/><button className="button" disabled={r.locked}>Revoke grant</button></form>}</td></tr>)}</DataTable>}
  </Panel>
 </div>;
}

function AuthorityForm({source,data,catalogue,locked,send}:{source:Authority;data:Data;catalogue:Catalogue;locked:boolean;send:ReturnType<typeof useAdminResource>['send']}){
 const [limits,setLimits]=useState<Limits>(source.limits);
 const original=catalogue.versions.find(v=>v.id===source.productVersionId);
 return <Panel title={`Propose authority · ${source.productName}`} note="Amounts and risk permissions must stay within the saved binder. Referral team changes apply to future submissions across the catalogue.">
  <form className="task-form" onSubmit={e=>{e.preventDefault();const f=new FormData(e.currentTarget);void send(adminIntent('/admin/authority/requests','POST',{sourceAuthorityId:source.id,sourceEtag:source.etag,productVersionId:f.get('product'),effectiveFrom:f.get('from')+'T00:00:00Z',effectiveTo:f.get('to')+'T00:00:00Z',limits,routingTeamId:f.get('team'),userIds:f.getAll('users'),reason:f.get('reason')}));}}>
   <label>Product version<select name="product" defaultValue={source.productVersionId} disabled={locked}>{catalogue.versions.filter(v=>v.productId===original?.productId&&v.state==='published').map(v=><option key={v.id} value={v.id}>{v.productName} · version {v.version}</option>)}</select></label>
   <label>Authority effective from<input type="date" name="from" defaultValue={new Date().toISOString().slice(0,10)} required disabled={locked}/></label><label>Authority effective to (exclusive)<input type="date" name="to" defaultValue={source.effectiveTo.slice(0,10)} required disabled={locked}/></label>
   <LimitFields limits={limits} setLimits={setLimits} disabled={locked}/>
   <label>Referral team<select name="team" defaultValue={data.routingTeamId??undefined} disabled={locked}>{data.teams.map(t=><option key={t.id} value={t.id}>{t.name}</option>)}</select></label>
   <fieldset disabled={locked}><legend>Grant this authority to staff</legend>{data.users.map(u=><label className="task-checkbox" key={u.id}><input type="checkbox" name="users" value={u.id}/>{u.displayName}</label>)}</fieldset>
   <label>Request reason<textarea name="reason" required maxLength={1000} disabled={locked}/></label><button className="button button-primary" disabled={locked}>Submit authority for approval</button>
  </form>
 </Panel>;
}

function LimitFields({limits,setLimits,disabled}:{limits:Limits;setLimits:(value:Limits)=>void;disabled:boolean}){
 return <fieldset disabled={disabled}><legend>Authority limits</legend>{Object.entries(limits).map(([key,value])=>typeof value==='object'&&!Array.isArray(value)?<LimitFields key={key} limits={value} setLimits={next=>setLimits({...limits,[key]:next})} disabled={disabled}/>:typeof value==='boolean'?<label className="task-checkbox" key={key}><input type="checkbox" checked={value} onChange={e=>setLimits({...limits,[key]:e.target.checked})}/>{label(key)}</label>:<label key={key}>{label(key)}{Array.isArray(value)?<input defaultValue={value.join(', ')} onChange={e=>setLimits({...limits,[key]:e.target.value.split(',').map(x=>Number(x.trim()))})} title="Comma-separated trade reference numbers"/>:<input required type={typeof value==='number'?'number':'text'} inputMode={typeof value==='number'?'numeric':'decimal'} value={value} onChange={e=>setLimits({...limits,[key]:typeof value==='number'?Number(e.target.value):e.target.value})}/>}</label>)}</fieldset>;
}
function LimitSummary({limits}:{limits:Limits}){return <dl>{Object.entries(limits).map(([key,value])=><div key={key}><dt>{label(key)}</dt><dd>{typeof value==='object'&&!Array.isArray(value)?<LimitSummary limits={value}/>:Array.isArray(value)?value.join(', '):typeof value==='boolean'?value?'Permitted':'Not permitted':String(value)}</dd></div>)}</dl>;}
