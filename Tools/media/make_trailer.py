"""PulseArena trailer builder.

Usage:
  python make_trailer.py <showcase_dir> <out_dir> [--bed-db -24] [--no-bed] [--keep-temp]

<showcase_dir> contains frames/f_00000.jpg... (1920x1080 @30fps) and events.csv (frame,clip,volume).
<out_dir> receives trailer.mp4, trailer-square.mp4, trailer-thumb.jpg.
logo.png is taken from <out_dir> (run make_logo.py first) and rendered on the fly if missing.

Structure: 2.5 s animated logo intro -> gameplay frames -> 4 s outro, 0.3 s cross-fades.
Audio: events.csv SFX placed sample-accurately, synthesized 120 BPM bed (-24 dB under SFX),
soft-clipped, loudnorm to about -14 LUFS, 48 kHz stereo AAC 192k.
"""
import argparse
import csv
import math
import re
import shutil
import subprocess
import sys
import tempfile
import wave
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, str(Path(__file__).resolve().parent))
import pa_common as pc  # noqa: E402

W, H, FPS, SR = 1920, 1080, 30, 48000
INTRO, OUTRO, XF = 2.5, 4.0, 0.3


# ---------------------------------------------------------------- visuals
class Backdrop:
    """Sky gradient + soft glow (static) with slowly rotating light rays."""

    def __init__(self):
        self.base = pc.gradient(W, H)
        glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        ImageDraw.Draw(glow).ellipse([W * .22, H * .1, W * .78, H * .9], fill=(255, 255, 255, 80))
        self.base.alpha_composite(glow.filter(ImageFilter.GaussianBlur(120)))
        self.s = int(math.hypot(W, H)) + 4
        self.rays = pc.rays(self.s, 16, (255, 255, 255, 38))

    def frame(self, t):
        f = self.base.copy()
        r = self.rays.rotate(t * 7, resample=Image.BILINEAR)
        ox, oy = (self.s - W) // 2, (self.s - H) // 2
        f.alpha_composite(r.crop((ox, oy, ox + W, oy + H)))
        return f


def paste_center(canvas, img, cx, cy, alpha=1.0):
    if alpha < 1.0:
        img = img.copy()
        img.putalpha(img.getchannel("A").point(lambda v: int(v * alpha)))
    canvas.paste(img, (int(cx - img.width / 2), int(cy - img.height / 2)), img)


def text_center(canvas, xy, text, size, fill, alpha=1.0, bold=True, stroke=4, stroke_fill=(30, 30, 110)):
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).text(xy, text, font=pc.font(size, bold), fill=fill, anchor="mm",
                               stroke_width=stroke, stroke_fill=stroke_fill)
    if alpha < 1.0:
        layer.putalpha(layer.getchannel("A").point(lambda v: int(v * alpha)))
    canvas.alpha_composite(layer)


def smooth(t, a, b):
    x = min(max((t - a) / (b - a), 0.0), 1.0)
    return x * x * (3 - 2 * x)


def render_segment(path, nframes, painter):
    cmd = [pc.tool("ffmpeg"), "-y", "-v", "error", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}",
           "-r", str(FPS), "-i", "-", "-c:v", "libx264", "-crf", "10", "-preset", "veryfast",
           "-pix_fmt", "yuv420p", str(path)]
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE, stderr=subprocess.PIPE)
    for i in range(nframes):
        img = painter(i / FPS)
        p.stdin.write(img.convert("RGB").tobytes())
    p.stdin.close()
    err = p.stderr.read().decode("utf-8", "replace")
    if p.wait() != 0:
        raise RuntimeError("segment encode failed: " + err[-1000:])


