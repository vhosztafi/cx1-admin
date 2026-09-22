# Back office demonstration

The current demo covers the foundation and client servicing slices. Sign-in, account identity, client/contact/support servicing, duplicate intake review, diagnostic jobs, recovery and audit use persistent SQL data. Quote capture APIs are available with partial readiness; their business UI, policies, finance and configuration editing remain in progress. The complete insurance MVP remains in progress. The supplied sales funnel remains unchanged.

## Start the native Windows preview

Follow SETUP.md for pinned prerequisites and locked dependency restore. The verified SQL instance is `.\SQL2022`; the demo database is `CoverMGA_Demo`. From the repository root, build and initialize without resetting:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.local/dotnet'
dotnet build backend/BackOffice.slnx --no-restore
$env:COVER_DEMO_PASSWORD = [IO.File]::ReadAllText((Join-Path (Get-Location) '.local/demo-password.txt'))
try {
    dotnet backend/src/BackOffice.Api/bin/Debug/net10.0/BackOffice.Api.dll --initialize-demo
} finally {
    Remove-Item Env:COVER_DEMO_PASSWORD
}
```

The ignored password file exists on the original development workstation only. On a fresh workstation, set your own COVER_DEMO_PASSWORD as described in SETUP.md. Reinitialization preserves existing passwords and records; a different seed password does not change an existing account's password. No password belongs in source control or a presentation.

In one terminal, start the API and its Development diagnostic worker:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Cover__RenewalLifecycleWorkerEnabled = 'true'
dotnet backend/src/BackOffice.Api/bin/Debug/net10.0/BackOffice.Api.dll --urls http://127.0.0.1:5087
```

In a second terminal, build and start the web preview with the same API origin:

```powershell
$env:BACKOFFICE_API_ORIGIN = 'http://127.0.0.1:5087'
pnpm web:build
pnpm --filter @cover/backoffice start --hostname 127.0.0.1 --port 3100
```

Open `http://127.0.0.1:3100`. Keep these terminals open; stop only these processes with Ctrl+C when finished. Port 5087 avoids an unrelated listener found on 5080 on the original workstation. The API origin must be set before both frontend build and startup because the proxy rewrite is compiled into the build.

## Five-minute walkthrough

1. Sign in as `servicing@cover.example` using the local demo password. Refresh the page and open Manage account. The displayed name, email and role come from SQL. Business dashboard counts deliberately say they are unavailable.
2. Open Admin while signed in as servicing. Access is denied by the application and API. Log out, then sign in as `system-admin@cover.example`.
3. Open Admin > Integrations. Select **Successful delivery**, then **Run demo probe**. The latest probe moves to succeeded. Refresh jobs to see its saved receipt and attempt count.
4. Select **Provider rejection** and run another probe. It fails with `provider-rejected`; it cannot be selected for recovery because repeating a definitive rejection is not a valid retry.
5. Select **Timeout after provider success**. The first attempt records the timeout; the next succeeds after a short retry delay. The provider result is reused, with one local receipt. **Temporary failure, then success** demonstrates a separate transient failure scenario.
6. Open Admin > Audit log. Filter by `diagnostic.completed`, then clear the filter. Sign-in, inspection and command events are persisted. Ordinary audit rows contain reviewed summaries, not raw credentials, payloads or sensitive before/after values.
7. Refresh the browser, or restart the two preview processes with the same commands and OS account. Sign-in persists within its fixed eight-hour lifetime, and jobs/audit remain. Retain the API Data Protection key directory; deleting it invalidates existing cookies.

Browser refresh does not refresh the jobs table automatically; use **Refresh jobs**. The latest-probe panel polls until a terminal result. Dates display Europe/London; authentication and retry timing use real UTC independently of the frozen business demo clock.

## Recovery demonstration and checks

The four scenarios normally finish within one or two attempts. To exercise exhaustion and bulk recovery, run `pnpm web:browser:operations` against the running native preview. It inserts two new, explicitly fictional exhausted jobs and six simulated historical attempts per job, using only `.\SQL2022/CoverMGA_Demo`. It selects these jobs, supplies a reason and verifies recovery, including a deliberately lost successful response. It leaves fixtures and audit as demo history; it performs no reset or deletion. Repeated runs create new fixture IDs.

For a manual recovery demonstration, run `sqlcmd -S '.\SQL2022' -E -C -b -d CoverMGA_Demo -i scripts/seed-browser-recovery.sql`, refresh jobs and filter failed. Select the eligible fictional jobs, enter a reason and choose **Retry selected**. They receive a new six-attempt allowance while preserving prior attempts and provider identity. At most two extra cycles are permitted. Old definitive rejections remain ineligible.

`pnpm web:browser` checks the sign-in/shell journey. Both browser scripts require Chrome, use only localhost, and save screenshots under ignored `.local/browser-evidence`. Automated checks and agent visual review do not constitute business-user acceptance.

The diagnostics make no external calls, send no messages and move no money. Products are draft foundation definitions without rating rules. All seven seeded accounts are listed in SETUP.md. Docker and GitHub-hosted CI execution remain unverified; the native SQL profile is the demonstrated runtime.

## Clients, contacts and support

Sign in as servicing@cover.example. Open Clients, search by business, contact or agency and filter by entity type. Create a fictional client with a legal name and address, edit its identity and reload. The CN reference is assigned by SQL-backed application logic. On Overview, add a Brightside or Kingsway agency relationship. Activity shows the recorded time and staff identity. Quotes and their Activity links open actual saved records. Policies remain unavailable until issuing is implemented.

