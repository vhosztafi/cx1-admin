import {closed as o,uid as id,money,positiveMoney,label as t,instant,hash,choice as e,many,conditionSchema} from './underwriting-contract-model.mjs';

// Phase 6 implementation contracts. These declarations do not open runtime
// routes: each owning plan must supply current-authority and SQL/API evidence.
export function addUnderwritingContracts({schemas:s,ref:r,operation:op,paths}){
  const bool={type:'boolean'}, count={type:'integer',minimum:0}, reason=t(2000);
  const etag={type:'string',pattern:'^"[!#-~]+"$',maxLength:100};
  const context={quoteId:id,cycleId:id,revisionId:id,pricingInputHash:hash};
  const commandContext={cycleId:id};
  const page=item=>o({items:many(item,100),nextCursor:t(2048)},['items']);
  const paging=[['cursor',t(2048)],['pageSize',{type:'integer',minimum:1,maximum:100}]];
  const enumeration=e('draft','rating-pending','rated','referred','approved','sent','accepted','declined','bound','withdrawn');
  s.UnderwritingConditionWrite=conditionSchema;
  s.UnderwritingContext=o({...context,clientId:id,relationshipId:id,productVersionId:id,agencyTermsVersionId:id,ratingRuleVersionId:id,binderVersionId:id,authorityVersionId:id});
  s.UnderwritingCommandResult=o({id,quoteId:id,quoteEtag:etag});
  s.UnderwritingWorkResult=o({id,quoteId:id,quoteEtag:etag,jobId:id,state:{const:'queued'}});
  s.UnderwritingBlocker=o({code:t(100),message:t(1000),path:t(500),targetId:id,dimension:t(60)},['code','message']);
  s.UnderwritingRefreshOption=o({productVersionId:id,displayName:t(),versionLabel:t(60),agencyTermsVersionId:id,termsVersion:{type:'integer',minimum:1},effectiveFrom:{type:'string',format:'date'}});
  s.UnderwritingProofRequirement=o({code:t(60),label:t(1000),path:t(500),riskItemId:id,conditionId:id,termsVersionId:id,capacitySubmissionId:id,inputFingerprint:hash,satisfied:bool},['code','label','path','inputFingerprint','satisfied']);
  s.UnderwritingAuthorityRow=o({code:t(100),label:t(200),requested:t(200),actorLimit:t(200),binderLimit:t(200),actorAllows:bool,binderAllows:bool});
  s.UnderwritingAuthorityView=o({hasCurrentGrant:bool,authorityVersionId:id,rows:many(r('UnderwritingAuthorityRow'),1000)},['hasCurrentGrant','rows']);
  s.UnderwritingAssessment=o({quoteId:id,quoteEtag:etag,state:enumeration,createdAt:instant,productLabel:t(),productVersionLabel:t(60),providerLabel:t(),providerId:id,context:r('UnderwritingContext'),ratingId:id,jobId:id,termsVersionId:id,termsHash:hash,assuranceHash:hash,acceptanceId:id,assignedUserId:id,assignedUserLabel:t(),submissionId:id,assignedTeamId:id,assignedTeamLabel:t(),refreshOptions:many(r('UnderwritingRefreshOption'),32),proofRequirements:many(r('UnderwritingProofRequirement'),1000),appliedEndorsements:many(r('UnderwritingEndorsement'),100),
    authorityViews:many(r('UnderwritingAuthorityView'),1000),blockers:many(r('UnderwritingBlocker'),200),capabilities:o(Object.fromEntries(['canRate','canSubmit','canRevise','canReviewEvidence','canDecide','canEscalate','canPrepareTerms','canSend','canAccept','canIssue'].map(k=>[k,bool])))},['quoteId','quoteEtag','state','createdAt','productLabel','productVersionLabel','providerLabel','blockers','capabilities','refreshOptions','proofRequirements','appliedEndorsements','authorityViews']);
  s.UnderwritingRateRequest=o({revisionId:id,reason:t(1000)});
  s.UnderwritingCycleRequest=o({...commandContext,reason});
  s.UnderwritingRefreshRequest=o({revisionId:id,productVersionId:id,confirmedTermsVersionId:id,reason:t(1000)});
  s.UnderwritingRatingFactor=o({code:t(60),label:t(),amount:money,direction:e('charge','discount'),basisAmount:money,basisPoints:count,targetId:id},['code','label','amount','direction']);
  s.UnderwritingRatingView=o({id,...context,ruleVersionId:id,completedAt:instant,expiresAt:instant,applicable:bool,
    currency:{const:'GBP'},annualPremium:money,termPremium:money,tax:money,fee:money,grossPayable:money,brokerCommission:money,
    agencyTermsVersionId:id,factors:many(r('UnderwritingRatingFactor'),200),input:r('QuoteCaptureProposal'),blockers:many(r('UnderwritingBlocker'),200)});
  s.UnderwritingRatingHistoryItem=o({id,cycleId:id,revisionId:id,revisionNumber:{type:'integer',minimum:1},completedAt:instant,expiresAt:instant,outcome:e('rated','rejected'),grossPayable:money});
  s.UnderwritingDecisionItem={oneOf:['approve','approve-with-conditions','query','decline','reopen'].map(outcome=>o({
    referralId:id,etag,outcome:{const:outcome},reason,
    ...(outcome==='approve-with-conditions'?{conditions:many(r('UnderwritingConditionWrite'),20,1)}:{}),
    ...(outcome==='query'?{question:t(2000),conditions:many(r('UnderwritingConditionWrite'),20,1)}:{}),
  }))};
  s.UnderwritingDecisionRequest=o({...commandContext,decisions:many(r('UnderwritingDecisionItem'),50,1)});
  s.UnderwritingSingleDecisionRequest=o({...commandContext,decision:r('UnderwritingDecisionItem')});
  s.UnderwritingDecisionView=o({id,referralId:id,...context,outcome:e('approve','approve-with-conditions','query','decline','reopen'),reason,question:t(2000),actorId:id,actorLabel:t(),authorityVersionId:id,recordedAt:instant,conditions:many(r('UnderwritingConditionWrite'),20)},['id','referralId',...Object.keys(context),'outcome','reason','actorId','actorLabel','authorityVersionId','recordedAt','conditions']);
  s.UnderwritingConditionView=o({id,decisionId:id,cycleId:id,etag,definition:r('UnderwritingConditionWrite'),state:e('outstanding','resolved','superseded'),evidenceAssociationId:id,latestResolutionId:id},['id','decisionId','cycleId','etag','definition','state']);
  s.UnderwritingReferralView=o({id,...context,etag,ruleCode:t(60),dimension:t(60),targetId:id,reason,state:e('open','approved','conditional','queried','declined','superseded'),assignedUserId:id,escalationId:id,
    requestedAmount:money,actorLimit:money,binderLimit:money,decisions:many(r('UnderwritingDecisionView'),100),conditions:many(r('UnderwritingConditionView'),100)},['id',...Object.keys(context),'etag','ruleCode','dimension','reason','state','decisions','conditions']);
  s.UnderwritingEvidenceAttachRequest=o({...commandContext,fileId:id,requirementCode:t(60),riskItemId:id,conditionId:id,termsVersionId:id,capacitySubmissionId:id,inputFingerprint:hash,reason},['cycleId','fileId','requirementCode','inputFingerprint','reason']);
  s.UnderwritingEvidenceReviewRequest=o({...commandContext,associationEtag:etag,outcome:e('accepted','rejected'),expectedFingerprint:hash,reason});
  s.UnderwritingEvidenceWithdrawRequest=o({...commandContext,associationEtag:etag,reason});
  s.UnderwritingEvidenceEvent=o({id,associationId:id,cycleId:id,sequence:{type:'integer',minimum:1},kind:e('review','withdrawal'),outcome:e('accepted','rejected'),reason,actorId:id,actorLabel:t(),recordedAt:instant,inputFingerprint:hash},['id','associationId','cycleId','sequence','kind','reason','actorId','actorLabel','recordedAt','inputFingerprint']);
  s.UnderwritingEvidenceView=o({id,...context,fileId:id,fileName:t(150),requirementCode:t(60),riskItemId:id,conditionId:id,termsVersionId:id,capacitySubmissionId:id,inputFingerprint:hash,etag,
    screeningState:e('pending','accepted','rejected'),reviewState:e('unreviewed','accepted','rejected'),withdrawn:bool,latestReviewId:id},['id',...Object.keys(context),'fileId','fileName','requirementCode','inputFingerprint','etag','screeningState','reviewState','withdrawn']);
  s.UnderwritingConditionResolutionRequest=o({...commandContext,conditionEtag:etag,evidenceAssociationId:id,outcome:e('satisfied','rejected'),reason});
  s.UnderwritingEscalationCreateRequest=o({...commandContext,referralEtag:etag,providerId:id,reason});
  s.UnderwritingEscalationSendRequest=o({...commandContext,escalationEtag:etag,body:t(8000),evidenceAssociationIds:many(id,20),scenarioVersionId:id});
  s.UnderwritingCapacityExtension={oneOf:[
    o({dimension:e('premium-limit','stock-limit','vehicle-limit','tools-limit','premises-limit'),maximumAmount:positiveMoney}),
    o({dimension:{const:'driver-age'},minimumAge:{type:'integer',minimum:16,maximum:100},maximumAge:{type:'integer',minimum:16,maximum:100}}),
    o({dimension:{const:'trade-restriction'},questionId:t(100),permitted:{const:true}}),
  ]};
  const supplied={...commandContext,escalationEtag:etag,submissionId:id,submissionHash:hash,providerUnderwriter:t(),providerReference:t(100),body:t(8000),receivedAt:instant,evidenceAssociationId:id};
  s.UnderwritingCapacityResponseRequest={oneOf:['approve','approve-with-conditions','query','decline'].map(outcome=>o({
    ...supplied,outcome:{const:outcome},
    ...(['approve','approve-with-conditions'].includes(outcome)?{validFrom:instant,validTo:instant,authorisedLimits:many(r('UnderwritingCapacityExtension'),20)}:{}),
    ...(outcome==='approve-with-conditions'?{conditions:many(r('UnderwritingConditionWrite'),20,1)}:{}),
  }))};
  s.UnderwritingCapacityMessage=o({id,escalationId:id,submissionId:id,submissionHash:hash,direction:e('outbound','inbound'),provenance:e('staff-submission','demo-provider','supplied-response'),body:t(8000),recordedAt:instant,receivedAt:instant,applicationState:e('applied','superseded'),recordedByLabel:t(),decisionId:id,
    providerUnderwriter:t(),providerReference:t(100),outcome:e('approve','approve-with-conditions','query','decline'),validFrom:instant,validTo:instant,evidenceAssociationId:id,providerEventId:t(100),authorisedLimits:many(r('UnderwritingCapacityExtension'),20),conditions:many(r('UnderwritingConditionWrite'),20)},['id','escalationId','submissionId','submissionHash','direction','provenance','body','recordedAt']);
  s.UnderwritingCapacityScenario=o({id,label:t(),version:{type:'integer',minimum:1}});
  s.UnderwritingCapacityAttempt=o({number:{type:'integer',minimum:1,maximum:18},startedAt:instant,endedAt:instant,outcome:t(30),errorCode:t(100)},['number','startedAt','outcome']);
  s.UnderwritingEscalationView=o({id,...context,referralId:id,providerId:id,binderVersionId:id,etag,quoteEtag:etag,state:e('draft','queued','sent','queried','approved','conditional','declined','superseded','failed'),
    binderContext:many(o({code:t(100),label:t(200),requested:t(200),binderLimit:t(200)}),1000),current:bool,reason,raisedAt:instant,raisedByLabel:t(),providerLabel:t(),ruleCode:t(60),dimension:t(60),assignedUserLabel:t(),
    currentSubmissionId:id,submissionHash:hash,submittedAt:instant,responseDueAt:instant,serviceStandard:t(200),jobId:id,currentResponseId:id,
    scenarios:many(r('UnderwritingCapacityScenario'),6),attemptHistory:many(r('UnderwritingCapacityAttempt'),18),conflictCount:count,capabilities:o({canSend:bool,canRecordResponse:bool,canRevise:bool}),
    messages:many(r('UnderwritingCapacityMessage'),100),blockers:many(r('UnderwritingBlocker'),200)},
    ['id',...Object.keys(context),'referralId','providerId','binderVersionId','etag','quoteEtag','state','current','reason','raisedAt','raisedByLabel','providerLabel','ruleCode','dimension','scenarios','attemptHistory','conflictCount','binderContext','capabilities','messages','blockers']);
  s.UnderwritingPrepareTermsRequest=o({...commandContext,ratingId:id,templateVersionId:id});
  s.UnderwritingSendTermsRequest=o({termsVersionId:id,recipientContactIds:many(id,20,1)});
  s.UnderwritingAcceptanceRequest=o({...commandContext,ratingId:id,termsVersionId:id,termsHash:hash,assuranceHash:hash,accepterLabel:t(),acceptedAt:instant,channel:e('email','written','telephone'),evidenceAssociationId:id});
  s.UnderwritingAcceptanceView=o({id,...s.UnderwritingAcceptanceRequest.properties,quoteId:id,recordedBy:id,recordedAt:instant});
  s.UnderwritingTermCover=o({code:e('road-risks','stock-custody','premises','tools-equipment'),limit:money,excess:money,targetIds:many(id,100)});
  s.UnderwritingEndorsement=o({code:t(60),version:t(60),wording:t(8000),decisionId:id,targetIds:many(id,100)});
  s.UnderwritingTermsView=o({id,...context,ratingId:id,number:{type:'integer',minimum:1},termsHash:hash,assuranceHashAtPreparation:hash,templateVersionId:id,preparedAt:instant,preparedBy:id,
    cover:many(r('UnderwritingTermCover'),20,1),endorsements:many(r('UnderwritingEndorsement'),100),conditions:many(r('UnderwritingConditionView'),100),rating:r('UnderwritingRatingView'),agencyTermsVersionId:id,
    settlement:o({collector:e('agency','mga'),mode:e('net-remittance','separate-payment'),commissionRateBps:{type:'integer',minimum:0,maximum:10000},feeShareBps:{type:'integer',minimum:0,maximum:10000}}),documentState:e('structured-payload','generation-queued','generated')});
  s.UnderwritingDeliveryView=o({id,termsVersionId:id,jobId:id,state:e('queued','delivered','failed','superseded'),recipientContactIds:many(id,20,1),payloadHash:hash,completedAt:instant,errorCode:t(100)},['id','termsVersionId','jobId','state','recipientContactIds','payloadHash']);
  s.UnderwritingIssueRequest=o({...commandContext,ratingId:id,acceptanceId:id,termsHash:hash,assuranceHash:hash,reason});
  s.UnderwritingIssueResult=o({policyId:id,policyReference:t(40),quoteId:id,quoteEtag:etag,termId:id,versionId:id,transactionId:id,obligationId:id,documentRequestIds:many(id,20,1)});
  s.FirstPolicyFinancialView=o({obligationId:id,transactionId:id,journalId:id,currency:{const:'GBP'},debtorKind:e('agency','relationship'),debtorId:id,amountDue:money,
    premium:money,tax:money,fee:money,brokerCommission:money,brokerFeeShare:money,insurerPayable:money,retainedFeeIncome:money,brokerRemunerationPayable:money,
    lines:many(o({accountCode:t(60),side:e('debit','credit'),amount:positiveMoney,componentCode:t(60)}),30,1)});
  s.FirstPolicyDocumentRequest=o({id,versionId:id,templateVersionId:id,kind:e('policy-schedule','statement-of-fact','quote-terms'),state:e('queued','generated','failed'),documentVersionId:id},['id','versionId','templateVersionId','kind','state']);
  s.FirstPolicyView=o({id,reference:t(40),sourceQuoteId:id,clientId:id,relationshipId:id,agencyId:id,termId:id,versionId:id,transactionId:id,issuedAt:instant,snapshot:r('PolicySnapshot'),financials:r('FirstPolicyFinancialView'),documentRequests:many(r('FirstPolicyDocumentRequest'),20)});

  const replace=(method,path,name,permission,options={})=>{
    delete paths[path]?.[method];op(method,path,name,permission,{...options,existing:method!=='get'});
    const operation=paths[path][method];
    operation['x-runtime-status']=['rateQuote','submitQuote','returnQuoteToDraft','refreshQuoteUnderwritingVersion','getQuoteUnderwriting','getRating'].includes(name)
      ? 'phase-6-03-implemented' : name === 'listQuoteRatings' ? 'phase-6-04-implemented'
        : ['decideQuoteReferrals','decideReferral','getReferral','listReferrals','attachUnderwritingEvidence','listUnderwritingEvidence',
          'listUnderwritingEvidenceEvents','listReferralDecisions','reviewUnderwritingEvidence','withdrawUnderwritingEvidence',
          'uploadUnderwritingEvidenceFile','resolveReferralCondition'].includes(name) ? 'phase-6-05-implemented' : 'phase-6-pending';
    operation.description+=' Phase 6 contract; runtime availability requires owning-plan verification. Current identity, agency and subject scope apply before receipt replay. Responses are no-store.';
    if(method!=='get'){
      operation['x-etag-resource']='quote';
      operation.parameters.find(p=>p.name==='If-Match').description='Strong current owning quote ETag. Relevant child ETags are separate request fields; lock the quote and authorise before replay.';
    }
  };
  const write=(path,name,permission,input,output='UnderwritingCommandResult',status=200)=>replace('post',path,name,permission,{input:r(input),output:r(output),status});
  const read=(path,name,permission,output,query=[])=>replace('get',path,name,permission,{output:r(output),query});
  write('/quotes/{quoteId}/rate','rateQuote','quote-rate','UnderwritingRateRequest','UnderwritingWorkResult',202);
  write('/quotes/{quoteId}/submit','submitQuote','quote-submit','UnderwritingCycleRequest');
  write('/quotes/{quoteId}/return-to-draft','returnQuoteToDraft','quote-revise','UnderwritingCycleRequest');
  write('/quotes/{quoteId}/underwriting/refresh','refreshQuoteUnderwritingVersion','quote-revise','UnderwritingRefreshRequest');
  read('/quotes/{quoteId}/underwriting','getQuoteUnderwriting','underwriting-read','UnderwritingAssessment');
  read('/ratings/{ratingId}','getRating','target-read','UnderwritingRatingView');
  replace('get','/quotes/{quoteId}/ratings','listQuoteRatings','underwriting-read',{output:page(r('UnderwritingRatingHistoryItem')),query:paging});
  write('/quotes/{quoteId}/referral-decisions','decideQuoteReferrals','underwriting-decide-within-authority','UnderwritingDecisionRequest');
  write('/referrals/{referralId}/decisions','decideReferral','underwriting-decide-within-authority','UnderwritingSingleDecisionRequest');
  read('/referrals/{referralId}','getReferral','underwriting-read','UnderwritingReferralView');
  replace('get','/referrals','listReferrals','underwriting-read',{output:page(r('UnderwritingReferralView')),query:[['quoteId',id],['cursor',t(2048)],['pageSize',{type:'integer',minimum:1,maximum:100}]]});
  paths['/referrals'].get.parameters.find(p=>p.name==='quoteId').required=true;
  write('/quotes/{quoteId}/underwriting/evidence','attachUnderwritingEvidence','underwriting-evidence-write','UnderwritingEvidenceAttachRequest','UnderwritingCommandResult',201);
  replace('get','/quotes/{quoteId}/underwriting/evidence','listUnderwritingEvidence','underwriting-read',{output:page(r('UnderwritingEvidenceView')),query:paging});
  replace('get','/quotes/{quoteId}/underwriting/evidence/{associationId}/events','listUnderwritingEvidenceEvents','underwriting-read',{output:page(r('UnderwritingEvidenceEvent')),query:paging});
  replace('get','/referrals/{referralId}/decisions','listReferralDecisions','underwriting-read',{output:page(r('UnderwritingDecisionView')),query:paging});
  write('/quotes/{quoteId}/underwriting/evidence/{associationId}/reviews','reviewUnderwritingEvidence','underwriting-evidence-review','UnderwritingEvidenceReviewRequest');
  write('/quotes/{quoteId}/underwriting/evidence/{associationId}/withdraw','withdrawUnderwritingEvidence','underwriting-evidence-write','UnderwritingEvidenceWithdrawRequest');
  replace('post','/quotes/{quoteId}/underwriting/evidence-files','uploadUnderwritingEvidenceFile','underwriting-evidence-write',{output:r('UnderwritingCommandResult'),status:201});
  paths['/quotes/{quoteId}/underwriting/evidence-files'].post.requestBody=structuredClone(paths['/quotes/{quoteId}/evidence-files'].post.requestBody);
  write('/referrals/{referralId}/conditions/{conditionId}/resolutions','resolveReferralCondition','underwriting-decide-within-authority','UnderwritingConditionResolutionRequest');
  write('/referrals/{referralId}/escalations','createEscalation','underwriting-escalate','UnderwritingEscalationCreateRequest','UnderwritingCommandResult',201);
  read('/escalations/{escalationId}','getEscalation','underwriting-read','UnderwritingEscalationView');
  replace('get','/escalations/{escalationId}/messages','listEscalationMessages','underwriting-read',{output:page(r('UnderwritingCapacityMessage')),query:paging});
  write('/escalations/{escalationId}/send','sendEscalation','underwriting-escalate','UnderwritingEscalationSendRequest','UnderwritingWorkResult',202);
  write('/escalations/{escalationId}/responses','recordCapacityResponse','underwriting-record-capacity','UnderwritingCapacityResponseRequest','UnderwritingCommandResult',201);
  write('/quotes/{quoteId}/terms/prepare','prepareQuoteTerms','quote-terms','UnderwritingPrepareTermsRequest','UnderwritingCommandResult',201);
  write('/quotes/{quoteId}/terms','sendQuoteTerms','quote-terms','UnderwritingSendTermsRequest','UnderwritingWorkResult',202);
  replace('get','/quotes/{quoteId}/terms','listQuoteTerms','quote-read',{output:o({terms:many(r('UnderwritingTermsView'),100),deliveries:many(r('UnderwritingDeliveryView'),100),acceptances:many(r('UnderwritingAcceptanceView'),100),nextTermsCursor:t(2048),nextDeliveriesCursor:t(2048),nextAcceptancesCursor:t(2048)},['terms','deliveries','acceptances']),query:[['termsCursor',t(2048)],['deliveriesCursor',t(2048)],['acceptancesCursor',t(2048)],['pageSize',{type:'integer',minimum:1,maximum:100}]]});
  write('/quotes/{quoteId}/acceptances','recordQuoteAcceptance','quote-acceptance','UnderwritingAcceptanceRequest','UnderwritingCommandResult',201);
  write('/quotes/{quoteId}/issue','issueQuote','policy-issue-within-authority','UnderwritingIssueRequest','UnderwritingIssueResult',201);
  // This first-issue detail does not change the later servicing draft contracts.
  read('/policies/{policyId}','getPolicy','policy-read','FirstPolicyView');
  for(const name of ['QuoteCaptureSummary','QuoteDiscoverySummary','QuoteCaptureView'])s[name].properties.state=enumeration;
  paths['/quotes'].get.parameters.find(p=>p.name==='status').schema=enumeration;
  const existing=new Set(paths['/policies'].get.parameters.map(p=>p.name));
  for(const [name,schema] of [['productCode',e('motor-trade-road-risks','motor-trade-combined')],['clientId',id],['registration',t(12)],['inceptionFrom',{type:'string',format:'date'}],['inceptionTo',{type:'string',format:'date'}],['sort',e('reference','inception','issued')],['direction',e('asc','desc')]])
    if(!existing.has(name))paths['/policies'].get.parameters.push({name,in:'query',required:false,schema});
}
