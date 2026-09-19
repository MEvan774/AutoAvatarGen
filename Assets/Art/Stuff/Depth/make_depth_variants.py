"""Generate the out-of-focus copies of the desk doodles used by the Violet Doodles backdrop.

WHY THIS EXISTS
    Depth in that backdrop used to be faked with transparency, which let the gradient's
    curves and the dust show THROUGH the doodles. Real depth keeps objects solid: things
    off the focal plane get soft, and they take on the colour of the air between them and
    the camera. Unity's standard sprite shader cannot blur, so the blur is baked into
    copies of the sprites here, once, instead of being computed every frame.

WHAT IT MAKES
    One PNG + .meta per (sprite, band) pair listed in VARIANTS, next to this file:
        Clock_close.png, Clock_far.png, Note_mid.png, ...
    Per band it bakes in a Gaussian blur and a fade toward the backdrop's deep indigo.
    Darkness is NOT baked in - that is the SpriteRenderer's Color on each doodle in
    Shapes.prefab, so it stays tweakable in the Inspector.

RE-RUNNING IS SAFE
    python make_depth_variants.py
    Existing .meta files keep their guid, so nothing in the prefabs loses its sprite.
    The originals in the parent folder are only ever read.

Needs: pillow, numpy.
"""
import math
import os
import re
import uuid

import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
SOURCE_DIR = os.path.dirname(HERE)            # Assets/Art/Stuff

# The colour distance fades things toward: the deep indigo of the backdrop's corners.
FOG_RGB = (38, 36, 120)

# band -> (blur radius in source pixels, how far to fade toward FOG_RGB, 0..1)
# The sharp "near" band is the focal plane and uses the original sprites, so it is not here.
BANDS = {
    "close": (4.0, 0.22),   # in front of the focal plane: soft, still clearly recognisable
    "mid":   (3.0, 0.32),   # just behind it
    "far":   (6.0, 0.50),   # well behind: softest, most faded
}

# (source sprite, band) - exactly the pairs Shapes.prefab uses.
VARIANTS = [
    ("Clock", "close"), ("Letter", "close"), ("Mug", "close"),
    ("Note", "mid"), ("CheckList", "mid"), ("PaperClip", "mid"),
    ("Clock", "far"), ("Letter", "far"), ("PaperClip", "far"), ("Paper", "far"),
]


def read_first_sprite(meta_text):
    """Name, rect and internalID of the FIRST sprite sliced out of a source image.

    The sources were auto-sliced in the Sprite Editor, so each sprite is a tight rectangle
    inside the 350 px canvas (Mug.png even has a second, stray slice - the first one is the mug).
    Unity measures rect.y from the BOTTOM of the image.
    """
    m = re.search(r"      name: (\S+)\n      rect:\n(?:        serializedVersion: \d+\n)?"
                  r"        x: (\d+)\n        y: (\d+)\n        width: (\d+)\n        height: (\d+)\n"
                  r"(?:.*\n)*?      internalID: (-?\d+)", meta_text)
    name, x, y, w, h, internal_id = m.groups()
    return name, (int(x), int(y), int(w), int(h)), internal_id


def make_image(source_png, rect, blur, fog):
    im = Image.open(source_png).convert("RGBA")
    width, height = im.size
    x, y_bottom, w, h = rect
    top = height - (y_bottom + h)             # Unity's bottom-up y -> Pillow's top-down y

    px = np.asarray(im).astype(np.float32) / 255.0
    rgb, alpha = px[..., :3], px[..., 3]

    # Keep only this sprite's own slice, so a stray neighbour can't bleed into the blur.
    keep = np.zeros_like(alpha)
    keep[top:top + h, x:x + w] = 1.0
    alpha = alpha * keep

    # Fade toward the backdrop colour (atmospheric perspective).
    fog_rgb = np.array(FOG_RGB, dtype=np.float32) / 255.0
    rgb = rgb * (1.0 - fog) + fog_rgb * fog

    # Blur in PREMULTIPLIED form (colour x alpha). Blurring plain RGBA drags the colour of
    # fully transparent pixels into the soft edge and leaves a dirty fringe around the sprite.
    def gaussian(channel):
        as_image = Image.fromarray(np.clip(channel * 255.0, 0, 255).astype(np.uint8), "L")
        return np.asarray(as_image.filter(ImageFilter.GaussianBlur(blur))).astype(np.float32) / 255.0

    blurred_alpha = gaussian(alpha)
    safe_alpha = np.maximum(blurred_alpha, 1e-4)
    blurred_rgb = np.stack([gaussian(rgb[..., c] * alpha) / safe_alpha for c in range(3)], axis=-1)

    out = np.concatenate([np.clip(blurred_rgb, 0, 1), blurred_alpha[..., None]], axis=-1)
    return Image.fromarray((out * 255.0 + 0.5).astype(np.uint8), "RGBA")


