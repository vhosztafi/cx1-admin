export function addAdministrationContracts({schemas,ref,operation,paths,id,instant,text,object,array}) {
 const nullableInstant={anyOf:[instant,{type:'null'}]};
 schemas.AdminProductVersion=object({id,productId:id,productCode:text(),productName:text(),version:{type:'integer',minimum:1},
  state:{enum:['draft','published','retired']},providerId:id,effectiveFrom:instant,effectiveTo:nullableInstant,
  schemaVersion:text(),questionSetVersion:text(),coverSections:array(text(100)),etag:text(100)});
 schemas.AdminProvider=object({id,code:text(50),name:text(),state:{enum:['active','inactive']},etag:text(100)});
 schemas.AdminProductEdit=object({providerId:id,effectiveFrom:instant,effectiveTo:nullableInstant,coverSections:{type:'array',minItems:1,maxItems:4,uniqueItems:true,items:text(100)},reason:text(1000)});
 schemas.AdminProviderEdit=object({code:{type:'string',minLength:1,maxLength:50,pattern:'^[A-Za-z0-9-]+$'},name:text(),state:{enum:['active','inactive']},reason:text(1000)});
 const permission='current active internal system administrator, checked in SQL before replay';
 const safeJson={description:'Only explicit non-secret field names are disclosed; omitted fields do not imply unchanged values.',anyOf:['object','array','string','number','boolean','null'].map(type=>({type}))};
 const job=object({id,kind:text(60),state:text(30),attempts:{type:'integer'},attemptLimit:{type:'integer'},createdAt:instant,nextAttemptAt:instant,completedAt:nullableInstant,errorCode:{type:['string','null']},subjectRecordId:{anyOf:[id,{type:'null'}]},scenarioVersionId:{anyOf:[id,{type:'null'}]},retryAllowed:{type:'boolean'},etag:text(100)});
 const audit=object({id,actorId:{anyOf:[id,{type:'null'}]},eventType:text(100),occurredAt:instant,subjectRecordId:{anyOf:[id,{type:'null'}]},correlationId:id,reason:{type:['string','null'],maxLength:1000}});
 delete paths['/admin/audit'];delete paths['/admin/audit/{id}'];
 operation('get','/admin/audit','getAdministrationAudit',permission,{output:object({items:array(object({...audit.properties,actorLabel:text(),summary:text(500)},['id','eventType','occurredAt','actorLabel','summary','correlationId'])),nextCursor:text(5000),totalCount:{type:'integer'}},['items','totalCount'])});
 paths['/admin/audit'].get.parameters=[...['actorId','subjectRecordId','from','to','eventType','reason','cursor'].map(name=>({name,in:'query',schema:{type:'string'}})),{name:'pageSize',in:'query',schema:{type:'integer',minimum:1,maximum:100}}];
 operation('get','/admin/audit/{id}','getAdministrationAuditDetail',permission,{output:object({...audit.properties,before:safeJson,after:safeJson})});
 operation('get','/admin/integration-health','getAdministrationIntegrationHealth',permission,{output:object({families:array(object({kind:text(60),queued:{type:'integer'},succeeded:{type:'integer'},failed:{type:'integer'},lastSuccess:nullableInstant,lastFailure:nullableInstant})),settings:array(object({id,scope:text(120),version:{type:'integer'},effectiveFrom:instant,scenario:{type:['string','null']},options:array(text(60)),etag:text(100)})),scenarioEditing:{type:'boolean'}})});
 operation('post','/admin/integration-scenarios','publishAdministrationIntegrationScenario',permission+'; Development environment only',{input:object({scope:text(120),scenario:text(60),reason:text(1000)}),output:object({id,scope:text(120),version:{type:'integer'},scenario:text(60),etag:text(100)}),existing:true,status:201});
 operation('get','/admin/oversight/jobs','getAdministrationJobHistory',permission,{output:object({items:array(job),totalCount:{type:'integer'},asOf:instant,nextOffset:{type:['integer','null']}})});
 paths['/admin/oversight/jobs'].get.parameters=[{name:'offset',in:'query',schema:{type:'integer',minimum:0,maximum:10000}},...['kind','state','asOf'].map(name=>({name,in:'query',schema:{type:'string'}}))];
 operation('get','/admin/oversight/jobs/{id}','getAdministrationJobAttempts',permission,{output:object({job,attempts:array(object({id,attemptNumber:{type:'integer'},startedAt:instant,endedAt:nullableInstant,outcome:text(100),errorCode:{type:['string','null']}})),providerOperations:array(object({id,createdAt:instant}))})});
 for(const path of Object.keys(paths))if(path==='/admin/users'||path.startsWith('/admin/users/')||path==='/admin/teams'||path.startsWith('/admin/teams/'))delete paths[path];
 schemas.AdminUserChange=object({userId:id,etag:text(100),email:{type:'string',format:'email',maxLength:254},teamId:id,roles:{type:'array',minItems:1,maxItems:10,uniqueItems:true,items:text(60)},resetMfa:{type:'boolean'},reason:text(1000)});
 schemas.AdminUserChange.properties.selfRequested={type:'boolean'};
 schemas.AdminUser=object({id,email:{type:'string',format:'email'},displayName:text(),state:{enum:['invited','active','suspended']},teamId:id,roles:array(text(60)),etag:text(100),mfaEnabled:{type:'boolean'},mustReset:{type:'boolean'}});
 schemas.AdminUserRequest=object({id,kind:{const:'user'},state:{enum:['pending','approved','rejected']},requestedBy:id,requestedAt:instant,proposal:ref('AdminUserChange'),decidedBy:{anyOf:[id,{type:'null'}]},decisionReason:{anyOf:[text(1000),{type:'null'}]},etag:text(100)});
 operation('get','/admin/users','getAdministrationUsers',permission,{output:object({users:array(ref('AdminUser')),teams:array(object({id,name:text(100),etag:text(100)})),roles:array(text(60)),requests:array(ref('AdminUserRequest')),deliveries:array(object({id,userId:id,kind:{enum:['invitation','password-reset']},expiresAt:instant})),demoDelivery:{type:'boolean'}})});
 operation('post','/admin/users/invitations','inviteInternalUser',permission,{input:object({email:{type:'string',format:'email'},displayName:text(),teamId:id,reason:text(1000)}),output:object({id,deliveryId:id}),status:201});
 operation('post','/admin/teams','createInternalTeam',permission,{input:object({name:text(100),reason:text(1000)}),output:object({id})});
 operation('put','/admin/teams/{id}','renameInternalTeam',permission,{input:object({name:text(100),reason:text(1000)}),output:object({id}),existing:true});
 operation('post','/admin/users/requests','proposeInternalUserChange',permission,{input:ref('AdminUserChange'),output:ref('AdminUserRequest'),status:201});
 operation('post','/admin/users/requests/{id}/decision','decideInternalUserChange',permission,{input:object({approve:{type:'boolean'},reason:text(1000)}),output:ref('AdminUserRequest'),existing:true});
 for(const action of ['suspend','resume','force-reset','revoke-sessions'])operation('post',`/admin/users/{id}/${action}`,'administrationUser'+action.replaceAll('-',''),permission,{input:object({reason:text(1000)}),output:object({id,state:{enum:['invited','active','suspended']}}),existing:true});
 operation('post','/admin/identity-deliveries/{id}/demo-reveal','revealInternalDemoDelivery','Development only; current system administrator; no public token or receipt',{idempotent:false,output:object({token:text(43)})});
 operation('post','/auth/internal-invitation','acceptInternalInvitation','anonymous CSRF rate-limited one-use invitation',{publicAuth:true,idempotent:false,input:object({token:text(43),password:{type:'string',minLength:12,maxLength:128,writeOnly:true}}),output:object({state:{const:'accepted'}})});
 for(const path of Object.keys(paths))if(path.startsWith('/account/mfa/')||path==='/account/password'||path==='/account/sessions'||path.startsWith('/account/sessions/')||path==='/account/identity-requests'||path==='/account/security-reports'||path.startsWith('/auth/password-reset/')||path==='/auth/mfa/verify')delete paths[path];
 delete paths['/account'].put;
 const proof=object({password:{type:'string',maxLength:1024,writeOnly:true},code:{anyOf:[text(100),{type:'null'}]}},['password']);
 proof.properties.deviceName=text(100);proof.properties.reason=text(1000);
 const preferences=object({fullName:text(),telephone:{type:'string',maxLength:50},jobTitle:{type:'string',maxLength:200},outOfOffice:{type:'boolean'},taskDigest:{type:'boolean'}});
 const state=object({state:text(60)}),codes=object({recoveryCodes:array(text(43))});
 schemas.AccountSecurity=object({id,preferences,taskDigest:{type:['integer','null']},notices:array(object({id,eventType:text(100),occurredAt:instant})),displayName:text(),email:{type:'string',format:'email'},teamId:{anyOf:[id,{type:'null'}]},etag:text(100),mfaEnabled:{type:'boolean'},currentRoles:array(text(60)),roles:array(text(60)),teams:array(object({id,name:text(100)})),requests:array(ref('AdminUserRequest')),reports:array(object({id,occurredAt:instant,reason:text(1000)})),sessions:array(object({id,deviceLabel:text(),createdAt:instant,lastSeenAt:instant,expiresAt:instant,current:{type:'boolean'}}))});
 operation('get','/account/security','getAccountSecurity','current account owner',{output:ref('AccountSecurity')});
 operation('put','/account/profile','saveOwnProfile','current account owner with exact profile version',{idempotent:false,input:object({displayName:text(),etag:text(100),preferences},['displayName','etag']),output:state});
 operation('post','/account/identity-requests','requestAccountIdentityChange','current internal account owner',{idempotent:false,input:object({email:{type:'string',format:'email'},teamId:id,roles:array(text(60)),reason:text(1000)}),output:state});
 operation('post','/account/password','changeAccountPassword','current account owner and recent password plus factor if enabled',{idempotent:false,input:object({...proof.properties,newPassword:{type:'string',minLength:12,maxLength:128,writeOnly:true}},['password','newPassword']),output:state});
 operation('post','/auth/password-forgot','requestLocalPasswordReset','anonymous generic response',{publicAuth:true,idempotent:false,input:object({email:{type:'string',format:'email'}}),output:object({state:text(),message:text(500)})});
 operation('post','/auth/password-reset','completeLocalPasswordReset','one-use token plus factor if MFA enabled',{publicAuth:true,idempotent:false,input:object({token:text(43),newPassword:{type:'string',minLength:12,maxLength:128,writeOnly:true},code:{anyOf:[text(100),{type:'null'}]}},['token','newPassword']),output:state});
 operation('post','/account/mfa/enrolments','beginLocalMfaEnrolment','current account owner and recent password',{idempotent:false,input:proof,output:object({enrolmentId:id,secret:text(32),otpauthUri:{type:'string',format:'uri'},expiresAt:instant})});
 operation('post','/account/mfa/enrolments/{id}/confirm','confirmLocalMfaEnrolment','current account owner and valid unreplayed factor',{idempotent:false,input:object({code:{type:'string',pattern:'^[0-9]{6}$'}}),output:object({state:{const:'enabled'},...codes.properties})});
 operation('post','/account/mfa/enrolments/{id}/cancel','cancelLocalMfaEnrolment','current account owner',{idempotent:false,output:state});
 operation('post','/account/mfa/recovery-codes','replaceLocalRecoveryCodes','current account owner and recent password plus valid factor',{idempotent:false,input:proof,output:codes});
 operation('post','/account/mfa/disable','disableLocalMfa','current account owner and recent password plus valid factor',{idempotent:false,input:proof,output:state});
 const authenticated=object({state:{const:'authenticated'},user:ref('Actor')});
 paths['/auth/login'].post.responses['200'].content={'application/json':{schema:{oneOf:[authenticated,object({state:{const:'mfa-required'},challengeToken:text(43)})]}}};
 operation('post','/auth/mfa','completeLocalMfaChallenge','valid unexpired one-use challenge and factor',{publicAuth:true,idempotent:false,input:object({challengeToken:text(43),code:text(100)}),output:authenticated});
 for(const [path,name] of [['/account/sessions/{id}/revoke','revokeOwnSession'],['/account/sessions/revoke-others','revokeOtherAccountSessions'],['/account/sessions/revoke-all','revokeAllAccountSessions'],['/account/security-reports','saveAccountSecurityReport']])operation('post',path,name,'current account owner',{idempotent:false,input:object({reason:text(1000)}),output:state});
 delete paths['/admin/templates/{configurationId}/preview'];
 const flag=object({enabled:{type:'boolean'},maximumReviewDays:{type:'integer',minimum:1,maximum:3650},agencySharingAllowed:{type:'boolean'}});
 const organisation=object({name:text(),clientReferencePrefix:{type:'string',pattern:'^[A-Z][A-Z0-9]{1,7}$'},notificationsEnabled:{type:'boolean'},notificationSignature:{type:'string',maxLength:1000}});
 const message=object({name:text(),body:text(7000),enabled:{type:'boolean'}});
 const matching=object({id,version:{type:'integer'},duplicateQuotePolicy:{enum:['refer','allow-competing','broker-of-record']},requireReview:{type:'boolean'},summary:text(1000),brokerOfRecordDays:{type:['integer','null'],minimum:1,maximum:3650}},['id','version','duplicateQuotePolicy','requireReview','summary']);
 const workflow=object({format:{const:'workflow-task-1'},publication:{const:'published'},code:text(64),family:text(64),taskType:text(64),title:text(),priority:{enum:['low','normal','high','urgent']},initialState:{enum:['open','awaiting-information']},dueDays:{type:'integer',minimum:0,maximum:365},leadDays:{type:'integer',minimum:0,maximum:365},checklist:{type:'array',maxItems:20,items:object({code:text(64),label:text(300),required:{type:'boolean'}})},assignmentTeamId:{anyOf:[id,{type:'null'}]}});
 workflow.required=workflow.required.filter(x=>x!=='assignmentTeamId');
 schemas.AdminConfigurationValues={oneOf:[flag,organisation,message,matching,workflow]};
 schemas.AdminConfigurationEdit={oneOf:[[{const:'organisation'},organisation],[{const:'matching-rule'},matching],[{const:'message-template/standard'},message],[{type:'string',pattern:'^workflow-task/'},workflow],[{type:'string',pattern:'^flag-definition/'},flag]].map(([scope,values])=>object({scope,values,effectiveFrom:instant,reason:text(1000)}))};
 schemas.AdminSetting=object({id,scope:text(120),version:{type:'integer'},values:ref('AdminConfigurationValues'),effectiveFrom:nullableInstant,etag:text(100)});
 schemas.AdminTemplateEdit=object({title:text(),notice:text(4000),effectiveFrom:instant,effectiveTo:instant,reason:text(1000)});
 schemas.AdminTemplate=object({id,productId:id,code:text(),kind:text(),version:{type:'integer'},effectiveFrom:instant,effectiveTo:instant,values:object({format:text(),title:text(),notice:text(4000)}),etag:text(100)});
 operation('get','/admin/configuration','getAdministrationConfiguration',permission,{output:object({settings:array(ref('AdminSetting')),templates:array(ref('AdminTemplate')),teams:array(object({id,name:text()})),products:array(object({id,name:text()}))})});
 operation('post','/admin/configuration','publishAdministrationConfiguration',permission,{input:ref('AdminConfigurationEdit'),output:ref('AdminSetting'),existing:true,status:201});
 operation('post','/admin/templates/{id}/successor','publishAdministrationTemplate',permission,{input:ref('AdminTemplateEdit'),output:ref('AdminTemplate'),existing:true,status:201});
 operation('post','/admin/templates/{id}/preview','previewAdministrationTemplate',permission,{idempotent:false,input:ref('AdminTemplateEdit'),output:{type:'string',format:'binary'}});
 paths['/admin/templates/{id}/preview'].post.responses['200'].content={'application/pdf':{schema:{type:'string',format:'binary'}}};
 operation('get','/communication/templates','getCommunicationTemplates','current internal message-write',{output:object({items:array(object({id,name:text(),body:text(8000)}))})});
 operation('get','/admin/catalogue','getAdministrationCatalogue',permission,{output:object({products:array(object({id,code:text(),name:text()})),versions:array(ref('AdminProductVersion')),providers:array(ref('AdminProvider'))})});
 for(const [method,path,name,input,output,existing,status] of [
  ['post','/admin/product-versions/{id}/clone','cloneProductVersion','AdminProductEdit','AdminProductVersion',true,201],
  ['put','/admin/product-versions/{id}','saveProductVersion','AdminProductEdit','AdminProductVersion',true,200],
  ['post','/admin/product-versions/{id}/publish','publishAdministrationProductVersion',null,'AdminProductVersion',true,200],
  ['post','/admin/providers','createCapacityProvider','AdminProviderEdit','AdminProvider',false,201],
  ['put','/admin/providers/{id}','updateCapacityProvider','AdminProviderEdit','AdminProvider',true,200]
 ]) operation(method,path,name,permission,{input:input?ref(input):object({reason:text(1000)}),output:ref(output),existing,status});
 schemas.AdminAuthorityLimits={oneOf:[{$ref:'./schemas/underwriting-config.schema.json#/oneOf/1/properties/limits'},{$ref:'./schemas/commercial-underwriting.schema.json#/oneOf/2/properties/limits'}]};
 schemas.AdminAuthorityProposal=object({sourceAuthorityId:id,sourceEtag:text(100),productVersionId:id,effectiveFrom:instant,effectiveTo:instant,
  limits:ref('AdminAuthorityLimits'),routingTeamId:id,userIds:{type:'array',maxItems:100,uniqueItems:true,items:id},reason:text(1000)});
 schemas.AdminAuthority=object({id,productVersionId:id,binderVersionId:id,version:text(60),state:{enum:['published','retired']},productName:text(),effectiveFrom:instant,effectiveTo:instant,limits:ref('AdminAuthorityLimits'),etag:text(100)});
 schemas.AdminAuthorityRequest=object({id,kind:{const:'authority'},state:{enum:['pending','approved','rejected']},requestedBy:id,requestedAt:instant,
  proposal:object({input:ref('AdminAuthorityProposal'),runtimeId:id,binderEtag:text(100),definition:text(65536),version:text(60)}),
  decidedBy:{anyOf:[id,{type:'null'}]},decisionReason:{anyOf:[text(1000),{type:'null'}]},etag:text(100)});
 schemas.AdminAuthorityGrant=object({id,userId:id,authorityVersionId:id,effectiveFrom:instant,effectiveTo:instant,revokedAt:nullableInstant,
  revocationReason:{anyOf:[text(1000),{type:'null'}]},etag:text(100)});
 operation('get','/admin/authority','getAdministrationAuthority',permission,{output:object({versions:array(ref('AdminAuthority')),grants:array(ref('AdminAuthorityGrant')),
  users:array(object({id,displayName:text()})),teams:array(object({id,name:text()})),routingTeamId:{anyOf:[id,{type:'null'}]},requests:array(ref('AdminAuthorityRequest'))})});
 operation('post','/admin/authority/requests','proposeAdministrationAuthority',permission,{input:ref('AdminAuthorityProposal'),output:ref('AdminAuthorityRequest'),status:201});
 operation('post','/admin/authority/requests/{id}/decision','decideAdministrationAuthority',permission,{input:object({approve:{type:'boolean'},reason:text(1000)}),output:ref('AdminAuthorityRequest'),existing:true});
 operation('post','/admin/authority/grants/{id}/revoke','revokeAdministrationAuthority',permission,{input:object({reason:text(1000)}),output:object({id}),existing:true});
 for(const [path,item] of Object.entries(paths)) if(path==='/admin/authority'||path.startsWith('/admin/authority/'))
  for(const op of Object.values(item)){op['x-runtime-status']='phase-11-02-implemented';for(const [code,response]of Object.entries(op.responses))if(Number(code)<300)response.headers={'Cache-Control':{schema:{type:'string',const:'no-store'}}};}
 for(const [path,item] of Object.entries(paths)) if(path==='/admin/catalogue'||path.startsWith('/admin/product-versions/')||path==='/admin/providers'||path==='/admin/providers/{id}')
  for(const op of Object.values(item)) {op['x-runtime-status']='phase-11-01-implemented';for(const [code,response] of Object.entries(op.responses))if(Number(code)<300)response.headers={'Cache-Control':{schema:{type:'string',const:'no-store'}}};}
}

