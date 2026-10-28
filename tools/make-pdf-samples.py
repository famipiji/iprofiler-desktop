"""Generates pages for checking the searchable-PDF text layer (Task 1.5): word boxes from ink gaps, phrase search.

    python tools/make-pdf-samples.py

Writes to tests/IProfiler.Tests/TestData/pdf/ (A4):
  phrases-g4.tif                    300 dpi 1-bit G4, upright (exported with a crop)
  phrases-skew-g4.tif               the same rotated 1.5 deg about the page centre (exported with deskew)
  phrases-upside-down-200dpi.tif    200 dpi 8-bit grey (LZW, resolution tag 200), fed upside down
  phrases.json                      per file: dpi and every word's box in upright page pixels; the phrases to search

The page mixes the hard cases for word boxes taken from ink gaps: a letterhead with wide letter spacing
("SIMEN UTARA SDN BHD"), very wide spacing where letter gaps are half a word gap ("TERIMA KASIH"), big and
small text on one line, and ordinary lines with commas, dates and an invoice number.
"""
import json
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parent.parent
OUT = REPO / "tests" / "IProfiler.Tests" / "TestData" / "pdf"
A4_INCHES = (8.27, 11.69)
SKEW_DEG = 1.5
FONTS = Path("C:/Windows/Fonts")
random.seed(1105)

# (x inches, y inches of the text top, font file, size in points, letter spacing in em, [words]).
# Word gaps are one space of the font plus the letter spacing, as a typesetter would set them.
LINES = [
    (1.0, 0.9, "arialbd.ttf", 28, 0.35, ["SIMEN", "UTARA", "SDN", "BHD"]),
    (1.0, 1.5, "arial.ttf", 10, 0.0, ["Lot", "23,", "Jalan", "Perusahaan", "4,", "Kawasan", "Perindustrian", "Simen", "Utara"]),
    (1.0, 1.75, "arial.ttf", 10, 0.0, ["51200", "Kuala", "Lumpur"]),
    (1.0, 2.6, "arial.ttf", 12, 0.0, ["No.", "Invois:", "INV-2026-10457"]),
    (5.2, 2.6, "arial.ttf", 12, 0.0, ["Tarikh:", "28/10/2026"]),
    (1.0, 3.3, "arial.ttf", 11, 0.0, ["Perihal:", "Bekalan", "simen", "Portland", "50", "kg", "x", "120", "beg"]),
    (1.0, 4.4, "arialbd.ttf", 30, None, ["TOTAL"]),  # big and small text on one line: see MIXED
    (1.0, 6.0, "arial.ttf", 20, 0.8, ["TERIMA", "KASIH"]),
    (1.0, 7.2, "arial.ttf", 9, 0.0, ["Dokumen", "ini", "dijana", "oleh", "komputer."]),
]
# The rest of the TOTAL line, small, sharing its baseline.
MIXED = (2.6, "arial.ttf", 11, 0.0, ["RM", "12,345.60"])
PHRASES = [
    "SIMEN UTARA",
    "SIMEN UTARA SDN BHD",
    "Kawasan Perindustrian Simen Utara",
    "INV-2026-10457",
    "Tarikh: 28/10/2026",
    "TOTAL RM 12,345.60",
    "TERIMA KASIH",
    "Portland 50 kg",
]


def draw_words(draw, x, y, font, spacing_em, words):
    """Draws the words letter by letter; returns their boxes (ink bounds) in this page's pixels."""
    size = font.size
    track = spacing_em * size
    space = font.getlength(" ")
    boxes = []
    for word in words:
        left = None
        top = bottom = None
        right = x
        for ch in word:
            x0, y0, x1, y1 = draw.textbbox((x, y), ch, font=font)
            draw.text((x, y), ch, font=font, fill=0)
            left = x0 if left is None else min(left, x0)
            top = y0 if top is None else min(top, y0)
            bottom = y1 if bottom is None else max(bottom, y1)
            right = max(right, x1)
            x += font.getlength(ch) + track
        boxes.append({"text": word, "box": [round(left), round(top), round(right - left), round(bottom - top)]})
        x += space + track
    return boxes, x


def page(dpi):
    """The upright page at `dpi`, and its word boxes (in its own pixels), line by line."""
    size = (round(A4_INCHES[0] * dpi), round(A4_INCHES[1] * dpi))
    img = Image.new("L", size, 255)
    draw = ImageDraw.Draw(img)
    lines = []
    for x_in, y_in, face, pt, spacing, words in LINES:
        font = ImageFont.truetype(str(FONTS / face), round(pt / 72 * dpi))
        if spacing is None:  # the mixed line: big word, then small words on the same baseline
            boxes, _ = draw_words(draw, x_in * dpi, y_in * dpi, font, 0.0, words)
            mx, mface, mpt, mspacing, mwords = MIXED
            small = ImageFont.truetype(str(FONTS / mface), round(mpt / 72 * dpi))
            baseline = y_in * dpi + font.getmetrics()[0]
            more, _ = draw_words(draw, mx * dpi, baseline - small.getmetrics()[0], small, mspacing, mwords)
            boxes += more
        else:
            boxes, _ = draw_words(draw, x_in * dpi, y_in * dpi, font, spacing, words)
        lines.append(boxes)
    return img, lines


def noise(gray, fraction):
    px = gray.load()
    w, h = gray.size
    for _ in range(int(w * h * fraction)):
        px[random.randrange(w), random.randrange(h)] = random.choice((0, 120, 200))
    return gray


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    upright, lines = page(300)
    upright.convert("1", dither=Image.Dither.NONE).save(OUT / "phrases-g4.tif", compression="group4", dpi=(300, 300))
    # PIL rotates counter-clockwise; the export straightens it again about the same centre.
    upright.rotate(SKEW_DEG, resample=Image.BICUBIC, fillcolor=255) \
        .convert("1", dither=Image.Dither.NONE).save(OUT / "phrases-skew-g4.tif", compression="group4", dpi=(300, 300))

    low, low_lines = page(200)
    noise(low, 0.0003).rotate(180).save(OUT / "phrases-upside-down-200dpi.tif", compression="tiff_lzw", dpi=(200, 200))

    def words(ls):
        return [w for line in ls for w in line]

    (OUT / "phrases.json").write_text(json.dumps({
        "skewDegreesCounterClockwise": SKEW_DEG,
        "lines": [[w["text"] for w in line] for line in lines],
        "phrases": PHRASES,
        # Boxes are where each word is on the upright, unskewed page; the export straightens the others back to it.
        "pages": {
            "phrases-g4.tif": {"dpi": 300, "words": words(lines)},
            "phrases-skew-g4.tif": {"dpi": 300, "words": words(lines)},
            "phrases-upside-down-200dpi.tif": {"dpi": 200, "words": words(low_lines)},
        },
    }, indent=2), encoding="utf-8")
    print(f"{len(words(lines))} words, {len(PHRASES)} phrases -> {OUT}")


if __name__ == "__main__":
    main()
