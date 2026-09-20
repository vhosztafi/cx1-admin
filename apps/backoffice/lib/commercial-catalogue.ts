// Server-page import: serialize only CC labels/options, not the whole source inventory.
import source from '../../../contracts/quote-question-catalogue.json';
import schema from '../../../contracts/schemas/commercial-combined.schema.json';
import type {CommercialCatalogue, CommercialQuestion} from './commercial-capture';
import {commercialQuestionVersion, commercialReferenceVersion} from './commercial-capture';

type SourceQuestion = {questionId: string; label: string; kind: string; targetContainer: string; stages?: string[]; referenceValues?: {value: number | string; label: string}[]; sourceOptions: string[]};
export const commercialCatalogue: CommercialCatalogue = {
  questionVersion: commercialQuestionVersion, referenceVersion: commercialReferenceVersion,
  questions: (source.deferredQuestions as SourceQuestion[]).map(q => ({id: q.questionId, label: q.label,
    kind: q.kind as CommercialQuestion['kind'], container: q.targetContainer,
    stage: Number(q.stages?.find(x => x.startsWith('Commercial Combined:step-'))?.split('step-')[1]) ||
      (q.targetContainer.includes('losses[]') ? 3 : q.targetContainer.includes('wages[]') ? 8 : 5),
    options: q.referenceValues?.length ? q.referenceValues : q.sourceOptions.map((label, index) => ({value: index + 1, label}))})),
  lossTypes: schema.$defs.LossType.oneOf.map(x => ({value: x.properties.value.const, label: x.properties.label.const})),
  wageCategories: schema.$defs.WageCategory.oneOf.map(x => ({value: x.properties.value.const, label: x.properties.label.const})),
  occupancy: schema.$defs.Location.properties.occupancy.enum,
  entityTypes: schema.$defs.Insured.properties.entityType.enum,
};
