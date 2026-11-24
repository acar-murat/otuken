using System.Collections.Generic;
using UnityEngine;

namespace Otuken
{
    public enum ObstacleType { Balbal, Kurgan, Rock, Tree }

    public struct Obstacle
    {
        public Vector2 Pos;
        public float R;
        public ObstacleType Type;
    }

    /// <summary>The Ötüken plain: 26 x 15 m, Tengri's Host spawns west, Erlik's Horde east, the Kut Tree in the middle.</summary>
    public static class Arena
    {
        public const float HX = 13f, HZ = 7.5f, PointR = 1.6f, SpawnR = 1.8f;
        public static readonly Vector2 Point = Vector2.zero;
        public static readonly Vector2[] Spawn = { new Vector2(-11.3f, 0f), new Vector2(11.3f, 0f) };
        public static readonly List<Obstacle> Obstacles = MakeObstacles();

        static List<Obstacle> MakeObstacles()
        {
            // West half; the east half is a mirror image.
            float[,] half =
            {
                { -7.8f,  3.7f, 0.46f, 0 }, { -7.8f, -3.7f, 0.46f, 0 },
                { -5.4f,  0.0f, 0.70f, 1 },
                { -4.0f,  4.5f, 0.90f, 1 }, { -4.0f, -4.5f, 0.90f, 1 },
                { -2.5f,  1.9f, 0.36f, 0 }, { -2.5f, -1.9f, 0.36f, 0 },
                { -6.6f,  1.6f, 0.28f, 0 }, { -6.6f, -1.6f, 0.28f, 0 },
                { -1.2f,  5.7f, 0.60f, 2 }, { -1.2f, -5.7f, 0.60f, 2 },
                { -9.5f,  5.0f, 0.55f, 2 }, { -9.5f, -5.0f, 0.55f, 2 },
            };
            var list = new List<Obstacle>();
            for (int i = 0; i < half.GetLength(0); i++)
            {
                var type = (ObstacleType)(int)half[i, 3];
                list.Add(new Obstacle { Pos = new Vector2(half[i, 0], half[i, 1]), R = half[i, 2], Type = type });
                list.Add(new Obstacle { Pos = new Vector2(-half[i, 0], half[i, 1]), R = half[i, 2], Type = type });
            }
            list.Add(new Obstacle { Pos = Point, R = 0.28f, Type = ObstacleType.Tree });
            return list;
        }

        public static bool LineOfSight(Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            float l2 = d.sqrMagnitude;
            foreach (var o in Obstacles)
            {
                float t = l2 > 0 ? Mathf.Clamp01(Vector2.Dot(o.Pos - a, d) / l2) : 0f;
                float rr = o.R - 0.04f;
                if ((a + d * t - o.Pos).sqrMagnitude < rr * rr) return false;
            }
            return true;
        }

