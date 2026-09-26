// Generated from the reporting OpenAPI schemas.
export type ReportMeasure = { code: string; label: string; definition: string; unit: "count" | "GBP" | "percent" | "hours"; numerator: string | null; denominator: string | null };
export type ReportDefinition = { id: string; code: string; category: "underwriting" | "portfolio" | "renewal" | "finance" | "agency" | "compliance-exceptions"; title: string; description: string; capability: string; defaultBasis: "effective" | "processed"; bases: ("effective" | "processed")[]; filters: ("agencyId" | "providerId" | "productCode" | "underwriterId")[]; measures: (ReportMeasure)[] };
export type ReportFilters = { from: string; to: string; basis: "effective" | "processed"; agencyId?: string | null; providerId?: string | null; productCode?: string | null; underwriterId?: string | null; offset?: number };
export type ReportValue = { code: string; value: string | null; denominator: string | null };
export type ReportSourceRow = { recordId: string; kind: string; reference: string; label: string; state: string; href: string; sourceId: string | null; sourceRuleId: string | null; agencyId: string | null; productCode: string | null; at: string; basisDate: string | null; values: Record<string, string | null> };
export type ReportResult = { reportId: string; generatedAt: string; filters: ReportFilters; definition: string; totalRows: number; measures: (ReportValue)[]; rows: (ReportSourceRow)[]; nextOffset: number | null; groups: ({ agencyId: string | null; month: string; productCode: string | null; measures: (ReportValue)[] })[] };
export type SavedReport = { id: string; reportId: string; name: string; filters: ReportFilters };
export type RecentReport = { reportId: string; filters: ReportFilters; ranAt: string; totalRows: number };
export type ReportPreferences = { version: number; favourites: (SavedReport)[]; recent: (RecentReport)[] };
export type SaveReportInput = { version: number; reportId: string; name: string; filters: ReportFilters };
