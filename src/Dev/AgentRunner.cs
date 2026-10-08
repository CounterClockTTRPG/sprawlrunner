using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace Sprawl;

/// <summary>
/// Remote control for an agent playtester: <c>./play.sh --agent</c> runs the
/// real game and watches <c>agent/cmd.txt</c>; <c>scripts/agent.sh</c> writes
/// commands there and prints the reply from <c>agent/out.txt</c>. Every key
/// and click goes in through <see cref="Input.ParseInputEvent"/>, exactly as
/// a player's would. Commands are separated by ';' -- see <see cref="Help"/>.
/// </summary>
public partial class AgentRunner : Node
{
    public Root Root;
    public static bool Active;
    /// <summary>Game time only passes while a command runs (default), so thinking between commands costs nothing.</summary>
    public static bool Frozen = true;
    public static bool Busy;
    /// <summary>True when the game may advance this frame.</summary>
    public static bool Running => !Active || !Frozen || Busy;
    private string _dir;
    private string _lastId;
    private double _poll;
    private bool _busy;
    private readonly HashSet<Key> _held = new();
    private Vector2 _mouse = new(800, 450);
    /// <summary>The world point last aimed at: kept under the cursor as the camera moves.</summary>
    private V2? _aimWorld;

    private const string Help = @"commands (separate with ';'):
  state                 what is on screen, in words (and an ASCII map on a run)
  shot NAME             screenshot to agent/NAME.png
  ui                    list buttons/fields on screen
  press TEXT            click the first enabled button whose text contains TEXT
  type TEXT             type into the focused text field (then 'key Enter')
  key K [MS]            tap a key (or hold it MS ms). K: W A S D E Q F G R H M V 1 2 3 Space Shift Esc Enter Up Down Left Right (V whistles to lure)
  hold K1+K2 MS         hold several keys together for MS ms
  mouse X Y             move the mouse to screen pixel X,Y (1600x900)
  aim X Y               (run) put the mouse on world tile X,Y
  click [left|right] [MS]   press a mouse button at the mouse (hold MS ms; right = throw)
  fire MS               hold the left button for MS ms
  wait MS               let the game run
  drive PLACE           (city) autopilot to home|fixer|doc|arms|rigger|decker|fence|talismonger|market|bazaar|job, or street (an encounter) or fare (a drop-off); stops there ('drive PLACE street' also stops when an encounter appears)
  walk X Y [MS]         (run) walk to tile X,Y by the shortest path, with real WASD; stops on arrival, when spotted, or after MS (default 15000)
  sneak X Y [MS]        like walk, holding Shift
  scroll up|down [N]    mouse wheel at the mouse (lists in shops)
  freeze on|off         on (default): game time passes only while a command runs
  ui all                also list buttons scrolled out of view
  answer [N]            in a dialog (a call, a scene, mail): press the Nth open choice (default the first), else Continue/OK
  perf start|report     (dev) record frame timings from now / print the slow frames (also agent/perf.txt)
  help";

