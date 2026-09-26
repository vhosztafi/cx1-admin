---
phase: 11-configuration-and-account-administration
plan: '02'
status: complete
requirements: [ADM-02]
---

# 11-02 — Authority and referral administration

Added versioned authority proposals with readable financial/risk limits, product version/binder scope, dates, referral team, explicit staff grants and revocation. A different current administrator must approve publication. Both requester and approver permissions, source ETags, runtime base, binder/rating windows and grant overlaps are checked inside the serializable command. Published authority/grants and runtime settings use existing consumer parsers; approvals append successors and leave old definitions/evidence unchanged. The approval editor, request history, grant controls and API/generated contracts are wired.

Focused native SQL passed (`phase11-authority-corrected.trx`): excessive limits, changed-key intent, self approval, independent approval, actual new rating eligibility and grant consumption, unchanged old definition, overlapping grants, stale approval, explicit revocation and revoked-role replay. The grant timestamp initially used the wall clock instead of the injected clock; corrected and rerun passed. Existing seed/migration/constraint test passed (`phase11-seed.trx`), including repeated additive seed with nine fictional users. TypeScript, ESLint and OpenAPI passed (104 existing OpenAPI warnings).

Browser request → self-approval denial → different administrator approval → reload passed in a disposable SQL fixture (`.local/phase11-tests/authority-browser.json`, screenshot `output/playwright/phase11-authority.png`). Initial browser failures were warm-up and an overly strict wrapped-select label locator; the final observed combobox journey passed. Generated preview output is now ignored by ESLint/git.

The source seed adds `admin-reviewer@cover.example` using the locally supplied demo password, without changing existing credentials. Automatic approval review rejected applying that new privileged identity to the retained demo database; that operation was not performed or bypassed. Browser proof used a separate `CoverMGA_Test_Phase11_*` database and separate keys. Applying the reviewer seed to the retained shared demo remains an access-dependent deployment step; feature correctness is verified in isolation.

No full regression or human UAT claimed. Existing grants require explicit revocation before replacement; approvals do not silently rewrite them.

## Self-Check: PASSED
