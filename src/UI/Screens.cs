using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>A dialog over everything: dimmed backdrop, centred panel.</summary>
public partial class Modal : Control
{
    protected VBoxContainer Body;
    public Action Closed;
    public bool EscapeCloses = true;
    public static int Open;

    public Modal(string title, float width = 560)
    {
        UITheme.Fill(this);
        MouseFilter = MouseFilterEnum.Stop;
        Theme = UITheme.Theme;
        var dim = new ColorRect { Color = new Color(0, 0, 0.02f, 0.7f), MouseFilter = MouseFilterEnum.Stop };
        UITheme.Fill(dim);
        AddChild(dim);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        UITheme.Fill(centre);
        AddChild(centre);
        Body = UITheme.VBox(10);
        Body.CustomMinimumSize = new Vector2(width, 0);
        centre.AddChild(UITheme.Panel(Body, 22));
        if (!string.IsNullOrEmpty(title))
        {
            Body.AddChild(UITheme.Label(title, 30, UITheme.Amber, HorizontalAlignment.Center, heading: true));
            Body.AddChild(UITheme.Rule());
        }
    }

    /// <summary>Subclasses that override these must call base: <see cref="Open"/> pauses the city.</summary>
    public override void _EnterTree() { Open++; UITheme.PowerOn(this); }
    public override void _ExitTree() => Open--;
    public void Add(Control c) => Body.AddChild(c);

    public void Close()
    {
        if (!IsInsideTree() || IsQueuedForDeletion()) return;
        Sfx.Instance?.Play(Sound.UiBack);
        QueueFree();
        Closed?.Invoke();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (Controls.Back(e) && EscapeCloses)
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public static Modal Tell(Node host, string title, string body, Action done = null)
    {
        var m = new Modal(title, 520);
        m.Add(UITheme.Label(body, 18, UITheme.Text));
        var row = UITheme.HBox();
        row.Alignment = BoxContainer.AlignmentMode.Center;
        row.AddChild(UITheme.Button("Continue", () => m.Close(), 160, 40));
        m.Add(row);
        m.Closed = done;
        host.AddChild(m);
        return m;
    }

    public static Modal Confirm(Node host, string title, string body, string yes, Action onYes)
    {
        var m = new Modal(title, 520);
        m.Add(UITheme.Label(body, 18, UITheme.Text));
        var row = UITheme.HBox(12);
        row.Alignment = BoxContainer.AlignmentMode.Center;
        row.AddChild(UITheme.Button(yes, () => { m.QueueFree(); onYes(); }, 160, 40));
        row.AddChild(UITheme.Button("Cancel", () => m.Close(), 160, 40));
        m.Add(row);
        host.AddChild(m);
        return m;
    }
}

public static partial class Settings
{
    /// <summary>Rebind keys: click an action, press the new key. A gamepad works as well, always.</summary>
    public static void Keys(Node host)
    {
        var m = new Modal("CONTROLS", 640);
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 4);
        foreach (Act a in Enum.GetValues<Act>())
        {
            grid.AddChild(UITheme.Label(Controls.Names[a], 16, UITheme.Text, wrap: false));
            Act act = a;
            Button b = null;
            b = UITheme.Button(Controls.KeyName(a), () =>
            {
                b.Text = "press a key...";
                var cap = new KeyCapture { Done = k => { Profile.Current.Bindings[act] = k; Profile.Current.Save(); b.Text = Controls.KeyName(act); } };
                m.AddChild(cap);
            }, 180, 30);
            grid.AddChild(b);
        }
        m.Add(UITheme.Scroll(grid, 520));
        m.Add(UITheme.Label("Gamepad: left stick moves/steers, right stick aims, RT fires/throttles, LT throws/brakes, A dash/handbrake, X use, B medkit, Y and RB abilities, LB grenade, d-pad weapons, L3 sneak, R3 whistle, Back map, Start pause.", 13, UITheme.Dim));
        var row = UITheme.HBox();
        row.Alignment = BoxContainer.AlignmentMode.Center;
        row.AddChild(UITheme.Button("Reset to defaults", () => { Profile.Current.Bindings.Clear(); Profile.Current.Save(); m.Close(); Keys(host); }, 200, 40));
        row.AddChild(UITheme.Button("Done", () => m.Close(), 160, 40));
        m.Add(row);
        host.AddChild(m);
    }

