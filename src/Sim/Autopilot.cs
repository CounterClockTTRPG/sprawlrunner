using System;
using System.Collections.Generic;

namespace Sprawl;

/// <summary>
/// Drives the player's car to a place's door with ordinary throttle and
/// steering -- for the agent playtester and the self-check. It plans
/// junction to junction along the avenues, keeps to the right-hand lane,
/// slows for corners, turns round when its target is behind it, and backs
/// off when it is stuck. Inputs are quantised to what keys can do.
/// </summary>
public sealed class DriveAutopilot
{
    private readonly List<V2> _route = new();
    private readonly Lot _lot;
    private readonly V2 _goal;
    private int _wp;
    private float _stuck, _reverse;
    private int _reverseSteer;
    public bool Done;
    /// <summary>The planned route, for drawing: junction to junction, then the kerb.</summary>
    public IReadOnlyList<V2> Waypoints => _route;
    public int Next => _wp;
    /// <summary>Where the route was planned from.</summary>
    public V2 From { get; private set; }

    public DriveAutopilot(City c, V2 from, Lot lot, Func<(int, int), (int, int), bool> closed = null)
    {
        _lot = lot;
        _goal = c.Kerb(lot).pos;
        // A door faces the road it opens onto: north or south means an east-west street.
        Plan(c, from, _goal, closed, lot.Facing.Y != 0);
    }

    /// <summary>To a point on the road (chasers use this to follow you round corners). <paramref name="closed"/>: junction steps to avoid.</summary>
    public DriveAutopilot(City c, V2 from, V2 kerb, Func<(int, int), (int, int), bool> closed = null)
    {
        _goal = kerb;
        Plan(c, from, kerb, closed, null);
    }

    /// <summary>
    /// Along the streets only: each end joins the road it is on at the two
    /// junctions either side of it (never the nearest corner of some other
    /// street, which drew lines through blocks), then the shortest way between
    /// them by road, a turn costing a little so it keeps to few corners.
    /// </summary>
    private void Plan(City c, V2 from, V2 kerb, Func<(int, int), (int, int), bool> closed, bool? kerbOnEastWest)
    {
        From = from;
        var starts = Ends(c, from, null);
        var goals = Ends(c, kerb, kerbOnEastWest);
        var nodes = Search(c, starts, goals, closed) ?? Search(c, starts, goals, null);
        // Both on the same stretch of road, and nothing shorter round the block: straight there.
        bool sameStretch = starts.Count == goals.Count && starts.TrueForAll(a => goals.Exists(b => b.node == a.node));
        if (sameStretch && V2.Distance(from, kerb) <= Length(c, from, nodes, kerb)) nodes = new List<(int, int)>();
        for (int k = 0; k < nodes.Count; k++)
        {
            V2 n = c.Node(nodes[k].Item1, nodes[k].Item2);
            V2 dir = k + 1 < nodes.Count ? M.Norm(c.Node(nodes[k + 1].Item1, nodes[k + 1].Item2) - n) : M.Norm(kerb - n);
            if (dir.LengthSquared() < 0.1f) dir = new V2(1, 0);
            _route.Add(n + M.Perp(dir));
        }
        _route.Add(kerb);
    }

    /// <summary>
    /// The first waypoint still ahead of a car at <paramref name="pos"/>, from <paramref name="from"/> on (the route
    /// line is drawn from the car to it). Passed means beyond it along the leg that led there -- not along the next
    /// one alone, or a car in the right-hand lane counts a right turn as taken a block early and the line cuts
    /// the corner (unless it is in the junction, taking it on the inside).
    /// </summary>
    public int Ahead(V2 pos, int from)
    {
        while (from < _route.Count - 1)
        {
            V2 t = _route[from], d = pos - t;
            float came = V2.Dot(d, M.Norm(t - (from > 0 ? _route[from - 1] : From)));
            bool passed = came > 0 || came > -3 && V2.Dot(d, M.Norm(_route[from + 1] - t)) > 1.5f;
            if (d.Length() >= 3 && !passed) break;
            from++;
        }
        return from;
    }

