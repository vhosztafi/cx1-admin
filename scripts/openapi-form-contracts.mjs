export function addFormContracts({schemas:s,ref:r,text:t,enumeration:e,object:o,array:a,id,instant,date,boolean:b,integer,decimal,operation:op,paths}){
 function query(path,name,schema){const operation=paths[path].get;if(!operation.parameters.some(p=>p.name===name))operation.parameters.push({in:'query',name,required:false,schema});}
 for(const path of ['/policies','/quotes'])query(path,'productCode',s.Quote.properties.productCode);
 query('/policies','status',e('active','renewing','cancelled','expired'));
 query('/quotes','status',e('draft','referral-required','quoted','declined','expired'));
 query('/clients','entityType',s.AgencyWrite.properties.entityType??e('limited-company','sole-trader','partnership','llp'));
 query('/agencies','relationshipManagerId',id);
 query('/tasks','kind',t(100));query('/tasks','dueWindow',e('overdue','today','next-seven-days'));
 for(const [name,schema] of Object.entries({status:t(60),productCode:s.Quote.properties.productCode,agencyId:id,providerId:id,underwriterId:id,inceptionFrom:date,reference:t(100)}))query('/search',name,schema);
 s.ReportFilters.properties.underwriterId=id;
 for(const path of ['/finance/journals','/finance/agency-statements','/finance/receipts','/finance/bank-lines','/finance/bordereaux','/finance/refunds']){
  if(!paths[path])continue;
  query(path,'from',date);query(path,'to',date);query(path,'agencyId',id);query(path,'filterStatus',e('outstanding','paid','allocated','exception'));
 }
 s.PolicyDraft.properties.cancellationReasonCode=t(100);
 paths['/drafts/{draftId}/proposal'].put.requestBody.content['application/json'].schema.properties.cancellationReasonCode=t(100);
 paths['/terms/{termId}/drafts'].post.requestBody.content['application/json'].schema.properties.cancellationReasonCode=t(100);
 s.ContactWrite.properties.fullName=t();
 s.ContactWrite.required=s.ContactWrite.required.filter(key=>!['firstName','surname'].includes(key));
 s.ContactWrite.anyOf=[{properties:{fullName:t()},required:['fullName']},{properties:{firstName:t(100),surname:t(100)},required:['firstName','surname']}];
 s.Contact.properties.fullName=t();
 s.Contact.required=['id',...s.ContactWrite.required];s.Contact.anyOf=s.ContactWrite.anyOf;
 s.AccountServiceRequest=o({id,userId:id,kind:e('email','role-team','authority'),details:t(2000),state:e('open','routed','completed','rejected')});
 op('post','/account/change-requests','requestAccountAdminChange','self',{input:o({kind:s.AccountServiceRequest.properties.kind,details:t(2000)}),output:r('AccountServiceRequest'),status:201});
 const settlementFields={termsVersionId:id,collector:e('agency','mga'),settlementMode:e('net-remittance','separate-payment'),brokerFeeShare:decimal,mgaFeeIncome:decimal,remunerationPayable:decimal,invoiceDue:decimal};
 Object.assign(s.Invoice.properties,settlementFields);s.Invoice.required.push(...Object.keys(settlementFields));
 s.Invoice.properties.debtorKind=e('agency','client');s.Invoice.properties.debtorRelationshipId=id;s.Invoice.required.push('debtorKind');
 s.Receipt.properties.payerKind=e('unidentified','agency','client');s.Receipt.properties.relationshipId=id;s.Receipt.required.push('payerKind');
 const receipt=paths['/finance/receipts'].post.requestBody.content['application/json'].schema;
 receipt.properties.payerKind=s.Receipt.properties.payerKind;receipt.properties.relationshipId=id;receipt.required.push('payerKind');
 const assign=paths['/finance/receipts/{receiptId}/assign-payer'].post.requestBody.content['application/json'].schema;
 assign.properties.payerKind=e('agency','client');assign.properties.relationshipId=id;assign.required.push('payerKind');
 s.Refund.properties.payeeKind=e('agency','client');s.Refund.properties.payeeRelationshipId=id;s.Refund.required.push('payeeKind');
 s.BrokerRemuneration=o({id,agencyId:id,transactionId:id,termsVersionId:id,commission:decimal,feeShare:decimal,amount:decimal,settledAmount:decimal,outstanding:decimal,currency:{const:'GBP'},state:e('payable','part-paid','paid','clawback')});
 op('get','/finance/remunerations','listBrokerRemuneration','finance-read',{query:[['agencyId',id],['cursor',t(2048)]],output:o({items:a(r('BrokerRemuneration')),nextCursor:t(2048)},['items'])});
 op('get','/finance/remunerations/{remunerationId}','getBrokerRemuneration','finance-read',{output:r('BrokerRemuneration')});
 op('post','/finance/remunerations/{remunerationId}/payments','payBrokerRemuneration','finance-remuneration-pay',{existing:true,input:o({amount:decimal,reason:t(1000)}),output:r('Job'),status:202});
 const namedContact=o({name:t(),email:{type:'string',format:'email'},telephone:t(50)},[]);
 const optionalAddress=o(s.Address.properties,[]);
 const details={
  tradingName:t(),entityType:e('limited-company','llp','partnership','sole-trader'),companyNumber:t(30),tradingAddress:optionalAddress,
  regulatoryStatus:e('directly-authorised','appointed-representative','introducer-appointed-representative'),principalFirm:t(),clientMoneyBasis:e('risk-transfer','cass5-client-money','no-client-money'),arrangesGeneralInsurance:b,territory:e('UK','UK-and-EEA','other'),
  complaintsContact:namedContact,relationshipManagerId:id,correspondencePreference:e('email','post','both'),officeHours:t(200),
  commercialTerms:o({effectiveFrom:date,commissionBasis:e('per-product','flat-rate'),flatCommissionBasisPoints:{type:'integer',minimum:0,maximum:10000},feeShareBasisPoints:{type:'integer',minimum:0,maximum:10000},volumeCommitment:decimal,minimumPremiumOverride:decimal,referralRouting:e('underwriting-team','relationship-manager')},[]),
  compliance:o({tobaStatus:e('not-sent','sent','signed'),tobaVersion:t(100),tobaSignedOn:date,professionalIndemnityLimit:decimal,piExpiresOn:date,financialStanding:e('not-started','pending','passed','failed'),sanctionsCheck:e('not-started','pending','clear','refer'),beneficialOwnershipVerified:b,dataProcessingAgreement:e('not-sent','sent','signed'),evidenceIds:a(id)},[]),
  settlement:o({statementCycle:e('monthly','fortnightly'),method:e('bank-transfer','direct-debit'),premiumCollection:e('agency','mga'),commissionSettlement:e('net-remittance','separate-payment')},[])
 };
 Object.assign(s.AgencyWrite.properties,details);
 s.ClientWrite.properties.entityType=e('sole-trader','partnership','limited-company','llp');
 s.Client.properties.entityType=s.ClientWrite.properties.entityType;
 s.AgencyWrite.properties.address=optionalAddress;
 for(const key of ['mainContact','complianceContact','accountsContact'])s.AgencyWrite.properties[key]=namedContact;
 op('post','/agencies/{agencyId}/validate','validateAgency','agency-admin',{existing:true,output:r('ValidationResult')});
 s.AgencyTermsRequest=o({id,agencyId:id,requestedBy:id,approvedBy:id,state:e('pending','approved','rejected','applied'),effectiveFrom:date,reason:t(1000),commercialTerms:details.commercialTerms,products:a(r('AgencyProduct'))},['id','agencyId','requestedBy','state','effectiveFrom','reason','commercialTerms','products']);
 op('post','/agencies/{agencyId}/terms-requests','requestAgencyTermsChange','agency-commercial-propose',{existing:true,input:o({effectiveFrom:date,reason:t(1000),commercialTerms:details.commercialTerms,products:a(r('AgencyProduct'))}),output:r('AgencyTermsRequest'),status:201});
 op('get','/agency-terms-requests/{requestId}','getAgencyTermsRequest','agency-commercial-read',{output:r('AgencyTermsRequest')});
 op('post','/agency-terms-requests/{requestId}/decision','decideAgencyTermsChange','agency-commercial-approve-no-self-approval',{existing:true,input:o({outcome:e('approve','reject'),reason:t(1000)}),output:r('AgencyTermsRequest')});
 paths['/agencies/{agencyId}/products'].put.description+=' Only draft onboarding may use this replacement. Active agency commission/product changes require requestAgencyTermsChange and independent approval.';
 paths['/agencies/{agencyId}'].put.description+=' Active agency commercial terms cannot change through this endpoint; reject with 409 agency-terms-approval-required.';
 // Incomplete incident saving is separate from validation/log/handoff gates.
 function partial(value){
  if(Array.isArray(value))return value.map(partial);
  if(value&&typeof value==='object')return Object.fromEntries(Object.entries(value).map(([key,v])=>[key,key==='required'?[]:partial(v)]));
  return value;
 }
 Object.assign(s.IncidentWrite.properties,{
  occurredOn:date,approximateLocalTime:{type:'string',pattern:'^([01][0-9]|2[0-3]):[0-5][0-9]$'},
  involvement:e('registered-vehicle','stock-or-customer-vehicle','premises','third-party-only'),
  locationDescription:t(1000),policeReference:t(100),itemDescription:t(1000),owner:e('insured','customer','third-party'),
  estimatedValueAtRisk:{type:'string',pattern:'^(0|[1-9][0-9]{0,12})\\.[0-9]{2}$'},
  thirdPartyInvolvement:e('yes','no','unknown'),thirdPartyName:t(),thirdPartyInsurerOrRegistration:t(300),
  reportedBy:t(),reportingRoute:e('agency','insured-direct','third-party-or-insurer','police-or-recovery'),bestContactDescription:t(1000)
 });
 s.IncidentWrite.properties.kind.enum=['road-accident','vehicle-theft','premises-theft','fire','malicious-damage','third-party-injury','customer-vehicle-damage','other'];
 s.IncidentWrite.required=s.IncidentWrite.required.filter(key=>key!=='occurredAt');s.IncidentWrite.required.push('occurredOn');
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
