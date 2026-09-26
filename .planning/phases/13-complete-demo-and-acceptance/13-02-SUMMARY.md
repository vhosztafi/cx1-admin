---
phase: 13-complete-demo-and-acceptance
plan: '02'
status: complete
requirements_completed: [ACC-02, ACC-05]
---
# 13-02 — Owned restart and visual acceptance

Stopped/restarted only the owned API 5095 and frontend 3193 with the same isolated database and key directory. Eight browser checks pass: retained session, exact favourite/filter ID, same three product quote IDs/count, current finance denial, repeated quote/report navigation, keyboard skip, drawer focus recovery and narrow bounds. The one existing Data Protection key hash is unchanged. SQL Server and retained 3100/5087 preview remained running.

Four loaded-state visual checks additionally pass with no page errors. Initial screenshots had caught loading placeholders; replaced acceptance captures with actual report/source/search/task data. Inspected prototype and current desktop/narrow screenshots. Source-style sidebar, typography, cards and table grouping remain recognisable; narrow tables scroll inside their panels. No reproducible functional navigation bug required a code change. Cold webpack route compilation took tens of seconds.

Evidence: `.local/phase13-tests/restart-browser.json`, `loaded-visual.json`, `preservation.json`; `output/playwright/phase13/*-loaded.png`, reporting-desktop and prototype-desktop. Real SQL-engine restart and human assistive-technology review remain explicitly unperformed; the handover provides a dedicated-instance recovery procedure. Cookie storage state stays ignored and is not a deliverable.
