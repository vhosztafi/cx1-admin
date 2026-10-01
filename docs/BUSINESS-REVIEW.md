# Hosted business review examples

The fictional review data belongs to the `cx1-admin-dev` staging back office. The connected `cx1-dev` demo funnel saves a Road Risks draft through the reviewer's existing back-office session after both updated sites are deployed.

## Submit from the funnel

Complete the Motor Trade journey at `https://cx1-dev.gyongyos.co.uk/` and select **Submit**. Keep the opened admin window available; sign in with an internal servicing or underwriting account if prompted. The admin window uses the approved Fictional CX1 Review Traders client and Fictional CX1 Review Agency relationship, creates a Road Risks draft, and attaches `cx1-funnel-answers.txt` with the complete submitted answers. The draft includes directly compatible proposer, contact, business and start-date fields. Review the remaining answer mapping and quote readiness in the admin record before rating or issuing. If attachment fails, use **Retry same save** in the admin window; the draft link remains visible.

## Create a quote

Sign in to `https://cx1-admin-dev.gyongyos.co.uk/` as a servicing or underwriting user. Open **Quotes → New Quote**, search for the fictional CX1 Review Traders client, select its active Fictional CX1 Review Agency relationship, and choose either available Motor Trade product. **Create quote draft** must open a saved `/quotes/{id}` record. Other seeded demo agencies are drafts and are not eligible for quote capture.

For the seeded **Fictional Demo Traders 07** client (`CN-00000007`), select the **Fictional CX1 Review Agency** relationship. This relationship was added for business review on 2026-10-01 and offers Motor Trade Combined and Motor Trade Road Risks. The original Fictional Kingsway Agency relationship remains visible but its agency is still in draft, so it cannot offer quote products. A direct entry link is `https://cx1-admin-dev.gyongyos.co.uk/quotes/new?clientId=32000000-0000-4000-8000-000000000007&relationshipId=daf740a6-f134-4d29-bccb-2c0c57959dee`.

The hosted Worker must preserve the API's strong ETag on read and write responses. API traffic requests identity encoding and is served with `Cache-Control: no-transform`. A weak `W/"..."` tag cannot be used as `If-Match` and makes successful creation appear unconfirmed in the browser.

## Issued policy examples

From the repository root, set `COVER_SITE_PASSWORD` and `COVER_APP_PASSWORD` privately in the current process, then run:

```powershell
node scripts/seed-hosted-review-policies.mjs
```

The script uses the existing fictional `agency-admin`, `agency-reviewer`, `servicing`, and `underwriter` accounts. It creates and independently approves one agency, creates one client relationship and two Motor Trade quotes, then uses normal lookup, rating, reviewed proof, terms delivery, acceptance, and issue APIs. It writes only fictional evidence. It prints policy links when both are issued. The local ignored `.local/hosted-review-policies-v1/commands.json` journal retains exact idempotency keys and requests; keep it with the database and rerun the same command to resume after interruption. Do not run two copies concurrently or delete `running.lock` until its recorded process is confirmed stopped.

The issued policies are bases for MTA, renewal, cancellation, finance, and operational demonstrations. Business users should start separate servicing drafts for separate scenarios so one completed action does not consume another example's current term.

The staging review seed completed on 2026-10-01:

- Motor Trade Combined: `https://cx1-admin-dev.gyongyos.co.uk/policies/74f0f2ce-601b-490a-abbc-a8e70258688a`
- Motor Trade Road Risks: `https://cx1-admin-dev.gyongyos.co.uk/policies/c8623175-d43a-4fe8-a6bb-2bfdcf8722d5`

### Worker prerequisite

On the hosted API, `Cover:HostedDemoEnabled` and `Cover:QuoteLookupWorkerEnabled` must both be true. If a quote lookup remains `pending` with `workState=pending` and `attempts=0`, the lookup dispatcher is not claiming jobs. Check the effective IIS `web.config`, `appsettings.json`, `appsettings.Staging.json`, and service logs. Resume the script after the dispatcher is running; the existing lookup and command journal are retained. Do not issue policies by inserting SQL rows or bypassing the normal underwriting checks.
