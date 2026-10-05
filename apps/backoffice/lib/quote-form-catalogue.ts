import fieldLabels from '../../../docs/design/funnel-field-mapping.json';
import { sourceDriverFields } from './quote-driver-fields';
import { sourceVehicleFields } from './quote-vehicle-fields';
import { sourceSectionFields } from './quote-section-fields';
import questions from '../../../contracts/quote-question-catalogue.json';
import { sourceBusinessQuestions } from './quote-source-questions';
// Imported by the server page only: send only the needed collections, not the
// entire reference source (and never import the sales snapshot at runtime).
import source from '../../../contracts/reference-data/motor-trade-capture.json';
import type { QuoteFormCatalogue } from './quote-catalogue';

export const quoteFormCatalogue: QuoteFormCatalogue = {
  version: source.version,
  sectionFields: sourceSectionFields(questions.mappings, source.bindings, source.collections, Object.fromEntries(fieldLabels.mappings.map(row => [row.owner, row.label]))),
  vehicleFields: sourceVehicleFields(questions.mappings, source.bindings, source.collections, Object.fromEntries(fieldLabels.mappings.map(row => [row.owner, row.label]))),
  driverOptions: { version: source.version, youngDriverConfiguration: source.youngDriverConfiguration,
    collections: Object.fromEntries(Object.entries(source.collections).filter(([name]) =>
      ['coverLevels', 'indemnityOwnVehicles', 'indemnityCustomerVehicles', 'mtOccupations', 'driverExperienceBasedExcesses', 'driverExperienceBasedVehicleCCLimits',
        'prototype.quote.2beaf3b8d546', 'prototype.quote.d9dd069a314c', 'prototype.quote.b4c7e25f7781', 'prototype.quote.00216de47ab5'].includes(name) ||
      name.startsWith('indemnityOwnVehicles/') || name.startsWith('youngDriverConfiguration/'))) },
  driverFields: sourceDriverFields(questions.mappings, source.bindings, source.collections, Object.fromEntries(fieldLabels.mappings.map(row => [row.owner, row.label]))),
  businessQuestions: sourceBusinessQuestions(questions.mappings, source.bindings, source.collections),
  collections: {
    mtOccupations: source.collections.mtOccupations.map(({ value, text, requireCarJockeyRadius }) => ({ value, text, requireCarJockeyRadius })),
    companyTypes: source.collections.companyTypes.map(({ value, text }) => ({ value, text })),
    proposerTitles: source.collections.proposerTitles.map(({ value, text }) => ({ value, text })),
    marketingConsents: source.collections.marketingConsents.map(({ value, text }) => ({ value, text })),
    marketingMethods: source.collections.marketingMethods.map(({ value, text }) => ({ value, text })),
  },
};

export const quoteReviewCatalogue:QuoteFormCatalogue={...quoteFormCatalogue,businessQuestions:sourceBusinessQuestions(questions.mappings.map(row=>row.questionId?.startsWith('prototype.')&&row.canonicalPath==='risk.business.responses.answers[]'&&!quoteFormCatalogue.businessQuestions.some(question=>question.id===row.questionId)?{...row,stages:['Motor Trade Road Risks:step-3'],...(row.questionId==='prototype.quote-value.2d662a3ec81d'?{label:'Vehicle details for any Yes answers'}:{})}:row),source.bindings,source.collections)};

export const quoteQuestionLabels: Record<string, string> = Object.fromEntries([
  ...fieldLabels.mappings.map(item => [item.owner, item.label]),
  ...[...quoteFormCatalogue.businessQuestions, ...(quoteFormCatalogue.driverFields ?? []), ...(quoteFormCatalogue.vehicleFields ?? []), ...(quoteFormCatalogue.sectionFields ?? [])]
    .map(item => [('questionId' in item && item.questionId) || item.id, item.label]),
]);