        public static ArenaView Build(Transform parent)
        {
            var root = new GameObject("Arena").transform;
            root.SetParent(parent, false);
            var view = new ArenaView { Root = root };

            // Ground: one quad with a painted texture (grass, the road, spawn camps, the sacred circle).
            var ground = new GameObject("Ground");
            ground.transform.SetParent(root, false);
            var mesh = new Mesh { name = "Ground" };
            mesh.vertices = new[] { new Vector3(-HX, 0, -HZ), new Vector3(HX, 0, -HZ), new Vector3(-HX, 0, HZ), new Vector3(HX, 0, HZ) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.RecalculateBounds();
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            var gm = new Material(Prim.Lit(Color.white)) { mainTexture = PaintGround() };
            ground.AddComponent<MeshRenderer>().sharedMaterial = gm;

            // Dark apron so the map edge does not end in a void.
            Prim.Make(PrimitiveType.Cube, root, new Vector3(0, -0.06f, -3f), new Vector3(60, 0.1f, 40), new Color(0.05f, 0.07f, 0.08f), shadows: false);

            var rnd = new System.Random(7);
            foreach (var o in Obstacles) BuildObstacle(root, o, rnd, view);

            // Border stones along the field edge.
            var stone = new Color(0.33f, 0.34f, 0.37f);
            for (float x = -HX; x <= HX + 0.01f; x += 1.3f)
            {
                Prim.Make(PrimitiveType.Cube, root, new Vector3(x, 0.12f, -HZ - 0.15f), new Vector3(0.35f, 0.24f, 0.25f), stone, new Vector3(0, x * 37f % 30f, 0));
                Prim.Make(PrimitiveType.Cube, root, new Vector3(x, 0.12f, HZ + 0.15f), new Vector3(0.35f, 0.24f, 0.25f), stone, new Vector3(0, x * 53f % 30f, 0));
            }

            // Yurts behind each spawn camp.
            for (int t = 0; t < 2; t++)
            {
                float sx = t == 0 ? -1 : 1;
                foreach (float z in new[] { -2.6f, 2.6f })
                {
                    var p = new Vector3(sx * 14.4f, 0, z);
                    Prim.Make(PrimitiveType.Cylinder, root, p + new Vector3(0, 0.35f, 0), new Vector3(1.6f, 0.35f, 1.6f), new Color(0.86f, 0.82f, 0.72f));
                    Prim.Make(PrimitiveType.Sphere, root, p + new Vector3(0, 0.7f, 0), new Vector3(1.6f, 0.7f, 1.6f), new Color(0.8f, 0.76f, 0.66f));
                    Prim.Make(PrimitiveType.Cylinder, root, p + new Vector3(0, 0.36f, 0), new Vector3(1.62f, 0.05f, 1.62f), HeroDb.TeamColor[t]);
                }
                var ring = Prim.Line(root, HeroDb.TeamColor[t] * new Color(1, 1, 1, 0.6f), 0.05f, true);
                Prim.Circle(ring, Util.Ground(Spawn[t]), SpawnR - 0.05f, 0.02f, 64);
            }

            // Sacred circle and capture progress.
            view.PointRing = Prim.Line(root, new Color(0.91f, 0.71f, 0.29f, 0.7f), 0.05f, true);
            Prim.Circle(view.PointRing, Vector3.zero, PointR, 0.025f, 72);
            view.Progress = Prim.Line(root, Color.white, 0.12f, false);
            view.Progress.positionCount = 0;

            var lightGo = new GameObject("Tree Light");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.localPosition = new Vector3(0, 1.6f, 0);
            view.TreeLight = lightGo.AddComponent<Light>();
            view.TreeLight.type = LightType.Point;
            view.TreeLight.range = 4.5f;
            view.TreeLight.intensity = 1.6f;
            view.TreeLight.shadows = LightShadows.None;
            return view;
        }

        static void BuildObstacle(Transform root, Obstacle o, System.Random rnd, ArenaView view)
        {
            var p = Util.Ground(o.Pos);
            float R() => (float)rnd.NextDouble();
            switch (o.Type)
            {
                case ObstacleType.Balbal:
                {
                    // A balbal: a carved standing stone with a face, turned toward the camera (south).
                    var g = new GameObject("Balbal").transform;
                    g.SetParent(root, false);
                    g.localPosition = p;
                    g.localEulerAngles = new Vector3(0, (R() - 0.5f) * 20f, 0);
                    var grey = new Color(0.52f, 0.53f, 0.56f);
                    Prim.Make(PrimitiveType.Cube, g, new Vector3(0, 0.6f, 0), new Vector3(o.R * 1.6f, 1.2f, o.R * 1.1f), grey);
                    Prim.Make(PrimitiveType.Sphere, g, new Vector3(0, 1.2f, 0), new Vector3(o.R * 1.6f, 0.4f, o.R * 1.1f), grey);
                    var carve = new Color(0.2f, 0.2f, 0.23f);
                    float f = -o.R * 0.56f;
                    Prim.Make(PrimitiveType.Cube, g, new Vector3(-o.R * 0.3f, 1.02f, f), new Vector3(o.R * 0.35f, 0.05f, 0.04f), carve, shadows: false);
                    Prim.Make(PrimitiveType.Cube, g, new Vector3(o.R * 0.3f, 1.02f, f), new Vector3(o.R * 0.35f, 0.05f, 0.04f), carve, shadows: false);
                    Prim.Make(PrimitiveType.Cube, g, new Vector3(0, 0.92f, f), new Vector3(0.04f, 0.16f, 0.04f), carve, shadows: false);
                    Prim.Make(PrimitiveType.Cube, g, new Vector3(-o.R * 0.22f, 0.8f, f), new Vector3(o.R * 0.45f, 0.04f, 0.04f), carve, new Vector3(0, 0, -14), false);
                    Prim.Make(PrimitiveType.Cube, g, new Vector3(o.R * 0.22f, 0.8f, f), new Vector3(o.R * 0.45f, 0.04f, 0.04f), carve, new Vector3(0, 0, 14), false);
                    break;
                }
                case ObstacleType.Kurgan:
                {
                    Prim.Make(PrimitiveType.Sphere, root, p, new Vector3(o.R * 2f, 0.9f, o.R * 2f), new Color(0.3f, 0.37f, 0.22f));
                    for (int i = 0; i < 14; i++)
                    {
                        float a = i * Util.Tau / 14;
                        var sp = p + new Vector3(Mathf.Cos(a) * (o.R + 0.06f), 0.06f, Mathf.Sin(a) * (o.R + 0.06f));
                        Prim.Make(PrimitiveType.Cube, root, sp, Vector3.one * (0.12f + R() * 0.06f), new Color(0.48f, 0.48f, 0.5f), new Vector3(0, R() * 90, 0), false);
                    }
                    break;
                }
                case ObstacleType.Rock:
                {
                    var rock = new Color(0.33f, 0.35f, 0.4f);
                    Prim.Make(PrimitiveType.Cube, root, p + new Vector3(0, 0.3f, 0), new Vector3(o.R * 1.55f, 0.7f, o.R * 1.35f), rock, new Vector3(R() * 8, R() * 90, R() * 8));
                    Prim.Make(PrimitiveType.Cube, root, p + new Vector3(o.R * 0.2f, 0.45f, -o.R * 0.1f), new Vector3(o.R * 1.0f, 0.7f, o.R * 1.0f), rock * 1.15f, new Vector3(R() * 15, R() * 90, R() * 15));
                    break;
                }
                case ObstacleType.Tree:
                {
                    // The Kut Tree: the world tree, hung with wishing rags.
                    var t = new GameObject("Kut Tree").transform;
                    t.SetParent(root, false);
                    t.localPosition = p;
                    view.Tree = t;
                    var bark = new Color(0.27f, 0.19f, 0.12f);
                    Prim.Make(PrimitiveType.Cylinder, t, new Vector3(0, 1.0f, 0), new Vector3(0.5f, 1.0f, 0.5f), bark);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Util.Tau / 6 + 0.4f;
                        Prim.Make(PrimitiveType.Cylinder, t, new Vector3(Mathf.Cos(a) * 0.45f, 1.85f, Mathf.Sin(a) * 0.45f), new Vector3(0.12f, 0.45f, 0.12f), bark,
                            new Vector3(Mathf.Sin(a) * 50f, 0, -Mathf.Cos(a) * 50f));
                    }
                    var leafA = new Color(0.42f, 0.55f, 0.25f);
                    var leafB = new Color(0.78f, 0.64f, 0.27f);
                    for (int i = 0; i < 9; i++)
                    {
                        float a = i * Util.Tau / 9, rr = 0.75f + (i % 2) * 0.15f;
                        Prim.Make(PrimitiveType.Sphere, t, new Vector3(Mathf.Cos(a) * rr, 2.35f + (i % 3) * 0.12f, Mathf.Sin(a) * rr), Vector3.one * (0.75f + (i % 2) * 0.15f),
                            i % 3 == 0 ? leafB : leafA);
                    }
                    Prim.Make(PrimitiveType.Sphere, t, new Vector3(0, 2.75f, 0), Vector3.one * 1.1f, leafA);
                    Color[] rags = { new Color(0.91f, 0.89f, 0.82f), new Color(0.76f, 0.25f, 0.18f), new Color(0.24f, 0.48f, 0.84f), new Color(0.91f, 0.71f, 0.29f) };
                    for (int i = 0; i < 14; i++)
                    {
                        float a = i * Util.Tau / 14 + 0.2f, rr = 1.05f + (i % 3) * 0.1f;
                        Prim.Make(PrimitiveType.Cube, t, new Vector3(Mathf.Cos(a) * rr, 1.85f - (i % 2) * 0.12f, Mathf.Sin(a) * rr), new Vector3(0.06f, 0.32f, 0.02f), rags[i % 4],
                            new Vector3(0, -a * Mathf.Rad2Deg, 0), false);
                    }
                    break;
                }
            }
        }

