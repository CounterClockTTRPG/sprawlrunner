using System;
using System.Linq;
using System.Collections.Generic;
using Godot;

namespace Sprawl;

/// <summary>
/// The runner's PDA (P in the city or the safehouse): who you know and how far they trust you,
/// who holds a grudge, what your career adds up to, achievements, and the
/// news so far.
/// </summary>
public partial class Pda : Control
{
    public static int Open;
    private readonly Career C;
    private readonly CitySim _sim;
    private VBoxContainer _content;
    private HBoxContainer _tabs;
    private string _tab = "Contacts";
    private string _inboxFilter = "Open";
    /// <summary>After a cab or the Kite moved you (the safehouse leaves with it).</summary>
    public Action Travelled;
    /// <summary>The door you're at (opened from a place): no cab to it.</summary>
    public Lot Here;

    public Pda(Career c, CitySim sim = null) { C = c; _sim = sim; }

    public override void _EnterTree() => Open++;
    public override void _ExitTree() => Open--;

    public override void _Ready()
    {
        UITheme.Fill(this);
        Theme = UITheme.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        var bg = new ColorRect { Color = new Color(0.01f, 0.012f, 0.015f, 0.92f) };
        UITheme.Fill(bg);
        AddChild(bg);
        var casing = new DeviceCasing { Clock = $"DAY {C.Day}  {(C.Day * 7 + 19) % 24:00}:{C.Day * 37 % 60:00}" };
        UITheme.Fill(casing);
        AddChild(casing);
        var screen = new ColorRect { Color = new Color(0.012f, 0.018f, 0.024f, 0.98f), MouseFilter = MouseFilterEnum.Ignore };
        UITheme.Fill(screen);
        screen.OffsetLeft = screen.OffsetTop = DeviceCasing.Bezel;
        screen.OffsetRight = screen.OffsetBottom = -DeviceCasing.Bezel;
        AddChild(screen);
        var m = new MarginContainer();
        UITheme.Fill(m);
        foreach (string s in new[] { "left", "right", "top", "bottom" }) m.AddThemeConstantOverride($"margin_{s}", 52);
        AddChild(m);
        var v = UITheme.VBox(10);
        m.AddChild(v);
        var head = UITheme.HBox(16);
        head.AddChild(UITheme.Label("PDA", 34, UITheme.Cyan, heading: true, wrap: false));
        head.AddChild(UITheme.Grow(UITheme.Label($"{C.Name} -- {C.ArchetypeName}   ·   day {C.Day}   ·   {M.Money(C.Cash)}   ·   rep {C.Rep}   ·   karma {C.Karma}", 18, UITheme.Dim, wrap: false)));
        head.AddChild(UITheme.Button("Close  [Esc]", Close, 160, 38));
        v.AddChild(head);
        _tabs = UITheme.HBox(8);
        v.AddChild(_tabs);
        UITheme.PowerOn(this);
        foreach (string t in new[] { "Inbox", "Travel", "Contacts", "Factions", "City", "Stats", "Street cred", "Journal" })
        {
            string tt = t;
            var b = UITheme.Button(t == "Inbox" && C.Unread > 0 ? $"Inbox ({C.Unread})" : t, () => Show(tt), 160, 38);
            b.Name = t;
            b.ToggleMode = true;
            _tabs.AddChild(b);
        }
        // Not a tab: the help pages open over the PDA.
        _tabs.AddChild(UITheme.Button("Help", () => Help.Open(this), 90, 38));
        var panel = UITheme.Panel(_content = UITheme.VBox(8), 16);
        panel.SizeFlagsVertical = SizeFlags.ExpandFill;
        v.AddChild(panel);
        if (C.Inbox.Any(m => !m.Read || C.Waiting(m))) _tab = "Inbox";
        Show(_tab);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if ((Controls.Back(e) || Controls.Is(e, Act.Pda)) && Modal.Open == 0)
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Close()
    {
        Sfx.Instance?.Play(Sound.UiBack);
        QueueFree();
    }

    private void Show(string tab)
    {
        _tab = tab;
        foreach (Node n in _tabs.GetChildren()) if (n is Button b) b.ButtonPressed = b.Name == tab;
        foreach (Node n in _content.GetChildren()) n.QueueFree();
        var list = UITheme.VBox(10);
        switch (tab)
        {
            case "Inbox": Inbox(list); break;
            case "Travel": Travel(list); break;
            case "Contacts": Contacts(list); break;
            case "Factions": Factions(list); break;
            case "City": CityTab(list); break;
            case "Stats": Stats(list); break;
            case "Street cred": Achieve(list); break;
            default:
                foreach (string s in C.News.AsEnumerable().Reverse()) list.AddChild(UITheme.Label(s, 16, UITheme.Text));
                break;
        }
        var scroll = UITheme.Scroll(list, 640);
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        _content.AddChild(scroll);
    }

    private void Inbox(VBoxContainer list)
    {
        if (C.Inbox.Count == 0) { list.AddChild(UITheme.Label("No messages. People write when there is something to say.", 16, UITheme.Dim)); return; }
        list.AddChild(UITheme.Label("Mail, calls and the people you met. Some want an answer; most keep until you give one. ★ marks the stories.", 15, UITheme.Dim));
        var filters = UITheme.HBox(8);
        foreach (string f in new[] { "Open", "Stories", "Everything" })
        {
            string ff = f;
            var fb = UITheme.Button(f, () => { _inboxFilter = ff; Show("Inbox"); }, 140, 30);
            fb.ToggleMode = true;
            fb.ButtonPressed = _inboxFilter == f;
            filters.AddChild(fb);
        }
        list.AddChild(filters);
        var shown = C.Inbox.AsEnumerable().Reverse().Where(m => _inboxFilter switch
        {
            "Open" => !m.Read || C.Waiting(m) || m.Day >= C.Day - 1,
            "Stories" => Career.Storyline(m),
            _ => true,
        }).ToList();
        if (shown.Count == 0) list.AddChild(UITheme.Label("Nothing here. 'Everything' shows the old mail.", 15, UITheme.Dim));
        foreach (Mail m in shown)
        {
            bool fresh = !m.Read;
            m.Read = true;
            var box = UITheme.VBox(6);
            if (Career.Storyline(m)) box.AddChild(UITheme.Label("★ story", 12, UITheme.Violet));
            box.AddChild(StoryModal.Letter(C, m, 1100));
            var opts = C.Options(m);
            if (C.Waiting(m))
                for (int k = 0; k < opts.Count; k++)
                {
                    int kk = k;
                    Mail mm = m;
                    var (text, block) = opts[k];
                    var b = UITheme.Button(text, () =>
                    {
                        if (C.Choose(mm, kk) != null) { Sfx.Instance?.Play(Sound.Error); return; }
                        Sfx.Instance?.Play(Sound.Coin);
                        Profile.SaveCareer(C);
                        Show("Inbox");
                    }, 0, 36, block ?? C.EffectOf(m, k) ?? "");
                    b.Disabled = block != null;
                    box.AddChild(b);
                    if (block != null) box.AddChild(UITheme.Label("    " + block, 13, UITheme.Bad));
                    else if (C.EffectOf(m, k) is string eff) box.AddChild(UITheme.Label("    " + eff, 13, UITheme.Dim));
                }
            else if (m.Reply != null && m.Answer >= 0 && opts.Count > 0)
            {
                box.AddChild(UITheme.Label($"> {opts[m.Answer].text}", 15, UITheme.Cyan));
                box.AddChild(UITheme.Label(m.Reply, 15, UITheme.Dim));
            }
            list.AddChild(UITheme.Panel(box, 10, fresh ? UITheme.Amber : null));
        }
    }

    private void Travel(VBoxContainer list)
    {
        if (_sim == null) { list.AddChild(UITheme.Label("Only from the street.", 16, UITheme.Dim)); return; }
        bool heli = C.OwnsHeli;
        list.AddChild(UITheme.Label(heli ? "Your Kite flies you and the car anywhere, free. (No package aboard.)" : "Call a cab: you ride, your car is towed behind. Pay by distance; the day doesn't pass.", 15, UITheme.Dim));
        string block = _sim.TravelBlock(heli);
        if (block != null) list.AddChild(UITheme.Label(block, 15, UITheme.Bad));
        foreach (Lot l in _sim.Destinations().Distinct().Where(l => l != Here))
        {
            Lot ll = l;
            string name = l.Place == null ? $"JOB: {C.Active?.Title}" : City.PlaceName(l.Place) + (l.Place is "home" or "loft" ? "" : $" ({City.PlaceWho(l.Place)})");
            var row = UITheme.HBox(12);
            row.AddChild(UITheme.Grow(UITheme.Label($"{name}  ·  {Tables.District(l.District).Name}", 16, UITheme.Text)));
            int fare = _sim.Fare(l);
            var b = UITheme.Button(heli ? "Fly" : $"Cab {M.Money(fare)}", () =>
            {
                _sim.TravelTo(ll, heli);
                Sfx.Instance?.Play(Sound.Door);
                Profile.SaveCareer(C);
                Close();
                Travelled?.Invoke();
            }, 140, 32, block ?? "");
            b.Disabled = block != null || !heli && C.Cash < fare;
            // Or drive there yourself: make it the target (the arrow and a route line point at it).
            if ((GetParent() as CityScreen ?? GetParent()?.GetParent() as CityScreen) is CityScreen cs)
            {
                bool on = cs.Target == ll;
                var t = UITheme.Button(on ? "Target ✓" : "Target", () => { cs.SetRoute(ll); Show("Travel"); }, 110, 32, on ? "Your target. Press again to clear it (the job leads again)." : "Make it your target: the arrow, the minimap and a route line point at it.");
                row.AddChild(t);
            }
            row.AddChild(b);
            list.AddChild(row);
        }
    }

    private void Contacts(VBoxContainer list)
    {
        list.AddChild(UITheme.Label("Trust grows with jobs done for a contact (and with money spent there). Trusted contacts pay more, offer harder work, do favours, and open their own stories.", 15, UITheme.Dim));
        int i = 2;
        foreach (ContactDef k in Tables.Contacts)
        {
            int trust = C.Loyalty.GetValueOrDefault(k.Id);
            var row = UITheme.HBox(16);
            row.AddChild(new Portrait { Contact = k, Seed = i++, CustomMinimumSize = new Vector2(120, 120), ClipContents = true });
            var t = UITheme.VBox(2);
            t.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            t.AddChild(UITheme.Label($"{k.Name} -- {k.Role}, {k.Venue} ({Tables.District(k.District).Name})", 19, Art.C(k.Colour), heading: true));
            var trow = UITheme.HBox(8);
            trow.AddChild(UITheme.Label("Trust", 15, UITheme.Yellow, wrap: false));
            trow.AddChild(new Pips { Value = trust });
            trow.AddChild(UITheme.Grow(UITheme.Label($"jobs from {k.Name} pay +{trust * 8} %" + (trust >= 3 ? ", and include harder work" : $"; harder work at trust 3"), 15, UITheme.Yellow)));
            t.AddChild(trow);
            if (Career.Arcs.TryGetValue(k.Id, out var arc))
            {
                int step = C.ArcStep.GetValueOrDefault(k.Id);
                string story = step >= 3 ? $"Story \"{arc.title}\": complete. Reward: {arc.reward}."
                    : C.Board.Any(j => j.Arc == k.Id) ? $"Story \"{arc.title}\": part {step + 1}/3 is on the board."
                    : trust < step + 1 ? $"Story \"{arc.title}\": part {step + 1}/3 opens at trust {step + 1}."
                    : $"Story \"{arc.title}\": part {step + 1}/3 -- check the terminal.";
                t.AddChild(UITheme.Label(story, 15, step >= 3 ? UITheme.Good : UITheme.Text));
            }
            else if (k.Id == "fixer") t.AddChild(UITheme.Label($"The Iron Spire story: chapter {C.Chapter}/4.", 15, UITheme.Text));
            if (Career.Favours.TryGetValue(k.Id, out var fav))
            {
                int next = C.FavourDay.TryGetValue(k.Id, out int last) ? last + 5 : 0;
                t.AddChild(UITheme.Label($"Favour: {fav.name} -- " + (trust < 2 ? "needs trust 2." : C.Day >= next ? "available now." : $"again on day {next}."), 14, UITheme.Dim));
            }
            t.AddChild(UITheme.Label(k.Blurb, 13, UITheme.Faint));
            row.AddChild(t);
            list.AddChild(row);
            list.AddChild(UITheme.Rule(UITheme.Faint));
        }
    }

    private void Factions(VBoxContainer list)
    {
        list.AddChild(UITheme.Label("Every job against a faction adds a point of grudge; it fades by a point a week. At 3 their sites field elite security; at 5 they send hitters after every job.", 15, UITheme.Dim));
        foreach (Faction f in Enum.GetValues<Faction>())
        {
            int g = C.Grudge.GetValueOrDefault(f);
            string effect = g >= 5 ? "elite security, and hitters after every job" : g >= 3 ? "elite security on their sites" : g > 0 ? "they know your name" : "no grudge";
            list.AddChild(UITheme.Label($"{Tables.FactionNames[(int)f]}   " + new string('■', Math.Min(g, 9)) + new string('□', Math.Max(0, 5 - g)) + $"   {effect}", 18, g >= 3 ? UITheme.Bad : UITheme.Text));
        }
        list.AddChild(UITheme.Spacer(8));
        list.AddChild(UITheme.Label($"Standing rises when you work for a faction (each job's client is in its brief). At {Tables.StandingSafe}: no ambushes on their streets. At {Tables.StandingJobs}: their own jobs on your board. At {Tables.StandingItem}: something only their friends can buy, at Juno's.", 15, UITheme.Dim));
        foreach (Faction f in Enum.GetValues<Faction>())
        {
            int st = C.StandingOf(f);
            string effect = st >= Tables.StandingItem ? "a friend: safe passage, their jobs, their gear" : st >= Tables.StandingJobs ? "safe passage, and their own jobs" : st >= Tables.StandingSafe ? "safe passage on their streets" : st > 0 ? "they've noticed you" : "nothing yet";
            list.AddChild(UITheme.Label($"{Tables.FactionNames[(int)f]}   " + new string('■', st) + new string('□', Math.Max(0, Tables.StandingItem - st)) + $"   {effect}", 18, st >= Tables.StandingSafe ? UITheme.Good : UITheme.Text));
        }
        if (C.RivalLine() is string rival)
        {
            list.AddChild(UITheme.Spacer(8));
            list.AddChild(UITheme.Label("THE OTHER CREW", 14, UITheme.Dim));
            list.AddChild(UITheme.Label(rival, 16, C.Rival.Active ? UITheme.Amber : UITheme.Dim));
        }
        list.AddChild(UITheme.Spacer(8));
        list.AddChild(UITheme.Label($"Heat: {C.Heat:0.0} / 5 stars (cools while no patrol is near; sleeping clears it).", 16, UITheme.Text));
    }

    private static Color[] FactionInk => new[] { UITheme.FactionInk(0), UITheme.FactionInk(1), UITheme.FactionInk(2) };

    private void CityTab(VBoxContainer list)
    {
        list.AddChild(UITheme.Label("WHO RUNS WHAT", 15, UITheme.Dim));
        list.AddChild(UITheme.Label("Every job against a faction costs it ground in that district; each week the city drifts back towards how it was. New jobs come from whoever holds the most.", 14, UITheme.Faint));
        var key = UITheme.HBox(16);
        for (int f = 0; f < 3; f++) key.AddChild(UITheme.Label(UITheme.FactionMark(f) + " " + Tables.FactionNames[f], 15, FactionInk[f], wrap: false));
        list.AddChild(key);
        foreach (DistrictDef d in Tables.Districts)
        {
            var row = UITheme.HBox(12);
            var name = UITheme.Label(d.Name, 17, Art.C(d.Colour), wrap: false);
            name.CustomMinimumSize = new Vector2(150, 0);
            row.AddChild(name);
            row.AddChild(new TurfBar { Shares = C.TurfOf(d.Id), CustomMinimumSize = new Vector2(420, 18), SizeFlagsVertical = SizeFlags.ShrinkCenter });
            row.AddChild(UITheme.Label($"held by {UITheme.FactionMark((int)C.Holder(d.Id))} {Tables.FactionNames[(int)C.Holder(d.Id)]}", 15, FactionInk[(int)C.Holder(d.Id)], wrap: false));
            list.AddChild(row);
        }
        list.AddChild(UITheme.Spacer(8));
        list.AddChild(UITheme.Label("THIS WEEK", 15, UITheme.Dim));
        var today = C.HappeningLines().ToList();
        if (today.Count == 0) list.AddChild(UITheme.Label("Nothing out of the ordinary. Which in this city is something.", 16, UITheme.Text));
        foreach (string h in today) list.AddChild(UITheme.Label(h, 16, UITheme.Yellow));
        if (C.Gossip() is string g) list.AddChild(UITheme.Label($"On the street: \"{g}\"", 15, UITheme.Cyan));
        list.AddChild(UITheme.Spacer(8));
        list.AddChild(UITheme.Label("YOUR NAME", 15, UITheme.Dim));
        list.AddChild(UITheme.Label($"Street rep {C.Rep} -- shows jobs up to threat {C.MaxThreat}.", 16, UITheme.Good));
        list.AddChild(UITheme.Label($"Notoriety {C.Notoriety}/10 -- " + (C.Notoriety >= 7 ? "the clean contacts only patch you up; set-ups are likelier." : C.Notoriety >= 4 ? "Ichikawa and Sabine charge you more; set-ups are likelier." : C.Notoriety > 0 ? "a few people have heard things." : "clean, as far as anyone knows.") + " It never fades.", 16, C.Notoriety >= 4 ? UITheme.Bad : UITheme.Text));
        list.AddChild(UITheme.Label($"Public awareness {C.Awareness}/10 -- " + (C.Awareness >= 6 ? "you are on the news; they know where you live." : C.Awareness >= 4 ? "Lone Star knows your face: every day starts with heat." : C.Awareness > 0 ? "a rumour on the street." : "nobody.") + " Fades a point every four days.", 16, C.Awareness >= 4 ? UITheme.Yellow : UITheme.Text));
        list.AddChild(UITheme.Label($"Etiquettes: {(C.Etiquettes.Count > 0 ? string.Join(", ", C.Etiquettes) : "none")} (learn more on the Runner tab at home).", 15, UITheme.Violet));
        if (C.RentOwed > 0) list.AddChild(UITheme.Label($"Rent owed: {M.Money(C.RentOwed)}.", 16, UITheme.Bad));
        else list.AddChild(UITheme.Label($"Living: {C.LifestyleLine}", 15, UITheme.Dim));
        list.AddChild(UITheme.Spacer(8));
        list.AddChild(UITheme.Label("CREW", 15, UITheme.Dim));
        foreach (var h in Tables.Hires)
        {
            CrewState cs = C.Crew.GetValueOrDefault(h.id);
            string state = cs == null ? "never worked with you" : cs.Dead ? "dead" : $"trust {cs.Trust}/5" + (cs.Perks.Count > 0 ? ", knows " + string.Join(", ", cs.Perks.Select(Career.PerkName)) : "") + (cs.OutUntil >= C.Day ? $", recovering until day {cs.OutUntil + 1}" : "") + (C.Partner == h.id ? ", your partner" : "");
            list.AddChild(UITheme.Label($"{h.name} -- {state}. Cut {C.CrewCut(h.id) * 100:0} %.", 16, cs?.Dead == true ? UITheme.Faint : UITheme.Text));
        }
    }

    /// <summary>A district's turf as one bar, a stretch per faction.</summary>
    private partial class TurfBar : Control
    {
        public float[] Shares;

        public override void _Draw()
        {
            float x = 0;
            for (int k = 0; k < 3; k++)
            {
                float w = Size.X * Shares[k];
                if (w > 0.5f) DrawRect(new Rect2(x, 0, w, Size.Y), FactionInk[k] with { A = 0.8f });
                x += w;
            }
            DrawRect(new Rect2(Vector2.Zero, Size), UITheme.PanelEdge, false, 1);
        }
    }

    private void Stats(VBoxContainer list)
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 40);
        grid.AddThemeConstantOverride("v_separation", 4);
        void S(string name, string value)
        {
            grid.AddChild(UITheme.Label(name, 16, UITheme.Text, wrap: false));
            grid.AddChild(UITheme.Label(value, 16, UITheme.Cyan, wrap: false));
        }
        S("Days survived", C.Day.ToString());
        S("Time played", M.Played(C.PlaySeconds));
        S("Jobs done / failed", $"{C.JobsDone} / {C.JobsFailed}");
        foreach (JobType t in Enum.GetValues<JobType>().Where(t => t != JobType.Hub))
            S($"  {Tables.JobNames[(int)t]}", $"{C.Stat($"done.{t}")} done, {C.Stat($"failed.{t}")} failed");
        S("Without an alarm", C.Stat("noalarm").ToString());
        S("Without a kill", C.Stat("nokill").ToString());
        S("Kills / knockouts", $"{C.Kills} / {C.Stat("kos")}");
        S("Bosses", C.Stat("bosses").ToString());
        S("Walls brought down", C.Stat("walls").ToString());
        S("Spells cast", C.Stat("spells").ToString());
        S("Matrix downloads", C.Stat("matrix").ToString());
        S("Nuyen earned / spent", $"{M.Money(C.Stat("earned"))} / {M.Money(C.Stat("spent"))}");
        S("Distance driven", $"{C.Stat("tiles") * 4 / 1000f:0.0} km");
        S("Busted / ambushed", $"{C.Stat("busts")} / {C.Stat("ambushes")}");
        S("Highest heat", $"{C.Stat("maxheat")} stars");
        S("Deaths (TraumaLink)", $"{C.Deaths} ({C.Stat("traumalink")})");
        S("Story", $"chapter {C.Chapter}/4, contact stories {Career.Arcs.Keys.Count(C.ArcDone)}/{Career.Arcs.Count}");
        S("Campaigns", $"Toxic Tide {C.Chapter2}/4, " + string.Join(", ", Career.Campaigns.Select(d => $"{d.Name} {C.Campaign.GetValueOrDefault(d.Id)}/4")));
        S("Heists", C.Flag("heists").ToString());
        list.AddChild(grid);
    }

    private void Achieve(VBoxContainer list)
    {
        var have = Profile.Current.Achieved;
        list.AddChild(UITheme.Label($"{have.Count} / {Achievements.All.Length} unlocked (across all careers).", 16, UITheme.Dim));
        foreach (var a in Achievements.All)
        {
            bool got = have.Contains(a.Id);
            list.AddChild(UITheme.Label($"{(got ? "✓" : "·")}  {a.Name} -- {a.Desc}", 17, got ? UITheme.Yellow : UITheme.Faint));
        }
    }
}
