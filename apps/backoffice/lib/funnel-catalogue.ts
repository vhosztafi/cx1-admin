import questions from '../../../contracts/quote-question-catalogue.json';
import references from '../../../contracts/reference-data/motor-trade-capture.json';
import schema from '../../../contracts/schemas/quote-draft.schema.json';
import type {FunnelCatalogue} from './funnel-adapter';
// Host-owned capture configuration; it does not resolve legacy CLR-007.
export const funnelCatalogue:FunnelCatalogue={version:references.version,mappings:questions.mappings,bindings:references.bindings,collections:references.collections,youngDriverConfiguration:references.youngDriverConfiguration,captureLimits:{driverLimit:schema.$defs.RoadRisk.properties.drivers.maxItems,pageSize:10}};
