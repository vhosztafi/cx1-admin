---
phase: 20-motor-trade-policy-records-and-history
plan: 01
requirements_completed: [POL-15, POL-16, POL-17]
---

Motor Trade policies now use the prototype saved client-title header, linked agency and recorded insurer, cover period and selected version. Tabs follow Overview, Risk Details, Cover, Drivers, Vehicles, Tasks, Documents, Transactions, Finance, Notes, Messages and Claims. Servicing entry opens from contextual actions. Version comparison, clone/reconstruction and effective/known-at controls belong to Transactions; posted movements are reachable through Finance. CC presentation is unchanged.

Driver and vehicle registers now present saved records in tables. Selecting a stable item shows its policy/term/version, effective and processing context, retained declarations, history, applicable cover and existing driver tasks or MID outcomes. No prototype mock claims, balances or identity are substituted for saved data.

Both focused lint and app type checks pass. The real-browser fixture verified client-title header/tab order, contextual servicing, driver/vehicle history, Finance, exclusion of an issue not yet recorded at the known-at cutoff, restoration of current policy, 390px containment and no application errors. A known future first issue remains labelled scheduled according to the existing temporal selector. Evidence: output/playwright/v1.1/policy-alignment-report.json, policy-overview.png, policy-vehicle.png and policy-mobile.png. Final all-section prototype comparison remains in phase 24.