    /// <summary>Where a car at <paramref name="pos"/> rejoins the leg into waypoint <paramref name="ahead"/>: the route line goes there first, so it follows the road even from the kerb.</summary>
    public V2 OnLeg(V2 pos, int ahead)
    {
        V2 a = ahead > 0 ? _route[ahead - 1] : From, b = _route[ahead], d = b - a;
        float u = d.LengthSquared() < 1e-4f ? 1 : Math.Clamp(V2.Dot(pos - a, d) / d.LengthSquared(), 0, 1);
        return a + d * u;
    }

    private static float Length(City c, V2 from, List<(int, int)> nodes, V2 to)
    {
        float len = 0;
        V2 at = from;
        foreach (var (i, j) in nodes) { len += V2.Distance(at, c.Node(i, j)); at = c.Node(i, j); }
        return len + V2.Distance(at, to);
    }

    /// <summary>
    /// The junctions a point on the road can drive to without leaving it: the
    /// street it is on (the nearer centre line, unless <paramref name="eastWest"/> says),
    /// and the junction before and after it along that street.
    /// </summary>
    internal static List<((int, int) node, float cost)> Ends(City c, V2 p, bool? eastWest)
    {
        int vi = 0, hj = 0;
        for (int i = 1; i < c.RoadX.Count; i++) if (MathF.Abs(c.Node(i, 0).X - p.X) < MathF.Abs(c.Node(vi, 0).X - p.X)) vi = i;
        for (int j = 1; j < c.RoadY.Count; j++) if (MathF.Abs(c.Node(0, j).Y - p.Y) < MathF.Abs(c.Node(0, hj).Y - p.Y)) hj = j;
        bool ew = eastWest ?? MathF.Abs(c.Node(0, hj).Y - p.Y) <= MathF.Abs(c.Node(vi, 0).X - p.X);
        var ends = new List<((int, int), float)>();
        void Add(int i, int j)
        {
            if (i < 0 || j < 0 || i >= c.RoadX.Count || j >= c.RoadY.Count || ends.Exists(e => e.Item1 == (i, j))) return;
            ends.Add(((i, j), V2.Distance(p, c.Node(i, j))));
        }
        if (ew)
        {
            int i = 0;
            while (i + 1 < c.RoadX.Count && c.Node(i + 1, 0).X <= p.X) i++;
            Add(i, hj);
            if (c.Node(i, 0).X < p.X) Add(i + 1, hj);
        }
        else
        {
            int j = 0;
            while (j + 1 < c.RoadY.Count && c.Node(0, j + 1).Y <= p.Y) j++;
            Add(vi, j);
            if (c.Node(0, j).Y < p.Y) Add(vi, j + 1);
        }
        return ends;
    }

    /// <summary>
    /// Junction to junction by road from any of the starts to the best of the
    /// goals, counting the legs on and off; round the closed steps (null: no way round).
    /// </summary>
    private static List<(int, int)> Search(City c, List<((int, int) node, float cost)> starts, List<((int, int) node, float cost)> goals,
        Func<(int, int), (int, int), bool> closed)
    {
        const float Turn = 4;
        var dirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        // State: a junction and the way you came into it (4: you started there).
        var dist = new Dictionary<((int, int), int), float>();
        var prev = new Dictionary<((int, int), int), ((int, int), int)>();
        var q = new PriorityQueue<((int, int), int), float>();
        foreach (var (n, cost) in starts) { dist[(n, 4)] = cost; q.Enqueue((n, 4), cost); }
        while (q.TryDequeue(out var st, out float d))
        {
            if (d > dist[st]) continue;
            var (n, came) = st;
            for (int k = 0; k < 4; k++)
            {
                var m = (n.Item1 + dirs[k].Item1, n.Item2 + dirs[k].Item2);
                if (m.Item1 < 0 || m.Item2 < 0 || m.Item1 >= c.RoadX.Count || m.Item2 >= c.RoadY.Count) continue;
                if (closed != null && closed(n, m)) continue;
                // Between equal ways, cross town east-west first and turn up the street after (as it always has).
                float turn = came == 4 || came == k ? 0 : came >= 2 && k < 2 ? Turn + 0.01f : Turn;
                float nd = d + V2.Distance(c.Node(n.Item1, n.Item2), c.Node(m.Item1, m.Item2)) + turn;
                if (dist.TryGetValue((m, k), out float old) && old <= nd) continue;
                dist[(m, k)] = nd;
                prev[(m, k)] = st;
                q.Enqueue((m, k), nd);
            }
        }
        float best = float.MaxValue;
        ((int, int), int) end = default;
        bool found = false;
        foreach (var kv in dist)
        {
            int g = goals.FindIndex(e => e.node == kv.Key.Item1);
            if (g < 0 || kv.Value + goals[g].cost >= best) continue;
            best = kv.Value + goals[g].cost;
            end = kv.Key;
            found = true;
        }
        if (!found) return null;
        var path = new List<(int, int)> { end.Item1 };
        while (prev.TryGetValue(end, out var p)) { end = p; path.Add(end.Item1); }
        path.Reverse();
        return path;
    }