Open Contacts and choose an agency relationship. Add a contact with its declared full name, role and marketing consent (Given, Withheld or Not asked). The first active contact is primary. Add another contact and use Make primary to switch atomically. To end the current primary while others remain, first choose a replacement. Include ended contacts to inspect retained records. Details and consent belong to the selected relationship.

For the seeded client CN-0000003, select Brightside and a contact in Customer flags. Add a flag with functional internal wording, recording basis, review date and reason. Explicitly select sharing relationships and supply separate agency wording when needed. Consent declined clears the sensitive draft and does not save it. Amend, Review, History and End flag preserve an audit trail. The agency-safe preview shows only current explicitly granted instructions for that relationship. Internal agency-admin can inspect this safe preview but cannot inspect internal flag details/history. Browser-created historical flags remain visible, so the exact count may grow.

Run pnpm web:browser:clients, pnpm web:browser:contacts and pnpm web:browser:support for persistent lifecycle, uncertain-response retry, stale-edit recovery, access and mobile checks. These checks create or amend clearly fictional demo records without a reset. Desktop and mobile evidence is under .local/browser-evidence. Business-user UAT remains separate.

## Duplicate intake review

Sign in as underwriter@cover.example and open Clients > Duplicate reviews. The six MI-DEMO cases are fictional saved intakes across Brightside and Kingsway. Their current statuses reflect previous demo decisions; reinitialization preserves history. Open a case to inspect the captured comparison and Rule tabs, candidate account, submitting agency and actual decision trail.

For a pending or queried case, choose Link, Not duplicate, Decline or Request information and enter a reason. Link associates only the submitting agency with the candidate account. Not duplicate creates a separate account from the intake identity and reuses it on later decisions. Request information records the request and shows Recorded; it sends no message. A terminal decision offers Reopen, which retains its trail and prior association before another explicit decision. Reload to demonstrate persistence. Candidate contacts and support instructions remain separate.

The review has no quote, policy, rating or claims evidence yet. Those workflows are owned by later phases. Internal servicing and agency-admin accounts cannot see duplicate-review evidence. Automated local verification is available through pnpm web:browser:matches; it retains fictional decisions and screenshots without resetting the demo.

Agency distribution readiness now uses the current fictional agency-distribution SettingVersion, pinned to the three exact seeded product versions. Removing a product from a later rule closes its eligibility; future rules wait until their effective instant. Provider inactivity or expired product validity also blocks readiness. Products stay draft and unavailable for rating or issue. The additive agency-reviewer@cover.example account provides the independent agency-admin identity for independent activation, terms and access decisions. Activation is available after the full readiness checklist passes.


### Agency action indicators

The Agents directory now displays actual pending state, terms and access-request counts both globally and per agency. Directory filters affect rows; the four KPI cards remain labelled all-agency totals. Recorded follow-up obligations due by the displayed London date are shown separately. These are immutable reminder records, not open/completed tasks; completion tracking will be supplied with the task workflows.

`node scripts/verify-agency-kpis-browser.mjs` uses the previously verified lifecycle agency, creates a fictional permission request and has the independent reviewer reject it. It checks the corresponding increase/decrease in global and filtered-row counts, persistence after reload, request failure/retry and mobile containment. Existing demo records and decision history are preserved.


### Agency lifecycle walkthrough

Sign in as agency-admin@cover.example and open Agents. Create a new fictional agency and save each of the six onboarding stages. Reload or leave and resume to demonstrate saved drafts. Complete required declarations, product/commission choices, agreements, PI evidence and the staged administrator. Uploaded evidence and simulated verification results retain their own histories; changing relevant inputs invalidates readiness. Incomplete activation presents the outstanding checklist.

Request activation with a reason, then sign in as agency-reviewer@cover.example to approve independently. The requester cannot approve their own request. Approval creates the initial agreed terms, invitation and notification work atomically. Activity shows actual demo delivery outcomes; queued work is not labelled delivered. With the notification worker disabled for acceptance, new notifications remain queued until a worker processes them.

On Products/Accounts, inspect agreed versions and propose changed terms with explicit effective dates. Have the other administrator approve; historical versions remain available. Request and independently approve suspension, then reactivation, to demonstrate retained history and replacement of revoked pending invitations. Users supports invitation resend/revoke and user deactivation/reactivation with reasons. For local demonstrations only, an internal administrator can reveal an invitation link; acceptance sets a password before a separate sign-in. Agency users land on the access confirmation page with logout, without staff navigation.

Permissions records independent decisions and revocation. The internal sharing reference shows only the selected agency's clients, declared contacts and explicitly shared servicing instructions. Own-agency quote summaries are also available. Real policy, task, statement and bordereau workflows remain with their later phases; a product or permission grant does not manufacture those records.

Run `pnpm web:browser:agencies` for the sequential, fail-fast foundation and agency suite. Each run creates a fresh report under `.local/agency-suite/`; failed and unrun stages cannot count as passed. Terms display/proposal/review scripts use explicit intercepted UI fixtures for recovery and presentation; the separate lifecycle journey proves actual SQL activation and terms publication. All scripts retain fictional records and histories. Browser checks do not constitute human UAT.


## Fictional quote capture data

