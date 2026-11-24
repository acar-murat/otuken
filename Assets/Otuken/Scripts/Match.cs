using System.Collections.Generic;
using UnityEngine;

namespace Otuken
{
    public struct PlayerCmd
    {
        public Vector2 Move;
        public bool Fire, HasAim, AutoAim, Skill, Ult;
        public float Aim;
    }

    public sealed class Proj
    {
        public Vector2 P, V;
        public float Ang, Speed, Dmg, R, Range, D;
        public int Team;
        public Unit Src;
        public bool Pierce, Ghost, Big;
        public readonly HashSet<Unit> Hit = new HashSet<Unit>();
        public Transform T;
    }

    public sealed class SpiritWolf
    {
        public Vector2 P;
        public float Ang, Life, Bite;
        public int Team;
        public Unit Owner;
        public Transform T;
    }

    /// <summary>
    /// One 4v4 match on the Ötüken plain. Pure simulation plus syncing of its own visuals;
    /// the GameManager feeds it input and draws the HUD from its public state.
    /// </summary>
    public sealed class Match
    {
        public const float MatchTime = 360f;
        public const float CaptureBase = 16f, CapturePerUnit = 6f, ScoreRate = 1.4f;

        public readonly List<Unit> Units = new List<Unit>(8);
        readonly List<Proj> projs = new List<Proj>();
        readonly List<SpiritWolf> wolves = new List<SpiritWolf>();
        readonly Stack<Proj> projPool = new Stack<Proj>();

        public Unit Player;
        public readonly bool Demo;
        public bool Over;
        public int Winner = -1;
        public float Time, Prog;
        public int Owner = -1;
        public readonly float[] Score = new float[2];
        public readonly int[] OnPoint = new int[2];
        public readonly Fx Fx;

        public System.Action<string, int> OnFeed;
        public System.Action<string, string> OnBanner;
        public System.Action<int> OnEnd;
        public System.Action<string> OnSound;
        public System.Action<float> OnShake;

        readonly Transform root;
        readonly ArenaView arena;

        public Match(Transform parent, ArenaView arena, bool demo, Hero playerHero, int difficulty)
        {
            this.arena = arena;
            Demo = demo;
            root = new GameObject(demo ? "Demo Match" : "Match").transform;
            root.SetParent(parent, false);
            Fx = new Fx(root);

            if (!demo)
            {
                int side = playerHero.Side;
                Player = Spawn(playerHero, side, true, 0, default);
                int i = 1;
                foreach (var h in HeroDb.Side(side)) if (h != playerHero) Spawn(h, side, false, i++, HeroDb.Ally);
                i = 0;
                foreach (var h in HeroDb.Side(1 - side)) Spawn(h, 1 - side, false, i++, HeroDb.Difficulty[difficulty]);
            }
            else
            {
                for (int t = 0; t < 2; t++)
                {
                    int i = 0;
                    foreach (var h in HeroDb.Side(t)) Spawn(h, t, false, i++, HeroDb.Difficulty[1]);
                }
            }
            arena.Sync(this);
        }

        public void Dispose() => Object.Destroy(root.gameObject);

        Unit Spawn(Hero h, int team, bool isPlayer, int slot, BotSkill skill)
        {
            var k = HeroDb.KitOf(h);
            var u = new Unit
            {
                Hero = h, Kit = k, Team = team, IsPlayer = isPlayer, R = k.Radius, MaxHp = k.Hp, Hp = k.Hp,
                Ang = team == 0 ? 0f : Mathf.PI,
            };
            if (!isPlayer)
            {
                u.DmgMul = skill.Mul; u.Jitter = skill.Jitter; u.React = skill.React;
                u.Ai = new BotState { Strafe = Random.value < 0.5f ? 1 : -1 };
            }
            PlaceAtSpawn(u, slot);
            u.View = UnitView.Build(u, root);
            Units.Add(u);
            return u;
        }

        static void PlaceAtSpawn(Unit u, int slot)
        {
            var s = Arena.Spawn[u.Team];
            float a = (slot < 0 ? Random.value * 4f : slot) / 4f * Mathf.PI - Mathf.PI / 2 + Mathf.PI / 8;
            u.Pos = s + new Vector2(Mathf.Cos(a) * 0.7f * (u.Team == 0 ? 1 : -1), Mathf.Sin(a) * 0.7f);
            u.Vel = Vector2.zero;
        }

        // ------------------------------------------------------------------ tick

