# 07-08 progress — terms delivery and exact acceptance (incomplete)

2026-09-18. Continuing the active execute-phase7 --auto turn inline, no agents.
07-07 closed in5a9cdc7 after production91ed93e. Current counts7/16Phase7,
56/65 implementation plans; POL-04/POL-07 remain open. Do not end the turn at
an implementation checkpoint; continue through the approved remaining plans.

## Design refinement

- Servicing terms are a new owned aggregate, not quote terms with fake QuoteId.
  Immutable terms bind draft/revision/cycle/base/rating/template, all dated risk
  slices, resolved conditions/endorsements, exact retained servicing rating price
  and configuration. Plan09 subsequently posts signed financial components;
  it does not change the accepted contract or recalculate a browser price.
- Cycle current terms/delivery/acceptance pointers use compound owned FKs.
  New terms invalidate current delivery/acceptance while retaining immutable
  histories. A late worker result can be recorded but cannot reactivate old terms.
- Signed-statement and acceptance-proof purposes must bind the exact terms ID,
  hash and complete date set, separately from capacity submission proof. Add a
  nullable terms owner to evidence associations with an iff-purpose constraint;
  derive it from server requirements rather than trusting a client owner ID.
- Terms payload identity and assurance identity are distinct: contractual changes
  require new terms; current proof, decisions, carrier extent and grant validity
  are re-evaluated at prepare/send/accept/issue. Signature and acceptance evidence
  cannot be a cyclic input to the terms hash they must prove. Acceptance records
  the exact current assurance fingerprint and delivery/evidence review identities.
- Delivery recipients are current contacts in the source relationship. Persist
  their snapshot, recheck before queue/application/acceptance, and use a dedicated
  servicing delivery work kind/operation key. Only applied delivered outcome
  permits separately recorded acceptance; no real message provider.
- Every command keeps parent ETag, live editing lease, current authorization
  before replay, CSRF, bounded parsing and receipt/readback semantics. Separate
  no-store bounded histories retain superseded terms, deliveries and acceptance.

## Initial predicate tests

ServicingTermsTests initially fails compilation because ServicingTermsSubject is
missing (.local/phase7-08-domain-red.log); no executable red pass is claimed.
Tests cover every owner/terms change, late/duplicate delivery, wrong delivery,
current assurance, delivery state, UTC ordering, expiry and accepter/channel.
ServicingTermsRules is now implemented as a pure predicate using the established
QuoteTermsRules acceptance window and identity rules. The initial unit run passed
24 cases (including existing QuoteTermsRules), zero skips.

## Immutable storage and terms proof prerequisites

Added ServicingTermsVersion and additive migration20260918094550. Compound
ownership, exact UTF-8 payload hash, current draft/rating/template, exact dated
schedule and six retained price components are checked. Terms are append-only;
the cycle cannot move its current terms pointer backwards. SQL source guards do
not replace the forthcoming service's authority/proof and full payload checks.
Both product tests first failed at the missing table, then passed with the new
migration, including empty-table rollback/reapply, rejected malformed owners,
wrong payload dates/price/hash, expiry, mutation and pointer rollback.

Fresh evidence: .local/phase7-08-terms-storage-green/{unit,sql}, 24 unit+2SQL,
zero skips; assert-test-results passed with cutoff2026-09-18T09:45:00Z.
Terms-specific proof rules bind exact owner/base/rating/terms hash, purpose and
ordered dates. Missing helper/owner fields produced a compile red; subsequent
targeted unit run passed35 cases in .local/phase7-08-terms-proof-green/unit.
Optional terms ownership is omitted from existing JSON when null, preserving
the currently exposed proof contracts until their explicit API extension.

Added migration20260918095455 for compound terms evidence ownership, iff-purpose
constraints, current terms at attachment and immutable terms provenance. Attach
and review commands derive/check the owner from server purposes; purposes are
not yet exposed until terms preparation and contract verification are wired.
Both-product SQL tests initially failed with missing TermsVersionId (207), then
passed. Fresh .local/phase7-08-terms-proof-storage-green/{unit,sql}:35unit+2SQL,
no skips. Infrastructure and API compiled as part of this integration run.

Preparation service now freezes all complete dated proposals, retained rating
input/result, configuration, commercial terms, non-documentary conditions and
six exact price components. Current proof/decision authority is checked before
receipt replay. Both products passed preparation, persisted contract readback,
reuse and replay rejection after proof withdrawal. A separate executable red
confirmed missing contract-specific purposes; the green run now exposes signed
statement and acceptance proof only for a payload-current contract.

Fresh .local/phase7-08-terms-purposes-green/{unit,sql}:35unit+2SQL, zero skips.
.local/phase7-08-proof-contracts-web.log:50API/frontend tests passed, including
failing-first terms-owner UI proof matching. Web typecheck and OpenAPI lint
passed (400operations,34 existing warnings). Proof DTO/history/schema include
optional termsVersionId; no public terms command endpoint is active yet.
SQL reviewed price guard also rejects fractional pennies rather than rounding
them; both-product storage regression passed in
.local/phase7-08-terms-storage-reviewed/sql.

Still incomplete: delivery, acceptance, terms endpoints/contracts/read model,
typed signed-statement condition integration and browser UI verification.
The shared demo database has not been migrated for07-08. No completion or
requirement closure is claimed; continue implementation.
