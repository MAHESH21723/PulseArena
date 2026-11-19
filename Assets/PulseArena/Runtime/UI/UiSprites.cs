using System;
using UnityEngine;

namespace PulseArena
{
    /// Procedural sprites for the bright stylized UI: 9-slice rounded rects, outline rings, soft drop shadows and glyph icons.
    /// Rounded sprites are white; tint them with Image.color. Use UiKit.Round(image, radius) to apply one with a given corner radius.
    public static class UiSprites
    {
        public const int Size = 128, Corner = 48, ShadowBlur = 40;
        static Sprite roundRect, roundRing, softShadow, gear, play, pause, close, check, arrow;

        /// White rounded rectangle, 9-sliced (border = 48px). Set Image.type = Sliced.
        public static Sprite RoundRect => roundRect ? roundRect : roundRect = Build(Size, Size, (x, y) => Mathf.Clamp01(.5f - Box(x, y, 64, 64, Corner)), Corner);
        /// Rounded outline (6px thick at texture scale), 9-sliced.
        public static Sprite RoundRing => roundRing ? roundRing : roundRing = Build(Size, Size, (x, y) =>
        {
            float d = Box(x, y, 64, 64, Corner);
            return Mathf.Clamp01(.5f - d) * Mathf.Clamp01(d + 6.5f);
        }, Corner);
        /// Soft blurred rounded rect for drop shadows. The visible body is inset by ShadowBlur px on every side.
        public static Sprite SoftShadow => softShadow ? softShadow : softShadow = Build(Size, Size, (x, y) =>
        {
            float d = Box(x, y, 64 - ShadowBlur, 64 - ShadowBlur, 18);
            float t = Mathf.Clamp01(d / ShadowBlur);
            float a = 1 - t * t * (3 - 2 * t);
            return a * a;
        }, 60);

        public static Sprite Gear => gear ? gear : gear = Icon((x, y) =>
        {
            float d = Mathf.Sqrt(x * x + y * y); float a = Mathf.Atan2(y, x);
            float r = .6f + .27f * Mathf.SmoothStep(-.2f, .2f, Mathf.Cos(a * 8));
            return Mathf.Clamp01((r - d) * 40) * Mathf.Clamp01((d - .27f) * 40);
        });
        public static Sprite Play => play ? play : play = Icon((x, y) =>
            Mathf.Clamp01(Mathf.Min((x + .45f) * 30, ((.7f - x) * .56f - Mathf.Abs(y)) * 30)));
        public static Sprite Pause => pause ? pause : pause = Icon((x, y) =>
            Mathf.Clamp01((.62f - Mathf.Abs(y)) * 30) * Mathf.Clamp01(Mathf.Min((Mathf.Abs(x) - .14f) * 30, (.5f - Mathf.Abs(x)) * 30)));
        public static Sprite Close => close ? close : close = Icon((x, y) =>
        {
            float d = Mathf.Min(Mathf.Abs(x - y), Mathf.Abs(x + y)) * .7071f;
            return Mathf.Clamp01((.13f - d) * 40) * Mathf.Clamp01((.64f - Mathf.Max(Mathf.Abs(x), Mathf.Abs(y))) * 30);
        });
        public static Sprite Check => check ? check : check = Icon((x, y) =>
        {
            float d1 = SegDist(x, y, -.55f, 0f, -.15f, -.45f), d2 = SegDist(x, y, -.15f, -.45f, .6f, .5f);
            return Mathf.Clamp01((.13f - Mathf.Min(d1, d2)) * 40);
        });
        public static Sprite Arrow => arrow ? arrow : arrow = Icon((x, y) =>
        {
            float d1 = SegDist(x, y, .3f, .55f, -.35f, 0f), d2 = SegDist(x, y, -.35f, 0f, .3f, -.55f);
            return Mathf.Clamp01((.13f - Mathf.Min(d1, d2)) * 40);
        });

        static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay; float t = Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy));
            float cx = ax + dx * t - px, cy = ay + dy * t - py; return Mathf.Sqrt(cx * cx + cy * cy);
        }
        /// Signed distance (pixels) to a rounded box centred in the texture with the given half size.
        static float Box(float x, float y, float halfW, float halfH, float r)
        {
            float qx = Mathf.Abs(x - 64) - (halfW - r), qy = Mathf.Abs(y - 64) - (halfH - r);
            float ox = Mathf.Max(qx, 0), oy = Mathf.Max(qy, 0);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
        }
        static Sprite Icon(Func<float, float, float> alpha)
        {
            var tex = NewTex(Size, Size); var px = new Color[Size * Size];
            for (int j = 0; j < Size; j++)
                for (int i = 0; i < Size; i++)
                    px[j * Size + i] = new Color(1, 1, 1, alpha((i + .5f) / Size * 2 - 1, (j + .5f) / Size * 2 - 1));
            tex.SetPixels(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(.5f, .5f), 100);
        }
        static Sprite Build(int w, int h, Func<float, float, float> alphaPx, float border)
        {
            var tex = NewTex(w, h); var px = new Color[w * h];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                    px[j * w + i] = new Color(1, 1, 1, alphaPx(i + .5f, j + .5f));
            tex.SetPixels(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }
        static Texture2D NewTex(int w, int h) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
    }
}
