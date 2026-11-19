using TMPro;
using UnityEngine;

namespace PulseArena
{
    /// Pooled floating damage numbers anchored at a world position; they rise in screen space and fade.
    public class DamageNumbers
    {
        class Num { public TMP_Text Text; public Vector3 World; public float Born, Life, Dx; public ArenaPlayer Victim; public int Amount; public bool Live, Kill; }
        Num[] nums; int next;
        static readonly string[] Txt = BuildText();
        static string[] BuildText() { var a = new string[301]; for (int i = 0; i < a.Length; i++) a[i] = i.ToString(); return a; }
        static readonly Color Yellow = new Color(1, .88f, .12f), Hot = new Color(1, .3f, .12f);

        public void Build(Transform parent, int count)
        {
            nums = new Num[count];
            for (int i = 0; i < count; i++)
            {
                var t = UiKit.Text("Damage number", parent, "", new Vector2(.5f, .5f), Vector2.zero, new Vector2(160, 70), 46, Yellow, TextAlignmentOptions.Center, true);
                t.gameObject.SetActive(false); nums[i] = new Num { Text = t };
            }
        }

        public void Add(ArenaPlayer victim, int amount, Vector3 world)
        {
            float now = Time.unscaledTime;
            foreach (var n in nums)   // merge shotgun pellets / rapid hits on the same target
                if (n.Live && !n.Kill && n.Victim == victim && now - n.Born < .15f) { n.Amount += amount; Style(n); return; }
            var num = nums[next]; next = (next + 1) % nums.Length;
            num.Live = true; num.Kill = false; num.Victim = victim; num.Amount = amount; num.Born = now; num.Life = .9f;
            num.World = world + Random.insideUnitSphere * .12f; num.Dx = Random.Range(-1f, 1f);
            Style(num); num.Text.gameObject.SetActive(true);
        }

        /// Upgrade the newest number on this victim to the big kill style.
        public void MarkKill(ArenaPlayer victim)
        {
            Num best = null;
            foreach (var n in nums) if (n.Live && n.Victim == victim && (best == null || n.Born > best.Born)) best = n;
            if (best == null) return;
            best.Kill = true; best.Born = Time.unscaledTime; best.Life = 1.2f; Style(best);
        }

        static void Style(Num n)
        {
            n.Text.text = Txt[Mathf.Clamp(n.Amount, 0, 300)];
            n.Text.fontSize = n.Kill ? 78 : n.Amount >= 40 ? 62 : 46;
            n.Text.color = n.Kill ? Hot : Yellow;
        }

        public void Clear() { if (nums == null) return; foreach (var n in nums) { n.Live = false; n.Text.gameObject.SetActive(false); } }

        public void Tick(float now, Camera cam, float scale, Vector2 half)
        {
            if (!cam) return;
            foreach (var n in nums)
            {
                if (!n.Live) continue;
                float age = now - n.Born;
                if (age >= n.Life) { n.Live = false; n.Text.gameObject.SetActive(false); continue; }
                Vector3 sp = cam.WorldToScreenPoint(n.World);
                var rt = n.Text.rectTransform;
                if (sp.z <= 0) { n.Text.alpha = 0; continue; }
                float k = age / n.Life, rise = (n.Kill ? 130f : 90f) * (1 - (1 - k) * (1 - k));
                rt.anchoredPosition = ((Vector2)sp - half) / scale + new Vector2(n.Dx * 28f * k, rise);
                n.Text.alpha = Mathf.Clamp01((n.Life - age) / .3f);
                rt.localScale = Vector3.one * (1 + Mathf.Clamp01(1 - age / .12f) * (n.Kill ? .8f : .4f));
            }
        }
    }
}
