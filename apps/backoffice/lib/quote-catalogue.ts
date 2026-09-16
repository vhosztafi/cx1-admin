import type { ReferenceChoice } from './quote-form';
import type { QuoteView } from './quotes';

export type ProposerCatalogue = {
  version: string;
  collections: { companyTypes: ReferenceChoice[]; proposerTitles: ReferenceChoice[]; marketingConsents: ReferenceChoice[]; marketingMethods: ReferenceChoice[] };
};
export function matchingQuoteCatalogue(versions: QuoteView['captureVersions'] | undefined, catalogue: Pick<ProposerCatalogue, 'version'>): boolean {
  return Boolean(versions && versions.schemaVersion === '1.0' && versions.questionSetVersion === catalogue.version && versions.referenceDataVersion === catalogue.version);
}
