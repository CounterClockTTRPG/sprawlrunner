using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

public enum CTile : byte
{
    Road, Sidewalk, Building, Grass, Tree, Water, Parking, Pier, Plaza, Statue,
    /// <summary>A two-tile service lane through a block, avenue to avenue (cars fit, cruisers don't follow).</summary>
    Alley,
    /// <summary>Waste ground: drivable, slow unless the ride is built for it.</summary>
    Dirt,
    /// <summary>A heap of what used to be a building, or a burnt-out car.</summary>
    Rubble,
    /// <summary>Stacked shipping containers or crates.</summary>
    Container,
    /// <summary>A rail spur through an Industrial block: drivable, the sleepers loosen your grip.</summary>
    Rail,
    /// <summary>The Bayshore Causeway and its ramps: smooth and fast.</summary>
    Highway,
    /// <summary>The causeway's crash barrier, and the Spire's bollards.</summary>
    Barrier,
    /// <summary>A neon arcade through a Strip block: people walk it, cars can (carefully).</summary>
    Arcade,
}
public enum LotKind { Building, Park, Parking, Yard }

public sealed class Lot
{
    public int Index;
    public Rect R;
    public LotKind Kind;
    public string District;
    public float Height;
    /// <summary>Sidewalk tile in front of the entrance, and the direction from the lot towards it.</summary>
    public Pt Door;
    public Pt Facing;
    public uint Sign;
    public int Variant;
    /// <summary>A landmark occupying the whole block (see City.Landmarks), or null.</summary>
    public string Landmark;
    /// <summary>"home", "loft", a contact id, or null.</summary>
    public string Place;
}

/// <summary>
/// Kessler Bay: a grid of four-tile avenues around blocks of lots, the bay to
/// the south and east. Generated from the career seed, so the same career
/// always drives the same streets.
/// </summary>
public sealed class City
{
    public const int W = 228, H = 170;
    /// <summary>Bump whenever generation changes: a career saved on another version gets told its city was rebuilt.</summary>
    public const int Version = 3;
    public const int Road = 4;
    public readonly CTile[] T = new CTile[W * H];
    public readonly List<Lot> Lots = new();
    public readonly List<int> RoadX = new(), RoadY = new();
    public readonly string[] DistrictAt = new string[W * H];
    public int LandW, LandH;

    public CTile this[int x, int y] => x >= 0 && y >= 0 && x < W && y < H ? T[y * W + x] : CTile.Water;
    public bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;

    public bool Solid(int x, int y) => this[x, y] is CTile.Building or CTile.Tree or CTile.Water or CTile.Statue or CTile.Rubble or CTile.Container or CTile.Barrier;

    /// <summary>A lane through a block (alley, arcade, rail spur): too tight for a cruiser.</summary>
    public bool Narrow(int x, int y) => this[x, y] is CTile.Alley or CTile.Arcade or CTile.Rail;

    /// <summary>Where people walk: the pavement, and the Strip's arcades.</summary>
    public bool Walk(CTile t) => t is CTile.Sidewalk or CTile.Arcade;

    /// <summary>Potholes in the Barrens' avenues (tile index): a jolt at speed.</summary>
    public readonly HashSet<int> Potholes = new();
    /// <summary>The lanes cut through blocks, for the checks and the map.</summary>
    public readonly List<Rect> Alleys = new();
    /// <summary>The Bayshore Causeway: its two legs over the bay (south and east), and the ramps down to the avenues.</summary>
    public Rect CausewaySouth, CausewayEast;
    public readonly List<Rect> Ramps = new();

    /// <summary>The lot that is this place ("home", a contact, a hub), or null.</summary>
    public Lot Place(string id) => id == null ? Lots.FirstOrDefault(l => l.Place == null) : _places.TryGetValue(id, out Lot l) ? l : null;

    /// <summary>Each place's lot (the first in <see cref="Lots"/> with it, as a scan would find), filled in at generation.</summary>
    private readonly Dictionary<string, Lot> _places = new();

