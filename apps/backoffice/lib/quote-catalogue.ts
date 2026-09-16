import type { DynamicCatalogue } from '../../../scripts/quote-dynamic-options.mjs';
import type { DriverField } from './quote-driver-fields';
import type { VehicleField } from './quote-vehicle-fields';
import type { SectionField } from './quote-section-fields';
import type { SourceQuestion } from './quote-source-questions';
import type { ReferenceChoice } from './quote-form';
import type { QuoteView } from './quotes';

export type QuoteFormCatalogue = {
  version: string;
  businessQuestions: SourceQuestion[];
  driverFields?: DriverField[];
  driverOptions?: DynamicCatalogue;
  vehicleFields?: VehicleField[];
  sectionFields?: SectionField[];
  collections: { companyTypes: ReferenceChoice[]; proposerTitles: ReferenceChoice[]; marketingConsents: ReferenceChoice[]; marketingMethods: ReferenceChoice[]; mtOccupations: (ReferenceChoice & { requireCarJockeyRadius: boolean })[] };
};
export function matchingQuoteCatalogue(versions: QuoteView['captureVersions'] | undefined, catalogue: Pick<QuoteFormCatalogue, 'version'>): boolean {
  return Boolean(versions && versions.schemaVersion === '1.0' && versions.questionSetVersion === catalogue.version && versions.referenceDataVersion === catalogue.version);
}
