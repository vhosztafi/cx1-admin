'use client';
import {useEffect,useRef,useState} from 'react';
import {Panel,DataTable} from '../primitives';
import {csrfToken} from '../../lib/auth';
import {agencyFetch,AgencyError,type AgencyDraft} from '../../lib/agencies';
import {brokerRoles,userInput,userReason,invitationActions,type AgencyUser,type Invitation} from '../../lib/agency-users';
import {clientDate,type Page} from '../../lib/clients';
import {LoadFeedback,Paging,useAgencyResource} from './shared';

type Action='create'|'edit'|'deactivate'|'reactivate'|'resend'|'revoke';
type Selection={action:Action;recordId?:string;etag:string};
const actionLabels:Record<Action,string>={create:'Invite user',edit:'Edit user',deactivate:'Deactivate user',reactivate:'Reactivate user',resend:'Resend invitation',revoke:'Revoke invitation'};
export function AgencyUsers({id,agencyState,onSaved,disabled=false,onGuardChange}: {id:string;agencyState:string;onSaved?:()=>void|Promise<void>;disabled?:boolean;onGuardChange?:(guarded:boolean)=>void}) {
  const [cursors,setCursors]=useState<string[]>([]);const [revision,setRevision]=useState(0);
  const users=useAgencyResource<Page<AgencyUser>>(`/api/v1/agencies/${id}/users?pageSize=10${cursors.at(-1)?`&cursor=${encodeURIComponent(cursors.at(-1)!)}`:''}`);
  const [history,setHistory]=useState<AgencyUser>();const [selected,setSelected]=useState<Selection>();
  const [name,setName]=useState('');const [email,setEmail]=useState('');const [role,setRole]=useState('broker-user');const [reason,setReason]=useState('');
  const [busy,setBusy]=useState(false);const [uncertain,setUncertain]=useState(false);const [stale,setStale]=useState(false);const [error,setError]=useState('');const [notice,setNotice]=useState('');
  const dialog=useRef<HTMLDialogElement>(null);const focusInput=useRef<HTMLInputElement>(null);const returnFocus=useRef<HTMLElement|null>(null);const locked=useRef(false);
  const refreshButton=useRef<HTMLButtonElement>(null);
  const command=useRef<{url:string;method:string;body:string;etag:string;key:string}|undefined>(undefined);
  useEffect(()=>{if(selected){dialog.current?.showModal();focusInput.current?.focus();}},[selected]);
  useEffect(()=>{if(!selected)return;const unload=(event:BeforeUnloadEvent)=>event.preventDefault();window.addEventListener('beforeunload',unload);return()=>window.removeEventListener('beforeunload',unload);},[selected]);
  useEffect(()=>{onGuardChange?.(busy||!!selected);},[busy,selected,onGuardChange]);
  function close(){if(locked.current||uncertain)return;dialog.current?.close();setSelected(undefined);setError('');setStale(false);command.current=undefined;requestAnimationFrame(()=>returnFocus.current?.focus());}
  async function begin(action:Action,row?:AgencyUser|Invitation) {
    if(disabled||locked.current||selected)return;returnFocus.current=document.activeElement as HTMLElement;locked.current=true;setBusy(true);setError('');setNotice('');setReason('');setUncertain(false);setStale(false);
    try {
      const snapshot=action==='create'?await agencyFetch<AgencyDraft>(`/api/v1/agencies/${id}`):await agencyFetch<AgencyUser|Invitation>(`/api/v1/agencies/${id}/${['resend','revoke'].includes(action)?'invitations':'users'}/${row!.id}`);
      if(action==='create'){setName('');setEmail('');setRole('broker-user');}else if(action==='edit'){const user=snapshot.data as AgencyUser;setName(user.displayName);setEmail(user.email);setRole(user.role);}
      setSelected({action,recordId:row?.id,etag:snapshot.etag!});
    }catch{setError('Unable to load the current user or invitation. Refresh and try again.');}finally{locked.current=false;setBusy(false);}
  }
  async function reload() {
    if(!selected||busy||uncertain)return;setBusy(true);
    try{const path=selected.action==='create'?`/api/v1/agencies/${id}`:`/api/v1/agencies/${id}/${['resend','revoke'].includes(selected.action)?'invitations':'users'}/${selected.recordId}`;const fresh=await agencyFetch(path);setSelected({...selected,etag:fresh.etag!});setStale(false);setError('Current version loaded. Check your retained entries before saving.');users.refresh();}
    catch{setError('Unable to reload. Your entries are retained.');}finally{setBusy(false);}
  }
  async function submit() {
    if(locked.current||!selected||stale)return;
    if(!uncertain){try{const action=selected.action;const body=['create','edit'].includes(action)?userInput(name,email,role,action==='create',reason):{reason:userReason(reason)};const path=action==='create'?`/api/v1/agencies/${id}/invitations`:['resend','revoke'].includes(action)?`/api/v1/invitations/${selected.recordId}/${action}`:`/api/v1/agencies/${id}/users/${selected.recordId}${action==='edit'?'':'/'+action}`;command.current={url:path,method:action==='edit'?'PUT':'POST',body:JSON.stringify(body),etag:selected.etag,key:crypto.randomUUID()};}catch(failure){setError((failure as Error).message);return;}}
    const pending=command.current!;locked.current=true;setBusy(true);setError('');
    try{await agencyFetch(pending.url,{method:pending.method,headers:{'Content-Type':'application/json','X-CSRF-Token':await csrfToken(),'If-Match':pending.etag,'Idempotency-Key':pending.key},body:pending.body});
      dialog.current?.close();setSelected(undefined);setUncertain(false);command.current=undefined;setNotice(selected.action==='create'?(agencyState==='draft'?'User staged. No invitation token or delivery was created.':'Invitation issued and queued for demo delivery. Acceptance is still pending.'):'Change saved. User and invitation history refreshed.');if(selected.action==='edit'&&history&&history.id===selected.recordId)setHistory({...history,displayName:name.trim()});users.refresh();setRevision(x=>x+1);await onSaved?.();requestAnimationFrame(()=>{if(returnFocus.current?.isConnected)returnFocus.current.focus();else refreshButton.current?.focus();});
    }catch(failure){if(!(failure instanceof AgencyError)||failure.status>=500){setUncertain(true);setError('The result is uncertain. Retry the same request to recover it.');}else{setUncertain(false);command.current=undefined;setStale([409,412,428].includes(failure.status));setError([409,412,428].includes(failure.status)?'This record changed or the action is no longer allowed. Your entries are retained. Reload before retrying; the last active administrator must be kept.':failure.message);}}
    finally{locked.current=false;setBusy(false);}
  }
  return <Panel title="Agency users"><p className="match-copy">{agencyState==='draft'?'Stage users during onboarding. Invitations are issued only when the agency is activated.':'Manage saved users and their invitation history.'}</p>
    {disabled&&<p className="match-copy">Save or resolve the onboarding draft before changing users.</p>}
    <div className="operations-actions"><button className="button" disabled={disabled||busy||!!selected||!['draft','active'].includes(agencyState)} onClick={()=>void begin('create')}>Invite user</button><button ref={refreshButton} className="button secondary" disabled={disabled||busy||!!selected} onClick={()=>{users.refresh();setRevision(x=>x+1);}}>Refresh users</button></div>
    {!users.data?<LoadFeedback error={users.error} retry={users.refresh}/>:<><DataTable caption="Agency users" columns={['Name','Email','Role','Status','Last active','Actions']}>
      {users.data.items.map(row=><tr key={row.id}><td>{row.displayName}</td><td>{row.email}</td><td>{brokerRoles[row.role]}</td><td>{row.state==='invited'?'Invited':row.state==='active'?'Active':'Inactive'}</td><td>{row.lastSeenAt?clientDate(row.lastSeenAt,true):'Not recorded'}</td><td><div className="operations-actions"><button className="button secondary" disabled={disabled||busy||!!selected||agencyState==='abandoned'} onClick={()=>void begin('edit',row)}>Edit user</button><button className="button secondary" disabled={disabled||busy||!!selected||agencyState==='abandoned'||row.state==='inactive'&&agencyState==='suspended'} onClick={()=>void begin(row.state==='inactive'?'reactivate':'deactivate',row)}>{row.state==='inactive'?'Reactivate':'Deactivate'}</button><button className="button secondary" disabled={disabled||busy||!!selected} onClick={()=>setHistory(row)}>Invitation history</button></div></td></tr>)}
    </DataTable>{!users.data.items.length&&<p className="match-copy">No users have been added to this agency.</p>}<Paging total={users.data.totalCount} previous={!selected&&cursors.length?()=>setCursors(x=>x.slice(0,-1)):undefined} next={!selected&&users.data.nextCursor?()=>setCursors(x=>[...x,users.data!.nextCursor!]):undefined}/></>}
    {history&&<InvitationHistory key={`${history.id}:${revision}`} id={id} user={history} agencyState={agencyState} disabled={disabled||busy||!!selected} onAction={(action,row)=>void begin(action,row)}/>}
    <dialog ref={dialog} className="agency-dialog" aria-labelledby="agency-user-dialog-title" onCancel={event=>{event.preventDefault();close();}}><form noValidate onSubmit={event=>{event.preventDefault();void submit();}}><h2 id="agency-user-dialog-title">{selected?actionLabels[selected.action]:''}</h2><p className="match-copy">{selected?.action==='create'?(agencyState==='draft'?'This user will be staged without delivery.':'A new invitation will be queued for demo delivery.'):selected?.action==='edit'?'Email and agency cannot be changed. Role changes end existing sessions.':selected?.action==='deactivate'?'Existing sessions and open invitations will be revoked. The last active administrator cannot be removed.':selected?.action==='reactivate'?'Existing sessions and old invitations stay revoked. Users without credentials receive a fresh invitation.':'Invitation history is retained. Resending invalidates the previous link.'}</p>
      {selected&&['create','edit'].includes(selected.action)?<><label>Name<input ref={focusInput} required maxLength={200} value={name} disabled={busy||uncertain} onChange={event=>setName(event.target.value)}/></label><label>Email<input type="email" required maxLength={254} value={email} disabled={busy||uncertain||selected.action==='edit'} onChange={event=>setEmail(event.target.value)}/></label><label>Role<select aria-label="Role" value={role} disabled={busy||uncertain} onChange={event=>setRole(event.target.value)}>{Object.entries(brokerRoles).map(([value,label])=><option key={value} value={value}>{label}</option>)}</select></label></>:null}
      {selected?.action!=='create'&&<label>Reason<input ref={selected?.action==='edit'?undefined:focusInput} required maxLength={1000} value={reason} disabled={busy||uncertain} onChange={event=>setReason(event.target.value)}/></label>}
      {error&&<p role="alert" className="error-message">{error}</p>}<div className="operations-actions"><button className="button" disabled={busy||stale}>{busy?'Saving…':uncertain?'Retry same request':'Save change'}</button>{stale&&<button className="button secondary" type="button" disabled={busy||uncertain} onClick={()=>void reload()}>Reload current record</button>}<button className="button secondary" type="button" disabled={busy||uncertain} onClick={close}>Cancel</button></div>
    </form></dialog>{!selected&&error&&<p role="alert" className="error-message">{error}</p>}<p role="status" className="match-copy">{notice}</p>
  </Panel>;
}