def grown_rect(rect, blur, canvas):
    """The source rect widened on every side to hold the blur's soft halo.

    Widened by the SAME amount on all four sides so the rect's centre - which is the sprite's
    pivot - does not move: a blurred copy sits exactly where its sharp original would.
    """
    pad = int(math.ceil(blur * 3)) + 2        # a Gaussian is effectively zero past 3 radii
    x, y, w, h = rect
    room = min(x, y, canvas[0] - (x + w), canvas[1] - (y + h))
    if pad > room:
        raise SystemExit("blur halo of %d px does not fit: only %d px of canvas around the sprite" % (pad, room))
    return x - pad, y - pad, w + 2 * pad, h + 2 * pad


def make_meta(source_meta, guid, sprite_name, rect, internal_id):
    """The source's import settings, re-pointed at the single grown sprite of the copy."""
    text = source_meta
    text = re.sub(r"^guid: \w+$", "guid: " + guid, text, count=1, flags=re.M)

    # Full Rect instead of Tight: a tight mesh is traced around the opaque pixels and would
    # slice the faint outer part of the halo off in a hard line.
    text = text.replace("spriteMeshType: 1", "spriteMeshType: 0")

    # id -> name table at the top: exactly one sprite.
    text = re.sub(r"  internalIDToNameTable:\n(?:.*\n)*?  externalObjects:",
                  "  internalIDToNameTable:\n  - first:\n      213: %s\n    second: %s\n  externalObjects:"
                  % (internal_id, sprite_name), text, count=1)

    # The sprite list: keep the first entry only, rename it and give it the grown rect.
    entries = re.search(r"(    sprites:\n)((?:    - serializedVersion: 2\n(?:      .*\n)+)+)", text)
    first = re.match(r"    - serializedVersion: 2\n(?:      .*\n)+?(?=    - serializedVersion: 2\n|\Z)", entries.group(2)).group(0)
    first = re.sub(r"      name: \S+", "      name: " + sprite_name, first, count=1)
    first = re.sub(r"        x: \d+\n        y: \d+\n        width: \d+\n        height: \d+",
                   "        x: %d\n        y: %d\n        width: %d\n        height: %d" % rect, first, count=1)
    first = re.sub(r"      spriteID: \w*", "      spriteID: " + uuid.uuid4().hex, first, count=1)
    text = text[:entries.start(2)] + first + text[entries.end(2):]

    # name -> id table at the bottom: exactly one sprite.
    text = re.sub(r"    nameFileIdTable:\n(?:      .*\n)*", "    nameFileIdTable:\n      %s: %s\n" % (sprite_name, internal_id),
                  text, count=1)
    return text


def main():
    for source, band in VARIANTS:
        blur, fog = BANDS[band]
        source_png = os.path.join(SOURCE_DIR, source + ".png")
        source_meta = open(source_png + ".meta", encoding="utf-8").read()
        _, rect, internal_id = read_first_sprite(source_meta)

        stem = "%s_%s" % (source, band)
        out_png = os.path.join(HERE, stem + ".png")
        image = make_image(source_png, rect, blur, fog)
        image.save(out_png)

        # Reuse the guid of an existing .meta: the prefabs reference sprites BY guid.
        guid = uuid.uuid4().hex
        if os.path.exists(out_png + ".meta"):
            guid = re.search(r"^guid: (\w+)$", open(out_png + ".meta", encoding="utf-8").read(), flags=re.M).group(1)

        meta = make_meta(source_meta, guid, stem + "_0", grown_rect(rect, blur, image.size), internal_id)
        with open(out_png + ".meta", "w", encoding="utf-8", newline="\n") as f:
            f.write(meta)
        print("%-20s blur %-4s fog %-5s guid %s  spriteFileID %s" % (stem + ".png", blur, fog, guid, internal_id))


if __name__ == "__main__":
    main()
