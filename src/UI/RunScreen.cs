using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>
/// A run in progress: reads the keys and mouse, steps the run at 60 Hz,
/// and draws the HUD (health, weapon, grenades, abilities, objective,
/// alarm, messages, the arrow to the objective, interaction prompts).
/// </summary>
public partial class RunScreen : Control
{
    /// <summary>How wide a HUD string is drawn, at the player's text size.</summary>
    /// <summary>A string's width in a HUD font; remembered, since the HUD measures the same few strings every frame.</summary>
    public static float TextW(string t, int size, bool heading = false, bool mono = false)
    {
        Font f = mono ? UITheme.Mono : heading ? UITheme.Heading : UITheme.Body;
        int px = UITheme.Scaled(size);
        if (_widths.TryGetValue((t, px, f), out float w)) return w;
        if (_widths.Count > 512) _widths.Clear();
        return _widths[(t, px, f)] = f.GetStringSize(t, HorizontalAlignment.Left, -1, px).X;
    }

    private static readonly System.Collections.Generic.Dictionary<(string, int, Font), float> _widths = new();

    /// <summary>The run's prompts name the default keys ("Hold E", "Shift+E"); show the ones actually bound.</summary>
    public static string Keyed(string prompt)
    {
        if (prompt == null) return null;
        string e = Controls.KeyName(Act.Interact), sh = Controls.KeyName(Act.Sneak);
        // The same prompt, frame after frame: rewrite it only when it or the keys change.
        if (prompt == _keyedFrom && e == _keyedE && sh == _keyedSh) return _keyed;
        _keyedFrom = prompt; _keyedE = e; _keyedSh = sh;
        return _keyed = System.Text.RegularExpressions.Regex.Replace(prompt.Replace("Shift+", sh + "+"), @"\bE\b(?=[:\s,)]|$)", e);
    }

    private static string _keyedFrom, _keyedE, _keyedSh, _keyed;



    public Root Root;
    public Run R;
    public RunView View;
    private HudLayer _hud;
    private bool _paused, _ended;
    /// <summary>The pause menu is up (not the debrief that follows the run, which is still play).</summary>
    public bool IsPaused => _paused && DebriefScreen.Open == 0;
    private float _endT;
    // Edge-triggered input collected between physics steps.
    private bool _dash, _reload, _medkit, _q, _f, _g, _throwRelease, _ePressed, _whistle, _rig;
    private int _slot = -1, _cycle;
    private readonly List<(string text, float t, Color c)> _log = new();
    private float _chipsBottom = 64;
    public IEnumerable<string> Log => _log.Select(l => l.text);
    public bool BigMap;
    private string _hint;
    private float _hintT, _stepT;
    private ulong _slowUntil;
    private readonly List<(int id, float t)> _spotted = new();

    private bool OnScreen(V2 p)
    {
        Vector2 s = View.GetGlobalTransformWithCanvas() * Art.G(p, RunView.T);
        return GetViewportRect().HasPoint(s);
    }
    private bool _padAiming, _padSneak, _ltHeld;
    private V2 _padAim = new(1, 0);

