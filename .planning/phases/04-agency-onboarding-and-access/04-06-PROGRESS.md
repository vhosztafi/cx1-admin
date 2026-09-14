# 04-06 — Activation, suspension and agreed terms

Status: in progress. No approval endpoint or completed activation workflow is claimed.

## Complete terms validation foundation

Added AgencyTermsRules.ValidateProposal for complete commercial/settlement/account/product proposals. It reuses the reviewed draft value normalizer but rejects partial snapshots, unknown/null/duplicate fields, missing selected-mode values, invalid money/bps, mismatched version/commercial dates, missing/duplicate products and invalid product dates. Explicit zero values remain distinct from unanswered fields. Schedule validation rejects backdating, collisions and insertion before an existing future version.

The result contains a canonical snapshot, ordered read-only product inputs, separate normalized reason and SHA-256 content fingerprint. Reordering JSON or changing only the separately stored reason does not change the financial content fingerprint. Product rows cannot begin before their version and at least one begins on the version date; this choice prevents a full replacement snapshot starting with an unintended distribution gap. See04-DATA-API-DESIGN. This rule module is not yet called by a public endpoint: authorization, SQL locking, eligibility, evidence/base fingerprint and independent approval must be added in the owning service.

Verification:21 new meaningful terms tests, all219 unit tests passed. Full260 backend cases including34 real-SQL scenarios passed without skips in .local/phase4-terms-rules-full (219 unit/41 integration); TRX gate passed. All77 Node contract tests and OpenAPI lint passed. No UI changed; previous22 frontend/build/browser evidence remains current. CI minimum totals updated to260/34 Windows and258/32 Linux; hosted CI not run. No tests or previews remain active.

## Next implementation

1. Persist state/terms proposal records and immutable terms/product versions with SQL one-time decision/independent-reviewer constraints, base rowversion, fingerprint, reason and actor provenance. Follow04-DATA-API-DESIGN state rules: proposal creation does not advance the agency base, live duplicates conflict unless the old base is stale, approving applies atomically.
2. Implement current-role-authorized services under agency-first lock order. Activation rechecks complete evidence/readiness/eligible effective products and publishes initial terms, issues staged invitations and enqueues protected demo notices/follow-up obligations in one transaction. Use a separate initial-draft extraction path; do not force a historical initial declaration through the future-proposal schedule rule.
3. Implement suspend/reactivate with separate reviewers, session/stamp/invitation revocation and the Phase3 relationship/matching lock-order fences. Add additive second-reviewer demo seed.
4. Wire scoped proposal/read/decision APIs and final-stage/Products UI only after storage/services are tested. Preserve strict JSON, CSRF, request ETag, exact replay and current authorization. Broker login stays closed until04-07.
5. Full SQL/race/browser/source verification before04-06-SUMMARY. No entire AGY requirement or human UAT is inferred complete.
