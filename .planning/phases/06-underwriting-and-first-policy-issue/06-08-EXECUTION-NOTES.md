# 06-08 execution notes

The quotation backend is complete in0ba6132. The full regression gate passed
799cases/144realSQL with zero skips in.local/phase6-08-backend-full; integration
duration30m20s. See06-08-SUMMARY for final evidence.

New paths refine the planned responsibilities: QuoteTermsContext handles shared
current eligibility/payload; QuoteTermsReadModel provides scoped paged history and
safe template/contact options; QuoteTermsSeed adds two immutable demo templates
and a versioned server-selected delivery scenario; QuoteDeliveryJobs and dispatcher
wire durable recovery/application. ActorContext already contained the exact named
quote-terms and quote-acceptance capabilities, so no redundant role edit is needed.

Applied migration: 20260917013041_QuoteTermsDeliveryStorage. Its forward script is
.local/phase6-08-migration-forward.sql. Fresh and retained-upgrade tests passed;
legacy storage fixtures now explicitly insert old-schema cycle columns before
upgrading. This preserves the earlier migration tests as the EF model grows.
The first demo application matched all 23 retained count/hash sets, including
credentials, quote/rating/decision/evidence/capacity history. Repeated initialization
is recorded separately in .local/phase6-08-demo-repeat.log and preservation-repeat.

Measured checks so far: 14 new pure acceptance cases; targeted SQL 11/11; expanded
SQL 17/18 with only an API fixture/read issue, subsequently corrected; new warranty,
expiry and runtime-version cases 3/3; final strict HTTP test 1/1. Current contract/
source suite 317 passed and OpenAPI is valid with the prior 10 warnings.

Tests exposed and corrected array-vs-object JSON constraints, binary-collation
operation key case, detached quote state persistence and disposed JSON projections.
Quote history cursors now bind to quote/delivery versions, avoiding unrelated
@@DBTS session/job churn; current record authority is still rechecked first.

The server distinguishes prepared terms, queued/delivered output and actual
acceptance. Reviewed exact signed-statement proof is required before send;
reviewed exact acceptance proof exists before hashing assurance. Documentary
review is excluded from contractual payload, preventing the signature loop.
Warranty changes create new terms, signature, delivery and acceptance requirements.
Prepared/delivery/acceptance history stays immutable. No external transmission,
payment, deployment, reset, sales-funnel edit or policy issue is claimed.

Next: execute06-09 UI and actual Chrome journeys.
