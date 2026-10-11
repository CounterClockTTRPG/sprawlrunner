global using V2 = System.Numerics.Vector2;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprawl;

/// <summary>A tile coordinate.</summary>
public readonly record struct Pt(int X, int Y)
{
    public static Pt operator +(Pt a, Pt b) => new(a.X + b.X, a.Y + b.Y);
    public static Pt operator -(Pt a, Pt b) => new(a.X - b.X, a.Y - b.Y);
    public V2 Centre => new(X + 0.5f, Y + 0.5f);
    public int Chebyshev(Pt o) => Math.Max(Math.Abs(X - o.X), Math.Abs(Y - o.Y));
    public static Pt Of(V2 p) => new((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));
    public static readonly Pt[] Dirs8 = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(1, -1), new(-1, 1), new(-1, -1) };
    public static readonly Pt[] Dirs4 = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
}

/// <summary>An axis-aligned rectangle of tiles.</summary>
public readonly record struct Rect(int X, int Y, int W, int H)
{
    public int Right => X + W;
    public int Bottom => Y + H;
    public int Area => W * H;
    public Pt Centre => new(X + W / 2, Y + H / 2);
    public bool Contains(Pt p) => p.X >= X && p.Y >= Y && p.X < Right && p.Y < Bottom;
    public bool Contains(V2 p) => p.X >= X && p.Y >= Y && p.X < Right && p.Y < Bottom;
    public Rect Inset(int n) => new(X + n, Y + n, W - 2 * n, H - 2 * n);
}

/// <summary>
/// Seeded xorshift64*. Everything random in the simulation draws from one of
/// these, so a seed reproduces a city, a level or a whole run exactly.
/// </summary>
public sealed class Rng
{
    public ulong State { get; set; }

    public Rng() : this(1) { }

    public Rng(ulong seed) => State = seed == 0 ? 0x9E3779B97F4A7C15UL : seed * 0x9E3779B97F4A7C15UL + 1;

    public ulong Next()
    {
        ulong x = State;
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        State = x;
        return x * 0x2545F4914F6CDD1DUL;
    }

    /// <summary>0 &lt;= n &lt; max.</summary>
    public int Int(int max) => max <= 1 ? 0 : (int)(Next() % (ulong)max);

    /// <summary>min &lt;= n &lt;= max.</summary>
    public int Range(int min, int max) => min + Int(max - min + 1);

    public float Float() => (Next() >> 40) / (float)(1UL << 24);

    public float Range(float min, float max) => min + Float() * (max - min);

    public bool Chance(float p) => Float() < p;

    /// <summary>Roughly normal in -1..1 (sum of three uniforms).</summary>
    public float Bell() => (Float() + Float() + Float()) / 1.5f - 1f;

    public T Pick<T>(System.Collections.Generic.IReadOnlyList<T> list) => list[Int(list.Count)];

    public ulong Fork() => Next() ^ 0xD1B54A32D192ED03UL;
}

public static class M
{
    public static V2 Dir(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));
    public static float Angle(V2 v) => MathF.Atan2(v.Y, v.X);
    public static V2 Norm(V2 v) { float l = v.Length(); return l > 1e-5f ? v / l : V2.Zero; }
    public static V2 Perp(V2 v) => new(-v.Y, v.X);
    public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
    public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Signed smallest difference b - a in -pi..pi.</summary>
    public static float AngleDiff(float a, float b)
    {
        float d = (b - a) % MathF.Tau;
        if (d > MathF.PI) d -= MathF.Tau;
        if (d < -MathF.PI) d += MathF.Tau;
        return d;
    }

    /// <summary>Turn angle a towards b by at most step.</summary>
    public static float TurnTo(float a, float b, float step)
    {
        float d = AngleDiff(a, b);
        return MathF.Abs(d) <= step ? b : a + MathF.Sign(d) * step;
    }

    public static V2 Rotate(V2 v, float a)
    {
        float c = MathF.Cos(a), s = MathF.Sin(a);
        return new V2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    /// <summary>Distance from p to segment ab, and where along it (0..1).</summary>
    public static float SegDist(V2 a, V2 b, V2 p, out float t)
    {
        V2 ab = b - a;
        float l2 = ab.LengthSquared();
        t = l2 < 1e-8f ? 0 : Clamp(V2.Dot(p - a, ab) / l2, 0, 1);
        return V2.Distance(a + ab * t, p);
    }

    public static string Money(int n) => "¥" + n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Time played, to the minute: "42m", "3h 05m".</summary>
    public static string Played(double seconds)
    {
        long min = (long)Math.Max(0, seconds) / 60;
        return min < 60 ? $"{min}m" : $"{min / 60}h {min % 60:00}m";
    }
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
