using System.Collections.Generic;
using UnityEngine;

namespace Otuken
{
    /// <summary>Creates the game in whatever scene is loaded, so no scene setup or prefabs are needed.</summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<GameManager>() != null) return;
            new GameObject("Otuken").AddComponent<GameManager>();
        }
    }

    public sealed class GameManager : MonoBehaviour
    {
        enum State { Menu, Playing, Paused, Ended }

        sealed class Stick { public int Id = -1; public Vector2 Origin, Cur; }

        struct FeedItem { public string Text; public int Team; public float Born; }

        const float StickR = 58f, UltR = 62f, SkillR = 48f;
        static readonly Vector3 ShowcasePos = new Vector3(0, 0, -10.4f);

        static readonly string[] PctText = new string[101];
        static readonly string[] ClaimText = new string[101];

        static GameManager()
        {
            for (int i = 0; i <= 100; i++) { PctText[i] = i + "%"; ClaimText[i] = "CLAIMING " + i + "%"; }
        }

        State state = State.Menu;
        Match match;
        ArenaView arena;
        Camera cam;
        Sfx sfx;
        readonly List<Hero>[] sides = new List<Hero>[2];

        Hero chosen;
        int side, difficulty = 1;
        Transform showcaseRoot, showcaseModel;
        Hero showcaseHero;
        float menuHeroVX = 0.5f, demoRestart = -1f, endDelay = -1f, shake;
        Vector3 camBase, camVel;

        readonly List<FeedItem> feed = new List<FeedItem>();
        string bannerTitle, bannerSub;
        float bannerT = -10f;
        readonly string[] teamLabel = new string[2];
        readonly List<Unit> endRows = new List<Unit>();

        readonly Stick moveS = new Stick(), aimS = new Stick();
        readonly HashSet<int> knownTouches = new HashSet<int>(), nowTouches = new HashSet<int>();
        bool touchMode, skillQ, ultQ;

        string clockStr, hpStr, respawnStr, playerName, skillName, ultName;
        int clockSec = -1, hpVal = -1, respawnVal = -1;

        // Automated smoke test (simulator / emulator runs): launch with env OTUKEN_AUTOTEST=1 or arg -otukenAutotest.
        bool autotest, autoEndLogged;
        float autoClock, autoNextReport, fpsTime;
        int fpsFrames, errorCount;
        string lastError;

        static bool AutotestRequested()
        {
            if (System.Environment.GetEnvironmentVariable("OTUKEN_AUTOTEST") == "1") return true;
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a == "-otukenAutotest") return true;
            return false;
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            errorCount++;
            lastError = message;
        }

        void AutoTick(float dt)
        {
            autoClock += dt; fpsTime += dt; fpsFrames++;
            if (state == State.Menu && autoClock > 6f)
            {
                chosen = HeroDb.Heroes[2];
                SetShowcase(chosen);
                StartMatch();
                match.Player.Ai = new BotState();
                Debug.Log("[OTUKEN] autotest: match started as " + chosen.Name);
            }
            if (autoClock >= autoNextReport)
            {
                autoNextReport = autoClock + 5f;
                float fps = fpsFrames / Mathf.Max(0.001f, fpsTime);
                fpsFrames = 0; fpsTime = 0;
                string m = "";
                if (match != null && !match.Demo)
                {
                    int alive = 0, kills = 0;
                    foreach (var u in match.Units) { if (u.Alive) alive++; kills += u.Kills; }
                    m = $" score={match.Score[0]:0}/{match.Score[1]:0} owner={match.Owner} prog={match.Prog:0} alive={alive}/8 kills={kills}" +
                        $" player={match.Player.Hero.Name} hp={match.Player.Hp:0} pos=({match.Player.Pos.x:0.0},{match.Player.Pos.y:0.0})";
                }
                Debug.Log($"[OTUKEN] t={autoClock:0}s state={state} fps={fps:0} errors={errorCount}{m}" + (lastError != null ? " lastError=" + lastError : ""));
            }
            if (state == State.Ended && !autoEndLogged)
            {
                autoEndLogged = true;
                Debug.Log($"[OTUKEN] autotest: match ended, winner={match.Winner}");
            }
        }

        // ------------------------------------------------------------------ setup

        void Awake()
        {
            Application.logMessageReceived += OnLog;
            autotest = AutotestRequested();
            Debug.Log($"[OTUKEN] boot autotest={autotest} device={SystemInfo.deviceModel} os={SystemInfo.operatingSystem} " +
                      $"gfx={SystemInfo.graphicsDeviceType} screen={Screen.width}x{Screen.height} dpi={Screen.dpi:0} " +
                      $"pipeline={(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name : "built-in")} " +
                      $"standard={(Shader.Find("Standard") != null)} sprites={(Shader.Find("Sprites/Default") != null)} keep={Prim.KeepTypes.Length}");
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;

            SetupCamera();
            SetupLight();
            sides[0] = HeroDb.Side(0);
            sides[1] = HeroDb.Side(1);
            arena = Arena.Build(transform);
            sfx = new Sfx(gameObject);
            chosen = HeroDb.Heroes[0];
            BuildShowcase();
            SetShowcase(chosen);
            StartDemo();
            camBase = ShowcasePos + new Vector3(0, 1.2f, -3.1f);
            cam.transform.position = camBase;
        }

        void SetupCamera()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            if (FindFirstObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            var bg = new Color(0.03f, 0.045f, 0.07f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = bg;
            cam.fieldOfView = 42f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 90f;
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = bg;
            RenderSettings.fogStartDistance = 24f;
            RenderSettings.fogEndDistance = 50f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.34f, 0.37f, 0.46f);
        }

        void SetupLight()
        {
            Light sun = null;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
            if (sun == null)
            {
                sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            sun.color = new Color(1f, 0.92f, 0.8f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.7f;
        }

        void BuildShowcase()
        {
            showcaseRoot = new GameObject("Showcase").transform;
            showcaseRoot.SetParent(transform, false);
            showcaseRoot.position = ShowcasePos;
            Prim.Make(PrimitiveType.Cylinder, showcaseRoot, new Vector3(0, -0.06f, 0), new Vector3(1.5f, 0.08f, 1.5f), new Color(0.36f, 0.34f, 0.31f));
            Prim.Make(PrimitiveType.Cylinder, showcaseRoot, new Vector3(0, -0.2f, 0), new Vector3(1.9f, 0.1f, 1.9f), new Color(0.22f, 0.21f, 0.2f));
            var ring = Prim.Line(showcaseRoot, Ui.Gold, 0.03f, true);
            Prim.Circle(ring, Vector3.zero, 0.7f, 0.03f, 64);
        }

        void SetShowcase(Hero h)
        {
            if (showcaseHero == h) return;
            showcaseHero = h;
            if (showcaseModel != null) Destroy(showcaseModel.gameObject);
            showcaseModel = UnitView.BuildModel(h, h.Side, HeroDb.KitOf(h).Radius, showcaseRoot);
            showcaseModel.localPosition = new Vector3(0, 0.02f, 0);
        }

        // ------------------------------------------------------------------ flow

        void Hook(Match m)
        {
            m.OnFeed = (text, team) =>
            {
                if (m.Demo) return;
                feed.Insert(0, new FeedItem { Text = text, Team = team, Born = Time.unscaledTime });
                if (feed.Count > 5) feed.RemoveAt(feed.Count - 1);
            };
            m.OnBanner = (title, sub) => { if (!m.Demo) { bannerTitle = title; bannerSub = sub; bannerT = Time.unscaledTime; } };
            m.OnSound = s => { if (!m.Demo) sfx.Play(s); };
            m.OnShake = a => { if (!m.Demo) shake = Mathf.Max(shake, a); };
            m.OnEnd = w =>
            {
                if (m.Demo) { demoRestart = 3f; return; }
                endDelay = 1.3f;
                sfx.Play(w == m.Player.Team ? "win" : "lose");
            };
        }

        void StartDemo()
        {
            match?.Dispose();
            match = new Match(transform, arena, true, null, 1);
            Hook(match);
            demoRestart = -1f;
        }

        void StartMatch()
        {
            match?.Dispose();
            match = new Match(transform, arena, false, chosen, difficulty);
            Hook(match);
            var p = match.Player;
            for (int t = 0; t < 2; t++) teamLabel[t] = HeroDb.TeamName[t].ToUpperInvariant() + (t == p.Team ? " (YOU)" : "");
            playerName = p.Hero.Name.ToUpperInvariant();
            skillName = p.Hero.Skill;
            ultName = p.Hero.Ult;
            hpVal = respawnVal = clockSec = -1;
            feed.Clear();
            ResetInput();
            endDelay = -1f;
            shake = 0;
            bannerTitle = "Hold the Kut Tree";
            bannerSub = "Stand under its branches. First side to 100% wins.";
            bannerT = Time.unscaledTime;
            showcaseRoot.gameObject.SetActive(false);
            camBase = GameCameraTarget();
            cam.transform.position = camBase;
            state = State.Playing;
        }

        void ToMenu()
        {
            state = State.Menu;
            StartDemo();
            showcaseRoot.gameObject.SetActive(true);
        }

        void Pause()
        {
            if (state != State.Playing || match.Over) return;
            state = State.Paused;
            ResetInput();
        }

        void Resume()
        {
            state = State.Playing;
            ResetInput();
        }

        void ResetInput()
        {
            moveS.Id = aimS.Id = -1;
            skillQ = ultQ = false;
            // A finger that is down right now (it just tapped a menu button) must not turn into a stick.
            knownTouches.Clear();
            foreach (var t in Controls.Touches) knownTouches.Add(t.Id);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) Pause();
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            Controls.Poll();
            Ui.Layout();
            float dt = Mathf.Min(Time.deltaTime, 0.05f);

            switch (state)
            {
                case State.Menu:
                    match.Tick(dt, default);
                    if (match.Over) { demoRestart -= dt; if (demoRestart <= 0) StartDemo(); }
                    if (showcaseModel != null) showcaseModel.localRotation = Quaternion.Euler(0, 160f + Mathf.Sin(Time.time * 0.7f) * 40f, 0);
                    MenuCamera(dt);
                    break;

                case State.Playing:
                    if (Controls.PausePressed) { Pause(); break; }
                    var cmd = ReadPlayer();
                    if (state != State.Playing) break;
                    if (!match.Over) match.Tick(dt, cmd);
                    else
                    {
                        match.TickVisuals(dt);
                        endDelay -= dt;
                        if (endDelay <= 0) EnterEnd();
                    }
                    GameCamera(dt);
                    break;

                case State.Paused:
                    if (Controls.PausePressed) Resume();
                    break;

                case State.Ended:
                    match.TickVisuals(dt);
                    GameCamera(dt);
                    break;
            }

            for (int i = feed.Count - 1; i >= 0; i--)
                if (Time.unscaledTime - feed[i].Born > 6f) feed.RemoveAt(i);

            if (autotest) AutoTick(Time.unscaledDeltaTime);
        }

        void EnterEnd()
        {
            state = State.Ended;
            endRows.Clear();
            endRows.AddRange(match.Units);
            endRows.Sort((a, b) => a.Team != b.Team ? a.Team - b.Team : b.Kills - a.Kills);
        }

        Rect PauseRect => new Rect(Ui.Safe.x + 12, Ui.Safe.y + 10, 52, 52);
        Vector2 UltC => new Vector2(Ui.Safe.xMax - 30 - UltR, Ui.Safe.yMax - 26 - UltR);
        Vector2 SkillC => new Vector2(UltC.x - UltR - 24 - SkillR, Ui.Safe.yMax - 26 - SkillR);

        PlayerCmd ReadPlayer()
        {
            var u = match.Player;
            var cmd = new PlayerCmd();

            nowTouches.Clear();
            foreach (var t in Controls.Touches)
            {
                nowTouches.Add(t.Id);
                var gp = Ui.FromScreen(t.Pos);
                if (t.Id == moveS.Id) { moveS.Cur = gp; continue; }
                if (t.Id == aimS.Id) { aimS.Cur = gp; continue; }
                if (knownTouches.Contains(t.Id)) continue;
                touchMode = true;
                if (PauseRect.Contains(gp)) Pause();
                else if ((gp - UltC).sqrMagnitude < UltR * UltR * 1.15f) ultQ = true;
                else if ((gp - SkillC).sqrMagnitude < SkillR * SkillR * 1.25f) skillQ = true;
                else if (gp.x < Ui.VW * 0.5f && moveS.Id < 0) { moveS.Id = t.Id; moveS.Origin = moveS.Cur = gp; }
                else if (gp.x >= Ui.VW * 0.5f && aimS.Id < 0) { aimS.Id = t.Id; aimS.Origin = aimS.Cur = gp; }
            }
            if (moveS.Id >= 0 && !nowTouches.Contains(moveS.Id)) moveS.Id = -1;
            if (aimS.Id >= 0 && !nowTouches.Contains(aimS.Id)) aimS.Id = -1;
            knownTouches.Clear();
            foreach (var id in nowTouches) knownTouches.Add(id);

            Vector2 mv = Controls.KeyMove();
            if (moveS.Id >= 0)
            {
                var d = moveS.Cur - moveS.Origin;
                float m = d.magnitude;
                if (m > 6f)
                {
                    var n = d / Mathf.Max(m, StickR);
                    mv = new Vector2(n.x, -n.y); // screen down is world south
                }
                if (m > StickR * 1.4f) moveS.Origin = moveS.Cur - d / m * (StickR * 1.4f); // the stick follows the thumb
            }
            cmd.Move = mv.sqrMagnitude > 1f ? mv.normalized : mv;

            if (aimS.Id >= 0)
            {
                cmd.Fire = true;
                var d = aimS.Cur - aimS.Origin;
                if (d.magnitude > 16f) { cmd.HasAim = true; cmd.Aim = Mathf.Atan2(-d.y, d.x); }
                else cmd.AutoAim = true;
            }
            else if (!touchMode && Controls.MousePresent)
            {
                var mp = Controls.MousePos;
                var gp = Ui.FromScreen(mp);
                bool onPause = PauseRect.Contains(gp);
                bool onUlt = (gp - UltC).sqrMagnitude < UltR * UltR;
                bool onSkill = (gp - SkillC).sqrMagnitude < SkillR * SkillR;
                if (Controls.MouseClicked)
                {
                    if (onPause) Pause();
                    else if (onUlt) ultQ = true;
                    else if (onSkill) skillQ = true;
                }
                var ray = cam.ScreenPointToRay(mp);
                if (ray.direction.y < -0.01f)
                {
                    var hit = ray.origin + ray.direction * (-ray.origin.y / ray.direction.y);
                    cmd.HasAim = true;
                    cmd.Aim = Mathf.Atan2(hit.z - u.Pos.y, hit.x - u.Pos.x);
                }
                cmd.Fire = (Controls.MouseHeld && !onPause && !onUlt && !onSkill) || Controls.FireKeyHeld;
            }
            else if (Controls.FireKeyHeld)
            {
                cmd.Fire = true;
                cmd.AutoAim = true;
            }

            if (Controls.SkillPressed) skillQ = true;
            if (Controls.UltPressed) ultQ = true;
            cmd.Skill = skillQ;
            cmd.Ult = ultQ;
            skillQ = ultQ = false;
            return u.Alive ? cmd : default;
        }

        // ------------------------------------------------------------------ camera

        Vector3 GameCameraTarget()
        {
            var target = Util.Ground(match.Player.Pos);
            float k = Mathf.Clamp(1.75f / Mathf.Max(0.3f, cam.aspect), 1f, 2.2f);
            return target + new Vector3(0, 10.5f * k, -6.2f * k);
        }

        void GameCamera(float dt)
        {
            var want = GameCameraTarget();
            camBase = Vector3.SmoothDamp(camBase, want, ref camVel, 0.12f, Mathf.Infinity, dt);
            var look = Util.Ground(match.Player.Pos) + new Vector3(0, 0, 0.4f);
            var offset = want - look;
            cam.transform.position = camBase;
            cam.transform.rotation = Quaternion.LookRotation(-offset);
            if (shake > 0)
            {
                cam.transform.position += Random.insideUnitSphere * (shake * 0.25f);
                shake = Mathf.Max(0, shake - dt * 2.5f);
            }
        }

        void MenuCamera(float dt)
        {
            const float dist = 3.1f;
            float tanH = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * cam.aspect;
            float shift = (2f * menuHeroVX - 1f) * tanH * dist;
            var focus = ShowcasePos + new Vector3(-shift, 0.62f, 0);
            var want = focus + new Vector3(0, 0.55f, -dist);
            camBase = Vector3.SmoothDamp(camBase, want, ref camVel, 0.25f, Mathf.Infinity, dt);
            cam.transform.position = camBase;
            cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(focus - want), Mathf.Min(1f, dt * 6f));
        }

        // ------------------------------------------------------------------ UI

        void OnGUI()
        {
            Ui.Begin();
            switch (state)
            {
                case State.Menu: DrawMenu(); break;
                case State.Playing: DrawHud(); break;
                case State.Paused: DrawHud(); DrawPause(); break;
                case State.Ended: DrawEnd(); break;
            }
        }

        void DrawMenu()
        {
            var sa = Ui.Safe;
            float L = sa.x + 28, T = sa.y + 20, lw = Mathf.Min(500f, Ui.VW * 0.4f);
            Ui.Fill(new Rect(0, 0, L + lw + 28, Ui.VH), new Color(Ui.Night.r, Ui.Night.g, Ui.Night.b, 0.88f));

            Ui.Text(new Rect(L, T, lw, 18), "THE SACRED PLAIN OF THE GÖKTÜRKS", 12, Ui.Gold, bold: true);
            Ui.Text(new Rect(L - 3, T + 14, lw, 100), "ÖTÜKEN", 86, Ui.Bone, TextAnchor.UpperLeft, true);
            Ui.Text(new Rect(L, T + 114, lw, 60), "Four against four. Hold the Kut Tree at the heart of the steppe until your side's claim reaches 100%.", 16, Ui.Ash, wrap: true);

            float y = T + 176, tw = (lw - 8) / 2;
            for (int s = 0; s < 2; s++)
            {
                var r = new Rect(L + s * (tw + 8), y, tw, 56);
                var tc = HeroDb.TeamColor[s];
                bool sel = side == s;
                Ui.Fill(r, sel ? new Color(tc.r, tc.g, tc.b, 0.16f) : new Color(0.12f, 0.16f, 0.26f, 0.5f));
                Ui.Frame(r, sel ? tc : Ui.Hairline, sel ? 2f : 1f);
                Ui.Text(new Rect(r.x + 12, r.y + 8, r.width - 16, 24), HeroDb.TeamName[s].ToUpperInvariant(), 18, Ui.Bone, bold: true);
                Ui.Text(new Rect(r.x + 12, r.y + 32, r.width - 16, 18), HeroDb.TeamTagline[s], 12, Ui.Ash);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    side = s;
                    if (chosen.Side != s) chosen = sides[s][0];
                    SetShowcase(chosen);
                    sfx.Play("tap");
                }
            }

            y += 66;
            float cw = (lw - 10) / 2, ch = 86;
            var list = sides[side];
            for (int i = 0; i < list.Count; i++)
            {
                var h = list[i];
                var r = new Rect(L + (i % 2) * (cw + 10), y + (i / 2) * (ch + 10), cw, ch);
                bool sel = h == chosen;
                Ui.Fill(r, sel ? Ui.Felt2 : Ui.Felt);
                Ui.Frame(r, sel ? Ui.Gold : Ui.Hairline, sel ? 2f : 1f);
                var c = new Vector2(r.x + 32, r.y + ch / 2);
                Ui.Disc(c, 19, HeroDb.TeamColor[h.Side]);
                Ui.Disc(c, 15, h.Color);
                Ui.Text(new Rect(r.x + 62, r.y + 18, cw - 68, 26), h.Name.ToUpperInvariant(), 19, Ui.Bone, bold: true);
                Ui.Text(new Rect(r.x + 62, r.y + 46, cw - 68, 20), HeroDb.KitOf(h).Role.ToUpperInvariant(), 12, Ui.Gold, bold: true);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { chosen = h; SetShowcase(h); sfx.Play("tap"); }
            }

            y += 2 * (ch + 10) + 6;
            Ui.Text(new Rect(L, y, lw, 18), "RIVAL STRENGTH", 12, Ui.Ash, bold: true);
            y += 22;
            float bw = (lw - 16) / 3;
            for (int d = 0; d < 3; d++)
                if (Ui.Button(new Rect(L + d * (bw + 8), y, bw, 40), HeroDb.DifficultyName[d], false, 16, difficulty == d)) { difficulty = d; sfx.Play("tap"); }
            y += 52;
            Ui.Text(new Rect(L, y, lw, Mathf.Max(20, sa.yMax - y - 8)),
                "Phone: left thumb moves, right thumb aims and fires (hold it still to auto-aim). Tap the round buttons for skill and ultimate.\nKeyboard: WASD to move, mouse to aim, click to fire, E skill, Q ultimate, Esc pause.",
                13, Ui.Ash, wrap: true);

            // Hero detail panel
            float pw = Mathf.Min(430f, Ui.VW * 0.33f), px = sa.xMax - pw - 24, py = T, ph = sa.yMax - 20 - py;
            Ui.Fill(new Rect(px, py, pw, ph), Ui.Felt);
            Ui.Frame(new Rect(px, py, pw, ph), Ui.Hairline);
            var k = HeroDb.KitOf(chosen);
            float x = px + 20, w = pw - 40, yy = py + 18;
            Ui.Text(new Rect(x, yy, w, 18), k.Role.ToUpperInvariant() + "  ·  " + k.Hp + " HEALTH", 12, Ui.Gold, bold: true); yy += 20;
            Ui.Text(new Rect(x - 2, yy, w, 48), chosen.Name.ToUpperInvariant(), 36, Ui.Bone, bold: true); yy += 44;
            Ui.Text(new Rect(x, yy, w, 20), chosen.Title, 14, Ui.Ash); yy += 28;
            float lh = Ui.TextHeight(chosen.Lore, w, 14);
            Ui.Text(new Rect(x, yy, w, lh), chosen.Lore, 14, Ui.Bone, wrap: true); yy += lh + 16;
            yy = Ability(x, yy, w, "WEAPON", chosen.Weapon, k.WeaponText);
            yy = Ability(x, yy, w, "SKILL", chosen.Skill, k.SkillText);
            Ability(x, yy, w, "ULTIMATE", chosen.Ult, k.UltText);
            if (Ui.Button(new Rect(x, py + ph - 20 - 60, w, 60), "RIDE OUT", true, 26)) { sfx.Play("tap"); StartMatch(); }

            menuHeroVX = ((L + lw + 28) + px) * 0.5f / Ui.VW;
        }

        static float Ability(float x, float y, float w, string label, string name, string desc)
        {
            Ui.Text(new Rect(x, y + 2, 76, 16), label, 11, Ui.Ash, bold: true);
            Ui.Text(new Rect(x + 80, y, w - 80, 20), name, 15, Ui.Bone, bold: true);
            float h = Ui.TextHeight(desc, w - 80, 13);
            Ui.Text(new Rect(x + 80, y + 20, w - 80, h), desc, 13, Ui.Ash, wrap: true);
            return y + 20 + h + 12;
        }

        void DrawHud()
        {
            var u = match.Player;
            var sa = Ui.Safe;

            // Health bars and damage numbers over the battlefield
            foreach (var v in match.Units)
            {
                if (!v.Alive) continue;
                var sp = cam.WorldToScreenPoint(Util.Ground(v.Pos, 1.42f));
                if (sp.z < 0) continue;
                var g = Ui.FromScreen(sp);
                float bw = v.Kit.Id == KitId.Tank ? 54f : 42f;
                Ui.Fill(new Rect(g.x - bw / 2 - 1, g.y - 5, bw + 2, 9), new Color(0, 0, 0, 0.6f));
                Ui.Fill(new Rect(g.x - bw / 2, g.y - 4, bw * v.HpFrac, 7), v.IsPlayer ? Ui.Gold : HeroDb.TeamColor[v.Team]);
            }
            foreach (var t in match.Fx.Texts)
            {
                var sp = cam.WorldToScreenPoint(t.Pos);
                if (sp.z < 0) continue;
                var g = Ui.FromScreen(sp);
                var c = t.Col; c.a = Mathf.Clamp01(t.Life * 2f);
                Ui.Text(new Rect(g.x - 40, g.y - 12, 80, 24), t.Text, 20, c, TextAnchor.MiddleCenter, true);
            }

            // Direction to the Kut Tree when it is off screen
            var ps = cam.WorldToScreenPoint(new Vector3(0, 0.5f, 0));
            var pg = Ui.FromScreen(ps);
            if (ps.z > 0 && (pg.x < 0 || pg.y < 0 || pg.x > Ui.VW || pg.y > Ui.VH))
            {
                var c = new Vector2(Mathf.Clamp(pg.x, sa.x + 50, sa.xMax - 50), Mathf.Clamp(pg.y, sa.y + 100, sa.yMax - 40));
                Ui.Disc(c, 10, match.Owner >= 0 ? HeroDb.TeamColor[match.Owner] : Ui.Gold);
                Ui.Text(new Rect(c.x - 60, c.y + 12, 120, 18), "KUT TREE", 12, Ui.Bone, TextAnchor.UpperCenter, true);
            }

            // Touch sticks
            if (moveS.Id >= 0) { Ui.RingAt(moveS.Origin, StickR, new Color(1, 1, 1, 0.35f)); Ui.Disc(Knob(moveS), 24, new Color(1, 1, 1, 0.4f)); }
            else if (touchMode && match.Time < 15f) Ui.RingAt(new Vector2(sa.x + 130, sa.yMax - 130), StickR, new Color(1, 1, 1, 0.15f));
            if (aimS.Id >= 0) { Ui.RingAt(aimS.Origin, StickR, new Color(Ui.Gold.r, Ui.Gold.g, Ui.Gold.b, 0.4f)); Ui.Disc(Knob(aimS), 24, new Color(Ui.Gold.r, Ui.Gold.g, Ui.Gold.b, 0.45f)); }

            // Score panel
            float sw = Mathf.Min(580f, Ui.VW - 300f);
            var sr = new Rect((Ui.VW - sw) / 2, sa.y + 10, sw, 64);
            Ui.Fill(sr, Ui.Felt);
            Ui.Frame(sr, Ui.Hairline);
            float colw = (sw - 150) / 2;
            for (int t = 0; t < 2; t++)
            {
                float x = t == 0 ? sr.x + 12 : sr.xMax - 12 - colw;
                var anchor = t == 0 ? TextAnchor.UpperLeft : TextAnchor.UpperRight;
                Ui.Text(new Rect(x, sr.y + 7, colw, 14), teamLabel[t], 11, t == u.Team ? Ui.Bone : Ui.Ash, anchor, true);
                Ui.Text(new Rect(x, sr.y + 20, colw, 30), PctText[Mathf.Clamp((int)match.Score[t], 0, 100)], 26, HeroDb.TeamColor[t], anchor, true);
                Ui.Bar(new Rect(x, sr.y + 52, colw, 5), match.Score[t] / 100f, HeroDb.TeamColor[t], t == 1);
            }
            int secLeft = Mathf.CeilToInt(Mathf.Max(0, Match.MatchTime - match.Time));
            if (secLeft != clockSec) { clockSec = secLeft; clockStr = (secLeft / 60) + ":" + (secLeft % 60).ToString("00"); }
            Ui.Text(new Rect(sr.center.x - 75, sr.y + 8, 150, 28), clockStr, 24, Ui.Bone, TextAnchor.UpperCenter, true);
            string st;
            Color stc;
            bool contested = match.OnPoint[0] > 0 && match.OnPoint[1] > 0;
            if (contested) { st = "CONTESTED"; stc = Ui.Gold; }
            else if (match.Owner < 0) { int p = Mathf.RoundToInt(Mathf.Abs(match.Prog)); st = p > 1 ? ClaimText[p] : "TREE UNCLAIMED"; stc = Ui.Ash; }
            else { st = match.Owner == u.Team ? "YOUR TREE" : "ENEMY TREE"; stc = HeroDb.TeamColor[match.Owner]; }
            Ui.Text(new Rect(sr.center.x - 75, sr.y + 40, 150, 16), st, 11, stc, TextAnchor.UpperCenter, true);

            // Pause button
            var pr = PauseRect;
            Ui.Fill(pr, Ui.Felt);
            Ui.Frame(pr, Ui.Hairline);
            Ui.Text(pr, "II", 20, Ui.Bone, TextAnchor.MiddleCenter, true);

            // Player panel
            var mr = new Rect(sa.x + 12, sa.y + 74, 230, 60);
            Ui.Fill(mr, Ui.Felt);
            Ui.Text(new Rect(mr.x + 10, mr.y + 6, 210, 20), playerName, 17, Ui.Bone, bold: true);
            Ui.Bar(new Rect(mr.x + 10, mr.y + 30, 210, 9), u.HpFrac, Ui.Gold);
            int hpNow = Mathf.CeilToInt(u.Hp);
            if (hpNow != hpVal) { hpVal = hpNow; hpStr = hpNow + " / " + (int)u.MaxHp; }
            Ui.Text(new Rect(mr.x + 10, mr.y + 42, 210, 16), hpStr, 11, Ui.Ash);

            // Kill feed
            float fy = sa.y + 84;
            foreach (var f in feed)
            {
                var r = new Rect(sa.xMax - 12 - 330, fy, 330, 24);
                Ui.Fill(r, new Color(0.04f, 0.06f, 0.1f, 0.75f));
                Ui.Fill(new Rect(r.xMax - 3, r.y, 3, r.height), HeroDb.TeamColor[f.Team]);
                Ui.Text(new Rect(r.x + 8, r.y, r.width - 18, r.height), f.Text, 12, Ui.Bone, TextAnchor.MiddleRight);
                fy += 28;
            }

            // Ability buttons
            bool skillReady = u.SkillT <= 0;
            AbilityButton(SkillC, SkillR, skillReady ? 1f : 1f - u.SkillT / u.Kit.SkillCd, skillReady && u.Alive, skillName, null, "E");
            AbilityButton(UltC, UltR, u.Ult / 100f, u.Ult >= 100f && u.Alive, ultName, PctText[Mathf.Clamp((int)u.Ult, 0, 100)], "Q");

            // Banner
            float age = Time.unscaledTime - bannerT;
            if (age < 2.8f && bannerTitle != null)
            {
                float a = Mathf.Clamp01((2.8f - age) / 0.6f);
                var br = new Rect((Ui.VW - 440) / 2, Ui.VH * 0.22f, 440, 72);
                Ui.Fill(br, new Color(0.04f, 0.06f, 0.1f, 0.8f * a));
                Ui.Text(new Rect(br.x, br.y + 8, br.width, 36), bannerTitle.ToUpperInvariant(), 30, new Color(Ui.Bone.r, Ui.Bone.g, Ui.Bone.b, a), TextAnchor.UpperCenter, true);
                Ui.Text(new Rect(br.x, br.y + 44, br.width, 20), bannerSub, 13, new Color(Ui.Ash.r, Ui.Ash.g, Ui.Ash.b, a), TextAnchor.UpperCenter);
            }

            // Death notice
            if (!u.Alive)
            {
                int left = Mathf.CeilToInt(u.Respawn);
                if (left != respawnVal) { respawnVal = left; respawnStr = "Back at camp in " + left; }
                var dr = new Rect((Ui.VW - 320) / 2, Ui.VH * 0.4f, 320, 76);
                Ui.Fill(dr, new Color(0.04f, 0.06f, 0.1f, 0.85f));
                Ui.Text(new Rect(dr.x, dr.y + 8, dr.width, 36), "FALLEN", 32, Ui.Bone, TextAnchor.UpperCenter, true);
                Ui.Text(new Rect(dr.x, dr.y + 46, dr.width, 20), respawnStr, 14, Ui.Ash, TextAnchor.UpperCenter);
            }
        }

        Vector2 Knob(Stick s)
        {
            var d = s.Cur - s.Origin;
            if (d.magnitude > StickR) d = d.normalized * StickR;
            return s.Origin + d;
        }

        void AbilityButton(Vector2 c, float r, float fill, bool ready, string name, string big, string key)
        {
            Ui.Disc(c, r, new Color(0, 0, 0, 0.45f));
            if (!ready && fill > 0.001f)
            {
                float h = 2 * r * Mathf.Clamp01(fill);
                GUI.BeginGroup(new Rect(c.x - r, c.y + r - h, 2 * r, h));
                Ui.DiscRect(new Rect(0, h - 2 * r, 2 * r, 2 * r), new Color(Ui.Bone.r, Ui.Bone.g, Ui.Bone.b, 0.35f));
                GUI.EndGroup();
            }
            Ui.Disc(c, r - 5, ready ? Ui.Gold : new Color(0.08f, 0.11f, 0.17f, 0.94f));
            var ink = ready ? Ui.Ink : Ui.Bone;
            if (big != null)
            {
                Ui.Text(new Rect(c.x - r, c.y - r * 0.62f, 2 * r, 28), big, 22, ink, TextAnchor.UpperCenter, true);
                Ui.Text(new Rect(c.x - r + 10, c.y - 2, 2 * r - 20, 34), name, 11, ink, TextAnchor.UpperCenter, true, true);
            }
            else Ui.Text(new Rect(c.x - r + 8, c.y - r, 2 * r - 16, 2 * r), name, 12, ink, TextAnchor.MiddleCenter, true, true);
            if (!touchMode) Ui.Text(new Rect(c.x - r, c.y + r - 18, 2 * r, 14), key, 11, ready ? Ui.Ink : Ui.Ash, TextAnchor.UpperCenter, true);
        }

        void DrawPause()
        {
            Ui.Fill(new Rect(0, 0, Ui.VW, Ui.VH), new Color(0.03f, 0.04f, 0.07f, 0.8f));
            var p = new Rect((Ui.VW - 420) / 2, (Ui.VH - 300) / 2, 420, 300);
            Ui.Fill(p, Ui.Felt);
            Ui.Frame(p, Ui.Hairline);
            Ui.Text(new Rect(p.x + 24, p.y + 18, p.width - 48, 60), "PAUSED", 52, Ui.Bone, bold: true);
            float y = p.y + 96;
            if (Ui.Button(new Rect(p.x + 24, y, p.width - 48, 52), "RESUME", true, 20)) Resume();
            y += 62;
            if (Ui.Button(new Rect(p.x + 24, y, p.width - 48, 52), sfx.Muted ? "SOUND: OFF" : "SOUND: ON", false, 18)) sfx.Muted = !sfx.Muted;
            y += 62;
            if (Ui.Button(new Rect(p.x + 24, y, p.width - 48, 52), "LEAVE MATCH", false, 18)) ToMenu();
        }

        void DrawEnd()
        {
            Ui.Fill(new Rect(0, 0, Ui.VW, Ui.VH), new Color(0.03f, 0.04f, 0.07f, 0.82f));
            var u = match.Player;
            bool draw = match.Winner < 0, won = match.Winner == u.Team;
            float pw = Mathf.Min(660f, Ui.VW - 40), ph = Mathf.Min(Ui.VH - 40, 560f);
            var p = new Rect((Ui.VW - pw) / 2, (Ui.VH - ph) / 2, pw, ph);
            Ui.Fill(p, Ui.Felt);
            Ui.Frame(p, Ui.Hairline);
            float x = p.x + 24, w = pw - 48, y = p.y + 16;
            Ui.Text(new Rect(x - 2, y, w, 70), draw ? "DRAW" : won ? "VICTORY" : "DEFEAT", 60, draw ? Ui.Bone : won ? Ui.Gold : HeroDb.TeamColor[1], bold: true);
            y += 70;
            int mine = Mathf.FloorToInt(match.Score[u.Team]);
            string sub = draw ? "Neither side could hold the tree."
                : won ? $"{HeroDb.TeamName[u.Team]} holds Ötüken. Your claim reached {mine}%."
                : $"{HeroDb.TeamName[1 - u.Team]} took Ötüken. Your side reached {mine}%.";
            Ui.Text(new Rect(x, y, w, 20), sub, 14, Ui.Ash);
            y += 32;

            float[] cols = { 0f, 0.5f, 0.6f, 0.7f, 0.85f };
            string[] heads = { "HERO", "K", "D", "DAMAGE", "HEALING" };
            for (int i = 0; i < heads.Length; i++)
                Ui.Text(new Rect(x + w * cols[i], y, i == 0 ? w * 0.5f : w * 0.15f, 16), heads[i], 11, Ui.Ash, i == 0 ? TextAnchor.UpperLeft : TextAnchor.UpperRight, true);
            y += 20;
            float rowH = Mathf.Min(28f, (p.yMax - 84 - y) / Mathf.Max(1, endRows.Count));
            foreach (var r in endRows)
            {
                Ui.Fill(new Rect(x, y + rowH - 1, w, 1), Ui.Hairline);
                Ui.Fill(new Rect(x, y + 4, 3, rowH - 8), HeroDb.TeamColor[r.Team]);
                var c = r.IsPlayer ? Ui.Gold : Ui.Bone;
                Ui.Text(new Rect(x + 10, y, w * 0.5f, rowH), r.Hero.Name + (r.IsPlayer ? " (you)" : ""), 14, c, TextAnchor.MiddleLeft, r.IsPlayer);
                Ui.Text(new Rect(x + w * cols[1], y, w * 0.08f, rowH), r.Kills.ToString(), 14, c, TextAnchor.MiddleRight);
                Ui.Text(new Rect(x + w * cols[2], y, w * 0.08f, rowH), r.Deaths.ToString(), 14, c, TextAnchor.MiddleRight);
                Ui.Text(new Rect(x + w * cols[3], y, w * 0.13f, rowH), Mathf.RoundToInt(r.Dealt).ToString(), 14, c, TextAnchor.MiddleRight);
                Ui.Text(new Rect(x + w * cols[4], y, w * 0.15f, rowH), Mathf.RoundToInt(r.Healed).ToString(), 14, c, TextAnchor.MiddleRight);
                y += rowH;
            }

            float bw = (w - 12) / 2, by = p.yMax - 24 - 52;
            if (Ui.Button(new Rect(x, by, bw, 52), "REMATCH", true, 20)) StartMatch();
            if (Ui.Button(new Rect(x + bw + 12, by, bw, 52), "CHANGE HERO", false, 18)) ToMenu();
        }
    }
}
