# .NET capture schema validation decision

Researched 2026-09-15 during05-02 identity implementation. Implemented and verified in05-02's closed draft schema slice.

The existing AgencyDraftRules uses a manually maintained small field tree. Quote capture has nested shared definitions, oneOf/anyOf typed answers, numeric/string/array bounds and formats across255source occurrences. Duplicating that vocabulary in a new homemade validator would create a second schema source and unnecessary correctness risk.

Choose JsonSchema.Net9.4.0 for the next shape-validation slice. The published package targets .NET8+ and .NET Standard2.0, compatible with this net10.0 solution. Pin its version centrally and commit all affected lock files after reviewed restore. [NuGet package](https://www.nuget.org/packages/JsonSchema.Net/).

Use the checked-in quote-draft schema as an embedded resource. Build once from trusted JSON, evaluate bounded parsed proposals, and explicitly enable RequireFormatValidation; formats are not necessarily assertions by default. This is structural validation, followed by scoped question/reference configuration and item identity checks. [Official basics](https://docs.json-everything.net/schema/basics/), [evaluation options](https://docs.json-everything.net/api/JsonSchema.Net/EvaluationOptions/).

The current API provides JsonSchema.Build(JsonElement, BuildOptions, Uri) and Evaluate(JsonElement, EvaluationOptions). Build options accept a local schema registry. Keep only bundled local references; no request-controlled schema or network fetching. Inspect actual restored signatures and test resource lifetime/concurrent evaluation before wiring a service. [Schema API](https://docs.json-everything.net/api/JsonSchema.Net/JsonSchema/), [build options](https://docs.json-everything.net/api/JsonSchema.Net/BuildOptions/).

Convert failures to bounded stable keyword/path issues; do not return library error strings containing caller values. EvaluationResults exposes IsValid, Details, Errors and InstanceLocation for this adapter. Tests must compare .NET outcomes with existing Node fixtures and mutations including invalid formats, unknown nested authority fields and all answer variants. [Results API](https://docs.json-everything.net/api/JsonSchema.Net/EvaluationResults/).

QuoteItemIdentity remains dependency-free; QuoteCaptureShape now uses this pinned package. The actual restored fetch callback takes (Uri, SchemaRegistry). All six fixtures and16new .NET cases pass, with387backend/57SQL regression results. The dependency graph includes JsonPointer.Net7.0.2, Json.More.Net3.0.1 and Humanizer.Core3.0.10; locked restore passes. Catalogue validation, SQL storage and endpoints remain unfinished.
