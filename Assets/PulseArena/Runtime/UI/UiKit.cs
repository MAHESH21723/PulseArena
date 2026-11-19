using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PulseArena
{
    /// Small uGUI construction helpers shared by PulseUI and PulseHud (everything is built in code).
    public static class UiKit
    {
        public static TMP_FontAsset Font, Bold;
        public static readonly Color White = new Color(.91f, .97f, 1), Muted = new Color(.62f, .74f, .82f), Ink = new Color(.028f, .055f, .084f, .97f);
        public static readonly Color Cyan = new Color(.2f, .86f, 1), Amber = new Color(1, .65f, .2f), Red = new Color(1, .22f, .16f), Panel = new Color(.09f, .15f, .2f);

        public static void LoadFonts()
        {
            if (Font) return;
            Font = Resources.Load<TMP_FontAsset>("Fonts/PulseFont");
            Bold = Resources.Load<TMP_FontAsset>("Fonts/PulseFontBold");
            if (!Bold) Bold = Font;
        }

        // ---- procedural sprites
        static Sprite circle, ring, vignette, arc, gradient;
        public static Sprite Circle => circle ? circle : circle = Make(128, (x, y) => Mathf.Clamp01((1 - Mathf.Sqrt(x * x + y * y)) * 64));
        public static Sprite Ring => ring ? ring : ring = Make(128, (x, y) => { float d = Mathf.Sqrt(x * x + y * y); return Mathf.Clamp01((1 - d) * 64) * Mathf.Clamp01((d - .93f) * 64); });
        public static Sprite Vignette => vignette ? vignette : vignette = Make(128, (x, y) => Mathf.SmoothStep(0, 1, Mathf.Clamp01((Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) * .6f + Mathf.Sqrt(x * x + y * y) * .4f - .45f) / .6f)));
        /// Ring segment of +-24 degrees at the top of a unit ring; rotate the Image to point at a direction.
        public static Sprite Arc => arc ? arc : arc = Make(128, (x, y) =>
        {
            float d = Mathf.Sqrt(x * x + y * y); float a = Mathf.Abs(Mathf.Atan2(x, y)) * Mathf.Rad2Deg;
            return Mathf.Clamp01((d - .84f) * 40) * Mathf.Clamp01((.99f - d) * 40) * Mathf.Clamp01((24 - a) * .5f);
        });
        /// Horizontal alpha ramp, opaque on the left.
        public static Sprite Gradient => gradient ? gradient : gradient = Make(64, (x, y) => Mathf.Clamp01(1 - (x + 1) * .5f), 4);

        static Sprite Make(int size, Func<float, float, float> alpha, int height = 0)
        {
            int h = height > 0 ? height : size;
            var tex = new Texture2D(size, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[size * h];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < size; i++)
                    px[j * size + i] = new Color(1, 1, 1, alpha((i + .5f) / size * 2 - 1, (j + .5f) / h * 2 - 1));
            tex.SetPixels(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, h), new Vector2(.5f, .5f), 100);
        }

        // ---- factories
        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform)); var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false); rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(.5f, .5f); rt.anchoredPosition = pos; rt.sizeDelta = size; return rt;
        }
        public static RectTransform Full(string name, Transform parent)
        { var r = Rect(name, parent, Vector2.zero, Vector2.zero, Vector2.zero); r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; return r; }
        public static RectTransform Corner(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        { var r = Rect(name, parent, anchor, pos, size); r.pivot = anchor; return r; }
        public static Image Img(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color, bool ray = false, Sprite sprite = null)
        {
            var r = Rect(name, parent, anchor, pos, size); var im = r.gameObject.AddComponent<Image>();
            im.color = color; im.raycastTarget = ray; if (sprite) im.sprite = sprite; return im;
        }
        public static TMP_Text Text(string name, Transform parent, string value, Vector2 anchor, Vector2 pos, Vector2 size, float points, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool bold = false)
        {
            var r = Rect(name, parent, anchor, pos, size); var t = r.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = bold ? Bold : Font; t.text = value; t.fontSize = points; t.color = color; t.alignment = align; t.raycastTarget = false;
            t.richText = true; t.textWrappingMode = TextWrappingModes.Normal; t.overflowMode = TextOverflowModes.Ellipsis;
            // Ellipsis hides a line entirely when the box is shorter than the font's line height
            // (Play Bold is ~1.2x the point size). Single-line boxes that are too short must overflow instead.
            if (size.y < points * 1.35f) { t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Overflow; }
            return t;
        }
        public static void Set(TMP_Text t, string s) { if (t.text != s) t.text = s; }

        public static string Clean(string s) => s == null ? "" : s.Replace('<', ' ');

        public enum Sfx { Click, Confirm, Back }

        // ---- bright stylized palette (HUD keeps using the dark colours above)
        public static readonly Color Paper = new Color(.97f, .98f, 1), PaperDeep = new Color(.88f, .93f, 1), Navy = new Color(.1f, .15f, .36f), Slate = new Color(.38f, .47f, .66f),
            Sky = new Color(.27f, .62f, 1), SkyDeep = new Color(.2f, .4f, .93f), Violet = new Color(.52f, .36f, .98f), Sun = new Color(1, .82f, .22f), Orange = new Color(1, .55f, .13f),
            Mint = new Color(.3f, .86f, .5f), Pink = new Color(1, .4f, .62f), Teal = new Color(.1f, .8f, .8f);
        public static readonly Color[] PlayerColors = { new Color(.27f, .62f, 1), new Color(1, .4f, .5f), new Color(.3f, .86f, .5f), new Color(1, .74f, .2f) };

        public enum BtnStyle { Primary, Secondary, Danger, Success, Blue, Glass, Segment }
        static ButtonFx.Palette PaletteFor(BtnStyle s)
        {
            switch (s)
            {
                case BtnStyle.Primary: return new ButtonFx.Palette(new Color(1, .86f, .28f), new Color(1, .56f, .12f), new Color(.74f, .31f, .04f), new Color(.27f, .1f, .0f));
                case BtnStyle.Danger: return new ButtonFx.Palette(new Color(1, .48f, .45f), new Color(.92f, .2f, .27f), new Color(.6f, .08f, .18f), Color.white);
                case BtnStyle.Success: return new ButtonFx.Palette(new Color(.56f, .96f, .46f), new Color(.14f, .74f, .38f), new Color(.06f, .46f, .24f), new Color(.02f, .22f, .1f));
                case BtnStyle.Blue: return new ButtonFx.Palette(new Color(.4f, .72f, 1), new Color(.2f, .42f, .96f), new Color(.1f, .24f, .66f), Color.white);
                case BtnStyle.Glass: return new ButtonFx.Palette(new Color(.2f, .27f, .5f, .82f), new Color(.1f, .14f, .34f, .82f), new Color(.04f, .06f, .18f, .85f), Color.white);
                case BtnStyle.Segment: return new ButtonFx.Palette(new Color(.97f, .98f, 1), new Color(.88f, .92f, 1), new Color(.68f, .77f, .94f), new Color(.2f, .3f, .55f));
                default: return new ButtonFx.Palette(Color.white, new Color(.86f, .92f, 1), new Color(.56f, .68f, .88f), Navy);
            }
        }

        /// Style-based chunky button. Hierarchy: root(Button) / Shadow, Lip, Face(gradient)/Gloss, Label (direct child, so SetLabel works).
        public static Button MakeButton(Transform parent, string objectName, string label, Vector2 anchor, Vector2 pos, Vector2 size, Action action, BtnStyle style, Sfx sfx = Sfx.Click, float points = 28)
        {
            var root = Rect(objectName, parent, anchor, pos, size);
            var hit = root.gameObject.AddComponent<Image>(); hit.color = new Color(1, 1, 1, 0); hit.raycastTarget = true;
            var button = root.gameObject.AddComponent<Button>(); button.targetGraphic = hit; button.transition = Selectable.Transition.None;
            float radius = Mathf.Min(size.y * .34f, 34), lip = Mathf.Clamp(size.y * .13f, 5, 12);
            var shadow = RImg("Shadow", root, Vector2.zero, Vector2.zero, Vector2.zero, new Color(.04f, .08f, .25f, .3f), 1);
            shadow.sprite = UiSprites.SoftShadow; shadow.pixelsPerUnitMultiplier = 2.2f;
            Stretch(shadow.rectTransform, new Vector2(-18, -18 - 9), new Vector2(18, 18 - 9));
            var lipImg = Round(Img("Lip", root, Vector2.zero, Vector2.zero, Vector2.zero, Color.white), radius); Stretch(lipImg.rectTransform, Vector2.zero, Vector2.zero);
            var face = Round(Img("Face", root, Vector2.zero, Vector2.zero, Vector2.zero, Color.white), radius); Stretch(face.rectTransform, new Vector2(0, lip), Vector2.zero);
            var grad = face.gameObject.AddComponent<UiGradient>();
            var gloss = Round(Img("Gloss", face.transform, Vector2.zero, Vector2.zero, Vector2.zero, new Color(1, 1, 1, .13f)), radius * .6f);
            gloss.rectTransform.anchorMin = new Vector2(0, .64f); gloss.rectTransform.anchorMax = Vector2.one; gloss.rectTransform.offsetMin = new Vector2(radius * .5f, 0); gloss.rectTransform.offsetMax = new Vector2(-radius * .5f, -size.y * .06f);
            var pal = PaletteFor(style);
            var lt = Text("Label", root, label, new Vector2(.5f, .5f), Vector2.zero, size, points, pal.Label, TextAlignmentOptions.Center, true);
            var lr = lt.rectTransform; lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = new Vector2(12, lip); lr.offsetMax = new Vector2(-12, 0);
            lt.enableAutoSizing = true; lt.fontSizeMax = points; lt.fontSizeMin = Mathf.Max(16, points * .65f);
            var fx = root.gameObject.AddComponent<ButtonFx>();
            fx.Normal = pal; fx.Selected = style == BtnStyle.Segment ? PaletteFor(BtnStyle.Blue) : PaletteFor(BtnStyle.Blue);
            fx.Lip = lipImg; fx.Face = face; fx.Gloss = gloss; fx.Shadow = shadow; fx.FaceRt = face.rectTransform; fx.LabelRt = lr; fx.Label = lt; fx.LipHeight = lip;
            fx.Apply(true);
            string key = sfx == Sfx.Confirm ? "ui_confirm" : sfx == Sfx.Back ? "ui_back" : "ui_click";
            button.onClick.AddListener(() => { if (PulseAudio.Instance) PulseAudio.Instance.PlayUi(key, .8f); action(); });
            return button;
        }
        /// Legacy signature: accent = Primary (orange), otherwise Secondary (white).
        public static Button MakeButton(Transform parent, string objectName, string label, Vector2 anchor, Vector2 pos, Vector2 size, Action action, bool accent = false, Sfx sfx = Sfx.Click, float points = 28)
            => MakeButton(parent, objectName, label, anchor, pos, size, action, accent ? BtnStyle.Primary : BtnStyle.Secondary, sfx, points);

        /// Square/round icon button (glyph sprite from UiSprites, or pass null and use SetLabel for a text glyph such as "?").
        public static Button MakeIconButton(Transform parent, string objectName, Sprite icon, string glyph, Vector2 anchor, Vector2 pos, Vector2 size, Action action, BtnStyle style = BtnStyle.Secondary, Sfx sfx = Sfx.Click)
        {
            var b = MakeButton(parent, objectName, glyph ?? "", anchor, pos, size, action, style, sfx, Mathf.Min(size.y * .5f, 48));
            var fx = b.GetComponent<ButtonFx>();
            if (icon)
            {
                float s = Mathf.Min(size.x, size.y) * .5f;
                var im = Img("Icon", b.transform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(s, s), fx.Normal.Label, false, icon);
                fx.Icon = im; fx.IconRt = im.rectTransform; fx.Apply(true);
            }
            return b;
        }
        public static void SetLabel(Button b, string text) { var t = b.transform.Find("Label"); if (t) Set(t.GetComponent<TMP_Text>(), text); }
        /// Selected state (segmented controls, mode toggles): blue face, white label.
        public static void SetSelected(Button b, bool selected) { if (!b) return; var fx = b.GetComponent<ButtonFx>(); if (fx) fx.SetSelected(selected); }

        // ---- rounded shapes, cards, text effects
        static void Stretch(RectTransform r, Vector2 min, Vector2 max) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = min; r.offsetMax = max; }
        /// Turn an Image into a 9-sliced rounded rect with the given corner radius (in canvas units).
        public static Image Round(Image im, float radius)
        {
            im.sprite = UiSprites.RoundRect; im.type = Image.Type.Sliced; im.pixelsPerUnitMultiplier = UiSprites.Corner / Mathf.Max(radius, 1f); return im;
        }
        public static Image RImg(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color, float radius, bool ray = false)
            => Round(Img(name, parent, anchor, pos, size, color, ray), radius);
        /// Fully rounded (pill) rect.
        public static Image Pill(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color, bool ray = false)
            => RImg(name, parent, anchor, pos, size, color, Mathf.Min(size.x, size.y) * .5f, ray);
        /// Rounded outline overlay (stretches to its parent). thicknessScale 1 = ~6px at radius 48.
        public static Image Outline(Transform parent, Color color, float radius)
        {
            var im = Round(Img("Outline", parent, Vector2.zero, Vector2.zero, Vector2.zero, color), radius); im.sprite = UiSprites.RoundRing;
            Stretch(im.rectTransform, Vector2.zero, Vector2.zero); return im;
        }
        /// Card with soft drop shadow. Returns the root; the fill image is root.Find("Body"). Add content as children of the root.
        public static RectTransform Card(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color fill, float radius = 36, bool shadow = true, float outline = 0)
        {
            var root = Rect(name, parent, anchor, pos, size);
            if (shadow)
            {
                var sh = Img("Shadow", root, Vector2.zero, Vector2.zero, Vector2.zero, new Color(.03f, .06f, .22f, .32f), false, UiSprites.SoftShadow);
                sh.type = Image.Type.Sliced; sh.pixelsPerUnitMultiplier = 1.6f; Stretch(sh.rectTransform, new Vector2(-26, -26 - 14), new Vector2(26, 26 - 14));
            }
            var body = Round(Img("Body", root, Vector2.zero, Vector2.zero, Vector2.zero, fill, true), radius); Stretch(body.rectTransform, Vector2.zero, Vector2.zero);
            if (outline > 0) Outline(root, new Color(.55f, .68f, .95f, outline), radius);
            return root;
        }
        /// Chunky title text: a darker copy offset downwards sits behind the main text. Returns the main text.
        public static TMP_Text ChunkyText(string name, Transform parent, string value, Vector2 anchor, Vector2 pos, Vector2 size, float points, Color color, Color shadow, float depth = 8,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var back = Text(name + " shadow", parent, value, anchor, pos + new Vector2(depth * .4f, -depth), size, points, shadow, align, true);
            var main = Text(name, parent, value, anchor, pos, size, points, color, align, true);
            if (anchor.x == 0) { back.rectTransform.pivot = new Vector2(0, .5f); main.rectTransform.pivot = new Vector2(0, .5f); }
            return main;
        }
        /// On/off switch with ON/OFF text. Returns the button; use SetSwitch to change the displayed state.
        public static Button MakeSwitch(Transform parent, string objectName, Vector2 anchor, Vector2 pos, Vector2 size, Action onClick)
        {
            var root = Rect(objectName, parent, anchor, pos, size);
            var hit = root.gameObject.AddComponent<Image>(); hit.color = new Color(1, 1, 1, 0); hit.raycastTarget = true;
            var button = root.gameObject.AddComponent<Button>(); button.targetGraphic = hit; button.transition = Selectable.Transition.None;
            var track = Pill("Track", root, new Vector2(.5f, .5f), Vector2.zero, size, Color.gray);
            var shade = Outline(track.transform, new Color(.1f, .2f, .5f, .28f), size.y * .5f);
            float k = size.y - 14;
            var knob = Img("Knob", track.transform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(k, k), Color.white, false, Circle);
            var text = Text("State", track.transform, "OFF", new Vector2(.5f, .5f), Vector2.zero, new Vector2(size.x - k - 16, size.y), 22, Color.white, TextAlignmentOptions.Center, true);
            var fx = root.gameObject.AddComponent<SwitchFx>(); fx.Track = track; fx.Knob = knob; fx.Text = text; fx.OnColor = new Color(.2f, .78f, .45f); fx.OffColor = new Color(.62f, .7f, .84f);
            fx.Set(false, true);
            button.onClick.AddListener(() => { if (PulseAudio.Instance) PulseAudio.Instance.PlayUi("ui_click", .8f); onClick(); });
            return button;
        }
        public static void SetSwitch(Button b, bool on, bool instant = false) { var fx = b ? b.GetComponent<SwitchFx>() : null; if (fx) fx.Set(on, instant); }
        /// Horizontal pale-to-transparent style gradient strip helper: vertical gradient on a rounded Image.
        public static UiGradient Gradient2(Image im, Color top, Color bottom) { var g = im.GetComponent<UiGradient>(); if (!g) g = im.gameObject.AddComponent<UiGradient>(); g.Set(top, bottom); return g; }

        public static void ConfigureScaler(CanvasScaler scaler, float match)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = PulseDisplay.Reference;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = match;
        }
    }

    /// Press-down scale feedback with unscaled time. (Must stay enabled to receive pointer events; Update exits early when idle.)
    public class PressFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        float target = 1; bool animating; RectTransform rt;
        void Awake() { rt = (RectTransform)transform; }
        public void OnPointerDown(PointerEventData e) { target = .93f; animating = true; }
        public void OnPointerUp(PointerEventData e) { target = 1; animating = true; }
        public void OnPointerExit(PointerEventData e) { target = 1; animating = true; }
        void OnDisable() { if (rt) rt.localScale = Vector3.one; target = 1; animating = false; }
        void Update()
        {
            if (!animating) return;
            float s = Mathf.MoveTowards(rt.localScale.x, target, Time.unscaledDeltaTime * 2.2f);
            rt.localScale = new Vector3(s, s, 1);
            if (Mathf.Approximately(s, target) && target == 1) animating = false;
        }
    }

    /// Press shows the scoreboard; a short tap keeps it open until the next tap.
    public class ScoreboardButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public Action<bool> Changed; float down; bool open, pinned;
        public bool Open => open;
        public void OnPointerDown(PointerEventData e) { down = Time.unscaledTime; if (pinned) { pinned = false; Set(false); } else Set(true); }
        public void OnPointerUp(PointerEventData e) { if (!open) return; if (Time.unscaledTime - down < .3f) pinned = true; else Set(false); }
        public void Set(bool value) { if (open == value) return; open = value; Changed?.Invoke(value); }
        void OnDisable() { pinned = false; Set(false); }
    }
}
