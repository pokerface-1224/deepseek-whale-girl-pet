"""Cut the three chibi poses out of the whale-girl character sheet.

The sheet background is a flat near-white (252,252,252) with a soft vignette.
A global colour key would also erase the white apron, headdress and socks, so
the background is instead flood-filled inward from the image border. The
resulting mask is pulled in by one pixel (which removes the anti-aliased light
halo) and then feathered, so interior whites keep their full opacity and the
silhouette keeps a clean edge.
"""
import os
import argparse
from collections import deque

from PIL import Image, ImageFilter

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("source", help="Path to the original character sheet")
args = parser.parse_args()
SRC = args.source
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "art", "raw")

# Crop boxes measured from the sheet's column/row runs (x0, y0, x1, y1).
CROPS = {
    "front": (0, 0, 545, 625),
    "side": (545, 0, 958, 625),
    "back": (958, 0, 1448, 625),
}

TOL = 26  # background tolerance, wide enough for the sheet's vignette


def cutout(im):
    w, h = im.size
    px = im.load()

    def is_bg(p):
        return (abs(p[0] - 252) <= TOL and abs(p[1] - 252) <= TOL
                and abs(p[2] - 252) <= TOL)

    mask = bytearray(w * h)  # 1 == background
    q = deque()

    def push(x, y):
        i = y * w + x
        if not mask[i] and is_bg(px[x, y]):
            mask[i] = 1
            q.append((x, y))

    for x in range(w):
        push(x, 0)
        push(x, h - 1)
    for y in range(h):
        push(0, y)
        push(w - 1, y)
    while q:
        x, y = q.popleft()
        if x > 0:
            push(x - 1, y)
        if x < w - 1:
            push(x + 1, y)
        if y > 0:
            push(x, y - 1)
        if y < h - 1:
            push(x, y + 1)

    m = Image.new("L", (w, h), 0)
    m.putdata([0 if b else 255 for b in mask])
    m = m.filter(ImageFilter.MinFilter(3))       # kill the light halo
    m = m.filter(ImageFilter.GaussianBlur(0.6))  # clean anti-aliased edge

    out = im.convert("RGBA")
    out.putalpha(m)
    return out


def trim(im, thresh=8):
    box = im.getchannel("A").point(lambda v: 255 if v > thresh else 0).getbbox()
    return im.crop(box) if box else im


def main():
    sheet = Image.open(SRC).convert("RGB")
    made = []
    for name, box in CROPS.items():
        cut = trim(cutout(sheet.crop(box)))
        # Normalise every pose to one sprite height so CSS sizing stays stable.
        target_h = 420
        scale = target_h / cut.height
        cut = cut.resize((max(1, round(cut.width * scale)), target_h), Image.LANCZOS)
        path = os.path.join(OUT, name + ".png")
        cut.save(path, optimize=True)
        made.append((name, cut.size, os.path.getsize(path)))
        print(name, cut.size, os.path.getsize(path))

    # Contact sheet over a dark and a light background for visual inspection.
    pad = 12
    total_w = sum(s[1][0] for s in made) + pad * (len(made) + 1)
    total_h = max(s[1][1] for s in made) + pad * 2
    for bg_name, bg in (("dark", (26, 29, 38, 255)), ("light", (244, 246, 250, 255))):
        sheet_out = Image.new("RGBA", (total_w, total_h), bg)
        x = pad
        for name, size, _ in made:
            sheet_out.alpha_composite(Image.open(os.path.join(OUT, name + ".png")), (x, pad))
            x += size[0] + pad
        sheet_out.convert("RGB").save(os.path.join(OUT, "_check_" + bg_name + ".png"))
        print("check", bg_name, sheet_out.size)


if __name__ == "__main__":
    main()
