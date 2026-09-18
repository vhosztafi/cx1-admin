---
phase: 07-policy-lifecycle-and-history
plan: '08'
status: complete
completed: 2026-09-18
requirements: [POL-04, POL-07]
requirements_completed: []
production_commit: 18166c6
---

# 07-08 — Servicing terms delivery and exact acceptance

Both Motor Trade products now support prepared immutable servicing contracts,
reviewed signature proof, scoped demo delivery and separately recorded acceptance.
Current applicability checks never infer acceptance from queued delivery or restore
withdrawn proof. Changes preserve the original contract and acceptance history.

## Implementation and review

- Four additive migrations retain exact revision/base/rating/template owners,
  complete dated proposal slices, price, contractual conditions and configuration.
  Contract hashes cover exact retained JSON. Proof-purpose fingerprints separately
  bind signature and acceptance evidence to the exact contract. SQL checks owned
  pointers, provenance, actual successful provider outcomes and append-only records.
- Send requires current reviewed signature and live relationship contacts. Its
  dedicated outbox job retains recipient/document bytes, payload hash, assurance,
  attempts and provider operation. Duplicate/time-out retry is idempotent; late or
  changed-scope completion is superseded. Terminal failure records its real attempt.
- Acceptance requires actual current delivery, current proof/authority, exact
  contract and assurance hashes, UTC received time and accepted purpose-specific
  proof. The current review ID is retained. Withdrawal, amendment, rerating,
  recipient changes, revocation and changed configuration remove applicability.
- Public APIs have bounded closed bodies, CSRF, current authorization before
  receipt replay, parent ETags and editing leases. History uses signed bounded
  cursors; lists omit large contract bodies and one owned historical document can
  be opened separately. Historical documents explicitly convey no current authority.
- Typed signed-statement conditions check current contract identity/hash. Explicit
  resolution requires current accepted matching proof. Internal condition proof
  checks now precede receipt replay; carrier condition/source application uses the
  same terms ownership check. Existing referral/carrier regression passed.
- Workspace controls prepare/review/send/record acceptance, show exact retained
  risk slices and price, and retain uncertain commands unchanged. A lost send
  response retries identical bytes/key/ETag/lease and creates only one delivery.
  Stale readback cannot display a current acceptance label. Mobile layout follows
  the existing prototype design; the sales funnel snapshot is unchanged.
- Principal commits: c65f892 contract/proof;8fcbced durable delivery;bdfa31f
  acceptance/API/history;18166c6 condition integration and verified workspace.

## Measured verification

- Final31cases:27unit+4SQL, zero skips, .local/phase7-08-final/{unit,sql}; gate
  cutoff2026-09-18T11:40:00Z. SQL verifies both products' storage/model/migrations
  and HTTP command/history flows. All24 actual current/history HTTP responses
  validate against closed schemas in .local/phase7-08-final/responses.
- Both product signed-condition SQL cases pass in .local/phase7-08-condition-green.
  Existing referral/carrier regression .local/phase7-08-condition-regression has
  29unit+3SQL, zero skips; gate cutoff11:25UTC.56 API/frontend tests pass in
  .local/phase7-08-condition-web-contracts.log. Runs overlap; counts are not summed
  as a unique suite. Earlier targeted delivery-failure evidence is in PROGRESS.md.
- Browser .local/browser-evidence/servicing-terms/report.json completed
  2026-09-18T11:43:57.848Z. Both products verify real UI preparation, proof review,
  demo delivery, exact lost-response retry, separate acceptance, amendment showing
  historical acceptance, fresh rating without carried acceptance, preserved history
  and unchanged issued snapshots. Desktop/390px terms and acceptance screenshots
  inspected. No browser console errors. Only harness-owned drafts were abandoned.
- Release API build passes with0warnings; final Next production build/typecheck
  passes in .local/phase7-08-web-build-current-label.log; final web lint passes in
  .local/phase7-08-final-lint.log. OpenAPI403operations passes lint with37 unused
  component warnings, including three superseded provisional input schemas.
- Shared demo upgrade preserved109 pre-existing table count/hash records, excluding
  newly introduced nullable columns and new aggregates. Added only2 missing terms
  templates and1 delivery scenario, without full demo reinitialization/reset.
  Evidence .local/phase7-08-preservation-{before,after}.txt and SQL alongside it.
  Current owned local preview PIDs: .local/phase7-08-preview-pids.json.

## Deviations and remaining boundaries

The plan's single-file suggestions became feature-local partial services, a
dedicated endpoint file, migrations/guards, contract generator and focused tests.
The existing shared SQL fixture hosts terms scenarios; a filter by the proposed
ServicingTermsTests integration class alone would miss them. Use the recorded
scenario filters/results, not a zero-test run. Initial history404, EF projection503
and render-time clock lint errors were fixed and rerun; none are counted as passes.

No original prototype control or field is assigned solely to07-08; this supplies
the terms/acceptance prerequisite for issue and renewal owners. Those controls
remain with07-10/12. POL-04/POL-07 remain open until their downstream scope is done.
No financial posting, issue, renewal invitation, real email, PDF rendering, hosted
CI, Docker or human business UAT is claimed. Continue07-09 inline.
