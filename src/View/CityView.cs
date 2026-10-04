using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>
/// Kessler Bay at night: streets baked into chunks once, buildings drawn as
/// boxes that lean away from the camera (a cheap 3D), traffic, people,
/// neon, headlights, and the markers of places you can go in.
/// </summary>
public partial class CityView : Node2D
{
    public const int T = 32;
    public CitySim Sim;
    public Camera2D Cam;
    private Node2D _street;
    private Movers _movers;
    private Buildings _buildings;
    private Node2D _glow;
    public FxLayer Fx;
    private Vector2 _camPos;
    private float _zoom = 1;
    public float Time;
    public float Shake;

    /// <summary>The planned route, drawn on the street (set by the city screen).</summary>
    public System.Collections.Generic.List<V2> Route;
    /// <summary>Rooftop tags for this visit to the streets.</summary>
    public System.Collections.Generic.Dictionary<int, (string text, uint colour)> Tags = new();

    public void Bind(CitySim sim)
    {
        Sim = sim;
        Tags = sim.C.Graffiti(sim.City);
        foreach (Node n in GetChildren()) n.QueueFree();
        Career.Sky sky = sim.C.Weather;
        AddChild(new CanvasModulate { Color = sky switch { Career.Sky.Fog => new Color(0.6f, 0.62f, 0.66f), Career.Sky.Storm => new Color(0.5f, 0.52f, 0.68f), _ => new Color(0.66f, 0.66f, 0.82f) } });
        _street = new Node2D();
        AddChild(_street);
        City c = sim.City;
        for (int cy = 0; cy < (City.H + 15) / 16; cy++)
        for (int cx = 0; cx < (City.W + 15) / 16; cx++)
            _street.AddChild(new StreetChunk { C = c, CX = cx, CY = cy });
        var lamps = new Node2D { Material = Art.Additive };
        AddChild(lamps);
        AddLamps(lamps, c);
        _movers = new Movers { V = this };
        AddChild(_movers);
        Fx = new FxLayer();
        AddChild(Fx);
        _buildings = new Buildings { V = this };
        AddChild(_buildings);
        _glow = new GlowLayer { V = this, Material = Art.Additive };
        AddChild(_glow);
        Cam = new Camera2D();
        AddChild(Cam);
        Cam.MakeCurrent();
        _camPos = Art.G(sim.Player.Pos, T);
        Cam.Position = _camPos;
    }

    private static void AddLamps(Node2D into, City c)
    {
        var rng = new Rng(99);
        foreach (int rx in c.RoadX)
            for (int y = 2; y < c.LandH; y += 9)
                foreach (int side in new[] { -1, City.Road })
                {
                    int x = rx + side;
                    if (c[x, y] != CTile.Sidewalk) continue;
                    Lamp(into, new Vector2((x + 0.5f) * T, (y + 0.5f) * T), rng);
                }
        foreach (int ry in c.RoadY)
            for (int x = 6; x < c.LandW; x += 11)
                foreach (int side in new[] { -1, City.Road })
                {
                    int y = ry + side;
                    if (c[x, y] != CTile.Sidewalk) continue;
                    Lamp(into, new Vector2((x + 0.5f) * T, (y + 0.5f) * T), rng);
                }
    }

    private static void Lamp(Node2D into, Vector2 at, Rng rng)
    {
        Color c = rng.Chance(0.7f) ? new Color(1f, 0.7f, 0.35f, 0.42f) : new Color(0.4f, 0.8f, 1f, 0.38f);
        var s = new Sprite2D { Texture = Art.Glow, Position = at, Modulate = c, Scale = Vector2.One * 4.6f * T / Art.Glow.GetWidth() };
        into.AddChild(s);
    }

    public void Frame(float dt)
    {
        Time += dt;
        Car car = Sim.Player;
        Vector2 target = Art.G(car.Pos + car.Vel * 0.55f, T);
        _camPos = _camPos.Lerp(target, MathF.Min(1, dt * 5));
        float speed = car.Vel.Length();
        float want = 1.35f - 0.5f * M.Clamp(speed / 15f, 0, 1);
        _zoom += (want - _zoom) * MathF.Min(1, dt * 1.5f);
        Cam.Zoom = new Vector2(_zoom, _zoom);
        Shake = MathF.Max(0, Shake - dt * 30);
        float sh = Profile.Current.ScreenShake ? Shake : 0;
        Cam.Position = _camPos + new Vector2(Fx.Rand(-1, 1), Fx.Rand(-1, 1)) * sh;

        // Tyre smoke when sliding.
        if (MathF.Abs(car.Lateral) > 3.2f && speed > 4)
        {
            Vector2 back = Art.G(car.Back, T);
            Fx.Add(FxKind.Smoke, back + new Vector2(Fx.Rand(-6, 6), Fx.Rand(-6, 6)), Vector2.Zero, 0.9f, 6, new Color(0.7f, 0.7f, 0.75f, 0.25f));
        }
        foreach (CityEvent e in Sim.Events)
        {
            Vector2 p = Art.G(e.Pos, T);
            switch (e.Kind)
            {
                case CityEv.Gunfire:
                    Fx.Add(FxKind.Flash, p, Vector2.Zero, 0.08f, 22, new Color(1, 0.8f, 0.4f, 0.9f));
                    Vector2 to = Art.G(Sim.Player.Pos, T) + new Vector2(Fx.Rand(-14, 14), Fx.Rand(-14, 14));
                    var tr = Fx.Add(FxKind.Spark, to, (to - p).Normalized() * 1400, 0.05f, 2, new Color(1, 0.9f, 0.6f));
                    tr.Drag = 0;
                    break;
                case CityEv.Crash:
                case CityEv.Bump:
                    Fx.Burst(FxKind.Spark, p, 10, 240, 0.3f, 1.5f, new Color(1, 0.8f, 0.4f));
                    Fx.Burst(FxKind.Debris, p, 5, 120, 0.5f, 3, new Color(0.5f, 0.5f, 0.55f));
                    Shake = 4 + e.Strength;
                    break;
            }
        }
        Fx.Step(dt);
        _movers.QueueRedraw();
        _buildings.QueueRedraw();
        _glow.QueueRedraw();
    }

