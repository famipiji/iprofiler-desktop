"""Checks the Task 1.6 end-to-end output (IProfiler.Spike e2e) in real readers, on the host or on results copied
back from Windows Sandbox:
  - PDF-OCR: page count, and every expected word found by search in MuPDF (PyMuPDF) and PDFium (pypdfium2) on
    its own page
  - multi-page TIFF: page count, and each page the same size as the PDF page (Pillow)
  - SQLite: the pages are stored, and no column holds a BLOB

    python tools/check-e2e-output.py <out dir> <samples dir>

Writes check.json into the out dir; exit code 0 only if every check passes.
"""
import json
import sqlite3
import sys
from pathlib import Path

import pymupdf
import pypdfium2 as pdfium
from PIL import Image


def main(out_dir: Path, samples: Path) -> int:
    report = json.loads((out_dir / "report.json").read_text(encoding="utf-8"))
    expected = json.loads((samples / "expected.json").read_text(encoding="utf-8"))
    words = {name: spec["words"] for name, spec in expected.items()}
    words["bw-g4-300dpi.tif"] = json.loads((samples / "bw-g4-300dpi.json").read_text(encoding="utf-8"))["words"]
    pages = report["pages"]
    pdf_path, tiff_path = out_dir / "batch.pdf", out_dir / "batch.tif"
    result = {"pages": len(pages)}

    mu = pymupdf.open(pdf_path)
    pdfium_doc = pdfium.PdfDocument(str(pdf_path))
    result["pdfPages"] = mu.page_count
    found = {"mupdf": 0, "pdfium": 0}
    missing = []
    total = 0
    for i, page in enumerate(pages):
        for word in words.get(page["file"], []):
            total += 1
            in_mu = bool(mu[i].search_for(word))
            text_page = pdfium_doc[i].get_textpage()
            searcher = text_page.search(word, match_case=True)
            in_pdfium = searcher.get_next() is not None
            found["mupdf"] += in_mu
            found["pdfium"] += in_pdfium
            if not (in_mu and in_pdfium):
                missing.append({"page": i, "word": word, "mupdf": in_mu, "pdfium": in_pdfium})
    result["pdfSearch"] = {k: f"{v}/{total}" for k, v in found.items()}
    result["pdfMissing"] = missing

    with Image.open(tiff_path) as tiff:
        sizes = []
        for i in range(tiff.n_frames):
            tiff.seek(i)
            sizes.append((tiff.size, tiff.mode, tiff.info.get("dpi")))
    result["tiffPages"] = len(sizes)
    # PDF page size in points x dpi / 72 = TIFF page size in pixels (within 2 px for rounding).
    result["tiffMatchesPdf"] = all(
        abs(w - mu[i].rect.width * dpi[0] / 72) <= 2 and abs(h - mu[i].rect.height * dpi[1] / 72) <= 2
        for i, ((w, h), _, dpi) in enumerate(sizes))
    result["tiffModes"] = [m for _, m, _ in sizes]

    db = out_dir / "data" / "iprofiler.db"
    with sqlite3.connect(db) as con:
        stored = con.execute("SELECT COUNT(*) FROM Pages").fetchone()[0]
        blobs = 0
        for (table,) in con.execute("SELECT name FROM sqlite_master WHERE type = 'table'"):
            columns = [c[1] for c in con.execute(f'PRAGMA table_info("{table}")')]
            for column in columns:
                blobs += con.execute(f'SELECT COUNT(*) FROM "{table}" WHERE typeof("{column}") = \'blob\'').fetchone()[0]
    result["dbPages"] = stored
    result["dbBlobValues"] = blobs

    result["ok"] = (result["pdfPages"] == len(pages) and not missing and result["tiffPages"] == len(pages)
                    and result["tiffMatchesPdf"] and stored == len(pages) and blobs == 0)
    (out_dir / "check.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))
    return 0 if result["ok"] else 1


if __name__ == "__main__":
    sys.exit(main(Path(sys.argv[1]), Path(sys.argv[2])))
