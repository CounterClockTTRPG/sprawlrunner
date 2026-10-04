using System;
using System.Collections.Generic;
using Godot;

namespace Sprawl;

/// <summary>
/// The frame every panel and button is drawn in: hardware that has been
/// carried around. Two corners cut at 45 degrees (top-left, bottom-right),
/// a dirt-and-scratches texture worked into the fill, bracket marks on the
/// square corners, a row of ticks, and optionally hazard stripes along the top
/// for anything that is a warning.
/// </summary>
///
/// <remarks>
/// A <see cref="StyleBox"/> so the Godot theme uses it for every Control, and
/// <see cref="UITheme.DrawPanel"/> draws the same box for the hand-drawn HUDs,
/// so the two never drift apart. The grime is one procedural texture, made
/// once; each panel picks its own patch of it from its size, so the dirt stays
/// put on a panel as it scrolls and two panels are never dirty the same way.
/// </remarks>
public partial class GrimeBox : StyleBox
{
    public Color Fill = UITheme.PanelFill;
    public Color Edge = UITheme.PanelEdge;
    /// <summary>Length of the two cut corners, in pixels.</summary>
    public float Chamfer = 10;
    /// <summary>How much of the dirt texture shows, 0..1.</summary>
    public float Grime = 1;
    public float EdgeWidth = 1;
    public bool Brackets = true, Ticks = true, Hazard;

    public GrimeBox() { }

    public GrimeBox(Color fill, Color edge, float chamfer = 10, float grime = 1, int margin = 12, int vmargin = 6)
    {
        Fill = fill;
        Edge = edge;
        Chamfer = chamfer;
        Grime = grime;
        ContentMarginLeft = ContentMarginRight = margin;
        ContentMarginTop = ContentMarginBottom = vmargin;
    }

    public GrimeBox With(Action<GrimeBox> change)
    {
        var b = new GrimeBox(Fill, Edge, Chamfer, Grime)
        {
            ContentMarginLeft = ContentMarginLeft, ContentMarginRight = ContentMarginRight,
            ContentMarginTop = ContentMarginTop, ContentMarginBottom = ContentMarginBottom,
        };
        (b.Fill, b.Edge, b.Chamfer, b.Grime, b.EdgeWidth, b.Brackets, b.Ticks, b.Hazard) = (Fill, Edge, Chamfer, Grime, EdgeWidth, Brackets, Ticks, Hazard);
        change(b);
        return b;
    }

    public override void _Draw(Rid ci, Rect2 r)
    {
        if (r.Size.X < 4 || r.Size.Y < 4) return;
        float c = MathF.Min(Chamfer, MathF.Min(r.Size.X, r.Size.Y) * 0.3f);
        float x0 = r.Position.X, y0 = r.Position.Y, x1 = r.End.X, y1 = r.End.Y;
        Vector2[] shape =
        {
            new(x0 + c, y0), new(x1, y0), new(x1, y1 - c), new(x1 - c, y1), new(x0, y1), new(x0, y1 - c > y0 + c ? y0 + c : y0),
        };

        if (Fill.A > 0.01f)
        {
            RenderingServer.CanvasItemAddPolygon(ci, shape, new[] { Fill });
            if (Grime > 0.01f)
            {
                // A patch of the dirt picked by the panel's size, so it is fixed to the panel.
                float span = MathF.Max(GrimeSize, MathF.Max(r.Size.X, r.Size.Y));
                var off = new Vector2(Hash(r.Size.X, 1) * 0.5f, Hash(r.Size.Y, 2) * 0.5f);
                var uv = new Vector2[shape.Length];
                for (int i = 0; i < shape.Length; i++) uv[i] = off + (shape[i] - r.Position) / span * 0.5f;
                RenderingServer.CanvasItemAddPolygon(ci, shape, new[] { new Color(1, 1, 1, Grime) }, uv, GrimeTexture.GetRid());
            }
        }
        if (Hazard) Stripes(ci, x0 + c, y0, x1, MathF.Min(7, r.Size.Y * 0.2f));
        if (Edge.A > 0.01f)
        {
            var loop = new Vector2[shape.Length + 1];
            Array.Copy(shape, loop, shape.Length);
            loop[^1] = shape[0];
            RenderingServer.CanvasItemAddPolyline(ci, loop, new[] { Edge }, EdgeWidth);
            Color hi = Edge.Lightened(0.25f) with { A = MathF.Min(1, Edge.A * 1.3f) };
            if (Brackets && r.Size.X > 40 && r.Size.Y > 24)
            {
                // Heavier brackets on the two square corners.
                float b = MathF.Min(12, r.Size.Y * 0.3f);
                RenderingServer.CanvasItemAddPolyline(ci, new[] { new Vector2(x1 - b, y0 + 1), new Vector2(x1 - 1, y0 + 1), new Vector2(x1 - 1, y0 + b) }, new[] { hi }, 2);
                RenderingServer.CanvasItemAddPolyline(ci, new[] { new Vector2(x0 + 1, y1 - b), new Vector2(x0 + 1, y1 - 1), new Vector2(x0 + b, y1 - 1) }, new[] { hi }, 2);
            }
            if (Ticks && r.Size.X > 160)
                for (int k = 0; k < 4; k++)
                {
                    float tx = x1 - 22 - k * 6;
                    RenderingServer.CanvasItemAddLine(ci, new Vector2(tx, y1 - 4), new Vector2(tx, y1 - (k == 0 ? 9 : 6)), Edge with { A = Edge.A * 0.7f }, 1);
                }
        }
    }

