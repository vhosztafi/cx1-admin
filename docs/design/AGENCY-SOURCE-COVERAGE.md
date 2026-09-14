# Agency source and delivery ownership

Phase 4 contract review, 04-01. This is design coverage; runtime acceptance is still pending. The immutable control inventory and reviewed API map retain individual CTL IDs. The rows below also cover implied reads and dynamic actions absent from fixed prototype rows. No separate broker portal is commissioned.

| Source surface | Functional mapping | Delivery owner |
|---|---|---|
| pAgents search/status/relationship manager, rows and Create agency | listAgencies/getAgency, scoped manager discovery, real getAgencyKpis; new form creates only on save | 04-02 |
| pNewAgency all six stages, Back/Continue/step buttons/Save draft/Save and exit | createAgencyDraft/saveAgencyDraft/replaceAgencyProducts; persist before navigation, preserve exact uncertain request for retry | 04-02 |
| Stage 1 legal/trading/entity/company/addresses/FCA/principal/client money/arranging/territory | AgencyWrite; all fixed choices have source-exact agency-option-mapping and option-targets fixtures; separate address mode/value | 04-02 |
| Stage 2 main/compliance/accounts/complaints contacts, manager, correspondence and office hours | Strict partial contact declarations, stored manager IDs, email/email-and-post/portal-only | 04-02 |
| Stage 3 product toggles/commission/effective date and commercial modes | Three-product catalog and draft product DTO; bps, fee mode/split, volume mode/target, override mode/value, standard internal referral only | 04-02, agreed updates 04-06 |
| Stage 4 TOBA/version/signature, PI/expiry, financial/sanctions/ownership/DPA and required files | Draft declarations; uploadAgencyEvidenceFile/downloadAgencyEvidenceFile/recordAgencyEvidence/runAgencyCheck/listAgencyChecks/validateAgency; immutable provenance and current fingerprint/rule | 04-03 |
| Stage 5 terms/statement cycle/limit/method/collector/commission settlement | AgencyWrite typed source choices, complete AgencyTermsWrite on approval | 04-02/04-06 |
| Stage 6 checklist/summary/completed-by/countersign/provider notice | Server checklist; actor from session, independent saved request decision, real notification state; no selectable approver bypass | 04-03/04-04/04-06 |
| Abandon draft, post-activation Open agency/Create another | Retained abandoned identity/invitation history; actual approved state; fresh unsaved form never resets previous agency | 04-02/04-06 |
| aguser name/email/role/Add to invitation list and staged Remove | AgencyInvitationWrite staged without issuance; revokeInvitation preserves history; broker-admin/user/readonly only | 04-05 |
| invite name/email/role/Send invitation; agency header/users Invite/Resend | Same typed role DTO; active issuance, fresh token on resend, 14 real-day expiry; staged and notification states separate | 04-04/04-05 |
| pAgency blue header/Overview/Users/Products/Permissions & access/Accounts/Activity | Real detail/user/products/terms/grants/activity reads; accounts explicit unavailable section until finance | 04-02/04-06/04-07; accounts 10 |
| User Edit/Deactivate/Reactivate | updateAgencyUser/deactivateAgencyUser/reactivateAgencyUser; current scope, stamp/session revocation and last administrator guard | 04-05/04-07 |
| Permissions matrix and bordereau request Approve/Reject | Allowlisted request/decision/revocation; independent actor; actual download remains unavailable | 04-07, export enforcement 10 |
| Suspend reason/manager approval; later Reactivate | Pending version-bound state request; independent decision applies atomically | 04-06 |
| Effective commission/product history | Immutable full terms versions; active edits create independently approved future proposals | 04-06 |
| Preview portal/pPortal Back to agency; visible fields and hidden internal data | Audited internal getAgencySharingPreview and scoped client/contact/instruction reads; same projection service as stored agency identity | 04-07 |
| Prototype policy row/Open in Accounting/portal policy and open-item counts | Real policy/quote links in Phases 5/6, task/message items 9, statements 10; no fake zero counts/balances or links before availability | 5/6/9/10 |
| Prototype implied activation emails/ledger/PI and quarterly review follow-ups | Durable demo notification receipts, persistent follow-up records; ledger remains unavailable until finance, tasks materialize later | 04-04/04-06; tasks 9, ledger 10 |
| Local invitation link/password acceptance (required to make invitations functional) | Development-only audited secret reveal, fragment removal, CSRF-protected one-time acceptance; no automatic sign-in | 04-05/04-07 |

The source relationship-manager names are examples replaced with stored selectable user IDs. Both source TOBA versions remain selectable declarations; only the current configured agreement can satisfy activation. Fleet references do not enable an unapproved fourth product. Compliance declarations preserve source choices but cannot claim regulator/provider verification. Phase 4 SQL/API/browser checks must prove persistence, denial, concurrency and truthful delivery separately from this source contract.
