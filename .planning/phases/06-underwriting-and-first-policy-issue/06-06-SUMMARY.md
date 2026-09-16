---
phase: 06-underwriting-and-first-policy-issue
plan: '06'
status: complete
completed: 2026-09-16
implementation_commit: f359532
requirements_completed: []
---

# 06-06 — Underwriting decisions and evidence UI

Implemented in `f359532`. Quote records now have a working Underwriting tab and
Overview referral navigation. Operators can select current referrals, record
approve/conditional/query/decline/reopen decisions, use the closed condition
catalogue and inspect paginated immutable histories. Real supporting files can be
uploaded, attached to exact purposes, independently accepted/rejected, withdrawn
and selected to resolve documentary/warranty conditions. History and current
applicability remain distinct. The decision rail is314px and stacks on mobile.

## API and implementation refinements

- Server assessment projects requested/actor/binder dimensions for each current
  grant separately; absent grants are explicit. Maxima are never merged across
  grants. Driver/licence/trade/history dimensions remain independent from premium.
  Pure semantic tests and real scoped API assertions cover this refinement.
- No schema migration was needed. Existing05proof/decision migrations were applied
  before UI verification. Prepared-statement/terms actions remain gated until08/09.
- Added focused decision-command modal and pure underwriting-decisions helpers
  rather than embedding transport/recovery logic in rendering components. Existing
  exact receipt validation now supports multipart files. Current account identity
  is rechecked before writes; uncertain outcomes freeze body/key/quote/child ETags.
- Stale412 retains form inputs, displays current state on request and requires
  deliberate reload/review. Native dialog contains keyboard focus and prevents
  leaving an uncertain command. A changed account prevents dispatch.
- Actual applied endorsement wording replaces Overview placeholders. Required
  reviewed driver proof produces source wording “At renewal”; no invented date or
  external DVLA verification is implied. Saved driver/premises targets are named.

## Verification

- **743backend cases =608unit+135integration;108realSQL;zero skips**.
  `.local/phase6-06-backend-20260916-first`, log`.local/phase6-06-backend-first.log`.
  Integration14m21s; assert-test-results passed measured743/108 minima. Production
  backend unchanged after this run. Targeted API evidence:
  `.local/phase6-06-authority-api`; pure tests first failed on the absent helper,
  then exposed an incomplete road-risks test fixture, corrected before full run.
- **90frontend tests**, lint and production build pass. Final logs:
  `.local/phase6-06-web-tests-targets.log`, `phase6-06-web-lint-targets.log`,
  `phase6-06-web-build-targets.log`. TypeScript is also verified by the final build;
  explicit preceding check:`phase6-06-web-typecheck-final.log`.
- **317contract/source tests** and OpenAPI validation with10retained warnings:
  `.local/phase6-06-contracts-final.log`, `phase6-06-openapi-final.log`.
- Actual Chrome both-product journey passed in`phase6-06-browser-final.log`:
  query/decline/reopen/conditional histories, deliberately lost committed response
  and exact retry, uncertain Escape protection, concurrent real upload causing412
  with retained reason/readback, actual bytes/association/content review,
  documentary and warranty resolution, withdrawal reopening the condition with
  unchanged price, servicing denial, changed-account denial and no decision write,
  focus wrapping,314px rail and390px containment.
- Combined excess-stock plus missing-premises-proof fixture selected3referrals;
  bulk approval was denied with **zero decision rows**. This is a real API-backed
  UI action, not an intercepted successful result.
- Final read-only browser pass after saved-target label refinement passed:
  `.local/phase6-06-browser-readback.log`. Report and screenshots:
  `.local/browser-evidence/underwriting-decisions/`. Both-product final detail
  screenshots inspected, including Combined desktop and Road Risks mobile.
  Initial screenshots exposed unstyled forms/cramped mobile columns; fixed with
  scoped existing-theme controls and local table scrolling, then rebuilt/rechecked.
- Final demo examples:Combined76917cfc-decc-49bc-a677-78b763fc6b6c;
  RoadRisks e13a0a3b-36a5-43a9-b055-f889beb7ab96;
  deniedCombined753d7a8c-cab9-4508-81ee-fac11325f40e. Browser harness uses04report
  as explicit approved product/relationship prerequisite and creates fresh quotes.
- git diff --check passed. Frontend-code untouched. Owned API66724/Next23432 stopped
  after command-line verification. No database reset or external transmissions.

## Source ownership and next work

Ten source control placements reviewed:9referral/navigation/decision placements
are implemented; “Refer to capacity provider” depends on07and remains pending.
Four assigned display occurrences use actual authority/referral/proof data; the
browser exercises trading proof, while discount proof follows actual server
requirements rather than a fabricated fixture. Endorsement/licence displays from04
now consume05projections. Generic messaging staysPhase9; query records a durable
case-specific request without claiming delivery.

Next07implements capacity escalation, exact provider submission/response and
dimension-specific extensions. Terms, acceptance and issue remain later plans.
No compound requirement is complete until14. Human business/assistive-tech UAT,
hostedCI and Docker remain unperformed.