    private static readonly (string id, float x, float y)[] Seeds =
    {
        ("spire", 0.5f, 0.22f), ("strip", 0.5f, 0.55f), ("docks", 0.42f, 0.92f), ("docks", 0.88f, 0.9f),
        ("industrial", 0.88f, 0.45f), ("barrens", 0.12f, 0.55f), ("barrens", 0.12f, 0.12f), ("spire", 0.75f, 0.12f),
    };

    public static City Generate(ulong seed)
    {
        var c = new City();
        var r = new Rng(seed ^ 0xC17E);
        c.LandW = W - 14;
        c.LandH = H - 12;
        Array.Fill(c.T, CTile.Water);

        // Avenues, spaced irregularly; the last one runs along the waterfront.
        int x = 0;
        while (x < c.LandW - Road - 10) { c.RoadX.Add(x); x += Road + r.Range(14, 22); }
        c.RoadX.Add(c.LandW - Road);
        int y = 0;
        while (y < c.LandH - Road - 10) { c.RoadY.Add(y); y += Road + r.Range(12, 18); }
        c.RoadY.Add(c.LandH - Road);

        for (int ty = 0; ty < c.LandH; ty++)
        for (int tx = 0; tx < c.LandW; tx++)
            c.T[ty * W + tx] = CTile.Sidewalk;
        foreach (int rx in c.RoadX)
            for (int ty = 0; ty < c.LandH; ty++)
            for (int k = 0; k < Road; k++) c.T[ty * W + rx + k] = CTile.Road;
        foreach (int ry in c.RoadY)
            for (int tx = 0; tx < c.LandW; tx++)
            for (int k = 0; k < Road; k++) c.T[(ry + k) * W + tx] = CTile.Road;

        for (int ty = 0; ty < H; ty++)
        for (int tx = 0; tx < W; tx++)
            c.DistrictAt[ty * W + tx] = c.NearestDistrict(tx, ty);

        // Blocks: sidewalk ring, then lots that all touch the ring -- or one landmark.
        var blocks = new List<Rect>();
        for (int i = 0; i + 1 < c.RoadX.Count; i++)
        for (int j = 0; j + 1 < c.RoadY.Count; j++)
            blocks.Add(new Rect(c.RoadX[i] + Road, c.RoadY[j] + Road, c.RoadX[i + 1] - c.RoadX[i] - Road, c.RoadY[j + 1] - c.RoadY[j] - Road));
        var marks = new Dictionary<Rect, string>();
        foreach (var (id, _, district, _) in Landmarks)
        {
            var fit = blocks.Where(b => !marks.ContainsKey(b) && b.W >= 14 && b.H >= 12 && c.DistrictAt[b.Centre.Y * W + b.Centre.X] == district).ToList();
            if (fit.Count == 0) fit = blocks.Where(b => !marks.ContainsKey(b) && b.W >= 14 && b.H >= 12).ToList();
            if (fit.Count > 0) marks[r.Pick(fit)] = id;
        }
        foreach (Rect block in blocks)
        {
            if (marks.TryGetValue(block, out string lm)) c.AddLandmark(block.Inset(1), lm);
            else c.Subdivide(block.Inset(1), r);
        }

        // Piers into the bay along the docks.
        for (int k = 0; k < 6; k++)
        {
            int px = r.Range(8, c.LandW - 10);
            if (c.DistrictAt[(c.LandH + 2) * W + px] != "docks") continue;
            for (int ty = c.LandH; ty < H - 2; ty++)
                for (int dx = 0; dx < 3; dx++) c.T[ty * W + px + dx] = CTile.Pier;
        }

        c.AssignPlaces(r);
        foreach (Lot l in c.Lots)
            if (l.Place != null) c._places.TryAdd(l.Place, l);
        // After every address is dealt, and from a stream of its own: the streets get their character
        // without moving a single door, so a saved career keeps its city.
        c.Dress(blocks, marks, new Rng(seed ^ 0xD2E55UL));
        c.BuildCauseway(new Rng(seed ^ 0xCA05EUL));
        return c;
    }

    // ---- the districts' own streets ------------------------------------------------------

