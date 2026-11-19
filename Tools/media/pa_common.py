"""Shared helpers for the PulseArena media scripts (Pillow + numpy + ffmpeg)."""
import math
import shutil
import subprocess
from pathlib import Path

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

REPO = Path(__file__).resolve().parents[2]
FFMPEG_DIR = Path(r"C:\Users\Maheshwaran\AppData\Local\Microsoft\WinGet\Packages"
                  r"\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin")
MIXED = REPO / "Assets/PulseArena/ThirdParty/Audio/Mixed"
BRANDING = REPO / "Assets/PulseArena/Branding"

SKY_TOP = (86, 190, 255)
SKY_BOTTOM = (120, 92, 235)   # sky blue -> violet
YELLOW = (255, 210, 31)
YELLOW_LIGHT = (255, 240, 130)
RED_BROWN = (110, 28, 20)
NAVY = (18, 32, 96)
WHITE = (255, 255, 255)


def tool(name):
    p = FFMPEG_DIR / f"{name}.exe"
    return str(p) if p.exists() else (shutil.which(name) or name)


def run(cmd, **kw):
    r = subprocess.run([str(c) for c in cmd], capture_output=True, **kw)
    if r.returncode != 0:
        raise RuntimeError("command failed: %s\n%s" % (" ".join(map(str, cmd))[:300],
                                                       r.stderr.decode("utf-8", "replace")[-1500:]))
    return r


def font(size, bold=True):
    name = "Play-Bold.ttf" if bold else "Play-Regular.ttf"
    cands = list((REPO / "Assets/PulseArena/ThirdParty").rglob(name))
    if cands:
        return ImageFont.truetype(str(cands[0]), int(size))
    for fb in (r"C:\Windows\Fonts\arialbd.ttf", r"C:\Windows\Fonts\arial.ttf"):
        if Path(fb).exists():
            return ImageFont.truetype(fb, int(size))
    return ImageFont.load_default()


def gradient(w, h, top=SKY_TOP, bottom=SKY_BOTTOM):
    t = np.linspace(0, 1, h, dtype=np.float32)[:, None, None]
    a = np.array(top, np.float32)[None, None, :]
    b = np.array(bottom, np.float32)[None, None, :]
    arr = (a + (b - a) * t) * np.ones((1, w, 1), np.float32)
    return Image.fromarray(arr.astype(np.uint8), "RGB").convert("RGBA")


def rays(size, n=14, color=(255, 255, 255, 46)):
    """Square RGBA image with a burst of triangular rays from the centre."""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = size / 2
    r = size * 0.75
    for i in range(n):
        a0 = 2 * math.pi * i / n
        a1 = a0 + math.pi / n
        d.polygon([(c, c), (c + r * math.cos(a0), c + r * math.sin(a0)),
                   (c + r * math.cos(a1), c + r * math.sin(a1))], fill=color)
    return img