        public void Tick(float dt, PlayerCmd pc)
        {
            Time += dt;
            foreach (var u in Units) UpdateUnit(u, dt, pc);
            Separate();
            foreach (var u in Units) if (u.Alive) Collide(u);
            UpdateWolves(dt);
            UpdateProjs(dt);
            if (!Over) UpdatePoint(dt);
            TickVisuals(dt);
        }

        /// <summary>After the match ends: keep effects and the scene alive without simulating.</summary>
        public void TickVisuals(float dt)
        {
            Fx.Tick(dt);
            foreach (var u in Units) u.View.Sync(u, Time);
            foreach (var p in projs) p.T.SetPositionAndRotation(Util.Ground(p.P, 0.55f), Quaternion.Euler(0, Util.Yaw(p.Ang), 0));
            foreach (var w in wolves)
            {
                w.T.SetPositionAndRotation(Util.Ground(w.P, Mathf.Abs(Mathf.Sin(Time * 14f + w.P.x)) * 0.08f), Quaternion.Euler(0, Util.Yaw(w.Ang), 0));
            }
            arena.Sync(this);
        }

        void UpdateUnit(Unit u, float dt, PlayerCmd pc)
        {
            if (!u.Alive)
            {
                u.Respawn -= dt;
                if (u.Respawn <= 0)
                {
                    u.Alive = true; u.Hp = u.MaxHp; u.Invuln = 1.5f;
                    PlaceAtSpawn(u, -1);
                    if (u.Ai != null) u.Ai.Tgt = null;
                }
                return;
            }
            u.FireT -= dt; u.SkillT -= dt; u.Stun -= dt; u.ShieldT -= dt; u.Invuln -= dt;
            u.Ult = Mathf.Min(100f, u.Ult + dt * 0.9f);
            if ((u.Pos - Arena.Spawn[u.Team]).sqrMagnitude < Arena.SpawnR * Arena.SpawnR) Heal(null, u, 70f * dt);
            if (u.Kit.Aura > 0)
                foreach (var a in Units)
                    if (a.Alive && a.Team == u.Team && (a.Pos - u.Pos).sqrMagnitude < 1.7f * 1.7f)
                        Heal(u, a, u.Kit.Aura * dt * (a == u ? 0.5f : 1f));

            // A player with a bot brain attached (automated test runs) plays on autopilot.
            Cmd c = u.IsPlayer && u.Ai == null ? FromPlayer(u, pc) : Brain.Think(this, u, dt);
            if (u.Stun > 0) c = default;

            if (u.Dashing)
            {
                u.Vel = u.DashVel;
                u.Pos += u.DashVel * dt;
                u.DashT -= dt;
                foreach (var e in Units)
                {
                    if (!e.Alive || e.Team == u.Team || u.DashHit.Contains(e)) continue;
                    if ((e.Pos - u.Pos).sqrMagnitude < Sq(u.R + e.R + 0.08f))
                    {
                        u.DashHit.Add(e);
                        Damage(u, e, 55f, u.Pos - u.DashVel);
                    }
                }
                if (u.DashT <= 0) u.Dashing = false;
            }
            else
            {
                float sp = u.Kit.Speed * (u.ShieldT > 0 ? 0.6f : 1f);
                float f = Mathf.Min(1f, dt * 12f);
                u.Vel += (c.Move * sp - u.Vel) * f;
                u.Pos += u.Vel * dt;
            }
            Collide(u);
            if (c.Fire && u.Stun <= 0) TryFire(u);
            if (c.Skill) TrySkill(u);
            if (c.Ult) TryUlt(u);
        }

        Cmd FromPlayer(Unit u, PlayerCmd p)
        {
            bool moving = p.Move.sqrMagnitude > 0.01f;
            if (p.HasAim) u.Ang = p.Aim;
            else if (p.AutoAim)
            {
                var f = NearestFoe(u, u.Kit.Melee ? u.Kit.Range + 1.6f : u.Kit.Range);
                if (f != null) u.Ang = Mathf.Atan2(f.Pos.y - u.Pos.y, f.Pos.x - u.Pos.x);
                else if (moving) u.Ang = Mathf.Atan2(p.Move.y, p.Move.x);
            }
            else if (moving) u.Ang = Mathf.Atan2(p.Move.y, p.Move.x);
            return new Cmd { Move = p.Move, Fire = p.Fire, Skill = p.Skill, Ult = p.Ult };
        }

        // ------------------------------------------------------------------ combat