    /// <summary>How often a deep block in each district has a lane cut through it.</summary>
    private static float AlleyChance(string district) => district switch
    {
        "barrens" => 0.65f, "strip" => 0.6f, "docks" => 0.45f, "industrial" => 0.45f, _ => 0.2f,
    };

    /// <summary>
    /// The districts' own streets: lanes through the blocks (alleys; arcades on the Strip; rail spurs in
    /// Industrial), container yards at the Docks, waste ground and potholes in the Barrens, crate yards in
    /// Industrial, corporate plazas behind bollards in the Spire. Only open lots change kind; buildings
    /// give up a tile of depth to a lane, never their door.
    /// </summary>
    private void Dress(List<Rect> blocks, Dictionary<Rect, string> marks, Rng r)
    {
        foreach (Rect block in blocks)
        {
            if (marks.ContainsKey(block)) continue;
            string dist = DistrictAt[block.Centre.Y * W + block.Centre.X];
            if (!r.Chance(AlleyChance(dist))) continue;
            CutLane(block, dist);
        }
        foreach (Lot l in Lots)
        {
            if (l.Landmark != null || l.Place != null) continue;
            switch (l.District)
            {
                case "docks" when l.Kind is LotKind.Parking or LotKind.Park && r.Chance(0.8f):
                    Yard(l, 2, r);
                    break;
                case "industrial" when l.Kind == LotKind.Parking && r.Chance(0.6f):
                    Yard(l, 1, r);
                    break;
                case "barrens" when l.Kind is LotKind.Park or LotKind.Parking:
                    WasteGround(l, r);
                    break;
                case "spire" when l.Kind == LotKind.Park:
                    CorporatePlaza(l);
                    break;
            }
        }
        // The Barrens' avenues are falling apart: potholes in the lanes, never in a junction.
        for (int y = 0; y < LandH; y++)
        for (int x = 0; x < LandW; x++)
        {
            if (T[y * W + x] != CTile.Road || DistrictAt[y * W + x] != "barrens") continue;
            bool inX = RoadX.Exists(rx => x >= rx && x < rx + Road), inY = RoadY.Exists(ry => y >= ry && y < ry + Road);
            if (inX && inY) continue;
            if (r.Chance(0.035f)) Potholes.Add(y * W + x);
        }
    }

    /// <summary>
    /// A two-tile lane along the line where a block's two strips of lots meet, from avenue to avenue across
    /// the pavement ring. Each lot beside it loses one tile of depth (and keeps its door, which faces out).
    /// </summary>
    private void CutLane(Rect block, string dist)
    {
        Rect inner = block.Inset(1);
        bool wide = inner.W >= inner.H;
        int depth = wide ? inner.H : inner.W;
        if (depth < 10) return;
        var inside = Lots.Where(l => inner.Contains(new Pt(l.R.X, l.R.Y))).ToList();
        if (inside.Count < 2) return;
        // The seam: the far edge of the first strip.
        Lot first = inside.FirstOrDefault(l => wide ? l.R.Y == inner.Y : l.R.X == inner.X);
        if (first == null) return;
        int cut = wide ? first.R.Bottom : first.R.Right;
        var before = new List<Lot>();
        var after = new List<Lot>();
        foreach (Lot l in inside)
        {
            int lo = wide ? l.R.Y : l.R.X, hi = wide ? l.R.Bottom : l.R.Right;
            if (hi == cut) before.Add(l);
            else if (lo == cut) after.Add(l);
            else return; // not two strips after all
            if (hi - lo < 4) return;
            // A door on the seam would open into the lane.
            int dc = wide ? l.Door.Y : l.Door.X;
            if (dc == cut - 1 || dc == cut) return;
        }
        if (before.Count == 0 || after.Count == 0) return;
        foreach (Lot l in before) l.R = wide ? new Rect(l.R.X, l.R.Y, l.R.W, l.R.H - 1) : new Rect(l.R.X, l.R.Y, l.R.W - 1, l.R.H);
        foreach (Lot l in after) l.R = wide ? new Rect(l.R.X, l.R.Y + 1, l.R.W, l.R.H - 1) : new Rect(l.R.X + 1, l.R.Y, l.R.W - 1, l.R.H);
        CTile lane = dist switch { "strip" => CTile.Arcade, "industrial" => CTile.Rail, _ => CTile.Alley };
        Rect a = wide ? new Rect(block.X, cut - 1, block.W, 2) : new Rect(cut - 1, block.Y, 2, block.H);
        for (int y = a.Y; y < a.Bottom; y++)
        for (int x = a.X; x < a.Right; x++) T[y * W + x] = lane;
        Alleys.Add(a);
    }

