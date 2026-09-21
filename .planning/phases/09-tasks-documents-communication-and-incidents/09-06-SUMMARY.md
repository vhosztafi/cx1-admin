---
phase: 09-tasks-documents-communication-and-incidents
plan: '06'
status: complete
completed: 2026-09-21
requirements-completed: []
---

# Exact-source PDF rendering

Policy, quotation, adjustment and renewal PDFs are implemented with immutable source/template provenance. Durable DocumentVersion generation/publication remains09-07; document UI09-08 and delivery09-10. OPS-04 and CC-05 are not yet complete compound requirements.

## Delivered

- `DocumentRenderContract.Create(DocumentRenderInput)` checks closed product/kind/template/source identities, exact hashes, duplicate properties, bounded JSON/text, strict existing issued/capture schemas and source applicability. Cancelled sources produce cancellation notices only. A Commercial Combined employers' liability certificate requires selected EL cover, not an unselected declared limit. Template text cannot execute markup, scripts or external assets.
- `DocumentQuoteTermsRules` binds quote revision/configuration hashes and retained contractual terms to their cycle, rating, template, product and exact declarations. Quotation price, named insured, agency, conditions and expiry come from retained terms. Statements do not invent prices or issued cover.
- `DocumentServicingTermsRules` validates saved servicing/renewal envelope hashes, chronological proposed slices, exact template, signed money and conditions. Renewal has one proposed slice. Each adjustment slice is rendered separately; proposals never claim to change issued cover.
- `DocumentPolicyProjection` projects readable insured, period, risk, selected cover, requested declarations, endorsements, warranties, price and cancellation wording. A frozen, hashed420-label catalogue preserves source question meanings. Unknown question IDs fail. Cancellation never misrepresents cumulative premium as refund.
- `IPolicyDocumentRenderer.Render` returns PDF bytes/SHA256/page count and renderer/projection/font versions. PDFsharp-MigraDoc6.2.4 is centrally pinned. Official IBM Plex Sansv6.4.0 regular/bold fonts and OFL1.1 are embedded with hash verification and a custom resolver including auxiliary fonts. A4 tables paginate with repeated headings, bounded continuation rows, long-token wrapping, Unicode text and provenance. Output is bounded to300pages/20MiB. Legacy pending notices remain unchanged under explicit historical provenance.
- Scoped `PolicyDocumentRenderService.RenderRetainedRequest`, `RenderQuoteTerms` and `RenderServicingTerms` hold current actor/parent access and verify the complete owned SQL graph before rendering. Optional commercial request projections are checked against the exact retained policy version. No request, template or issued source is rewritten. DI is registered; no new public document endpoint is claimed by this slice.
- `DocumentTemplateSeed.SeedAsync` publishes15 missing future templates effective2026-09-22 across all three products: policy schedule/certificate/statement version2 and endorsement/cancellation notice version1. Initialization is additive and transactional. Migration `20260921180754_OperationalDocumentTemplates` extends the kind constraint; Down refuses when retained new-kind templates exist. Retained demo initialization has not run.

## Verification

Final focused gate `.local/phase9-06-final-strict` passes72 unique cases:55unit and17integration, including7realSQL, no skips. `scripts/assert-test-results.ps1` accepted both current reports. Integration took6m40s. Root412 passed in `.local/phase9-06-final-root.log`. No acceptance process remains active.

The seven SQL scenarios cover normally accepted MT and CC quotations and issued request rendering; both MT renewals; CC renewal; a two-slice MT adjustment; and additive template publication, safe migration Down/Up and unsafe downgrade refusal. Source/request/template preservation and suspended actor denial are asserted. The remaining ten integration cases are nine actual product PDFs and the embedded-font proof. No full Phase9 regression or browser UI acceptance is inferred from this focused backend slice.

Independent pypdf checks in `scripts/verify-policy-pdf-examples.py` pass15 examples: nine product documents, two normally prepared SQL quotations and four normally prepared SQL servicing/renewal documents. Checks cover expected names, glyphs, exact selected cover values, source/template identity, A4/pages, retained SQL parties/prices/conditions/terms hashes and effective slices. The long CC declaration retains all30 clauses and end markers. Documents span2–17pages. Font proof separately preserves£/€/Café Noël/Ω/Ж/bullet.

Poppler visual review includes final `mt-cancellation-final.png`, `mt-certificate-final.png`, `mt-certificate-page4-final.png`, `cc-endorsement-final.png`, `cc-endorsement-page2-final.png` and `cc-cancellation-page2-final.png`, plus earlier inspected certificate/hash wraps, long CC table continuation, quotation, renewal and adjustment pages under `.local/phase9-06-pdfs`. No observed clipping/overlap. This is neither legal/PDF-UA certification nor human business/assistive UAT.

RED-to-GREEN evidence includes missing renderer/font symbols, strict servicing contract rejection before implementation, missing template seed, and the old SQL template-kind constraint rejecting new kinds. Fixes include PageCount capture before save, long hash wrapping, canonical retained statement kind mapping and fail-closed template downgrade. The independent certificate assertion was corrected to compare selected cover (own vehicles/£10,000/£500 excess), rather than a requested option printed only on the schedule.

## Review and continuity

Reviewed exact-source ownership, current scope, bounded literal rendering, old-template preservation, source-specific monetary semantics and additive migration rollback. Test helper callbacks preserve original paths when omitted. Earlier increments are72fa80e(fonts),c3c3385(policy) and81bdff9(quotation). Remaining servicing/templates increment accompanies this summary after the final gate.

Keep frontend-code, retained CoverMGA_Demo, preview processes and original data-protection keys unchanged. Preserve the two pre-existing generated Next files.09-07 must persist original generated bytes, recover finalization without another version, associate existing work rather than duplicate intent, and expose scoped document/version endpoints. Current renderer calls alone do not mark documents ready.
