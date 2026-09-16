import type { ReferenceChoice } from './quote-form';
import type { QuoteView } from './quotes';

export type QuoteFormCatalogue = {
  version: string;
  collections: { companyTypes: ReferenceChoice[]; proposerTitles: ReferenceChoice[]; marketingConsents: ReferenceChoice[]; marketingMethods: ReferenceChoice[]; mtOccupations: (ReferenceChoice & { requireCarJockeyRadius: boolean })[] };
};
export function matchingQuoteCatalogue(versions: QuoteView['captureVersions'] | undefined, catalogue: Pick<QuoteFormCatalogue, 'version'>): boolean {
  return Boolean(versions && versions.schemaVersion === '1.0' && versions.questionSetVersion === catalogue.version && versions.referenceDataVersion === catalogue.version);
}
