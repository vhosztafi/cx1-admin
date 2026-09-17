---
phase: 07
slug: policy-lifecycle-and-history
status: approved
shadcn_initialized: false
preset: none
created: 2026-09-17
---

# Phase 7 UI design contract

Design review only. No Phase7 implementation, screenshots, human approval or
browser verification is implied. Generated using gsd-ui-phase inline under its
Codex adapter and the approved autonomous workflow. Sources: 07-CONTEXT,
07-DATA-API-DESIGN, source pPolicy/pMta/pRenewal/pCancelReview/pAsAt/modalVals,
Phase6 UI-SPEC, current globals.css and existing local primitives.

## Design system, spacing and typography

Reuse Next.js/TypeScript/Tailwind, local IBM Plex Sans, Panel, Status, DataTable,
SectionTabs, existing dialogs and record shell. No new registry, font, icon
package, artwork or Catalyst/shadcn dependency. Retain 238px sidebar and 62px
topbar. Record content uses minmax(0,1fr) plus 314px rail; rail moves below content
at the existing 1100px breakpoint. At 390px, cards and forms fit the viewport;
wide tables and tabs scroll in labelled focusable local regions.

Spacing: 4/8/12/16/24/32px internal scale. Keep source exceptions: 6px label gap,
10px action gap/panel radius, 18px stack gap, 20px main/rail gap, 22px page vertical
inset and 7px input/button radius. Reuse existing Panel's 16px/18px heading padding
and 14px/600 heading; do not globally restyle it to satisfy a generic scale.
Current page heading 23px/600, body 13px/400, labels 12px/600, inputs 14px/400.
Use existing line heights, with 1.5 for body/form values. Dense tables use
tabular right-aligned money; never calculate financial totals using browser
floating-point arithmetic. Human references precede IDs; provenance IDs/hashes
belong in expandable detail.

## Color and hierarchy

Canvas #f6f7f9, white panels, text #14161c, muted #6b7184, border #e4e6ec,
accent #3f5ef5. Retain existing status tokens and 3px focus outline. All status
colors have text labels. One next primary action per surface; cancellation and
abandonment use destructive treatment and a reason confirmation. Proposed values
use labelled before/after columns, not color alone. Removed items remain readable
in comparison; do not rely on strike-through as the only explanation.

## Screens and persistent interactions

| Surface | Content and behavior |
| --- | --- |
| Policy header/overview | Actual policy/client/agency/product/insurer, applicable term/version, effective cover state, source quote and next actions. Future issue appears as scheduled with effective date; today's summary stays on today's cover. |
| Policy product tabs | Actual applicable risk, cover, drivers, vehicles and premises. Propose change opens a persisted draft with selected stable item; never edits issued JSON. Generic operational tabs retain explicit Phase9/10 ownership. |
| Transaction/version history | Effective and processed timestamps, kind, sequence, reason, actor, amount due/credit, all version slices and durable document requests. Separate saved drafts from issued transactions. Same-policy version comparison resolves both versions server-side. |
| MTA Changes | Source Changes / Review & rate / Documents tabs; lease banner, requested-by, reason, common effective date, cover-only date overrides, grouped proposals, item-level validation and current/proposed values. Add/edit/remove typed changes; Save draft, Release lease, Take over, Abandon. |
| Change picker/editors | All eight source categories: add/remove driver, add/remove vehicle, premises, trade activities, cover/limits/excesses, policyholder correction. Select actual removal targets; repeated additions get separate stable identities. Full typed fields reuse capture controls; no generic JSON editor. |
| MTA review/rate | Cumulative risk per slice, rating reference/expiry, annual premiums and prorated movement breakdown, tax/fee/commission, total due or credit, current blockers, referrals/authority and evidence requirements. Expose dated slices and one transaction fee clearly. |
| Evidence and capacity | Actual files and target items, screening separately from review, attachment/withdrawal history, current referral decisions and carrier correspondence/conditions. Current servicing cycle is explicit; quote approvals do not appear as current servicing decisions. |
| Terms and acceptance | Prepare/version terms, required proof, select scoped recipient, demo delivery outcome, record exact acceptance with evidence and received time. Re-rating or changed evidence makes previous acceptance historical. |
| Adjustment receipt | Persisted transaction and all version references, dates, amount due/credit, one fee, document/MID requests and policy links. Use “Adjustment issued”, not “Payment collected”. |
| Renewal | Source four-stage layout, actual expiring and proposed term/risk differences, supplied experience form/evidence, premium movement, UW-31 and current checks. Stage navigation only navigates. Review, rate, invite, accept and issue are distinct persisted actions. |
| Renewal timeline/lapse | Configured-clock invitation due, expiry, inception and auto-lapse dates; overdue text; explicit lapse reason and demo notification status. Missing experience is unknown. Early issue leaves current cover unchanged; unsupported late issue shows a clear block. |
| Cancellation proposal/review | Source reason/date/notes form, notice evidence, effective-date/authority checks, exact per-component return preview, retained fees, credit/refund obligation and approval. Amend invalidates preview/approval; Abandon preserves history. Final confirmation names policy, effective time and financial effect. |
| As-at reconstruction | Arbitrary effective date/time, processing cutoff and basis; explicit London labels/offset disambiguation. Selected version/outcome, included/excluded transactions, supersession and applicable document requests. Draft exclusion is fixed. Export queues the selected immutable reconstruction. |
| Clone to quote | Confirm source version and authorized target relationship, then open actual new incomplete quote. Explain that underwriting decisions and acceptance must be obtained again. |

