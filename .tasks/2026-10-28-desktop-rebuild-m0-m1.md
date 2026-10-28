# iProfiler Desktop — Rebuild Plan, Milestones 0–1 — 2026-10-28

## STATUS: In Progress: Tasks 0.2–0.4 done; Task 0.5 built, Verify pending (manual Sandbox test); Task 0.1 deferred until before 1.1

Execution plan for **iProfiler_Desktop_Rebuild_Plan.docx** (28 Aug 2026): a greenfield
.NET / WPF rebuild of the legacy iProfiler ScanStation (WinForms + GdPicture). This is a **new
repo**. It is not the Flask `iProfiler-AI-Recognition` project.

Covers **Milestone 0 (environment & risk burn-down)** and **Milestone 1 (architecture spike)** in
detail. Milestones 2–6 are outlined in section E and each gets its own task file **after** the
Milestone 1 gate, because the spike results decide their details.

**How to run:** one task at a time, in order. Each task ends with a **Verify** step that must
pass before you start the next. After each task: commit (`feat:` / `chore:` / `docs:` Conventional
Commits), add a line to `progress.md`, and tick the task here.

Tasks marked **👤 manual** are yours to do (hardware, accounts, decisions); the rest can be run in
Cowork / Claude Code.

---

## A. Corrections to the rebuild doc (raise these with the team lead)

| # | Doc says | Problem | Proposed fix |
|---|---|---|---|
| A1 | .NET 8 (§3.1) | **.NET 8 reaches end of support on 10 Nov 2026**, so it would be unsupported before v1 ships | Target **.NET 10 (LTS, supported to Nov 2028)**. WPF, EF Core, and every library below support it |
| A2 | Default to an **x86** build for scanner-driver compatibility (§6.3) | **PaddleOCR's .NET runtime (Sdcb.PaddleInference) is x64-only**, so x86 + PaddleOCR is impossible | **x64 app + a 32-bit TWAIN worker process** the app starts itself (shipped in the same installer, not a separate service, so §3.3's anti-pattern rule still holds). NAPS2.Sdk provides this (`SetUpWin32Worker()`) |
| A3 | §3.3 says PaddleOCR is "confirmed", but §5.2 calls it a "future upgrade", and M1 + the risk register test **Tesseract** | Doc was only partly updated | PaddleOCR is primary everywhere; Tesseract stays only as the documented fallback behind `IOcrProvider` |
| A4 | Leptonica "ships alongside Tesseract" for deskew (§3.5) | With PaddleOCR primary, Leptonica isn't there | Use **OpenCvSharp4**, which Sdcb.PaddleOCR already depends on: deskew, blank-page, rotation. One fewer library |
| A5 | Explorer tree: 2 levels Folder → Document (§3.7) **vs** Batch → Document → Page (M2, §5.1) | Inconsistent | **Decided (D4): 3 levels, Batch → Document → Page** |
| A6 | Patch-code separation is listed as a mode (§2) | **No library is named for it, and ZXing does not read patch codes** | Prefer the **scanner's own patch-code detection via TWAIN** (most production scanners expose it); software fallback = simple bar-pattern check with OpenCvSharp. Confirm in the scanner inventory |
| A7 | WiX **or** Inno Setup (§3.9) | WiX v6+ asks commercial users to pay an Open Source Maintenance Fee | Default to **Inno Setup** (free, simpler solo); WiX only if MSI is a hard client requirement |
| A8 | NTwain directly (§3.2) | Low-level; you'd build the 32-bit worker yourself | Evaluate **NAPS2.Sdk** (built on NTwain, also WIA, .NET 8+, maintained) in Task 1.1. **Check its licence first** |

