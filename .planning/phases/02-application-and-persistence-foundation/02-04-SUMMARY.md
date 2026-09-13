---
phase: 02-application-and-persistence-foundation
plan: '04'
status: complete
requirements: [FND-03, FND-02]
completed: 2026-09-13
---

# Prototype shell and real sign-in UI

Commit 40f3fd1 delivers protected server-rendered workspace pages, client sign-in/out, current account display, responsive sidebar/header/user menu, account section navigation, reusable panels/table/status/empty states and safe loading/error/restricted states. Actor data comes from the .NET API with forwarded session cookie, no-store and per-render fetch deduplication. Authentication changes perform a full navigation to discard previous router payloads. No browser-stored bearer token is introduced.

Source fidelity: inspected the extracted template and original rendered prototype at 1560x1000. Reused the bundled IBM Plex Sans Latin font through a reproducible extraction script; matched the 238px sidebar, 62px header, source colors, borders, radii, typography hierarchy and compact navigation. Dashboard metrics remain unavailable markers, not invented zero counts. Account profile is read-only; later security tabs show explicit availability states. Full business tables/counts and operational job/audit visibility arrive with their owning workflows, including 02-05.

Validation: production webpack build and TypeScript pass, zero-warning lint passes, four frontend unit checks pass. Pinned Playwright 1.62.1 runs a real Chrome journey through Next's API proxy and native SQL-backed .NET authentication. It covers empty/invalid credentials, preserved email, login/reload/logout, replayed revoked cookie, missing network for login/logout, anonymous route redirect, restricted Admin view, account tabs, keyboard skip link, native mobile dialog/Escape/focus return, no horizontal overflow at 390px and no browser page errors. Sidebar/header dimensions and font load are asserted.

Visual review: source/app desktop and mobile/login/drawer screenshots were inspected. The first mobile review found that skip-to-content could scroll the heading under the sticky header; corrected scroll margin and off-screen skip-link positioning, added a browser assertion, then re-ran the entire journey. Summary-card density was tightened to source proportions. Updated mobile screenshot shows the complete heading below the header. Focus rings, labels, non-color status text and reduced-motion CSS are present. This is agent visual/automated verification, not human UAT or final all-feature UI acceptance.

Evidence: ignored .local/browser-evidence contains prototype-desktop.png, shell-desktop.png, shell-mobile.png, navigation-mobile.png, login-desktop.png and login-mobile-error.png. Reproduce using pnpm web:browser with documented local servers. Passwords were cleared before login screenshots; the script loads the secret from environment/ignored local storage without printing it.

Environment: unrelated Docker process occupied 5080, so tests used API 5087/web 3100 with BACKOFFICE_API_ORIGIN set at frontend build and runtime. Did not alter the existing listener. Both test-owned API/web processes were ownership-checked and stopped after verification. Native SQL remains the verified storage profile; no Docker SQL deployment is claimed.

Next: 02-05 durable audit/idempotency/outbox/provider/inbox behavior and operational views, then 02-06 foundation acceptance. Phase 2 and FND requirements remain open.
