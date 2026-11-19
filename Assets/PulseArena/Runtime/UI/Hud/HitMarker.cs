using UnityEngine;
using UnityEngine.UI;

namespace PulseArena
{
    /// Diagonal X around the crosshair. White on hit, bigger and red on kill.
    public class HitMarker
    {
        readonly Image[] bars = new Image[4], shadows = new Image[4];
        RectTransform root; CanvasGroup group; float t, speed = 5; bool kill;
        static readonly Color Red = new Color(1, .2f, .14f);

        public void Build(Transform parent)
        {
            root = UiKit.Rect("Hit marker", parent, new Vector2(.5f, .5f), Vector2.zero, new Vector2(4, 4));
            group = root.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0; group.blocksRaycasts = false;
            for (int i = 0; i < 4; i++) shadows[i] = Bar("Shadow", 45 + 90 * i, 6f, new Color(0, 0, 0, .6f));
            for (int i = 0; i < 4; i++) bars[i] = Bar("Bar", 45 + 90 * i, 3.5f, Color.white);
        }

        Image Bar(string n, float a, float thick, Color c)
        {
            var dir = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
            var bar = UiKit.Img(n, root, new Vector2(.5f, .5f), dir * 20f, new Vector2(16, thick), c);
            bar.rectTransform.localRotation = Quaternion.Euler(0, 0, a); return bar;
        }

        public void Show(bool isKill)
        {
            kill = isKill; t = 1; speed = isKill ? 1.8f : 5f;
            var c = isKill ? Red : Color.white;
            for (int i = 0; i < 4; i++) bars[i].color = c;
        }

        public void Tick(float dt)
        {
            if (t <= 0) return;
            t = Mathf.MoveTowards(t, 0, dt * speed);
            group.alpha = Mathf.Min(1, t * 2f);
            float baseScale = kill ? 1.55f : 1f;
            root.localScale = Vector3.one * (baseScale * (1 + (1 - t) * (kill ? .5f : .3f)) * (1 - Mathf.Clamp01((t - .85f) / .15f) * .25f));
        }
    }
}
