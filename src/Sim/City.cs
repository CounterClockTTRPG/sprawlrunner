using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

public enum CTile : byte { Road, Sidewalk, Building, Grass, Tree, Water, Parking, Pier, Plaza, Statue }
public enum LotKind { Building, Park, Parking }

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

    public bool Solid(int x, int y) => this[x, y] is CTile.Building or CTile.Tree or CTile.Water or CTile.Statue;

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
        return c;
    }

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
