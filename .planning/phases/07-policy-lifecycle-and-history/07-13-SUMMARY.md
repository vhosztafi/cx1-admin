---
phase: 07-policy-lifecycle-and-history
plan: '13'
status: complete
completed: 2026-09-19
requirements: [POL-09]
requirements_completed: []
---

# 07-13 — Cancellation proposal, preview and approval

Both Motor Trade products now support saved cancellation reasons, effective dates,
notice evidence, server-computed component returns and explicit current approval.
Policy Cancel actions open real typed drafts. Amend and abandon retain history.
Review creates neither cancellation cover nor a cash payment; atomic issue is07-14.

## Implementation and review

- Five versioned fictional reason rules enforce evidence, actual notice delivery,
  minimum London calendar days, current delegated authority and distinct senior
  approval where required. Generic administration does not bypass approval.
- Preview hashes bind revision/base, complete posted ledger, later issued terms,
  configuration and evidence/review applicability. Edits, actual adjustment/renewal
  issue, revoked grants and configuration changes invalidate applicability.
- Returns preserve each original component's interval and lineage, including
  negative adjustments. Fees remain retained. Decimal amounts are strings on the
  API; signed credits render correctly and cash paid is explicitly zero.
- Four append-only tables use compound ownership keys and SQL source guards.
  SQL checks bind both input hash and retained result bytes; approved amounts
  cannot be substituted independently of the hash. Authorization precedes replay.
- Six strict API routes require current scope, CSRF, strong ETags, idempotency
  keys and edit leases for mutations. Browser retries freeze the original request.
- Feature-local persistence, endpoint and assessment/evidence files refine the
  proposed larger Servicing files into manageable modules. Existing shared draft,
  upload screening, command and authority boundaries remain in use.
- Three source field entries for Issue cancellation and Consequences on issue
  are assigned to07-14, which already owns atomic issue. They are not reported
  as implemented review outcomes. All other07-13 fields/controls have evidence.

## Measured verification

- Final clean gate: **27 passing cases,16 unit and11 real SQL, zero skips** in
  `.local/phase7-13-corrected`, verified by `assert-test-results.ps1`.
  This includes3 review cases,2 actual adjustment issue cases and6 renewal
  lifecycle cases. Tests independently reproduce362.17,462.77 and326.96 credits.
- Three additional real HTTP/SQL cases pass in `.local/phase7-13-http-sql`:
  unauthorized access, CSRF, ETag/key/lease, unknown fields/query and media checks.
- Two actual Chrome/Next/Kestrel/SQL journeys pass in
  `.local/phase7-13-browser/sql.trx`, completed05:30:32UTC. They edit reason/date,
  upload and review delivered notice, reject requester approval, lose/retry a
  committed preview response, switch to a distinct senior, approve, reload and
  abandon. Actual policy/financial history remains unchanged.
- Latest screenshot folders are `CoverMGA_Test_43325b152e6c43bb8c79d73898880b83`
  and `CoverMGA_Test_80456b08df234f7cbdfc46ed33a63ee3` under
  `.local/browser-evidence/cancellation-review`. Desktop/mobile screenshots were
  inspected after fixing negative amounts displaying as Unavailable.
- Six fresh captured HTTP responses validate against closed schemas and reject
  unknown properties in `.local/phase7-13-browser-responses`.
-19 contract/source/frontend checks, TypeScript, ESLint, production Next and
  Release API builds pass. Release has zero warnings/errors. OpenAPI is valid
  with41 existing warnings. EF reports no pending model changes.
- Failed intermediate runs remain recorded: incomplete revocation metadata,
  fixture clock mismatch and a ledger probe missing its cancellation reason.
  Corrected final cases pass; failed and overlapping reports are not added to
  the clean gate totals.

## Demo preservation and continuation

Migration20260919044848_CancellationReviewStorage preserves **123 existing table
and setting count/hash rows**, compared in `.local/phase7-13-before.txt` and
`after.txt`. It adds one cancellation configuration and empty review tables;
no reset/full reseed. API5087 and web3100 both return200 after restoration.
Owned hidden preview processes are in `.local/phase7-13-preview-pids.json`.

Sales funnel files remain unchanged. Human business/assistive-technology UAT,
hostedCI and Docker were not performed. Continue07-14 inline, then finish07-09
cancellation lineage and07-15/16. POL-09 remains open until issue is verified.
