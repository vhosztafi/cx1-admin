import { csrfToken } from './auth';
export type {ProductVersion, Provider, Catalogue} from '../../../contracts/generated/administration';
export class AdminError extends Error { constructor(public status:number,public code:string){super(status===412?'This record changed. Reload and review the saved version.':status===403?'Your current access does not permit this action.':status===409?`This change cannot be applied (${code}). Review the saved configuration.`:status===401?'Your session ended. Sign in again.':status===400?'Check the fields and enter a reason.':'The result could not be confirmed. Retry the same action.');} }
export async function adminRead<T>(path:string,signal?:AbortSignal):Promise<T>{
 const response=await fetch('/api/v1'+path,{cache:'no-store',signal:signal??AbortSignal.timeout(15000)});
 if(!response.ok){const body=await response.json().catch(()=>({}));throw new AdminError(response.status,body.code??'read-failed');}return response.json();
}
export type AdminIntent={path:string;method:string;body:string;key:string;etag?:string};
export function adminIntent(path:string,method:string,body:unknown,etag?:string):AdminIntent{return {path,method,body:JSON.stringify(body),key:crypto.randomUUID(),etag};}
export async function adminSend<T>(intent:AdminIntent):Promise<T>{
 const response=await fetch('/api/v1'+intent.path,{method:intent.method,cache:'no-store',signal:AbortSignal.timeout(30000),headers:{'Content-Type':'application/json','X-CSRF-Token':await csrfToken(),'Idempotency-Key':intent.key,...(intent.etag?{'If-Match':intent.etag}:{})},body:intent.body});
 if(!response.ok){const body=await response.json().catch(()=>({}));throw new AdminError(response.status,body.code??'command-failed');}return response.json();
}
