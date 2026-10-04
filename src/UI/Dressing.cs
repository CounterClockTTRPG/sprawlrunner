using System;
using Godot;

namespace Sprawl;

/// <summary>
/// The props that make a screen an object in the world: rubber stamps,
/// barcodes, a taped-up photo, paper price tags, punched loyalty cards,
/// thermal-printer paper, the glass of an old CRT and the casing of a cheap
/// commlink. All drawn in code; each takes a seed so the same thing always
/// looks worn the same way.
/// </summary>
public static class Dressing
{
    public static readonly Color Paper = new(0.83f, 0.79f, 0.68f);
    public static readonly Color Ink = new(0.13f, 0.11f, 0.09f);
    public static readonly Color InkRed = new(0.66f, 0.1f, 0.08f);
    public static readonly Color InkGreen = new(0.13f, 0.42f, 0.2f);
    public static readonly Color InkBlue = new(0.12f, 0.22f, 0.5f);
    public static readonly Color Plastic = new(0.085f, 0.08f, 0.072f);

    public static float Hash(int seed, int salt) => Art.Hash(seed, salt, 911);

    /// <summary>A strip of masking tape, centred on <paramref name="at"/>.</summary>
    public static void Tape(CanvasItem ci, Vector2 at, float angle, float len = 46, float wid = 14)
    {
        Vector2 d = new(MathF.Cos(angle), MathF.Sin(angle)), n = new(-d.Y, d.X);
        var c = new Color(0.86f, 0.8f, 0.6f, 0.72f);
        Vector2 a = at - d * len / 2, b = at + d * len / 2;
        ci.DrawColoredPolygon(new[] { a - n * wid / 2, b - n * wid / 2 + d * 2, b + n * wid / 2, a + n * wid / 2 - d * 2 }, c);
    }

    /// <summary>A barcode from a seed, filling <paramref name="r"/>.</summary>
    public static void Barcode(CanvasItem ci, Rect2 r, int seed, Color c)
    {
        float x = r.Position.X;
        for (int i = 0; x < r.End.X; i++)
        {
            float w = 1 + MathF.Floor(Hash(seed, i) * 3.2f);
            if (i % 2 == 0) ci.DrawRect(new Rect2(x, r.Position.Y, MathF.Min(w, r.End.X - x), r.Size.Y), c);
            x += w;
        }
    }

    /// <summary>Text a rubber stamp would leave: a box, slightly turned, with gaps in the ink.</summary>
    public static void StampAt(CanvasItem ci, Vector2 centre, string text, int size, Color ink, float angle, Color ground)
    {
        Font f = UITheme.Stamp;
        Vector2 ts = f.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        var box = new Rect2(-ts.X / 2 - 12, -ts.Y / 2 - 4, ts.X + 24, ts.Y + 8);
        ci.DrawSetTransform(centre, angle);
        ci.DrawRect(box, ink with { A = ink.A * 0.85f }, false, 3);
        ci.DrawRect(box.Grow(-5), ink with { A = ink.A * 0.6f }, false, 1);
        ci.DrawString(f, new Vector2(-ts.X / 2, ts.Y / 2 - f.GetDescent(size) + 1), text, HorizontalAlignment.Left, -1, size, ink with { A = ink.A * 0.85f });
        // Where the stamp did not quite meet the surface.
        Texture2D g = GrimeBox.GrimeTexture;
        ci.DrawTextureRectRegion(g, box.Grow(2), new Rect2(Hash(text.Length, 4) * 200, Hash(size, 5) * 200, box.Size.X * 0.6f, box.Size.Y * 0.6f), ground with { A = 1 });
        ci.DrawSetTransform(Vector2.Zero, 0);
    }
}

/// <summary>A stamp as a control: draws centred on its own rect, offset by <see cref="Shift"/>, so it can sit over something else.</summary>
public partial class Stamp : Control
{
    public string Text = "";
    public int FontSize = 30;
    public Color Ink = Dressing.InkRed;
    public Color Ground = UITheme.PanelFill;
    public float Angle = -0.12f;
    /// <summary>Where it lands, as a fraction of the control's rect (centre by default).</summary>
    public Vector2 At = new(0.5f, 0.5f);