    public Vector2 CamCentre => Cam.GetScreenCenterPosition();

    public Rect2 ViewRect
    {
        get
        {
            Vector2 size = GetViewportRect().Size / Cam.Zoom;
            return new Rect2(CamCentre - size / 2, size).Grow(260);
        }
    }

    /// <summary>The roof offset of a building of height h whose base centre is at p.</summary>
    public Vector2 Lean(Vector2 p, float h) => (p - CamCentre) * h * 0.055f;

    // ---- layers ---------------------------------------------------------------------

    private partial class StreetChunk : Node2D
    {
        public City C;
        public int CX, CY;

        public override void _Draw()
        {
            for (int y = CY * 16; y < Math.Min(City.H, CY * 16 + 16); y++)
            for (int x = CX * 16; x < Math.Min(City.W, CX * 16 + 16); x++)
                DrawCityTile(this, C, x, y);
        }
    }

    private static void DrawCityTile(CanvasItem ci, City c, int x, int y)
    {
        var o = new Vector2(x * T, y * T);
        var r = new Rect2(o, new Vector2(T, T));
        float n = Art.Hash(x, y) * 0.03f;
        switch (c[x, y])
        {
            case CTile.Road:
            {
                ci.DrawRect(r, new Color(0.11f + n, 0.115f + n, 0.13f + n));
                // Lane lines: centre (yellow, dashed) and edges.
                int vi = c.RoadX.FindIndex(rx => x >= rx && x < rx + City.Road);
                int hi = c.RoadY.FindIndex(ry => y >= ry && y < ry + City.Road);
                bool cross = vi >= 0 && hi >= 0;
                if (!cross && vi >= 0 && x - c.RoadX[vi] == 2 && y % 3 != 0) ci.DrawLine(o, o + new Vector2(0, T), new Color(0.9f, 0.75f, 0.2f, 0.7f), 2);
                if (!cross && hi >= 0 && y - c.RoadY[hi] == 2 && x % 3 != 0) ci.DrawLine(o, o + new Vector2(T, 0), new Color(0.9f, 0.75f, 0.2f, 0.7f), 2);
                if (cross && vi >= 0 && hi >= 0)
                {
                    // Zebra crossings at the edges of the junction.
                    int lx = x - c.RoadX[vi], ly = y - c.RoadY[hi];
                    if (ly == 0 || ly == City.Road - 1) for (int k = 4; k < T; k += 8) ci.DrawLine(o + new Vector2(k, ly == 0 ? 2 : T - 10), o + new Vector2(k, ly == 0 ? 10 : T - 2), new Color(0.8f, 0.8f, 0.8f, 0.25f), 3);
                    if (lx == 0 || lx == City.Road - 1) for (int k = 4; k < T; k += 8) ci.DrawLine(o + new Vector2(lx == 0 ? 2 : T - 10, k), o + new Vector2(lx == 0 ? 10 : T - 2, k), new Color(0.8f, 0.8f, 0.8f, 0.25f), 3);
                }
                if (Art.Hash(x, y, 4) > 0.9f) ci.DrawCircle(o + new Vector2(16, 16), 8 + Art.Hash(x, y, 5) * 8, new Color(0.3f, 0.4f, 0.6f, 0.12f));
                break;
            }
            case CTile.Sidewalk:
                ci.DrawRect(r, new Color(0.23f + n, 0.23f + n, 0.25f + n));
                ci.DrawRect(r, new Color(0.17f, 0.17f, 0.19f), false, 1);
                break;
            case CTile.Building:
                ci.DrawRect(r, new Color(0.05f, 0.05f, 0.07f));
                break;
            case CTile.Grass:
            case CTile.Tree:
                ci.DrawRect(r, new Color(0.08f + n, 0.16f + n, 0.09f + n));
                if (c[x, y] == CTile.Tree)
                {
                    ci.DrawCircle(o + new Vector2(18, 19), 14, new Color(0, 0, 0, 0.35f));
                    ci.DrawCircle(o + new Vector2(16, 16), 14, new Color(0.1f, 0.3f, 0.14f));
                    ci.DrawCircle(o + new Vector2(13, 13), 7, new Color(0.15f, 0.4f, 0.18f));
                }
                break;
            case CTile.Parking:
                ci.DrawRect(r, new Color(0.13f + n, 0.13f + n, 0.14f + n));
                if (x % 2 == 0) ci.DrawLine(o, o + new Vector2(0, T * 0.7f), new Color(0.8f, 0.8f, 0.8f, 0.3f), 2);
                break;
            case CTile.Water:
                ci.DrawRect(r, new Color(0.02f, 0.05f + n, 0.1f + n));
                if (Art.Hash(x, y, 7) > 0.8f) ci.DrawLine(o + new Vector2(4, 12), o + new Vector2(20, 12), new Color(0.2f, 0.4f, 0.6f, 0.3f), 1);
                break;
            case CTile.Plaza:
            case CTile.Statue:
                ci.DrawRect(r, new Color(0.3f + n, 0.29f + n, 0.27f + n));
                if ((x + y) % 2 == 0) ci.DrawRect(r.Grow(-3), new Color(0.34f + n, 0.33f + n, 0.31f + n));
                break;
            case CTile.Pier:
                ci.DrawRect(r, new Color(0.25f, 0.18f, 0.12f));
                for (int k = 0; k < 4; k++) ci.DrawLine(o + new Vector2(0, k * 8), o + new Vector2(T, k * 8), new Color(0.15f, 0.1f, 0.06f), 1);
                break;
        }
    }

