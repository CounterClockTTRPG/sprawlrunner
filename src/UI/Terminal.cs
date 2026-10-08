using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>
/// The shadow-net terminal: a green console that lists the job board. Type
/// commands (help, jobs, info N, accept N, drop, news) or click a job line.
/// Output types itself out.
/// </summary>
public partial class Terminal : PanelContainer
{
    public Root Root;
    public Action Changed;
    public Action Escape;
    private VBoxContainer _out;
    private ScrollContainer _scroll;
    private LineEdit _in;
    private readonly Queue<(string text, Color colour, Action click)> _pending = new();
    private Label _typing;
    private string _typingText;
    private int _shown;
    private float _acc;
    private Action _typingClick;
    private Color _typingColour;

    private Career C => Root.Career;
    private static readonly Color G = UITheme.Term;
    private static readonly Color GDim = UITheme.Term.Darkened(0.45f);
    private static readonly Color Warn = UITheme.Yellow;

    public override void _Ready()
    {
        // An old monitor: a thick plastic bezel, rounded glass, a power light and a badge (CrtGlass).
        var bezel = new StyleBoxFlat { BgColor = new Color(0f, 0.028f, 0.014f, 0.99f), BorderColor = new Color(0.1f, 0.094f, 0.082f), CornerDetail = 10, ShadowColor = new Color(0, 0, 0, 0.6f), ShadowSize = 8 };
        bezel.SetBorderWidthAll(16);
        bezel.BorderWidthBottom = 28;
        bezel.SetCornerRadiusAll(22);
        bezel.ContentMarginLeft = bezel.ContentMarginRight = 28;
        bezel.ContentMarginTop = 24;
        bezel.ContentMarginBottom = 34;
        AddThemeStyleboxOverride("panel", bezel);
        var v = UITheme.VBox(4);
        AddChild(v);
        _out = UITheme.VBox(1);
        _scroll = UITheme.Scroll(_out, 470);
        _scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        // A long line runs off the glass instead of widening the whole monitor.
        _scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever;
        v.AddChild(_scroll);
        var row = UITheme.HBox(4);
        row.AddChild(UITheme.Label(">", 18, G, mono: true));
        _in = new LineEdit { CustomMinimumSize = new Vector2(0, 32), PlaceholderText = "type 'help'", CaretBlink = true };
        _in.AddThemeFontOverride("font", UITheme.Mono);
        _in.AddThemeColorOverride("font_color", G);
        _in.AddThemeStyleboxOverride("normal", UITheme.Box(new Color(0, 0.05f, 0.02f), GDim, 1, 0));
        _in.AddThemeStyleboxOverride("focus", UITheme.Box(new Color(0, 0.05f, 0.02f), G, 1, 0));
        // The glass goes over everything on the screen; a PanelContainer fits it to the same rect.
        CallDeferred(Node.MethodName.AddChild, new CrtGlass());
        _in.KeepEditingOnTextSubmit = true;
        _in.TextSubmitted += t => { Run(t); _in.Text = ""; };
        _in.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Keycode: Key.Escape }) { _in.ReleaseFocus(); Escape?.Invoke(); _in.AcceptEvent(); }
        };
        row.AddChild(UITheme.Grow(_in));
        v.AddChild(row);

        Line($"KESSLER BAY SHADOWNET  //  node {C.Seed % 0xffff:x4}", G);
        Line(C.JobsDone + C.JobsFailed == 0 ? $"handshake ok. new handle registered: {C.Name.ToLower()}." : $"handshake ok. welcome back, {C.Name.ToLower()}.", GDim);
        foreach (string h in C.HappeningLines()) Line(h, Warn);
        int unread = C.Unread, waiting = C.Inbox.Count(C.Waiting);
        if (unread > 0 || waiting > 0) Line($"{unread} new message(s){(waiting > 0 ? $", {waiting} waiting on your answer" : "")}. type 'mail'.", UITheme.Amber, Mailbox);
        Line("", G);
        Jobs();
        _in.CallDeferred(LineEdit.MethodName.GrabFocus);
    }

    public void Line(string text, Color c, Action click = null) => _pending.Enqueue((text, c, click));

    public override void _Process(double delta)
    {
        if (_typing == null && _pending.Count > 0)
        {
            var (text, colour, click) = _pending.Dequeue();
            _typingText = text;
            _typingClick = click;
            _typingColour = colour;
            _shown = 0;
            _typing = UITheme.Label("", 16, colour, wrap: false, mono: true);
            _typing.MouseFilter = click != null ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
            if (click != null)
            {
                Label l = _typing;
                l.GuiInput += e =>
                {
                    if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { Sfx.Instance?.Play(Sound.UiSelect); click(); }
                };
                l.MouseEntered += () => l.LabelSettings.FontColor = UITheme.Cyan;
                l.MouseExited += () => l.LabelSettings.FontColor = colour;
                l.MouseDefaultCursorShape = CursorShape.PointingHand;
            }
            _out.AddChild(_typing);
        }
        if (_typing != null)
        {
            _acc += (float)delta * 900;
            int add = (int)_acc;
            _acc -= add;
            if (add > 0)
            {
                _shown = Math.Min(_typingText.Length, _shown + add);
                _typing.Text = _typingText[.._shown];
                if (_shown % 12 < add) Sfx.Instance?.Play(Sound.Key, 1, 0.35f);
            }
            if (_shown >= _typingText.Length) _typing = null;
            _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
        }
    }

    private void Run(string cmd)
    {
        cmd = cmd.Trim().ToLowerInvariant();
        if (cmd.Length == 0) return;
        Line("> " + cmd, G);
        string[] w = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int n = w.Length > 1 && int.TryParse(w[1], out int k) ? k : -1;
        switch (w[0])
        {
            case "help": case "?":
                Line("  jobs         list the board", GDim);
                Line("  info N       details of job N", GDim);
                Line("  accept N     take job N (one at a time; 'J' jobs: mr. johnson negotiates)", GDim);
                Line("  deal / haggle / walk   answer a johnson", GDim);
                Line("  drop         walk away from the active job (-1 rep)", GDim);
                Line("  news         what the street is saying", GDim);
                Line("  mail         your inbox;  read N  opens one;  reply N K  answers it", GDim);
                Line("  clear        clear the screen", GDim);
                break;
            case "jobs": case "ls": case "list": Jobs(); break;
            case "info": case "i": Info(n); break;
            case "accept": case "a": Accept(n); break;
            case "deal": case "yes": Deal(true); break;
            case "haggle": Deal(false); break;
            case "walk": case "no":
                if (_johnson == null) Line("nobody is waiting on you.", Warn);
                else { Line("you hang up on mr. johnson.", GDim); _johnson = null; }
                break;
            case "drop":
                if (C.Active == null) Line("no active job.", Warn);
                else { C.Drop(); Line("job dropped. the client won't call again.", Warn); Changed?.Invoke(); }
                break;
            case "news":
                foreach (string s in C.News.TakeLast(8)) Line("  " + s, GDim);
                break;
            case "mail": case "inbox": Mailbox(); break;
            case "read": case "r": Read(n); break;
            case "reply": Reply(n, w.Length > 2 && int.TryParse(w[2], out int kk) ? kk : -1); break;
            case "clear": case "cls":
                foreach (Node c in _out.GetChildren()) c.QueueFree();
                break;
            default: Line($"unknown command '{w[0]}'. try 'help'.", Warn); break;
        }
    }

    // ---- mail ---------------------------------------------------------------------------

    private void Mailbox()
    {
        if (C.Inbox.Count == 0) { Line("inbox empty.", GDim); return; }
        Line("INBOX", G);
        Line(" #   DAY  FROM                 SUBJECT", GDim);
        var list = C.Inbox.AsEnumerable().Reverse().Take(20).ToList();
        for (int i = 0; i < list.Count; i++)
        {
            Mail m = list[i];
            int idx = i + 1;
            string tag = !m.Read ? " [new]" : C.Waiting(m) ? " [reply]" : "";
            Line($"{(m.Read ? " " : "*")}{idx,-3} {m.Day,-4} {Cut(m.From, 20),-20} {m.Subject}{tag}", m.Read && !C.Waiting(m) ? GDim : G, () => Read(idx));
        }
        Line("click one, or 'read N'.", GDim);
    }

    private static string Cut(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + ".";

    private Mail MailAt(int n)
    {
        var list = C.Inbox.AsEnumerable().Reverse().Take(20).ToList();
        return n >= 1 && n <= list.Count ? list[n - 1] : null;
    }

    private void Read(int n)
    {
        Mail m = MailAt(n);
        if (m == null) { Line("no such message.", Warn); return; }
        m.Read = true;
        Line("", G);
        Line($"[{n}] FROM {m.From.ToUpperInvariant()}  ·  {m.Subject}  ·  day {m.Day}", UITheme.Cyan);
        foreach (string l in Wrap(m.Text, 70)) Line("    " + l, G);
        var opts = C.Options(m);
        if (C.Waiting(m))
            for (int k = 0; k < opts.Count; k++)
            {
                int kk = k;
                Line($"    [ reply {n} {k + 1} ]  {opts[k].text}{(opts[k].block != null ? $"  -- {opts[k].block.ToLower()}" : C.EffectOf(m, k) is string eff ? $"  ({eff})" : "")}", opts[k].block != null ? GDim : UITheme.Cyan, () => Reply(n, kk + 1));
            }
        else if (m.Reply != null && m.Answer >= 0 && opts.Count > 0)
        {
            Line($"    you answered: {opts[m.Answer].text}", GDim);
            foreach (string l in Wrap(m.Reply, 70)) Line("    " + l, GDim);
        }
        Changed?.Invoke();
    }

    private void Reply(int n, int k)
    {
        Mail m = MailAt(n);
        if (m == null) { Line("no such message.", Warn); return; }
        string err = C.Choose(m, k - 1);
        if (err != null) { Line(err.ToLower(), Warn); return; }
        Sfx.Instance?.Play(Sound.Objective);
        foreach (string l in Wrap(m.Reply, 70)) Line("  " + l, G);
        Profile.SaveCareer(C);
        Changed?.Invoke();
    }

    private void Jobs()
    {
        var board = C.Board.OrderBy(j => j.Threat).ThenBy(j => j.Id).ToList();
        Line($"JOB BOARD  ·  day {C.Day}  ·  rep {C.Rep} (threat up to {C.MaxThreat})", G);
        var grudges = C.Grudge.Where(g => g.Value > 0).Select(g => $"{Tables.FactionNames[(int)g.Key].ToLower()} {g.Value}").ToList();
        if (grudges.Count > 0) Line($"grudges: {string.Join(", ", grudges)}  (each job against a faction adds one; 3: their sites get elite security; 5: they send hitters; fades a point a week)", GDim);
        Line(" #   TYPE        DISTRICT     THREAT    PAY       KARMA  DAYS", GDim);
        if (board.Count == 0) Line("  nothing on the board. sleep on it.", GDim);
        for (int i = 0; i < board.Count; i++)
        {
            Job j = board[i];
            int idx = i + 1;
            bool active = j.Id == C.ActiveJob;
            string line = $"{(active ? "*" : j.Tutorial ? "T" : j.Story > 0 ? "S" : j.Arc != null ? "C" : j.Tag != null ? "!" : j.Contested ? "R" : j.Johnson ? "J" : " ")}{idx,-3} {Tables.JobNames[(int)j.Type],-11} {Tables.District(j.District).Name,-12} {Threat(j.Threat),-9} {M.Money(j.Pay),-9} +{j.Karma,-5} {j.Expires - C.Day + 1}";
            Line(line, active ? UITheme.Magenta : G, () => Info(idx));
        }
        Line("click a job, or 'info N'.  (* active  T tutorial  S story  C a contact's own story", GDim);
        Line("                            ! someone's personal business  J mr. johnson  R a rival crew wants it too)", GDim);
    }

    public static string Threat(int t) => t > 5 ? "[#####+]" : "[" + new string('#', t) + new string('.', 5 - t) + "]";

    private Job ByIndex(int n)
    {
        var board = C.Board.OrderBy(j => j.Threat).ThenBy(j => j.Id).ToList();
        return n >= 1 && n <= board.Count ? board[n - 1] : null;
    }

    private void Info(int n)
    {
        Job j = ByIndex(n);
        if (j == null) { Line("no such job.", Warn); return; }
        Line("", G);
        Line($"[{n}] {j.Title.ToUpperInvariant()}{(j.Johnson ? "   -- MR. JOHNSON (negotiated on accept)" : "")}", UITheme.Cyan);
        Line($"    employer: {Tables.Contact(j.Employer).Name} ({Tables.Contact(j.Employer).Role.ToLower()})", GDim);
        Line($"    site:     {Tables.District(j.District).Name}, held by {Tables.FactionNames[(int)j.Faction]}", GDim);
        Line($"    threat:   {Threat(j.Threat)}    pay {M.Money(j.Pay)}{(!Career.GhostPays(j) ? "" : Career.NoKillPays(j) ? $" (+25% if no alarm, +{(j.JohnsonId == "vale" ? 30 : 15)}% if no kills)" : " (+25% if no alarm)")}    karma +{j.Karma}", GDim);
        if (j.Hired != null) Line($"    hired: {Tables.Hires.First(h => h.id == j.Hired).name} comes along.", GDim);
        foreach (string l in Wrap(j.Brief, 70)) Line("    " + l, G);
        if (j.Johnson) Line($"    a mr. johnson job{(Career.JohnsonById(j.JohnsonId) is JohnsonDef jj ? $" ({jj.Name.ToLower()})" : "")}: terms are negotiated when you accept.", GDim);
        if (j.DoubleCross && C.Loyalty.GetValueOrDefault("fixer") >= 3) Line("    juno: 'something about this johnson smells wrong. watch the exit.'", Warn);
        int grudge = C.Grudge.GetValueOrDefault(j.Faction);
        if (grudge >= 3) Line($"    {Tables.FactionNames[(int)j.Faction].ToLower()} remember you (grudge {grudge}): elite security{(grudge >= 5 ? ", and hitters after the job" : "")}.", Warn);
        if (j.Id == C.ActiveJob) Line("    >> this is your active job. drive to the pink marker.", UITheme.Magenta);
        else Line($"    [ accept {n} ]", UITheme.Cyan, () => Accept(n));
    }

    private Job _johnson;

    private void Deal(bool take)
    {
        Job j = _johnson;
        if (j == null || !C.Board.Contains(j)) { Line("nobody is waiting on you.", Warn); _johnson = null; return; }
        if (take)
        {
            j.Negotiated = true;
            _johnson = null;
            string err = C.Accept(j);
            if (err != null) { Line(err.ToLower(), Warn); return; }
            Sfx.Instance?.Play(Sound.Objective);
            Line($"deal. {j.Title}: {M.Money(j.Pay)}. site marked on your map.", UITheme.Magenta);
            Changed?.Invoke();
            return;
        }
        string say = C.Haggle(j);
        Line("  " + say.ToLower(), G);
        if (!C.Board.Contains(j)) { _johnson = null; Changed?.Invoke(); return; }
        Offer(j);
    }

    private void Offer(Job j)
    {
        _johnson = j;
        Line($"  {(Career.JohnsonById(j.JohnsonId)?.Name ?? "mr. johnson").ToLower()} offers {M.Money(j.Pay)} for '{j.Title}'.", UITheme.Cyan);
        Line("    [ deal ]", UITheme.Cyan, () => Deal(true));
        if (!j.Haggled) Line("    [ haggle ]  (charisma helps; he may cut the price or walk)", UITheme.Cyan, () => Deal(false));
        Line("    [ walk ]", UITheme.Cyan, () => { _johnson = null; Line("you hang up on mr. johnson.", GDim); });
    }

    private void Accept(int n)
    {
        Job j = ByIndex(n);
        if (j == null) { Line("no such job.", Warn); return; }
        if (j.Johnson && !j.Negotiated && j.Id != C.ActiveJob)
        {
            JohnsonDef jd = Career.JohnsonById(j.JohnsonId);
            if (jd == null) Line("incoming call: a suit with no name. 'call me mr. johnson.'", G);
            else
            {
                Line($"incoming call: {jd.Look}. '{jd.Name.ToLower()}.'", G);
                C.Explain("johnson", "The same five Johnsons broker most negotiated work. Three clean jobs and they stop setting you up; one that sells you out never calls again.");
                int done = C.JohnsonJobs(jd.Id);
                Line($"  ({jd.Name.ToLower()} {jd.Habit}.{(done > 0 ? $" you've done {done} job(s) for them." : "")})", GDim);
            }
            Offer(j);
            return;
        }
        string err = C.Accept(j);
        if (err != null) { Line(err.ToLower(), Warn); return; }
        Sfx.Instance?.Play(Sound.Objective);
        Line($"accepted: {j.Title}. site marked on your map.", UITheme.Magenta);
        Changed?.Invoke();
    }

    public static IEnumerable<string> Wrap(string s, int width)
    {
        var line = "";
        foreach (string word in s.Split(' '))
        {
            if (line.Length + word.Length + 1 > width) { yield return line; line = word; }
            else line = line.Length == 0 ? word : line + " " + word;
        }
        if (line.Length > 0) yield return line;
    }
}