    private partial class KeyCapture : Node
    {
        public Action<Key> Done;

        public override void _Input(InputEvent e)
        {
            if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
            GetViewport().SetInputAsHandled();
            Key key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
            Done?.Invoke(key);
            QueueFree();
        }
    }

    public static Modal Open(Node host)
    {
        var m = new Modal("SETTINGS", 480);
        Profile p = Profile.Current;
        void Slider(string name, float value, Action<float> set)
        {
            var row = UITheme.HBox(12);
            row.AddChild(UITheme.Grow(UITheme.Label(name, 18, UITheme.Text)));
            var s = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = value, CustomMinimumSize = new Vector2(220, 24) };
            s.ValueChanged += v => { set((float)v); p.Apply(); };
            row.AddChild(s);
            m.Add(row);
        }
        var sound = new CheckBox { Text = "Sound", ButtonPressed = p.SoundOn };
        sound.Toggled += on => { p.SoundOn = on; p.Apply(); };
        m.Add(sound);
        Slider("Master", p.Master, v => p.Master = v);
        Slider("Music", p.MusicVolume, v => p.MusicVolume = v);
        Slider("Effects", p.Effects, v => p.Effects = v);
        var fs = new CheckBox { Text = "Fullscreen (F11)", ButtonPressed = p.Fullscreen };
        fs.Toggled += on => { p.Fullscreen = on; p.Apply(); };
        m.Add(fs);
        // A setting with a few values: the button steps to the next one.
        void Cycle(string name, Func<string> value, Action next)
        {
            var row = UITheme.HBox(12);
            row.AddChild(UITheme.Grow(UITheme.Label(name, 18, UITheme.Text)));
            Button b = null;
            b = UITheme.Button(value(), () => { next(); p.Apply(); b.Text = value(); }, 220, 34);
            row.AddChild(b);
            m.Add(row);
        }
        Cycle("Window size", () => $"{p.WindowW} x {p.WindowH}", () =>
        {
            int k = Array.FindIndex(Profile.WindowSizes, s => s.w == p.WindowW && s.h == p.WindowH);
            (p.WindowW, p.WindowH) = Profile.WindowSizes[(k + 1) % Profile.WindowSizes.Length];
        });
        Cycle("Frame-rate cap", () => p.FpsCap == 0 ? "none" : $"{p.FpsCap} fps", () =>
            p.FpsCap = Profile.FpsCaps[(Array.IndexOf(Profile.FpsCaps, p.FpsCap) + 1) % Profile.FpsCaps.Length]);
        var vsync = new CheckBox { Text = "VSync (no tearing; off can run smoother on a slow machine)", ButtonPressed = p.VSync };
        vsync.Toggled += on => { p.VSync = on; p.Apply(); };
        m.Add(vsync);
        var pause = new CheckBox { Text = "Pause when the game window loses focus", ButtonPressed = p.AutoPause };
        pause.Toggled += on => p.AutoPause = on;
        m.Add(pause);
        var shake = new CheckBox { Text = "Screen shake", ButtonPressed = p.ScreenShake };
        shake.Toggled += on => p.ScreenShake = on;
        m.Add(shake);
        Slider("Screen grime", p.Grime, v => p.Grime = v);
        var light = new CheckBox { Text = "Light screen effects (faster on weak graphics)", ButtonPressed = p.LightScreenFx };
        light.Toggled += on => p.LightScreenFx = on;
        m.Add(light);
        Slider("Text size", (p.TextScale - 1) / 0.4f, v => p.TextScale = 1 + 0.4f * v);
        var cb = new CheckBox { Text = "Colour-blind safe faction colours", ButtonPressed = p.ColourBlind };
        cb.Toggled += on => p.ColourBlind = on;
        m.Add(cb);
        m.Add(UITheme.Label("Text size and colours apply to screens as they open.", 13, UITheme.Dim));
        var row2 = UITheme.HBox();
        row2.Alignment = BoxContainer.AlignmentMode.Center;
        row2.AddChild(UITheme.Button("Controls", () => Keys(m), 160, 40));
        row2.AddChild(UITheme.Button("Done", () => m.Close(), 160, 40));
        m.Add(row2);
        m.Closed = () => p.Save();
        host.AddChild(m);
        return m;
    }
}