def make_intro(path, logo, bd):
    def paint(t):
        f = bd.frame(t)
        s = 0.92 * max(0.02, pc.ease_out_back((t - 0.15) / 0.75))
        a = smooth(t, 0.1, 0.35)
        lg = logo.resize((max(2, int(logo.width * s)), max(2, int(logo.height * s))), Image.BILINEAR)
        paste_center(f, lg, W / 2, H * 0.47, a)
        text_center(f, (W / 2, H * 0.93), "4-PLAYER MOBILE ARENA SHOOTER", 52, (255, 255, 255),
                    smooth(t, 1.2, 1.7))
        return f
    render_segment(path, int(round(INTRO * FPS)), paint)


def make_outro(path, logo, bd):
    lg = pc.fit(logo, 760, 430)
    lines = [("Made by Maheshwaran", 72, pc.YELLOW, 0.3, (80, 14, 14)),
             ("kmaheshwaran9965@gmail.com", 48, (255, 255, 255), 0.7, (30, 30, 110)),
             ("github.com/MAHESH21723/PulseArena", 48, (255, 255, 255), 1.0, (30, 30, 110)),
             ("Android · Unity 6 · Netcode", 40, (225, 235, 255), 1.4, (30, 30, 110))]

    def paint(t):
        f = bd.frame(t + 20)
        paste_center(f, lg, W / 2, 280, smooth(t, 0.0, 0.4))
        y = 600
        for text, size, col, t0, sc in lines:
            text_center(f, (W / 2, y), text, size, col, smooth(t, t0, t0 + 0.4), True, 5, sc)
            y += size + 52
        k = 1.0 - smooth(t, OUTRO - 0.9, OUTRO)  # soft fade to dark
        if k < 1.0:
            dark = Image.new("RGBA", f.size, (10, 12, 40, int(255 * (1 - k))))
            f.alpha_composite(dark)
        return f
    render_segment(path, int(round(OUTRO * FPS)), paint)


# ---------------------------------------------------------------- audio
_wav_cache = {}


def load_clip(name):
    if name in _wav_cache:
        return _wav_cache[name]
    p = pc.MIXED / (name if name.endswith(".wav") else name + ".wav")
    if not p.exists():
        _wav_cache[name] = None
        return None
    r = pc.run([pc.tool("ffmpeg"), "-v", "error", "-i", p, "-f", "f32le", "-ac", "1", "-ar", SR, "-"])
    a = np.frombuffer(r.stdout, dtype="<f4").astype(np.float32)
    _wav_cache[name] = a
    return a


def place(buf, clip, t, vol):
    i = int(round(t * SR))
    if clip is None or i >= len(buf):
        return
    i0 = max(i, 0)
    seg = clip[i0 - i:]
    seg = seg[:len(buf) - i0]
    buf[i0:i0 + len(seg)] += seg * vol


def synth_whoosh(dur=0.9):
    n = int(dur * SR)
    rng = np.random.default_rng(7)
    noise = rng.standard_normal(n).astype(np.float32)
    t = np.linspace(0, 1, n, dtype=np.float32)
    cutoff = 300 * (2 ** (t * 5))          # sweep up
    alpha = 1 - np.exp(-2 * np.pi * np.minimum(cutoff, 9000) / SR)
    out = np.zeros(n, np.float32)
    y = 0.0
    for i in range(n):
        y += alpha[i] * (noise[i] - y)
        out[i] = y
    env = np.sin(np.pi * np.clip(t, 0, 1) ** 0.8) ** 2
    out *= env
    return out / (np.abs(out).max() + 1e-9) * 0.7


