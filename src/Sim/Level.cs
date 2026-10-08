using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

public enum Tile : byte
{
    Floor, Rubble, Glass, Drywall, Brick, Concrete, Reinforced, Door, DoorOpen, LockedDoor,
    Crate, Desk, Barrel, Fence, Wreck, Rack, Vat, Mainframe,
    Console, Safe, Locker, Stairs, Lift, Ward,
    /// <summary>Barracks bunks (low cover), and a server room's coolant pipe (rupture it: a freezing cloud).</summary>
    Bunk, Pipe,
}

/// <summary>What the floor under a tile looks like. Purely visual.</summary>
public enum Floor : byte { Street, Yard, Concrete, Carpet, LabTile, Dance, Wood, Grass, Ritual }

public readonly struct TileDef
{
    public readonly float Hp;
    public readonly bool BlocksMove, BlocksSight, BlocksShot, Low, Flammable;
    public TileDef(float hp, bool move, bool sight, bool shot, bool low = false, bool flammable = false)
    {
        Hp = hp; BlocksMove = move; BlocksSight = sight; BlocksShot = shot; Low = low; Flammable = flammable;
    }
}

/// <summary>
/// A run's map: a grid of tiles, each a material with hit points. Every change
/// goes through <see cref="Set"/> so <see cref="Version"/> and the per-chunk
/// versions move and the view knows what to repaint.
/// </summary>
public sealed class Level
{
    public const int Chunk = 16;
    public readonly int W, H;
    public readonly Tile[] T;
    public readonly float[] Hp;
    public readonly Floor[] F;
    public readonly bool[] Explored;
    public readonly int[] ChunkVersion;
    public readonly int ChunksX, ChunksY;
    public int Version;
    /// <summary>Moves only when a tile starts or stops blocking sight (FOV and the lamps' shadows key on it).</summary>
    public int SightVersion;
    /// <summary>Moves only when a tile starts or stops being passable to walkers or fliers (the path fields key on it).</summary>
    public int PassVersion;
    /// <summary>Decorative lamps: tile centre and colour.</summary>
    public readonly List<(V2 pos, uint colour, float radius)> Lights = new();
    /// <summary>The building footprints, for roofs over unexplored interiors.</summary>
    public readonly List<Rect> Buildings = new();
    /// <summary>Stairs: each end's tile index to the other end's.</summary>
    public readonly Dictionary<int, int> Stair = new();

    public void AddStairs(Pt a, Pt b)
    {
        Set(a, Tile.Stairs);
        Set(b, Tile.Stairs);
        Stair[Idx(a.X, a.Y)] = Idx(b.X, b.Y);
        Stair[Idx(b.X, b.Y)] = Idx(a.X, a.Y);
        if (_stairTo == null) { _stairTo = new int[W * H]; Array.Fill(_stairTo, -1); }
        _stairTo[Idx(a.X, a.Y)] = Idx(b.X, b.Y);
        _stairTo[Idx(b.X, b.Y)] = Idx(a.X, a.Y);
        PassVersion++;
    }

    /// <summary><see cref="Stair"/> as a flat array (-1: no stairs), for the path search.</summary>
    private int[] _stairTo;

    public Pt PtOf(int i) => new(i % W, i / W);

    /// <summary>The other end of the stairs at p, if p is stairs.</summary>
    public bool OtherEnd(Pt p, out Pt end)
    {
        end = default;
        if (!In(p) || !Stair.TryGetValue(Idx(p.X, p.Y), out int j)) return false;
        end = PtOf(j);
        return true;
    }

    public static readonly TileDef[] Defs = MakeDefs();