/// <summary>
/// Every achievement, from the title: the ones earned (across all careers)
/// first, then the ones still open, each with what it asks of you.
/// </summary>
public static class AchievementList
{
    public static Modal Open(Node host)
    {
        var have = Profile.Current.Achieved;
        var earned = Achievements.All.Where(a => have.Contains(a.Id)).ToList();
        var open = Achievements.All.Where(a => !have.Contains(a.Id)).ToList();
        var m = new Modal("ACHIEVEMENTS", 760);
        m.Add(UITheme.Label($"{earned.Count} of {Achievements.All.Length} earned, across all your careers.", 16, UITheme.Dim));
        var list = UITheme.VBox(6);
        list.CustomMinimumSize = new Vector2(680, 0);
        void Section(string title, List<Achievements.Def> items, bool got)
        {
            list.AddChild(UITheme.Label($"{title}  ({items.Count})", 14, UITheme.Dim));
            if (items.Count == 0) list.AddChild(UITheme.Label(got ? "Nothing yet. The streets are waiting." : "Every one of them. Nobody does that.", 15, UITheme.Faint));
            foreach (var a in items)
            {
                var row = UITheme.HBox(12);
                var name = UITheme.Label($"{(got ? "✓" : "·")}  {a.Name}", 17, got ? UITheme.Yellow : UITheme.Text, wrap: false, heading: got);
                name.CustomMinimumSize = new Vector2(250, 0);
                row.AddChild(name);
                var desc = UITheme.Label(a.Desc, 15, got ? UITheme.Dim : UITheme.Faint);
                desc.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                row.AddChild(desc);
                list.AddChild(row);
            }
            list.AddChild(UITheme.Spacer(8));
        }
        Section("EARNED", earned, true);
        Section("STILL OPEN", open, false);
        var pad = new MarginContainer();
        pad.AddThemeConstantOverride("margin_right", 22);
        pad.AddChild(list);
        m.Add(UITheme.Scroll(pad, 520));
        var row2 = UITheme.HBox();
        row2.Alignment = BoxContainer.AlignmentMode.Center;
        row2.AddChild(UITheme.Button("Done", () => m.Close(), 160, 40));
        m.Add(row2);
        host.AddChild(m);
        return m;
    }
}

/// <summary>The title: a living city behind the logo.</summary>
public partial class TitleScreen : Control
{
    public Root Root;
    public CitySim Sim;
    public CityView View;
    private float _t, _dropout;
    private VBoxContainer _menu;
    private Label _logo;

    public override void _Ready()
    {
        UITheme.Fill(this);
        Theme = UITheme.Theme;
        MouseFilter = MouseFilterEnum.Ignore;
        var shade = new ColorRect { Color = new Color(0, 0, 0.03f, 0.35f), MouseFilter = MouseFilterEnum.Ignore };
        UITheme.Fill(shade);
        AddChild(shade);
        // The key art, mirrored so the runner stands clear of the menu, darkened towards the left.
        if (Assets.Picture("title-key") is Texture2D key)
        {
            var art = new TextureRect
            {
                Texture = key, FlipH = true, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = MouseFilterEnum.Ignore, TextureFilter = TextureFilterEnum.LinearWithMipmaps,
            };
            UITheme.Fill(art);
            AddChild(art);
            var fade = new TextureRect
            {
                Texture = new GradientTexture2D
                {
                    Gradient = new Gradient { Colors = new[] { new Color(0.01f, 0.008f, 0.006f, 0.88f), new Color(0.01f, 0.008f, 0.006f, 0.55f), new Color(0, 0, 0, 0) }, Offsets = new[] { 0f, 0.38f, 0.62f } },
                    FillFrom = Vector2.Zero, FillTo = new Vector2(1, 0), Width = 256, Height = 4,
                },
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore,
            };
            UITheme.Fill(fade);
            AddChild(fade);
        }
        var box = UITheme.VBox(14);
        box.Position = new Vector2(110, 150);
        box.CustomMinimumSize = new Vector2(520, 0);
        AddChild(box);
        box.AddChild(_logo = UITheme.Label("SPRAWLRUNNER", 76, UITheme.Magenta, heading: true, wrap: false));
        box.AddChild(UITheme.Label("Kessler Bay never sleeps. Neither do you.", 22, UITheme.Cyan));
        box.AddChild(UITheme.Spacer(20));
        _menu = UITheme.VBox(10);
        // One width for every row: a slot button and its "New here" span the same as Settings below.
        _menu.CustomMinimumSize = new Vector2(540, 0);
        box.AddChild(_menu);
        Build();
        var foot = UITheme.Label("WASD drive / move   Mouse aim & fire   E enter / use   Esc menu   F11 fullscreen", 15, UITheme.Dim, wrap: false);
        foot.SetAnchorsPreset(LayoutPreset.BottomLeft);
        foot.Position = new Vector2(110, 850);
        AddChild(foot);
        if (Profile.Current.Legacy.Count > 0)
        {
            var leg = UITheme.Label("Legacy: " + string.Join("  ·  ", Profile.LegacyPerks.Where(p => Profile.Current.Legacy.Contains(p.id)).Select(p => p.name)), 15, UITheme.Violet, wrap: false);
            leg.Position = new Vector2(110, 796);
            AddChild(leg);
        }
        if (Profile.Current.BestJobs > 0)
        {
            var best = UITheme.Label($"Best career: {Profile.Current.BestJobs} jobs, {Profile.Current.BestDays} days", 15, UITheme.Dim, wrap: false);
            best.Position = new Vector2(110, 822);
            AddChild(best);
        }
    }

