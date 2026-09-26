# CX1 back-office development deployment

## Architecture

Browser → `https://cx1-admin-dev.gyongyos.co.uk` (Cloudflare Worker password gate) → individual back-office login → Worker proxy → `https://cx1-admin-api-dev.gyongyos.co.uk` (IIS) → `.\sql2022`, database `Cx1_Dev`.

The Worker protects all pages, assets and `/api` paths. Its password is a Cloudflare secret, never browser JavaScript. It issues a Secure, HttpOnly host cookie, throttles password attempts and disables caching. A separate random origin secret authenticates Worker-to-API requests; direct API visits, including health/login, return 404 without it. Individual application roles, MFA, CSRF and session controls remain enforced. Rotate the site password to invalidate site-gate cookies.

## Required on your shared server

- 64-bit Windows Server version supported by .NET 10, IIS, and the ASP.NET Core Module V2 from the .NET 10 Hosting Bundle. This package includes the .NET runtime but IIS still needs its hosting module. Install/repair the bundle in a maintenance window if it affects shared services; do not run `iisreset` for this app.
- SQL Server 2022 instance `.\sql2022`; migration operator with permission to create/migrate `Cx1_Dev`. Runtime uses the dedicated `IIS APPPOOL\Cx1AdminDev` account and only this database.
- An HTTPS certificate covering the API hostname, plus an appropriate Cloudflare DNS record pointing to your server, using Full (strict) TLS. Alternatively use Cloudflare Tunnel with a loopback IIS binding (see below).
- Persistent private storage for keys and documents, with backups covering the database, files and keys together. DPAPI keys belong to the IIS pool identity and server; do not copy workstation keys into this deployment or change the pool identity casually.
- A strong initial password for the fictional application accounts. This is separate from the site-gate password and origin secret.

## Fresh installation

1. Extract the ZIP into a private administrator-only staging folder. Verify its SHA-256 and the bundled manifest. The archive contains no workstation database, passwords, browser cookies, private keys or business files.
2. Open elevated **Windows PowerShell 5.1**. Run:

   ```powershell
   .\Configure-Iis.ps1 -CertificateThumbprint 'YOUR_INSTALLED_CERTIFICATE_THUMBPRINT'
   ```

   Enter `BACKOFFICE_ORIGIN_SECRET` from the private deployment credential file when prompted. The installer creates a dedicated 64-bit pool, restricts filesystem access, stores configuration in IIS-protected `web.config`, and leaves the new site stopped. It refuses an existing site/pool/app folder. No shared IIS settings or unrelated application pools are restarted.

3. Run database initialization under a SQL migration administrator:

   ```powershell
   .\Initialize-Database.ps1
   ```

   Enter the initial application password when prompted. Only `Cx1_Dev` is permitted. There is no reset/drop path. Existing passwords and records are preserved. Back up an existing `Cx1_Dev` before applying migrations. If using SQL authentication, supply the connection string through a local protected variable or secure operator procedure; do not paste it into shared logs.
4. As SQL administrator run `Grant-Runtime.sql`. It grants the dedicated local IIS virtual account read/write/execute on **Cx1_Dev only**, not `sysadmin` or `db_owner`. For a remote SQL instance, use an appropriate domain service account instead and adjust pool/SQL permissions deliberately.
5. In IIS start only `Cx1AdminDev`. Check `https://cx1-admin-api-dev.gyongyos.co.uk/health/live` returns 404 without the origin header. With the correct secret it should return 200; use a secure local tool and avoid saving its headers in logs.
6. Open the frontend, enter the site password, then sign in with `system-admin@cover.example` or another fictional role and the initial application password. Seeded accounts: `system-admin`, `underwriter`, `senior-underwriter`, `servicing`, `finance`, `agency-admin`, `admin-reviewer`, `agency-reviewer`, `finance-reviewer` at `cover.example`.
7. Verify an authorized saved record, report and logout, and verify unauthorized API/asset access is denied. Change application passwords and configure MFA as needed. The site gate is an additional shared-password boundary, not individual user identity.

