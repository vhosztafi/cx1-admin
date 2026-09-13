import {readFile,writeFile} from 'node:fs/promises';
const inventory=JSON.parse(await readFile('docs/design/control-inventory.json','utf8'));
const path='docs/design/reviewed-api-controls.json';
const rows=JSON.parse(await readFile(path,'utf8'));
const put=row=>{const index=rows.findIndex(x=>x.controlId===row.controlId);if(index<0)rows.push(row);else rows[index]=row;};
const add=(ids,disposition,operationIds,reason)=>ids.split(' ').forEach(suffix=>put({controlId:`CTL-${suffix}`,status:'reviewed',disposition,operationIds,reason}));
const agencyFields={
 'Legal name':'legalName','Trading name':'tradingName','Entity type':'entityType','Company number':'companyNumber','Registered address':'address','Trading address':'tradingAddress','FCA reference':'regulatoryReference','Regulatory status':'regulatoryStatus','Principal firm':'principalFirm','Client money basis':'clientMoneyBasis','Permission to arrange general insurance':'arrangesGeneralInsurance',Territory:'territory',
 'Main contact':'mainContact.name','Main contact email':'mainContact.email','Main contact telephone':'mainContact.telephone','Compliance contact':'complianceContact','Accounts contact':'accountsContact','Complaints contact':'complaintsContact','Relationship manager':'relationshipManagerId','Correspondence preference':'correspondencePreference','Office hours':'officeHours',
 'Effective from':'commercialTerms.effectiveFrom','Commission basis':'commercialTerms.commissionBasis','Fee sharing':'commercialTerms.feeShareBasisPoints','Volume commitment':'commercialTerms.volumeCommitment','Minimum premium override':'commercialTerms.minimumPremiumOverride','Referral routing':'commercialTerms.referralRouting',
 'TOBA status':'compliance.tobaStatus','TOBA version':'compliance.tobaVersion','Date signed':'compliance.tobaSignedOn','Professional indemnity cover':'compliance.professionalIndemnityLimit','PI expiry date':'compliance.piExpiresOn','Financial standing check':'compliance.financialStanding','Sanctions and adverse media':'compliance.sanctionsCheck','Beneficial ownership verified':'compliance.beneficialOwnershipVerified','Data processing agreement':'compliance.dataProcessingAgreement',
 'Credit terms':'paymentTermsDays','Statement cycle':'settlement.statementCycle','Credit limit':'creditLimit','Settlement method':'settlement.method','Premium collection':'settlement.premiumCollection','Commission settlement':'settlement.commissionSettlement'
};
for(const control of inventory.controls.filter(c=>c.method==='pNewAgency'&&c.kind==='input')){
 const field=agencyFields[control.label];if(!field)continue;
 put({controlId:control.id,status:'reviewed',disposition:'edit-then-save',operationIds:['saveAgencyDraft','validateAgency'],requestField:`details.${field}`,reason:`Reviewed onboarding field maps to typed details.${field}. Save may be incomplete; activation checks evidence, authority and conditional requirements. Percentages become integer basis points and displayed addresses become structured values.`});
}
add('cb7e499f5ef9','command',['createPolicyDraft','savePolicyDraft','getCancellationPreview'],'Validate cancellation reason/date/audit notes and save cancellation intent, then read calculated review; disabled sample cannot bypass date rules.');
add('10248c38843b','command',['createTask'],'Confirm new task with typed subject, owner, priority and due date; persist and return actual task ID.');
add('69897d577944 9fcf0e4113a6','client-only',[],'Close picker/product-selection modal without persisting a domain change.');
add('48357417c7d9 d95a33ac4ba9 06d9667cb5b0 2ce7af342224 8c4c56e91474 d333e12d28da 27a26f44394a b84c84a05598','edit-then-save',['savePolicyDraft'],'Select supported driver/vehicle/premises/activity/cover/insured draft change; editor confirmation saves typed proposal and invalidates rating. No issued snapshot mutation.');
add('c55788c6143d 49a52324ce46 edd4bf8b620d','opens-command-form',['listProductVersions','createQuote'],'Select a published eligible product and agency relationship before creating the typed quote draft; product label is not an API identity.');
add('0668ac887c59','read',['listProducts','getProductVersion'],'Draft Fleet selection displays the publication restriction and product configuration; it cannot create or bind a quote.');
add('061a390a6d24 7209efc79397 abc0712b3b1a 8355c466b4c3 d46524a6c7e6 52e3f70b9519 3a75c3b303c4 2c6b742f5f51','edit-then-save',['saveQuoteProposal'],'Confirm typed driver/conviction/incident/vehicle/location/wage/loss/premises item in the quote proposal. Preserve stable child IDs, field units and conditional validation; the enclosing quote save owns persistence.');
add('17e744eccc12','command',['createContact'],'Create agency-scoped client contact, with primary uniqueness and separate marketing consent.');
add('12694b54dab5','command',['recordCapacityResponse'],'Record provider outcome, named underwriter, reference and evidence on the exact escalation; decision cannot automatically issue cover.');
add('e7ea175c76d6','command',['inviteAgencyUser'],'Add invitation while onboarding; delivery waits for activation and eligible broker-admin prerequisites.');
add('2843860a71f4','command',['createSupportFlag'],'Confirm person-linked flag with restricted category, safe agency instruction, explicit consent/review/reason.');
add('e94274aa8d30','command',['requestIdentityChange'],'Persist requested email change; nominated second approver is routing only, no fabricated approval. Required audit/security notifications cannot be disabled.');
add('dc47c1c54ea5','client-only',[],'Copy newly generated recovery codes from current one-time UI state to clipboard after explicit click; no secret-retrieval API or plaintext persistence.');
add('b88a29907695','command',['disableMfa'],'Verify recent password and MFA/recovery proof plus required-role policy before disabling.');
add('fd38a2890d20','edit-then-save',['savePolicyDraft'],'Save actual edited driver values and completeness state, invalidate rating and approval; do not turn nonempty text into licence verification.');
add('db8560576a23','command',['inviteAgencyUser','inviteInternalUser'],'Invitation confirmation chooses internal or agency endpoint from authorised parent context, never mixes role scopes.');
await writeFile(path,JSON.stringify(rows,null,2)+'\n');
console.log(`Persisted ${rows.length} reviewed controls.`);