    public override void _Ready()
    {
        UITheme.Fill(this);
        Theme = UITheme.Theme;
        MouseFilter = MouseFilterEnum.Ignore;
        _hud = new HudLayer { S = this, Name = "RunHud" };
        UITheme.Fill(_hud);
        AddChild(_hud);
        Input.MouseMode = Root.RunMouse;
        if (Profile.Scratch && DisplayServer.GetName() == "headless") Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _ExitTree() => Input.MouseMode = Input.MouseModeEnum.Visible;

    public override void _UnhandledInput(InputEvent e)
    {
        if (_paused || _ended) return;
        if (e is InputEventMouseMotion) _padAiming = false;
        bool hit = true;
        if (Controls.Is(e, Act.Dash)) _dash = true;
        else if (Controls.Is(e, Act.Reload)) _reload = true;
        else if (Controls.Is(e, Act.Medkit)) _medkit = true;
        else if (Controls.Is(e, Act.AbilityQ)) _q = true;
        else if (Controls.Is(e, Act.AbilityF)) _f = true;
        else if (Controls.Is(e, Act.Grenade)) _g = true;
        else if (Controls.Is(e, Act.Interact)) _ePressed = true;
        else if (Controls.Is(e, Act.Whistle)) _whistle = true;
        else if (Controls.Is(e, Act.Map)) BigMap = !BigMap;
        else if (Controls.Is(e, Act.Rig)) _rig = true;
        else if (Controls.Is(e, Act.Weapon1)) _slot = 0;
        else if (Controls.Is(e, Act.Weapon2)) _slot = 1;
        else if (Controls.Is(e, Act.Weapon3)) _slot = 2;
        else if (e is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.LeftStick }) _padSneak = !_padSneak;
        else if (Controls.Is(e, Act.Pause)) Pause();
        else hit = false;
        if (hit) GetViewport().SetInputAsHandled();
        else if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right && !mb.Pressed) _throwRelease = true;
            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp) _cycle = -1;
            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown) _cycle = 1;
        }
    }

    /// <summary>The pause menu, unless it is already up or the run is over.</summary>
    public void PauseIfPlaying()
    {
        if (_paused || _ended || R == null || R.Phase != RunPhase.Playing || Modal.Open > 0) return;
        Pause();
    }

    private void Pause()
    {
        _paused = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        var m = new Modal("PAUSED", 440);
        m.Add(UITheme.Label(R.ObjectiveText(), 17, UITheme.Text));
        m.Add(UITheme.Button("Resume", () => m.Close(), 0, 44));
        m.Add(UITheme.Button("Settings", () => Settings.Open(m), 0, 44));
        m.Add(UITheme.Button("Bail out (job fails)", () =>
        {
            // Close (not QueueFree) so the run unpauses and goes on to the debrief.
            m.Close();
            R.Aborted = true;
            R.Phase = RunPhase.Extracted;
        }, 0, 44, "Leave the site now. The job fails; what you found is kept."));
        m.Closed = () => { _paused = false; Input.MouseMode = Root.RunMouse; };
        AddChild(m);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_paused || R == null || !AgentRunner.Running) return;
        float dt = (float)delta;
        if (!_ended)
        {
            var inp = new RunInput { Slot = _slot, Cycle = _cycle };
            if (Controls.Down(Act.Up)) inp.Move.Y -= 1;
            if (Controls.Down(Act.Down)) inp.Move.Y += 1;
            if (Controls.Down(Act.Left)) inp.Move.X -= 1;
            if (Controls.Down(Act.Right)) inp.Move.X += 1;
            inp.Move += Controls.Stick(true);
            inp.Sneak = Controls.Down(Act.Sneak) || _padSneak;
            inp.Fire = Controls.Mouse(MouseButton.Left) || Controls.Trigger(true) > 0.5f;
            bool lt = Controls.Trigger(false) > 0.5f;
            if (_ltHeld && !lt) _throwRelease = true;
            _ltHeld = lt;
            inp.ThrowHeld = Controls.Mouse(MouseButton.Right) || lt;
            inp.ThrowRelease = _throwRelease;
            inp.Interact = Controls.Down(Act.Interact);
            inp.InteractPressed = _ePressed; inp.Whistle = _whistle; inp.Rig = _rig;
            inp.Dash = _dash; inp.Reload = _reload; inp.Medkit = _medkit; inp.AbilityQ = _q; inp.AbilityF = _f; inp.NextGrenade = _g;
            // Twin-stick: the right stick aims six tiles out; touching the mouse takes over again.
            V2 rs = Controls.Stick(false);
            if (rs != V2.Zero) { _padAim = M.Norm(rs); _padAiming = true; }
            inp.Aim = _padAiming ? (R.Rigging && R.Drone != null ? R.Drone.Pos : R.Player.Pos) + _padAim * 6 : View.MouseWorld();
            View.Aim = inp.Aim;
            View.ShowArc = inp.ThrowHeld;
            _dash = _reload = _medkit = _q = _f = _g = _throwRelease = _ePressed = _whistle = _rig = false;
            _slot = -1;
            _cycle = 0;
            using (Perf.Time("run.step")) R.Step(inp);
        }
        foreach (RunEvent e in R.Events)
        {
            if (e.Kind == Ev.Hit && e.A == R.Player.Id && e.Value > 0) ScreenFx.Instance?.Jolt(0.25f + e.Value / R.Player.MaxHp * 3);
            if (e.Kind == Ev.Hint) { _hint = e.Text; _hintT = 12; Sfx.Instance?.Play(Sound.Beep, 1.3f, 0.5f); }
            if (e.Kind == Ev.Alert && R.ActorById(e.A) is Actor al && !OnScreen(al.Pos)) _spotted.Add((al.Id, 2.5f));
            if (e.Kind == Ev.Death && R.ActorById(e.A) is Actor dead && (dead == R.Boss || dead == R.Vip || R.Job.Type == JobType.Clear && R.HostilesLeft == 0 && dead.Team == 1))
                _slowUntil = Time.GetTicksMsec() + 700;
            if (e.Kind is Ev.Message or Ev.Alarm or Ev.Objective or Ev.Wave && e.Text != null)
                _log.Add((e.Text, 7f, e.Kind == Ev.Alarm ? UITheme.Bad : e.Kind == Ev.Objective ? UITheme.Good : UITheme.Text));
        }
        if (R.Glitching) ScreenFx.Instance?.Interfere(0.9f);
        else if (R.Rigging) ScreenFx.Instance?.Interfere(0.35f);
        else if (R.Alarm) ScreenFx.Instance?.Interfere(0.15f);
        using (Perf.Time("run.view.frame")) View.Frame(dt);
        R.Events.Clear();
        _hintT -= dt;
        for (int i = _spotted.Count - 1; i >= 0; i--) { var sp = _spotted[i]; sp.t -= dt; if (sp.t <= 0) _spotted.RemoveAt(i); else _spotted[i] = sp; }
        // A beat of slow motion for the big kills.
        if (R.Phase == RunPhase.Playing) Engine.TimeScale = Time.GetTicksMsec() < _slowUntil ? 0.3 : 1;
        // Footsteps.
        if (!R.Player.Dead && R.Player.Vel.Length() > 1.5f && !R.Sneaking && R.Matrix == null && (_stepT -= dt) <= 0)
        {
            _stepT = 0.33f;
            Sfx.Instance?.Play(Sound.Step, 1, 0.25f);
        }
        for (int i = _log.Count - 1; i >= 0; i--) { var l = _log[i]; l.t -= dt; if (l.t <= 0) _log.RemoveAt(i); else _log[i] = l; }
        if (_log.Count > 6) _log.RemoveRange(0, _log.Count - 6);

        if (Music.Instance != null)
            Music.Instance.Intensity = R.Alarm ? 1 : R.Actors.Any(a => a.Hostile && a.Alert && a.Def != null) ? 0.75f : R.Security > 0 ? 0.4f : 0.05f;

        if (R.Phase != RunPhase.Playing && !_ended)
        {
            _ended = true;
            _endT = R.Phase == RunPhase.Dead ? 2.2f : 0.8f;
            if (R.Phase == RunPhase.Dead) Engine.TimeScale = 0.4f;
        }
        if (_ended)
        {
            _endT -= dt / (float)Math.Max(0.1, Engine.TimeScale);
            if (_endT <= 0)
            {
                Engine.TimeScale = 1;
                _ended = false;
                _paused = true;
                Input.MouseMode = Input.MouseModeEnum.Visible;
                Root.EndRun(R);
            }
        }
        _hudDirty = true;
    }

    private Run _chipsFor;
    private readonly System.Collections.Generic.List<(string name, string what, Color c)> _chips = new();

    /// <summary>The run's conditions as chips (the weather, the job's mods, the district, the site's rule, legwork): fixed for the run, so made once.</summary>
    private System.Collections.Generic.List<(string name, string what, Color c)> Chips(Run r)
    {
        if (_chipsFor == r) return _chips;
        _chipsFor = r;
        var chips = _chips;
        chips.Clear();
        if (r.C.Weather != Career.Sky.Clear)
            chips.Add((r.C.Weather.ToString().ToUpper(), r.C.Weather == Career.Sky.Fog ? "everyone sees less" : "masks footsteps, shorter sight", UITheme.Cyan));
        foreach (RunMod m in r.Job.Mods)
            chips.Add((Tables.RunMods.First(x => x.mod == m).name.ToUpper(), m switch { RunMod.Blackout => "no cameras, dark", RunMod.ShiftChange => "double patrols", RunMod.Deadline => "on the clock", RunMod.Elite => "tougher guards", _ => "fewer guards" }, m is RunMod.Skeleton or RunMod.Blackout ? UITheme.Good : UITheme.Bad));
        if (r.C.On("grid", r.Job.District) && !r.Job.Has(RunMod.Blackout)) chips.Add(("GRID DOWN", "the district is dark", UITheme.Good));
        if (r.StyleRule is var (sname, swhat)) chips.Add((sname, swhat, UITheme.Amber));
        foreach (string lw in r.Job.Legwork) chips.Add(("LEGWORK", Career.LegworkName(lw), UITheme.Good));
        return chips;
    }

    private bool _hudDirty;

    /// <summary>The HUD repaints once a rendered frame, however many physics steps ran.</summary>
    public override void _Process(double delta)
    {
        if (!_hudDirty) return;
        _hudDirty = false;
        _hud.QueueRedraw();
    }

    // ---- HUD ----------------------------------------------------------------------------

    private partial class HudLayer : Control
    {
        public RunScreen S;

        public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

        /// <summary>The prompt's width in its font (remembered: the same prompt shows for many frames).</summary>
        private static float PromptW(string t) => TextW(t, 17, heading: true);

        public override void _Draw()
        {
            using var _perf = Perf.Time("run.draw.hud");
            Run r = S.R;
            if (r == null) return;
            Vector2 size = GetViewportRect().Size;
            Actor p = r.Player;

            // Screen tints: overclock, cloak, low health, flashbang.
            if (r.OverclockT > 0) DrawRect(new Rect2(Vector2.Zero, size), new Color(0.1f, 0.4f, 0.6f, 0.12f));
            if (r.CloakT > 0) DrawRect(new Rect2(Vector2.Zero, size), new Color(0.5f, 0.6f, 0.8f, 0.08f));
            float hurt = 1 - p.Hp / p.MaxHp;
            if (hurt > 0.5f || p.HitFlash > 0)
            {
                float a = MathF.Max((hurt - 0.5f) * 0.6f, p.HitFlash * 2);
                for (int k = 0; k < 6; k++)
                    DrawRect(new Rect2(k * 12, k * 12, size.X - k * 24, size.Y - k * 24), new Color(0.8f, 0, 0.05f, a * 0.22f), false, 12);
            }
            if (r.Glitching)
                for (int k = 0; k < 5; k++)
                {
                    float gy = (Art.Hash(k, (int)(r.Time * 20)) * size.Y);
                    DrawRect(new Rect2(0, gy, size.X, 2 + Art.Hash(k, 3) * 4), (k % 2 == 0 ? UITheme.Magenta : UITheme.Cyan) with { A = 0.12f });
                }
            if (r.FlashT > 0) DrawRect(new Rect2(Vector2.Zero, size), new Color(1, 1, 1, MathF.Min(1, r.FlashT * 1.4f)));

            // Health.
            var hb = new Rect2(24, size.Y - 74, 320, 22);
            Panel(new Rect2(14, size.Y - 112, 420, 98));
            // Laid out from the text's measured widths, so a larger text size pushes things along instead of piling them up.
            UITheme.Write(this, new Vector2(24, size.Y - 84), UITheme.Caps(r.C.Name), 18, UITheme.Cyan, heading: true);
            float nameEnd = 24 + TextW(UITheme.Caps(r.C.Name), 18, true) + 14;
            float hp = p.Hp / p.MaxHp;
            UITheme.DrawSegBar(this, hb, hp, hp > 0.5f ? UITheme.Good : hp > 0.25f ? UITheme.Yellow : UITheme.Bad, 24);
            DrawRect(hb, UITheme.PanelEdge, false, 1);
            // The figure on a dark chip at the left end, where the cells cannot swallow it.
            string hpText = $"{p.Hp:0} / {p.MaxHp:0}";
            DrawRect(new Rect2(hb.Position + new Vector2(2, 2), new Vector2(MathF.Min(hb.Size.X - 4, TextW(hpText, 15, mono: true) + 14), hb.Size.Y - 4)), new Color(0.02f, 0.018f, 0.015f, 0.92f));
            UITheme.Write(this, hb.Position + new Vector2(8, 17), hpText, 15, UITheme.Text, mono: true);
            UITheme.Write(this, new Vector2(354, size.Y - 57), $"ARM {r.C.ArmorValue}", 15, UITheme.Text);
            // Edge: one diamond a point, the hollow ones spent -- after the name.
            UITheme.Write(this, new Vector2(nameEnd, size.Y - 84), "EDGE", 12, UITheme.Dim, heading: true);
            float edgeX = nameEnd + TextW("EDGE", 12, true) + 10;
            for (int k = 0; k < r.Edge + r.EdgeUsed; k++)
            {
                Vector2 ec = new(edgeX + k * 12, size.Y - 89);
                var dia = new[] { ec + new Vector2(0, -5), ec + new Vector2(5, 0), ec + new Vector2(0, 5), ec + new Vector2(-5, 0) };
                if (k < r.Edge) DrawColoredPolygon(dia, UITheme.Amber);
                else DrawPolyline(new[] { dia[0], dia[1], dia[2], dia[3], dia[0] }, UITheme.Faint, 1.2f);
            }
            // How lit you stand: the dark hides you, a lamp gives you away.
            float lit = r.L.LightAt(p.Pos);
            string light = lit > 0.55f ? "LIT" : lit > 0.15f ? "DIM" : "DARK";
            UITheme.Write(this, new Vector2(edgeX + (r.Edge + r.EdgeUsed) * 12 + 10, size.Y - 84), light, 12, lit > 0.55f ? UITheme.Yellow : lit > 0.15f ? UITheme.Dim : UITheme.Cyan, heading: true);
            string med = $"[{Controls.KeyName(Act.Medkit)}] Medkits {r.Medkits}", dashT = $"[{Controls.KeyName(Act.Dash)}] Dash";
            UITheme.Write(this, new Vector2(24, size.Y - 28), med, 15, r.Medkits > 0 ? UITheme.Good : UITheme.Faint);
            float dash = r.DashCd > 0 ? 1 - r.DashCd / r.C.DashCooldown : 1;
            float dashX = 24 + TextW(med, 15) + 22;
            UITheme.Write(this, new Vector2(dashX, size.Y - 28), dashT, 15, dash >= 1 ? UITheme.Cyan : UITheme.Faint);
            DrawRect(new Rect2(dashX, size.Y - 22, TextW(dashT, 15) * dash, 3), UITheme.Cyan);
            // Whistle (and the drone) at the panel's right edge, out of the name's way.
            UITheme.Write(this, new Vector2(14, size.Y - 84), $"[{Controls.KeyName(Act.Whistle)}] whistle", 13, r.WhistleCd > 0 ? UITheme.Faint : UITheme.Dim, HorizontalAlignment.Right, 410);
            if (r.CashFound > 0) UITheme.Write(this, new Vector2(dashX + TextW(dashT, 15) + 22, size.Y - 28), $"+{M.Money(r.CashFound)}", 15, UITheme.Yellow);

            V2? objective = r.ObjectivePos();
            if (r.Peaceful)
            {
                Minimap(r, size, objective);
                if (r.Prompt != null)
                {
                    float pw = MathF.Max(440, PromptW(Keyed(r.Prompt)) + 40);
                    var pr = new Rect2(size.X / 2 - pw / 2, size.Y / 2 + 70, pw, 40);
                    Panel(pr);
                    UITheme.Write(this, pr.Position + new Vector2(0, 26), Keyed(r.Prompt), 17, UITheme.Cyan, HorizontalAlignment.Center, pr.Size.X, heading: true);
                }
                var ob2 = new Rect2(size.X / 2 - 330, 14, 660, 64);
                Panel(ob2);
                UITheme.Write(this, ob2.Position + new Vector2(0, 26), r.ObjectiveText(), 18, UITheme.Text, HorizontalAlignment.Center, ob2.Size.X, heading: true);
                UITheme.Write(this, ob2.Position + new Vector2(0, 52), $"{M.Money(r.C.Cash)}   ·   heat {r.C.Heat:0.0}", 14, UITheme.Yellow, HorizontalAlignment.Center, ob2.Size.X);
                float ly = 104;
                foreach (var (text, t, c) in S._log) { UITheme.Write(this, new Vector2(24, ly), text, 16, c with { A = MathF.Min(1, t) }); ly += 22; }
                return;
            }
            // Weapons.
            Panel(new Rect2(size.X - 434, size.Y - 112, 420, 98));
            WeaponDef w = r.CurrentWeapon;
            Gun g = r.CurrentGun;
            UITheme.Write(this, new Vector2(size.X - 424, size.Y - 84), w?.Name ?? "--", 18, UITheme.Text, heading: true);
            if (g != null)
            {
                string ammo = g.Reload > 0 ? "RELOADING" : $"{g.Ammo} / {g.Mag}";
                UITheme.Write(this, new Vector2(size.X - 240, size.Y - 84), ammo, 20, g.Ammo == 0 ? UITheme.Bad : UITheme.Yellow, HorizontalAlignment.Right, 220, heading: true);
                if (g.Reload > 0) DrawRect(new Rect2(size.X - 424, size.Y - 74, 400 * (1 - g.Reload / (g.Def.Reload * r.C.ReloadMul(g.Def))), 4), UITheme.Yellow);
                else for (int k = 0; k < Math.Min(g.Mag, 40); k++)
                {
                    bool full = k < g.Ammo * Math.Min(g.Mag, 40) / g.Mag;
                    DrawRect(new Rect2(size.X - 424 + k * 10, size.Y - 74, 6, 8), full ? UITheme.Yellow : UITheme.Faint.Darkened(0.4f));
                }
            }
            string slots = $"[{Controls.KeyName(Act.Weapon1)}] {(r.Guns[0]?.Def.Name ?? "--")}   [{Controls.KeyName(Act.Weapon2)}] {(r.Guns[1]?.Def.Name ?? "--")}   [{Controls.KeyName(Act.Weapon3)}] {r.MeleeDef.Name}";
            UITheme.Write(this, new Vector2(size.X - 424, size.Y - 48), slots, 13, UITheme.Dim);
            int n = r.Nades.GetValueOrDefault(r.NadeSel);
            string nade = n > 0 || r.Nades.Values.Any(v => v > 0) ? $"[RMB] {Tables.Grenade(r.NadeSel).Name} x{n}   [{Controls.KeyName(Act.Grenade)}] next" : "No grenades";
            UITheme.Write(this, new Vector2(size.X - 424, size.Y - 24), nade, 15, n > 0 ? UITheme.Good : UITheme.Faint);

            // Abilities.
            float ax = size.X / 2 - 150;
            foreach (var (key, ab) in new[] { (Controls.KeyName(Act.AbilityQ), r.C.SlotQ), (Controls.KeyName(Act.AbilityF), r.C.SlotF) })
            {
                if (ab == Ability.None) { ax += 150; continue; }
                var box = new Rect2(ax, size.Y - 74, 140, 56);
                Panel(box);
                float cd = r.AbilityCd.GetValueOrDefault(ab);
                float max = r.C.Cooldown(ab);
                bool active = ab == Ability.Overclock && r.OverclockT > 0 || ab == Ability.Cloak && r.CloakT > 0;
                if (cd > 0) DrawRect(new Rect2(box.Position, new Vector2(box.Size.X * (1 - cd / max), box.Size.Y)), UITheme.Cyan with { A = 0.12f });
                UITheme.Write(this, box.Position + new Vector2(10, 24), $"[{key}] {Career.AbilityName(ab)}", 16, active ? UITheme.Magenta : cd > 0 ? UITheme.Faint : UITheme.Cyan, heading: true);
                UITheme.Write(this, box.Position + new Vector2(10, 46), active ? "ACTIVE" : cd > 0 ? $"{cd:0.0}s" : "ready", 14, UITheme.Dim);
                ax += 150;
            }

            // Objective and alarm.
            var ob = new Rect2(size.X / 2 - 330, 14, 660, r.Peaceful ? 64 : 84);
            if (r.Alarm) UITheme.DrawPanel(this, ob, UITheme.Bad with { A = 0.8f }, 0.84f, hazard: true);
            else Panel(ob);
            UITheme.Write(this, ob.Position + new Vector2(0, 26), r.ObjectiveText() + (r.FloorNote(objective) is string fn ? "  " + fn : ""), 18, r.ObjectiveDone ? UITheme.Good : r.ObjectiveLost ? UITheme.Bad : UITheme.Text, HorizontalAlignment.Center, ob.Size.X, heading: true);
            if (r.Peaceful) UITheme.Write(this, ob.Position + new Vector2(0, 52), "ON FOOT -- weapons holstered", 14, UITheme.Cyan, HorizontalAlignment.Center, ob.Size.X);
            else
            {
                // The security ladder: six cells, the current level named, and what the next one brings.
                int lv = r.SecurityLevel;
                Color lc = lv >= 3 ? UITheme.Bad : lv >= 1 ? UITheme.Yellow : UITheme.Good.Darkened(0.2f);
                float cw = 22, gap = 3, x0 = ob.Position.X + 16, y0 = ob.Position.Y + 42;
                for (int k = 0; k < 6; k++)
                {
                    var cell = new Rect2(x0 + k * (cw + gap), y0, cw, 12);
                    float fill = k < lv ? 1 : k == lv ? r.SecurityTick : 0;
                    DrawRect(cell, new Color(0, 0, 0, 0.5f));
                    if (fill > 0) DrawRect(new Rect2(cell.Position, new Vector2(cw * fill, 12)), (k >= 2 ? UITheme.Bad : UITheme.Yellow) with { A = k < lv ? 1 : 0.6f });
                    DrawRect(cell, UITheme.PanelEdge, false, 1);
                }
                string now = r.Levels[lv].name;
                string next = lv < 6 ? $"next: {r.Levels[lv + 1].name.ToLower()} -- {r.Levels[lv + 1].does}" : r.Levels[6].does;
                string why = r.CloakT > 0 ? "CLOAKED" : r.CoolingOff ? "lost them -- cooling off" : r.SecurityTick > 0 && !r.CoolingOff && lv < 3 && r.AlarmMeter > 0 ? "being called in -- silence them" : null;
                bool flash = lv >= 3 && MathF.Sin(r.Time * 8) > 0;
                UITheme.Write(this, new Vector2(x0 + 6 * (cw + gap) + 8, y0 + 11), $"SECURITY {lv}  {now}" + (why != null ? $"  ·  {why}" : ""), 13, flash ? lc.Darkened(0.3f) : lc, heading: true);
                UITheme.Write(this, new Vector2(x0, y0 + 30), next, 12, UITheme.Dim);
            }
            if (r.Deadline > 0)
            {
                float tl = r.TimeLeft;
                UITheme.Write(this, ob.Position + new Vector2(0, ob.Size.Y + 24), tl > 0 ? $"DEADLINE  {TimeSpan.FromSeconds(tl):m\\:ss}" : "DEADLINE PASSED", 18, tl < 30 && MathF.Sin(r.Time * 8) > 0 ? UITheme.Bad : UITheme.Yellow, HorizontalAlignment.Center, ob.Size.X, heading: true);
            }
            // Conditions as chips: what they are, and in a few words what they do.
            var chips = S.Chips(r);
            float cx = 24, cy = 64;
            foreach (var (name, what, cc) in chips)
            {
                float cw = 18 + name.Length * 9 + what.Length * 6.2f;
                // Wrap before the objective panel in the middle.
                if (cx > 24 && cx + cw > 460) { cx = 24; cy += 28; }
                UITheme.DrawPanel(this, new Rect2(cx, cy, cw, 24), cc with { A = 0.6f }, 0.85f);
                UITheme.Write(this, new Vector2(cx + 8, cy + 18), name, 13, cc, heading: true);
                UITheme.Write(this, new Vector2(cx + 14 + name.Length * 9, cy + 18), what, 12, UITheme.Dim);
                cx += cw + 6;
            }
            S._chipsBottom = chips.Count > 0 ? cy + 34 : 64;
            string cond = chips.Count > 0 ? "x" : "";
            if (r.Rigging && r.Drone != null)
            {
                DrawRect(new Rect2(Vector2.Zero, size), new Color(0.2f, 1f, 0.6f, 0.05f));
                UITheme.Write(this, new Vector2(0, size.Y / 2 - 200), $"RIGGING: {UITheme.Caps(r.Drone.Name)}  ·  hull {r.Drone.Hp:0}/{r.Drone.MaxHp:0}  ·  signal {r.DroneSignal * 100:0} %  ·  T to come back", 17, r.DroneSignal < 0.2f ? UITheme.Bad : UITheme.Term, HorizontalAlignment.Center, size.X, heading: true);
            }
            else if (r.Drone != null && !r.Drone.Dead) UITheme.Write(this, new Vector2(14, size.Y - 100), $"[{Controls.KeyName(Act.Rig)}] drone", 13, UITheme.Term, HorizontalAlignment.Right, 410);
            if (r.FloorLabel is string fl && !r.Plan.Tower) UITheme.Write(this, new Vector2(24, 76 + (cond.Length > 0 ? 20 : 0)), fl, 16, UITheme.Yellow, heading: true);
            if (r.Pending is RunChoice rc)
            {
                var box = new Rect2(size.X / 2 - 330, size.Y / 2 - 90, 660, 180);
                UITheme.DrawPanel(this, box, UITheme.Amber, 0.95f);
                UITheme.Write(this, box.Position + new Vector2(20, 36), "A DECISION", 20, UITheme.Amber, heading: true);
                // Wrap by hand: two lines of text, then the options.
                string t = rc.Text;
                int cut = t.Length > 80 ? t.LastIndexOf(' ', 80) : t.Length;
                UITheme.Write(this, box.Position + new Vector2(20, 66), t[..cut], 16, UITheme.Text);
                if (cut < t.Length) UITheme.Write(this, box.Position + new Vector2(20, 88), t[cut..].Trim(), 16, UITheme.Text);
                for (int k = 0; k < rc.Options.Length; k++) UITheme.Write(this, box.Position + new Vector2(20, 124 + k * 24), $"[{k + 1}]  {rc.Options[k]}", 17, UITheme.Cyan, heading: true);
            }
            if (r.LiftTo >= 0) UITheme.Write(this, new Vector2(0, size.Y / 2 - 160), $"ELEVATOR  ·  {r.Plan.FloorName(r.LiftTo)}", 22, UITheme.Cyan, HorizontalAlignment.Center, size.X, heading: true);
            if (r.CarriedBody != null) UITheme.Write(this, new Vector2(0, size.Y / 2 + 130), "CARRYING A BODY -- slow, no weapons", 16, UITheme.Yellow, HorizontalAlignment.Center, size.X, heading: true);
            UITheme.Write(this, new Vector2(size.X - 150, 40), TimeSpan.FromSeconds(r.Time).ToString(@"mm\:ss"), 18, UITheme.Dim, HorizontalAlignment.Right, 130);

            // Messages, under the chips.
            float my = MathF.Max(104, S._chipsBottom + 20);
            foreach (var (text, t, c) in S._log)
            {
                UITheme.Write(this, new Vector2(24, my), text, 16, c with { A = MathF.Min(1, t) });
                my += 22;
            }

            // Prompt.
            if (r.Prompt != null && r.Phase == RunPhase.Playing)
            {
                float pw = MathF.Max(440, PromptW(Keyed(r.Prompt)) + 40);
                var pr = new Rect2(size.X / 2 - pw / 2, size.Y - 168, pw, 40);
                Panel(pr);
                UITheme.Write(this, pr.Position + new Vector2(0, 26), Keyed(r.Prompt), 17, UITheme.Cyan, HorizontalAlignment.Center, pr.Size.X, heading: true);
                if (r.PromptProgress >= 0) DrawRect(new Rect2(pr.Position.X, pr.End.Y - 4, pr.Size.X * MathF.Min(1, r.PromptProgress), 4), UITheme.Cyan);
            }

            Minimap(r, size, objective);
            // Someone off screen has seen you.
            foreach (var (id, t) in S._spotted)
            {
                Actor a = r.ActorById(id);
                if (a == null || a.Dead) continue;
                Vector2 sp = S.View.GetGlobalTransformWithCanvas() * Art.G(a.Pos, RunView.T);
                Vector2 c = size / 2;
                Vector2 d = (sp - c).Normalized();
                float tx = d.X != 0 ? (size.X / 2 - 50) / MathF.Abs(d.X) : float.MaxValue, ty = d.Y != 0 ? (size.Y / 2 - 130) / MathF.Abs(d.Y) : float.MaxValue;
                Vector2 at = c + d * MathF.Min(tx, ty);
                DrawCircle(at, 16, UITheme.Bad with { A = MathF.Min(1, t) * 0.8f });
                UITheme.Write(this, at + new Vector2(-5, 8), "!", 24, Colors.White with { A = MathF.Min(1, t) }, heading: true);
            }
            if (S._hintT > 0 && S._hint != null && r.Matrix == null)
            {
                var tb = new Rect2(size.X / 2 - 420, 128, 840, 58);
                Panel(tb);
                DrawRect(tb, UITheme.Yellow with { A = MathF.Min(1, S._hintT) * 0.8f }, false, 2);
                UITheme.Write(this, tb.Position + new Vector2(14, 22), "TUTORIAL", 13, UITheme.Yellow, heading: true);
                UITheme.Write(this, tb.Position + new Vector2(14, 44), S._hint, 15, UITheme.Text with { A = MathF.Min(1, S._hintT) }, HorizontalAlignment.Left, 812);
            }
            if (r.Boss != null && !r.Boss.Dead && r.Boss.Alert && r.Shown(r.Boss))
            {
                var bb = new Rect2(size.X / 2 - 260, 104, 520, 16);
                UITheme.DrawSegBar(this, bb, r.Boss.Hp / r.Boss.MaxHp, UITheme.Yellow, 30);
                DrawRect(bb, UITheme.Yellow, false, 1);
                UITheme.Write(this, bb.Position + new Vector2(0, -4), UITheme.Caps(r.Boss.Name), 15, UITheme.Yellow, HorizontalAlignment.Center, bb.Size.X, heading: true);
            }

            // Arrow to the objective when it is off screen.
            V2? goal = r.NavTarget(objective);
            if (goal is V2 gpos) EdgeArrow(gpos, r.ObjectiveDone || r.ObjectiveLost ? UITheme.Good : UITheme.Magenta, size);

            if (r.Matrix != null) DrawMatrix(r, r.Matrix, size, r.Time);

            if (r.Phase == RunPhase.Dead)
                UITheme.Write(this, new Vector2(0, size.Y / 2), "FLATLINED", 72, UITheme.Bad, HorizontalAlignment.Center, size.X, heading: true);
            else if (r.Phase == RunPhase.Extracted)
                UITheme.Write(this, new Vector2(0, size.Y / 2), r.Success ? "EXTRACTED" : "GETTING OUT", 64, r.Success ? UITheme.Good : UITheme.Yellow, HorizontalAlignment.Center, size.X, heading: true);
        }

        /// <summary>The Matrix: the host's nodes and data lines over the room your body is sitting in, the trace across the top, the deck along the bottom.</summary>
        private void DrawMatrix(Run r, MatrixRun m, Vector2 size, float t)
        {
            DrawRect(new Rect2(Vector2.Zero, size), new Color(0, 0.04f, 0.02f, 0.85f));
            for (int y = 0; y < (int)size.Y; y += 3) DrawLine(new Vector2(0, y), new Vector2(size.X, y), new Color(0.1f, 1f, 0.5f, 0.03f), 1);
            int decking = r.C.S(Skill.Decking);

            // ---- the trace and the host, across the top ----
            var top = new Rect2(120, 40, size.X - 240, 74);
            UITheme.DrawPanel(this, top, m.Alert ? UITheme.Bad : UITheme.Term with { A = 0.7f }, 0.8f, hazard: m.Alert);
            string host = $"HOST RATING {m.Rating}  ·  {(m.Alert ? "ALERT" : "CALM")}{(m.SpiderIn ? $"  ·  {m.Ice.Count(i => i.Kind == IcKind.Spider)} SPIDER{(m.Ice.Count(i => i.Kind == IcKind.Spider) > 1 ? "S" : "")} IN THE SYSTEM" : "")}{(m.CoreDown ? "  ·  CORE CRASHED" : "")}";
            UITheme.Write(this, top.Position + new Vector2(14, 26), host, 17, m.Alert ? UITheme.Bad : UITheme.Term, heading: true);
            if (m.Locked > 0) UITheme.Write(this, top.Position + new Vector2(0, 26), $"LINK-LOCKED {m.Locked:0.0} s", 17, UITheme.Magenta, HorizontalAlignment.Right, top.Size.X - 14, heading: true);
            Color tc = m.Trace < 0.5f ? UITheme.Term : m.Trace < 0.8f ? UITheme.Yellow : UITheme.Bad;
            UITheme.DrawSegBar(this, new Rect2(top.Position + new Vector2(14, 38), new Vector2(top.Size.X - 220, 24)), m.Trace, tc, 40);
            UITheme.Write(this, top.Position + new Vector2(top.Size.X - 196, 58), $"TRACE {m.Trace * 100:0} %  +{m.TraceRate * 100:0.0}/s", 15, tc, mono: true);

            // ---- the host ----
            float gx = 160, gy = 170, sx = (size.X - 560) / (MatrixRun.W - 1), sy = (size.Y - 490) / (MatrixRun.H - 1);
            Vector2 At(Pt p) => new(gx + p.X * sx, gy + p.Y * sy);
            Color line = UITheme.Term with { A = 0.5f };
            for (int y = 0; y < MatrixRun.H; y++)
            for (int x = 0; x < MatrixRun.W; x++)
            {
                var p = new Pt(x, y);
                foreach (Pt d in new[] { new Pt(1, 0), new Pt(0, 1) })
                {
                    Pt q = p + d;
                    if (!m.Linked(p, q)) continue;
                    if (!m.Gated(p, q)) { DrawLine(At(p), At(q), line, 2); continue; }
                    // A firewall gate: a red dashed line, and how far it has been cracked.
                    Vector2 a = At(p), b = At(q);
                    for (float f = 0; f < 1; f += 0.1f) DrawLine(a.Lerp(b, f), a.Lerp(b, f + 0.05f), UITheme.Bad, 3);
                    float c = m.CrackOf(p, q);
                    if (c > 0) DrawLine(a, a.Lerp(b, c), UITheme.Yellow, 5);
                    Vector2 mid = a.Lerp(b, 0.5f);
                    DrawRect(new Rect2(mid - new Vector2(7, 7), new Vector2(14, 14)), UITheme.Bad, false, 2);
                }
            }
            for (int y = 0; y < MatrixRun.H; y++)
            for (int x = 0; x < MatrixRun.W; x++)
                DrawCircle(At(new Pt(x, y)), 7, new Color(0.05f, 0.25f, 0.12f));
            DrawArc(At(m.Entry), 18, 0, MathF.Tau, 24, UITheme.Cyan, 2);
            UITheme.Write(this, At(m.Entry) + new Vector2(-40, 40), "JACK OUT", 11, UITheme.Cyan, HorizontalAlignment.Center, 80);
            foreach (MxFile f in m.Files)
            {
                Vector2 c = At(f.At);
                Vector2[] dia = { c + new Vector2(0, -16), c + new Vector2(16, 0), c + new Vector2(0, 16), c + new Vector2(-16, 0) };
                if (f.Objective)
                    DrawColoredPolygon(dia, (f.Taken ? UITheme.Good : UITheme.Yellow) with { A = f.Taken ? 0.4f : 0.9f });
                else
                {
                    Color fc = f.Taken ? UITheme.Faint : UITheme.Amber;
                    DrawPolyline(dia.Append(dia[0]).ToArray(), fc, 2.5f);
                    if (!f.Taken) UITheme.Write(this, c + new Vector2(-50, 34), $"{f.Mp} Mp  {M.Money(f.Value)}{(f.Encrypted ? "  ENC" : "")}", 11, fc, HorizontalAlignment.Center, 100, mono: true);
                }
            }
            foreach (var kv in m.ControlAt)
            {
                Vector2 c = At(new Pt(kv.Key % MatrixRun.W, kv.Key / MatrixRun.W));
                bool got = m.ControlsTaken.Contains(kv.Key);
                Color cc = got ? UITheme.Good : UITheme.Cyan;
                DrawRect(new Rect2(c - new Vector2(13, 13), new Vector2(26, 26)), cc with { A = got ? 0.35f : 0.85f }, false, 3);
                string label = kv.Value switch { MxControl.Cameras => "CAMERAS", MxControl.Maglocks => "MAGLOCKS", MxControl.Turrets => "TURRETS", MxControl.Alarm => "ALARM", _ => "ACCOUNTS" };
                UITheme.Write(this, c + new Vector2(-50, 34), label, 11, cc, HorizontalAlignment.Center, 100, heading: true);
            }
            {
                Vector2 c = At(m.Core);
                var hex = Enumerable.Range(0, 6).Select(k => c + new Vector2(MathF.Cos(k * MathF.Tau / 6), MathF.Sin(k * MathF.Tau / 6)) * 20).ToArray();
                DrawColoredPolygon(hex, (m.CoreDown ? UITheme.Faint : UITheme.Magenta) with { A = m.CoreDown ? 0.3f : 0.55f });
                DrawPolyline(hex.Append(hex[0]).ToArray(), m.CoreDown ? UITheme.Faint : UITheme.Magenta, 2);
                UITheme.Write(this, c + new Vector2(-50, 38), m.CoreDown ? "CORE (DOWN)" : "CORE", 11, UITheme.Magenta, HorizontalAlignment.Center, 100, heading: true);
                if (!m.CoreDown && m.CoreHp < m.CoreMax) DrawRect(new Rect2(c + new Vector2(-20, 24), new Vector2(40 * m.CoreHp / m.CoreMax, 4)), UITheme.Magenta);
            }
            if (m.PlantAt.X >= 0)
            {
                Vector2 c = At(m.PlantAt);
                DrawRect(new Rect2(c - new Vector2(14, 14), new Vector2(28, 28)), (m.Planted ? UITheme.Good : UITheme.Violet) with { A = 0.8f });
                UITheme.Write(this, c + new Vector2(-50, 36), m.Planted ? "BACKDOOR IN" : "ARCHIVE", 11, UITheme.Violet, HorizontalAlignment.Center, 100, heading: true);
            }
            if (m.DecoyT > 0) DrawArc(At(m.Decoy), 14, 0, MathF.Tau, 20, UITheme.Cyan with { A = 0.4f + 0.3f * MathF.Sin(t * 10) }, 3);

            // ---- IC ----
            foreach (Ic ic in m.Ice)
            {
                Vector2 c = At(ic.Pos);
                Color ic0 = IcColour(ic.Kind);
                float pulse = 1 + 0.12f * MathF.Sin(t * 10 + ic.Pos.X);
                if (decking >= 3 && ic.Next != ic.Pos) DrawDashedLine(c, At(ic.Next), ic0 with { A = 0.6f }, 2, 6);
                if (ic.Kind == IcKind.Spider)
                {
                    DrawCircle(c, 17 * pulse, new Color(0.1f, 0, 0.06f));
                    DrawArc(c, 17 * pulse, 0, MathF.Tau, 24, ic0, 3);
                }
                else
                {
                    Vector2[] tri = { c + new Vector2(0, -19) * pulse, c + new Vector2(17, 13) * pulse, c + new Vector2(-17, 13) * pulse };
                    DrawColoredPolygon(tri, ic.Kind == IcKind.Black ? new Color(0.05f, 0, 0.1f) : ic0);
                    if (ic.Kind == IcKind.Black) DrawPolyline(tri.Append(tri[0]).ToArray(), ic0, 3);
                }
                UITheme.Write(this, c + new Vector2(-12, 8), ic.Def.Glyph.ToString(), 15, ic.Kind is IcKind.Black or IcKind.Spider ? ic0 : new Color(0, 0, 0), HorizontalAlignment.Center, 24, heading: true, outline: 1);
                if (decking >= 3 || ic.Hp < ic.MaxHp) DrawRect(new Rect2(c + new Vector2(-18, 18), new Vector2(36 * MathF.Max(0, ic.Hp) / ic.MaxHp, 4)), ic0);
                if (ic.Report >= 0)
                {
                    UITheme.Write(this, c + new Vector2(-10, -26), "!", 22, UITheme.Yellow, HorizontalAlignment.Center, 20, heading: true);
                    DrawArc(c, 26, -MathF.PI / 2, -MathF.PI / 2 + MathF.Tau * (1 - ic.Report / Tables.MxReport), 24, UITheme.Yellow, 3);
                }
            }

            // ---- you ----
            float slide = Math.Clamp(1 - m.MoveCd / 0.2f, 0, 1);
            Vector2 y0 = At(m.From).Lerp(At(m.You), slide);
            DrawCircle(y0, 14, UITheme.Cyan);
            DrawArc(y0, 20 + MathF.Sin(t * 6) * 3, 0, MathF.Tau, 24, UITheme.Cyan with { A = 0.6f }, 2);
            if (m.HoldT > 0 && m.HoldNeed > 0) DrawArc(y0, 28, -MathF.PI / 2, -MathF.PI / 2 + MathF.Tau * Math.Clamp(m.HoldT / m.HoldNeed, 0, 1), 32, UITheme.Yellow, 4);
            if (m.SpikeTarget is Ic tgt) DrawArc(At(tgt.Pos), 24, 0, MathF.Tau, 24, UITheme.Bad with { A = 0.5f }, 2);

            // ---- the deck, along the bottom ----
            var bot = new Rect2(120, size.Y - 250, size.X - 240, 150);
            UITheme.DrawPanel(this, bot, UITheme.Term with { A = 0.6f }, 0.82f);
            DeckDef deck = r.C.ActiveDeck;
            Vector2 o = bot.Position + new Vector2(14, 24);
            UITheme.Write(this, o, $"{deck.Name.ToUpper()}  ·  A {m.Attack}  S {m.Sleaze}  D {m.Data}  F {m.Firewall}{(m.Firewall < m.S.Mx[3] ? $" (of {m.S.Mx[3]})" : "")}  ·  DECKING {decking}", 15, UITheme.Term, heading: true);
            float half = (bot.Size.X - 42) / 2;
            UITheme.Write(this, o + new Vector2(0, 24), $"CONDITION {m.Boxes - m.Damage}/{m.Boxes}", 13, UITheme.Dim, mono: true);
            UITheme.DrawSegBar(this, new Rect2(o + new Vector2(150, 8), new Vector2(half - 150, 20)), (m.Boxes - m.Damage) / (float)m.Boxes, m.Damage * 3 >= m.Boxes * 2 ? UITheme.Bad : UITheme.Cyan, m.Boxes);
            // Memory: a cell per Mp -- programs violet, files yellow, the rest free.
            int progMp = m.Programs.Sum(p => Tables.Program(p).Mp), mem = Math.Max(1, m.S.Memory);
            UITheme.Write(this, o + new Vector2(half + 14, 24), $"MEMORY {m.Free}/{mem} Mp free", 13, UITheme.Dim, mono: true);
            var mr = new Rect2(o + new Vector2(half + 200, 8), new Vector2(half - 200, 20));
            DrawRect(mr, new Color(0, 0, 0, 0.65f));
            float cw = (mr.Size.X - 2) / mem;
            for (int k = 0; k < mem; k++)
            {
                Color cc = k < progMp ? UITheme.Violet : k < progMp + m.UsedMp ? UITheme.Yellow : UITheme.Term with { A = 0.12f };
                DrawRect(new Rect2(mr.Position + new Vector2(2 + k * cw, 2), new Vector2(cw - 2, mr.Size.Y - 4)), cc);
            }
            var actives = m.Programs.Where(p => Tables.Program(p).Active).Take(3).ToList();
            string keys = string.Join("   ", actives.Select((p, k) => $"[{"QFR"[k]}] {Tables.Program(p).Name}"));
            string passive = string.Join(", ", m.Programs.Where(p => !Tables.Program(p).Active).Select(p => Tables.Program(p).Name));
            UITheme.Write(this, o + new Vector2(0, 58), (keys.Length > 0 ? keys + "     " : "") + (passive.Length > 0 ? "running: " + passive : "no programs running") + (m.Crashed.Count > 0 ? $"     crashed: {string.Join(", ", m.Crashed.Select(p => Tables.Program(p).Name))}" : ""), 14, UITheme.Violet);
            string hold = m.Holding != null ? $"Hold E: {m.Holding}" : m.You == m.Entry ? (m.Locked > 0 ? "Link-locked: you can't jack out yet" : "E: jack out") : m.SpikeTarget is Ic st ? $"Space / click: spike the {st.Def.Name}" : "";
            UITheme.Write(this, o + new Vector2(0, 86), hold, 16, UITheme.Yellow, heading: true);
            UITheme.Write(this, o + new Vector2(0, 110), "WASD: move  ·  Space/click: spike IC next to you, or brute-force a gate  ·  push into a gate to crack it quietly  ·  G: purge a program  ·  your body is sitting still out there", 12, UITheme.Dim);
            // The IC legend, on the right.
            var kinds = m.Ice.Select(i => i.Kind).Distinct().OrderBy(k => k).ToList();
            float ly = 150;
            foreach (IcKind kind in kinds)
            {
                IcDef def = Tables.IcOf(kind);
                UITheme.Write(this, new Vector2(size.X - 330, ly), $"{def.Glyph}  {def.Name.ToUpper()}", 12, IcColour(kind), heading: true);
                ly += 17;
                // The description, wrapped by hand (custom-drawn text doesn't wrap).
                var words = new System.Text.StringBuilder();
                foreach (string word in def.Desc.Split(' '))
                {
                    if (words.Length + word.Length > 44) { UITheme.Write(this, new Vector2(size.X - 312, ly), words.ToString(), 11, UITheme.Dim); ly += 15; words.Clear(); }
                    words.Append(words.Length > 0 ? " " : "").Append(word);
                }
                if (words.Length > 0) { UITheme.Write(this, new Vector2(size.X - 312, ly), words.ToString(), 11, UITheme.Dim); ly += 15; }
                ly += 6;
            }
        }

        private static Color IcColour(IcKind k) => k switch
        {
            IcKind.Patrol => new Color(0.95f, 0.85f, 0.4f),
            IcKind.Killer => UITheme.Bad,
            IcKind.Track => UITheme.Amber,
            IcKind.TarBaby => new Color(0.6f, 0.45f, 0.3f),
            IcKind.Acid => new Color(0.6f, 1f, 0.2f),
            IcKind.Crash => new Color(1f, 0.5f, 0.8f),
            IcKind.Blaster => new Color(1f, 0.55f, 0.3f),
            IcKind.Black => UITheme.Violet,
            _ => UITheme.Magenta,
        };

        private Image _mapImg;
        private ImageTexture _mapTex;
        private byte[] _mapData;
        private float _mapT;

        /// <summary>What you have explored, top right: walls, you, the way out, the objective, and who you can see.</summary>
        private int _mapFov = -1, _mapVersion = -1;

        private void Minimap(Run r, Vector2 size, V2? objective)
        {
            Level L = r.L;
            if (_mapImg == null)
            {
                _mapData = new byte[L.W * L.H * 4];
                _mapImg = Image.CreateFromData(L.W, L.H, false, Image.Format.Rgba8, _mapData);
                _mapTex = ImageTexture.CreateFromImage(_mapImg);
                TextureFilter = TextureFilterEnum.Nearest;
            }
            // A few times a second, and only when something on it can have changed (what you see, the tiles).
            if ((_mapT -= (float)GetProcessDeltaTime()) <= 0 && (r.FovVersion != _mapFov || L.Version != _mapVersion))
            {
                _mapT = 0.25f;
                _mapFov = r.FovVersion;
                _mapVersion = L.Version;
                for (int i = 0; i < L.W * L.H; i++)
                {
                    Color c;
                    if (!L.Explored[i]) c = new Color(0, 0, 0, 0.0f);
                    else
                    {
                        TileDef d = Level.Def(L.T[i]);
                        c = d.BlocksSight ? new Color(0.55f, 0.6f, 0.65f, 0.9f)
                            : L.T[i] == Tile.Glass ? new Color(0.4f, 0.8f, 1f, 0.9f)
                            : d.BlocksMove ? new Color(0.35f, 0.3f, 0.25f, 0.85f)
                            : r.Visible[i] ? new Color(0.16f, 0.2f, 0.26f, 0.85f) : new Color(0.08f, 0.1f, 0.14f, 0.8f);
                    }
                    _mapData[i * 4] = (byte)(c.R * 255); _mapData[i * 4 + 1] = (byte)(c.G * 255); _mapData[i * 4 + 2] = (byte)(c.B * 255); _mapData[i * 4 + 3] = (byte)(c.A * 255);
                }
                _mapImg.SetData(L.W, L.H, false, Image.Format.Rgba8, _mapData);
                _mapTex.Update(_mapImg);
            }
            // A tower shows the floor you're on; anything else, the whole site.
            var (rx0, rx1) = r.Plan.Tower ? r.Plan.Bounds(r.PlayerFloor) : (0, L.W);
            int rw = rx1 - rx0;
            float s = S.BigMap ? MathF.Min(1300f / rw, 640f / L.H) : MathF.Min(260f / rw, 190f / L.H);
            var at0 = S.BigMap ? (size - new Vector2(rw * s, L.H * s)) / 2 : new Vector2(size.X - rw * s - 16, 56);
            var box = new Rect2(at0 - new Vector2(4, 4), new Vector2(rw * s, L.H * s) + new Vector2(8, 8));
            Panel(box);
            DrawTextureRectRegion(_mapTex, new Rect2(at0, new Vector2(rw * s, L.H * s)), new Rect2(rx0, 0, rw, L.H));
            if (r.Plan.Tower) UITheme.Write(this, at0 + new Vector2(0, L.H * s + 20), $"{r.Plan.FloorName(r.PlayerFloor)}  ({r.PlayerFloor + 1}/{r.FloorCount}){(r.Secure(r.PlayerFloor) ? "  SECURE" : "")}", 14, r.Secure(r.PlayerFloor) ? UITheme.Bad : UITheme.Yellow, HorizontalAlignment.Left, rw * s + 60, heading: true);
            var at = at0 - new Vector2(rx0 * s, 0);
            bool On(V2 p) => p.X >= rx0 && p.X < rx1;
            Rect ex = r.Plan.Extraction;
            DrawRect(new Rect2(at + new Vector2(ex.X, ex.Y) * s, new Vector2(ex.W, ex.H) * s), UITheme.Good, false, 1.5f);
            if (r.Plan.BackDoor.W > 0) { Rect bd = r.Plan.BackDoor; DrawRect(new Rect2(at + new Vector2(bd.X, bd.Y) * s, new Vector2(bd.W, bd.H) * s), UITheme.Good, false, 1.5f); }
            if (r.Plan.Basement.W > 0)
            {
                Rect bm = r.Plan.Basement;
                DrawRect(new Rect2(at + new Vector2(bm.X, bm.Y) * s, new Vector2(bm.W, bm.H) * s), UITheme.Yellow with { A = 0.5f }, false, 1);
                UITheme.Write(this, at + new Vector2(bm.X, bm.Y - 1) * s, "BASEMENT", 11, UITheme.Yellow, HorizontalAlignment.Left, bm.W * s);
                foreach (var kv in r.L.Stair) DrawCircle(at + Art.G(r.L.PtOf(kv.Key).Centre) * s, 2.5f, UITheme.Yellow);
            }
            if (objective is V2 o && !(r.ObjectiveDone || r.ObjectiveLost) && L.Explored[L.Idx((int)o.X, (int)o.Y)] && On(o))
                DrawCircle(at + Art.G(o) * s, 3.5f, UITheme.Magenta);
            if (r.Plan.Tower) DrawRect(new Rect2(at + new Vector2(r.Plan.Lifts[r.PlayerFloor].X, r.Plan.Lifts[r.PlayerFloor].Y) * s, new Vector2(s, s)), UITheme.Cyan);
            foreach (Actor a in r.Actors)
                if (!a.IsPlayer && !a.Dead && r.Shown(a) && On(a.Pos)) DrawCircle(at + Art.G(a.Pos) * s, 2.2f, a.IsNpc ? UITheme.Dim : a.Team == 0 ? UITheme.Violet : UITheme.Bad);
            DrawCircle(at + Art.G(r.Player.Pos) * s, 3, UITheme.Cyan);
        }

        private void Panel(Rect2 r)
        {
            UITheme.DrawPanel(this, r, UITheme.PanelEdge with { A = 0.7f }, 0.84f);
        }

        private void EdgeArrow(V2 target, Color colour, Vector2 size)
        {
            RunView v = S.View;
            Vector2 screen = v.GetGlobalTransformWithCanvas() * Art.G(target, RunView.T);
            var inner = new Rect2(new Vector2(70, 100), size - new Vector2(140, 240));
            if (inner.HasPoint(screen))
            {
                if (S.R.Seen(target)) return;
                DrawArc(screen, 16 + MathF.Sin(S.R.Time * 5) * 3, 0, MathF.Tau, 24, colour, 2);
                return;
            }
            Vector2 c = inner.GetCenter();
            Vector2 d = (screen - c).Normalized();
            float tx = d.X != 0 ? (inner.Size.X / 2) / MathF.Abs(d.X) : float.MaxValue;
            float ty = d.Y != 0 ? (inner.Size.Y / 2) / MathF.Abs(d.Y) : float.MaxValue;
            Vector2 p = c + d * MathF.Min(tx, ty);
            Vector2 s = new(-d.Y, d.X);
            DrawColoredPolygon(new[] { p + d * 16, p - d * 8 + s * 11, p - d * 8 - s * 11 }, colour);
        }
    }
}