        static readonly float[] ShanyrakBars = { -0.15f, 0f, 0.15f };

        // The shanyrak (tündük), the crown of the yurt roof, painted as each camp's emblem.
        static bool Shanyrak(Vector2 p)
        {
            float d = p.magnitude;
            if (d > 0.75f) return false;
            if (Mathf.Abs(d - 0.46f) < 0.03f || Mathf.Abs(d - 0.38f) < 0.022f) return true;
            if (d < 0.38f)
                foreach (float o in ShanyrakBars)
                    if (Mathf.Abs(p.x - o) < 0.018f || Mathf.Abs(p.y - o) < 0.018f) return true;
            if (d > 0.46f && d < 0.72f)
            {
                float a = Mathf.Atan2(p.y, p.x) / Util.Tau * 24f;
                if (Mathf.Abs(a - Mathf.Round(a)) < 0.038f / d) return true;
            }
            return false;
        }

        static Texture2D PaintGround()
        {
            const int TW = 780, TH = 450;
            var px = new Color32[TW * TH];
            var baseA = new Color(0.17f, 0.25f, 0.18f);
            var baseB = new Color(0.29f, 0.26f, 0.16f);
            var grass = new Color(0.23f, 0.33f, 0.2f);
            var road = new Color(0.36f, 0.31f, 0.22f);
            var gold = new Color(0.91f, 0.71f, 0.29f);
            for (int y = 0; y < TH; y++)
            {
                float wz = -HZ + (y + 0.5f) / TH * 2 * HZ;
                for (int x = 0; x < TW; x++)
                {
                    float wx = -HX + (x + 0.5f) / TW * 2 * HX;
                    float n1 = Mathf.PerlinNoise(wx * 0.35f + 10f, wz * 0.35f + 10f);
                    float n2 = Mathf.PerlinNoise(wx * 2.2f + 50f, wz * 2.2f + 50f);
                    float n3 = Mathf.PerlinNoise(wx * 9f + 3f, wz * 9f + 7f);
                    var c = Color.Lerp(baseA, baseB, Mathf.SmoothStep(0.35f, 0.75f, n1));
                    c = Color.Lerp(c, grass, n2 * 0.5f);
                    c *= 0.88f + n3 * 0.24f;

                    float rz = 1.1f * Mathf.Sin(wx / 11.3f * Mathf.PI);
                    float rd = Mathf.Abs(wz - rz);
                    if (rd < 0.55f) c = Color.Lerp(c, road, (1 - rd / 0.55f) * 0.55f * (0.7f + 0.3f * n3));

                    var wp = new Vector2(wx, wz);
                    for (int t = 0; t < 2; t++)
                    {
                        var rel = wp - Spawn[t];
                        float d = rel.magnitude;
                        if (d < 2.1f) c = Color.Lerp(c, HeroDb.TeamColor[t] * 0.55f, (1 - d / 2.1f) * 0.35f);
                        if (Shanyrak(rel)) c = Color.Lerp(c, HeroDb.TeamColor[t], 0.55f);
                    }

                    float pd = wp.magnitude;
                    if (pd < PointR) c = Color.Lerp(c, gold * 0.5f, 0.1f);
                    if (Mathf.Abs(pd - PointR) < 0.035f || Mathf.Abs(pd - (PointR - 0.3f)) < 0.025f) c = Color.Lerp(c, gold, 0.6f);
                    if (pd > PointR - 0.25f && pd < PointR - 0.05f)
                    {
                        // Rune-like notches between the two rings.
                        float k = (Mathf.Atan2(wz, wx) / Util.Tau + 0.5f) * 48f;
                        int idx = Mathf.FloorToInt(k);
                        float f = k - idx;
                        bool stroke = (idx % 3 == 0) ? Mathf.Abs(f - 0.5f) < 0.1f : (Mathf.Abs(f - 0.3f) < 0.08f || Mathf.Abs(f - 0.7f) < 0.08f) && pd > PointR - 0.18f;
                        if (stroke) c = Color.Lerp(c, gold, 0.5f);
                    }

                    float edge = Mathf.Min(HX - Mathf.Abs(wx), HZ - Mathf.Abs(wz));
                    if (edge < 0.6f) c *= 0.55f + 0.45f * (edge / 0.6f);
                    c.a = 1f;
                    px[y * TW + x] = c;
                }
            }
            var tex = new Texture2D(TW, TH, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, anisoLevel = 4, name = "OtukenGround" };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }
    }