function InvitationHistory({id,user,agencyState,disabled,onAction}:{id:string;user:AgencyUser;agencyState:string;disabled:boolean;onAction:(action:'resend'|'revoke',row:Invitation)=>void}) {
  const [cursors,setCursors]=useState<string[]>([]);const history=useAgencyResource<Page<Invitation>>(`/api/v1/agencies/${id}/invitations?userId=${user.id}&pageSize=10${cursors.at(-1)?`&cursor=${encodeURIComponent(cursors.at(-1)!)}`:''}`);
  const latest=useAgencyResource<Page<Invitation>>(`/api/v1/agencies/${id}/invitations?userId=${user.id}&pageSize=1`);
  const currentUser=useAgencyResource<AgencyUser>(`/api/v1/agencies/${id}/users/${user.id}`);
  return <section aria-label={`Invitations for ${user.displayName}`}><h3>Invitations for {user.displayName}</h3><p className="match-copy">Acceptance and delivery are separate. Revoked and replaced invitations remain in history.</p>{!history.data?<LoadFeedback error={history.error} retry={history.refresh}/>:<><DataTable caption="Invitation history" columns={['Created','Acceptance','Expires','Delivery','Actions']}>{history.data.items.map(row=>{const allowed=invitationActions(row,agencyState);const current=latest.data?.items[0]?.id===row.id&&currentUser.data?.state==='invited';return <tr key={row.id}><td>{clientDate(row.createdAt,true)}</td><td>{row.state}</td><td>{row.expiresAt?clientDate(row.expiresAt,true):'Not issued'}</td><td>{row.notificationId?<Delivery id={id} notificationId={row.notificationId}/>: 'No delivery'}</td><td><div className="operations-actions">{current&&allowed.resend&&<button className="button secondary" disabled={disabled} onClick={()=>onAction('resend',row)}>Resend</button>}{current&&allowed.revoke&&<button className="button secondary" disabled={disabled} onClick={()=>onAction('revoke',row)}>Revoke</button>}{current&&allowed.reveal&&<DemoInvitationLink invitationId={row.id} disabled={disabled}/>}</div></td></tr>;})}</DataTable><Paging total={history.data.totalCount} previous={!disabled&&cursors.length?()=>setCursors(x=>x.slice(0,-1)):undefined} next={!disabled&&history.data.nextCursor?()=>setCursors(x=>[...x,history.data!.nextCursor!]):undefined}/></>}</section>;
}
function Delivery({id,notificationId}:{id:string;notificationId:string}) {const data=useAgencyResource<{state:string}>(`/api/v1/agencies/${id}/notifications/${notificationId}`);return <span>{data.data?.state.replaceAll('-',' ')??(data.error?'Delivery unavailable':'Loading delivery…')}</span>;}



function DemoInvitationLink({invitationId,disabled}:{invitationId:string;disabled:boolean}) {
  const [busy,setBusy]=useState(false);const [error,setError]=useState('');const lock=useRef(false);
  async function open(){if(lock.current)return;const preview=window.open('about:blank','_blank');if(!preview){setError('Allow a new tab to open the demo invitation.');return;}preview.opener=null;lock.current=true;setBusy(true);setError('');
    try{const result=await agencyFetch<{invitationToken:string}>(`/api/v1/invitations/${invitationId}/demo-link`,{method:'POST',headers:{'X-CSRF-Token':await csrfToken()}});if(!/^[A-Za-z0-9_-]{43}$/.test(result.data.invitationToken))throw Error();preview.location.replace(`/invitations/accept#${result.data.invitationToken}`);}
    catch{preview.close();setError('Demo link unavailable. Refresh the invitation; links require local demo mode and current access.');}finally{lock.current=false;setBusy(false);}}
  return <><button className="button secondary" disabled={disabled||busy} onClick={()=>void open()}>{busy?'Opening…':'Open demo invitation'}</button>{error&&<p role="alert" className="form-error">{error}</p>}</>;
}
