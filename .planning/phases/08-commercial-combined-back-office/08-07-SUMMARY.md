---
phase: 08-commercial-combined-back-office
plan: '07'
status: complete
subsystem: commercial-underwriting
requires: [08-06]
provides: [cc-carrier-permissions, cc-terms, cc-demo-delivery, cc-acceptance]
affects: [08-08, 08-09, 08-12, 08-13, 08-15]
requirements-completed: []
completed: 2026-09-20
---

# 08-07 — Commercial Combined carrier permissions, terms and acceptance

Implementation commit: `fad26a6`.

Commercial Combined now progresses through exact-subject carrier permissions, reviewed proof and internal decisions into prepared, signed, delivered and accepted quotation terms. The actual browser journey passed all109 captured questions, two carrier requests, proof/decision boundaries and accepted current terms. Policy issue remains unavailable pending dated exposure and atomic issue in08-08/09.

## Delivered

- CommercialCapacityExtension(Dimension, MaximumAmount, RiskItemId?), CommercialCapacityContext and CommercialCapacityDecision form a separate typed CC extent model. CommercialCapacityRules.Extension/Applies validate exact quote/cycle/submission/hash, location, dimension, positive money and complete dated applicability. CapacityAuthority matches every current actor/binder blocker to its own current active-provider response and proof. No district-book, outside-appetite or missing internal review permission can be overridden.
- Existing CapacityService/Responses/Worker/ReadModel/Jobs dispatch explicitly for CC. Current product-specific scenario selection, provider, revision/runtime pins, proof, child versions and durable outbox/inbox boundaries are retained. New missing-only fictional scenarios are cc-approve-requested, cc-conditional-proof, cc-query-proof, cc-decline and cc-transient-then-approve. Supplied response proof is separately screened/reviewed and withdrawal removes authority. Conditional carrier records do not become internal decisions.
- QuoteTermsSeed adds missing-only demo-commercial-combined-terms. Existing QuoteTermsService and QuoteAcceptanceService work through the typed held CC context; their immutable quote-contract-1, current-signature/recipient/delivery/assurance checks are reused. QuoteTermsReadModel renders exact selected property/location, BI, liabilities, goods in transit, money, glass, contract works and BI extension cover. Uncaptured excess/limit values are not fabricated. The common closed signed-statement condition binds exact terms ID/hash. A120-row bound accommodates100 permitted locations plus selected sections/extensions.
- QuoteCapacityExtension is a closed quote-only API union. The original UnderwritingCapacityExtension remains MT-only for servicing. Capacity readback includes optional riskItemId and the UI displays its saved location. Commercial receipt adds Quotation and routes the underwriting Send quote action to it. Shared quotation refresh reloads assessment/history/evidence after commands. Actual terms preparation, delivery and acceptance use the existing confirmation/ETag UI.
- Commercial submission routing and assignment readback are enabled with current typed configuration. New code does not construct synthetic MT risk. CommercialUnderwritingSeed already supplied the configuration, so its existing implementation and DemoDatabase initialization order were reused. No migration, live reseed or production authority grant was needed. Test contacts/grants exist only in isolated SQL fixtures.
- Source evidence extends AU-05/AU-06 and the six source-document controls; original owners and denominators166/109/60/298/7 are preserved. CC-03/04 remain open for subsequent exposure/issue work; CC-05 stays partial through Phase9.

## Acceptance evidence

- Strict44 unique backend successes including4 real SQL, zero failures/skips: .local/phase8-07-final/{unit,commercial-sql,retained-api,browser}.trx; scripts/assert-test-results.ps1 passed.
-40 focused units: .local/phase8-07-final-unit/unit.trx, including15 CommercialCapacity cases plus retained capacity/condition/terms rules.
- Complete CC SQL scenario: .local/phase8-07-final-sql/sql.trx. Covers conditional carrier response without automatic internal approval, independent proof/condition resolution, internal decision, signed/prepared/delivered/accepted terms, actual retained records, exact create/send/response/acceptance retries, stale creation/preparation, wrong submission hash/subject/district extension, expired approval, withdrawn supplied proof, changed recipient/assurance and revoked grant before exact response replay. Earlier explicit denial-code assertion was corrected to accept either valid authority gate; final run passed.
-2 retained MT API scenarios: .local/phase8-07-retained-api/sql.trx. CSRF, strict fields, exact versions, scoped structured terms history and current role before replay pass. Both retained MT exact-term lifecycle cases also passed in .local/phase8-07-expanded-sql/sql.trx; that earlier mixed run is not included in strict final accounting because its CC assertion failed before correction.
- Actual Chrome/SQL browser: .local/phase8-07-browser-d658f738-95ce-4ff5-b0d8-6d6a82443353/sql.trx,4m11s. Final report and actual API/screenshot evidence: .local/browser-evidence/commercial-capture/CoverMGA_Test_7bcede49e6fa411198bce3bc89c69024/report.json. Full capture109 -> two exact-location carrier UI submissions -> conditional responses -> scoped API proof/review and independent internal decision -> UI prepared/delivered/accepted terms. Exact current response schemas, desktop and390px no-overflow readback, location labels, source SQL readback and retained MT creation pass. Accepted mobile viewport was visually inspected. Earlier passing journey b906683d is superseded by this current-label run.
-162 frontend tests (.local/phase8-07-final-web.log);382 root tests (.local/phase8-07-root-tests.log),47 final changed-contract cases (.local/phase8-07-final-contracts.log) and4 final source-ledger tests (.local/phase8-07-source-tests.log).
- Current build/typecheck, lint and422-operation OpenAPI validation pass with57 existing warnings: .local/phase8-07-final-build.log, .local/phase8-07-typecheck2.log, .local/phase8-07-final-lint.log, .local/phase8-07-final-openapi.log. Final backend build has zero warnings/errors. A prior overlapping test rebuild encountered a Windows assembly lock; the owned browser finished successfully and the rebuild then passed. git diff --check passed.

## Review and next ownership

08-07-REVIEW.md passed; no unresolved HIGH/CRITICAL finding. No live demo database/key/service reset, frontend-code changes, external provider/email, human business/assistive UAT, hosted CI or Docker run. Phase-wide full regression/restart/preservation remains08-16.

Continue08-08: dated commercial exposure books/limits, immutable complete headers/rows and bitemporal footprint evaluation.08-09 owns atomic exposure/issue writes, documents and the issue capability. Accepted quotation terms alone cannot issue a CC policy. Servicing extensions remain08-11/12/13 and missing-only business demo data08-15.

## Self-Check: PASSED

Implementation and passing current-source artifacts exist; strict counts and actual browser/SQL readback verified. Final accounting contains only executed successful cases.