**Not covered by the doc at all, so scope these with the team lead:**
- **AI field extraction.** Not in the plan. It needs a GPU, which client sites can't be assumed to have. Keep it out of v1, or make it optional and route it to the existing Flask/Qwen server when the site has one.
- **Migration of existing sites.** Old settings (BinaryFormatter), profiles/properties/selection lists, and license keys from ScanStation.
- **Code-signing certificate for the installer.** Without one, client sites get SmartScreen "unknown publisher" warnings.
- **TWAIN DSM.** Make sure `TWAINDSM.dll` (TWAIN 2.x data source manager) is present or installed.
- **UI language.** English only, or English + Malay?
- **Effort estimates per milestone.** The doc has none; fill them in after the M1 spike.

---

## B. Decisions (team lead, 2026-10-28) ✅ all answered

- [x] **D1** ✅ **.NET 10** (LTS) instead of .NET 8
- [x] **D2** ✅ **x64 app + 32-bit TWAIN worker process** (keeps PaddleOCR)
- [x] **D3** ✅ **PaddleOCR primary**, Tesseract only as fallback behind `IOcrProvider`
- [x] **D4** ✅ Explorer tree: **3 levels: Batch → Document → Page** (pages get their own tree row)
- [x] **D5** ✅ Installer: **Inno Setup**
- [x] **D6** ✅ **Open-source stack** (no paid all-in-one SDK). If Task 1.1 shows TWAIN driver problems, raise it again with the team lead rather than switching on your own.
- [x] **D7** ✅ **In v1 scope: AI field extraction AND legacy-site migration.**
  - AI extraction must still respect the offline constraint: design it as an **optional** `IAiExtractionProvider` that is off by default and only used when a site has a reachable AI server (e.g. the existing Flask/Qwen service) or a capable local GPU. The core capture flow must work fully without it. Design note required before building (planned for M3/M4).
  - Legacy migration (ScanStation settings via BinaryFormatter, Profiles/Properties/Selection Lists, license keys) is planned for M5. Collect sample legacy config files from a real site **now** so the format is known early (add to Task 0.1).

---

## C. Milestone 0 — Environment & risk burn-down

### Task 0.1: Fill in the TWAIN scanner inventory 👤 manual

**⏸ Deferred (2026-10-28):** skipped for now. **Must be done before Task 1.1** (the scanner spike), so 1.1 is tested on the right scanner model. Helper script: `tools/scanner-inventory.ps1`.

**File:** `TWAIN_Scanner_Inventory.xlsx` (Documentation folder of the ScanStation project)

For every scanner at every client site, record:
- site
- make / model
- driver name and version
- TWAIN 1.x or 2.x
- whether a **64-bit** TWAIN driver exists
- whether WIA is available
- duplex / ADF
- **hardware patch-code detection (yes/no)**
- typical daily volume

Also (D7): copy a set of **legacy ScanStation config files** from one real site (settings, profiles/properties/selection lists, license file) into a private `samples/legacy/` folder (git-ignored). Needed for the M5 migration design.

**Verify:** every site has at least one row. Count how many scanners have **no 64-bit driver**. That number decides whether the 32-bit worker (D2) is required or just insurance.

### Task 0.2: Dev machine + test environment

1. Install: **.NET 10 SDK**, a Visual Studio version with .NET 10 / WPF support (Community is fine), Git, **Inno Setup 6**.
2. Enable **Windows Sandbox** (or a Hyper-V VM): a clean Windows with nothing installed. Every installer test runs there, never on the dev machine.
3. Have one real scanner physically available for Milestone 1 (ideally the most common model in the inventory).

**Verify:** `dotnet --list-sdks` shows 10.x; Windows Sandbox opens; the scanner works with its vendor's own scan utility.

**Result (2026-10-28): ✅ (Inno Setup path to confirm)**, via `tools/setup-dev.ps1`.
- .NET SDKs: 10.0.403 (newest), 10.0.401, 8.0.425, 3.1.426. Task 0.3 should add a `global.json` pinning SDK `10.0.403` with `rollForward: latestFeature` so builds don't pick a different SDK.
- Visual Studio Community 2026 with the .NET desktop workload ✅; Git 2.55.0 ✅; Windows Sandbox enabled ✅.
- Inno Setup 6.7.3 installed, but the script didn't find `ISCC.exe`, likely a per-user install under `%LOCALAPPDATA%\Programs\Inno Setup 6\` (check path added to the script).