        public Unit NearestFoe(Unit u, float range)
        {
            Unit best = null;
            float bd = range * range;
            foreach (var e in Units)
            {
                if (!e.Alive || e.Team == u.Team) continue;
                float d = (e.Pos - u.Pos).sqrMagnitude;
                if (d < bd && Arena.LineOfSight(u.Pos, e.Pos)) { bd = d; best = e; }
            }
            return best;
        }

        public int CountNear(Unit u, float r, bool foes, float below = 2f)
        {
            int n = 0;
            foreach (var a in Units)
            {
                if (!a.Alive || a == u) continue;
                if (foes ? a.Team == u.Team : a.Team != u.Team) continue;
                if ((a.Pos - u.Pos).sqrMagnitude < r * r && a.HpFrac < below) n++;
            }
            return n;
        }

        static float Sq(float x) => x * x;

        // Damage numbers come from a fixed table so hits never allocate strings.
        static readonly string[] Numbers = BuildNumbers();
        static string[] BuildNumbers() { var a = new string[501]; for (int i = 0; i < a.Length; i++) a[i] = i.ToString(); return a; }
        static string Num(float v) => Numbers[Mathf.Clamp(Mathf.RoundToInt(v), 0, 500)];

        void Heal(Unit src, Unit t, float amt)
        {
            if (!t.Alive || amt <= 0) return;
            float a = Mathf.Min(amt, t.MaxHp - t.Hp);
            if (a <= 0) return;
            t.Hp += a;
            if (src != null) { src.Healed += a; src.Ult = Mathf.Min(100f, src.Ult + a / 5f); }
        }

        void Damage(Unit src, Unit t, float amt, Vector2 from)
        {
            if (!t.Alive || t.Invuln > 0 || t.Team == src.Team) return;
            amt *= src.DmgMul;
            if (t.ShieldT > 0 && t.Kit.Id == KitId.Tank && Util.AngDiff(Mathf.Atan2(from.y - t.Pos.y, from.x - t.Pos.x), t.Ang) < 1.25f)
            {
                amt *= 0.15f;
                Fx.RingAt(t.Pos + Util.Dir(t.Ang) * t.R * 1.9f, 0.05f, 0.25f, 0.2f, new Color(0.94f, 0.9f, 0.82f));
            }
            t.Hp -= amt;
            src.Dealt += amt;
            src.Ult = Mathf.Min(100f, src.Ult + amt / 6f);
            Fx.Burst(t.Pos, src.Hero.Color, 3, 2.2f);
            if (src.IsPlayer) { Fx.Text(t.Pos, 1.5f, Num(amt), new Color(0.94f, 0.9f, 0.82f)); OnSound?.Invoke("hit"); }
            if (t.IsPlayer) { Fx.Text(t.Pos, 1.5f, Num(amt), new Color(1f, 0.48f, 0.4f)); OnShake?.Invoke(0.25f); OnSound?.Invoke("hurt"); }
            if (t.Hp <= 0) Kill(t, src);
        }

        void Kill(Unit t, Unit src)
        {
            t.Alive = false; t.Hp = 0; t.Deaths++;
            t.Respawn = t.IsPlayer ? 5f : 6f;
            t.Dashing = false; t.ShieldT = 0; t.Stun = 0;
            Fx.Burst(t.Pos, HeroDb.TeamColor[t.Team], 12, 3.5f);
            Fx.Burst(t.Pos, t.Hero.Color, 8, 2.5f);
            Fx.RingAt(t.Pos, 0.2f, 1.1f, 0.5f, HeroDb.TeamColor[t.Team]);
            src.Kills++;
            src.Ult = Mathf.Min(100f, src.Ult + 10f);
            OnFeed?.Invoke($"{src.Hero.Name} defeated {t.Hero.Name}", src.Team);
            if (src.IsPlayer) OnSound?.Invoke("kill");
        }

        void Shoot(Unit u, float ang, float dmg, float speed, float range, float r, bool pierce = false, bool ghost = false, bool big = false)
        {
            var p = projPool.Count > 0 ? projPool.Pop() : new Proj();
            p.Ang = ang; p.Speed = speed; p.Dmg = dmg; p.R = r; p.Range = range; p.D = 0;
            p.V = Util.Dir(ang) * speed;
            p.P = u.Pos + Util.Dir(ang) * (u.R + 0.05f);
            p.Team = u.Team; p.Src = u; p.Pierce = pierce; p.Ghost = ghost; p.Big = big;
            p.Hit.Clear();
            p.T = BuildProjModel(u, big);
            projs.Add(p);
        }

