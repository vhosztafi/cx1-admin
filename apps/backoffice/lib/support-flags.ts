export type FlagWrite = {typeCode:string;internalCategory:string;internalInstruction:string;agencyInstruction?:string;consentBasis:string;reviewOn:string;reason:string;visibleRelationshipIds:string[]};
export type SupportFlag = FlagWrite & {id:string;personId:string;originRelationshipId:string;endedAt?:string};
export type FlagHistory = {id:string;flagId:string;actorLabel:string;occurredAt:string;action:string;reason:string;snapshot:SupportFlag};
export type SafeInstruction = {id:string;personId:string;instruction:string;reviewOn:string};
export const flagTypes:Record<string,string> = {'vulnerability':'Vulnerability','third-party-authority':'Third-party authority','financial-difficulty':'Financial difficulty','accessible-format':'Accessible format','interpreter-required':'Interpreter required','deceased-or-business-ceased':'Deceased or business ceased'};
export const flagCategories:Record<string,string> = {'health':'Health','life-event':'Life event','resilience':'Resilience','capability':'Capability','authority':'Authority'};
export const flagConsents:Record<string,string> = {'verbal-consent':'Given verbally, recorded','written-consent':'Written consent on file','third-party-authority':'Not required — third-party authority','declined':'Declined — do not record detail'};
export const canServiceSupport = (roles:string[]) => roles.some(role => ['servicing','underwriter','senior-underwriter'].includes(role));
export function flagPayload(form:FormData,grants:string[]):FlagWrite {
  const value = (name:string) => String(form.get(name) ?? '').trim();
  if(value('consentBasis') === 'declined') throw new Error('Consent declined. No support details have been saved.');
  return {typeCode:value('typeCode'),internalCategory:value('internalCategory'),internalInstruction:value('internalInstruction'),
    ...(value('agencyInstruction') && {agencyInstruction:value('agencyInstruction')}),consentBasis:value('consentBasis'),reviewOn:value('reviewOn'),reason:value('reason'),visibleRelationshipIds:[...new Set(grants)].sort()};
}
