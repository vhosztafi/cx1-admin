# 10-01 — Finance source and data contracts

Completed 2026-09-24. `docs/design/FINANCE-CONTRACTS.md` fixes the intended identities, posting/date/money bases, current scope, statement equation, cash residual locks, collected refund entitlement, versioned demo approval rule, batch immutability and deterministic adapter states. These are design contracts, not implemented payment or insurer behavior. Plan 10-01 was refined to remove a premature unused C# contract type; actual DTOs belong to their owning implementation slices.

`docs/design/finance-source-ledger.json` pins the current decoded source SHA-256, the older inventory SHA-256 separately, every one of the 24 direct pAccounting control IDs, all 159 nonblank source lines from 2824–2989, three inherited controls and 11 explicit behavior obligations lacking active handlers. Source ownership is retained for Phase 4 agency, Phase 7 policy and Phase 11 admin callers. Every entry remains `planned`; no runtime binding or Phase 10 requirement is marked verified.

Verification: `node --test tests/finance-contracts.test.mjs tests/finance-source.test.mjs` passed 7/7, zero skips. The initial run failed on the differing old/current source hashes and a contract wording assertion; the ledger now records both hash identities without claiming equality, and the test checks the actual exact current line text. `git diff --check` passed. No SQL, API or browser suite was run for this documentation slice.

Next: 10-02 must implement additive finance posting/read projection while preserving existing insurance JournalLine guards and legacy first issues. It must turn planned source bindings into real saved-state proof before any requirement is completed. Retained demo database, file root and keys were not changed.
