using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>
/// Draws a run: the map in 16x16 chunks repainted when their tiles change,
/// static lamps on an additive layer, decals, everyone on site, particles,
/// and a soft fog of war from a one-pixel-per-tile texture. Turns the run's
/// events into sound, particles and screen shake.
/// </summary>
public partial class RunView : Node2D
{
    /// <summary>What a stolen uniform looks like on you: a guard of the site's faction.</summary>
    /// <summary>The rival crew: lime on black, so you can tell them from the site's people at a glance.</summary>
    private static readonly Color RivalColour = new(0.65f, 1f, 0.2f);
    private static readonly Art.Look RivalLook = new() { Body = new Color(0.1f, 0.11f, 0.12f), Trim = new Color(0.65f, 1f, 0.2f), Skin = new Color(0.7f, 0.55f, 0.45f), Hair = new Color(0.15f, 0.12f, 0.1f) };
    private static readonly Actor DisguiseLook = new() { Def = Tables.Enemy(EnemyKind.Guard) };
    public const int T = LevelArt.T;
    public Run R;
    public Camera2D Cam;
    public V2 Aim;
    public bool ShowArc;
    public float HitMarkT;
    public bool HitKill;
    private TileChunk[] _chunks;
    private int[] _drawn;
    private Node2D _lights;
    /// <summary>Each lamp's glow, by its index in <see cref="Level.Lights"/> (so a lamp shot out can go dark).</summary>
    private readonly List<Sprite2D> _lamps = new();
    private DecalLayer _decals;
    private TileLife _life;
    private float _pickupAt = -10;
    private int _pickupStack;
    private Entities _entities;
    public FxLayer Fx, Glow;
    private FogLayer _fog;
    private Top _top;
    private float _shake;
    private Vector2 _camPos;
    public float Shake { get => _shake; set => _shake = MathF.Max(_shake, value); }

    public void Bind(Run run)
    {
        R = run;
        foreach (Node n in GetChildren()) n.QueueFree();
        Level L = run.L;
        AddChild(new CanvasModulate { Color = new Color(0.78f, 0.78f, 0.9f) });
        _chunks = new TileChunk[L.ChunksX * L.ChunksY];
        _drawn = new int[_chunks.Length];
        for (int cy = 0; cy < L.ChunksY; cy++)
        for (int cx = 0; cx < L.ChunksX; cx++)
        {
            var c = new TileChunk { L = L, CX = cx, CY = cy };
            _chunks[cy * L.ChunksX + cx] = c;
            _drawn[cy * L.ChunksX + cx] = -1;
            AddChild(c);
        }
        _lamps.Clear();
        _lights = new Node2D { Material = Art.Additive, Name = "Lights" };
        AddChild(_lights);
        foreach (var (pos, colour, radius) in L.Lights)
        {
            var s = new Sprite2D { Texture = Art.Glow, Position = Art.G(pos, T), Modulate = Art.C(colour) with { A = 0.32f } };
            s.Scale = Vector2.One * radius * T * 2 / Art.Glow.GetWidth();
            _lights.AddChild(s);
            _lamps.Add(s);
        }
        _life = new TileLife { V = this, Name = "TileLife" };
        _life.Scan(L);
        AddChild(_life);
        _decals = new DecalLayer { Name = "Decals" };
        AddChild(_decals);
        _entities = new Entities { V = this, Name = "Entities" };
        AddChild(_entities);
        Fx = new FxLayer { Name = "Fx" };
        AddChild(Fx);
        Glow = new FxLayer { Material = Art.Additive, AdditiveLayer = true, Name = "Glow" };
        AddChild(Glow);
        _fog = new FogLayer { R = run, Name = "Fog" };
        AddChild(_fog);
        _top = new Top { V = this, Name = "Top" };
        AddChild(_top);
        Cam = new Camera2D { Zoom = new Vector2(1.45f, 1.45f), PositionSmoothingEnabled = false };
        AddChild(Cam);
        Cam.MakeCurrent();
        _camPos = Art.G(run.Player.Pos, T);
        Cam.Position = _camPos;
    }

    /// <summary>What the camera sees, in world pixels.</summary>
    public Rect2 ViewRect()
    {
        Vector2 half = GetViewportRect().Size / Cam.Zoom / 2;
        return new Rect2(Cam.GetScreenCenterPosition() - half, half * 2);
    }

    public V2 MouseWorld()
    {
        Vector2 m = GetGlobalTransformWithCanvas().AffineInverse() * Controls.MousePos(GetViewport());
        return new V2(m.X / T, m.Y / T);
    }

    private bool _dirty;

    /// <summary>Repaint once per rendered frame, however many steps ran (see CityView._Process).</summary>
    public override void _Process(double delta)
    {
        if (R == null) return;
        PaintChunks();
        if (!_dirty) return;
        _dirty = false;
        using (Perf.Time("run.fog.refresh")) _fog.Refresh();
        _entities.QueueRedraw();
        _life.QueueRedraw();
        _top.QueueRedraw();
    }

    /// <summary>
    /// Repaint changed chunks near the camera at once, and the rest (a door
    /// opened across the site, the first paint of a new site) a couple a frame:
    /// entering a site no longer paints all of it in one frame.
    /// </summary>
    private void PaintChunks()
    {
        Level L = R.L;
        Rect2 near = ViewRect().Grow(Level.Chunk * T);
        // A chunk is a few ms of drawing. Unpainted ones in view go at once (no black
        // squares); one already painted can show its old picture a frame longer, so a
        // door opening on a chunk corner (four chunks) is spread over two frames.
        int spare = 2, repaints = 2;
        for (int i = 0; i < _chunks.Length; i++)
        {
            if (_drawn[i] == L.ChunkVersion[i]) continue;
            TileChunk c = _chunks[i];
            var r = new Rect2(c.CX * Level.Chunk * T, c.CY * Level.Chunk * T, Level.Chunk * T, Level.Chunk * T);
            if (!near.Intersects(r) ? spare-- <= 0 : c.Armed && repaints-- <= 0) continue;
            _drawn[i] = L.ChunkVersion[i];
            c.Armed = true;
            c.QueueRedraw();
        }
    }

    public void Frame(float dt)
    {
        if (R == null) return;
        using (Perf.Time("run.events")) HandleEvents();
        Fx.Step(dt);
        Glow.Step(dt);
        AmbientFire(dt);
        _dirty = true;

        // Camera: follow, lean towards the mouse, shake.
        Vector2 target = Art.G(R.Projecting && R.AstralForm != null ? R.AstralForm.Pos : R.Rigging && R.Drone != null ? R.Drone.Pos : R.Player.Pos, T);
        Vector2 mouse = Controls.MousePos(GetViewport()) - GetViewportRect().Size / 2;
        target += (mouse * 0.25f / Cam.Zoom.X).LimitLength(170);
        // Keep the view on the map rather than half on the void past its edge.
        Vector2 half = GetViewportRect().Size / Cam.Zoom / 2;
        var (bx0, bx1) = R.Plan.Regions.Count > 0 ? R.Plan.Bounds(R.PlayerFloor) : (R.Downstairs ? R.Plan.MainW : 0, R.Downstairs ? R.L.W : R.Plan.MainW);
        float mapR = bx1 * T, mapL = bx0 * T, mapB = R.L.H * T;
        target.X = mapR - mapL > half.X * 2 ? Math.Clamp(target.X, mapL + half.X - T, mapR - half.X + T) : (mapL + mapR) / 2;
        target.Y = mapB > half.Y * 2 ? Math.Clamp(target.Y, half.Y - T, mapB - half.Y + T * 3) : mapB / 2;
        if (_camPos.DistanceTo(target) > 12 * T) _camPos = target;
        _camPos = _camPos.Lerp(target, MathF.Min(1, dt * 8));
        _shake = MathF.Max(0, _shake - dt * 30);
        HitMarkT = MathF.Max(0, HitMarkT - dt);
        float sh = Profile.Current.ScreenShake ? _shake : 0;
        Cam.Position = _camPos + new Vector2(Fx.Rand(-1, 1), Fx.Rand(-1, 1)) * sh;
    }

    private float _fireT;

    private void AmbientFire(float dt)
    {
        _fireT += dt;
        if (_fireT < 0.06f) return;
        _fireT = 0;
        foreach (var kv in R.Fire)
        {
            var p = new Pt(kv.Key % R.L.W, kv.Key / R.L.W);
            if (!R.SeenTile(p)) continue;
            Vector2 at = Art.G(p.Centre, T) + new Vector2(Fx.Rand(-12, 12), Fx.Rand(-12, 12));
            Glow.Add(FxKind.Flash, at, Vector2.Zero, 0.35f, Fx.Rand(14, 26), new Color(1f, 0.45f, 0.1f, 0.5f));
            if (Fx.Rand(0, 1) < 0.3f) Glow.Add(FxKind.Ember, at, new Vector2(Fx.Rand(-20, 20), Fx.Rand(-60, -20)), 0.5f, 1.5f, new Color(1, 0.7f, 0.2f));
            if (Fx.Rand(0, 1) < 0.15f) Fx.Add(FxKind.Smoke, at, new Vector2(Fx.Rand(-10, 10), Fx.Rand(-30, -10)), 1.6f, 8, new Color(0.1f, 0.1f, 0.1f, 0.4f));
        }
    }

    // ---- events ---------------------------------------------------------------------

    private float Vol(V2 at, float range = 22)
    {
        float d = V2.Distance(at, R.Player.Pos);
        return MathF.Max(0, 1 - d / range);
    }

    private void Play(Sound s, V2 at, float vol = 1, float pitch = 1, float range = 22)
    {
        float v = Vol(at, range) * vol;
        if (v > 0.02f) Sfx.Instance?.Play(s, pitch * (R.OverclockT > 0 ? 0.6f : 1), v);
    }

