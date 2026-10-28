# Risk Register: iProfiler Desktop

Living document. Seeded from the rebuild doc (§6.3) plus everything found in Milestone 0.
Review at every milestone gate; add new risks as they surface (Maintenance phase feeds back here).

**Scales:** Impact / Likelihood = High · Medium · Low. **Status:** Open · Mitigating · Watching · Closed.

| ID | Risk | Impact | Likelihood | Mitigation | Owner | Status | Source / updated |
|---|---|---|---|---|---|---|---|
| R1 | **Unsigned installer/app blocked by Smart App Control / App Control** on client PCs (seen in Windows Sandbox: "could not verify its publisher") | **High**: install fails at a client site | **High**: SAC is on for many clean Windows 11 installs | Buy a code-signing certificate (OV, or a cloud signing service if available for the company); set `AppPublisher` to the legal company name first; sign app exe + setup via Inno `SignTool`; re-test on a clean PC with SAC **on**. **Pilot blocker.** | Team lead (purchase), dev (signing) | Open: purchase **deferred** (2026-10-28); must be done before the pilot (M6) | Task 0.5, 2026-10-28 |
| R2 | Scanner drivers at client sites are 32-bit only / unknown | High: specific scanners can't be used | High: inventory not filled yet | D2: x64 app + 32-bit TWAIN worker. Fill the scanner inventory (Task 0.1, `tools/scanner-inventory.ps1`) before Task 1.1; test 1.1 on a 32-bit-only model if any exist. | Dev | Mitigating | Doc §6.3, D2 |
| R3 | 32-bit TWAIN worker (NAPS2.Sdk) licence not acceptable for a closed-source commercial product | High: fall back to NTwain + build the worker ourselves | Medium | Licence check is step 1 of Task 1.1, before any NAPS2 code. Fallback: NTwain. | Dev → team lead | Open | A8 |
| R4 | Patch-code separation has no library (ZXing doesn't read patch codes) | Medium: one of 5 separation modes missing | Medium | Prefer hardware patch-code detection via TWAIN (record per scanner in the inventory); software fallback with OpenCvSharp. Decide in Task 1.3. | Dev | Open | A6 |
| R5 | PaddleOCR footprint/speed on client hardware (models + native runtime, CPU-only) | Medium: big installer, slow OCR on old PCs | Medium | Mobile PP-OCRv5 models; downscale to ~1600 px; measure installer size and time per page in Task 1.2/1.6; Tesseract fallback behind `IOcrProvider`. | Dev | Open | Doc §3.3, Flask migration lessons |
| R6 | OCR accuracy on poor-quality real scans disappoints users | Medium: capture UX rework | Medium | Test on 5+ real sample scans in Task 1.6, not post-launch. | Dev | Open | Doc §6.3 |
| R7 | Native DLL conflicts between runtimes (PaddleInference, OpenCvSharp, TWAIN worker) | Medium: crashes on some machines | Low–Medium | Always test the *installed* build on a clean machine (Sandbox); log native load failures. Seen before: `libiomp5md.dll` clash in the Flask migration. | Dev | Watching | Flask migration |
| R8 | Solo developer = single point of knowledge | Medium | Medium | Mainstream libraries; CLAUDE.md + design notes + task files kept current; architecture enforced by tests, not memory. | Dev + team lead | Mitigating | Doc §6.3 |
| R9 | Offline licence-file scheme repeats old security mistakes | Medium | Medium | RSA/ECDSA signature via a maintained library; no keys in source; DPAPI for secrets. Design note before M5. | Dev | Open | Doc §6.3 |
| R10 | Legacy-site migration (BinaryFormatter settings, profiles, licence keys) harder than expected (D7) | Medium: sites can't upgrade smoothly | Medium | Collect real legacy config samples early (Task 0.1); read BinaryFormatter data in a one-off isolated converter, never in the main app; migration design note before M5. | Dev | Open | D7 |
| R11 | Optional AI extraction (D7) leaks into the core flow or breaks the offline promise | Medium | Low–Medium | `IAiExtractionProvider` is optional and off by default (`NullAiExtractionProvider`); capture flow must pass all tests with AI disabled; design note in M3. | Dev | Mitigating | D7 |
| R12 | QuestPDF licence terms don't fit the company | Low–Medium | Low | Using PDFsharp (MIT) for the spike; revisit QuestPDF only with a licence check. | Dev | Watching | Doc §6.3 |
| R13 | .NET support window | Low (now) | Low | Moved to .NET 10 LTS (supported to Nov 2028), since .NET 8 ends 10 Nov 2026. Plan the next LTS upgrade before Nov 2028. | Dev | Closed (mitigated) | A1 / D1 |
| R14 | Build suddenly fails when a NuGet package gets a security advisory (warnings as errors) | Low: CI red without code change | Medium | Intended: update the package, never silence the warning. | Dev | Watching | Task 0.3 review |

## Change log
- 2026-10-28: Register created (Task 0.6). R1 added from the Task 0.5 Sandbox test.
- 2026-10-28: R1 certificate purchase deferred by the user; revisit before M5/M6. Development continues unsigned; installer tests use Sandbox with SAC off.