    /// <summary>Cars and people, under the buildings.</summary>
    private partial class Movers : Node2D
    {
        public CityView V;

        public override void _Draw()
        {
            CitySim s = V.Sim;
            Rect2 view = V.ViewRect;
            // Place markers on the pavement.
            foreach (Lot l in s.Destinations())
            {
                Vector2 p = Art.G(l.Door.Centre, T);
                if (!view.HasPoint(p)) continue;
                bool job = s.C.Active != null && l.Index == s.C.Active.Lot;
                Color c = job ? UITheme.Magenta : Art.C(l.Sign == 0 ? 0x40ffb0ffu : l.Sign);
                float pulse = 0.5f + 0.5f * MathF.Sin(V.Time * 3);
                DrawCircle(p, 20, c with { A = 0.15f + 0.1f * pulse });
                DrawArc(p, 20 + pulse * 4, 0, MathF.Tau, 28, c with { A = 0.8f }, 2);
            }
            foreach (Ped p in s.Peds)
            {
                Vector2 at = Art.G(p.Pos, T);
                if (!view.HasPoint(at)) continue;
                Color c = Art.C(p.Colour);
                if (p.Down > 0)
                {
                    DrawCircle(at, 6, c.Darkened(0.4f));
                    DrawCircle(at + new Vector2(6, 0), 3.5f, new Color(0.7f, 0.55f, 0.45f));
                    continue;
                }
                DrawCircle(at + new Vector2(1.5f, 2), 6, new Color(0, 0, 0, 0.3f));
                DrawCircle(at, 6, c.Darkened(0.2f));
                DrawCircle(at, 3.5f, new Color(0.15f, 0.12f, 0.1f));
            }
            // The planned route, a dotted line along the lanes.
            if (V.Route != null && V.Route.Count >= 2)
                for (int k = 0; k + 1 < V.Route.Count; k++)
                {
                    Vector2 a = Art.G(V.Route[k], T), b = Art.G(V.Route[k + 1], T);
                    float len = a.DistanceTo(b);
                    for (float u = 0; u < len; u += 26) DrawCircle(a.Lerp(b, u / len), 3.5f, UITheme.Cyan with { A = 0.55f });
                }
            // Torque's gadgets: oil on the road, smoke hanging over it.
            foreach (var (pos, life) in s.OilPatches)
            {
                Vector2 at = Art.G(pos, T);
                DrawCircle(at, T * 1.4f, new Color(0.02f, 0.02f, 0.04f, 0.75f * MathF.Min(1, life)));
                DrawArc(at, T * 1.3f, 0, MathF.Tau, 20, new Color(0.4f, 0.2f, 0.6f, 0.35f * MathF.Min(1, life)), 2);
            }
            // Crackdown checkpoints: striped barriers and two cruisers parked across the road.
            foreach (Roadblock rb in s.Roadblocks)
            {
                Vector2 at = Art.G(rb.At, T);
                if (!view.HasPoint(at)) continue;
                Vector2 across = new(rb.Across.X, rb.Across.Y), along = new(-across.Y, across.X);
                bool open = rb.OpenT > 0;
                foreach (Pt t in rb.Tiles)
                {
                    Vector2 tc = Art.G(new V2(t.X + 0.5f, t.Y + 0.5f), T);
                    for (int k = 0; k < 2; k++)
                        DrawLine(tc - across * T * 0.5f + across * T * 0.5f * k, tc + across * T * 0.5f * k, k == 0 ? new Color(0.95f, 0.85f, 0.2f, open ? 0.3f : 1) : new Color(0.1f, 0.1f, 0.1f, open ? 0.3f : 1), 4);
                }
                for (int k = -1; k <= 1; k += 2)
                {
                    Vector2 cp = at + across * T * 2.6f * k + along * T * 1.6f;
                    float h = MathF.Atan2(along.Y, along.X) + (k > 0 ? 0.35f : -0.35f);
                    Art.Car(this, cp, h, CitySim.PoliceCar.Length * T, CitySim.PoliceCar.Width * T, Art.C(CitySim.PoliceCar.Colour), false, false);
                    bool ph = MathF.Sin(V.Time * 9 + k) > 0;
                    Art.DrawGlow(this, cp, T * 2.2f, (ph ? UITheme.Bad : new Color(0.2f, 0.4f, 1f)) with { A = 0.35f });
                }
            }
            foreach (TrafficCar t in s.Traffic)
            {
                Vector2 at = Art.G(t.Pos, T);
                if (!view.HasPoint(at)) continue;
                Art.Vehicle(this, t.Kind, at, t.Heading, t.Length * T, t.Width * T, Art.C(t.Colour), t.Speed < t.Cruise * 0.5f, V.Time);
            }
            foreach (var (pos, life) in s.SmokeClouds)
                for (int k = 0; k < 5; k++)
                    Art.DrawGlow(this, Art.G(pos, T) + new Vector2(MathF.Sin(k * 2.1f + V.Time) * T * 1.5f, MathF.Cos(k * 1.7f + V.Time * 0.7f) * T * 1.5f), T * (3 + k * 0.6f), new Color(0.7f, 0.72f, 0.75f, 0.25f * MathF.Min(1, life / 2)));
            foreach (Chaser ch in s.Chasers)
            {
                Car cc = ch.Car;
                Vector2 at = Art.G(cc.Pos, T);
                if (!view.HasPoint(at)) continue;
                Art.Car(this, at, cc.Heading, cc.Def.Length * T, cc.Def.Width * T, Art.C(cc.Def.Colour), false, false, cc.Def.Bike, cc.Lean, cc.Wheelie);
                Vector2 f = new(MathF.Cos(cc.Heading), MathF.Sin(cc.Heading)), sd = new(-f.Y, f.X);
                if (ch.Kind == ChaserKind.Police)
                {
                    DrawRect(new Rect2(at - new Vector2(4, 4), new Vector2(8, 8)), new Color(0.05f, 0.05f, 0.1f));
                    bool ph = MathF.Sin(V.Time * 14) > 0;
                    DrawCircle(at + sd * 6, 3, ph ? UITheme.Bad : new Color(0.3f, 0, 0));
                    DrawCircle(at - sd * 6, 3, ph ? new Color(0, 0.1f, 0.3f) : new Color(0.2f, 0.4f, 1f));
                }
                else DrawLine(at - f * 20 + sd * 9, at + f * 20 + sd * 9, UITheme.Good with { A = 0.7f }, 2);
            }
            Car car = s.Player;
            if (V.HidePlayer) return;
            Art.Car(this, Art.G(car.Pos, T), car.Heading, car.Def.Length * T, car.Def.Width * T, Art.C(car.Def.Colour), false, car.Speed > 0.5f && V.Braking, car.Def.Bike, car.Lean, car.Wheelie);
        }
    }