    private void HandleEvents()
    {
        foreach (RunEvent e in R.Events)
        {
            Vector2 p = Art.G(e.Pos, T);
            bool seen = R.Seen(e.Pos);
            switch (e.Kind)
            {
                case Ev.Shot:
                {
                    var wc = (WeaponClass)(int)e.Value;
                    bool mine = e.A == R.Player.Id;
                    Sound s = wc switch
                    {
                        WeaponClass.Pistol => mine && (R.C.NoiseOf(R.CurrentWeapon) < 6) ? Sound.Silenced : Sound.Pistol,
                        WeaponClass.Smg => mine && R.CurrentWeapon != null && R.C.NoiseOf(R.CurrentWeapon) < 6 ? Sound.Silenced : Sound.Smg,
                        WeaponClass.Shotgun => Sound.Shotgun, WeaponClass.Rifle => Sound.Rifle, WeaponClass.Sniper => Sound.Sniper,
                        WeaponClass.Lmg => Sound.Lmg, WeaponClass.Launcher => Sound.Rocket, _ => Sound.Pistol,
                    };
                    if (!mine && s is Sound.Pistol or Sound.Smg or Sound.Rifle or Sound.Lmg) s = Sound.EnemyShot;
                    Actor shooter = R.ActorById(e.A);
                    if (shooter != null && shooter.Is(EnemyKind.Mage)) s = Sound.Fireball;
                    Play(s, e.Pos, mine ? 0.8f : 0.7f, 1, 30);
                    if (seen || mine)
                    {
                        Vector2 d = Art.G(e.Dir);
                        Glow.Add(FxKind.Flash, p, Vector2.Zero, 0.06f, wc == WeaponClass.Shotgun ? 34 : 24, new Color(1, 0.8f, 0.4f, 0.9f));
                        Glow.Burst(FxKind.Spark, p, 3, 300, 0.08f, 1.5f, new Color(1, 0.85f, 0.4f), d, 0.4f);
                        if (wc is not (WeaponClass.Launcher or WeaponClass.Melee)) Fx.Add(FxKind.Shell, p, new Vector2(-d.Y, d.X) * Fx.Rand(60, 120), 0.5f, 3, new Color(0.9f, 0.7f, 0.3f));
                        if (mine) Shake = wc switch { WeaponClass.Shotgun => 5, WeaponClass.Sniper => 6, WeaponClass.Launcher => 7, WeaponClass.Lmg => 2.5f, _ => 1.5f };
                    }
                    break;
                }
                case Ev.Impact:
                    if (!seen) break;
                    var tile = (Tile)e.A;
                    if (tile == Tile.Glass) break;
                    Glow.Burst(FxKind.Spark, p, 4, 200, 0.15f, 1.2f, new Color(1, 0.8f, 0.5f), -Art.G(e.Dir), 0.9f);
                    Fx.Burst(FxKind.Debris, p, 2, 60, 0.4f, 3, LevelArt.WallColour(tile));
                    Fx.Add(FxKind.Smoke, p, Vector2.Zero, 0.6f, 4, new Color(0.6f, 0.6f, 0.6f, 0.25f));
                    Play(Sound.Debris, e.Pos, 0.25f, 1.6f);
                    break;
                case Ev.Hit:
                {
                    Actor a = R.ActorById(e.A);
                    bool robot = a != null && (a.Is(EnemyKind.Drone) || a.Is(EnemyKind.Turret) || a.Is(EnemyKind.Camera));
                    if (seen || (a?.IsPlayer ?? false))
                    {
                        if (robot) Glow.Burst(FxKind.Spark, p, 6, 220, 0.2f, 1.5f, new Color(0.6f, 0.9f, 1f));
                        else
                        {
                            Fx.Burst(FxKind.Blood, p, 5, 90, 0.4f, 2.2f, new Color(0.6f, 0.02f, 0.05f, 0.9f));
                            if (e.Value > 12) _decals.Add(p + new Vector2(Fx.Rand(-6, 6), Fx.Rand(-6, 6)), Fx.Rand(3, 7), new Color(0.35f, 0.02f, 0.04f, 0.7f));
                        }
                        var t = Fx.Add(FxKind.Text, p + new Vector2(0, -14), new Vector2(0, -40), 0.7f, e.Text == "crit" ? 18 : 14,
                            a != null && a.IsPlayer ? UITheme.Bad : e.Text == "crit" ? UITheme.Yellow : UITheme.Text);
                        t.Text = $"{e.Value:0}{(e.Text == "crit" ? "!" : "")}";
                        t.Drag = 2;
                    }
                    Play(e.Text == "crit" ? Sound.Crit : Sound.Hit, e.Pos, 0.6f);
                    if (a != null && a.IsPlayer) Shake = 6;
                    break;
                }
                case Ev.Death:
                {
                    Actor a = R.ActorById(e.A);
                    if (a == null) break;
                    bool robot = a.Is(EnemyKind.Drone) || a.Is(EnemyKind.Turret) || a.Is(EnemyKind.Camera);

                    if (robot) _decals.Body(a, R.Job.Faction);
                    Play(robot ? Sound.Debris : Sound.Death, e.Pos, 0.7f);
                    break;
                }
                case Ev.Explosion:
                {
                    var kind = (GrenadeKind)e.A;
                    float rad = e.Value * T;
                    if (kind == GrenadeKind.Fire)
                    {
                        Play(Sound.Ignite, e.Pos, 1, 1, 30);
                        Glow.Add(FxKind.Flash, p, Vector2.Zero, 0.5f, rad * 1.4f, new Color(1, 0.5f, 0.1f, 0.8f));
                        break;
                    }
                    Play(Sound.Explosion, e.Pos, 1, Fx.Rand(0.9f, 1.1f), 45);
                    Glow.Add(FxKind.Flash, p, Vector2.Zero, 0.35f, rad * 2.2f, new Color(1, 0.7f, 0.35f, 1));
                    Glow.Add(FxKind.Flash, p, Vector2.Zero, 0.15f, rad * 1.2f, new Color(1, 1, 0.9f, 1));
                    Glow.Burst(FxKind.Spark, p, 24, 600, 0.35f, 2, new Color(1, 0.75f, 0.3f));
                    Fx.Burst(FxKind.Debris, p, 14, 260, 0.8f, 4, new Color(0.3f, 0.3f, 0.3f));
                    for (int k = 0; k < 8; k++) Fx.Add(FxKind.Smoke, p + new Vector2(Fx.Rand(-rad, rad), Fx.Rand(-rad, rad)) * 0.4f, new Vector2(Fx.Rand(-30, 30), Fx.Rand(-30, 30)), Fx.Rand(1.2f, 2.4f), Fx.Rand(10, 22), new Color(0.12f, 0.12f, 0.13f, 0.65f));
                    var ring = Fx.Add(FxKind.Ring, p, new Vector2(rad * 3, 0), 0.3f, rad * 0.3f, new Color(1, 0.9f, 0.7f, 0.6f));
                    ring.Drag = 0;
                    _decals.Add(p, rad * 0.55f, new Color(0.02f, 0.02f, 0.02f, 0.55f));
                    Shake = 10 + 14 * Vol(e.Pos, 20);
                    break;
                }
                case Ev.Gas:
                    Play(Sound.Ignite, e.Pos, 0.6f, 0.6f);
                    Fx.Burst(FxKind.Smoke, p, 10, 90, 1.2f, 12, (GasKind)e.A == GasKind.Coolant ? new Color(0.85f, 0.95f, 1f, 0.5f) : new Color(0.5f, 0.9f, 0.35f, 0.5f));
                    break;
                case Ev.LampOut:
                {
                    if (e.A >= 0 && e.A < _lamps.Count) _lamps[e.A].Visible = false;
                    Play(Sound.Glass, e.Pos, 0.55f);
                    Glow.Burst(FxKind.Spark, p, 14, 220, 0.4f, 1.6f, new Color(1f, 0.9f, 0.6f));
                    Fx.Burst(FxKind.Debris, p, 6, 90, 0.5f, 1.8f, new Color(0.8f, 0.9f, 1f, 0.8f));
                    _decals.Add(p, 5, new Color(0.03f, 0.03f, 0.03f, 0.5f));
                    break;
                }
                case Ev.Broken:
                {
                    var was = (Tile)e.A;
                    if (was == Tile.Glass)
                    {
                        Play(Sound.Glass, e.Pos, 0.8f);
                        Fx.Burst(FxKind.Debris, p, 10, 140, 0.6f, 2.5f, new Color(0.7f, 0.95f, 1f, 0.8f));
                    }
                    else
                    {
                        Play(Sound.Debris, e.Pos, 0.8f);
                        Color c = was switch { Tile.Crate or Tile.Door or Tile.Desk => new Color(0.45f, 0.33f, 0.18f), Tile.Barrel => new Color(0.6f, 0.1f, 0.1f), _ => LevelArt.WallColour(was) };
                        Fx.Burst(FxKind.Debris, p, 12, 160, 0.7f, 4, c);
                        for (int k = 0; k < 3; k++) Fx.Add(FxKind.Smoke, p, new Vector2(Fx.Rand(-20, 20), Fx.Rand(-20, 20)), 1.4f, 10, new Color(0.5f, 0.5f, 0.5f, 0.35f));
                        if (was is Tile.Rack or Tile.Vat) Glow.Burst(FxKind.Spark, p, 20, 300, 0.5f, 2, new Color(0.5f, 1f, 0.6f));
                    }
                    break;
                }
                case Ev.Throw: Play(Sound.Throw, e.Pos); break;
                case Ev.Bounce: Play(Sound.Bounce, e.Pos, 0.6f); break;
                case Ev.Reload: Play(Sound.Reload, e.Pos, 0.7f); break;
                case Ev.Empty: Sfx.Instance?.Play(Sound.Empty); break;
                case Ev.Swap: Sfx.Instance?.Play(Sound.Reload, 1.6f, 0.4f); break;
                case Ev.Door: Play(Sound.Door, e.Pos, 0.5f); break;
                case Ev.Dash:
                    Play(Sound.Dash, e.Pos);
                    for (int k = 0; k < 5; k++) Fx.Add(FxKind.Smoke, p, -Art.G(e.Dir) * 60 + new Vector2(Fx.Rand(-20, 20), Fx.Rand(-20, 20)), 0.5f, 5, new Color(0.6f, 0.9f, 1f, 0.3f));
                    break;
                case Ev.Melee:
                    Play(e.Value > 0 ? Sound.Slash : Sound.Swing, e.Pos, 0.8f);
                    break;
                case Ev.Pickup:
                {
                    Play(e.Value == (int)PickupKind.Cash ? Sound.Coin : Sound.Pickup, e.Pos);
                    // Several at once (a safe's cash and its bonds) stack instead of printing over each other.
                    if (R.Time - _pickupAt > 0.4f) _pickupStack = 0;
                    _pickupAt = R.Time;
                    var t = Fx.Add(FxKind.Text, p + new Vector2(0, -16 - 18 * _pickupStack++), new Vector2(0, -30), 1.4f, 15, UITheme.Yellow);
                    t.Text = e.Text;
                    t.Drag = 1;
                    break;
                }
                case Ev.Alarm: Sfx.Instance?.Play(Sound.Alarm, 1, 0.8f); Shake = 3; break;
                case Ev.Alert: Play(Sound.Alert, e.Pos, 0.6f); break;
                case Ev.Panic: Play(Sound.Scream, e.Pos, 0.45f); break;
                case Ev.Spotted: Play(Sound.Beep, e.Pos, 1); break;
                case Ev.Heal:
                    Sfx.Instance?.Play(Sound.Heal);
                    Glow.Burst(FxKind.Ember, p, 14, 60, 0.8f, 2, UITheme.Good);
                    break;
                case Ev.Hack:
                    Play(Sound.Hack, e.Pos, 1, 1, 40);
                    Glow.Burst(FxKind.Spark, p, 16, 160, 0.5f, 1.5f, UITheme.Violet);
                    break;
                case Ev.Ability:
                    Sfx.Instance?.Play(Sound.Ability);
                    var ab = (Ability)(int)e.Value;
                    var rr = Glow.Add(FxKind.Ring, p, new Vector2(400, 0), 0.4f, 10, ab == Ability.Cloak ? new Color(0.6f, 0.8f, 1f, 0.8f) : UITheme.Cyan);
                    rr.Drag = 0;
                    break;
                case Ev.Flash:
                    Play(Sound.Flash, e.Pos, 1, 1, 30);
                    Glow.Add(FxKind.Flash, p, Vector2.Zero, 0.5f, e.Value * T * 2.5f, new Color(1, 1, 1, 1));
                    break;
                case Ev.Emp:
                {
                    Play(Sound.Emp, e.Pos, 1, 1, 30);
                    var ring = Glow.Add(FxKind.Ring, p, new Vector2(e.Value * T * 4, 0), 0.4f, 6, new Color(0.4f, 0.7f, 1f, 1));
                    ring.Drag = 0;
                    Glow.Burst(FxKind.Spark, p, 30, 300, 0.4f, 1.2f, new Color(0.5f, 0.8f, 1f));
                    break;
                }
                case Ev.Ignite: Play(Sound.Ignite, e.Pos, 0.5f); break;
                case Ev.Telegraph: Play(Sound.Charge, e.Pos, 0.7f); break;
                case Ev.Objective: Sfx.Instance?.Play(Sound.Objective); break;
                case Ev.Wave: Sfx.Instance?.Play(Sound.Radio); break;
                case Ev.Whistle:
                {
                    Sfx.Instance?.Play(Sound.Beep, 0.55f, 0.8f);
                    var ring = Fx.Add(FxKind.Ring, p, new Vector2(7 * T * 3, 0), 0.35f, 8, UITheme.Yellow with { A = 0.5f });
                    ring.Drag = 0;
                    break;
                }
                case Ev.Stairs: Sfx.Instance?.Play(Sound.Door, 0.8f); break;
                case Ev.Carry: Sfx.Instance?.Play(Sound.Swing, 0.6f, 0.6f); break;
                case Ev.Safe: Sfx.Instance?.Play(Sound.Coin); Sfx.Instance?.Play(Sound.Door, 0.7f); break;
                case Ev.HitMark: HitMarkT = 0.15f; HitKill = e.Value > 0; break;
                case Ev.EnemyThrow:
                {
                    Play(Sound.Throw, e.Pos);
                    var t = Fx.Add(FxKind.Text, p + new Vector2(0, -20), new Vector2(0, -25), 1.2f, 18, UITheme.Bad);
                    t.Text = "GRENADE!";
                    break;
                }
                case Ev.Spell:
                {
                    var spell = (Ability)(int)e.Value;
                    Vector2 to = Art.G(e.Dir, T);
                    Sfx.Instance?.Play(spell == Ability.Fireball ? Sound.Fireball : Sound.Ability, spell == Ability.Manabolt ? 1.6f : 1f);
                    Glow.Add(FxKind.Flash, p, Vector2.Zero, 0.3f, 40, new Color(0.7f, 0.4f, 1f, 0.9f));
                    if (spell == Ability.Manabolt)
                    {
                        for (int k = 0; k <= 10; k++)
                        {
                            Vector2 q = p.Lerp(to, k / 10f) + new Vector2(Fx.Rand(-5, 5), Fx.Rand(-5, 5));
                            Glow.Add(FxKind.Flash, q, Vector2.Zero, 0.25f, 14, new Color(0.75f, 0.45f, 1f, 0.9f));
                        }
                        Glow.Burst(FxKind.Spark, to, 14, 200, 0.3f, 1.5f, new Color(0.85f, 0.6f, 1f));
                    }
                    break;
                }
            }
        }
    }

