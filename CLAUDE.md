# CLAUDE.md

Guidance for Claude (Cowork / Claude Code) working in this repository.

## What this is

**iProfiler Desktop**: a ground-up rebuild of the legacy iProfiler ScanStation (C# WinForms +
GdPicture) as a standalone, **offline**, Windows desktop capture app installed at client sites.
Scan (TWAIN/WIA) → OCR / barcode / QR capture → document separation → indexing (Profiles /
Properties / Selection Lists) → export (PDF, PDF-OCR, TIFF + sidecar) → upload (M-Files/iVault).

Source plan: `iProfiler_Desktop_Rebuild_Plan.docx` (28 Aug 2026). Execution plan with the
corrections and decisions: `.tasks/2026-10-28-desktop-rebuild-m0-m1.md`.

This is **not** the Flask `iProfiler-AI-Recognition` web app. Nothing is shared with it.

## How tasks are run

- Work comes from `.tasks/*.md`. Run **one task at a time, in order**, then stop.
- Before starting, read **section B (decisions)** of the task file and follow what is recorded there.
  If a decision a task depends on is still unticked, stop and say so; don't guess.
- Every task ends with its **Verify** step. Write the result under the task in the task file
  (`**Result (date): ✅/❌ ...**`, with numbers, not just "works").
- Add one line to `progress.md` per task.
- Commit with Conventional Commits (`feat:`, `fix:`, `chore:`, `docs:`, `test:`).
- Tasks marked 👤 manual are for the user; don't attempt them.

## Stack (per section B decisions)

- C#, **.NET 10** (LTS), **WPF + MVVM**, **x64** app + 32-bit TWAIN worker process
- Explorer tree: Batch → Document → Page. AI field extraction is optional (`IAiExtractionProvider`, off by default)
- Scan: NAPS2.Sdk (or NTwain); OCR: Sdcb.PaddleOCR (PP-OCRv5 mobile, local model files);
  barcode/QR: ZXing.Net; image ops: OpenCvSharp4; PDF: PDFsharp; TIFF: BitMiracle.LibTiff.NET
- SQLite via EF Core (metadata only, never image blobs); System.Text.Json for settings
- Serilog (rolling file); Microsoft.Extensions.Hosting DI; xUnit + NetArchTest; Inno Setup installer
- Package versions are pinned centrally in `Directory.Packages.props`.

## Architecture rules (review-blocking)

Layers, each depending only on the layer(s) below:

| Project | Role | May reference |
|---|---|---|
| `IProfiler.Core` | domain models + interfaces | nothing third-party |
| `IProfiler.Application` | workflow / orchestration (separation dispatch lives here, **once**) | Core |
| `IProfiler.Infrastructure` | one provider class per interface, each wrapping one library | Core + third-party libs |
| `IProfiler.Data` | SQLite repositories, storage only | Core + EF Core |
| `IProfiler.App` | WPF views/viewmodels + composition root | all |

1. **Third-party imaging/scan/OCR/PDF namespaces (NAPS2, NTwain, Sdcb, OpenCvSharp, ZXing,
   PdfSharp, QuestPDF, BitMiracle, Tesseract) appear only in `IProfiler.Infrastructure`.** The
   architecture tests enforce this; never weaken or skip them.
2. No library types in ViewModels or XAML code-behind.
3. Interfaces first, providers second.
4. `SeparationMode` is an enum with one dispatch method used by every scan/import/hot-folder path.
5. **No empty `catch`.** Every catch logs through Serilog (or rethrows).
6. Paths come from `AppPaths` (`AppContext.BaseDirectory` for install files and models,
   `%LOCALAPPDATA%\iProfiler\` for data). **Never** `Environment.CurrentDirectory`.
7. Original page images are stored untouched; rotation/crop/order are metadata applied on export.
8. Offline: no runtime downloads (models included), no phone-home licensing, no auto-update by default.
9. No secrets or keys in source; use DPAPI for anything sensitive on the client.

## Commands

```powershell
dotnet build
dotnet test
dotnet publish src/IProfiler.App -c Release -r win-x64 --self-contained true
```
Installer: Inno Setup script in `installer/`. Always test installs in **Windows Sandbox**, launched
from the Start-menu shortcut, never on the dev machine.

## Lessons carried over (from the Flask PaddleOCR migration)

- Mobile PaddleOCR models are ~3× faster than server ones; downscale to ~1600 px longest side first.
- PaddleOCR returns lines. Use its real word boxes for text layers/highlights.
- One engine instance, one call at a time (not thread-safe, hundreds of MB).
- Native runtimes can ship the same DLL; load order matters. Test the installed build on a clean machine.
