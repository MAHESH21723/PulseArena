using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PulseArena
{
    /// HUD-only construction helpers (kept out of UiKit, which belongs to the menu UI).
    public static class HudKit
    {
        public static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

        /// Anchor a child to a fraction of its parent rect.
        public static T Frac<T>(T c, float x0, float y0, float x1, float y1) where T : Component
        {
            var r = (RectTransform)c.transform; r.anchorMin = new Vector2(x0, y0); r.anchorMax = new Vector2(x1, y1); r.offsetMin = r.offsetMax = Vector2.zero; return c;
        }

        public static void Place(RectTransform r, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        { r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size; }

        /// Chunky round button: coloured disc, dark outline ring, gloss highlight and a pressed overlay. The returned Image is the touch target.
        public static Image Round(Transform parent, string name, Color fill, Color rim)
        {
            var main = UiKit.Img(name, parent, new Vector2(1, 0), Vector2.zero, new Vector2(100, 100), fill, true, UiKit.Circle);
            var rimImg = UiKit.Img("Rim", main.transform, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, rim, false, UiKit.Ring); Stretch(rimImg.rectTransform);
            var rim2 = UiKit.Img("Rim2", main.transform, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, new Color(1, 1, 1, .55f), false, UiKit.Ring); Frac(rim2, .07f, .07f, .93f, .93f);
            var gloss = UiKit.Img("Gloss", main.transform, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, new Color(1, 1, 1, .22f), false, UiKit.Circle); Frac(gloss, .16f, .5f, .84f, .93f);
            var pressed = UiKit.Img("Pressed", main.transform, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, new Color(1, 1, 1, 0), false, UiKit.Circle); Stretch(pressed.rectTransform);
            main.gameObject.AddComponent<HudPress>().Overlay = pressed;
            return main;
        }
    }

    /// Lights a white overlay while a touch is down on the button.
    public class HudPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public Image Overlay;
        void Set(float a) { if (!Overlay) return; var c = Overlay.color; c.a = a; Overlay.color = c; }
        public void OnPointerDown(PointerEventData e) { Set(.35f); }
        public void OnPointerUp(PointerEventData e) { Set(0); }
        void OnDisable() { Set(0); }
    }
}
