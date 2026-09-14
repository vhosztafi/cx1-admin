# 04-06 — Activation, suspension and agreed terms

Status: in progress. No approval endpoint or completed activation workflow is claimed.

## Complete terms validation foundation

Added AgencyTermsRules.ValidateProposal for complete commercial/settlement/account/product proposals. It reuses the reviewed draft value normalizer but rejects partial snapshots, unknown/null/duplicate fields, missing selected-mode values, invalid money/bps, mismatched version/commercial dates, missing/duplicate products and invalid product dates. Explicit zero values remain distinct from unanswered fields. Schedule validation rejects backdating, collisions and insertion before an existing future version.

The result contains a canonical snapshot, ordered read-only product inputs, separate normalized reason and SHA-256 content fingerprint. Reordering JSON or changing only the separately stored reason does not change the financial content fingerprint. Product rows cannot begin before their version and at least one begins on the version date; this choice prevents a full replacement snapshot starting with an unintended distribution gap. See04-DATA-API-DESIGN. This rule module is not yet called by a public endpoint: authorization, SQL locking, eligibility, evidence/base fingerprint and independent approval must be added in the owning service.

Verification:21 new meaningful terms tests, all219 unit tests passed. Full260 backend cases including34 real-SQL scenarios passed without skips in .local/phase4-terms-rules-full (219 unit/41 integration); TRX gate passed. All77 Node contract tests and OpenAPI lint passed. No UI changed; previous22 frontend/build/browser evidence remains current. CI minimum totals updated to260/34 Windows and258/32 Linux; hosted CI not run. No tests or previews remain active.

## State proposal storage

Added AgencyStateRequest, its EF mapping and migration20260914173625_AgencyStateProposals. The record owns its decision ETag while retaining the unchanged agency base version, fingerprint, requested kind/state, actor and reason. SQL allows one pending agency/kind request, requires independent current internal administration for applied/rejected decisions, requires complete decision provenance, and retains stale history without inventing a human reviewer. Triggers reject direct terminal insertion, immutable proposal changes including case-only reason edits, terminal rewrites/reopening and deletion. CreatedBy must match RequestedBy.

The real-SQL storage test passes independent rereads, unchanged parent rowversion, duplicate pending denial, fingerprint/reason/state tampering, self-decision and wrong-role denial, valid independent rejection, terminal immutability, release of the pending slot, stale closure, direct terminal insertion denial and unauthorized requester denial. These are storage invariants, not a claim that approving applies business effects; no approval API is enabled yet.

Full261 backend tests /35 real-SQL scenarios passed without skips in .local/phase4-state-proposals-full (219 unit/42 integration), verified by the TRX gate. Targeted storage test passed in .local/phase4-approval-storage. The new additive migration was applied to CoverMGA_Demo through --initialize-demo, preserving existing fictional data. No frontend or public contract changed; previous22 frontend/build/browser and77 contract evidence remains current. CI minima261/35 Windows and259/33 Linux; hosted CI unperformed. No test/preview remains active.

Migration tooling note: use the Infrastructure project as both project/startup with its design-time factory. Build --no-restore first, then ef migrations add --no-build. No new packages were installed.

## Terms and product storage

Added AgencyTermsRequest, AgencyTermsVersion and AgencyProduct mappings and additive migrations. Request decisions retain the same independent reviewer and immutable history safeguards. Scoped composite approval FKs prevent cross-agency publication; the initial version requires an applied activation, subsequent versions an applied terms request whose snapshot/date/reviewer match exactly. Versions append sequentially with strictly later effective dates. Product grants are materialized from the immutable snapshot by SQL in the same statement; invalid references, duplicate products, empty sets or invalid rates roll back publication. Grants preserve exact version, commission, dates and reviewer provenance and cannot be edited/deleted. Agency scope derives from the immutable terms parent rather than a duplicated agency column; this refinement and trigger-owned writes are recorded in the design contract.

Targeted SQL test passed in .local/phase4-product-storage. It verifies initial approval prerequisite, self-decision denial, cross-agency rejection, snapshot/date tampering, version-gap denial, current versus scheduled selection, independent rereads, immutable decisions/terms/products and complete rollback of malformed product sets. The fixture deliberately uses minimal commercial JSON and does not assert complete business approval. The complete AgencyTermsRules unit coverage remains the business-input foundation.

Full262 backend tests /36 real-SQL scenarios passed without skips in .local/phase4-terms-products-full (219 unit/43 integration); TRX gate passed. All77 contract tests and OpenAPI lint passed; EF reports no pending model changes. Both additive migrations were applied to CoverMGA_Demo through --initialize-demo, preserving fictional data. CI minima262/36 Windows and260/34 Linux; hosted CI unperformed. No UI change: prior22 frontend/build/browser evidence remains current. No tests or previews active. Inline review checked scoped FKs, immutable snapshots, trigger-owned product projection, rollback and migration/model consistency. No04-06 completion or entire AGY requirement is claimed.

## Next implementation

1. State/terms proposal records and immutable terms/product storage are implemented. Continue with business approval services; storage alone does not apply agency state. Follow04-DATA-API-DESIGN state rules: proposal creation does not advance the agency base, live duplicates conflict unless the old base is stale, approving applies atomically.
2. Implement current-role-authorized services under agency-first lock order. Activation rechecks complete evidence/readiness/eligible effective products and publishes initial terms, issues staged invitations and enqueues protected demo notices/follow-up obligations in one transaction. Use a separate initial-draft extraction path; do not force a historical initial declaration through the future-proposal schedule rule.
3. Implement suspend/reactivate with separate reviewers, session/stamp/invitation revocation and the Phase3 relationship/matching lock-order fences. Add additive second-reviewer demo seed.
4. Wire scoped proposal/read/decision APIs and final-stage/Products UI only after storage/services are tested. Preserve strict JSON, CSRF, request ETag, exact replay and current authorization. Broker login stays closed until04-07.
5. Full SQL/race/browser/source verification before04-06-SUMMARY. No entire AGY requirement or human UAT is inferred complete.
