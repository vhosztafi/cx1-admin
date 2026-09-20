# 08-13 implementation review

Status: complete. Current backend, SQL/browser, contracts, frontend and source checks accepted; no unresolved HIGH/CRITICAL findings.

## Reviewed behavior

- Trusted stored product chooses commercial-renewal-preparation. Separate missing-only settings use the fictional GBP45 renewal fee; Motor Trade keeps GBP35 and commercial adjustment GBP25. Existing published/adverse assessments are authoritative. No staff/agency authority is silently granted.
- commercial-renewal-subjects-1 binds the saved base/revision and canonical whole-risk hash to exact location, wage, loss and selected liability identities. Experience totals are counted once. Retained positive EL limit does not itself select EL. API input is closed and bounded; SQL has composite ownership, immutable revision/review and cycle source guards. Prior and foreign subjects are rejected; missing experience cannot be approved away.
- commercial-servicing-rating-input-2 carries full-term commercial preparation/experience. Format1 remains commercial adjustment; Motor Trade formats and null-omitted canonical experience bytes are preserved. Structural subject comparison survives JSON roundtrip.
- Renewal uses the known expiring term-end winner, including scheduled adjustments. A separate prospective term ID is assessed under the common exposure fence and used unchanged by the atomic issue writer. Old cover/exposure remain current until inception; the new term starts there. Exact replay creates no second term/version/exposure/journal. Late issue, overlap, stale terms, stale lease and accepted lapse are refused. Expired/revoked issuing authority denies receipt replay before disclosure.
- UI connects preparation, whole-risk experience, evidence, carrier/internal decisions, invitation, acceptance and issue. The selected-policy subheading now says Renewal policy rather than New-business policy; this is display copy only. Receipt, transaction and routing logic did not change. Final frontend checks/build follow the running browser, whose receipt screenshots are unaffected by this subheading.

## Corrected findings

1. Five policy-read call sites inherited write:true and produced unnecessary update locks. Native DMV sampling identified LCK_M_U chains up to25.7s. Evidence requirements, referrals, current authority and both condition-satisfaction reads now explicitly use the existing write:false mode. Transaction-held source consistency/current access remain; mutation fences are unchanged. Actual SQL command-interceptor regression: RED24 reader UPDLOCK statements, GREEN0; sequential/concurrent source assertions remain current. Four retained Motor Trade read scenarios pass.
2. Initial migration Down could remove retained commercial experience metadata. A real SQL downgrade reproduced it. Down now refuses with51953 before schema changes once commercial renewal drafts or lapse history exist. Exact saved manifest/rating/lapse identities survive. An unused installation can downgrade/reapply while retaining published settings/template/evidence identities.
3. Rating fixture callback now uses its existing stopAfterAccepted option; unrelated retained quote assertions were not weakened. OpenAPI references the registered ServicingCaptureCommercialRenewalSubjects component. The browser uses the actual Number of claims accessible label.
4. The browser command harness initially knew only Retry same proof action. Browser6 reached issue201 but lost acknowledgement and correctly displayed Retry same issue. The harness now uses that existing control for /issue, only after the backend returned the expected status, with one retry of the retained UI command. A new key/body would be rejected by the closed-draft/idempotency boundary. No production timeout or business assertion was changed.

## Current accepted evidence

Strict `.local/phase8-13-verified` proves1077 distinct passing backend cases including15 real SQL, zero skips:

| Retained report | Cases | Evidence |
|---|---:|---|
|browser-sql.trx|1 SQL/browser|Complete commercial capture, first issue, renewal invitation/acceptance/issue and exact retained-command recovery|
|unit.trx|1059|Full current application/domain unit suite|
|configuration-sql.trx|1 SQL|Missing-only settings plus unused migration downgrade/reapply|
|downgrade-sql.trx|1 SQL|Retained experience/rating prevents destructive downgrade|
|experience-temporal-sql.trx|3 SQL|Prior revision, unresolved missing experience, final scheduled expiring risk|
|issue-retry-sql.trx|2 SQL|Commercial early issue/overlap/retry/expired and revoked authority; retained MT experience permission|
|lapse-sql.trx|1 SQL|Lapse/replay, no extra cover/exposure, lapse-only downgrade refusal|
|selectors.trx|3|Cancelled-term, future-base and overlap/adjacency selection|
|rating-sql.trx|1 SQL|Owned experience and separate full-term rating|
|concurrent-reads-sql.trx|1 SQL|Current sequential/concurrent reads with no reader update locks|
|retained-reads-sql.trx|4 SQL|Both MT products, evidence-service and referral-authority|

