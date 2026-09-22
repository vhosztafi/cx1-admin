#09-14 reviewed and accepted

No unresolved HIGH/CRITICAL finding in current reviewed diff. Verified original-version/
predecessor ownership, current authority before replay/provider/apply, no CC, exact original
queue identity, late-result isolation, lease/inbox/duplicate handling, immutability/rollback,
not-required semantics and rejection versus uncertain retry. No current policy pointer is
used to rebuild old requests. UI frozen commands validate original job receipt and block
navigation until confirmed; generated DTOs include actual nullable result/attempt fields.

Fixed: work configuration saved before immutable submission insert within the transaction;
correct actual motor product codes; revoked test used EF table mapping; expression-tree test
captured latest version ID; servicing test now uses actual vehicle/cover-change fixture.
Dispatcher catches original policy scope failures as a persistent source exception instead
of endlessly retrying registration. Earlier failing/diagnostic runs are not acceptance.

Accepted1306unique/10SQL,420root/200frontend,9browser+SQL, build/typecheck/lint/OpenAPI. See
SUMMARY for exact artifact paths. Retained demo/full source/human UAT remain explicitly pending.
