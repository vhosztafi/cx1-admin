# Phase 1 research

Date: 2026-09-13
Status: Initial research complete; contract validation remains execution work.

Use ../../research/SUMMARY.md and ../../ARCHITECTURE.md for official-source storage/concurrency/OpenAPI findings. No changes to those recommendations are required by the approved scope.

## Implementation decisions

- SQL Server `nvarchar(max)` with JSON validation avoids dependence on native JSON feature availability. Version schema separately from insurance version numbers.
- Use published JSON Schema dialect and OpenAPI versions supported by the chosen validators; validate all examples automatically before accepting the contracts.
- Extract the bundled template deterministically, recording its SHA256 and method-local source evidence. Build a reviewed inventory above extraction; regex matches alone cannot establish complete behaviour.
- Read canonical funnel Documents 01–04 and clarifications in the owning order before mapping fields; inspect only relevant local code consumers. Preserve unknowns rather than inventing legacy semantics.
- Define error/precondition/idempotency contracts with concurrency examples. Require 401/403 or non-disclosing 404, 409, 412, 422 and 428 semantics explicitly.
- Model finance posting and temporal selection with worked fixtures before implementation; verify exact decimal arithmetic and policy-local date conversion.

## Risks addressed by plan order

Start with source/control inventory to prevent invented scope. Data/lifecycle design follows field evidence. API contracts follow entity/transition definitions. A final automated validation and traceability review checks all three together. Do not implement the application before this design phase passes.

## Environment

.NET SDK 10.0.401 is installed. Docker daemon was unavailable in the initial sandboxed preflight, although docker/sqlcmd clients exist. Phase 2 must inspect available SQL Server services or start an authorised isolated demo database; do not substitute in-memory persistence as a verified SQL implementation.