def synth_bed(total, bpm=120):
    n = int(total * SR)
    bed = np.zeros(n, np.float32)
    beat = 60.0 / bpm
    # kick
    kn = int(0.22 * SR)
    tk = np.arange(kn) / SR
    kick = np.sin(2 * np.pi * (45 * tk + 75 * (1 - np.exp(-tk * 30)) / 30)) * np.exp(-tk * 18)
    # hat
    hn = int(0.05 * SR)
    th = np.arange(hn) / SR
    hat = np.diff(np.random.default_rng(3).standard_normal(hn + 1)) * np.exp(-th * 90) * 0.25
    # bass notes (Hz) per half-beat, 2-bar pattern in A minor
    notes = [55, 55, 82.4, 55, 65.4, 65.4, 98, 65.4, 49, 49, 73.4, 49, 55, 55, 82.4, 98]
    bn = int(beat / 2 * SR)
    tb = np.arange(bn) / SR
    env = np.minimum(1, tb * 200) * np.exp(-tb * 5)
    b = 0
    while b * beat / 2 < total:
        t0 = b * beat / 2
        i = int(t0 * SR)
        f = notes[b % len(notes)]
        seg = (np.sin(2 * np.pi * f * tb) + 0.35 * np.sin(2 * np.pi * 2 * f * tb)) * env * 0.5
        m = min(bn, n - i)
        if m > 0:
            bed[i:i + m] += seg[:m]
        if b % 2 == 0:
            m = min(kn, n - i)
            if m > 0:
                bed[i:i + m] += kick[:m] * 1.0
        else:
            m = min(hn, n - i)
            if m > 0:
                bed[i:i + m] += hat[:m]
        b += 1
    return bed


