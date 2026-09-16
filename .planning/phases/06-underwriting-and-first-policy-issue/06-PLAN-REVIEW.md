# Phase 6 plan review

Status: **PASS after one revision**, 2026-09-16. Sequential inline review using gsd-plan-checker dimensions and the local Codex skill adapter; no independent agent or external peer review is claimed. This is executable-plan approval, not a runtime verification result.

## Findings corrected before approval

| Severity | Finding | Correction / verification |
|---|---|---|
| BLOCKER | Initial rating draft restricted new business to annual despite QuoteTerm/QuoteTermTests supporting short-period capture | RULE-CATALOG now defines both existing term kinds, London civil-duration proration, partial days and180-day worked example; the existing design-rules maximum anniversary is retained with an explicit eligibility blocker for longer captures;06-01/02 cover pointer fixtures, arithmetic and effective interval boundaries |
| BLOCKER | Creating all underwriting/capacity/terms storage in06-02 made a broad migration precede feature knowledge | Feature-owned migrations now belong to06-02/05/07/08/10, each with blocking isolated/retained SQL application and history checks |
| BLOCKER | Provider approval text alone could appear to override a hard binder limit | RULE-CATALOG and06-07/11 require typed authorised dimensions, exact submission/cycle hash and validity; no global authority mutation or unrelated blocker waiver |
| BLOCKER | Acceptance-evidence creation could change the assurance hash while recording acceptance | Evidence must exist and be reviewed before computing acceptance hash; acceptance itself is excluded from assurance input;06-08 explicitly tests the complete signature/send/accept path |
| WARNING | Planned client service filename did not exist |06-13 explicitly extends actual ClientEndpoints.cs list/records routes and existing projections; no parallel fictional client service required |
| WARNING | New service files without registrations could pass unit tests yet leave unavailable routes/workers |06-03/05/07/08/11/13 explicitly own Program.cs and ActorContext integration, dispatcher/allowlist where relevant; API and real browser checks prove wiring |

No unresolved blocker or warning remains in the plan review. Any contract/source discovery in06-01 that changes these assumptions must update affected plans before dependent runtime execution; it may not be silently deferred.

## Goal-backward coverage

| Required outcome | Implementation tasks | Acceptance proof |
|---|---|---|
| UWR-01 repeatable rate and provenance |06-01-01,06-02-01,06-03-01,06-04-01 | Typed source fixtures, pure exact arithmetic, SQL fenced worker and both-product rating browser |
| UWR-02 independent referral routing |06-02-01,06-03-01,06-05-01,06-06-01 | Every dimension mapped, stored submission/assignment, missing proof plus excess stock shown independently |
| UWR-03 authorised decision/conditions |06-05-01,06-06-01 | Narrow/broad actor boundaries, selected all-or-none decisions, actual proof and typed condition resolution |
| UWR-04 capacity correspondence |06-07-01 | Persisted submit/outcome/attempt/evidence, supplied-response provenance, exact extension and stale/deduped events |
| UWR-05 exact quotation/acceptance |06-08-01,06-09-01 | No signature deadlock; safe recipients, queued versus delivered, current version/assurance and real acceptance evidence |
| UWR-06 valid unique bind |06-11-01,06-12-01 | Stale/expired/revoked/servicing denial; different-key concurrency; failure after posting; exact retry/readback |
| UWR-07 complete policy and effects |06-10-01,06-11-01,06-12-01,06-13-01 | Same-owner immutable issued JSON, term/transaction/obligation, balanced journal and durable document work; real navigation |
| Inherited QUO-01/03/06, CLI-01 and AGY-04 policy portions |06-05-01,06-11-01,06-13-01 | Applied endorsements, actual progressed-state invalidation, policy discovery/registration/client links/accepted agency cookies; generic agency tasks remainPhase9 |
| All source, persistence and regression |06-14-01/02 | Real no-reset both-product suite,37 retained journeys, fresh full results, actual process restart/hashes and goal verification |

## Checker dimensions

1. **Requirements — PASS:** UWR-01..07 each has behavior-level implementation and negative tests, not merely frontmatter references. Inherited partial requirements have explicit owners and are not prematurely completed.
2. **Tasks — PASS:**14plans/33tasks. Each has files, read_first, concrete action, observable acceptance, automated command and done. Schema commands apply before browser verification. No placeholder test or skipped SQL gate.
3. **Dependencies — PASS:**14strictly sequential waves; each plan depends on the preceding one.06-01 exact contract gate precedes all runtime. Rating creates the initial referral shell before decisions; terms precede acceptance; posting precedes atomic issue; UI issue uses real issue API; discovery uses actual policy identities.
4. **Key links — PASS:** UI→typed endpoint→current scope→atomic storage/outbox→fenced worker→current readback is explicit. Program/identity/job allowlists are owned, as are client/agency projections and generated contract integration.
5. **Scope — PASS:** Feature slices separate backend/UI where larger. Schema split keeps each migration review bounded.06-07 remains a single capacity slice with existing worker/dialog patterns and at most3tasks; actual scope expansion triggers further splitting before execution. No sales-funnel modifications or future lifecycle/admin/document/finance workspace creep.
6. **Goal-derived invariants — PASS:** Captured closures cannot authorise progression; all dimensions conjunctive; immutable IDs and hashes; exact acceptance; one transaction/one first issue. Preconditions tested through public APIs rather than trusting UI flags.
7. **Context — PASS:** D-01..12 mapped in PLAN-INDEX. Source14days overrides explicitly provisional30days; SQL2022 retained; fictional adapters; both MotorTrade products, CC later; no implicit real messages/payments/deployment.
8. **Validation — PASS (planning):** All33tasks have feedback commands and named test/fixture owners. Fresh full backend for changed backend slices; frontend build/actual browser for UI; scoped migrations and final restart. wave_0_complete remainsfalse until06-01 executes. Runtime passes remain pending.
9. **Security — PASS (planning):** Every plan contains a threat_model block with HIGH/CRITICAL tests. Source scope, current auth before replay, composite ownership, authority, provider provenance, lost responses and disclosure have explicit cases; issue rollback/concurrency are blocking.

## UI contract review

06-UI-SPEC passes Copywriting, Visuals, Color, Typography, Spacing and Registry Safety as design. Source-specific238px shell/314px rail/390px containment, token exceptions, exhaustive state mapping, meaningful demo/amount-due copy, focus/retry and action owners are explicit. No images/runtime screenshots or human accessibility acceptance are claimed.

## Mechanical evidence

GSD verify.plan-structure reports14valid plans,33tasks,0errors,0warnings in `.local/phase6-plan-structure.json`. Supplemental checks require all7requirements/all12decisions; all51Phase6 placement controls, Phase6 display occurrences and all15shared Phase6 operation IDs have task owners.211candidate controls/98display occurrences retained with original source hashes and explicit later owners. These checks prove planning traceability only.06-01 provides exact schema pointer/option validation;06-14 supplies runtime coverage.