    public override void _Ready()
    {
        Active = true;
        // Stay out of the way of whoever is at the machine: no focus, clicks fall through.
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
            DisplayServer.WindowSetMousePassthrough(new[] { Vector2.Zero, new Vector2(1, 0), new Vector2(1, 1) });
        }
        _dir = ProjectSettings.GlobalizePath("res://agent");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "out.txt"), "ready\n" + Help + "\n");
        if (File.Exists(Path.Combine(_dir, "cmd.txt"))) _lastId = File.ReadAllLines(Path.Combine(_dir, "cmd.txt")).FirstOrDefault();
        AddChild(new Perf.Probe());
        GD.Print("agent: listening on " + _dir);
    }

    /// <summary>Real keyboard and mouse never reach the game in agent mode; only injected events do.</summary>
    public override void _Input(InputEvent e)
    {
        if (e.Device != Controls.AgentDevice) GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (_busy) return;
        _poll += delta;
        if (_poll < 0.1) return;
        _poll = 0;
        string path = Path.Combine(_dir, "cmd.txt");
        if (!File.Exists(path)) return;
        string[] lines;
        try { lines = File.ReadAllLines(path); } catch { return; }
        if (lines.Length < 1 || lines[0] == _lastId) return;
        _lastId = lines[0];
        string cmds = string.Join(";", lines.Skip(1));
        _ = RunCommands(_lastId, cmds);
    }

    private async Task RunCommands(string id, string cmds)
    {
        _busy = true;
        Busy = true;
        var sb = new StringBuilder();
        foreach (string raw in cmds.Split(';'))
        {
            string c = raw.Trim();
            if (c.Length == 0) continue;
            try
            {
                string r = await Do(c);
                if (!string.IsNullOrEmpty(r)) sb.AppendLine(r);
            }
            catch (Exception e) { sb.AppendLine($"error in '{c}': {e.Message} -- stopped here"); break; }
        }
        ReleaseAll();
        Busy = false;
        if (sb.Length == 0) sb.AppendLine("ok");
        // Write beside, then swap in: the reader never sees half a reply, and a locked file is retried.
        string tmp = Path.Combine(_dir, "out.tmp"), dst = Path.Combine(_dir, "out.txt");
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try { File.WriteAllText(tmp, id + "\n" + sb); File.Move(tmp, dst, true); break; }
            catch (Exception e) { if (attempt == 19) GD.PrintErr("agent: cannot write reply: " + e.Message); await Ms(100); }
        }
        _busy = false;
    }

    // ---- input ------------------------------------------------------------------------

    private static readonly Dictionary<string, Key> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["w"] = Key.W, ["a"] = Key.A, ["s"] = Key.S, ["d"] = Key.D, ["e"] = Key.E, ["q"] = Key.Q, ["f"] = Key.F, ["g"] = Key.G,
        ["r"] = Key.R, ["h"] = Key.H, ["m"] = Key.M, ["1"] = Key.Key1, ["2"] = Key.Key2, ["3"] = Key.Key3, ["space"] = Key.Space,
        ["shift"] = Key.Shift, ["esc"] = Key.Escape, ["escape"] = Key.Escape, ["enter"] = Key.Enter, ["up"] = Key.Up, ["down"] = Key.Down,
        ["left"] = Key.Left, ["right"] = Key.Right, ["v"] = Key.V, ["p"] = Key.P, ["t"] = Key.T, ["tab"] = Key.Tab, ["backspace"] = Key.Backspace,
    };

    private void KeyDown(Key k)
    {
        if (_held.Contains(k)) return;
        _held.Add(k);
        Controls.AgentKeys.Add(k);
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true, ShiftPressed = _held.Contains(Key.Shift), Device = Controls.AgentDevice });
    }

    private void KeyUp(Key k)
    {
        if (!_held.Remove(k)) return;
        Controls.AgentKeys.Remove(k);
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false, Device = Controls.AgentDevice });
    }

    private void ReleaseAll()
    {
        foreach (Key k in _held.ToList()) KeyUp(k);
        if (Controls.AgentLeft) Button(MouseButton.Left, false);
        if (Controls.AgentRight) Button(MouseButton.Right, false);
    }

    private void MoveMouse(Vector2 at)
    {
        _mouse = at;
        Controls.AgentMouse = at;
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, ButtonMask = Mask(), Device = Controls.AgentDevice });
    }

    private static MouseButtonMask Mask() =>
        (Controls.AgentLeft ? MouseButtonMask.Left : 0) | (Controls.AgentRight ? MouseButtonMask.Right : 0);

    private void Button(MouseButton b, bool down)
    {
        if (b == MouseButton.Left) Controls.AgentLeft = down;
        if (b == MouseButton.Right) Controls.AgentRight = down;
        Input.ParseInputEvent(new InputEventMouseButton { Position = _mouse, GlobalPosition = _mouse, ButtonIndex = b, Pressed = down, ButtonMask = Mask(), Device = Controls.AgentDevice });
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Ms(int ms)
    {
        ulong end = Time.GetTicksMsec() + (ulong)Math.Max(0, ms);
        do { await Frames(1); } while (Time.GetTicksMsec() < end);
    }

    // ---- commands -----------------------------------------------------------------------

    private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    private async Task<string> Do(string c)
    {
        string[] w = c.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string op = w[0].ToLowerInvariant();
        string rest = c.Length > w[0].Length ? c[w[0].Length..].Trim() : "";
        switch (op)
        {
            case "help": return Help;
            case "state": await Frames(1); return State();
            case "wait": await Ms((int)F(w[1])); return null;
            case "shot":
            {
                await Frames(2);
                string name = w.Length > 1 ? w[1] : "shot";
                Image img = GetViewport().GetTexture().GetImage();
                string p = Path.Combine(_dir, name + ".png");
                img.SavePng(p);
                return $"saved agent/{name}.png";
            }
            case "key" when w.Length > 1 && w[1].Equals("enter", StringComparison.OrdinalIgnoreCase) && GetViewport().GuiGetFocusOwner() is LineEdit le:
                le.EmitSignal(LineEdit.SignalName.TextSubmitted, le.Text);
                await Frames(3);
                return null;
            case "key":
            {
                if (!Keys.TryGetValue(w[1], out Key k)) throw new Exception($"unknown key '{w[1]}'");
                KeyDown(k);
                string why = await HoldFor(w.Length > 2 ? (int)F(w[2]) : 60);
                KeyUp(k);
                await Frames(3);
                return why;
            }
            case "hold":
            {
                var ks = new List<Key>();
                foreach (string x in w[1].Split('+'))
                    ks.Add(Keys.TryGetValue(x, out Key k) ? k : throw new Exception($"unknown key '{x}'"));
                foreach (Key k in ks) KeyDown(k);
                string why = await HoldFor(w.Length > 2 ? (int)F(w[2]) : 500);
                foreach (Key k in ks) KeyUp(k);
                await Frames(3);
                return why;
            }
            case "mouse": _aimWorld = null; MoveMouse(new Vector2(F(w[1]), F(w[2]))); await Frames(2); return null;
            case "aim":
            {
                if (Root.RunView == null) return "not on a run";
                _aimWorld = new V2(F(w[1]), F(w[2]));
                MoveMouse(WorldToScreen(_aimWorld.Value));
                await Frames(2);
                return null;
            }
            case "click":
            {
                MouseButton b = w.Length > 1 && w[1].StartsWith("r", StringComparison.OrdinalIgnoreCase) ? MouseButton.Right : MouseButton.Left;
                int ms = w.Length > 2 ? (int)F(w[2]) : 60;
                Button(b, true);
                await Ms(ms);
                Button(b, false);
                await Frames(3);
                return null;
            }
            case "fire":
            {
                Button(MouseButton.Left, true);
                string why = await HoldFor(w.Length > 1 ? (int)F(w[1]) : 300, stopOnHurt: false);
                Button(MouseButton.Left, false);
                await Frames(2);
                return why;
            }
            case "scroll":
            {
                MouseButton dir = w.Length > 1 && w[1].StartsWith("u", StringComparison.OrdinalIgnoreCase) ? MouseButton.WheelUp : MouseButton.WheelDown;
                int n = w.Length > 2 ? (int)F(w[2]) : 3;
                for (int i = 0; i < n; i++)
                {
                    Input.ParseInputEvent(new InputEventMouseButton { Position = _mouse, GlobalPosition = _mouse, ButtonIndex = dir, Pressed = true, Factor = 1, Device = Controls.AgentDevice });
                    Input.ParseInputEvent(new InputEventMouseButton { Position = _mouse, GlobalPosition = _mouse, ButtonIndex = dir, Pressed = false, Factor = 1, Device = Controls.AgentDevice });
                    await Frames(2);
                }
                return null;
            }
            case "freeze":
                Frozen = w.Length < 2 || !w[1].Equals("off", StringComparison.OrdinalIgnoreCase);
                return Frozen ? "frozen: the game only runs while a command runs" : "unfrozen: the game runs in real time";
            case "ui": return ListUi(w.Length > 1 && w[1] == "all");
            case "press": return await PressButton(rest);
            case "answer": return await Answer(w.Length > 1 ? (int)F(w[1]) : 1);
            case "type":
                foreach (char ch in rest)
                {
                    Input.ParseInputEvent(new InputEventKey { Pressed = true, Unicode = ch, Keycode = Key.None, Device = Controls.AgentDevice });
                    Input.ParseInputEvent(new InputEventKey { Pressed = false, Unicode = ch, Keycode = Key.None, Device = Controls.AgentDevice });
                    await Frames(1);
                }
                return null;
            case "drive": return await Drive(w.Length > 1 ? w[1] : "job", w.Length > 2 && w[2] == "street");
            case "walk": return await Walk(new V2(F(w[1]), F(w[2])), w.Length > 3 ? (int)F(w[3]) : 15000, false);
            case "sneak": return await Walk(new V2(F(w[1]), F(w[2])), w.Length > 3 ? (int)F(w[3]) : 15000, true);
            case "perf" when w.Length > 1 && w[1] == "start": Perf.Start(GetViewport()); return "perf: recording";
            case "perf" when w.Length > 1 && w[1] == "bench":
            {
                // How long the screens and views take just to construct (3 times each), and whether a GC landed.
                var sb = new StringBuilder();
                foreach (var (name, make) in new (string, Func<Node>)[] { ("RunScreen", () => new RunScreen()), ("CityScreen", () => new CityScreen()), ("RunView", () => new RunView()), ("CityView", () => new CityView()) })
                for (int k = 0; k < 3; k++)
                {
                    int g = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    Node n = make();
                    double ms = sw.Elapsed.TotalMilliseconds;
                    n.Free();
                    sb.AppendLine($"  new {name,-10} {ms,7:0.00} ms{(GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2) > g ? "  (GC)" : "")}");
                }
                return sb.ToString();
            }
            case "perf" when w.Length > 2 && w[1] == "grime":
                if (ScreenFx.Instance == null) return "no ScreenFx";
                ScreenFx.Instance.Force = w[2] switch { "off" => 0, "light" => 1, "full" => 2, _ => -1 };
                return $"grime forced {w[2]}";
            case "perf" when w.Length > 2 && w[1] == "toggle":
            {
                // Hide or show a named layer (Lights, Fog, Glow, Entities, Top, TileLife, Decals, Fx, RunHud; Grime is the screen shader).
                if (w[2].Equals("grime", StringComparison.OrdinalIgnoreCase))
                {
                    var fx = GetTree().Root.FindChild("ScreenFx", true, false) as CanvasLayer;
                    if (fx == null) return "no ScreenFx";
                    fx.Visible = !fx.Visible;
                    return $"grime {(fx.Visible ? "shown" : "hidden")}";
                }
                if (GetTree().Root.FindChild(w[2], true, false) is not CanvasItem ci) return $"no layer '{w[2]}'";
                ci.Visible = !ci.Visible;
                return $"{w[2]} {(ci.Visible ? "shown" : "hidden")}";
            }
            case "perf":
            {
                string rep = Perf.Report();
                File.WriteAllText(Path.Combine(_dir, "perf.txt"), rep);
                File.WriteAllText(Path.Combine(_dir, "perf.csv"), Perf.Csv());
                return rep;
            }
            case "quit": GetTree().Quit(); return "bye";
        }
        return $"unknown command '{op}' (try 'help')";
    }

    private int HpNow() => Root.RunScreen?.R?.Player is Actor p ? (int)p.Hp : -1;

    /// <summary>Hold for ms; on a run, stop early if you get hurt (and say so).</summary>
    private async Task<string> HoldFor(int ms, bool stopOnHurt = true)
    {
        int hp = HpNow();
        ulong end = Time.GetTicksMsec() + (ulong)Math.Max(0, ms);
        while (Time.GetTicksMsec() < end)
        {
            await Frames(1);
            if (_aimWorld is V2 aw && Root.RunView != null) MoveMouse(WorldToScreen(aw));
            int now = HpNow();
            if (Root.RunScreen?.R?.Phase == RunPhase.Dead) return "you died";
            if (stopOnHurt && hp >= 0 && now >= 0 && now < hp)
                return $"interrupted: took {hp - now} damage (hp {now})";
        }
        int end2 = HpNow();
        return hp >= 0 && end2 >= 0 && end2 < hp ? $"(took {hp - end2} damage meanwhile, hp {end2})" : null;
    }

    private Vector2 WorldToScreen(V2 tile) => Root.RunView.GetGlobalTransformWithCanvas() * Art.G(tile, RunView.T);

    // ---- UI -----------------------------------------------------------------------------

    private IEnumerable<Control> Visible(Node n)
    {
        foreach (Node c in n.GetChildren())
        {
            if (c is CanvasItem ci && !ci.Visible) continue;
            if (c is Control ctl) yield return ctl;
            foreach (Control d in Visible(c)) yield return d;
        }
    }

    private List<BaseButton> Buttons(bool all = false)
    {
        var rect = GetViewport().GetVisibleRect();
        // Only what is on top: if a modal or place screen is open, only its controls.
        Node top = TopLayer();
        return Visible(top).OfType<BaseButton>().Where(b => b.IsVisibleInTree() && (all || rect.Intersects(b.GetGlobalRect()) && ClipVisible(b))).ToList();
    }

    private static bool ClipVisible(Control c)
    {
        Rect2 r = c.GetGlobalRect();
        for (Node p = c.GetParent(); p != null; p = p.GetParent())
            if (p is ScrollContainer sc && !sc.GetGlobalRect().Intersects(r)) return false;
        return true;
    }

    private Node TopLayer()
    {
        Node ui = Root.GetNodeOrNull("UI") ?? Root;
        Node best = ui;
        void Scan(Node n)
        {
            foreach (Node c in n.GetChildren())
            {
                if (c is Modal or PlaceScreen or DebriefScreen or CreateScreen or Pda) best = c;
                Scan(c);
            }
        }
        Scan(ui);
        return best;
    }

    private string ListUi(bool all)
    {
        var sb = new StringBuilder();
        foreach (BaseButton b in Buttons(all))
        {
            string text = b is Button bt ? bt.Text : b.Name;
            Vector2 c = b.GetGlobalRect().GetCenter();
            string extra = b.Disabled ? " (disabled" + (string.IsNullOrEmpty(b.TooltipText) ? "" : ": " + b.TooltipText) + ")" : "";
            if (b.ToggleMode && b.ButtonPressed) extra += " [selected]";
            if (all && !ClipVisible(b)) extra += " (scrolled out of view: 'scroll down' over the list)";
            sb.AppendLine($"  [{text}] at {c.X:0},{c.Y:0}{extra}");
        }
        foreach (LineEdit l in Visible(TopLayer()).OfType<LineEdit>().Where(l => l.IsVisibleInTree()))
            sb.AppendLine($"  text field at {l.GetGlobalRect().GetCenter().X:0},{l.GetGlobalRect().GetCenter().Y:0}: '{l.Text}'{(l.HasFocus() ? " (focused)" : "")}");
        return sb.Length == 0 ? "no buttons on screen" : sb.ToString().TrimEnd();
    }

    /// <summary>The scripted playtester's way through a dialog: the Nth open choice, or whatever closes it.</summary>
    private async Task<string> Answer(int n)
    {
        if (TopLayer() is not Modal) return "no dialog open";
        string[] closers = { "Later", "Call back later", "Hang up (answer later)", "Continue", "Hang up", "OK", "Close", "The end" };
        var open = Buttons().OfType<Button>().Where(b => !b.Disabled && !closers.Contains(b.Text)).ToList();
        Button pick = open.Count > 0 ? open[Math.Clamp(n - 1, 0, open.Count - 1)]
            : Buttons().OfType<Button>().FirstOrDefault(b => !b.Disabled && closers.Skip(3).Contains(b.Text));
        if (pick == null) return "nothing to press";
        return await PressButton(pick.Text);
    }

    private async Task<string> PressButton(string text)
    {
        BaseButton b = Buttons().FirstOrDefault(x => x is Button bt && !x.Disabled && bt.Text.Equals(text, StringComparison.OrdinalIgnoreCase))
            ?? Buttons().FirstOrDefault(x => x is Button bt && !x.Disabled && bt.Text.Contains(text, StringComparison.OrdinalIgnoreCase));
        if (b == null)
        {
            BaseButton dis = Buttons().FirstOrDefault(x => x is Button bt && bt.Text.Contains(text, StringComparison.OrdinalIgnoreCase));
            return dis != null ? $"button '{((Button)dis).Text}' is disabled{(string.IsNullOrEmpty(dis.TooltipText) ? "" : ": " + dis.TooltipText)}" : $"no button matching '{text}'";
        }
        string label = ((Button)b).Text;
        MoveMouse(b.GetGlobalRect().GetCenter());
        await Frames(2);
        Button(MouseButton.Left, true);
        await Frames(2);
        Button(MouseButton.Left, false);
        await Frames(6);
        return $"pressed [{label}]";
    }

    // ---- state --------------------------------------------------------------------------

    private string Screen()
    {
        Node top = TopLayer();
        string over = top switch { CreateScreen => "create", Modal => "dialog", PlaceScreen => "place", DebriefScreen => "debrief", _ => null };
        string under = Root.RunScreen != null ? "run" : Root.CityScreen != null ? "city" : Root.Screen is TitleScreen ? "title" : "other";
        return over != null ? $"{over} (over {under})" : under;
    }

    private string State()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"screen: {Screen()}");
        Node top = TopLayer();
        if (top is Modal or DebriefScreen or CreateScreen)
        {
            foreach (Label l in Visible(top).OfType<Label>().Where(l => l.IsVisibleInTree() && l.Text.Length > 0).Take(30)) sb.AppendLine("  | " + l.Text.Replace("\n", " / "));
        }
        Career c = Root.Career;
        if (c != null && Root.RunScreen == null)
            sb.AppendLine($"career: {c.Name} ({c.ArchetypeName}) day {c.Day}, {M.Money(c.Cash)}, karma {c.Karma}, rep {c.Rep}, TraumaLink {c.TraumaLinks}, jobs {c.JobsDone} done/{c.JobsFailed} failed, loot items {c.Loot.Count}");
        if (c != null && Root.RunScreen == null && c.Inbox.Count > 0)
            sb.AppendLine($"inbox: {c.Unread} unread, {c.Inbox.Count(c.Waiting)} waiting on an answer (PDA 'Inbox' tab with P, or 'mail' at the terminal){(c.Today.Any() ? " | today: " + string.Join("; ", c.Today.Select(h => Career.HappeningDef(h.Id).name)) : "")}");
        if (c?.Active is Job j && Root.RunScreen == null) sb.AppendLine($"active job: {j.Title} -- {Tables.District(j.District).Name}, threat {j.Threat}, {M.Money(j.Pay)}, expires day {j.Expires}");
        if (top is PlaceScreen or Pda)
        {
            sb.AppendLine("(inside a place -- 'ui' lists tabs and buttons, 'ui all' includes ones scrolled out of view; Esc or [Back to the car] leaves)");
            var labels = Visible(top).OfType<Label>().Where(l => l.IsVisibleInTree() && l.Text.Length > 0 && ClipVisible(l)).Select(l => l.Text.Replace("\n", " / ")).ToList();
            foreach (string l in labels.Count > 160 ? labels.Skip(labels.Count - 160) : labels) sb.AppendLine("  | " + l);
        }
        if (Root.RunScreen != null) RunState(sb);
        else if (Root.CityScreen != null && Root.CitySim != null) CityState(sb);
        return sb.ToString().TrimEnd();
    }

    private void CityState(StringBuilder sb)
    {
        CitySim s = Root.CitySim;
        Car car = s.Player;
        float hdg = (car.Heading * 180 / MathF.PI % 360 + 360) % 360;
        sb.AppendLine($"heat {s.C.Heat:0.0}/5{(s.Chasers.Count > 0 ? $", chased by {string.Join(", ", s.Chasers.Select(x => $"{x.Kind} at ({x.Car.Pos.X:0},{x.Car.Pos.Y:0}) {V2.Distance(x.Car.Pos, car.Pos):0} tiles"))}" : "")}{(s.C.Courier ? $" | PACKAGE aboard, {s.C.CourierT:0} s left" : "")} | weather {s.C.Weather}");
        sb.AppendLine($"car: {car.Def.Name} at ({car.Pos.X:0.0},{car.Pos.Y:0.0}) heading {hdg:0} deg (0 = east, 90 = south), speed {car.Speed:0.0} t/s, hull {car.Hp:0}/{car.Def.Hp}");
        Lot at = s.AtDoor();
        sb.AppendLine(at != null ? $"at the door of: {PlaceName(at)} -- press E to go in" : "not at any door");
        if (s.C.RivalLine() is string rival) sb.AppendLine($"rival crew: {rival}");
        if (s.Street is StreetEncounter se)
            sb.AppendLine($"street: {se.Kind} at ({se.At.X:0},{se.At.Y:0}), {V2.Distance(se.At, car.Pos):0} tiles, {se.Life:0} s left{(s.AtStreet() ? " -- you're beside it: press E" : " -- stop beside it and press E ('drive street')")}");
        if (s.C.Passenger is Fare fare && s.FareKerb is V2 drop)
            sb.AppendLine($"aboard: {fare.Who} -- stop at the kerb ({drop.X:0},{drop.Y:0}), {V2.Distance(drop, car.Pos):0} tiles ('drive fare')");
        if (s.C.Active is Job cj && cj.Driving)
            sb.AppendLine($"courier: {(s.C.Courier ? $"package aboard -- deliver to the DROP door ({s.City.Lots[cj.Lot2].Door.X},{s.City.Lots[cj.Lot2].Door.Y}), {s.C.CourierT:0} s left; 'drive job' goes there" : $"collect at the PICKUP door ({s.City.Lots[cj.Lot].Door.X},{s.City.Lots[cj.Lot].Door.Y}) with E; 'drive job' goes there")}");
        foreach (Lot l in s.Destinations())
            sb.AppendLine($"  {PlaceName(l),-34} door ({l.Door.X},{l.Door.Y})  {V2.Distance(l.Door.Centre, car.Pos):0} tiles  [{l.District}]");
        if (Root.Career.News.Count > 0) sb.AppendLine("news: " + Root.Career.News[^1]);
    }

    private string PlaceName(Lot l) => l.Place switch
    {
        null => (Root.Career.Active?.Driving == true ? (Root.Career.Courier ? "COURIER DROP: " : "COURIER PICKUP: ") : "JOB SITE: ") + Root.Career.Active?.Title,
        "home" or "loft" => "safehouse (home)",
        _ => $"{City.PlaceName(l.Place)} ({l.Place})",
    };

    private void RunState(StringBuilder sb)
    {
        Run r = Root.RunScreen.R;
        Actor p = r.Player;
        sb.AppendLine(r.Peaceful ? $"on foot at the {r.Job.Title} | time {r.Time:0}s" : $"job: {r.Job.Title} | {r.Job.Faction} {r.Job.Style} threat {r.Job.Threat} | time {r.Time:0}s | phase {r.Phase}");
        sb.AppendLine($"objective: {r.ObjectiveText()}" + (r.ObjectivePos() is V2 o ? $"  -> at ({o.X:0.0},{o.Y:0.0}), {V2.Distance(o, p.Pos):0.0} tiles" : "") + (r.FloorNote() is string fn && r.NavTarget() is V2 nt ? $"  {fn} Stairs on your floor at ({nt.X:0.0},{nt.Y:0.0})." : ""));
        Rect ex = r.Plan.Extraction;
        sb.AppendLine($"extraction zone: x {ex.X}..{ex.Right - 1}, y {ex.Y}..{ex.Bottom - 1}{(r.Plan.BackDoor.W > 0 ? $"; also the dock exit x {r.Plan.BackDoor.X}..{r.Plan.BackDoor.Right - 1}, y {r.Plan.BackDoor.Y}..{r.Plan.BackDoor.Bottom - 1}" : "")}{(r.InExtraction(p.Pos) ? " (you are in it)" : "")}");
        if (r.StyleRule is var (sname, swhat)) sb.AppendLine($"site: {sname} -- {swhat}{(r.HasBadge ? " | you have the keycard" : "")}{(r.HasSuit ? " | you wear the clean-room suit" : "")}{(r.Plan.CleanRoom.W > 0 ? $" | clean room x {r.Plan.CleanRoom.X}..{r.Plan.CleanRoom.Right - 1}, y {r.Plan.CleanRoom.Y}..{r.Plan.CleanRoom.Bottom - 1}" : "")}{(r.L.Keycards.Count > 0 ? $" | keycard doors at {string.Join(" ", r.L.Keycards.Select(i => $"({i % r.L.W},{i / r.L.W})"))}" : "")}");
        if (r.Plan.Tower)
            sb.AppendLine($"tower: you are on {r.Plan.FloorName(r.PlayerFloor)} ({r.PlayerFloor + 1}/{r.FloorCount}{(r.Secure(r.PlayerFloor) ? ", SECURE" : "")}); elevator panel at ({r.Plan.Lifts[r.PlayerFloor].X},{r.Plan.Lifts[r.PlayerFloor].Y}) -- {(r.LiftHacked ? "stops at " + string.Join(", ", Enumerable.Range(0, r.FloorCount).Where(r.Served).Select(k => (k + 1).ToString())) : "not hacked yet")}; E rides up, Shift+E down; stairs '^' join each floor to the next");
        if (r.Plan.Basement.W > 0) sb.AppendLine($"this site has a basement (x >= {r.Plan.MainW}); stairs '^' connect the floors -- you are {(r.Downstairs ? "DOWNSTAIRS" : "upstairs")}");
        float lit = r.L.LightAt(p.Pos);
        sb.AppendLine($"you: ({p.Pos.X:0.0},{p.Pos.Y:0.0}) {(lit > 0.55f ? "lit" : lit > 0.15f ? "dim" : "dark")} ({lit:0.00}: seen x{r.LightDetect(p.Pos):0.00}) hp {p.Hp:0}/{p.MaxHp:0} armour {r.C.ArmorValue} | weapon {r.CurrentWeapon?.Name} {(r.CurrentGun != null ? $"{r.CurrentGun.Ammo}/{r.CurrentGun.Mag}{(r.CurrentGun.Reload > 0 ? " reloading" : "")}" : "")} | slots 1:{r.Guns[0]?.Def.Name ?? "-"} 2:{r.Guns[1]?.Def.Name ?? "-"} 3:{r.MeleeDef.Name}");
        sb.AppendLine($"grenades: {(r.Nades.Values.Any(v => v > 0) ? string.Join(", ", r.Nades.Where(n => n.Value > 0).Select(n => $"{n.Key} x{n.Value}")) + $" (selected {r.NadeSel})" : "none")} | medkits {r.Medkits} | dash {(r.DashCd > 0 ? $"{r.DashCd:0.0}s" : "ready")}");
        sb.AppendLine($"abilities: Q {Career.AbilityName(r.C.SlotQ)} {Cd(r, r.C.SlotQ)}, F {Career.AbilityName(r.C.SlotF)} {Cd(r, r.C.SlotF)}");
        if (r.Drone != null) sb.AppendLine(r.Rigging ? $"RIGGING the {r.Drone.Name} at ({r.Drone.Pos.X:0.0},{r.Drone.Pos.Y:0.0}), hull {r.Drone.Hp:0}, signal {r.DroneSignal * 100:0}% -- WASD flies it, aim/fire use it, key T returns (your body stands still)" : r.Drone.Dead ? "your drone is down" : $"drone {r.Drone.Name} with you at ({r.Drone.Pos.X:0.0},{r.Drone.Pos.Y:0.0}) -- key T to jack in");
        if (r.Deadline > 0) sb.AppendLine($"deadline: {r.TimeLeft:0} s left");
        if (r.Job.Mods.Count > 0 || r.C.Weather != Career.Sky.Clear) sb.AppendLine($"conditions: {r.C.Weather}{string.Concat(r.Job.Mods.Select(m => ", " + m))}");
        if (r.CarriedBody != null) sb.AppendLine("carrying a body (E drops it)");
        sb.AppendLine($"security: level {r.SecurityLevel}/6 {Run.SecurityLevels[r.SecurityLevel].name} ({r.SecurityTick * 100:0} % to the next){(r.Alarm ? ", ALARM raised" : "")}{(r.CoolingOff ? ", cooling off" : "")} | kills {r.Kills} | cash found {r.CashFound}");
        if (r.Prompt != null) sb.AppendLine($"prompt: {r.Prompt} {(r.PromptProgress >= 0 ? $"{MathF.Min(1, r.PromptProgress) * 100:0}%" : "")}");
        if (r.Matrix is MatrixRun mx)
        {
            sb.AppendLine($"JACKED INTO THE MATRIX: move with WASD one node at a time (key D 150 etc.); E at the entry ring jacks out. Datastores {mx.Taken.Count}/3{(mx.Black ? ", BLACK ICE" : "")}{(mx.SpoofLeft ? ", spoof ready" : "")}.");
            sb.AppendLine("grid (Y you, E entry, $ datastore, + taken, I ICE, - and | data lines):");
            for (int y = 0; y < MatrixRun.H; y++)
            {
                var row = new StringBuilder("  ");
                var below = new StringBuilder("  ");
                for (int x = 0; x < MatrixRun.W; x++)
                {
                    var q = new Pt(x, y);
                    char ch = q == mx.You ? 'Y' : mx.Ice.Contains(q) ? 'I' : mx.Taken.Contains(q) ? '+' : mx.Stores.Contains(q) ? '$' : q == mx.Entry ? 'E' : 'o';
                    row.Append(ch).Append(mx.Linked(q, q + new Pt(1, 0)) ? "---" : "   ");
                    below.Append(mx.Linked(q, q + new Pt(0, 1)) ? '|' : ' ').Append("   ");
                }
                sb.AppendLine(row.ToString());
                if (y < MatrixRun.H - 1) sb.AppendLine(below.ToString());
            }
        }
        var seen = r.Actors.Where(a => !a.IsPlayer && !a.Dead && r.Shown(a)).OrderBy(a => V2.Distance(a.Pos, p.Pos)).ToList();
        if (r.Plan.KindAt(Pt.Of(p.Pos)) is RoomKind rk && rk != RoomKind.Plain) sb.AppendLine($"room: {SetPieces.Name(rk)}");
        foreach (GasCloud g in r.Clouds.Where(g => r.Seen(g.Pos))) sb.AppendLine($"gas: {g.Kind} cloud at ({g.Pos.X:0},{g.Pos.Y:0}), {g.Reach:0.0} tiles across, {g.Life - g.T:0} s left{(r.InGas(p.Pos) ? " -- YOU'RE IN IT" : "")}");
        sb.AppendLine(seen.Count == 0 ? "nobody in sight" : "in sight:");
        foreach (Actor a in seen.Take(12))
        {
            string who = a.IsHostage ? $"HOSTAGE {a.Name} ({a.State})" : a.IsNpc ? (a.Vendor != null ? $"vendor: {a.Name}" : a.Cleaner ? "night cleaner" : "passer-by") : a.IsGhost ? "??? (a figure -- real?)" : a.IsAlly ? $"{a.Name} [your {(a.IsDrone ? "drone" : a.Summoned ? "spirit" : "hire")}]" : a.Rival ? $"{a.Name} [RIVAL, {r.C.Rival?.Name}]{(a == r.RivalCarrier ? " CARRYING THE OBJECTIVE" : "")}" : a.Def != null ? $"{a.Def.Name}{(a == r.Vip ? " [TARGET]" : "")}{(a.Team == 0 ? " [hacked, yours]" : "")}" : a.Name;
            string mind = a.Def == null ? "" : a.Alert ? "ALERT" : a.State == AiState.Suspicious ? "suspicious" : a.Detect > 0.05f ? $"noticing {a.Detect * 100:0}%" : "unaware";
            sb.AppendLine($"  {Letter(a, r)} {who} at ({a.Pos.X:0.0},{a.Pos.Y:0.0}) {V2.Distance(a.Pos, p.Pos):0.0} tiles, hp {a.Hp:0}/{a.MaxHp:0} {mind}{(a.Mood == Mood.Asleep ? " ASLEEP" : a.Mood == Mood.OffDuty ? " off duty" : a.Mood == Mood.Bound ? " bound to its circle" : "")}{(a.Drunk ? " drunk" : "")}{(a.Badge ? " (has the keycard)" : "")}{(a.Cleaner ? a.CallT > 0 ? $" CALLING SECURITY ({a.CallT:0.0} s)" : " (sees a gun: calls security)" : "")}{(a.Stun > 0 ? " stunned" : "")}");
        }
        var loot = r.Pickups.Where(k => !k.Taken && r.Seen(k.Pos)).ToList();
        foreach (Actor b in r.Actors.Where(a => a.Dead && !a.Escaped && a.Def != null && r.Seen(a.Pos) && !a.Static && !a.Flying).Take(6))
            sb.AppendLine($"  {(b.KO ? "unconscious" : "dead")} {b.Def.Name} at ({b.Pos.X:0.0},{b.Pos.Y:0.0}){(b == r.CarriedBody ? " (carried)" : "")}");
        foreach (Pickup k in loot.Take(8)) sb.AppendLine($"  pickup {k.Kind} {k.Name ?? k.Id ?? ""} at ({k.Pos.X:0.0},{k.Pos.Y:0.0})");
        sb.AppendLine(AsciiMap(r, 22));
        foreach (var l in Root.RunScreen.Log) sb.AppendLine("msg: " + l);
    }

    private static string Cd(Run r, Ability a) => a == Ability.None ? "" : r.AbilityCd.GetValueOrDefault(a) > 0 ? $"({r.AbilityCd[a]:0.0}s)" : "(ready)";

    private static char Letter(Actor a, Run r)
    {
        if (a.IsHostage) return 'h';
        if (a == r.Vip) return 'V';
        if (a.Team == 0) return 'y';
        return a.Kind switch
        {
            EnemyKind.Camera => 'c', EnemyKind.Turret => 't', EnemyKind.Drone => 'd', EnemyKind.Hound => 'k', EnemyKind.Mage => 'm',
            EnemyKind.Heavy => 'H', EnemyKind.Sniper => 'S', EnemyKind.Spider => 'D', EnemyKind.Cloaker => 'G', EnemyKind.Bulwark => 'B',
            EnemyKind.Barghest => 'K', EnemyKind.HiveQueen => 'Q', EnemyKind.HiveWorker => 'w', EnemyKind.EggSac => 'e', _ => 'g',
        };
    }

    /// <summary>What the player knows of the map around them: seen and remembered tiles only.</summary>
    private static string AsciiMap(Run r, int radius)
    {
        Actor p = r.Player;
        Pt c = Pt.Of(p.Pos);
        var sb = new StringBuilder();
        sb.AppendLine($"map around you (x {c.X - radius}..{c.X + radius}, y {c.Y - radius}..{c.Y + radius}; @ you, # wall, = reinforced, \" glass, + door, L locked door, x cover, o barrel, r/v racks/vats to destroy, M mainframe, % security console, & safe, l locker, b bunk, p coolant pipe, ^ stairs, ~ fire, $ pickup, E extraction, letters people (see list), space unknown, lower-case dot remembered floor):");
        var people = new Dictionary<Pt, char>();
        foreach (Actor a in r.Actors.Where(a => !a.IsPlayer && !a.Dead && r.Shown(a))) people[Pt.Of(a.Pos)] = Letter(a, r);
        var loot = r.Pickups.Where(k => !k.Taken && r.Seen(k.Pos)).Select(k => Pt.Of(k.Pos)).ToHashSet();
        sb.Append("     ");
        for (int x = c.X - radius; x <= c.X + radius; x++) sb.Append(x % 10 == 0 ? '|' : ' ');
        sb.AppendLine();
        for (int y = c.Y - radius; y <= c.Y + radius; y++)
        {
            sb.Append($"{y,4} ");
            for (int x = c.X - radius; x <= c.X + radius; x++)
            {
                var t = new Pt(x, y);
                if (!r.L.In(t)) { sb.Append(' '); continue; }
                int i = r.L.Idx(x, y);
                bool vis = r.Visible[i], known = r.L.Explored[i];
                char ch;
                if (t == c) ch = '@';
                else if (people.TryGetValue(t, out char pc)) ch = pc;
                else if (!known) ch = ' ';
                else if (loot.Contains(t)) ch = '$';
                else if (r.Fire.ContainsKey(i) && vis) ch = '~';
                else
                {
                    ch = r.L[t] switch
                    {
                        Tile.Floor or Tile.Rubble or Tile.DoorOpen => r.Plan.Extraction.Contains(t) || r.Plan.BackDoor.W > 0 && r.Plan.BackDoor.Contains(t) ? 'E' : vis ? ' ' : '.',
                        Tile.Glass => '"', Tile.Drywall or Tile.Brick or Tile.Concrete => '#', Tile.Reinforced => '=',
                        Tile.Door => '+', Tile.LockedDoor => 'L', Tile.Crate or Tile.Desk or Tile.Wreck or Tile.Fence => 'x',
                        Tile.Barrel => 'o', Tile.Rack => 'r', Tile.Vat => 'v', Tile.Mainframe => 'M', Tile.Console => '%', Tile.Safe => '&', Tile.Locker => 'l', Tile.Stairs => '^', Tile.Bunk => 'b', Tile.Pipe => 'p', _ => '?',
                    };
                    if (ch == ' ' && vis) ch = '·';
                }
                sb.Append(ch);
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    // ---- autopilots ---------------------------------------------------------------------

    private async Task<string> Walk(V2 goal, int timeoutMs, bool sneak)
    {
        if (Root.RunScreen == null) return "not on a run";
        Run r = Root.RunScreen.R;
        Pt gt = Pt.Of(goal);
        if (!r.L.In(gt)) return "outside the map";
        ulong end = Time.GetTicksMsec() + (ulong)timeoutMs;
        bool wasAlert = r.AlarmMeter > 0 || r.Alarm;
        int alertCount = r.Actors.Count(a => a.Hostile && a.Alert);
        V2 lastPos = r.Player.Pos;
        float hp0 = r.Player.Hp;
        ulong lastMove = Time.GetTicksMsec();
        if (sneak) KeyDown(Key.Shift);
        string why = "timed out";
        while (Time.GetTicksMsec() < end && r.Phase == RunPhase.Playing && Root.RunScreen != null)
        {
            V2 p = r.Player.Pos;
            if (V2.Distance(p, lastPos) > 3) { lastPos = p; lastMove = Time.GetTicksMsec(); } // stairs
            if (V2.Distance(p, goal) < 0.45f) { why = "arrived"; break; }
            // Path over what is walkable now (doors open on contact; locked doors stop it).
            ushort[] field = r.L.Field(gt, 300);
            Pt here = Pt.Of(p);
            Pt next = r.L.Step(field, here);
            // A step to the far end of the stairs: just stand on them.
            V2 target = next == here ? goal : next.Chebyshev(here) > 1 ? here.Centre : next.Centre;
            if (next == here && field[r.L.Idx(here.X, here.Y)] == Level.Far) { why = "no path there from here"; break; }
            V2 d = target - p;
            SetMove(d);
            if (r.Actors.Count(a => a.Hostile && a.Alert) > alertCount) { why = "stopped: someone is alerted"; break; }
            if (!wasAlert && (r.AlarmMeter > 0 || r.Alarm)) { why = "stopped: you were spotted"; break; }
            if (r.Player.Hp < hp0) { why = $"stopped: took {hp0 - r.Player.Hp:0} damage (hp {r.Player.Hp:0})"; break; }
            await Frames(1);
            if (V2.Distance(r.Player.Pos, lastPos) > 0.1f) { lastPos = r.Player.Pos; lastMove = Time.GetTicksMsec(); }
            else if (Time.GetTicksMsec() - lastMove > 1500) { why = "stuck"; break; }
        }
        foreach (Key k in new[] { Key.W, Key.A, Key.S, Key.D, Key.Shift }) KeyUp(k);
        await Frames(2);
        V2 at = r.Player.Pos;
        if (r.Phase == RunPhase.Dead) why = "you died";
        else if (r.Phase == RunPhase.Extracted) why = "run over: extracted";
        return $"walk: {why}, now at ({at.X:0.0},{at.Y:0.0})";
    }

    private void SetMove(V2 d)
    {
        void Set(Key k, bool on) { if (on) KeyDown(k); else KeyUp(k); }
        float len = d.Length();
        if (len < 1e-3f) { Set(Key.W, false); Set(Key.S, false); Set(Key.A, false); Set(Key.D, false); return; }
        d /= len;
        Set(Key.D, d.X > 0.38f); Set(Key.A, d.X < -0.38f);
        Set(Key.S, d.Y > 0.38f); Set(Key.W, d.Y < -0.38f);
    }

    private async Task<string> Drive(string place, bool stopForStreet = false)
    {
        if (Root.CityScreen == null || Root.CitySim == null) return "not in the city";
        CitySim s = Root.CitySim;
        // A street encounter or a fare's drop-off: a kerb, not a door.
        if (place is "street" or "fare")
        {
            V2? kerb = place == "street" ? s.Street?.At : s.FareKerb;
            if (kerb is not V2 k) return $"no {place} to drive to";
            return await DriveTo(s, new DriveAutopilot(s.City, s.Player.Pos, k, s.Roadblocks.Count > 0 ? s.Closed : null), place == "street" ? () => s.AtStreet() : () => s.C.Passenger == null, false);
        }
        Lot lot = place.ToLowerInvariant() switch
        {
            "job" => s.C.Active == null ? null : s.C.Active.Driving ? s.CourierLot(s.C.Active) : s.City.Lots[s.C.Active.Lot],
            "home" or "safehouse" => s.HomeLot,
            _ => s.City.Place(place.ToLowerInvariant()),
        };
        if (lot == null) return $"no such place '{place}'";
        return await DriveTo(s, s.RouteTo(lot), null, stopForStreet);
    }

    /// <summary>Drive an autopilot with real keys until it arrives (or <paramref name="there"/> says so), the car is wrecked, or 150 s pass.</summary>
    private async Task<string> DriveTo(CitySim s, DriveAutopilot ap, Func<bool> there, bool stopForStreet)
    {
        string passed = null;
        ulong end = Time.GetTicksMsec() + 150000;
        string why = "timed out";
        void Set(Key k, bool on) { if (on) KeyDown(k); else KeyUp(k); }
        while (Time.GetTicksMsec() < end && Root.CityScreen != null)
        {
            if (Root.CityScreen.Paused) { await Frames(2); continue; }
            DriveInput d = ap.Step(s, (float)GetProcessDeltaTime());
            if (ap.Done || there?.Invoke() == true) { why = "arrived"; break; }
            if (s.Street != null) passed ??= $"{s.Street.Kind} at ({s.Street.At.X:0},{s.Street.At.Y:0})";
            if (stopForStreet && s.Street != null) { why = $"stopped: something on the street ahead ({passed}) -- 'drive street' goes to it"; break; }
            Set(Key.W, d.Throttle > 0); Set(Key.S, d.Throttle < 0);
            Set(Key.D, d.Steer > 0); Set(Key.A, d.Steer < 0);
            await Frames(1);
            if (s.Wrecked) { why = "the car was wrecked"; break; }
        }
        foreach (Key k in new[] { Key.W, Key.A, Key.S, Key.D }) KeyUp(k);
        // Come to a stop before reporting where we are.
        for (int i = 0; i < 90 && MathF.Abs(s.Player.Speed) > 0.5f && Root.CityScreen != null; i++) { KeyDown(Key.S); await Frames(1); KeyUp(Key.S); if (s.Player.Speed < 0) break; }
        KeyUp(Key.S);
        await Ms(300);
        Lot at = s.AtDoor();
        string where = s.AtStreet() ? "beside the street encounter (press E)" : at != null ? $"at the door of {PlaceName(at)} (press E)" : "not at a door";
        return $"drive: {why}; {where}; hull {s.Player.Hp:0}{(passed != null && !stopForStreet ? $"; passed a street encounter ({passed})" : "")}";
    }

}