    public sealed class ArenaView
    {
        public Transform Root, Tree;
        public LineRenderer PointRing, Progress;
        public Light TreeLight;
        float shownProg = float.NaN;
        int shownOwner = -2;

        public void Sync(Match m)
        {
            if (Tree != null) Tree.localRotation = Quaternion.Euler(0, Mathf.Sin(Time.time * 0.4f) * 2f, 0);
            if (m.Owner != shownOwner)
            {
                shownOwner = m.Owner;
                var c = m.Owner >= 0 ? HeroDb.TeamColor[m.Owner] : new Color(0.91f, 0.71f, 0.29f);
                PointRing.startColor = PointRing.endColor = new Color(c.r, c.g, c.b, 0.75f);
                TreeLight.color = c;
            }
            TreeLight.intensity = 1.3f + Mathf.Sin(Time.time * 2f) * 0.25f + (m.OnPoint[0] > 0 && m.OnPoint[1] > 0 ? 0.8f : 0f);
            float p = Mathf.Round(m.Prog);
            if (p == shownProg) return;
            shownProg = p;
            float frac = Mathf.Abs(p) / 100f;
            if (frac < 0.01f) { Progress.positionCount = 0; return; }
            Progress.startColor = Progress.endColor = HeroDb.TeamColor[p > 0 ? 0 : 1];
            int seg = Mathf.Max(2, Mathf.CeilToInt(72 * frac));
            Prim.Arc(Progress, Vector3.zero, Arena.PointR + 0.12f, Mathf.PI / 2, Mathf.PI / 2 - frac * Util.Tau, seg, 0.03f);
        }
    }
}
