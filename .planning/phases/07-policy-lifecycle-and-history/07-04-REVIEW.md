# 07-04 implementation and source review

Review scope: typed servicing capture, editor GET, proposal rules, six focused
editor components, date/review components and verification harnesses. Sequential
inline review; no external reviewer or human UAT is claimed.

The eight prototype picker categories use six focused editors (driver and
vehicle each provide add/remove). The proposed monolithic servicing-change-editor
file was split along those category boundaries. Existing quote capture controls
supply pinned enums, question IDs, exact money and stable nested identities.

Source action equivalences verified by the final browser gate:

- Prototype policy header/rail/section actions all navigate to the same MTA
  workspace. The functional implementation uses the policy's Servicing drafts
  panel: choose Policy adjustment and create, or resume an existing saved draft.
  It supplies real policy/term/base identity instead of fixed MTA-007. Back to
  policy returns to that exact record. Duplicate source entry points share this
  command rather than creating duplicate independent implementations.
- Add change/Done and picker options are represented by labelled category actions,
  native dialogs, Apply to draft and Discard/Keep form. Saving the whole draft is
  explicit; no local form action claims persistence until the API save succeeds.
- Prototype named change rows use actual stable IDs and current names instead of
  Rebecca/Liam/Kevin fixture strings. A retained item is edited in its category;
  Remove proposed change cancels a change, including restoring a proposed removal.
  A removal has no editable risk payload: correcting the person's details requires
  cancelling the removal and creating a typed update. This preserves the closed
  add/update/remove contract rather than saving ignored fields on a removal.
- Prototype licence and date-of-test fields map to typed licence references,
  licence.number and licence.testDate. Claims choices are represented by detailed
  dated claim rows/fault/status/amounts, avoiding lossy fixed summary strings.
- Requested-by/date/date-basis fields use the shared saved draft controls and
  explicit London clock choices. Cover dates remain attached to their change IDs.

Security and data review:

- Current scope, ETag and held lease are checked by the existing command boundary
  before persistence/replay. The editor read holds scope and returns no-store.
- Closed proposal schema and server rules reject foreign IDs, client transfer,
  duplicate/nested identity reuse, system fields and non-cover date overrides.
- Explicit replacement clears fields; vehicle selection/requirement changes are
  declared. Dependent driver/vehicle/premises references are validated server-side.
- Cumulative slices are assessed at each effective date. Local dated cover context
  excludes future values and does not grant rating or issue authority.
- Issued data is never written by capture. Rating/approval/acceptance remains a
  later plan; revision identity is the future applicability boundary.

Earlier checkpoint backend evidence:31 unit +1 native real-SQL case, zero skips, under
.local/phase7-04-final. assert-test-results.ps1 verified32 passed results against
2026-09-17T15:10:00Z. The SQL case checks persisted proposal/editor projection,
current grants and unchanged issued JSON/hash/registrations. Existing03 lease
and CSRF negative evidence remains applicable; the new browser audit explicitly
rejects a non-cover override without advancing the saved revision.

Fresh root351 and frontend116 cases, typecheck and lint pass. Last production
build is .local/phase7-04-cover-build.log and serves all current UI changes.
The final gate results below supersede this earlier verification checkpoint.

Review finding and fix: primitive membership lists (premisesIds/driverIds/
specifiedVehicleIds) still produced material differences when reordered. A
failing-first covered-premises test reproduced two spurious changes. Servicing
comparison now normalizes only these GUID membership sets; ordered business
values such as proposer names remain ordered. The shared quote diff is unchanged.
32 focused unit cases pass after this fix. Fresh SQL verification is running
under .local/phase7-04-reviewed; the owned Release preview will be rebuilt after
the current browser suite finishes, then the saved-review browser check repeated.

A second failing-first comparison case showed that sorting stable-ID rows before
diffing changed displayed row positions (Driver1 could be labelled Driver2).
Servicing normalization now retains stable-ID row order; QuoteRevisionDiff
already matches by ID and reports reorder separately, which servicing excludes.
Known GUID membership sets still normalize, and question-answer identity sorting
remains. The new test verifies the actual driver path and stable ItemId together.
This is a comparison-only backend refinement; no issued/capture storage changed.

The16-journey editor gate passed after one test-locator correction: the saved
comparison value also appeared in a driver option, so its assertion now scopes
to the comparison card. The gate resumed at the first unfinished case and checked
completed report timestamps/policy identities. Final evidence includes the
initial and resumed logs; no failed case was counted as a pass. The second
comparison refinement has33 focused unit cases plus fresh SQL pending, followed
by a targeted saved-review browser rerun on the rebuilt API. Earlier successful
capture/editor journeys remain valid for unchanged behavior.


## Final disposition

Both findings are fixed in 0d38d91. Final fresh evidence is 33 focused unit cases
plus 1 real SQL case, zero skips, in .local/phase7-04-reviewed-final. The result
assertion verified 34 cases with cutoff 2026-09-17T15:27:00Z. Final API Release
build and both-product saved-review browser rerun passed (see SUMMARY paths).
The complete 16-journey gate retains immutable per-case reports and hashes in
.local/browser-evidence/servicing-editors. All 38 plan-owned source entries are
implemented-browser-and-sql-verified; none remain open. Four source coverage
checks pass. Earlier pending statements above describe chronological checkpoints,
not outstanding work. No unresolved HIGH/CRITICAL finding. Plan 07-04 is complete;
rating and later authority workflows are explicitly outside this plan.
