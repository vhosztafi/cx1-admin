# Hosted business review examples

The fictional review data belongs to the `cx1-admin-dev` staging back office. The separate `cx1-dev` funnel is a mock preview that ends before quotation transport.

## Create a quote

Sign in to `https://cx1-admin-dev.gyongyos.co.uk/` as a servicing or underwriting user. Open **Quotes → New Quote**, search for the fictional CX1 Review Traders client, select its active Fictional CX1 Review Agency relationship, and choose either available Motor Trade product. **Create quote draft** must open a saved `/quotes/{id}` record. Other seeded demo agencies are drafts and are not eligible for quote capture.

The hosted Worker must preserve the API's strong ETag on read and write responses. API traffic requests identity encoding and is served with `Cache-Control: no-transform`. A weak `W/"..."` tag cannot be used as `If-Match` and makes successful creation appear unconfirmed in the browser.

## Issued policy examples

From the repository root, set `COVER_SITE_PASSWORD` and `COVER_APP_PASSWORD` privately in the current process, then run:

```powershell
node scripts/seed-hosted-review-policies.mjs
```

The script uses the existing fictional `agency-admin`, `agency-reviewer`, `servicing`, and `underwriter` accounts. It creates and independently approves one agency, creates one client relationship and two Motor Trade quotes, then uses normal lookup, rating, reviewed proof, terms delivery, acceptance, and issue APIs. It writes only fictional evidence. It prints policy links when both are issued. The local ignored `.local/hosted-review-policies-v1/commands.json` journal retains exact idempotency keys and requests; keep it with the database and rerun the same command to resume after interruption. Do not run two copies concurrently or delete `running.lock` until its recorded process is confirmed stopped.

The issued policies are bases for MTA, renewal, cancellation, finance, and operational demonstrations. Business users should start separate servicing drafts for separate scenarios so one completed action does not consume another example's current term.

### Worker prerequisite

On the hosted API, `Cover:HostedDemoEnabled` and `Cover:QuoteLookupWorkerEnabled` must both be true. If a quote lookup remains `pending` with `workState=pending` and `attempts=0`, the lookup dispatcher is not claiming jobs. Check the effective IIS `web.config`, `appsettings.json`, `appsettings.Staging.json`, and service logs. Resume the script after the dispatcher is running; the existing lookup and command journal are retained. Do not issue policies by inserting SQL rows or bypassing the normal underwriting checks.
