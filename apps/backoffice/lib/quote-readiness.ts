import type { SourceQuestion } from './quote-source-questions';
import type { QuoteIssue, QuoteObject, QuoteProposal } from './quotes';

export type ReadinessTarget = { stage: number; label: string };
const fields: Record<string, ReadinessTarget> = Object.fromEntries([
  ...Object.entries({ kind: 'Term type', localStartDate: 'Requested start date', localStartTime: 'Requested start time', utcOffsetMinutes: 'Start clock offset', localEndDate: 'Requested end date', localEndTime: 'Requested end time', endUtcOffsetMinutes: 'End clock offset' }).map(([key, label]) => [`/termIntent/${key}`, { stage: 0, label }]),
  ...Object.entries({ firstName: 'First name', surname: 'Surname', legalName: 'Company or partnership name', tradingName: 'Trading name', companyNumber: 'Company number', title: 'Proposer title', declaredCompanyType: 'Company category', entityType: 'Legal entity' }).map(([key, label]) => [`/insured/${key}`, { stage: 1, label }]),
  ...Object.entries({ email: 'Email address', telephone: 'Telephone', mobile: 'Mobile' }).map(([key, label]) => [`/insured/contact/${key}`, { stage: 1, label }]),
  ...Object.entries({ houseNumber: 'House number or name', street: 'Street', town: 'Town', city: 'City', county: 'County', postcode: 'Postcode' }).map(([key, label]) => [`/insured/address/${key}`, { stage: 1, label }]),
  ...Object.entries({ startedOn: 'Business start date', description: 'Business description', turnover: 'Annual turnover (GBP)', wageRoll: 'Annual wage roll (GBP)' }).map(([key, label]) => [`/risk/business/${key}`, { stage: key === 'startedOn' || key === 'description' ? 1 : 2, label }]),
  ...Object.entries({ sales: 'Vehicle sales', servicing: 'Servicing', mechanicalRepair: 'Mechanical repair', breakdownRecovery: 'Breakdown and recovery', bodyRepairs: 'Body repairs', valeting: 'Valeting', other: 'Other activities' }).map(([key, label]) => [`/risk/business/declaredActivitySplit/${key}`, { stage: 2, label: `${label} (%)` }]),
]);
const answers: Record<string, ReadinessTarget> = Object.fromEntries([
  ...Object.entries({ 'MTS-01-Q01': 'Consent to collect quotation data' }).map(([key, label]) => [key, { stage: 1, label }]),
  ...Object.entries({ 'MTS-03-Q04': 'Are you a member of any trade associations?', 'MTS-03-Q05': 'Trade association name', 'MTS-03-Q06': 'Is the company VAT registered?', 'MTS-03-Q07': 'VAT number', 'MTS-03-Q08': 'Number of vehicles handled per year', 'MTS-03-Q09': 'Motor Insurance Policy Database (MIPD) vehicle limit' }).map(([key, label]) => [key, { stage: 2, label }]),
]);

// Only explicit editable targets are linked. Never infer a wizard stage from
// risk.business: that container also owns later claims/vehicle answers.
export function readinessTarget(issue: QuoteIssue, proposal: QuoteProposal, questions: SourceQuestion[]): ReadinessTarget | undefined {
  if (issue.questionId) {
    const question = questions.find(item => item.id === issue.questionId && item.products.includes(proposal.productCode));
    return question ? { stage: question.stage, label: question.label } : answers[issue.questionId];
  }
  const exact = fields[issue.path];
  if (exact) return issue.path === '/termIntent/endUtcOffsetMinutes' && proposal.termIntent?.kind === 'annual' ? { ...exact, label: 'Annual end clock offset' } : exact;
  const name = /^\/insured\/proposerNames(?:\/([0-2]))?$/.exec(issue.path);
  if (name) return { stage: 1, label: `Proposer ${Number(name[1] ?? 0) + 1} full name` };
  if (issue.path === '/insured/contact') return { stage: 1, label: 'Telephone' };
  if (issue.path === '/risk/business/declaredActivitySplit') return { stage: 2, label: 'Vehicle sales (%)' };
  const activity = /^\/risk\/business\/activities(?:\/(\d+)(?:\/(code|turnoverBasisPoints))?)?$/.exec(issue.path);
  if (activity) {
    const rows = (proposal.risk?.business as QuoteObject | undefined)?.activities;
    if (activity[1] === undefined) return { stage: 2, label: Array.isArray(rows) && rows.length ? 'Occupation 1 turnover share (%)' : 'Add occupation' };
    const index = Number(activity[1]);
    if (Array.isArray(rows) && index < rows.length) return { stage: 2, label: `Occupation ${index + 1}${activity[2] === 'turnoverBasisPoints' ? ' turnover share (%)' : ''}` };
  }
  return undefined;
}