### Task 0.3: Repo + solution skeleton with the layering enforced

**Goal:** the first commit already has the §5 architecture, with the layering rule **checked by a test**, not just by code review.

1. New GitHub repo `iprofiler-desktop` with the `VisualStudio` `.gitignore`. Trunk-based branching, Conventional Commits.
2. Solution `IProfiler.Desktop.sln`:

   | Project | Type | May reference |
   |---|---|---|
   | `IProfiler.Core` | class lib, `net10.0` | nothing third-party (domain models + interfaces only) |
   | `IProfiler.Application` | class lib | Core |
   | `IProfiler.Infrastructure` | class lib, `net10.0-windows` | Core, plus **all** third-party imaging/OCR/scan/PDF libs (one folder per provider) |
   | `IProfiler.Data` | class lib | Core, EF Core SQLite |
   | `IProfiler.App` | WPF, `net10.0-windows`, **x64** | everything (composition root only) |
   | `IProfiler.Tests` | xUnit | everything + `NetArchTest.Rules` |
3. `Directory.Build.props`: `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<PlatformTarget>x64</PlatformTarget>` (per D2). Use `Directory.Packages.props` (central package versions) so every version is pinned in one place.
4. **Architecture tests** in `IProfiler.Tests`: fail the build if any type outside `IProfiler.Infrastructure` references `NTwain`, `NAPS2`, `Sdcb`, `OpenCvSharp`, `ZXing`, `PdfSharp`, `QuestPDF`, `BitMiracle`, or `Tesseract`. This automates the doc's "review-blocking finding" rule (§5.2).
5. GitHub Actions on `windows-latest`: `dotnet build` + `dotnet test` on every push/PR.

**Verify:** the solution builds; the tests pass; adding a dummy `using ZXing;` in `IProfiler.Application` makes a test fail (then remove it); CI is green.

