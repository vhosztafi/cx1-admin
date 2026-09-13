---
phase: 01-data-and-api-design
plan: '03'
status: complete
requirements: [DES-04]
completed: 2026-09-13
---

# API and adapter contracts

Completed 283 OpenAPI operations, typed domain DTOs, scope/CSRF/ETag/idempotency conventions, error/example validation and explicit mappings for all 949 inventoried controls. The 99 navigation closures were resolved in an isolated VM with navigation intercepted, without browser/network/filesystem capabilities. Conditional capture rules supplement the finite render inventory; they do not claim exhaustive runtime-state testing.

Review added missing operational details: account/MFA transitions, independent agency approvals and abandonment, versioned evidence association/withdrawal, per-cover MTA dates, actual finance debtor/remuneration, short-period annual proration, capped NCD semantics, incident uncertainty, record/filter projections and versioned administration inputs. SQL dictionary and lifecycle/convention documents reflect these additions.

Nine internal adapter request/result pairs and typed failure envelopes compile strictly. Durable provider/inbox boundaries and recovery walkthroughs cover rollback, post-commit failure, restart after provider payment and duplicate/changed callbacks. Pure design-oracle tests verify expected effects; they are explicitly not SQL persistence or production-adapter proof.

Validation: 51 tests pass; OpenAPI lint passes without warnings; build-api-control-map --complete reports 949/949. Policy JSON examples cover all three products; generated and inline schemas compile with local references. Main delivery commit: d1a4923; preceding reviewed increments remain in history.

Next plan 01-04 performs acceptance traceability, mutation checks and phase verification. No application, SQL deployment, real delivery, browser acceptance or human UAT has been claimed. Runtime question applicability, authority and transaction enforcement remain implementation-phase acceptance obligations.
