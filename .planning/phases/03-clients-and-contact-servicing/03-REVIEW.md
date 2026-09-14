---
phase: 03-clients-and-contact-servicing
reviewed: 2026-09-14
status: passed
method: inline source, SQL/API and browser review
---

# Client servicing code and security review

Scope: Phase 3 identity/relationship/contact/support/matching services and HTTP boundaries, ActorContext, scoped reads/paging, mutation receipts, browser forms and regression scripts. Storage invariants were cross-checked against real migration/constraint tests. This is an inline agent review, not an independent penetration test or production approval.

## Findings fixed

- P2: Client activity and restricted support history replaced persisted actor identities with the generic label Back office staff. Both now resolve display names only for the actors in the authorized page, preserving null System events and an explicit unavailable-identity fallback. SupportFlagApiTests asserts actual labels in activity and every history row; client/support browser scripts assert rendered names. No actor fields were added to agency-safe instructions.
- P2: Match status changed visually without an explicit accessible result announcement. MatchWorkspace now retains a status live region across reload and both tabs. The matching browser helper waits for the confirmed review-status announcement after a decision. Errors and loading retain their existing alert/status feedback.
- P3: Shared loading/paging copy called support/match records client records. It now uses neutral Records wording.
- Test reliability: Shell geometry was measured before streamed content became visible, producing width0. The harness now waits for the expected heading and visible sidebar, retaining exact 238px/62px assertions. No arbitrary delay or weaker assertion was introduced.

## Reviewed boundaries

| Area | Evidence and conclusion |
|---|---|
| Authorization and disclosure | ActorContext separates identity/contact/support/matching capabilities; AgencyId actors cannot obtain internal capabilities through a role. PartyScope filters before discovery and paging. Support safe preview includes only active same-person membership and explicit grants; internal detail, history and hidden totals are absent. Match evidence/trails use match-read; ordinary activity filters sensitive event types before counting. |
| Replay | Typed shape/authorization checks precede SqlCommandBoundary. Mutable prerequisites and ETags run in its transaction after replay. Sensitive support/match receipts contain only ID/ETag. Current role loss denies replay. Ended-record history may authorize an original successful receipt without allowing a new mutation. |
| Contacts | Relationship parent locks serialize primary changes and first contact creation. Demotion flush avoids filtered-index conflict while remaining inside the caller transaction. Final primary count is checked. Person identity is not rewritten by declared contact edits; scoped reuse excludes inaccessible relationships. |
| Support | Stable parent locks plus flag version prevent competing grant/history mutations. Consent declined is rejected before sensitive intent persistence. Grant reconciliation preserves unchanged provenance. Relative review-date validation runs after replay. End retains history even after original membership ends. |
| Matching | Intake -> review -> client -> relationship locks; immutable evidence and pinned rule; one retained separate client. No destructive merge, contact copying or inferred sharing. Link checks active association and agency availability. Query remains recorded only. Phase 5 must introduce the real progressed-quote guard before linking quotes. |
| Inputs and storage | Bounded strict JSON rejects duplicate/unknown/null fields; parameterized SQL and EF queries avoid string-built user SQL. FKs, JSON/UTC checks, rowversion and append-only triggers are covered with real SQL. No sensitive logging or HTML injection introduced in the reviewed slice. |
| Paging | Protected cursors bind actor roles/scope, route, filters, ordering, page size and expiry. Every page re-evaluates current scope. Offset paging is a bounded MVP choice, not a transactionally frozen snapshot under concurrent edits. |
| Frontend | Server actor guards plus API enforcement; obsolete loads aborted, explicit relationship/person selection, retained uncertain command keys/body/ETag, frozen retry inputs and explicit stale reload. React text rendering; no localStorage of sensitive forms or raw server-error display. |
| Demo and cleanup | SQL scenarios use generated CoverMGA_Test names, clear attached-file settings and verify cleanup target before deletion. Browser records are explicitly fictional and retained in CoverMGA_Demo. No sales snapshot edits, real delivery/payment or deployment. |

Full native acceptance passed 123 cases/19 real-SQL scenarios without skips. The actor fix passed the targeted real SQL API scenario. All contract tests and relevant browser suites pass as detailed in 03-VERIFICATION.md. No unresolved material Phase 3 code finding remains. Current staff display names are resolved from persisted actors; immutable historical name snapshots are not claimed.