def burst_background(w, h, angle=0.0, strength=1.0):
    """Sky->violet gradient with a subtle light-ray burst and glow in the centre."""
    bg = gradient(w, h)
    s = int(math.hypot(w, h)) + 4
    r = rays(s, 16, (255, 255, 255, int(40 * strength))).rotate(angle, resample=Image.BILINEAR)
    bg.alpha_composite(r.crop(((s - w) // 2, (s - h) // 2, (s - w) // 2 + w, (s - h) // 2 + h)))
    glow = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse([w * .25, h * .15, w * .75, h * .85], fill=(255, 255, 255, int(70 * strength)))
    bg.alpha_composite(glow.filter(ImageFilter.GaussianBlur(min(w, h) * .12)))
    return bg


def alpha_bbox(img, thr=24):
    a = img.getchannel("A").point(lambda v: 255 if v > thr else 0)
    return a.getbbox() or (0, 0, img.width, img.height)


def fit(img, w, h):
    s = min(w / img.width, h / img.height)
    return img.resize((max(1, round(img.width * s)), max(1, round(img.height * s))), Image.LANCZOS)


def trim(img, thr=4):
    return img.crop(alpha_bbox(img, thr))


def shadow(img, offset=(0, 14), blur=14, opacity=110):
    """Return img with a soft drop shadow underneath (same canvas size, so keep margin)."""
    a = img.getchannel("A").point(lambda v: v * opacity // 255)
    sh = Image.new("RGBA", img.size, (10, 10, 50, 0))
    sh.putalpha(a)
    sh = sh.filter(ImageFilter.GaussianBlur(blur))
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    out.alpha_composite(ImageChops.offset(sh, offset[0], offset[1]))
    out.alpha_composite(img)
    return out


def pad_img(img, p):
    out = Image.new("RGBA", (img.width + 2 * p, img.height + 2 * p), (0, 0, 0, 0))
    out.alpha_composite(img, (p, p))
    return out


def chunky_text(text, fnt, fill_top, fill_bottom, extrude, outline, depth, stroke):
    """Chunky game-style lettering: gradient face, extruded side, thick outline."""
    pad = depth + stroke * 2 + 8
    probe = ImageDraw.Draw(Image.new("L", (4, 4)))
    l, t, r, b = probe.textbbox((0, 0), text, font=fnt, stroke_width=stroke)
    w, h = r - l + pad * 2, b - t + pad * 2 + depth
    ox, oy = pad - l, pad - t
    layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for i in range(depth, 0, -1):
        d.text((ox + i * 0.45, oy + i), text, font=fnt, fill=extrude, stroke_width=stroke, stroke_fill=outline)
    d.text((ox, oy), text, font=fnt, fill=outline, stroke_width=stroke, stroke_fill=outline)
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).text((ox, oy), text, font=fnt, fill=255)
    face = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    face.paste(gradient(w, h, fill_top, fill_bottom), (0, 0), mask)
    layer.alpha_composite(face)
    hl = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    ImageDraw.Draw(hl).rectangle([0, 0, w, int(h * .33)], fill=(255, 255, 255, 70))
    hl.putalpha(ImageChops.multiply(hl.getchannel("A"), mask))
    layer.alpha_composite(hl)
    return layer


def wordmark(width):
    """Stacked 'PULSE' / 'ARENA' wordmark, tilted, transparent, trimmed."""
    f = font(300, True)
    pulse = trim(chunky_text("PULSE", f, YELLOW_LIGHT, YELLOW, RED_BROWN, (60, 12, 12), 26, 12))
    arena = trim(chunky_text("ARENA", f, (255, 255, 255), (205, 226, 255), NAVY, (8, 14, 50), 26, 12))
    tw = max(pulse.width, arena.width)
    arena = arena.resize((tw, round(arena.height * tw / arena.width * 0.92)), Image.LANCZOS)
    pulse = pulse.resize((tw, round(pulse.height * tw / pulse.width)), Image.LANCZOS)
    gap = -int(pulse.height * 0.06)
    stack = Image.new("RGBA", (tw, pulse.height + arena.height + gap), (0, 0, 0, 0))
    stack.alpha_composite(pulse, (0, 0))
    stack.alpha_composite(arena, (0, pulse.height + gap))
    canvas = trim(pad_img(stack, 40).rotate(4, resample=Image.BICUBIC, expand=True))
    return fit(canvas, width, 10 ** 5)


def badge(size):
    """Circular sky-blue/violet burst badge with white ring (transparent outside)."""
    s = size * 2
    bg = burst_background(s, s, 0, 1.2)
    m = Image.new("L", (s, s), 0)
    ImageDraw.Draw(m).ellipse([s * .02, s * .02, s * .98, s * .98], fill=255)
    out = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    out.paste(bg, (0, 0), m)
    d = ImageDraw.Draw(out)
    d.ellipse([s * .02, s * .02, s * .98, s * .98], outline=(255, 255, 255, 255), width=int(s * .018))
    d.ellipse([s * .045, s * .045, s * .955, s * .955], outline=(40, 40, 130, 150), width=int(s * .008))
    return out.resize((size, size), Image.LANCZOS)


def load_character(showcase, alt=False):
    p = Path(showcase) / ("logo_character_alt.png" if alt else "logo_character.png")
    return Image.open(p).convert("RGBA") if p.exists() else None


def with_shadow(img, offset=(0, 10), blur=12, opacity=120):
    return trim(shadow(pad_img(img, blur * 3), offset, blur, opacity), 2)


def make_logo_image(char, w=1600, h=900):
    """Transparent 1600x900 logo: badge + character on the left, wordmark on the right."""
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    bsz = int(h * 0.96)
    bx, by = int(w * 0.03), (h - bsz) // 2
    img.alpha_composite(badge(bsz), (bx, by))
    if char is not None:
        c = fit(with_shadow(trim(char)), int(bsz * 1.02), int(h * 0.98))
        img.alpha_composite(c, (bx + (bsz - c.width) // 2, h - c.height - int(h * 0.01)))
    wm = with_shadow(wordmark(int(w * 0.56)), (0, 10), 10, 130)
    img.alpha_composite(wm, (w - wm.width - int(w * 0.02), (h - wm.height) // 2 + 10))
    return img


def ease_out_back(t, s=1.9):
    t = min(max(t, 0), 1) - 1
    return 1 + (s + 1) * t ** 3 + s * t ** 2