        readonly Dictionary<(Hero, bool), Stack<Transform>> modelPool = new Dictionary<(Hero, bool), Stack<Transform>>();

        Transform BuildProjModel(Unit u, bool big)
        {
            var key = (u.Hero, big);
            if (modelPool.TryGetValue(key, out var stack) && stack.Count > 0)
            {
                var t = stack.Pop();
                t.gameObject.SetActive(true);
                return t;
            }
            var c = u.Hero.Color;
            GameObject g;
            if (big) g = Prim.Make(PrimitiveType.Cube, root, Vector3.zero, new Vector3(0.2f, 0.2f, 1.3f), Color.Lerp(c, Color.white, 0.5f), shadows: false);
            else if (u.Kit.Id == KitId.Archer) g = Prim.Make(PrimitiveType.Cube, root, Vector3.zero, new Vector3(0.04f, 0.04f, 0.38f), c, shadows: false);
            else g = Prim.Make(PrimitiveType.Sphere, root, Vector3.zero, Vector3.one * 0.2f, c, shadows: false);
            return g.transform;
        }

        void Release(Proj p)
        {
            var key = (p.Src.Hero, p.Big);
            if (!modelPool.TryGetValue(key, out var stack)) modelPool[key] = stack = new Stack<Transform>();
            p.T.gameObject.SetActive(false);
            stack.Push(p.T);
            p.T = null;
            p.Src = null;
            projPool.Push(p);
        }

        void TryFire(Unit u)
        {
            if (u.FireT > 0) return;
            var k = u.Kit;
            u.FireT = k.FireCd;
            if (k.Melee)
            {
                Fx.Swing(u.Pos, u.Ang, k.Arc, k.Range + u.R, 0.15f, u.Hero.Color);
                foreach (var e in Units)
                {
                    if (!e.Alive || e.Team == u.Team) continue;
                    float d = Vector2.Distance(u.Pos, e.Pos);
                    if (d > k.Range + u.R + e.R * 0.6f) continue;
                    if (d > u.R + e.R + 0.06f && Util.AngDiff(Mathf.Atan2(e.Pos.y - u.Pos.y, e.Pos.x - u.Pos.x), u.Ang) > k.Arc / 2) continue;
                    if (!Arena.LineOfSight(u.Pos, e.Pos)) continue;
                    Damage(u, e, k.Damage, u.Pos);
                }
                if (u.IsPlayer) OnSound?.Invoke("swing");
            }
            else
            {
                Shoot(u, u.Ang, k.Damage, k.ProjSpeed, k.Range, k.ProjRadius);
                if (u.IsPlayer) OnSound?.Invoke(k.Id == KitId.Healer ? "orb" : "bow");
            }
        }

        void TrySkill(Unit u)
        {
            if (u.SkillT > 0 || !u.Alive) return;
            u.SkillT = u.Kit.SkillCd;
            switch (u.Kit.Id)
            {
                case KitId.Tank:
                    u.ShieldT = 3f;
                    break;
                case KitId.Wolf:
                    u.Dashing = true; u.DashT = 0.22f; u.DashVel = Util.Dir(u.Ang) * 11.5f; u.DashHit.Clear();
                    u.Invuln = Mathf.Max(u.Invuln, 0.18f);
                    break;
                case KitId.Archer:
                    for (int i = -2; i <= 2; i++) Shoot(u, u.Ang + i * 0.12f, 28f, 10f, 5.8f, 0.08f);
                    break;
                case KitId.Healer:
                    foreach (var a in Units)
                        if (a.Alive && a.Team == u.Team && (a.Pos - u.Pos).sqrMagnitude < 2.4f * 2.4f) { Heal(u, a, 90f); Fx.Burst(a.Pos, u.Hero.Color, 4, 1.5f); }
                    Fx.RingAt(u.Pos, 0.2f, 2.4f, 0.45f, u.Hero.Color);
                    break;
            }
            if (u.IsPlayer) OnSound?.Invoke("skill");
        }