    public Stamp() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw() => Dressing.StampAt(this, Size * At, Text, FontSize, Ink, Angle, Ground);
}

/// <summary>A barcode as a control.</summary>
public partial class Barcode : Control
{
    public int Seed;
    public Color Colour = UITheme.Dim;
    public Barcode() => MouseFilter = MouseFilterEnum.Ignore;
    public override void _Draw() => Dressing.Barcode(this, new Rect2(Vector2.Zero, Size), Seed, Colour);
}

/// <summary>A photo print taped to the page: a pale border, the picture cropped to fit, tape on two corners.</summary>
public partial class Photo : Control
{
    public Texture2D Texture;
    public int Seed;

    public Photo()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        float tilt = (Dressing.Hash(Seed, 1) - 0.5f) * 0.018f;
        DrawSetTransform(Size / 2, tilt);
        var at = new Rect2(-Size / 2, Size);
        DrawRect(at.Grow(3), new Color(0, 0, 0, 0.5f));
        DrawRect(at, new Color(0.78f, 0.75f, 0.68f));
        Rect2 pic = at.Grow(-6);
        if (Texture != null) Portrait.DrawCover(this, Texture, pic, 0.5f);
        DrawTextureRectRegion(GrimeBox.GrimeTexture, at, new Rect2(Dressing.Hash(Seed, 2) * 300, 0, 200, 200 * Size.Y / Size.X), new Color(1, 1, 1, 0.55f));
        DrawSetTransform(Vector2.Zero, 0);
        Dressing.Tape(this, r.Position + new Vector2(16, 6), -0.6f);
        Dressing.Tape(this, new Vector2(r.End.X - 16, r.End.Y - 6), -0.6f);
    }
}

/// <summary>A paper price tag on a string hole, slightly askew.</summary>
public partial class PriceTag : Control
{
    public string Text = "";
    public Color Ink = Dressing.Ink;
    public int Seed;
    public bool Faded;

    public PriceTag()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(104, 32);
    }

    public override void _Draw()
    {
        float tilt = (Dressing.Hash(Seed, 3) - 0.5f) * 0.09f;
        DrawSetTransform(Size / 2, tilt);
        float w = Size.X - 6, h = Size.Y - 6, x = -w / 2, y = -h / 2;
        Color paper = Faded ? Dressing.Paper.Darkened(0.45f) : Dressing.Paper;
        var shape = new[] { new Vector2(x + 12, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x + 12, y + h), new Vector2(x, y + h / 2) };
        DrawColoredPolygon(shape, paper);
        var uv = new Vector2[shape.Length];
        for (int i = 0; i < shape.Length; i++) uv[i] = (shape[i] + new Vector2(w, h)) / 400 + new Vector2(Dressing.Hash(Seed, 4) * 0.5f, 0);
        DrawPolygon(shape, new[] { new Color(1, 1, 1, 0.9f) }, uv, GrimeBox.GrimeTexture);
        DrawCircle(new Vector2(x + 9, 0), 3, new Color(0.05f, 0.04f, 0.03f));
        Font f = UITheme.Ocr;
        int fs = 16;
        Vector2 ts = f.GetStringSize(Text, HorizontalAlignment.Left, -1, fs);
        DrawString(f, new Vector2(x + 16 + (w - 18 - ts.X) / 2, ts.Y / 2 - f.GetDescent(fs)), Text, HorizontalAlignment.Left, -1, fs, Faded ? Ink with { A = 0.6f } : Ink);
        DrawSetTransform(Vector2.Zero, 0);
    }
}

/// <summary>Loyalty or trust as a punch card: a stamped square per point earned, an empty box for the rest.</summary>
public partial class Pips : Control
{
    public int Value, Max = 5;
    public Color Colour = UITheme.Yellow;

