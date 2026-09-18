import {quoteFetch,validQuoteEtag} from './quotes.ts';

export type RenewalTerm={kind:'annual'|'short-period';startsAt:string;endsAt:string;timeZone:'Europe/London'};
export type RenewalPreview={policyId:string;expiringTermId:string;baseVersionId:string;termEtag:string;term:RenewalTerm;
  termIntent:{kind:'annual'|'short-period';localStartDate:string;localStartTime:string;timeZone:'Europe/London';utcOffsetMinutes:0|60};
  productVersionId:string;binderVersionId:string;agencyTermsVersionId:string;ruleSettingVersionId:string;ruleVersion:string;
  fairValueAssessmentId:string|null;fairValueEvidenceFileId:string|null;fairValueSatisfied:boolean;fairValueState:string;brokerArrearsState:'unavailable'};
export type RenewalWorkspace={draftId:string;draftEtag:string;assessedAt:string;current:boolean;blockers:string[];allowedTermMonths:number[];
  defaultTermMonths:number|null;expiringAnnualPremium:string;expiringStartsAt:string;expiringEndsAt:string;eligibility:RenewalPreview|null;
  preparation:null|{id:string;sequence:number;termMonths:number;term:RenewalTerm;productVersionId:string;binderVersionId:string;
    agencyTermsVersionId:string;ruleSettingVersionId:string;fairValueAssessmentId:string|null;preparedAt:string}};
export type RenewalExperienceFacts={observationStartsOn:string;observationEndsOn:string;claimCount:number;paid:string;outstanding:string;
  earnedPremium:string;sourceCode:'insured'|'agency'|'administrator';sourceReference:string;evidenceAssociationId:string};
export type RenewalExperienceView={draftId:string;evidenceFileId:string|null;
  experience:null|RenewalExperienceFacts&{id:string;sequence:number;recordedAt:string;recordedBy:string};
  review:null|{id:string;experienceVersionId:string;outcome:'accepted'|'rejected';reason:string;authorityVersionId:string;
    authorityGrantId:string;recordedAt:string;recordedBy:string}};
export type RenewalCommand=Readonly<{scope:Readonly<{draftId:string;etag:string;fence:string}>;url:string;method:'POST'|'PUT';key:string;body?:string;file?:File}>;
export type RenewalReceipt={draftId:string;resourceId:string;kind:'renewal';updatedAt:string};
const id=(value:string)=>/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)&&value!=='00000000-0000-0000-0000-000000000000';

export function renewalCommand(scope:RenewalCommand['scope'],path:string,body?:unknown,file?:File):RenewalCommand {
  if(!id(scope.draftId)||!id(scope.fence)||!validQuoteEtag(scope.etag))throw new Error('Refresh the draft and acquire its editing lease.');
  if(!/^\/(preparation|experience|experience\/uploads|experience\/[0-9a-f-]{36}\/reviews)$/i.test(path))throw new Error('Invalid renewal action.');
  if(path.endsWith('/reviews')&&!id(path.split('/')[2]))throw new Error('Choose the saved experience version.');
  if(path==='/experience/uploads') {
    if(body!==undefined||!file||!file.size||file.size>10*1024*1024||!['application/pdf','image/png','image/jpeg','text/plain'].includes(file.type))
      throw new Error('Choose a PDF, PNG, JPEG or text file up to 10 MiB.');
  } else if(file||body===null||typeof body!=='object'||Array.isArray(body))throw new Error('Complete the renewal form before saving.');
  return Object.freeze({scope:Object.freeze({...scope}),url:`/api/v1/drafts/${scope.draftId}/renewal${path}`,
    method:path==='/experience'?'PUT':'POST',key:crypto.randomUUID(),body:body===undefined?undefined:JSON.stringify(body),file});
}
export function confirmRenewalReceipt(command:RenewalCommand,data:RenewalReceipt,etag:string|null) {
  if(!data||data.draftId!==command.scope.draftId||!id(data.resourceId)||data.kind!=='renewal'||!Number.isFinite(Date.parse(data.updatedAt))||!validQuoteEtag(etag))
    throw new Error('The saved renewal result is unconfirmed. Retry the same action.');
  return data;
}
export async function sendRenewal(command:RenewalCommand) {
  const {data:csrf}=await quoteFetch<{requestToken:string}>('/api/v1/auth/csrf');
  let body:BodyInit|undefined=command.body;
  if(command.file) {const form=new FormData();form.set('file',command.file,command.file.name);form.set('fileName',command.file.name);form.set('contentType',command.file.type);body=form;}
  const result=await quoteFetch<RenewalReceipt>(command.url,{method:command.method,body,headers:{
    ...(command.file?{}:{'Content-Type':'application/json'}),'X-CSRF-Token':csrf.requestToken,'Idempotency-Key':command.key,
    'If-Match':command.scope.etag,'X-Edit-Lease':command.scope.fence}});
  return {data:confirmRenewalReceipt(command,result.data,result.etag),etag:result.etag!};
}

// Display only. The server's pinned rating and referral remain authoritative.
export function renewalExperienceDisplay(view:Pick<RenewalExperienceView,'experience'|'review'>,current:boolean):{label:string;ratio:number|null} {
  if(!view.experience)return {label:'Renewal experience has not been supplied.',ratio:null};
  if(!current)return {label:'Current experience checks are unconfirmed.',ratio:null};
  if(view.review?.experienceVersionId!==view.experience.id||view.review.outcome!=='accepted')
    return {label:'Supplied experience needs underwriting review.',ratio:null};
  const earned=Number(view.experience.earnedPremium),paid=Number(view.experience.paid),outstanding=Number(view.experience.outstanding);
  if(!Number.isFinite(earned)||!Number.isFinite(paid)||!Number.isFinite(outstanding)||earned<=0||paid<0||outstanding<0)
    return {label:'Loss ratio is unresolved: positive earned premium is required.',ratio:null};
  const ratio=(paid+outstanding)/earned;
  return {label:`${(ratio*100).toLocaleString('en-GB',{maximumFractionDigits:2})}% recorded loss ratio`,ratio};
}
