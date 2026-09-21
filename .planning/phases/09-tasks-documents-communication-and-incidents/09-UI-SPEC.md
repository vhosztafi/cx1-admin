---
phase: 09
slug: tasks-documents-communication-and-incidents
status: approved
shadcn_initialized: false
preset: none
created: 2026-09-21
---

# Phase 9 UI design contract

Inline autonomous GSD UI research/check, based on09-CONTEXT,09-RESEARCH, decoded prototype pTasks/pTask/pLogClaim/pClaim and operational tabs, existing globals.css and Phase8 UI contract. Approval here is a design review; browser/visual implementation evidence remains required.

## Design system, spacing and typography

Reuse Next.js, TypeScript, Tailwind, local IBM Plex Sans, Panel, DataTable, Status, SectionTabs, record header, dialog/field/command patterns. No new UI library, registry, Catalyst or remote imagery. Existing business workspace is distinct from the infrastructure job console. Preserve238px sidebar,62px topbar and314px action rail; rail stacks below content at1100px. Main focal point is the current record/title and its next business action.

Spacing scale4/8/12/16/24/32px. Source-preserving exceptions:6px field-label gap,10px action gap and panel radius,18px panel/stack gap,20px rail gap,22px page inset,7px input/button radius. Do not restyle shared components globally.

| Role | Size | Weight | Line height |
|---|---|---|---|
| Body/table |13px |400 |1.5 |
| Label/help |12px |600/400 |1.5 |
| Panel heading/input |14px |600/400 |1.5 |
| Record title |23px |600 |1.25 |

Source KPI27px and status11.5px remain existing shell exceptions. Human references use tabular numbers; money right-aligns. Avoid showing UUIDs/hashes outside expandable provenance.

## Color

Canvas #f6f7f9; white panels/sidebar; text #14161c; muted #6b7184; borders #e4e6ec. Accent #3f5ef5 only for primary action, selected tabs/navigation and record links. Neutral secondary controls. Success #0f6b45/#e8f6ee, warning #8a5300/#fff4e0, error/destructive #b3241f/#fdecec, muted #5b6172/#f1f2f6. Every state has text; red is not used for a normal awaiting-information state. Preserve3px visible focus outline.

## Screens and interactions

| Surface | Contract |
|---|---|
| Tasks list | Source My open/Team queue/Created by me/Completed tabs; actual scoped Open/Due today/Overdue/Awaiting others/Completed7days measures; search/priority/type/due filters; selection checkbox labels include reference. Columns priority, reference, title, type, linked record, owner, due, status. Selection count and Reassign/Change due date/Complete actions appear together; no bulk referral decision. Clear/select applies visible result set and communicates its extent. |
| Task detail | Record header with actual reference, source rule, owner, due, priority and linked parent; Task/Linked records tabs; requested action, checklist, current versus source revision warning, attachments, internal comment composer and immutable activity; assignment rail. Explicit reasoned complete/reopen, eligible owner/team selector and date control. Never imply task completion approved/issued a linked transaction. |
| Shared notes | Dedicated Internal notes heading and author/time timeline on scoped policy/quote/client-relationship/agency surfaces. Add note requires nonblank text; clear only after persisted success. Internal visibility is visible beside composer, not only in help text. |
| Documents | Table of kind/title, exact source date/version, file version, generated/uploaded date, availability and actions. Show generation pending, screening pending, failed, ready, withdrawn separately. Preview and Download choose an exact ready version. Version drawer lists original hashes/provenance and delivery history; issued files cannot be overwritten/deleted. Upload evidence uses real file picker and readable size/type limits. |
| PDF preview | Authenticated selected-version preview in labelled dialog/panel with filename, version and Download. Native PDF viewing may vary; provide direct download and text metadata fallback. Closing returns focus. Never embed an arbitrary caller URL or expose storage paths. Actual files use restrained A4 brand header, dated policy/reference, clear section hierarchy, repeated table headers and page numbers. Fictional demo notice remains visible. |
| Messages | Separate agency thread from internal notes; thread audience badge, selected verified recipients, draft composer and eligible exact-version attachments with remove buttons. Recipient/attachment picker identifies subject and visibility; server rejection retains draft. Sent item shows queued/delivered/failed plus attempts; Retry retains operation, Resend creates explicit delivery after confirmation of selected versions. No composer pretending to send via mailto. |
| Pack | Select applicable ready documents/versions and recipients; review filename/version/source date plus audience before Send pack. Failed delivery names safe cause and next action. Old pack view stays pinned even after new policy/document version. |
| Incident entry | Policy context rail and source Incident, conditional Vehicle and driver or Property involved, Third party, Description and Notification sections; Save draft/Save without sending, Save description and Log and hand off. Labels preserve date/approximate time/unknown values. A draft can be saved incomplete. Required-before-handoff checklist links to fields; persisted draft reference returned. |
| Occurrence resolution | Show loss date/time precision and cover at that date. If ambiguous, explain affected version intervals and require factual clarification with reason; no preselected inferred midnight. Do not promise cover merely because an incident can be logged. CC uses owned location/cover/liability/occupation fields and has no motor selectors. |
| Incident detail | Logged detail/Summary from administrator tabs, exact immutable submitted facts, handoff attempts, administrator reference and Contact administrator action. Not yet advised differs from0. Paid/reserve/status summaries are labelled external demo summary with received time; there are no local settlement buttons. Failed/awaiting acknowledgement does not use a sent-success banner. |
| MID | Motor Trade vehicle/trade-plate context, action add/change/remove, effective date, selected version, exact submission and attempt history, accepted/rejected/pending state, safe error, Retry and Open exception task. No MID tab/actions for CC. Later vehicle version remains independently visible. |
| Cancellation | Notice/file delivery and effective certificate withdrawal/task closure each show their own saved state/time. Future actions read Scheduled for [date/time]; notices may be delivered earlier. Historical document view indicates withdrawal without deleting the original download history. |

