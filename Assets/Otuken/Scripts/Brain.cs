using UnityEngine;

namespace Otuken
{
    /// <summary>
    /// Bot behaviour: pick the closest visible foe (weak ones first), keep the range that suits the kit,
    /// strafe, fall back to the spawn camp when hurt, otherwise contest the Kut Tree. Healers shadow the
    /// most wounded ally. Aim is led for projectiles and blurred by the difficulty's jitter.
    /// </summary>
    public static class Brain
    {
        static readonly float[] PreferredRange = { 0.26f, 0.2f, 3.6f, 2.9f };
        const float Sight = 7.6f;

        public static Cmd Think(Match m, Unit u, float dt)
        {
            var A = u.Ai;
            var k = u.Kit.Id;
            A.Think -= dt; A.StrafeT -= dt; A.OffT -= dt;
            if (A.StrafeT <= 0) { A.Strafe = -A.Strafe; A.StrafeT = Random.Range(0.7f, 1.8f); }
            if (A.OffT <= 0) { A.OffT = Random.Range(3f, 6f); A.Off = new Vector2(Random.Range(-0.9f, 0.9f), Random.Range(-0.9f, 0.9f)); }

            if (A.Think <= 0)
            {
                A.Think = Random.Range(0.18f, 0.3f);
                Unit best = null;
                float bs = float.MaxValue;
                foreach (var e in m.Units)
                {
                    if (!e.Alive || e.Team == u.Team) continue;
                    if ((e.Pos - Arena.Spawn[e.Team]).sqrMagnitude < 4f) continue; // do not dive into enemy camps
                    float d = Vector2.Distance(u.Pos, e.Pos);
                    if (d > Sight) continue;
                    float s = d - (e.HpFrac < 0.4f ? 1.2f : 0f);
                    if (s < bs && Arena.LineOfSight(u.Pos, e.Pos)) { bs = s; best = e; }
                }
                if (best != A.Tgt) { A.Tgt = best; A.Seen = 0; }

                if (k == KitId.Healer)
                {
                    Unit w = null;
                    float wr = 2f;
                    foreach (var a in m.Units)
                    {
                        if (!a.Alive || a.Team != u.Team || a == u) continue;
                        float r = a.HpFrac - (a.IsPlayer ? 0.08f : 0f);
                        if (Vector2.Distance(u.Pos, a.Pos) < 11f && r < wr) { wr = r; w = a; }
                    }
                    A.Ward = w;
                }
            }

            var t = A.Tgt != null && A.Tgt.Alive ? A.Tgt : null;
            if (t != null) A.Seen += dt;
            float hpR = u.HpFrac;
            float dist = t != null ? Vector2.Distance(u.Pos, t.Pos) : 0f;
            var ward = A.Ward != null && A.Ward.Alive ? A.Ward : null;

            Vector2 mv = Vector2.zero;
            bool hasGoal = false;
            Vector2 goal = default;
            float pref = PreferredRange[(int)k];

            if (hpR < 0.28f && k != KitId.Tank && Vector2.Distance(u.Pos, Arena.Spawn[u.Team]) > 2f)
            {
                hasGoal = true; goal = Arena.Spawn[u.Team];
            }
            else if (t != null)
            {
                var dir = (t.Pos - u.Pos) / Mathf.Max(dist, 0.001f);
                if (dist > pref + 0.5f) mv = dir;
                else if (dist < pref - 0.7f) mv = -dir;
                float sw = (k == KitId.Tank || k == KitId.Wolf) ? 0.35f : 0.8f;
                mv += new Vector2(-dir.y, dir.x) * (A.Strafe * sw);
                if (k == KitId.Healer && ward != null && Vector2.Distance(u.Pos, ward.Pos) > 1.8f)
                    mv = mv * 0.4f + (ward.Pos - u.Pos).normalized;
            }
            else if (k == KitId.Healer && ward != null && Vector2.Distance(u.Pos, ward.Pos) > 1.6f && ward.HpFrac < 0.95f)
            {
                hasGoal = true; goal = ward.Pos;
            }
            else
            {
                hasGoal = true; goal = Arena.Point + A.Off;
            }
            if (hasGoal)
            {
                var dg = goal - u.Pos;
                if (dg.magnitude > 0.24f) mv = dg.normalized;
            }

            // Unstick: if we meant to move but barely did, wander off at a random angle for a moment.
            A.Chk -= dt;
            if (A.Chk <= 0)
            {
                A.Chk = 0.5f;
                if (Vector2.Distance(u.Pos, A.Last) < 0.14f && mv.magnitude > 0.5f && t == null) { A.UnT = 0.7f; A.UnA = Random.Range(0f, Util.Tau); }
                A.Last = u.Pos;
            }
            if (A.UnT > 0) { A.UnT -= dt; mv = Util.Dir(A.UnA); }
            else mv = Steer(u, mv);

            var c = new Cmd { Move = mv };
            if (t != null)
            {
                var aim = t.Pos;
                if (!u.Kit.Melee) aim += t.Vel * (dist / u.Kit.ProjSpeed * 0.85f);
                A.JitV += (Random.Range(-1f, 1f) * u.Jitter - A.JitV) * Mathf.Min(1f, dt * 4f);
                u.Ang = Mathf.Atan2(aim.y - u.Pos.y, aim.x - u.Pos.x) + A.JitV;
                float range = u.Kit.Melee ? u.Kit.Range + u.R + t.R * 0.6f : u.Kit.Range * 0.92f;
                c.Fire = A.Seen > u.React && dist < range;
            }
            else if (mv.sqrMagnitude > 0.0001f)
            {
                u.Ang = Util.Turn(u.Ang, Mathf.Atan2(mv.y, mv.x), dt * 8f);
            }

            if (u.SkillT <= 0)
            {
                switch (k)
                {
                    case KitId.Tank: c.Skill = t != null && dist < 3.2f && hpR < 0.85f; break;
                    case KitId.Wolf: c.Skill = t != null && dist > 0.9f && dist < 2.5f && A.Seen > u.React; break;
                    case KitId.Archer: c.Skill = t != null && dist < 4.8f && A.Seen > u.React; break;
                    case KitId.Healer: c.Skill = hpR < 0.55f || m.CountNear(u, 2.4f, false, 0.6f) > 0; break;
                }
            }
            if (u.Ult >= 100f)
            {
                switch (k)
                {
                    case KitId.Tank:
                        int n = m.CountNear(u, 2.3f, true);
                        c.Ult = n >= 2 || (n >= 1 && hpR < 0.45f);
                        break;
                    case KitId.Wolf: c.Ult = t != null && dist < 4.2f; break;
                    case KitId.Archer: c.Ult = t != null && A.Seen > u.React + 0.3f; break;
                    case KitId.Healer: c.Ult = m.CountNear(u, 3.2f, false, 0.5f) >= 2 || hpR < 0.3f; break;
                }
            }
            return c;
        }

        /// <summary>Bend the wanted direction around obstacles ahead, sliding along their tangent.</summary>
        static Vector2 Steer(Unit u, Vector2 mv)
        {
            float m = mv.magnitude;
            if (m < 0.01f) return Vector2.zero;
            mv /= m;
            Vector2 acc = Vector2.zero;
            foreach (var o in Arena.Obstacles)
            {
                var dv = o.Pos - u.Pos;
                float d = Mathf.Max(dv.magnitude, 0.001f);
                float gap = d - o.R - u.R;
                if (gap > 0.9f) continue;
                float fwd = Vector2.Dot(dv, mv) / d;
                if (fwd < 0.15f) continue;
                var tg = new Vector2(-dv.y, dv.x) / d;
                float dot = Vector2.Dot(tg, mv);
                if (dot < 0 || (Mathf.Abs(dot) < 0.05f && u.Ai != null && u.Ai.Strafe < 0)) tg = -tg;
                acc += tg * ((1f - Mathf.Max(0f, gap) / 0.9f) * fwd * 1.6f);
            }
            mv += acc;
            return mv.normalized * Mathf.Min(1f, m);
        }
    }
}