    private void Build()
    {
        foreach (Node n in _menu.GetChildren()) n.QueueFree();
        // The slots: continue a career, or start one in an empty slot (or over one, after a warning).
        var names = new string[Profile.Slots + 1];
        for (int s = 1; s <= Profile.Slots; s++)
        {
            int slot = s;
            var row = UITheme.HBox(8);
            row.Name = $"slot{slot}";
            if (Profile.HasSlot(slot))
            {
                Career c = Profile.LoadCareer(slot);
                names[slot] = c != null ? $"{c.Name} (day {c.Day})" : "the career there";
                string label = c != null
                    ? $"Continue -- {c.Name}, day {c.Day}\n{c.ArchetypeName}   ·   {M.Played(c.PlaySeconds)} played   ·   {M.Money(c.Cash)}"
                    : $"Continue -- slot {slot}\n(this save could not be read)";
                var go = UITheme.Button(label, () => { if (!Root.Continue(slot)) Modal.Tell(this, "NO SIGNAL", "That career could not be loaded."); }, 420, 58);
                if (slot == Profile.Slot && Profile.Slots > 1) go.AddThemeColorOverride("font_color", UITheme.Cyan);
                row.AddChild(UITheme.Grow(go));
                row.AddChild(UITheme.Button("New here", () => StartOver(slot, names[slot]), 110, 58, "Start a new career in this slot. Ironman: the one there is erased."));
            }
            else
            {
                var empty = UITheme.Button(Profile.Slots > 1 ? $"Slot {slot}  --  empty\nstart a new career here" : "No career yet\nstart a new one", () => Create(slot), 420, 58);
                empty.AddThemeColorOverride("font_color", UITheme.Dim);
                row.AddChild(UITheme.Grow(empty));
            }
            _menu.AddChild(row);
        }
        _menu.AddChild(UITheme.Button("New career", () =>
        {
            int free = Profile.FreeSlot();
            if (free > 0) Create(free);
            else StartOver(Profile.Slot, names[Profile.Slot]);
        }, 420, 42));
        int earned = Achievements.All.Count(a => Profile.Current.Achieved.Contains(a.Id));
        _menu.AddChild(UITheme.Button($"Achievements  ({earned} / {Achievements.All.Length})", () => AchievementList.Open(this), 420, 42));
        _menu.AddChild(UITheme.Button("Settings", () => Settings.Open(this), 420, 42));
        _menu.AddChild(UITheme.Button("Quit", () => GetTree().Quit(), 420, 42));
    }

    private void Create(int slot) => AddChild(new CreateScreen { Root = Root, Slot = slot });

