import type { Field } from './agencies';
// Fixed source choices, reviewed against agency-option-mapping.json.
export const agencyFields: Field[] = [
  {
    "path": "legalName",
    "label": "Legal name",
    "stage": 1,
    "max": 200
  },
  {
    "path": "tradingName",
    "label": "Trading name",
    "stage": 1,
    "max": 200
  },
  {
    "path": "entityType",
    "label": "Entity type",
    "stage": 1,
    "options": {
      "Limited company": "limited-company",
      "LLP": "llp",
      "Partnership": "partnership",
      "Sole trader": "sole-trader"
    }
  },
  {
    "path": "companyNumber",
    "label": "Company number",
    "stage": 1,
    "max": 30
  },
  {
    "path": "address.line1",
    "label": "Registered address line 1",
    "stage": 1,
    "max": 200
  },
  {
    "path": "address.line2",
    "label": "Registered address line 2",
    "stage": 1,
    "max": 200
  },
  {
    "path": "address.town",
    "label": "Registered town or city",
    "stage": 1,
    "max": 100
  },
  {
    "path": "address.county",
    "label": "Registered county",
    "stage": 1,
    "max": 100
  },
  {
    "path": "address.postcode",
    "label": "Registered postcode",
    "stage": 1,
    "max": 20
  },
  {
    "path": "address.country",
    "label": "Registered country",
    "stage": 1,
    "options": {
      "United Kingdom": "GB"
    }
  },
  {
    "path": "tradingAddressMode",
    "label": "Trading address",
    "stage": 1,
    "options": {
      "Same as registered": "same-as-registered",
      "Different — capture separately": "different"
    }
  },
  {
    "path": "tradingAddress.line1",
    "label": "Trading address line 1",
    "stage": 1,
    "max": 200
  },
  {
    "path": "tradingAddress.line2",
    "label": "Trading address line 2",
    "stage": 1,
    "max": 200
  },
  {
    "path": "tradingAddress.town",
    "label": "Trading town or city",
    "stage": 1,
    "max": 100
  },
  {
    "path": "tradingAddress.county",
    "label": "Trading county",
    "stage": 1,
    "max": 100
  },
  {
    "path": "tradingAddress.postcode",
    "label": "Trading postcode",
    "stage": 1,
    "max": 20
  },
  {
    "path": "tradingAddress.country",
    "label": "Trading country",
    "stage": 1,
    "options": {
      "United Kingdom": "GB"
    }
  },
  {
    "path": "regulatoryReference",
    "label": "FCA reference",
    "stage": 1,
    "max": 30
  },
  {
    "path": "regulatoryStatus",
    "label": "Regulatory status",
    "stage": 1,
    "options": {
      "Directly authorised": "directly-authorised",
      "Appointed representative": "appointed-representative",
      "Introducer appointed representative": "introducer-appointed-representative"
    }
  },
  {
    "path": "principalFirm",
    "label": "Principal firm",
    "stage": 1,
    "max": 200
  },
  {
    "path": "clientMoneyBasis",
    "label": "Client money basis",
    "stage": 1,
    "options": {
      "Risk transfer only": "risk-transfer",
      "Client money with CASS 5 trust account": "cass5-client-money",
      "No client money held": "no-client-money"
    }
  },
  {
    "path": "arrangesGeneralInsurance",
    "label": "Permission to arrange general insurance",
    "stage": 1,
    "options": {
      "Not yet checked": "unchecked",
      "Confirmed on the register": "confirmed",
      "Restricted — review required": "restricted"
    }
  },
  {
    "path": "territory",
    "label": "Territory",
    "stage": 1,
    "options": {
      "United Kingdom": "UK",
      "Great Britain only": "GB",
      "Northern Ireland only": "NI"
    }
  },
  {
    "path": "correspondencePreference",
    "label": "Correspondence preference",
    "stage": 2,
    "options": {
      "Email": "email",
      "Email and post": "email-and-post",
      "Portal only": "portal-only"
    }
  },
  {
    "path": "officeHours",
    "label": "Office hours",
    "stage": 2,
    "max": 200
  },
  {
    "path": "mainContact.name",
    "label": "Main contact name",
    "stage": 2,
    "max": 200
  },
  {
    "path": "mainContact.email",
    "label": "Main contact email",
    "stage": 2,
    "type": "email",
    "max": 254
  },
  {
    "path": "mainContact.telephone",
    "label": "Main contact telephone",
    "stage": 2,
    "max": 50
  },
  {
    "path": "complianceContact.name",
    "label": "Compliance contact name",
    "stage": 2,
    "max": 200
  },
  {
    "path": "complianceContact.email",
    "label": "Compliance contact email",
    "stage": 2,
    "type": "email",
    "max": 254
  },
  {
    "path": "complianceContact.telephone",
    "label": "Compliance contact telephone",
    "stage": 2,
    "max": 50
  },
  {
    "path": "accountsContact.name",
    "label": "Accounts contact name",
    "stage": 2,
    "max": 200
  },
  {
    "path": "accountsContact.email",
    "label": "Accounts contact email",
    "stage": 2,
    "type": "email",
    "max": 254
  },
  {
    "path": "accountsContact.telephone",
    "label": "Accounts contact telephone",
    "stage": 2,
    "max": 50
  },
  {
    "path": "complaintsContact.name",
    "label": "Complaints contact name",
    "stage": 2,
    "max": 200
  },
  {
    "path": "complaintsContact.email",
    "label": "Complaints contact email",
    "stage": 2,
    "type": "email",
    "max": 254
  },
  {
    "path": "complaintsContact.telephone",
    "label": "Complaints contact telephone",
    "stage": 2,
    "max": 50
  },
  {
    "path": "relationshipManagerId",
    "label": "Relationship manager",
    "stage": 2,
    "options": {}
  },
  {
    "path": "commercialTerms.commissionBasis",
    "label": "Commission basis",
    "stage": 3,
    "options": {
      "Per product, as above": "per-product",
      "Flat rate across all products": "flat-rate"
    }
  },
  {
    "path": "commercialTerms.feeSharing",
    "label": "Fee sharing",
    "stage": 3,
    "options": {
      "No — MGA retains all fees": "none",
      "Yes — agreed split": "agreed-split"
    }
  },
  {
    "path": "commercialTerms.volumeCommitmentMode",
    "label": "Volume commitment",
    "stage": 3,
    "options": {
      "None": "none",
      "Agreed target — no penalty": "target-no-penalty",
      "Agreed target — commission tiered": "target-tiered"
    }
  },
  {
    "path": "commercialTerms.minimumPremiumOverrideMode",
    "label": "Minimum premium override",
    "stage": 3,
    "options": {
      "No": "none",
      "Yes — agreed with the capacity provider": "capacity-provider-agreed"
    }
  },
  {
    "path": "commercialTerms.referralRouting",
    "label": "Referral routing",
    "stage": 3,
    "options": {
      "Standard — internal underwriting": "standard-internal-underwriting"
    }
  },
  {
    "path": "commercialTerms.effectiveFrom",
    "label": "Effective from",
    "stage": 3,
    "type": "date"
  },
  {
    "path": "commercialTerms.flatCommissionBasisPoints",
    "label": "Flat commission (%)",
    "stage": 3,
    "type": "percent"
  },
  {
    "path": "commercialTerms.feeShareBasisPoints",
    "label": "Agency fee share (%)",
    "stage": 3,
    "type": "percent"
  },
  {
    "path": "commercialTerms.volumeCommitment",
    "label": "Volume target (£)",
    "stage": 3,
    "type": "money"
  },
  {
    "path": "commercialTerms.minimumPremiumOverride",
    "label": "Agreed minimum premium (£)",
    "stage": 3,
    "type": "money"
  },
  {
    "path": "compliance.tobaStatus",
    "label": "TOBA status",
    "stage": 4,
    "options": {
      "Not issued": "not-sent",
      "Issued — awaiting signature": "sent",
      "Signed": "signed"
    }
  },
  {
    "path": "compliance.tobaVersion",
    "label": "TOBA version",
    "stage": 4,
    "options": {
      "2026.1": "2026.1",
      "2025.2": "2025.2"
    }
  },
  {
    "path": "compliance.professionalIndemnityStatus",
    "label": "Professional indemnity cover",
    "stage": 4,
    "options": {
      "Not supplied": "not-supplied",
      "Evidenced — meets minimum": "meets-minimum",
      "Evidenced — below minimum": "below-minimum"
    }
  },
  {
    "path": "compliance.financialStanding",
    "label": "Financial standing check",
    "stage": 4,
    "options": {
      "Not started": "not-started",
      "In progress": "pending",
      "Complete": "passed",
      "Adverse — refer": "refer"
    }
  },
  {
    "path": "compliance.sanctionsCheck",
    "label": "Sanctions and adverse media",
    "stage": 4,
    "options": {
      "Not started": "not-started",
      "Clear": "clear",
      "Possible match — refer": "refer"
    }
  },
  {
    "path": "compliance.beneficialOwnershipVerified",
    "label": "Beneficial ownership verified",
    "stage": 4,
    "options": {
      "Not started": "not-started",
      "Verified": "verified",
      "Unable to verify — refer": "refer"
    }
  },
  {
    "path": "compliance.dataProcessingAgreement",
    "label": "Data processing agreement",
    "stage": 4,
    "options": {
      "Not issued": "not-sent",
      "Issued": "sent",
      "Signed": "signed"
    }
  },
  {
    "path": "compliance.tobaSignedOn",
    "label": "Date signed",
    "stage": 4,
    "type": "date"
  },
  {
    "path": "compliance.professionalIndemnityLimit",
    "label": "PI cover limit (£)",
    "stage": 4,
    "type": "money"
  },
  {
    "path": "compliance.piExpiresOn",
    "label": "PI expiry date",
    "stage": 4,
    "type": "date"
  },
  {
    "path": "paymentTermsDays",
    "label": "Credit terms",
    "stage": 5,
    "type": "days",
    "options": {
      "30 days": 30,
      "45 days": 45,
      "60 days": 60
    }
  },
  {
    "path": "settlement.statementCycle",
    "label": "Statement cycle",
    "stage": 5,
    "options": {
      "Monthly": "monthly",
      "Fortnightly": "fortnightly"
    }
  },
  {
    "path": "settlement.method",
    "label": "Settlement method",
    "stage": 5,
    "options": {
      "Bank transfer": "bank-transfer",
      "Direct debit": "direct-debit"
    }
  },
  {
    "path": "settlement.premiumCollection",
    "label": "Premium collection",
    "stage": 5,
    "options": {
      "Agency collects from the insured": "agency",
      "Cover MGA collects direct": "mga"
    }
  },
  {
    "path": "settlement.commissionSettlement",
    "label": "Commission settlement",
    "stage": 5,
    "options": {
      "Net of commission on statement": "net-remittance",
      "Paid separately": "separate-payment"
    }
  },
  {
    "path": "creditLimit",
    "label": "Credit limit (£)",
    "stage": 5,
    "type": "money"
  }
];
