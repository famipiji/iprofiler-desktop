# iProfiler Desktop

Standalone, offline Windows capture application (scan, OCR, barcode/QR, separation, indexing,
export, upload). Ground-up rebuild of iProfiler ScanStation.

- Plan and tasks: [`.tasks/`](./.tasks/INDEX.md)
- Rules for contributors (human and AI): [`CLAUDE.md`](./CLAUDE.md)
- Progress: [`progress.md`](./progress.md)

**Code-review rules** (review-blocking; most are enforced by `tests/IProfiler.Tests/Architecture`):

- **No empty `catch`; every catch logs** (or rethrows).
- Third-party imaging/OCR/scan/PDF namespaces only inside `IProfiler.Infrastructure`.
- EF Core only inside `IProfiler.Data`.
- Log through `ILogger<T>` (`Microsoft.Extensions.Logging.Abstractions`); Serilog is configured only in
  `IProfiler.App`.
- Paths come from `AppPaths`, never from the current working directory.

## Installer

```powershell
installer\build-installer.ps1                       # publish (self-contained win-x64) + Inno Setup compile
installer\build-installer.ps1 -VcRuntimeDir <dir>   # if VS has no C++ Redist folder installed
installer\build-installer.ps1 -WithSpike            # Milestone 1 only: adds IProfiler.Spike.exe (-spike.exe)
```

The installer also puts the **Visual C++ runtime** (`msvcp140`, `vcruntime140`, `vcruntime140_1`, `vcomp140`)
next to the exe: PaddleOCR's and MKL's native DLLs need it, and a clean Windows may not have it (risk R17). By
default the script takes it from Visual Studio's `VC\Redist\MSVC\<ver>\x64` folder (the VS "MSVC C++ x64 build
tools" component); release builds must use that folder.

Output: `installer\Output\iProfilerDesktop-Setup-<version>.exe`. The version comes from `<Version>`
in `Directory.Build.props`. Per-machine install to Program Files with a Start-menu shortcut; no .NET
runtime is needed on the client. Uninstall keeps the user's data in `%LOCALAPPDATA%\iProfiler`
unless they choose to remove it (silent uninstall: pass `/REMOVEDATA=1`). Test installs in
**Windows Sandbox** only, launched from the Start-menu shortcut.

If startup fails before logging is running, the app shows a message and writes
`%LOCALAPPDATA%\iProfiler\startup-error.log` (or `%TEMP%\iProfiler-startup-error.log` if that
folder isn't writable). After startup, everything goes to `%LOCALAPPDATA%\iProfiler\logs\`.

**Code-signing gap (must close before the pilot):** the installer and `IProfiler.App.exe` are
**unsigned**. Client sites will see SmartScreen "Windows protected your PC / unknown publisher"
warnings, and some sites' policies block unsigned installers outright. A code-signing certificate
(OV or EV) is needed, then sign both the app exe and the setup exe (Inno `SignTool`).

- Backlog board (Notion): https://app.notion.com/p/402328f456c0472a9729eaf1879fbbe2
- Risks: [`docs/risk-register.md`](./docs/risk-register.md) · Design notes: [`docs/design-notes/`](./docs/design-notes/TEMPLATE.md)
