'use client';
import {useState} from 'react';
import {Panel} from '../primitives';
import {adminIntent,AdminError} from '../../lib/admin-api';
import {csrfToken} from '../../lib/auth';
import {useAdminResource} from './use-admin-resource';
import type {AdministrationSetting as Setting,AdministrationTemplate as Template,ConfigurationAdministrationView as Data} from '../../../../contracts/generated/administration';

type Values=Record<string,unknown>;
const name=(s:string)=>s.replace('workflow-task/','').replace('flag-definition/','').replaceAll('-',' ').replace(/^./,c=>c.toUpperCase());
type Resource=ReturnType<typeof useAdminResource<Data>>;

export function ConfigurationAdministration({tab}:{tab:string}){
 const r=useAdminResource<Data>('/admin/configuration');
 const scopes=(x:Setting)=>tab==='workflow'?x.scope.startsWith('workflow-task/'):tab==='matching'?x.scope==='matching-rule':tab==='flags'?x.scope.startsWith('flag-definition/'):tab==='templates'?x.scope.startsWith('message-template/'):x.scope==='organisation';
 return <div className="operations-stack"><div aria-live="polite">{r.notice&&<p className="notice">{r.notice}</p>}{r.error&&<p role="alert">{r.error}</p>}{r.retry&&<button className="button" disabled={r.busy} onClick={()=>void r.send(r.retry!)}>Retry same action</button>}</div>
  <Panel title="Published configuration" note="Changes apply to future work from their effective time. Existing records retain their saved configuration."><button className="button" disabled={r.locked} onClick={r.reload}>Reload configuration</button>{!r.data&&<p role="status">Loading configuration…</p>}
   {r.data?.settings.filter(scopes).map(row=><details key={row.scope+row.etag}><summary>{name(row.scope)} · version {row.version||'default'}</summary><p>Effective: {row.effectiveFrom?new Date(row.effectiveFrom).toLocaleString():'Built-in default'}</p><SettingForm row={row} r={r}/></details>)}
   {tab==='workflow'&&r.data&&!r.data.settings.some(scopes)&&<p>No published workflow definitions are configured.</p>}
  </Panel>
  {tab==='templates'&&r.data&&<Panel title="Document templates" note="Publish a successor or download a fictional quote preview of the edited title and notice. Preview creates no customer document or delivery.">{r.data.templates.map(row=><details key={row.id}><summary>{r.data!.products.find(x=>x.id===row.productId)?.name} · {name(row.code)} · version {row.version}</summary><TemplateForm row={row} r={r}/></details>)}</Panel>}
 </div>;
}