    public DriveInput Step(CitySim s, float dt) => Step(s, s.Player, dt);

    public DriveInput Step(CitySim s, Car car, float dt, float cruise = 9)
    {
        // Skip waypoints reached or already passed.
        while (_wp < _route.Count - 1)
        {
            V2 t = _route[_wp], next = _route[_wp + 1];
            bool reached = V2.Distance(car.Pos, t) < 2.4f;
            bool passed = V2.Dot(car.Pos - t, next - t) > 0 && V2.Distance(car.Pos, t) < 6;
            if (reached || passed) _wp++;
            else break;
        }
        V2 target = _route[_wp];
        bool final = _wp == _route.Count - 1;
        float dist = V2.Distance(car.Pos, target);
        float speed = car.Speed;
        var inp = new DriveInput();

        if (final && (_lot != null ? s.AtDoor() == _lot : dist < 2.5f))
        {
            inp.Throttle = speed > 0.3f ? -1 : speed < -0.3f ? 1 : 0;
            Done = MathF.Abs(speed) < 0.5f;
            return inp;
        }

        float diff = M.AngleDiff(car.Heading, M.Angle(target - car.Pos));
        if (_reverse > 0)
        {
            _reverse -= dt;
            inp.Throttle = -1;
            inp.Steer = _reverseSteer;
            return Quantise(inp);
        }
        // Target behind: back round towards it.
        if (MathF.Abs(diff) > 2.0f && dist < 12)
        {
            _reverse = 0.9f;
            _reverseSteer = diff > 0 ? -1 : 1;
            inp.Throttle = -1;
            return Quantise(inp);
        }
        // Slow for the corner ahead and for the door.
        float want = cruise;
        if (!final && _wp + 1 < _route.Count)
        {
            float turn = MathF.Abs(M.AngleDiff(M.Angle(target - car.Pos), M.Angle(_route[_wp + 1] - target)));
            if (turn > 0.6f && dist < 10) want = MathF.Min(want, 3.5f + dist * 0.45f);
        }
        if (final) want = MathF.Min(6, 1 + dist * 0.7f);
        if (MathF.Abs(diff) > 0.7f) want = MathF.Min(want, 3.5f);
        inp.Steer = M.Clamp(diff * 3, -1, 1);
        inp.Throttle = speed < want ? 1 : speed > want + 1.2f ? -1 : 0;

        // Stuck against something: back off.
        if (inp.Throttle > 0 && MathF.Abs(speed) < 0.4f) _stuck += dt; else _stuck = 0;
        if (_stuck > 1.0f)
        {
            _stuck = 0;
            _reverse = 0.8f;
            _reverseSteer = diff > 0 ? -1 : 1;
        }
        return Quantise(inp);
    }

    /// <summary>Keys are on or off.</summary>
    private static DriveInput Quantise(DriveInput i) => new()
    {
        Throttle = i.Throttle > 0.1f ? 1 : i.Throttle < -0.1f ? -1 : 0,
        Steer = i.Steer > 0.15f ? 1 : i.Steer < -0.15f ? -1 : 0,
        Handbrake = false,
    };
}
