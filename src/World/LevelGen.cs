using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

public sealed class Room
{
    public int Id;
    /// <summary>The floor inside the walls.</summary>
    public Rect R;
    public List<int> Links = new();
    public int Depth = -1;
    public bool Vault, Outer;
    /// <summary>What it's for (see <see cref="SetPieces"/>).</summary>
    public RoomKind Kind;
    public V2 Centre => new(R.X + R.W / 2f, R.Y + R.H / 2f);
}

public sealed class Spawn
{
    public EnemyKind Kind;
    public V2 Pos;
    public float Facing;
    public List<V2> Patrol = new();
    public bool Bodyguard;
    public Mood Mood;
    /// <summary>A den's drinker (slower, worse aim, slower to notice); an office guard who carries the keycard.</summary>
    public bool Drunk, Badge;
}

public enum PickupKind { Cash, Medkit, Grenade, Loot, Objective, Clue, Suit, Badge }

public sealed class PickupSpawn
{
    public PickupKind Kind;
    public V2 Pos;
    public int Amount;
    public string Name, Id;
}

/// <summary>A way in, chosen on the briefing map: where you start, and what it costs you.</summary>
public sealed record EntryPoint(string Id, string Name, string Risk, Pt At);

/// <summary>Everything a run starts from: the map and what is on it.</summary>
public sealed class LevelPlan
{
    public Level L;
    public SiteStyle Style;
    public Pt Entry;
    public Rect Extraction;
    public List<Room> Rooms = new();
    public int[] RoomOf;
    public List<Spawn> Enemies = new();
    public List<PickupSpawn> Pickups = new();
    public List<Pt> Targets = new();
    public Pt Mainframe = new(-1, -1);
    public V2 Hostage = new(-1, -1);
    /// <summary>Plant jobs: the spot the device goes.</summary>
    public V2 PlantAt = new(-1, -1);
    public List<V2> Reinforce = new();
    /// <summary>Hubs: people about, with the vendor they are (or null for the crowd).</summary>
    public List<(V2 pos, string vendor)> Npcs = new();
    public Rect Building;
    /// <summary>The ways in (the front first); the job's choice picks one (see <see cref="Job.Entry"/>).</summary>
    public List<EntryPoint> Entries = new();
    /// <summary>The panic room the target runs to when the alarm goes (W 0: none).</summary>
    public Rect PanicRoom;
    /// <summary>A lab's clean room (entering without a suit trips its sensors), and a warehouse's second way out through the loading dock (W 0: none).</summary>
    public Rect CleanRoom, BackDoor;
    /// <summary>The room a point is in, and what kind it is (Plain outside any room).</summary>
    public RoomKind KindAt(Pt p) => L.In(p) && RoomOf[L.Idx(p.X, p.Y)] is int i && i >= 0 && i < Rooms.Count ? Rooms[i].Kind : RoomKind.Plain;
    /// <summary>The basement, when the site has one (W 0 otherwise), and the main map's width.</summary>
    public Rect Basement;
    public int MainW;

    /// <summary>
    /// The map's floors, side by side: each a band of columns [x0, x1). Empty
    /// for an ordinary site (one floor, the whole map); the ground floor and
    /// the basement when there is one; or a tower's lobby and its floors.
    /// </summary>
    public List<(int x0, int x1, string name)> Regions = new();
    /// <summary>A tower: each floor's building, its elevator panel and the spot in front of it, and whether it is a secure floor.</summary>
    public List<Rect> Floors = new();
    public List<Pt> Lifts = new();
    public List<V2> LiftSpots = new();
    public List<bool> Secure = new();
    public bool Tower => Lifts.Count > 0;

    /// <summary>Which floor a point is on (0 for a single-floor site).</summary>
    public int FloorAt(float x)
    {
        for (int k = Regions.Count - 1; k >= 0; k--) if (x >= Regions[k].x0) return k;
        return 0;
    }

    public (int x0, int x1) Bounds(int floor) => Regions.Count == 0 ? (0, L.W) : (Regions[floor].x0, Regions[floor].x1);
    public string FloorName(int floor) => Regions.Count == 0 ? "" : Regions[floor].name;
}

/// <summary>
/// Builds a run's site from its job: a yard round a building of BSP rooms,
/// doors on every split (so the rooms form a tree), a few extra doors for
/// loops, a vault on a leaf, objectives deep inside, cover, loot and guards.
/// A flood fill from the entry proves everything that matters is reachable.
/// </summary>
public sealed class LevelGen
{
    private readonly Job _job;
    private readonly Rng _r;
    private Level L;
    private LevelPlan P;
    private Tile _inner;
    private Floor _floor;
    private readonly List<(Rect a, Rect b, bool vertical, int at, int from, int to)> _splits = new();

    private LevelGen(Job job)
    {
        _job = job;
        _r = new Rng(job.Seed);
        _k = new Rng(job.Seed ^ 0x6005E7B1ECEUL);
    }

    /// <summary>Room kinds and set pieces draw from their own stream, so they don't reshuffle the rest of the site.</summary>
    private readonly Rng _k;
    /// <summary>Spots for guards who start off duty or asleep (filled from the site's budget), and each room's objective spot.</summary>
    private readonly List<(V2 pos, Mood mood, float facing)> _moods = new();
    private readonly Dictionary<Room, V2> _hot = new();

    public static LevelPlan Generate(Job job) => job.Type == JobType.Hack ? HomeRig(job) : new LevelGen(job).Build();

    /// <summary>A Matrix job from home: your own four walls, the rig against one of them.</summary>
    private static LevelPlan HomeRig(Job job)
    {
        int w = 13, h = 9;
        var L = new Level(w, h);
        var P = new LevelPlan { L = L, Style = SiteStyle.Office, RoomOf = new int[w * h], MainW = w, Building = new Rect(1, 1, w - 2, h - 2) };
        Array.Fill(P.RoomOf, -1);
        for (int i = 0; i < w * h; i++) { L.F[i] = Floor.Wood; L.T[i] = Tile.Floor; }
        for (int x = 0; x < w; x++) { L.Set(x, 0, Tile.Reinforced); L.Set(x, h - 1, Tile.Reinforced); }
        for (int y = 0; y < h; y++) { L.Set(0, y, Tile.Reinforced); L.Set(w - 1, y, Tile.Reinforced); }
        L.Set(8, 2, Tile.Mainframe);
        P.Mainframe = new Pt(8, 2);
        L.Set(3, 6, Tile.Desk);
        L.Set(10, 6, Tile.Crate);
        P.Entry = new Pt(4, 4);
        P.Extraction = new Rect(3, 3, 3, 3);
        P.Reinforce.Add(new V2(w - 2.5f, h - 2.5f));
        L.Buildings.Add(P.Building);
        Array.Fill(L.Explored, true);
        return P;
    }

    private LevelPlan Build()
    {
        int t = Math.Clamp(_job.Threat, 1, 5);
        int w = 52 + 7 * t, h = 38 + 5 * t;
        L = new Level(w, h);
        P = new LevelPlan { L = L, Style = _job.Style, RoomOf = new int[w * h] };
        Array.Fill(P.RoomOf, -1);
        SiteStyle st = _job.Style;
        Floor yard = st is SiteStyle.Office or SiteStyle.Club ? Floor.Street : Floor.Yard;
        for (int i = 0; i < w * h; i++) { L.F[i] = yard; L.T[i] = Tile.Floor; }
        for (int x = 0; x < w; x++) { L.Set(x, 0, Tile.Reinforced); L.Set(x, h - 1, Tile.Reinforced); }
        for (int y = 0; y < h; y++) { L.Set(0, y, Tile.Reinforced); L.Set(w - 1, y, Tile.Reinforced); }

        // Entry on one edge; the building sits back from it.
        int side = _r.Int(4);
        int far = _r.Range(10, 13), near = _r.Range(3, 6);
        int mL = near, mR = near, mT = near, mB = near;
        switch (side)
        {
            case 0: mL = far; P.Entry = new Pt(2, _r.Range(h / 3, 2 * h / 3)); break;
            case 1: mR = far; P.Entry = new Pt(w - 3, _r.Range(h / 3, 2 * h / 3)); break;
            case 2: mT = far; P.Entry = new Pt(_r.Range(w / 3, 2 * w / 3), 2); break;
            default: mB = far; P.Entry = new Pt(_r.Range(w / 3, 2 * w / 3), h - 3); break;
        }
        var b = new Rect(mL, mT, w - mL - mR, h - mT - mB);
        P.Building = b;
        L.Buildings.Add(b);
        P.Extraction = ZoneAt(P.Entry, side);
        if (_job.Type == JobType.Retrieve && !_job.Tutorial && _r.Chance(0.5f))
        {
            int other = side ^ 1;
            Pt ex = other switch
            {
                0 => new Pt(2, _r.Range(4, h - 5)), 1 => new Pt(w - 3, _r.Range(4, h - 5)),
                2 => new Pt(_r.Range(4, w - 5), 2), _ => new Pt(_r.Range(4, w - 5), h - 3),
            };
            P.Extraction = ZoneAt(ex, other);
        }

        Tile outer = st == SiteStyle.Den ? Tile.Brick : Tile.Concrete;
        _inner = st == SiteStyle.Den ? Tile.Brick : Tile.Drywall;
        _floor = st switch
        {
            SiteStyle.Warehouse => Floor.Concrete, SiteStyle.Office => Floor.Carpet, SiteStyle.Den => Floor.Wood,
            SiteStyle.Lab => Floor.LabTile, _ => Floor.Carpet,
        };
        for (int y = b.Y; y < b.Bottom; y++)
        for (int x = b.X; x < b.Right; x++)
        {
            bool edge = x == b.X || y == b.Y || x == b.Right - 1 || y == b.Bottom - 1;
            L.Set(x, y, edge ? outer : Tile.Floor);
            L.F[L.Idx(x, y)] = _floor;
        }

        (int min, int max) = st switch
        {
            SiteStyle.Warehouse => (6, 15), SiteStyle.Office => (4, 8), SiteStyle.Den => (5, 11), SiteStyle.Lab => (5, 9), _ => (4, 8),
        };
        Rect inside = b.Inset(1);
        switch (st)
        {
            case SiteStyle.Club: BuildClub(inside, side, min, max); break;
            case SiteStyle.Office when inside.W >= 14 && inside.H >= 12: BuildOffice(inside); break;
            case SiteStyle.Lab when inside.W >= 22 && inside.H >= 18: BuildLab(inside); break;
            case SiteStyle.Warehouse when inside.W >= 16 && inside.H >= 12: BuildWarehouse(inside, side); break;
            default: Split(inside, min, max, 0); break;
        }

        foreach (var s in _splits) PlaceSplitDoor(s);
        IndexRooms();
        LinkRooms();
        OuterDoors(b, side);
        Windows(b, outer);
        P.MainW = L.W;
        if (_job.Floors > 0) Tower();
        else if (_job.Threat >= 3 && !_job.Driving && _job.Style != SiteStyle.Club && _r.Chance(0.6f)) Basement();
        if (st == SiteStyle.Den) { IndexRooms(); Decay(); }
        IndexRooms();
        LinkRooms();
        Depths();
        Vault();
        ExtraDoors();
        IndexRooms();
        LinkRooms();
        Depths();

        AssignKinds();
        StampKinds();
        Objectives();
        SpecialRooms();
        Furnish(yard);
        EnsureReachable();
        Lights(b);
        Defenders();
        Entries();
        Loot();
        if (_job.Style == SiteStyle.Club)
        {
            // Clubbers on the floor -- in the way, in the crossfire, and not your enemy.
            Room floor = P.Rooms.FirstOrDefault(r => L.F[L.Idx(r.R.X, r.R.Y)] == Floor.Dance);
            for (int k = 0; floor != null && k < 10; k++)
            {
                V2 c = FreeSpot(floor);
                if (L[Pt.Of(c)] == Tile.Floor) P.Npcs.Add((c, null));
            }
            // And some of the muscle out on the floor too.
            foreach (Spawn s in P.Enemies.Where(e => e.Kind is not (EnemyKind.Camera or EnemyKind.Turret or EnemyKind.Vip or EnemyKind.Boss) && !e.Bodyguard).Take(2))
                if (floor != null) { s.Pos = FreeSpot(floor); s.Patrol.Clear(); }
        }
        // Night: the light is the lamps' (the hand-drawn checks' sites stay fully lit).
        P.L.Ambient = Tables.NightAmbient;
        return P;
    }

    private static Rect ZoneAt(Pt p, int side) => side switch
    {
        0 => new Rect(1, p.Y - 2, 3, 4),
        1 => new Rect(p.X - 1, p.Y - 2, 3, 4),
        2 => new Rect(p.X - 2, 1, 4, 3),
        _ => new Rect(p.X - 2, p.Y - 1, 4, 3),
    };

