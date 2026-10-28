"""Checks the Task 1.5 spike export in real PDF readers: phrase search, highlight positions, invisible text,
page sizes; plus the multi-page TIFF and that the SQLite file holds metadata only.

    IProfiler.Spike export tests/IProfiler.Tests/TestData <out>
    python tools/check-searchable-pdf.py <out>/manifest.json

Readers: MuPDF (PyMuPDF) and PDFium (pypdfium2, the engine in Chrome and Edge). A phrase passes when the
reader finds it and its highlight is on the phrase's true position (from tools/make-pdf-samples.py): it
covers the same horizontal extent (overlap >= 0.8, or both ends within 1.5 pt for short words), the true
text's vertical centre is inside it, and it is at most 3x the ink height. (Highlights span the font's ascent to descent, so they are taller than the
tight ink boxes of the truth; plain IoU is reported too.) Writes check.json next to the manifest; exit
code 0 only if every check passes.
"""
import json
import sqlite3
import sys
from pathlib import Path

import pymupdf
import pypdfium2 as pdfium
from PIL import Image

MIN_X_OVERLAP = 0.8
EDGE_TOLERANCE_PT = 1.5  # ~6 px at 300 dpi: invisible in a highlight, and 2-letter words need it
MAX_HEIGHT_RATIO = 3.0


def iou(a, b):
    ax0, ay0, ax1, ay1 = a
    bx0, by0, bx1, by1 = b
    ix = max(0.0, min(ax1, bx1) - max(ax0, bx0))
    iy = max(0.0, min(ay1, by1) - max(ay0, by0))
    inter = ix * iy
    union = (ax1 - ax0) * (ay1 - ay0) + (bx1 - bx0) * (by1 - by0) - inter
    return inter / union if union > 0 else 0.0


def union(rects):
    return (min(r[0] for r in rects), min(r[1] for r in rects), max(r[2] for r in rects), max(r[3] for r in rects))


def expected_words(spec, page_info):
    """True word boxes in PDF points (top-left origin) on the exported page."""
    crop = page_info.get("crop") or [0, 0, 0, 0]
    k = 72.0 / spec["dpi"]
    return [(w["text"], ((w["box"][0] - crop[0]) * k, (w["box"][1] - crop[1]) * k,
                         (w["box"][0] + w["box"][2] - crop[0]) * k, (w["box"][1] + w["box"][3] - crop[1]) * k))
            for w in spec["words"]]


def phrase_box(words, phrase):
    """Union of the consecutive true words that make up the phrase."""
    tokens = phrase.split()
    texts = [t for t, _ in words]
    for i in range(len(texts) - len(tokens) + 1):
        if texts[i:i + len(tokens)] == tokens:
            return union([b for _, b in words[i:i + len(tokens)]])
    raise ValueError(f"phrase {phrase!r} not in the page's words")


def mupdf_hits(page, text, want):
    """MuPDF returns one rect per line of a hit; the rects near the true position are joined into one hit."""
    h = want[3] - want[1]
    near = [tuple(r) for r in page.search_for(text)
            if want[0] - h <= (r[0] + r[2]) / 2 <= want[2] + h and want[1] - 2 * h <= (r[1] + r[3]) / 2 <= want[3] + 2 * h]
    others = [tuple(r) for r in page.search_for(text) if tuple(r) not in near]
    return ([union(near)] if near else []) + others


def pdfium_hits(textpage, height, text):
    hits = []
    searcher = textpage.search(text, match_case=True)
    while (found := searcher.get_next()) is not None:
        start, count = found
        # Loose boxes: what the viewer highlights (font ascent to descent), not the glyph ink.
        boxes = [textpage.get_charbox(i, loose=True) for i in range(start, start + count)]
        boxes = [(l, height - t, r, height - b) for l, b, r, t in boxes if r > l]
        if boxes:
            hits.append(union(boxes))
    searcher.close()
    return hits


def evaluate(hits, want):
    """The hit closest to the true box: horizontal overlap, whether it is on the right line, IoU, centre offset."""
    if not hits:
        return {"found": False, "pass": False, "hits": 0}

    def x_overlap(r):
        inter = max(0.0, min(r[2], want[2]) - max(r[0], want[0]))
        return inter / (max(r[2], want[2]) - min(r[0], want[0]))

    rect = max(hits, key=lambda r: (x_overlap(r), iou(r, want)))
    cy = (want[1] + want[3]) / 2
    on_line = rect[1] <= cy <= rect[3]
    height_ratio = (rect[3] - rect[1]) / (want[3] - want[1])
    dx = (rect[0] + rect[2]) / 2 - (want[0] + want[2]) / 2
    dy = (rect[1] + rect[3]) / 2 - cy
    xo = x_overlap(rect)
    edges = abs(rect[0] - want[0]) <= EDGE_TOLERANCE_PT and abs(rect[2] - want[2]) <= EDGE_TOLERANCE_PT
    return {"found": True, "hits": len(hits), "xOverlap": round(xo, 3), "onLine": on_line,
            "heightRatio": round(height_ratio, 2), "iou": round(iou(rect, want), 3),
            "offsetPt": round((dx * dx + dy * dy) ** 0.5, 1),
            "pass": (xo >= MIN_X_OVERLAP or edges) and on_line and height_ratio <= MAX_HEIGHT_RATIO}