        void TryUlt(Unit u)
        {
            if (u.Ult < 100f || !u.Alive || u.Stun > 0) return;
            u.Ult = 0;
            OnFeed?.Invoke($"{u.Hero.Name} called {u.Hero.Ult}", u.Team);
            if (u.IsPlayer) { OnSound?.Invoke("ult"); OnBanner?.Invoke(u.Hero.Ult, u.Hero.Name); }
            switch (u.Kit.Id)
            {
                case KitId.Tank:
                    Fx.RingAt(u.Pos, 0.3f, 2.6f, 0.5f, u.Hero.Color, 0.18f);
                    foreach (var e in Units)
                    {
                        if (!e.Alive || e.Team == u.Team) continue;
                        var d = e.Pos - u.Pos;
                        if (d.sqrMagnitude > 2.5f * 2.5f) continue;
                        e.Stun = 1.6f;
                        e.Pos += d.normalized * 0.8f;
                        Damage(u, e, 70f, u.Pos);
                    }
                    if (Player != null && Vector2.Distance(Player.Pos, u.Pos) < 7f) OnShake?.Invoke(0.6f);
                    break;
                case KitId.Wolf:
                    for (int i = 0; i < 3; i++)
                    {
                        float a = u.Ang + (i - 1) * 0.7f;
                        var w = new SpiritWolf { Team = u.Team, Owner = u, P = u.Pos + Util.Dir(a) * 0.3f, Ang = a, Life = 7f };
                        w.T = BuildWolf(u.Hero.Color);
                        wolves.Add(w);
                    }
                    Fx.RingAt(u.Pos, 0.1f, 1.2f, 0.4f, u.Hero.Color);
                    break;
                case KitId.Archer:
                    Shoot(u, u.Ang, 170f, 15f, 29f, 0.14f, pierce: true, ghost: true, big: true);
                    break;
                case KitId.Healer:
                    foreach (var a in Units)
                        if (a.Alive && a.Team == u.Team && (a.Pos - u.Pos).sqrMagnitude < 3.3f * 3.3f) { a.Hp = a.MaxHp; a.Invuln = 3f; Fx.Burst(a.Pos, u.Hero.Color, 8, 2f); }
                    Fx.RingAt(u.Pos, 0.4f, 3.3f, 0.7f, u.Hero.Color, 0.16f);
                    break;
            }
        }

        Transform BuildWolf(Color c)
        {
            var t = new GameObject("Spirit Wolf").transform;
            t.SetParent(root, false);
            var spirit = Color.Lerp(c, Color.white, 0.35f);
            Prim.Make(PrimitiveType.Capsule, t, new Vector3(0, 0.22f, 0), new Vector3(0.16f, 0.2f, 0.16f), spirit, new Vector3(90, 0, 0), false);
            Prim.Make(PrimitiveType.Sphere, t, new Vector3(0, 0.3f, 0.2f), Vector3.one * 0.15f, spirit, shadows: false);
            Prim.Make(PrimitiveType.Cube, t, new Vector3(0, 0.28f, 0.3f), new Vector3(0.07f, 0.06f, 0.12f), spirit, shadows: false);
            return t;
        }

        // ------------------------------------------------------------------ world

        void Collide(Unit u)
        {
            foreach (var o in Arena.Obstacles)
            {
                var d = u.Pos - o.Pos;
                float m = o.R + u.R, dd = d.sqrMagnitude;
                if (dd < m * m && dd > 1e-6f) u.Pos = o.Pos + d / Mathf.Sqrt(dd) * m;
            }
            u.Pos.x = Mathf.Clamp(u.Pos.x, -Arena.HX + u.R, Arena.HX - u.R);
            u.Pos.y = Mathf.Clamp(u.Pos.y, -Arena.HZ + u.R, Arena.HZ - u.R);
        }

        void Separate()
        {
            for (int i = 0; i < Units.Count; i++)
                for (int j = i + 1; j < Units.Count; j++)
                {
                    var a = Units[i]; var b = Units[j];
                    if (!a.Alive || !b.Alive) continue;
                    var d = b.Pos - a.Pos;
                    float m = a.R + b.R, dd = d.sqrMagnitude;
                    if (dd < m * m && dd > 1e-6f)
                    {
                        float len = Mathf.Sqrt(dd);
                        var push = d / len * ((m - len) / 2f);
                        a.Pos -= push; b.Pos += push;
                    }
                }
        }

