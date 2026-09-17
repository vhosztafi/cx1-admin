---
phase: 06-underwriting-and-first-policy-issue
plan: '10'
status: complete
completed: 2026-09-17
implementation_commit: af10de7
requirements_completed: []
---

# 06-10 — Policy storage and balanced first-issue posting

Implemented in `af10de7`. Policy/source-quote identity, same-owner terms/current
versions, immutable transactions and JSON snapshots, registrations, original
financial components, sealed journals and three durable document requests now
persist in SQL. Quote binding requires its own current issued version, complete
balanced posting and document requests. Storage does not yet expose issue APIs.

## Rules and storage

- Exact decimal posting covers net remittance, separate remuneration and direct
  MGA collection. Unit fixtures verify 1379 gross, 1259/1252 net, 127 separate
  broker payable, rounding, zero amounts and invalid/overflow rejection. No cash
  receipt or paid balance is generated.
- Added explicit immutable IssueFinancialComponent identities. Journal lines
  reference original amounts and coverage through composite foreign keys. SQL
  checks exact source rating, retained agency terms, accounts, counterparties,
  component pairs and balance before sealing. Posted rows cannot be changed.
- Policy JSON hashes are verified over UTF-8 bytes. Transaction/source ownership,
  effective dates, registrations and current pointers are constrained. Document
  payload/outbox bytes and published product templates must agree. Generation
  remains Phase9; request status is truthfully requested.
- Quotation template choices retain their quote-terms filter. Old-schema upgrade
  fixtures use explicit original quote columns rather than asking the current EF
  model to select a column absent before migration.

## Verification

- Seven new unit cases and five targeted real-SQL cases pass. Initial failing
  storage test demonstrated premature binding before guards were wired in;
  `.local/phase6-10-red-storage` retains that evidence. Final targeted results:
  `.local/phase6-10-storage-1`; pure rules `.local/phase6-10-rules.log`.
- Fresh full suite `.local/phase6-10-backend-full`: **811 passing tests**,
  **635 unit / 176 integration**, **149 real-SQL scenarios**, **zero skips**.
  Integration duration 22m41s. `assert-test-results.ps1` passed with measured
  minimums811/149. No pending EF model changes.
- Generated and inspected forward SQL, composite FKs, indexes, nullable quote
  column and trigger ordering. Migration
  `20260917030154_FirstPolicyIssueStorage` applied to existing CoverMGA_Demo.
  Isolated fresh and retained upgrade tests both pass.
- Initialization and repeat initialization preserved all **31** captured count/
  hash sets, including credentials, quote revisions, evidence, rating/capacity
  history, exact terms, deliveries and acceptance. Private evidence:
  `.local/phase6-10-preservation-{before,after,repeat}.txt`, corresponding SQL,
  migration-forward SQL and initialize/repeat logs. No reset or password change.
- Tests reuse the existing UnderwritingRuntimeTests/UnderwritingStorageTests
  partial fixtures; filters use the actual RealSqlPolicyStorage,
  RealSqlIssuePosting and RealSqlPolicyDocument method names instead of filenames.
- Diff check passed. Sales funnel untouched. No real provider/email/payment,
  deployment, Docker, hostedCI or human UAT claimed.

## Continuation

06-11 atomic issue and protected reads is in progress. Its new tests were first
built to failing missing-service errors in separate output, preserving the
running06-10 test binaries. No whole UWR requirement is marked complete;06-14
retains final source reconciliation and conditional capacity supplement gates.
