# 09-08 review

Status: complete. Final acceptance passes; no unresolved HIGH/CRITICAL finding in this slice.

Reviewed shared document confirmation/upload/preview/list, record integrations,
legacy evidence composition, task attachment storage/API/client, generated
contracts and browser fixture ownership. Checks focused on original-parent
authority, exact immutable source/version identity, uncertain retry, stale ETags,
private content and retained history.

## Findings addressed

- Task attachment migration Down could erase retained association/removal history.
  An actual EF downgrade in an isolated SQL fixture reproduced this: RED24943,
  `.local/phase9-08-attachment-downgrade-red`. Added a nonempty-history guard before
  DropTable. The current final gate must prove rejection and unchanged row count.
- Browser fixture agencies were created after the initial onboarding seed. Reused
  the missing-only AgencyDemoSeed in the isolated database. No production bypass.
- Browser fixture relationship timestamps used wall clock after the retained
  workflow clock; cursor as-of filtering correctly hid the row. Aligned only the
  isolated fixture timestamp. All three products' intermediate browser runs
  passed the first21 checks before this failure; current Road Risks completed22.
- Initial policy picker lacked a reliable accessible label; prior execution added
  the explicit label and successful browser evidence. Native dialog focus returns
  to its trigger, uncertain execution retains the command, and Escape cannot
  discard an unconfirmed write.

## Preserved boundaries

New associations demand task-write plus original document-read before receipt
replay and ready state before insertion. Cross-parent links are rejected before
another parent lock, with SQL reinforcement. Removal retains original bytes,
identity and removal provenance. Terminal tasks require reopening. The current
task ETag protects both add and remove; the normal task receipt remounts the editor
and attachment list after confirmed success. Reads/downloads independently
reauthorize original scope. No FileObject path/ID is exposed.

Legacy evidence retains typed download APIs, file IDs and review states. New
uploads do not record acceptance proof or underwriting review. Quote and servicing
documents use an explicitly selected terms/version identity. Client documents
select an actual relationship rather than creating a global client subject.

Frontend186/root417 tests, production build, lint and clean-exit OpenAPI validation
have passed.49 focused backend unit cases passed. Current SQL/browser gate16902
and per-product evidence must be inspected before closing this review. Final
strict evidence must exclude failed attempts and duplicate test identities.

## Later integration work

Existing policy/CC/history links to `/agencies/` need correction to `/agents/`
during the Phase9 navigation integration review. This predates the document
composition change and is tracked in09-09-INTEGRATION-NOTES as well. Delivery,
withdrawal, message history and final full-phase acceptance keep their later plan
owners. Human business and assistive-technology UAT remain unperformed.

## Review continuation,2026-09-22

- Explicit null reason could skip the optional generic task reason check. Two
  service-boundary regression cases reproduced NullReferenceException instead of
  the required TaskRuleException; both now pass after mandatory reason guards on
  attach/remove. Reports: phase9-08-null-reason-red/green.
- Renewal browser correction passed23 checks and SQL readback in
  .local/phase9-08-renewal-corrected. Prepared document access is independent of
  editing leases; the test no longer waits for editing under a frozen clock.
- Desktop document list and390px task attachment screenshot inspected. Exact
  provenance, version, actions and focus are readable. Headless mobile PDF iframe
  is blank; the visible exact-version download fallback and hashed bytes work.
  pdfinfo parsed the generated11-page schedule; its first rendered page was
  inspected with Poppler. This does not claim native PDF viewer rendering or
  human assistive UAT.
- Strict staging currently verifies57 unique passes/6realSQL/no skips. The older
  options-only report was excluded because metadata-green already includes that
  same case. Raw reports remain unchanged. Final changed-source SQL/browser
  session82627 still needs to complete before adding its report and closing review.

Final gate:63 unique cases/12realSQL/no skips in .local/phase9-08-final-strict; browser validator passes22/22/22/23 checks with current hashes and SQL readback. Session82627 exited0. See09-08-SUMMARY for authoritative final evidence.
