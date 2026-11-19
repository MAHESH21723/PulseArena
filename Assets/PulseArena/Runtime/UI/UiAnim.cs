using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PulseArena
{
    /// Vertical two-colour gradient multiplied into an Image's vertex colours (works with sliced sprites).
    public class UiGradient : BaseMeshEffect
    {
        Color topColor = Color.white, bottomColor = Color.white;
        public Color Top { get => topColor; set { if (topColor == value) return; topColor = value; if (graphic) graphic.SetVerticesDirty(); } }
        public Color Bottom { get => bottomColor; set { if (bottomColor == value) return; bottomColor = value; if (graphic) graphic.SetVerticesDirty(); } }
        public void Set(Color top, Color bottom) { Top = top; Bottom = bottom; }
        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var r = ((RectTransform)transform).rect; UIVertex v = default;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                float t = r.height > .01f ? Mathf.Clamp01((v.position.y - r.yMin) / r.height) : 1;
                Color c = v.color; c *= Color.Lerp(bottomColor, topColor, t); v.color = c;
                vh.SetUIVertex(v, i);
            }
        }
    }

    /// Chunky 3D-ish button: soft shadow, dark lip, gradient face. Press pushes the face down over the lip and squashes;
    /// hover (mouse) brightens and grows; release gives a small pop. Handles Button.interactable (disabled palette) and a Selected palette.
    public class ButtonFx : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public struct Palette { public Color Top, Bottom, Lip, Label; public Palette(Color top, Color bottom, Color lip, Color label) { Top = top; Bottom = bottom; Lip = lip; Label = label; } }
        public Palette Normal, Selected, Disabled = new Palette(new Color(.84f, .86f, .9f), new Color(.72f, .75f, .81f), new Color(.56f, .6f, .67f), new Color(.46f, .5f, .58f));
        public Image Lip, Face, Gloss, Shadow, Icon;
        public RectTransform FaceRt, LabelRt, IconRt;
        public TMP_Text Label;
        public float LipHeight = 10;
        public float ShadowAlpha = .3f;
        public bool IsSelected { get; private set; }

        Button button; UiGradient grad; float press, hover, punch; bool down, over, dirty = true, lastEnabled = true;

        void Awake() { button = GetComponent<Button>(); }
        public void SetSelected(bool value) { if (IsSelected == value) return; IsSelected = value; dirty = true; }
        public void Refresh() { dirty = true; }
        public void OnPointerDown(PointerEventData e) { down = true; }
        public void OnPointerUp(PointerEventData e) { if (down && button && button.interactable) punch = 1; down = false; }
        public void OnPointerEnter(PointerEventData e) { over = true; }
        public void OnPointerExit(PointerEventData e) { over = false; down = false; }
        void OnDisable() { down = over = false; press = hover = punch = 0; dirty = true; transform.localScale = Vector3.one; }
        void OnEnable() { dirty = true; }

        void Update()
        {
            if (!button) button = GetComponent<Button>();
            bool enabled = !button || button.interactable;
            if (enabled != lastEnabled) { lastEnabled = enabled; dirty = true; }
            float dt = Time.unscaledDeltaTime;
            float tp = down && enabled ? 1 : 0, th = over && enabled ? 1 : 0;
            if (!dirty && press == tp && hover == th && punch <= 0) return;
            press = Mathf.MoveTowards(press, tp, dt * 14); hover = Mathf.MoveTowards(hover, th, dt * 8); punch = Mathf.MoveTowards(punch, 0, dt * 5);
            dirty = false; Apply(enabled);
            if (press != tp || hover != th || punch > 0) dirty = true;
        }

        public void Apply(bool enabled)
        {
            var p = !enabled ? Disabled : IsSelected ? Selected : Normal;
            float glow = hover * .22f + press * -.06f;
            if (Face) { if (!grad) grad = Face.GetComponent<UiGradient>(); if (grad) grad.Set(Color.Lerp(p.Top, Color.white, glow), Color.Lerp(p.Bottom, Color.white, glow)); }
            if (Lip) Lip.color = p.Lip;
            if (Label) Label.color = p.Label;
            if (Icon) Icon.color = p.Label;
            if (Gloss) Gloss.color = new Color(1, 1, 1, enabled ? .13f : .06f);
            float lh = LipHeight, shift = lh * press;
            if (FaceRt) { FaceRt.offsetMin = new Vector2(0, lh - shift); FaceRt.offsetMax = new Vector2(0, -shift); }
            if (LabelRt) { LabelRt.offsetMin = new Vector2(12, lh - shift); LabelRt.offsetMax = new Vector2(-12, -shift); }
            if (IconRt) IconRt.anchoredPosition = new Vector2(0, lh * .5f - shift);
            if (Shadow) { var c = Shadow.color; c.a = (enabled ? ShadowAlpha : ShadowAlpha * .5f) * (1 - press * .6f); Shadow.color = c; }
            float pop = Mathf.Sin(punch * Mathf.PI) * .05f;
            transform.localScale = new Vector3(1 + hover * .03f + press * .02f + pop, 1 + hover * .03f - press * .045f + pop, 1);
        }
    }

    /// Animated on/off switch (pill track + sliding knob). Create with UiKit.MakeSwitch.
    public class SwitchFx : MonoBehaviour
    {
        public Image Track, Knob; public TMP_Text Text; public Color OnColor, OffColor;
        public bool On { get; private set; } float t; bool init;
        public void Set(bool on, bool instant = false)
        {
            On = on; if (instant || !init) { t = on ? 1 : 0; init = true; Apply(); }
        }
        void Update() { float target = On ? 1 : 0; if (t == target) return; t = Mathf.MoveTowards(t, target, Time.unscaledDeltaTime * 8); Apply(); }
        void Apply()
        {
            if (!Track) return;
            float e = t * t * (3 - 2 * t);
            Track.color = Color.Lerp(OffColor, OnColor, e);
            var tr = (RectTransform)Track.transform; float w = tr.rect.width, h = tr.rect.height, k = Knob.rectTransform.rect.width;
            float pad = (h - k) * .5f;
            Knob.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-w * .5f + pad + k * .5f, w * .5f - pad - k * .5f, e), 0);
            if (Text) { Text.text = On ? "ON" : "OFF"; var rt = Text.rectTransform; rt.anchoredPosition = new Vector2(On ? -(k * .5f + pad) * .5f : (k * .5f + pad) * .5f, 0); }
        }
    }

    /// Gentle floating/rotating motion for decorative elements (unscaled time).
    public class UiBob : MonoBehaviour
    {
        public Vector2 Amplitude = new Vector2(0, 8); public float Speed = 1, Rotate, Phase;
        Vector2 basePos; bool have; RectTransform rt; float baseRot;
        void OnEnable() { rt = (RectTransform)transform; if (!have) { basePos = rt.anchoredPosition; baseRot = rt.localEulerAngles.z; have = true; } }
        void Update()
        {
            float s = Mathf.Sin(Time.unscaledTime * Speed + Phase);
            rt.anchoredPosition = basePos + Amplitude * s;
            if (Rotate != 0) rt.localRotation = Quaternion.Euler(0, 0, baseRot + Rotate * Mathf.Sin(Time.unscaledTime * Speed * .7f + Phase));
        }
    }
}