    private static TileDef[] MakeDefs()
    {
        var d = new TileDef[Enum.GetValues<Tile>().Length];
        d[(int)Tile.Floor] = new(0, false, false, false);
        d[(int)Tile.Rubble] = new(0, false, false, false);
        d[(int)Tile.Glass] = new(5, true, false, true);
        d[(int)Tile.Drywall] = new(60, true, true, true);
        d[(int)Tile.Brick] = new(160, true, true, true);
        d[(int)Tile.Concrete] = new(400, true, true, true);
        d[(int)Tile.Reinforced] = new(float.PositiveInfinity, true, true, true);
        d[(int)Tile.Door] = new(80, true, true, true, flammable: true);
        d[(int)Tile.DoorOpen] = new(80, false, false, false);
        d[(int)Tile.LockedDoor] = new(200, true, true, true);
        d[(int)Tile.Crate] = new(50, true, false, true, low: true, flammable: true);
        d[(int)Tile.Desk] = new(40, true, false, true, low: true, flammable: true);
        d[(int)Tile.Barrel] = new(25, true, false, true, low: true);
        d[(int)Tile.Fence] = new(40, true, false, false, low: true);
        d[(int)Tile.Wreck] = new(300, true, false, true, low: true);
        d[(int)Tile.Rack] = new(120, true, false, true, low: true);
        d[(int)Tile.Vat] = new(120, true, false, true, low: true);
        d[(int)Tile.Mainframe] = new(float.PositiveInfinity, true, false, true, low: true);
        d[(int)Tile.Console] = new(90, true, false, true, low: true);
        d[(int)Tile.Safe] = new(float.PositiveInfinity, true, false, true, low: true);
        d[(int)Tile.Locker] = new(70, true, false, true, low: true);
        d[(int)Tile.Stairs] = new(0, false, false, false);
        // The elevator: its doors and panel are part of the building's core.
        d[(int)Tile.Lift] = new(float.PositiveInfinity, true, true, true);
        // A mage's ward: light you can see through and nothing can cross.
        d[(int)Tile.Ward] = new(float.PositiveInfinity, true, false, true);
        d[(int)Tile.Bunk] = new(45, true, false, true, low: true, flammable: true);
        d[(int)Tile.Pipe] = new(35, true, false, true, low: true);
        return d;
    }

    public Level(int w, int h)
    {
        W = w; H = h;
        T = new Tile[w * h];
        Hp = new float[w * h];
        F = new Floor[w * h];
        Explored = new bool[w * h];
        ChunksX = (w + Chunk - 1) / Chunk;
        ChunksY = (h + Chunk - 1) / Chunk;
        ChunkVersion = new int[ChunksX * ChunksY];
    }

    public bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
    public bool In(Pt p) => In(p.X, p.Y);
    public int Idx(int x, int y) => y * W + x;

    public Tile this[int x, int y] => In(x, y) ? T[y * W + x] : Tile.Reinforced;
    public Tile this[Pt p] => this[p.X, p.Y];

    public void Set(int x, int y, Tile t)
    {
        if (!In(x, y)) return;
        int i = y * W + x;
        Tile was = T[i];
        T[i] = t;
        Hp[i] = Defs[(int)t].Hp;
        Version++;
        if (Defs[(int)was].BlocksSight != Defs[(int)t].BlocksSight)
        {
            SightVersion++;
            _sightChanged?.Add(i);
        }
        if (WalkPass[(int)was] != WalkPass[(int)t] || FlyPass[(int)was] != FlyPass[(int)t]) PassVersion++;
        Touch(x, y);
    }

    public void Set(Pt p, Tile t) => Set(p.X, p.Y, t);

