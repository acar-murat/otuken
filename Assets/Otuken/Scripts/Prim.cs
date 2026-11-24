using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Otuken
{
    /// <summary>
    /// Builds everything visible from Unity's primitive meshes, so the project needs no imported art.
    /// Lit materials are cloned from the primitive's default material, which the render pipeline
    /// always ships in builds; lines use Sprites/Default (kept in builds by the editor setup).
    /// </summary>
    public static class Prim
    {
        static Material baseLit, lineMat;
        static readonly Dictionary<Color, Material> cache = new Dictionary<Color, Material>();

        public static Material Lit(Color c)
        {
            if (cache.TryGetValue(c, out var m) && m != null) return m;
            if (baseLit == null)
            {
                var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                baseLit = new Material(probe.GetComponent<Renderer>().sharedMaterial);
                Object.Destroy(probe);
            }
            m = new Material(baseLit) { color = c };
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.15f);
            cache[c] = m;
            return m;
        }

        public static Material LineMat
        {
            get
            {
                if (lineMat == null)
                {
                    var sh = Shader.Find("Sprites/Default");
                    if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
                    lineMat = new Material(sh);
                }
                return lineMat;
            }
        }

        public static GameObject Make(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale, Color color,
            Vector3 euler = default, bool shadows = true)
        {
            var g = GameObject.CreatePrimitive(type);
            var col = g.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            var t = g.transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localScale = localScale;
            t.localEulerAngles = euler;
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = Lit(color);
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return g;
        }

        public static LineRenderer Line(Transform parent, Color color, float width, bool loop, bool worldSpace = false)
        {
            var g = new GameObject("Line");
            g.transform.SetParent(parent, false);
            var lr = g.AddComponent<LineRenderer>();
            lr.sharedMaterial = LineMat;
            lr.startColor = lr.endColor = color;
            lr.widthMultiplier = width;
            lr.loop = loop;
            lr.useWorldSpace = worldSpace;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 2;
            lr.alignment = LineAlignment.TransformZ;
            g.transform.localRotation = Quaternion.Euler(90, 0, 0);
            return lr;
        }

        /// <summary>Lays a line flat on the ground as an arc. Angles are radians in the XZ plane (0 = +X, PI/2 = +Z).</summary>
        public static void Arc(LineRenderer lr, Vector3 center, float radius, float a0, float a1, int segments, float y)
        {
            lr.positionCount = segments + 1;
            for (int i = 0; i <= segments; i++)
            {
                float a = a0 + (a1 - a0) * i / segments;
                lr.SetPosition(i, ToLine(lr, center + new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius)));
            }
        }

        public static void Circle(LineRenderer lr, Vector3 center, float radius, float y, int segments = 48)
        {
            lr.loop = true;
            lr.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments;
                lr.SetPosition(i, ToLine(lr, center + new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius)));
            }
        }

        // Lines lie flat: their transform is rotated 90° about X so the ribbon faces up.
        // That rotation maps local (x, y, z) to parent (x, -z, y), so a parent-space point p sits at local (p.x, p.z, -p.y).
        static Vector3 ToLine(LineRenderer lr, Vector3 p) => lr.useWorldSpace ? p : new Vector3(p.x, p.z, -p.y);
    }

    public static class Util
    {
        public const float Tau = Mathf.PI * 2f;

        public static float AngDiff(float a, float b)
        {
            float d = Mathf.Repeat(a - b + Mathf.PI, Tau) - Mathf.PI;
            return Mathf.Abs(d);
        }

        public static float Turn(float from, float to, float maxStep)
        {
            float d = Mathf.Repeat(to - from + Mathf.PI, Tau) - Mathf.PI;
            return from + Mathf.Clamp(d, -maxStep, maxStep);
        }

        public static Vector2 Dir(float a) => new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        public static Vector3 Ground(Vector2 p, float y = 0) => new Vector3(p.x, y, p.y);
        public static float Yaw(float ang) => 90f - ang * Mathf.Rad2Deg;
    }
}
