// Phase 3 refinements run last so source-control mappings retain their operation IDs.
export function addPartyContracts({schemas:s,ref:r,text:t,enumeration:e,object:o,array:a,id,instant,date,boolean:b,integer,operation:op,list,paths}) {
 const output=(path,method,schema,status=200)=>{paths[path][method].responses[status].content['application/json'].schema=r(schema);};
 const query=(path,name,schema)=>{if(!paths[path].get.parameters.some(p=>p.name===name))paths[path].get.parameters.push({in:'query',name,required:false,schema});};
 const etagTarget=(path,method,target)=>{
  const action=paths[path][method];action['x-etag-resource']=target;
  const header=action.parameters.find(p=>p.name==='If-Match');
  if(!header)throw new Error(`Missing concurrency header: ${method} ${path}`);
  header.description=`Strong ETag of ${target}; current scope is checked before replay, and concurrency after replay.`;
 };
 const consent=(state,email,telephone)=>o({state:{const:state},email,telephone,recordedAt:instant,source:t()});
 const given=consent('given',b,b);
 given.anyOf=[{properties:{email:{const:true}},required:['email']},{properties:{telephone:{const:true}},required:['telephone']}];
 s.MarketingConsent={oneOf:[given,consent('withheld',{const:false},{const:false}),consent('not-asked',{const:false},{const:false})],description:'Declared channel consent; not-asked is distinct from withheld. Recording audit actor/time are server-owned.'};
 s.ContactWrite.properties.marketingConsent=r('MarketingConsent');
 s.Contact.properties.marketingConsent=r('MarketingConsent');
 s.ContactWrite.properties.personId={...id,description:'Optional existing person from an authorized same-client relationship; never a global person search. Names update this contact declaration, not the canonical person or another contact.'};
 s.Contact.properties.relationshipId=id;
 s.Contact.required=[...new Set([...s.Contact.required,'personId','relationshipId'])];
 query('/relationships/{relationshipId}/contacts','includeEnded',b);
 s.Client.properties.createdAt=instant;
 s.Client.properties.identityState=e('active','inactive');
 s.Relationship.properties.agencyName=t();s.Relationship.properties.agencyReference=t(40);
 s.Relationship.required.push('agencyName','agencyReference');
 s.RelationshipAgency=o({id,reference:t(40),legalName:t(),state:e('draft','active','suspended','abandoned')});
 list('/relationship-agencies','listRelationshipAgencies','relationship-read',r('RelationshipAgency'));
 op('get','/relationships/{relationshipId}','getClientRelationship','relationship-read',{output:r('Relationship')});
 s.ClientRecordAvailability={oneOf:[o({state:{const:'unavailable'}}),o({state:{const:'available'},policyCount:integer,quoteCount:integer})]};
 s.ClientSummary=o({...s.Client.properties,primaryContactName:t(),agencies:a(o({id,name:t(),reference:t(40)})),records:r('ClientRecordAvailability'),tradeActivities:a(t(100))},[...s.Client.required,'agencies','records']);
 paths['/clients'].get.responses[200].content['application/json'].schema.properties.items.items=r('ClientSummary');
 s.ClientActivity=o({id,occurredAt:instant,actorLabel:t(),eventType:t(100),summary:t(500),relationshipId:id,recordId:id,recordKind:e('client','contact','match','quote')},['id','occurredAt','actorLabel','eventType','summary']);
 list('/clients/{clientId}/activity','listClientActivity','client-read',r('ClientActivity'));
 s.ClientRecordLink=o({id,kind:e('quote','policy'),reference:t(40),relationshipId:id,agencyName:t(),productCode:e('motor-trade-road-risks','motor-trade-combined','commercial-combined'),state:t(30)});
 list('/clients/{clientId}/records','listClientRecords','client-read',r('ClientRecordLink'),[['kind',e('quote','policy')]]);
 paths['/clients/{clientId}/records'].get.description+=' Only actual implemented quote/policy records are returned. Module unavailability is 503, not an invented empty live portfolio.';

 s.PartyMutationReceipt=o({id});
 s.PartyMutationReceipt.description='Safe identity-only command receipt. The original strong ETag is returned in the header; fetch current details through an authorized GET.';
 for(const [path,method,status] of [['/relationships/{relationshipId}/people/{personId}/flags','post',201],['/flags/{flagId}','put',200],['/flags/{flagId}/end','post',200]])output(path,method,'PartyMutationReceipt',status);
 const flagTypes=e('vulnerability','third-party-authority','financial-difficulty','accessible-format','interpreter-required','deceased-or-business-ceased');
 const categories=e('health','life-event','resilience','capability','authority');
 const basis=e('verbal-consent','written-consent','third-party-authority');
 for(const name of ['FlagWrite','SupportFlag']){
  s[name].properties.typeCode=flagTypes;s[name].properties.internalCategory=categories;s[name].properties.consentBasis=basis;
  s[name].properties.visibleRelationshipIds={...a(id),uniqueItems:true,maxItems:100};
  s[name].allOf=[{if:{properties:{visibleRelationshipIds:{type:'array',minItems:1}},required:['visibleRelationshipIds']},then:{properties:{agencyInstruction:t(1000)},required:['agencyInstruction']}}];
 }
 s.FlagWrite.description='Declined consent cannot create or update sensitive detail. Each explicit visibility grant requires an active same-client relationship containing this person and functional agency wording. API validates membership and dates.';
 s.FlagHistoryEntry=o({id,flagId:id,actorLabel:t(),occurredAt:instant,action:e('created','amended','reviewed','ended'),reason:t(1000),snapshot:r('SupportFlag')});
 list('/flags/{flagId}/history','listSupportFlagHistory','support-internal-read',r('FlagHistoryEntry'));
 list('/relationships/{relationshipId}/support-instructions/preview','previewSafeSupportInstructions','relationship-read',r('SafeSupportInstruction'));
 paths['/relationships/{relationshipId}/support-instructions/preview'].get.description='Audited internal preview of the exact explicitly granted relationship-safe projection. Does not impersonate an agency session or reveal hidden flag totals.';

 s.MatchSignal=o({code:t(100),summary:t(500),submittedValue:t(500),candidateValue:t(500),weight:e('definitive','strong','moderate','weak'),result:e('match','near-match','different','cannot-compare')});
 s.MatchRuleSnapshot=o({id,version:{type:'integer',minimum:1},duplicateQuotePolicy:e('allow-competing','broker-of-record','refer'),requireReview:b,summary:t(1000)});
 s.MatchSubmission=o({id,reference:t(40),agencyId:id,agencyName:t(),identity:r('ClientWrite'),createdAt:instant,quoteId:id,linkedClientId:id,linkedRelationshipId:id},['id','reference','agencyId','agencyName','identity','createdAt']);
 s.MatchReview.properties.submissionId=id;s.MatchReview.properties.submission=r('MatchSubmission');
 s.MatchReview.properties.candidateRelationshipId={...id,description:'The captured candidate relationship, for unambiguous internal agency context; never infer it from the first account relationship.'};
 s.MatchReview.properties.rule=r('MatchRuleSnapshot');s.MatchReview.properties.signals={...a(r('MatchSignal')),maxItems:100};
 s.MatchReview.required=[...s.MatchReview.required.filter(k=>k!=='quoteId'),'submissionId','submission','rule'];
 s.MatchReview.description='Internal comparison evidence pinned at capture. submissionId and embedded submission.id agree; ruleVersionId and rule.id agree. quoteId is absent until linked to an actual quote. No candidate data belongs in a submission-side decline.';
 s.MatchDecision=o({id,matchId:id,outcome:e('link','separate','decline','query','reopen'),reason:t(1000),actorLabel:t(),occurredAt:instant,clientId:id,relationshipId:id,informationRequestId:id},['id','matchId','outcome','reason','actorLabel','occurredAt']);
 s.MatchInformationRequest=o({id,matchId:id,description:t(1000),recordedAt:instant,deliveryState:e('recorded','queued','delivered','failed')});
 list('/matches/{matchId}/decisions','listMatchDecisions','match-review',r('MatchDecision'));
 list('/matches/{matchId}/information-requests','listMatchInformationRequests','match-review',r('MatchInformationRequest'));
 const decision=paths['/matches/{matchId}/decisions'].post;
 s.MatchMutationResult=o({id});output('/matches/{matchId}/decisions','post','MatchMutationResult');
 for(const path of ['/matches','/matches/{matchId}','/matches/{matchId}/decisions','/matches/{matchId}/information-requests']){
  paths[path].get['x-permission']='match-read';paths[path].get.description=paths[path].get.description.replace('Requires match-review.','Requires match-read.');
 }
 decision.description+=' Successful mutation receipts contain only the review ID and original ETag. Reload evidence using GET under current match-read permission.';
 decision.description+=' Link only associates the submission agency; no destructive merge or new access to candidate contacts. Separate reuses this intake\'s created client on replay/reopen. Query records a request; delivery is a separate workflow. Reopen retains the trail and cannot undo progressed quote/policy effects.';
 decision.requestBody.content['application/json'].schema.properties.candidateClientId={...id,description:'If supplied, must equal the review\'s immutable candidate; arbitrary reassignment is rejected.'};
 query('/matches','candidateClientId',id);

 for(const [path,method,target] of [
  ['/clients/{clientId}','put','client'],['/clients/{clientId}/relationships','post','client'],
  ['/relationships/{relationshipId}/contacts','post','relationship'],
  ['/relationships/{relationshipId}/contacts/{contactId}','put','contact'],
  ['/relationships/{relationshipId}/contacts/{contactId}/make-primary','post','contact'],
  ['/relationships/{relationshipId}/contacts/{contactId}/end','post','contact'],
  ['/relationships/{relationshipId}/people/{personId}/flags','post','origin relationship'],
  ['/flags/{flagId}','put','flag'],['/flags/{flagId}/end','post','flag'],['/matches/{matchId}/decisions','post','match review']
 ])etagTarget(path,method,target);
 for(const path of ['/relationships/{relationshipId}/contacts/{contactId}/make-primary','/relationships/{relationshipId}/contacts/{contactId}/end'])output(path,'post','Contact');
}
