---
phase: 05-motor-trade-quote-capture
plan: '03'
status: complete
completed: 2026-09-16
requirements_completed: []
requirements_supported: [QUO-02, QUO-03]
---

# 05-03 — quote creation and business capture

Both Motor Trade products now have persisted creation, saved receipt, resume and nine-stage wizard shells. The first three stages capture the complete source proposer/business/term slice through structured controls. Later stages remain explicitly unavailable. No raw JSON editor, sales-funnel change or final readiness claim is introduced.

## Delivered and reconciled

| Source / concern | Implementation and acceptance |
|---|---|
| Client/agency/product | Real relationship/product lookup, explicit create, durable revision receipt, scoped edit route and actor checks. |
| MTS-01 identity/contact/address | All15 direct mapped insured fields, independent full-name proposer list, legal entity/company number; title/company and consent/marketing answers use owned revision pins. |
| Business fields | Date established and description on prototype Proposer stage; turnover/wage roll, MTS-03-Q04..09 and trading-from on Trade activities. False, zero, empty selections and omitted answers remain distinct. |
| Prototype stages2/3 | All14 question-backed controls projected from their source stage and exact reference bindings. Later claims/vehicle questions sharing the same JSON container remain with their later owners. |
| Activity declarations | Seven separate prototype percentages and stable UUID occupation rows, add/edit/remove/reorder, exact basis points, duplicate/exclusive/minimum/total guidance backed by API assessment. |
| Conditional capture | Details remain visible and retained after controlling-answer changes; explicit clearing removes them. Required/inactive explanations are assessed without silently changing declarations. |
| Policy term | London local dates/times, annual/short-period, leap anniversary, DST skipped/repeated hours and independent end offsets. Partial intent persists; invalid complete intent blocks save. |
| Readiness navigation | Saved-revision issue paths and question identities link to actual rendered controls.43 distinct targets per product passed actual focus checks. Links disable while dirty/frozen; unknown or section-level errors are not falsely assigned to a field. Later checks remain separate and overall readiness=false. |
| Recovery | Frozen exact body/key/ETag retry, uncertain readback, retained412 comparison, explicit discard, dirty navigation warning, save/continue and save/exit receipt semantics. |

Final change e43af11 adds readiness links and aligns date/description placement. Earlier implementation and precise backend evidence are retained in05-03-PROGRESS.md. Final source review confirmed all15 insured direct mappings and all14 initial-stage prototype question bindings have controls. Generic VAT legal assurances from the reference funnel are not presented as legal advice or an invented capture answer; VAT declaration/optional number are preserved under the canonical contract.

## Verification

- Frontend59tests,0skips; ESLint, TypeScript and production Next build pass: `.local/phase5-readiness-{web,lint,types,build}.log`.
- Fresh contract validation293tests,949controls/336operations: `.local/phase5-readiness-contracts.log`.
- Latest backend run for this plan's backend changes:523=430unit+93integration,67realSQL,0skips; `.local/phase5-proposer-pins-20260916` and corresponding.log, checked fresh before run. Final changes were frontend-only; this backend result is carried forward, not described as rerun.
- Final actual Chrome runs: readiness56/57(43focused targets each), business recovery59/60, creation61/62, term63/64, proposer65/66, occupations67/68, business answers69/70, source questions71/72. References have prefixQT-MT-00000000. Logs `.local/phase5-readiness-{browser,business,create,term,proposer,occupations,business-answers,source-business}.log`; report JSON and screenshots under corresponding `.local/browser-evidence/quote-*` directories.
- Browser fixes included stable accessible Legal entity naming, waiting for scheduled focus, removing misleading section-level-to-first-name fallback, and making an older proposer mismatch selector specific after new source controls were added. All final journeys passed; failed fictional attempts retained.
- Desktop prototype styling and390px no-overflow exercised; final mobile page inspected. Earlier creation/term runs verify314px rail. No human or assistive-technology UAT claim.
- Owned final previews47192/49652 verified and stopped. No migration, database reset, external send or deployment. Working changes reviewed and git diff --check passed.

## Next

Execute05-04: driver basis, drivers and nested histories with stable identities, exact source references, chronology/eligibility and cross-reference checks. Keep server assessment blocker until05-06/08/10 are composed; no QUO signoff before05-11. Browser beforeunload can be overridden by a user; durable recovery after explicit leave and non-Chrome fallback are not claimed.
