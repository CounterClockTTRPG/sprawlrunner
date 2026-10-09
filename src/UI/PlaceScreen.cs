using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>
/// Inside a place: the safehouse (terminal, runner sheet, loadout, upgrades,
/// garage, sleep), a contact (their shop), or a job site (the briefing and
/// the way in). The city waits while this is open.
/// </summary>
public partial class PlaceScreen : Control
{
    private readonly Root _root;
    private readonly Lot _lot;
    private readonly string _kind;
    public Action Closed;
    private VBoxContainer _content;
    private HBoxContainer _tabs;
    private string _tab;
    private Label _status;

    private Career C => _root.Career;
    /// <summary>Off in the walkthrough's shop and terminal shots, so a scene never covers them.</summary>
    public static bool Scenes = true;

    public PlaceScreen(Root root, Lot lot, string kind)
    {
        _root = root;
        _lot = lot;
        _kind = kind;
    }

    public override void _Ready()
    {
        UITheme.Fill(this);
        Theme = UITheme.Theme;
        MouseFilter = MouseFilterEnum.Stop;
        var bg = new ColorRect { Color = new Color(0.01f, 0.015f, 0.03f, 0.985f) };
        UITheme.Fill(bg);
        AddChild(bg);
        UITheme.PowerOn(this);
        if (_kind == "job") { BuildBriefing(); return; }

        var outer = new MarginContainer();
        UITheme.Fill(outer);
        foreach (string s in new[] { "left", "right", "top", "bottom" }) outer.AddThemeConstantOverride($"margin_{s}", 36);
        AddChild(outer);
        var cols = UITheme.HBox(26);
        outer.AddChild(cols);

        // Who is here.
        var left = UITheme.VBox(10);
        left.CustomMinimumSize = new Vector2(340, 0);
        cols.AddChild(left);
        bool home = _kind is "home" or "loft";
        ContactDef k = home ? null : Tables.Contact(_kind);
        _portrait = new Portrait { ClipContents = true, Contact = k, Seed = home ? 1 : Array.IndexOf(Tables.Contacts, k) + 2, CustomMinimumSize = new Vector2(340, 300), Picture = home ? HomePicture() : null };
        left.AddChild(_portrait);
        left.AddChild(UITheme.Label(home ? (C.HasUpgrade("loft") ? "SPIRE LOFT" : "SAFEHOUSE") : k.Venue.ToUpperInvariant(), 30, home ? UITheme.Good : Art.C(k.Colour), heading: true));
        if (k != null)
        {
            left.AddChild(UITheme.Label($"{k.Name} -- {k.Role}", 19, UITheme.Text));
            int loy = C.Loyalty.GetValueOrDefault(k.Id);
            var lrow = UITheme.HBox(8);
            lrow.AddChild(UITheme.Label("Loyalty", 16, UITheme.Yellow, wrap: false));
            lrow.AddChild(new Pips { Value = loy });
            lrow.AddChild(UITheme.Label(k.Id == "fence" ? $"(pays {C.SellMul * 100:0} % of value)" : $"(prices -{(1 - C.PriceMul(k.Id)) * 100:0} %)", 16, UITheme.Yellow, wrap: false));
            left.AddChild(lrow);
            left.AddChild(UITheme.Label("Loyalty grows with jobs for them, and with every ¥8,000 spent here (up to 3).", 13, UITheme.Faint));
            int greeting = (C.Day + loy) % k.Greetings.Length;
            left.AddChild(UITheme.Label($"\"{k.Greetings[greeting]}\"", 18, UITheme.Cyan));
            Sfx.Instance?.Voice(Assets.Voice(Assets.Greeting(k, greeting)));
            left.AddChild(UITheme.Label(k.Blurb, 15, UITheme.Dim));
            if (C.Shut(k.Id) is string shut) left.AddChild(UITheme.Label(shut, 17, UITheme.Bad));
        }
        else
        {
            // How you live here: the tier, what it does for you, and the rent.
            _lifeHead = UITheme.Label("", 17, UITheme.Amber, heading: true);
            _lifeDesc = UITheme.Label("", 15, UITheme.Dim);
            _lifeRent = UITheme.Label("", 14, UITheme.Faint);
            left.AddChild(_lifeHead);
            left.AddChild(_lifeDesc);
            left.AddChild(_lifeRent);
            ShowLifestyle();
        }
        left.AddChild(UITheme.Spacer(10));
        _status = UITheme.Label("", 18, UITheme.Yellow);
        left.AddChild(_status);
        left.AddChild(UITheme.Spacer(10));
        if (home) left.AddChild(UITheme.Button("Sleep until tomorrow", Sleep, 0, 42, "A new day: the board refreshes. Saves the career."));
        // A career with a story to end on can end by choice, not only by death.
        if (home && C.Active == null && C.RetireReason() is string why)
            left.AddChild(UITheme.Button("Retire...", () => Modal.Confirm(this, "WALK AWAY?", $"{why} You could stop here: the career ends, with its receipt and whatever legacy it earned. There is no coming back.", "Retire", () =>
            {
                C.Retired = true;
                Leave();
            }), 0, 42, "End the career on your own terms: the receipt and the legacy."));
        left.AddChild(UITheme.Button("Back to the car  [Esc]", Leave, 0, 42));

        var right = UITheme.VBox(10);
        right.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        cols.AddChild(right);
        _tabs = UITheme.HBox(8);
        right.AddChild(_tabs);
        var panel = UITheme.Panel(_content = UITheme.VBox(8), 16);
        panel.SizeFlagsVertical = SizeFlags.ExpandFill;
        right.AddChild(panel);

        string[] tabs = _kind switch
        {
            "home" or "loft" => new[] { "Terminal", "Runner", "Loadout", "Upgrades", "Garage" },
            "fixer" => new[] { "Terminal", "Intel" },
            "doc" => new[] { "Clinic" },
            "arms" => new[] { "Guns & gear" },
            "rigger" => new[] { "Vehicles" },
            "decker" => new[] { "Programs", "Decks", "Paydata" },
            "talismonger" => new[] { "Spells" },
            "cop" => new[] { "Precinct" },
            "press" => new[] { "Newsroom" },
            _ => new[] { "Sell loot" },
        };
        foreach (string t in tabs)
        {
            string tt = t;
            var b = UITheme.Button(t, () => Show(tt), 150, 40);
            b.ToggleMode = true;
            b.Name = t.Replace(" ", "_").Replace("&", "and");
            _tabs.AddChild(b);
        }
        Show(tabs[0]);
        Refresh();
        // Someone has something to say: a scene as you walk in.
        if (Scenes && C.SceneAt(home ? "home" : _kind) is Mail scene)
            CallDeferred(Node.MethodName.AddChild, new StoryModal(C, scene) { Answered = () => { Refresh(); Show(_tab); } });
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape } && Modal.Open == 0)
        {
            Leave();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Leave()
    {
        Sfx.Instance?.Voice(null);
        Sfx.Instance?.Play(Sound.UiBack);
        QueueFree();
        Closed?.Invoke();
    }

    private void Refresh()
    {
        if (_status != null)
            _status.Text = $"{M.Money(C.Cash)}   karma {C.Karma}   rep {C.Rep}   day {C.Day}";
        ShowLifestyle();
    }

    private Portrait _portrait;
    private Label _lifeHead, _lifeDesc, _lifeRent;

    /// <summary>The safehouse picture for how you live now (place-home if that one hasn't been painted).</summary>
    private string HomePicture()
    {
        string p = Assets.HomePicture(C.Lifestyle, C.HasUpgrade("loft"));
        return Assets.Picture(p) != null ? p : "place-home";
    }

    /// <summary>How you live, on the left: the picture, the tier, what it does, the rent. Moves when you do.</summary>
    private void ShowLifestyle()
    {
        if (_lifeHead == null) return;
        var life = Career.Lifestyles[C.LifeTier];
        _lifeHead.Text = $"{life.name.ToUpper()} LIFESTYLE";
        _lifeDesc.Text = life.desc;
        _lifeRent.Text = C.RentOwed > 0 ? $"Rent owed: {M.Money(C.RentOwed)}." : $"Rent {M.Money(C.RentDue)} a week, paid {M.Money(C.DailyRent)} every morning. Change it on the Upgrades tab.";
        _lifeRent.LabelSettings.FontColor = C.RentOwed > 0 ? UITheme.Bad : UITheme.Faint;
        string pic = HomePicture();
        if (_portrait != null && _portrait.Picture != pic) { _portrait.Picture = pic; _portrait.QueueRedraw(); }
    }

    public void Show(string tab)
    {
        _tab = tab;
        ShowLifestyle();
        foreach (Node n in _tabs.GetChildren()) if (n is Button b) b.ButtonPressed = b.Text == tab;
        foreach (Node n in _content.GetChildren()) n.QueueFree();
        switch (tab)
        {
            case "Terminal":
                var term = new Terminal { Root = _root, Changed = Refresh, Escape = Leave, SizeFlagsVertical = SizeFlags.ExpandFill };
                _content.AddChild(term);
                break;
            case "Runner": RunnerTab(); break;
            case "Loadout": LoadoutTab(); break;
            case "Upgrades": Offers("home", $"Safehouse upgrades, bought once and kept for good -- and how you live (now {Career.Lifestyles[C.LifeTier].name.ToLower()}: {M.Money(C.RentDue)} a week).", o => o.Kind is ShopKind.Upgrade or ShopKind.Lifestyle); break;
            case "Garage": GarageTab(); break;
            case "Intel": Offers("fixer", "Juno sells what she knows about your current job's site."); break;
            case "Clinic": Offers("doc", $"Essence {C.Essence:0.00} / 6.00. Chrome eats your essence; medkits work worse the lower it gets."); break;
            case "Guns & gear": Offers("arms", "Weapons, armour, grenades and medkits. Mods fit weapons you own."); break;
            case "Vehicles": Offers("rigger", "Wheels. The one you buy is the one you drive away in."); break;
            case "Programs": Offers("decker", $"Programs run loaded in your deck's memory ({C.ActiveDeck.Name}: {C.MemoryFree}/{C.Memory} Mp free; load them in the safehouse Loadout tab). Paydata you pull shares the same memory.", o => o.Kind is not (ShopKind.Deck or ShopKind.Sell)); break;
            case "Decks": Offers("decker", $"Null's hardware. You carry the {C.ActiveDeck.Name} (rating {C.ActiveDeck.Rating}, {C.Memory} Mp). Arrays are Attack, Sleaze, Data processing, Firewall -- deal them out as you like in the Loadout tab.", o => o.Kind == ShopKind.Deck); break;
            case "Paydata": Offers("decker", "Null buys paydata at what it's worth. Wen pays her cut, like for anything else.", o => o.Kind == ShopKind.Sell); break;
            case "Precinct": Offers("cop", "Dace sells what his badge is worth. Cash, and he never met you."); break;
            case "Newsroom": Offers("press", "Quill buys true stories, kills false ones, and prints the ones that hurt the right people."); break;
            case "Spells": Offers("talismonger", C.Awakened ? $"Spell power x{C.SpellPower:0.00} (essence {C.Essence:0.00})." : "Sabine only teaches the Awakened. You are not."); break;
            default: Offers("fence", $"Wen pays {C.SellMul * 100:0} % of the value."); break;
        }
    }

    // ---- shops ----------------------------------------------------------------------

    private static string GroupOf(ShopKind k) => k switch
    {
        ShopKind.Weapon => "Guns", ShopKind.Grenade or ShopKind.Medkit => "Gear", ShopKind.Armor => "Armour", ShopKind.Mod or ShopKind.Ammo => "Mods",
        ShopKind.Cyber or ShopKind.Grade or ShopKind.Heal => "Chrome", ShopKind.TraumaLink => "TraumaLink", ShopKind.Vehicle or ShopKind.Repair => "Vehicles",
        ShopKind.Drone => "Drones", ShopKind.Deck => "Decks", ShopKind.Program => "Programs", ShopKind.CarMod => "Car work", ShopKind.Property or ShopKind.Retainer => "Business", ShopKind.Favour or ShopKind.Hire => "Favours",
        _ => "Other",
    };

    private string _group = "All";
    private bool _cheap;

    private void Offers(string contact, string intro, Func<Offer, bool> only = null)
    {
        if (intro.Length > 0) _content.AddChild(UITheme.Label(intro, 16, UITheme.Dim));
        var list = UITheme.VBox(6);
        var all = C.Offers(contact).Where(o => only == null || only(o)).ToList();
        // A long counter gets filters: by kind, what you can afford, cheapest first.
        var groups = all.Select(o => GroupOf(o.Kind)).Distinct().ToList();
        if (all.Count > 8)
        {
            var bar = new HFlowContainer();
            foreach (string g in groups.Prepend("Affordable").Prepend("All"))
            {
                string gg = g;
                var b = UITheme.Button(g, () => { _group = gg; Show(_tab); }, 0, 30);
                b.ToggleMode = true;
                b.ButtonPressed = _group == g;
                bar.AddChild(b);
            }
            var sort = UITheme.Button(_cheap ? "Cheapest first ✓" : "Cheapest first", () => { _cheap = !_cheap; Show(_tab); }, 0, 30);
            bar.AddChild(sort);
            _content.AddChild(bar);
        }
        if (_group != "All" && _group != "Affordable" && !groups.Contains(_group)) _group = "All";
        var offers = all.Where(o => _group == "All" || _group == "Affordable" ? (_group == "All" || o.Block == null) : GroupOf(o.Kind) == _group).ToList();
        if (_cheap) offers = offers.OrderBy(o => o.Block != null).ThenBy(o => o.Price).ToList();
        if (offers.Count == 0 && intro.Length > 0) list.AddChild(UITheme.Label(contact == "fence" ? "You have nothing to sell. Loot found on runs ends up here." : "Nothing for you right now.", 17, UITheme.Dim));
        foreach (Offer o in offers)
        {
            var row = UITheme.HBox(12);
            var text = UITheme.VBox(0);
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            text.AddChild(UITheme.Label(o.Name + (o.Owned != null ? $"   [{o.Owned}]" : ""), 18, o.Block == "Owned." || o.Block == "Installed." ? UITheme.Dim : UITheme.Text, heading: true));
            text.AddChild(UITheme.Label(o.Desc, 14, UITheme.Dim));
            row.AddChild(text);
            bool sell = o.Kind is ShopKind.Sell or ShopKind.SellAll || o.Kind == ShopKind.Service && o.Extra != null;
            string price = o.Kind == ShopKind.Service && o.Extra != null ? "+" + M.Money(int.Parse(o.Extra)) : o.Price > 0 ? (sell ? "+" : "") + M.Money(o.Price) : "free";
            var tag = new PriceTag { Text = price, Ink = sell ? Dressing.InkGreen : Dressing.Ink, Seed = o.Name.GetHashCode(), Faded = o.Block != null, TooltipText = price };
            tag.CustomMinimumSize = new Vector2(Math.Max(104, price.Length * 11 + 36), 32);
            row.AddChild(tag);
            Offer oo = o;
            var b = UITheme.Button(sell ? "Sell" : o.Kind is ShopKind.Favour or ShopKind.Hire ? "Ask" : "Deal", () => Buy(oo), 90, 36, o.Block ?? "");
            b.Disabled = o.Block != null;
            row.AddChild(b);
            list.AddChild(row);
            if (o.Block != null && o.Block != "Owned." && o.Block != "Installed.")
                list.AddChild(UITheme.Label("    " + o.Block, 13, UITheme.Bad));
            list.AddChild(UITheme.Rule(UITheme.Faint));
        }
        var scroll = UITheme.Scroll(list, 560);
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        _content.AddChild(scroll);
    }

    private void Buy(Offer o)
    {
        string err = C.Buy(o);
        if (err != null) { Sfx.Instance?.Play(Sound.Error); _status.Text = err; return; }
        Sfx.Instance?.Play(Sound.Coin);
        Profile.SaveCareer(C);
        _root.CitySim?.RefreshVehicle();
        // Keep the list where it was.
        int scroll = FindScroll(_content)?.ScrollVertical ?? 0;
        Show(_tab);
        GetTree().CreateTimer(0.05).Timeout += () => { if (IsInstanceValid(this) && FindScroll(_content) is ScrollContainer sc) sc.ScrollVertical = scroll; };
        Refresh();
    }

    private static ScrollContainer FindScroll(Node n)
    {
        foreach (Node c in n.GetChildren())
        {
            if (c is ScrollContainer s && !s.IsQueuedForDeletion()) return s;
            if (FindScroll(c) is ScrollContainer d) return d;
        }
        return null;
    }

    // ---- the runner -----------------------------------------------------------------

    private void RunnerTab()
    {
        _content.AddChild(UITheme.Label($"{C.Name} -- {C.ArchetypeName}.   Karma to spend: {C.Karma}", 20, UITheme.Cyan, heading: true));
        var cols = UITheme.HBox(30);
        _content.AddChild(cols);
        var a = UITheme.VBox(4);
        a.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        cols.AddChild(a);
        a.AddChild(UITheme.Label("ATTRIBUTES", 15, UITheme.Dim));
        for (int i = 0; i < 6; i++)
        {
            var attr = (Attr)i;
            int cost = C.AttrCost(attr);
            a.AddChild(StatRow(Tables.AttrNames[i], C.Attrs[i], Tables.AttrDesc[i], cost, () => C.RaiseAttr(attr)));
        }
        a.AddChild(UITheme.Spacer(6));
        a.AddChild(UITheme.Label("SKILLS", 15, UITheme.Dim));
        for (int i = 0; i < 7; i++)
        {
            var sk = (Skill)i;
            int cost = C.SkillCost(sk);
            a.AddChild(StatRow(Tables.SkillNames[i], C.Skills[i], Tables.SkillDesc[i], cost, () => C.RaiseSkill(sk)));
        }
        var d = UITheme.VBox(4);
        d.CustomMinimumSize = new Vector2(330, 0);
        cols.AddChild(d);
        d.AddChild(UITheme.Label("CONDITION", 15, UITheme.Dim));
        void Stat(string n, string v) { var r = UITheme.HBox(); r.AddChild(UITheme.Grow(UITheme.Label(n, 16, UITheme.Text))); r.AddChild(UITheme.Label(v, 16, UITheme.Cyan, HorizontalAlignment.Right, wrap: false)); d.AddChild(r); }
        Stat("Max HP", $"{C.MaxHp:0}");
        Stat("Armour", $"{C.ArmorValue}");
        Stat("Speed", $"{C.MoveSpeed:0.0} tiles/s");
        Stat("Essence", $"{C.Essence:0.00}");
        Stat("Sight", $"{C.Fov} tiles{(C.Thermal ? " + thermal" : "")}");
        Stat("Throw range", $"{C.ThrowRange:0} tiles");
        Stat("Hacking speed", $"x{C.HackRate:0.0}");
        Stat("Deck", $"{C.ActiveDeck.Name}, {C.DeckBoxes} boxes, {C.Memory} Mp");
        Stat("Dash cooldown", $"{C.DashCooldown:0.0} s");
        Stat("Grenades carried", $"{C.GrenadeCap} per type");
        d.AddChild(UITheme.Spacer(6));
        d.AddChild(UITheme.Label("CYBERWARE", 15, UITheme.Dim));
        if (C.Cyber.Count == 0) d.AddChild(UITheme.Label("All meat.", 15, UITheme.Dim));
        foreach (string id in C.Cyber) d.AddChild(UITheme.Label($"{Tables.CyberById(id).Name} ({Tables.CyberById(id).Essence:0.0})", 15, UITheme.Violet));
        d.AddChild(UITheme.Spacer(6));
        d.AddChild(UITheme.Label($"Jobs {C.JobsDone} done, {C.JobsFailed} failed.  Kills {C.Kills}.", 15, UITheme.Dim));
        if (C.Spells.Count > 0)
        {
            d.AddChild(UITheme.Spacer(6));
            d.AddChild(UITheme.Label("SPELLS (practice: +15 % a rank)", 15, UITheme.Dim));
            foreach (string sp in C.Spells)
            {
                string spp = sp;
                int cost = C.SpellRankCost(sp);
                var srow = UITheme.HBox(8);
                srow.AddChild(UITheme.Grow(UITheme.Label($"{Tables.Spells.First(x => x.Id == sp).Name}  {new string('■', C.SpellRank.GetValueOrDefault(sp))}{new string('□', 3 - C.SpellRank.GetValueOrDefault(sp))}", 15, UITheme.Violet)));
                var sb = UITheme.Button(cost < 0 ? "mastered" : $"+ ({cost})", () =>
                {
                    string err = C.RaiseSpell(spp);
                    if (err != null) { Sfx.Instance?.Play(Sound.Error); _status.Text = err; return; }
                    Sfx.Instance?.Play(Sound.Heal);
                    Profile.SaveCareer(C);
                    Show(_tab);
                    Refresh();
                }, 0, 28, cost < 0 ? "Mastered." : $"Spend {cost} karma.");
                sb.Disabled = cost < 0 || C.Karma < cost;
                srow.AddChild(sb);
                d.AddChild(srow);
            }
        }
        d.AddChild(UITheme.Spacer(6));
        d.AddChild(UITheme.Label($"EDGE  {C.EdgeMax} a run", 15, UITheme.Dim));
        d.AddChild(UITheme.Label("Luck you make: the last point saves you from a killing blow; spare points slip a detection or a Matrix trace. Charisma gives some; karma buys up to two more.", 13, UITheme.Faint));
        int ec = C.EdgeCost;
        var eb = UITheme.Button(ec < 0 ? "Edge: as lucky as anyone gets" : $"+1 edge ({ec} karma)", () =>
        {
            string err = C.BuyEdge();
            if (err != null) { Sfx.Instance?.Play(Sound.Error); _status.Text = err; return; }
            Sfx.Instance?.Play(Sound.Heal);
            Profile.SaveCareer(C);
            Show(_tab);
            Refresh();
        }, 0, 30, ec < 0 ? "Bought all there is." : $"Spend {ec} karma.");
        eb.Disabled = ec < 0 || C.Karma < ec || C.EdgeMax >= Tables.EdgeMax;
        d.AddChild(eb);
        d.AddChild(UITheme.Spacer(6));
        d.AddChild(UITheme.Label("ETIQUETTE", 15, UITheme.Dim));
        d.AddChild(UITheme.Label("The codes you can talk. They open choices when people talk to you.", 13, UITheme.Faint));
        var et = new HFlowContainer();
        foreach (string e in Career.AllEtiquettes)
        {
            string ee = e;
            int cost = (int)MathF.Round(Career.EtiquetteCost * C.KarmaMul);
            var b = UITheme.Button(C.Talks(e) ? $"{e} ✓" : $"{e} ({cost})", () =>
            {
                string err = C.LearnEtiquette(ee);
                if (err != null) { Sfx.Instance?.Play(Sound.Error); _status.Text = err; return; }
                Sfx.Instance?.Play(Sound.Heal);
                Profile.SaveCareer(C);
                Show(_tab);
                Refresh();
            }, 0, 30, C.Talks(e) ? "You talk it." : $"Learn it for {cost} karma.");
            b.Disabled = C.Talks(e) || C.Karma < cost;
            et.AddChild(b);
        }
        d.AddChild(et);
    }

    private Control StatRow(string name, int value, string desc, int cost, Func<string> raise)
    {
        var row = UITheme.HBox(10);
        var n = UITheme.Label(name, 17, UITheme.Text, wrap: false);
        n.CustomMinimumSize = new Vector2(140, 0);
        n.TooltipText = desc;
        n.MouseFilter = MouseFilterEnum.Pass;
        row.AddChild(n);
        row.AddChild(UITheme.Label(new string('■', value) + new string('□', 6 - value), 17, UITheme.Cyan, wrap: false));
        row.AddChild(UITheme.Grow(UITheme.Label(desc, 13, UITheme.Dim)));
        var b = UITheme.Button(cost < 0 ? "max" : $"+ ({cost} karma)", () =>
        {
            string err = raise();
            if (err != null) { Sfx.Instance?.Play(Sound.Error); _status.Text = err; return; }
            Sfx.Instance?.Play(Sound.Heal);
            Profile.SaveCareer(C);
            Show(_tab);
            Refresh();
        }, 130, 30, cost < 0 ? "Already at the maximum." : C.Karma < cost ? $"Needs {cost} karma (you have {C.Karma})." : $"Spend {cost} karma.");
        b.Disabled = cost < 0 || C.Karma < cost;
        row.AddChild(b);
        return row;
    }

    // ---- loadout ----------------------------------------------------------------------

    private void LoadoutTab()
    {
        _content.AddChild(UITheme.Label("What you carry onto a run.", 16, UITheme.Dim));
        var guns = C.Weapons.Where(w => !Tables.Weapon(w).Melee).ToList();
        Choice("Primary  [1]", guns.Prepend(null).ToList(), C.Primary, v => { if (v != null && v == C.Secondary) C.Secondary = C.Primary; C.Primary = v; }, id => id == null ? "--" : Tables.Weapon(id).Name);
        Choice("Secondary  [2]", guns.Prepend(null).ToList(), C.Secondary, v => { if (v != null && v == C.Primary) C.Primary = C.Secondary; C.Secondary = v; }, id => id == null ? "--" : Tables.Weapon(id).Name);
        Choice("Melee  [3]", C.Weapons.Where(w => Tables.Weapon(w).Melee).ToList(), C.Melee, v => C.Melee = v, id => Tables.Weapon(id).Name + (id == "knife" && C.Has("spurs") ? " (spurs)" : ""));
        Choice("Armour", C.ArmorsOwned, C.ArmorId, v => C.ArmorId = v, id => $"{Tables.Armor(id).Name} ({Tables.Armor(id).Armor})");
        var ab = C.Abilities.Distinct().Prepend(Ability.None).ToList();
        Choice("Ability  [Q]", ab, C.SlotQ, v => { if (v != Ability.None && v == C.SlotF) C.SlotF = C.SlotQ; C.SlotQ = v; }, Career.AbilityName);
        Choice("Ability  [F]", ab, C.SlotF, v => { if (v != Ability.None && v == C.SlotQ) C.SlotQ = C.SlotF; C.SlotF = v; }, Career.AbilityName);
        foreach (string gid in new[] { C.Primary, C.Secondary }.Where(x => x != null).Distinct())
        {
            WeaponDef gw = Tables.Weapon(gid);
            if (gw.Blast > 0 || gw.NonLethal || C.AmmoOwned.Count == 0) continue;
            string g2 = gid;
            Choice($"Ammo: {gw.Name}", C.AmmoOwned.Prepend("standard").ToList(), C.Ammo(gw), v => C.AmmoFor[g2] = v, a => a == "standard" ? "Standard" : Tables.AmmoTypes.First(x => x.id == a).name);
        }
        // Two kits, saved and swapped in one click.
        var kits = UITheme.HBox(8);
        kits.AddChild(UITheme.Label("Kits", 17, UITheme.Text, wrap: false));
        foreach (string k in new[] { "A", "B" })
        {
            string kk = k;
            kits.AddChild(UITheme.Button($"Save as kit {k}", () => { C.SaveKit(kk); Profile.SaveCareer(C); _status.Text = $"Kit {kk} saved."; }, 0, 32));
            var use = UITheme.Button($"Use kit {k}", () => { string err = C.UseKit(kk); _status.Text = err ?? $"Kit {kk} on."; Profile.SaveCareer(C); Show(_tab); }, 0, 32);
            use.Disabled = !C.Kits.ContainsKey(k);
            kits.AddChild(use);
        }
        _content.AddChild(kits);
        if (C.Drones.Count > 0)
            Choice("Drone  [T]", C.Drones.Prepend(null).ToList(), C.DroneSel, v => C.DroneSel = v, id => id == null ? "--" : Tables.Drones.First(d => d.Id == id).Name);
        _content.AddChild(UITheme.Spacer(8));
        string nades = string.Join(",  ", Tables.Grenades.Where(g => C.Grenades.GetValueOrDefault(g.Id) > 0).Select(g => $"{g.Name} x{C.Grenades[g.Id]}"));
        _content.AddChild(UITheme.Label($"Grenades: {(nades.Length > 0 ? nades : "none")}   ·   Medkits: {C.Medkits}", 16, UITheme.Text));
        if (C.Vehicle == "van") _content.AddChild(UITheme.Label("The van carries one more of each grenade type you own.", 15, UITheme.Good));
        if (C.Mods.Count > 0) _content.AddChild(UITheme.Label("Mods: " + string.Join(", ", C.Mods.Select(m => $"{Tables.Weapon(m.Split(':')[0]).Name} {Tables.Mods.First(x => x.Id == m.Split(':')[1]).Name.ToLower()}")), 15, UITheme.Dim));
        if (C.Spells.Count > 0) _content.AddChild(UITheme.Label($"Spells: {string.Join(", ", C.Spells.Select(id => Tables.Spells.First(x => x.Id == id).Name))}  (power x{C.SpellPower:0.00})", 15, UITheme.Violet));
        DeckSection();
    }

    /// <summary>The deck: which one, how its values are dealt out, which programs are loaded into its memory.</summary>
    private void DeckSection()
    {
        _content.AddChild(UITheme.Rule(UITheme.Faint));
        Choice("Deck", C.DecksOwned.ToList(), C.ActiveDeck.Id, v => C.CarryDeck(v), id => $"{Tables.Deck(id).Name} (R{Tables.Deck(id).Rating}, {Tables.Deck(id).Memory} Mp)");
        int[] v = C.DeckValues(C.ActiveDeck.Id);
        var row = UITheme.HBox(8);
        var l = UITheme.Label("Array", 17, UITheme.Text, wrap: false);
        l.CustomMinimumSize = new Vector2(160, 0);
        row.AddChild(l);
        row.AddChild(UITheme.Label($"Attack {v[0]}  ·  Sleaze {v[1]}  ·  Data {v[2]}  ·  Firewall {v[3]}{(C.HotSim ? "   (hot-sim)" : "")}", 16, UITheme.Term, wrap: false));
        foreach (var (a, b) in new[] { (MxAttr.Attack, MxAttr.Sleaze), (MxAttr.Sleaze, MxAttr.Data), (MxAttr.Data, MxAttr.Firewall), (MxAttr.Attack, MxAttr.Firewall) })
        {
            var (aa, bb) = (a, b);
            row.AddChild(UITheme.Button($"{Tables.MxAttrNames[(int)a][0]}<>{Tables.MxAttrNames[(int)b][0]}", () => { C.SwapDeck(aa, bb); Profile.SaveCareer(C); Show(_tab); }, 0, 30,
                $"Swap {Tables.MxAttrNames[(int)a]} ({Tables.MxAttrDesc[(int)a]}) and {Tables.MxAttrNames[(int)b]} ({Tables.MxAttrDesc[(int)b]})."));
        }
        _content.AddChild(row);
        var prow = UITheme.HBox(8);
        var pl = UITheme.Label($"Programs\n{C.MemoryUsed}/{C.Memory} Mp", 17, UITheme.Text, wrap: false);
        pl.CustomMinimumSize = new Vector2(160, 0);
        prow.AddChild(pl);
        var flow = new HFlowContainer();
        flow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        foreach (string id in C.Programs)
        {
            ProgramDef p = Tables.Program(id);
            string pid = id;
            var b = UITheme.Button($"{p.Name} ({p.Mp} Mp{(p.Active ? ", key" : "")})", () =>
            {
                string err = C.ToggleProgram(pid);
                if (err != null) { Sfx.Instance?.Play(Sound.Error); _status.Text = err; return; }
                Profile.SaveCareer(C);
                Show(_tab);
            }, 0, 34, p.Desc);
            b.ToggleMode = true;
            b.ButtonPressed = C.HasProgram(id);
            flow.AddChild(b);
        }
        if (C.Programs.Count == 0) flow.AddChild(UITheme.Label("none -- Null sells them", 15, UITheme.Dim, wrap: false));
        prow.AddChild(flow);
        _content.AddChild(prow);
        if (C.MemoryFree < 3) _content.AddChild(UITheme.Label($"Only {C.MemoryFree} Mp free: a datasteal's three datastores need 3 (G purges a program inside, if it comes to that).", 14, UITheme.Yellow));
    }

    private void Choice<T>(string label, System.Collections.Generic.List<T> options, T current, Action<T> set, Func<T, string> name)
    {
        var row = UITheme.HBox(8);
        var l = UITheme.Label(label, 17, UITheme.Text, wrap: false);
        l.CustomMinimumSize = new Vector2(160, 0);
        row.AddChild(l);
        var flow = new HFlowContainer();
        flow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        foreach (T o in options)
        {
            T oo = o;
            var b = UITheme.Button(name(o), () => { set(oo); Profile.SaveCareer(C); Show(_tab); }, 0, 34);
            b.ToggleMode = true;
            b.ButtonPressed = Equals(o, current);
            flow.AddChild(b);
        }
        row.AddChild(flow);
        _content.AddChild(row);
    }

    private void GarageTab()
    {
        _content.AddChild(UITheme.Label($"Vehicles: {C.Vehicles.Count} / {C.VehicleCap}. The one you pick waits at the kerb.", 16, UITheme.Dim));
        foreach (string id in C.Vehicles)
        {
            VehicleDef v = Tables.Vehicle(id);
            var row = UITheme.HBox(12);
            int hp = v.Hp - C.CarDamage.GetValueOrDefault(id);
            var t = UITheme.VBox(0);
            t.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            t.AddChild(UITheme.Label(v.Name + (id == C.Vehicle ? "   [driving]" : ""), 18, id == C.Vehicle ? UITheme.Cyan : UITheme.Text, heading: true));
            t.AddChild(UITheme.Label($"Top {v.Top:0}, accel {v.Accel:0.#}, grip {v.Grip:0.00}, hull {hp}/{v.Hp}. {v.Desc}", 14, UITheme.Dim));
            row.AddChild(t);
            string vid = id;
            var b = UITheme.Button(v.Air ? "Fly (PDA)" : "Drive", () => { C.Vehicle = vid; Profile.SaveCareer(C); Show(_tab); }, 90, 36);
            b.Disabled = id == C.Vehicle || v.Air;
            row.AddChild(b);
            _content.AddChild(row);
        }
        _content.AddChild(UITheme.Rule());
        _content.AddChild(UITheme.Label(C.HasUpgrade("garage") ? "Your garage repairs for free." : "No garage: patch the car up yourself when it is badly hurt, or have Torque fix it properly.", 14, UITheme.Dim));
        Offers("home", "", o => o.Kind == ShopKind.Repair);
    }

    private void Sleep()
    {
        C.NextDay(_root.City, rested: true);
        _root.CitySim?.Chasers.Clear();
        Profile.SaveCareer(C);
        Sfx.Instance?.Play(Sound.Heal);
        _status.Text = $"Day {C.Day}. The board has changed.";
        Show(_tab);
        Refresh();
    }

    // ---- the briefing ---------------------------------------------------------------------

    /// <summary>A small face with a caption, when its picture exists; nothing otherwise.</summary>
    private static void Face(HBoxContainer row, string picture, string role, string name, Color tint)
    {
        if (Assets.Picture(picture) == null) return;
        var cell = UITheme.HBox(10);
        cell.AddChild(new Portrait { Picture = picture, Tint = tint, ClipContents = true, CustomMinimumSize = new Vector2(84, 84) });
        var text = UITheme.VBox(0);
        text.Alignment = BoxContainer.AlignmentMode.Center;
        text.AddChild(UITheme.Label(role, 14, UITheme.Dim, wrap: false));
        text.AddChild(UITheme.Label(name, 19, tint, wrap: false, heading: true));
        cell.AddChild(text);
        row.AddChild(cell);
    }

    private void BuildBriefing()
    {
        Job j = C.Active;
        var centre = new CenterContainer();
        UITheme.Fill(centre);
        AddChild(centre);
        var body = UITheme.VBox(10);
        bool site = j.Type is not (JobType.Courier or JobType.Race or JobType.Hub or JobType.Hack);
        // The site itself, for the map and its ways in (the same plan the run will build).
        LevelPlan plan = site ? LevelGen.Generate(j) : null;
        bool plans = plan != null && plan.Entries.Count > 0;
        body.CustomMinimumSize = new Vector2(plans ? 1080 : 700, 0);
        centre.AddChild(UITheme.Panel(body, 26));
        // The fixer's file on the job: a file number, a stamp, the site photo taped in.
        var file = UITheme.HBox(10);
        file.AddChild(UITheme.Grow(UITheme.Label($"FILE {j.Id:0000}  ·  {Tables.District(j.District).Name.ToUpperInvariant()}  ·  FOR {C.Name.ToUpperInvariant()} ONLY", 14, UITheme.Dim, wrap: false, mono: true)));
        file.AddChild(new Stamp { Text = "EYES ONLY", FontSize = 22, CustomMinimumSize = new Vector2(170, 34), Angle = -0.06f });
        body.AddChild(file);
        if (site && Assets.Picture(Assets.SitePicture(j.Style)) is Texture2D banner)
            body.AddChild(new Photo { Texture = banner, Seed = j.Id, CustomMinimumSize = new Vector2(plans ? 1080 : 700, plans ? 120 : 180) });
        body.AddChild(UITheme.Label(j.Title.ToUpperInvariant(), 34, UITheme.Magenta, HorizontalAlignment.Center, heading: true));
        body.AddChild(UITheme.Label($"{Tables.District(j.District).Name}  ·  {Tables.FactionNames[(int)j.Faction]}  ·  threat {j.Skulls}  ·  {M.Money(j.Pay)}  ·  karma +{j.Karma}", 17, UITheme.Dim, HorizontalAlignment.Center));
        body.AddChild(UITheme.Rule());
        // The brief and what you know on the left; the site plan and the ways in on the right.
        var cols = UITheme.HBox(22);
        body.AddChild(cols);
        var left = UITheme.VBox(10);
        left.CustomMinimumSize = new Vector2(plans ? 640 : 700, 0);
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        cols.AddChild(left);
        left.AddChild(UITheme.TypeOut(UITheme.Label(j.Brief, 19, UITheme.Text), MathF.Min(1.6f, j.Brief.Length / 90f)));
        // Who you will meet in there: every threat-5 site has a boss (LevelGen), and a hire comes along.
        var faces = UITheme.HBox(18);
        if (site && j.Threat >= 5) Face(faces, Assets.BossPicture(j.Faction), "In charge", Tables.BossOf(j.Faction).name, UITheme.Bad);
        if (j.Hired != null) Face(faces, Assets.HirePicture(j.Hired), "With you", Tables.Hires.First(h => h.id == j.Hired).name, UITheme.Good);
        if (faces.GetChildCount() > 0) left.AddChild(faces);
        left.AddChild(UITheme.Label((Career.GhostPays(j) ? "Go in quiet: no alarm pays a quarter extra. " : "") + "Loot you carry out is yours. Leaving before the job is done fails it.", 16, UITheme.Dim));
        if (C.TraumaLinks == 0) left.AddChild(UITheme.Label("No TraumaLink on file. If you go down in there, you stay down -- the career ends.", 16, UITheme.Bad));
        else left.AddChild(UITheme.Label($"TraumaLink: {C.TraumaLinks} pick-up(s) on file.", 16, UITheme.Good));
        // Legwork: what a little money and the right friends buy before you go in.
        var legwork = C.LegworkOffers(j);
        if (legwork.Count > 0)
        {
            left.AddChild(UITheme.Label("LEGWORK", 14, UITheme.Dim));
            var flow = new HFlowContainer();
            foreach (var (id, name, desc, price, block) in legwork)
            {
                string lid = id;
                var b = UITheme.Button(block == "Done." ? $"{name} ✓" : price > 0 ? $"{name} ({M.Money(price)})" : name, () =>
                {
                    string err = C.BuyLegwork(j, lid);
                    if (err != null) { Sfx.Instance?.Play(Sound.Error); return; }
                    Sfx.Instance?.Play(Sound.Coin);
                    Profile.SaveCareer(C);
                    Rebuild();
                }, 0, 32, block ?? desc);
                b.Disabled = block != null;
                flow.AddChild(b);
            }
            left.AddChild(flow);
        }
        bool schematics = j.Schematics || C.HasUpgrade("rig2");
        if (schematics) left.AddChild(UITheme.Label("You have the site schematics.", 16, UITheme.Cyan));
        else if (site && !plans) left.AddChild(UITheme.Label("Site layout:  ██████████ ███████ ████  (the fixer sells schematics)", 15, UITheme.Faint));
        if (j.Hired != null) left.AddChild(UITheme.Label($"{Tables.Hires.First(h => h.id == j.Hired).name} is coming with you ({(C.Retained.Contains(j.Hired) ? "on retainer: no cut" : $"{C.CrewCut(j.Hired) * 100:0} % of the pay; ¥300 if it goes wrong")}). If they go down, carry them out -- or lose them for good.", 16, UITheme.Good));
        if (plans)
        {
            var right = UITheme.VBox(8);
            right.CustomMinimumSize = new Vector2(400, 0);
            cols.AddChild(right);
            right.AddChild(UITheme.Label(schematics ? "SITE PLAN" : "SITE PLAN  (the fixer sells schematics)", 14, UITheme.Dim));
            right.AddChild(new SitePlan { Plan = plan, Job = j, Schematics = schematics, Chosen = j.Entry, CustomMinimumSize = new Vector2(400, 250) });
            right.AddChild(UITheme.Label("WAY IN", 14, UITheme.Dim));
            for (int k = 0; k < plan.Entries.Count; k++)
            {
                EntryPoint e = plan.Entries[k];
                bool on = (j.Entry ?? "front") == e.Id;
                string block = e.Id == "roof" && !C.OwnsHeli ? "Needs a helicopter (the Kite)." : null;
                var b = UITheme.Button($"{k + 1}  {e.Name}{(on ? "  ✓" : "")}", () =>
                {
                    j.Entry = e.Id == "front" ? null : e.Id;
                    Sfx.Instance?.Play(Sound.UiSelect);
                    Profile.SaveCareer(C);
                    Rebuild();
                }, 0, 32, block ?? e.Risk);
                b.Disabled = block != null;
                b.Alignment = HorizontalAlignment.Left;
                right.AddChild(b);
                if (on) right.AddChild(UITheme.Label(e.Risk, 14, UITheme.Amber));
            }
        }
        body.AddChild(UITheme.Label("WASD move · Shift sneak · mouse aim, LMB fire · RMB throw · 1/2/3 weapons · G grenade · R reload · Space dash · Q/F abilities · E use · H medkit", 14, UITheme.Dim, HorizontalAlignment.Center));
        var row = UITheme.HBox(14);
        row.Alignment = BoxContainer.AlignmentMode.Center;
        row.AddChild(UITheme.Button("Go in", () => { QueueFree(); _root.StartRun(j); }, 200, 48));
        row.AddChild(UITheme.Button("Not yet", Leave, 160, 48));
        body.AddChild(row);
    }

    /// <summary>Lay the briefing out again (legwork bought, a way in chosen).</summary>
    private void Rebuild()
    {
        foreach (Node n in GetChildren()) if (n is not ColorRect) n.QueueFree();
        BuildBriefing();
    }
}

