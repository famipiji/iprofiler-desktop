"""Generates 1-bit CCITT G4 TIFF test pages for barcode / QR / patch-code reading (Task 1.3).

    python tools/make-barcode-samples.py

Bilevel G4 at 300 dpi is what production scanners usually output (the default ScanColorMode).
The barcodes are drawn with zxing-cpp, so the writer is independent of the ZXing.Net reader under test.
All text is made up.

Writes to tests/IProfiler.Tests/TestData/:
  barcode/bw-g4-300dpi.tif    A4 delivery note: text + Code39 + Code128 + QR, 0.5 deg skew, speckle noise
  barcode/bw-g4-300dpi.json   expected values, symbologies and regions (pixels), plus words for the OCR check
  patch/patch-<code>.tif      one A4 patch sheet per patch code (1, 2, 3, 4, 6, T), bars near the leading (top) edge
and copies the delivery note to samples/spike/ for the spike console.
"""
import json
import math
import random
import shutil
from pathlib import Path

import numpy as np
import zxingcpp
from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parent.parent
FONTS = Path("C:/Windows/Fonts")
DPI = 300
A4 = (2480, 3508)
random.seed(1103)

# Kodak patch codes: two wide (0.20") and two narrow (0.08") bars, narrow (0.08") spaces.
PATCHES = {"1": "WWNN", "2": "WNNW", "3": "WNWN", "4": "NWWN", "6": "NNWW", "T": "NWNW"}


def font(name, size):
    return ImageFont.truetype(str(FONTS / name), size)


def barcode(text, fmt, scale, height=None):
    img = zxingcpp.create_barcode(text, fmt).to_image(scale=scale, add_quiet_zones=True)
    im = Image.fromarray(np.array(img)).convert("L")
    if height:
        im = im.resize((im.width, height), Image.NEAREST)
    return im


def rotate_point(x, y, deg, w, h):
    a = math.radians(-deg)  # PIL rotates counter-clockwise around the centre
    cx, cy = w / 2, h / 2
    dx, dy = x - cx, y - cy
    return cx + dx * math.cos(a) - dy * math.sin(a), cy + dx * math.sin(a) + dy * math.cos(a)


def region(box, skew, pad):
    x, y, bw, bh = box
    pts = [rotate_point(px, py, skew, *A4) for px, py in ((x, y), (x + bw, y), (x, y + bh), (x + bw, y + bh))]
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    return [int(min(xs) - pad), int(min(ys) - pad), int(max(xs) - min(xs) + 2 * pad), int(max(ys) - min(ys) + 2 * pad)]


def to_bilevel(img, skew, speckle):
    img = img.rotate(skew, resample=Image.BICUBIC, fillcolor=255)
    bw = img.point(lambda v: 255 if v > 150 else 0).convert("1")
    px = bw.load()
    for _ in range(int(A4[0] * A4[1] * speckle)):
        px[random.randrange(A4[0]), random.randrange(A4[1])] = 0
    return bw


def delivery_note(out_dir):
    img = Image.new("L", A4, 255)
    d = ImageDraw.Draw(img)
    d.text((200, 180), "HARBOUR LOGISTICS BERHAD", font=font("arialbd.ttf", 64), fill=0)
    d.text((200, 280), "DELIVERY NOTE", font=font("arialbd.ttf", 56), fill=0)
    body = font("arial.ttf", 40)
    d.text((200, 400), "Delivery No: DN-2026-55120", font=body, fill=0)
    d.text((200, 470), "Date: 03/11/2026", font=body, fill=0)
    d.text((200, 540), "Ship to: Meridian Office Supplies, Petaling Jaya", font=body, fill=0)

    codes = [
        ("Code39", "SEP-0001", zxingcpp.BarcodeFormat.Code39, 4, 220, (200, 760)),
        ("Code128", "DN-2026-55120", zxingcpp.BarcodeFormat.Code128, 4, 220, (200, 1160)),
        ("QrCode", "IPROFILER|DOC=10457|SITE=PJ", zxingcpp.BarcodeFormat.QRCode, 10, None, (1650, 700)),
    ]
    skew = 0.5
    expected = []
    for name, text, fmt, scale, height, (x, y) in codes:
        bc = barcode(text, fmt, scale, height)
        img.paste(bc, (x, y))
        d.text((x + 40, y + bc.height + 10), f"{name}: {text}", font=font("arial.ttf", 32), fill=0)
        expected.append({"symbology": name, "text": text, "region": region((x, y, bc.width, bc.height), skew, pad=20)})

    d.text((200, 3200), "Received in good condition. Signature: ____________", font=body, fill=0)
    page = to_bilevel(img, skew, speckle=0.0004)
    path = out_dir / "bw-g4-300dpi.tif"
    page.save(path, compression="group4", dpi=(DPI, DPI))
    meta = {"barcodes": expected,
            "words": ["HARBOUR", "DELIVERY", "DN-2026-55120", "03/11/2026", "Meridian", "Received"]}
    (out_dir / "bw-g4-300dpi.json").write_text(json.dumps(meta, indent=2))
    return path


def patch_sheet(code, out_dir):
    img = Image.new("L", A4, 255)
    d = ImageDraw.Draw(img)
    narrow, wide, space, length = int(0.08 * DPI), int(0.20 * DPI), int(0.08 * DPI), int(1.5 * DPI)
    total = sum(wide if c == "W" else narrow for c in PATCHES[code]) + 3 * space
    x, top = (A4[0] - total) // 2, int(0.5 * DPI)  # centred, half an inch below the leading edge
    for c in PATCHES[code]:
        w = wide if c == "W" else narrow
        d.rectangle([x, top, x + w - 1, top + length - 1], fill=0)
        x += w + space
    d.text((900, 1500), f"PATCH {code}", font=font("arialbd.ttf", 120), fill=0)
    d.text((620, 1700), "Separator sheet - remove before filing", font=font("arial.ttf", 48), fill=0)
    to_bilevel(img, 0.3, speckle=0.0002).save(out_dir / f"patch-{code}.tif", compression="group4", dpi=(DPI, DPI))


def main():
    data = REPO / "tests" / "IProfiler.Tests" / "TestData"
    (data / "barcode").mkdir(parents=True, exist_ok=True)
    (data / "patch").mkdir(parents=True, exist_ok=True)
    note = delivery_note(data / "barcode")
    for code in PATCHES:
        patch_sheet(code, data / "patch")

    spike = REPO / "samples" / "spike"
    spike.mkdir(parents=True, exist_ok=True)
    shutil.copy(note, spike / note.name)
    shutil.copy(note.with_suffix(".json"), spike / "bw-g4-300dpi.json")
    shutil.copy(data / "patch" / "patch-T.tif", spike / "patch-T.tif")
    for p in sorted((data / "barcode").iterdir()) + sorted((data / "patch").iterdir()):
        print(f"{p.relative_to(REPO)}  {p.stat().st_size:,} bytes")


if __name__ == "__main__":
    main()
