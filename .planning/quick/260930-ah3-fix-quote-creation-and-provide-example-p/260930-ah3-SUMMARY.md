---
status: complete
date: 2026-10-01
---

# Hosted business review progress

- Fixed the Cloudflare Worker API path to preserve strong ETags; deployed version `765e6250-3052-4817-b0de-04e266de313c` and verified the live API now returns a strong tag with `no-transform`.
- Created and independently approved a fictional review agency, linked a fictional client, and saved two Motor Trade quotes through authenticated hosted commands. A separate servicing browser flow successfully created another draft and opened its saved record.
- Added `scripts/seed-hosted-review-policies.mjs` with a durable ignored command journal. Issuance has paused at a hosted vehicle lookup whose work is still pending with zero attempts; the API's `QuoteLookupDispatcher` has not claimed it.
- On 2026-10-01, business showed `CN-00000007` with a Kingsway relationship marked active but no selectable products. Live `quote-products` confirmed Kingsway's agency remains draft. Added a second relationship for that client to the independently approved review agency through the hosted API. Both Motor Trade products are eligible there, and a hosted browser created saved quote `7c68532a-fb6b-49ef-8822-b964bf8b1cf7`. The quote picker now renders the API's exact `unavailableReason` for ineligible products.
- The hosted lookup worker resumed. The seed used a fictional Combined limit within the stored senior authority, added a fictional relationship contact for terms delivery, and issued both examples through normal underwriting and issue commands: Combined policy `74f0f2ce-601b-490a-abbc-a8e70258688a` and Road Risks policy `c8623175-d43a-4fe8-a6bb-2bfdcf8722d5`. The durable command journal remains in `.local/hosted-review-policies-v1/commands.json`.

## Verification

- .NET solution build passed before the unused API seed command was removed.
- Gateway test: 13 passed.
- Worker production build and deployment passed.
- Live gateway strong ETag and browser quote creation passed.
- `node --check scripts/seed-hosted-review-policies.mjs` passed.