    // ---- rooms ------------------------------------------------------------------

    /// <summary>BSP: split until rooms fit; each split line becomes wall, joined later by one door.</summary>
    private void Split(Rect r, int min, int max, int depth)
    {
        bool canV = r.W >= 2 * min + 1, canH = r.H >= 2 * min + 1;
        bool want = r.W > max || r.H > max || (depth < 2 && (canV || canH)) || (_r.Chance(0.3f) && (r.W > min * 2 + 2 || r.H > min * 2 + 2));
        if (!want || (!canV && !canH)) return;
        bool vertical = canV && (!canH || r.W > r.H || (r.W == r.H && _r.Chance(0.5f)));
        if (vertical)
        {
            int at = _r.Range(r.X + min, r.Right - min - 1);
            for (int y = r.Y; y < r.Bottom; y++) L.Set(at, y, WallFor());
            var a = new Rect(r.X, r.Y, at - r.X, r.H);
            var c = new Rect(at + 1, r.Y, r.Right - at - 1, r.H);
            _splits.Add((a, c, true, at, r.Y, r.Bottom));
            Split(a, min, max, depth + 1);
            Split(c, min, max, depth + 1);
        }
        else
        {
            int at = _r.Range(r.Y + min, r.Bottom - min - 1);
            for (int x = r.X; x < r.Right; x++) L.Set(x, at, WallFor());
            var a = new Rect(r.X, r.Y, r.W, at - r.Y);
            var c = new Rect(r.X, at + 1, r.W, r.Bottom - at - 1);
            _splits.Add((a, c, false, at, r.X, r.Right));
            Split(a, min, max, depth + 1);
            Split(c, min, max, depth + 1);
        }
    }

    private Tile WallFor() => _job.Style == SiteStyle.Lab && _r.Chance(0.35f) ? Tile.Glass : _inner;

    // ---- layouts ------------------------------------------------------------------

    /// <summary>A wall line from a to b (inclusive), axis-aligned.</summary>
    private void WallLine(Pt a, Pt b, Tile t)
    {
        int dx = Math.Sign(b.X - a.X), dy = Math.Sign(b.Y - a.Y);
        for (Pt p = a; ; p += new Pt(dx, dy))
        {
            if (L[p] == Tile.Floor) L.Set(p, t);
            if (p == b) break;
        }
    }

    /// <summary>A door somewhere along a wall run, where both sides are floor.</summary>
    private bool DoorAlong(Pt a, Pt b, int width = 1)
    {
        var opts = new List<Pt>();
        int dx = Math.Sign(b.X - a.X), dy = Math.Sign(b.Y - a.Y);
        Pt across = new(dy, dx);
        for (Pt p = a; ; p += new Pt(dx, dy))
        {
            if (IsWall(L[p]) && L[p] != Tile.Reinforced && L[p + across] == Tile.Floor && L[p - across] == Tile.Floor) opts.Add(p);
            if (p == b) break;
        }
        if (opts.Count == 0) return false;
        Pt d = opts[_r.Int(opts.Count)];
        L.Set(d, Tile.Door);
        for (int k = 1; k < width; k++)
        {
            Pt e = d + new Pt(dx * k, dy * k);
            if (opts.Contains(e)) L.Set(e, Tile.DoorOpen);
        }
        return true;
    }

    /// <summary>
    /// Offices: a corridor down the long axis, rooms off both sides with a door
    /// each onto it -- glass-fronted meeting rooms among them -- one side's
    /// middle left open as an open-plan floor, and the executive suite at the end.
    /// </summary>
    private void BuildOffice(Rect inside)
    {
        bool wide = inside.W >= inside.H;
        int len = wide ? inside.W : inside.H, dep = wide ? inside.H : inside.W;
        int c0 = dep / 2 - 1; // corridor occupies c0, c0+1 across
        Pt At(int u, int v) => wide ? new Pt(inside.X + u, inside.Y + v) : new Pt(inside.X + v, inside.Y + u);
        WallLine(At(0, c0 - 1), At(len - 1, c0 - 1), _inner);
        WallLine(At(0, c0 + 2), At(len - 1, c0 + 2), _inner);
        int openFrom = len / 3 + _r.Int(len / 6), openTo = openFrom + len / 3;
        foreach (bool top in new[] { true, false })
        {
            int v0 = top ? 0 : c0 + 3, v1 = top ? c0 - 2 : dep - 1;
            if (v1 - v0 < 2) continue;
            int u = 0;
            while (u < len)
            {
                bool open = top && u >= openFrom && u < openTo;
                int w = open ? openTo - u : Math.Min(len - u, _r.Range(4, 7));
                if (len - u - w < 3) w = len - u;
                int ue = u + w - 1;
                if (ue < len - 1) WallLine(At(ue + 1, v0), At(ue + 1, v1), _inner);
                // The door onto the corridor; meeting rooms get a glass front.
                Pt wa = At(u, top ? c0 - 1 : c0 + 2), wb = At(ue, top ? c0 - 1 : c0 + 2);
                if (!open && _r.Chance(0.3f)) for (int k = u; k <= ue; k++) { Pt g = At(k, top ? c0 - 1 : c0 + 2); if (L[g] == _inner) L.Set(g, Tile.Glass); }
                DoorAlong(wa, wb, open ? 2 : 1);
                if (open) DoorAlong(wa, wb);
                u = ue + 2;
            }
        }
        // The corridor's ends get the building's interior wall broken by a door where the rooms run out.
    }

    /// <summary>
    /// Labs: a ring corridor inside the outer wall, offices and labs round the
    /// outside of it, a glass clean room in the middle with two airlocks.
    /// </summary>
    private void BuildLab(Rect inside)
    {
        int ring = 5;
        var inner = new Rect(inside.X + ring, inside.Y + ring, inside.W - 2 * ring, inside.H - 2 * ring);
        // Outer wall of the corridor (between the rooms and the corridor) and the clean room's glass.
        var outerRim = new Rect(inner.X - 3, inner.Y - 3, inner.W + 6, inner.H + 6);
        Rim(outerRim, _inner);
        Rim(inner, Tile.Glass);
        DoorAlong(new Pt(inner.X, inner.Y), new Pt(inner.Right - 1, inner.Y), 2);
        DoorAlong(new Pt(inner.X, inner.Bottom - 1), new Pt(inner.Right - 1, inner.Bottom - 1), 2);
        // Rooms round the outside, each opening onto the corridor.
        void Strip(Pt from, Pt step, int count, Pt depthDir, int depth)
        {
            int k = 0;
            while (k < count)
            {
                int w = Math.Min(count - k, _r.Range(4, 7));
                if (count - k - w < 3) w = count - k;
                Pt a = from + new Pt(step.X * k, step.Y * k), b = from + new Pt(step.X * (k + w - 1), step.Y * (k + w - 1));
                Pt end = b + step;
                if (k + w < count) WallLine(end, end + new Pt(depthDir.X * (depth - 1), depthDir.Y * (depth - 1)), _inner);
                Pt wa = a + new Pt(depthDir.X * depth, depthDir.Y * depth), wb = b + new Pt(depthDir.X * depth, depthDir.Y * depth);
                DoorAlong(wa, wb);
                k += w + 1;
            }
        }
        int d = outerRim.Y - inside.Y;
        Strip(new Pt(inside.X, inside.Y), new Pt(1, 0), inside.W, new Pt(0, 1), d);
        Strip(new Pt(inside.X, inside.Bottom - 1), new Pt(1, 0), inside.W, new Pt(0, -1), d);
        Strip(new Pt(inside.X, outerRim.Y + 1), new Pt(0, 1), outerRim.H - 2, new Pt(1, 0), outerRim.X - inside.X);
        Strip(new Pt(inside.Right - 1, outerRim.Y + 1), new Pt(0, 1), outerRim.H - 2, new Pt(-1, 0), inside.Right - outerRim.Right);
        // Corners where strips meet need closing off.
        WallLine(new Pt(inside.X, outerRim.Y), new Pt(outerRim.X - 1, outerRim.Y), _inner);
        WallLine(new Pt(outerRim.Right, outerRim.Y), new Pt(inside.Right - 1, outerRim.Y), _inner);
        WallLine(new Pt(inside.X, outerRim.Bottom - 1), new Pt(outerRim.X - 1, outerRim.Bottom - 1), _inner);
        WallLine(new Pt(outerRim.Right, outerRim.Bottom - 1), new Pt(inside.Right - 1, outerRim.Bottom - 1), _inner);
        for (int y = inner.Y + 1; y < inner.Bottom - 1; y++)
        for (int x = inner.X + 1; x < inner.Right - 1; x++) L.F[L.Idx(x, y)] = Floor.LabTile;
        P.CleanRoom = inner.Inset(1);
    }

    private void Rim(Rect r, Tile t)
    {
        WallLine(new Pt(r.X, r.Y), new Pt(r.Right - 1, r.Y), t);
        WallLine(new Pt(r.X, r.Bottom - 1), new Pt(r.Right - 1, r.Bottom - 1), t);
        WallLine(new Pt(r.X, r.Y), new Pt(r.X, r.Bottom - 1), t);
        WallLine(new Pt(r.Right - 1, r.Y), new Pt(r.Right - 1, r.Bottom - 1), t);
    }

    /// <summary>Warehouses: one big hall, a strip of offices along the back, loading bays on the front.</summary>
    private void BuildWarehouse(Rect inside, int entrySide)
    {
        bool wide = inside.W >= inside.H;
        int depth = 5;
        // Offices along the side away from the way in.
        Rect strip, hall;
        if (wide) { int y = entrySide == 2 ? inside.Bottom - depth : inside.Y; strip = new Rect(inside.X, y, inside.W, depth); hall = entrySide == 2 ? new Rect(inside.X, inside.Y, inside.W, inside.H - depth - 1) : new Rect(inside.X, inside.Y + depth + 1, inside.W, inside.H - depth - 1); }
        else { int x = entrySide == 0 ? inside.Right - depth : inside.X; strip = new Rect(x, inside.Y, depth, inside.H); hall = entrySide == 0 ? new Rect(inside.X, inside.Y, inside.W - depth - 1, inside.H) : new Rect(inside.X + depth + 1, inside.Y, inside.W - depth - 1, inside.H); }
        Pt wa, wb;
        if (wide) { int y = strip.Y == inside.Y ? strip.Bottom : strip.Y - 1; wa = new Pt(inside.X, y); wb = new Pt(inside.Right - 1, y); }
        else { int x = strip.X == inside.X ? strip.Right : strip.X - 1; wa = new Pt(x, inside.Y); wb = new Pt(x, inside.Bottom - 1); }
        WallLine(wa, wb, _inner);
        // Split the strip into offices along its length.
        int len = wide ? strip.W : strip.H, k = 0;
        while (k < len)
        {
            int w = Math.Min(len - k, _r.Range(4, 7));
            if (len - k - w < 3) w = len - k;
            if (k + w < len)
            {
                Pt a = wide ? new Pt(strip.X + k + w, strip.Y) : new Pt(strip.X, strip.Y + k + w);
                Pt b = wide ? new Pt(strip.X + k + w, strip.Bottom - 1) : new Pt(strip.Right - 1, strip.Y + k + w);
                WallLine(a, b, _inner);
            }
            Pt da = wide ? new Pt(strip.X + k, wa.Y) : new Pt(wa.X, strip.Y + k), db = wide ? new Pt(strip.X + k + w - 1, wa.Y) : new Pt(wa.X, strip.Y + k + w - 1);
            DoorAlong(da, db);
            k += w + 1;
        }
        _hall = hall;
    }

    private Rect _hall;

    /// <summary>Gang dens: the building is falling apart -- holes in the walls, a courtyard open to the sky.</summary>
    private void Decay()
    {
        Rect inside = P.Building.Inset(1);
        // Holes only where they join two floors (never a lone pocket of rubble in a wall junction).
        bool Hole(int x, int y) => L[x - 1, y] == Tile.Floor && L[x + 1, y] == Tile.Floor || L[x, y - 1] == Tile.Floor && L[x, y + 1] == Tile.Floor;
        for (int y = inside.Y; y < inside.Bottom; y++)
        for (int x = inside.X; x < inside.Right; x++)
            if (L[x, y] == _inner && Hole(x, y) && _r.Chance(0.07f)) L.Set(x, y, Tile.Rubble);
        // One room loses its roof: yard floor, broken walls.
        var room = P.Rooms.Where(r => r.R.Area >= 20 && !r.Vault).OrderBy(_ => _r.Next()).FirstOrDefault();
        if (room == null) return;
        for (int y = room.R.Y; y < room.R.Bottom; y++)
        for (int x = room.R.X; x < room.R.Right; x++) L.F[L.Idx(x, y)] = Floor.Yard;
        Rect ring = new(room.R.X - 1, room.R.Y - 1, room.R.W + 2, room.R.H + 2);
        for (int y = ring.Y; y < ring.Bottom; y++)
        for (int x = ring.X; x < ring.Right; x++)
            if (L[x, y] == _inner && Hole(x, y) && _r.Chance(0.25f)) L.Set(x, y, Tile.Rubble);
        L.Lights.Add((room.Centre, 0xff8030ff, 3));
    }