    /// <summary>Rows of stacks <paramref name="stack"/> tiles wide with two-tile lanes between, and a clear apron along the street.</summary>
    private void Yard(Lot l, int stack, Rng r)
    {
        l.Kind = LotKind.Yard;
        bool rowsAcross = l.Facing.Y != 0; // the street is north or south: the lanes run north-south
        CTile floor = l.District == "docks" ? CTile.Parking : CTile.Dirt;
        for (int y = l.R.Y; y < l.R.Bottom; y++)
        for (int x = l.R.X; x < l.R.Right; x++)
        {
            bool apron = l.Facing.Y < 0 ? y == l.R.Y : l.Facing.Y > 0 ? y == l.R.Bottom - 1 : l.Facing.X < 0 ? x == l.R.X : x == l.R.Right - 1;
            int k = rowsAcross ? x - l.R.X : y - l.R.Y;
            bool lane = k % (stack + 2) >= stack;
            T[y * W + x] = apron || lane ? floor : r.Chance(0.92f) ? CTile.Container : floor;
        }
    }

    /// <summary>Where a building came down: dirt, heaps of rubble, a burnt-out car.</summary>
    private void WasteGround(Lot l, Rng r)
    {
        l.Kind = LotKind.Yard;
        for (int y = l.R.Y; y < l.R.Bottom; y++)
        for (int x = l.R.X; x < l.R.Right; x++)
        {
            bool edge = x == l.R.X || y == l.R.Y || x == l.R.Right - 1 || y == l.R.Bottom - 1;
            T[y * W + x] = !edge && r.Chance(0.16f) ? CTile.Rubble : CTile.Dirt;
        }
    }

    /// <summary>A Spire park becomes a corporate plaza: paving, planters in rows, bollards along the street with a gap.</summary>
    private void CorporatePlaza(Lot l)
    {
        for (int y = l.R.Y; y < l.R.Bottom; y++)
        for (int x = l.R.X; x < l.R.Right; x++)
        {
            bool front = l.Facing.Y < 0 ? y == l.R.Y : l.Facing.Y > 0 ? y == l.R.Bottom - 1 : l.Facing.X < 0 ? x == l.R.X : x == l.R.Right - 1;
            int along = l.Facing.Y != 0 ? x - l.R.X : y - l.R.Y, len = l.Facing.Y != 0 ? l.R.W : l.R.H;
            bool gap = Math.Abs(along - len / 2) <= 1;
            bool planter = !front && (x - l.R.X) % 3 == 1 && (y - l.R.Y) % 3 == 1;
            T[y * W + x] = front ? (gap || along % 2 == 1 ? CTile.Plaza : CTile.Barrier) : planter ? CTile.Tree : CTile.Plaza;
        }
    }

    // ---- the Bayshore Causeway -----------------------------------------------------------

