using UnityEngine;
using UnityEngine.UI;

namespace PulseArena
{
    /// Dynamic 4-tick crosshair with centre dot. Gap grows with weapon spread, movement and recent fire; turns red on target.
    public class HudCrosshair
    {
        const float Len = 15, Thick = 3.5f, Out = 2.5f;
        readonly Image[] bars = new Image[4], shadows = new Image[4];
        Image dot, dotShadow; RectTransform root;
        float gap = 8, kick, lastGap = -1; Color color = Color.white, lastColor; bool shown = true;
        static readonly Vector2[] Dirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        static readonly Color Red = new Color(1, .2f, .14f), Shadow = new Color(0, 0, 0, .55f);

        public void Build(Transform parent)
        {
            root = UiKit.Rect("Crosshair", parent, new Vector2(.5f, .5f), Vector2.zero, new Vector2(4, 4));
            for (int i = 0; i < 4; i++)
            {
                bool vert = i < 2;
                shadows[i] = UiKit.Img("Shadow", root, new Vector2(.5f, .5f), Vector2.zero, vert ? new Vector2(Thick + Out, Len + Out) : new Vector2(Len + Out, Thick + Out), Shadow);
            }
            for (int i = 0; i < 4; i++)
            {
                bool vert = i < 2;
                bars[i] = UiKit.Img("Tick", root, new Vector2(.5f, .5f), Vector2.zero, vert ? new Vector2(Thick, Len) : new Vector2(Len, Thick), Color.white);
            }
            dotShadow = UiKit.Img("Dot shadow", root, new Vector2(.5f, .5f), Vector2.zero, new Vector2(8, 8), Shadow, false, UiKit.Circle);
            dot = UiKit.Img("Dot", root, new Vector2(.5f, .5f), Vector2.zero, new Vector2(5, 5), Color.white, false, UiKit.Circle);
            lastGap = -1; Apply();
        }

        public void Kick(float v) { kick = Mathf.Min(kick + v, 26); }

        public void Tick(float dt, float baseGap, float move, bool sprint, bool onTarget, bool visible)
        {
            if (visible != shown) { shown = visible; root.gameObject.SetActive(visible); }
            if (!visible) return;
            kick = Mathf.MoveTowards(kick, 0, dt * 80f);
            float target = baseGap + move * 7f + (sprint ? 4f : 0) + kick + (onTarget ? 3f : 0);
            gap = Mathf.Lerp(gap, target, 1 - Mathf.Exp(-dt * 20f));
            color = Color.Lerp(color, onTarget ? Red : Color.white, 1 - Mathf.Exp(-dt * 30f));
            if (Mathf.Abs(gap - lastGap) > .2f || (color - lastColor).maxColorComponent > .01f) Apply();
        }

        void Apply()
        {
            lastGap = gap; lastColor = color;
            for (int i = 0; i < 4; i++)
            {
                var p = Dirs[i] * (gap + Len * .5f);
                bars[i].rectTransform.anchoredPosition = p; shadows[i].rectTransform.anchoredPosition = p; bars[i].color = color;
            }
            dot.color = color;
        }
    }
}