Latest original reports: `.local/phase8-13-active-final/sql.trx`, `.local/phase8-13-downgrade-fixed/sql.trx`, `.local/phase8-13-migration-unused/sql.trx`, `.local/phase8-13-lapse-final/sql.trx`, `.local/phase8-13-selectors-final/selectors.trx`, `.local/phase8-13-read-fixed/sql.trx`, `.local/phase8-13-retained-reads/sql.trx`. Other original paths are retained in08-13-DECISIONS.md. Strict reports do not duplicate test IDs. Supplementary MT full-term rating/missing-experience checks pass in the overlapping issue-negative-retained report and are not double-counted.

Root389 pass in `.local/phase8-13-root-issue-retry.log`; web172 and contracts71 pass in their recorded reports. Existing OpenAPI warning set is retained. Integration build `.local/phase8-13-authority-final-bin` has zero warnings/errors. `git diff --check` passes. Final lint/type checks after the heading correction pass in .local/phase8-13-web-lint-close.log and .local/phase8-13-web-typecheck-close.log. Final production build passes in .local/phase8-13-web-build-final.log using isolated .local/next-phase8-13-final.

## Browser acceptance and limits

Browser7 runs in `.local/phase8-13-browser-7` from authority-final-bin and the updated harness, using isolated `.local/next-phase8-13`. Browser6's disposable artifact folder is `CoverMGA_Test_896570c8a1cf450397c42f0032c74174`; its issue201 is diagnostic evidence, not a completed acceptance result. Browser5 exhausted the adjustment-derived25-minute whole-process allowance during carrier processing. The renewal-only allowance is40 minutes for this complete capture/new-business/renewal journey. Individual request limits and production defaults are unchanged.

Browser command responseElapsedMs includes Playwright waiting for a button to become actionable; it is command acknowledgement time, not isolated server/SQL duration. Use native wait metadata and the separate read regression for SQL contention claims. Failed/incomplete attempts remain excluded. Exact chronology and sanitized artifact locations are in08-13-DECISIONS.md; raw Playwright request-failure logs can contain ephemeral disposable-test cookies and must not be printed.

CC-04 remains compound through08-14. That plan explicitly owns actual issued-commercial-cancellation-to-renewal refusal;13 verifies the shared cancelled-term selector. Phase8 cannot close that requirement before14's complete evidence. CC-05 retains Phase9 operational ownership. Sales-funnel files, live demo processes/data and persistent keys remain untouched. Human/assistive UAT, hostedCI and Docker execution are not claimed.

Accepted test-strengthening: the shared browser fixture binds its original commercial policy before that policy's inception. The native early-renewal scenario now explicitly advances its test clock to one day after the original term starts and asserts active cover/current original exposure before and after early renewal issue, in addition to the exact next-inception boundary. This changes test setup only. The expanded native issue/retry cases pass in .local/phase8-13-active-final/sql.trx; this replaces the prior report without duplicate accounting.

Browser7 completed successfully: .local/phase8-13-browser-7/sql.trx (1 passing SQL/browser case, zero skips). Desktop1480 and mobile390 renewal receipts were visually inspected in .local/browser-evidence/commercial-capture/CoverMGA_Test_a3c166ca7d4e4e24b25debd467c30c68. The exact retained issue retry returned its single original201 receipt; final API and SQL assertions passed. The existing historical-acceptance label above the closed issued receipt remains the same low-priority08-16 presentation follow-up recorded for08-12.
