export function addAdministrationContracts({schemas,ref,operation,paths,id,instant,text,object,array}) {
 const nullableInstant={anyOf:[instant,{type:'null'}]};
 schemas.AdminProductVersion=object({id,productId:id,productCode:text(),productName:text(),version:{type:'integer',minimum:1},
  state:{enum:['draft','published','retired']},providerId:id,effectiveFrom:instant,effectiveTo:nullableInstant,
  schemaVersion:text(),questionSetVersion:text(),coverSections:array(text(100)),etag:text(100)});
 schemas.AdminProvider=object({id,code:text(50),name:text(),state:{enum:['active','inactive']},etag:text(100)});
 schemas.AdminProductEdit=object({providerId:id,effectiveFrom:instant,effectiveTo:nullableInstant,coverSections:{type:'array',minItems:1,maxItems:4,uniqueItems:true,items:text(100)},reason:text(1000)});
 schemas.AdminProviderEdit=object({code:{type:'string',minLength:1,maxLength:50,pattern:'^[A-Za-z0-9-]+$'},name:text(),state:{enum:['active','inactive']},reason:text(1000)});
 const permission='current active internal system administrator, checked in SQL before replay';
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
export type AuthorityLimits = {[key:string]:string|number|boolean|number[]|AuthorityLimits};
export type AdministrationAuthority = {id:string;productVersionId:string;binderVersionId:string;version:string;state:string;productName:string;effectiveFrom:string;effectiveTo:string;limits:AuthorityLimits;etag:string};
export type AuthorityProposal = {sourceAuthorityId:string;sourceEtag:string;productVersionId:string;effectiveFrom:string;effectiveTo:string;limits:AuthorityLimits;routingTeamId:string;userIds:string[];reason:string};
export type AuthorityRequest = {id:string;kind:'authority';state:'pending'|'approved'|'rejected';requestedBy:string;requestedAt:string;etag:string;decidedBy:string|null;decisionReason:string|null;proposal:{input:AuthorityProposal;runtimeId:string;binderEtag:string;definition:string;version:string}};
export type AuthorityAdministrationView = {versions:AdministrationAuthority[];grants:{id:string;userId:string;authorityVersionId:string;effectiveFrom:string;effectiveTo:string;revokedAt:string|null;revocationReason:string|null;etag:string}[];users:{id:string;displayName:string}[];teams:{id:string;name:string}[];routingTeamId:string|null;requests:AuthorityRequest[]};
`);
}