**Result (2026-10-28): ✅**
- `global.json` pins SDK `10.0.403`, `rollForward: latestFeature`; `dotnet --version` → 10.0.403.
- `IProfiler.Desktop.sln` (classic `.sln` format) with 6 projects: `src/IProfiler.{Core,Application,Infrastructure,Data,App}` + `tests/IProfiler.Tests`. Every solution platform maps to x64.
- `Directory.Build.props` (Nullable, TreatWarningsAsErrors, PlatformTarget x64) + `Directory.Packages.props` (central versions: EF Core Sqlite 10.0.14, xunit 2.9.3, xunit.runner.visualstudio 3.1.4, Microsoft.NET.Test.Sdk 18.11.0, NetArchTest.Rules 1.3.2). No imaging/OCR/scan packages yet; they come in with their M1 tasks (NAPS2 only after its licence check).
- `.gitignore` = VisualStudio template (`dotnet new gitignore`) + the project's own local-data and secrets rules.
- Build (Debug and Release): 0 warnings, 0 errors. Tests: **15/15 passed**.
- Architecture tests (`tests/IProfiler.Tests/Architecture/LayeringTests.cs`): (a) NetArchTest IL check, no type in Core/Application/Data/App depends on the 9 restricted namespaces; (b) no restricted `PackageReference` outside Infrastructure; (c) Core has no packages; (d) project references follow the layer table; (e) every `src/*.csproj` is covered by the rules.
- Violation check: ZXing.Net 0.16.11 added to Application temporarily. With only `using ZXing;` → **1 failed** (package rule; a bare `using` leaves nothing in IL, so the IL rule alone can't see it). With a type that uses `BarcodeFormat` → **2 failed** (IL rule names `IProfiler.Application.ArchViolationProbe`, plus the package rule). Reverted → 15/15 pass.
- Fixed along the way: `App.xaml.cs` now derives from `System.Windows.Application`. Without that, as soon as `IProfiler.Application` contains a type, `Application` inside `IProfiler.App` resolves to that namespace (CS0118).
- `.github/workflows/ci.yml`: `windows-latest`, setup-dotnet from `global.json`, restore → build Release → test (trx uploaded), on every push/PR. Its steps were run locally: pass.
- ✅ Pushed: `main` at `ac1c710` is on `origin` (github.com/famipiji/iprofiler-desktop). First CI run [#40623687968](https://github.com/famipiji/iprofiler-desktop/actions/runs/40623687968) on `ac1c710`: **success**, every step green (checkout, setup-dotnet, restore, build, test, upload), about 1.5 min.
- Review (2026-10-28): approved. Follow-ups folded into Task 0.4: also restrict `Microsoft.EntityFrameworkCore` to `IProfiler.Data`, and `Serilog` sinks to `IProfiler.App`/`Infrastructure`.

### Task 0.4: Interfaces, domain model, logging, paths (no real providers yet)

**Target:** `IProfiler.Core`, `IProfiler.App`

1. **Domain (Core):**
   - `Batch`, `Document`, `Page` (3-level tree per D4). A Page stores `ImagePath` (the original TIFF, untouched), `Rotation`, `Crop`, `Order`, so edits are metadata and never re-encode the image (doc §3.6).
   - `SeparationMode` **enum** (`PageCount`, `Barcode`, `BlankPage`, `PatchCode`, `KeywordZone`), plus one typed settings record per mode. This replaces the legacy untyped int flag (doc §5.2).
2. **Interfaces (Core), written before any provider:**
   - `IScanProvider`, `IImportProvider`
   - `IOcrProvider` (full page **and** region → lines with boxes + confidence)
   - `IBarcodeProvider` (region/page → symbology, text, box)
   - `IImageProcessor` (deskew, blank ratio, rotate/crop for export)
   - `ISeparationEngine` (one `Separate(pages, mode, settings)` used by every path)
   - `IPdfAssembler`, `ITiffAssembler`
   - `IUploadClient`
   - `IHotFolderWatcher`
   - `ILicenseValidator`
   - `IAiExtractionProvider` (optional, D7): interface + a `NullAiExtractionProvider` that does nothing; no real implementation in M0–M1
3. **Fake providers** in Infrastructure that return canned data, wired via `Microsoft.Extensions.Hosting` DI in the WPF app.
4. **Serilog** rolling file at `%LOCALAPPDATA%\iProfiler\logs\`. Add a code-review rule to the README: *no empty `catch`; every catch logs*.
5. **`AppPaths`** class: install dir from `AppContext.BaseDirectory` (models, native libs) and data dir under `%LOCALAPPDATA%\iProfiler\` (SQLite, working TIFFs). **Never `Environment.CurrentDirectory`** (doc §3.3 trap).
6. A minimal main window that lists fake pages from the fake scan provider, just enough to show DI and binding work.

**Verify:** the app runs and shows fake pages; the log file appears; unit tests cover `SeparationMode` dispatch with the fake providers (one test per mode); the architecture tests still pass.

**Result (2026-10-28): ✅**
- **Core:** `Batch`/`Document`/`Page` (Page: `ImagePath`, `Rotation`, `Crop`, `Order`, hardware `PatchCode`); `SeparationMode` enum + one settings record per mode (`PageCountSettings`, `BarcodeSettings`, `BlankPageSettings`, `PatchCodeSettings`, `KeywordZoneSettings`; each carries its `Mode`); the 12 interfaces listed above; `AppPaths` (Install = `AppContext.BaseDirectory`, Data = `%LOCALAPPDATA%\iProfiler\`, plus `Logs`, `Scans`, `Models`, `Database`). Core still has no packages.
- **Application:** `SeparationEngine`, the single `SeparationMode` dispatch (one `switch` over the settings records, one shared grouping loop; never emits an empty document; renumbers documents and pages).
- **Infrastructure:** fakes for every interface in `Fakes/`, all answering from one canned 8-page batch (`FakeBatch`: keyword "INVOICE" p01/p04/p07, Code39 separator p03, blank p05, Patch T p06); `Ai/NullAiExtractionProvider` (D7, `IsAvailable = false`).
- **App:** composition root `Composition/AppHost.cs`: `Host.CreateEmptyApplicationBuilder` with content root = install dir (no appsettings/env/cwd), `ValidateOnBuild` + `ValidateScopes`, Serilog rolling daily file `%LOCALAPPDATA%\iProfiler\logs\iprofiler-YYYYMMDD.log` (31 kept). Unhandled UI/AppDomain/task exceptions are logged. `MainWindow` + `MainViewModel` list the fake scan's pages.
- **Review follow-ups (Task 0.3):** EF Core restricted to Data and Serilog restricted to App (IL check + PackageReference check); Core/Application/Infrastructure may only reference `Microsoft.Extensions.Logging.Abstractions` for logging. The review note said Serilog in App *or* Infrastructure; the 2026-10-28 instruction was **App only**, which is what's enforced.
- **Added source-rule tests:** no `Environment.CurrentDirectory` / `Directory.Get|SetCurrentDirectory` in `src/` (rule 6), no empty or comment-only `catch` (rule 5). README code-review rules updated.
- Build: 0 warnings, 0 errors. Tests: **47/47 passed** (34 layering, 2 source rules, 8 separation, 2 `AppPaths`, 1 host).
- Separation tests, one per mode on the fake batch: PageCount(4) → [p01–p04][p05–p08]; Barcode Code39 "SEP-" → [p01–p02][p04–p08], p03 removed; BlankPage 0.99 → [p01–p04][p06–p08], p05 removed; PatchCode T kept → [p01–p05][p06–p08]; KeywordZone "invoice" → [p01–p03][p04–p06][p07–p08]. Plus: every enum value has settings and dispatches; non-matching barcode filter doesn't split; order renumbered.
- Violation check: temporarily added EF Core + a `DbContext` to Application, Serilog + `Log.Information` to Infrastructure, `Microsoft.Extensions.Logging` to Core, and a `catch { }` + `Environment.CurrentDirectory` in Application → **9 failed** (each new rule caught its violation). Reverted → 47/47 pass.
- App run: `IProfiler.App.exe` started with working directory `C:\Windows`; window "iProfiler Desktop" listed the 8 fake pages; closed with exit code 0. Log written: `%LOCALAPPDATA%\iProfiler\logs\iprofiler-20261028.log` (1,054 bytes) with startup (install + data paths), "Fake scan on fake-scanner at 300 dpi", "Loaded 8 pages from Fake Scanner (TWAIN)", and shutdown lines. The host test also checks the log file appears (in a temp data folder).

- Review (2026-10-28): ✅ approved. Follow-ups:
  - **Task 0.5:** if startup fails before logging is up (e.g. data folder not writable), show a message box and write a fallback log, instead of a silent crash; also show a friendly message for unhandled UI exceptions (still log + exit).
  - **Task 1.4:** add `Page.DeskewAngle` (double?, metadata only, applied on export like `Rotation`).
  - **M3:** legacy barcode separation also has fixed-value / char-count / regex matching (doc §2), and keyword zone has regex; `BarcodeSettings` only does symbology + prefix today. Extend the settings records then.
  - **M1/M2 performance note:** each classifier re-reads the page image from disk (OCR, barcode, white ratio). Fine for now; add a per-page decode cache if batch timings need it.

### Task 0.5: Installer proven on a clean machine

1. `dotnet publish IProfiler.App -c Release -r win-x64 --self-contained true`, so the client needs **no** .NET runtime installed (doc §1: "a runtime the client's machine won't already have").
2. Inno Setup script `installer/iprofiler.iss`:
   - per-machine install to Program Files
   - Start-menu shortcut
   - clean uninstall that **keeps** user data in `%LOCALAPPDATA%` unless the user ticks "remove data"
   - version from the assembly
3. Note the code-signing gap in the README (cert needed before the pilot).

**Verify (in Windows Sandbox):** install, then launch **from the Start-menu shortcut** (this proves the path handling works outside the install dir), the fake pages show, the log is written, uninstall leaves no files in Program Files. Record the installer size (the baseline before the PaddleOCR models are added).

**Result (2026-10-28): ⏳ build done; Verify pending (manual Sandbox test by the user)**
- **Publish:** `dotnet publish src/IProfiler.App -c Release -r win-x64 --self-contained true` → `artifacts/publish/win-x64`: 150 MB, 308 files.
- **Installer:** `installer/iprofiler.iss` (Inno Setup 6.7.3) → `installer/Output/iProfilerDesktop-Setup-0.1.0.0.exe`, **46.8 MB (49,085,283 bytes)**. That is the baseline before the PaddleOCR models.
  - Per-machine install to `{autopf}\iProfiler Desktop`, x64 only.
  - Start-menu shortcut `{autoprograms}\iProfiler Desktop`, with no working directory set on purpose.
  - Version comes from the published exe (0.1.0.0).
  - Uninstall keeps `%LOCALAPPDATA%\iProfiler`. An interactive uninstall asks (default No); a silent one removes the data only with `/REMOVEDATA=1`.
  - `installer/build-installer.ps1` does both the publish and the compile. It wasn't run as a script here because the agent's permission check blocked `-ExecutionPolicy Bypass`; its two steps were run directly.
- **Review follow-up (Task 0.4):**
  - A failure before Serilog is up shows a message box and writes `%LOCALAPPDATA%\iProfiler\startup-error.log`. If that fails, it writes `%TEMP%\iProfiler-startup-error.log`, which also records why the first file failed (`App/Startup/StartupErrorLog.cs`, `AppPaths.StartupErrorLog`).
  - A failure after logging is up is logged, and the user sees a message box.
  - Unhandled UI exception: logged as critical, then a friendly message box, then `Shutdown(1)`, so `OnExit` flushes the log. Not exercised at runtime (the app has no way to trigger one).
- README: installer section, startup-error log locations, and the code-signing gap in more detail.
- Build: 0 warnings, 0 errors. Tests: **50/50 passed** (3 new `StartupErrorLog` tests, plus a `StartupErrorLog` path check in `AppPathsTests`).
- **Sandbox attempt (automated, networking disabled): ❌ blocked.** The silent install was refused by Windows inside the sandbox: *"An Application Control policy has blocked this file."* The installer is unsigned (`Get-AuthenticodeSignature`: NotSigned). Smart App Control on the host is off, so the policy is the sandbox's own. It's unknown whether it blocks all unsigned files or only files run from the mapped folder. Nothing was installed, so no Verify check ran. Per the user: no retry. **Risk for the pilot:** client PCs with App Control / Smart App Control on may block the unsigned installer and app; this adds to the code-signing gap (risk register, Task 0.6).
- **Still to do (user, manual):** in Windows Sandbox:
  1. Install, then launch from the Start-menu shortcut. Check the 8 fake pages show and `%LOCALAPPDATA%\iProfiler\logs\iprofiler-*.log` is written.
  2. Uninstall, and check `C:\Program Files\iProfiler Desktop` is gone and the data folder is kept.
  3. Optionally, make `%LOCALAPPDATA%\iProfiler\logs` a file to see the startup-error message and `startup-error.log`.
  4. Record the results here.

### Task 0.6: Backlog + process artifacts 👤 manual

1. Notion backlog board: one row per feature slice, status = the doc's phases (Design → Spike → Build → Regression → UAT → Released).
2. `progress.md` at the repo root (keep your existing habit).
3. `docs/risk-register.md`: seed from doc §6.3, plus the A-table items (x64/TWAIN, patch codes, .NET EOL, signing cert, NAPS2 licence).
4. `docs/design-notes/` folder: one half-page note per slice, signed off before the code starts.

**Verify:** team lead has seen the board and the risk register. **Milestone 0 gate: team lead OK to start the spike.**

---

## D. Milestone 1 — Architecture spike (proof, not production code)

Everything here goes in a throwaway `spike/` console project **plus** the real Infrastructure
provider classes (those are kept). No real UI yet.

### Task 1.1: Scan acquisition: NAPS2.Sdk vs NTwain on the real scanner

1. **Licence check first:** read NAPS2.Sdk's licence and confirm it's acceptable for a commercial, closed-source product. If it isn't, go straight to NTwain.
2. `Naps2ScanProvider : IScanProvider`:
   - list devices (TWAIN + WIA)
   - scan 3 pages from the ADF, then do one silent scan (no driver UI)
   - save each page as an **untouched TIFF** under `AppPaths.Data`
3. From the **x64** process: try the scanner directly first; if the driver is 32-bit only, enable the Win32 worker (`SetUpWin32Worker()`) and retry.
4. If NAPS2 fails or the licence is a no: the same provider with NTwain, and note how much worker plumbing you'd have to write yourself.
5. Check whether the scanner reports **hardware patch codes** through TWAIN (A6).

**Verify:** 3 TIFFs on disk from an x64 build; record (driver, direct vs worker, silent scan OK, patch codes reported yes/no) in `docs/spike-report.md`.

### Task 1.2: OCR: PaddleOCR in-process, fully offline

1. `PaddleOcrProvider : IOcrProvider` using **Sdcb.PaddleOCR** + `Sdcb.PaddleInference.runtime.win64.mkl` + `OpenCvSharp4.runtime.win`.
2. **Models from local files only.** Ship the PP-OCRv5 **mobile** det + English rec model files under `AppPaths.Install\models\`. **No online model download at runtime**: the client site may be offline. Test with the network disabled.
3. One shared instance, guarded by a lock (not thread-safe); loaded lazily on first use.
4. Before OCR, downscale so the longest side is at most ~1600 px; scale the boxes back. Filter lines below confidence 0.5 (removes watermark noise).
5. Region OCR: crop a rectangle from the page, then OCR it. This is the core of "FlyOCR".

**Verify (network off):** full-page OCR of a scanned TIFF and a region OCR of one field both return correct text; log the time per page (expect roughly 1–5 s on CPU based on the Flask migration's numbers) and the model-files size; record both in the spike report.

### Task 1.3: Barcode + QR: ZXing.Net

1. `ZXingBarcodeProvider : IBarcodeProvider` (ZXing.Net with OpenCvSharp bindings, so it reuses the same image types as OCR).
2. Read Code39, Code128 and QR from the full page and from a drawn region (FlyBCR / FlyQR).
3. Patch codes: if Task 1.1 showed the scanner reports them, read them from the scan metadata; otherwise prototype the OpenCvSharp bar-pattern check on a sample patch sheet.

**Verify:** a test page with one of each symbology decodes correctly; patch-code approach recorded in the spike report.

### Task 1.4: Image ops: deskew, blank page, rotation as metadata

1. `OpenCvImageProcessor : IImageProcessor`:
   - deskew-angle detection
   - blank-page white-pixel ratio (threshold configurable)
   - apply rotation/crop **only on export**
2. Confirm the original TIFF is byte-identical after rotating a page in the model.

**Verify:** a skewed scan gets a sensible angle; a blank page and a near-blank page (a page with just a staple shadow) are classified correctly at the default threshold; hash of the original TIFF unchanged.

### Task 1.5: SQLite + PDF/TIFF output

1. `IProfiler.Data`: EF Core SQLite with `Batch`, `Document`, `Page` (+ the EF migration). Metadata only, no image blobs (doc §3.7).
2. `PdfSharpAssembler : IPdfAssembler`: image pages + **invisible OCR text layer** at the word positions, so the PDF is searchable (the "PDF-OCR" export). Use PDFsharp for the spike to sidestep the QuestPDF licence question.
3. `LibTiffAssembler : ITiffAssembler`: multi-page TIFF.

**Verify:** the PDF opens in a normal reader and Ctrl+F finds 3 OCR'd words in the right spot; the multi-page TIFF opens with all pages in order; the DB file contains only metadata.

### Task 1.6: End-to-end run + spike report → go/no-go gate

1. `spike/` console: scan 3 pages → OCR one field region → read one barcode → write rows to SQLite → assemble one PDF + one TIFF.
2. Add the models + native runtimes to the Inno Setup installer; install in **Windows Sandbox with the network disabled** and run the end-to-end flow there (scanner passed through if possible, otherwise from a saved TIFF).
3. `docs/spike-report.md`:
   - timings per step
   - installer size
   - x64 / worker result
   - patch-code approach
   - OCR accuracy on 5 real sample scans
   - open risks
   - **a go/no-go recommendation on D6 (open-source vs paid SDK)**

**Verify:** the end-to-end run works offline from the installed app. **Milestone 1 gate: team lead signs off on the spike report before any Milestone 2 UI is built.**

---

## E. Milestones 2–6 (outline; each gets its own task file after the M1 gate)

| Milestone | Scope (from doc §7, with the A-table fixes) |
|---|---|
| **M2 Core capture** | Scan/import UI, explorer tree (Batch → Document → Page, D4), thumbnail panel with drag-reorder (GongSolutions.WPF.DragDrop), rotate/deskew/delete, **one viewer control** that owns all zoom/pan → image-pixel math, reused for region capture |
| **M3 Indexing & separation** | Profile / Property / Selection List admin screens (MVVM); FlyOCR/BCR/QR region capture into fields; the single typed `ISeparationEngine` with all 5 modes; regression tests per mode; **design note for optional AI field extraction (D7)** |
| **M4 Hot folder & export/upload** | FileSystemWatcher + debounce + 30–60 s reconciliation poll (also covers network shares, where the watcher is least reliable); PDF / PDF-OCR / TIFF + CSV/TXT/JSON/XML sidecar; M-Files/iVault upload with HttpClientFactory + Polly + logging; Google Drive (current Google.Apis.Drive.v3) |
| **M5 Reporting, licensing, packaging** | Reporting (scope with the team first); signed offline license file (RSA/ECDSA, no keys in source, DPAPI for secrets); **legacy-site migration** (in scope, D7: import old settings/profiles/licenses into the new format); code-signed installer; FlaUI regression suite |
| **M6 Pilot & rollout** | One real site first; run the regression checklist there; feedback; then the wider rollout |

---

## F. Lessons from the Flask PaddleOCR migration (Oct 2026) that apply here

- **Mobile models:** about 3× faster than server models on CPU, and at least as accurate on receipts and invoices. Default to mobile.
- **Downscale:** shrinking to about 1600 px longest side took a receipt from 9.6 s to 3.4 s with no loss of useful text.
- **Word boxes:** PaddleOCR gives **lines**. Use its real word boxes for searchable-PDF text and highlights; splitting a line by character count misplaces words when big and small text share a line.
- **Hidden text width:** in the searchable PDF, stretch each word to its box width, or phrase search breaks.
- **Confidence filter:** a line-confidence cutoff around 0.5 removes most faint watermark text.
- **Concurrency and memory:** one engine instance, one call at a time; it's hundreds of MB in memory.
- **Windows DLL clashes:** two native runtimes can ship the same DLL (e.g. `libiomp5md.dll`), and load order then matters. Test the installed app on a clean machine, not just the dev box.
- **Jawi:** Jawi (Arabic script) needs a different model and right-to-left handling. Keep it out of scope unless a client needs it.
