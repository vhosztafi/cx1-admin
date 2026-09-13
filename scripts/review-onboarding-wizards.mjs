import {readFile,writeFile} from 'node:fs/promises';
const inventory=JSON.parse(await readFile('docs/design/control-inventory.json','utf8'));
const file='docs/design/reviewed-api-controls.json';const rows=JSON.parse(await readFile(file,'utf8'));
function put(c,operationIds,reason){const row={controlId:c.id,status:'reviewed',disposition:operationIds.length?'wizard-command':'client-only',operationIds,reason};const index=rows.findIndex(r=>r.controlId===c.id);if(index<0)rows.push(row);else rows[index]=row;}
const agency={
 '0055900cc624':[['saveAgencyDraft','listAgencies'],'Save incomplete onboarding and navigate only on success; preserve edits on conflict.'],
 '9b4b674f0fcc':[['saveAgencyDraft'],'Persist incomplete typed onboarding with current step and ETag.'],
 '6017f06cf201':[['abandonAgencyDraft','listAgencies'],'Abandon only draft state with reason, retain record and evidence, revoke pending invitations.'],
 '1fa0848d9221':[[],'Open a fresh empty onboarding form without changing the activated agency. First persisted save creates a new identity.'],
 '7442850b6ef1':[[],'Open invite-user modal. Its separately reviewed confirm creates the scoped persistent invitation using the deterministic demo delivery adapter.'],
 '182459f3feb7':[['saveAgencyDraft','activateAgency'],'Persist answers then request activation; server checks all compliance, products, terms and authority. Show field-specific missing requirements and keep draft on failure.'],
 be3f8cc4fdae:[[],'Progress style changes local presentation only and does not mark compliance requirements satisfied.']
};
const mfa={
 a0bc1139dfb9:[[],'Authenticator-app choice supplies setup instructions only; all offered compatible apps use the same TOTP protocol.'],
 f42db530136f:[['startMfaEnrolment'],'Device name is optional enrolment metadata; persist at setup start with recent-auth proof.'],
 e17512c0f03a:[['startMfaEnrolment','confirmMfaEnrolment'],'Advance by current stage: start pending enrolment once, then verify six-digit code at verification stage. Never enable MFA based on local UI state.'],
 cd1aa2b17992:[['startMfaEnrolment','confirmMfaEnrolment'],'Reuse current unexpired enrolment on stage changes; confirm TOTP before showing one-time recovery codes.'],
 '37ac8f55741b':[['cancelMfaEnrolment'],'Cancel any pending enrolment; before one exists only close setup. Destroy temporary secret/code hashes and clear local secrets.'],
 '60474f988911':[['cancelMfaEnrolment'],'Cancel pending setup server-side and clear displayed secret/recovery codes; never disable existing active MFA.'],
 ccdd7bd72bdb:[[],'Display static MFA help without sending credentials or modifying enrolment.'],
 '61d9eb5ae433':[[],'Move back within the same unexpired enrolment; do not regenerate secrets or recover already-cleared codes.'],
 f6ee6e414ad3:[['confirmMfaEnrolment'],'Six-digit code is transient secret input; submit to rate-limited enrolment verification, never persist in form history/logs.'],
 bf00dc2dbf93:[[],'Show manual setup key derived from the currently held otpauth URI; never put the secret in analytics, URLs or local storage.'],
 '3117235789fa':[[],'Copy only currently displayed one-time recovery codes with user gesture. Clipboard failure is shown; no server retrieval of plaintext hashes.'],
 '2648097bf60e':[[],'Local acknowledgement gates activation and maps to recoveryCodesSaved=true; toggling alone does not enable MFA.'],
 f593365902af:[['activateMfaEnrolment'],'Activate only verified unexpired enrolment after saved-code acknowledgement. Promote secret/hashes atomically and clear plaintext. Retry reads current account state; it never returns codes again.']
};
for(const c of inventory.controls){
 const key=c.id.slice(4);
 if(c.method==='pNewAgency'){
  if(agency[key])put(c,...agency[key]);
  else if(['() => onJump(n)','() => next(step + 1)','() => next(step - 1)'].includes(c.handlers.onClick))put(c,['saveAgencyDraft'],'Persist dirty incomplete onboarding and bounded step before navigation; on failure keep edits and current step. Visited steps do not satisfy compliance.');
  else if(['f41622633e22','1933ce1acddb','1b1eaa015853'].includes(key))put(c,['replaceAgencyProducts'],'Toggle the selected product-version ID in draft onboarding and save with ETag. Active agencies require independently approved terms changes; labels and row indexes are not identities.');
 }else if(c.method==='faWizard'&&mfa[key])put(c,...mfa[key]);
}
await writeFile(file,JSON.stringify(rows,null,2)+'\n');console.log(`${rows.length} controls reviewed`);
