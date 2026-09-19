import {quoteFetch, validQuoteEtag} from './quotes.ts';

export function formatCancellationMoney(value:string):string {
  if(!/^-?(0|[1-9][0-9]{0,12})\.[0-9]{2}$/.test(value))return 'Unavailable';
  const negative=value.startsWith('-'),[whole,fraction]=(negative?value.slice(1):value).split('.');
  return `${negative?'−':''}£${whole.replace(/\B(?=(\d{3})+(?!\d))/g,',')}.${fraction}`;
}

export const cancellationReasons = [
  ['insured-request', 'Insured request'], ['non-payment', 'Non-payment'], ['non-disclosure', 'Non-disclosure'],
  ['trade-ceased', 'Trade ceased'], ['insurer-instruction', 'Insurer instruction'],
] as const;
export type CancellationReason = typeof cancellationReasons[number][0];
export type CancellationView = {
  draftId:string; revisionId:string; baseVersionId:string; draftEtag:string; previewId:string|null; approvalId:string|null;
  previewHash:string; ruleVersion:string; reasonCode:string; effectiveAt:string; supportedFrom:string; noticeEffectiveFrom:string|null;
  blockers:string[]; canApprove:boolean; amounts:null|{retainedFee:string; retainedFeeShare:string; posting:{
    premium:string; tax:string; fee:string; commission:string; feeShare:string; grossDue:string; invoiceDue:string; netDue:string;
    brokerPayable:string; debtorKind:string; settlement:string; movements:{code:string;ordinal:number;amount:string;startsAt:string;endsAt:string;originalComponentId:string}[];
  }};
  daysOnCover:number; termDays:number; premiumChargedToDate:string|null;
};
export type CancellationEvidencePage = {draftId:string;draftEtag:string;items:{id:string;revisionId:string;fileId:string;fileName:string;purpose:string;
  noticeDeliveredAt:string|null;reviewId:string|null;reviewState:string;reviewReason:string|null}[]};
export type CancellationCommand = Readonly<{draftId:string;url:string;etag:string;fence:string;key:string;body?:string;file?:File;purpose?:string;delivered?:string}>;
const validId=(value:string)=>/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)&&value!=='00000000-0000-0000-0000-000000000000';
export function cancellationCommand(draftId:string, etag:string, fence:string, path:string, body?:unknown,
  upload?:{file:File;purpose:string;delivered?:string}):CancellationCommand {
  if(!validQuoteEtag(etag)||!validId(fence)||!validId(draftId))throw new Error('Acquire the current editing lease before continuing.');
  if(!['cancellation-preview','cancellation-approvals','cancellation-evidence/uploads'].includes(path)&&!/^cancellation-evidence\/[0-9a-f-]{36}\/reviews$/i.test(path))throw new Error('Unsupported cancellation action.');
  if(upload&&(!upload.file.size||upload.file.size>10*1024*1024||!['application/pdf','image/png','image/jpeg','text/plain'].includes(upload.file.type)))throw new Error('Choose a PDF, PNG, JPEG or text file up to 10 MiB.');
  return Object.freeze({draftId,url:`/api/v1/drafts/${draftId}/${path}`,etag,fence,key:crypto.randomUUID(),body:body===undefined?undefined:JSON.stringify(body),
    file:upload?.file,purpose:upload?.purpose,delivered:upload?.delivered});
}
export async function sendCancellation(command:CancellationCommand) {
  const {data:csrf}=await quoteFetch<{requestToken:string}>('/api/v1/auth/csrf');
  let body:string|FormData|undefined=command.body;
  if(command.file){const form=new FormData();form.set('file',command.file);form.set('fileName',command.file.name);form.set('contentType',command.file.type);
    form.set('purpose',command.purpose!);if(command.delivered)form.set('noticeDeliveredAt',command.delivered);body=form;}
  const result=await quoteFetch<{draftId:string;resourceId:string;draftEtag:string}>(command.url,{method:'POST',body,
    headers:{...(!command.file?{'Content-Type':'application/json'}:{}),'X-CSRF-Token':csrf.requestToken,'Idempotency-Key':command.key,
      'If-Match':command.etag,'X-Edit-Lease':command.fence}});
  if(!validQuoteEtag(result.etag)||result.data.draftEtag!==result.etag||result.data.draftId!==command.draftId||!validId(result.data.resourceId))throw new Error('Saved cancellation readback is unconfirmed.');
  return result;
}
export function cancellationBlocker(code:string):string {
  if(code.startsWith('evidence-required:'))return `Accepted ${code.slice(18).replaceAll('-',' ')} evidence is required.`;
  return ({'cancellation-reason-required':'Select and save a cancellation reason.',
    'cancellation-effective-time-invalid':'Choose a valid London effective date and time.',
    'cancellation-notice-delivery-required':'Record actual delivery of the cancellation notice and obtain an accepted review.',
    'notice-period-incomplete':'The effective date must allow the full notice period shown below.',
    'before-latest-issued-slice':'Move the effective date to or after the latest issued change shown below.',
    'later-term-issued':'A later renewal term has already been issued. Cancelling its predecessor is not supported.',
    'outside-term':'The effective date must fall inside this policy term.',
    'cancellation-backdate-authority-required':'A backdated cancellation requires current senior authority.',
    'servicing-base-stale':'The policy has changed. Start a cancellation draft from its latest issued version.',
    'servicing-draft-closed':'This draft is closed. Its review history is retained.',
    'policy-already-cancelled':'This term already has an issued cancellation.',
    'cancellation-risk-changes-forbidden':'Remove proposed risk changes before reviewing cancellation.',
    'cancellation-posted-ledger-required':'The complete posted policy ledger is required.',
    'cancellation-settlement-combination-unsupported':'The original settlement arrangements cannot be combined automatically.',
    'cancellation-return-ledger-unsupported':'The ledger contains a future, previously returned or unsupported component.'} as Record<string,string>)[code]??'Review is unavailable. Refresh and check the policy record.';
}
