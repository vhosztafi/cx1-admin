# 04-05 implementation review

Reviewed inline against04-05-PLAN, approved source coverage and UI/data contracts. No outstanding blocking finding within this plan's scope.

| Area | Implementation and evidence |
|---|---|
| Identity separation and persistence | Agency-owned StaffUser, single agency role, normalized email uniqueness, immutable invitation history and SQL scope/ownership constraints. Real-SQL duplicate/mixed-scope/rollback cases pass. |
| Delivery and acceptance | Draft staging creates no delivery token/job. Active issuance and resend preserve protected delivery ownership; stale/expired/revoked/reused tokens cannot accept. Acceptance locks agency/user/invitation, creates one hashed credential and no session. Full SQL suite and acceptance browser passed. |
| Authority and concurrency | Current internal permission before replay, resource-specific ETags, last-active-admin protection, role/session revocation and retained history. Lost-response browser replay uses identical key/body/version; stale edit retains inputs. |
| Wizard integration | Users outside the form; dirty answers block mutations; child operations guard saves/navigation. Parent version refresh checks retained details/products; concurrent draft edits require explicit reload. Browser verifies both success and conflict. |
| Source UI/data | Name/email/three roles/status/Last active/actions, paged invitation history with separate delivery/acceptance, lifecycle dialogs, safe demo link. Last active derives from real session history. Directory/header/KPI user totals and invited subsets are persisted and truthful. Desktop/mobile reviewed. |
| Scope boundaries | Activation-result counters/counts and activation notices belong04-06; broker login/scoped broker administration04-07; cross-phase review04-08. Operational/financial counts remain with their owning phases. No fabricated totals or completed UAT. |

Final verification:239 backend cases including34 real-SQL scenarios,22 frontend tests,77 Node contract tests, OpenAPI lint, ESLint and production build pass. The latest real users browser includes directory/header counts. Full report gate: .local/phase4-user-counts-full. No hosted CI or human assistive-technology UAT is claimed.
