#!/usr/bin/env python3
"""Shrink a generated picture to the size the game draws it at.

    fit.py <in.png> <out.webp> <pixels>

Run with ../assetgen's interpreter, which is where Pillow lives -- see
scripts/assets.sh. The longest side is scaled to <pixels>, after any black
letterbox bars are trimmed, so a wide banner stays wide.

assetgen's library keeps the full-size original and the prompt that made it,
so the repository only needs the picture at the size it is shown. Lossy WebP
at quality 88 is invisible on a painted image at that size and about a
twentieth of a PNG; Godot decodes it from bytes (Image.LoadWebpFromBuffer).
"""

import sys

from PIL import Image

QUALITY = 88

# A row whose brightest pixel is below this counts as letterbox. Asked for
# 16:9, flux-1.1-pro tends to paint black bars top and bottom however the
# prompt pleads, so they are cut rather than argued with.
BAR_LEVEL = 14


def trim_bars(image: Image.Image) -> Image.Image:
    grey = image.convert("L")
    width, height = grey.size
    def dark(y: int) -> bool:
        return grey.crop((0, y, width, y + 1)).getextrema()[1] < BAR_LEVEL
    top, bottom = 0, height
    while top < height // 4 and dark(top):
        top += 1
    while bottom > height - height // 4 and dark(bottom - 1):
        bottom -= 1
    return image.crop((0, top, width, bottom)) if top or bottom < height else image


def main() -> int:
    if len(sys.argv) != 4:
        print("usage: fit.py <in.png> <out.webp> <pixels>", file=sys.stderr)
        return 2
    source, target, pixels = sys.argv[1], sys.argv[2], int(sys.argv[3])
    with Image.open(source) as opened:
        image = trim_bars(opened.convert("RGB"))
        width, height = image.size
        scale = pixels / float(max(width, height))
        size = (max(1, round(width * scale)), max(1, round(height * scale)))
        image.resize(size, Image.LANCZOS).save(target, "WEBP", quality=QUALITY, method=6)
    return 0


if __name__ == "__main__":
    sys.exit(main())
