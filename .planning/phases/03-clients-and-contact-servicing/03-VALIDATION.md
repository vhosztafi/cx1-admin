---
phase: 03
slug: clients-and-contact-servicing
status: planned
nyquist_compliant: false
wave_0_complete: true
created: 2026-09-14
---

# Phase 3 validation strategy

Existing infrastructure: xUnit with real SQL Server, Node test runner, TypeScript/ESLint/Next build and local Playwright Chrome. No new framework or mock database is needed. Planned tests do not exist yet and are written with each owning implementation task; nothing below is marked passed.

After a contract task: `node scripts/validate-contracts.mjs` and `node --test tests/party-contracts.test.mjs`. After backend tasks: relevant xUnit filter plus the full suite after each plan (`dotnet test backend/BackOffice.slnx --no-restore`). After frontend tasks: `pnpm web:test`, `pnpm web:lint`, `pnpm web:typecheck` and browser smoke; build before full preview acceptance. SQL checks are generally seconds to minutes and must fail if SQL is unavailable. Fix a failed boundary before advancing its dependent work.

| Tasks | Requirement | Threat | Automated evidence planned |
|---|---|---|---|
| 03-01-1/2 | All CLI | T-01 | Party schema positive/negative cases plus all source/control contracts; no consent state loss or restricted safe fields |
| 03-01-3 | All CLI | T-02 | CommandBoundaryTests real SQL original ETag replay, changed intent and rollback |
| 03-02-1 | CLI-01/02 | T-03 | PartyValidationTests and ClientTests schema/reseed/reference/FK/UTC/JSON checks |
| 03-02-2/3 | CLI-01/02 | T-04 | ClientTests real cookies/CSRF/capability/scope/cursor/ETag tests; browser create/edit/search/reload |
| 03-03-1/2/3 | CLI-02 | T-05 | ContactRulesTests and ContactTests concurrency, rollback, primary/end/consent/person-isolation; browser lifecycle |
| 03-04-1/2/3 | CLI-03 | T-06 | SupportFlagTests unit/SQL/API grant isolation/revocation, non-persistence on declined consent, redacted activity and restricted history; browser safe preview |
| 03-05-1/2/3 | CLI-04 | T-07 | MatchRulesTests and MatchTests decision matrix/concurrency/replay/reopen/candidate isolation; browser all outcomes |
| 03-06-1/2/3 | All CLI | T-08 | verify-clients-browser, full design/frontend/backend suites, CI count gate updates, inline security/UI review and truthful evidence docs |

Browser command to add: `pnpm web:browser:clients`, local API5087/web3100 only, unique fictional data with no reset. Current shell/operations browser tests remain regressions. Capture actual source/app evidence at 1560x1000 and 390px; human judgment/assistive-technology acceptance remains separate.

Sign-off remains pending execution. Set nyquist_compliant true only when all planned boundary cases have passing evidence. Do not inflate CI minimums using old TRX directories, and do not count hosted CI as run merely because its YAML exists.