    public Pips()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(5 * 17, 18);
    }

    public override void _Draw()
    {
        for (int i = 0; i < Max; i++)
        {
            var r = new Rect2(i * 17 + 1, 3, 13, 13);
            DrawRect(r, Colour with { A = 0.55f }, false, 1);
            if (i < Value)
            {
                DrawRect(r.Grow(-2), Colour with { A = 0.9f });
                DrawLine(r.Position + new Vector2(3, 10), r.Position + new Vector2(10, 3), UITheme.PanelFill, 1);
            }
        }
    }
}

/// <summary>Thermal-printer paper: torn zigzag top and bottom, coffee stains and all.</summary>
public partial class PaperBox : StyleBox
{
    public int Seed;

    public PaperBox()
    {
        ContentMarginLeft = ContentMarginRight = 30;
        ContentMarginTop = ContentMarginBottom = 26;
    }

    public override void _Draw(Rid ci, Rect2 r)
    {
        const float tooth = 9, depth = 6;
        var pts = new System.Collections.Generic.List<Vector2>();
        int n = Math.Max(2, (int)(r.Size.X / tooth));
        float step = r.Size.X / n;
        for (int i = 0; i <= n; i++) pts.Add(new Vector2(r.Position.X + i * step, r.Position.Y + (i % 2 == 0 ? 0 : depth)));
        for (int i = n; i >= 0; i--) pts.Add(new Vector2(r.Position.X + i * step, r.End.Y - (i % 2 == 0 ? 0 : depth)));
        var shape = pts.ToArray();
        var shadow = new Vector2[shape.Length];
        for (int i = 0; i < shape.Length; i++) shadow[i] = shape[i] + new Vector2(5, 7);
        RenderingServer.CanvasItemAddPolygon(ci, shadow, new[] { new Color(0, 0, 0, 0.45f) });
        RenderingServer.CanvasItemAddPolygon(ci, shape, new[] { Dressing.Paper });
        var uv = new Vector2[shape.Length];
        float span = MathF.Max(r.Size.X, r.Size.Y);
        for (int i = 0; i < shape.Length; i++) uv[i] = (shape[i] - r.Position) / span * 0.6f + new Vector2(0.2f, 0.1f);
        RenderingServer.CanvasItemAddPolygon(ci, shape, new[] { new Color(1, 1, 1, 0.5f) }, uv, GrimeBox.GrimeTexture.GetRid());
        // The fold the printer leaves every so often.
        for (float y = r.Position.Y + 120; y < r.End.Y - 20; y += 160)
            RenderingServer.CanvasItemAddLine(ci, new Vector2(r.Position.X, y), new Vector2(r.End.X, y), new Color(0, 0, 0, 0.05f), 2);
    }
}

/// <summary>
/// The glass of an old monitor, drawn over whatever is on it: scanlines,
/// darker corners, a smear of reflection, the ghost of text burnt in, and a
/// power light and maker's badge on the bezel below.
/// </summary>
public partial class CrtGlass : Control
{
    public string BurnIn = "SHADOWNET";
    public string Badge = "KB-7  TERMINAL";
    public Color Phosphor = UITheme.Term;

    public CrtGlass() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        Vector2 s = Size;
        Font big = UITheme.Heading;
        int bs = (int)MathF.Min(140, s.X / BurnIn.Length * 1.5f);
        Vector2 bt = big.GetStringSize(BurnIn, HorizontalAlignment.Left, -1, bs);
        DrawString(big, new Vector2((s.X - bt.X) / 2, s.Y * 0.55f), BurnIn, HorizontalAlignment.Left, -1, bs, Phosphor with { A = 0.025f });
        for (float y = 0; y < s.Y; y += 3) DrawLine(new Vector2(0, y), new Vector2(s.X, y), new Color(0, 0, 0, 0.16f), 1);
        // A slow roll bar, the way a monitor beats against the mains.
        float roll = (float)(Time.GetTicksMsec() % 7000) / 7000f * (s.Y + 120) - 60;
        DrawRect(new Rect2(0, roll, s.X, 60), Phosphor with { A = 0.018f });
        for (int k = 0; k < 10; k++)
            DrawRect(new Rect2(Vector2.One * k * 3, s - Vector2.One * k * 6), new Color(0, 0, 0, 0.05f), false, 3);
        DrawColoredPolygon(new[] { new Vector2(s.X * 0.05f, 0), new Vector2(s.X * 0.32f, 0), new Vector2(s.X * 0.12f, s.Y * 0.45f), new Vector2(0, s.Y * 0.45f), new Vector2(0, s.Y * 0.1f) },
            new Color(1, 1, 1, 0.022f));
        // On the bezel, below the glass.
        Vector2 led = new(s.X - 26, s.Y + 13);
        DrawCircle(led, 3.5f, Phosphor);
        Art.DrawGlow(this, led, 14, Phosphor with { A = 0.5f });
        UITheme.Write(this, new Vector2(18, s.Y + 18), Badge, 12, new Color(0.45f, 0.42f, 0.36f), outline: 1, mono: true);
    }
}

