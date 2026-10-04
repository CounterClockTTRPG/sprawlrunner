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

    public DriveAutopilot(City c, V2 from, Lot lot, Func<(int, int), (int, int), bool> closed = null) : this(c, from, c.Kerb(lot).pos, closed) => _lot = lot;

    /// <summary>To a point on the road (chasers use this to follow you round corners). <paramref name="closed"/>: junction steps to avoid.</summary>
    public DriveAutopilot(City c, V2 from, V2 kerb, Func<(int, int), (int, int), bool> closed = null)
    {
        _goal = kerb;
        (int, int) Nearest(V2 p)
        {
            int bi = 0, bj = 0;
            float bd = float.MaxValue;
            for (int i = 0; i < c.RoadX.Count; i++)
            for (int j = 0; j < c.RoadY.Count; j++)
            {
                float d = V2.Distance(c.Node(i, j), p);
                if (d < bd) { bd = d; bi = i; bj = j; }
            }
            return (bi, bj);
        }
        var a = Nearest(from);
        var b = Nearest(kerb);
        var nodes = closed == null ? null : Search(c, a, b, closed);
        if (nodes == null)
        {
            nodes = new List<(int, int)> { a };
            var cur = a;
            while (cur.Item1 != b.Item1) { cur = (cur.Item1 + Math.Sign(b.Item1 - cur.Item1), cur.Item2); nodes.Add(cur); }
            while (cur.Item2 != b.Item2) { cur = (cur.Item1, cur.Item2 + Math.Sign(b.Item2 - cur.Item2)); nodes.Add(cur); }
        }
        for (int k = 0; k < nodes.Count; k++)
        {
            V2 n = c.Node(nodes[k].Item1, nodes[k].Item2);
            V2 dir = k + 1 < nodes.Count ? M.Norm(c.Node(nodes[k + 1].Item1, nodes[k + 1].Item2) - n) : M.Norm(kerb - n);
            if (dir.LengthSquared() < 0.1f) dir = new V2(1, 0);
            _route.Add(n + M.Perp(dir));
        }
        _route.Add(kerb);
    }

    /// <summary>Junction to junction by the fewest blocks, round the closed ones (null: no way round).</summary>
    private static List<(int, int)> Search(City c, (int, int) a, (int, int) b, Func<(int, int), (int, int), bool> closed)
    {
        var from = new Dictionary<(int, int), (int, int)> { [a] = a };
        var q = new Queue<(int, int)>();
        q.Enqueue(a);
        while (q.Count > 0)
        {
            var n = q.Dequeue();
            if (n == b) break;
            // Prefer the straight line: try the steps towards the goal first.
            foreach (var (di, dj) in new[] { (Math.Sign(b.Item1 - n.Item1), 0), (0, Math.Sign(b.Item2 - n.Item2)), (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                if (di == 0 && dj == 0) continue;
                var m = (n.Item1 + di, n.Item2 + dj);
                if (m.Item1 < 0 || m.Item2 < 0 || m.Item1 >= c.RoadX.Count || m.Item2 >= c.RoadY.Count || from.ContainsKey(m) || closed(n, m)) continue;
                from[m] = n;
                q.Enqueue(m);
            }
        }
        if (!from.ContainsKey(b)) return null;
        var path = new List<(int, int)> { b };
        while (path[^1] != a) path.Add(from[path[^1]]);
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
