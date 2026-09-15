# Agency UI contract

Baseline: existing Phase2/3 shell and prototype pNewAgency/pAgents/pAgency/pPortal. Use IBM Plex Sans, brand#3F5EF5, canvas#f6f7f9, white panels/border#e4e6ec/radius10, compact typography. Sidebar238px/topbar62px remain unchanged. RecordHeader retains blue background, white title/facts and uppercase muted labels. Source action/progress rail314px,20px main gap,16px rail-panel gap. At390px the rail follows content and labelled tables scroll inside the viewport. No Catalyst dependency is necessary.

## Screens and actions

| Route | Source structure and required states |
|---|---|
| /agents | Agents heading/Create agency; four KPI positions use scoped real counts (active, broker users/invited, suspended, open agency proposals/follow-ups). Search/reference/status/relationship-manager filters retained in URL, paged API rows. No copied fixed totals. |
| /agents/new | New unsaved form; first save assigns AG reference. Six stages: Agency & regulatory; Contacts; Products & commission; Agreement & compliance; Accounts; Review & activate. Source progress rail, Save draft/Save and exit/Back/Continue/Abandon; navigation saves current incomplete typed draft first. |
| /agents/{id}/onboarding | Reload saved step/details/products/users/evidence. Current server checklist explains each missing/stale item and links to stage. Regulatory syntax is labelled format-valid until persisted demo verification succeeds. Signature/PI proof comes from actual evidence. Activation requests countersign; show waiting/rejected/approved/applied with real actors. |
| /agents/{id} | Blue Wholesale agency header with real reference/contact/manager/user counts/products; balance unavailable until finance. Source tabs Overview, Users, Permissions & access, Products, Accounts, Activity. Invite/Preview portal/Suspend actions require effective capabilities. |
| Users and invitation dialog | Source Name/Email/Role/Status/Last active/Actions. Draft invitation list clearly says staged; active invites show delivery and acceptance separately. Edit/deactivate/reactivate/resend/revoke with reason/expiry. Dialog initially focuses name/email, Escape and focus return; uncertain saves prevent closing and preserve command key. |
| Permissions & access | Effective current role matrix from server policy and persisted allowlisted grants. Pending requests have request actor/date, approve/reject/revoke controls and real outcome. Future capabilities say granted but feature unavailable where appropriate; never promise underwriting access merely from a product grant. |
| Products and Accounts | Persisted product names/provider, grant state/commission/effective dates and terms version. Draft edits use onboarding; agreed changes require proposal plus separate approver. Accounts shows saved credit/settlement settings; balance/statement/export remain unavailable until Phase10. |
| Activity and notifications | Real actors/times, safe summaries and scoped links. Delivery panel shows queued/demo-delivered/rejected/exhausted and retry when allowed. A queued job never displays Sent. No token or raw provider payload in rows/screenshots. |
| /agents/{id}/sharing | Source internal data-reference banner and Back to agency. Actual own clients/relationship contacts/granted support instructions; hidden-field matrix. Policies/quotes/open tasks/statements unavailable until owners exist. Never render another agency's identifiers, internal support evidence or a fake broker session. |
| /invitations/accept | Minimal local identity acceptance form, protected token handled in request body/URL fragment, password never echoed; expired/revoked/replaced/used link feedback. No automatic login/role override, external links or broker workflow pages. |

## Form/feedback rules

Preserve every source option in04-01 field mapping, including conditional principal/trading address/commission share/volume/minimum override inputs. Money as exact decimal strings and rates as percent UI converted to integer basis points. Dates are explicit local business dates; technical UTC details stay out of ordinary fields except where necessary. Save success appears only after confirmed API result. Long validation lists link to labelled fields/stages. Use role=alert for errors and persistent status live regions for saves/check completion.

ETag conflict retains current draft and offers explicit reload; uncertain response retains exact body/key/version, disables duplicate submit and blocks navigation that would discard command identity. Read errors have retry and never reveal server details. Declining/revoking/abandoning requires explicit reason, preserves history and explains the concrete effect. Avoid generic Delete and success-only toasts.

Use shared accessible native dialogs, semantic headings, table captions/focusable scroll regions, visible focus, skip link and reduced motion. Browser checks must exercise dialogs, multi-step drafts, two-user approval, mismatched agency/session denial,390px overflow and exact314px rail. Full human/assistive-technology UAT stays separate.

## Preimplementation UI check

PASS: approved source fidelity, known component reuse, complete states and bounded route ownership. Content for future phases is clearly separated from current persistence; no invented financial/insurance rows. Every new action has a planned API and verification owner. Runtime visual review remains04-08.


## Implemented action-count semantics

The four directory KPI cards retain the approved positions. Open actions counts pending state/terms/access decisions and shows that breakdown; directory rows show their own pending count. A separate dated line and per-row sublabel show recorded follow-up obligations due, with honest notice that completion tracking is not available. Due obligations are not mixed into open workflow actions. All-agency totals remain visibly independent of directory filters; loading uses an ellipsis and failures offer retry.
