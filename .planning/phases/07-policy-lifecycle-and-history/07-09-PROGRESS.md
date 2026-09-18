# 07-09 financial foundation — partial, 2026-09-18

Implementation commit: `44b9e02`. This is deliberately not a completion SUMMARY.
07-09 remains open; Phase7 still has 8/16 completed plans (57/65 overall).

## Implemented and measured

- Signed exact-penny movements retain code/ordinal and individual UTC intervals;
  negative movements reverse positive debit/credit lines. Mixed adjustment and
  cancellation worked totals pass pure tests. First-issue rules remain unchanged.
- Additive component ordinal/lineage columns, accounting period/posting date,
  and mutually exclusive original quote versus servicing transaction provenance.
  SourceQuoteId remains the original policy source. Compound FKs, exact rating
  movement view, per-code totals, owned parties, positive lines, full seal balance
  and append-only guards reject substitutions/omissions. No manual posting API.
- Infrastructure posting primitive consumes an accepted adjustment transaction;
  SQL independently derives expected movements from its retained rating. Real
  mixed positive/negative same-code intervals pass for Combined; both Motor Trade
  products pass owned posting, tamper, rollback/upgrade and old byte preservation.
- Accounting periods are nonoverlapping, with immutable boundaries and rowversion.
  Processing date uses Europe/London; a closed current period falls forward to the
  earliest open period, otherwise posting fails. Held period blocks concurrent
  close. Sealing independently rechecks the period. Missing-only seed uses the
  configured DemoClock year and next two years, never changing existing periods.
- Narrow Development-only `--seed-accounting-periods-demo` adds missing periods
  without full demo seeding. Both new migrations are applied to shared demo;
  all 112 existing table counts/hashes match before/after, including issued bytes.

## Evidence

- Missing-type red runs: `.local/phase7-09-posting-red.log` and
  `.local/phase7-09-period-red.log`. These are compile-time prerequisite failures,
  not claimed assertion failures.
- Initial pure run: 6 passed in `.local/phase7-09-posting-green/unit`.
- Initial accounting SQL: 2 passed in `.local/phase7-09-period-green/sql`.
- First signed/first-issue SQL run: 6 passed in `.local/phase7-09-signed-first/sql`.
- Strengthened mixed and upgrade SQL: 4 passed in `.local/phase7-09-mixed/sql`.
- Final affected regression: **797 unit + 26 real SQL = 823**, no skips,
  `.local/phase7-09-regression`; gate cutoff `2026-09-18T12:00:00Z`.
  SQL includes first issue/API authorization/replay/rollback, policy storage,
  posting seals, both-product terms storage rollback, both-product servicing
  postings and accounting contention. Earlier runs overlap this final evidence.
- `pnpm web:typecheck` passes: `.local/phase7-09-web-typecheck.log`.
- Preservation: `.local/phase7-09-preservation.sql`, `-before.txt`, `-after.txt`;
  migration `.local/phase7-09-demo-migration.log`; narrow seed `-demo-seed.log`.
- No new browser journey is claimed: this prerequisite exposes no public command.
  Existing preview still uses the 07-08 Release binary; shared schema is compatible.

## Dependency repair and work still required

Plan09's cancellation return storage tests depend on the reviewed preview/approval
decision introduced by plans13/14. Opening cancellation transactions now would
bypass that decision. Storage therefore **fails closed** for renewal/cancellation;
OriginalComponentId cannot yet be used in a posted movement. The additive FK and
pure cancellation arithmetic do not prove return posting, original-party lineage,
cumulative-return bounds or duplicate-return protection.

Continue plan10 using the verified adjustment foundation, then finish the
cancellation portion of09 alongside13/14 before closing09 or the phase. Required
remaining evidence: accepted cancellation source; same policy/term/currency,
original party/settlement and interval validation; positive returns of originally
negative movements; prior-return and duplicate-original rejection; expected
cancellation movement set and sealed balance. Renewal activation belongs11/12.

The transaction provenance portion of10 was brought forward because09 cannot
validate a real servicing ledger against a fake original quote rating. Plan10
still owns current issue authority/evidence/base/lease checks, atomic versions,
registration/document/MID intents, receipt/replay, API and actual browser issue.
Do not mark POL-04/POL-09 or09 complete based on this foundation commit.
