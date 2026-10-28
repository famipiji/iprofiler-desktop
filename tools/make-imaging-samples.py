"""Generates test pages for deskew, blank-page and orientation checks (Task 1.4).

    python tools/make-imaging-samples.py      (run tools/make-barcode-samples.py first)

Writes to tests/IProfiler.Tests/TestData/imaging/ (300 dpi A4; 1-bit CCITT G4 unless noted):
  blank-g4.tif                  white sheet with scanner specks
  near-blank-staple-g4.tif      specks + a staple and its shadow near the top-left corner + an edge shadow
  near-blank-staple-gray.jpg    the same in 8-bit grayscale with paper texture (JPEG q80)
  sparse-text-g4.tif            only a short footer line (measured, not classified: see the spike report)
  skew-3deg-g4.tif              the delivery note rotated a further 2.5 deg (3.0 deg in total)
  upside-down-g4.tif            the delivery note rotated 180 deg (fed upside down)
  imaging.json                  expected skew-correction angles
"""
import json
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

REPO = Path(__file__).resolve().parent.parent
DATA = REPO / "tests" / "IProfiler.Tests" / "TestData"
A4 = (2480, 3508)
DPI = (300, 300)
random.seed(1104)


def specks(img, fraction):
    px = img.load()
    for _ in range(int(A4[0] * A4[1] * fraction)):
        x, y = random.randrange(A4[0]), random.randrange(A4[1])
        px[x, y] = 0
        if random.random() < 0.2:  # some specks are 2x2
            px[min(x + 1, A4[0] - 1), y] = 0
            px[x, min(y + 1, A4[1] - 1)] = 0


def staple(gray):
    """Staple (dark, thin) with a soft shadow beside it, slanted, near the top-left corner; plus a left-edge shadow."""
    shadow = Image.new("L", A4, 0)
    ImageDraw.Draw(shadow).line([(170, 140), (340, 120)], fill=255, width=26)
    shadow = shadow.filter(ImageFilter.GaussianBlur(8))
    gray.paste(Image.new("L", A4, 150), mask=shadow)
    d = ImageDraw.Draw(gray)
    d.line([(165, 130), (335, 110)], fill=40, width=9)
    d.line([(165, 130), (165, 150)], fill=40, width=7)
    d.line([(335, 110), (335, 130)], fill=40, width=7)
    d.rectangle([0, 0, 7, A4[1] - 1], fill=70)  # scanner edge shadow along the left edge


def bilevel(gray):
    return gray.point(lambda v: 255 if v > 128 else 0).convert("1")


def main():
    out = DATA / "imaging"
    out.mkdir(parents=True, exist_ok=True)

    blank = Image.new("1", A4, 1)
    specks(blank, 0.0003)
    blank.save(out / "blank-g4.tif", compression="group4", dpi=DPI)

    gray = Image.new("L", A4, 255)
    staple(gray)
    near = bilevel(gray)
    specks(near, 0.0003)
    near.save(out / "near-blank-staple-g4.tif", compression="group4", dpi=DPI)

    paper = Image.effect_noise(A4, 6).point(lambda v: 232 + (v - 128) // 16)
    staple(paper)
    paper.save(out / "near-blank-staple-gray.jpg", quality=80, dpi=DPI)

    sparse = Image.new("L", A4, 255)
    ImageDraw.Draw(sparse).text((1000, 3300), "Page 2 of 2", font=ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 40), fill=0)
    sparse = bilevel(sparse)
    specks(sparse, 0.0003)
    sparse.save(out / "sparse-text-g4.tif", compression="group4", dpi=DPI)

    note = Image.open(DATA / "barcode" / "bw-g4-300dpi.tif")
    note.convert("L").rotate(2.5, resample=Image.BICUBIC, fillcolor=255).point(lambda v: 255 if v > 128 else 0) \
        .convert("1").save(out / "skew-3deg-g4.tif", compression="group4", dpi=DPI)
    note.rotate(180).save(out / "upside-down-g4.tif", compression="group4", dpi=DPI)

    # PIL rotates counter-clockwise, so the correction is the same angle clockwise (positive, per IImageProcessor).
    (out / "imaging.json").write_text(json.dumps({
        "skewCorrection": {"barcode/bw-g4-300dpi.tif": 0.5, "imaging/skew-3deg-g4.tif": 3.0,
                           "imaging/upside-down-g4.tif": 0.5, "ocr/invoice-150dpi.png": 0.0},
    }, indent=2))
    for p in sorted(out.iterdir()):
        print(f"{p.relative_to(REPO)}  {p.stat().st_size:,} bytes")


if __name__ == "__main__":
    main()
