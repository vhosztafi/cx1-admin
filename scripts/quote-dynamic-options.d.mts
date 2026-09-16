export type DynamicChoice = { value: number | string; text: string; numericValue?: number; customerLOI?: boolean };
export type DynamicCatalogue = {
  version: string;
  collections: Record<string, DynamicChoice[]>;
  youngDriverConfiguration: { ageFrom?: number | null; ageTo?: number | null; indemnities: DynamicChoice[]; cCs: DynamicChoice[] }[];
};
export type DriverOptionState = { driverIndex: number; questionId: string; active?: boolean; collection?: string; choices: DynamicChoice[] };
export function ageOn(dateOfBirth: unknown, onDate: unknown): number | undefined;
export function selectQuoteDynamicOptions(proposal: unknown, catalogue: DynamicCatalogue, options?: { requireDriverAnswers?: boolean; includeDriverOptions?: boolean }): {
  selectedCollections: Record<string, string[]>;
  issues: { code: string; path: string; questionId?: string }[];
  driverOptions?: DriverOptionState[];
};
