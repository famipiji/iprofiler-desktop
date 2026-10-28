# Milestone 1 spike report

Filled in task by task (run order 1.2 → 1.3 → 1.4 → 1.5 → 1.1 → 1.6). Task 1.6 adds the summary and
the go/no-go recommendation on D6.

## Task 1.2: OCR (PaddleOCR in-process, offline), 2026-10-28

**Setup**
- `PaddleOcrProvider : IOcrProvider` (`src/IProfiler.Infrastructure/Ocr/`).
- Libraries: Sdcb.PaddleOCR 3.3.1 + Sdcb.PaddleInference.runtime.win64.mkl 3.3.1.70 + OpenCvSharp4 / OpenCvSharp4.runtime.win
  4.11.0.20250507 (the version Sdcb.PaddleOCR is built against).
- Models: PP-OCRv5 mobile det + English mobile rec, Paddle 3 format, in the repo under `models/` (source and hashes in
  `models/README.md`). They are copied to `<install>\models\` and read through `AppPaths.Models`. No Sdcb "Online" model
  package: a test fails if one is ever added.
- One engine, lazily loaded inside a lock, one call at a time. Pages are downscaled to 1600 px longest side; boxes are
  scaled back to original pixels. Lines below 0.5 confidence are dropped. Region OCR crops the rectangle, adds a 16 px
  white margin, OCRs it, and drops text fragments whose centre lies outside the region.
- Samples: no real scans yet in `samples/spike/`, so `tools/make-ocr-samples.py` generates synthetic "scans" (made-up
  text): an A4 300 dpi LZW TIFF invoice (0.6° skew, noise, blur, faint "COPY" watermark) and an A4 200 dpi JPEG letter
  (Times, JPEG q70). **Re-run on real scans in Task 1.6** (the plan asks for 5 real samples).

**Results** (dev PC, 8 logical CPUs, self-contained Release build)

| Measure | Result |
|---|---|
| Model files | 6 files, **12.3 MB** (det 4.7 MB, rec 7.8 MB) |
| Engine load (first call) | 1.0–1.9 s |
| Full page, invoice 2480×3508 TIFF | 29 lines, **6/6** expected words; **3.4–3.8 s** warm (5.7 s first call incl. load) |
| Full page, letter 1654×2339 JPEG | 9 lines, **6/6** expected words; **2.8 s** warm |
| Region OCR, invoice-number field (351×92 px) | `INV-2026-10457` ✅ exact; 0.9–1.1 s |
| Network | Host run with a dead proxy: **0 TCP/UDP endpoints** opened by the process (polled every 100 ms) |

Within the expected 1–5 s per page on CPU.

**Offline test in Windows Sandbox (networking disabled): blocked.** In the sandbox, the network was confirmed off: ping
failed with "General failure" and DNS had no server. But App Control blocked the unsigned spike exe ("blocked by your
organization's Device Guard policy"). This is the same unsigned-binary block as the Task 0.5 installer (risk R1, code
signing). Pending a manual run, as for Task 0.5.

**Findings**
- **Word boxes:** Sdcb.PaddleOCR gives lines plus per-character text, but **no character positions** (`RecognizedChar.Index`
  is the index in the string). Word boxes are therefore taken from the line's pixels: the n−1 widest ink-free column gaps
  are the spaces. This is not a character-count split. Check it against the searchable PDF in Task 1.5. If it isn't good
  enough, the fallback is to run the recogniser ourselves and keep the CTC time-step positions.
- **Reading order:** sorting lines by Y broke on the 0.6° skew (table columns came out right-to-left). Lines are now
  grouped into rows first.
- **Watermark:** the generated "COPY" watermark (grey 235) wasn't detected at all, so the 0.5 confidence filter wasn't
  exercised here. Re-check it on real scans that have watermarks.
- **Native size:** the publish is 458 MB uncompressed. The largest files are `paddle_inference_c` 89 MB, `mklml` 88 MB,
  `OpenCvSharpExtern` 59 MB, `mkldnn` 46 MB and `phi` 44 MB. Probably unneeded: `opencv_videoio_ffmpeg` (27 MB),
  `onnxruntime` + `paddle2onnx` (11 MB). Trim these in Task 1.6 and measure the installer size there.
- Paddle's native logging writes a few harmless lines to stderr (deprecated-API warning, oneDNN version).
