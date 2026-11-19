"""PulseArena logo / icon / banner generator.

Usage:
  python make_logo.py <showcase_dir> <out_dir> [--out-branding DIR]
         [--face-box X,Y,SIZE] [--face-scale 1.0] [--face-dx 0.0] [--face-dy 0.0]

<showcase_dir> must contain logo_character.png (1024x1024 RGBA), optionally logo_character_alt.png (frog).
Writes logo.png, logo-square.png, icon-512.png, banner-1280x640.png to <out_dir>, and
AppIcon.png / AppIconForeground.png / AppIconBackground.png to --out-branding
(default Assets/PulseArena/Branding).

Face crop: auto-detected from the alpha mask (centroid of the top 40% of the character bbox,
square side = 0.62 * bbox height). Override with --face-box (pixels in the character image) or
nudge with --face-scale (zoom, >1 = tighter), --face-dx / --face-dy (fraction of crop side).
"""
import argparse
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent))
import pa_common as pc  # noqa: E402


def face_crop(char, args):
    """Return a square RGBA crop of the face region."""
    if args.face_box:
        x, y, s = (int(v) for v in args.face_box.split(","))
    else:
        l, t, r, b = pc.alpha_bbox(char)
        bh = b - t
        a = np.asarray(char.getchannel("A"))[t:t + max(1, int(bh * 0.4)), l:r] > 24
        xs = np.nonzero(a.any(axis=0))[0]
        cx = l + (xs.mean() if len(xs) else (r - l) / 2)
        cols = np.nonzero(a)[1]
        if len(cols):
            cx = l + float(cols.mean())
        s = int(min(bh * 0.62, (r - l) * 1.05) / args.face_scale)
        x = int(cx - s / 2 + args.face_dx * s)
        y = int(t - s * 0.04 + args.face_dy * s)
    pad = s
    canvas = Image.new("RGBA", (char.width + 2 * pad, char.height + 2 * pad), (0, 0, 0, 0))
    canvas.alpha_composite(char, (pad, pad))
    return canvas.crop((x + pad, y + pad, x + pad + s, y + pad + s))


def icon_background(size):
    return pc.burst_background(size, size, 8, 1.0)


def compose_icon(face, size):
    bg = icon_background(size)
    f = pc.with_shadow(face, (0, size // 60), size // 50, 130)
    f = pc.fit(f, int(size * 0.94), int(size * 0.94))
    bg.alpha_composite(f, ((size - f.width) // 2, size - f.height - int(size * 0.02)))
    d = ImageDraw.Draw(bg)  # small "PA" tag, bottom-right
    fnt = pc.font(size * 0.1)
    d.text((size * 0.93, size * 0.94), "PA", font=fnt, fill=pc.YELLOW, anchor="rd",
           stroke_width=max(2, size // 150), stroke_fill=pc.RED_BROWN)
    return bg


def rounded(img, radius_frac=0.22):
    s = img.width
    m = Image.new("L", (s * 2, s * 2), 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, s * 2 - 1, s * 2 - 1], radius=int(s * 2 * radius_frac), fill=255)
    out = img.copy()
    out.putalpha(m.resize((s, s), Image.LANCZOS))
    return out


def make_banner(char, alt, logo_wm, w=1280, h=640):
    bg = pc.burst_background(w, h, 0, 1.0)
    # character(s) on the right
    if alt is not None:
        a = pc.fit(pc.with_shadow(pc.trim(alt)), int(h * 0.62), int(h * 0.62))
        bg.alpha_composite(a, (int(w * 0.56), h - a.height - 10))
    if char is not None:
        c = pc.fit(pc.with_shadow(pc.trim(char)), int(w * 0.40), int(h * 0.92))
        bg.alpha_composite(c, (int(w * 0.95) - c.width, h - c.height - 6))
    wm = pc.with_shadow(logo_wm, (0, 8), 8, 130)
    wm = pc.fit(wm, int(w * 0.50), int(h * 0.50))
    bg.alpha_composite(wm, (int(w * 0.05), int(h * 0.10)))
    d = ImageDraw.Draw(bg)
    x = int(w * 0.06)
    d.text((x, int(h * 0.72)), "4-player mobile arena shooter · Unity 6", font=pc.font(34, True),
           fill=(255, 255, 255), stroke_width=3, stroke_fill=(30, 30, 110))
    d.text((x, int(h * 0.83)), "by Maheshwaran", font=pc.font(30, False),
           fill=pc.YELLOW, stroke_width=3, stroke_fill=(60, 12, 12))
    return bg.convert("RGB")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("showcase_dir")
    ap.add_argument("out_dir")
    ap.add_argument("--out-branding", default=str(pc.BRANDING))
    ap.add_argument("--face-box", help="X,Y,SIZE square crop in character-image pixels")
    ap.add_argument("--face-scale", type=float, default=1.0)
    ap.add_argument("--face-dx", type=float, default=0.0)
    ap.add_argument("--face-dy", type=float, default=0.0)
    args = ap.parse_args()

    out = Path(args.out_dir)
    brand = Path(args.out_branding)
    out.mkdir(parents=True, exist_ok=True)
    brand.mkdir(parents=True, exist_ok=True)

    char = pc.load_character(args.showcase_dir)
    alt = pc.load_character(args.showcase_dir, alt=True)
    if char is None:
        sys.exit("logo_character.png not found in %s" % args.showcase_dir)

    logo = pc.make_logo_image(char)
    logo.save(out / "logo.png")

    sq = pc.burst_background(1024, 1024, 0, 1.0)
    lg = pc.fit(logo, 960, 960)
    sq.alpha_composite(lg, ((1024 - lg.width) // 2, (1024 - lg.height) // 2))
    sq.convert("RGB").save(out / "logo-square.png")

    face = face_crop(char, args)
    icon = compose_icon(face, 1024)
    icon.convert("RGB").save(brand / "AppIcon.png")
    rounded(icon.resize((512, 512), Image.LANCZOS)).save(out / "icon-512.png")

    fg = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
    f = pc.fit(pc.with_shadow(face, (0, 8), 10, 110), int(1024 * 0.64), int(1024 * 0.64))
    fg.alpha_composite(f, ((1024 - f.width) // 2, (1024 - f.height) // 2))
    fg.save(brand / "AppIconForeground.png")
    icon_background(1024).convert("RGB").save(brand / "AppIconBackground.png")

    make_banner(char, alt, pc.wordmark(1000)).save(out / "banner-1280x640.png")
    for p in ("logo.png", "logo-square.png", "icon-512.png", "banner-1280x640.png"):
        print(out / p)
    for p in ("AppIcon.png", "AppIconForeground.png", "AppIconBackground.png"):
        print(brand / p)


if __name__ == "__main__":
    main()
