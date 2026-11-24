using System.Collections.Generic;
using UnityEngine;

namespace Otuken
{
    /// <summary>What a unit wants to do this frame, from the player or from its bot brain.</summary>
    public struct Cmd
    {
        public Vector2 Move;
        public bool Fire, Skill, Ult;
    }

    public sealed class BotState
    {
        public float Think, Seen, Strafe = 1, StrafeT = 1, OffT, Chk = 0.5f, UnT, UnA, JitV;
        public Vector2 Off, Last;
        public Unit Tgt, Ward;
    }

    public sealed class Unit
    {
        public Hero Hero;
        public Kit Kit;
        public int Team;
        public bool IsPlayer;

        public Vector2 Pos, Vel;
        public float Ang; // radians on the ground plane: 0 = +X (east), PI/2 = +Z (north)

        public float Hp, MaxHp, R, Respawn;
        public bool Alive = true;
        public float FireT, SkillT = 2f, Ult, ShieldT, Stun, Invuln;

        public bool Dashing;
        public Vector2 DashVel;
        public float DashT;
        public readonly HashSet<Unit> DashHit = new HashSet<Unit>();

        public int Kills, Deaths;
        public float Dealt, Healed;

        public float DmgMul = 1f, Jitter, React;
        public BotState Ai;
        public UnitView View;

        public float HpFrac => Hp / MaxHp;
    }

    /// <summary>Primitive-built hero model plus the rings that show shield, stun and invulnerability.</summary>
    public sealed class UnitView
    {
        public GameObject Root;
        public Transform Model;
        public LineRenderer Shield, Ring;

        static readonly Color Gold = new Color(0.91f, 0.71f, 0.29f);

        public static UnitView Build(Unit u, Transform parent)
        {
            var v = new UnitView();
            v.Root = new GameObject(u.Hero.Name + (u.IsPlayer ? " (player)" : ""));
            v.Root.transform.SetParent(parent, false);
            v.Model = BuildModel(u.Hero, u.Team, u.R, v.Root.transform);

            v.Shield = Prim.Line(v.Root.transform, u.Hero.Color, 0.09f, false);
            Prim.Arc(v.Shield, Vector3.zero, u.R * 1.9f, Mathf.PI / 2 - 1.2f, Mathf.PI / 2 + 1.2f, 18, 0.5f);
            v.Shield.enabled = false;

            v.Ring = Prim.Line(v.Root.transform, Gold, 0.045f, true);
            Prim.Circle(v.Ring, Vector3.zero, u.R * 1.65f, 0.035f, 36);
            v.Ring.enabled = u.IsPlayer;
            return v;
        }

        public void Sync(Unit u, float time)
        {
            if (Root.activeSelf != u.Alive) Root.SetActive(u.Alive);
            if (!u.Alive) return;
            Root.transform.SetPositionAndRotation(Util.Ground(u.Pos), Quaternion.Euler(0, Util.Yaw(u.Ang), 0));
            Shield.enabled = u.ShieldT > 0;

            Color ring = default;
            bool show = true;
            if (u.Invuln > 0) ring = new Color(0.96f, 0.9f, 0.72f, 0.55f + 0.35f * Mathf.Sin(time * 12f));
            else if (u.Stun > 0) ring = new Color(1f, 0.85f, 0.2f, 0.9f);
            else if (u.IsPlayer) ring = Gold;
            else show = false;
            Ring.enabled = show;
            if (show) Ring.startColor = Ring.endColor = ring;

            float speed = u.Vel.magnitude;
            float bob = speed > 0.2f ? Mathf.Abs(Mathf.Sin(time * 11f)) * 0.05f : 0f;
            Model.localPosition = new Vector3(0, bob, 0);
            Model.localRotation = u.Stun > 0 ? Quaternion.Euler(0, Mathf.Sin(time * 30f) * 12f, 0) : Quaternion.identity;
        }