/// <summary>The casing of a cheap commlink around a full-screen panel: scuffed plastic, status bar, a cracked corner, a repair sticker.</summary>
public partial class DeviceCasing : Control
{
    public int Seed = 7;
    public string Clock = "";
    /// <summary>How far in from the casing's edge the screen starts.</summary>
    public const float Bezel = 34, Edge = 8;

    private GrimeBox _body;

    public DeviceCasing() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        Vector2 s = Size;
        _body ??= new GrimeBox(Dressing.Plastic, new Color(0.24f, 0.22f, 0.19f), 34, 1) { Ticks = false, EdgeWidth = 2 };
        var outer = new Rect2(Edge, Edge, s.X - Edge * 2, s.Y - Edge * 2);
        _body.Draw(GetCanvasItem(), outer);
        var screen = outer.Grow(-(Bezel - Edge));
        DrawRect(screen.Grow(2), new Color(0, 0, 0, 0.8f), false, 3);
        // Status bar in the top bezel: signal, battery, network and the time.
        float y = Edge + 8;
        for (int i = 0; i < 4; i++)
            DrawRect(new Rect2(s.X - 120 + i * 7, y + 12 - i * 3, 5, 4 + i * 3), i < 3 ? UITheme.Dim : UITheme.Faint);
        DrawRect(new Rect2(s.X - 80, y + 4, 26, 12), UITheme.Dim, false, 1);
        DrawRect(new Rect2(s.X - 54, y + 7, 3, 6), UITheme.Dim);
        DrawRect(new Rect2(s.X - 78, y + 6, 14, 8), UITheme.Yellow.Darkened(0.2f));
        UITheme.Write(this, new Vector2(s.X - 330, y + 15), $"KBNET  ·  {Clock}", 13, UITheme.Dim, HorizontalAlignment.Right, 200, mono: true);
        UITheme.Write(this, new Vector2(Edge + 40, y + 16), "ZEPHYR-4 POCKETSEC", 12, new Color(0.42f, 0.4f, 0.35f), outline: 1, heading: true);
        // A crack across the top-right of the glass.
        Vector2 c0 = new(screen.End.X, screen.Position.Y + 40);
        Vector2[] crack = { c0, c0 + new Vector2(-38, 22), c0 + new Vector2(-61, 30), c0 + new Vector2(-104, 66), c0 + new Vector2(-122, 71) };
        DrawPolyline(crack, new Color(0.85f, 0.9f, 0.95f, 0.22f), 1.2f);
        DrawPolyline(new[] { crack[1], crack[1] + new Vector2(-6, 34), crack[1] + new Vector2(-17, 49) }, new Color(0.85f, 0.9f, 0.95f, 0.16f), 1);
        DrawPolyline(new[] { crack[2], crack[2] + new Vector2(-24, -6) }, new Color(0.85f, 0.9f, 0.95f, 0.14f), 1);
        // The repair shop's sticker on the bottom bezel.
        DrawSetTransform(new Vector2(Edge + 120, s.Y - Edge - 17), -0.05f);
        DrawRect(new Rect2(-62, -11, 124, 22), new Color(0.8f, 0.74f, 0.32f, 0.92f));
        DrawString(UITheme.Heading, new Vector2(-56, 5), "TORQUE'S  REPAIRS", HorizontalAlignment.Left, -1, 12, Dressing.Ink);
        DrawSetTransform(Vector2.Zero, 0);
    }
}
