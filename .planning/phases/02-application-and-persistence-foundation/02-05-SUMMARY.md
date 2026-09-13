---
phase: 02-application-and-persistence-foundation
plan: '05'
status: complete
requirements: [FND-06, FND-05]
completed: 2026-09-13
---

# Durable operations and admin visibility

Implemented atomic audited command receipts, fenced SQL job leases, bounded retries, independently persisted deterministic provider outcomes, transactional inbox/receipt application, duplicate acknowledgement and changed-callback quarantine. Command identity uses actor, canonical route and key; current authorization precedes replay, and replay precedes stale resource checks. Same key/different normalized intent conflicts. Receipt and audit SQL triggers prevent update/delete. No real external service is called.

DiagnosticDispatcher runs only in Development and claims only diagnostic work. Four immutable seeded scenarios demonstrate success, rejection, fail-once and provider success before local timeout. Separate provider commits survive local rollback or process loss. A lease token/expiry/attempt fence prevents stale owners from applying results. Exhaustion creates one infrastructure JobException; no business task implementation is implied.

Operational APIs expose authorized probe creation, creator/admin job status, scoped diagnostic job lists, redacted audit lists, typed current/effective diagnostic settings, single and atomic batch recovery. Protected cursors bind actor/route/filters/page size with expiry. Administrator inspections are audited. Manual recovery keeps provider identity and attempt history, expands the initial six-attempt budget at most twice (12/18), rejects definitive rejection/invalid payload/conflict, and requires a reason and current rowversion. Batch selection locks in stable order and rolls back entirely on any invalid/stale member. OpenAPI contains 284 operations and reviewed optional job budget/eligibility fields.

Admin retains prototype tabs, font, palette and table density. Integrations supports scenario runs, live latest-probe status, persisted/filterable/paged jobs and selected recovery; Audit log supports exact action-code filters and safe summaries. Future business adapters/configuration remain explicitly unavailable. Fixed messages replace arbitrary server error bodies. Mutation locking and retained command keys protect uncertain responses. Browser review corrected ambiguous select names and excessive mobile UUID wrapping; tables now scroll horizontally with readable rows.

Verification: full backend suite passed 32 unit and 14 integration cases; subsequent focused DTO/lease/settings changes were retested against owned SQL test databases. All 53 design checks, six frontend tests, TypeScript, zero-warning lint and final Next production build pass. SQL tests cover flushed-write rollback, concurrent replay, changed intent, immutable receipts/audit, exclusive/stale leases, delays/exhaustion, persisted provider outcomes, duplicate/changed callbacks, batch rollback and recovery history. An actual first API process commits provider success, stops before local completion, and a second API process reconciles without duplicate effect.

Real Chrome operational journey passes against Next/API/native SQL: success, rejection, timeout recovery, reload persistence, bulk recovery after deliberately losing a successful HTTP response, safe list failure/retry, two-page navigation, audit filters, mobile width/row height and denied servicing access. Existing shell browser regression also passes (auth/revocation/network errors/focus/mobile/account). Desktop integrations/audit and corrected mobile screenshots were visually inspected. Evidence is under ignored .local/browser-evidence/admin-*.png. This is automated/agent verification, not human UAT.

Reproduce with pnpm web:browser:operations and documented native preview. Its SQL fixture script refuses databases other than CoverMGA_Demo, adds two explicitly fictional recovery examples with six simulated attempt records each, and never resets/deletes existing history. Each run uses new IDs. The native SQL2022 profile is verified; Docker SQL runtime is not claimed. Test-owned API5087/web3100 processes were ownership-checked and stopped; unrelated port5080 was untouched. Funnel reference files remain unchanged.

Key commits: de573c0 commands; 84bb124 leases; 682c2bb provider/inbox; a938a66 hosted/process recovery; 299e8b5 probe API; 4df95e1 lists; f2c546f recovery budgets; e805a35 batches; 68def00 settings; 3b39c3f UI; 53b9045 browser verification/fixes. Migrations through 20260913213924_ManualJobRecoveryBudget are applied to CoverMGA_Demo.

Next: 02-06 foundation-wide fresh setup/repeat seed/CI/code-security-UI review and requirement verification. Phase 2/FND requirements remain open until that gate passes.
