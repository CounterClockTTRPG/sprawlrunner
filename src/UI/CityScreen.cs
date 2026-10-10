using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>
/// Driving: steps the city at 60 Hz from the keys, draws the HUD (status,
/// active job, minimap, the arrow to where you are going, the car), and
/// opens places when you stop at their door and press E.
/// </summary>
public partial class CityScreen : Control
{
    public Root Root;
    private Hud _hud;
    private bool _enter, _map, _horn, _smoke, _oil;
    /// <summary>The planned route (click a place on the map), and the map overlays (1 turf, 2 police, 4 people).</summary>
    private DriveAutopilot _route;
    private Lot _routeTo;
    private int _routeFrom, _overlay = 1;
    private float _explainT;
    private float _replanT;
    private Vector2 _mapOrigin;
    private float _mapScale = 1;

    /// <summary>The place pin under the mouse on the big map, if any.</summary>
    private Lot MapPin()
    {
        Vector2 m = Controls.MousePos(GetViewport());
        Lot best = null;
        float bd = 12;
        foreach (Lot l in Sim.Destinations())
        {
            float d = (_mapOrigin + Art.G(l.Door.Centre) * _mapScale).DistanceTo(m);
            if (d < bd) { bd = d; best = l; }
        }
        return best;
    }

    /// <summary>The place you're heading for: the one you set on the map (or the PDA), else null and the job leads.</summary>
    public Lot Target => _routeTo;

    /// <summary>Set (or, picking it again or null, clear) your target: a route line to it, and the arrow and minimap point there.</summary>
    public void SetRoute(Lot to, bool quiet = false)
    {
        if (to == null || to == _routeTo) { ClearRoute(); if (!quiet) Sfx.Instance?.Play(Sound.UiBack); return; }
        _routeTo = to;
        _route = Sim.RouteTo(to);
        _routeFrom = 0;
        C.TargetLot = to.Index;
        if (!quiet) Sfx.Instance?.Play(Sound.UiSelect);
    }

    private void ClearRoute()
    {
        _route = null;
        _routeTo = null;
        C.TargetLot = -1;
        if (Root.CityView != null) Root.CityView.Route = null;
    }

    /// <summary>What to call a place on the HUD.</summary>
    public string LotName(Lot l) => l.Place == null ? (C.Active is Job j && (j.Lot == l.Index || j.Lot2 == l.Index) ? "the job" : "a job site") : City.PlaceName(l.Place);

    /// <summary>Keep the route on the car: drop the waypoints behind you, replan when you wander off it, clear it on arrival.</summary>
    private void StepRoute(float dt)
    {
        // Something else set the target (a call you answered): follow it.
        if (C.TargetLot >= 0 && (_routeTo == null || _routeTo.Index != C.TargetLot) && C.TargetLot < Root.City.Lots.Count && Sim.Destinations().Contains(Root.City.Lots[C.TargetLot]))
            SetRoute(Root.City.Lots[C.TargetLot], quiet: true);
        if (_route == null) return;
        // Arrived, or the place has gone off the list (the job you were heading for is done): back to the job.
        if (Sim.AtDoor() == _routeTo || V2.Distance(Sim.Player.Pos, _routeTo.Door.Centre) < 3 || !Sim.Destinations().Contains(_routeTo)) { ClearRoute(); return; }
        var w = _route.Waypoints;
        _routeFrom = _route.Ahead(Sim.Player.Pos, _routeFrom);
        if ((_replanT -= dt) <= 0)
        {
            _replanT = 1.5f;
            if (V2.Distance(Sim.Player.Pos, w[Math.Min(_routeFrom, w.Count - 1)]) > 14) { _route = Sim.RouteTo(_routeTo); _routeFrom = 0; }
        }
        var pts = _routePts;
        pts.Clear();
        pts.Add(Sim.Player.Pos);
        pts.Add(_route.OnLeg(Sim.Player.Pos, _routeFrom));
        for (int k = _routeFrom; k < _route.Waypoints.Count; k++) pts.Add(_route.Waypoints[k]);
        Root.CityView.Route = pts;
    }

    /// <summary>The route from the car on, handed to the view (one list, refilled each step).</summary>
    private readonly System.Collections.Generic.List<V2> _routePts = new();
    public bool Paused => Modal.Open > 0 || _place != null || DebriefScreen.Open > 0 || Pda.Open > 0;
    private PlaceScreen _place;
    private float _towT = -1;

    public override void _Ready()
    {
        UITheme.Fill(this);
        Theme = UITheme.Theme;
        MouseFilter = MouseFilterEnum.Ignore;
        var rain = new Rain { Drops = Root.Career.Weather switch { Career.Sky.Clear => 0, Career.Sky.Rain => 260, Career.Sky.Storm => 520, _ => 0 }, Fog = Root.Career.Weather == Career.Sky.Fog, Storm = Root.Career.Weather == Career.Sky.Storm };
        // A clear night has nothing to draw: no layer redrawn every frame for it.
        if (rain.Drops > 0 || rain.Fog || rain.Storm) AddChild(rain);
        else rain.Free();
        _hud = new Hud { S = this };
        UITheme.Fill(_hud);
        AddChild(_hud);
        Root.Career.RefreshBoard(Root.City);
        // The target you set last time, if it's still somewhere you can go.
        if (C.TargetLot >= 0 && C.TargetLot < Root.City.Lots.Count && Sim.Destinations().Contains(Root.City.Lots[C.TargetLot])) SetRoute(Root.City.Lots[C.TargetLot], quiet: true);
        else C.TargetLot = -1;
        if (Root.Career.Day == 1 && Root.Career.JobsDone == 0 && Root.Career.ActiveJob < 0 && Root.CitySim.AtDoor() == Root.CitySim.HomeLot)
            Modal.Tell(this, "KESSLER BAY", "You're parked outside your safehouse (the green marker). Press E there to use the terminal and pick a job.\n\nW/S throttle and brake, A/D steer, Space handbrake, H horn (clears people off the pavement). M shows the map.");
    }

    public CitySim Sim => Root.CitySim;
    private string _toast;
    private float _saveT = 20, _achT = 2;
    private float _toastT;
    public Career C => Root.Career;

