using System;
using Godot;

namespace Sprawl;

/// <summary>
/// The look of every screen: worn street hardware. Warm grimy blacks, sodium
/// amber for the frames, and neon kept for what deserves it -- cyan for the
/// Matrix and AR readouts, magenta and red for danger and the corps, violet
/// for magic. Every frame is a <see cref="GrimeBox"/>. Fonts are system fonts
/// by name with a fallback chain, so the project ships no font files.
/// </summary>
public static class UITheme
{
    /// <summary>Off-white gone slightly yellow, like an old phosphor or a receipt.</summary>
    public static readonly Color Text = new(0.9f, 0.87f, 0.78f);
    public static readonly Color Dim = new(0.6f, 0.57f, 0.5f);
    public static readonly Color Faint = new(0.4f, 0.37f, 0.33f);
    public static readonly Color Cyan = new(0.12f, 0.82f, 0.86f);
    public static readonly Color Magenta = new(0.97f, 0.18f, 0.4f);
    public static readonly Color Yellow = new(1f, 0.76f, 0.14f);
    /// <summary>Sodium streetlight: the colour of the frames.</summary>
    public static readonly Color Amber = new(0.96f, 0.6f, 0.16f);
    public static readonly Color Violet = new(0.62f, 0.45f, 1f);
    public static readonly Color Good = new(0.45f, 0.95f, 0.55f);
    public static readonly Color Bad = new(1f, 0.32f, 0.25f);
    public static readonly Color Term = new(0.2f, 1f, 0.55f);
    public static readonly Color PanelFill = new(0.055f, 0.048f, 0.04f, 0.95f);
    public static readonly Color PanelEdge = new(0.62f, 0.43f, 0.16f);

    private static Font _heading, _body, _mono, _stamp, _ocr;
    private static Godot.Theme _theme;

    /// <summary>Bahnschrift narrowed to its condensed width and spaced out: stencilled kit markings.</summary>
    public static Font Heading => _heading ??= Narrow(Make(new[] { "Bahnschrift", "Agency FB", "Segoe UI", "DejaVu Sans", "sans-serif" }, 700), 78, 700, 1);
    public static Font Body => _body ??= Make(new[] { "Bahnschrift", "Segoe UI", "DejaVu Sans", "sans-serif" }, 400);
    public static Font Mono => _mono ??= Make(new[] { "Consolas", "Cascadia Mono", "Lucida Console", "DejaVu Sans Mono", "monospace" }, 400);
    /// <summary>Rubber stamps.</summary>
    public static Font Stamp => _stamp ??= Make(new[] { "Stencil", "Impact", "Bahnschrift", "DejaVu Sans", "sans-serif" }, 700);
    /// <summary>Printed figures: receipts, tags, IDs.</summary>
    public static Font Ocr => _ocr ??= Make(new[] { "OCR A Extended", "Consolas", "DejaVu Sans Mono", "monospace" }, 400);

    /// <summary>
    /// A variable font pulled to another width (Bahnschrift runs 75..100) and
    /// letter-spaced; a font without the axis simply ignores it.
    /// </summary>
    private static Font Narrow(Font f, int width, int weight, int spacing)
    {
        TextServer ts = TextServerManager.GetPrimaryInterface();
        return new FontVariation
        {
            BaseFont = f,
            SpacingGlyph = spacing,
            VariationOpentype = new Godot.Collections.Dictionary { { ts.NameToTag("wdth"), width }, { ts.NameToTag("wght"), weight } },
        };
    }

    private static Font Make(string[] names, int weight)
    {
        var f = new SystemFont { FontNames = names, FontWeight = weight, Antialiasing = TextServer.FontAntialiasing.Gray };
        f.Fallbacks = new Godot.Collections.Array<Font> { ThemeDB.FallbackFont };
        return f;
    }

    /// <summary>
    /// A faction's colour on maps and bars: Vipers green, Halcyon cyan, Lotus
    /// magenta -- or, with the colour-blind palette, the Okabe-Ito orange,
    /// sky blue and reddish purple, which stay apart for every kind of colour
    /// blindness. Always paired with <see cref="FactionMark"/>.
    /// </summary>
    public static Color FactionInk(int f) => Profile.Current.ColourBlind
        ? f switch { 0 => new Color(0.9f, 0.62f, 0f), 1 => new Color(0.34f, 0.71f, 0.91f), _ => new Color(0.8f, 0.47f, 0.65f) }
        : f switch { 0 => new Color(0.45f, 0.85f, 0.3f), 1 => new Color(0.3f, 0.8f, 1f), _ => new Color(1f, 0.3f, 0.55f) };