    private void BuildClub(Rect inside, int entrySide, int min, int max)
    {
        // Dance floor at the front, a strip of back rooms behind it.
        int depth = Math.Min(7, (entrySide is 0 or 1 ? inside.W : inside.H) / 3);
        Rect main, back;
        bool vertical = entrySide is 0 or 1;
        int at;
        switch (entrySide)
        {
            case 0: at = inside.Right - depth - 1; main = new Rect(inside.X, inside.Y, at - inside.X, inside.H); back = new Rect(at + 1, inside.Y, inside.Right - at - 1, inside.H); break;
            case 1: at = inside.X + depth; back = new Rect(inside.X, inside.Y, at - inside.X, inside.H); main = new Rect(at + 1, inside.Y, inside.Right - at - 1, inside.H); break;
            case 2: at = inside.Bottom - depth - 1; main = new Rect(inside.X, inside.Y, inside.W, at - inside.Y); back = new Rect(inside.X, at + 1, inside.W, inside.Bottom - at - 1); break;
            default: at = inside.Y + depth; back = new Rect(inside.X, inside.Y, inside.W, at - inside.Y); main = new Rect(inside.X, at + 1, inside.W, inside.Bottom - at - 1); break;
        }
        if (vertical) for (int y = inside.Y; y < inside.Bottom; y++) L.Set(at, y, _inner);
        else for (int x = inside.X; x < inside.Right; x++) L.Set(x, at, _inner);
        _splits.Add((main, back, vertical, at, vertical ? inside.Y : inside.X, vertical ? inside.Bottom : inside.Right));
        Split(back, min, max, 2);
        for (int y = main.Y; y < main.Bottom; y++)
        for (int x = main.X; x < main.Right; x++)
            L.F[L.Idx(x, y)] = Floor.Dance;
    }

    private void PlaceSplitDoor((Rect a, Rect b, bool vertical, int at, int from, int to) s)
    {
        var opts = new List<Pt>();
        for (int k = s.from; k < s.to; k++)
        {
            Pt p = s.vertical ? new Pt(s.at, k) : new Pt(k, s.at);
            Pt n1 = s.vertical ? new Pt(s.at - 1, k) : new Pt(k, s.at - 1);
            Pt n2 = s.vertical ? new Pt(s.at + 1, k) : new Pt(k, s.at + 1);
            if (L[n1] == Tile.Floor && L[n2] == Tile.Floor && IsWall(L[p])) opts.Add(p);
        }
        if (opts.Count == 0) return;
        Pt d = opts[_r.Int(opts.Count)];
        L.Set(d, Tile.Door);
        // Warehouses get wide doors.
        if (_job.Style == SiteStyle.Warehouse && _r.Chance(0.5f))
        {
            Pt d2 = s.vertical ? new Pt(d.X, d.Y + 1) : new Pt(d.X + 1, d.Y);
            if (opts.Contains(d2)) L.Set(d2, Tile.DoorOpen);
        }
    }

    private static bool IsWall(Tile t) => t is Tile.Drywall or Tile.Brick or Tile.Concrete or Tile.Glass or Tile.Reinforced or Tile.Lift;

    /// <summary>Rooms are the floor regions inside the building, found by flood fill (doors are boundaries).</summary>
    private void IndexRooms()
    {
        P.Rooms.Clear();
        Array.Fill(P.RoomOf, -1);
        foreach (Rect inside in new[] { P.Building.Inset(1), P.Basement.W > 0 ? P.Basement.Inset(1) : default }.Concat(P.Floors.Select(f => f.Inset(1))))
        for (int y = inside.Y; y < inside.Bottom; y++)
        for (int x = inside.X; x < inside.Right; x++)
        {
            int i = L.Idx(x, y);
            if (P.RoomOf[i] >= 0 || IsWall(L.T[i]) || L.T[i] is Tile.Door or Tile.DoorOpen or Tile.LockedDoor) continue;
            var room = new Room { Id = P.Rooms.Count };
            int minX = x, maxX = x, minY = y, maxY = y;
            var q = new Queue<Pt>();
            q.Enqueue(new Pt(x, y));
            P.RoomOf[i] = room.Id;
            while (q.Count > 0)
            {
                Pt p = q.Dequeue();
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                foreach (Pt d in Pt.Dirs4)
                {
                    Pt n = p + d;
                    if (!inside.Contains(n)) continue;
                    int ni = L.Idx(n.X, n.Y);
                    Tile t = L.T[ni];
                    if (P.RoomOf[ni] >= 0 || IsWall(t) || t is Tile.Door or Tile.DoorOpen or Tile.LockedDoor) continue;
                    P.RoomOf[ni] = room.Id;
                    q.Enqueue(n);
                }
            }
            room.R = new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
            // A pocket of a tile or two is not a room.
            if (room.R.Area < 4) { foreach (int k in Enumerable.Range(0, L.W * L.H).Where(k => P.RoomOf[k] == room.Id)) P.RoomOf[k] = -1; continue; }
            P.Rooms.Add(room);
        }
    }

    private IEnumerable<Pt> DoorTiles()
    {
        for (int y = 0; y < L.H; y++)
        for (int x = 0; x < L.W; x++)
            if (L[x, y] is Tile.Door or Tile.DoorOpen or Tile.LockedDoor) yield return new Pt(x, y);
    }

    private (int, int) DoorSides(Pt d)
    {
        int Room(Pt p) => L.In(p) ? P.RoomOf[L.Idx(p.X, p.Y)] : -1;
        int a = Room(d + new Pt(-1, 0)), b = Room(d + new Pt(1, 0));
        if (a >= 0 || b >= 0)
        {
            if (L[d + new Pt(-1, 0)] is Tile.Floor && L[d + new Pt(1, 0)] is Tile.Floor) return (a, b);
        }
        return (Room(d + new Pt(0, -1)), Room(d + new Pt(0, 1)));
    }

    private void LinkRooms()
    {
        foreach (Room r in P.Rooms) { r.Links.Clear(); r.Outer = false; }
        // The stairs join the room at the top to the room at the bottom.
        foreach (var kv in L.Stair)
        {
            int a = P.RoomOf[kv.Key], b = P.RoomOf[kv.Value];
            if (a >= 0 && b >= 0 && a != b && !P.Rooms[a].Links.Contains(b)) { P.Rooms[a].Links.Add(b); P.Rooms[b].Links.Add(a); }
        }
        foreach (Pt d in DoorTiles())
        {
            (int a, int b) = DoorSides(d);
            if (a >= 0 && b >= 0 && a != b)
            {
                if (!P.Rooms[a].Links.Contains(b)) P.Rooms[a].Links.Add(b);
                if (!P.Rooms[b].Links.Contains(a)) P.Rooms[b].Links.Add(a);
            }
            else if (a >= 0 && b < 0) P.Rooms[a].Outer = true;
            else if (b >= 0 && a < 0) P.Rooms[b].Outer = true;
        }
    }

    private void OuterDoors(Rect b, int entrySide)
    {
        // One on the side facing the entry, and one or two elsewhere.
        var sides = new List<int> { entrySide };
        foreach (int s in new[] { 0, 1, 2, 3 }.OrderBy(_ => _r.Next()))
            if (s != entrySide && sides.Count < 2 + (_r.Chance(0.5f) ? 1 : 0)) sides.Add(s);
        foreach (int s in sides)
        {
            var opts = new List<Pt>();
            if (s is 0 or 1)
            {
                int x = s == 0 ? b.X : b.Right - 1, dx = s == 0 ? 1 : -1;
                for (int y = b.Y + 2; y < b.Bottom - 2; y++)
                    if (L[x + dx, y] == Tile.Floor && L[x - dx, y] == Tile.Floor) opts.Add(new Pt(x, y));
            }
            else
            {
                int y = s == 2 ? b.Y : b.Bottom - 1, dy = s == 2 ? 1 : -1;
                for (int x = b.X + 2; x < b.Right - 2; x++)
                    if (L[x, y + dy] == Tile.Floor && L[x, y - dy] == Tile.Floor) opts.Add(new Pt(x, y));
            }
            if (opts.Count == 0) continue;
            // The entry-side door lines up with the way in when it can.
            Pt pick = s == entrySide ? opts.OrderBy(p => p.Chebyshev(P.Entry) + _r.Int(4)).First() : opts[_r.Int(opts.Count)];
            L.Set(pick, Tile.Door);
            if (_job.Style is SiteStyle.Warehouse or SiteStyle.Club)
            {
                Pt next = s is 0 or 1 ? pick + new Pt(0, 1) : pick + new Pt(1, 0);
                if (opts.Contains(next)) L.Set(next, Tile.DoorOpen);
            }
        }
    }