    /// <summary>Ironman: a new career over an old one erases it, so ask first.</summary>
    private void StartOver(int slot, string who) =>
        Modal.Confirm(this, Profile.Slots > 1 ? $"NEW CAREER IN SLOT {slot}" : "NEW CAREER",
            $"Starting over erases {who ?? "the career there"} for good." + (Profile.Slots > 1 ? " The other slots are left alone." : ""),
            "Start over", () => Create(slot));

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _t += dt;
        // Drift the camera over the city with the traffic running.
        Sim.Player.Pos = new V2(City.W / 2f + MathF.Sin(_t * 0.03f) * 50, City.H / 2f + MathF.Sin(_t * 0.045f) * 30);
        Sim.Player.Vel = new V2(MathF.Cos(_t * 0.03f) * 1.5f, MathF.Cos(_t * 0.045f) * 1.35f);
        Sim.Step(dt, default);
        Sim.Player.Pos = new V2(City.W / 2f + MathF.Sin(_t * 0.03f) * 50, City.H / 2f + MathF.Sin(_t * 0.045f) * 30);
        View.Frame(dt);
        // The sign is a neon tube on its last legs: every so often it drops out for a beat.
        _dropout -= dt;
        if (ScreenFx.Flicker && _dropout < -2.5f && GD.Randf() < dt * 0.6f) _dropout = 0.06f + GD.Randf() * 0.18f;
        _logo.Modulate = _dropout > 0 && (int)(_dropout * 40) % 2 == 0 ? new Color(1, 1, 1, 0.25f) : Colors.White;
    }
}

/// <summary>The empty photo box on a fake ID: a silhouette and a note that nothing is on file.</summary>
public partial class IdPhoto : Control
{
    public IdPhoto() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        Vector2 s = Size;
        DrawRect(new Rect2(Vector2.Zero, s), new Color(0.02f, 0.018f, 0.015f));
        Color sil = UITheme.Faint with { A = 0.6f };
        DrawCircle(new Vector2(s.X / 2, s.Y * 0.36f), s.X * 0.2f, sil);
        DrawColoredPolygon(new[] { new Vector2(s.X * 0.12f, s.Y), new Vector2(s.X * 0.22f, s.Y * 0.66f), new Vector2(s.X * 0.78f, s.Y * 0.66f), new Vector2(s.X * 0.88f, s.Y) }, sil);
        DrawRect(new Rect2(Vector2.Zero, s), UITheme.PanelEdge, false, 1);
        UITheme.Write(this, new Vector2(0, s.Y - 8), "NO IMAGE ON FILE", 10, UITheme.Dim, HorizontalAlignment.Center, s.X, outline: 1, mono: true);
    }
}

/// <summary>Name and archetype, filled in on a fake ID.</summary>
public partial class CreateScreen : Modal
{
    public Root Root;
    /// <summary>The save slot the new runner goes in (0: the slot in play).</summary>
    public int Slot;
    private string _arch = "samurai";
    private LineEdit _name;
    private Label _blurb;

    public CreateScreen() : base("FAKE ID  //  NEW RUNNER", 900) { }

    private static Label Field(string text) { var l = UITheme.Label(text, 14, UITheme.Dim, wrap: false); l.LabelSettings.Font = UITheme.Ocr; return l; }

