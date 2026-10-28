# iProfiler Desktop

Standalone, offline Windows capture application (scan, OCR, barcode/QR, separation, indexing,
export, upload). Ground-up rebuild of iProfiler ScanStation.

- Plan and tasks: [`.tasks/`](./.tasks/INDEX.md)
- Rules for contributors (human and AI): [`CLAUDE.md`](./CLAUDE.md)
- Progress: [`progress.md`](./progress.md)

**Code-review rules:** no empty `catch` (always log); third-party imaging/OCR/scan/PDF
namespaces only inside `IProfiler.Infrastructure`; no paths from the current working directory.

**Before pilot:** a code-signing certificate is required for the installer (SmartScreen).