    /// <summary>
    /// A floor below: the map grows to the east, behind reinforced rock, and
    /// a basement of concrete rooms goes there, joined to the building above
    /// by a pair of stairs.
    /// </summary>
    private void Basement()
    {
        Level old = L;
        int bw = 22 + 2 * _job.Threat, bh = Math.Min(old.H - 4, 18 + _job.Threat);
        var nl = new Level(old.W + bw + 2, old.H);
        for (int y = 0; y < nl.H; y++)
        for (int x = 0; x < nl.W; x++)
        {
            int ni = nl.Idx(x, y);
            if (x < old.W) { nl.T[ni] = old.T[old.Idx(x, y)]; nl.Hp[ni] = old.Hp[old.Idx(x, y)]; nl.F[ni] = old.F[old.Idx(x, y)]; }
            else { nl.T[ni] = Tile.Reinforced; nl.Hp[ni] = float.PositiveInfinity; nl.F[ni] = Floor.Concrete; }
        }
        nl.Lights.AddRange(old.Lights);
        nl.Buildings.AddRange(old.Buildings);
        var roomOf = new int[nl.W * nl.H];
        Array.Fill(roomOf, -1);
        L = nl;
        P.L = nl;
        P.RoomOf = roomOf;
        var br = new Rect(old.W + 1, (old.H - bh) / 2, bw, bh);
        P.Basement = br;
        L.Buildings.Add(br);
        for (int y = br.Y; y < br.Bottom; y++)
        for (int x = br.X; x < br.Right; x++)
        {
            bool edge = x == br.X || y == br.Y || x == br.Right - 1 || y == br.Bottom - 1;
            L.Set(x, y, edge ? Tile.Concrete : Tile.Floor);
            L.F[L.Idx(x, y)] = Floor.Concrete;
        }
        int before = _splits.Count;
        Tile keep = _inner;
        _inner = Tile.Concrete;
        Split(br.Inset(1), 4, 9, 1);
        _inner = keep;
        for (int k = before; k < _splits.Count; k++) PlaceSplitDoor(_splits[k]);
        // Stairs: from a room upstairs (not one with a door to the street) to one downstairs.
        IndexRooms();
        Room top = P.Rooms.Where(r => r.R.X < old.W && r.R.Area >= 9).OrderByDescending(r => r.R.Area + _r.Int(20)).FirstOrDefault();
        Room bottom = P.Rooms.Where(r => r.R.X > old.W).OrderBy(r => _r.Next()).FirstOrDefault();
        if (top == null || bottom == null) return;
        Pt a = WallSpot(top), b = WallSpot(bottom);
        if (a.X < 0) a = Pt.Of(FreeSpot(top));
        if (b.X < 0) b = Pt.Of(FreeSpot(bottom));
        if (L[a] != Tile.Floor || L[b] != Tile.Floor)
        {
            // No room for stairs: no basement after all.
            for (int y = br.Y; y < br.Bottom; y++)
            for (int x = br.X; x < br.Right; x++) L.Set(x, y, Tile.Reinforced);
            L.Buildings.Remove(br);
            P.Basement = default;
            return;
        }
        L.AddStairs(a, b);
        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) { _reserved.Add(a + new Pt(dx, dy)); _reserved.Add(b + new Pt(dx, dy)); }
        L.Lights.Add((b.Centre, 0xffd080ff, 3f));
    }

    /// <summary>
    /// A tower: the map grows east by one band per floor, each a copy of the
    /// building's footprint hanging in the dark, with offices inside and a
    /// concrete core in the same place on every floor -- the elevator panel
    /// on one side, the stairs up and down on the other. The top floors are
    /// secure: their core doors are locked, and the elevator won't stop there
    /// without a pass.
    /// </summary>
    private void Tower()
    {
        int n = _job.Floors;
        Level old = L;
        Rect b = P.Building;
        int fw = b.W + 8;
        var nl = new Level(old.W + n * fw, old.H);
        for (int y = 0; y < nl.H; y++)
        for (int x = 0; x < nl.W; x++)
        {
            int ni = nl.Idx(x, y);
            if (x < old.W) { nl.T[ni] = old.T[old.Idx(x, y)]; nl.Hp[ni] = old.Hp[old.Idx(x, y)]; nl.F[ni] = old.F[old.Idx(x, y)]; }
            else { nl.T[ni] = Tile.Reinforced; nl.Hp[ni] = float.PositiveInfinity; nl.F[ni] = Floor.Concrete; }
        }
        nl.Lights.AddRange(old.Lights);
        nl.Buildings.AddRange(old.Buildings);
        L = nl;
        P.L = nl;
        P.RoomOf = new int[nl.W * nl.H];
        Array.Fill(P.RoomOf, -1);
        P.Regions.Add((0, old.W, "LOBBY"));
        // The core sits in the middle of the footprint, the same on every floor.
        var core = new Rect(b.X + b.W / 2 - 3, b.Y + b.H / 2 - 2, 7, 5);
        int secureFrom = n >= 3 ? n - 1 : n;
        for (int k = 0; k <= n; k++)
        {
            int dx = k == 0 ? 0 : old.W + (k - 1) * fw + 4 - b.X;
            if (k > 0)
            {
                var fb = new Rect(b.X + dx, b.Y, b.W, b.H);
                P.Floors.Add(fb);
                L.Buildings.Add(fb);
                P.Regions.Add((old.W + (k - 1) * fw, old.W + k * fw, $"FLOOR {k + 1}"));
                Floor carpet = _job.Style == SiteStyle.Lab ? Floor.LabTile : Floor.Carpet;
                for (int y = fb.Y; y < fb.Bottom; y++)
                for (int x = fb.X; x < fb.Right; x++)
                {
                    bool edge = x == fb.X || y == fb.Y || x == fb.Right - 1 || y == fb.Bottom - 1;
                    L.Set(x, y, edge ? Tile.Concrete : Tile.Floor);
                    L.F[L.Idx(x, y)] = carpet;
                }
                Windows(fb, Tile.Concrete);
                Rect inside = fb.Inset(1);
                int before = _splits.Count;
                if (inside.W >= 14 && inside.H >= 12) BuildOffice(inside);
                else Split(inside, 4, 8, 0);
                for (int q = before; q < _splits.Count; q++) PlaceSplitDoor(_splits[q]);
            }
            bool secure = k >= secureFrom;
            Rect c = new(core.X + dx, core.Y, core.W, core.H);
            for (int y = c.Y; y < c.Bottom; y++)
            for (int x = c.X; x < c.Right; x++)
            {
                bool edge = x == c.X || y == c.Y || x == c.Right - 1 || y == c.Bottom - 1;
                L.Set(x, y, edge ? Tile.Concrete : Tile.Floor);
                L.F[L.Idx(x, y)] = Floor.Concrete;
            }
            // The core's door onto the floor, and the floor kept clear in front of it.
            var door = new Pt(c.X + 3, c.Bottom - 1);
            L.Set(door, secure && k > 0 ? Tile.LockedDoor : Tile.Door);
            for (int q = 1; q <= 2; q++) { Pt f = door + new Pt(0, q); if (L.In(f) && L[f] is Tile.Drywall or Tile.Brick or Tile.Glass or Tile.Desk) L.Set(f, Tile.Floor); }
            var lift = new Pt(c.X + 1, c.Y + 1);
            L.Set(lift, Tile.Lift);
            P.Lifts.Add(lift);
            P.LiftSpots.Add(new Pt(c.X + 2, c.Y + 2).Centre);
            P.Secure.Add(secure && k > 0);
            for (int dy = -1; dy <= 3; dy++) for (int ddx = -1; ddx <= 6; ddx++) _reserved.Add(new Pt(c.X + ddx, c.Y + dy));
            L.Lights.Add((new Pt(c.X + 3, c.Y + 2).Centre, secure && k > 0 ? 0xff4040ffu : 0xd0e8ffffu, 3f));
        }
        // Stairs: up from the top of each core, down into the bottom of the next one.
        for (int k = 0; k < n; k++)
        {
            int dxa = k == 0 ? 0 : old.W + (k - 1) * fw + 4 - b.X, dxb = old.W + k * fw + 4 - b.X;
            L.AddStairs(new Pt(core.X + 5 + dxa, core.Y + 1), new Pt(core.X + 5 + dxb, core.Y + 3));
        }
    }

    private void Windows(Rect b, Tile outer)
    {
        float chance = _job.Style switch { SiteStyle.Office => 0.8f, SiteStyle.Lab => 0.6f, SiteStyle.Club => 0.1f, _ => 0.3f };
        void Run(Pt start, Pt step, Pt inward, int len)
        {
            int k = 2;
            while (k < len - 3)
            {
                if (_r.Chance(chance))
                {
                    int run = _r.Range(2, 3);
                    bool ok = true;
                    for (int q = 0; q < run; q++)
                    {
                        Pt p = start + new Pt(step.X * (k + q), step.Y * (k + q));
                        if (L[p] != outer || L[p + inward] != Tile.Floor) ok = false;
                    }
                    if (ok)
                        for (int q = 0; q < run; q++) L.Set(start + new Pt(step.X * (k + q), step.Y * (k + q)), Tile.Glass);
                    k += run + _r.Range(2, 4);
                }
                else k += 3;
            }
        }
        Run(new Pt(b.X, b.Y), new Pt(1, 0), new Pt(0, 1), b.W);
        Run(new Pt(b.X, b.Bottom - 1), new Pt(1, 0), new Pt(0, -1), b.W);
        Run(new Pt(b.X, b.Y), new Pt(0, 1), new Pt(1, 0), b.H);
        Run(new Pt(b.Right - 1, b.Y), new Pt(0, 1), new Pt(-1, 0), b.H);
    }

    private void Depths()
    {
        foreach (Room r in P.Rooms) r.Depth = -1;
        var q = new Queue<Room>();
        foreach (Room r in P.Rooms.Where(r => r.Outer)) { r.Depth = 0; q.Enqueue(r); }
        if (q.Count == 0 && P.Rooms.Count > 0) { P.Rooms[0].Depth = 0; q.Enqueue(P.Rooms[0]); }
        while (q.Count > 0)
        {
            Room r = q.Dequeue();
            foreach (int n in r.Links)
                if (P.Rooms[n].Depth < 0) { P.Rooms[n].Depth = r.Depth + 1; q.Enqueue(P.Rooms[n]); }
        }
        foreach (Room r in P.Rooms.Where(r => r.Depth < 0)) r.Depth = 0;
        // In a tower, the higher the floor the deeper the room: what matters is at the top.
        if (P.Tower) foreach (Room r in P.Rooms) r.Depth += P.FloorAt(r.R.X) * 1000;
    }

    private Room _vault;

    private void Vault()
    {
        bool wants = _job.Type is JobType.Retrieve or JobType.Datasteal or JobType.Rescue && _job.Threat >= 2;
        if (!wants) return;
        Room v = P.Rooms.Where(r => r.Links.Count == 1 && !r.Outer && r.R.Area >= 12)
            .OrderByDescending(r => r.Depth * 100 + _r.Int(50)).FirstOrDefault();
        if (v == null) return;
        v.Vault = true;
        _vault = v;
        Rect ring = new(v.R.X - 1, v.R.Y - 1, v.R.W + 2, v.R.H + 2);
        for (int y = ring.Y; y < ring.Bottom; y++)
        for (int x = ring.X; x < ring.Right; x++)
        {
            if (v.R.Contains(new Pt(x, y))) continue;
            Tile t = L[x, y];
            if (t is Tile.Door or Tile.DoorOpen) L.Set(x, y, Tile.LockedDoor);
            else if (IsWall(t)) L.Set(x, y, Tile.Reinforced);
        }
        for (int y = v.R.Y; y < v.R.Bottom; y++)
        for (int x = v.R.X; x < v.R.Right; x++)
            L.F[L.Idx(x, y)] = Floor.Concrete;
    }

    private void ExtraDoors()
    {
        // Loops: a door through some walls that separate two different rooms.
        int want = 1 + P.Rooms.Count / 4;
        for (int tries = 0; tries < 300 && want > 0; tries++)
        {
            Rect inside = P.Building.Inset(1);
            Pt p = new(_r.Range(inside.X, inside.Right - 1), _r.Range(inside.Y, inside.Bottom - 1));
            if (L[p] is not (Tile.Drywall or Tile.Brick or Tile.Glass)) continue;
            int a, b;
            if (L[p + new Pt(-1, 0)] == Tile.Floor && L[p + new Pt(1, 0)] == Tile.Floor)
            { a = P.RoomOf[L.Idx(p.X - 1, p.Y)]; b = P.RoomOf[L.Idx(p.X + 1, p.Y)]; }
            else if (L[p + new Pt(0, -1)] == Tile.Floor && L[p + new Pt(0, 1)] == Tile.Floor)
            { a = P.RoomOf[L.Idx(p.X, p.Y - 1)]; b = P.RoomOf[L.Idx(p.X, p.Y + 1)]; }
            else continue;
            if (a < 0 || b < 0 || a == b || P.Rooms[a].Vault || P.Rooms[b].Vault || P.Rooms[a].Links.Contains(b)) continue;
            L.Set(p, Tile.Door);
            P.Rooms[a].Links.Add(b);
            P.Rooms[b].Links.Add(a);
            want--;
        }
    }

    // ---- objectives --------------------------------------------------------------

    private Room Deepest(Func<Room, bool> ok = null) =>
        P.Rooms.Where(r => ok == null || ok(r)).OrderByDescending(r => r.Depth * 100 + Math.Min(r.R.Area, 60) + _r.Int(30)).FirstOrDefault();

    private V2 FreeSpot(Room r, int margin = 1)
    {
        for (int tries = 0; tries < 60; tries++)
        {
            int x = _r.Range(r.R.X + Math.Min(margin, r.R.W / 2), r.R.Right - 1 - Math.Min(margin, r.R.W / 2));
            int y = _r.Range(r.R.Y + Math.Min(margin, r.R.H / 2), r.R.Bottom - 1 - Math.Min(margin, r.R.H / 2));
            if (L[x, y] == Tile.Floor && !NearDoor(new Pt(x, y), 1)) return new Pt(x, y).Centre;
        }
        // Crowded room: any open floor in it will do.
        for (int y = r.R.Y; y < r.R.Bottom; y++)
        for (int x = r.R.X; x < r.R.Right; x++)
            if (L[x, y] == Tile.Floor) return new Pt(x, y).Centre;
        return r.Centre;
    }

    private bool NearDoor(Pt p, int d)
    {
        for (int y = p.Y - d; y <= p.Y + d; y++)
        for (int x = p.X - d; x <= p.X + d; x++)
            if (L[x, y] is Tile.Door or Tile.DoorOpen or Tile.LockedDoor) return true;
        return false;
    }

    /// <summary>A floor tile against a wall of the room, away from doors (for racks, the mainframe, turrets).</summary>
    private Pt WallSpot(Room r, HashSet<Pt> avoid = null)
    {
        var opts = new List<Pt>();
        for (int y = r.R.Y; y < r.R.Bottom; y++)
        for (int x = r.R.X; x < r.R.Right; x++)
        {
            var p = new Pt(x, y);
            if (L[p] != Tile.Floor || NearDoor(p, 2) || (avoid != null && avoid.Contains(p))) continue;
            bool edge = x == r.R.X || y == r.R.Y || x == r.R.Right - 1 || y == r.R.Bottom - 1;
            bool corner = (x == r.R.X || x == r.R.Right - 1) && (y == r.R.Y || y == r.R.Bottom - 1);
            if (edge && !corner) opts.Add(p);
        }
        return opts.Count == 0 ? new Pt(-1, -1) : opts[_r.Int(opts.Count)];
    }

    private readonly HashSet<Pt> _reserved = new();

    // ---- what rooms are for ------------------------------------------------------------

    /// <summary>
    /// Give some rooms a purpose, by the site's style: a manager's office, a
    /// break room, servers, barracks, an armoury, a ritual circle, a panic room
    /// for a target with something to fear. The rest stay plain.
    /// </summary>
    private void AssignKinds()
    {
        bool target = _job.Type is JobType.Eliminate or JobType.Kidnap;
        bool security = _job.Faction == Faction.Halcyon || _job.Threat >= 3;
        var wants = _job.Style switch
        {
            SiteStyle.Office => new (RoomKind, float)[] { (RoomKind.Manager, 0.85f), (RoomKind.Break, 0.7f), (RoomKind.Server, 0.6f), (RoomKind.Security, security ? 1 : 0), (RoomKind.Panic, target ? 0.5f : 0) },
            SiteStyle.Lab => new[] { (RoomKind.LabBay, 0.9f), (RoomKind.LabBay, 0.6f), (RoomKind.Server, 0.6f), (RoomKind.Break, 0.5f), (RoomKind.Manager, 0.5f), (RoomKind.Security, security ? 1 : 0), (RoomKind.Panic, target ? 0.4f : 0) },
            SiteStyle.Warehouse => new[] { (RoomKind.Dock, 0.6f), (RoomKind.Manager, 0.6f), (RoomKind.Break, 0.6f), (RoomKind.Armoury, _job.Threat >= 2 ? 0.5f : 0) },
            SiteStyle.Den => new[] { (RoomKind.Barracks, 0.8f), (RoomKind.Barracks, 0.4f), (RoomKind.Break, 0.5f), (RoomKind.Armoury, _job.Threat >= 2 ? 0.6f : 0), (RoomKind.Ritual, _job.Threat >= 2 ? 0.35f : 0) },
            _ => new[] { (RoomKind.Booth, 0.85f), (RoomKind.Manager, 0.8f), (RoomKind.Break, 0.6f), (RoomKind.Armoury, _job.Faction == Faction.Lotus ? 0.3f : 0), (RoomKind.Security, security ? 1 : 0) },
        };
        if (_job.Type == JobType.Defend || _job.Tutorial) return;
        // A nest has no barracks or break room: only, sometimes, the circle something was bound in.
        if (_job.Type == JobType.Bounty) wants = new[] { (RoomKind.Ritual, _job.Threat >= 2 ? 0.4f : 0) };
        foreach (var (kind, chance) in wants)
        {
            if (!_k.Chance(chance)) continue;
            // A room, not a corridor: not long and thin, not a junction of doorways.
            // (A loading dock is meant to be long, on the outside, with doors.)
            var free = P.Rooms.Where(r => r.Kind == RoomKind.Plain && !r.Vault && r.R.W >= 4 && r.R.H >= 4 && r.R.Area >= 16 && L.F[L.Idx(r.R.X, r.R.Y)] != Floor.Dance
                && (kind == RoomKind.Dock || Math.Max(r.R.W, r.R.H) <= 3 * Math.Min(r.R.W, r.R.H) && r.Links.Count <= 3)).ToList();
            if (kind == RoomKind.Dock) free = free.Where(r => r.Outer).ToList();
            // The booth looks out over the dance floor.
            if (kind == RoomKind.Booth) free = free.Where(r => r.Links.Any(l => l < P.Rooms.Count && L.F[L.Idx(P.Rooms[l].R.X, P.Rooms[l].R.Y)] == Floor.Dance)).ToList();
            if (kind == RoomKind.Panic) free = free.Where(r => r.Depth >= 1 && r.R.Area <= 48 && !r.Outer).ToList();
            if (free.Count == 0) continue;
            // The office and the panic room sit deep; the rest anywhere.
            Room pick = kind is RoomKind.Manager or RoomKind.Panic
                ? free.OrderByDescending(r => r.Depth + _k.Float() * 2).First()
                : free[_k.Int(free.Count)];
            pick.Kind = kind;
        }
    }

    /// <summary>Furnish each room with a purpose from a set piece (turned to fit), and build what it needs.</summary>
    private void StampKinds()
    {
        foreach (Room r in P.Rooms)
        {
            if (r.Kind == RoomKind.Plain) continue;
            var fits = SetPieces.Fitting(r.Kind, r.R.W, r.R.H, _k);
            if (fits.Count == 0) { r.Kind = RoomKind.Plain; continue; }
            string[] piece = fits[0];
            int pw = piece[0].Length, ph = piece.Length;
            int x0 = r.R.X + (r.R.W - pw) / 2 + (r.R.W - pw > 2 ? _k.Range(-1, 1) : 0);
            int y0 = r.R.Y + (r.R.H - ph) / 2 + (r.R.H - ph > 2 ? _k.Range(-1, 1) : 0);
            V2 centre = new(x0 + pw / 2f, y0 + ph / 2f);
            Mood mood = r.Kind switch { RoomKind.Break or RoomKind.Security => Mood.OffDuty, RoomKind.Barracks => Mood.Asleep, _ => Mood.None };
            for (int y = 0; y < ph; y++)
            for (int x = 0; x < pw; x++)
            {
                char c = piece[y][x];
                var p = new Pt(x0 + x, y0 + y);
                if (!r.R.Contains(p) || L[p] != Tile.Floor) continue;
                if (SetPieces.TileFor(c) is Tile t) { if (Free(p)) L.Set(p, t); continue; }
                switch (c)
                {
                    case '~': L.F[L.Idx(p.X, p.Y)] = Floor.Ritual; break;
                    case '*': _hot[r] = p.Centre; break;
                    case 'g' when mood != Mood.None && Free(p): _moods.Add((p.Centre, mood, M.Angle(centre - p.Centre))); _reserved.Add(p); break;
                    case 'x' when _job.Threat >= 2 && Free(p):
                        L.F[L.Idx(p.X, p.Y)] = Floor.Ritual;
                        P.Enemies.Add(new Spawn { Kind = EnemyKind.Spirit, Pos = p.Centre, Facing = _k.Float() * MathF.Tau, Mood = Mood.Bound });
                        _reserved.Add(p);
                        break;
                }
            }
            if (r.Kind == RoomKind.Panic)
            {
                // Armoured walls, and the door seals from inside (Run: the target runs here).
                Rect rim = r.R.Inset(-1);
                for (int y = rim.Y; y < rim.Bottom; y++)
                for (int x = rim.X; x < rim.Right; x++)
                    if (!r.R.Contains(new Pt(x, y)) && L[x, y] is Tile.Drywall or Tile.Brick or Tile.Glass) L.Set(x, y, Tile.Concrete);
                P.PanicRoom = r.R;
            }
            if (r.Kind == RoomKind.Dock && P.BackDoor.W == 0) LoadingDoors(r);
            if (r.Kind == RoomKind.Armoury)
            {
                // The armoury's doors are locked: hack them, or blow them.
                Rect rim = r.R.Inset(-1);
                for (int y = rim.Y; y < rim.Bottom; y++)
                for (int x = rim.X; x < rim.Right; x++)
                    if (!r.R.Contains(new Pt(x, y)) && L[x, y] == Tile.Door) L.Set(x, y, Tile.LockedDoor);
                // And it's stocked.
                V2 at = _hot.TryGetValue(r, out V2 h) ? h : r.Centre;
                if (_job.Type != JobType.Retrieve) P.Pickups.Add(new PickupSpawn { Kind = PickupKind.Grenade, Pos = at, Amount = 1, Id = _k.Pick(new[] { "frag", "flash", "emp" }) });
            }
        }
    }

    /// <summary>
    /// The loading dock's roller doors: three tiles of the building's outer wall
    /// opened onto the yard, and a second extraction just outside them.
    /// </summary>
    private void LoadingDoors(Room r)
    {
        Rect b = P.Building;
        var sides = new List<(Pt a, Pt step, Pt outward)>();
        if (r.R.Y - 1 == b.Y) sides.Add((new Pt(r.R.X, b.Y), new Pt(1, 0), new Pt(0, -1)));
        if (r.R.Bottom == b.Bottom - 1) sides.Add((new Pt(r.R.X, b.Bottom - 1), new Pt(1, 0), new Pt(0, 1)));
        if (r.R.X - 1 == b.X) sides.Add((new Pt(b.X, r.R.Y), new Pt(0, 1), new Pt(-1, 0)));
        if (r.R.Right == b.Right - 1) sides.Add((new Pt(b.Right - 1, r.R.Y), new Pt(0, 1), new Pt(1, 0)));
        foreach (var (a, step, outward) in sides.OrderBy(_ => _k.Next()))
        {
            int len = step.X != 0 ? r.R.W : r.R.H;
            if (len < 5) continue;
            int at = len / 2 - 1;
            Pt d0 = a + new Pt(step.X * at, step.Y * at);
            var doors = new[] { d0, d0 + step, d0 + new Pt(step.X * 2, step.Y * 2) };
            // Room to stand outside: floor two tiles out, inside the map's edge.
            Pt o1 = d0 + step + new Pt(outward.X * 2, outward.Y * 2);
            if (!L.In(o1) || L[o1] != Tile.Floor || o1.X <= 1 || o1.Y <= 1 || o1.X >= P.MainW - 2 || o1.Y >= L.H - 2) continue;
            foreach (Pt d in doors) L.Set(d, Tile.Door);
            Pt c = d0 + step + new Pt(outward.X * 2, outward.Y * 2);
            P.BackDoor = new Rect(c.X - 1, c.Y - 1, 3, 3);
            for (int y = P.BackDoor.Y; y < P.BackDoor.Bottom; y++)
            for (int x = P.BackDoor.X; x < P.BackDoor.Right; x++)
                if (L.In(x, y) && L[x, y] is not Tile.Floor and not Tile.Door && x > 0 && y > 0 && x < P.MainW - 1 && y < L.H - 1) L.Set(x, y, Tile.Floor);
            for (int y = P.BackDoor.Y - 1; y <= P.BackDoor.Bottom; y++)
            for (int x = P.BackDoor.X - 1; x <= P.BackDoor.Right; x++) _reserved.Add(new Pt(x, y));
            return;
        }
    }

    /// <summary>The deepest room of one of these kinds, or null.</summary>
    private Room ByKind(params RoomKind[] kinds) => P.Rooms.Where(r => kinds.Contains(r.Kind)).OrderByDescending(r => r.Depth).FirstOrDefault();

    /// <summary>The room's own objective spot (from its set piece), else anywhere free in it.</summary>
    private V2 SpotIn(Room r) => _hot.TryGetValue(r, out V2 h) && L[Pt.Of(h)] == Tile.Floor && !_reserved.Contains(Pt.Of(h)) ? h : FreeSpot(r);

    private void Objectives()
    {
        switch (_job.Type)
        {
            case JobType.Eliminate:
            case JobType.Kidnap:
            {
                // Sometimes the target is working late in the manager's office (in a club: holding court in the booth).
                Room r = Deepest(x => !x.Vault);
                if (_k.Chance(0.7f) && ByKind(RoomKind.Booth) is Room booth) r = booth;
                else if (_k.Chance(0.45f) && ByKind(RoomKind.Manager) is Room office) r = office;
                V2 at = SpotIn(r);
                P.Enemies.Add(new Spawn { Kind = EnemyKind.Vip, Pos = at, Facing = _r.Float() * MathF.Tau });
                Reserve(at);
                for (int k = 0; k < 1 + _job.Threat / 2; k++)
                {
                    V2 g = FreeSpot(r);
                    Reserve(g);
                    P.Enemies.Add(new Spawn { Kind = BodyguardKind(), Pos = g, Bodyguard = true, Facing = _r.Float() * MathF.Tau });
                }
                break;
            }
            case JobType.Retrieve:
            {
                Room r = _vault ?? Deepest();
                if (_vault == null && _k.Chance(0.5f) && ByKind(RoomKind.Armoury, RoomKind.Manager, RoomKind.Dock) is Room fit) r = fit;
                V2 at = SpotIn(r);
                P.Pickups.Add(new PickupSpawn { Kind = PickupKind.Objective, Pos = at, Name = _job.Item });
                Reserve(at);
                break;
            }
            case JobType.Destroy:
            {
                Tile thing = _job.Style == SiteStyle.Lab ? Tile.Vat : Tile.Rack;
                var rooms = P.Rooms.Where(r => !r.Outer || P.Rooms.Count < 3).OrderByDescending(r => r.Depth + _r.Float()).ToList();
                if (rooms.Count == 0) rooms = P.Rooms.ToList();
                int placed = 0;
                for (int k = 0; placed < _job.Count && k < _job.Count * 6; k++)
                {
                    Room r = rooms[k % Math.Max(1, Math.Min(rooms.Count, 3))];
                    Pt p = WallSpot(r, _reserved);
                    if (p.X < 0) { r = rooms[_r.Int(rooms.Count)]; p = WallSpot(r, _reserved); }
                    if (p.X < 0) continue;
                    L.Set(p, thing);
                    P.Targets.Add(p);
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) _reserved.Add(p + new Pt(dx, dy));
                    placed++;
                }
                break;
            }
            case JobType.Datasteal:
            {
                Room r = _vault ?? Deepest();
                if (_vault == null && _k.Chance(0.6f) && ByKind(RoomKind.Server) is Room servers) r = servers;
                Pt p = WallSpot(r);
                // No good wall spot: any floor not in front of a door (never block the only way in).
                if (p.X < 0)
                    for (int y = r.R.Y; y < r.R.Bottom && p.X < 0; y++)
                    for (int x = r.R.X; x < r.R.Right; x++)
                        if (L[x, y] == Tile.Floor && !NearDoor(new Pt(x, y), 1)) { p = new Pt(x, y); break; }
                if (p.X < 0) p = Pt.Of(FreeSpot(r));
                L.Set(p, Tile.Mainframe);
                P.Mainframe = p;
                for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) _reserved.Add(p + new Pt(dx, dy));
                break;
            }
            case JobType.Rescue:
            {
                Room r = _vault ?? Deepest();
                if (_vault == null && _k.Chance(0.35f) && ByKind(RoomKind.Barracks, RoomKind.Armoury) is Room cell) r = cell;
                V2 at = SpotIn(r);
                P.Hostage = at;
                Reserve(at);
                break;
            }
            case JobType.Investigate:
            {
                // Three pieces of evidence in three of the deeper rooms (the vault, if any, keeps one).
                var deep = P.Rooms.OrderByDescending(r => r.Depth + _r.Float() * 2).ToList();
                // Evidence is where people keep it: the manager's desk, the security office, the break-room table.
                foreach (Room k in P.Rooms.Where(x => x.Kind is RoomKind.Manager or RoomKind.Security or RoomKind.Break).ToList())
                    if (_k.Chance(0.6f)) { deep.Remove(k); deep.Insert(0, k); }
                if (_vault != null) { deep.Remove(_vault); deep.Insert(0, _vault); }
                string[] names = { "a ledger", "a recording", "a file of photographs", "a burner commlink", "shipping manifests" };
                for (int k = 0; k < 3; k++)
                {
                    Room r = deep[Math.Min(k, deep.Count - 1)];
                    V2 at = FreeSpot(r);
                    P.Pickups.Add(new PickupSpawn { Kind = PickupKind.Clue, Pos = at, Name = names[(k + (int)(_job.Seed % 5)) % names.Length] });
                    Reserve(at);
                }
                break;
            }
            case JobType.Escort:
            {
                // The client starts at your side; the meeting is deep inside.
                Room r = Deepest(x => !x.Vault);
                V2 at = FreeSpot(r);
                P.PlantAt = at;
                Reserve(at);
                V2 start = P.Entry.Centre + new V2(0.7f, 0);
                if (L.Solid((int)start.X, (int)start.Y)) start = P.Entry.Centre;
                P.Hostage = start;
                break;
            }
            case JobType.Plant:
            {
                Room r = _vault ?? Deepest();
                if (_vault == null && _k.Chance(0.5f) && ByKind(RoomKind.Server, RoomKind.Manager, RoomKind.Security) is Room spot) r = spot;
                V2 at = SpotIn(r);
                P.PlantAt = at;
                Reserve(at);
                break;
            }
        }
    }

    /// <summary>A security room with a console, an armoury with lockers, a safe or two.</summary>
    private void SpecialRooms()
    {
        var rooms = P.Rooms.Where(r => !r.Outer && r.R.Area >= 12).OrderBy(_ => _r.Next()).ToList();
        if (rooms.Count == 0) rooms = P.Rooms.Where(r => r.R.Area >= 9).ToList();
        int used = 0;
        Room Next() => used < rooms.Count ? rooms[used++] : null;
        void Put(Room r, Tile t)
        {
            if (r == null) return;
            Pt p = WallSpot(r, _reserved);
            if (p.X < 0) return;
            L.Set(p, t);
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) _reserved.Add(p + new Pt(dx, dy));
        }
        bool security = P.Rooms.Any(x => x.Kind == RoomKind.Security), armoury = P.Rooms.Any(x => x.Kind == RoomKind.Armoury);
        if ((_job.Faction == Faction.Halcyon || _job.Threat >= 3) && !security) Put(Next(), Tile.Console);
        if (_job.Threat >= 2 && _r.Chance(0.6f) && !armoury)
        {
            Room arm = Next();
            Put(arm, Tile.Locker);
            Put(arm, Tile.Locker);
        }
        if (_vault != null) Put(_vault, Tile.Safe);
        if (_r.Chance(0.45f)) Put(Next(), Tile.Safe);
        StyleRules();
    }

    /// <summary>
    /// What each style brings of its own (Run applies the rules): an office's
    /// badge-reader doors and its night cleaners; a lab's clean-room suit hung
    /// in a locker room.
    /// </summary>
    private void StyleRules()
    {
        if (_job.Type is JobType.Defend or JobType.Hub || _job.Tutorial) return;
        if (_job.Style == SiteStyle.Office)
        {
            // One or two doors deeper in open only to a keycard (or a hack, or a charge).
            var doors = new List<Pt>();
            foreach (Room r in P.Rooms.Where(x => x.Depth >= 2 && !x.Vault && !x.Outer).OrderByDescending(x => x.Depth))
            {
                Rect rim = r.R.Inset(-1);
                for (int y = rim.Y; y < rim.Bottom; y++)
                for (int x = rim.X; x < rim.Right; x++)
                    if (!r.R.Contains(new Pt(x, y)) && L[x, y] == Tile.Door && !doors.Contains(new Pt(x, y))) { doors.Add(new Pt(x, y)); goto next; }
                next:
                if (doors.Count >= 1 + _job.Threat / 3) break;
            }
            foreach (Pt d in doors) { L.Set(d, Tile.LockedDoor); L.Keycards.Add(L.Idx(d.X, d.Y)); }
            // Night cleaners: they don't fight, but a gun in their sight is a phone call.
            for (int k = 0; k < Tables.Cleaners && P.Rooms.Count > 0; k++)
            {
                Room r = P.Rooms[_k.Int(P.Rooms.Count)];
                P.Npcs.Add((FreeSpot(r), "cleaner"));
            }
        }
        if (_job.Style == SiteStyle.Lab && P.CleanRoom.W > 0)
        {
            var rooms = P.Rooms.Where(x => !P.CleanRoom.Contains(Pt.Of(x.Centre)) && !x.Vault).ToList();
            if (rooms.Count > 0) P.Pickups.Add(new PickupSpawn { Kind = PickupKind.Suit, Pos = FreeSpot(rooms[_k.Int(rooms.Count)]), Name = "a clean-room suit" });
        }
    }

    private void Reserve(V2 at)
    {
        Pt p = Pt.Of(at);
        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) _reserved.Add(p + new Pt(dx, dy));
    }

    private EnemyKind BodyguardKind() => _job.Faction switch
    {
        Faction.Vipers => EnemyKind.Ganger,
        Faction.Lotus => _r.Chance(0.5f) ? EnemyKind.Blade : EnemyKind.Guard,
        _ => _job.Threat >= 4 && _r.Chance(0.4f) ? EnemyKind.Heavy : EnemyKind.Guard,
    };

    // ---- furniture -----------------------------------------------------------------

    private bool Free(Pt p) =>
        L[p] == Tile.Floor && !_reserved.Contains(p) && !NearDoor(p, 1) && P.Extraction.Inset(-2).Contains(p) == false
        && p.Chebyshev(P.Entry) > 3 && L.In(p);

    private void Furnish(Floor yard)
    {
        SiteStyle st = _job.Style;
        foreach (Room r in P.Rooms)
        {
            if (r.R.W < 3 || r.R.H < 3 || r.Kind != RoomKind.Plain) continue;
            switch (st)
            {
                case SiteStyle.Office:
                    if (r.R.Area >= 40)
                    {
                        // Open plan: cubicle rows with aisles.
                        for (int y = r.R.Y + 1; y < r.R.Bottom - 1; y += 3)
                        for (int x = r.R.X + 1; x < r.R.Right - 1; x++)
                            if ((x - r.R.X) % 5 != 0 && Free(new Pt(x, y))) L.Set(x, y, Tile.Desk);
                    }
                    else if (_r.Chance(0.35f))
                    {
                        // A meeting room: one long table.
                        int y = r.R.Y + r.R.H / 2;
                        for (int x = r.R.X + 1; x < r.R.Right - 1; x++) if (Free(new Pt(x, y))) L.Set(x, y, Tile.Desk);
                    }
                    else Clutter(r.R, Math.Max(1, r.R.Area / 12), Tile.Desk);
                    break;
                case SiteStyle.Lab:
                    for (int k = 0; k < r.R.Area / (r.R.Area >= 80 ? 34 : 14); k++)
                    {
                        Pt p = new(_r.Range(r.R.X + 1, r.R.Right - 2), _r.Range(r.R.Y + 1, r.R.Bottom - 2));
                        if (Free(p)) L.Set(p, Tile.Desk);
                        Pt q = p + new Pt(1, 0);
                        if (Free(q)) L.Set(q, Tile.Desk);
                    }
                    break;
                case SiteStyle.Club:
                    if (L.F[L.Idx(r.R.X, r.R.Y)] == Floor.Dance)
                    {
                        // A bar along one wall.
                        int y = r.R.Y + 1;
                        for (int x = r.R.X + 2; x < r.R.X + 2 + Math.Min(8, r.R.W - 4); x++)
                            if (Free(new Pt(x, y))) L.Set(x, y, Tile.Desk);
                        for (int k = 0; k < r.R.Area / 40; k++)
                        {
                            Pt p = new(_r.Range(r.R.X + 2, r.R.Right - 3), _r.Range(r.R.Y + 3, r.R.Bottom - 2));
                            if (Free(p)) L.Set(p, Tile.Crate);
                        }
                    }
                    else Clutter(r.R, r.R.Area / 12, Tile.Crate);
                    break;
                case SiteStyle.Warehouse:
                    if (r.R.Area >= 80)
                    {
                        // Shelving: long rows of crates with aisles and cross-gaps.
                        bool rowsAcross = r.R.W >= r.R.H;
                        int span = rowsAcross ? r.R.W : r.R.H, rows = rowsAcross ? r.R.H : r.R.W;
                        for (int q = 2; q < rows - 2; q += 3)
                        for (int k = 2; k < span - 2; k++)
                        {
                            if (k % 7 == 0) continue;
                            Pt p = rowsAcross ? new Pt(r.R.X + k, r.R.Y + q) : new Pt(r.R.X + q, r.R.Y + k);
                            if (Free(p)) L.Set(p, _r.Chance(0.06f) ? Tile.Barrel : Tile.Crate);
                        }
                    }
                    else
                    {
                        for (int k = 0; k < r.R.Area / 30 + 1; k++) CrateStack(r.R);
                        Clutter(r.R, r.R.Area / 40, Tile.Barrel);
                    }
                    break;
                default:
                    Clutter(r.R, r.R.Area / 10, Tile.Crate);
                    Clutter(r.R, r.R.Area / 60, Tile.Barrel);
                    break;
            }
        }
        // The yard: cars, crates, barrels, fences.
        var yardArea = new Rect(1, 1, L.W - 2, L.H - 2);
        int count = L.W * L.H / 120;
        for (int k = 0; k < count; k++)
        {
            Pt p = new(_r.Range(2, L.W - 3), _r.Range(2, L.H - 3));
            if (P.Building.Inset(-2).Contains(p) || p.X >= P.MainW - 1 || !Free(p)) continue;
            float roll = _r.Float();
            if (roll < 0.35f)
            {
                bool horiz = _r.Chance(0.5f);
                Pt q = p + (horiz ? new Pt(1, 0) : new Pt(0, 1));
                if (Free(q) && !P.Building.Inset(-2).Contains(q)) { L.Set(p, Tile.Wreck); L.Set(q, Tile.Wreck); }
            }
            else if (roll < 0.7f) { L.Set(p, Tile.Crate); if (_r.Chance(0.5f) && Free(p + new Pt(1, 0))) L.Set(p + new Pt(1, 0), Tile.Crate); }
            else if (roll < 0.85f) L.Set(p, Tile.Barrel);
            else if (_job.Style is SiteStyle.Den or SiteStyle.Warehouse)
            {
                bool horiz = _r.Chance(0.5f);
                for (int q = 0; q < _r.Range(3, 6); q++)
                {
                    Pt f = p + (horiz ? new Pt(q, 0) : new Pt(0, q));
                    if (Free(f) && !P.Building.Inset(-1).Contains(f)) L.Set(f, Tile.Fence);
                }
            }
        }
    }

    private void Clutter(Rect r, int n, Tile t)
    {
        for (int k = 0; k < n; k++)
        {
            Pt p = new(_r.Range(r.X, r.Right - 1), _r.Range(r.Y, r.Bottom - 1));
            if (Free(p)) L.Set(p, t);
        }
    }

    private void CrateStack(Rect r)
    {
        int w = _r.Range(1, 3), h = _r.Range(1, 2);
        Pt o = new(_r.Range(r.X + 1, Math.Max(r.X + 1, r.Right - w - 1)), _r.Range(r.Y + 1, Math.Max(r.Y + 1, r.Bottom - h - 1)));
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            Pt p = o + new Pt(x, y);
            if (r.Contains(p) && Free(p)) L.Set(p, _r.Chance(0.12f) ? Tile.Barrel : Tile.Crate);
        }
    }

    /// <summary>
    /// Flood fill from the entry through doors (locked ones count -- they can
    /// be hacked or blown). Furniture that seals off any floor is removed.
    /// </summary>
    private void EnsureReachable()
    {
        for (int iter = 0; iter < 40; iter++)
        {
            bool[] seen = Reach();
            var fix = new List<Pt>();
            for (int y = 1; y < L.H - 1; y++)
            for (int x = 1; x < L.W - 1; x++)
                if (L[x, y] == Tile.Floor && !seen[L.Idx(x, y)]) fix.Add(new Pt(x, y));
            if (fix.Count == 0) return;
            // Sealed rooms (a split that got no door): cut one through a wall between known and unknown floor.
            if (iter >= 2)
            {
                foreach (Pt p in fix)
                {
                    bool cut = false;
                    foreach (Pt d in Pt.Dirs4)
                    for (int thick = 1; thick <= 3 && !cut; thick++)
                    {
                        Pt beyond = p + new Pt(d.X * (thick + 1), d.Y * (thick + 1));
                        if (!L.In(beyond) || !seen[L.Idx(beyond.X, beyond.Y)] || !Passable(L[beyond])) continue;
                        bool walls = true;
                        for (int k = 1; k <= thick; k++) if (L[p + new Pt(d.X * k, d.Y * k)] is not (Tile.Drywall or Tile.Brick or Tile.Concrete or Tile.Glass)) walls = false;
                        if (!walls) continue;
                        for (int k = 1; k <= thick; k++) L.Set(p + new Pt(d.X * k, d.Y * k), k == 1 ? Tile.Door : Tile.DoorOpen);
                        cut = true;
                    }
                    if (cut) break;
                }
                continue;
            }
            foreach (Pt p in fix)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    Pt q = p + new Pt(dx, dy);
                    if (L[q] is Tile.Crate or Tile.Desk or Tile.Barrel or Tile.Fence or Tile.Wreck) L.Set(q, Tile.Floor);
                }
        }
    }

    public static bool Passable(Tile t) => t is Tile.Floor or Tile.Rubble or Tile.Door or Tile.DoorOpen or Tile.LockedDoor or Tile.Stairs;

    private static IEnumerable<Pt> Next(Level l, Pt c)
    {
        foreach (Pt d in Pt.Dirs4) yield return c + d;
        if (l.OtherEnd(c, out Pt e)) yield return e;
    }

    private bool[] Reach()
    {
        var seen = new bool[L.W * L.H];
        var q = new Queue<Pt>();
        q.Enqueue(P.Entry);
        seen[L.Idx(P.Entry.X, P.Entry.Y)] = true;
        while (q.Count > 0)
        {
            Pt p = q.Dequeue();
            foreach (Pt n in Next(L, p))
            {
                if (!L.In(n) || seen[L.Idx(n.X, n.Y)] || !Passable(L[n])) continue;
                seen[L.Idx(n.X, n.Y)] = true;
                q.Enqueue(n);
            }
        }
        return seen;
    }

    /// <summary>For the self-check: is p (or, for a solid objective, a tile next to it) reachable from the entry?</summary>
    public static bool Reachable(LevelPlan plan, Pt p)
    {
        Level l = plan.L;
        var seen = new bool[l.W * l.H];
        var q = new Queue<Pt>();
        q.Enqueue(plan.Entry);
        seen[l.Idx(plan.Entry.X, plan.Entry.Y)] = true;
        while (q.Count > 0)
        {
            Pt c = q.Dequeue();
            if (c == p || c.Chebyshev(p) == 1 && !Passable(l[p])) return true;
            foreach (Pt n in Next(l, c))
            {
                if (!l.In(n) || seen[l.Idx(n.X, n.Y)] || !Passable(l[n])) continue;
                seen[l.Idx(n.X, n.Y)] = true;
                q.Enqueue(n);
            }
        }
        return false;
    }

    // ---- lights, enemies, loot -----------------------------------------------------

    private void Lights(Rect b)
    {
        uint col = _job.Style switch
        {
            SiteStyle.Office => 0xbfe8ffff, SiteStyle.Lab => 0x80fff0ff, SiteStyle.Den => 0xffa040ff,
            SiteStyle.Club => 0xff40c0ff, _ => 0xffe0a0ff,
        };
        if (_job.Has(RunMod.Blackout))
        {
            // Emergency lighting only.
            foreach (Room r in P.Rooms.Where(_ => _r.Chance(0.3f))) L.Lights.Add((r.Centre, 0xff2020ff, 2.5f));
            return;
        }
        // At night most rooms have their lights off: which, by a hash of the room (not the site's
        // random stream, so the rest of the site is the same as before there was light).
        float on = _job.Style switch { SiteStyle.Office => 0.45f, SiteStyle.Lab => 0.6f, SiteStyle.Warehouse => 0.6f, SiteStyle.Den => 0.7f, _ => 0.8f };
        foreach (Room r in P.Rooms)
        {
            uint c = r.Vault ? 0xff3030ff : _job.Style == SiteStyle.Club && L.F[L.Idx(r.R.X, r.R.Y)] == Floor.Dance ? 0xff40c0ff : col;
            if (c == col && new Rng(_job.Seed * 0x9E3779B97F4A7C15UL + (ulong)(r.R.X * 131 + r.R.Y * 7919 + 1)).Float() >= on) continue;
            // A big room (a hall, an open-plan floor) hangs a row of lamps: pools of light, dark aisles between.
            int nx = Math.Max(1, (int)MathF.Round(r.R.W / (float)Tables.LampEvery)), ny = Math.Max(1, (int)MathF.Round(r.R.H / (float)Tables.LampEvery));
            if (nx * ny > 1 && c == col)
            {
                for (int gy = 0; gy < ny; gy++)
                for (int gx = 0; gx < nx; gx++)
                    L.Lights.Add((new V2(r.R.X + r.R.W * (gx + 0.5f) / nx, r.R.Y + r.R.H * (gy + 0.5f) / ny), c, 4.5f));
                continue;
            }
            L.Lights.Add((r.Centre, c, MathF.Min(6, MathF.Max(r.R.W, r.R.H) * 0.6f)));
            if (_job.Style == SiteStyle.Club && c == 0xff40c0ff)
            {
                L.Lights.Add((r.Centre + new V2(-r.R.W / 4f, 0), 0x40e0ffff, 5));
                L.Lights.Add((r.Centre + new V2(r.R.W / 4f, 0), 0xa040ffff, 5));
            }
        }
        // Yard lamps.
        for (int k = 0; k < 6; k++)
        {
            V2 p = new(_r.Range(3f, P.MainW - 3f), _r.Range(3f, L.H - 3f));
            if (!b.Inset(-1).Contains(p)) L.Lights.Add((p, _r.Chance(0.5f) ? 0xffb050ffu : 0x40c0ffffu, 4.5f));
        }
    }

    /// <summary>A Bounty's nest: what lives in the Barrens' basements.</summary>
    public static readonly (EnemyKind kind, float weight)[] Critters =
    {
        (EnemyKind.Hound, 5f), (EnemyKind.Bruiser, 4f), (EnemyKind.Spirit, 0.8f), (EnemyKind.Barghest, 1.2f),
    };

    /// <summary>A hive: workers to fill it (the queen and her sacs are placed by hand).</summary>
    public static readonly (EnemyKind kind, float weight)[] Hive = { (EnemyKind.HiveWorker, 1f) };

    private void Defenders()
    {
        float budget = _job.Threat == 1 ? 5.5f : 4 + _job.Threat * 3.5f;
        if (_job.Type == JobType.Clear) budget *= 1.4f;
        // Defending: the place is yours, and they come from outside (Run.StepDefend).
        bool defend = _job.Type == JobType.Defend;
        if (defend) budget = 0;
        // A tower: a full garrison on every floor, heavier the higher you go (TowerGuards).
        if (P.Tower) budget *= 1.2f;
        if (_job.Has(RunMod.Skeleton)) budget *= 0.6f;
        float patrol = _job.Has(RunMod.ShiftChange) ? 0.9f : 0.45f;
        bool bounty = _job.Type == JobType.Bounty;
        var roster = (_job.Hive ? Hive : bounty ? Critters : Tables.Roster(_job.Faction)).Where(e => (_job.Threat >= 2 || e.kind is not (EnemyKind.Heavy or EnemyKind.Sniper or EnemyKind.Mage))
            && (_job.Threat >= 3 || e.kind is not (EnemyKind.Shield or EnemyKind.Rigger or EnemyKind.Spirit))
            // The high end only on hard jobs (a barghest leads a bounty pack from threat 3).
            && _job.Threat >= (bounty && e.kind == EnemyKind.Barghest ? 3 : Tables.FirstThreat(e.kind))).ToArray();
        if (_job.Hive) PlaceHive(ref budget);
        if (_job.Threat >= 5 && P.Rooms.Count > 0 && !defend && _job.Type != JobType.Bounty)
        {
            // Every threat-5 site has someone in charge.
            Room br = Deepest(x => !x.Vault) ?? P.Rooms[0];
            V2 bp = FreeSpot(br);
            P.Enemies.Add(new Spawn { Kind = EnemyKind.Boss, Pos = bp, Facing = _r.Float() * MathF.Tau });
            budget -= 4;
            // Prestige sites keep a second one, somewhere else.
            if (_job.Threat >= 6 && P.Rooms.Count > 2)
            {
                Room b2 = P.Rooms.Where(x => x != br && !x.Vault).OrderByDescending(x => x.Depth + _r.Float()).Skip(1).FirstOrDefault() ?? br;
                P.Enemies.Add(new Spawn { Kind = EnemyKind.Boss, Pos = FreeSpot(b2), Facing = _r.Float() * MathF.Tau });
            }
        }
        var rooms = P.Rooms.Where(r => r.R.Area >= 6).ToList();
        if (rooms.Count == 0) rooms = P.Rooms;

        // Fixed defences for corporate sites.
        if (_job.Faction == Faction.Halcyon && !defend && _job.Type != JobType.Bounty)
        {
            // A security spider at the deepest console (threat 5 and up): find him, or every hack is slow.
            // A security spider at the deepest console (threat 5, or 4 where there is data to guard): find him, or
            // every hack is slow and he is waiting in the host. On the hardest sites he has a partner.
            bool data = _job.Type == JobType.Datasteal || _job.Style is SiteStyle.Office or SiteStyle.Lab;
            int spiders = _job.Threat >= Tables.FirstThreat(EnemyKind.Spider) ? (data ? 2 : 1) : _job.Threat >= 4 && data ? 1 : 0;
            for (int k = 0; k < spiders && PlaceSpider(); k++) budget -= Tables.Enemy(EnemyKind.Spider).Budget;
            int cams = 1 + _job.Threat;
            for (int k = 0; k < cams && rooms.Count > 0; k++)
            {
                Room r = rooms[_r.Int(rooms.Count)];
                Pt corner = new(_r.Chance(0.5f) ? r.R.X : r.R.Right - 1, _r.Chance(0.5f) ? r.R.Y : r.R.Bottom - 1);
                if (L[corner] != Tile.Floor) continue;
                V2 at = corner.Centre;
                P.Enemies.Add(new Spawn { Kind = EnemyKind.Camera, Pos = at, Facing = M.Angle(r.Centre - at) });
                budget -= 0.5f;
            }
            int turrets = _job.Threat >= 2 ? _r.Range(1, 1 + _job.Threat / 2) : 0;
            for (int k = 0; k < turrets && rooms.Count > 0; k++)
            {
                Room r = rooms.Where(x => x.Depth >= 1).OrderBy(_ => _r.Next()).FirstOrDefault() ?? rooms[0];
                Pt p = WallSpot(r, _reserved);
                if (p.X < 0) continue;
                P.Enemies.Add(new Spawn { Kind = EnemyKind.Turret, Pos = p.Centre, Facing = M.Angle(r.Centre - p.Centre) });
                _reserved.Add(p);
                budget -= 2.5f;
            }
        }

        float total = roster.Sum(x => x.weight);
        int guard = 0;
        while (budget > 0.9f && guard++ < 80)
        {
            float roll = _r.Float() * total;
            EnemyKind kind = roster[0].kind;
            foreach (var e in roster) { roll -= e.weight; if (roll <= 0) { kind = e.kind; break; } }
            EnemyDef def = Tables.Enemy(kind);
            if (def.Budget > budget + 0.5f) continue;
            budget -= def.Budget;
            var s = new Spawn { Kind = kind, Facing = _r.Float() * MathF.Tau };
            // Someone's on their break, or asleep: a person, not a drone or a dog.
            if (_moods.Count > 0 && kind is EnemyKind.Ganger or EnemyKind.Guard or EnemyKind.Blade or EnemyKind.Heavy or EnemyKind.Bruiser or EnemyKind.Firebug or EnemyKind.Shield or EnemyKind.Sniper or EnemyKind.Mage or EnemyKind.Rigger)
            {
                var (mp, mood, mf) = _moods[0];
                _moods.RemoveAt(0);
                s.Pos = mp;
                // Nothing in a nest is on its break: it's asleep.
                s.Mood = bounty && mood == Mood.OffDuty ? Mood.Asleep : mood;
                s.Facing = mf;
                P.Enemies.Add(s);
                continue;
            }
            bool inYard = _r.Chance(0.25f) || rooms.Count == 0;
            if (inYard)
            {
                s.Pos = YardSpot();
                if (_r.Chance(patrol + 0.25f)) s.Patrol.Add(YardSpot());
            }
            else
            {
                // Mostly deep inside, but spread through the building so no wing is empty.
                Room r = _r.Chance(0.55f) ? rooms[_r.Int(rooms.Count)] : rooms.OrderByDescending(x => x.Depth + _r.Float() * 3).First();
                s.Pos = FreeSpot(r);
                if (_r.Chance(patrol) && r.Links.Count > 0)
                    s.Patrol.Add(FreeSpot(P.Rooms[r.Links[_r.Int(r.Links.Count)]]));
            }
            if (V2.Distance(s.Pos, P.Entry.Centre) < 12) s.Pos = FreeSpot(rooms.Count > 0 ? rooms[_r.Int(rooms.Count)] : P.Rooms[0]);
            if (s.Patrol.Count > 0) s.Patrol.Insert(0, s.Pos);
            P.Enemies.Add(s);
        }

        if (P.Tower) TowerGuards(roster);

        // Dens drink: some of the gangers are slow, loud and bad shots tonight.
        if (_job.Style == SiteStyle.Den && _job.Type != JobType.Bounty)
            foreach (Spawn s in P.Enemies.Where(e => e.Kind is EnemyKind.Ganger or EnemyKind.Bruiser)) s.Drunk = _k.Chance(Tables.DrunkShare);
        // Offices: one guard carries the keycard for the badge-reader doors.
        if (P.L.Keycards.Count > 0)
        {
            var holders = P.Enemies.Where(e => e.Kind is EnemyKind.Guard or EnemyKind.Heavy && !e.Bodyguard).ToList();
            if (holders.Count > 0) holders[_k.Int(holders.Count)].Badge = true;
            else P.Pickups.Add(new PickupSpawn { Kind = PickupKind.Badge, Pos = FreeSpot(P.Rooms.OrderBy(x => x.Depth).First()) });
        }

        // Reinforcements come in at the edges, away from the entry.
        for (int k = 0; k < 4; k++)
        {
            for (int tries = 0; tries < 30; tries++)
            {
                V2 p = _r.Int(4) switch
                {
                    0 => new V2(2.5f, _r.Range(3, L.H - 4) + 0.5f), 1 => new V2(P.MainW - 2.5f, _r.Range(3, L.H - 4) + 0.5f),
                    2 => new V2(_r.Range(3, P.MainW - 4) + 0.5f, 2.5f), _ => new V2(_r.Range(3, P.MainW - 4) + 0.5f, L.H - 2.5f),
                };
                if (V2.Distance(p, P.Entry.Centre) < 16 || L[Pt.Of(p)] != Tile.Floor) continue;
                P.Reinforce.Add(p);
                break;
            }
        }
        if (P.Reinforce.Count == 0) P.Reinforce.Add(YardSpot());
    }

    /// <summary>
    /// The ways in besides the front gate: a fence cut on another side, the
    /// loading dock's doors, a sewer hatch that comes up inside, the roof of a
    /// tower. Each starts clear of every guard's post; the way out stays where it is.
    /// </summary>
    private void Entries()
    {
        P.Entries.Add(new EntryPoint("front", "Front gate", "the way they expect you to come", P.Entry));
        if (_job.Type is JobType.Defend or JobType.Hub or JobType.Hack || _job.Tutorial || _job.Driving) return;
        var posts = P.Enemies.Where(e => e.Kind is not (EnemyKind.Camera or EnemyKind.Turret)).Select(e => e.Pos).ToList();
        bool Clear(Pt p, float d) => posts.All(q => V2.Distance(q, p.Centre) >= d);
        bool[] reach = Reach();
        bool Reached(Pt p) => L.In(p) && reach[L.Idx(p.X, p.Y)];
        // A fence cut on another side of the yard: a longer, more open walk.
        for (int tries = 0; tries < 40; tries++)
        {
            int side = _k.Int(4);
            Pt p = side switch
            {
                0 => new Pt(2, _k.Range(3, L.H - 4)), 1 => new Pt(P.MainW - 3, _k.Range(3, L.H - 4)),
                2 => new Pt(_k.Range(3, P.MainW - 4), 2), _ => new Pt(_k.Range(3, P.MainW - 4), L.H - 3),
            };
            if (L[p] != Tile.Floor || p.Chebyshev(P.Entry) < 18 || !Clear(p, 10) || !Reached(p)) continue;
            P.Entries.Add(new EntryPoint("fence", "Cut the fence", "a long way round, in the open yard", p));
            break;
        }
        // The loading dock's roller doors, straight into the hall.
        if (P.BackDoor.W > 0)
        {
            Pt d = new(P.BackDoor.X + 1, P.BackDoor.Y + 1);
            if (Reached(d) && Clear(d, 6)) P.Entries.Add(new EntryPoint("dock", "Loading dock", "straight into the hall -- whoever is working there", d));
        }
        // A sewer hatch in a quiet room: inside from the start, nowhere to back off to.
        if (_job.Style is SiteStyle.Den or SiteStyle.Warehouse or SiteStyle.Lab && _job.Threat >= 2 && !P.Tower)
        {
            var rooms = P.Rooms.Where(r => r.Kind == RoomKind.Plain && !r.Vault && r.Depth >= 1 && r.R.Area >= 9).OrderBy(_ => _k.Next()).ToList();
            foreach (Room r in rooms)
            {
                Pt p = Pt.Of(FreeSpot(r));
                if (L[p] != Tile.Floor || !Clear(p, 7) || !Reached(p)) continue;
                P.Entries.Add(new EntryPoint("sewer", "Sewer hatch", "you come up inside -- found, you're cornered", p));
                break;
            }
        }
        // A tower's roof, if you can get a helicopter over it.
        if (P.Tower && P.LiftSpots.Count > 0)
        {
            Pt top = Pt.Of(P.LiftSpots[^1]);
            P.Entries.Add(new EntryPoint("roof", "The roof, by the Kite", "the top floor first, its guards between you and the stairs", top));
        }
    }

    /// <summary>The spider sits at a console, as far from the way in as there is one.</summary>
    private bool PlaceSpider()
    {
        Pt best = new(-1, -1);
        float bd = -1;
        for (int y = 1; y < L.H - 1; y++)
        for (int x = 1; x < L.W - 1; x++)
        {
            if (L[x, y] != Tile.Console) continue;
            if (P.Enemies.Any(e => e.Kind == EnemyKind.Spider && Pt.Of(e.Pos).Chebyshev(new Pt(x, y)) <= 1)) continue;
            float d = V2.Distance(new Pt(x, y).Centre, P.Entry.Centre) + (P.Tower ? P.FloorAt(x) * 100 : 0);
            if (d > bd) { bd = d; best = new Pt(x, y); }
        }
        if (best.X < 0) return false;
        foreach (Pt n in Pt.Dirs4.Select(d => new Pt(best.X + d.X, best.Y + d.Y)))
        {
            if (L[n] != Tile.Floor) continue;
            P.Enemies.Add(new Spawn { Kind = EnemyKind.Spider, Pos = n.Centre, Facing = M.Angle(best.Centre - n.Centre) });
            return true;
        }
        return false;
    }

    /// <summary>The hive: the queen in the deepest room, egg sacs through the rooms around her.</summary>
    private void PlaceHive(ref float budget)
    {
        if (P.Rooms.Count == 0) return;
        Room nest = Deepest(x => !x.Vault) ?? P.Rooms[0];
        P.Enemies.Add(new Spawn { Kind = EnemyKind.HiveQueen, Pos = FreeSpot(nest), Facing = _r.Float() * MathF.Tau });
        var near = P.Rooms.OrderByDescending(r => r.Depth + _r.Float()).Take(Math.Max(3, P.Rooms.Count / 2)).ToList();
        int eggs = 6 + _job.Threat;
        for (int k = 0; k < eggs; k++)
        {
            Room r = k < 3 ? nest : near[_r.Int(near.Count)];
            P.Enemies.Add(new Spawn { Kind = EnemyKind.EggSac, Pos = FreeSpot(r), Facing = 0 });
        }
        budget = (budget - eggs * Tables.Enemy(EnemyKind.EggSac).Budget - 3) * Tables.HiveCrowd;
    }

    /// <summary>Each floor of a tower gets its own guards, more and tougher the higher it is; the secure floors guard their stairwell door.</summary>
    private void TowerGuards((EnemyKind kind, float weight)[] roster)
    {
        float total = roster.Sum(x => x.weight);
        for (int k = 1; k < P.Regions.Count; k++)
        {
            var (x0, x1, _) = P.Regions[k];
            var rooms = P.Rooms.Where(r => r.R.X >= x0 && r.R.X < x1 && r.R.Area >= 6).ToList();
            if (rooms.Count == 0) continue;
            float budget = 3 + _job.Threat * (1.2f + 0.45f * k) + (P.Secure[k] ? 4 : 0);
            int guard = 0;
            while (budget > 0.9f && guard++ < 60)
            {
                float roll = _r.Float() * total;
                EnemyKind kind = roster[0].kind;
                foreach (var e in roster) { roll -= e.weight; if (roll <= 0) { kind = e.kind; break; } }
                EnemyDef def = Tables.Enemy(kind);
                if (def.Budget > budget + 0.5f) continue;
                budget -= def.Budget;
                Room r = rooms[_r.Int(rooms.Count)];
                var s = new Spawn { Kind = kind, Pos = FreeSpot(r), Facing = _r.Float() * MathF.Tau };
                if (_r.Chance(0.4f) && r.Links.Count > 0) { s.Patrol.Add(s.Pos); s.Patrol.Add(FreeSpot(P.Rooms[r.Links[_r.Int(r.Links.Count)]])); }
                P.Enemies.Add(s);
            }
            // The secure floors watch the stairwell door, and their cameras watch the lifts.
            if (P.Secure[k])
            {
                V2 door = P.LiftSpots[k] + new V2(1, 3);
                for (int g = 0; g < 2; g++) P.Enemies.Add(new Spawn { Kind = g == 0 ? EnemyKind.Guard : EnemyKind.Heavy, Pos = door + new V2(g * 1.2f - 0.6f, 0.8f), Facing = -MathF.PI / 2 });
            }
            if (_job.Faction == Faction.Halcyon) P.Enemies.Add(new Spawn { Kind = EnemyKind.Camera, Pos = P.LiftSpots[k] + new V2(2, 0), Facing = MathF.PI });
        }
    }

    private V2 YardSpot()
    {
        for (int tries = 0; tries < 80; tries++)
        {
            Pt p = new(_r.Range(2, L.W - 3), _r.Range(2, L.H - 3));
            if (P.Building.Contains(p) || p.X >= P.MainW - 1 || L[p] != Tile.Floor || p.Chebyshev(P.Entry) < 12) continue;
            return p.Centre;
        }
        return new V2(L.W / 2f, 2.5f);
    }

    private void Loot()
    {
        int t = _job.Threat;
        var rooms = P.Rooms.ToList();
        if (rooms.Count == 0) return;
        for (int k = 0; k < 2 + t; k++)
        {
            Room r = rooms[_r.Int(rooms.Count)];
            P.Pickups.Add(new PickupSpawn { Kind = PickupKind.Cash, Pos = FreeSpot(r), Amount = _r.Range(80, 300) * t });
        }
        int paydata = _r.Chance(0.6f) ? 1 : 2;
        for (int k = 0; k < paydata; k++)
        {
            Room r = Deepest() ?? rooms[0];
            string[] names = { "paydata chip", "corporate ledger", "gold-plated commlink", "prototype optic", "art deco cyberhand", "bearer credstick" };
            P.Pickups.Add(new PickupSpawn { Kind = PickupKind.Loot, Pos = FreeSpot(r), Amount = _r.Range(250, 650) * t, Name = _r.Pick(names) });
        }
        if (_r.Chance(0.6f)) P.Pickups.Add(new PickupSpawn { Kind = PickupKind.Medkit, Pos = FreeSpot(rooms[_r.Int(rooms.Count)]), Amount = 1 });
        if (_r.Chance(0.6f)) P.Pickups.Add(new PickupSpawn { Kind = PickupKind.Grenade, Pos = FreeSpot(rooms[_r.Int(rooms.Count)]), Amount = 1, Id = _r.Pick(new[] { "frag", "frag", "flash", "emp", "fire" }) });
    }
}
