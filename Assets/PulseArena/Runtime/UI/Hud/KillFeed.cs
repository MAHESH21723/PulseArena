using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PulseArena
{
    /// Top-right kill feed: newest on top, last 4 entries, own name highlighted.
    public class KillFeed
    {
        public const int Rows = 4;
        class Row { public RectTransform Rt; public TMP_Text Text; public Image Back; public CanvasGroup Group; }
        struct Entry { public string Text; public float Until; public bool Mine; }
        readonly Row[] rows = new Row[Rows];
        readonly List<Entry> entries = new List<Entry>();
        public RectTransform Root;
        static readonly Color Normal = new Color(.02f, .04f, .08f, .62f), MineColor = new Color(.95f, .45f, .08f, .8f);

        public void Build(Transform parent)
        {
            Root = UiKit.Rect("Kill feed", parent, new Vector2(1, 1), Vector2.zero, Vector2.zero);
            for (int i = 0; i < Rows; i++)
            {
                var back = UiKit.Img("Row", Root, new Vector2(1, 1), Vector2.zero, new Vector2(480, 40), Normal);
                back.rectTransform.pivot = new Vector2(1, .5f);
                var t = UiKit.Text("Text", back.transform, "", new Vector2(.5f, .5f), Vector2.zero, new Vector2(460, 36), 24, UiKit.White, TextAlignmentOptions.MidlineRight, true);
                var g = back.gameObject.AddComponent<CanvasGroup>(); g.blocksRaycasts = false;
                rows[i] = new Row { Rt = back.rectTransform, Text = t, Back = back, Group = g }; back.gameObject.SetActive(false);
            }
        }

        public void Layout() { for (int i = 0; i < Rows; i++) rows[i].Rt.anchoredPosition = new Vector2(0, -22 - i * 46); }

        public void Add(string killer, string weapon, string victim, bool killerMine, bool victimMine)
        {
            entries.Add(new Entry
            {
                Text = "<color=" + (killerMine ? "#FFF2A0" : "#E8F7FF") + ">" + killer + "</color>  <color=#FFC933>[" + weapon + "]</color>  <color=" + (victimMine ? "#FF6A5A" : "#E8F7FF") + ">" + victim + "</color>",
                Until = Time.unscaledTime + 5f, Mine = killerMine || victimMine
            });
            while (entries.Count > Rows) entries.RemoveAt(0);
            Refresh();
        }

        public void Clear() { entries.Clear(); Refresh(); }

        void Refresh()
        {
            for (int i = 0; i < Rows; i++)
            {
                int idx = entries.Count - 1 - i; bool on = idx >= 0; rows[i].Rt.gameObject.SetActive(on);
                if (!on) continue;
                rows[i].Text.text = entries[idx].Text; rows[i].Back.color = entries[idx].Mine ? MineColor : Normal; rows[i].Group.alpha = 1;
            }
        }

        public void Tick(float now)
        {
            if (entries.Count == 0) return;
            if (entries[0].Until < now) { entries.RemoveAt(0); Refresh(); return; }
            for (int i = 0; i < Rows; i++)
            {
                int idx = entries.Count - 1 - i; if (idx < 0) break;
                float a = Mathf.Clamp01((entries[idx].Until - now) / .5f);
                if (!Mathf.Approximately(rows[i].Group.alpha, a)) rows[i].Group.alpha = a;
            }
        }
    }
}
