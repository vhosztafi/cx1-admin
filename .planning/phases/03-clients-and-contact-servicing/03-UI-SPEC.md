# Client servicing UI contract

Source authority: supplied prototype and extracted template; preserve Phase 2 shell. This contract adds actual client record headers and action rails and is reviewed inline before implementation. No Catalyst dependency is necessary.

## Layout and visual rules

- Reuse IBM Plex Sans, brand #3F5EF5, canvas #f6f7f9, white panels with #e4e6ec borders/radius10, compact source typography and status palette. Keep 238px desktop sidebar and 62px topbar.
- Extract record-header container styles from the source renderer before coding; retain white title/value text on source blue, type/status row, compact identity facts and source-style header tabs. Labels use 10.5px uppercase tracking .08em and #bcc8fa; facts use 13.5px/500. Do not substitute generic white dashboard cards for this header.
- At 1560x1000, client overview uses full-width records/identity panels and smaller contact/activity panels. Match detail has a main evidence region plus a compact right decision rail. Use the exact source rail width/gaps after inspecting the template container. At 390px, facts wrap, rail follows content, form actions remain reachable, and tables scroll within focusable labelled regions without page overflow.
- Existing focus outline/skip link, semantic headings, table captions, status text and reduced-motion rules apply. Modals use a labelled native dialog with initial focus, Escape and focus return; no keyboard trap outside the modal. Inline forms are acceptable for full identity editing.

## Screens and states

| Screen | Required content and interactions |
|---|---|
| Clients list | Search business/contact/agency, entity-type filter, paged SQL results, reference/name links, Create client. Show unavailable fields honestly until their owners exist. Clear filter and retry actions for empty/error cases. |
| Client header/Overview | Actual CN reference, legal identity, address, creation date, relationship names and primary contact for explicitly selected relationship. Edit identity subject to capability. No fake tenure, screening, claims or exposure. |
| Policies/Quotes | Source tab structure with a clear not-yet-available state; no invented rows. Later phases plug in scoped real data. New quote action stays explicitly unavailable until Phase 5. |
| Contacts | Visible relationship selector, source columns and all source role options. Add/edit, make primary, end with reason, ended-history toggle. Consent has Given/Withheld/Not asked; primary conflicts explain how to choose a replacement. |
| Support flags | Person selection restricted to chosen relationship contacts; type/category/internal wording/agency wording/consent/review/reason controls; explicit sharing selection. Internal table and protected history, amend/review/end. Declined consent removes sensitive entry and does not claim a saved flag. Safe preview is labelled with the selected relationship and excludes hidden flag indicators. |
| Activity | Persisted actor/time/fixed event summaries and safe links. Flag details never appear in a broad activity list. |
| Match list/detail | Intake label until real quote exists; evidence comparison and pinned rule tabs; decision rail with link/separate/decline/query/reopen and mandatory reason; saved state and timeline. Information request says Recorded, not Sent. No invented policy/claim evidence. |

## Copy and interaction contracts

Use source labels where functionality matches; use plain functional copy for implementation additions. A successful save appears only after API confirmation. Preserve data after 422/412/network errors; stale writes offer reload and never silently overwrite. Keep one idempotency key for uncertain same-intent retries. Disable duplicate submit. Announce busy/error/result feedback with aria-live. Redirect an ended session to login without exposing prior actor data. Display local business dates without UTC shifting; instants explicitly use Europe/London.

Prototype control options: contact roles Director/Company secretary/Partner/Proprietor/Office manager/Accounts/Workshop manager/Other. Flag types Vulnerability/Third-party authority/Financial difficulty/Accessible format/Interpreter required/Deceased or business ceased; categories Health/Life event/Resilience/Capability/Authority. Consent choices retain the prototype distinction; no legal certification is inferred from a label.

## UI contract check

PASS for source fidelity, component reuse, bounded scope, responsive/accessibility behavior, honest content and testability. 03-06 must capture source/app desktop and mobile renders, inspect blue header/rail proportions, and exercise focus/error states. This preimplementation check does not claim runtime or human UAT passed.
