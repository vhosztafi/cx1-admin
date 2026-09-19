---
phase: 08
slug: commercial-combined-back-office
status: approved
shadcn_initialized: false
preset: none
created: 2026-09-19
---

# Phase8 — UI design contract

Design contract reviewed inline under gsd-ui-phase and the approved autonomous workflow. This is not an implementation, screenshot review or human UAT result. Sources:08-CONTEXT,08-RESEARCH, the CC prototype capture/policy methods, Phase7 UI contract, current globals.css and policy/quote components.

## Design system

Reuse Next.js/TypeScript/Tailwind and existing Panel, Status, DataTable, SectionTabs, record header, dialogs and field controls. Local IBM Plex Sans; existing inline icons with text labels. No new component registry, font, image, Catalyst or shadcn dependency. The prototype is the visual authority.

Keep238px sidebar,62px topbar, main content plus314px action rail. At1100px the rail moves below the content. At390px forms become one column, money summaries wrap, and only labelled focusable tables/tabs scroll locally. Page-level horizontal scrolling is a failure.

## Spacing and typography

Use4/8/12/16/24/32px spacing. Retain source exceptions:6px label gap,10px action gap/panel radius,18px stack gap,20px content/rail gap,22px vertical page inset,7px control radius. These preserve the approved prototype; do not globally restyle existing primitives for a generic scale.

| Role | Size | Weight | Line height |
|---|---|---|---|
| Body/table |13px |400 |1.5 |
| Label/help |12px |600/400 |1.5 |
| Panel heading/input |14px |600/400 |1.5 |
| Record heading |23px |600 |1.25 |

Right-align currency with tabular numerals. Use exact saved totals. Human references and location names precede technical IDs; hashes and configuration IDs belong in expandable provenance.

## Color and hierarchy

Canvas #f6f7f9, panels/sidebar white, text #14161c, muted #6b7184, borders #e4e6ec. Accent #3f5ef5 is reserved for the next primary action, selected stage/tab and scoped record links. Neutral styling for secondary actions. Preserve existing warning/error/success tokens with explicit text; destructive treatment only for removal, abandonment and cancellation. Retain3px visible keyboard focus.

Quote focal point: current capture stage heading and its first incomplete section, followed by next action and compact readiness rail. Policy focal point: client/policy reference, product and effective cover status, followed by selected-version risk. Exposure focal point: proposed SI and resulting capacity status with dated context. Do not use a decorative chart or large dashboard tiles to displace the source tables.

## Screens and interactions

| Surface | Required content and behavior |
|---|---|
| New quote | Existing agency/client/product flow. CC selection only with a published supported capture pipeline. Clearly identify Commercial Combined. |
| Capture stages | Agency & product; Proposer & history; Claims & losses; Locations & occupancy; Construction & protections; Flood & subsidence; Sums insured & BI; Liability & wage roll; Health & safety; Cover & declarations. Stage changes navigate; they do not submit underwriting decisions. |
| Location schedule/editor | Reference/address/postcode, occupancy, construction/protection and flood answers, buildings/contents/stock and computed total. Add location, Edit location, Remove location. Per-location questions identify their subject. Include empty state and incomplete-row markers. |
| Wage/loss editors | Category/headcount/employee/LOSC/BFSC amounts; loss date/type/location/paid/reserve/details. Stable rows survive save/reload. Removing a row explains the proposal effect. |
| Conditional questions | All109 source questions rendered in their appropriate global/item context. Hidden inapplicable answers are handled explicitly by the schema, never silently used to insure or price a deselected section. Unknown remains an unanswered state. |
| Review/rating | Actual section factors, annual/term premium, tax/fee/commission, configuration provenance, blockers/referrals and demo assumptions. Property, BI and liabilities remain distinguishable. No driver/NCD placeholders. |
| Exposure | Per-location SI and largest-location demo MEL proxy. Per-district proposed contribution, authorized book total/limit/headroom and effective interval/observed time. Agency users see own exposure and capacity outcome only. Preview states that issue rechecks capacity. |
| Underwriting/terms | Current CC conditions/evidence targets, independent internal/carrier decisions, terms preparation/demo delivery/acceptance. Use existing persistent action states. |
| Policy overview | Actual client/agency/insurer/product/term/version, CC business summary, property and liability/BI values, current or selected historical status. Opening obligation is labelled separately from cash balance. |
| Product tabs | Risk Details, Cover, Property schedule, Liability & employees, Business interruption. No Drivers, Vehicles, registration or MID actions. Shared supported history/servicing tabs remain available. |
| Adjustment | Shared Changes/Review & rate/Documents structure with typed CC property, business, liability/wages, BI, loss and cover editors; labelled current/proposed values and effective slices. No edits to issued JSON. |
| Renewal/cancellation | Shared expiring-risk, invitation, acceptance and cancellation preview/notice/issue flows with CC subjects and cover. Early renewal/future cancellation show dated status without changing current cover prematurely. |
| Documents/incidents | Show durable product-appropriate requests and exact version; generation/delivery/logging remain Phase9. Do not offer a fake download or imply a queued certificate has been produced. |

