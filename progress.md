# Progress Log: iProfiler Desktop

One line per task: date, task, result, commit.

| Date | Task | Result | Commit |
|---|---|---|---|
| 2026-10-28 | Project folder set up (plan, CLAUDE.md, progress log, git config files) | ✅ | — |
| 2026-10-28 | Section B decisions recorded (D1–D7) | ✅ | — |
| 2026-10-28 | Task 0.1 deferred until before Task 1.1 | ⏸ | — |
| 2026-10-28 | tools/setup-dev.ps1 added for Task 0.2 | ✅ | — |
| 2026-10-28 | Task 0.2 dev tools installed (.NET 10.0.403, VS 2026, Git, Inno Setup, Sandbox) | ✅ | — |
| 2026-10-28 | Task 0.3 solution skeleton + global.json (SDK 10.0.403) + architecture tests (15/15 pass; ZXing probe fails 1–2 tests as intended) + CI workflow; pushed to GitHub, first CI run green | ✅ | ac1c710 |
| 2026-10-28 | Task 0.4 domain + 12 interfaces + SeparationEngine + fakes + DI/Serilog + AppPaths + main window; EF Core/Serilog/logging rules + source rules added (47/47 pass, 9 probe violations caught); app shows 8 fake pages, log written | ✅ | see git log |
| 2026-10-28 | Task 0.5 installer (46.8 MB, unsigned) + startup-error fallback log/message + friendly UI-crash message (50/50 tests); automated Sandbox install blocked by App Control policy, Verify pending (manual) | ⏳ | see git log |
