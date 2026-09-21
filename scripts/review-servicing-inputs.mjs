import {readFile,writeFile} from 'node:fs/promises';
const file='docs/design/reviewed-api-controls.json';const reviews=JSON.parse(await readFile(file,'utf8'));
const groups=[
 ['savePolicyDraft','a5cbae9b8992:commonEffectiveIntent 60ada0e23106:dateBasis 37837fb0eb28:requestedBy','Change timing and requestor are persisted with the lease/ETag; schedule timing changes invalidate rating and acceptance.'],
 ['addTaskComment','b1917d871627:body','Comment composer has onSend as well as onChange; explicit send saves an internal task note and rejects blank content.'],
 ['updateTask','340623a67370:assignment d3cef2ca0552:dueOn','Selected typed assignment and local due date save under task ETag and assignment scope.'],
 ['transitionTask','bb257a94cac5:state','Changing status is an explicit lifecycle command with reason/checklist gates, not a generic task write.'],
 ['updateProfile','e9ca9b8ddf5f:fullName fb7fd0380a0e:displayName ae3dc91658bf:telephone 6fa068333beb:jobTitle a6317a500517:outOfOffice 10bc9f0e6f7c:taskDigest','Self-profile fields persist under profile ETag; out-of-office affects new assignments only.'],
 ['changePassword','664235c9a920:currentPassword 1ca7dc6c8e73:newPassword','Password input remains transient; explicit command uses framework verification/history and never caches secrets.'],
 ['sendEscalation','4aa26265290c:body','Composer text is sent explicitly via its onSend handler; pin referral revision, provider and attachments.'],
 ['savePolicyDraft','bdb6e9ab15e0:cancellationReasonCode c6ed3365f90b:commonEffectiveIntent 9c276d4c11eb:reason','Cancellation modal retains a structured reason selection and separate audit detail in command intent; neither control issues cancellation.'],
 ['createTask','912fdd744a9d:typeCode deb1c93c9380:title 3b92b95bf767:assignment 1244b13ffc62:priority c1a4eed5f8d7:dueOn','Task modal values save only on confirmation with an authorised typed parent.'],
 ['createContact','a824b6d082ef:role bbfeae72249c:email 768bb83c62f3:telephone af4438a7842d:isPrimary 44e8ab200a0e:marketingConsent','Agency-scoped contact values preserve primary uniqueness and distinct marketing consent channel/source.'],
 ['recordCapacityResponse','996063cc0f6f:outcome 544b08dc9b9d:providerUnderwriter 4d20dd209c0d:providerReference ae1b5b522c32:body','Provider outcome fields are recorded with evidence and received time; not treated as direct permission to issue.'],
 ['inviteAgencyUser','35c078b12b2f:email 2b148516c13c:role','Onboarding invitation is agency-scoped and sends only when activation prerequisites hold.'],
 ['createSupportFlag','fbf14ee7ae39:typeCode be8c44f83a57:internalCategory cdd26ca2bb50:internalInstruction 3d59fbcbb5d6:agencyInstruction 737f31d19425:consentBasis 3aa3b6d10f6d:reviewOn efe74f614e7d:reason','Flag modal stores restricted support evidence and separate safe wording; sharing remains explicit and excluded from rating.'],
 ['requestIdentityChange','02d5a8374d81:newEmail d8964d48425a:requestedApproverId 50bae4c3910a:notifyUser','Email-change proposal requires independent approval; routing approver is not proof of approval and required notifications cannot be suppressed.'],
 ['replaceRecoveryCodes','b18928254b6a:password','Password is transient reauthentication for generating new codes; old plaintext codes cannot be retrieved.'],
 ['disableMfa','1906c3818071:password ed9a5b84734d:reason','MFA disable requires verified password/code and role policy; never a client-side boolean toggle.']
];
function put(row){const index=reviews.findIndex(r=>r.controlId===row.controlId);if(index<0)reviews.push(row);else reviews[index]=row;}
for(const [operationId,fields,reason] of groups)for(const pair of fields.split(' ')){
 const [suffix,field]=pair.split(':');put({controlId:`CTL-${suffix}`,status:'reviewed',disposition:'edit-then-save',operationIds:[operationId],apiFields:[{operationId,field}],reason});
}
const special=[
 ['84a03cc130a7','client-only',[],'New-password confirmation is a transient equality check; only the actual password is submitted and server policy remains authoritative.'],
 ['ff9e226baf10','client-only',[],'The source offers only No for draft inclusion; render a fixed explanation. Drafts never enter issued as-at results.'],
 ['6e2b2ebf7bc6','read',['getPolicyAsAt'],'Effective-date selector sets effectiveAt; process-basis uses knownAt with E=K unless user explicitly supplies both.'],
 ['d27b77003a60','read',['getPolicyAsAt'],'Basis controls conversion to effectiveAt/knownAt query semantics, not a stored policy mutation.'],
 ['aba9e5f38f4e','edit-then-save',['saveQuoteProposal'],'Driver selector chooses the stable parent driver ID for conviction or loss; never links by display-name equality.'],
 ['6cb19bd3e05c','edit-then-save',['createContact','inviteAgencyUser'],'Shared full-name control belongs to contact or agency-invite context; use fullName for contact and displayName for invite without guessing surname.'],
 ['53fe844ca2f5','edit-then-save',['createSupportFlag'],'Contact choice resolves the person ID and current relationship in the endpoint path; no cross-agency person enumeration.'],
 ['6024d631eea5','read',['getUser'],'Selected subject is the authorised target user ID for identity request; client input cannot select an inaccessible user.'],
 ['84ae08d2ace0','read',['getUser'],'Current email is read from server state and not trusted as proof or editable identity input.'],
 ['5ec6b96d9dd9','client-only',[],'Codes are one-time secret display/copy state from successful generation, not an editable/persisted field.'],
 ['2be8051a493d','edit-then-save',['savePolicyDraft'],'Driver fullName in proposed risk retains declared name without splitting or changing issued risk.'],
 ['006262166a6c','edit-then-save',['savePolicyDraft'],'Driver dateOfBirth is an ISO date on selected stable draft driver.'],
 ['67729c63e82c','edit-then-save',['savePolicyDraft'],'Driver licence.type is a versioned reference; data completeness differs from evidence verification.'],
 ['31515d181764','edit-then-save',['savePolicyDraft'],'Driver licence.number is retained on selected proposed risk, with restricted scope.'],
 ['48fcf50be539','edit-then-save',['savePolicyDraft'],'Driver licence.testDate is an ISO date checked against birth/inception; never accepts arbitrary text.'],
 ['3b28acf92b73','edit-then-save',['savePolicyDraft'],'Declared claims count is a typed driver question reconciled against detailed losses before rating.'],
 ['2ae7c284ac04','edit-then-save',['inviteAgencyUser','inviteInternalUser'],'Invite email routes according to authorised agency/internal context.'],
 ['0f97487577d1','edit-then-save',['inviteAgencyUser','inviteInternalUser'],'Invite full name maps to initial displayName; user may later complete own profile.'],
 ['07f724941351','edit-then-save',['inviteAgencyUser','inviteInternalUser'],'Role selection uses endpoint-specific allowlist and prohibits mixing internal/agency access.'],
 ['0a0a12156da2','command',['requestAccountAdminChange'],'Generic account-change request supports email, role/team or authority routing; submitting it does not grant requested access.']
];
for(const [suffix,disposition,operationIds,reason] of special)put({controlId:`CTL-${suffix}`,status:'reviewed',disposition,operationIds,reason});
await writeFile(file,JSON.stringify(reviews,null,2)+'\n');console.log(`${reviews.length} reviewed controls.`);
