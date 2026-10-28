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

**Before pilot:** a code-signing certificate is required for the installer (SmartScreen).