    /// <summary>A shape for each faction, so colour is never the only cue.</summary>
    public static string FactionMark(int f) => f switch { 0 => "▲", 1 => "■", _ => "●" };

    public static Color Rgba(uint c) => new(((c >> 24) & 0xff) / 255f, ((c >> 16) & 0xff) / 255f, ((c >> 8) & 0xff) / 255f, (c & 0xff) / 255f);

    public static Godot.Theme Theme
    {
        get
        {
            if (_theme != null) return _theme;
            var t = new Godot.Theme { DefaultFont = Body, DefaultFontSize = 17 };
            var button = new GrimeBox(new Color(0.075f, 0.064f, 0.052f, 0.96f), PanelEdge.Darkened(0.35f), 7, 0.75f) { Brackets = false, Ticks = false };
            t.SetStylebox("normal", "Button", button);
            t.SetStylebox("hover", "Button", button.With(b => { b.Fill = new Color(0.15f, 0.1f, 0.045f, 0.98f); b.Edge = Amber; b.Brackets = true; }));
            t.SetStylebox("pressed", "Button", button.With(b => { b.Fill = new Color(0.22f, 0.05f, 0.08f, 1f); b.Edge = Magenta; b.EdgeWidth = 2; b.Brackets = true; }));
            t.SetStylebox("focus", "Button", button.With(b => { b.Fill = Colors.Transparent; b.Edge = Amber.Darkened(0.2f); }));
            t.SetStylebox("disabled", "Button", button.With(b => { b.Fill = new Color(0.05f, 0.045f, 0.04f, 0.8f); b.Edge = Faint.Darkened(0.35f); b.Grime = 1; }));
            t.SetColor("font_color", "Button", Text);
            t.SetColor("font_hover_color", "Button", Amber.Lightened(0.35f));
            t.SetColor("font_focus_color", "Button", Text);
            t.SetColor("font_pressed_color", "Button", Magenta.Lightened(0.3f));
            t.SetColor("font_disabled_color", "Button", Faint);
            t.SetFont("font", "Button", Body);
            t.SetFontSize("font_size", "Button", 17);

            t.SetStylebox("panel", "PanelContainer", Frame);
            t.SetStylebox("panel", "Panel", Frame);
            t.SetColor("font_color", "Label", Text);
            t.SetFontSize("font_size", "Label", 17);

            t.SetStylebox("slider", "HSlider", Box(new Color(0.06f, 0.1f, 0.14f), PanelEdge.Darkened(0.4f), 1, 2, 6));
            t.SetStylebox("grabber_area", "HSlider", Box(Amber.Darkened(0.3f), Amber.Darkened(0.3f), 0, 0, 6));
            t.SetStylebox("grabber_area_highlight", "HSlider", Box(Amber, Amber, 0, 0, 6));

            foreach (string type in new[] { "CheckBox", "CheckButton" })
            {
                t.SetColor("font_color", type, Text);
                t.SetColor("font_hover_color", type, Amber);
                t.SetColor("font_pressed_color", type, Text);
                t.SetColor("font_focus_color", type, Text);
                t.SetStylebox("normal", type, new StyleBoxEmpty());
                t.SetStylebox("hover", type, new StyleBoxEmpty());
                t.SetStylebox("pressed", type, new StyleBoxEmpty());
                t.SetStylebox("focus", type, new StyleBoxEmpty());
                t.SetIcon("unchecked", type, CheckIcon(false));
                t.SetIcon("checked", type, CheckIcon(true));
            }

            var field = new GrimeBox(new Color(0.025f, 0.021f, 0.017f), PanelEdge.Darkened(0.3f), 6, 0.45f) { Brackets = false, Ticks = false };
            t.SetStylebox("normal", "LineEdit", field);
            t.SetStylebox("focus", "LineEdit", field.With(b => b.Edge = Amber));
            t.SetColor("font_color", "LineEdit", Text);
            t.SetColor("caret_color", "LineEdit", Amber);

            t.SetStylebox("panel", "TooltipPanel", new GrimeBox(new Color(0.03f, 0.026f, 0.02f, 0.97f), PanelEdge, 6, 0.5f) { Brackets = false, Ticks = false });
            t.SetColor("font_color", "TooltipLabel", Text);
            t.SetFontSize("font_size", "TooltipLabel", 15);

            t.SetStylebox("scroll", "VScrollBar", Box(new Color(0.04f, 0.06f, 0.09f), new Color(0, 0, 0, 0), 0, 2, 0));
            t.SetStylebox("scroll", "VScrollBar", Box(new Color(0.06f, 0.05f, 0.04f), new Color(0, 0, 0, 0), 0, 0, 0));
            t.SetStylebox("grabber", "VScrollBar", Box(PanelEdge.Darkened(0.35f), new Color(0, 0, 0, 0), 0, 0, 0));
            t.SetStylebox("grabber_highlight", "VScrollBar", Box(Amber.Darkened(0.1f), new Color(0, 0, 0, 0), 0, 0, 0));
            _theme = t;
            return t;
        }
    }