After the normal no-reset initialization above, seed the dedicated quote demonstration in Development:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet backend/src/BackOffice.Api/bin/Debug/net10.0/BackOffice.Api.dll --seed-quote-demo
```

The command is restricted to CoverMGA_Demo. It imports a clearly labelled fictional pre-approved agency/client/relationship/terms context (`AG-DEMO-QUOTES`, `CL-DEMO-QUOTES`), then creates eight quotes through the normal QuoteService: one incomplete and three captured examples for each Motor Trade product. It does not execute or certify the agency approval workflow; its request/decision/audit explicitly identify an imported fictional fixture. Existing draft agencies, permissions and business records are not activated or overwritten.

Repeated execution returns the same quote IDs with Created=0 and preserves later quote revisions. Suspended context, revoked access, changed configuration or missing context are not silently repaired. Each quote creation has its own durable command receipt, so an interrupted batch can resume. Complete captured examples still need real saved vehicle decisions and evidence before capture checks can pass; supplying declaration JSON alone does not provide those outcomes.

The command prints the agency/client/relationship and quote IDs. After starting the updated API and signing in as the demo underwriter, use GET /api/v1/quote-products?relationshipId=<printed relationship ID> and GET /api/v1/quotes/<printed quote ID> to inspect persisted capture. Open Quotes or the saved record to use the complete editor and history; the seed command itself is not browser or agency-workflow verification.

### Motor Trade quote draft creation

Sign in as `servicing@cover.example`, choose **New Quote**, then search for `CL-DEMO-QUOTES`. Select the active **Fictional Quote Demonstration Agency** relationship and either Motor Trade product. **Create quote draft** saves the selected relationship/product and opens the actual quote reference. Reload to show persistence. The dedicated fixture does not change the earlier draft agencies or their onboarding state.

Business/risk editing and quote search are available. These newly created drafts remain incomplete until their capture checks pass; rating and issuing remain unavailable. Reproduce the current creation/retry/access/responsive checks with `node scripts/verify-quote-create-browser.mjs` while the documented native previews are running; it adds two fictional drafts without resetting existing records.

### Initial quote editing

From a saved draft, choose **Edit quote draft**. Proposer details and initial business fields can now be saved and resumed. Record full proposer names independently of the separate source first/surname fields; remove empty slots explicitly. Turnover/wage roll use GBP decimal amounts, and the seven activity buckets use percentages. Invalid numeric text blocks saving. **Save draft** stays, **Save and continue** saves before advancing, and **Save and exit** returns to the saved record. Concurrent edits retain your local draft for comparison and require explicit discard to load the saved revision.

The editor contains all nine capture stages, including title/company category, consent/marketing, occupations, conditional questions and policy-term controls. `node scripts/verify-quote-business-browser.mjs` verifies editing, retry and concurrency against native previews without a reset.

### Requested policy term

In **Edit quote draft → Agency & product**, enter annual or short-period dates and London local times. An annual leap-day start uses28February for the next non-leap anniversary. Skipped spring clock times cannot be saved; repeated autumn times offer independent GMT/British Summer Time choices for start and end. Switching from a short period to annual retains its end fields until you explicitly remove them. Incomplete terms remain saveable drafts. Term checks feed current capture readiness; rating remains unavailable. Reproduce the saved term checks with `node scripts/verify-quote-term-browser.mjs` against the documented native previews.

### Proposer selections and consent

The quote editor now captures proposer title, source company category, quotation-data consent, marketing consent and contact methods using the saved quote’s catalogue version. **Record no…** records an explicit empty answer; **Clear… answer** returns it to unanswered. Removing marketing consent retains contact-method choices for explicit review. These answers belong to the quote and do not change client contact records or send messages. A missing or unsupported catalogue version makes these controls unavailable rather than substituting current options. Reproduce this slice with `node scripts/verify-quote-proposer-browser.mjs`; conditional business questions and dependent details are available in the same capture workflow.

Under **Trade activities**, add Motor Trade occupations and their turnover shares. Reorder or remove rows without changing their identities. Each occupation needs at least 1% and the total must be 100% for readiness; incomplete drafts can still be saved. Duplicate occupations and car-jockey combinations show readiness guidance. Invalid percentage text must be corrected or its row removed before saving. These shares are separate from the activity split above. Reproduce with `node scripts/verify-quote-occupations-browser.mjs`. The remaining sections and full capture-readiness assessment are available in the wizard.

**Business details** under Trade activities now records trade association membership/name, VAT registration/number, annual vehicles handled and the MIPD vehicle limit. Changing Yes to No retains entered details for explicit review; clear the text to remove it. Fractional vehicle counts block saving, while zero remains a recorded value with readiness assessed separately. Run `node scripts/verify-quote-business-answers-browser.mjs` to reproduce persistence and conditional-answer checks. Prototype trader and trade-declaration questions are described below.

**Motor trader details** on Proposer now captures full/part-time trading, experience, main occupation, employment status and other business/directorship. **Trade declarations** captures trading location, each source activity declaration and supporting details. These use the saved quote catalogue. Retained conditional details are visible and can be cleared explicitly; saving does not mean readiness. Reproduce all15 new controls with `node scripts/verify-quote-source-business-browser.mjs`. Readiness links and final source reconciliation remain pending.

**Saved draft readiness** lists issues from the saved revision. Choose **Review…** to open the relevant stage and focus its control. Save unsaved edits before using these links. General and later-section checks remain separate; a saved draft cannot yet be rated or issued. Business description and start date are now under Proposer, matching the prototype. Test with `node scripts/verify-quote-readiness-browser.mjs`.


## Driver and history capture checkpoint

For a saved Motor Trade quote, choose Edit quote draft, then Drivers or Claims & convictions. Add named drivers and record their independent name, address, licence, residence and cover declarations. Add occupations, motoring convictions, accidents/claims, criminal convictions and county court judgments. Rows can be edited, reordered and removed; Save draft persists a new immutable revision. Related vehicles/trips/history prevent removal that would leave an orphan reference.

Use the separate global declarations for the proposer and all named drivers. Saved readiness guidance links to specific available controls. An uncertain save retains the original request for Retry same save; a stale revision offers comparison and explicit discard. Young/inexperienced driver options derive from the recorded age, licence experience and cover limits. Changed circumstances retain earlier selections for explicit clearing. Missing context is explained rather than guessed. Saved capture readiness combines all section checks with current evidence, vehicle provenance, eligibility and matching. Rating and issue remain separate Phase6 work.


## Vehicles, portfolio and trade plates

Choose Vehicles & trade plates in either Motor Trade wizard. Add/edit/reorder vehicles, choose their register and ownership purpose separately, and explicitly select specified vehicles. Enter manual vehicle attributes, exact GBP values, kg/cc, independent manufacture/registration dates, overnight postcode and modifications. Named owners come from proposal drivers with personal cover. Retained owner/loss/specification links protect removal. No provider verification or MID submission is implied.

Expand Vehicle portfolio and proportions for categories and percentages. Expand Trade plates to maintain distinct held inventory and covered rows. Save draft retains incomplete declarations and shows field-linked guidance; changed circumstances never silently delete rows. Vehicle changes appear in stale-save comparisons. Reproduce the real two-product journey with `node scripts/verify-quote-vehicles-browser.mjs`. Current registration search is available in Quotes; history and comparison retain saved revisions.


## Premises, insurance, cover and declarations

All nine Motor Trade capture stages are now editable. Combined has a dedicated Premises stage; Road Risks has trading premises under Trade activities. Premises retain stable identities, source address/use declarations and, for Combined, separate declared use, security, sums insured, overnight and public access answers.

Use Previous insurance & NCD for Road Risks, or the previous-insurance subsection of Cover & excess for Combined. Policy expiry and NCB expiry are separate, as are exact years and at-least years. Cover & excess includes the source limits, dynamic own-vehicle excess, loan and optional covers, annual European vehicles and temporary trips linked to actual named drivers. Changing a controlling answer retains earlier details for explicit review.

Declarations & review captures each Yes/No answer and its explanation, plus material facts. Save/reload and stale-edit comparison preserve these sections. Readiness lists actual missing photocard/DVLA proof per driver, motor-trader proof and applicable discount proof; upload and attach fictional proof in the evidence panel. Capture checks do not themselves authorise issue; use the Underwriting tab for the current rating and referral assessment. Reproduce the two-product persistence, trip-error focus, retry and comparison journey with `node scripts/verify-quote-cover-browser.mjs`.


## Complete Motor Trade capture and discovery

Use Quotes to search current registrations, quote/client references, business names or agencies. Product, draft/withdrawn status, agency, requested start and updated order are available with paging. Lists refresh after versioned changes rather than silently skipping records. Client Overview, Quotes and Activity open actual quotes; Policies opens actual issued records and their immutable source snapshots.

The quote editor reports **Capture checks passed** only for a complete saved proposal with current eligibility, matching, actual attached evidence and recorded vehicle lookup/manual decisions. This is not a rate, bind or issue decision. Incomplete drafts remain saveable. Evidence files and attachments survive reload; changed relevant answers make evidence stale. Deterministic lookup scenarios make no external provider calls. Entering a registration alone does not verify a vehicle or submit it to MID.

Quotes with possible duplicate stored client identities create an underwriting matching review. Underwriter/senior-underwriter can link, keep separate, query, decline or reopen an editable draft using current review and quote versions. Earlier client ownership stays in revision history. Complete legacy client identity first and save again to perform matching. A withdrawn/closed quote rejects fresh matching decisions.

History compares saved values and ownership. Cloning creates new child identities and independently assesses destination matching; evidence and lookup decisions must be supplied again. Withdrawal retains the quote, reasons, evidence and history. Agency sharing exposes only own-agency reference, client name, product, status and dates; internal risk/evidence content remains private. There is no separate broker portal.

Run `pnpm web:browser:quotes` for sequential additive Motor Trade journeys and all20retained agency journeys. The readiness journey creates complete fictional Road Risks and Combined records with real persisted manual decisions and evidence. Reports/screenshots are local under `.local/quote-suite` and `.local/browser-evidence`. Recovery interceptions are labelled test fixtures, never provider or publication proof. Human business UAT remains a separate activity.

The final 2026-09-16 acceptance run created capture-ready **QT-MT-0000000257 (Motor Trade Combined)** and **QT-MT-0000000258 (Motor Trade Road Risks)** in the preserved native demo. Both are revision2 and remain fictional drafts. Sign in as the demo underwriter, search either reference in Quotes, inspect its saved Risk details/Cover/Drivers/Vehicles tabs and open the editor to see current capture checks. These local records are not automatically present in a fresh database; rerun the readiness journey there to create equivalent examples.

## Capacity referrals and carrier correspondence

Sign in as the demo underwriter and open a rated quote's Underwriting tab. Expand
**Refer to capacity provider** on the relevant referral, enter the reason and
create the escalation. Open it to compare the requested exposure with retained
binder limits and your current authority, then select an explicit fictional demo
scenario and submit a message. Refresh escalation to see the persisted provider
response and processing history. A queued submission is not an approval.

For a supplied carrier response, expand **Attach and review supporting proof**.
Upload the fictional letter, attach it to **Supplied capacity provider response**
for this exact submission and record an underwriting review. The response form
requires the actual underwriter, reference, received date/time, body and reviewed
letter. Approvals also require the exact permitted limit and validity interval;
conditional approvals use the same typed warranty/condition catalogue as the
quote. Carrier approval only extends its stated scope. Other referrals and proof
requirements still need attention on the parent quote.

**Return quote to draft** adapts the prototype's Reduce request/Remove change
controls for new business. Give a reason, revise the saved risk and obtain a new
rating. Previous submissions, responses and their provenance remain readable.
Servicing staff can inspect correspondence; recording and sending require current
underwriting authority. Failed transient jobs expose recovery only to an operator
with both integration recovery permission and underwriting authority.

The native demo's **QT-MT-0000000284** shows a supplied £150,000 stock response with
an overnight-security W-07 condition. **QT-MT-0000000285** demonstrates a queried
Road Risks escalation retained after return to draft. These are fictional records
created by the local Chrome journey, not fixtures guaranteed in a fresh database.
Reproduce with `node scripts/verify-underwriting-capacity-browser.mjs`; add
`--readback` to check saved correspondence and page-refresh recovery without new
business mutations. Screenshots/report are in `.local/browser-evidence/underwriting-capacity`.
Terms, acceptance and first policy issue are available as described below.


## Rate, accept and issue a Motor Trade policy

Use the demo underwriter account. On a complete Motor Trade quote, open Underwriting
and rate the saved revision. Inspect the actual component breakdown, rule/binder
versions, authority limits, referrals and current proof requirements. Review proof
and resolve each applicable referral; an attachment alone is not accepted evidence.
A new rating or a material change requires current reassessment.

Capacity correspondence supports Answer the query and Chase a response. Each is a
new explicit submission with immutable correspondence; old approval/acceptance no
longer applies. Withdraw an open request or reopen a concluded request to draft;
prior messages remain visible. Escalate internally assigns an active senior
underwriter and records the reason separately from carrier correspondence. Similar
past referrals show the latest ten actual same-agency/product/provider/rule cases,
with source links. Previous decisions do not confer authority on the new request.

Open Quotation, prepare the exact terms, attach/review the signed statement and
choose a current relationship contact. Demo delivery must reach Delivered before
acceptance. Attach/review acceptance proof, then record the named accepter, received
instant and channel for those exact terms. Review policy issue shows the client,
agency, cover dates and opening amount due. Confirming atomically creates the
policy, immutable version, transaction, obligation, balanced journal and three
requested documents. A lost response can be retried with the same saved command.
No payment is collected and document generation is still pending Phase 9.

Policies supports current reference/client/agency/registration search, product and
state/date filters, sort and paging. Client Policies and Activity open the same
records. The agency-sharing view exposes only safe own-agency policy summaries;
internal risk, evidence, rating and finance fields are not shared.

The preserved local demo contains PL-MT-0000000001 (Combined) and
PL-MT-0000000002 (Road Risks), both issued through the actual acceptance journey.
PL-MT-0000000005 is the final Combined carrier example. Further examples created
by the final suite retain the reviewed W-07
overnight-security condition, supplied carrier responses and internal action history.
They are local demo records, not guaranteed IDs in a fresh database.

Run `pnpm web:browser:underwriting` against the configured local API/Next previews.
It adds a fictional carrier case, reads both retained issued product examples,
checks discovery/sharing, and runs all 37 earlier quote/agency/client journeys.
On a fresh initialized demo, first run these additive journeys in order:
`node scripts/verify-underwriting-rating-browser.mjs`,
`node scripts/verify-underwriting-terms-browser.mjs`, and
`node scripts/verify-underwriting-issue-browser.mjs`. They produce the prerequisite
reports and two actually issued product examples. The final suite requires those
reports; retain them alongside the preserved local database. Reports live under `.local/underwriting-suite`
and `.local/browser-evidence`. UI failure fixtures are explicitly labelled and do
not count as persisted business outcomes. Restart capture/verify is a separate
operator step documented in SETUP. Human business/assistive-technology UAT remains
unperformed.

For Phase 7 servicing acceptance, run `node scripts/verify-servicing-suite.mjs`
after the underwriting suite. It runs all servicing stages sequentially, including
both products' editor, evidence, capacity, signed adjustment, renewal, cancellation
and history journeys. Set `COVER_SQL_TEST_CONNECTION` for the isolated SQL browser
cases, compile the backend first, and build both the normal Next output and the
isolated `.local/next-policy-history` output. Do not run multiple browser suites
against shared demo fixtures at once. Each aggregate run gets a new report directory
under `.local/servicing-suite`; a partial or failed report is not acceptance.
Each run first issues two fresh fictional policies through the normal quote,
proof, terms and acceptance APIs. Its `policy-bases/fixtures.json` is passed to
the servicing stages, so an already issued renewal never needs to be replaced.

After issuing both product examples, the API's local Development command
`--seed-servicing-drafts-demo` adds editable and leased cancellation drafts to
the earliest issued policy for each product. Where that term already has multiple
issued versions, it also adds a historical-base draft that demonstrates the
stale-base blocker. The JSON output contains scenario and draft IDs. Preserve it
as `.local/servicing-demo-drafts.json` and run
`node scripts/verify-servicing-demo-drafts.mjs` promptly to verify the initial
lease before it expires. The command serializes concurrent seeders, reuses its
original drafts, and never renews expired leases or reopens records changed by a
business user. It creates drafts through normal services and does not issue or
cancel cover. Richer servicing scenarios are separate from this draft seed.

Additional local examples use these sequential scripts after the prerequisite
underwriting and renewal journeys:

- `node scripts/seed-servicing-policy-bases.mjs`, followed by
  `node scripts/seed-servicing-cancellation-demo.mjs`, issues two dedicated
  fictional policies and their approved cancellation credits. Posted journals
  are checked for balance; no cash is paid.
- `node scripts/seed-servicing-lapse-demo.mjs` records manual non-renewal of the
  retained future terms, effective at their original expiry. Current cover is
  preserved and the deterministic notification outcome is stored.
- `node scripts/seed-servicing-underwriting-demo.mjs` retains missing trading
  proof, unresolved internal referrals, carrier query history and conditional
  carrier permission on an adjustment for each product. Issued cover is unchanged.

Reports and desktop/mobile captures are in `.local/servicing-*-demo-v1`.
Keep each directory and its command journal with the database: the journal
reuses exact idempotency keys and requests after an interrupted response.
Scripts stop on validation or concurrency conflicts; they do not bypass leases
or reset records. A stale `running.lock` requires checking its recorded process
has ended before removing it. Do not run seeds while business users edit their
example records. These scripts use normal authenticated APIs and fictional
documents, with no real carrier or customer delivery.

Use one explicit, persistent encryption-key directory for the API and every demo
CLI process. From the repository root in PowerShell, set
`$env:Cover__DataProtectionPath = Join-Path (Get-Location) '.local/data-protection'`
before starting either. Different working directories otherwise select different
default key directories and can make retained invitation payloads unreadable.
Preserve existing keys; never replace or delete them to repair a demo. Also set
`BACKOFFICE_API_ORIGIN` before building Next, since its API rewrite is compiled
into the build. Changing only the web server's runtime environment is insufficient.

The servicing preservation check covers all database tables, including migration
history, with ordered row-count/SHA256 fingerprints. Capture policy API readback
with `node scripts/verify-servicing-restart.mjs capture .local/UNIQUE-RESTART-RUN`,
stop only verified owned demo API/web processes, then run
`./scripts/verify-servicing-preservation.ps1 -EvidenceDirectory .local/UNIQUE-SQL-RUN`.
Pass `-SqlCommand` with the absolute SQLCMD executable path if needed. This runs
initialization twice without reset and rejects any retained table change. Restart
with the same database/key configuration, then run
`node scripts/verify-servicing-restart.mjs compare .local/UNIQUE-RESTART-RUN`.
The API comparison retains every version, financial and history field except the
two request-time cutoffs on explicit-version reads; history cutoffs are pinned.
Record the verified old/new process identities alongside these reports. A repeat
read without a process restart only verifies the comparison harness.

## Commercial proposal preparation

The additive commercial proposal command requires an existing fictional client–agency relationship with approved access to the commercial product version. Obtain both IDs from its normal quote/product readback. Run with the same local SQL configuration and persistent keys as the demo:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project backend/src/BackOffice.Api --no-launch-profile -- --seed-commercial-proposals-demo --relationship-id <relationship-guid> --product-version-id <commercial-product-version-guid>
```