    public bool Braking;
    /// <summary>The title screen drifts over the city without a car of its own.</summary>
    public bool HidePlayer;

    /// <summary>Buildings as boxes leaning away from the camera, far ones first.</summary>
    private partial class Buildings : Node2D
    {
        public CityView V;

        public override void _Draw()
        {
            City c = V.Sim.City;
            Rect2 view = V.ViewRect;
            Vector2 cam = V.CamCentre;
            var list = c.Lots.Where(l => l.Kind == LotKind.Building || l.Landmark != null)
                .Select(l => (l, rect: new Rect2(l.R.X * T, l.R.Y * T, l.R.W * T, l.R.H * T)))
                .Where(x => view.Intersects(x.rect))
                .OrderByDescending(x => (x.rect.GetCenter() - cam).LengthSquared());
            foreach (var (l, rect) in list) DrawBuilding(l, rect);
        }

        /// <summary>A box from a base rectangle (shifted by baseOff) up to its roof (shifted by topOff).</summary>
        private void Box(Rect2 b, Vector2 baseOff, Vector2 topOff, Color c, bool roof = true)
        {
            Vector2[] q = { b.Position, b.Position + new Vector2(b.Size.X, 0), b.End, b.Position + new Vector2(0, b.Size.Y) };
            Color[] shade = { c.Darkened(0.2f), c.Darkened(0.45f), c.Darkened(0.32f), c.Darkened(0.5f) };
            for (int i = 0; i < 4; i++)
                Poly(new[] { q[i] + baseOff, q[(i + 1) % 4] + baseOff, q[(i + 1) % 4] + topOff, q[i] + topOff }, shade[i]);
            if (roof)
            {
                DrawRect(new Rect2(b.Position + topOff, b.Size), c);
                DrawRect(new Rect2(b.Position + topOff, b.Size), c.Lightened(0.3f), false, 1.5f);
            }
        }

