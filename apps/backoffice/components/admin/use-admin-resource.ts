'use client';
import {useEffect,useRef,useState} from 'react';
import {AdminError,adminRead,adminSend,type AdminIntent} from '../../lib/admin-api';

export function useAdminResource<T>(path:string){
 const [data,setData]=useState<T>(),[error,setError]=useState(''),[notice,setNotice]=useState(''),[revision,setRevision]=useState(0),[busy,setBusy]=useState(false),[retry,setRetry]=useState<AdminIntent>();
 const lock=useRef(false);
 useEffect(()=>{const c=new AbortController();adminRead<T>(path,c.signal).then(value=>{if(!c.signal.aborted)setData(value);},e=>{if(!c.signal.aborted)setError(e.message);});return()=>c.abort();},[path,revision]);
 function reload(){setError('');setRevision(x=>x+1);}
 async function send(intent:AdminIntent){if(lock.current)return;lock.current=true;setBusy(true);setError('');try{await adminSend(intent);setRetry(undefined);setNotice('Saved. The list shows the latest stored records.');reload();}catch(e){setError(e instanceof Error?e.message:'Unable to confirm this change.');setRetry(e instanceof AdminError&&e.status<500?undefined:intent);}finally{lock.current=false;setBusy(false);}}
 return {data,error,notice,busy,locked:busy||!!retry,retry,reload,send};
}