The command is restricted to `CoverMGA_Demo`. It returns five scenario names, quote IDs and references. All proposals start on1November2026 and contain two fictional premises. They cover a normal two-location proposal, flood referral, outside-appetite activity, conditional capacity and a second capacity contender. The last two require actual carrier review; neither is labelled approved by initialization. The current senior underwriter must still have the authority required for subsequent decisions.

Rerunning discovers the original scenarios and preserves business edits, existing quotes, configuration, grants and issue history. It does not renew a lease, rerate an edited proposal or create a replacement for a withdrawn record. Access withdrawal is authoritative even on a repeat run.

Open the returned quote reference in the back office and use the normal rating, evidence, referral, capacity, terms, acceptance and issue controls. Issued commercial documents currently show requested work. Their stored content is pinned to the exact version; generation and delivery belong to Phase9.

### Commercial lifecycle orchestration

Save the proposal command's JSON array to `.local/commercial-proposals.json`. With the local API/web running, use:

```powershell
$env:COVER_WEB_ORIGIN = 'http://127.0.0.1:3100'
$env:COVER_COMMERCIAL_DEMO_DIRECTORY = '.local/commercial-lifecycle-demo-v1'
node scripts/seed-commercial-lifecycle-demo.mjs .local/commercial-proposals.json full
```