    /// <summary>
    /// An expressway on stilts over the bay, along the south and east shores, joined at the corner, with a
    /// crash barrier both sides and ramps down to the waterfront avenues. No junctions, no traffic lights,
    /// nobody on foot: the quick way round the city's edge, and the piers stop short of it.
    /// </summary>
    private void BuildCauseway(Rng r)
    {
        int sy = LandH + 6, ex = LandW + 6;
        CausewaySouth = new Rect(0, sy, ex + Road, Road);
        CausewayEast = new Rect(ex, 0, Road, sy + Road);
        // The piers end at the causeway's inner barrier.
        for (int y = LandH + 5; y < H; y++)
        for (int x = 0; x < W; x++)
            if (T[y * W + x] == CTile.Pier) T[y * W + x] = CTile.Water;
        foreach (Rect leg in new[] { CausewaySouth, CausewayEast })
            for (int y = leg.Y - 1; y <= leg.Bottom; y++)
            for (int x = leg.X - 1; x <= leg.Right; x++)
            {
                if (!In(x, y)) continue;
                bool deck = leg.Contains(new Pt(x, y)) || CausewaySouth.Contains(new Pt(x, y)) || CausewayEast.Contains(new Pt(x, y));
                T[y * W + x] = deck ? CTile.Highway : T[y * W + x] == CTile.Highway ? CTile.Highway : CTile.Barrier;
            }
        // Ramps: from the waterfront avenue's end of a north-south avenue (south leg) or an east-west one (east leg).
        var south = new List<int> { 0 };
        for (int i = 1; i < RoadX.Count - 1; i++) if (r.Chance(0.3f) && RoadX[i] - RoadX[south[^1]] > 40) south.Add(i);
        var east = new List<int> { 0 };
        for (int j = 1; j < RoadY.Count - 1; j++) if (r.Chance(0.4f) && RoadY[j] - RoadY[east[^1]] > 34) east.Add(j);
        foreach (int i in south) Ramp(new Rect(RoadX[i], LandH, Road, sy - LandH));
        foreach (int j in east) Ramp(new Rect(LandW, RoadY[j], ex - LandW, Road));
    }

    private void Ramp(Rect ramp)
    {
        for (int y = ramp.Y; y < ramp.Bottom; y++)
        for (int x = ramp.X; x < ramp.Right; x++) T[y * W + x] = CTile.Highway;
        // Barriers down both sides of the ramp, over the water.
        bool down = ramp.H > ramp.W;
        for (int k = 0; k < (down ? ramp.H : ramp.W); k++)
            foreach (int side in new[] { -1, down ? ramp.W : ramp.H })
            {
                int x = down ? ramp.X + side : ramp.X + k, y = down ? ramp.Y + k : ramp.Y + side;
                if (In(x, y) && T[y * W + x] is CTile.Water or CTile.Pier) T[y * W + x] = CTile.Barrier;
            }
        Ramps.Add(ramp);
    }

    /// <summary>On the causeway or a ramp.</summary>
    public bool OnCauseway(V2 p) => this[(int)MathF.Floor(p.X), (int)MathF.Floor(p.Y)] == CTile.Highway;

    private string NearestDistrict(int x, int y)
    {
        float best = float.MaxValue;
        string id = "strip";
        foreach (var s in Seeds)
        {
            float dx = x - s.x * LandW, dy = (y - s.y * LandH) * 1.3f;
            float d = dx * dx + dy * dy;
            if (d < best) { best = d; id = s.id; }
        }
        return id;
    }

    private void Subdivide(Rect inner, Rng r)
    {
        if (inner.W < 4 || inner.H < 4) return;
        // Two strips along the long side when the block is deep enough, so every lot faces a street.
        bool wide = inner.W >= inner.H;
        var strips = new List<Rect>();
        int depth = wide ? inner.H : inner.W;
        if (depth >= 10)
        {
            int cut = depth / 2 + r.Range(-1, 1);
            strips.Add(wide ? new Rect(inner.X, inner.Y, inner.W, cut) : new Rect(inner.X, inner.Y, cut, inner.H));
            strips.Add(wide ? new Rect(inner.X, inner.Y + cut, inner.W, inner.H - cut) : new Rect(inner.X + cut, inner.Y, inner.W - cut, inner.H));
        }
        else strips.Add(inner);
        foreach (Rect s in strips)
        {
            int len = wide ? s.W : s.H;
            int at = 0;
            while (at < len)
            {
                int seg = Math.Min(len - at, r.Range(5, 10));
                if (len - at - seg < 4) seg = len - at;
                Rect lot = wide ? new Rect(s.X + at, s.Y, seg, s.H) : new Rect(s.X, s.Y + at, s.W, seg);
                AddLot(lot, inner, r);
                at += seg;
            }
        }
    }

