# Document rendering

Status: Phase09-06 in progress. Policy rendering is implemented; quotation/servicing terms, future template publication and the complete acceptance gate remain open. This is not a claim that document generation/delivery is complete.

## Boundaries

`DocumentRenderContract.Create(DocumentRenderInput)` validates an exact stored UTF-8 source and template against their SHA-256 values. Policy inputs use a nonempty immutable PolicyVersion ID and `policy-version` source kind. Product, requested kind and SQL template product/kind must agree. The existing strict issued-policy schemas remain authoritative. Duplicate JSON properties, excessive input/text/array sizes, unsupported formats, arbitrary layout fields, markup and external asset instructions fail closed.

The closed `document-template-1` object contains exactly `format`, `productCode`, `kind`, `title` and `notice`. Existing `policy-template-1` objects retain their exact bytes and have the original three fields. The renderer prints their old pending-generation notice under **Retained template wording (historical provenance)** with an explanation; it does not represent it as current generation state. No legacy template or request is rewritten.

Policy kinds currently supported are policy schedule, statement of fact (including retained `policy-statement` identity), policy certificate, selected endorsement and cancellation notice. An issued cancellation snapshot can produce only a cancellation notice. The retained cumulative premium is not a refund. Commercial certificates require the selected `employers-liability` section; a declaration of an employers limit alone does not qualify. Commercial documents use property, business interruption, wages and liability declarations and never add motor fields.

`IPolicyDocumentRenderer.Render` returns real PDF bytes, their SHA-256, page count and renderer/projection/font versions. It neither writes a FileObject nor marks a DocumentVersion/request ready. Those state transitions belong to09-07. PDFs contain an explicit fictional demonstration notice and make no PDF/UA or legal certificate compliance claim.

`PolicyDocumentRenderService.RenderRetainedRequest` loads a real request, holds current identity and policy scope, joins the exact policy version and product-owned template, checks the request envelope hash and source/template identities/content, then calls the renderer while scope remains held. Historical published requests can use their retained templates; this operation does not select a new template by current date. There is no new public HTTP endpoint in this increment.

## Layout and immutable dependencies

PDFsharp-MigraDoc6.2.4 is centrally pinned with package locks. IBM Plex Sans regular/bold from official releasev6.4.0, commit383c681f015ed2626e919ec4e3cca16ccc204e9d, is embedded. Font hashes, original download URLs and OFL1.1 are retained in Infrastructure/Operations/Fonts. A custom resolver registers the fonts and MigraDoc auxiliary fonts without installed-font lookup.

A4 pages use the prototype's IBM Plex Sans, blue accent and restrained grey table borders. Tables repeat column headings; long field values use bounded continuation rows without truncation. Footer page numbering and immutable source/template/renderer provenance are included. Output limits are300pages and20MiB; source JSON is8MiB with bounded depth/value/array counts.

`document-question-labels-v1.json` freezes420 readable labels derived from `docs/design/funnel-field-mapping.json`, the three retained `contracts/examples/prototype-*-questions.json` catalogues, and the approved no-claims reason in `contracts/examples/conditional-capture.json`. The sales snapshot was not changed. Unknown question identities fail instead of silently dropping answers. Future label changes require a new catalogue/projection version. Internal client/relationship IDs and question configuration IDs do not clutter the declaration body; the exact source remains identified by its immutable ID/hash.

Money formatting applies only to known monetary fields. Other saved strings and decimal-looking values remain declarations, not inferred prices. Selected cover and requested options have distinct headings. All content is emitted through text/table APIs; source text is never executed or resolved as a resource.

## Verification in progress

Current strict policy increment passes37unique cases/1realSQL/no skips:31unit and6integration including four product PDFs, font proof and a normally issued policy with preserved source/template/request and suspended-user denial. Root412passes. Independent `scripts/verify-policy-pdf-examples.py` checks expected names, money, glyphs, source/template IDs, page numbers, A4 geometry and all30 repeated clauses/end markers of a long declaration. Poppler pages were inspected; hash wrapping was corrected. Current fixtures span10–13pages for declaration-rich schedules/statements and2pages for the commercial certificate. Evidence is in `.local/phase9-06-policy-wrap-strict` and09-06-CHECKPOINT. Quotation/renewal and full plan acceptance remain open.