    /// <summary>Yellow-and-black warning stripes in a band along the top.</summary>
    private void Stripes(Rid ci, float xa, float y, float xb, float h)
    {
        var dark = new Color(0.03f, 0.025f, 0.02f, 0.9f);
        var lit = Edge with { A = MathF.Min(1, Edge.A) * 0.75f };
        RenderingServer.CanvasItemAddRect(ci, new Rect2(xa, y + 1, xb - xa - 1, h), dark);
        for (float x = xa; x < xb; x += 14)
        {
            float a = M.Clamp(x, xa, xb - 1), b = M.Clamp(x + 7, xa, xb - 1), a2 = M.Clamp(x + h, xa, xb - 1), b2 = M.Clamp(x + 7 + h, xa, xb - 1);
            if (b - a < 0.5f && b2 - a2 < 0.5f) continue;
            RenderingServer.CanvasItemAddPolygon(ci, new[] { new Vector2(a2, y + 1), new Vector2(b2, y + 1), new Vector2(b, y + 1 + h), new Vector2(a, y + 1 + h) }, new[] { lit });
        }
    }

    private static float Hash(float v, int salt)
    {
        uint h = (uint)(MathF.Round(v) * 73856093) ^ (uint)(salt * 19349663);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return (h & 0xffff) / 65535f;
    }

    // ---- the dirt -------------------------------------------------------------------------

    public const int GrimeSize = 512;
    private static Texture2D _grime;

    /// <summary>
    /// Dirt, stains, scratches and specks on a transparent ground, tileable
    /// enough to be sampled anywhere. Dark where it is dirt and pale where it
    /// is a scratch through the paint; alpha is how much of either.
    /// </summary>
    public static Texture2D GrimeTexture => _grime ??= MakeGrime(GrimeSize, 1337);

    public static Texture2D MakeGrime(int n, int seed)
    {
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        var stain = new FastNoiseLite { Seed = seed, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.006f, FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 5 };
        var blot = new FastNoiseLite { Seed = seed + 1, NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 0.02f, CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance2Sub };
        var rng = new Random(seed);
        var a = new float[n * n];
        var light = new float[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float s = stain.GetNoise2D(x, y) * 0.5f + 0.5f;
            float b = blot.GetNoise2D(x, y) * 0.5f + 0.5f;
            float dirt = MathF.Max(0, s - 0.45f) * 0.55f + MathF.Max(0, 0.18f - b) * 0.9f;
            if (rng.NextDouble() < 0.012) dirt += 0.35f; // specks
            a[y * n + x] = dirt;
        }
        // Scratches: short straight scores through the paint, mostly near-horizontal.
        for (int k = 0; k < 90; k++)
        {
            float x = rng.Next(n), y = rng.Next(n), ang = (float)(rng.NextDouble() - 0.5) * 1.1f + (rng.NextDouble() < 0.3 ? MathF.PI / 2 : 0);
            int len = 10 + rng.Next(70);
            float str = 0.25f + (float)rng.NextDouble() * 0.35f;
            for (int i = 0; i < len; i++)
            {
                int px = ((int)(x + MathF.Cos(ang) * i) % n + n) % n, py = ((int)(y + MathF.Sin(ang) * i) % n + n) % n;
                light[py * n + px] = MathF.Max(light[py * n + px], str * (1 - MathF.Abs(i - len / 2f) / len));
            }
        }
        for (int i = 0; i < n * n; i++)
        {
            float l = light[i], d = MathF.Min(0.6f, a[i]);
            img.SetPixel(i % n, i / n, l > d ? new Color(0.85f, 0.8f, 0.7f, l * 0.35f) : new Color(0.02f, 0.015f, 0.01f, d));
        }
        return ImageTexture.CreateFromImage(img);
    }

    // ---- for hand-drawn HUDs ---------------------------------------------------------------

    private static readonly Dictionary<(Color, Color, bool, float), GrimeBox> _cache = new();

    /// <summary>A cached box for a fill/edge pair, so a HUD drawing every frame allocates nothing.</summary>
    public static GrimeBox For(Color fill, Color edge, bool hazard, float width)
    {
        var key = (fill, edge, hazard, width);
        if (!_cache.TryGetValue(key, out GrimeBox b))
            _cache[key] = b = new GrimeBox(fill, edge, 9, 0.8f) { Hazard = hazard, EdgeWidth = width };
        return b;
    }
}
