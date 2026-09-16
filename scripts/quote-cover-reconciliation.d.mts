import type { DynamicCatalogue } from './quote-dynamic-options.mjs';
export function reconcileQuoteCover(proposal: unknown, catalogue: DynamicCatalogue): { facts: Record<string, string>; issues: { code: string; path: string }[] };
