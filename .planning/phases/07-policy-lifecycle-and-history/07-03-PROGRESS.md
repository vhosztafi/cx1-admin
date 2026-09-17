# 07-03 in progress — persistent drafts and editing leases

2026-09-17. This is a continuation note, not a completion SUMMARY. Plans07-01/02
are fully committed;07-03 remains incomplete and has no production commit.

## Working-tree implementation

- `backend/src/BackOffice.Application/Policies/ServicingLeaseRules.cs`: pure
  five-minute lease acquisition, generation/token replacement, explicit takeover
  capability/reason, holder/token checks, exclusive expiry, renewal and release.
  The token fences edits and never grants authentication or policy authority.
- `backend/tests/BackOffice.UnitTests/ServicingLeaseTests.cs`:4 tests for fresh
  generation/expiry, unauthorized/reasonless takeover, stale renew/release after
  another holder acquires, and release/renewal semantics.

RED `.local/phase7-03-lease-red.log` is missing-symbol compilation failure.
GREEN `.local/phase7-03-lease-pure-1.log` and fresh TRX:4 passed,0 skipped.
Full unit `.local/phase7-03-unit-full-1.log` and fresh TRX:663 passed,0 skipped.
No storage/API/browser lease behavior is claimed. These two new code files remain
uncommitted until the whole plan meets its gates; do not overwrite them.

## Next implementation

1. Add ServicingDraft/Revision/Lease records, model and an additive EF migration.
   Existing PolicyVersion already has (Id,TermId,PolicyId) alternate key; reuse it
   for same-policy/base-term/base-version ownership. Revision owns (Id,DraftId),
   draft current-revision pointer uses that key. Filtered uniqueness permits one
   active adjustment and one renewal per base term; cancellation may coexist.
   Append-only revisions and immutable base ownership need database guards.
2. Embed the07-01 servicing schema in Application and add bounded duplicate-safe
   proposal parsing/validation. QuoteCaptureShape.BuildBundled already uses a
   private registry with external schema fetch disabled. Existing QuoteHttpInput
   has a1MiB quote limit; servicing's approved limit is2MiB, so do not silently
   apply the quote limit or accept unbounded JSON. Preserve1,000 aggregate item
   cap, stable IDs, exact base/version, local intent and capture/readiness split.
3. Implement scoped create/read/save/abandon and lease commands through
   SqlCommandBoundary.ExecuteAuthorizedAsync. Current identity/grants must be
   checked under lock before receipt replay. PolicyScope currently takes shared
   locks for reads; the new write path needs a deterministic update lock before
   draft/lease access, avoiding shared-to-update upgrade races. Do not reopen the
   bound source quote. Persist revisions and change ETags atomically.
4. Add explicit internal draft-write/takeover capabilities to ActorContext and
   API authorization registration (currently neither exists), with tests denying
   agency/system-admin bypass and revoked roles. Lease token is a fencing value,
   always paired with current holder identity; never treat it as standalone
   authorization. Renewal/release cannot affect another generation.
5. Add actual API routes and a reachable Next servicing workspace with resume,
   save, explicit takeover/release, read-only conflicts and preserved local input.
   Creation needs the base term's strong ETag; existing policy record currently
   does not expose that header. Add a scoped term/draft read returning it rather
   than invent a version on the client. Use the existing retained demo policies.
6. Complete the plan's realSQL race/expiry/replay/immutable-history tests and
   two-session browser takeover/reload evidence before committing production,
   then SUMMARY, then state. Do not mark POL requirements complete prematurely.

## Retained verified state

07-01 commits66da10d/3a6b84d;07-02 b2b142f/a72617d;state de869a4.
07-02 final SQL `.local/phase7-02-temporal-sql-final-3`:9 passed,0 skipped,
including4 realSQL and5 synthetic chronology tests. Both-product browser passed
on the final build; report/screenshots in `.local/browser-evidence/policy-temporal`.
104 frontend tests,348 contract tests and API/Next builds/typecheck/lint passed.
379 OpenAPI operations. Future persisted servicing scenarios remain07-10/12/14.

Owned running previews are `.local/phase7-02-preview-pids.json`:API41036 on5087,
Next55000 on3100. Always verify PID command/executable before stopping; Phase6
PID files are stale. Demo SQL remains `CoverMGA_Demo` on `.\SQL2022`, unchanged
except existing authentication/session activity. No reset, migration or seed run.

`contracts/schemas/policy.schema.json` may show modified due to the Windows index;
its actual bytes were compared with `git show HEAD:` and are identical. No content
change needs committing there. Sales-funnel files remain untouched.
