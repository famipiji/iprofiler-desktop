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
| 2026-10-28 | Task 0.5 Verify passed in Sandbox (install, Start-menu launch 8 pages, log, uninstall keeps data); SAC blocks unsigned installer → code signing = pilot blocker | ✅ | 4443dca |
| 2026-10-28 | Task 0.6: Notion backlog board (31 rows), docs/risk-register.md (R1–R14), design-note template; M0 gate pending team-lead review | ✅ | — |
| 2026-10-28 | Code-signing certificate deferred (needed before pilot) | ⏸ | — |
| 2026-10-28 | Task 0.1 (office part): AD7100 + FB6380E have 32/64-bit TWAIN; 0 scanners without 64-bit driver; AD7100 = Task 1.1 test scanner. Client sites + legacy samples open | 🟡 | — |
| 2026-10-28 | Task 0.1 remainder deferred; M1 reordered: 1.2 → 1.3 → 1.4 → 1.5 → 1.1 (needs AD7100) → 1.6 | ⏸ | — |
| 2026-10-28 | Task 1.2 PaddleOCR provider (PP-OCRv5 mobile, 12.3 MB models in repo): page OCR 6/6 words, 2.8–3.8 s/page warm; region field exact (~1 s); 0 network endpoints on host; Sandbox network-off run blocked by App Control (unsigned exe), pending manual; 59/59 tests | 🟡 | see git log |
| 2026-10-28 | Task 1.3 ZXing barcode/QR provider + OpenCV patch-code check; 300 dpi G4 TIFF: barcodes 3/3 (0.57 s page, ~25 ms region), PaddleOCR 6/6 words; patch sheets 6/6 (upside-down 1↔6, 3↔T risk recorded); AD7100 hardware patch codes → Task 1.1; 74/74 tests | ✅ | see git log |
| 2026-10-28 | Task 1.4 OpenCvImageProcessor (deskew 0.50/2.95° on G4, blank & stapled-blank ≥ 0.9998, content < 0.99) + Page.DeskewAngle + upside-down detection → Page.Rotation (PageAnalyzer); original hash unchanged; sparse page counts as blank (M3 fix noted); 95/95 tests | ✅ | see git log |
| 2026-10-28 | Task 1.5 EF Core SQLite (Batch/Document/Page + Rotation/DeskewAngle/Dpi/Crop persisted, 48 KB, no blobs) + PDF-OCR (PDFsharp 6.2.4) + G4/LZW multi-page TIFF (LibTiff) via RenderForExportAsync/ExportGeometry, real TIFF DPI; MuPDF + PDFium: 24/24 phrase searches (incl. SIMEN UTARA, TERIMA KASIH), 119/120 words; letter-spaced word boxes fixed; ⚠ PDFsharp 6.5 is AGPL (R12), 2 Paddle engines crash (R16); 131/131 tests | ✅ | see git log |
| 2026-10-28 | Task 1.1 stopped after step 1: AD7100 not connected; NAPS2.Sdk 1.6.0 = LGPL-2.1+ (deps MIT/Apache; NTwain 3.7.6 MIT); R3 updated, team-lead decision (a) NAPS2 under LGPL vs (b) NTwain + own worker | ⏸ | see git log |
| 2026-10-28 | Task 1.1 deferred (AD7100 not connected; D8 NAPS2 LGPL vs NTwain pending); 1.6 to run in import mode; M1 gate conditional | ⏸ | — |