The configured environment is **Staging**, with an explicit hosted-demo opt-in. Secure host cookies and private persistent paths are required. Demo rating/document/payment/claims workers are enabled, but adapters remain deterministic fictional services. The seed uses the documented frozen demo clock; changing clock/configuration should be deliberate. Fresh seed configuration does not import the old workstation's complete issued-policy/finance history.

## Optional Cloudflare Tunnel

Use `Configure-Iis.ps1 -Tunnel -TunnelPort 5096` instead of an HTTPS certificate when using a Tunnel. Configure the public API hostname to `http://127.0.0.1:5096`, set the Tunnel's HTTP Host header to `cx1-admin-api-dev.gyongyos.co.uk`, and keep the final ingress rule as `http_status:404`. The IIS listener is loopback-only; do not add a public HTTP binding. TLS terminates at Cloudflare. The origin secret is still required. Tunnel installation/token and routing are server-specific and are not included in this ZIP.

The Tunnel installer enables `Cover__TrustGatewayHttps`. The API accepts the gateway's HTTPS assertion only after validating its origin secret. Generic client forwarding headers cannot enable HTTPS; public IIS certificate installations leave this option disabled.

The Tunnel installer enables `Cover__TrustGatewayHttps`. The API accepts the gateway's HTTPS assertion only after validating its origin secret. Generic client forwarding headers cannot enable HTTPS; public IIS certificate installations leave this option disabled.

## Updates and rollback

Keep the same app-pool identity, data directory, keys and documents. Back up SQL/files/keys together first. Stop only this site/pool, keep the previous app folder, copy the new published `app` contents into a separate version folder and preserve the configured `web.config`. Apply explicit migrations with the operator account, point the site's physical path to the new folder, then start and smoke-test it. Never overwrite private data with package files. A binary rollback is safe only if its schema compatibility is known; otherwise restore the matched backup in a dedicated recovery procedure. Do not run the fresh-site installer over an existing installation.

## Worker deployment and remaining infrastructure

Worker sources/config live under `apps/backoffice/worker` and `apps/backoffice/wrangler.jsonc`. Build with `node scripts/deploy/build-worker.mjs`. Set Cloudflare secrets `SITE_PASSWORD` and `BACKOFFICE_ORIGIN_SECRET`; deployment fails closed when secrets are absent. The API origin is fixed to the requested hostname. `workers.dev` and preview URLs are disabled; static assets run through the Worker gate first.

The build creates an isolated source snapshot and records its app directory in `.local/deployment/worker-build.json`. Run Wrangler from that recorded directory, using its `wrangler.jsonc` and `.open-next` output. On Windows the build uses directory junctions when native symlink privileges are unavailable. Run `node node_modules/wrangler/bin/wrangler.js deploy` in the recorded directory. Upload the two secrets using Wrangler's `secret bulk` with the private `worker-secrets.json` file; never include that file in source control or the server ZIP.

The build creates an isolated source snapshot and records its app directory in `.local/deployment/worker-build.json`. Run Wrangler from that recorded directory, using its `wrangler.jsonc` and `.open-next` output. On Windows the build uses directory junctions when native symlink privileges are unavailable. Run `node node_modules/wrangler/bin/wrangler.js deploy` in the recorded directory. Upload the two secrets using Wrangler's `secret bulk` with the private `worker-secrets.json` file; never include that file in source control or the server ZIP.

API DNS target/certificate or Tunnel and the server installation must be supplied on the destination host. This package does not perform remote server administration. Do not claim the business application is online until the API, SQL and authenticated frontend smoke check pass.

References: [Cloudflare Next.js](https://developers.cloudflare.com/workers/framework-guides/web-apps/nextjs/), [OpenNext custom Worker](https://opennext.js.org/cloudflare/howtos/custom-worker), [Microsoft IIS hosting](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0).
