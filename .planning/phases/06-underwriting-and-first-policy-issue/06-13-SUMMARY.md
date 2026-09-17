---
phase: 06-underwriting-and-first-policy-issue
plan: '13'
status: complete
completed: 2026-09-17
implementation_commit: a19685e
requirements_completed: []
---

# 06-13 — Policy discovery and safe agency sharing

Implemented in `a19685e`. Internal policy discovery searches actual current issued
records by reference, client, agency and normalized registration, with product,
state/date filters, ordering and protected pagination. Client Policies and Activity
open real policy identities. Agency sharing returns only seven allowed summary
fields and applies current own-agency/relationship permissions before counts,
pages or detail. Hidden registration/risk/evidence cannot be queried through that
projection. Desktop and mobile UI reads the same persisted records.

The retained integration journey exposed a pagination defect: database-wide row
versioning included normal session/job writes. Quote and policy discovery now
fingerprint relevant record counts and versions under the scoped read transaction.
Relevant quote/client/agency changes still invalidate a cursor; session maintenance
does not. The real SQL regression failed before this fix and passes after it.

## Verification

- Full fresh `.local/phase6-13-backend-full-2`: **836 passing tests** (642 unit,
  194 integration), including **167 real SQL scenarios**, zero skips. Integration
  duration 32m53s. Exact start-bound gate passed in `phase6-13-result-gate.log`.
  The first full run exposed the obsolete client-policy unavailable assertion;
  it was corrected to the actual available-empty list and the entire suite rerun.
- Targeted cursor RED: `phase6-13-cursor-red`; GREEN eight SQL cases:
  `phase6-13-cursor-green`. The full run also includes session-maintenance paging
  for the restricted agency policy projection.
- **104 frontend tests**, lint/typecheck and production build pass in
  `.local/phase6-13-{web-final,lint-final,typecheck-final-2,build-2}.log`.
- Contract/source run `phase6-13-contracts-final-2.log`: **327 passing cases**,
  comprising 320 contract/source tests plus seven forthcoming result-gate cases.
  OpenAPI validates 363 operations with 12 retained unused-component warnings.
- Actual Chrome both-product discovery/sharing journey passes in
  `phase6-13-browser-final.log`; desktop/390px screenshots inspected under
  `.local/browser-evidence/policy-discovery`. Relevant retained integration
  navigation passes in `phase6-13-integration-fix.log`.
- Repeated additive initialization preserves **44 count/hash sets**, including
  issued policy graphs, old revisions, postings, work and credentials; evidence
  `.local/phase6-13-preservation*`. Missing actual policy-issued activity is added
  once, without rewriting existing records.

## Boundaries

Commercial Combined, servicing/as-at reconstruction, generic task sharing,
document generation, finance administration and global advanced search retain
their approved downstream owners. The final all-37 journey run, capacity source
supplement, restart and requirement signoff remain 06-14. Human UAT, hosted CI and
Docker were not performed. No sales-funnel changes, reset or real external send.
