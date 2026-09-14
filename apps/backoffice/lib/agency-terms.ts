import type {AgencyJson,AgencyProduct,Field} from './agencies';
export type AgencyTermsSnapshot={effectiveFrom:string;commercialTerms:AgencyJson;settlement:AgencyJson;paymentTermsDays:number;creditLimit:string;products:AgencyProduct[]};
export type AgencyTermsVersion=AgencyTermsSnapshot & {id:string;agencyId:string;version:number;approvedRequestId:string;approvedRequestKind:'activation'|'terms';status:'current'|'scheduled'|'historical';effectiveTo?:string;createdAt:string};
export function termsFieldValue(field:Field,value:string) {
  if(field.options)return Object.entries(field.options).find(([,stored])=>String(stored)===value)?.[0]??value;
  // Decimal strings remain strings: Number would round large SQL decimal(19,2) values.
  if(field.type==='money')return `£${value.replace(/\B(?=(\d{3})+(?!\d))/g,',')}`;
  if(field.type==='percent')return `${value}%`;
  return value;
}

export type AgencyTermsRequest=AgencyTermsSnapshot & {id:string;agencyId:string;requestedBy:string;requestedByLabel:string;reason:string;createdAt:string;state:"pending"|"applied"|"rejected"|"stale";etag:string;decisionByLabel?:string;decisionReason?:string;decidedAt?:string};