    /// <summary>The city's landmarks: one block each.</summary>
    public static readonly (string id, string name, string district, float height)[] Landmarks =
    {
        ("arcology", "Halcyon Arcology", "spire", 9f),
        ("pagoda", "Neon Pagoda", "strip", 4.5f),
        ("cathedral", "St. Brigid's", "barrens", 3f),
        ("port", "Container port", "docks", 1.2f),
        ("stadium", "Kessler Stadium", "industrial", 2f),
        ("plaza", "Memorial Plaza", "spire", 0.2f),
    };

    private void AddLandmark(Rect lr, string id)
    {
        var def = Landmarks.First(x => x.id == id);
        bool plaza = id == "plaza";
        var lot = new Lot { Index = Lots.Count, R = lr, Kind = plaza ? LotKind.Park : LotKind.Building, District = DistrictAt[lr.Centre.Y * W + lr.Centre.X], Height = def.height, Landmark = id };
        for (int y = lr.Y; y < lr.Bottom; y++)
        for (int x = lr.X; x < lr.Right; x++)
            T[y * W + x] = plaza ? CTile.Plaza : CTile.Building;
        if (plaza)
        {
            // A fountain with a statue in the middle; trees at the corners. You can drive round it.
            Pt c0 = lr.Centre;
            for (int y = c0.Y - 1; y <= c0.Y; y++) for (int x = c0.X - 1; x <= c0.X; x++) T[y * W + x] = CTile.Statue;
            foreach (Pt t in new[] { new Pt(lr.X + 1, lr.Y + 1), new Pt(lr.Right - 2, lr.Y + 1), new Pt(lr.X + 1, lr.Bottom - 2), new Pt(lr.Right - 2, lr.Bottom - 2) })
                T[t.Y * W + t.X] = CTile.Tree;
        }
        lot.Door = new Pt(-100, -100);
        Lots.Add(lot);
    }

    private void AddLot(Rect lr, Rect inner, Rng r)
    {
        string dist = DistrictAt[lr.Centre.Y * W + lr.Centre.X];
        DistrictDef dd = Tables.District(dist);
        float roll = r.Float();
        LotKind kind = roll < 0.07f ? LotKind.Park : roll < 0.15f ? LotKind.Parking : LotKind.Building;
        var lot = new Lot { Index = Lots.Count, R = lr, Kind = kind, District = dist, Height = r.Range(dd.MinHeight, dd.MaxHeight), Variant = r.Int(4) };
        if (r.Chance(dist is "strip" ? 0.7f : 0.3f)) lot.Sign = r.Pick(new uint[] { 0xff2a6dff, 0x05d9e8ff, 0xf9c80eff, 0x9a6bffff, 0x40ffb0ff });
        for (int y = lr.Y; y < lr.Bottom; y++)
        for (int x = lr.X; x < lr.Right; x++)
            T[y * W + x] = kind switch
            {
                LotKind.Park => r.Chance(0.18f) && x > lr.X && y > lr.Y && x < lr.Right - 1 && y < lr.Bottom - 1 ? CTile.Tree : CTile.Grass,
                LotKind.Parking => CTile.Parking,
                _ => CTile.Building,
            };
        // The entrance: the middle of a side on the block's edge.
        var sides = new List<(Pt door, Pt facing)>();
        if (lr.Y == inner.Y) sides.Add((new Pt(lr.X + lr.W / 2, lr.Y - 1), new Pt(0, -1)));
        if (lr.Bottom == inner.Bottom) sides.Add((new Pt(lr.X + lr.W / 2, lr.Bottom), new Pt(0, 1)));
        if (lr.X == inner.X) sides.Add((new Pt(lr.X - 1, lr.Y + lr.H / 2), new Pt(-1, 0)));
        if (lr.Right == inner.Right) sides.Add((new Pt(lr.Right, lr.Y + lr.H / 2), new Pt(1, 0)));
        if (sides.Count == 0) sides.Add((new Pt(lr.X + lr.W / 2, lr.Y - 1), new Pt(0, -1)));
        (lot.Door, lot.Facing) = r.Pick(sides);
        Lots.Add(lot);
    }

