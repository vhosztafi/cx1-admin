import {quoteFetch,validQuoteEtag} from './quotes.ts';
import type {ProofCommand} from './servicing-proof.ts';

export type ServicingSubmission = {id:string;cycleId:string;revisionId:string;ratingId:string;inputHash:string;reason:string;submittedBy:string;submittedAt:string;applicable:boolean};
export type ServicingSubmissionPage = {draftId:string;draftEtag:string;assessedAt:string;currentCycleId:string|null;current:ServicingSubmission|null;items:ServicingSubmission[];nextCursor:string|null};
const id=(value:unknown)=>typeof value==='string'&&/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)&&value!=='00000000-0000-0000-0000-000000000000';

export function recordedSubmission(command:ProofCommand,page:ServicingSubmissionPage):ServicingSubmission|null {
 if(command.url!==`/api/v1/drafts/${command.scope.draftId}/submit` || page.draftId!==command.scope.draftId || !validQuoteEtag(page.draftEtag) || !Array.isArray(page.items))
  throw new Error('Submission readback could not be confirmed.');
 const item=[page.current,...page.items].find(row=>row?.cycleId===command.scope.cycleId&&row.revisionId===command.scope.revisionId);
 if(!item)return null;
 if(![item.id,item.cycleId,item.revisionId,item.ratingId,item.submittedBy].every(id) || !/^[a-f0-9]{64}$/.test(item.inputHash) ||
  !Number.isFinite(Date.parse(item.submittedAt)) || typeof item.reason!=='string' || item.reason.trim().length<10 || typeof item.applicable!=='boolean')
  throw new Error('Submission readback could not be confirmed.');
 // A unique cycle handoff may have been recorded by another editor. Confirm
 // the persisted handoff, never attribute it to this command or grant approval.
 return item;
}

export async function recoverSubmission(command:ProofCommand):Promise<ServicingSubmission|null> {
 let cursor='';const visited=new Set<string>();let version:string|undefined;
 for(let pageNumber=0;pageNumber<10;pageNumber++) {
  const {data}=await quoteFetch<ServicingSubmissionPage>(`/api/v1/drafts/${command.scope.draftId}/submissions?pageSize=50${cursor?'&cursor='+encodeURIComponent(cursor):''}`);
  if(version && data.draftEtag!==version)throw new Error('Submission history changed; check the saved result again.');
  version=data.draftEtag;const found=recordedSubmission(command,data);if(found)return found;
  if(data.nextCursor===null)return null;
  if(typeof data.nextCursor!=='string' || !data.nextCursor || visited.has(data.nextCursor))throw new Error('Submission history could not be confirmed.');
  cursor=data.nextCursor;visited.add(cursor);
 }
 throw new Error('Submission history is incomplete; retain the original action and check again.');
}