The script signs in as the existing senior underwriter using `.local/demo-password.txt`. It requires current commercial authority, an eligible contact and the normal demo rating, capacity, terms and cancellation workers. It never grants itself authority. For separate local API/web origins, set `COVER_COMMERCIAL_DEMO_API_ORIGIN` to the API origin. Both origins must be loopback; API port5000 is rejected. Keep the journal directory with its database and rerun the same command after an interrupted response. Stop business edits while preparing these fictional examples; validation and concurrency conflicts require review, not replacement of retained records.

For the initial local demonstration only, `dotnet run --project backend/src/BackOffice.Api --no-launch-profile -- --seed-commercial-authority-demo` explicitly creates a missing grant from the existing active system administrator to the active senior underwriter for published commercial v3. It is restricted to Development and `CoverMGA_Demo`, and checks both current roles and current published authority before discovering a retained grant. An existing revoked or expired grant stays unchanged. This command is never part of application startup, repeat initialization or proposal/lifecycle preparation. Agency adoption of commercial v3 still uses its normal approved terms workflow.

The supported stages are `issue`, `adjustment`, `renewal`, `cancellation`, `scenarios` (issue plus the separate referral/capacity examples), and `full`. The full sequence creates one issued two-location policy, increases its first location's stock by£111.11 effective14days after inception, renews from that final expiring version with explicitly reviewed fictional experience, and cancels the renewal30days after its inception. Cancellation posts a credit with cash paid£0.00 and releases exposure at its effective date. The original policy and adjustment remain in history.

