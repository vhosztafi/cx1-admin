---
phase: 11-configuration-and-account-administration
plan: '03'
status: complete
requirements: [ADM-03]
---

# 11-03 — Versioned configuration

Delivered field-based workflow/assignment/checklist, matching, support flag, message/document template and organisation editors. Published successors use current-authority checks, stable command receipts, ETags and audited immutable versions. Future workflow tasks consume the configured team; matching uses its existing versioned consumer; flag declarations enforce enabled/sharing/review limits; new client references use the configured prefix without resetting the sequence; new message deliveries obey notification enablement. Message insertion resolves organisation name and signature before the draft is saved. Existing outputs retain their recorded content and versions.

Production PDF preview uses bundled fictional proposals and the actual renderer, with no customer file or delivery. Initial focused checks found a missing fixture seed and an incomplete preview proposal; both were fixed. Native SQL `RealSqlConfigurationSuccessorsValidateConsumeAndPreservePriorVersions` passed: 1 case, zero skips. Covers save/replay/stale denial, live reference/flag consumption, future workflow selection, prior version preservation, PDF rendering, unsafe markup and revoked-role replay. Browser saved settings/reload and PDF download passed in the isolated fixture (`configuration-browser.json`). Screenshot inspected. Typecheck, lint, API build and OpenAPI validation passed; existing warnings remain.

Commit: `52ae1e3`. No full regression or human UAT claimed. Retained demo was not modified. Evidence: `.local/phase11-tests/phase11-configuration.trx`, `openapi-configuration.log`, `configuration-browser.json`; `output/playwright/phase11-template-preview.pdf`.

## Self-Check: PASSED
