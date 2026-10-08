using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace Sprawl;

public partial class SmokeTest
{
    /// <summary>The streets run every frame: keep a step cheap (a generous bound, to catch a quadratic slip).</summary>
    private void Performance()
    {
        City c = City.Generate(777);
        var sim = new CitySim(c, TestCareer(), 1);
        for (int i = 0; i < 120; i++) sim.Step(1 / 60f, new DriveInput { Throttle = 1 });
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 3000; i++) sim.Step(1 / 60f, new DriveInput { Throttle = 1 });
        double ms = sw.Elapsed.TotalMilliseconds / 3000;
        GD.Print($"    a street step: {ms:0.000} ms");
        Check(ms < 1.0, $"a street step stays cheap ({ms:0.000} ms)");
        PathFields();
        foreach (var (name, make) in new (string, System.Func<Node>)[] { ("RunScreen", () => new RunScreen()), ("RunView", () => new RunView()), ("CityScreen", () => new CityScreen()), ("CityView", () => new CityView()) })
        {
            var times = new List<string>();
            for (int k = 0; k < 5; k++)
            {
                var w = Stopwatch.StartNew();
                Node n = make();
                times.Add($"{w.Elapsed.TotalMilliseconds:0.0}");
                n.Free();
            }
            GD.Print($"    new {name}: {string.Join(" ", times)} ms");
        }
    }

    /// <summary>
    /// Distance fields drive every guard: the fast search must give exactly the
    /// distances of a plain Dijkstra, on real sites (towers with stairs too), for
    /// walkers and fliers, near and far -- and stay cheap.
    /// </summary>
    private void PathFields()
    {
        int compared = 0, wrong = 0, fields = 0, early_ = 0, earlyWrong = 0;
        double fastMs = 0, slowMs = 0;
        foreach (SiteStyle st in Enum.GetValues<SiteStyle>())
        for (int t = 1; t <= 5; t += 2)
        {
            var job = new Job { Id = 1, Type = JobType.Retrieve, Style = st, Threat = t, Seed = (ulong)(500 + (int)st * 10 + t), Faction = Faction.Halcyon, Count = 2, Target = "T", Item = "I", Title = "fields", Floors = t == 5 ? 3 : 0 };
            Level L = LevelGen.Generate(job).L;
            var rng = new Rng((ulong)(91 + (int)st * 7 + t));
            var buf = new ushort[L.W * L.H];
            for (int k = 0; k < 6; k++)
            {
                Pt from;
                do from = new Pt(rng.Range(0, L.W - 1), rng.Range(0, L.H - 1)); while (!L.Walkable(from.X, from.Y));
                bool fly = k % 2 == 1;
                int max = k < 4 ? 400 : 30;
                var sw = Stopwatch.StartNew();
                ushort[] fast = L.Field(from, max, fly, buf);
                fastMs += sw.Elapsed.TotalMilliseconds;
                fields++;
                sw.Restart();
                ushort[] slow = ReferenceField(L, from, max, fly);
                slowMs += sw.Elapsed.TotalMilliseconds;
                compared++;
                if (!fast.AsSpan().SequenceEqual(slow)) wrong++;
                // Stopped early near a walker: the same up to its reach, nothing beyond pretending to be closer.
                Pt to;
                do to = new Pt(rng.Range(0, L.W - 1), rng.Range(0, L.H - 1)); while (!L.Walkable(to.X, to.Y));
                ushort[] early = L.Field(from, max, fly, new ushort[L.W * L.H], L.Idx(to.X, to.Y), 40, out int reach);
                early_++;
                for (int i = 0; i < early.Length; i++)
                    if (slow[i] <= reach ? early[i] != slow[i] : early[i] != Level.Far && early[i] <= reach) { earlyWrong++; break; }
            }
        }
        Check(earlyWrong == 0, $"a path field stopped near the walker agrees with the full one within its reach ({early_ - earlyWrong}/{early_})");
        GD.Print($"    a path field: {fastMs / fields:0.000} ms, a plain Dijkstra {slowMs / fields:0.000} ms (avg over {fields}, sites up to threat 5 and 3 floors)");
        Check(wrong == 0, $"the path search matches a plain Dijkstra ({compared - wrong}/{compared})");
        Check(fastMs / fields < 2.0, $"a path field stays cheap ({fastMs / fields:0.000} ms)");
    }

    /// <summary>The textbook search (a heap, the level's own Walkable and Stair): what Level.Field must equal.</summary>
    private static ushort[] ReferenceField(Level L, Pt from, int maxDist, bool flying)
    {
        var dist = new ushort[L.W * L.H];
        Array.Fill(dist, Level.Far);
        var open = new PriorityQueue<int, int>();
        dist[L.Idx(from.X, from.Y)] = 0;
        open.Enqueue(L.Idx(from.X, from.Y), 0);
        while (open.TryDequeue(out int i, out int d))
        {
            if (d > dist[i]) continue;
            if (d > maxDist * 10) break;
            int x = i % L.W, y = i / L.W;
            for (int k = 0; k < 8; k++)
            {
                Pt dir = Pt.Dirs8[k];
                int nx = x + dir.X, ny = y + dir.Y;
                if (!L.In(nx, ny) || !L.Walkable(nx, ny, flying)) continue;
                if (k >= 4 && (!L.Walkable(x + dir.X, y, flying) || !L.Walkable(x, y + dir.Y, flying))) continue;
                int nd = d + (k >= 4 ? 14 : 10);
                int ni = ny * L.W + nx;
                if (nd < dist[ni]) { dist[ni] = (ushort)Math.Min(nd, Level.Far - 1); open.Enqueue(ni, nd); }
            }
            if (L.Stair.TryGetValue(i, out int si) && d + 10 < dist[si]) { dist[si] = (ushort)(d + 10); open.Enqueue(si, d + 10); }
        }
        return dist;
    }
}