        private void DrawLandmark(Lot l, Rect2 b)
        {
            Vector2 cen = b.GetCenter();
            Vector2 Lean(float h) => V.Lean(cen, h);
            float t = V.Time;
            string name = City.Landmarks.First(x => x.id == l.Landmark).name.ToUpperInvariant();
            Vector2 label = cen;
            switch (l.Landmark)
            {
                case "arcology":
                {
                    // Four stepped tiers of dark glass, a beacon on top.
                    Color glass = new(0.16f, 0.24f, 0.32f);
                    Vector2 prev = Vector2.Zero;
                    for (int k = 0; k < 4; k++)
                    {
                        float inset = k * 0.13f;
                        var tier = new Rect2(b.Position + b.Size * inset, b.Size * (1 - 2 * inset));
                        Vector2 top = Lean(l.Height * (k + 1) / 4);
                        Box(tier, prev, top, glass.Lightened(k * 0.05f));
                        for (float y = 6; y < tier.Size.Y - 4; y += 10)
                            DrawLine(tier.Position + top + new Vector2(4, y), tier.Position + top + new Vector2(tier.Size.X - 4, y), new Color(0.5f, 0.85f, 1f, 0.25f), 1);
                        prev = top;
                        label = tier.GetCenter() + top;
                    }
                    break;
                }
                case "pagoda":
                {
                    // Tiers with wide eaves, each edged in neon.
                    Vector2 prev = Vector2.Zero;
                    for (int k = 0; k < 4; k++)
                    {
                        float inset = 0.08f + k * 0.1f;
                        var tier = new Rect2(b.Position + b.Size * inset, b.Size * (1 - 2 * inset));
                        Vector2 top = Lean(l.Height * (k + 1) / 4);
                        Box(tier, prev, top, new Color(0.25f, 0.1f, 0.12f), roof: false);
                        var eave = tier.Grow(10);
                        DrawRect(new Rect2(eave.Position + top, eave.Size), new Color(0.5f, 0.08f, 0.12f));
                        DrawRect(new Rect2(eave.Position + top, eave.Size), k % 2 == 0 ? UITheme.Magenta : UITheme.Cyan, false, 2.5f);
                        prev = top;
                        label = tier.GetCenter() + top;
                    }
                    break;
                }
                case "cathedral":
                {
                    // A cross of nave and transept in old stone, a spire over the crossing.
                    Color stone = new(0.3f, 0.29f, 0.27f);
                    bool wide = b.Size.X >= b.Size.Y;
                    var nave = wide ? new Rect2(b.Position.X + 6, cen.Y - b.Size.Y * 0.2f, b.Size.X - 12, b.Size.Y * 0.4f) : new Rect2(cen.X - b.Size.X * 0.2f, b.Position.Y + 6, b.Size.X * 0.4f, b.Size.Y - 12);
                    var tran = wide ? new Rect2(cen.X + b.Size.X * 0.1f, b.Position.Y + 6, b.Size.X * 0.18f, b.Size.Y - 12) : new Rect2(b.Position.X + 6, cen.Y + b.Size.Y * 0.1f, b.Size.X - 12, b.Size.Y * 0.18f);
                    Box(nave, Vector2.Zero, Lean(l.Height), stone);
                    Box(tran, Vector2.Zero, Lean(l.Height * 0.95f), stone.Darkened(0.05f));
                    var spire = new Rect2(tran.GetCenter() - new Vector2(10, 10), new Vector2(20, 20));
                    Box(spire, Lean(l.Height), Lean(l.Height * 2.2f), stone.Lightened(0.08f));
                    Vector2 tip = spire.GetCenter() + Lean(l.Height * 2.8f);
                    DrawLine(spire.GetCenter() + Lean(l.Height * 2.2f), tip, stone.Lightened(0.2f), 3);
                    // The nave roof ridge.
                    Vector2 r0 = wide ? new Vector2(nave.Position.X, nave.GetCenter().Y) : new Vector2(nave.GetCenter().X, nave.Position.Y);
                    Vector2 r1 = wide ? new Vector2(nave.End.X, nave.GetCenter().Y) : new Vector2(nave.GetCenter().X, nave.End.Y);
                    DrawLine(r0 + Lean(l.Height), r1 + Lean(l.Height), stone.Lightened(0.25f), 2);
                    label = nave.GetCenter() + Lean(l.Height);
                    break;
                }
                case "port":
                {
                    // Stacks of containers in rows, and two cranes reaching over the water.
                    DrawRect(b, new Color(0.18f, 0.18f, 0.2f));
                    uint[] cols = { 0xb03a2aff, 0x2a6aa0ff, 0x3a8a4aff, 0xc0902aff, 0x7a7a80ff, 0x8a3a8aff };
                    for (float y = b.Position.Y + 6; y < b.End.Y - 20; y += 26)
                    for (float x = b.Position.X + 6; x < b.End.X - 40; x += 44)
                    {
                        float h = 0.4f + Art.Hash((int)x, (int)y, l.Index) * 1.4f;
                        Box(new Rect2(x, y, 40, 22), Vector2.Zero, Lean(h), Art.C(cols[(int)(Art.Hash((int)x, (int)y, 3) * cols.Length)]).Darkened(0.2f));
                    }
                    for (int k = 0; k < 2; k++)
                    {
                        Vector2 foot = b.Position + new Vector2(b.Size.X * (0.3f + 0.4f * k), b.Size.Y - 10);
                        Vector2 top = foot + Lean(5);
                        DrawLine(foot, top, new Color(0.85f, 0.6f, 0.15f), 5);
                        DrawLine(top, top + new Vector2(0, 160) + Lean(0.5f), new Color(0.85f, 0.6f, 0.15f), 4);
                        DrawLine(top + new Vector2(-30, 0), top + new Vector2(30, 0), new Color(0.85f, 0.6f, 0.15f), 3);
                    }
                    break;
                }
                case "stadium":
                {
                    // An oval stand round a lit pitch.
                    var pts = new Vector2[28];
                    var top = new Vector2[28];
                    var inner = new Vector2[28];
                    Vector2 lean = Lean(l.Height);
                    for (int i = 0; i < 28; i++)
                    {
                        float a = i / 28f * MathF.Tau;
                        var e = new Vector2(MathF.Cos(a) * b.Size.X * 0.48f, MathF.Sin(a) * b.Size.Y * 0.48f);
                        pts[i] = cen + e;
                        top[i] = cen + e + lean;
                        inner[i] = cen + e * 0.7f + lean * 0.6f;
                    }
                    for (int i = 0; i < 28; i++) Poly(new[] { pts[i], pts[(i + 1) % 28], top[(i + 1) % 28], top[i] }, new Color(0.3f, 0.3f, 0.34f).Darkened(0.2f + 0.2f * MathF.Abs(MathF.Sin(i / 28f * MathF.Tau))));
                    DrawColoredPolygon(top, new Color(0.4f, 0.4f, 0.45f));
                    DrawColoredPolygon(inner, new Color(0.15f, 0.42f, 0.18f));
                    for (int i = 0; i < 28; i += 2) DrawLine(inner[i], top[i], new Color(0.25f, 0.25f, 0.3f), 1);
                    label = cen + lean;
                    break;
                }
                case "plaza":
                {
                    Vector2 f0 = cen;
                    DrawCircle(f0, T * 2.2f, new Color(0.4f, 0.42f, 0.45f));
                    DrawCircle(f0, T * 1.9f, new Color(0.1f, 0.3f, 0.45f));
                    for (int k = 0; k < 6; k++) DrawArc(f0, T * (0.6f + k * 0.22f + (t * 0.3f % 0.22f)), 0, MathF.Tau, 24, new Color(0.5f, 0.8f, 1f, 0.25f), 1);
                    // The statue: a figure with a raised fist.
                    Vector2 st = f0 + Lean(1.2f);
                    DrawLine(f0, st, new Color(0.3f, 0.35f, 0.3f), 10);
                    DrawCircle(st, 8, new Color(0.35f, 0.42f, 0.36f));
                    DrawLine(st, st + Lean(0.6f) + new Vector2(6, -14), new Color(0.35f, 0.42f, 0.36f), 4);
                    label = f0 + new Vector2(0, T * 2.6f);
                    break;
                }
            }
            UITheme.Write(this, label + new Vector2(-150, 6), name, 15, new Color(0.9f, 0.9f, 0.95f, 0.75f), HorizontalAlignment.Center, 300, heading: true);
        }

