#09-01 checkpoint

Status: IN PROGRESS, not complete.

Phase9 planning approved in54c32bc. All18 structures valid,8/8 OPS requirements,15/15 decisions. Source extraction now118 original control union,509 exact rendered occurrences,33 overlapping CC claims occurrences and10 supplemental branches. All original62 direct and34 explicit inherited controls retained. Source tests3/3 plus retained CC4/4 pass. No product/API/storage implementation or SQL/browser run claimed.

Continue09-01 closed data/API design and field-by-field mapping. Important discovery: scripts/openapi-form-contracts.mjs already modifies IncidentWrite/IncidentDraftWrite after scripts/openapi-operations.mjs. Current draft requires policyId/versionId and permits partial nested fields. Update the final generated contract intentionally; do not edit an earlier schema only to have later modifiers overwrite it. scripts/openapi-review-additions.mjs owns sendDocumentPack. Existing API tests at321 and443 validate legacy draft shapes. Add meaningful negative tests before new schema changes; avoid weakening unrelated operational endpoints.

Need complete OPERATIONS-CONTRACTS.md, contracts/schemas/operations.schema.json, generator/API changes, TS generation, exact source field mapping and tests; no09-01-SUMMARY exists and09-02 must not start yet. Existing source extraction status remains pending detailed review. Supplemental source requirements: Complaint/Agency onboarding type union, Medium→normal, awaiting-information state with derived overdue, statement-of-fact and selected endorsement PDFs. Plans01/02/06 amended accordingly.

No active verification sessions; no duplicate long suites. Retained preview identities and completed Phase8 reports remain inSTATE. Modified next-env.d.ts/tsconfig.json predate this turn and have no substantive diff; leave untouched. New source ledger generator/test changes need normal reviewed commits, not a demo reset.