    public override void _UnhandledInput(InputEvent e)
    {
        if (Paused) return;
        if (_map)
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { if (MapPin() is Lot pin) SetRoute(pin); GetViewport().SetInputAsHandled(); return; }
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) { if (_routeTo != null) SetRoute(null); GetViewport().SetInputAsHandled(); return; }
            if (Controls.Is(e, Act.Weapon1)) { _overlay ^= 1; GetViewport().SetInputAsHandled(); return; }
            if (Controls.Is(e, Act.Weapon2)) { _overlay ^= 2; GetViewport().SetInputAsHandled(); return; }
            if (Controls.Is(e, Act.Weapon3)) { _overlay ^= 4; GetViewport().SetInputAsHandled(); return; }
        }
        if (Controls.Is(e, Act.Interact)) _enter = true;
        else if (Controls.Is(e, Act.Map)) _map = !_map;
        else if (Controls.Is(e, Act.Pda)) AddChild(new Pda(C, Sim));
        else if (Controls.Is(e, Act.Pause)) PauseMenu();
        else if (Controls.Is(e, Act.Horn)) _horn = true;
        else if (Controls.Is(e, Act.AbilityQ)) _smoke = true;
        else if (Controls.Is(e, Act.Grenade)) _oil = true;
        else return;
        GetViewport().SetInputAsHandled();
    }

    /// <summary>The pause menu, unless something is already on screen (a place, a dialog, the PDA).</summary>
    public void PauseIfDriving()
    {
        if (Paused || Root.CitySim == null) return;
        Sfx.Instance?.Engine(0, 0);
        PauseMenu();
    }

    private void PauseMenu()
    {
        var m = new Modal("PAUSED", 420);
        m.Add(UITheme.Button("Resume", () => m.Close(), 0, 44));
        m.Add(UITheme.Button("PDA  [P]", () => { m.QueueFree(); AddChild(new Pda(C, Sim)); }, 0, 44));
        m.Add(UITheme.Button("Settings", () => Settings.Open(m), 0, 44));
        m.Add(UITheme.Button("Save and quit to title", () => { m.QueueFree(); Root.SaveAndQuit(); }, 0, 44));
        AddChild(m);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Root.CitySim == null || !AgentRunner.Running) return;
        float dt = (float)delta;
        if (Paused)
        {
            Sfx.Instance?.Engine(0, 0);
            _enter = false;
            return;
        }
        if (_towT >= 0)
        {
            _towT -= dt;
            if (_towT < 0) Tow();
            return;
        }
        // Walked away a legend (from a call): the career ends here.
        if (C.Retired) { Root.Retire(); return; }
        // A new day: what changed, on one card, before anything else.
        if (C.Day > 1 && C.Flag("card") < C.Day && !Sim.Chasers.Any() && !C.Courier)
        {
            C.Set("card", C.Day);
            ShowMorning();
            return;
        }
        // The first time a system bites, a line about it.
        if (_explainT > 0) _explainT -= dt;
        else if (C.Explainers.Count > 0)
        {
            _toast = C.Explainers[0];
            C.Explainers.RemoveAt(0);
            _toastT = 7;
            _explainT = 7.5f;
        }
        // Someone is calling: it rings as soon as nothing else is on screen.
        if (C.NextCall() is Mail call && !Sim.Chasers.Any() && !C.Courier)
        {
            Sfx.Instance?.Engine(0, 0);
            AddChild(new StoryModal(C, call));
            return;
        }
        var inp = new DriveInput();
        if (Controls.Down(Act.Up)) inp.Throttle += 1;
        if (Controls.Down(Act.Down)) inp.Throttle -= 1;
        if (Controls.Down(Act.Left)) inp.Steer -= 1;
        if (Controls.Down(Act.Right)) inp.Steer += 1;
        // Gamepad: triggers for throttle and brake, the left stick steers.
        inp.Throttle = M.Clamp(inp.Throttle + Controls.Trigger(true) - Controls.Trigger(false), -1, 1);
        inp.Steer = M.Clamp(inp.Steer + Controls.Stick(true).X, -1, 1);
        inp.Handbrake = Controls.Down(Act.Handbrake);
        inp.Horn = _horn;
        _horn = false;
        inp.Boost = Controls.Down(Act.Sneak);
        inp.Smoke = _smoke;
        inp.Oil = _oil;
        _smoke = _oil = false;
        using (Perf.Time("sim.step")) Sim.Step(dt, inp);
        using (Perf.Time("route")) StepRoute(dt);
        Root.CityView.Braking = inp.Throttle < 0 && Sim.Player.Speed > 0.5f || inp.Handbrake;
        foreach (CityEvent e in Sim.Events)
        {
            float v = MathF.Max(0, 1 - V2.Distance(e.Pos, Sim.Player.Pos) / 16);
            switch (e.Kind)
            {
                case CityEv.Crash: Sfx.Instance?.Play(Sound.Crash, 1, 0.9f); break;
                case CityEv.Bump: Sfx.Instance?.Play(Sound.Bump, 1, 0.8f); break;
                case CityEv.Scrape: Sfx.Instance?.Play(Sound.Scrape, 1, 0.4f); break;
                case CityEv.Honk: Sfx.Instance?.Play(Sound.Honk, e.Strength > 1 ? 0.85f : 1, v * 0.5f * e.Strength); break;
                case CityEv.Knock: Sfx.Instance?.Play(Sound.Knock, 1, 0.6f); break;
                case CityEv.Jingle: Sfx.Instance?.Play(Sound.Jingle, 1, 0.15f + 0.45f * e.Strength); break;
                case CityEv.Siren: Sfx.Instance?.Play(Sound.Siren, 1, 0.25f + 0.5f * v); break;
                case CityEv.Wail: Sfx.Instance?.Play(Sound.Siren, 1.25f, 0.08f + 0.3f * e.Strength); break;
                case CityEv.Gunfire: Sfx.Instance?.Play(Sound.EnemyShot, 1, 0.4f + 0.5f * v); break;
                case CityEv.Scream: Sfx.Instance?.Play(Sound.Scream, 0.9f + 0.25f * Sim.R.Float(), 0.15f + 0.35f * v); break;
                case CityEv.Busted:
                    Sfx.Instance?.Play(Sound.Fail);
                    Modal.Tell(this, "BUSTED", e.Text);
                    break;
                case CityEv.Message:
                case CityEv.Lost:
                case CityEv.Pickup:
                    if (e.Text != null) { _toast = e.Text; _toastT = 5; }
                    if (e.Kind == CityEv.Pickup) Sfx.Instance?.Play(Sound.Pickup);
                    break;
            }
        }
        using (Perf.Time("view.frame")) Root.CityView.Frame(dt);
        float sp = MathF.Abs(Sim.Player.Speed) / Sim.Player.Def.Top;
        Sfx.Instance?.Engine(0.18f + 0.2f * sp, sp);

        if ((_saveT -= dt) <= 0) { using var _ = Perf.Time("autosave"); _saveT = 20; Sim.SaveCar(); Profile.SaveCareer(C); }
        if ((_achT -= dt) <= 0)
        {
            _achT = 2;
            using var _ = Perf.Time("achievements");
            foreach (string a in Profile.Current.CheckAchievements(C)) { _toast = $"Street cred earned: {a}"; _toastT = 5; Sfx.Instance?.Play(Sound.Objective); }
        }
        if (Sim.CourierFailed != null)
        {
            Job f = Sim.CourierFailed;
            Sim.CourierFailed = null;
            Root.EndCourier(f, false, 0);
            return;
        }
        if (Sim.Wrecked && _towT < 0)
        {
            _towT = 1.2f;
            Sfx.Instance?.Engine(0, 0);
            return;
        }
        if (_enter)
        {
            _enter = false;
            if (Sim.AtStreet()) { OpenStreet(); return; }
            Lot l = Sim.AtDoor();
            if (l != null) Enter(l);
        }
    }

    /// <summary>Stopped beside a street encounter: its scene plays; closed unanswered, you drive on (its last choice).</summary>
    private void OpenStreet()
    {
        Storylet s = Storylets.ById(Sim.Street.Story);
        Sim.Street = null;
        if (s == null) return;
        Sim.Player.Vel = V2.Zero;
        Sfx.Instance?.Engine(0, 0);
        Mail m = C.Send(s);
        var modal = new StoryModal(C, m);
        modal.Closed = () =>
        {
            if (m.Answer < 0 && s.Choices is { Length: > 0 }) C.Choose(m, s.Choices.Length - 1);
            // What the street does back: a toll you ran sends the bikes after you.
            if (C.Flag("street:ambush") is int n && n > 0) { C.Set("street:ambush", 0); Sim.Ambush(n); }
        };
        AddChild(modal);
    }

    /// <summary>The morning card: the weather, what's going on, what came in, what it cost.</summary>
    private void ShowMorning()
    {
        var m = new Modal($"DAY {C.Day}", 640);
        m.Add(UITheme.Label($"{C.Weather}.   {M.Money(C.Cash)}   ·   heat {C.Heat:0.0}   ·   rent {M.Money(C.DailyRent)} a day", 16, UITheme.Dim));
        foreach (string h in C.HappeningLines()) m.Add(UITheme.Label(h.Replace("TODAY: ", ""), 16, UITheme.Yellow));
        var today = C.News.Where(n => n.StartsWith($"Day {C.Day}.")).Select(n => n[$"Day {C.Day}.".Length..].Trim()).Where(n => !n.StartsWith("TODAY")).TakeLast(8).ToList();
        foreach (string n in today) m.Add(UITheme.Label("· " + n, 15, n.StartsWith("KBN") ? UITheme.Cyan : UITheme.Text));
        if (today.Count == 0 && !C.Today.Any()) m.Add(UITheme.Label("A quiet morning. Rain on the window.", 15, UITheme.Dim));
        int unread = C.Unread, waiting = C.Inbox.Count(C.Waiting);
        if (unread + waiting > 0) m.Add(UITheme.Label($"{unread} new message(s), {waiting} waiting on an answer -- P for the PDA.", 15, UITheme.Amber));
        foreach (Job u in C.Board.Where(b => b.Tag != null && b.Tag.StartsWith("danger:")))
            m.Add(UITheme.Label($"URGENT: {u.Title} -- {(u.Expires <= C.Day ? "today is the last chance" : $"by the end of day {u.Expires}")}. Miss it and they're shut for a week{(C.Flag("dangerfail:" + u.Tag[7..]) >= 1 ? " -- and this time, gone for good" : "")}.", 15, UITheme.Bad));
        foreach (ContactDef k in Tables.Contacts.Where(k => C.Shut(k.Id) != null && C.Loyalty.GetValueOrDefault(k.Id) > 0)) m.Add(UITheme.Label(C.Shut(k.Id), 14, UITheme.Bad));
        var row = UITheme.HBox();
        row.Alignment = BoxContainer.AlignmentMode.Center;
        row.AddChild(UITheme.Button("Out into the rain", () => m.Close(), 220, 40));
        m.Add(row);
        AddChild(m);
    }

    private void Tow()
    {
        int fee = C.TowFee;
        C.Cash -= fee;
        C.Count("spent", fee);
        C.CarHp = C.CarMaxHp / 2;
        Sim.RefreshVehicle();
        Sim.Wrecked = false;
        Sim.ParkAt(Sim.HomeLot);
        Profile.SaveCareer(C);
        Modal.Tell(this, "TOTALLED", $"The {Tables.Vehicle(C.Vehicle).Name} is a write-off for now. A tow truck dragged it home ({M.Money(fee)}). It runs at half hull -- patch it up at home or let Torque fix it properly.");
    }

    public void Enter(Lot l)
    {
        if (Sim.Chasers.Any(c => c.Kind != ChaserKind.Racer && V2.Distance(c.Car.Pos, Sim.Player.Pos) < 8))
        {
            _toast = "Not with them on your tail. Lose them first.";
            _toastT = 3;
            Sfx.Instance?.Play(Sound.Error);
            return;
        }
        Job cj = C.Active;
        if (cj != null && cj.Driving && l.Place == null)
        {
            if (!C.Courier) { if (cj.Type == JobType.Race) Sim.StartRace(cj); else Sim.CollectPackage(cj); Sfx.Instance?.Play(Sound.Pickup); }
            else if (l.Index == cj.Lot2) { float left = C.CourierT; C.Courier = false; Root.EndCourier(cj, true, left); }
            return;
        }
        Sim.Player.Vel = V2.Zero;
        Sim.SaveCar();
        Sfx.Instance?.Play(Sound.Door);
        if (l.Place != null && City.IsHub(l.Place)) { Root.EnterHub(l); return; }
        // Your own door with a raid coming, or a friend's with their enemies outside: the briefing, not the sofa or the shop.
        string kind = cj != null && l.Index == cj.Lot && l.Place != null && (cj.Tag == "raid" || cj.Tag?.StartsWith("danger:") == true || cj.Type == JobType.Hack) ? "job" : l.Place ?? "job";
        _place = new PlaceScreen(Root, l, kind);
        _place.Closed = () =>
        {
            _place = null;
            Sim.RefreshVehicle();
            Profile.SaveCareer(C);
        };
        AddChild(_place);
    }

    public override void _Process(double delta)
    {
        _toastT -= (float)delta;
        // Under a place, a dialog or the PDA nothing on the HUD moves: one last paint, then leave it be.
        bool paused = Paused;
        if (!paused || !_hudPaused) _hud.QueueRedraw();
        _hudPaused = paused;
    }

    private bool _hudPaused;

    // ---- HUD ------------------------------------------------------------------------

    private partial class Hud : Control
    {
        /// <summary>An X: a checkpoint on the map.</summary>
        private void Cross(Vector2 p, float r, Color c)
        {
            DrawLine(p - new Vector2(r, r), p + new Vector2(r, r), c, 2.5f);
            DrawLine(p - new Vector2(r, -r), p + new Vector2(r, -r), c, 2.5f);
        }

        public CityScreen S;
        private ImageTexture _mini;

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            TextureFilter = TextureFilterEnum.Nearest;
        }

        private void BuildMini()
        {
            City c = S.Sim.City;
            var img = Image.CreateEmpty(City.W, City.H, false, Image.Format.Rgba8);
            for (int y = 0; y < City.H; y++)
            for (int x = 0; x < City.W; x++)
            {
                Color col = c[x, y] switch
                {
                    CTile.Road => new Color(0.35f, 0.38f, 0.45f),
                    CTile.Sidewalk => new Color(0.18f, 0.19f, 0.22f),
                    CTile.Building => Art.C(Tables.District(c.DistrictAt[y * City.W + x]).Colour).Darkened(0.72f),
                    CTile.Grass or CTile.Tree => new Color(0.08f, 0.2f, 0.1f),
                    CTile.Parking => new Color(0.2f, 0.2f, 0.22f),
                    CTile.Pier => new Color(0.3f, 0.22f, 0.14f),
                    CTile.Plaza or CTile.Statue => new Color(0.4f, 0.4f, 0.38f),
                    _ => new Color(0.02f, 0.05f, 0.12f),
                };
                img.SetPixel(x, y, col);
            }
            _mini = ImageTexture.CreateFromImage(img);
        }

        public override void _Draw()
        {
            if (S.Root.CitySim == null || S.Root.CityView == null || !IsInstanceValid(S.Root.CityView)) return;
            using var _perf = Perf.Time("draw.hud");
            Career c = S.C;
            CitySim sim = S.Sim;
            Vector2 size = GetViewportRect().Size;
            if (S._map) { DrawMap(c, sim, size); return; }
            // Status.
            var panel = new Rect2(16, 16, 380, 92);
            UITheme.DrawPanel(this, panel);
            Status st = StatusOf(c);
            UITheme.Write(this, new Vector2(30, 44), st.Who, 20, UITheme.Cyan, heading: true);
            UITheme.Write(this, new Vector2(30, 72), st.Cash, 20, UITheme.Yellow, heading: true);
            UITheme.Write(this, new Vector2(170, 72), st.Karma, 17, UITheme.Violet);
            UITheme.Write(this, new Vector2(280, 72), st.Rep, 17, UITheme.Good);
            UITheme.Write(this, new Vector2(30, 97), st.Day, 15, UITheme.Dim);
            if (st.Unread > 0) UITheme.Write(this, new Vector2(300, 97), st.Mail, 15, MathF.Sin(sim.Time * 4) > 0 ? UITheme.Amber : UITheme.Yellow, heading: true);
            bool police = false;
            foreach (Chaser ch in sim.Chasers) police |= ch.Kind == ChaserKind.Police;
            // Heat.
            if (c.Heat > 0.01f)
            {
                var hp2 = new Rect2(398, 16, 330, 66);
                UITheme.DrawPanel(this, hp2, UITheme.Bad with { A = 0.7f }, hazard: police);
                bool flash = police && MathF.Sin(sim.Time * 8) > 0;
                for (int k = 0; k < 5; k++)
                {
                    float fill = M.Clamp(c.Heat - k, 0, 1);
                    Vector2 sp = new(418 + k * 26, 38);
                    Star(sp, 10, new Color(0.2f, 0.2f, 0.25f, 0.8f));
                    if (fill > 0) Star(sp, 10 * MathF.Max(0.35f, fill), flash ? UITheme.Bad : UITheme.Yellow);
                }
                UITheme.Write(this, new Vector2(408, 72), police ? "LONE STAR ON YOU -- don't stop near them" : "Heat cools while no patrol is near (or sleep it off)", 14, UITheme.Dim);
            }
            if (c.Courier)
            {
                var tp = new Rect2(size.X / 2 - 160, 16, 320, 50);
                UITheme.DrawPanel(this, tp, c.CourierT < 15 ? UITheme.Bad : UITheme.Yellow, hazard: c.CourierT < 15, width: 2);
                string what = $"PACKAGE  {c.CourierT:0.0} s";
                if (c.Active?.Type == JobType.Race)
                {
                    V2 fin = sim.City.Kerb(sim.City.Lots[c.Active.Lot2]).pos;
                    float you = V2.Distance(sim.Player.Pos, fin) * 4, rival = sim.Racer != null ? V2.Distance(sim.Racer.Car.Pos, fin) * 4 : 0;
                    what = sim.Racer != null ? $"RACE  you {you:0} m  ·  rival {rival:0} m" : $"RACE  {you:0} m to go";
                }
                UITheme.Write(this, tp.Position + new Vector2(0, 34), what, 24, c.CourierT < 15 && MathF.Sin(sim.Time * 10) > 0 ? UITheme.Bad : UITheme.Yellow, HorizontalAlignment.Center, 320, heading: true);
            }
            if (S._toastT > 0 && S._toast != null)
                UITheme.Write(this, new Vector2(0, 110), S._toast, 19, UITheme.Text with { A = MathF.Min(1, S._toastT) }, HorizontalAlignment.Center, size.X, heading: true);

            // Job.
            Job j = c.Active;
            var jp = new Rect2(16, 118, 380, j != null ? 64 : 40);
            UITheme.DrawPanel(this, jp, j != null ? UITheme.Magenta : UITheme.Faint);
            if (j != null)
            {
                // Someone's counting on you: the deadline in plain words, flashing on the last day.
                bool urgent = j.Tag != null && j.Tag.StartsWith("danger:");
                bool lastDay = j.Expires <= c.Day;
                UITheme.Write(this, new Vector2(30, 142), urgent ? "URGENT  " + j.Title : j.Title, 17, urgent ? UITheme.Bad : UITheme.Magenta, heading: true);
                string when = urgent ? (lastDay ? "TODAY is the last chance" : $"by the end of day {j.Expires} (tomorrow)") : $"{j.Expires - c.Day + 1} day(s) left";
                UITheme.Write(this, new Vector2(30, 166), $"{Tables.District(j.District).Name}  {j.Skulls}  {M.Money(j.Pay)}  ·  {when}", 15, urgent && lastDay && MathF.Sin(sim.Time * 6) > 0 ? UITheme.Bad : UITheme.Text);
            }
            else UITheme.Write(this, new Vector2(30, 144), "No job on. The terminal at the safehouse has work.", 15, UITheme.Dim);

            // Minimap (the big map is its own screen: see DrawMap).
            if (_mini == null) BuildMini();
            float ms = 1.0f;
            Vector2 msize = new Vector2(City.W, City.H) * ms;
            Vector2 mo = new Vector2(size.X - msize.X - 16, 16);
            UITheme.DrawPanel(this, new Rect2(mo - new Vector2(6, 6), msize + new Vector2(12, 12)));
            DrawTextureRect(_mini, new Rect2(mo, msize), false);
            DrawRect(new Rect2(mo, msize), UITheme.PanelEdge with { A = 0.5f }, false, 1);
            DrawRoute(mo, ms);
            foreach (Lot l in sim.Destinations())
            {
                bool job = j != null && l.Index == j.Lot;
                Pin(mo + Art.G(l.Door.Centre) * ms, l, job, job ? 5 : 3.5f);
            }
            Vector2 cp = mo + Art.G(sim.Player.Pos) * ms;
            Vector2 cf = new(MathF.Cos(sim.Player.Heading), MathF.Sin(sim.Player.Heading));
            DrawColoredPolygon(new[] { cp + cf * 7, cp + new Vector2(-cf.Y, cf.X) * 4 - cf * 4, cp - new Vector2(-cf.Y, cf.X) * 4 - cf * 4 }, Colors.White);

            // Arrow to your target when it is off screen: the place you set, else the job.
            // (A package aboard always shows its drop: the clock is running.)
            Lot dest = j == null ? null : j.Driving ? sim.CourierLot(j) : sim.City.Lots[j.Lot];
            Lot target = S.Target;
            if (dest != null && (target == null || c.Courier) && dest != target) EdgeArrow(dest.Door.Centre, UITheme.Magenta, j.Driving ? (c.Courier ? "DROP" : "PICKUP") : "JOB");
            if (target != null)
            {
                EdgeArrow(target.Door.Centre, UITheme.Cyan, S.LotName(target).ToUpperInvariant());
                Vector2 tp2 = mo + Art.G(target.Door.Centre) * ms;
                DrawArc(tp2, 7, 0, MathF.Tau, 16, UITheme.Cyan, 2);
                // What you're heading for, under the job.
                var tb = new Rect2(16, jp.End.Y + 6, 380, 28);
                UITheme.DrawPanel(this, tb, UITheme.Cyan);
                UITheme.Write(this, tb.Position + new Vector2(12, 20), $"TARGET  {S.LotName(target)}  ·  {V2.Distance(target.Door.Centre, sim.Player.Pos) * 4:0} m   (M: change or clear)", 14, UITheme.Cyan, heading: true);
            }
            foreach (Roadblock rb in sim.Roadblocks) Cross(mo + Art.G(rb.At) * ms, 4, UITheme.Bad);
            foreach (Chaser ch in sim.Chasers)
            {
                Vector2 bp = mo + Art.G(ch.Car.Pos) * ms;
                DrawCircle(bp, 3.5f, ch.Kind == ChaserKind.Police ? (MathF.Sin(sim.Time * 10) > 0 ? UITheme.Bad : UITheme.Cyan) : ch.Kind == ChaserKind.Racer ? UITheme.Yellow : UITheme.Good);
            }

            // Car.
            var cpnl = new Rect2(16, size.Y - 76, 300, 60);
            UITheme.DrawPanel(this, cpnl);
            UITheme.Write(this, new Vector2(30, size.Y - 52), sim.Player.Def.Name, 16, UITheme.Text, heading: true);
            float hull = sim.Player.Hp / sim.Player.Def.Hp;
            UITheme.DrawSegBar(this, new Rect2(30, size.Y - 42, 180, 13), hull, hull > 0.5f ? UITheme.Good : hull > 0.25f ? UITheme.Yellow : UITheme.Bad, 12);
            UITheme.Write(this, new Vector2(222, size.Y - 30), $"{MathF.Abs(sim.Player.Speed) * 9:0} km/h", 15, UITheme.Dim);
            var gadgets = new System.Collections.Generic.List<string>();
            if (c.CarHas("nitro")) gadgets.Add(sim.BoostT > 0 ? "NITRO!" : $"[{Controls.KeyName(Act.Sneak)}] nitro {sim.Nitro}");
            if (c.CarHas("smoke")) gadgets.Add($"[{Controls.KeyName(Act.AbilityQ)}] smoke {sim.SmokeLeft}");
            if (c.CarHas("oil")) gadgets.Add($"[{Controls.KeyName(Act.Grenade)}] oil {sim.OilLeft}");
            if (c.CarHas("guns")) gadgets.Add("guns armed");
            if (gadgets.Count > 0) UITheme.Write(this, new Vector2(30, size.Y - 84), string.Join("  ·  ", gadgets), 13, sim.BoostT > 0 ? UITheme.Cyan : UITheme.Amber);

            // Street encounter prompt (it takes the door prompt's place).
            if (sim.AtStreet() && !S._map)
            {
                string what = sim.Street.Kind switch { StreetKind.Wreck => "E  Get out and look", StreetKind.Toll => "E  Deal with the toll", _ => "E  Wind the window down" };
                var pr = new Rect2(size.X / 2 - 300, size.Y - 120, 600, 44);
                UITheme.DrawPanel(this, pr, UITheme.Amber);
                UITheme.Write(this, new Vector2(pr.Position.X, pr.Position.Y + 29), what, 19, UITheme.Amber, HorizontalAlignment.Center, 600, heading: true);
            }
            // A fare aboard: who, and where they're going.
            if (c.Passenger is Fare fare && sim.FareKerb is V2 drop)
            {
                EdgeArrow(drop, UITheme.Yellow, "DROP-OFF");
                var fp = new Rect2(16, jp.End.Y + (target != null ? 40 : 6), 380, 28);
                UITheme.DrawPanel(this, fp, UITheme.Yellow);
                UITheme.Write(this, fp.Position + new Vector2(12, 20), $"ABOARD  {fare.Who}  ·  {V2.Distance(drop, sim.Player.Pos) * 4:0} m  (stop at the kerb)", 14, UITheme.Yellow, heading: true);
                DrawArc(mo + Art.G(drop) * ms, 6, 0, MathF.Tau, 16, UITheme.Yellow, 2);
            }
            if (sim.Street != null) DrawCircle(mo + Art.G(sim.Street.At) * ms, 3, UITheme.Amber);
            // Door prompt.
            Lot at = sim.AtDoor();
            if (at != null && !S._map && !sim.AtStreet())
            {
                string what = at.Place switch
                {
                    null => j?.Type == JobType.Race ? (c.Courier ? "E  Cross the finish line" : "E  Line up at the start") : j?.Driving == true ? (c.Courier ? "E  Hand over the package" : "E  Collect the package") : $"E  Go in: {j?.Title}",
                    "home" or "loft" when j?.Tag == "raid" => "E  Defend the safehouse",
                    "home" or "loft" when j?.Type == JobType.Hack => "E  Safehouse -- jack in for the job",
                    "home" or "loft" => "E  Safehouse",
                    _ => $"E  {City.PlaceName(at.Place)} -- {City.PlaceWho(at.Place)}",
                };
                var pr = new Rect2(size.X / 2 - 300, size.Y - 120, 600, 44);
                UITheme.DrawPanel(this, pr, UITheme.Amber);
                UITheme.Write(this, new Vector2(pr.Position.X, pr.Position.Y + 29), what, 19, UITheme.Amber, HorizontalAlignment.Center, 600, heading: true);
            }
            // The car radio, a station every eight seconds (worked out once per station, district and chase).
            string district = sim.City.DistrictAt[(int)M.Clamp(sim.Player.Pos.Y, 0, City.H - 1) * City.W + (int)M.Clamp(sim.Player.Pos.X, 0, City.W - 1)];
            var radioKey = ((int)(sim.Time / 8), district, police);
            if (!radioKey.Equals(_radioKey) || _radioFor != c)
            {
                _radioKey = radioKey;
                _radioFor = c;
                var radio = c.Radio(district, police);
                if (radio.Count == 0) _radioLine = null;
                else
                {
                    var (station, text) = radio[(int)(sim.Time / 8) % radio.Count];
                    _radioLine = $"RADIO · {station}: {text}";
                    _radioColour = station == "SCANNER" ? UITheme.Cyan with { A = 0.8f } : station == "KBAY PIRATE" ? UITheme.Violet : UITheme.Dim;
                }
            }
            if (_radioLine != null) UITheme.Write(this, new Vector2(340, size.Y - 28), _radioLine, 15, _radioColour);
        }

        private (int, string, bool) _radioKey = (-1, null, false);
        private Career _radioFor;
        private string _radioLine;
        private Color _radioColour;

        /// <summary>The status panel's lines, remade only when what they show changes.</summary>
        private sealed class Status
        {
            public string Name, Archetype, Who, Cash, Karma, Rep, Day, Mail;
            public int CashV = int.MinValue, KarmaV = int.MinValue, RepV = int.MinValue, DayV = -1, Links = -1, Unread = -1;
            public Career.Sky Sky;
        }

        private readonly Status _status = new();

        private Status StatusOf(Career c)
        {
            Status st = _status;
            if (st.Name != c.Name || st.Archetype != c.Archetype) { st.Name = c.Name; st.Archetype = c.Archetype; st.Who = $"{c.Name.ToUpper()}  ·  {c.ArchetypeName}"; }
            if (st.CashV != c.Cash) { st.CashV = c.Cash; st.Cash = $"{M.Money(c.Cash)}"; }
            if (st.KarmaV != c.Karma) { st.KarmaV = c.Karma; st.Karma = $"Karma {c.Karma}"; }
            if (st.RepV != c.Rep) { st.RepV = c.Rep; st.Rep = $"Rep {c.Rep}"; }
            if (st.DayV != c.Day || st.Sky != c.Weather || st.Links != c.TraumaLinks || st.Day == null)
            {
                st.DayV = c.Day; st.Sky = c.Weather; st.Links = c.TraumaLinks;
                st.Day = $"Day {c.Day}  ·  {c.Weather}  ·  TraumaLink {c.TraumaLinks}";
            }
            int unread = c.Unread;
            if (st.Unread != unread) { st.Unread = unread; st.Mail = $"MAIL {unread} (P)"; }
            return st;
        }

        /// <summary>The route you planned, from where you are to the door.</summary>
        private void DrawRoute(Vector2 mo, float ms, float width = 2)
        {
            if (S._route == null) return;
            var pts = new System.Collections.Generic.List<Vector2> { mo + Art.G(S.Sim.Player.Pos) * ms, mo + Art.G(S._route.OnLeg(S.Sim.Player.Pos, S._routeFrom)) * ms };
            for (int k = S._routeFrom; k < S._route.Waypoints.Count; k++) pts.Add(mo + Art.G(S._route.Waypoints[k]) * ms);
            if (pts.Count >= 2) DrawPolyline(pts.ToArray(), UITheme.Cyan with { A = 0.8f }, width);
        }

        /// <summary>
        /// The big map, as a screen of its own: districts, landmarks and places
        /// without labels on top of each other; click a place to plan a route;
        /// 1, 2 and 3 lay turf, police and people over it.
        /// </summary>
        private void DrawMap(Career c, CitySim sim, Vector2 size)
        {
            DrawRect(new Rect2(Vector2.Zero, size), new Color(0.01f, 0.012f, 0.02f, 0.88f));
            var area = new Rect2(20, 64, size.X - 40, size.Y - 150);
            float ms = MathF.Min(area.Size.X / City.W, area.Size.Y / City.H);
            Vector2 msize = new Vector2(City.W, City.H) * ms;
            Vector2 mo = area.Position + (area.Size - msize) / 2;
            S._mapOrigin = mo;
            S._mapScale = ms;
            UITheme.DrawPanel(this, new Rect2(mo - new Vector2(6, 6), msize + new Vector2(12, 12)));
            DrawTextureRect(_mini, new Rect2(mo, msize), false);
            UITheme.Write(this, new Vector2(0, 44), "KESSLER BAY", 26, UITheme.Amber, HorizontalAlignment.Center, size.X, heading: true);
            // Turf: who holds each district, over its buildings.
            if ((S._overlay & 1) != 0)
                foreach (Lot l in sim.City.Lots.Where(x => x.Kind == LotKind.Building))
                    DrawRect(new Rect2(mo + new Vector2(l.R.X, l.R.Y) * ms, new Vector2(l.R.W, l.R.H) * ms), UITheme.FactionInk((int)c.Holder(l.District)) with { A = 0.35f });
            foreach (DistrictDef d in Tables.Districts)
            {
                var lots = sim.City.Lots.Where(l => l.District == d.Id).ToList();
                if (lots.Count == 0) continue;
                var cen = new Vector2((float)lots.Average(l => l.R.X + l.R.W / 2f), (float)lots.Average(l => l.R.Y + l.R.H / 2f));
                string held = (S._overlay & 1) != 0 ? $"\n{UITheme.FactionMark((int)c.Holder(d.Id))} {Tables.FactionNames[(int)c.Holder(d.Id)]}" : "";
                UITheme.Write(this, mo + cen * ms - new Vector2(130, 0), d.Name.ToUpper() + held, 28, Art.C(d.Colour) with { A = 0.4f }, HorizontalAlignment.Center, 260, heading: true);
            }
            var taken = new System.Collections.Generic.List<Rect2>();
            bool Free(Rect2 r) { if (taken.Any(t => t.Intersects(r))) return false; taken.Add(r); return true; }
            foreach (Lot l in sim.City.Lots.Where(x => x.Landmark != null))
            {
                DrawRect(new Rect2(mo + new Vector2(l.R.X, l.R.Y) * ms, new Vector2(l.R.W, l.R.H) * ms), new Color(1, 1, 1, 0.4f), false, 1);
                Vector2 lp = mo + new Vector2(l.R.X + l.R.W / 2f, l.R.Y + l.R.H / 2f) * ms;
                string name = City.Landmarks.First(x => x.id == l.Landmark).name;
                if (Free(new Rect2(lp - new Vector2(name.Length * 3.2f, 8), new Vector2(name.Length * 6.4f, 14))))
                    UITheme.Write(this, lp + new Vector2(-80, 4), name, 12, new Color(0.9f, 0.9f, 0.95f, 0.7f), HorizontalAlignment.Center, 160);
            }
            DrawRoute(mo, ms, 3);
            Job j = c.Active;
            Lot hover = S.MapPin();
            foreach (Lot l in sim.Destinations().Distinct().OrderBy(l => l == hover ? 1 : 0))
            {
                bool job = j != null && l.Index == j.Lot;
                Color lc = PinColour(l, job);
                Vector2 p = mo + Art.G(l.Door.Centre) * ms;
                Pin(p, l, job, job ? 6 : 4.5f);
                if (l == S._routeTo) DrawArc(p, 9, 0, MathF.Tau, 16, UITheme.Cyan, 2);
                // People: who has something to say, whose shutters are down.
                if ((S._overlay & 4) != 0 && l.Place != null && Tables.Contacts.Any(k => k.Id == l.Place))
                {
                    if (c.Shut(l.Place) != null) { DrawLine(p + new Vector2(-6, -6), p + new Vector2(6, 6), UITheme.Bad, 2); DrawLine(p + new Vector2(6, -6), p + new Vector2(-6, 6), UITheme.Bad, 2); }
                    else if (c.Eligible(Hook.Place, l.Place).Any() || c.Inbox.Any(m => m.Place == l.Place && !m.Read)) UITheme.Write(this, p + new Vector2(6, -6), "!", 18, UITheme.Yellow, heading: true);
                }
                string name = l.Place == null ? (job ? "JOB" : "") : City.PlaceName(l.Place);
                if (name.Length == 0) continue;
                float w = name.Length * 7f + 6;
                // Above the pin if it fits, else below, else only when you point at it.
                var above = new Rect2(p + new Vector2(-w / 2, -20), new Vector2(w, 15));
                var below = new Rect2(p + new Vector2(-w / 2, 6), new Vector2(w, 15));
                Rect2? at = l == hover ? above : Free(above) ? above : Free(below) ? below : null;
                if (at is Rect2 a) UITheme.Write(this, a.Position + new Vector2(0, 12), name, 13, lc, HorizontalAlignment.Center, a.Size.X);
            }
            // Police: patrols on the streets, and whoever is chasing you.
            if ((S._overlay & 2) != 0)
                foreach (TrafficCar t in sim.Traffic.Where(t => t.Kind == CarKind.Patrol)) DrawCircle(mo + Art.G(t.Pos) * ms, 3, UITheme.Bad with { A = 0.8f });
            foreach (Roadblock rb in sim.Roadblocks) Cross(mo + Art.G(rb.At) * ms, 7, UITheme.Bad);
            foreach (Chaser ch in sim.Chasers) DrawCircle(mo + Art.G(ch.Car.Pos) * ms, 4, ch.Kind == ChaserKind.Police ? UITheme.Bad : ch.Kind == ChaserKind.Racer ? UITheme.Yellow : UITheme.Good);
            Vector2 cp = mo + Art.G(sim.Player.Pos) * ms;
            Vector2 cf = new(MathF.Cos(sim.Player.Heading), MathF.Sin(sim.Player.Heading));
            DrawColoredPolygon(new[] { cp + cf * 9, cp + new Vector2(-cf.Y, cf.X) * 5 - cf * 5, cp - new Vector2(-cf.Y, cf.X) * 5 - cf * 5 }, Colors.White);
            string On(int bit) => (S._overlay & bit) != 0 ? "on" : "off";
            UITheme.Write(this, new Vector2(0, size.Y - 54), $"Click a place: make it your target{(S._routeTo != null ? $" (now {S.LotName(S._routeTo)}: click it again or right-click to clear)" : " (otherwise the job is)")}   ·   [1] turf {On(1)}   [2] police {On(2)}   [3] people {On(4)}   ·   M: close", 15, UITheme.Dim, HorizontalAlignment.Center, size.X);
            if (hover != null) UITheme.Write(this, new Vector2(0, size.Y - 30), $"{(hover.Place == null ? "Job site" : City.PlaceName(hover.Place) + (City.PlaceWho(hover.Place) is string who && who.Length > 0 ? " -- " + who : ""))}  ·  {Tables.District(hover.District).Name}  ·  {V2.Distance(hover.Door.Centre, sim.Player.Pos) * 4:0} m", 15, UITheme.Text, HorizontalAlignment.Center, size.X);
        }

        private static Color PinColour(Lot l, bool job) => job ? UITheme.Magenta : Art.C(l.Sign == 0 ? 0x40ffb0ffu : l.Sign);

        /// <summary>A place on a map: a dot, or for the safehouse a white-edged square in its own colour.</summary>
        private void Pin(Vector2 p, Lot l, bool job, float r)
        {
            Color c = PinColour(l, job);
            if (!City.IsSafehouse(l.Place)) { DrawCircle(p, r, c); return; }
            float h = r + 1;
            DrawRect(new Rect2(p - new Vector2(h + 1, h + 1), new Vector2(h + 1, h + 1) * 2), Colors.White);
            DrawRect(new Rect2(p - new Vector2(h, h), new Vector2(h, h) * 2), c);
        }

        private void Star(Vector2 c, float r, Color col)
        {
            var pts = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = -MathF.PI / 2 + i * MathF.PI / 5;
                float rr = i % 2 == 0 ? r : r * 0.45f;
                pts[i] = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * rr;
            }
            DrawColoredPolygon(pts, col);
        }

        private void EdgeArrow(V2 target, Color colour, string label)
        {
            CityView v = S.Root.CityView;
            Vector2 world = Art.G(target, CityView.T);
            Vector2 screen = v.GetGlobalTransformWithCanvas() * world;
            Vector2 size = GetViewportRect().Size;
            // Inside the panels, so the arrow never sits on the HUD.
            // (Below the job and target panels, above the car panel and the radio.)
            var inner = new Rect2(new Vector2(70, 240), size - new Vector2(140, 360));
            if (inner.HasPoint(screen)) return;
            Vector2 c = inner.GetCenter();
            Vector2 d = (screen - c).Normalized();
            float tx = d.X != 0 ? (inner.Size.X / 2) / MathF.Abs(d.X) : float.MaxValue;
            float ty = d.Y != 0 ? (inner.Size.Y / 2) / MathF.Abs(d.Y) : float.MaxValue;
            Vector2 p = c + d * MathF.Min(tx, ty);
            Vector2 s = new(-d.Y, d.X);
            // A slim chevron: long enough that it can only point one way.
            DrawColoredPolygon(new[] { p + d * 20, p - d * 10 + s * 9, p - d * 4, p - d * 10 - s * 9 }, colour);
            float dist = V2.Distance(target, S.Sim.Player.Pos);
            UITheme.Write(this, p - d * 32 + new Vector2(-110, 5), $"{label} {dist * 4:0}m", 13, colour, HorizontalAlignment.Center, 220, heading: true);
        }
    }

    /// <summary>Rain over everything, in screen space.</summary>
    private partial class Rain : Control
    {
        public int Drops = 260;
        public bool Fog, Storm;
        private Vector2[] _drops;
        private float[] _len;
        private readonly Random _rng = new();
        private float _flash, _t;

        public override void _Ready()
        {
            UITheme.Fill(this);
            MouseFilter = MouseFilterEnum.Ignore;
            _drops = new Vector2[Drops];
            _len = new float[Drops];
            for (int i = 0; i < _drops.Length; i++)
            {
                _drops[i] = new Vector2(_rng.Next(1700), _rng.Next(1000));
                _len[i] = 10 + _rng.Next(16);
            }
        }

        public override void _Process(double delta)
        {
            Vector2 size = GetViewportRect().Size;
            _t += (float)delta;
            _flash = MathF.Max(0, _flash - (float)delta * 3);
            if (Storm && _rng.NextDouble() < delta / 9) { _flash = 1; Sfx.Instance?.Play(Sound.Explosion, 0.35f, 0.25f); }
            for (int i = 0; i < _drops.Length; i++)
            {
                _drops[i] += new Vector2(-180, 900) * (float)delta * (0.7f + _len[i] / 40f);
                if (_drops[i].Y > size.Y + 20) _drops[i] = new Vector2(_rng.Next((int)size.X + 200), -20);
            }
            QueueRedraw();
        }

        public override void _Draw()
        {
            Vector2 size = GetViewportRect().Size;
            if (Fog)
                for (int k = 0; k < 6; k++)
                {
                    float x = (k * 400 + _t * (12 + k * 3)) % (size.X + 800) - 400;
                    DrawTextureRect(Art.Soft, new Rect2(x, 120 + k * 120 - 260, 900, 520), false, new Color(0.7f, 0.75f, 0.85f, 0.10f));
                }
            if (_flash > 0) DrawRect(new Rect2(Vector2.Zero, size), new Color(0.8f, 0.85f, 1f, 0.35f * _flash * _flash));
            if (_drops.Length == 0) return;
            // Every streak in one call.
            _lines ??= new Vector2[_drops.Length * 2];
            for (int i = 0; i < _drops.Length; i++)
            {
                _lines[i * 2] = _drops[i];
                _lines[i * 2 + 1] = _drops[i] + new Vector2(-0.2f, 1) * _len[i];
            }
            DrawMultiline(_lines, new Color(0.6f, 0.75f, 0.9f, 0.18f), 1);
        }

        private Vector2[] _lines;
    }
}