function SettingForm({row,r}:{row:Setting;r:Resource}){
 const [v,setV]=useState<Values>(row.values);
 const set=(k:string,value:unknown)=>setV(x=>({...x,[k]:value}));
 const text=(key:string,label:string,max=200)=><label>{label}<input required maxLength={max} value={String(v[key]??'')} onChange={e=>set(key,e.target.value)}/></label>;
 const check=(key:string,label:string)=><label className="task-checkbox"><input type="checkbox" checked={Boolean(v[key])} onChange={e=>set(key,e.target.checked)}/>{label}</label>;
 const num=(key:string,label:string,min:number,max:number)=><label>{label}<input required type="number" min={min} max={max} value={Number(v[key])} onChange={e=>set(key,Number(e.target.value))}/></label>;
 const select=(key:string,label:string,options:string[])=><label>{label}<select value={String(v[key])} onChange={e=>set(key,e.target.value)}>{options.map(x=><option key={x} value={x}>{name(x)}</option>)}</select></label>;
 return <form className="task-form" onSubmit={e=>{e.preventDefault();const f=new FormData(e.currentTarget);void r.send(adminIntent('/admin/configuration','POST',{scope:row.scope,values:v,effectiveFrom:f.get('effective')?new Date(String(f.get('effective'))).toISOString():new Date().toISOString(),reason:f.get('reason')},row.etag));}}><fieldset disabled={r.locked}><legend>{name(row.scope)}</legend>
  {row.scope==='organisation'?<>{text('name','Organisation name')}{text('clientReferencePrefix','New client reference prefix',8)}{check('notificationsEnabled','Allow new agency message deliveries')}<label>Signature added when inserting a message template<textarea maxLength={1000} value={String(v.notificationSignature)} onChange={e=>set('notificationSignature',e.target.value)}/></label></>:
   row.scope==='matching-rule'?<>{select('duplicateQuotePolicy','Duplicate quote policy',['refer','allow-competing','broker-of-record'])}{check('requireReview','Require matching review')}{text('summary','Rule description',1000)}</>:
   row.scope.startsWith('flag-definition/')?<>{check('enabled','Allow this flag on new declarations and amendments')}{num('maximumReviewDays','Maximum days until review',1,3650)}{check('agencySharingAllowed','Allow sharing with agency relationships')}</>:
   row.scope.startsWith('message-template/')?<>{text('name','Template name')}{check('enabled','Offer in message composer')}<label>Message body<textarea required maxLength={7000} rows={6} value={String(v.body)} onChange={e=>set('body',e.target.value)}/></label><p>Use {'{{organisation}}'} for the configured organisation name. Plain text only.</p></>:
   <>{text('title','Task title')}{select('priority','Priority',['low','normal','high','urgent'])}{select('initialState','Initial state',['open','awaiting-information'])}{num('dueDays','Due after days',0,365)}{num('leadDays','Lead days',0,365)}<label>Assignment team<select value={String(v.assignmentTeamId??'')} onChange={e=>set('assignmentTeamId',e.target.value||null)}><option value="">Unassigned</option>{r.data!.teams.map(x=><option key={x.id} value={x.id}>{x.name}</option>)}</select></label><p>Workflow family: {name(String(v.family))}. Task type: {name(String(v.taskType))}.</p>
    {(v.checklist as {code:string;label:string;required:boolean}[]).map((item,index)=><div key={item.code}><label>Checklist item {index+1}<input required maxLength={300} value={item.label} onChange={e=>set('checklist',(v.checklist as typeof item[]).map((x,i)=>i===index?{...x,label:e.target.value}:x))}/></label><label className="task-checkbox"><input type="checkbox" checked={item.required} onChange={e=>set('checklist',(v.checklist as typeof item[]).map((x,i)=>i===index?{...x,required:e.target.checked}:x))}/>Required</label></div>)}
   </>}
  <label>Effective time (local; leave empty for now)<input type="datetime-local" name="effective"/></label><label>Change reason<textarea name="reason" required maxLength={1000}/></label><button className="button button-primary">Publish configuration</button>
 </fieldset></form>;
}
function TemplateForm({row,r}:{row:Template;r:Resource}){
 const [title,setTitle]=useState(row.values.title),[notice,setNotice]=useState(row.values.notice),[from,setFrom]=useState(''),[to,setTo]=useState(row.effectiveTo.slice(0,10)),[error,setError]=useState(''),[busy,setBusy]=useState(false);
 const body=(reason:string)=>({title,notice,effectiveFrom:from?new Date(from).toISOString():new Date().toISOString(),effectiveTo:to+'T00:00:00Z',reason});
 async function preview(){setBusy(true);setError('');try{const response=await fetch(`/api/v1/admin/templates/${row.id}/preview`,{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-Token':await csrfToken()},body:JSON.stringify(body('Fictional preview'))});if(!response.ok)throw new AdminError(response.status,(await response.json()).code);const url=URL.createObjectURL(await response.blob());const a=document.createElement('a');a.href=url;a.download='fictional-template-preview.pdf';a.click();setTimeout(()=>URL.revokeObjectURL(url),30000);}catch(e){setError((e as Error).message);}finally{setBusy(false);}}
 return <form className="task-form" onSubmit={e=>{e.preventDefault();void r.send(adminIntent(`/admin/templates/${row.id}/successor`,'POST',body(String(new FormData(e.currentTarget).get('reason'))),row.etag));}}><fieldset disabled={r.locked||busy}><legend>Template successor</legend><label>Document title<input required maxLength={200} value={title} onChange={e=>setTitle(e.target.value)}/></label><label>Notice<textarea required maxLength={4000} rows={5} value={notice} onChange={e=>setNotice(e.target.value)}/></label><label>Effective time (local; leave empty for now)<input type="datetime-local" value={from} onChange={e=>setFrom(e.target.value)}/></label><label>Effective to (exclusive)<input required type="date" value={to} onChange={e=>setTo(e.target.value)}/></label><button className="button" type="button" onClick={()=>void preview()}>Download fictional preview</button><label>Change reason<textarea name="reason" required maxLength={1000}/></label><button className="button button-primary">Publish template successor</button></fieldset>{error&&<p role="alert">{error}</p>}</form>;
}
