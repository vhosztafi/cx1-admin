---
phase: 07-policy-lifecycle-and-history
plan: '04'
status: complete
completed: 2026-09-17
requirements: [POL-01, POL-02, POL-03, POL-06]
requirements_completed: []
production_commit: 0d38d91
---

# 07-04 - typed servicing editors and stable-item comparison

All eight prototype adjustment categories now save typed proposals through the
scoped, leased draft API: driver add/remove, vehicle add/remove, premises,
business/trade, policyholder and cover. Six focused editors replace the planned
monolithic editor. They reuse existing capture controls and pinned references.
Issued policy JSON, hashes and registrations remain unchanged by draft capture.

## Implementation

- Closed typed payloads and stable targets reject foreign ownership, system fields,
  client transfer, duplicate identities and invalid dependent references. Explicit
  replacement supports clearing fields. Vehicle selection and required status are
  declared rather than inferred from a removed vehicle.
- Dates use explicit London clock choices, term/latest-version bounds and current
  backdate authority. Only cover changes may override the common date. Assessment
  checks each cumulative risk slice; a later correction cannot hide an earlier
  invalid slice. Capture readiness does not grant rating or issue authority.
- The scoped, no-store editor GET returns base/proposed captures, saved comparison,
  changes, readiness and slices. The catalogue has 380 operations. Saved revision
  identity remains the applicability boundary for later workflow authority.
- Dialogs retain unsaved input across ownership changes, restore focus, and expose
  real Apply/Discard/Keep form controls. Dirty local state is distinct from saved
  comparison. Whole-cover and section editors preserve future dated intent.
- Two failing-first review cases fixed spurious differences from reordered GUID
  membership sets and incorrect displayed positions after stable-ID row sorting.
  Shared quote comparison behavior is unchanged.
- All 38 source actions owned by this plan have verified implementations. Duplicate
  prototype navigation uses the policy's Servicing drafts entry; fixture names use
  actual record identities. Exact equivalences are in 07-04-REVIEW.md.

## Measured verification

- Final backend: 33 focused unit cases and 1 native SQL2022 integration case, zero
  skips, in `.local/phase7-04-reviewed-final/{unit,sql}`. Result assertion verified
  34 passing cases including real SQL, with cutoff 2026-09-17T15:27:00Z. SQL covers
  persisted editor projection, explicit replacement/vehicle declarations, revoked
  roles and unchanged issued bytes/hash/registrations.
- 116 frontend cases: `.local/phase7-04-final-web-tests.log`; 351 root cases:
  `.local/phase7-04-final-contracts.log`; 4 source cases after coverage updates:
  `.local/phase7-04-final-source.log`. Typecheck and lint passed in the corresponding
  `.local/phase7-04-final-{typecheck,lint}.log` files.
- Next production build: `.local/phase7-04-cover-build.log`; final API Release build:
  `.local/phase7-04-final-api-build.log`, no errors or warnings. OpenAPI validated
  with 24 existing warnings in `.local/phase7-04-specified-openapi.log`.
- Full editor browser gate: 16 actual journeys, eight scripts for both Motor Trade
  products, in `.local/browser-evidence/servicing-editors/report.json`. Per-case
  immutable reports retain SHA256, execution times and policy identities. Evidence:
  `.local/phase7-04-editors-final-browser.log` and `-resumed.log`. A comparison
  locator was scoped after matching a driver dropdown option; the failed attempt
  was not counted as a pass. Resume checks retained results and script freshness.
- After both comparison fixes, saved-review browser journeys passed again for both
  products: `.local/phase7-04-final-review-browser.log`. Date persistence, comparison,
  dirty retention, removal, 390px layout and issued immutability were asserted.
- Desktop/mobile screenshots were inspected. Browser audit includes ownership
  takeover/retained forms and invalid non-cover date overrides. No human business
  UAT or assistive-technology assessment is claimed; hosted CI/Docker remain unrun.

## Commits and remaining scope

Production checkpoints: f654ae8, cad94b0, 72b2e55, 7cbb2dd, 4bd9a3f,
cd54956, 4fe6e84, b9588b1, e8f4701, 480a5e5 and final 0d38d91.
No unresolved high review finding. Sales funnel sources remain unchanged and demo
storage was preserved. Next is 07-05 persisted rating cycles and cumulative-slice
pricing. Plans 07-05 through 07-16 and compound phase requirements remain open.