    public override void _Ready()
    {
        // The papers you are about to buy: a photo box, the fields, a serial and a barcode.
        var card = UITheme.HBox(18);
        card.AddChild(new IdPhoto { CustomMinimumSize = new Vector2(112, 136) });
        var fields = UITheme.VBox(6);
        fields.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        fields.AddChild(Field("STREET NAME"));
        _name = new LineEdit { Text = new[] { "Wraith", "Ronin", "Kestrel", "Jinx", "Static", "Vesper", "Grit" }[GD.Randi() % 7], CustomMinimumSize = new Vector2(0, 38), MaxLength = 18 };
        fields.AddChild(_name);
        uint serial = GD.Randi();
        fields.AddChild(Field($"ID NO. KB-{serial % 90000 + 10000}-{serial / 7 % 900 + 100}   ·   ISSUED KESSLER BAY   ·   CLASS: NONE"));
        fields.AddChild(new Barcode { Seed = (int)(serial % 100000), CustomMinimumSize = new Vector2(240, 26), SizeFlagsHorizontal = SizeFlags.ShrinkBegin });
        card.AddChild(fields);
        card.AddChild(new Stamp { Text = "UNREGISTERED", FontSize = 20, CustomMinimumSize = new Vector2(190, 136), Angle = 0.18f, At = new Vector2(0.5f, 0.42f) });
        Add(card);
        Add(UITheme.Rule());
        Add(Field("ARCHETYPE"));
        var grid = new GridContainer { Columns = 3, Name = "archetypes" };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        foreach (var (id, name, blurb) in Career.Archetypes)
        {
            string k = id;
            var b = UITheme.Button(name, () => Pick(k), 280, 46);
            b.ToggleMode = true;
            b.ButtonPressed = id == _arch;
            b.Name = id;
            grid.AddChild(b);
        }
        Add(grid);
        Add(Field("WHERE YOU COME FROM"));
        var bg = new HFlowContainer();
        foreach (var (id, name, _) in Career.Backgrounds)
        {
            string k = id;
            var b = UITheme.Button(name, () => PickBackground(k), 0, 38);
            b.ToggleMode = true;
            b.ButtonPressed = id == _bg;
            b.Name = "bg_" + id;
            bg.AddChild(b);
        }
        Add(bg);
        _bgText = UITheme.Label("", 15, UITheme.Dim);
        Add(_bgText);
        var gentle = new CheckBox { Text = "Gentle: a save every morning, so dying sends you back to the start of the day (not ironman)", ButtonPressed = _gentle };
        gentle.Toggled += on => _gentle = on;
        Add(gentle);
        // The archetype's numbers on the left, what earlier careers left you on the right: side by side, so the papers fit the screen.
        var info = UITheme.HBox(18);
        _blurb = UITheme.Label("", 16, UITheme.Text);
        _blurb.CustomMinimumSize = new Vector2(0, 90);
        _blurb.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _blurb.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        info.AddChild(_blurb);
        // What earlier careers left you: each earned perk a box to tick (or not -- a mage may not want the chrome).
        // (Three to a row under the archetype, so a long legacy never pushes the button off the screen.)
        var legacy = new GridContainer { Columns = 3 };
        legacy.AddThemeConstantOverride("h_separation", 14);
        legacy.AddThemeConstantOverride("v_separation", -6);
        foreach (var (id, name, how) in Profile.LegacyPerks)
        {
            if (!Profile.Current.Legacy.Contains(id))
            {
                var locked = UITheme.Label($"· locked: {how}", 12, UITheme.Violet with { A = 0.55f }, wrap: false);
                locked.CustomMinimumSize = new Vector2(270, 26);
                locked.ClipText = true;
                locked.TooltipText = $"Locked: {how}.";
                locked.MouseFilter = MouseFilterEnum.Pass;
                legacy.AddChild(locked);
                continue;
            }
            string pid = id;
            var box = new CheckBox { Text = name, ButtonPressed = true, Name = "perk_" + id, TooltipText = "Untick to start this career without it." };
            box.AddThemeFontSizeOverride("font_size", UITheme.Scaled(12));
            box.AddThemeColorOverride("font_color", UITheme.Violet);
            box.AddThemeColorOverride("font_pressed_color", UITheme.Violet);
            box.Toggled += on => { if (on) _declined.Remove(pid); else _declined.Add(pid); };
            box.CustomMinimumSize = new Vector2(270, 26);
            box.ClipText = true;
            box.TooltipText = name + ". Untick to start this career without it.";
            legacy.AddChild(box);
        }
        Add(info);
        Add(Field("LEGACY PERKS  (what earlier careers left you -- untick any you'd rather do without)"));
        Add(legacy);
        var row = UITheme.HBox(12);
        row.Alignment = BoxContainer.AlignmentMode.Center;
        row.AddChild(UITheme.Button("Hit the streets", () => Root.NewCareer(_name.Text, _arch, _bg, _gentle, declined: _declined, slot: Slot), 220, 44));
        row.AddChild(UITheme.Button("Back", () => Close(), 140, 44));
        Add(row);
        Pick(_arch);
        PickBackground(_bg);
    }

    private string _bg = "street";
    private readonly HashSet<string> _declined = new();
    private bool _gentle;
    private Label _bgText;

    private void PickBackground(string id)
    {
        _bg = id;
        foreach (Node n in FindChildren("bg_*", "Button", true, false)) if (n is Button b) b.ButtonPressed = b.Name == "bg_" + id;
        if (_bgText != null) _bgText.Text = Career.Backgrounds.First(x => x.id == id).blurb;
    }

