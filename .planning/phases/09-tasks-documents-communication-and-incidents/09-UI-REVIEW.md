# Phase 9 — UI review

**Audited:** 22 September 2026, inline under the autonomous execution approval.
**Baseline:** 09-UI-SPEC.md, original prototype and retained shell.
**Scope:** Source review plus saved operational screenshots and browser evidence. The full v4 regression is still running; this report neither completes Phase9 nor substitutes for human UAT.
**Screenshots:** Existing actual acceptance captures inspected; no competing browser suite or new screenshot session launched. Screenshot storage remains ignored, including `.planning/ui-reviews/.gitignore`.

## Pillar scores

| Pillar | Score | Finding |
| --- | --- | --- |
| Copywriting | 3/4 | Concrete business commands and honest queued/unknown states; shared pagination renders “1 records”. |
| Visuals | 3/4 | Task title/action, metrics and panels retain the prototype hierarchy; the mobile shared-open-item action column becomes narrow and tall. |
| Color | 4/4 | Inspected views retain the white/grey workspace, blue actions/selection and textual amber awaiting states. No color-only operational outcome found in the reviewed components. |
| Typography | 3/4 | Local IBM Plex and shell roles remain consistent; the response subsection uses an unstyled h4, inheriting body size/weight under Tailwind preflight. |
| Spacing | 4/4 | Inspected task and incident views retain aligned fields, stacked narrow layouts and shared panel/action spacing. No new global spacing system or page overflow was introduced. |
| Experience design | 3/4 | Actual retries, stale edits, exact-version history and dialog focus have browser evidence. The response form's custom reason-validation alert has no explicit focus target or field association. |

**Overall: 20/24.** No task-blocking defect identified in this bounded review. Four WARNING findings remain below; none is a HIGH/CRITICAL data, authorization or workflow defect. Runtime source remains frozen for the active full gate. These presentation/accessibility refinements should be addressed in the Phase13 usability pass, with focused checks rather than an invented current fix.

## Priority refinements

1. **WARNING — response reason error focus.** `components/operations/agency-response-tracking.tsx` catches custom validation and renders `role="alert"`, but does not focus that alert or associate it with the reason input. A whitespace-only value can pass native minLength and fail the trimmed rule. Link the error to the field and focus the invalid field/summary; verify keyboard recovery. The reason is already rejected and retained, so invalid data is not saved.
2. **WARNING — narrow shared action column.** Both 390px agency open-item captures preserve the full text and fit the viewport, but even a short instruction wraps into many lines. Use a source-consistent narrow row/card or accessible expanded detail treatment while retaining the complete instruction and status. Do not truncate the only copy of the agency request.
3. **WARNING — singular count wording.** `components/clients/shared.tsx:21` formats every numeric total as “records”; task and agency screenshots show “1 records”. Pluralize the common component and verify zero/one/many without changing paging behavior.

Additional **WARNING — subsection typography:** `agency-response-tracking.tsx:14` renders an h4 without a class; the imported Tailwind preflight resets heading size/weight to inherit. Give this subsection the existing declared heading treatment, scoped to the component. Do not restyle every h4 globally.

## Evidence and limits

- Current full-run task capture: `.local/browser-evidence/tasks/CoverMGA_Test_a03f56b220674ecebd5ae3831bc205ee/tasks-desktop.png`, visually inspected. Its browser report contains19 successful checks: actual creator scope, dialog Escape/focus restoration, saved task/reload, uncertain identical retry, stale-input retention, reasoned complete/reopen, required checklist, scoped personal/team measures, atomic bulk conflict, due/assignment changes, search,390px queue/detail and immutable workflow provenance. This one result is not a full550-case pass.
- Both response mobile captures inspected: `.local/browser-evidence/communications/CoverMGA_Test_7985e30748294025bc0735ac83054f49/agency-open-items-mobile.png` and the same filename under `CoverMGA_Test_d431bd87d3de4fe098656c383ce3b2d2`. Their bounded SQL/browser gate passed2cases/22checks per product, including lost-response recovery, saved public row/reload, closure and independent SQL readback. These precede the current full run.
- Commercial incident mobile capture inspected: `.local/browser-evidence/incidents/CoverMGA_Test_c98e2dfefef44b6db910b7c13a27c60d/incident-mobile.png`. Property/liability, historical occupation and third-party/reporting fields remain labelled and stacked. Its14-check bounded report covers incomplete saves, exact retained bytes, conditional branches, historical ambiguity, clarification, stale edits and unsent-versus-handoff meaning.
- Commercial document bounded report reviewed: `.local/browser-evidence/documents/CoverMGA_Test_abb6d3b218ab4054a9fdf3661c368c20/browser-report.json`,22checks. Pending files refuse access; actual PDF bytes/hash, exact-version preview/focus, additive versions, foreign metadata denial, task attachments and original typed evidence are independently exercised. Full current collector validation remains queued.
- `communication-command.tsx` uses a titled native dialog, explicit focus placement/trap/restoration, frozen command bytes/key, current actor check, uncertainty navigation guards and retained stale input. `communication-shared.tsx` aborts obsolete loads and does not display a result under a different resource key. These source observations supplement the actual browser reports; they do not prove assistive-technology behavior.
- `globals.css` retains13px body, local font,62px topbar,3px focus outline and shared button/panel styles. Screenshots confirm the inspected composition. No new registry or remote imagery was introduced.

This is a sampled engineering visual review, not exhaustive pixel comparison of every source occurrence. Exact source-ID coverage and final current collectors remain09-17/18 gates. Human business, screen-reader and broader assistive-technology UAT remain unperformed. Existing200%/keyboard evidence is recorded in the owning slice reports; this review did not rerun or broaden those checks.

## Files examined

09-UI-SPEC.md,09-CONTEXT.md,09-17/18 plans and reviews,09-16 summary,09-17-DISPLAY-REVIEW.md; `apps/backoffice/app/globals.css`; operations response, communication command/resource components; agency shared-open-items; shared pagination; communication API command/error handling; task/document/incident browser scripts and the named saved reports. Existing Phase9 summaries/checkpoint supply the earlier PDF, retry and navigation provenance.
