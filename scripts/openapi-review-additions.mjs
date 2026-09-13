// Contract gaps identified by the semantic/source-control review.
export function addReviewedApi({schemas:s,ref:r,text:t,enumeration:e,object:o,array:a,id,instant,date,boolean:b,integer,decimal,operation:op,list,paths}){
 const reason=o({reason:t(1000)});
 s.CoverChangeSchedule=o({changeId:id,effectiveAt:instant,section:{$ref:'./schemas/policy.schema.json#/$defs/Cover/properties/sections/items'},action:e('add','replace','remove')});
 const timing={dateBasis:e('shared','per-cover-change'),requestedBy:t(),coverChangeSchedule:a(r('CoverChangeSchedule'))};
 Object.assign(s.PolicyDraft.properties,timing);s.PolicyDraft.required.push(...Object.keys(timing));
 const saveDraft=paths['/drafts/{draftId}/proposal'].put.requestBody.content['application/json'].schema;
 Object.assign(saveDraft.properties,timing);saveDraft.required.push(...Object.keys(timing));
 s.IssueResult.properties.versionIds=a(id);s.IssueResult.required.push('versionIds');
 s.RatingResult.properties.effectiveSlices=a(o({effectiveAt:instant,premiumChange:decimal,taxChange:decimal,commissionChange:decimal,currency:{const:'GBP'}}));
 s.RatingResult.required.push('effectiveSlices');
 op('post','/quotes/{quoteId}/clone','cloneQuote','quote-write',{existing:true,input:o({sourceRevisionId:id,relationshipId:id,reason:t(1000)}),output:r('Quote'),status:201});
 op('post','/versions/{versionId}/clone-quote','clonePolicyToQuote','quote-write',{input:o({relationshipId:id,reason:t(1000)}),output:r('Quote'),status:201});
 list('/quotes/{quoteId}/revisions','listQuoteRevisions','quote-read',o({id,number:integer,savedAt:instant,proposal:r('PolicyProposal')}));
 s.VersionComparison=o({leftVersionId:id,rightVersionId:id,changes:a(o({path:t(500),kind:e('added','removed','changed'),beforeText:t(8000),afterText:t(8000)},['path','kind']))});
 op('get','/versions/{versionId}/compare','comparePolicyVersions','policy-read',{query:[['otherVersionId',id]],output:r('VersionComparison')});
 op('get','/quotes/{quoteId}/compare','compareQuoteRevisions','quote-read',{query:[['leftRevisionId',id],['rightRevisionId',id]],output:r('VersionComparison')});
 op('post','/relationships/{relationshipId}/contacts/{contactId}/end','endContact','contact-write',{existing:true,input:reason,output:r('Contact')});
 s.Contact.properties.endedAt=instant;
 op('post','/quotes/{quoteId}/referral-decisions','decideQuoteReferrals','underwriting-decide-within-authority',{existing:true,input:o({decisions:a(o({referralId:id,etag:t(100),targetHash:t(64),outcome:e('approve','approve-with-conditions','query','decline','reopen'),reason:t(2000),conditions:a(o({description:t(1000),evidenceRequired:b}))}))}),output:o({referrals:a(r('Referral'))}),summary:'Decide selected referrals atomically after checking each authority and version'});
 op('post','/records/{recordId}/document-deliveries','sendDocumentPack','document-send',{input:o({documentVersionIds:a(id),recipientContactIds:a(id),templateVersionId:id,subject:t(300),body:t(8000)}),output:r('Job'),status:202});
 op('post','/terms/{termId}/as-at/export','exportPolicyReconstruction','policy-read',{input:o({effectiveAt:instant,knownAt:instant,format:{const:'pdf'}}),output:r('Job'),status:202});
 s.AgencyStateRequest=o({id,agencyId:id,stateRequested:e('active','suspended'),reason:t(1000),requestedBy:id,approvedBy:id,state:e('pending','approved','rejected','applied')},['id','agencyId','stateRequested','reason','requestedBy','state']);
 const suspension=paths['/agencies/{agencyId}/suspend'].post;
 suspension.operationId='requestAgencySuspension';suspension.summary='Request manager approval to suspend an agency';suspension.description='Creates a suspension proposal; manager approval atomically suspends access and revokes agency sessions.';
 suspension.responses[200].content['application/json'].schema=r('AgencyStateRequest');
 list('/agencies/{agencyId}/state-requests','listAgencyStateRequests','agency-admin',r('AgencyStateRequest'));
 op('get','/agency-state-requests/{requestId}','getAgencyStateRequest','agency-manager-approve',{output:r('AgencyStateRequest')});
 op('post','/agency-state-requests/{requestId}/decision','decideAgencyStateRequest','agency-manager-approve-no-self-approval',{existing:true,input:o({outcome:e('approve','reject'),reason:t(1000)}),output:r('AgencyStateRequest')});
 op('post','/finance/bank-lines/{bankLineId}/exclude','excludeDuplicateBankLine','finance-reconcile',{existing:true,input:o({duplicateOfBankLineId:id,reason:t(1000)}),output:r('BankLine')});
 s.BankLine.properties.excludedAsDuplicate=b;s.BankLine.properties.duplicateOfBankLineId=id;
 op('post','/finance/bordereaux/{batchId}/rows/{rowId}/correction','correctBordereauRow','bordereau-write',{existing:true,input:o({policyReference:t(40),providerProductCode:t(100),agencyReference:t(40),reason:t(1000)},['reason']),output:r('BordereauRow')});
 op('post','/finance/bordereaux/{batchId}/exclude-failures','excludeFailingBordereauRows','bordereau-write',{existing:true,input:o({rowIds:a(id),reason:t(1000)}),output:r('Bordereau')});
 s.ProfileWrite=o({fullName:t(),displayName:t(),telephone:t(50),jobTitle:t(),outOfOffice:b,taskDigest:e('daily-0800','twice-daily','off')});
 s.AccountProfile=o({...s.Actor.properties,...s.ProfileWrite.properties,recoveryCodesRemaining:{type:'integer',minimum:0,maximum:10},passwordChangedAt:instant},[...new Set([...s.Actor.required,...s.ProfileWrite.required,'recoveryCodesRemaining','passwordChangedAt'])]);
 paths['/account'].get.responses[200].content['application/json'].schema=r('AccountProfile');
 paths['/account'].put.requestBody.content['application/json'].schema=r('ProfileWrite');
 paths['/account'].put.responses[200].content['application/json'].schema=r('AccountProfile');
 s.AccessChangeRequest=o({id,subjectUserId:id,requestedBy:id,approvedBy:id,roleCodes:a(t(60)),teamId:id,stateRequested:e('active','suspended'),reason:t(1000),state:e('pending','approved','rejected','applied'),reassignTasksToUserId:id},['id','subjectUserId','requestedBy','roleCodes','stateRequested','reason','state']);
 const access=paths['/admin/users/{userId}/access'].put;
 access.operationId='requestUserAccessChange';
 access.summary='Request manager approval for role, team or activation changes';
 access.description='Persists a proposal only. Manager approval, session revocation and task reassignment apply atomically through decideUserAccessChange.';
 access.responses[200].content['application/json'].schema=r('AccessChangeRequest');
 access.requestBody.content['application/json'].schema.properties.reassignTasksToUserId=id;
 list('/admin/access-requests','listAccessChangeRequests','access-manager-approve',r('AccessChangeRequest'));
 op('get','/admin/access-requests/{requestId}','getAccessChangeRequest','access-manager-approve',{output:r('AccessChangeRequest')});
 op('post','/admin/access-requests/{requestId}/decision','decideUserAccessChange','access-manager-approve-no-self-approval',{existing:true,input:o({outcome:e('approve','reject'),reason:t(1000)}),output:r('AccessChangeRequest')});
 for(const [path,name,permission,schema] of [
  ['/relationships/{relationshipId}/contacts/{contactId}','getContact','relationship-read','Contact'],
  ['/flags/{flagId}','getSupportFlag','support-internal-read','SupportFlag'],
  ['/terms/{termId}','getPolicyTerm','policy-read','PolicyTerm'],
  ['/authority-versions/{authorityVersionId}','getAuthorityVersion','authority-read','AuthorityVersion'],
  ['/admin/flag-types/{flagTypeId}','getFlagType','flag-type-admin','FlagType'],
  ['/admin/integrations/{integrationId}','getIntegrationSetting','integration-admin','IntegrationSetting'],
  ['/admin/identity-approvals/{approvalId}','getIdentityApproval','identity-approve','IdentityApproval'],
  ['/notifications/{notificationId}','getNotification','self','Notification']
 ])op('get',path,name,permission,{output:r(schema)});
 s.SafeSupportInstruction=o({id,personId:id,instruction:t(1000),reviewOn:date});
 list('/relationships/{relationshipId}/support-instructions','listSafeSupportInstructions','support-safe-read-explicit-grant',r('SafeSupportInstruction'));
 op('post','/account/identity-requests','requestOwnIdentityChange','self',{input:o({newEmail:{type:'string',format:'email'},reason:t(1000)}),output:r('IdentityApproval'),status:201});
 op('post','/account/sessions/revoke-others','revokeOtherSessions','self-recent-auth',{input:reason,output:o({revokedCount:integer})});
 op('post','/account/security-reports','reportUnrecognisedActivity','self',{input:o({sessionId:id,description:t(2000)},['description']),output:r('Task'),status:201});
 op('post','/admin/users/{userId}/password-reset','adminRequestPasswordReset','user-admin',{existing:true,input:reason,output:r('Job'),status:202});
 op('post','/admin/templates/{configurationId}/preview','previewTemplate','template-admin',{existing:true,input:o({sourceVersionId:id,audience:e('internal','agency','insurer')}),output:r('Job'),status:202});
 op('post','/admin/jobs/retry-batch','retryIntegrationBatch','integration-retry',{input:o({jobs:a(o({jobId:id,etag:t(100)})),reason:t(1000)}),output:o({jobIds:a(id)}),summary:'Retry selected failed jobs atomically after checking every scope and version'});
 s.ProposalEvidence=o({id,documentVersionId:id,requirementCode:t(100),riskItemId:id,state:e('pending','accepted','rejected','withdrawn'),reason:t(1000),targetRevision:integer},['id','documentVersionId','requirementCode','state','reason','targetRevision']);
 for(const [path,name,permission] of [['/quotes/{quoteId}','Quote','quote-write'],['/drafts/{draftId}','Draft','policy-draft-write']]){
  list(`${path}/evidence`,`list${name}Evidence`,permission,r('ProposalEvidence'));
  op('post',`${path}/evidence`,`attach${name}Evidence`,permission,{existing:true,lease:name==='Draft',input:o({documentVersionId:id,requirementCode:t(100),riskItemId:id,reason:t(1000)},['documentVersionId','requirementCode','reason']),output:r('ProposalEvidence'),status:201});
  op('post',`${path}/evidence/{evidenceId}/withdraw`,`withdraw${name}Evidence`,permission,{existing:true,lease:name==='Draft',input:o({reason:t(1000)}),output:r('ProposalEvidence'),summary:'Withdraw evidence association, retaining the document and audit history; verify target ownership and invalidate dependent validation'});
 }
 op('post','/proposal-evidence/{evidenceId}/decision','decideProposalEvidence','underwriting-evidence',{existing:true,input:o({state:e('accepted','rejected'),reason:t(1000)}),output:r('ProposalEvidence')});
 op('post','/referrals/{referralId}/conditions/{conditionId}/evidence','recordReferralConditionEvidence','underwriting-evidence',{existing:true,input:o({decisionId:id,targetHash:t(64),evidenceDocumentVersionId:id,satisfied:b,reason:t(1000)}),output:r('Referral')});
 const lookupQueries=[
  o({kind:{const:'address'},postcode:t(20),building:t(100)},['kind','postcode']),
  o({kind:{const:'company'},companyNumber:t(30)}),
  o({kind:{const:'fca'},regulatoryReference:t(30)}),
  o({kind:{const:'vehicle'},registration:t(20)}),
  o({kind:{const:'licence'},driverId:id,licenceNumber:t(100)})
 ];
 s.LookupRequest=o({subjectRecordId:id,referenceDataVersionId:id,query:{oneOf:lookupQueries}},['referenceDataVersionId','query']);
 s.LookupResult=o({id,kind:e('address','company','fca','vehicle','licence'),status:e('matched','no-match','multiple-matches','rejected'),referenceDataVersionId:id,asOf:instant,expiresAt:instant,candidates:a({oneOf:[
  o({kind:{const:'address'},address:r('Address')}),
  o({kind:{const:'company'},companyNumber:t(30),legalName:t(),address:r('Address'),state:e('active','dissolved')}),
  o({kind:{const:'fca'},regulatoryReference:t(30),legalName:t(),state:e('authorised','inactive','not-found')}),
  o({kind:{const:'vehicle'},registration:t(20),make:t(100),model:t(100),year:{type:'integer',minimum:1900,maximum:2200},bodyTypeCode:t(100)}),
  o({kind:{const:'licence'},driverId:id,valid:b,categories:a(t(30)),expiresOn:date})
 ]})});
 op('post','/lookups','requestLookup','lookup-use',{input:r('LookupRequest'),output:r('Job'),status:202});
 op('get','/lookups/{lookupId}','getLookupResult','lookup-owner-or-subject-read',{output:r('LookupResult')});
 s.RuleValue={oneOf:[o({kind:{const:'text'},value:t(500)}),o({kind:{const:'integer'},value:{type:'integer'}}),o({kind:{const:'decimal'},value:decimal}),o({kind:{const:'boolean'},value:b}),o({kind:{const:'reference'},collection:t(100),value:{oneOf:[t(200),{type:'integer'}]}})]};
 s.RuleCondition=o({questionId:t(100),operator:e('equals','not-equals','contains','present','greater-than','less-than'),value:r('RuleValue')},['questionId','operator']);
 s.QuestionDefinition.properties.appliesWhen=a(r('RuleCondition'));
 s.QuestionDefinition.properties.unit=e('none','GBP','basis-points','count','days','years','metres','square-metres');
 s.QuestionDefinition.required.push('unit');
 s.RatingRuleDefinition=o({basePremium:decimal,minimumPremium:decimal,factors:a(o({code:t(100),conditions:a(r('RuleCondition')),multiplier:{type:'string',pattern:'^[0-9]+(\\.[0-9]{1,6})?$'},reason:t(500)})),referrals:a(o({code:t(100),conditions:a(r('RuleCondition')),dimension:s.AuthorityRule.properties.dimension,reason:t(1000)})),expiryMinutes:{type:'integer',minimum:1,maximum:10080}});
 s.RatingRuleVersion=o({id,productId:id,version:{type:'integer',minimum:1},state:e('draft','published','retired'),effectiveFrom:instant,definition:r('RatingRuleDefinition')});
 list('/admin/rating-rules','listRatingRules','rating-rule-admin',r('RatingRuleVersion'),[['productId',id]]);
 op('get','/admin/rating-rules/{ruleVersionId}','getRatingRule','rating-rule-admin',{output:r('RatingRuleVersion')});
 op('post','/admin/rating-rules','createRatingRule','rating-rule-admin',{input:o({productId:id,effectiveFrom:instant,definition:r('RatingRuleDefinition')}),output:r('RatingRuleVersion'),status:201});
 op('put','/admin/rating-rules/{ruleVersionId}','updateRatingRule','rating-rule-admin',{existing:true,input:o({effectiveFrom:instant,definition:r('RatingRuleDefinition')}),output:r('RatingRuleVersion')});
 op('post','/admin/rating-rules/{ruleVersionId}/publish','publishRatingRule','rating-rule-publish',{existing:true,input:reason,output:r('RatingRuleVersion')});
 // The response cursor resumes the same scoped snapshot; running a report again
 // creates a new snapshot rather than combining rows across changing data.
 s.ReportResult.properties.runId=id;s.ReportResult.required.push('runId');
 paths['/reports/{reportId}/export'].post.requestBody.content['application/json'].schema={oneOf:[o({runId:id,format:e('csv','xlsx','pdf')}),o({filters:r('ReportFilters'),format:e('csv','xlsx','pdf')})]};
 op('get','/report-runs/{runId}','getReportRunPage','report-run-owner',{query:[['cursor',t(2048)],['pageSize',{type:'integer',minimum:1,maximum:100,default:25}]],output:r('ReportResult')});
 paths['/report-runs/{runId}'].get.responses[410]={description:'Pinned report run expired; start a new run.',content:{'application/problem+json':{schema:r('Problem')}}};
 // Require explicit policy aggregate concurrency for servicing issue, even
 // though new-business issue does not yet have a Policy aggregate.
 s.DraftIssueWrite=o(s.IssueWrite.properties,[...s.IssueWrite.required,'policyEtag']);
 paths['/drafts/{draftId}/issue'].post.requestBody.content['application/json'].schema=r('DraftIssueWrite');
}
