# 09-09 review

Status: complete. Final SQL and current-source browser acceptance passed.

Reviewed separate internal notes, immutable thread identity/audience, draft storage,
normalized exact recipient/file associations, current authority before replay,
ETag edits, bounded input, protected history, options/counts and all record callers.
No unresolved HIGH/CRITICAL implementation finding found in this review.

## Corrections and test evidence

- SQL nvarchar(max) does not enforce the declared8000-character EF maximum. Added
  DATALENGTH guards for note and draft bodies; actual oversized SQL insertion must
  fail with52001. SQL notes/thread history and queued content/selections are retained.
- Explicit internal-thread relationship properties are denied. The shared JSON
  envelope rejects explicit null with422 before the audience-shape400 check; tests
  distinguish null from a populated forbidden relationship property.
- Draft selection-only updates explicitly modify UpdatedAt to advance rowversion.
  Stale writes retain text; saved-current review and adoption are separate actions.
- Fixed preexisting policy/CC/history agency links from /agencies to /agents.
- Browser waits now wait for actual attachment choices and use accessible roles for
  native controls whose implicit label text includes loaded options/value. Response
  wait/click promises are handled together; unused error bodies are not fetched
  from Chrome after412. Earlier failed harness runs are retained and excluded.
- Actual agency sharing clients/contacts/instructions projections are compared
  before and after creating private notes and drafts. Visibility filtering alone
  is not treated as external projection proof. No draft broker API is introduced.

## Boundaries

Notes and threads are physically separate and have independent counts. All new
capabilities are internal-only and never broaden original insurance/client scope.
Current relationship and recipient/attachment authority precedes receipt replay.
Read links retain original file authorization. Closed DTOs never serialize support
flags or underwriting data. Ended recipients remain removable from a draft; they
cannot be resaved. Source document composition pins exact versions; a draft is not
sent. Immutable versions/provider delivery belong09-10. Task comments reuse the
existing task event store. The retained demo, original keys and frontend-code
remain unchanged. Downgrade refuses retained communication history.

Motor browser passes13 checks and SQL readback in phase9-09-browser-motor-conflict;
12unit and417root cases pass, frontend190pass, production build/lint/typecheck pass.
OpenAPI exits0 with101 warnings (not a warning-free claim). Visual inspection of
saved notes desktop and390px composer confirms readable content/no page overflow;
checkbox styling remains vertically stacked but usable. Human business/AT UAT
has not been performed. Final SQL/commercial evidence must be recorded before
SUMMARY/completion. Source placement/seeded history acceptance remains09-18.

Final: SQL2 and Commercial browser passed in phase9-09-final-sql-commercial; strict16unique/4realSQL/no skips. Visual correction added shared communication-form and checkbox styles; final phase9-09-styled-browser2pass,13checks/product plus SQLreadback, normalized current fingerprints verified. Both final mobile screenshots inspected. Production rebuild/lint pass. Earlier pending statements above describe review chronology, not outstanding work.