    private void AssignPlaces(Rng r)
    {
        Claim("home", "docks", r);
        Claim("loft", "spire", r);
        foreach (ContactDef k in Tables.Contacts.Where(k => !k.Late)) Claim(k.Id, k.District, r);
        foreach (var h in Hubs) Claim(h.id, h.district, r);
        // Contacts added later move into whatever is free, so every older city keeps its addresses.
        foreach (ContactDef k in Tables.Contacts.Where(k => k.Late)) Claim(k.Id, k.District, r);
        // The Spire loft is in the Spire: the arcology's upper floors. Moved in after every claim (and with no draw),
        // so all the other addresses stay where they were; the lot the loft was dealt goes back to being a building.
        if (Lots.FirstOrDefault(l => l.Landmark == "arcology") is Lot spire && Lots.FirstOrDefault(l => l.Place == "loft") is Lot dealt)
        {
            dealt.Place = null;
            dealt.Sign = 0;
            spire.Place = "loft";
            spire.Sign = SafehouseColour;
            // A landmark fills its block, so every side is on a road: the door is the middle of the south face.
            spire.Door = new Pt(spire.R.X + spire.R.W / 2, spire.R.Bottom);
            spire.Facing = new Pt(0, 1);
        }
    }

    /// <summary>Places you can walk around in.</summary>
    public static readonly (string id, string name, string district, uint colour)[] Hubs =
    {
        ("market", "Night market", "strip", 0xffd040ff),
        ("bazaar", "Barrens bazaar", "barrens", 0xff8040ff),
    };

    /// <summary>The safehouse's sign and map pin: a lime no contact or hub wears.</summary>
    public const uint SafehouseColour = 0xc8ff3cff;

    /// <summary>Your safehouse (the docks flat or the Spire loft).</summary>
    public static bool IsSafehouse(string place) => place is "home" or "loft";

    public static bool IsHub(string place) => Hubs.Any(h => h.id == place);

    /// <summary>What a place is called on signs and maps.</summary>
    public static string PlaceName(string place) => place switch
    {
        null => "Job site",
        "home" or "loft" => "Safehouse",
        _ when IsHub(place) => Hubs.First(h => h.id == place).name,
        _ => Tables.Contact(place).Venue,
    };

    public static string PlaceWho(string place) => place switch
    {
        null or "home" or "loft" => "",
        _ when IsHub(place) => "on foot",
        _ => $"{Tables.Contact(place).Name}, {Tables.Contact(place).Role.ToLower()}",
    };

    private void Claim(string place, string district, Rng r)
    {
        var options = Lots.Where(l => l.Kind == LotKind.Building && l.Place == null && l.Landmark == null && l.District == district && l.R.W >= 5 && l.R.H >= 5).ToList();
        if (options.Count == 0) options = Lots.Where(l => l.Kind == LotKind.Building && l.Place == null && l.Landmark == null).ToList();
        Lot lot = r.Pick(options);
        lot.Place = place;
        lot.Sign = place switch
        {
            "home" or "loft" => SafehouseColour,
            _ when IsHub(place) => Hubs.First(h => h.id == place).colour,
            _ => Tables.Contact(place).Colour,
        };
    }

    /// <summary>A building lot in the district free for a job site, or -1.</summary>
    public int FreeSite(string district, Rng r, HashSet<int> taken)
    {
        var options = Lots.Where(l => l.Kind == LotKind.Building && l.Place == null && l.Landmark == null && l.District == district && !taken.Contains(l.Index)).ToList();
        if (options.Count == 0) return -1;
        return r.Pick(options).Index;
    }

    /// <summary>Where a car leaving a lot parks: on the road in front of the door, facing along it.</summary>
    public (V2 pos, float heading) Kerb(Lot lot)
    {
        V2 door = lot.Door.Centre;
        V2 f = new(lot.Facing.X, lot.Facing.Y);
        V2 pos = door + f * 1.6f;
        // Drive on the right: face so the kerb is on our right.
        float heading = M.Angle(M.Perp(f));
        return (pos, heading);
    }

    // ---- traffic network ----------------------------------------------------------

    public V2 Node(int i, int j) => new(RoadX[i] + Road / 2f, RoadY[j] + Road / 2f);
}
