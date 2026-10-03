"""Derive the plugin card icon from the front pose."""
import os

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
front = Image.open(os.path.join(ROOT, 'art', 'raw', 'front.png')).convert('RGBA')

w, h = front.size
# Head and shoulders: the sheet's own close-up crop, kept wide enough for the ears.
box = (max(0, int(w * 0.02)), int(h * 0.01), min(w, int(w * 0.98)), int(h * 0.60))
head = front.crop(box)
head = head.resize((384, 384), Image.LANCZOS)
head = head.quantize(colors=160, method=Image.FASTOCTREE, dither=Image.FLOYDSTEINBERG)

out = os.path.join(ROOT, 'icon.png')
head.save(out, optimize=True)
print('icon', head.size, os.path.getsize(out), 'bytes')
