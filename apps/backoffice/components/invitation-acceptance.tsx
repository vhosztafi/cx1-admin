'use client';
import {useState} from 'react';
import {csrfToken} from '../lib/auth';
export function InternalInvitation(){
 const [notice,setNotice]=useState(''),[busy,setBusy]=useState(false);
 return <details><summary>Accept an internal invitation</summary><form className="sign-in-form" onSubmit={async e=>{e.preventDefault();const form=e.currentTarget,f=new FormData(form);setBusy(true);setNotice('');try{const response=await fetch('/api/v1/auth/internal-invitation',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-Token':await csrfToken()},body:JSON.stringify({token:f.get('token'),password:f.get('password')})});setNotice(response.ok?'Invitation accepted. Sign in with your new password.':'Invitation unavailable or password invalid. Use 12 to 128 characters.');if(response.ok)form.reset();}catch{setNotice('Unable to confirm acceptance. Try signing in before retrying.');}finally{setBusy(false);}}}><label>Invitation token<input name="token" required autoComplete="off" maxLength={43}/></label><label>New password<input name="password" required type="password" autoComplete="new-password" minLength={12} maxLength={128}/></label><button className="button" disabled={busy}>Accept invitation</button><p role="status">{notice}</p></form></details>;
}
