// Imported by the server page only: send the four needed collections, not the
// entire reference source (and never import the sales snapshot at runtime).
import source from '../../../contracts/reference-data/motor-trade-capture.json';
import type { ProposerCatalogue } from './quote-catalogue';

export const proposerCatalogue: ProposerCatalogue = {
  version: source.version,
  collections: {
    companyTypes: source.collections.companyTypes.map(({ value, text }) => ({ value, text })),
    proposerTitles: source.collections.proposerTitles.map(({ value, text }) => ({ value, text })),
    marketingConsents: source.collections.marketingConsents.map(({ value, text }) => ({ value, text })),
    marketingMethods: source.collections.marketingMethods.map(({ value, text }) => ({ value, text })),
  },
};
