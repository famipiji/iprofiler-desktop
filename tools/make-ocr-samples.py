"""Generates synthetic "scanned" pages for the OCR spike (Task 1.2).

    python tools/make-ocr-samples.py

Writes (all text is made up):
  samples/spike/gen-invoice-300dpi.tif   A4 @ 300 dpi, grayscale, slight skew, scanner noise, faint "COPY" watermark
  samples/spike/gen-letter-200dpi.jpg    A4 @ 200 dpi, serif body text, JPEG artefacts
  samples/spike/expected.json            expected words per page + the invoice-number field region (pixels)
  tests/IProfiler.Tests/TestData/ocr/invoice-150dpi.png   small clean copy used by the xUnit OCR test
"""
import json
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

REPO = Path(__file__).resolve().parent.parent
FONTS = Path("C:/Windows/Fonts")
random.seed(1028)


def font(name, size):
    return ImageFont.truetype(str(FONTS / name), size)


def invoice(dpi):
    s = dpi / 300
    w, h = int(2480 * s), int(3508 * s)
    img = Image.new("L", (w, h), 255)
    d = ImageDraw.Draw(img)
    big, mid, small = font("arialbd.ttf", int(64 * s)), font("arial.ttf", int(40 * s)), font("arial.ttf", int(34 * s))

    def text(x, y, t, f=mid):
        d.text((int(x * s), int(y * s)), t, fill=0, font=f)

    text(200, 200, "MERIDIAN OFFICE SUPPLIES SDN BHD", big)
    text(200, 300, "Lot 12, Jalan Teknologi 3, 47810 Petaling Jaya", small)
    text(1700, 480, "INVOICE", font("arialbd.ttf", int(80 * s)))
    text(200, 620, "Invoice No:", mid)
    field_text = "INV-2026-10457"
    text(520, 620, field_text, mid)
    fx0, fy0, fx1, fy1 = d.textbbox((int(520 * s), int(620 * s)), field_text, font=mid)
    text(200, 690, "Date: 28/10/2026", mid)
    text(200, 760, "Customer: Harbour Logistics", mid)

    rows = [("A4 Copy Paper 80gsm", "20", "12.50", "250.00"),
            ("Toner Cartridge TN-2480", "3", "189.00", "567.00"),
            ("Lever Arch File", "40", "6.90", "276.00"),
            ("Barcode Labels 100x50", "10", "14.15", "141.50")]
    y = 950
    d.line([(int(200 * s), int(y * s)), (int(2280 * s), int(y * s))], fill=0, width=max(1, int(3 * s)))
    for col, x in (("Description", 200), ("Qty", 1300), ("Unit Price", 1550), ("Amount", 1950)):
        text(x, y + 20, col, font("arialbd.ttf", int(40 * s)))
    y += 100
    for r in rows:
        for t, x in zip(r, (200, 1300, 1550, 1950)):
            text(x, y, t)
        y += 80
    d.line([(int(200 * s), int(y * s)), (int(2280 * s), int(y * s))], fill=0, width=max(1, int(3 * s)))
    text(1550, y + 40, "TOTAL: RM 1,234.50", font("arialbd.ttf", int(44 * s)))
    text(200, 3200, "Payment within 30 days. Thank you for your business.", small)

    # Faint watermark (the confidence filter should drop most of it).
    wm = Image.new("L", (w, h), 0)
    ImageDraw.Draw(wm).text((int(500 * s), int(1800 * s)), "COPY", fill=255, font=font("arialbd.ttf", int(420 * s)))
    wm = wm.rotate(30, center=(w // 2, h // 2))
    img.paste(Image.new("L", (w, h), 235), mask=wm.point(lambda v: 255 if v > 128 else 0))
    # Re-draw nothing over it: real watermarks sit under/over text; fine for a spike.
    return img, {"text": field_text, "box": [fx0, fy0, fx1 - fx0, fy1 - fy0]}


def scanify(img, skew_deg, noise, blur):
    img = img.rotate(skew_deg, resample=Image.BICUBIC, fillcolor=255, expand=False)
    img = img.filter(ImageFilter.GaussianBlur(blur))
    px = img.load()
    w, h = img.size
    for _ in range(int(w * h * noise)):
        x, y = random.randrange(w), random.randrange(h)
        px[x, y] = random.choice((0, 90, 160))
    return img


def rotate_point(x, y, deg, w, h):
    # PIL rotates counter-clockwise around the centre.
    a = math.radians(-deg)
    cx, cy = w / 2, h / 2
    dx, dy = x - cx, y - cy
    return cx + dx * math.cos(a) - dy * math.sin(a), cy + dx * math.sin(a) + dy * math.cos(a)


def field_region(box, skew, size, pad):
    x, y, bw, bh = box
    pts = [rotate_point(px, py, skew, *size) for px, py in ((x, y), (x + bw, y), (x, y + bh), (x + bw, y + bh))]
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    return [int(min(xs) - pad), int(min(ys) - pad), int(max(xs) - min(xs) + 2 * pad), int(max(ys) - min(ys) + 2 * pad)]


def letter():
    w, h = 1654, 2339  # A4 @ 200 dpi
    img = Image.new("L", (w, h), 255)
    d = ImageDraw.Draw(img)
    serif, bold = font("times.ttf", 30), font("timesbd.ttf", 34)
    d.text((140, 140), "Harbour Logistics Berhad", font=bold, fill=0)
    d.text((140, 190), "Reference: HL/OPS/2026/118", font=serif, fill=0)
    body = ["Dear Sir or Madam,",
            "",
            "We confirm receipt of your quotation dated 14 October 2026 for the",
            "supply of archive boxes and scanning services. Please proceed with",
            "the order and deliver the goods to our Port Klang warehouse before",
            "the end of November. The purchase order number is PO-88213.",
            "",
            "Yours faithfully,",
            "Operations Manager"]
    y = 320
    for line in body:
        d.text((140, y), line, font=serif, fill=0)
        y += 48
    return img


def main():
    out = REPO / "samples" / "spike"
    out.mkdir(parents=True, exist_ok=True)

    inv, field = invoice(300)
    skew = 0.6
    inv = scanify(inv, skew, noise=0.0008, blur=0.6)
    inv.save(out / "gen-invoice-300dpi.tif", compression="tiff_lzw", dpi=(300, 300))

    let = scanify(letter(), -0.4, noise=0.0005, blur=0.5)
    let.save(out / "gen-letter-200dpi.jpg", quality=70, dpi=(200, 200))

    expected = {
        "gen-invoice-300dpi.tif": {
            "words": ["MERIDIAN", "INVOICE", "INV-2026-10457", "28/10/2026", "Toner", "1,234.50"],
            "field": {"name": "InvoiceNo", "text": field["text"],
                      "region": field_region(field["box"], skew, inv.size, pad=30)},
        },
        "gen-letter-200dpi.jpg": {
            "words": ["Harbour", "HL/OPS/2026/118", "quotation", "Klang", "PO-88213", "faithfully"],
        },
    }
    (out / "expected.json").write_text(json.dumps(expected, indent=2))

    small, small_field = invoice(150)
    test_dir = REPO / "tests" / "IProfiler.Tests" / "TestData" / "ocr"
    test_dir.mkdir(parents=True, exist_ok=True)
    small.save(test_dir / "invoice-150dpi.png", optimize=True)
    (test_dir / "invoice-150dpi.json").write_text(json.dumps(
        {"fieldText": small_field["text"], "fieldRegion": field_region(small_field["box"], 0, small.size, pad=15)},
        indent=2))
    print("written:", *sorted(p.name for p in out.iterdir()), "+ test fixture")


if __name__ == "__main__":
    main()