        private void DrawBuilding(Lot l, Rect2 b)
        {
            if (l.Landmark != null) { DrawLandmark(l, b); return; }
            b = b.Grow(-3);
            DistrictDef d = Tables.District(l.District);
            Vector2 lean = V.Lean(b.GetCenter(), l.Height);
            Color baseCol = DistrictTint(l, d);
            Vector2[] q = { b.Position, b.Position + new Vector2(b.Size.X, 0), b.End, b.Position + new Vector2(0, b.Size.Y) };
            Vector2[] top = q.Select(p => p + lean).ToArray();
            // Walls: shade by which way they face, with rows of lit windows.
            Color wall = baseCol.Darkened(0.25f);
            Color[] shade = { wall.Lightened(0.08f), wall.Darkened(0.3f), wall.Darkened(0.15f), wall.Darkened(0.4f) };
            int floors = Math.Max(1, (int)(l.Height * 2.4f));
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = q[i], bb = q[(i + 1) % 4], ta = top[i], tb = top[(i + 1) % 4];
                Poly(new[] { a, bb, tb, ta }, shade[i]);
                for (int f = 1; f <= floors; f++)
                {
                    float u = f / (floors + 1f);
                    Vector2 p1 = a.Lerp(ta, u), p2 = bb.Lerp(tb, u);
                    if (p1.DistanceTo(p2) < 4 || a.DistanceTo(ta) < 6) continue;
                    float lit = Art.Hash(l.Index, f, i);
                    Color win = lit > 0.55f ? new Color(1f, 0.82f, 0.45f, 0.75f) : lit > 0.25f ? new Color(0.45f, 0.85f, 1f, 0.6f) : new Color(0.1f, 0.12f, 0.16f, 0.8f);
                    DrawDashedLine(p1.Lerp(p2, 0.06f), p2.Lerp(p1, 0.06f), win, 3f, 6, true);
                }
            }
            // Roof: material, parapet, clutter.
            DrawColoredPolygon(top, baseCol);
            var roof = new Rect2(top[0], b.Size);
            switch (l.Variant)
            {
                case 0: // tar and gravel
                    for (int k = 0; k < 18; k++)
                        DrawRect(new Rect2(roof.Position + new Vector2(Art.Hash(l.Index, k, 11) * (roof.Size.X - 4), Art.Hash(l.Index, k, 12) * (roof.Size.Y - 4)), new Vector2(3, 3)), baseCol.Lightened(0.12f));
                    break;
                case 1: // ribbed metal
                    for (float x = 8; x < roof.Size.X - 4; x += 10)
                        DrawLine(roof.Position + new Vector2(x, 6), roof.Position + new Vector2(x, roof.Size.Y - 6), baseCol.Lightened(0.1f), 2);
                    break;
                case 2: // tiles
                    for (float y = 12; y < roof.Size.Y - 6; y += 16)
                        DrawLine(roof.Position + new Vector2(6, y), roof.Position + new Vector2(roof.Size.X - 6, y), baseCol.Darkened(0.2f), 1);
                    for (float x = 12; x < roof.Size.X - 6; x += 16)
                        DrawLine(roof.Position + new Vector2(x, 6), roof.Position + new Vector2(x, roof.Size.Y - 6), baseCol.Darkened(0.2f), 1);
                    break;
                default: // solar or garden
                    if (l.District == "spire" || l.District == "strip")
                        for (int k = 0; k < 3; k++)
                            DrawRect(new Rect2(roof.Position + new Vector2(10, 10 + k * 22), new Vector2(Math.Max(10, roof.Size.X * 0.45f), 14)), new Color(0.1f, 0.18f, 0.3f));
                    else DrawRect(roof.Grow(-10), new Color(0.12f, 0.22f, 0.12f));
                    break;
            }
            DrawRect(roof.Grow(-4), baseCol.Darkened(0.35f), false, 3);
            DrawRect(roof, baseCol.Lightened(0.3f), false, 1.5f);
            int n = 1 + (int)(Art.Hash(l.Index, 1) * 3);
            for (int k = 0; k < n; k++)
            {
                float bw = 14 + Art.Hash(l.Index, k, 4) * 16, bh = 12 + Art.Hash(l.Index, k, 5) * 10;
                float rx = 8 + Art.Hash(l.Index, k, 2) * Math.Max(1, roof.Size.X - bw - 16), ry = 8 + Art.Hash(l.Index, k, 3) * Math.Max(1, roof.Size.Y - bh - 16);
                var box = new Rect2(roof.Position + new Vector2(rx, ry), new Vector2(bw, bh));
                Vector2 up = lean * 0.12f;
                DrawRect(box, baseCol.Darkened(0.45f));
                DrawRect(new Rect2(box.Position + up, box.Size), baseCol.Lightened(0.08f));
                DrawRect(new Rect2(box.Position + up, box.Size), baseCol.Lightened(0.3f), false, 1);
                if (Art.Hash(l.Index, k, 6) > 0.6f) DrawCircle(box.Position + up + box.Size / 2, Math.Min(bw, bh) * 0.32f, baseCol.Darkened(0.3f));
            }
            if (Art.Hash(l.Index, 21) > 0.75f)
            {
                Vector2 tc = roof.Position + roof.Size * new Vector2(0.75f, 0.3f);
                DrawCircle(tc + lean * 0.1f, 10, new Color(0.3f, 0.25f, 0.2f));
                DrawArc(tc + lean * 0.1f, 10, 0, MathF.Tau, 16, new Color(0.5f, 0.42f, 0.32f), 2);
            }
            if (Art.Hash(l.Index, 22) > 0.7f) DrawRect(new Rect2(roof.Position + roof.Size * new Vector2(0.2f, 0.6f), new Vector2(18, 12)), new Color(1f, 0.85f, 0.5f, 0.7f));
            if (l.Height > 3.8f && roof.Size.X > 120 && roof.Size.Y > 120)
            {
                Vector2 hc = roof.GetCenter();
                DrawCircle(hc, 30, baseCol.Darkened(0.2f));
                DrawArc(hc, 30, 0, MathF.Tau, 24, new Color(0.9f, 0.9f, 0.9f, 0.6f), 2);
                UITheme.Write(this, hc + new Vector2(-8, 9), "H", 24, new Color(0.9f, 0.9f, 0.9f, 0.6f), heading: true, outline: 1);
            }
            // A neon billboard standing on the front edge.
            if (l.Sign != 0)
            {
                Color sc = Art.C(l.Sign);
                int fi = l.Facing.Y < 0 ? 0 : l.Facing.X > 0 ? 1 : l.Facing.Y > 0 ? 2 : 3;
                Vector2 e1 = top[fi], e2 = top[(fi + 1) % 4];
                Vector2 a1 = e1.Lerp(e2, 0.18f), a2 = e1.Lerp(e2, 0.82f);
                Vector2 rise = lean * 0.35f + new Vector2(0, -10);
                Poly(new[] { a1, a2, a2 + rise, a1 + rise }, new Color(0.03f, 0.03f, 0.05f, 0.95f));
                DrawPolyline(new[] { a1, a2, a2 + rise, a1 + rise, a1 }, sc, 2);
                int bars = 3 + (int)(Art.Hash(l.Index, 30) * 4);
                for (int k = 0; k < bars; k++)
                {
                    float u0 = 0.12f + k * 0.76f / bars, u1 = u0 + 0.76f / bars * 0.7f;
                    Vector2 m0 = a1.Lerp(a2, u0) + rise * 0.5f, m1 = a1.Lerp(a2, u1) + rise * 0.5f;
                    DrawLine(m0, m1, sc.Lightened(0.3f), 3);
                }
            }
            // Rooftop graffiti: who runs the street, and who they talk about.
            if (V.Tags.TryGetValue(l.Index, out var tag) && l.Place == null)
                UITheme.Write(this, roof.GetCenter() + new Vector2(-roof.Size.X / 2, 4), tag.text, 15, Art.C(tag.colour) with { A = 0.8f }, HorizontalAlignment.Center, roof.Size.X, heading: true, outline: 1);
            bool ownLoft = V.Sim.C.HasUpgrade("loft");
            if (l.Place != null && !(l.Place == "loft" && !ownLoft) && !(l.Place == "home" && ownLoft))
            {
                string label = City.PlaceName(l.Place).ToUpperInvariant();
                Color sc = Art.C(l.Sign);
                UITheme.Write(this, roof.GetCenter() + new Vector2(-roof.Size.X / 2 - 40, 6), label, 17, sc, HorizontalAlignment.Center, roof.Size.X + 80, heading: true);
            }
            else if (V.Sim.C.Active != null && V.Sim.C.Active.Lot == l.Index)
            {
                UITheme.Write(this, roof.GetCenter() + new Vector2(-roof.Size.X / 2 - 40, 6), "JOB SITE", 17, UITheme.Magenta, HorizontalAlignment.Center, roof.Size.X + 80, heading: true);
            }
        }