Other proposals remain available as a flood referral, an actual declined risk, a conditional demo-carrier response with outstanding proof, and a district-capacity contender. The contender has£39,900,000.01 buildings at the first premises: its proposed district exposure is within£40m by itself, but the issued base consumes enough shared headroom to produce a real book-capacity warning. It is not issued or presented as a capacity reservation. Existing or edited scenarios are preserved; an older contender recipe requires explicit review rather than automatic overwriting.

Actual IDs and references are written after scoped API readback to `issued-base.json`, `adjustment.json`, `renewal.json`, `cancellation.json`, and `referrals-and-capacity.json` in the journal directory. These reports distinguish queued document work, actual persisted demo correspondence and posted finance. No real customer/carrier transmission, generated Phase9 document or cash refund is claimed.

The complete normal-API lifecycle and all scenarios passed twice in the08-15 acceptance test and twice in the retained local demo. The database upgrade preserved all56,867 preexisting rows, both encryption keys and the Motor Trade policy graphs after actual restart. The explicit commercial staff grant was applied after the user approved that exact local privilege change.

For a fully approved fictional agency, `node scripts/prepare-commercial-demo-context.mjs <agency-id> <commercial-v3-id>` prepares the context through normal APIs: one operator proposes the added commercial product, a different operator approves it, and a dedicated client, relationship and contact are created. Prior terms snapshots remain unchanged. The command pins its selection and exact requests in the same persistent demo directory; a repeat retains the existing records. It neither activates an agency nor creates a staff grant.

