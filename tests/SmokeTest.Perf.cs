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
    }
}
