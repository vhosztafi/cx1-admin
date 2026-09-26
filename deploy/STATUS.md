# Development deployment — 26 September 2026

- Frontend deployed: https://cx1-admin-dev.gyongyos.co.uk, Worker `cx1-admin-dev`.
- Live password gate checks passed for pages, static JavaScript and API paths. Correct password opens the application login page (HTTP 200). Unauthorized requests return HTTP 401.
- API package: `artifacts/cx1-admin-api-dev-20260926-150503.zip` (self-contained Windows x64). Its adjacent `.sha256` and bundled manifest verify the archive and 398 published application files.
- IIS configuration targets `.\sql2022`, database `Cx1_Dev`. Dedicated pool, persistent keys/files, private origin secret, and optional loopback Cloudflare Tunnel configuration are included. Installation instructions: [Windows README](windows/README.md).
- API installation/DNS is pending. The live authenticated API proxy currently returns Cloudflare HTTP 530. Application login and business journeys cannot be verified on the hosted environment until the API is installed and routed.
- Private credentials are in `.local/deployment/cx1-admin-dev/credentials.json`, restricted to the local user, sandbox account and SYSTEM. The Worker has its two secrets configured. Credentials are excluded from Git and the server ZIP. The password-gate implementation matches the sibling project, with independent credentials and cookies.
- Verification: 12 gateway tests, 13 API boundary tests, frontend ESLint and production TypeScript/build, Wrangler dry run, live HTTP gate/asset checks, PowerShell parse checks and package manifest checks passed.

No remote Windows administration or database initialization has been performed. Existing workstation databases and previews are retained. v1.0 business/human verification limits remain recorded in the milestone audit.
