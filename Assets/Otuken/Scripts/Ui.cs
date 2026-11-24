using UnityEngine;

namespace Otuken
{
    /// <summary>
    /// Immediate-mode UI helpers. Everything is laid out on a virtual canvas 720 units tall
    /// (scaled to the real screen), with the device safe area respected.
    /// </summary>
    public static class Ui
    {
        public static float S = 1f, VW = 1280f, VH = 720f;
        public static Rect Safe;

        public static readonly Color Night = new Color(0.043f, 0.059f, 0.102f);
        public static readonly Color Felt = new Color(0.08f, 0.11f, 0.17f, 0.94f);
        public static readonly Color Felt2 = new Color(0.12f, 0.16f, 0.26f, 0.96f);
        public static readonly Color Bone = new Color(0.94f, 0.9f, 0.82f);
        public static readonly Color Ash = new Color(0.56f, 0.59f, 0.67f);
        public static readonly Color Gold = new Color(0.91f, 0.71f, 0.29f);
        public static readonly Color Ink = new Color(0.1f, 0.07f, 0.02f);
        public static readonly Color Hairline = new Color(0.94f, 0.9f, 0.82f, 0.16f);

        static Texture2D white, disc, ring;
        static GUIStyle label;

        /// <summary>Recompute scale and safe area. Safe to call outside OnGUI (input hit-testing uses it).</summary>
        public static void Layout()
        {
            S = Mathf.Max(0.3f, Mathf.Min(Screen.width, Screen.height) / 720f);
            VW = Screen.width / S;
            VH = Screen.height / S;
            var sa = Screen.safeArea;
            Safe = new Rect(sa.x / S, (Screen.height - sa.yMax) / S, sa.width / S, sa.height / S);
        }

        public static void Begin()
        {
            Layout();
            GUI.matrix = Matrix4x4.Scale(new Vector3(S, S, 1f));
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { richText = false, clipping = TextClipping.Overflow, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0) };
            }
        }

        /// <summary>Screen pixels (origin bottom-left) to virtual UI units (origin top-left).</summary>
        public static Vector2 FromScreen(Vector2 p) => new Vector2(p.x / S, (Screen.height - p.y) / S);

        static Texture2D White
        {
            get
            {
                if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
                return white;
            }
        }

        static Texture2D MakeRound(bool hollow)
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            float c = (N - 1) / 2f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(c - d);
                    if (hollow) a *= Mathf.Clamp01(d - (c - 6f));
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        public static void Fill(Rect r, Color c)
        {
            var o = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White);
            GUI.color = o;
        }

        public static void Frame(Rect r, Color c, float w = 1f)
        {
            Fill(new Rect(r.x, r.y, r.width, w), c);
            Fill(new Rect(r.x, r.yMax - w, r.width, w), c);
            Fill(new Rect(r.x, r.y, w, r.height), c);
            Fill(new Rect(r.xMax - w, r.y, w, r.height), c);
        }

        public static void Disc(Vector2 center, float radius, Color c) => DiscRect(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), c);

        public static void DiscRect(Rect r, Color c)
        {
            if (disc == null) disc = MakeRound(false);
            var o = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, disc);
            GUI.color = o;
        }

        public static void RingAt(Vector2 center, float radius, Color c)
        {
            if (ring == null) ring = MakeRound(true);
            var o = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), ring);
            GUI.color = o;
        }

        public static void Text(Rect r, string s, int size, Color c, TextAnchor anchor = TextAnchor.UpperLeft, bool bold = false, bool wrap = false)
        {
            label.fontSize = size;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            label.alignment = anchor;
            label.wordWrap = wrap;
            label.normal.textColor = c;
            GUI.Label(r, s, label);
        }

        public static float TextHeight(string s, float width, int size, bool bold = false)
        {
            label.fontSize = size;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            label.wordWrap = true;
            return label.CalcHeight(new GUIContent(s), width);
        }

        /// <summary>A flat button. Returns true on click or tap.</summary>
        public static bool Button(Rect r, string text, bool primary = false, int size = 20, bool selected = false)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            Color bg = primary ? Gold : selected ? Felt2 : new Color(0.12f, 0.16f, 0.26f, 0.85f);
            if (hover && !primary) bg = Color.Lerp(bg, Bone, 0.08f);
            Fill(r, bg);
            Frame(r, primary ? Gold : selected ? Gold : Hairline, selected ? 2f : 1f);
            Text(r, text, size, primary ? Ink : Bone, TextAnchor.MiddleCenter, true);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        public static void Bar(Rect r, float frac, Color fill, bool alignRight = false)
        {
            Fill(r, new Color(1, 1, 1, 0.1f));
            float w = r.width * Mathf.Clamp01(frac);
            Fill(alignRight ? new Rect(r.xMax - w, r.y, w, r.height) : new Rect(r.x, r.y, w, r.height), fill);
        }
    }
}