Replace all inherited Phase9 placeholders on relevant existing pages only when their actual endpoint and saved workflow exist. Source global-tab fallbacks must not introduce irrelevant motor content on CC. Shared client/agency views aggregate authorized records; they cannot expose another relationship's internal content.

## Copywriting contract

| Action/state | Copy |
|---|---|
| Task actions | Create task; Save task; Complete task; Reopen task; Reassign selected; Change due date |
| Empty tasks | No tasks match this view. / Change the filters or create a task. |
| Completion | Complete [reference]? Record why the work is complete. / Linked referral decisions remain separate. |
| Reopen | Reopen [reference]? Record what further work is needed. |
| Bulk conflict | Some selected tasks changed or are no longer available. Refresh the selection before applying this action. |
| Empty documents | No documents available yet. / Generate an applicable document or upload evidence. |
| Upload pending | File received. Checks and storage are pending. |
| Upload rejected | This file cannot be accepted: [safe reason]. Choose a supported file within [limit]. |
| Ready document | Ready to download · Version [number] |
| Generated request pending | Document generation is queued. |
| Withdrawal | Withdrawn from [date/time]. This is the retained historical version. |
| Note | Add internal note / Visible to internal users only |
| Empty thread | No messages in this thread. / Choose recipients and write the first message. |
| Send | Send to agency / Send document pack |
| Queued | Delivery queued. |
| Failed | Delivery failed: [safe reason]. Review the details or retry this delivery. |
| Uncertain | Delivery is not yet confirmed. Check its status before starting another send. |
| Resend confirmation | Resend these [count] document versions to [recipients]? This records a new delivery. |
| Incident draft | Incident draft saved. It has not been sent to the claims administrator. |
| Incident empty | No incidents recorded. / Log an incident to record the details and request a claims handoff. |
| Ambiguous date | Cover changed during the reported loss period. Clarify when the incident occurred before handoff. |
| Incident handoff | Handoff queued. / Awaiting acknowledgement. / Acknowledged by [administrator]. |
| Missing summary | No administrator summary received yet. |
| MID retry | Retry this submission / Submission queued for retry. |
| Stale input | This record changed after you opened it. Review the saved version before applying your changes. |

Use specific commands, never generic OK/Submit or toast-only success. Do not expose implementation terms such as outbox, storage key or idempotency key in normal business text. Demo status remains honestly labelled.

## Responsive, accessible and recovery behavior

At390px stack fields and rail, wrap headers/actions/filenames, retain full references and amount precision. Page-level horizontal scroll fails acceptance; only labelled focusable table/tab regions may scroll. At200% zoom controls and messages remain reachable. Dialogs have title, focus trap, Escape/close, restored focus and labelled fields. Selection uses native checkboxes, checklist changes have text status, keyboard users can open linked records without activating selection. Error summary links to invalid fields and focuses after failed validation. Save/queued/completed outcomes use live regions; pending state disables only conflicting actions.

Preserve permitted unsaved inputs, selected versions and exact request identity in memory across network uncertainty. Retry same intent; reload before a subsequent edit after replay. Remove inaccessible cached details on authorization loss; no sensitive browser localStorage. Draft edits before hydration cannot be acknowledged as saved. Large uploads show progress/processing/error honestly and cannot replace an existing immutable file. Download rechecks current authorization even from historical pages.

## Registry safety and checker sign-off

No registry blocks or new frontend package. PDF renderer/font assets belong to the .NET document boundary with pinned provenance/licensing, not a UI registry.

- [x] Copywriting PASS — concrete commands, empty/error/conflict/uncertainty states and reasoned confirmations.
- [x] Visuals PASS — source task/incident composition, shared operational tabs, focal points and390px behavior.
- [x] Color PASS — exact retained palette and textual statuses.
- [x] Typography PASS — four main roles and documented existing source exceptions.
- [x] Spacing PASS — declared scale plus source-preserving exceptions.
- [x] Registry safety PASS — existing local UI components only.

Approved by inline autonomous design review2026-09-21. ContextD-01..15 respected. Runtime visual/browser/PDF checks remain pending implementation.
