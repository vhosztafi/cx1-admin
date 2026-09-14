export type Consent = { state: 'given' | 'withheld' | 'not-asked'; email: boolean; telephone: boolean; recordedAt: string; source: string };
export type ContactWrite = { fullName: string; firstName?: string; surname?: string; personId?: string; role: string; email?: string; telephone?: string; isPrimary: boolean; marketingConsent: Consent };
export type Contact = ContactWrite & { id: string; personId: string; relationshipId: string; endedAt?: string };
export const contactRoles = ['Director','Company secretary','Partner','Proprietor','Office manager','Accounts','Workshop manager','Other'];
export function contactPayload(form: FormData, existing?: Contact): ContactWrite {
  const value = (name: string) => String(form.get(name) ?? '').trim();
  const state = value('consent') as Consent['state'];
  return { fullName:value('fullName'), role:value('role'), isPrimary:value('primary') === 'yes',
    ...(existing && {personId:existing.personId}), ...(existing?.firstName && {firstName:existing.firstName}), ...(existing?.surname && {surname:existing.surname}),
    ...(value('email') && {email:value('email')}), ...(value('telephone') && {telephone:value('telephone')}),
    marketingConsent:{state,email:state === 'given' && form.has('consentEmail'),telephone:state === 'given' && form.has('consentTelephone'),
      recordedAt:new Date(value('recordedAt') + 'Z').toISOString(),source:value('source')} };
}