    /// <summary>Check boxes that show on a dark panel: a cyan frame, filled when on.</summary>
    private static Texture2D CheckIcon(bool on)
    {
        const int n = 20;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            bool edge = x <= 1 || y <= 1 || x >= n - 2 || y >= n - 2;
            bool fill = on && x >= 5 && y >= 5 && x < n - 5 && y < n - 5;
            img.SetPixel(x, y, edge ? Amber.Darkened(0.1f) : fill ? Magenta : new Color(0.05f, 0.04f, 0.03f, 1));
        }
        return ImageTexture.CreateFromImage(img);
    }

    public static StyleBoxFlat Box(Color fill, Color edge, int border, int radius = 2, int height = 0)
    {
        var s = new StyleBoxFlat { BgColor = fill, BorderColor = edge, ShadowColor = new Color(edge.R, edge.G, edge.B, 0.18f), ShadowSize = border > 0 ? 3 : 0 };
        s.SetBorderWidthAll(border);
        s.SetCornerRadiusAll(radius);
        s.ContentMarginLeft = s.ContentMarginRight = 12;
        s.ContentMarginTop = s.ContentMarginBottom = 6;
        if (height > 0) s.ContentMarginTop = s.ContentMarginBottom = height / 2f;
        return s;
    }

    public static void Fill(Control c)
    {
        c.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        c.OffsetLeft = c.OffsetTop = c.OffsetRight = c.OffsetBottom = 0;
    }

    public static Label Label(string text, int size, Color colour, HorizontalAlignment align = HorizontalAlignment.Left, bool heading = false, bool wrap = true, bool mono = false)
    {
        return new Label
        {
            Text = text,
            HorizontalAlignment = align,
            AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
            LabelSettings = new LabelSettings
            {
                Font = mono ? Mono : heading ? Heading : Body,
                FontSize = Scaled(size),
                FontColor = colour,
                OutlineSize = heading ? Math.Max(2, size / 10) : 0,
                OutlineColor = new Color(0, 0, 0, 0.85f),
                ShadowSize = heading ? Math.Max(2, size / 6) : 0,
                ShadowColor = new Color(colour.R, colour.G, colour.B, 0.35f),
                ShadowOffset = Vector2.Zero,
            },
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }

    public static Button Button(string text, Action pressed, int width = 0, int height = 36, string tip = null)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(width, height), FocusMode = Control.FocusModeEnum.None, TooltipText = tip ?? "" };
        b.Pressed += () =>
        {
            Sfx.Instance?.Play(Sound.UiSelect);
            pressed?.Invoke();
        };
        b.MouseEntered += () =>
        {
            if (b.Disabled) return;
            Sfx.Instance?.Play(Sound.UiMove, 1, 0.5f);
            // A tired backlight catching up.
            if (ScreenFx.Flicker) b.CreateTween().TweenProperty(b, "modulate", Colors.White, 0.09).From(new Color(1, 1, 1, 0.55f));
        };
        return b;
    }

    /// <summary>The standard panel frame.</summary>
    public static GrimeBox Frame => _frame ??= new GrimeBox(PanelFill, PanelEdge, 12, 1);
    private static GrimeBox _frame;

    /// <summary>A panel; give it an edge colour to mark what it is, and <paramref name="hazard"/> for warnings.</summary>
    public static PanelContainer Panel(Control content, int pad = 14, Color? edge = null, bool hazard = false)
    {
        var p = new PanelContainer();
        if (edge != null || hazard) p.AddThemeStyleboxOverride("panel", Frame.With(b => { b.Edge = edge ?? PanelEdge; b.Hazard = hazard; }));
        var m = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" }) m.AddThemeConstantOverride($"margin_{side}", pad);
        m.AddChild(content);
        p.AddChild(m);
        return p;
    }

    public static VBoxContainer VBox(int sep = 8) { var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", sep); return v; }
    public static HBoxContainer HBox(int sep = 8) { var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", sep); return h; }
    public static Control Spacer(float h = 8, float w = 0) => new Control { CustomMinimumSize = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
    public static Control Grow(Control c) { c.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; return c; }

    public static ColorRect Rule(Color? c = null) => new() { Color = (c ?? PanelEdge) with { A = 0.5f }, CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore };

    public static ScrollContainer Scroll(Control content, float height)
    {
        var s = new ScrollContainer { CustomMinimumSize = new Vector2(0, height), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        s.AddChild(content);
        return s;
    }

    /// <summary>
    /// Bring a screen up the way a cheap panel lights: a stutter of half-power
    /// before it holds. Skipped when flicker is off (the walkthrough).
    /// </summary>
    public static void PowerOn(CanvasItem c)
    {
        if (!ScreenFx.Flicker) return;
        c.Modulate = new Color(1, 1, 1, 0);
        Tween t = c.CreateTween();
        t.TweenProperty(c, "modulate:a", 0.7f, 0.04);
        t.TweenProperty(c, "modulate:a", 0.15f, 0.04);
        t.TweenProperty(c, "modulate:a", 1f, 0.08);
    }

    /// <summary>Type a label out, a beat at a time (instant when flicker is off).</summary>
    public static Label TypeOut(Label l, float seconds)
    {
        if (!ScreenFx.Flicker) return l;
        l.VisibleRatio = 0;
        l.CreateTween().TweenProperty(l, "visible_ratio", 1f, seconds).SetDelay(0.15);
        return l;
    }

    /// <summary>The panel frame, for hand-drawn HUDs: the same box the themed screens use.</summary>
    public static void DrawPanel(CanvasItem ci, Rect2 r, Color? edge = null, float fillAlpha = 0.95f, bool hazard = false, float width = 1)
        => GrimeBox.For(PanelFill with { A = fillAlpha }, edge ?? PanelEdge, hazard, width).Draw(ci.GetCanvasItem(), r);

    /// <summary>
    /// A bar in lit segments, the way cheap hardware shows a level: the lit
    /// part in <paramref name="colour"/>, the rest as dark cells.
    /// </summary>
    public static void DrawSegBar(CanvasItem ci, Rect2 r, float fraction, Color colour, int segments = 20)
    {
        ci.DrawRect(r, new Color(0, 0, 0, 0.65f));
        float gap = 2, w = (r.Size.X - gap * (segments + 1)) / segments;
        float lit = M.Clamp(fraction, 0, 1) * segments;
        for (int i = 0; i < segments; i++)
        {
            var cell = new Rect2(r.Position.X + gap + i * (w + gap), r.Position.Y + 2, w, r.Size.Y - 4);
            float f = M.Clamp(lit - i, 0, 1);
            ci.DrawRect(cell, colour with { A = 0.12f });
            if (f > 0) ci.DrawRect(new Rect2(cell.Position, new Vector2(cell.Size.X * f, cell.Size.Y)), colour);
        }
    }

    /// <summary>Draw text with an outline, for custom-drawn views.</summary>
    /// <summary>A font size with the player's text-size setting applied.</summary>
    public static int Scaled(int size) => (int)MathF.Round(size * Math.Clamp(Profile.Current?.TextScale ?? 1, 1, 1.4f));

    public static void Write(CanvasItem ci, Vector2 at, string text, int size, Color colour, HorizontalAlignment align = HorizontalAlignment.Left,
        float width = -1f, bool heading = false, int outline = 0, bool mono = false)
    {
        size = Scaled(size);
        Font f = mono ? Mono : heading ? Heading : Body;
        int o = outline > 0 ? outline : Math.Max(3, size / 5);
        ci.DrawStringOutline(f, at, text, align, width, size, o, new Color(0, 0, 0, 0.85f * colour.A));
        ci.DrawString(f, at, text, align, width, size, colour);
    }
}
