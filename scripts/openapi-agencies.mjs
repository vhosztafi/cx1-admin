// Phase 4 owns the final agency contract. These definitions are implementation
// contracts; a generated endpoint is not evidence that a runtime route exists.
export function addAgencyContracts({schemas:s,ref:r,text:t,enumeration:e,object:o,array:a,id,instant,date,boolean:b,integer,operation:op,list,paths}) {
 const email={type:'string',format:'email',maxLength:254};
 const money={type:'string',pattern:'^(0|[1-9][0-9]{0,16})\\.[0-9]{2}$'};
 const bps={type:'integer',minimum:0,maximum:10000};
 const hash={type:'string',pattern:'^[a-f0-9]{64}$'};
 const etag=t(100), reason=o({reason:t(1000)});
 const bounded=(items,maxItems)=>({...a(items),maxItems});
 const partial=properties=>o(properties,[]);
 const role=e('broker-admin','broker-user','broker-readonly');
 const state=e('draft','active','suspended','abandoned');
 const step={type:'integer',minimum:1,maximum:6};
 const productCode=e('motor-trade-road-risks','motor-trade-combined','commercial-combined');
 const evidenceKind=e('fca','toba','professional-indemnity','financial-check','sanctions','ownership','dpa','client-money');
 const providerKind=e('fca','financial-check','sanctions','ownership');
 const contact=partial({name:t(),email,telephone:t(50)});
 const commercial={effectiveFrom:date,commissionBasis:e('per-product','flat-rate'),flatCommissionBasisPoints:bps,
  feeSharing:e('none','agreed-split'),feeShareBasisPoints:bps,
  volumeCommitmentMode:e('none','target-no-penalty','target-tiered'),volumeCommitment:money,
  minimumPremiumOverrideMode:e('none','capacity-provider-agreed'),minimumPremiumOverride:money,
  referralRouting:e('standard-internal-underwriting')};
 const settlement={statementCycle:e('monthly','fortnightly'),method:e('bank-transfer','direct-debit'),premiumCollection:e('agency','mga'),commissionSettlement:e('net-remittance','separate-payment')};
 s.AgencyWrite=partial({legalName:t(),tradingName:t(),entityType:e('limited-company','llp','partnership','sole-trader'),companyNumber:t(30),
  address:partial(structuredClone(s.Address.properties)),tradingAddressMode:e('same-as-registered','different'),tradingAddress:partial(structuredClone(s.Address.properties)),
  regulatoryReference:t(30),regulatoryStatus:e('directly-authorised','appointed-representative','introducer-appointed-representative'),principalFirm:t(),
  clientMoneyBasis:e('risk-transfer','cass5-client-money','no-client-money'),arrangesGeneralInsurance:e('unchecked','confirmed','restricted'),territory:e('UK','GB','NI'),
  mainContact:contact,complianceContact:structuredClone(contact),accountsContact:structuredClone(contact),complaintsContact:structuredClone(contact),
  relationshipManagerId:id,correspondencePreference:e('email','email-and-post','portal-only'),officeHours:t(),
  commercialTerms:partial(commercial),compliance:partial({tobaStatus:e('not-sent','sent','signed'),tobaVersion:e('2026.1','2025.2'),tobaSignedOn:date,
   professionalIndemnityStatus:e('not-supplied','meets-minimum','below-minimum'),professionalIndemnityLimit:money,piExpiresOn:date,
   financialStanding:e('not-started','pending','passed','refer'),sanctionsCheck:e('not-started','clear','refer'),beneficialOwnershipVerified:e('not-started','verified','refer'),
   dataProcessingAgreement:e('not-sent','sent','signed')}),
  paymentTermsDays:{type:'integer',enum:[30,45,60]},creditLimit:money,settlement:partial(settlement)});
 s.AgencyWrite.description='Incomplete typed draft, maximum UTF-8 JSON body 65536 bytes. Omit missing fields; reject null/unknown keys. Declarations never set verification or activation status.';
 s.AgencyDraftSave=o({details:r('AgencyWrite'),onboardingStep:step,products:bounded(r('AgencyProductWrite'),3)},['details','onboardingStep']);
 s.AgencyChecklistItem=o({code:t(80),path:t(200),stage:step,state:e('missing','pending','satisfied','failed','stale','expired','unavailable'),message:t(1000),evidenceId:id},['code','path','stage','state','message']);
 s.AgencyChecklist=o({valid:b,agencyEtag:etag,ruleVersionId:id,calculatedAt:instant,items:bounded(r('AgencyChecklistItem'),80)});
 s.AgencyUnavailableSection=o({kind:e('quotes','policies','tasks','statements'),state:{const:'unavailable'},owningPhase:{type:'integer',enum:[5,6,9,10]},message:t(300)});
 s.Agency=o({id,reference:t(40),state,onboardingStep:step,userCount:integer,invitedUserCount:integer,details:r('AgencyWrite'),validation:r('AgencyChecklist'),unavailableSections:bounded(r('AgencyUnavailableSection'),4)});
 s.AgencySummary=o({id,reference:t(40),legalName:t(),state,onboardingStep:step,mainContactName:t(),relationshipManagerId:id,relationshipManagerName:t(),userCount:integer,invitedUserCount:integer,productCodes:bounded(productCode,3),lastActivityAt:instant,openActionCount:integer,dueFollowUpCount:integer,asOfDate:date},['id','reference','state','onboardingStep','productCodes','openActionCount','dueFollowUpCount','asOfDate']);
 s.AgencyKpis=o({active:integer,onboarding:integer,suspended:integer,brokerUsers:integer,invitedUsers:integer,openActions:integer,pendingStateRequests:integer,pendingTermsRequests:integer,pendingPermissionRequests:integer,dueFollowUps:integer,asOfDate:date});
 s.AgencyKpis.description='brokerUsers counts retained agency-scoped identities, including inactive users; invitedUsers counts the invited subset including staged identities. openActions sums pending state, terms and permission requests; dueFollowUps separately counts immutable recorded obligations with DueOn on or before asOfDate in London, not open or completed workflow tasks. KPI totals cover all agencies independently of directory filters.';
 s.AgencyProductWrite=o({productVersionId:id,effectiveFrom:date,brokerCommissionBasisPoints:bps});
 s.AgencyProduct=o({id,...structuredClone(s.AgencyProductWrite.properties),productCode,termsVersionId:id,effectiveTo:date},['id','productVersionId','effectiveFrom','brokerCommissionBasisPoints','productCode']);
 s.AgencyProductCatalogItem=o({productVersionId:id,productCode,name:t(),capacityProviderName:t(),distributionEligible:b,eligibilitySettingVersionId:id,ratingReady:b,unavailableReason:t(300)},['productVersionId','productCode','name','capacityProviderName','distributionEligible','ratingReady']);
 s.AgencyEvidenceFile=o({id,agencyId:id,fileName:t(150),contentType:e('application/pdf','image/png','image/jpeg','text/plain'),byteLength:{type:'integer',minimum:1,maximum:10485760},sha256:hash,screeningState:e('pending','demo-cleared','rejected'),uploadedAt:instant});
 s.AgencyEvidence=o({id,agencyId:id,kind:evidenceKind,state:e('pending','verified','rejected','unavailable'),fileId:id,inputFingerprint:hash,ruleVersionId:id,attestedBy:id,recordedAt:instant,verifiedAt:instant,expiresOn:date,resultCode:t(100),notes:t(1000)},['id','agencyId','kind','state','inputFingerprint','ruleVersionId','recordedAt','resultCode']);
 s.AgencyEvidenceWrite=o({kind:e('toba','professional-indemnity','dpa','client-money'),fileId:id,notes:t(1000),expiresOn:date},['kind','fileId','notes']);
 s.AgencyCheckWrite=o({kind:providerKind});
 s.AgencyCheckAttempt=o({id,agencyId:id,kind:providerKind,state:e('pending','running','passed','refer','unavailable'),inputFingerprint:hash,ruleVersionId:id,createdAt:instant,completedAt:instant,resultCode:t(100),evidenceId:id},['id','agencyId','kind','state','inputFingerprint','ruleVersionId','createdAt']);
 s.AgencyUser=o({id,agencyId:id,displayName:t(),email,role,state:e('invited','active','inactive'),createdAt:instant,etag,lastSeenAt:instant},['id','agencyId','displayName','email','role','state','createdAt','etag']);
 s.AgencyUserWrite=o({displayName:t(),role,reason:t(1000)});
 s.AgencyInvitationWrite=o({email,displayName:t(),role});
 s.AgencyNotification=o({id,agencyId:id,kind:e('invitation','activation','suspension','reactivation','terms-change'),state:e('queued','processing','demo-delivered','rejected','exhausted','superseded'),attempts:integer,createdAt:instant,completedAt:instant,lastResultCode:t(100),retryAllowed:b,etag},['id','agencyId','kind','state','attempts','createdAt','retryAllowed','etag']);
 const invitationCommon={id,userId:id,agencyId:id,email,role,createdAt:instant,etag};
 s.Invitation={oneOf:[o({...invitationCommon,state:{const:'staged'}}),o({...invitationCommon,state:e('pending','accepted','expired','revoked'),issuedAt:instant,expiresAt:instant,notificationId:id,acceptedAt:instant,revokedAt:instant},[...Object.keys(invitationCommon),'state','issuedAt','expiresAt','notificationId']),o({...invitationCommon,state:{const:'revoked'},revokedAt:instant})]};
 s.Invitation.description='Staging has no token, expiry or delivery job. Issuance expires exactly 14 real-time days later. State is acceptance validity, not delivery. Resend revokes the predecessor and creates a fresh invitation.';
 const proposal={id,agencyId:id,requestedBy:id,reason:t(1000),baseVersion:etag,inputFingerprint:hash,createdAt:instant,state:e('pending','applied','rejected','stale'),decisionBy:id,decisionReason:t(1000),decidedAt:instant};
 const proposalRequired=['id','agencyId','requestedBy','reason','baseVersion','inputFingerprint','createdAt','state'];
 s.AgencyStateRequest=o({...proposal,requestKind:e('activation','suspension','reactivation'),stateRequested:e('active','suspended'),etag,requestedByLabel:t(200),decisionByLabel:t(200)},[...proposalRequired,'requestKind','stateRequested','etag','requestedByLabel']);
 const termsCommercial=o(structuredClone(commercial),['effectiveFrom','commissionBasis','feeSharing','volumeCommitmentMode','minimumPremiumOverrideMode','referralRouting']);
 // Drafts may be incomplete. A proposal must provide every value selected by a mode.
 termsCommercial.allOf=[
  {if:{properties:{commissionBasis:{const:'flat-rate'}},required:['commissionBasis']},then:{properties:{flatCommissionBasisPoints:bps},required:['flatCommissionBasisPoints']}},
  {if:{properties:{feeSharing:{const:'agreed-split'}},required:['feeSharing']},then:{properties:{feeShareBasisPoints:bps},required:['feeShareBasisPoints']}},
  {if:{properties:{volumeCommitmentMode:{enum:['target-no-penalty','target-tiered']}},required:['volumeCommitmentMode']},then:{properties:{volumeCommitment:money},required:['volumeCommitment']}},
  {if:{properties:{minimumPremiumOverrideMode:{const:'capacity-provider-agreed'}},required:['minimumPremiumOverrideMode']},then:{properties:{minimumPremiumOverride:money},required:['minimumPremiumOverride']}}
 ];
 s.AgencyTermsWrite=o({effectiveFrom:date,reason:t(1000),commercialTerms:termsCommercial,settlement:o(structuredClone(settlement)),paymentTermsDays:{type:'integer',enum:[30,45,60]},creditLimit:money,products:{...bounded(r('AgencyProductWrite'),3),minItems:1}});
 s.AgencyTermsRequest=o({...proposal,...structuredClone(s.AgencyTermsWrite.properties),etag,requestedByLabel:t(200),decisionByLabel:t(200)},[...new Set([...proposalRequired,...s.AgencyTermsWrite.required,'etag','requestedByLabel'])]);
 s.AgencyTermsVersion=o({id,agencyId:id,version:{type:'integer',minimum:1},approvedRequestId:id,approvedRequestKind:e('activation','terms'),status:e('current','scheduled','historical'),...Object.fromEntries(Object.entries(structuredClone(s.AgencyTermsWrite.properties)).filter(([k])=>k!=='reason')),effectiveTo:date,createdAt:instant},['id','agencyId','version','approvedRequestId','approvedRequestKind','status','effectiveFrom','commercialTerms','settlement','paymentTermsDays','creditLimit','products','createdAt']);
 s.AgencyMatrixColumn=o({role:t(50),label:t(100)});
 s.AgencyMatrixCell=o({role:t(50),allowed:b,available:b,label:t(200)});
 s.AgencyMatrixRow=o({capability:t(100),label:t(100),cells:bounded(r('AgencyMatrixCell'),4)});
 s.AgencyPermissionMatrix=o({columns:bounded(r('AgencyMatrixColumn'),4),rows:bounded(r('AgencyMatrixRow'),8)});
 s.AgencyPermissionRequest=o({id,agencyId:id,permission:{const:'bordereau-download'},requestedBy:id,requestedByLabel:t(200),reason:t(1000),state:e('pending','granted','rejected'),createdAt:instant,decisionBy:id,decisionByLabel:t(200),decidedAt:instant,decisionReason:t(1000),etag},['id','agencyId','permission','requestedBy','requestedByLabel','reason','state','createdAt','etag']);
 s.AgencyPermissionGrant=o({id,agencyId:id,permission:{const:'bordereau-download'},requestId:id,grantedBy:id,grantedByLabel:t(200),grantedAt:instant,revokedBy:id,revokedByLabel:t(200),revokedAt:instant,revocationReason:t(1000),etag},['id','agencyId','permission','requestId','grantedBy','grantedByLabel','grantedAt','etag']);
 s.AgencyActivity=o({id,occurredAt:instant,actorLabel:t(),action:t(100),summary:t(1000)});
 s.AgencySharedProduct=o({productCode,name:t(),effectiveFrom:date,effectiveTo:date,available:b},['productCode','name','effectiveFrom','available']);
 s.AgencySharedQuote=o({id,reference:t(40),clientName:t(),productCode:e('motor-trade-road-risks','motor-trade-combined'),state:e('draft','withdrawn'),updatedAt:instant,startDate:date},['id','reference','clientName','productCode','state','updatedAt']);
 s.AgencySharedPolicy=o({id,reference:t(40),clientName:t(),productCode:e('motor-trade-road-risks','motor-trade-combined'),state:e('scheduled','active','expired','cancelled'),startsAt:instant,endsAt:instant});
 s.AgencySharing=o({policies:o({state:{const:'available'},totalCount:integer}),quotes:o({state:{const:'available'},totalCount:integer}),agency:o({id,reference:t(40),legalName:t(),state}),products:bounded(r('AgencySharedProduct'),3),permissions:bounded(o({permission:{const:'bordereau-download'},granted:b,available:b}),1),unavailableSections:bounded(r('AgencyUnavailableSection'),4)});
 s.AgencySharedClient=o({id,relationshipId:id,reference:t(40),legalName:t()});
 s.AgencySharedContact=o({id,personId:id,fullName:t(),role:t(100),email,telephone:t(50),isPrimary:b},['id','personId','fullName','role','isPrimary']);
 s.AgencySharedInstruction=o({id,personId:id,contactName:t(200),instruction:t(1000),reviewOn:date},['id','personId','contactName','instruction']);

 // Rebuild existing operations using the shared security/concurrency conventions,
 // retaining the exact IDs used by the reviewed prototype inventory.
 function replace(method,path,permission,options){
  const previous=paths[path]?.[method];if(!previous)throw new Error(`Missing agency operation ${method} ${path}`);
  delete paths[path][method];op(method,path,previous.operationId,permission,options);
 }
 const out=(path,method,schema,status=200)=>{paths[path][method].responses[status].content['application/json'].schema=r(schema);};
 out('/agencies','get','AgencyList');
 s.AgencyList=o({items:bounded(r('AgencySummary'),100),nextCursor:t(2048),totalCount:integer},['items','totalCount']);
 if(!paths['/agencies'].get.parameters.some(p=>p.name==='relationshipManagerId'&&p.in==='query'))paths['/agencies'].get.parameters.push({name:'relationshipManagerId',in:'query',schema:id});
 op('get','/agencies/kpis','getAgencyKpis','agency-read',{output:r('AgencyKpis')});
 list('/agency-relationship-managers','listAgencyRelationshipManagers','agency-read',o({id,displayName:t()}));
 op('get','/agency-product-catalog','listAgencyProductCatalog','agency-read',{output:o({items:bounded(r('AgencyProductCatalogItem'),3)})});
 replace('post','/agencies','agency-admin',{input:r('AgencyDraftSave'),output:o({id}),status:201,summary:'Create an incomplete agency with answers, selected step and optional products atomically; return ID/ETag then authorized GET'});
 replace('put','/agencies/{agencyId}','agency-admin',{existing:true,input:r('AgencyDraftSave'),output:o({id}),summary:'Save draft answers, selected step and optional products atomically; return ID/ETag then authorized GET'});
 replace('get','/agencies/{agencyId}/products','agency-read',{output:o({items:bounded(r('AgencyProduct'),3)})});
 replace('put','/agencies/{agencyId}/products','agency-admin',{existing:true,input:o({products:bounded(r('AgencyProductWrite'),3),reason:t(1000)}),output:o({id}),summary:'Replace draft product selections only; server owns row IDs and rejects duplicate or unknown products; selection grants no access'});
 replace('post','/agencies/{agencyId}/abandon','agency-admin',{existing:true,input:reason,output:o({id}),summary:'Abandon draft only; retain history and revoke staged or pending invitations when implemented'});
 replace('post','/agencies/{agencyId}/evidence','agency-admin',{existing:true,input:r('AgencyEvidenceWrite'),output:o({id}),status:201});
 replace('post','/agencies/{agencyId}/checks','agency-admin',{existing:true,input:r('AgencyCheckWrite'),output:o({id}),status:202});
 list('/agencies/{agencyId}/checks','listAgencyChecks','agency-read',r('AgencyCheckAttempt'));
 replace('post','/agencies/{agencyId}/validate','agency-admin',{existing:true,idempotent:false,output:r('AgencyChecklist'),summary:'Evaluate current readiness without changing state or caching a receipt; current agency ETag and CSRF required'});
 op('post','/agencies/{agencyId}/evidence-files','uploadAgencyEvidenceFile','agency-admin',{existing:true,output:o({id}),status:201});
 paths['/agencies/{agencyId}/evidence-files'].post.requestBody={required:true,content:{'multipart/form-data':{schema:o({file:{type:'string',format:'binary',maxLength:10485760},fileName:t(150),contentType:s.AgencyEvidenceFile.properties.contentType})}}};
 list('/agencies/{agencyId}/evidence-files','listAgencyEvidenceFiles','agency-read',r('AgencyEvidenceFile'));
 for(const [suffix,name,schema] of [['evidence-files','EvidenceFile','AgencyEvidenceFile'],['evidence','Evidence','AgencyEvidence'],['checks','CheckAttempt','AgencyCheckAttempt']])op('get',`/agencies/{agencyId}/${suffix}/{recordId}`,`getAgency${name}`,'agency-read',{output:r(schema)});
 op('get','/agencies/{agencyId}/evidence-files/{fileId}/content','downloadAgencyEvidenceFile','agency-read',{output:r('AgencyEvidenceFile')});
 const download=paths['/agencies/{agencyId}/evidence-files/{fileId}/content'].get;
 download.responses[200].content={'application/octet-stream':{schema:{type:'string',format:'binary'}}};
 Object.assign(download.responses[200].headers,{'Content-Disposition':{description:'Attachment with sanitized filename.',schema:t(300)},'X-Content-Type-Options':{description:'Block MIME sniffing.',schema:{const:'nosniff'}}});
 for(const action of ['activate','suspend','reactivate'])replace('post',`/agencies/{agencyId}/${action}`,'agency-admin',{existing:true,input:reason,output:o({id}),status:202,summary:`Request ${action}; bind current agency version, require independent countersign and atomic application`});
 replace('get','/agency-state-requests/{requestId}','agency-admin',{output:r('AgencyStateRequest')});
 replace('post','/agency-state-requests/{requestId}/decision','agency-admin',{existing:true,input:o({outcome:e('approve','reject'),reason:t(1000)}),output:o({id}),summary:'Apply an independent current reviewer decision atomically; return only request ID and ETag, then authorized GET'});

 s.AgencyUserList=o({items:bounded(r('AgencyUser'),100),totalCount:integer,nextCursor:t(2048)},['items','totalCount']);
 s.AgencyInvitationList=o({items:bounded(r('Invitation'),100),totalCount:integer,nextCursor:t(2048)},['items','totalCount']);
 out('/agencies/{agencyId}/users','get','AgencyUserList');
 out('/agencies/{agencyId}/invitations','get','AgencyInvitationList');
 for(const suffix of ['users','invitations'])paths[`/agencies/{agencyId}/${suffix}`].get['x-permission']='internal-agency-admin-or-own-broker-admin';
 paths['/agencies/{agencyId}/invitations'].get.parameters.push({name:'userId',in:'query',schema:id});
 op('get','/agencies/{agencyId}/users/{userId}','getAgencyUser','internal-agency-admin-or-own-broker-admin',{output:r('AgencyUser')});
 op('get','/agencies/{agencyId}/invitations/{invitationId}','getAgencyInvitation','internal-agency-admin-or-own-broker-admin',{output:r('Invitation')});
 op('put','/agencies/{agencyId}/users/{userId}','updateAgencyUser','internal-agency-admin-or-own-broker-admin',{existing:true,input:r('AgencyUserWrite'),output:o({id}),summary:'Edit display name/broker role with user ETag; identity-only receipt, immutable email/agency, role changes revoke sessions'});
 for(const action of ['deactivate','reactivate'])op('post',`/agencies/{agencyId}/users/{userId}/${action}`,`${action}AgencyUser`,'internal-agency-admin-or-own-broker-admin',{existing:true,input:reason,output:o({id}),summary:`${action} agency user with user ETag; return identity only then refresh; never restore old sessions or invitations`});
 replace('post','/agencies/{agencyId}/invitations','internal-agency-admin-or-own-broker-admin',{existing:true,input:r('AgencyInvitationWrite'),output:o({id,invitationId:id}),status:201,summary:'Create user/invitation using agency ETag; return user ID and invitation ID; draft staging has no token or delivery'});
 for(const action of ['resend','revoke'])replace('post',`/invitations/{invitationId}/${action}`,'internal-agency-admin-or-own-broker-admin',{existing:true,input:reason,output:o({id,userId:id}),status:action==='resend'?202:200,summary:`${action} with invitation ETag; identity-only receipt, history retained and current authority before replay`});
 op('post','/invitations/{invitationId}/demo-link','revealDemoInvitationLink','internal-agency-user-admin-development-only',{idempotent:false,output:o({invitationToken:{type:'string',pattern:'^[A-Za-z0-9_-]{43}$'}}),summary:'Development-only audited secret reveal; no-store, no receipt, authenticated internal agency user admin and CSRF required'});
 paths['/invitations/{invitationId}/demo-link'].post.responses[200].headers['Cache-Control']={description:'Never cache an invitation secret.',schema:{const:'no-store'}};
 replace('post','/auth/invitations/accept','invitation-token-owner',{publicAuth:true,input:o({invitationToken:{type:'string',pattern:'^[A-Za-z0-9_-]{43}$'},password:{type:'string',minLength:12,maxLength:128}}),output:o({accepted:{const:true}}),summary:'Consume a current one-time invitation with password setup; trusted stored role only, no automatic login'});
 list('/agencies/{agencyId}/terms-requests','listAgencyTermsRequests','agency-admin',r('AgencyTermsRequest'));
 list('/agencies/{agencyId}/terms','listAgencyTermsVersions','agency-read',r('AgencyTermsVersion'));
 replace('post','/agencies/{agencyId}/terms-requests','agency-admin',{existing:true,input:r('AgencyTermsWrite'),output:o({id}),status:202,summary:'Propose complete agreed terms without changing current terms; return request ID and ETag'});
 replace('get','/agency-terms-requests/{requestId}','agency-admin',{output:r('AgencyTermsRequest')});
 replace('post','/agency-terms-requests/{requestId}/decision','agency-admin',{existing:true,input:o({outcome:e('approve','reject'),reason:t(1000)}),output:o({id}),summary:'Independently approve or reject an immutable terms proposal; return ID and request ETag'});
 paths['/agencies/{agencyId}/terms-requests'].get['x-permission']='agency-admin';
 paths['/agencies/{agencyId}/terms'].get['x-permission']='agency-read';
 s.AgencyTermsRequestList=o({items:bounded(r('AgencyTermsRequest'),100),totalCount:integer,nextCursor:t(2048)},['items','totalCount']);
 s.AgencyTermsVersionList=o({items:bounded(r('AgencyTermsVersion'),100),totalCount:integer,asOf:instant,nextCursor:t(2048)},['items','totalCount','asOf']);
 out('/agencies/{agencyId}/terms-requests','get','AgencyTermsRequestList');out('/agencies/{agencyId}/terms','get','AgencyTermsVersionList');
 paths['/agencies/{agencyId}/products'].get.description+=' Active/suspended agencies return currently effective approved product grants; future and historical selections are available through terms history. Draft/abandoned records retain draft selection history. Product/terms effectiveTo is exclusive.';
 for(const [suffix,name,schema] of [['activity','Activity','AgencyActivity'],['notifications','Notifications','AgencyNotification'],['permission-requests','PermissionRequests','AgencyPermissionRequest'],['permission-grants','PermissionGrants','AgencyPermissionGrant']])list(`/agencies/{agencyId}/${suffix}`,`listAgency${name}`,suffix === 'activity' ? 'agency-read' : 'agency-admin',r(schema));
 op('get','/agencies/{agencyId}/permission-matrix','getAgencyPermissionMatrix','agency-admin',{output:r('AgencyPermissionMatrix')});
 paths['/agencies/{agencyId}/activity'].get['x-permission']='agency-read';
 op('get','/agencies/{agencyId}/notifications/{notificationId}','getAgencyNotification','agency-admin',{output:r('AgencyNotification')});
 op('post','/agencies/{agencyId}/notifications/{notificationId}/retry','retryAgencyNotification','agency-admin',{existing:true,input:reason,output:o({id}),status:202});
 op('post','/agencies/{agencyId}/permission-requests','requestAgencyPermission','agency-user-admin',{existing:true,input:o({permission:{const:'bordereau-download'},reason:t(1000)}),output:o({id}),status:202});
 op('post','/agencies/{agencyId}/permission-requests/{requestId}/decision','decideAgencyPermission','agency-admin-no-self-approval',{existing:true,input:o({outcome:e('approve','reject'),reason:t(1000)}),output:o({id})});
 op('post','/agencies/{agencyId}/permission-grants/{grantId}/revoke','revokeAgencyPermission','agency-admin',{existing:true,input:reason,output:o({id})});
 for(const [suffix,status] of [['permission-requests',202],['permission-requests/{requestId}/decision',200],['permission-grants/{grantId}/revoke',200]]) {
  const operation=paths[`/agencies/{agencyId}/${suffix}`].post,response=operation.responses[status];
  response.description='ID-only committed receipt; read the current permission list for current state and resource ETag.';
  delete response.headers.Location;
  if(suffix==='permission-requests')response.headers.ETag.description='Agency version after the request. Decision writes use the request etag from the permission list.';
  for(const code of [413,415])operation.responses[code]={description:'Invalid body size or content type.',content:{'application/problem+json':{schema:r('Problem')}}};
 }
 op('get','/agencies/{agencyId}/sharing','getAgencySharingPreview','agency-read-audited',{output:r('AgencySharing')});
 op('get','/agency-context','getCurrentAgencyContext','active-own-agency',{output:r('AgencySharing')});
 paths['/agency-context'].get.responses['200'].headers.ETag={description:'Current agency version for own-agency commands.',schema:{type:'string'}};
 for(const [prefix,suffix] of [['/agencies/{agencyId}/sharing','Preview'],['/agency-context','Current']]){
  const permission=suffix==='Preview'?'agency-read-audited':'active-own-agency';
  list(`${prefix}/quotes`,`list${suffix}AgencyQuotes`,permission,r('AgencySharedQuote'),[['q',t(200)]]);
  list(`${prefix}/policies`,`list${suffix}AgencyPolicies`,permission,r('AgencySharedPolicy'),[['q',t(200)]]);
  op('get',`${prefix}/policies/{policyId}`,`get${suffix}AgencyPolicy`,permission,{output:r('AgencySharedPolicy')});
  list(`${prefix}/clients`,`list${suffix}AgencyClients`,permission,r('AgencySharedClient'),[['q',t()]]);
  list(`${prefix}/relationships/{relationshipId}/contacts`,`list${suffix}AgencyContacts`,permission,r('AgencySharedContact'));
  list(`${prefix}/relationships/{relationshipId}/instructions`,`list${suffix}AgencyInstructions`,permission,r('AgencySharedInstruction'));
 }
 for(const path of ['/agencies/{agencyId}/statement','/agencies/{agencyId}/statement/export'])for(const operation of Object.values(paths[path])){
  operation['x-owning-phase']=10;operation.description+=' Unavailable in Phase 4; do not return fabricated balances or enqueue exports before Phase 10 implementation.';
 }
 for(const [path,methods] of Object.entries(paths))if(/^\/(agencies|agency-|invitations)/.test(path))for(const operation of Object.values(methods)){
  operation.description+=' Recheck current agency and actor scope before receipts/data. Agency mutations lock Agency, then ordered users, then child records. A proposal has its own ETag and creation does not advance its agency base. See Phase 4 data/access protocols.';
 }
}