    // ---- layers ---------------------------------------------------------------------

    private partial class TileChunk : Node2D
    {
        public Level L;
        public int CX, CY;
        /// <summary>Paints nothing until the view asks (see PaintChunks): no whole-site paint on the first frame.</summary>
        public bool Armed;

        public override void _Draw()
        {
            if (!Armed) return;
            using var _perf = Perf.Time("run.draw.chunk");
            int x0 = CX * Level.Chunk, y0 = CY * Level.Chunk;
            for (int y = y0; y < Math.Min(L.H, y0 + Level.Chunk); y++)
            for (int x = x0; x < Math.Min(L.W, x0 + Level.Chunk); x++)
                LevelArt.DrawTile(this, L, x, y);
        }
    }

    /// <summary>
    /// The site's machinery, alive: rack lights blinking, vats bubbling, the
    /// mainframe's running lights, console screens, a pulsing dance floor --
    /// and whatever the runner is working on (a safe's dial swinging and its
    /// tumblers falling, a console's screen scrolling, a locker rattling, a
    /// maglock's keypad sparking), and the fixtures already used (a safe hanging
    /// open, a locker ajar, a console LOOPED). Drawn every frame over the
    /// tile chunks, for the tiles in view only.
    /// </summary>
    private partial class TileLife : Node2D
    {
        public RunView V;
        private readonly List<Pt> _machines = new(), _dance = new();
        private static Vector2 D(float a) => new(MathF.Cos(a), MathF.Sin(a));

        public void Scan(Level L)
        {
            for (int y = 0; y < L.H; y++)
            for (int x = 0; x < L.W; x++)
            {
                Tile t = L[x, y];
                if (t is Tile.Rack or Tile.Vat or Tile.Mainframe or Tile.Console or Tile.Lift) _machines.Add(new Pt(x, y));
                else if (L.F[L.Idx(x, y)] == Floor.Dance && t == Tile.Floor) _dance.Add(new Pt(x, y));
            }
            // Ritual circles: one per patch of chalked floor, round its middle.
            var done = new bool[L.W * L.H];
            for (int y = 0; y < L.H; y++)
            for (int x = 0; x < L.W; x++)
            {
                if (done[L.Idx(x, y)] || L.F[L.Idx(x, y)] != Floor.Ritual) continue;
                int x0 = x, x1 = x, y0 = y, y1 = y;
                var stack = new Stack<Pt>();
                stack.Push(new Pt(x, y));
                done[L.Idx(x, y)] = true;
                while (stack.Count > 0)
                {
                    Pt p = stack.Pop();
                    x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); y0 = Math.Min(y0, p.Y); y1 = Math.Max(y1, p.Y);
                    foreach (Pt d in Pt.Dirs4)
                    {
                        Pt n = p + d;
                        if (!L.In(n) || done[L.Idx(n.X, n.Y)] || L.F[L.Idx(n.X, n.Y)] != Floor.Ritual) continue;
                        done[L.Idx(n.X, n.Y)] = true;
                        stack.Push(n);
                    }
                }
                _circles.Add((new Vector2((x0 + x1 + 1) * T / 2f, (y0 + y1 + 1) * T / 2f), MathF.Min(x1 - x0 + 1, y1 - y0 + 1) * T / 2f - 3));
            }
        }

        private readonly List<(Vector2 c, float r)> _circles = new();

        /// <summary>A summoning circle in chalk: two rings, a star, five candles flickering.</summary>
        private void Circle(Vector2 c, float rad, float time)
        {
            var chalk = new Color(0.88f, 0.88f, 0.94f, 0.65f);
            DrawArc(c, rad, 0, MathF.Tau, 48, chalk, 2);
            DrawArc(c, rad - 7, 0, MathF.Tau, 48, chalk with { A = 0.4f }, 1);
            var star = new Vector2[6];
            for (int k = 0; k < 5; k++) star[k] = c + new Vector2(MathF.Cos(-MathF.PI / 2 + k * 2 * MathF.Tau / 5), MathF.Sin(-MathF.PI / 2 + k * 2 * MathF.Tau / 5)) * (rad - 8);
            star[5] = star[0];
            DrawPolyline(star, chalk with { A = 0.5f }, 1.5f);
            for (int k = 0; k < 5; k++)
            {
                Vector2 cp = c + new Vector2(MathF.Cos(-MathF.PI / 2 + k * MathF.Tau / 5), MathF.Sin(-MathF.PI / 2 + k * MathF.Tau / 5)) * (rad + 1);
                DrawCircle(cp, 3, new Color(0.9f, 0.86f, 0.72f));
                DrawCircle(cp - new Vector2(0, 3 + MathF.Sin(time * 9 + k) * 0.6f), 1.6f, new Color(1f, 0.7f, 0.2f));
            }
        }

