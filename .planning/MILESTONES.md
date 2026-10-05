# Project Milestones: Cover MGA Back Office

## v1.1 Prototype Alignment and Funnel Servicing (Shipped: 2026-10-05)

**Locally completed:** Phases 14–24, 11 plans, 38/38 approved requirements. Tasks were not separately counted in these inline plans.

**Key accomplishments:**

- Source-derived shell, record tabs, contextual actions and responsive Motor Trade/shared section layouts.
- Client, agency, broker and eligible product selected before quote capture; existing cx1-implementation funnel reused for quote and MTA.
- Saved quote/MTA/renewal/cancellation issue, exact replay, issued history and balanced financial consequences.
- Real document PDF/demo delivery, historical-cover claims summary and reviewed carrier correspondence/outcome.
- Saved receipt allocation and two-person refund/payment, scoped reporting/agency summaries, administration and actual password/MFA/session controls.

**Evidence:** [Section comparison and verification](../docs/design/PROTOTYPE-ALIGNMENT-v1.1.md), [archived audit](milestones/v1.1-MILESTONE-AUDIT.md). Final contracts: 42+16+12; SQL: two Motor Trade multi-date issue scenarios plus scoped capacity API regression; TypeScript/ESLint and saved desktop/mobile browser journeys passed.

**Statistics:** 2026-10-05; back-office change range 2ab8e3a through 74a6995, 152 files, 44,194 inserted and 528 deleted lines including generated EF migration designers/contracts/planning, not product LOC. Funnel commits 5c1ba5c and b9c6d57.

**Limits:** Local implementation and verification; hosted rollout/remote push and human UAT not performed. Demo outcomes are not real provider operations. Two historical v1.0 artifact records remain accepted debt in STATE.md; no new v1.1 requirement gap is hidden. Phase directories retained for history.

---

## v1.0 Functional Back Office MVP (Closed: 2026-09-26)

**Delivered:** Complete local insurance back office with persistent fictional workflows; no production deployment.

**Phases:** 1–13, 131 plans and summaries.

### Accomplishments

- Persistent Motor Trade Road Risks, Motor Trade Combined and Commercial Combined capture, underwriting and issue.
- Immutable policy history, adjustments, renewals, cancellation and reconciled signed finance.
- Saved tasks, scoped communications, versioned documents, claims/MID handoffs and durable demo retries.
- Agency/client administration, independent sensitive approvals, local MFA and current authorization.
- Scoped search, live dashboards, eleven reports, saved favourites and safe CSV exports.
- 949-control traceability, accumulated SQL/browser evidence, concise demo and developer handover.

### Statistics

- 13 elapsed calendar days: 2026-09-13 through 2026-09-26.
- 795 commits from cfda6e7 through 47e73e5, before archive commits.
- 2,725 changed files, 2,141,477 inserted lines and one deleted line across all tracked artifacts; includes generated contracts and planning, not product LOC.
- 66,172 physical source lines across 1,006 tracked app/backend source files, excluding generated files and EF migrations; includes blanks/comments.
- 261 XML task declarations in plans. Summaries do not consistently record task totals; this is a planning count, not an independently measured completion count.

### Known gaps

Closed as a local MVP with accepted verification limits following the user’s explicit complete-milestone command after the audit. ACC-02 remains partially verified: application restart passed, dedicated SQL engine restart unperformed. Human business and assistive reviews remain pending. Acceptance of debt does not turn these into test passes.

Open-artifact scanner records acknowledged at close: 2 (see STATE.md Deferred Items). The Phase 10 initial gap report is historical and superseded; the Phase 13 UAT record is genuinely partial. Additional accepted audit debt remains in [the archived audit](milestones/v1.0-MILESTONE-AUDIT.md).

**Next:** $gsd-new-milestone; scope/version remain undecided.
