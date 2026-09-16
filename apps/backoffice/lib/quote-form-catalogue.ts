import fieldLabels from '../../../docs/design/funnel-field-mapping.json';
import { sourceDriverFields } from './quote-driver-fields';
import questions from '../../../contracts/quote-question-catalogue.json';
import { sourceBusinessQuestions } from './quote-source-questions';
// Imported by the server page only: send only the needed collections, not the
// entire reference source (and never import the sales snapshot at runtime).
import source from '../../../contracts/reference-data/motor-trade-capture.json';
import type { QuoteFormCatalogue } from './quote-catalogue';

export const quoteFormCatalogue: QuoteFormCatalogue = {
  version: source.version,
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