    private void Pick(string id)
    {
        _arch = id;
        // Chrome dims the magic: a mage starts with the implant perks unticked (tick them back if you like).
        foreach (string p in Profile.ChromePerks)
            if (FindChild("perk_" + p, true, false) is CheckBox cb) cb.ButtonPressed = id != "mage";
        foreach (Node n in Body.GetChildren())
            if (n is GridContainer g && g.Name == "archetypes")
                foreach (Node b in g.GetChildren()) if (b is Button bb) bb.ButtonPressed = bb.Name == id;
        var c = Career.New("x", id, 1);
        var a = Career.Archetypes.First(x => x.id == id);
        string attrs = string.Join("  ", Tables.AttrNames.Select((n, i) => $"{n[..3].ToUpper()} {c.Attrs[i]}"));
        string kit = string.Join(", ", c.Weapons.Select(w => Tables.Weapon(w).Name)) + (c.Cyber.Count > 0 ? "; " + string.Join(", ", c.Cyber.Select(x => Tables.CyberById(x).Name)) : "");
        _blurb.Text = $"{a.blurb}\n{attrs}\nKit: {kit}. Cash {M.Money(c.Cash)}.";
    }
}

/// <summary>After a run: what it paid and what it cost.</summary>
public partial class DebriefScreen : Control
{
    public static int Open;
    /// <summary>Gentle mode: the button wakes you up this morning instead of ending the career.</summary>
    public bool WakeUp;
    public override void _EnterTree() => Open++;
    public override void _ExitTree() => Open--;

    public DebriefScreen(Debrief d, Run run, Action done, bool wakeUp = false)
        : this(d, run.Job, run.Success, $"Time {TimeSpan.FromSeconds(run.Time):mm\\:ss}   Kills {run.Kills}   Alarm {(run.Alarm ? "raised" : "not raised")}", done, wakeUp) { }