def build_audio(events_csv, game_start, total, wav_path, bed_db, use_bed):
    n = int(total * SR) + SR
    sfx = np.zeros(n, np.float32)
    # intro whoosh + impact, outro sting
    place(sfx, synth_whoosh(), 0.05, 0.8)
    place(sfx, load_clip("explosion_1"), 0.72, 0.45)
    place(sfx, load_clip("match_win"), total - OUTRO + 0.2, 0.55)
    missing = set()
    count = 0
    if events_csv.exists():
        with open(events_csv, newline="", encoding="utf-8-sig") as fh:
            for row in csv.DictReader(fh):
                try:
                    fr = int(row["frame"])
                    vol = float(row.get("volume") or 1.0)
                except (ValueError, KeyError):
                    continue
                clip = load_clip(row["clip"].strip())
                if clip is None:
                    missing.add(row["clip"])
                    continue
                place(sfx, clip, game_start + fr / FPS, vol)
                count += 1
    print("audio: placed %d SFX events%s" % (count, (", missing clips: " + ", ".join(sorted(missing))) if missing else ""))
    mix = sfx
    if use_bed:
        bed = synth_bed(total + 1)[:n]
        bed = np.pad(bed, (0, n - len(bed)))
        act = sfx[np.abs(sfx) > 0.01]
        ref = float(np.sqrt(np.mean(act ** 2))) if len(act) > 100 else 0.15
        brms = float(np.sqrt(np.mean(bed ** 2))) + 1e-9
        bed *= ref * (10 ** (bed_db / 20)) / brms
        t = np.arange(n) / SR
        bed *= np.clip(t / 1.0, 0, 1) * np.clip((total - t) / 1.5, 0, 1)
        mix = sfx + bed
    mix = mix[:int(total * SR)]
    mix = np.tanh(mix * 1.1) / np.tanh(1.1) * 0.9    # soft clip
    t = np.arange(len(mix)) / SR
    mix *= np.clip(t / 0.05, 0, 1) * np.clip((total - t) / 0.8, 0, 1)
    st = (np.clip(mix, -1, 1) * 32767).astype("<i2")
    st = np.repeat(st[:, None], 2, axis=1)
    with wave.open(str(wav_path), "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(st.tobytes())


# ---------------------------------------------------------------- main
def probe_duration(p):
    r = pc.run([pc.tool("ffprobe"), "-v", "error", "-show_entries", "format=duration",
                "-of", "default=nw=1:nk=1", p])
    return float(r.stdout.decode().strip())


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("showcase_dir")
    ap.add_argument("out_dir")
    ap.add_argument("--bed-db", type=float, default=-24.0, help="music bed level relative to SFX (dB)")
    ap.add_argument("--no-bed", action="store_true")
    ap.add_argument("--keep-temp", action="store_true")
    args = ap.parse_args()

    show, out = Path(args.showcase_dir), Path(args.out_dir)
    out.mkdir(parents=True, exist_ok=True)
    frames_dir = show / "frames"
    files = sorted(frames_dir.glob("f_*.jpg"))
    if not files:
        sys.exit("no frames in %s" % frames_dir)
    start = int(re.search(r"(\d+)", files[0].stem).group(1))
    n = len(files)
    G = n / FPS
    total = INTRO + G + OUTRO - 2 * XF
    game_start = INTRO - XF
    print("frames: %d (%.2fs); trailer %.2fs" % (n, G, total))

    logo_path = out / "logo.png"
    if logo_path.exists():
        logo = Image.open(logo_path).convert("RGBA")
    else:
        logo = pc.make_logo_image(pc.load_character(show))
    bd = Backdrop()

    tmp = Path(tempfile.mkdtemp(prefix="pa_trailer_"))
    try:
        make_intro(tmp / "intro.mp4", logo, bd)
        make_outro(tmp / "outro.mp4", logo, bd)
        build_audio(show / "events.csv", game_start, total, tmp / "audio.wav", args.bed_db, not args.no_bed)

        norm = "fps=%d,scale=%d:%d:in_range=auto:out_range=tv,format=yuv420p,setsar=1" % (FPS, W, H)
        fc = ("[0:v]%s[a];[1:v]%s[b];[2:v]%s[c];"
              "[a][b]xfade=transition=fade:duration=%s:offset=%s[ab];"
              "[ab][c]xfade=transition=fade:duration=%s:offset=%s[v]") % (
            norm, norm, norm, XF, round(INTRO - XF, 3), XF, round(INTRO + G - 2 * XF, 3))
        trailer = out / "trailer.mp4"
        pc.run([pc.tool("ffmpeg"), "-y", "-v", "error",
                "-i", tmp / "intro.mp4",
                "-framerate", FPS, "-start_number", start, "-i", frames_dir / "f_%05d.jpg",
                "-i", tmp / "outro.mp4", "-i", tmp / "audio.wav",
                "-filter_complex", fc, "-map", "[v]", "-map", "3:a",
                "-af", "loudnorm=I=-14:TP=-1.5:LRA=11,alimiter=limit=0.9",
                "-c:v", "libx264", "-crf", 18, "-preset", "medium", "-profile:v", "high", "-pix_fmt", "yuv420p",
                "-r", FPS, "-c:a", "aac", "-b:a", "192k", "-ar", SR, "-ac", 2,
                "-t", "%.3f" % total, "-movflags", "+faststart", trailer])

        sq = ("[0:v]split[a][b];[a]scale=1080:1080:force_original_aspect_ratio=increase,crop=1080:1080,"
              "boxblur=30:5,eq=brightness=-0.08[bg];[b]scale=1080:-2[fg];"
              "[bg][fg]overlay=(W-w)/2:(H-h)/2,format=yuv420p[v]")
        pc.run([pc.tool("ffmpeg"), "-y", "-v", "error", "-i", trailer, "-filter_complex", sq,
                "-map", "[v]", "-map", "0:a", "-c:v", "libx264", "-crf", 20, "-preset", "medium",
                "-profile:v", "high", "-pix_fmt", "yuv420p", "-c:a", "copy", "-movflags", "+faststart",
                out / "trailer-square.mp4"])

        # thumbnail: gameplay frame ~35% in, with the logo in the corner
        mid = files[int(n * 0.35)]
        th = Image.open(mid).convert("RGBA").resize((W, H), Image.LANCZOS)
        lg = pc.fit(logo, 640, 360)
        th.alpha_composite(lg, (40, 30))
        th.convert("RGB").save(out / "trailer-thumb.jpg", quality=90)
    finally:
        if args.keep_temp:
            print("temp kept:", tmp)
        else:
            shutil.rmtree(tmp, ignore_errors=True)

    for p in ("trailer.mp4", "trailer-square.mp4", "trailer-thumb.jpg"):
        print(out / p)
    print("trailer.mp4 duration: %.2fs (expected %.2fs)" % (probe_duration(out / "trailer.mp4"), total))


if __name__ == "__main__":
    main()