        /// <summary>Builds a hero facing local +Z. Shared by the battlefield and the hero-select pedestal.</summary>
        public static Transform BuildModel(Hero h, int team, float r, Transform parent)
        {
            var root = new GameObject("Model").transform;
            root.SetParent(parent, false);
            Color hc = h.Color, tc = HeroDb.TeamColor[team];
            var dark = new Color(0.09f, 0.11f, 0.16f);
            var body = Color.Lerp(hc, dark, 0.45f);
            var bone = new Color(0.94f, 0.9f, 0.82f);
            var steel = new Color(0.82f, 0.84f, 0.88f);
            var wood = new Color(0.36f, 0.24f, 0.14f);

            Prim.Make(PrimitiveType.Cylinder, root, new Vector3(0, 0.012f, 0), new Vector3(r * 2.5f, 0.012f, r * 2.5f), tc, shadows: false);
            Prim.Make(PrimitiveType.Capsule, root, new Vector3(0, 0.42f, 0), new Vector3(r * 1.7f, 0.4f, r * 1.7f), body);
            Prim.Make(PrimitiveType.Cylinder, root, new Vector3(0, 0.42f, 0), new Vector3(r * 1.75f, 0.035f, r * 1.75f), tc);
            Prim.Make(PrimitiveType.Sphere, root, new Vector3(0, 0.92f, 0), Vector3.one * r * 1.25f, hc);

            switch (h.Kit)
            {
                case KitId.Tank:
                    if (h.Mark == "eye")
                    {
                        Prim.Make(PrimitiveType.Sphere, root, new Vector3(0, 0.95f, r * 0.5f), Vector3.one * 0.22f, bone);
                        Prim.Make(PrimitiveType.Sphere, root, new Vector3(0, 0.95f, r * 0.62f), new Vector3(0.11f, 0.11f, 0.05f), new Color(0.1f, 0.06f, 0.03f));
                        Prim.Make(PrimitiveType.Cylinder, root, new Vector3(r * 1.05f, 0.5f, r * 0.85f), new Vector3(0.12f, 0.38f, 0.12f), wood, new Vector3(80, 0, 0));
                    }
                    else
                    {
                        Prim.Make(PrimitiveType.Cube, root, new Vector3(r * 1.05f, 0.5f, r * 0.75f), new Vector3(0.06f, 0.05f, 0.75f), steel);
                        Prim.Make(PrimitiveType.Cube, root, new Vector3(r * 1.05f, 0.5f, r * 0.3f), new Vector3(0.18f, 0.05f, 0.05f), hc);
                        Prim.Make(PrimitiveType.Cylinder, root, new Vector3(-r * 1.0f, 0.52f, r * 0.55f), new Vector3(r * 1.7f, 0.035f, r * 1.7f), hc, new Vector3(90, -25, 0));
                        Prim.Make(PrimitiveType.Cylinder, root, new Vector3(r * 0.45f, 1.08f, 0), new Vector3(0.05f, 0.13f, 0.05f), bone, new Vector3(0, 0, -35));
                        Prim.Make(PrimitiveType.Cylinder, root, new Vector3(-r * 0.45f, 1.08f, 0), new Vector3(0.05f, 0.13f, 0.05f), bone, new Vector3(0, 0, 35));
                    }
                    break;

                case KitId.Wolf:
                    Prim.Make(PrimitiveType.Cube, root, new Vector3(0, 0.88f, r * 0.8f), new Vector3(0.13f, 0.11f, 0.24f), hc);
                    Prim.Make(PrimitiveType.Cube, root, new Vector3(-r * 0.8f, 0.45f, r * 0.7f), new Vector3(0.05f, 0.05f, 0.18f), bone);
                    Prim.Make(PrimitiveType.Cube, root, new Vector3(r * 0.8f, 0.45f, r * 0.7f), new Vector3(0.05f, 0.05f, 0.18f), bone);
                    if (h.Mark == "hair")
                    {
                        for (int i = 0; i < 5; i++)
                            Prim.Make(PrimitiveType.Cube, root, new Vector3(-0.1f + i * 0.05f, 0.9f, -r * 0.75f), new Vector3(0.035f, 0.035f, 0.42f), hc, new Vector3(25, (i - 2) * 6f, 0));
                    }
                    else
                    {
                        Prim.Make(PrimitiveType.Cube, root, new Vector3(r * 0.38f, 1.13f, -r * 0.05f), new Vector3(0.07f, 0.15f, 0.05f), hc, new Vector3(0, 0, -15));
                        Prim.Make(PrimitiveType.Cube, root, new Vector3(-r * 0.38f, 1.13f, -r * 0.05f), new Vector3(0.07f, 0.15f, 0.05f), hc, new Vector3(0, 0, 15));
                        Prim.Make(PrimitiveType.Cube, root, new Vector3(0, 0.45f, -r * 1.1f), new Vector3(0.08f, 0.08f, 0.35f), hc, new Vector3(-30, 0, 0));
                    }
                    break;

                case KitId.Archer:
                    Prim.Make(PrimitiveType.Cylinder, root, new Vector3(r * 1.0f, 0.6f, r * 0.55f), new Vector3(0.035f, 0.36f, 0.035f), hc);
                    Prim.Make(PrimitiveType.Cube, root, new Vector3(r * 0.9f, 0.6f, r * 0.45f), new Vector3(0.012f, 0.68f, 0.012f), bone, shadows: false);
                    Prim.Make(PrimitiveType.Cylinder, root, new Vector3(0, 0.6f, -r * 0.95f), new Vector3(0.11f, 0.2f, 0.11f), wood, new Vector3(25, 0, 0));
                    break;

                case KitId.Healer:
                    Prim.Make(PrimitiveType.Cube, root, new Vector3(r * 1.35f, 0.72f, -r * 0.55f), new Vector3(0.55f, 0.035f, 0.24f), hc, new Vector3(0, -25, 18));
                    Prim.Make(PrimitiveType.Cube, root, new Vector3(-r * 1.35f, 0.72f, -r * 0.55f), new Vector3(0.55f, 0.035f, 0.24f), hc, new Vector3(0, 25, -18));
                    Prim.Make(PrimitiveType.Sphere, root, new Vector3(0, 1.28f, 0), Vector3.one * 0.12f, bone);
                    Prim.Make(PrimitiveType.Cylinder, root, new Vector3(r * 1.0f, 0.55f, r * 0.4f), new Vector3(0.04f, 0.5f, 0.04f), wood);
                    break;
            }

            if (h.Mark == "horns")
            {
                var horn = new Color(0.17f, 0.05f, 0.08f);
                Prim.Make(PrimitiveType.Cylinder, root, new Vector3(r * 0.42f, 1.1f, 0), new Vector3(0.045f, 0.16f, 0.045f), horn, new Vector3(0, 0, -40));
                Prim.Make(PrimitiveType.Cylinder, root, new Vector3(-r * 0.42f, 1.1f, 0), new Vector3(0.045f, 0.16f, 0.045f), horn, new Vector3(0, 0, 40));
            }
            return root;
        }
    }
}
