# 08-10 implementation review

Reviewed the actual implementation, contracts, scoped API responses, source inventory and persisted browser evidence against T08-10. No unresolved HIGH/CRITICAL finding.

- Shared observation fence is acquired before all retained parent scope locks and uses the same resource/order as exclusive issue/publication. A read cannot observe partial projection children. Current identities/roles and agency ownership are checked inside the transaction.
- Capability-shaped allowlists are constructed directly; agency responses never receive book totals/counts/limits/pins or other policies' IDs. Actual accepted broker cookies prove own quote/policy/draft routing, all three foreign-subject denials and relationship revocation. Query filters cannot change capacity.
- Policy observation uses exact E/K selected version/hash. Bound quotes reuse the same observation without double counting; the whole book includes another agency's policy. Scheduled/expired own contribution is zero; pure rules cover known cancellation and missing/ambiguous limits. Unbound quote preview assesses the full proposed term. Draft projection is explicitly unavailable until08-11/12 rather than showing a misleading issued base.
- All nine commercial tabs render immutable values, exact monetary calculations and actual transaction/document/history data. History accepts an explicit snapshot union. No Motor Trade driver/vehicle/MID tab, sample balance or false operational success is exposed. Source reconciliation preserves all denominators and distinguishes implemented views from uncaptured/later-phase fields.
- Earlier diagnostics found two test-fixture errors (invalid user state and JsonElement reference equality), corrected before the final SQL run. The first browser run covered all tabs but hit Playwright datetime input normalization; minute-format input corrected the final full browser run. These earlier reports are excluded from strict accounting.

## Current-source evidence

- `.local/phase8-10-final`: strict30 unique passing cases,7 real SQL, zero skips.23 exposure units;3 commercial read/HTTP cases;3 retained Motor Trade temporal/history cases;1 actual full browser case.
- `.local/phase8-10-read-sql-v3/sql.trx`: exact E/K source/hash and whole-book totals, scheduled/expired contribution, bound-quote parity, agency privacy/current revocation, actual broker invitation/cookies, real foreign quote/policy/draft, owned unavailable draft, and rejected filters.
- `.local/phase8-10-retained-mt-sql/sql.trx`: retained known issued bytes/foreign-term rejection and both Motor Trade product history authorization cases.
- `.local/phase8-10-browser-92b76064-0850-4ad7-931c-4682eff9218c/sql.trx`:4m47s complete commercial UI capture→underwriting/carrier→terms→issue→all policy tabs and historical dates. Artifacts `.local/browser-evidence/commercial-capture/CoverMGA_Test_10e796995f244a168647d8317efa2457/`; nine tabs at desktop/390px, exact location/wage/BI readback, validated exposure DTO and source hash, scheduled/active/expired/no-cover, history selection, source quote link and retained Motor Trade creation/editor route. Screenshots inspected; tables scroll locally and page does not overflow.
-387 root tests `.local/phase8-10-root.log`;166 web tests `.local/phase8-10-web-tests.log`;4 source-ledger tests `.local/phase8-10-source-ledger.log`.71 contract checks plus422-operation OpenAPI lint with56 existing warnings `.local/phase8-10-contract-validation.log`.
- Final typecheck/lint `.local/phase8-10-{typecheck,lint}-final.log`; production build `.local/phase8-10-web-build-final.log`. Backend SQL run build: zero warnings/errors. No migration or model change. `git diff --check` clean.

## Remaining ownership

08-11/12 must replace the explicit unavailable draft projection with actual commercial proposed slices and issue them atomically.08-14 must prove an issued commercial cancellation in the browser; this slice covers cancellation labels/rules without inventing such a transaction. Phase9 owns generation/delivery/incidents; Phase10 owns cash balances; Phase11 owns configuration administration. No human/assistive UAT or hosted CI is claimed. Live demo processes/database/keys and sales-funnel files were preserved.
