# Foundation UI contract

Source: docs/prototype/Cover MGA Back Office-4.html and extracted template. Preserve IBM Plex Sans and exact source colors: card white, border #e4e6ec, radius 10px; informational blue #2f49cc / #eef1fe, success #0f6b45 / #e8f6ee, warning #8a5300 / #fff4e0, error #b3241f / #fdecec, muted #5b6172 / #f1f2f6. Extract remaining shell dimensions/colors from source during implementation, not from memory. User's fidelity requirement overrides generic skill recommendations to invent a new visual style.

Foundation screens: sign-in, authenticated dashboard shell, navigation, current user menu/sign-out, safe empty/loading/error/403 states and operational job/audit visibility for supported foundation records. Use real API data; future screens show explicit not-yet-implemented information without pretending a workflow succeeded. Add subsequent feature screens in their owning phases.

Desktop: persistent left navigation, top search/user area, compact content/table layout and action rail where source calls for one. Smaller screens collapse navigation into a labelled keyboard-operable drawer; tables scroll horizontally without truncating actions. Match source at 1560x1000 and inspect 390px width. Visible focus, semantic labels, keyboard navigation, non-color status labels and reduced-motion support are required.

Login validates empty fields before submit, preserves email after rejected credentials, masks password and prevents duplicate submit. Busy/error feedback uses an aria-live region. Successful login fetches current actor; logout revokes server session before navigating. Never show demo credentials in production mode. Browser tests cover actual login, reload, logout, expired/denied session and network failure; screenshots compare shell proportions and type hierarchy with source.

Inline UI contract review: pass for phase scope, source fidelity, responsive states, accessibility, content honesty and testability. Actual visual audit awaits rendered implementation.
