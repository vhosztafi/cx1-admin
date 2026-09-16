import type { DriverField } from './quote-driver-fields';
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
export function readinessTarget(issue: QuoteIssue, proposal: QuoteProposal, questions: SourceQuestion[], driverFields: DriverField[] = []): ReadinessTarget | undefined {
  const driverTarget = driverReadinessTarget(issue, proposal, driverFields);
  if (driverTarget) return driverTarget;
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


function driverReadinessTarget(issue: QuoteIssue, proposal: QuoteProposal, fields: DriverField[]): ReadinessTarget | undefined {
  if (!fields.length) return undefined;
  const stage = proposal.productCode === 'motor-trade-combined' ? 4 : 3;
  const globalLabels: Record<string, string> = { 'prototype.quote.ef70e80708bb': 'Motoring convictions in the last five years or pending prosecutions', 'prototype.quote.36da21d3c935': 'Accidents, claims or losses in the last three years', 'prototype.quote.922ca15dc9ed': 'County court judgments in the last five years', 'prototype.quote.46414cc10100': 'Criminal convictions or pending prosecutions' };
  if (issue.path.startsWith('/risk/business/responses/') && issue.questionId && globalLabels[issue.questionId]) return { stage: stage + 1, label: globalLabels[issue.questionId] };
  if (issue.path === '/risk/drivers' && issue.code === 'named-driver-required') return { stage, label: 'Add driver' };
  if (issue.path.startsWith('/risk/responses/')) {
    const field = fields.find(field => field.group === 'plan' && field.questionId === issue.questionId);
    return field ? { stage, label: `Driver basis · ${field.label}` } : undefined;
  }
  const match = /^\/risk\/drivers\/(\d+)\/(.*)$/.exec(issue.path);
  if (!match || !Array.isArray(proposal.risk?.drivers) || !proposal.risk.drivers[Number(match[1])]) return undefined;
  let row = proposal.risk.drivers[Number(match[1])] as QuoteObject;
  let path = match[2]; let prefix = `Driver ${Number(match[1]) + 1}`; let group = 'driver';
  const history = /^(occupations|convictions|losses|criminalConvictions|countyCourtJudgments)\/(\d+)\/(.*)$/.exec(path);
  if (history) {
    group = history[1]; const rows = row[group]; if (!Array.isArray(rows) || !rows[Number(history[2])]) return undefined;
    row = rows[Number(history[2])] as QuoteObject; path = history[3];
    const labels: Record<string, string> = { occupations: 'Additional occupations', convictions: 'Motoring convictions', losses: 'Accidents and claims', criminalConvictions: 'Criminal convictions', countyCourtJudgments: 'County court judgments' };
    prefix += ` · ${labels[group]} ${Number(history[2]) + 1}`;
  }
  const answerIndex = /^responses\/answers\/(\d+)\/value$/.exec(path);
  const responses = row.responses as QuoteObject | undefined;
  const answer = answerIndex && Array.isArray(responses?.answers) ? responses.answers[Number(answerIndex[1])] as QuoteObject | undefined : undefined;
  const id = issue.questionId ?? (typeof answer?.questionId === 'string' ? answer.questionId : undefined);
  const field = fields.find(field => field.group === group && (id ? field.questionId === id || field.id === id : !field.questionId && field.path === path.replaceAll('/', '.')));
  if (!field || field.unavailable) return undefined;
  return { stage: group === 'driver' || group === 'occupations' ? stage : stage + 1, label: `${prefix} · ${field.label}` };
}