    public DebriefScreen(Debrief d, Job job, bool success, string stats, Action done, bool wakeUp = false)
    {
        WakeUp = wakeUp;
        UITheme.Fill(this);
        Theme = UITheme.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        var dim = new ColorRect { Color = new Color(0, 0, 0.02f, 0.82f) };
        UITheme.Fill(dim);
        AddChild(dim);
        var centre = new CenterContainer();
        UITheme.Fill(centre);
        AddChild(centre);
        var column = UITheme.VBox(18);
        centre.AddChild(column);

        // The credstick transfer, as the bank's printer leaves it.
        var paper = new PanelContainer();
        paper.AddThemeStyleboxOverride("panel", new PaperBox { Seed = job.Id });
        column.AddChild(paper);
        var body = UITheme.VBox(5);
        body.CustomMinimumSize = new Vector2(560, 0);
        paper.AddChild(body);
        Label Ink(string text, int size, Color? c = null, HorizontalAlignment align = HorizontalAlignment.Left, bool wrap = true)
        {
            var l = UITheme.Label(text, size, c ?? Dressing.Ink, align, wrap: wrap, mono: true);
            l.LabelSettings.Font = UITheme.Ocr;
            body.AddChild(l);
            return l;
        }
        string Rule() => new string('-', 46);
        // The bank's header on the left, the verdict stamped beside it.
        var head = UITheme.HBox(10);
        body.AddChild(head);
        var bank = UITheme.VBox(2);
        bank.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(bank);
        Color hc = d.CareerOver && !success ? Dressing.Ink : success ? Dressing.InkGreen : Dressing.InkRed;
        head.AddChild(new Stamp { Text = d.Headline.ToUpperInvariant(), FontSize = 30, Ink = hc, Ground = Dressing.Paper, Angle = -0.14f, CustomMinimumSize = new Vector2(230, 70) });
        Ink("KESSLER BAY", 21).Reparent(bank);
        Ink("CREDIT UNION", 15).Reparent(bank);
        Ink($"CREDSTICK TRANSFER  //  REF {job.Seed % 1000000:000000}", 12, Dressing.Ink with { A = 0.7f }).Reparent(bank);
        Ink(Rule(), 14, align: HorizontalAlignment.Center, wrap: false);
        Ink(job.Title.ToUpperInvariant(), 15, align: HorizontalAlignment.Center);
        Ink(stats.ToUpperInvariant(), 13, Dressing.Ink with { A = 0.7f }, HorizontalAlignment.Center);
        Ink(Rule(), 14, align: HorizontalAlignment.Center, wrap: false);
        float delay = 0.35f;
        foreach (var (what, amount) in d.Lines)
        {
            var row = UITheme.HBox();
            var name = UITheme.Label(what.ToUpperInvariant(), 16, Dressing.Ink, wrap: false);
            name.LabelSettings.Font = UITheme.Ocr;
            row.AddChild(UITheme.Grow(name));
            bool bad = amount.StartsWith("-") || amount == "failed";
            var value = UITheme.Label(amount.ToUpperInvariant(), 16, bad ? Dressing.InkRed : Dressing.Ink, HorizontalAlignment.Right, wrap: false);
            value.LabelSettings.Font = UITheme.Ocr;
            row.AddChild(value);
            body.AddChild(row);
            if (Roll && !AgentRunner.Active) _rolls.Add((value, amount.ToUpperInvariant(), delay));
            delay += 0.18f;
        }
        Ink(Rule(), 14, align: HorizontalAlignment.Center, wrap: false);
        if (d.Quote != null) Ink($"{d.QuoteBy}: {d.Quote}", 15, Dressing.InkBlue, HorizontalAlignment.Center);
        if (d.Unlocked.Count > 0) Ink("STREET CRED: " + string.Join(", ", d.Unlocked).ToUpperInvariant(), 14, Dressing.InkGreen, HorizontalAlignment.Center);
        if (d.Epilogue.Count > 0)
        {
            if (job.Story == 4) Ink("The prototype cores burn. Iron Saint dies with them.", 15, Dressing.InkGreen, HorizontalAlignment.Center);
            foreach (string e in d.Epilogue.Take(3)) Ink(e, 13, Dressing.Ink, HorizontalAlignment.Center);
            if (d.Epilogue.Count > 3) Ink("(the rest of the story is in your PDA's journal)", 11, Dressing.Ink with { A = 0.6f }, HorizontalAlignment.Center);
        }
        else if (d.CareerOver && WakeUp)
            Ink("Gentle mode: the Sprawl lets you have this one back. You wake up this morning.", 15, Dressing.InkBlue, HorizontalAlignment.Center);
        else if (d.CareerOver)
            Ink("No TraumaLink on file. The Sprawl keeps what it takes. Your career is over.", 15, Dressing.InkRed, HorizontalAlignment.Center);
        body.AddChild(UITheme.Spacer(4));
        var code = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        code.AddChild(new Barcode { Seed = job.Id + 3, Colour = Dressing.Ink, CustomMinimumSize = new Vector2(260, 34) });
        body.AddChild(code);
        Ink("RETAIN FOR YOUR RECORDS  ·  NO REFUNDS", 11, Dressing.Ink with { A = 0.6f }, HorizontalAlignment.Center);

        var r2 = UITheme.HBox();
        r2.Alignment = BoxContainer.AlignmentMode.Center;
        r2.AddChild(UITheme.Button(d.CareerOver ? (WakeUp ? "Wake up this morning" : "Back to the title") : "Back to the streets", () => { QueueFree(); done(); }, 260, 46));
        column.AddChild(r2);
    }

    // ---- the figures counting up as the printer runs ------------------------------------

    /// <summary>Count the figures up (off in the walkthrough, so the shot shows the totals).</summary>
    public static bool Roll = true;
    private readonly System.Collections.Generic.List<(Label label, string text, float at)> _rolls = new();
    private float _t;
    private static readonly System.Text.RegularExpressions.Regex Number = new(@"\d[\d,]*");

    public override void _Process(double delta)
    {
        if (_rolls.Count == 0) return;
        _t += (float)delta;
        bool ticking = false;
        foreach (var (label, text, at) in _rolls)
        {
            float p = M.Clamp((_t - at) / 0.55f, 0, 1);
            if (p > 0 && p < 1) ticking = true;
            label.Text = p >= 1 ? text : p <= 0 ? "" : Number.Replace(text, m =>
            {
                long v = long.Parse(m.Value.Replace(",", ""));
                long now = (long)MathF.Round(v * p * p * (3 - 2 * p));
                return m.Value.Contains(',') ? now.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) : now.ToString();
            });
        }
        if (ticking && Engine.GetProcessFrames() % 4 == 0) Sfx.Instance?.Play(Sound.Key, 1.4f, 0.25f);
        if (_t > _rolls[^1].at + 0.6f) _rolls.Clear();
    }
}