## Copywriting contract

| State/action | Copy |
|---|---|
| Save |“Save quote draft” / “Save adjustment draft” |
| No locations |“No locations added.” / “Add a location to record property and construction details.” |
| No wages |“No wage categories added.” / “Add employee and subcontractor wages, or review whether employers’ liability is required.” |
| No losses |“No losses recorded.” / “Confirm the loss declaration or add the relevant loss details.” |
| Missing answer |“Answer [question] for [location/section] before requesting a rating.” |
| Save success |“Draft saved.” plus actual revision/time |
| Exposure proxy |“Demo MEL — largest location sum insured” |
| Exposure preview |“Capacity checked as at [time]. Issue will check the book again.” |
| Capacity changed |“District capacity changed after this review. Refresh the exposure assessment before continuing.” |
| Hard ceiling |“The proposed cover exceeds the district limit for [district] from [date]. Review the proposed exposure.” |
| Retry uncertainty |“The result of this request is not yet confirmed. Check its status or retry the same request.” |
| Stale draft |“This draft changed after you opened it. Compare the saved version before applying your changes.” |
| Remove location |“Remove [location reference] from this proposal? Issued cover changes only when the adjustment is issued.” |
| EL deselected |“Employers’ liability is not selected. No EL certificate will be requested.” |
| Request queued |“Document request recorded. Generation is pending.” |
| Incident boundary |“Incident logging is not available yet.” No invented incident reference. |

Use specific action names: Request rating, Review referral, Prepare terms, Record acceptance, Issue policy, Issue adjustment, Renew policy. Final cancellation confirmation names policy/effective date and exact financial effect. No generic Submit/OK buttons.

## Recovery and accessibility

Keep mutation body, key, ETags and lease generation fixed for exact retries. Preserve unsaved permissible inputs in memory during412/lease recovery; compare before resubmission. Remove inaccessible server details after authorization loss; never cache policy/evidence data in localStorage. Server capabilities drive available actions and safe explanatory disabled states.

Dialogs have labelled fields, visible title, focus trap, Escape/close and focus restoration. Validation summary links to invalid fields and identifies location/category. Announce save/error/queued/completed outcomes through appropriate live regions. All icon actions have accessible labels. Before/after and status information remains understandable without color. Currency and full references remain readable at390px and200% zoom.

## Registry safety

No third-party registry blocks or new packages. Existing local components only; no remote artwork or image generation required for this data-entry application.

## Checker sign-off

- [x] Copywriting: PASS — specific actions, empty/error/recovery/removal copy.
- [x] Visuals: PASS — source shell, focal points, product surfaces and responsive behavior.
- [x] Color: PASS — exact palette, restricted accent and text status.
- [x] Typography: PASS — four role sizes, retained source hierarchy.
- [x] Spacing: PASS — declared scale and explicit source-preservation exceptions.
- [x] Registry safety: PASS — no registry dependency.

**Approval:** autonomous inline design check2026-09-19. Context D-01..12 respected; no unresolved design blocker. Runtime visual/browser evidence is required in implementation plans.