/// <summary>
/// A face, or the safehouse view: the painted picture from <see cref="Assets"/>
/// when there is one, otherwise drawn -- neon hair, a cyber eye, a jaw line.
/// </summary>
public partial class Portrait : Control
{
    public ContactDef Contact;
    public int Seed;
    /// <summary>The <see cref="Assets"/> picture to show; defaults to the contact's.</summary>
    public string Picture;
    /// <summary>Border colour when there is no contact to take it from.</summary>
    public Color? Tint;

    public Portrait() => TextureFilter = TextureFilterEnum.LinearWithMipmaps;

    public override void _Draw()
    {
        Vector2 s = Size;
        Color tint = Tint ?? (Contact != null ? Art.C(Contact.Colour) : UITheme.Good);
        if (Assets.Picture(Picture ?? (Contact != null ? Assets.ContactPicture(Contact) : "")) is Texture2D pic)
        {
            DrawCover(this, pic, new Rect2(Vector2.Zero, s), 0.3f);
            for (int y = 0; y < (int)s.Y; y += 4) DrawLine(new Vector2(0, y), new Vector2(s.X, y), new Color(0, 0, 0, 0.12f), 1);
            DrawRect(new Rect2(Vector2.Zero, s), tint, false, 2);
            return;
        }
        DrawRect(new Rect2(Vector2.Zero, s), new Color(0.02f, 0.03f, 0.06f));
        for (int y = 0; y < (int)s.Y; y += 4) DrawLine(new Vector2(0, y), new Vector2(s.X, y), tint with { A = 0.04f }, 1);
        DrawTextureRect(Art.Glow, new Rect2(s.X * 0.1f, s.Y * 0.05f, s.X * 0.8f, s.Y * 0.9f), false, tint with { A = 0.25f });
        Vector2 c = new(s.X / 2, s.Y * 0.52f);
        float h = Art.Hash(Seed, 1), h2 = Art.Hash(Seed, 2), h3 = Art.Hash(Seed, 3);
        if (Contact == null)
        {
            // The safehouse: a window on the city.
            for (int k = 0; k < 9; k++)
            {
                float bx = 20 + k * 36, bh = 60 + Art.Hash(k, 7) * 150;
                DrawRect(new Rect2(bx, s.Y - bh, 30, bh), new Color(0.06f, 0.08f, 0.14f));
                for (int w = 0; w < bh / 14; w++) if (Art.Hash(k, w) > 0.5f) DrawRect(new Rect2(bx + 6, s.Y - bh + 6 + w * 14, 4, 4), UITheme.Yellow with { A = 0.6f });
            }
            DrawRect(new Rect2(Vector2.Zero, s), UITheme.Good, false, 2);
            return;
        }
        Color skin = new Color(0.55f + h * 0.35f, 0.42f + h * 0.25f, 0.32f + h * 0.2f);
        // Shoulders.
        DrawColoredPolygon(new[] { c + new Vector2(-150, 160), c + new Vector2(-90, 70), c + new Vector2(90, 70), c + new Vector2(150, 160) }, new Color(0.08f, 0.08f, 0.1f));
        DrawPolyline(new[] { c + new Vector2(-150, 160), c + new Vector2(-90, 70), c + new Vector2(90, 70), c + new Vector2(150, 160) }, tint with { A = 0.7f }, 2);
        DrawRect(new Rect2(c + new Vector2(-24, 30), new Vector2(48, 50)), skin.Darkened(0.2f));
        // Head.
        var head = new Vector2[24];
        for (int i = 0; i < 24; i++)
        {
            float a = i / 24f * MathF.Tau;
            float jaw = MathF.Sin(a) > 0 ? 1 + 0.25f * MathF.Sin(a) * (0.6f + h2 * 0.6f) : 1;
            head[i] = c + new Vector2(MathF.Cos(a) * 62 * (MathF.Sin(a) > 0 ? 1 - 0.25f * MathF.Sin(a) : 1), MathF.Sin(a) * 80 * jaw);
        }
        DrawColoredPolygon(head, skin);
        // Hair.
        Color hair = tint.Darkened(0.2f);
        var hairPts = new System.Collections.Generic.List<Vector2>();
        for (int i = 0; i <= 12; i++)
        {
            float a = MathF.PI + i / 12f * MathF.PI;
            float r = 70 + Art.Hash(Seed, i) * 18;
            hairPts.Add(c + new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r * 1.1f - 10));
        }
        hairPts.Add(c + new Vector2(66, -10));
        hairPts.Add(c + new Vector2(-66, -10));
        if (h3 > 0.25f) DrawColoredPolygon(hairPts.ToArray(), hair);
        // Eyes: one of them chrome.
        Vector2 le = c + new Vector2(-24, 2), re = c + new Vector2(24, 2);
        DrawRect(new Rect2(le - new Vector2(12, 4), new Vector2(24, 8)), new Color(0.95f, 0.95f, 0.95f));
        DrawCircle(le, 4, new Color(0.15f, 0.1f, 0.08f));
        DrawRect(new Rect2(re - new Vector2(14, 6), new Vector2(28, 12)), new Color(0.15f, 0.16f, 0.18f));
        DrawCircle(re, 5, tint);
        Art.DrawGlow(this, re, 22, tint with { A = 0.6f });
        // Brow, nose, mouth.
        DrawLine(le + new Vector2(-14, -12), le + new Vector2(12, -10), hair.Darkened(0.3f), 4);
        DrawLine(c + new Vector2(0, 8), c + new Vector2(-6, 34), skin.Darkened(0.3f), 2);
        DrawLine(c + new Vector2(-20, 52), c + new Vector2(20, 50 + h2 * 6), skin.Darkened(0.45f), 3);
        // Circuit tattoo.
        DrawPolyline(new[] { c + new Vector2(-50, 20), c + new Vector2(-40, 30), c + new Vector2(-40, 50), c + new Vector2(-30, 58) }, tint with { A = 0.7f }, 1.5f);
        DrawRect(new Rect2(Vector2.Zero, s), tint, false, 2);
    }

    /// <summary>
    /// Fill <paramref name="to"/> with <paramref name="t"/>, cropping whichever
    /// way it overflows; <paramref name="bias"/> is where a vertical crop sits
    /// (0 top, 0.5 centre) -- faces are in the upper half.
    /// </summary>
    public static void DrawCover(CanvasItem ci, Texture2D t, Rect2 to, float bias = 0.5f)
    {
        Vector2 ts = t.GetSize();
        float want = to.Size.X / to.Size.Y;
        Rect2 from = ts.X / ts.Y > want
            ? new Rect2((ts.X - ts.Y * want) / 2, 0, ts.Y * want, ts.Y)
            : new Rect2(0, (ts.Y - ts.X / want) * bias, ts.X, ts.X / want);
        ci.DrawTextureRectRegion(t, to, from);
    }
}
