using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Godot;

namespace Sprawl;

/// <summary>
/// A frame profiler for hunting hitches: named sections timed with
/// <c>using (Perf.Time("x"))</c>, gathered per rendered frame with the GC and
/// the renderer's own timings, and a report of the slow frames. Off (and
/// free) unless the agent harness says <c>perf start</c>.
/// </summary>
public static partial class Perf
{
    public static bool On;

    private struct Frame
    {
        public double Ms, RenderCpu, RenderGpu;
        public long Alloc;
        public int Gc0, Gc1, Gc2, Draws;
        public double GcPause;
        public Dictionary<string, double> Sections;
    }

    private static readonly List<Frame> _frames = new();
    private static Dictionary<string, double> _cur = new();
    private static long _alloc;
    private static int _g0, _g1, _g2;
    private static TimeSpan _pause;

    public readonly struct Scope : IDisposable
    {
        private readonly string _name;
        private readonly long _t0;
        public Scope(string name) { _name = name; _t0 = Stopwatch.GetTimestamp(); }
        public void Dispose()
        {
            if (_name == null) return;
            double ms = (Stopwatch.GetTimestamp() - _t0) * 1000.0 / Stopwatch.Frequency;
            _cur[_name] = _cur.GetValueOrDefault(_name) + ms;
        }
    }

    public static Scope Time(string name) => On ? new Scope(name) : default;

    /// <summary>A count for this frame, reported beside the timings (names start with #).</summary>
    public static void Count(string name, int n) { if (On) _cur["#" + name] = _cur.GetValueOrDefault("#" + name) + n; }

    public static void Start(Viewport vp)
    {
        _frames.Clear();
        _cur = new();
        _alloc = GC.GetTotalAllocatedBytes();
        (_g0, _g1, _g2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        _pause = GC.GetTotalPauseDuration();
        RenderingServer.ViewportSetMeasureRenderTime(vp.GetViewportRid(), true);
        On = true;
    }

    /// <summary>Close the frame that just ran (called once per _Process by <see cref="Probe"/>).</summary>
    private static void EndFrame(double delta, Viewport vp)
    {
        long a = GC.GetTotalAllocatedBytes();
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        TimeSpan p = GC.GetTotalPauseDuration();
        Rid rid = vp.GetViewportRid();
        _frames.Add(new Frame
        {
            Ms = delta * 1000, Alloc = a - _alloc, Gc0 = g0 - _g0, Gc1 = g1 - _g1, Gc2 = g2 - _g2,
            GcPause = (p - _pause).TotalMilliseconds, Sections = _cur,
            RenderCpu = RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid),
            RenderGpu = RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid),
            Draws = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
        });
        _cur = new();
        _alloc = a;
        (_g0, _g1, _g2, _pause) = (g0, g1, g2, p);
    }

    /// <summary>Every frame as a row: frame ms, render, GC, then each section.</summary>
    public static string Csv()
    {
        var keys = _frames.SelectMany(x => x.Sections.Keys).Distinct().OrderBy(k => k).ToList();
        var sb = new StringBuilder("frame,ms,render_cpu,render_gpu,draws,alloc_kb,gc,gc_pause," + string.Join(",", keys) + "\n");
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        for (int i = 0; i < _frames.Count; i++)
        {
            Frame x = _frames[i];
            sb.Append(string.Format(inv, "{0},{1:0.000},{2:0.000},{3:0.000},{4},{5},{6},{7:0.000}", i, x.Ms, x.RenderCpu, x.RenderGpu, x.Draws, x.Alloc / 1024, x.Gc0 + x.Gc1 + x.Gc2, x.GcPause));
            foreach (string k in keys) sb.Append(string.Format(inv, ",{0:0.000}", x.Sections.GetValueOrDefault(k)));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    public static string Report(int worst = 15)
    {
        if (_frames.Count < 2) return "perf: no frames recorded (perf start, then drive)";
        var f = _frames.Skip(1).ToList();
        double Pct(IEnumerable<double> xs, double q) { var s = xs.OrderBy(x => x).ToList(); return s[Math.Min(s.Count - 1, (int)(q * s.Count))]; }
        var ms = f.Select(x => x.Ms).ToList();
        double med = Pct(ms, 0.5);
        var sb = new StringBuilder();
        sb.AppendLine($"perf: {f.Count} frames, {ms.Sum() / 1000:0.0} s");
        sb.AppendLine($"frame ms  median {med:0.00}  p90 {Pct(ms, 0.9):0.00}  p99 {Pct(ms, 0.99):0.00}  max {ms.Max():0.00}");
        sb.AppendLine($"render cpu ms  median {Pct(f.Select(x => x.RenderCpu), 0.5):0.00}  max {f.Max(x => x.RenderCpu):0.00};  gpu median {Pct(f.Select(x => x.RenderGpu), 0.5):0.00}  max {f.Max(x => x.RenderGpu):0.00};  draw calls median {Pct(f.Select(x => (double)x.Draws), 0.5):0}");
        sb.AppendLine($"alloc  {f.Average(x => x.Alloc) / 1024:0} KB/frame avg, {f.Max(x => x.Alloc) / 1024:0} KB max;  GC gen0 {f.Sum(x => x.Gc0)}  gen1 {f.Sum(x => x.Gc1)}  gen2 {f.Sum(x => x.Gc2)};  GC pause total {f.Sum(x => x.GcPause):0.0} ms, max/frame {f.Max(x => x.GcPause):0.0} ms");
        int spikes = f.Count(x => x.Ms > Math.Max(med * 1.5, med + 6));
        sb.AppendLine($"hitches (> 1.5x median): {spikes}");
        sb.AppendLine("sections (ms/frame)    avg     p99     max");
        foreach (string k in f.SelectMany(x => x.Sections.Keys).Distinct().OrderBy(k => k))
        {
            var v = f.Select(x => x.Sections.GetValueOrDefault(k)).ToList();
            sb.AppendLine($"  {k,-20} {v.Average(),6:0.000}  {Pct(v, 0.99),6:0.000}  {v.Max(),6:0.000}");
        }
        sb.AppendLine($"worst {worst} frames:");
        foreach (var (x, i) in f.Select((x, i) => (x, i)).OrderByDescending(t => t.x.Ms).Take(worst))
        {
            string secs = string.Join(" ", x.Sections.Where(s => s.Value > 0.3).OrderByDescending(s => s.Value).Select(s => $"{s.Key}={s.Value:0.0}"));
            string gc = x.Gc0 + x.Gc1 + x.Gc2 > 0 ? $" GC{(x.Gc2 > 0 ? 2 : x.Gc1 > 0 ? 1 : 0)} pause {x.GcPause:0.0}" : "";
            sb.AppendLine($"  #{i,-5} {x.Ms,6:0.0} ms  render cpu {x.RenderCpu:0.0} gpu {x.RenderGpu:0.0}  alloc {x.Alloc / 1024} KB{gc}  {secs}");
        }
        return sb.ToString();
    }

    /// <summary>Closes each frame; processes last so it sees everything that frame did.</summary>
    public partial class Probe : Node
    {
        public override void _Ready() => ProcessPriority = int.MaxValue;
        public override void _Process(double delta) { if (On) EndFrame(delta, GetViewport()); }
    }
}