        /// <summary>A quad that may have collapsed to a line when the lean is tiny: skip it rather than fail to triangulate.</summary>
        private void Poly(Vector2[] q, Color c)
        {
            float area = 0;
            for (int i = 0; i < q.Length; i++) { Vector2 a = q[i], b = q[(i + 1) % q.Length]; area += a.X * b.Y - b.X * a.Y; }
            if (MathF.Abs(area) < 2f) return;
            // A twisted quad (lean across an edge) is drawn as two triangles instead.
            if (q.Length == 4)
            {
                DrawPrimitive(new[] { q[0], q[1], q[2] }, new[] { c, c, c }, null);
                DrawPrimitive(new[] { q[0], q[2], q[3] }, new[] { c, c, c }, null);
                return;
            }
            DrawColoredPolygon(q, c);
        }

        private static Color DistrictTint(Lot l, DistrictDef d)
        {
            float v = Art.Hash(l.Index, 9) * 0.06f;
            return l.District switch
            {
                "spire" => new Color(0.26f + v, 0.31f + v, 0.38f + v),
                "strip" => new Color(0.27f + v, 0.2f + v, 0.31f + v),
                "docks" => new Color(0.3f + v, 0.29f + v, 0.26f + v),
                "industrial" => new Color(0.33f + v, 0.3f + v, 0.22f + v),
                _ => new Color(0.32f + v, 0.23f + v, 0.18f + v),
            };
        }
    }

    /// <summary>Additive: neon on the roofs, headlights, the glow of places.</summary>
    private partial class GlowLayer : Node2D
    {
        public CityView V;

        public override void _Draw()
        {
            CitySim s = V.Sim;
            Rect2 view = V.ViewRect;
            foreach (Lot l in s.City.Lots)
            {
                if (l.Sign == 0 || l.Kind != LotKind.Building) continue;
                if (l.Place == "loft" && !s.C.HasUpgrade("loft") || l.Place == "home" && s.C.HasUpgrade("loft")) continue;
                var b = new Rect2(l.R.X * T, l.R.Y * T, l.R.W * T, l.R.H * T);
                if (!view.Intersects(b)) continue;
                Vector2 lean = V.Lean(b.GetCenter(), l.Height);
                Color c = Art.C(l.Sign);
                float flick = Art.Hash(l.Index, (int)(V.Time * 8)) > 0.97f ? 0.3f : 1f;
                // A neon strip along the roof edge facing the entrance, and its spill on the street.
                Vector2 f = new(l.Facing.X, l.Facing.Y);
                Vector2 edgeMid = b.GetCenter() + new Vector2(f.X * b.Size.X / 2, f.Y * b.Size.Y / 2);
                Vector2 along = new Vector2(-f.Y, f.X) * (f.X != 0 ? b.Size.Y : b.Size.X) * 0.35f;
                Vector2 top = edgeMid + lean;
                DrawLine(top - along, top + along, c with { A = 0.9f * flick }, 3);
                Art.DrawGlow(this, top, along.Length() * 1.2f, c with { A = 0.18f * flick });
                Art.DrawGlow(this, Art.G(l.Door.Centre, T), 48, c with { A = 0.22f });
            }
            foreach (Lot l in s.City.Lots)
            {
                if (l.Landmark == null) continue;
                var b = new Rect2(l.R.X * T, l.R.Y * T, l.R.W * T, l.R.H * T);
                if (!view.Intersects(b)) continue;
                Vector2 cen = b.GetCenter();
                switch (l.Landmark)
                {
                    case "arcology":
                        if (MathF.Sin(V.Time * 3) > 0.3f) Art.DrawGlow(this, cen + V.Lean(cen, l.Height), 60, new Color(1, 0.1f, 0.1f, 0.7f));
                        Art.DrawGlow(this, cen + V.Lean(cen, l.Height * 0.5f), b.Size.X * 0.7f, new Color(0.3f, 0.7f, 1f, 0.12f));
                        break;
                    case "pagoda": Art.DrawGlow(this, cen + V.Lean(cen, l.Height * 0.6f), b.Size.X * 0.8f, new Color(1, 0.2f, 0.6f, 0.18f)); break;
                    case "cathedral": Art.DrawGlow(this, cen + V.Lean(cen, l.Height), 70, new Color(1f, 0.75f, 0.35f, 0.25f)); break;
                    case "stadium":
                        foreach (var (dx, dy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                            Art.DrawGlow(this, cen + new Vector2(dx * b.Size.X * 0.42f, dy * b.Size.Y * 0.42f) + V.Lean(cen, l.Height * 1.8f), 90, new Color(1, 1, 0.9f, 0.35f));
                        break;
                    case "plaza": Art.DrawGlow(this, cen, 90, new Color(0.3f, 0.7f, 1f, 0.22f)); break;
                    case "port": Art.DrawGlow(this, cen, b.Size.X * 0.6f, new Color(1f, 0.7f, 0.3f, 0.12f)); break;
                }
            }
            foreach (TrafficCar t in s.Traffic)
            {
                Vector2 at = Art.G(t.Pos, T);
                if (!view.HasPoint(at)) continue;
                Vector2 f = new(MathF.Cos(t.Heading), MathF.Sin(t.Heading));
                Art.DrawGlow(this, at + f * T * 2.2f, T * 1.6f, new Color(1, 0.95f, 0.8f, 0.16f));
            }
            foreach (Chaser ch in s.Chasers)
            {
                Vector2 at = Art.G(ch.Car.Pos, T);
                if (!view.HasPoint(at)) continue;
                Vector2 f = new(MathF.Cos(ch.Car.Heading), MathF.Sin(ch.Car.Heading));
                Art.DrawGlow(this, at + f * T * 2.2f, T * 1.8f, new Color(1, 0.95f, 0.8f, 0.18f));
                if (ch.Kind == ChaserKind.Police)
                {
                    bool ph = MathF.Sin(V.Time * 14) > 0;
                    Art.DrawGlow(this, at, T * 3.2f, ph ? new Color(1, 0.1f, 0.1f, 0.35f) : new Color(0.1f, 0.3f, 1f, 0.35f));
                }
            }
            Car car = s.Player;
            if (V.HidePlayer) return;
            Vector2 cf = new(MathF.Cos(car.Heading), MathF.Sin(car.Heading));
            Vector2 cp = Art.G(car.Pos, T);
            Art.DrawGlow(this, cp + cf * T * 2.6f, T * 2.2f, new Color(1, 0.95f, 0.85f, 0.25f));
            Art.DrawGlow(this, cp + cf * T * 4.5f, T * 2.6f, new Color(1, 0.95f, 0.85f, 0.12f));
            if (V.Braking) Art.DrawGlow(this, cp - cf * T * 1.3f, T * 0.9f, new Color(1, 0.1f, 0.05f, 0.4f));
        }
    }
}
