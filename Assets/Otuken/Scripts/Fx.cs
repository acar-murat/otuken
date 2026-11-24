using System.Collections.Generic;
using UnityEngine;

namespace Otuken
{
    /// <summary>Pooled ground rings, melee arcs, hit sparks and floating numbers. Nothing here allocates once warmed up.</summary>
    public sealed class Fx
    {
        sealed class Ring
        {
            public LineRenderer Lr;
            public Vector3 C;
            public float R0, R1, A0, A1, Life, Max, W;
            public Color Col;
            public bool Full;
        }

        sealed class Bit
        {
            public Transform T;
            public Renderer Rend;
            public Vector3 V;
            public float Life, Max, S;
        }

        public struct FloatText
        {
            public Vector3 Pos;
            public string Text;
            public Color Col;
            public float Life;
        }

        readonly Transform root;
        readonly List<Ring> rings = new List<Ring>(), ringPool = new List<Ring>();
        readonly List<Bit> bits = new List<Bit>(), bitPool = new List<Bit>();
        public readonly List<FloatText> Texts = new List<FloatText>();

        public Fx(Transform parent)
        {
            root = new GameObject("Fx").transform;
            root.SetParent(parent, false);
        }

        Ring TakeRing()
        {
            Ring r;
            if (ringPool.Count > 0) { r = ringPool[ringPool.Count - 1]; ringPool.RemoveAt(ringPool.Count - 1); r.Lr.gameObject.SetActive(true); }
            else r = new Ring { Lr = Prim.Line(root, Color.white, 0.1f, false, true) };
            rings.Add(r);
            return r;
        }

        public void RingAt(Vector2 p, float r0, float r1, float life, Color col, float width = 0.08f)
        {
            var r = TakeRing();
            r.C = Util.Ground(p, 0.05f); r.R0 = r0; r.R1 = r1; r.Life = r.Max = life; r.Col = col; r.W = width; r.Full = true;
            r.Lr.loop = true;
            Shape(r, 0);
        }

        public void Swing(Vector2 p, float ang, float arc, float radius, float life, Color col)
        {
            var r = TakeRing();
            r.C = Util.Ground(p, 0.4f); r.R0 = radius * 0.7f; r.R1 = radius; r.A0 = ang - arc / 2; r.A1 = ang + arc / 2;
            r.Life = r.Max = life; r.Col = col; r.W = 0.12f; r.Full = false;
            r.Lr.loop = false;
            Shape(r, 0);
        }

        void Shape(Ring r, float k)
        {
            float radius = Mathf.Lerp(r.R0, r.R1, k);
            float alpha = 1f - k;
            var c = new Color(r.Col.r, r.Col.g, r.Col.b, r.Col.a * alpha);
            r.Lr.startColor = r.Lr.endColor = c;
            r.Lr.widthMultiplier = r.W;
            if (r.Full) Prim.Circle(r.Lr, r.C, radius, 0f, 40);
            else Prim.Arc(r.Lr, r.C, radius, r.A0, r.A1, 14, 0f);
        }

        public void Burst(Vector2 p, Color col, int n, float speed)
        {
            for (int i = 0; i < n; i++)
            {
                Bit b;
                if (bitPool.Count > 0) { b = bitPool[bitPool.Count - 1]; bitPool.RemoveAt(bitPool.Count - 1); b.T.gameObject.SetActive(true); }
                else
                {
                    var g = Prim.Make(PrimitiveType.Cube, root, Vector3.zero, Vector3.one, col, shadows: false);
                    b = new Bit { T = g.transform, Rend = g.GetComponent<Renderer>() };
                }
                b.Rend.sharedMaterial = Prim.Lit(col);
                b.T.position = Util.Ground(p, 0.5f);
                var dir = Random.insideUnitSphere;
                dir.y = Mathf.Abs(dir.y) + 0.3f;
                b.V = dir.normalized * speed * Random.Range(0.4f, 1f);
                b.Life = b.Max = Random.Range(0.25f, 0.55f);
                b.S = Random.Range(0.05f, 0.1f);
                b.T.localScale = Vector3.one * b.S;
                bits.Add(b);
            }
        }

        public void Text(Vector2 p, float height, string text, Color col)
        {
            if (Texts.Count > 40) Texts.RemoveAt(0);
            Texts.Add(new FloatText { Pos = Util.Ground(p + new Vector2(Random.Range(-0.1f, 0.1f), 0), height), Text = text, Col = col, Life = 0.8f });
        }

        public void Tick(float dt)
        {
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var r = rings[i];
                r.Life -= dt;
                if (r.Life <= 0) { r.Lr.gameObject.SetActive(false); rings.RemoveAt(i); ringPool.Add(r); continue; }
                Shape(r, 1f - r.Life / r.Max);
            }
            for (int i = bits.Count - 1; i >= 0; i--)
            {
                var b = bits[i];
                b.Life -= dt;
                if (b.Life <= 0) { b.T.gameObject.SetActive(false); bits.RemoveAt(i); bitPool.Add(b); continue; }
                b.V.y -= 9f * dt;
                b.T.position += b.V * dt;
                b.T.localScale = Vector3.one * (b.S * b.Life / b.Max);
            }
            for (int i = Texts.Count - 1; i >= 0; i--)
            {
                var t = Texts[i];
                t.Life -= dt;
                t.Pos.y += 0.6f * dt;
                if (t.Life <= 0) Texts.RemoveAt(i); else Texts[i] = t;
            }
        }
    }
}