Policyholder correction must distinguish contractual insured details from the
client master record. Do not expose cross-client transfer through that editor.
Future cancellation reads “Cancellation scheduled for [date]” until effective;
afterward “Cancelled from [date]”. Expired cover is not active. No generic note,
task, claim, document download or collection button reports success before its
owning module stores the operation.

## Copy and state contract

| State | Required meaning/copy |
| --- | --- |
| No adjustments | “No adjustment drafts.” / “Create an adjustment to propose changes.” Only show action with capability. |
| Draft saved | “Draft saved. Issued cover is unchanged.” Show actual revision/time. |
| Own lease | “You hold this draft.” Display renewable expiry and Release lease. |
| Other lease | “[Name] is editing this draft.” Show read-only status, expiry and eligible takeover action. |
| Lease lost | “Your editing lease ended. Your unsaved changes are still shown for comparison.” Offer compare/current reload; no silent discard. |
| Base changed | “The policy changed after this draft was started. Review the issued changes before continuing.” No automatic rebase. |
| Invalid dates | Name the conflicting date, term boundary or later issued slice; give valid next action. Driver backdating is explicitly blocked. |
| Rating pending/expired | “Rating requested” / “This rating has expired. Re-rate before issuing.” No premium success from queued work. |
| Evidence received | “Attached — underwriting review required” until actual review. |
| Invitation | “Demo invitation queued” then “Demo invitation delivered”; do not say invited before applied delivery. |
| Missing experience | “Renewal experience has not been supplied.” Never show 0% as an inferred result. |
| Acceptance invalidated | “These terms changed after acceptance. Obtain acceptance of the current version.” |
| Cancellation preview changed | “The cancellation calculation changed. Review the new amounts and obtain fresh approval.” |
| Refund | “Credit/refund obligation recorded. Payment has not been made.” |
| Export pending | “Reconstruction request recorded. Document generation is pending.” No fake download. |
| Empty as-at | “No cover was in force at this effective date using the selected processing cutoff.” Show the cutoffs. |
| Abandon | “Abandon this draft? Its history will remain available and it can no longer be issued.” Require reason. |

## Recovery and accessibility

Freeze body, operation key, draft ETag, cycle/child ETags and lease generation
for an in-flight mutation. Uncertain responses offer exact retry/readback; do not
send a fresh operation while the previous outcome is unknown. On 412 retain
local inputs and show current/submitted revision context; explicit user review
precedes resubmission. Lease expiry/revocation disables mutation immediately;
an expired generation cannot be renewed by a delayed heartbeat. A takeover
requires a reason and clear effect on the previous editor, with no invented
notification-delivered claim.

Session recovery retains permissible unsaved input in memory only. On loss of
authorization remove inaccessible server details and explain safely; no sensitive
policy/evidence persistence in browser local storage. Capabilities and blockers
come from the server; disabled buttons are guidance, not enforcement.

Dialogs have a visible title, labelled controls, focus trap, first-field focus,
focus return and safe Escape. Unresolved writes cannot be discarded by Escape.
Errors link through aria-describedby; focus first invalid field. Announce job
and save status noninterruptively, not a countdown every second. Lease banners
remain readable without color. Use keyboard links/buttons instead of clickable
rows, no nested interactive elements, captioned tables and wrap long reasons.
Date/time fields identify London time and explicitly request offset choice for
ambiguous times. Confirmation summaries remain visible while action is pending.

## Verification contract

Owning implementation plans must include actual persisted desktop and 390px
journeys, screenshots, keyboard/dialog focus and empty/error states. Required
cases: two-session takeover/stale save with retained input, all picker categories,
both Motor products, multi-date cover change, future read/discovery stability,
as-at effective/processing difference, old/new snapshot comparison, renewal
delivery/acceptance/issue, configured-clock lapse, cancellation approval and
credit readback, clone with fresh identities, role denial and exact uncertain
retry. Validate document requests separately from Phase9 generated files.
Source-control/field traceability and full regression remain phase verification
gates. This design sign-off is not implementation or human UAT evidence.

## Inline checker sign-off

- Copywriting: PASS — specific primary, empty, conflict and destructive states;
  queued delivery, effective cover and unpaid credit remain distinct.
- Visuals: PASS — prototype record/rail/stages/comparison hierarchy retained;
  desktop and 390px verification required.
- Color: PASS — existing tokens and text-supported statuses; visible focus.
- Typography: PASS — local font and actual current component hierarchy preserved.
- Spacing: PASS — compact scale and existing source exceptions recorded.
- Registry safety: PASS — local components only, no registry/dependency install.

Design approved 2026-09-17 by inline review under the sequential workflow.
Runtime visual verification and human business review remain unperformed.
