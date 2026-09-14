---
phase: 03-clients-and-contact-servicing
plan: '05'
status: complete
requirements: [CLI-04, CLI-01]
completed: 2026-09-14
---

# Persistent duplicate intake review

All three tasks are complete. Immutable MatchSubmission identity, MatchReview comparison/rule evidence, append-only MatchDecision and MatchInformationRequest records persist in SQL. Current associations are separate from captured candidate evidence. Composite ownership keys and migration triggers protect identity, rule version, retained separate-account identity and trail immutability. Six fictional saved intakes span two agencies; repeat initialization preserves prior decisions.

The audited decision service implements link, separate, decline, query and reopen. Stable intake/review/client/relationship locks, ETags and command receipts make concurrent/repeated requests safe. Separate creates at most one separate account per intake and later reuses it. Link creates or reuses only the submitting agency relationship. Existing contacts, people, support flags and visibility grants remain unchanged. Reopen preserves prior association and decisions. Query records a request without sending a message. Exceptions roll back domain changes, history, audit and receipts together.

Internal match-read and match-review capabilities are distinct and currently restricted to underwriting roles. Scoped list/detail/trail APIs apply current authorization before replay. Mutation receipts contain only the review ID and original ETag; evidence is loaded through a fresh authorized GET. The trusted submission-side projection has only submissionId/state and exposes no candidate or hidden metadata. No broker HTTP session is enabled.

Clients now links to Duplicate reviews for authorized staff. The paged/filterable list opens a source-faithful blue header, captured comparison, Rule tab, actual actor/reason/time trail and 314px decision rail. The rail follows the main content on mobile. All five actions require a bounded reason. Uncertain saves retain the exact body/key/ETag and freeze inputs; retries confirm the original command. Drafts survive evidence/Rule switches. Stale decisions require explicit reload. Matching activity links resolve to the real review. Candidate relationship ID was added to the read DTO so the header loads the captured agency rather than guessing from another relationship.

Verification: full backend baseline `.local/match-api-regression-results` passed 98 unit +25 integration =123 cases, including nineteen real-SQL scenarios and zero skips; report gate 123/19 passed. The later candidate-relationship DTO assertion passed in `.local/match-ui-api-results`. Sixty-three contract/design tests passed for the API slice; the final DTO refinement passed ten party-contract tests and OpenAPI lint. Fifteen frontend tests, lint, TypeScript and production build pass. CI minima remain Windows123/19 and Linux121/17; hosted execution is not claimed.

Matching Chrome checks pass all outcomes, persisted reload, retained separate identity, lost-success same-key replay, frozen controls, draft tab retention, stale-version reload, safe list failure recovery, role denial and 390px keyboard/layout checks. Evidence/Rule desktop and mobile form screenshots were visually inspected. The client regression initially hit a detail-load timeout after creating a record; the final client and contact browser rerun passed, including replay, stale recovery, consent, primary replacement and retained history. No error logs explained the transient timeout; Phase 3 final regression remains responsible for checking the assembled slice. Browser fixtures and their history remain in the demo.

Production commits: ce3436a (storage/fixtures), 2fcee7d (audited APIs), 32fab3c (UI/browser/docs). CLI-04 implementation is complete pending final Phase 3 verification. CLI-01 remains partial for actual quote/policy links in Phases 5/6. Real quote capture, downstream progressed-quote guards and message delivery remain owning-phase obligations. Human UAT is unperformed. Sales funnel unchanged. Next: 03-06 final acceptance and reviews.