        void UpdateWolves(float dt)
        {
            for (int i = wolves.Count - 1; i >= 0; i--)
            {
                var w = wolves[i];
                w.Life -= dt; w.Bite -= dt;
                if (w.Life <= 0)
                {
                    Fx.Burst(w.P, w.Owner.Hero.Color, 5, 1.5f);
                    Object.Destroy(w.T.gameObject);
                    wolves.RemoveAt(i);
                    continue;
                }
                Unit best = null;
                float bd = 9f * 9f;
                foreach (var e in Units)
                {
                    if (!e.Alive || e.Team == w.Team) continue;
                    float d = (e.Pos - w.P).sqrMagnitude;
                    if (d < bd) { bd = d; best = e; }
                }
                if (best == null) continue;
                float dist = Mathf.Sqrt(bd);
                w.Ang = Util.Turn(w.Ang, Mathf.Atan2(best.Pos.y - w.P.y, best.Pos.x - w.P.x), dt * 10f);
                if (dist > best.R + 0.12f) w.P += Util.Dir(w.Ang) * 2.9f * dt;
                else if (w.Bite <= 0) { w.Bite = 0.45f; Damage(w.Owner, best, 14f, w.P); }
                foreach (var o in Arena.Obstacles)
                {
                    var d = w.P - o.Pos;
                    float r = o.R + 0.1f;
                    if (d.sqrMagnitude < r * r && d.sqrMagnitude > 1e-6f) w.P = o.Pos + d.normalized * r;
                }
            }
        }

        void UpdateProjs(float dt)
        {
            for (int i = projs.Count - 1; i >= 0; i--)
            {
                var p = projs[i];
                p.P += p.V * dt;
                p.D += p.Speed * dt;
                bool dead = p.D > p.Range || Mathf.Abs(p.P.x) > Arena.HX + 0.5f || Mathf.Abs(p.P.y) > Arena.HZ + 0.5f;
                if (!dead && !p.Ghost)
                {
                    foreach (var o in Arena.Obstacles)
                        if ((p.P - o.Pos).sqrMagnitude < Sq(o.R + p.R)) { Fx.Burst(p.P, new Color(0.6f, 0.6f, 0.64f), 2, 1.2f); dead = true; break; }
                }
                if (!dead)
                {
                    foreach (var u in Units)
                    {
                        if (!u.Alive || u.Team == p.Team || p.Hit.Contains(u)) continue;
                        if ((p.P - u.Pos).sqrMagnitude < Sq(u.R + p.R))
                        {
                            Damage(p.Src, u, p.Dmg, p.P - p.V * 0.05f);
                            if (p.Pierce) p.Hit.Add(u); else { dead = true; break; }
                        }
                    }
                }
                if (dead) { Release(p); projs.RemoveAt(i); }
            }
        }

        void UpdatePoint(float dt)
        {
            OnPoint[0] = OnPoint[1] = 0;
            foreach (var u in Units)
                if (u.Alive && (u.Pos - Arena.Point).sqrMagnitude < Arena.PointR * Arena.PointR) OnPoint[u.Team]++;
            int was = Owner;
            int n0 = OnPoint[0], n1 = OnPoint[1];
            if (n0 > 0 && n1 == 0) Prog = Mathf.Min(100f, Prog + dt * (CaptureBase + CapturePerUnit * n0));
            else if (n1 > 0 && n0 == 0) Prog = Mathf.Max(-100f, Prog - dt * (CaptureBase + CapturePerUnit * n1));
            else if (n0 == 0 && n1 == 0)
            {
                float home = Owner == 0 ? 100f : Owner == 1 ? -100f : 0f;
                Prog = Mathf.MoveTowards(Prog, home, 12f * dt);
            }
            if (Prog >= 100f) Owner = 0;
            else if (Prog <= -100f) Owner = 1;

            if (Owner != was)
            {
                OnFeed?.Invoke($"{HeroDb.TeamName[Owner]} took the Kut Tree", Owner);
                if (Player != null)
                {
                    bool ours = Owner == Player.Team;
                    OnBanner?.Invoke(ours ? "Tree taken" : "Tree lost", ours ? "Hold it to raise your claim" : "Stand under the branches to win it back");
                    OnSound?.Invoke("cap");
                }
                Fx.RingAt(Arena.Point, 0.3f, Arena.PointR + 0.5f, 0.8f, HeroDb.TeamColor[Owner], 0.16f);
            }

            if (Owner >= 0)
            {
                float cap = OnPoint[1 - Owner] > 0 ? 99f : 100f;
                if (Score[Owner] < cap) Score[Owner] = Mathf.Min(cap, Score[Owner] + dt * ScoreRate);
                if (Score[Owner] >= 100f) End(Owner);
            }
            if (!Over && Time >= MatchTime)
            {
                int w = Score[0] == Score[1] ? Owner : (Score[0] > Score[1] ? 0 : 1);
                End(w);
            }
        }

        void End(int winner)
        {
            if (Over) return;
            Over = true;
            Winner = winner;
            OnEnd?.Invoke(winner);
        }
    }
}
