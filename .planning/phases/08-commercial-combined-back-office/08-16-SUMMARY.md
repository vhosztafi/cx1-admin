---
phase: 08-commercial-combined-back-office
plan: '16'
status: complete
subsystem: commercial-final-acceptance
requires: [08-15]
provides: [verified-commercial-lifecycle, retained-motor-regression, preserved-demo-restart, phase9-operational-handoff]
affects: [09]
requirements-completed: [CC-01, CC-02, CC-03, CC-04]
completed: 2026-09-21
---

# 08-16 — Commercial acceptance and retained regression

Commercial Combined capture, underwriting, issue, policy views, adjustments, renewal and cancellation pass current UI/API/SQL acceptance. All 16 Phase 8 plans are complete. CC-05 remains partial: exact incident/document payloads are implemented; Phase 9 owns incident logging, rendering, delivery and operational consequences.

## Corrections made during acceptance

- `PolicyDiscoveryEndpoints` and the policy-list UI now accept Commercial Combined, with closed discovery schemas and actual saved-policy browser proof (`e0a682a`). Existing authorization and Motor Trade filtering remain enforced.
- `AgencyCatalogRating` reports readiness from current published product/runtime/provider/rating/binder/authority configuration (`d608c7e`). Actual rating still performs actor, agency, terms and risk checks. A preserved earlier binary supplied the meaningful failing SQL regression before the fix passed.
- `SignIn` disables credentials and submission until hydration and explicitly uses POST (`0a18a17`). Actual no-JavaScript and hydrated browser checks demonstrate that native GET cannot expose credentials in a URL. Generated fixture passwords are redacted from failure logs.
- `readCommercialHistory` retries only the first-page `409/underwriting-history-changed` conflict, at most three retries. Cursor reads, unrelated conflicts and denied reads are not retried. Four focused tests pass.
- Acceptance helpers wait for completed navigation and loaded commercial editors. `resumablePrefix` and the servicing aggregate's guarded `--resume` preserve failed attempts and verify the full stage inventory, contiguous successful prefix, source/log hashes, origin and policy fixtures (`66fa48a`, `db219d0`). Product binaries remained unchanged across the two retained-servicing resumes. Four resume tests pass.
- Rollback assertions now expect the later migration's 51971 retention guard while preserving original data and migration checks. Stale seed-row counts were replaced with exact identity/scope/version/date/JSON preservation checks. No SQL was skipped and no test threshold was reduced.

Other acceptance commits: `1919a98`, `b60c273`, `ad86680`. This plan introduces no new database migration; the exact operational-payload migration belongs to 08-15.

## Final evidence

| Gate | Result and retained artifact |
|---|---|
| Full backend | **1,511 unique passes: 1,091 unit + 420 integration; 383 real-SQL scenarios; zero skips.** `.local/phase8-16-final-strict.log` and `.local/phase8-16-final-strict/` |
| Complete integration inventory | All 420 discovered names equal all 420 executed names, no differences. Assembly SHA256 `E08FFFC444DB2350F619395A2911D93428055C113DED92EB8A8EE8B70C3DFB8F`. `inventory-proof.json` in the strict directory |
| Full commercial aggregate | Five stages passed with unchanged source/assembly hashes. `.local/commercial-suite/2026-09-21T06-55-30-182Z-e697e685-bbbb-41d1-aa17-2f3212527ab5/report.json`. Its 1,096-case gate overlaps the full backend total and is not added to it |
| Retained underwriting | `.local/underwriting-suite/2026-09-21T06-15-42-927Z/report.json`; includes retained quote and agency journeys. Explicit terms UI fixtures are not claimed as SQL publication proof |
| Retained servicing | All 17 stages passed with two preserved resume attempts. `.local/servicing-suite/2026-09-21T06-23-21-643Z-76069fa0-830a-4fa4-a7b4-82da34e1073f/report.json` |
| Root/frontend | 399 source/contract tests and 172 frontend tests passed; lint, typecheck, API/solution and frontend production builds passed. `.local/phase8-16-resume-root.log`, `.local/phase8-16-hydration-{tests,lint,typecheck,build}.log`, `.local/phase8-16-debug-build.log` |
| Repeated initialization | All 139 retained table fingerprints unchanged across two additive initializations. `.local/phase8-16-final-initialization/report.json` |
| Actual restart | Three policy graphs, 12 versions, five pinned exposure readings and original key hashes preserved after restarting verified owned preview processes. `.local/phase8-16-final-restart/compare-report.json` |

The full integration run completed successfully in 4.1284 hours. Original failed baselines, interrupted attempts and the initially incorrect preview rewrite are retained but excluded. Targeted repeats and aggregate overlaps are never added as unique cases.

## Review, source and boundaries

The final review found no unresolved HIGH/CRITICAL defect. Every original source identity remains: 166 capture controls, 109 questions, 60 policy controls, 298 display occurrences, seven supplemental facts and 27 branches. Existing Phase 9/10/11 owners remain explicit; no missing Phase 8 behavior was relabelled as future work. See 08-SOURCE-AUDIT.md and 08-VERIFICATION.md.

Desktop and 390px capture, issued policy and adjustment receipt screenshots were inspected; the cover schedule's horizontal scrolling stays contained. Automated keyboard/error recovery checks pass. Human business/assistive-technology UAT, hosted CI and Docker runtime are unperformed.

The retained demo is policy PL-CC-0000000025 under independently approved agency AG-0000154. Imported AG-DEMO-QUOTES has incomplete historical terms and is not repaired by rewriting immutable history. No sales-funnel changes, real provider communication or cash refund are claimed. Document requests are queued content, not generated or delivered files.

## Self-check

PASSED. Both plan tasks and all final acceptance gates are complete. Continue with the explicit Phase 9 operational handoff.
