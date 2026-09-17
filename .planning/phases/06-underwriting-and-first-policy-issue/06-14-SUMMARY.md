---
phase: 06-underwriting-and-first-policy-issue
plan: '14'
status: complete
completed: 2026-09-17
implementation_commit: 40911be
requirements_completed: [UWR-01, UWR-02, UWR-03, UWR-04, UWR-05, UWR-06, UWR-07, QUO-01, QUO-03, QUO-06, CLI-01]
---

# 06-14 — Final carrier workflow and durable acceptance

Implementation `40911be` closes the conditional capacity source controls and adds
repeatable full acceptance, fresh-result and preservation gates. Withdraw/reopen
preserves correspondence and monotonic pointers while draft state fences old
responses and authority. Active senior assignment is audited routing, not extra
authority. Query replies/chasing make explicit new submissions. Similar cases are
actual scoped records with real parent links. The final Combined journey resolves
reviewed W-07 proof through delivery, exact acceptance and actual first issue.

## Measured verification

- **838 backend tests pass**:642 unit/196 integration, **169 real SQL**, zero skips.
  Fresh `.local/phase6-14-backend-final-2`; integration duration **36 m 38 s**.
  Exact UTC-start-bound gate: `phase6-14-result-gate.log`. The initial restricted
  shell run failed Windows SQL encryption initialization; it was not accepted.
  The complete native run, rather than selected failures, was rerun successfully.
- **104 frontend tests**, lint and typecheck pass in
  `.local/phase6-14-{web,lint,typecheck}-final.log`; production build passes in
  `phase6-14-build-1.log` with API origin5087. No runtime frontend edit followed
  that build; the optional response-field type refinement also typechecks.
- **334 contract/source/gate tests pass**, OpenAPI364 operations validates, in
  `phase6-14-contracts-final-2.log`. Seven source semantic tests pass again after
  final audit reconciliation (`phase6-14-source-final.log`). Thirteen result and
  preservation gate cases reject failures/skips/stale/duplicate/missing/changed
  evidence; their stub inputs are not SQL evidence.
- Complete actual Chrome suite passes in `phase6-14-suite-5.log` and
  `.local/underwriting-suite/2026-09-17T06-18-58-078Z/report.json`: carrier-conditions
  issue, retained both-product policy readback, discovery/sharing and **all37**
  earlier journeys (17quote/20agency-client). Three retained agency terms stages
  are labelled UI fixtures; actual lifecycle publication has separate SQL proof.
- Carrier demo policy **PL-MT-0000000005**,885feca7-dcb0-48d9-8b39-c2ef6b795f5a,
  is linked to quote b088982c-2b8f-4ee4-8614-b34a983c09ee and escalation
  bab60d4e-fd6c-4dcf-a77e-a37f563930cf. The report retains receipt IDs/hash and
  actual supplied-response/condition/acceptance provenance. The expiry409 is
  explicitly a UI failure fixture; real SQL clock-advance tests assert zero issue
  side effects. Desktop/390px screenshots inspected;314px rail/overflow tested.
- **Actual restart passes**: previews66980/70364 stopped,53128/30204 restarted.
  Fresh cookies compare three policy graphs across both products, full compound
  reads, source revisions, acceptance, evidence/referrals/ratings, balanced journals,
  document requests and carrier actions/attempts/messages. Live capacity JSON also
  satisfies the published schema. Evidence `phase6-14-restart-{capture-2,verify}.log`
  and `.local/browser-evidence/underwriting-restart/report.json`.
- **Two additive initializations preserve44 validated count/hash sets** in
  `.local/phase6-14-preservation-final-1`, with owned workers stopped. The earlier
  06-13 query returned SQL errors and is explicitly withdrawn as evidence. The
  committed corrected script rejects SQL errors/malformed or duplicate rows,
  hashes empty sets explicitly, and compares actual records; it never resets.

## Source, review and handoff

06-SOURCE-AUDIT retains211candidates:51Phase6 placements and160other-phase owners;
98display occurrences:45Phase6,47retained capture and6later-phase. Seven conditional
capacity entries have concrete operations and runtime evidence. No later module is
silently marked implemented. See06-14-REVIEW and06-PHASE07-HANDOFF for inline security,
UI, atomic-write review and policy/finance/document lineage. No unresolved high or
critical implementation finding remains. This was inline review, not independent
peer review or human acceptance.

AGY-04 remains partial solely for Phase9 generic task sharing; safe policies and
quotes are verified. Servicing/as-at/renewal/cancellation arePhase7, CCPhase8,
document generation/messages/tasksPhase9, collections/statementsPhase10, admin
editorsPhase11 and global reporting/searchPhase12. Document work remains requested,
not generated; money is due, not collected. Human business/assistive-technology
UAT, hostedCI and Docker are unperformed. No sales-funnel change, reset, live
provider/message/payment, portal or deployment occurred.
