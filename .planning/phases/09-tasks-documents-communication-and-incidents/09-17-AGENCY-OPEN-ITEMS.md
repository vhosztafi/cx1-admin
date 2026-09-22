# Agency-facing open items — source reconciliation gap

Status: implemented and bounded SQL/browser/retained-upgrade verification passed; full Phase9 acceptance pending.

The original `pPortal` at decoded prototype line4468 is an internal data-sharing reference with Their policies, Their open items and Hidden from agency users. Its open rows include an adjustment awaiting agency information and a quotation awaiting client acceptance. `docs/design/AGENCY-SOURCE-COVERAGE.md` explicitly hands open task/message items to Phase9. Current `AgencySharing` still displays “Shared tasks are not available yet”, and `AgencySharingService.Context` lists tasks as unavailable in owning phase9. This is an unresolved Phase9 source gap; do not mark plan17/phase9 complete.

## Required boundary

Use the existing agency-scoped projection service and internal sharing-reference page. Current staff preview and broker projection must share one explicit allowlist and enforce current active agency/relationship identity before paging or counts. Preserve hidden internal notes, task comments/titles, support flags, referral rules, authority limits and foreign-agency data. Never make the internal task list generally agency-visible.

Open items need a real public source and a real completion rule. Inferring an information request from any unfinished internal task, or treating every delivered message as awaiting a reply, is unsafe and inaccurate. A queued or failed message does not establish that the agency received a request. Sending a reply request does not resolve or approve an underwriting referral.

## Proposed implementation to refine against existing services

1. Add an explicit response-tracking record for an actual delivered agency message version. Staff deliberately track a requested response; its publicly shared instruction comes from that immutable delivered message, never from an internal task/comment. Preserve original subject, relationship and message-version identity, state, actor/timestamps and reasoned response-received/closure history. Use existing current-scope/idempotency/ETag machinery and immutable SQL guards. One tracked request per exact message version prevents retry/resend duplicates.
2. Provide tracked-request controls/list in the actual record correspondence workspace. Confirming receipt of a response closes only this correspondence obligation and does not apply an insurance decision. Failed or unsent drafts cannot become public open items.
3. Project pending quotation/servicing acceptance from exact currently applicable delivered terms without recorded acceptance; pin the current cycle/version and exclude superseded, accepted, issued or expired sources. Do not expose pricing factors or internal blockers through this projection.
4. Add a paged/searchable Their open items view using public reference, item kind, agency-facing requested action and status. Count/search/cursor fingerprints use only those visible values. Remove the Phase9 unavailable placeholder once this is implemented; retain the genuine Phase10 statements/download boundary.

Implementation follows the inspected existing scope/command boundaries. AgencyResponseRequest pins delivered MessageVersionId/SubjectId/RelationshipId and exact public snapshot, with internal reasons and terminal response-received/withdrawn state. SQL guards retain provenance and history; downgrade refuses retained requests. POST messages/{id}/agency-response, GET same and POST agency-responses/{id}/resolve use current scope before replay. Shared projections expose only explicit public fields. Current quotation sent state (capture lock is not withdrawal), current unaccepted servicing terms and expiry govern acceptance items. Verification must cover both products, source changes/closure, foreign agency, ended relationship, revoked identity, stale cursor/ETag, duplicate keys, private-content non-disclosure and real browser reloads.

The full v3 run and waiting continuation were stopped before any source changes, preserving `.local/phase9-final-v3-interrupted.json`; no acceptance stages ran in the continuation. This prevents a misleading full-current pass while the known handoff remains missing. A fresh full inventory/build will be required after the implementation.