    private void Touch(int x, int y)
    {
        // Neighbouring chunks too: walls are drawn with a little overhang.
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int cx = (x + dx) / Chunk, cy = (y + dy) / Chunk;
            if (x + dx < 0 || y + dy < 0 || cx >= ChunksX || cy >= ChunksY) continue;
            ChunkVersion[cy * ChunksX + cx]++;
        }
    }

    public static TileDef Def(Tile t) => Defs[(int)t];
    public bool BlocksSight(int x, int y) => Defs[(int)this[x, y]].BlocksSight;
    public bool BlocksShot(int x, int y) => Defs[(int)this[x, y]].BlocksShot;

    /// <summary>Whether a walker (or, with flying, a drone) can stand here.</summary>
    public bool Solid(int x, int y, bool flying = false)
    {
        TileDef d = Defs[(int)this[x, y]];
        if (!d.BlocksMove) return false;
        if (flying && d.Low) return false;
        return true;
    }

    /// <summary>Passable for path planning: closed doors open on contact.</summary>
    public bool Walkable(int x, int y, bool flying = false)
    {
        Tile t = this[x, y];
        if (t == Tile.Door) return true;
        return !Solid(x, y, flying);
    }

    /// <summary>Damage a tile. Returns the tile it was if this destroyed it, else null.</summary>
    public Tile? Damage(int x, int y, float amount)
    {
        if (!In(x, y) || amount <= 0) return null;
        int i = y * W + x;
        Tile t = T[i];
        if (Defs[(int)t].Hp <= 0 || float.IsPositiveInfinity(Hp[i])) return null;
        Hp[i] -= amount;
        if (Hp[i] > 0) return null;
        Set(x, y, Tile.Rubble);
        return t;
    }

    // ---- light -----------------------------------------------------------------

    /// <summary>Locked doors with a badge reader (an office's keycard doors), by tile index.</summary>
    public readonly HashSet<int> Keycards = new();
    /// <summary>The light everywhere before the lamps: 1 on a hand-drawn site (no lighting at all), the night on a generated one.</summary>
    public float Ambient = 1;
    /// <summary>Lamps shot out (by index into <see cref="Lights"/>).</summary>
    public readonly HashSet<int> LampsOut = new();
    private float[] _light;
    private int _lightSight = -1, _lampsOut = -1, _lightLamps = -1;
    private float _lightAmbient = float.NaN;
    /// <summary>Each lamp's own light (0 where it does not reach), over the square around it; null until built or after a wall in it changed.</summary>
    private float[][] _lampPatch;
    /// <summary>Tiles whose sight-blocking changed since the light was last built (null before the first build).</summary>
    private List<int> _sightChanged;

    /// <summary>
    /// How lit a spot is, 0 (dark: the night outside the lamps) to 1: the
    /// brightest lamp that sees the tile, falling off over its radius. Walls
    /// cast shadows (the same opacity as sight). Rebuilt only when sight
    /// changes or a lamp goes out, and then only the lamps whose square holds
    /// a changed tile are re-cast.
    /// </summary>
    public float LightAt(V2 p)
    {
        Pt t = Pt.Of(p);
        if (!In(t)) return 0;
        if (_light == null || _lightSight != SightVersion || _lampsOut != LampsOut.Count || _lightLamps != Lights.Count || _lightAmbient != Ambient) BuildLight();
        return _light[Idx(t.X, t.Y)];
    }

    private void BuildLight()
    {
        _light ??= new float[W * H];
        if (_lampPatch == null || _lampPatch.Length != Lights.Count) Array.Resize(ref _lampPatch, Lights.Count);
        // Re-cast the lamps whose square holds a tile that started or stopped blocking sight.
        if (_sightChanged != null)
            foreach (int ci in _sightChanged)
            {
                int cx = ci % W, cy = ci / W;
                for (int k = 0; k < Lights.Count; k++)
                {
                    if (_lampPatch[k] == null) continue;
                    var (pos, _, radius) = Lights[k];
                    int r = (int)MathF.Ceiling(radius);
                    Pt c = Pt.Of(pos);
                    if (Math.Abs(cx - c.X) <= r && Math.Abs(cy - c.Y) <= r) _lampPatch[k] = null;
                }
            }
        (_sightChanged ??= new List<int>()).Clear();
        Array.Fill(_light, Ambient);
        for (int k = 0; k < Lights.Count; k++)
        {
            if (LampsOut.Contains(k)) continue;
            var (pos, _, radius) = Lights[k];
            int r = (int)MathF.Ceiling(radius), side = 2 * r + 1;
            Pt c = Pt.Of(pos);
            float[] patch = _lampPatch[k] ??= CastLamp(pos, radius, c, r, side);
            for (int py = 0; py < side; py++)
            for (int px = 0; px < side; px++)
            {
                float v = patch[py * side + px];
                if (v <= 0) continue;
                int i = Idx(c.X - r + px, c.Y - r + py);
                if (v > _light[i]) _light[i] = v;
            }
        }
        _lightSight = SightVersion;
        _lampsOut = LampsOut.Count;
        _lightLamps = Lights.Count;
        _lightAmbient = Ambient;
    }

    /// <summary>One lamp's light over the square around it: 0 off the map, past its radius or in a wall's shadow.</summary>
    private float[] CastLamp(V2 pos, float radius, Pt c, int r, int side)
    {
        var patch = new float[side * side];
        for (int y = c.Y - r; y <= c.Y + r; y++)
        for (int x = c.X - r; x <= c.X + r; x++)
        {
            if (!In(x, y)) continue;
            var at = new Pt(x, y).Centre;
            float d = V2.Distance(at, pos);
            if (d > radius) continue;
            float v = 1 - d / radius;
            // Full light near the lamp, a soft edge out to its radius.
            v = MathF.Min(1, v * 1.6f);
            if (v <= 0 || !Los(pos, at)) continue;
            patch[(y - c.Y + r) * side + (x - c.X + r)] = v;
        }
        return patch;
    }

    /// <summary>The lamp under this point (within a third of a tile), or -1.</summary>
    public int LampNear(V2 p, float within)
    {
        int best = -1;
        float bd = within;
        for (int k = 0; k < Lights.Count; k++)
        {
            if (LampsOut.Contains(k)) continue;
            float d = V2.Distance(Lights[k].pos, p);
            if (d < bd) { bd = d; best = k; }
        }
        return best;
    }

    // ---- sight -----------------------------------------------------------------

    /// <summary>
    /// Recursive shadowcasting from a tile, radius r: fills <paramref name="vis"/>
    /// (cleared first) and marks explored. Opaque tiles are seen but block what
    /// is behind them.
    /// </summary>
    public void Fov(Pt from, int r, bool[] vis)
    {
        Array.Clear(vis);
        if (!In(from)) return;
        Mark(from.X, from.Y, vis);
        for (int oct = 0; oct < 8; oct++) Cast(from, 1, 1f, 0f, r, Oct[oct, 0], Oct[oct, 1], Oct[oct, 2], Oct[oct, 3], vis);
    }

    private static readonly int[,] Oct =
    {
        { 1, 0, 0, 1 }, { 0, 1, 1, 0 }, { 0, -1, 1, 0 }, { -1, 0, 0, 1 },
        { -1, 0, 0, -1 }, { 0, -1, -1, 0 }, { 0, 1, -1, 0 }, { 1, 0, 0, -1 },
    };

    private void Mark(int x, int y, bool[] vis)
    {
        int i = y * W + x;
        vis[i] = true;
        Explored[i] = true;
    }

    private void Cast(Pt o, int row, float start, float end, int r, int xx, int xy, int yx, int yy, bool[] vis)
    {
        if (start < end) return;
        float newStart = 0;
        int r2 = r * r;
        for (int j = row; j <= r; j++)
        {
            bool blocked = false;
            for (int dx = -j, dy = -j; dx <= 0; dx++)
            {
                float lSlope = (dx - 0.5f) / (dy + 0.5f);
                float rSlope = (dx + 0.5f) / (dy - 0.5f);
                if (start < rSlope) continue;
                if (end > lSlope) break;
                int x = o.X + dx * xx + dy * xy;
                int y = o.Y + dx * yx + dy * yy;
                if (!In(x, y)) continue;
                if (dx * dx + dy * dy <= r2) Mark(x, y, vis);
                bool opaque = BlocksSight(x, y);
                if (blocked)
                {
                    if (opaque) { newStart = rSlope; continue; }
                    blocked = false;
                    start = newStart;
                }
                else if (opaque && j < r)
                {
                    blocked = true;
                    Cast(o, j + 1, start, lSlope, r, xx, xy, yx, yy, vis);
                    newStart = rSlope;
                }
            }
            if (blocked) break;
        }
    }

    /// <summary>
    /// Clear line between two points: no sight-blocking tile strictly between
    /// them (the end tile itself may be opaque, so walls can be seen).
    /// </summary>
    public bool Los(V2 a, V2 b)
    {
        Pt end = Pt.Of(b);
        var w = new Walker(a, b);
        while (w.Next(out int x, out int y))
        {
            if (x == end.X && y == end.Y) return true;
            if (BlocksSight(x, y)) return false;
        }
        return true;
    }

    /// <summary>Clear for a projectile: nothing shot-blocking between.</summary>
    public bool ShotClear(V2 a, V2 b)
    {
        Pt start = Pt.Of(a), end = Pt.Of(b);
        var w = new Walker(a, b);
        while (w.Next(out int x, out int y))
        {
            if (x == end.X && y == end.Y) return true;
            if ((x != start.X || y != start.Y) && BlocksShot(x, y)) return false;
        }
        return true;
    }

    /// <summary>
    /// The first shot-blocking tile on segment a-b (skipping the start tile),
    /// with the point where the segment enters it.
    /// </summary>
    public bool FirstShotBlock(V2 a, V2 b, out Pt hit, out V2 at)
    {
        Pt start = Pt.Of(a);
        var w = new Walker(a, b);
        while (w.Next(out int x, out int y))
        {
            if (x == start.X && y == start.Y) continue;
            if (BlocksShot(x, y))
            {
                hit = new Pt(x, y);
                at = Entry(a, b, hit);
                return true;
            }
        }
        hit = default;
        at = b;
        return false;
    }

    /// <summary>The first sight-blocking tile on segment a-b past the start tile, and where the segment enters it (b if none).</summary>
    public V2 FirstSightBlock(V2 a, V2 b)
    {
        Pt start = Pt.Of(a);
        var w = new Walker(a, b);
        while (w.Next(out int x, out int y))
        {
            if (x == start.X && y == start.Y) continue;
            if (BlocksSight(x, y)) return Entry(a, b, new Pt(x, y));
        }
        return b;
    }

    /// <summary>Where segment a-b first enters tile p (or a if it starts inside).</summary>
    public static V2 Entry(V2 a, V2 b, Pt p)
    {
        V2 d = b - a;
        float tMin = 0, tMax = 1;
        for (int axis = 0; axis < 2; axis++)
        {
            float o = axis == 0 ? a.X : a.Y, v = axis == 0 ? d.X : d.Y;
            float lo = axis == 0 ? p.X : p.Y, hi = lo + 1;
            if (MathF.Abs(v) < 1e-7f) { if (o < lo || o > hi) return b; continue; }
            float t1 = (lo - o) / v, t2 = (hi - o) / v;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = MathF.Max(tMin, t1);
            tMax = MathF.Min(tMax, t2);
        }
        return a + d * MathF.Max(0, tMin - 0.001f);
    }

    /// <summary>Visit every tile the segment passes through, in order (Amanatides-Woo). Stop when the visitor returns false.</summary>
    public static void Walk(V2 a, V2 b, Func<int, int, bool> visit)
    {
        var w = new Walker(a, b);
        while (w.Next(out int x, out int y))
            if (!visit(x, y)) return;
    }

    /// <summary>
    /// <see cref="Walk"/> without a delegate: <c>while (w.Next(out x, out y))</c> yields the
    /// same tiles in the same order (stop by leaving the loop).
    /// </summary>
    public struct Walker
    {
        private int _x, _y, _guard, _state;
        private readonly int _ex, _ey, _sx, _sy;
        private readonly float _tdx, _tdy;
        private float _tmx, _tmy;

        public Walker(V2 a, V2 b)
        {
            _x = (int)MathF.Floor(a.X); _y = (int)MathF.Floor(a.Y);
            _ex = (int)MathF.Floor(b.X); _ey = (int)MathF.Floor(b.Y);
            V2 d = b - a;
            _sx = d.X > 0 ? 1 : -1; _sy = d.Y > 0 ? 1 : -1;
            _tdx = MathF.Abs(d.X) < 1e-9f ? float.MaxValue : MathF.Abs(1 / d.X);
            _tdy = MathF.Abs(d.Y) < 1e-9f ? float.MaxValue : MathF.Abs(1 / d.Y);
            _tmx = MathF.Abs(d.X) < 1e-9f ? float.MaxValue : (_sx > 0 ? (_x + 1 - a.X) : (a.X - _x)) * _tdx;
            _tmy = MathF.Abs(d.Y) < 1e-9f ? float.MaxValue : (_sy > 0 ? (_y + 1 - a.Y) : (a.Y - _y)) * _tdy;
            _guard = 0; _state = 0;
        }

        public bool Next(out int x, out int y)
        {
            x = y = 0;
            if (_state == 2) return false;
            if (_state == 1)
            {
                if (_x == _ex && _y == _ey) { _state = 2; return false; }
                if (_tmx > 1 && _tmy > 1)
                {
                    // Floating error near the end: finish on the end tile.
                    _state = 2;
                    x = _ex; y = _ey;
                    return true;
                }
                if (++_guard >= 512) { _state = 2; return false; }
                if (_tmx < _tmy) { _tmx += _tdx; _x += _sx; }
                else { _tmy += _tdy; _y += _sy; }
            }
            _state = 1;
            x = _x; y = _y;
            return true;
        }
    }

    // ---- paths -----------------------------------------------------------------

    public const ushort Far = ushort.MaxValue;

    /// <summary>
    /// Distances (in tenths of a tile, 10 straight, 14 diagonal) from a tile to
    /// everywhere walkable, out to maxDist tiles. Diagonals never cut corners.
    /// </summary>
    public ushort[] Field(Pt from, int maxDist = 400, bool flying = false, ushort[] into = null) => Field(from, maxDist, flying, into, -1, 0, out _);

    /// <summary>
    /// <see cref="Field(Pt, int, bool, ushort[])"/>, but it may stop early: once the tile
    /// <paramref name="until"/> (an index, -1: never) is reached and the search has gone
    /// <paramref name="margin"/> tenths past it. Every distance up to <paramref name="reach"/>
    /// is then exactly the full field's; everything beyond is either <see cref="Far"/> or
    /// more than <paramref name="reach"/> -- so <see cref="Step"/> from any tile within reach
    /// picks the same neighbour as on the full field. <paramref name="reach"/> is
    /// <see cref="int.MaxValue"/> when the search ran to the end.
    /// </summary>
    public ushort[] Field(Pt from, int maxDist, bool flying, ushort[] into, int until, int margin, out int reach)
    {
        reach = int.MaxValue;
        var dist = into ?? new ushort[W * H];
        Array.Fill(dist, Far);
        if (!In(from)) return dist;
        // Dijkstra with a bucket queue: every step costs 10 or 14, so a ring of
        // 16 buckets by distance replaces the heap (same distances, far cheaper).
        List<int>[] buckets = _buckets ??= Enumerable.Range(0, 16).Select(_ => new List<int>(64)).ToArray();
        foreach (var b in buckets) b.Clear();
        bool[] pass = flying ? FlyPass : WalkPass;
        Tile[] t = T;
        int[] stairTo = _stairTo;
        int start = from.Y * W + from.X;
        dist[start] = 0;
        buckets[0].Add(start);
        int pending = 1, limit = maxDist * 10;
        for (int d = 0; pending > 0 && d <= limit; d++)
        {
            List<int> bucket = buckets[d & 15];
            for (int bi = 0; bi < bucket.Count; bi++)
            {
                int i = bucket[bi];
                if (dist[i] != d) continue;
                int x = i % W, y = i / W;
                for (int k = 0; k < 8; k++)
                {
                    Pt dir = Pt.Dirs8[k];
                    int nx = x + dir.X, ny = y + dir.Y;
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                    int ni = ny * W + nx;
                    if (!pass[(int)t[ni]]) continue;
                    if (k >= 4 && (!pass[(int)t[y * W + nx]] || !pass[(int)t[ny * W + x]])) continue;
                    int nd = d + (k >= 4 ? 14 : 10);
                    if (nd < dist[ni]) { dist[ni] = (ushort)Math.Min(nd, Far - 1); buckets[nd & 15].Add(ni); pending++; }
                }
                if (stairTo != null && stairTo[i] >= 0 && d + 10 < dist[stairTo[i]])
                {
                    int si = stairTo[i];
                    dist[si] = (ushort)(d + 10);
                    buckets[(d + 10) & 15].Add(si);
                    pending++;
                }
            }
            pending -= bucket.Count;
            bucket.Clear();
            // Far enough past the tile that asked: everything up to here is final.
            if (until >= 0 && dist[until] != Far && d >= dist[until] + margin && pending > 0 && d < limit) { reach = d; break; }
        }
        return dist;
    }

    private List<int>[] _buckets;

    /// <summary><see cref="Walkable"/> by tile type, for walkers and for fliers.</summary>
    private static readonly bool[] WalkPass = PassTable(false), FlyPass = PassTable(true);

    private static bool[] PassTable(bool flying)
    {
        var p = new bool[Defs.Length];
        for (int k = 0; k < p.Length; k++)
        {
            TileDef d = Defs[k];
            p[k] = (Tile)k == Tile.Door || !d.BlocksMove || flying && d.Low;
        }
        return p;
    }

    /// <summary>The neighbour of p that is closest to the field's source (or farthest, to flee).</summary>
    public Pt Step(ushort[] field, Pt p, bool flying = false, bool away = false)
    {
        Pt best = p;
        int here = In(p) ? field[p.Y * W + p.X] : Far;
        int bestD = here;
        if (away && here == Far) return p;
        for (int k = 0; k < 8; k++)
        {
            Pt dir = Pt.Dirs8[k];
            int nx = p.X + dir.X, ny = p.Y + dir.Y;
            if (!In(nx, ny) || !Walkable(nx, ny, flying)) continue;
            if (k >= 4 && (!Walkable(p.X + dir.X, p.Y, flying) || !Walkable(p.X, p.Y + dir.Y, flying))) continue;
            int d = field[ny * W + nx];
            if (d == Far) continue;
            if (away ? d > bestD : d < bestD) { bestD = d; best = new Pt(nx, ny); }
        }
        if (OtherEnd(p, out Pt end))
        {
            int d = field[end.Y * W + end.X];
            if (d != Far && (away ? d > bestD : d < bestD)) best = end;
        }
        return best;
    }
}
