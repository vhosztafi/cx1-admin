export function addFormContracts({schemas:s,ref:r,text:t,enumeration:e,object:o,array:a,id,instant,date,boolean:b,integer,decimal,operation:op,paths}){
 const namedContact=o({name:t(),email:{type:'string',format:'email'},telephone:t(50)},[]);
 const optionalAddress=o(s.Address.properties,[]);
 const details={
  tradingName:t(),entityType:e('limited-company','partnership','sole-trader','other'),companyNumber:t(30),tradingAddress:optionalAddress,
  regulatoryStatus:e('directly-authorised','appointed-representative','pending'),principalFirm:t(),clientMoneyBasis:e('risk-transfer','client-money','no-client-money'),arrangesGeneralInsurance:b,territory:e('UK','UK-and-EEA','other'),
  complaintsContact:namedContact,relationshipManagerId:id,correspondencePreference:e('email','post','both'),officeHours:t(200),
  commercialTerms:o({effectiveFrom:date,commissionBasis:e('gross-written-premium','net-premium'),feeShareBasisPoints:{type:'integer',minimum:0,maximum:10000},volumeCommitment:decimal,minimumPremiumOverride:decimal,referralRouting:e('underwriting-team','relationship-manager')},[]),
  compliance:o({tobaStatus:e('not-sent','sent','signed'),tobaVersion:t(100),tobaSignedOn:date,professionalIndemnityLimit:decimal,piExpiresOn:date,financialStanding:e('not-started','pending','passed','failed'),sanctionsCheck:e('not-started','pending','clear','refer'),beneficialOwnershipVerified:b,dataProcessingAgreement:e('not-sent','sent','signed'),evidenceIds:a(id)},[]),
  settlement:o({statementCycle:e('monthly','fortnightly'),method:e('bank-transfer','direct-debit'),premiumCollection:e('agency','mga'),commissionSettlement:e('net-remittance','separate-payment')},[])
 };
 Object.assign(s.AgencyWrite.properties,details);
 s.AgencyWrite.properties.address=optionalAddress;
 for(const key of ['mainContact','complianceContact','accountsContact'])s.AgencyWrite.properties[key]=namedContact;
 op('post','/agencies/{agencyId}/validate','validateAgency','agency-admin',{existing:true,output:r('ValidationResult')});
 // Incomplete incident saving is separate from validation/log/handoff gates.
 function partial(value){
  if(Array.isArray(value))return value.map(partial);
  if(value&&typeof value==='object')return Object.fromEntries(Object.entries(value).map(([key,v])=>[key,key==='required'?[]:partial(v)]));
  return value;
 }
 s.IncidentDraftWrite=partial(s.IncidentWrite);
 s.IncidentDraftWrite.required=['policyId','versionId'];
 for(const path of ['/incidents','/incidents/{incidentId}']){
  const method=path==='/incidents'?'post':'put';paths[path][method].requestBody.content['application/json'].schema=r('IncidentDraftWrite');
 }
 for(const [key,value] of Object.entries(s.IncidentDraftWrite.properties))s.Incident.properties[key]=value;
 s.Incident.required=['id','policyId','versionId','state'];
 op('post','/incidents/validate','validateIncidentProposal','incident-write',{input:r('IncidentWrite'),output:r('ValidationResult'),idempotent:false});
 const capacity=paths['/escalations/{escalationId}/responses'].post.requestBody.content['application/json'].schema;
 capacity.properties.providerUnderwriter=t();capacity.required.push('providerUnderwriter');
 const identity=paths['/admin/users/{userId}/identity-requests'].post.requestBody.content['application/json'].schema;
 identity.properties.requestedApproverId=id;
 identity.properties.notifyUser=b;
 // Approver supplied here is a routing request only, never proof of approval.
}