def check_pdf(manifest, truth):
    results = {"pages": []}
    ok = True
    mu = pymupdf.open(manifest["pdf"])
    pd = pdfium.PdfDocument(manifest["pdf"])
    for index, info in enumerate(manifest["pages"]):
        mpage, ppage = mu[index], pd[index]
        width, height = ppage.get_size()
        textpage = ppage.get_textpage()
        traces = mpage.get_texttrace()
        page = {
            "file": info["file"], "rotation": info["rotation"], "deskew": info["deskew"], "crop": info["crop"],
            "sizePt": [round(width, 1), round(height, 1)],
            "textSpans": len(traces),
            # MuPDF span type 3 = invisible (render mode 3).
            "allTextInvisible": all(t["type"] == 3 for t in traces),
            "pdfiumText": textpage.get_text_range().replace("\r\n", "\n").strip(),
        }
        ok &= page["allTextInvisible"] and page["textSpans"] > 0
        spec = truth["pages"].get(info["file"])
        if spec:
            words = expected_words(spec, info)
            page["phrases"] = []
            for phrase in truth["phrases"]:
                want = phrase_box(words, phrase)
                m = evaluate(mupdf_hits(mpage, phrase, want), want)
                p = evaluate(pdfium_hits(textpage, height, phrase), want)
                ok &= m["pass"] and p["pass"]
                page["phrases"].append({"phrase": phrase, "mupdf": m, "pdfium": p, "pass": m["pass"] and p["pass"]})
            # Every single word, in both readers (case-sensitive in PDFium, so "simen" and "Simen" are told apart).
            scores = [(t, evaluate(mupdf_hits(mpage, t, b), b), evaluate(pdfium_hits(textpage, height, t), b)) for t, b in words]
            page["words"] = {"total": len(words),
                             "mupdf": sum(m["pass"] for _, m, _ in scores), "pdfium": sum(p["pass"] for _, _, p in scores),
                             "meanXOverlap": round(sum(p.get("xOverlap", 0) for _, _, p in scores) / len(words), 3),
                             "missed": [t for t, m, p in scores if not (m["pass"] and p["pass"])]}
        textpage.close()
        results["pages"].append(page)
    results["ok"] = ok
    return results


def check_tiff(path):
    frames = []
    with Image.open(path) as im:
        for i in range(im.n_frames):
            im.seek(i)
            frames.append({"size": list(im.size), "mode": im.mode, "dpi": [round(v) for v in im.info.get("dpi", (0, 0))],
                           "compression": im.info.get("compression")})
    return frames


def check_db(path):
    con = sqlite3.connect(path)
    tables = [r[0] for r in con.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")]
    columns = {t: [f"{r[1]} {r[2]}" for r in con.execute(f"PRAGMA table_info('{t}')")] for t in tables}
    blobs = sum(con.execute(f"SELECT COUNT(*) FROM \"{t}\" WHERE " +
                            " OR ".join(f"typeof(\"{c.split()[0]}\") = 'blob'" for c in cols)).fetchone()[0]
                for t, cols in columns.items())
    pages = [dict(zip(("ImagePath", "Rotation", "DeskewAngle", "Crop"), (Path(r[0]).name, r[1], r[2], r[3])))
             for r in con.execute("SELECT ImagePath, Rotation, DeskewAngle, "
                                  "CASE WHEN Crop_X IS NULL THEN NULL ELSE Crop_X||','||Crop_Y||','||Crop_Width||','||Crop_Height END "
                                  "FROM Pages ORDER BY rowid")]
    con.close()
    raw = Path(path).read_bytes()
    return {"bytes": len(raw), "tables": columns, "blobValues": blobs,
            "imageSignatures": sum(raw.count(s) for s in (b"II*\x00", b"MM\x00*", b"\x89PNG", b"\xff\xd8\xff")),
            "pages": pages}


def main():
    manifest_path = Path(sys.argv[1])
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    truth = json.loads(Path(manifest["phrases"]).read_text(encoding="utf-8"))
    report = {"pdf": check_pdf(manifest, truth), "tiff": check_tiff(manifest["tiff"]), "db": check_db(manifest["database"])}
    db_ok = report["db"]["blobValues"] == 0 and report["db"]["imageSignatures"] == 0
    tiff_ok = len(report["tiff"]) == len(manifest["pages"])
    report["ok"] = report["pdf"]["ok"] and db_ok and tiff_ok
    (manifest_path.parent / "check.json").write_text(json.dumps(report, indent=2), encoding="utf-8")

    for page in report["pdf"]["pages"]:
        print(f"page {page['file']}: {page['sizePt']} pt, {page['textSpans']} spans, invisible={page['allTextInvisible']}")
        for p in page.get("phrases", []):
            def fmt(r):
                if not r["found"]:
                    return "NOT FOUND".ljust(38)
                return f"x {r['xOverlap']:.2f} line {'y' if r['onLine'] else 'n'} h {r['heightRatio']:.1f}x off {r['offsetPt']:>4} pt"
            print(f"   {'PASS' if p['pass'] else 'FAIL'} {p['phrase']!r:36} mupdf {fmt(p['mupdf'])} | pdfium {fmt(p['pdfium'])}")
        if "words" in page:
            w = page["words"]
            print(f"   words: mupdf {w['mupdf']}/{w['total']}, pdfium {w['pdfium']}/{w['total']}, "
                  f"mean x-overlap {w['meanXOverlap']}, missed {w['missed']}")
    print("tiff:", report["tiff"])
    print("db:", {k: v for k, v in report["db"].items() if k != "pages"})
    print("db pages:", report["db"]["pages"])
    print("OK" if report["ok"] else "FAILED")
    return 0 if report["ok"] else 1


if __name__ == "__main__":
    sys.exit(main())