export async function writeAdministrationTypes(){
 const {writeFile}=await import('node:fs/promises');
 await writeFile('contracts/generated/administration.ts',`// Generated by scripts/openapi-administration.mjs.
export type ProductVersion = {id:string;productId:string;productCode:string;productName:string;version:number;state:'draft'|'published'|'retired';providerId:string;effectiveFrom:string;effectiveTo:string|null;schemaVersion:string;questionSetVersion:string;coverSections:string[];etag:string};
export type Provider = {id:string;code:string;name:string;state:'active'|'inactive';etag:string};
export type Catalogue = {products:{id:string;code:string;name:string}[];versions:ProductVersion[];providers:Provider[]};
export type AdministrationUser={id:string;email:string;displayName:string;state:string;teamId:string;roles:string[];etag:string;mfaEnabled:boolean;mustReset:boolean};
export type AdministrationUserChange={userId:string;etag:string;email:string;teamId:string;roles:string[];resetMfa:boolean;reason:string;selfRequested?:boolean};
export type UserAdministrationView={users:AdministrationUser[];teams:{id:string;name:string;etag:string}[];roles:string[];requests:{id:string;requestedBy:string;state:string;proposal:AdministrationUserChange;etag:string;decisionReason:string|null}[];deliveries:{id:string;userId:string;kind:string;expiresAt:string}[];demoDelivery:boolean};
export type AccountSecurityView={preferences:{fullName:string;telephone:string;jobTitle:string;outOfOffice:boolean;taskDigest:boolean};taskDigest:number|null;notices:{id:string;eventType:string;occurredAt:string}[];id:string;displayName:string;email:string;teamId:string;etag:string;mfaEnabled:boolean;currentRoles:string[];roles:string[];teams:{id:string;name:string}[];requests:{id:string;state:string;proposal:{reason:string};decisionReason:string|null}[];sessions:{id:string;deviceLabel:string;createdAt:string;lastSeenAt:string;expiresAt:string;current:boolean}[];reports:{id:string;occurredAt:string;reason:string}[]};
export type MfaEnrolment={enrolmentId:string;secret:string;otpauthUri:string;expiresAt:string};
export type AdministrationSetting={id:string;scope:string;version:number;effectiveFrom:string|null;values:Record<string,unknown>;etag:string};
export type AdministrationTemplate={id:string;productId:string;code:string;kind:string;version:number;effectiveFrom:string;effectiveTo:string;values:{title:string;notice:string};etag:string};
export type ConfigurationAdministrationView={settings:AdministrationSetting[];templates:AdministrationTemplate[];teams:{id:string;name:string}[];products:{id:string;name:string}[]};
export type AuthorityLimits = {[key:string]:string|number|boolean|number[]|AuthorityLimits};
export type AdministrationAuthority = {id:string;productVersionId:string;binderVersionId:string;version:string;state:string;productName:string;effectiveFrom:string;effectiveTo:string;limits:AuthorityLimits;etag:string};
export type AuthorityProposal = {sourceAuthorityId:string;sourceEtag:string;productVersionId:string;effectiveFrom:string;effectiveTo:string;limits:AuthorityLimits;routingTeamId:string;userIds:string[];reason:string};
export type AuthorityRequest = {id:string;kind:'authority';state:'pending'|'approved'|'rejected';requestedBy:string;requestedAt:string;etag:string;decidedBy:string|null;decisionReason:string|null;proposal:{input:AuthorityProposal;runtimeId:string;binderEtag:string;definition:string;version:string}};
export type AuthorityAdministrationView = {versions:AdministrationAuthority[];grants:{id:string;userId:string;authorityVersionId:string;effectiveFrom:string;effectiveTo:string;revokedAt:string|null;revocationReason:string|null;etag:string}[];users:{id:string;displayName:string}[];teams:{id:string;name:string}[];routingTeamId:string|null;requests:AuthorityRequest[]};
`);
}