        public override void _Draw()
        {
            using var _perf = Perf.Time("run.draw.life");
            Run r = V.R;
            if (r == null) return;
            float time = r.Time;
            Rect2 view = V.ViewRect().Grow(T * 2);
            bool In(Pt p) => view.HasPoint(new Vector2(p.X * T + T / 2f, p.Y * T + T / 2f));
            foreach (var (c, rad) in _circles)
                if (view.HasPoint(c) && r.SeenTile(new Pt((int)(c.X / T), (int)(c.Y / T)))) Circle(c, rad, time);
            foreach (Pt p in _dance)
            {
                if (!In(p)) continue;
                // Panels lighting in waves across the floor, to a beat.
                float w = MathF.Sin(time * 3.2f + p.X * 0.7f + p.Y * 0.45f) * 0.5f + 0.5f;
                if (w < 0.72f) continue;
                Color c = (p.X + p.Y) % 3 == 0 ? UITheme.Magenta : (p.X + p.Y) % 3 == 1 ? UITheme.Cyan : new Color(1f, 0.85f, 0.2f);
                DrawRect(new Rect2(p.X * T + 3, p.Y * T + 3, T - 6, T - 6), c with { A = (w - 0.72f) * 1.3f });
            }
            foreach (Pt p in _machines)
            {
                if (!In(p)) continue;
                Tile t = r.L[p];
                Vector2 o = new(p.X * T, p.Y * T);
                float h = Art.Hash(p.X, p.Y);
                switch (t)
                {
                    case Tile.Rack:
                        for (int k = 0; k < 5; k++)
                        {
                            bool on = MathF.Sin(time * (2 + Art.Hash(p.X, p.Y, k) * 9) + k * 1.7f) > 0.2f;
                            if (on) DrawRect(new Rect2(o + new Vector2(7 + (k % 2) * 10, 6 + k * 4.5f), new Vector2(4, 2)), Art.Hash(p.X, p.Y, k) > 0.5f ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.8f, 0.3f));
                        }
                        break;
                    case Tile.Vat:
                        // Bubbles rising and popping in the green.
                        for (int k = 0; k < 3; k++)
                        {
                            float u = (time * (0.5f + 0.2f * k) + h + k * 0.33f) % 1f;
                            Vector2 c = o + new Vector2(T / 2f + MathF.Sin(k * 2.3f + h * 6) * 5, T / 2f + 6 - u * 12);
                            DrawArc(c, 1.2f + u * 2.2f, 0, MathF.Tau, 8, new Color(0.75f, 1f, 0.75f, 0.8f * (1 - u)), 1);
                        }
                        break;
                    case Tile.Mainframe:
                    {
                        // A light running along the front, and the drive bays blinking.
                        int at = (int)(time * 6 + h * 10) % 3;
                        for (int k = 0; k < 3; k++) DrawRect(new Rect2(o + new Vector2(6 + k * 7, 20), new Vector2(5, 5)), k == at ? new Color(1f, 0.6f, 1f) : UITheme.Magenta.Darkened(0.5f));
                        float scan = (time * 0.8f + h) % 1f;
                        DrawRect(new Rect2(o + new Vector2(6, 6 + scan * 9), new Vector2(T - 12, 1)), new Color(1, 1, 1, 0.5f));
                        break;
                    }
                    case Tile.Console when !r.Used(p) && !(r.WorkingNow && r.WorkTile == p):
                        // Screens flickering through the camera feeds.
                        for (int k = 0; k < 3; k++)
                            if (MathF.Sin(time * 1.3f + k * 2 + h * 9) > 0.6f) DrawRect(new Rect2(o + new Vector2(5 + k * 8, 7), new Vector2(6, 5)), new Color(0.8f, 1f, 1f, 0.35f));
                        break;
                    case Tile.Lift:
                        if (MathF.Sin(time * 4 + h * 6) > 0) DrawRect(new Rect2(o + new Vector2(T - 7, T / 2f - 4), new Vector2(4, 8)), new Color(0.8f, 1f, 1f, 0.8f));
                        break;
                }
            }
            // Used up: hanging open, ajar, looped.
            foreach (Pt p in r.UsedFixtures)
            {
                if (!In(p)) continue;
                Vector2 o = new(p.X * T, p.Y * T);
                switch (r.L[p])
                {
                    case Tile.Safe:
                        DrawRect(new Rect2(o + new Vector2(6, 6), new Vector2(T - 12, T - 12)), new Color(0.05f, 0.05f, 0.06f));
                        DrawRect(new Rect2(o + new Vector2(6, 6), new Vector2(T - 12, T - 12)), new Color(0.2f, 0.2f, 0.22f), false, 1);
                        // The door swung out to the side, dial and all.
                        DrawRect(new Rect2(o + new Vector2(T - 6, 4), new Vector2(6, T - 8)), new Color(0.42f, 0.44f, 0.48f));
                        DrawCircle(o + new Vector2(T - 3, T / 2f), 2.2f, new Color(0.2f, 0.2f, 0.22f));
                        break;
                    case Tile.Locker:
                        DrawRect(new Rect2(o + new Vector2(6, 3), new Vector2(T / 2f - 7, T - 6)), new Color(0.06f, 0.07f, 0.06f));
                        DrawLine(o + new Vector2(6, 3), o + new Vector2(1, 8), new Color(0.35f, 0.42f, 0.35f), 3);
                        break;
                    case Tile.Console:
                        DrawRect(new Rect2(o + new Vector2(5, 7), new Vector2(T - 10, 5)), new Color(0.05f, 0.2f, 0.08f));
                        UITheme.Write(this, o + new Vector2(0, 12), "LOOP", 7, UITheme.Good, HorizontalAlignment.Center, T, mono: true, outline: 1);
                        break;
                }
            }
            if (r.WorkingNow) DrawWork(r, r.WorkTile, r.WorkFrac, time);
        }

        /// <summary>The thing in your hands, while you work on it.</summary>
        private void DrawWork(Run r, Pt p, float frac, float time)
        {
            Vector2 o = new(p.X * T, p.Y * T), c = o + new Vector2(T / 2f, T / 2f);
            switch (r.L[p])
            {
                case Tile.Safe:
                {
                    // The dial swings one way, then the other, settling as each tumbler falls.
                    float ang = MathF.Sin(time * 5.5f) * 2.4f + MathF.Sin(time * 13) * 0.3f;
                    DrawCircle(c, 6.5f, new Color(0.16f, 0.16f, 0.18f));
                    for (int k = 0; k < 12; k++)
                    {
                        float a = ang + k / 12f * MathF.Tau;
                        DrawLine(c + D(a) * 4.5f, c + D(a) * 6.2f, new Color(0.8f, 0.8f, 0.85f), k == 0 ? 2 : 1);
                    }
                    DrawLine(c, c + D(ang) * 5, UITheme.Yellow, 2);
                    DrawArc(c, 9.5f, -MathF.PI / 2, -MathF.PI / 2 + MathF.Tau * frac, 24, UITheme.Yellow, 2);
                    // Three tumblers: lit as each falls into place.
                    for (int k = 0; k < 3; k++)
                    {
                        bool done = frac >= (k + 1) / 3f;
                        DrawRect(new Rect2(o + new Vector2(8 + k * 6, T - 8), new Vector2(4, 3)), done ? UITheme.Good : new Color(0.25f, 0.25f, 0.28f));
                    }
                    break;
                }
                case Tile.Console:
                {
                    // Lines of code scrolling up the screens, and a bar filling.
                    DrawRect(new Rect2(o + new Vector2(4, 6), new Vector2(T - 8, 8)), new Color(0.02f, 0.06f, 0.08f));
                    for (int k = 0; k < 4; k++)
                    {
                        float w = 4 + Art.Hash((int)(time * 12) + k, p.X) * 16;
                        DrawRect(new Rect2(o + new Vector2(5, 7 + k * 2), new Vector2(w, 1)), UITheme.Term with { A = 0.9f });
                    }
                    DrawRect(new Rect2(o + new Vector2(6, 16), new Vector2((T - 12) * frac, 4)), UITheme.Good);
                    break;
                }
                case Tile.Locker:
                {
                    // The door rattles as you work the lock.
                    float j = MathF.Sin(time * 40) * 1.2f;
                    DrawRect(new Rect2(o + new Vector2(5 + j, 2), new Vector2(T / 2f - 5, T - 4)), new Color(0.34f, 0.4f, 0.34f));
                    DrawArc(c, 13, -MathF.PI / 2, -MathF.PI / 2 + MathF.Tau * frac, 24, UITheme.Yellow, 2);
                    break;
                }
                case Tile.LockedDoor:
                {
                    // The maglock's keypad, digits flickering through, and sparks off the bypass.
                    DrawRect(new Rect2(c - new Vector2(6, 5), new Vector2(12, 10)), new Color(0.05f, 0.05f, 0.06f));
                    for (int k = 0; k < 4; k++)
                    {
                        bool lit = k < (int)(frac * 4) || Art.Hash((int)(time * 15), k) > 0.6f;
                        DrawRect(new Rect2(c + new Vector2(-5 + k * 2.6f, -3), new Vector2(2, 3)), lit ? (k < (int)(frac * 4) ? UITheme.Good : UITheme.Yellow) : new Color(0.2f, 0.05f, 0.05f));
                    }
                    DrawRect(new Rect2(c + new Vector2(-5, 2), new Vector2(10 * frac, 2)), UITheme.Good);
                    if (Art.Hash((int)(time * 20), p.Y) > 0.7f)
                        for (int k = 0; k < 3; k++) DrawLine(c + new Vector2(6, 0), c + new Vector2(6, 0) + D(Art.Hash((int)(time * 20), k) * MathF.Tau) * 5, new Color(1f, 0.9f, 0.4f), 1);
                    break;
                }
            }
        }
    }

    /// <summary>Blood, scorch marks and the fallen. Only ever added to.</summary>
    private partial class DecalLayer : Node2D
    {
        private readonly List<(Vector2 p, float r, Color c)> _spots = new();
        private readonly List<(Actor a, Faction f, float facing)> _bodies = new();

        public void Add(Vector2 p, float r, Color c)
        {
            if (_spots.Count > 600) _spots.RemoveAt(0);
            _spots.Add((p, r, c));
            _dirty = true;
        }

        public void Body(Actor a, Faction f)
        {
            _bodies.Add((a, f, a.Facing));
            _dirty = true;
        }

        private bool _dirty;

        /// <summary>Hits come in from the physics steps: repaint once a frame, not once a hit.</summary>
        public override void _Process(double delta)
        {
            if (!_dirty) return;
            _dirty = false;
            QueueRedraw();
        }

        public override void _Draw()
        {
            using var _perf = Perf.Time("run.draw.decals");
            foreach (var (p, r, c) in _spots) DrawCircle(p, r, c);
            foreach (var (a, f, facing) in _bodies)
            {
                Vector2 p = Art.G(a.Pos, T);
                float rad = a.Radius * T;
            if (a.Is(EnemyKind.Drone)) { Art.Drone(this, p, facing, rad, new Color(0.2f, 0.2f, 0.2f), 0); continue; }
                if (a.Is(EnemyKind.Turret)) { Art.Turret(this, p, facing, rad, new Color(0.2f, 0.2f, 0.2f)); continue; }
                if (a.Is(EnemyKind.Camera)) continue;
                Art.Look l = Art.LookFor(a, f);
                l.Body = l.Body.Darkened(0.35f);
                l.Skin = l.Skin.Darkened(0.35f);
                Art.Person(this, p + new Vector2(MathF.Cos(facing + 3.14f), MathF.Sin(facing + 3.14f)) * 4, facing + 1.4f, rad * 1.1f, l, null);
            }
        }
    }

    private partial class FogLayer : Node2D
    {
        public Run R;
        private Image _img;
        private ImageTexture _tex;
        private byte[] _data;

        public void Refresh()
        {
            Level L = R.L;
            if (_img == null)
            {
                _data = new byte[L.W * L.H * 4];
                _img = Image.CreateFromData(L.W, L.H, false, Image.Format.Rgba8, _data);
                _tex = ImageTexture.CreateFromImage(_img);
                TextureFilter = TextureFilterEnum.Linear;
            }
            for (int i = 0; i < L.W * L.H; i++)
            {
                byte a = R.Visible[i] ? (byte)0 : L.Explored[i] ? (byte)185 : (byte)250;
                _data[i * 4] = 2; _data[i * 4 + 1] = 3; _data[i * 4 + 2] = 8; _data[i * 4 + 3] = a;
            }
            _img.SetData(L.W, L.H, false, Image.Format.Rgba8, _data);
            _tex.Update(_img);
            QueueRedraw();
        }

        public override void _Draw()
        {
            using var _perf = Perf.Time("run.draw.fog");
            if (_tex == null) return;
            // Pixel centres sit on tile centres, so linear filtering feathers the edge.
            DrawTextureRect(_tex, new Rect2(0, 0, R.L.W * T, R.L.H * T), false);
        }
    }

    /// <summary>Above the fog: the extraction zone and markers that must always show.</summary>
    private partial class Top : Node2D
    {
        public RunView V;

        public override void _Draw()
        {
            using var _perf = Perf.Time("run.draw.top");
            Run r = V.R;
            Rect ex = r.Plan.Extraction;
            var rect = new Rect2(ex.X * T, ex.Y * T, ex.W * T, ex.H * T);
            float pulse = 0.5f + 0.5f * MathF.Sin(r.Time * 4);
            Color c = r.ObjectiveDone ? UITheme.Good : UITheme.Good with { A = 0.5f };
            DrawRect(rect, c with { A = 0.08f + 0.06f * pulse });
            DrawRect(rect, c with { A = 0.5f + 0.4f * pulse }, false, 2);
            UITheme.Write(this, rect.Position + new Vector2(-30, -6), r.Peaceful ? "YOUR CAR" : "EXTRACTION", 13, c, HorizontalAlignment.Center, rect.Size.X + 60, heading: true);
            // The loading dock's way out.
            if (r.Plan.BackDoor.W > 0)
            {
                Rect bd = r.Plan.BackDoor;
                var br = new Rect2(bd.X * T, bd.Y * T, bd.W * T, bd.H * T);
                DrawRect(br, c with { A = 0.08f + 0.06f * pulse });
                DrawRect(br, c with { A = 0.5f + 0.4f * pulse }, false, 2);
                UITheme.Write(this, br.Position + new Vector2(-40, -6), "DOCK EXIT", 13, c, HorizontalAlignment.Center, br.Size.X + 80, heading: true);
            }
            // A lab's clean room: the sensors' line on the floor.
            if (r.Plan.CleanRoom.W > 0 && !r.HasSuit)
            {
                Rect cr = r.Plan.CleanRoom;
                if (r.L.Explored[r.L.Idx(cr.X + cr.W / 2, cr.Y + cr.H / 2)])
                {
                    var crr = new Rect2(cr.X * T + 3, cr.Y * T + 3, cr.W * T - 6, cr.H * T - 6);
                    DrawDashedLine(crr.Position, crr.Position + new Vector2(crr.Size.X, 0), UITheme.Cyan with { A = 0.55f }, 1.5f, 8);
                    DrawDashedLine(crr.End, crr.End - new Vector2(crr.Size.X, 0), UITheme.Cyan with { A = 0.55f }, 1.5f, 8);
                    UITheme.Write(this, crr.Position + new Vector2(0, -4), "CLEAN ROOM -- SUIT ONLY", 11, UITheme.Cyan with { A = 0.8f }, HorizontalAlignment.Center, crr.Size.X, heading: true);
                }
            }
            // Reticle.
            Vector2 m = Art.G(V.Aim, T);
            Color rc = UITheme.Cyan;
            DrawArc(m, 9, 0, MathF.Tau, 20, rc with { A = 0.9f }, 1.5f);
            DrawLine(m + new Vector2(-14, 0), m + new Vector2(-5, 0), rc, 1.5f);
            DrawLine(m + new Vector2(14, 0), m + new Vector2(5, 0), rc, 1.5f);
            DrawLine(m + new Vector2(0, -14), m + new Vector2(0, -5), rc, 1.5f);
            DrawLine(m + new Vector2(0, 14), m + new Vector2(0, 5), rc, 1.5f);
            if (V.HitMarkT > 0)
            {
                Color hc = V.HitKill ? UITheme.Bad : Colors.White;
                foreach (var (dx, dy) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
                    DrawLine(m + new Vector2(dx, dy) * 6, m + new Vector2(dx, dy) * 13, hc, 2.5f);
            }
        }
    }

    /// <summary>Everyone and everything that moves, redrawn every frame.</summary>
    private partial class Entities : Node2D
    {
        public RunView V;

        public override void _Draw()
        {
            using var _perf = Perf.Time("run.draw.entities");
            Run r = V.R;
            float time = r.Time;
            // Pickups.
            foreach (Pickup k in r.Pickups)
            {
                if (k.Taken || !r.Seen(k.Pos)) continue;
                Vector2 p = Art.G(k.Pos, T);
                float bob = MathF.Sin(time * 4 + k.Pos.X) * 2;
                Color c = k.Kind switch
                {
                    PickupKind.Cash => UITheme.Yellow, PickupKind.Medkit => UITheme.Good, PickupKind.Grenade => new Color(0.6f, 0.8f, 0.4f),
                    PickupKind.Loot => UITheme.Violet, PickupKind.Suit => UITheme.Cyan, PickupKind.Badge => new Color(0.4f, 0.7f, 1f), _ => UITheme.Magenta,
                };
                Art.DrawGlow(this, p, 18, c with { A = 0.3f });
                switch (k.Kind)
                {
                    case PickupKind.Cash:
                        DrawRect(new Rect2(p + new Vector2(-7, -4 + bob), new Vector2(14, 8)), new Color(0.3f, 0.6f, 0.3f));
                        DrawRect(new Rect2(p + new Vector2(-7, -4 + bob), new Vector2(14, 8)), c, false, 1);
                        break;
                    case PickupKind.Medkit:
                        DrawRect(new Rect2(p + new Vector2(-7, -6 + bob), new Vector2(14, 12)), new Color(0.9f, 0.9f, 0.9f));
                        DrawRect(new Rect2(p + new Vector2(-1.5f, -4 + bob), new Vector2(3, 8)), UITheme.Bad);
                        DrawRect(new Rect2(p + new Vector2(-4, -1.5f + bob), new Vector2(8, 3)), UITheme.Bad);
                        break;
                    case PickupKind.Grenade:
                        DrawCircle(p + new Vector2(0, bob), 5, new Color(0.3f, 0.4f, 0.2f));
                        break;
                    case PickupKind.Suit:
                        // A folded white suit, the hood on top.
                        DrawRect(new Rect2(p + new Vector2(-7, -5 + bob), new Vector2(14, 10)), new Color(0.9f, 0.92f, 0.95f));
                        DrawCircle(p + new Vector2(0, -6 + bob), 4, new Color(0.85f, 0.9f, 0.95f));
                        DrawRect(new Rect2(p + new Vector2(-3, -8 + bob), new Vector2(6, 3)), new Color(0.3f, 0.6f, 0.8f));
                        break;
                    case PickupKind.Badge:
                        DrawRect(new Rect2(p + new Vector2(-6, -4 + bob), new Vector2(12, 8)), new Color(0.85f, 0.88f, 0.92f));
                        DrawRect(new Rect2(p + new Vector2(-6, -4 + bob), new Vector2(12, 3)), new Color(0.3f, 0.55f, 0.9f));
                        DrawRect(new Rect2(p + new Vector2(-4, 0 + bob), new Vector2(4, 3)), new Color(0.6f, 0.6f, 0.65f));
                        break;
                    default:
                        DrawRect(new Rect2(p + new Vector2(-6, -6 + bob), new Vector2(12, 12)), new Color(0.1f, 0.1f, 0.14f));
                        DrawRect(new Rect2(p + new Vector2(-6, -6 + bob), new Vector2(12, 12)), c, false, 2);
                        break;
                }
            }
            // Fire on the ground.
            foreach (var kv in r.Fire)
            {
                var t = new Pt(kv.Key % r.L.W, kv.Key / r.L.W);
                if (!r.SeenTile(t)) continue;
                float fl = 0.5f + 0.5f * MathF.Sin(time * 20 + kv.Key);
                DrawCircle(new Vector2(t.X * T + T / 2f, t.Y * T + T / 2f), T * 0.45f, new Color(1, 0.35f + 0.2f * fl, 0.05f, 0.18f));
            }
            // Vision cones of the unaware, so stealth can be read.
            foreach (Actor a in r.Actors)
            {
                if (a.Dead || a.Team != 1 || a.Def == null || !r.Shown(a) || a.Stun > 0) continue;
                if (a.Alert && !a.Is(EnemyKind.Camera)) continue;
                float range = a.Def.Vision * T;
                float half = a.Is(EnemyKind.Camera) ? 0.62f : a.Is(EnemyKind.Turret) ? 1.05f : 0.96f;
                Color cc = a.Detect > 0.3f ? new Color(1, 0.75f, 0.2f, 0.10f + a.Detect * 0.12f) : new Color(0.8f, 0.9f, 1f, 0.06f);
                if (a.Is(EnemyKind.Camera)) cc = new Color(1, 0.2f, 0.2f, 0.10f + a.Detect * 0.15f);
                Cone(Art.G(a.Pos, T), a.Facing, half, range, cc, r);
            }
            // Telegraphs: laser sights and gathering fire.
            foreach (Actor a in r.Actors)
            {
                if (a.Dead || (a.Telegraph <= 0 && !(a.Is(EnemyKind.Boss) && a.ChargeT > 1.2f)) || !r.Shown(a)) continue;
                Vector2 p = Art.G(a.Pos, T);
                if (a.Is(EnemyKind.Boss))
                {
                    if (a.ChargeT > 1.2f)
                    {
                        Vector2 end = p + Art.G(a.ChargeDir) * T * 9;
                        DrawLine(p, end, new Color(1, 0.2f, 0.1f, 0.35f + 0.3f * MathF.Sin(time * 25)), a.Radius * T * 2);
                    }
                }
                else if (a.Is(EnemyKind.Sniper))
                {
                    V2 end = a.Pos + M.Dir(a.Facing) * 40;
                    if (r.L.FirstShotBlock(a.Pos, end, out _, out V2 hit)) end = hit;
                    DrawLine(p, Art.G(end, T), new Color(1, 0.1f, 0.1f, 0.4f + 0.4f * MathF.Sin(time * 30)), 1.5f);
                }
                else Art.DrawGlow(this, p + Art.G(M.Dir(a.Facing)) * 14, 14 + (1 - a.Telegraph) * 16, new Color(1, 0.5f, 0.15f, 0.7f));
            }
            // The fallen: drawn live, so a carried body moves with you.
            foreach (Actor a in r.Actors)
            {
                if (!a.Dead || a.Escaped || a.IsPlayer || a.Def == null || a.IsAlly && a.Summoned || a.Is(EnemyKind.Drone) || a.Is(EnemyKind.Turret) || a.Is(EnemyKind.Camera) || a.Is(EnemyKind.Spirit)) continue;
                if (!r.Seen(a.Pos) && !r.L.Explored[r.L.Idx((int)a.Pos.X, (int)a.Pos.Y)]) continue;
                Vector2 bp = Art.G(a.Pos, T);
                Art.Look l = a.IsPlayer ? (r.Disguised ? Art.LookFor(DisguiseLook, r.Job.Faction) : Art.PlayerLook(r.C.Archetype, r.C.Has("eyes"))) : a.Rival ? RivalLook : a.IsMedic ? new Art.Look { Body = new Color(0.85f, 0.88f, 0.86f), Trim = new Color(0.15f, 0.75f, 0.4f), Skin = new Color(0.75f, 0.6f, 0.5f), Hair = new Color(0.2f, 0.2f, 0.2f), Heavy = true } : Art.LookFor(a, r.Job.Faction);
                float lie = a.Facing + (Art.Hash(a.Id, 7) - 0.5f) * 1.6f + MathF.PI;
                Art.Corpse(this, bp, lie, a.Radius * T * 1.1f, l, a.Kind, a.KO, a.Id);
                if (a.KO && r.Seen(a.Pos)) UITheme.Write(this, bp + new Vector2(6, -8 - MathF.Sin(time * 2) * 2), "z", 13, UITheme.Cyan with { A = 0.8f }, heading: true);
            }
            // Fixtures already used.
            for (int y = 0; y < r.L.H; y++)
            for (int x = 0; x < r.L.W; x++)
                if (r.Used(new Pt(x, y)) && r.SeenTile(new Pt(x, y))) DrawRect(new Rect2(x * T + 4, y * T + 4, T - 8, T - 8), UITheme.Good with { A = 0.5f }, false, 2);
            // People and machines.
            foreach (Actor a in r.Actors.OrderBy(a => a.Flying ? 1 : 0))
            {
                if (a.Dead || !r.Shown(a)) continue;
                DrawActor(a, r, time);
                Vector2 ap = Art.G(a.Pos, T);
                // A cleaner pushes a cart; a drinker carries the bottle.
                if (a.Cleaner)
                {
                    Vector2 back = ap - new Vector2(MathF.Cos(a.Facing), MathF.Sin(a.Facing)) * -11;
                    DrawRect(new Rect2(back - new Vector2(6, 5), new Vector2(12, 10)), new Color(0.25f, 0.45f, 0.6f));
                    DrawRect(new Rect2(back - new Vector2(6, 5), new Vector2(12, 10)), new Color(0.12f, 0.2f, 0.28f), false, 1);
                    DrawLine(back + new Vector2(-3, -5), back + new Vector2(-6, -14), new Color(0.75f, 0.65f, 0.4f), 1.5f);
                    if (a.CallT > 0) UITheme.Write(this, ap + new Vector2(-30, -14), "CALLING", 11, UITheme.Bad, HorizontalAlignment.Center, 60, heading: true);
                }
                else if (a.Drunk && !a.KO)
                {
                    Vector2 hand = ap + new Vector2(MathF.Cos(a.Facing + 1.2f), MathF.Sin(a.Facing + 1.2f)) * 9;
                    DrawRect(new Rect2(hand - new Vector2(1.5f, 3.5f), new Vector2(3, 7)), new Color(0.25f, 0.45f, 0.2f, 0.9f));
                }
                // Asleep in a bunk: the z's drift up.
                if (a.Mood == Mood.Asleep)
                    for (int k = 0; k < 2; k++)
                    {
                        float u = (time * 0.6f + k * 0.5f) % 1;
                        UITheme.Write(this, Art.G(a.Pos, T) + new Vector2(8 + u * 6, -10 - u * 14), "z", 11 + k * 2, new Color(0.8f, 0.85f, 1f, 0.8f * (1 - u)), heading: true);
                    }
            }
            // Gas hanging on the floor: frost-blue coolant, green vapour.
            foreach (GasCloud g in r.Clouds)
            {
                if (!r.Seen(g.Pos)) continue;
                Vector2 gp = Art.G(g.Pos, T);
                float fade = MathF.Min(1, (g.Life - g.T) / 2);
                Color gc = g.Kind == GasKind.Coolant ? new Color(0.8f, 0.92f, 1f) : new Color(0.45f, 0.9f, 0.3f);
                for (int k = 0; k < 6; k++)
                {
                    float a = k * 1.05f + time * (0.2f + k * 0.05f);
                    Vector2 off = new Vector2(MathF.Cos(a), MathF.Sin(a)) * g.Reach * T * 0.45f;
                    DrawCircle(gp + off, g.Reach * T * (0.55f + 0.08f * MathF.Sin(time + k)), gc with { A = 0.12f * fade });
                }
                DrawCircle(gp, g.Reach * T * 0.7f, gc with { A = 0.16f * fade });
            }
            // The mage's astral form, drifting through walls.
            if (r.Projecting && r.AstralForm != null)
            {
                Vector2 ap = Art.G(r.AstralForm.Pos, T);
                for (int k = 0; k < 3; k++) Art.DrawGlow(this, ap + new Vector2(MathF.Sin(time * 3 + k * 2) * 4, MathF.Cos(time * 2.5f + k) * 4), T * (0.9f - k * 0.2f), new Color(0.75f, 0.6f, 1f, 0.35f + 0.15f * k));
                DrawArc(ap, T * 0.5f, 0, MathF.Tau, 20, new Color(0.85f, 0.75f, 1f, 0.8f), 1.5f);
                DrawLine(ap, Art.G(r.Player.Pos, T), new Color(0.75f, 0.6f, 1f, 0.25f), 1.5f);
            }
            // Grenades.
            foreach (Grenade g in r.Grenades)
            {
                if (!r.Seen(g.Pos)) continue;
                Vector2 p = Art.G(g.Pos, T);
                float z = g.Z * T;
                DrawCircle(p + new Vector2(2, 3), 4, new Color(0, 0, 0, 0.4f));
                Color c = g.Def.Kind switch { GrenadeKind.Emp => new Color(0.4f, 0.6f, 1f), GrenadeKind.Fire => new Color(1, 0.5f, 0.1f), GrenadeKind.Flash => new Color(0.9f, 0.9f, 0.9f), GrenadeKind.Breach => new Color(0.9f, 0.8f, 0.2f), _ => new Color(0.35f, 0.45f, 0.25f) };
                if (g.Def.Kind == GrenadeKind.Breach) DrawRect(new Rect2(p - new Vector2(6, 4), new Vector2(12, 8)), c);
                else DrawCircle(p - new Vector2(0, z), 4.5f, c);
                if (MathF.Sin(g.Fuse * 25) > 0) DrawCircle(p - new Vector2(0, z + 3), 1.8f, UITheme.Bad);
            }
            // Projectiles.
            foreach (Projectile s in r.Shots)
            {
                if (!r.Seen(s.Pos)) continue;
                Vector2 p = Art.G(s.Pos, T);
                Vector2 tail = p - Art.G(s.Vel, T) * 0.025f;
                switch (s.Kind)
                {
                    case ShotKind.Rocket:
                        DrawLine(p, p - Art.G(M.Norm(s.Vel)) * 10, new Color(0.7f, 0.7f, 0.7f), 4);
                        Art.DrawGlow(this, p - Art.G(M.Norm(s.Vel)) * 10, 14, new Color(1, 0.6f, 0.2f, 0.9f));
                        if (V.Fx.Rand(0, 1) < 0.6f) V.Fx.Add(FxKind.Smoke, p, Vector2.Zero, 0.8f, 5, new Color(0.6f, 0.6f, 0.6f, 0.4f));
                        break;
                    case ShotKind.Fireball:
                        Art.DrawGlow(this, p, 22, new Color(1, 0.45f, 0.1f, 0.9f));
                        DrawCircle(p, 5, new Color(1, 0.9f, 0.6f));
                        if (V.Fx.Rand(0, 1) < 0.5f) V.Glow.Add(FxKind.Ember, p, new Vector2(V.Fx.Rand(-30, 30), V.Fx.Rand(-30, 30)), 0.4f, 2, new Color(1, 0.6f, 0.2f));
                        break;
                    default:
                        Color tc = s.Team == 0 ? new Color(1, 0.95f, 0.6f) : new Color(1, 0.55f, 0.35f);
                        DrawLine(tail, p, tc with { A = 0.35f }, s.Kind == ShotKind.Pellet ? 1.5f : 2.5f);
                        DrawLine(tail.Lerp(p, 0.6f), p, tc, s.Kind == ShotKind.Pellet ? 1 : 1.5f);
                        break;
                }
            }
            // The player's aim aids.
            Actor pl = r.Player;
            if (!pl.Dead)
            {
                Vector2 pp = Art.G(pl.Pos, T);
                if (r.C.Has("smartlink") && r.CurrentGun != null)
                {
                    V2 end = pl.Pos + M.Dir(pl.Facing) * 14;
                    if (r.L.FirstShotBlock(pl.Pos, end, out _, out V2 hit)) end = hit;
                    DrawLine(pp, Art.G(end, T), new Color(0.2f, 1f, 1f, 0.25f), 1);
                }
                if (V.ShowArc && r.Nades.GetValueOrDefault(r.NadeSel) > 0 && r.NadeSel != "breach")
                {
                    V2 tgt = r.ThrowTarget(V.Aim);
                    Vector2 a = pp, b = Art.G(tgt, T);
                    float h = a.DistanceTo(b) * 0.35f;
                    Vector2 prev = a;
                    for (int k = 1; k <= 16; k++)
                    {
                        float u = k / 16f;
                        Vector2 q = a.Lerp(b, u) - new Vector2(0, MathF.Sin(u * MathF.PI) * h);
                        if (k % 2 == 0) DrawLine(prev, q, UITheme.Yellow with { A = 0.7f }, 2);
                        prev = q;
                    }
                    float rad = Tables.Grenade(r.NadeSel).Radius * T;
                    DrawArc(b, rad, 0, MathF.Tau, 32, UITheme.Yellow with { A = 0.5f }, 1.5f);
                }
                if (r.MeleeSwing > 0)
                {
                    float arc = r.MeleeDef.Arc * MathF.PI / 360f;
                    DrawArc(pp, r.MeleeDef.Reach * T, pl.Facing - arc, pl.Facing + arc, 16, new Color(0.8f, 1f, 1f, r.MeleeSwing * 4), 3);
                }
            }
        }

        private void Cone(Vector2 p, float facing, float half, float range, Color c, Run r)
        {
            // Clip each ray against walls so the cone shows what is actually covered.
            const int n = 14;
            var pts = new Vector2[n + 2];
            pts[0] = p;
            V2 from = Art.S(p) / T;
            for (int i = 0; i <= n; i++)
            {
                float a = facing - half + 2 * half * i / n;
                V2 end = from + M.Dir(a) * (range / T);
                V2 hit = end;
                Pt start = Pt.Of(from);
                Level.Walk(from, end, (x, y) =>
                {
                    if (x == start.X && y == start.Y) return true;
                    if (r.L.BlocksSight(x, y)) { hit = Level.Entry(from, end, new Pt(x, y)); return false; }
                    return true;
                });
                pts[i + 1] = Art.G(hit, T);
            }
            DrawColoredPolygon(pts, c);
        }

        private void DrawActor(Actor a, Run r, float time)
        {
            Vector2 p = Art.G(a.Pos, T);
            float rad = a.Radius * T;
            bool thermalOnly = !r.Seen(a.Pos) && !a.IsPlayer;
            float flash = a.HitFlash > 0 ? 0.7f : 0;
            if (thermalOnly)
            {
                Art.DrawGlow(this, p, rad * 2.4f, r.OverwatchT > 0 && !r.C.Thermal ? new Color(1, 0.2f, 0.3f, 0.6f) : new Color(1, 0.45f, 0.1f, 0.6f));
                return;
            }
            if (a.IsGhost)
            {
                float fl = MathF.Sin(time * 37 + a.Id) > 0 ? 0.55f : 0.2f;
                Art.Look gl = Art.LookFor(a, r.Job.Faction);
                Art.Person(this, p + new Vector2(3, 0), a.Facing, rad * 1.15f, gl with { Body = new Color(1, 0, 0.4f), Trim = UITheme.Magenta }, WeaponClass.Smg, 0, time * 6, fl * 0.6f);
                Art.Person(this, p - new Vector2(3, 0), a.Facing, rad * 1.15f, gl with { Body = new Color(0, 0.9f, 1f), Trim = UITheme.Cyan }, WeaponClass.Smg, 0, time * 6, fl);
                return;
            }
            if (a.Is(EnemyKind.Spirit) || a.Summoned)
            {
                Color sc = a.Summoned ? new Color(1f, 0.55f, 0.15f) : new Color(0.6f, 0.4f, 1f);
                for (int k = 0; k < 3; k++) Art.DrawGlow(this, p + new Vector2(MathF.Sin(time * 5 + k * 2) * 4, MathF.Cos(time * 4 + k) * 4), rad * (2.4f - k * 0.5f), sc with { A = 0.35f + 0.15f * k });
                DrawCircle(p, rad * 0.5f, sc.Lightened(0.5f));
                if (a.Summoned) UITheme.Write(this, p + new Vector2(-50, -rad - 10), "SPIRIT", 11, sc, HorizontalAlignment.Center, 100, heading: true);
                return;
            }
            if (a.IsDrone)
            {
                Art.Drone(this, p - new Vector2(0, 6), a.Facing, rad, UITheme.Good, time, flash);
                if (r.Rigging) DrawArc(Art.G(r.Player.Pos, T), r.DroneRange * T, 0, MathF.Tau, 64, UITheme.Term with { A = 0.25f }, 1.5f);
            }
            else if (a.Is(EnemyKind.Drone)) { Art.Drone(this, p - new Vector2(0, 6), a.Facing, rad, a.Team == 0 ? UITheme.Violet : UITheme.Bad, time, flash); }
            else if (a.Is(EnemyKind.Turret)) Art.Turret(this, p, a.Facing, rad, a.Team == 0 ? UITheme.Violet : a.Stun > 0 ? UITheme.Faint : UITheme.Bad, flash);
            else if (a.Is(EnemyKind.Camera)) Art.Camera(this, p, a.Facing, rad, a.Stun > 0 ? UITheme.Faint : MathF.Sin(time * 6) > 0 ? UITheme.Bad : UITheme.Bad.Darkened(0.5f));
            else if (a.Is(EnemyKind.EggSac)) Egg(p, rad, a.EggT / Tables.EggHatch, flash, time + a.Id);
            else if (a.Is(EnemyKind.HiveWorker) || a.Is(EnemyKind.HiveQueen)) Bug(p, a.Facing, rad, flash, time * a.Speed * 3 + a.Id, a.Is(EnemyKind.HiveQueen));
            else if (a.IsNpc)
            {
                float h1 = Art.Hash(a.Id, 3), h2 = Art.Hash(a.Id, 5);
                var l = new Art.Look { Body = Color.FromHsv(h1, 0.5f, 0.45f), Trim = Color.FromHsv(h2, 0.8f, 1f), Skin = new Color(0.5f + h2 * 0.4f, 0.4f + h2 * 0.25f, 0.3f + h2 * 0.2f), Hair = Color.FromHsv(h2, 0.6f, 0.5f), Coat = h1 > 0.5f };
                Art.Person(this, p, a.Facing, rad * 1.1f, l, null, 0, time * a.Speed * 2.2f);
                if (a.Vendor != null) UITheme.Write(this, p + new Vector2(-70, -rad - 12), a.Name.ToUpperInvariant(), 11, UITheme.Yellow, HorizontalAlignment.Center, 140, heading: true);
                else if (a.PanicT > 0) UITheme.Write(this, p + new Vector2(-20, -rad - 8 - MathF.Abs(MathF.Sin(time * 9 + a.Id)) * 3), "!", 15, UITheme.Yellow, HorizontalAlignment.Center, 40, heading: true);
            }
            else
            {
                Art.Look l = a.IsPlayer ? (r.Disguised ? Art.LookFor(DisguiseLook, r.Job.Faction) : Art.PlayerLook(r.C.Archetype, r.C.Has("eyes"))) : a.Rival ? RivalLook : a.IsMedic ? new Art.Look { Body = new Color(0.85f, 0.88f, 0.86f), Trim = new Color(0.15f, 0.75f, 0.4f), Skin = new Color(0.75f, 0.6f, 0.5f), Hair = new Color(0.2f, 0.2f, 0.2f), Heavy = true } : Art.LookFor(a, r.Job.Faction);
                WeaponClass? wc = a.IsPlayer ? r.CurrentWeapon?.Class : a.Gun?.Def.Class;
                if (a.IsHostage && a.State == AiState.Tied) wc = null;
                float walk = a.IsPlayer ? r.Player.Vel.Length() * time * 2.2f : time * a.Speed * 2.2f;
                float alpha = a.IsPlayer && r.CloakT > 0 ? 0.3f : 1f;
                if (a.Kind == EnemyKind.Hound && !a.IsPlayer && a.Def != null)
                {
                    Hound(p, a.Facing, rad, flash, walk, r.Job.Faction == Faction.Halcyon);
                }
                else if (a.Is(EnemyKind.Barghest))
                {
                    // Not a hellhound: a pale, ash-grey beast the size of a pony, with a cold spectral glow and ice-blue eyes;
                    // the howl winding up shows as a ring.
                    Color spectral = new(0.45f, 0.75f, 1f);
                    Art.DrawGlow(this, p, rad * 3.2f, spectral with { A = 0.25f });
                    Hound(p, a.Facing, rad * 1.1f, flash, walk, coat: new Color(0.62f, 0.64f, 0.68f), eyes: new Color(0.55f, 0.9f, 1f));
                    if (a.HowlT > 0) DrawArc(p, rad * (1.5f + 2.5f * (1 - a.HowlT / Tables.HowlWind)), 0, MathF.Tau, 32, spectral with { A = 0.85f }, 3);
                }
                else
                {
                    // A ghost operative in plain sight is still half there: the camouflage shimmers.
                    if (a.Is(EnemyKind.Cloaker)) { alpha = a.RevealT > 0 ? 0.8f : 0.4f + 0.15f * MathF.Sin(time * 23 + a.Id); l = l with { Body = new Color(0.35f, 0.42f, 0.45f), Trim = UITheme.Cyan }; }
                    if (a.Is(EnemyKind.Bulwark)) l = l with { Heavy = true, Body = new Color(0.32f, 0.34f, 0.38f), Trim = new Color(1f, 0.82f, 0.25f) };
                    if (a.Is(EnemyKind.Spider)) l = l with { Body = new Color(0.2f, 0.22f, 0.28f), Trim = UITheme.Term, Coat = true };
                    float moving = a.IsPlayer ? r.Player.Vel.Length() : r.Time - a.MovedAt < 0.12f ? 1 : 0;
                    float kick = MathF.Max(0, 1 - (r.Time - a.ShotAt) / 0.12f);
                    Art.Person(this, p, a.Facing, rad * 1.15f, l, a.Is(EnemyKind.Spider) ? null : wc, flash, a.Is(EnemyKind.Spider) || moving < 0.3f ? 0 : walk, alpha, time + a.Id * 0.7f, kick);
                }
                if (a.Is(EnemyKind.Bulwark))
                {
                    // Plates on the shoulders and a visor slit.
                    Vector2 bf = new(MathF.Cos(a.Facing), MathF.Sin(a.Facing)), bs = new(-bf.Y, bf.X);
                    foreach (float side in new[] { 1f, -1f })
                        DrawRect(new Rect2(p + bs * side * rad * 0.85f - new Vector2(4, 4), new Vector2(8, 8)), new Color(0.55f, 0.58f, 0.62f));
                    DrawLine(p + bf * rad * 0.5f + bs * rad * 0.3f, p + bf * rad * 0.5f - bs * rad * 0.3f, new Color(1f, 0.3f, 0.2f), 2);
                }
                if (a.Is(EnemyKind.Spider))
                {
                    // The deck on his knees, and the glow of the system he's in.
                    Vector2 sf = new(MathF.Cos(a.Facing), MathF.Sin(a.Facing));
                    DrawRect(new Rect2(p + sf * rad * 0.9f - new Vector2(6, 4), new Vector2(12, 8)), new Color(0.1f, 0.12f, 0.12f));
                    Art.DrawGlow(this, p + sf * rad * 0.9f, rad * 2f, UITheme.Term with { A = a.Stun > 0 ? 0.05f : 0.35f + 0.15f * MathF.Sin(time * 5) });
                    UITheme.Write(this, p + new Vector2(-60, -rad - 14), "SPIDER", 11, UITheme.Term, HorizontalAlignment.Center, 120, heading: true);
                }
                if (a.Is(EnemyKind.Shield))
                {
                    Vector2 f = new(MathF.Cos(a.Facing), MathF.Sin(a.Facing)), sd = new(-f.Y, f.X);
                    Vector2 c0 = p + f * rad * 1.25f;
                    DrawColoredPolygon(new[] { c0 + sd * rad * 1.1f, c0 + sd * rad * 1.1f + f * 4, c0 - sd * rad * 1.1f + f * 4, c0 - sd * rad * 1.1f }, new Color(0.6f, 0.7f, 0.8f, 0.85f));
                    DrawLine(c0 + sd * rad * 1.1f + f * 2, c0 - sd * rad * 1.1f + f * 2, new Color(0.9f, 0.95f, 1f), 1);
                }
                if (a.Is(EnemyKind.Bruiser))
                {
                    Vector2 bf = new(MathF.Cos(a.Facing), MathF.Sin(a.Facing)), bs = new(-bf.Y, bf.X);
                    DrawLine(p + bf * rad * 0.6f + bs * rad * 0.5f, p + bf * rad * 1.7f + bs * rad * 0.2f, new Color(0.45f, 0.45f, 0.5f), 3);
                }
                if (a.Is(EnemyKind.Firebug)) DrawCircle(p + new Vector2(MathF.Cos(a.Facing + 0.6f), MathF.Sin(a.Facing + 0.6f)) * rad * 1.1f, 3, new Color(1f, 0.6f, 0.1f));
                if (a.Is(EnemyKind.Rigger)) DrawLine(p, p - new Vector2(MathF.Cos(a.Facing), MathF.Sin(a.Facing)) * rad * 1.2f + new Vector2(0, -rad * 1.4f), UITheme.Cyan, 1.5f);
                if (a.Is(EnemyKind.Boss) || a.Is(EnemyKind.Bulwark)) UITheme.Write(this, p + new Vector2(-70, -rad - 16), a.Name.ToUpperInvariant(), 12, UITheme.Yellow, HorizontalAlignment.Center, 140, heading: true);
                if (a.IsHostage && a.State == AiState.Tied) DrawArc(p, rad * 1.1f, 0, MathF.Tau, 16, new Color(0.8f, 0.7f, 0.4f), 2);
                if (a.IsPlayer && r.DashT > 0) DrawArc(p, rad * 1.6f, 0, MathF.Tau, 20, UITheme.Cyan with { A = 0.6f }, 2);
            }
            if (a.Stun > 0 && !a.Is(EnemyKind.Camera) && !a.Is(EnemyKind.Turret))
                for (int k = 0; k < 3; k++)
                {
                    float ang = time * 5 + k * 2.1f;
                    DrawCircle(p + new Vector2(MathF.Cos(ang), MathF.Sin(ang) * 0.5f) * rad - new Vector2(0, rad * 1.4f), 2, UITheme.Yellow);
                }
            if (a.Team == 1 && a.Def != null && !a.Is(EnemyKind.Camera))
            {
                // Awareness marks and health.
                if (a.Alert && a.ReactT > 0) UITheme.Write(this, p + new Vector2(-4, -rad - 10), "!", 20, UITheme.Bad, heading: true);
                else if (a.State == AiState.Suspicious || (!a.Alert && a.Detect > 0.3f)) UITheme.Write(this, p + new Vector2(-4, -rad - 10), "?", 18, UITheme.Yellow, heading: true);
                if (!a.Alert && a.Detect > 0.05f) DrawArc(p, rad + 5, -MathF.PI / 2, -MathF.PI / 2 + MathF.Tau * a.Detect, 20, UITheme.Yellow, 2);
                if (a.Hp < a.MaxHp)
                {
                    var bar = new Rect2(p + new Vector2(-14, rad + 6), new Vector2(28, 3));
                    DrawRect(bar, new Color(0, 0, 0, 0.7f));
                    DrawRect(new Rect2(bar.Position, new Vector2(28 * a.Hp / a.MaxHp, 3)), a == r.Vip ? UITheme.Magenta : UITheme.Bad);
                }
                if (a == r.Vip) UITheme.Write(this, p + new Vector2(-60, -rad - 14), "TARGET", 11, UITheme.Magenta, HorizontalAlignment.Center, 120, heading: true);
            }
            if (a.Surrendered && !a.Dead) UITheme.Write(this, p + new Vector2(-60, -rad - 10), "HANDS UP", 11, UITheme.Text, HorizontalAlignment.Center, 120, heading: true);
            if (a.IsMedic && !a.Dead) UITheme.Write(this, p + new Vector2(-60, -rad - 10), "TRAUMALINK", 11, a.Team == 2 ? new Color(0.3f, 0.95f, 0.5f) : UITheme.Bad, HorizontalAlignment.Center, 120, heading: true);
            if (a.IsAlly) UITheme.Write(this, p + new Vector2(-50, -rad - 10), a.Name.ToUpperInvariant(), 11, UITheme.Good, HorizontalAlignment.Center, 100, heading: true);
            else if (a.Rival && !a.Dead)
            {
                UITheme.Write(this, p + new Vector2(-60, -rad - 10), (a == r.RivalCarrier ? a.Name + " -- HAS IT" : a.Name).ToUpperInvariant(), 11, RivalColour, HorizontalAlignment.Center, 120, heading: true);
                DrawArc(p, rad + 4, 0, MathF.Tau, 16, RivalColour with { A = 0.8f }, 1.5f);
            }
            else if (a.Team == 0 && a.Def != null) DrawArc(p, rad + 4, 0, MathF.Tau, 16, UITheme.Violet, 1.5f);
        }

        /// <summary>A hive insect from above: head, thorax, a long abdomen, six legs moving; the queen is bloated and glows.</summary>
        private void Bug(Vector2 p, float facing, float r, float flash, float walk, bool queen)
        {
            Vector2 f = new(MathF.Cos(facing), MathF.Sin(facing)), s = new(-f.Y, f.X);
            Color shell = (queen ? new Color(0.42f, 0.36f, 0.12f) : new Color(0.25f, 0.28f, 0.14f)).Lerp(Colors.White, flash);
            if (queen) Art.DrawGlow(this, p, r * 3, new Color(0.8f, 0.9f, 0.2f, 0.18f));
            for (int k = 0; k < 6; k++)
            {
                float along = (k / 2 - 1) * r * 0.45f, side = k % 2 == 0 ? 1 : -1;
                float sw = MathF.Sin(walk + k) * r * 0.25f;
                DrawLine(p + f * along, p + f * (along + sw) + s * side * r * 1.25f, shell.Darkened(0.4f), queen ? 3 : 1.5f);
            }
            DrawSetTransform(p - f * r * (queen ? 0.9f : 0.6f), facing, new Vector2(queen ? 1.9f : 1.3f, queen ? 1.1f : 0.7f));
            DrawCircle(Vector2.Zero, r * 0.7f, shell.Darkened(0.15f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            if (queen)
                for (int k = 1; k < 4; k++) DrawLine(p - f * r * (0.4f + k * 0.35f) + s * r * 0.6f, p - f * r * (0.4f + k * 0.35f) - s * r * 0.6f, shell.Darkened(0.5f), 1.5f);
            DrawCircle(p, r * 0.5f, shell);
            DrawCircle(p + f * r * 0.65f, r * 0.32f, shell.Lightened(0.1f));
            DrawCircle(p + f * r * 0.8f + s * r * 0.15f, queen ? 2.5f : 1.5f, new Color(0.9f, 1f, 0.3f));
            DrawCircle(p + f * r * 0.8f - s * r * 0.15f, queen ? 2.5f : 1.5f, new Color(0.9f, 1f, 0.3f));
            if (queen) UITheme.Write(this, p + new Vector2(-70, -r * 2.2f), "HIVE QUEEN", 12, UITheme.Yellow, HorizontalAlignment.Center, 140, heading: true);
        }

        /// <summary>An egg sac: translucent, pulsing faster as it gets ready to split.</summary>
        private void Egg(Vector2 p, float r, float ripe, float flash, float time)
        {
            float pulse = 1 + 0.08f * MathF.Sin(time * (3 + 9 * ripe));
            Art.DrawGlow(this, p, r * 2.6f, new Color(0.7f, 0.9f, 0.3f, 0.12f + 0.25f * ripe));
            DrawSetTransform(p, 0.4f, new Vector2(1.15f * pulse, 0.9f * pulse));
            DrawCircle(Vector2.Zero, r, new Color(0.55f, 0.62f, 0.3f, 0.85f).Lerp(Colors.White, flash));
            DrawCircle(new Vector2(-r * 0.25f, -r * 0.2f), r * 0.35f, new Color(0.9f, 1f, 0.6f, 0.35f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            DrawCircle(p, r * 0.35f * (0.5f + ripe), new Color(0.15f, 0.18f, 0.05f, 0.8f));
        }

        /// <param name="coat">Fur colour (default: the hellhound's rust, or a K9's grey); <paramref name="eyes"/> likewise.</param>
        private void Hound(Vector2 p, float facing, float r, float flash, float walk, bool k9 = false, Color? coat = null, Color? eyes = null)
        {
            Vector2 f = new(MathF.Cos(facing), MathF.Sin(facing));
            Vector2 s = new(-f.Y, f.X);
            Color fur = (coat ?? (k9 ? new Color(0.45f, 0.46f, 0.5f) : new Color(0.34f, 0.16f, 0.1f))).Lerp(Colors.White, flash);
            Color eye = eyes ?? new Color(1, 0.3f, 0.1f);
            Art.DrawGlow(this, p, r * 2.2f, eye with { A = 0.18f });
            DrawCircle(p + new Vector2(2, 3), r, new Color(0, 0, 0, 0.3f));
            float sw = MathF.Sin(walk) * r * 0.3f;
            for (int k = 0; k < 4; k++)
                DrawCircle(p + f * ((k < 2 ? 0.5f : -0.5f) * r + (k % 2 == 0 ? sw : -sw)) + s * (k % 2 == 0 ? 0.45f : -0.45f) * r, r * 0.18f, fur.Darkened(0.4f));
            // The tail, swinging with the stride.
            DrawLine(p - f * r * 1.2f, p - f * r * 1.9f + s * MathF.Sin(walk * 1.3f) * r * 0.5f, fur.Darkened(0.15f), r * 0.2f);
            DrawSetTransform(p, facing, new Vector2(1.4f, 0.75f));
            DrawCircle(Vector2.Zero, r, fur);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            DrawCircle(p + f * r * 1.1f, r * 0.45f, fur);
            // Ears pricked back.
            foreach (float e in new[] { 1f, -1f })
                DrawColoredPolygon(new[] { p + f * r * 1.0f + s * e * r * 0.28f, p + f * r * 0.7f + s * e * r * 0.5f, p + f * r * 0.85f + s * e * r * 0.1f }, fur.Darkened(0.25f));
            DrawCircle(p + f * r * 1.3f + s * r * 0.18f, 1.8f, eye);
            DrawCircle(p + f * r * 1.3f - s * r * 0.18f, 1.8f, eye);
        }
    }
}