The retained example uses agencyAG-0000154, clientCN-0000197 and commercial policy [PL-CC-0000000025](http://127.0.0.1:3100/policies/91964a43-2afe-45ee-b01e-06cfcd717795). The original policy starts1November2026; its adjustment takes effect15November2026, renewal starts1November2027, and cancellation takes effect1December2027 at09:00UTC. Cancellation posted a£1,695.88 credit with cash paid£0.00.

| Example | Retained quote |
| --- | --- |
| Issued two-location policy | [QT-MT-0000000691](http://127.0.0.1:3100/quotes/bd91bf9e-60fc-4797-ae91-bf7b64bc4647) |
| Flood referral | [QT-MT-0000000692](http://127.0.0.1:3100/quotes/1c72cb31-9e24-432b-af71-236399e8cf5b) |
| Declined outside appetite | [QT-MT-0000000693](http://127.0.0.1:3100/quotes/6f3ff65e-760f-474b-8780-6263478e323f) |
| Conditional demo capacity | [QT-MT-0000000694](http://127.0.0.1:3100/quotes/bb215589-d27d-43d6-b077-de98e3a1cc69) |
| District capacity contention | [QT-MT-0000000695](http://127.0.0.1:3100/quotes/1b695b03-5cbf-443c-b48a-972b61535eb8) |

These are Commercial Combined quotes; their opaque references retain the existing shared `QT-MT` numbering format. The exact IDs, versions, consequences and current scenario readbacks are in `.local/commercial-lifecycle-demo-v1/references.json` and its Markdown companion. Keep that directory with the database.

## Phase 8 acceptance — 21 September 2026

Commercial capture, underwriting, issue, all nine adjustment editor groups, renewal and cancellation passed the full local acceptance suite. The current complete backend gate passes1,511 unique tests, including383 real-SQL scenarios, with no skips; all420 discovered integration cases executed. Retained MotorTrade underwriting and all17 servicing stages also pass. See .planning/phases/08-commercial-combined-back-office/08-VERIFICATION.md for exact artifacts and exclusions.

Two additive initializations preserved139 table fingerprints. Actual preview restart preserved three policy graphs/12 versions, five pinned commercial exposure readings and the original keys. The retained business references above are unchanged. Documents are queued content; rendering/delivery and incident logging remainPhase9. Posted cancellation credit is not proof of a cash refund. Human business/assistive-technology UAT remains to be performed.

## Phase9 retained operational examples (partial acceptance)

The retained preview now uses the Phase9 API on5087 and web on3100, with the original data-protection keys. Process identities are recorded in `.local/phase9-16-preview-pids.json`; verify identities before stopping a preview. Its private generated files are in `.local/operational-files`. The Phase9 upgrade and additive setup preserved all68,356 original rows and both key files; two repeat foundation initializations preserved all176 current table fingerprints. Read-only upgrade evidence is in `.local/phase9-16-retained-upgrade`; initialization evidence is in `.local/phase9-16-two-initializations`.

`--seed-operational-demo` is a Development-only, missing-only setup command for CoverMGA_Demo. It connects retained matching information requests and renewal lapse notices to internal correspondence. Existing recorded/sent legacy states and receipts remain unchanged; this does not resend a historical notification. The retained run associated31 matching requests and2 lapse notices. Links open the actual agency or policy Messages tab.

A separate policy **PL-CC-0000000033** starts21September2026 so it can support a historical commercial incident. The original **PL-CC-0000000025**, including its future renewal/cancellation, is preserved. `--prepare-operational-commercial-demo --relationship-id <existing-id> --product-version-id <published-id> --starts-on YYYY-MM-DD` prepares its missing quote using the existing senior underwriter and normal quote service. It does not grant authority or issue a policy. The first-revision scenario marker preserves later staff edits. Issue uses `node scripts/seed-commercial-lifecycle-demo.mjs <fixtures.json> issue operational-incident` with `COVER_COMMERCIAL_DEMO_DIRECTORY` set to a separate local journal. Keep `.local/operational-commercial-demo-v1` with the database; its journal retains normal rate, proof, referral decision, terms, acceptance and issue commands.

Two fictional incidents are now available under each policy's Claims tab:

- **INC-0000001**, Motor Trade **PL-MT-0000000012**, occurrence20September2026 at12:00 London.
- **INC-0000002**, Commercial Combined **PL-CC-0000000033**, occurrence21September2026 at12:00 London.

Both resolve to the exact issued historical version, have one acknowledged deterministic administrator handoff, and retain separate Notified and Open summaries. Paid/reserve remain unknown, displayed as “Not advised”. No real administrator was contacted. Open the incident, then **Summary from administrator** to inspect these records.

`node scripts/seed-operational-incidents-demo.mjs <fixtures.json>` accepts two explicit `{policyId,occurredOn}` fixtures. It uses normal scoped APIs and a persistent local command journal; rerunning preserves the same incidents, revisions, handoffs and summaries. Retained fixtures are `.local/phase9-16-incidents-fixtures.json`; reports and desktop/mobile captures are in `.local/operational-incidents-demo-v1`. `scripts/verify-operational-incidents-readback.ps1` independently checks SQL provenance and exact counts. Both first and repeat browser runs and SQL readback passed. Keep these journals; removing them is not a supported reset.

Phase9 is still in progress: current full browser/source reconciliation and final full-phase verification remain unfinished. These examples do not constitute human business UAT.
### Retained failure and retry examples

The Commercial Combined policy's Documents tab contains the **Fictional operational retry demonstration pack**. Its agency-visible copies were generated explicitly from the original issued source and template hashes; the original internal PDFs were preserved. Open **View delivery and attempts** to inspect six transient failures and the seventh successful attempt. The same delivery, recipient and three exact PDF versions survived retry. The exception is task `6b5e4e97-e11c-4cfc-97c6-9a5737b55a66`.

Motor Trade **PL-MT-0000000012 → Vehicles → MID submissions** shows the corresponding accepted initial submission. Its six failed attempts, seventh accepted attempt, original version and exception task `47a1d24b-3c24-4922-9bfe-6a0b2bbb9184` remain in history. This demonstrates recovery from a transient failure; a definite rejection is not retried.

Both examples use real hosted lease/provider/application processing against persistent deterministic adapters. To avoid waiting through the long demo backoff, `scripts/wake-operational-demo-retries.ps1` brought forward only these two recorded pending jobs' next-attempt times. It never changed attempts, results, payloads, permissions or provider receipts. The schedule records disclose that time compression. The browser then used the actual Retry controls. Independent SQL verification proves one exception task and provider operation per original job, seven actual attempts each, and three unchanged PDF hashes. Reports and screenshots are in `.local/operational-pack-demo-v1`; run `scripts/verify-operational-retries-readback.ps1` for the independent readback.

Setup tools are explicit local demo commands, not application startup:

- `--register-operational-mid-demo --version-id <existing-issued-motor-version>` uses current senior-underwriter scope and the normal missing-only initial-intent registration service.
- `scripts/operational-retry-demo-settings.ps1 -Mode Prepare` appends immutable retry-required settings for the bounded setup window; `-Mode Restore` appends the previous success values. Keep its journal and always restore in `finally`. Existing work retains the selected scenario. The retained defaults have been restored.
- `node scripts/seed-operational-pack-demo.mjs prepare` creates explicit agency copies from exact retained source/templates. `queue` uses its saved command journal; `read` only inspects the same pack. Do not delete journals to rerun setup.
- `node scripts/verify-retained-operational-retries.mjs` opens the real exception tasks and retries only while the original operations are failed; subsequent runs verify the completed operations without creating a resend.

The three downloaded documents independently parse as a 13-page schedule, 12-page statement of fact and 2-page certificate. Schedule first/last and certificate final pages, plus desktop/mobile retry views, were visually inspected. This is engineering verification, not human business or assistive-technology UAT.
