using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>
/// The fourth pass: storylets, consequences, double-crosses, TraumaLink in
/// the streets, the moving city, the crew, the new jobs, the block, the
/// campaign -- and a long simulated career that plays them all together.
/// </summary>
public partial class SmokeTest
{
    private static Job StoryJob(JobType type = JobType.Retrieve, Faction f = Faction.Vipers, string district = "barrens", ulong seed = 11) =>
        new() { Id = 900 + (int)(seed % 1000), Type = type, Faction = f, Style = SiteStyle.Den, District = district, Threat = 2, Seed = seed, Target = "Vera Lindqvist", Item = "sealed briefcase", Title = "Test job", Lot = -1, Pay = 2000, Karma = 6, Employer = "fixer" };

    private static RunResult Won(Career c, bool alarm = false, int kills = 0) => new() { Success = true, Alarm = alarm, Kills = kills, GrenadesLeft = c.RunGrenades(), MedkitsLeft = c.RunMedkits(), Seconds = 60 };
    private static RunResult Lost(Career c) => new() { Success = false, GrenadesLeft = c.RunGrenades(), MedkitsLeft = c.RunMedkits(), Seconds = 60 };

    /// <summary>A career with a city, money, every code and every trust: most storylets can do what they do.</summary>
    private static Career RichCareer(City city)
    {
        Career c = TestCareer();
        c.CityRef = city;
        c.Cash = 200000;
        c.Karma = 200;
        c.Etiquettes = Career.AllEtiquettes.ToList();
        foreach (ContactDef k in Tables.Contacts) c.Loyalty[k.Id] = 5;
        c.Day = 30;
        c.Set("fate:Vera Lindqvist", Career.Fates["fled"]);
        c.Set("fateday:Vera Lindqvist", 20);
        c.Set("fate:Tomas Szabo", Career.Fates["rescued"]);
        c.Set("fateday:Tomas Szabo", 20);
        c.Set("fate:Mr. Ten", Career.Fates["killed"]);
        c.Set("fateday:Mr. Ten", 20);
        foreach (var h in Tables.Hires) c.CrewOf(h.id).Trust = 1;
        c.Last = new LastRun { Job = StoryJob(), Success = true, Alarm = true, Pay = 2000, Kills = 3 };
        // The city mid-crisis: a district just changed hands, a plan nearly done, the lights out.
        c.Set("held:barrens", 3);
        c.Set("heldday:barrens", c.Day);
        c.Clocks["vipers"] = 6;
        c.Happenings.Add(new Happening { Id = "grid", Start = c.Day, End = c.Day + 1, District = "docks" });
        c.Happenings.Add(new Happening { Id = "gangwar", Start = c.Day, End = c.Day + 1 });
        c.RefreshBoard(city);
        return c;
    }

    private static Career Clone(Career c)
    {
        Career k = Career.FromJson(c.ToJson());
        k.CityRef = c.CityRef;
        return k;
    }

    private void Storylets()
    {
        var ids = global::Sprawl.Storylets.All.Select(s => s.Id).ToList();
        Eq(ids.Distinct().Count(), ids.Count, "storylet ids are unique");
        Check(ids.Count >= 60, $"plenty of storylets ({ids.Count})");
        City city = City.Generate(55);
        Career fresh = TestCareer();
        fresh.CityRef = city;
        Career rich = RichCareer(city);
        foreach (Storylet s in global::Sprawl.Storylets.All)
        {
            foreach (Career c in new[] { fresh, rich })
            {
                try { s.When(c); }
                catch (Exception e) { Check(false, $"{s.Id}: its test throws ({e.GetType().Name}: {e.Message})"); }
            }
            // Forced: every storylet can be sent, and every answer taken, on a career that can afford it.
            Career k = Clone(rich);
            Mail m;
            try { m = k.Send(s); }
            catch (Exception e) { Check(false, $"{s.Id}: sending throws ({e.GetType().Name}: {e.Message}) {e.StackTrace}"); continue; }
            Check(!string.IsNullOrWhiteSpace(m.Text) && !m.Text.Contains('{'), $"{s.Id}: its words are filled in ('{m.Text}')");
            int n = s.Choices?.Length ?? 0;
            for (int i = 0; i < n; i++)
            {
                Career kk = Clone(k);
                Mail mm = kk.Inbox.First(x => x.Id == m.Id);
                try
                {
                    string err = kk.Choose(mm, i);
                    if (err == null) Check(mm.Reply != null && !mm.Reply.Contains('{') && kk.Cash >= 0, $"{s.Id} answer {i}: a reply, and no debt ({mm.Reply}, ¥{kk.Cash})");
                }
                catch (Exception e) { Check(false, $"{s.Id} answer {i} throws ({e.GetType().Name}: {e.Message}) {e.StackTrace}"); }
            }
        }
        // The engine.
        Career c2 = TestCareer();
        c2.CityRef = city;
        c2.Day = 2;
        ulong rng = c2.Rng.State;
        var sent = c2.Tell(Hook.Morning);
        Check(sent.Count == 1 && sent[0].Story == "welcome", "day 2: Juno explains the inbox");
        Eq(c2.Rng.State, rng, "story draws never touch the career's own random numbers");
        Check(c2.Tell(Hook.Morning).All(x => x.Story != "welcome"), "a storylet fires once");
        Check(c2.Unread == 1, "one unread message");
        Career twin = TestCareer();
        twin.CityRef = city;
        twin.Day = 2;
        Eq(twin.Tell(Hook.Morning).FirstOrDefault()?.Story, "welcome", "the same career gets the same story");
        // Scenes, and their choices.
        Career c3 = TestCareer();
        c3.CityRef = city;
        c3.Day = 5;
        c3.Cash = 0;
        Mail sc = c3.SceneAt("rigger");
        Check(sc != null && sc.Place == "rigger", "a scene plays when you walk in");
        Check(c3.SceneAt("rigger") == sc, "until you deal with it, it is still there");
        var opts = c3.Options(sc);
        Check(opts.Count > 0 && opts[0].block != null, "a choice you can't afford says why");
        Check(c3.Choose(sc, 0) != null && c3.Waiting(sc), "and cannot be taken");
        Check(c3.Choose(sc, opts.Count - 1) == null && !c3.Waiting(sc), "another can");
        Check(c3.Choose(sc, 0) != null, "a mail is only answered once");
        Check(c3.SceneAt("rigger") == null, "one scene per place per day");
    }

    private void Consequences()
    {
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        // A target who got away.
        Job e = StoryJob(JobType.Eliminate);
        var lost = Lost(c);
        lost.TargetFled = true;
        c.Finish(e, lost);
        Eq(c.Flag("fate:Vera Lindqvist"), Career.Fates["fled"], "the target who fled is remembered");
        c.Day += 3;
        Check(c.Eligible(Hook.Morning).Any(s => s.Id == "fled"), "and writes to you within a few days");
        Mail fm = c.Send("fled");
        Check(fm.From == "Vera Lindqvist", "in their own name");
        c.Cash = 5000;
        c.Choose(fm, 0);
        Check(c.Board.Any(j => j.Tag == "revenge:Vera Lindqvist" && j.Target == "Vera Lindqvist"), "a second chance at them goes on the board");
        // A hostage you got out.
        Career r = TestCareer();
        r.CityRef = city;
        Job res = StoryJob(JobType.Rescue, seed: 12);
        res.Target = "Tomas Szabo";
        r.Finish(res, Won(r));
        r.Day += 4;
        Check(r.Eligible(Hook.Morning).Any(s => s.Id == "rescued"), "a rescued hostage says thank you");
        int cash = r.Cash, kits = r.Medkits;
        r.Send("rescued");
        Check(r.Cash > cash || r.Medkits > kits || r.Flag("freeschem") > 0, "with something useful");
        // Choices after a run, on about a third of ordinary jobs.
        int decided = 0;
        for (ulong seed = 1; seed < 60; seed++)
        {
            Career d = TestCareer();
            d.CityRef = city;
            d.Finish(StoryJob(JobType.Retrieve, seed: seed), Won(d));
            if (d.Inbox.Any(m => m.Story == "collector" && m.Call)) decided++;
        }
        Check(decided > 8 && decided < 35, $"about a third of retrievals end with a choice ({decided}/59)");
        // Notoriety: bad company.
        Career clean = TestCareer(), dirty = TestCareer();
        clean.CityRef = dirty.CityRef = city;
        dirty.Notoriety = 10;
        int Crosses(Career k) => Enumerable.Range(0, 600).Select(_ => k.MakeJob(city)).Count(j => j != null && j.DoubleCross);
        int cc = Crosses(clean), dc = Crosses(dirty);
        Check(dc > cc, $"notoriety makes set-ups likelier ({cc} vs {dc})");
        dirty.Cash = 100000;
        dirty.Notoriety = 7;
        Check(dirty.Offers("doc").Where(o => o.Kind == ShopKind.Cyber).All(o => o.Block != null), "the doc won't sell chrome to a monster");
        Check(dirty.Offers("doc").Any(o => o.Kind == ShopKind.TraumaLink && o.Block == null), "but still sells TraumaLink");
        dirty.Notoriety = 9;
        Check(dirty.Offers("doc").All(o => o.Block != null) && dirty.Shut("doc") != null, "and past a point won't open the door at all");
        Career mid = TestCareer();
        mid.Notoriety = 4;
        Check(mid.PriceMul("doc") > TestCareer().PriceMul("doc"), "and charges a bad name more");
        // Fame.
        Career f = TestCareer();
        f.CityRef = city;
        Job loud = StoryJob(JobType.Destroy, Faction.Halcyon, "industrial", 21);
        loud.Threat = 4;
        f.Finish(loud, Won(f, alarm: true, kills: 12));
        Check(f.Awareness >= 2, "a loud, bloody run on a big site gets you noticed");
        Check(f.News.Any(n => n.StartsWith($"Day {f.Day - 1}. KBN:") || n.Contains("KBN:")), "and on the news");
        f.Awareness = 7;
        f.NextDay(city);
        Check(f.Heat >= 2, "fame starts every day with heat");
    }

    private void Crosses()
    {
        City city = City.Generate(55);
        // A stiff: no squad, no pay.
        Job stiff = null;
        for (ulong seed = 1; stiff == null && seed < 400; seed++)
        {
            Job j = StoryJob(seed: seed);
            j.Johnson = j.DoubleCross = true;
            if (j.Stiff) stiff = j;
        }
        Check(stiff != null, "some set-ups are stiffs");
        Career c = TestCareer();
        c.CityRef = city;
        int cash = c.Cash;
        Debrief d = c.Finish(stiff, Won(c));
        Check(d.Lines.Any(l => l.what == "Payment" && l.amount == "never arrived") && c.Cash <= cash, "a stiff never pays");
        Check(c.Flag("crossed") > 0 && c.Eligible(Hook.Morning).Any(s => s.Id == "crossed"), "and Juno finds the Johnson");
        Mail m = c.Send("crossed");
        c.Cash = 3000;
        Check(c.Options(m)[1].block != null, "squeezing him needs Juno's trust");
        c.Choose(m, 0);
        Check(c.Board.Any(j => j.Tag == "revenge:johnson"), "payback goes on the board");
        // The tell.
        Job deal = StoryJob(seed: 3);
        deal.Johnson = deal.DoubleCross = true;
        int pay = deal.Pay;
        Career face = TestCareer();
        face.Etiquettes.Add("Corporate");
        string said = face.Haggle(deal);
        Check(deal.Pay > pay && said.Contains("set-up"), "a set-up Johnson agrees too fast -- Corporate etiquette sees it");
        // Burned exits have a cause.
        Career b = TestCareer();
        b.CityRef = city;
        b.Heat = 3;
        var res = Won(b);
        res.ExfilBurned = true;
        Debrief bd = b.Finish(StoryJob(seed: 5), res);
        Check(bd.Lines.Any(l => l.what == "Exfil burned" && l.amount.Contains("plate")), "a burned exit names its cause");
        Check(b.Eligible(Hook.Morning).Any(s => s.Id == "burned-heat"), "and someone offers to fix it");
        Career jb = TestCareer();
        jb.CityRef = city;
        Job jj = StoryJob(seed: 6);
        jj.Johnson = jj.DoubleCross = true;
        res = Won(jb);
        res.ExfilBurned = true;
        jb.Finish(jj, res);
        Eq(jb.Last.Exfil, "johnson", "a crossed job's exit was sold by the Johnson");
    }

    private void TraumaLinkStreets()
    {
        City city = City.Generate(55);
        Career c = TestCareer();
        var sim = new CitySim(city, c, 3);
        Eq(sim.Traffic.Count(t => t.Kind == CarKind.Ambulance), 1, "one ambulance in traffic");
        Lot site = city.Lots.First(l => l.Place == null && l.District == "barrens");
        sim.Respond(site);
        Check(sim.Traffic.Count(t => t.Kind == CarKind.Ambulance) == 2 && sim.Traffic.Count(t => !float.IsNaN(t.Call.X)) == 2, "a loud run brings an ambulance and a patrol");
        TrafficCar amb = sim.Traffic.First(t => t.Kind == CarKind.Ambulance && !float.IsNaN(t.Call.X));
        float before = V2.Distance(amb.Pos, amb.Call);
        for (int k = 0; k < 600; k++) sim.Step(1 / 60f, new DriveInput());
        Check(float.IsNaN(amb.Call.X) || V2.Distance(amb.Pos, amb.Call) < before, "and it heads for the scene");
        // Ramming one.
        var sim2 = new CitySim(city, TestCareer(), 4);
        TrafficCar a = sim2.Traffic.First(t => t.Kind == CarKind.Ambulance);
        a.Pos = sim2.Player.Pos + sim2.Player.Forward * 1.6f;
        a.Speed = a.Cruise = 0;
        a.Bumped = 99;
        sim2.Player.Vel = sim2.Player.Forward * 12;
        for (int k = 0; k < 20; k++) sim2.Step(1 / 60f, new DriveInput { Throttle = 1 });
        Check(sim2.C.Notoriety >= 1 && sim2.C.Heat > 0, "ramming an ambulance is remembered");
        // Gold.
        Career g = TestCareer();
        g.Cash = 50000;
        Offer gold = g.Offers("doc").First(o => o.Id == "tlgold");
        Check(g.Buy(gold) == null && g.TraumaGold, "TraumaLink Gold is for sale at the doc");
        g.TraumaLinks = 1;
        int cash = g.Cash;
        g.Finish(StoryJob(), new RunResult { Died = true, GrenadesLeft = g.RunGrenades(), MedkitsLeft = g.RunMedkits() });
        Eq(g.Cash, cash, "a Gold pick-up keeps your cash");
    }

    private void Turf()
    {
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        foreach (DistrictDef d in Tables.Districts) Check(MathF.Abs(c.TurfOf(d.Id).Sum() - 1) < 1e-4f, $"{d.Name}: turf adds up");
        Eq(c.Holder("spire"), Faction.Halcyon, "Halcyon holds the Spire");
        Eq(c.Holder("barrens"), Faction.Vipers, "the Vipers hold the Barrens");
        float v = c.TurfOf("barrens")[(int)Faction.Vipers];
        c.Finish(StoryJob(JobType.Clear, Faction.Vipers, "barrens", 31), Won(c));
        Check(c.TurfOf("barrens")[(int)Faction.Vipers] < v - 0.05f, "clearing a Viper den costs them ground");
        for (int k = 0; k < 8; k++) c.Push("barrens", Faction.Vipers, 0.1f);
        Check(c.Holder("barrens") != Faction.Vipers && c.Flag("held:barrens") > 0, "push long enough and the district changes hands");
        Check(c.News.Any(n => n.Contains("now run the Barrens")), "which makes the news");
        Check(c.Eligible(Hook.Morning).Any(s => s.Id == "turf"), "and the new owners write");
        Faction owner = c.Holder("barrens");
        c.Grudge[owner] = 3;
        c.Grudge[Faction.Vipers] = 0;
        Mail turf = c.Send("turf");
        Check(c.Choose(turf, 0) == null, "offering them your services");
        Eq(c.Grudge.GetValueOrDefault(owner), 1, "makes friends of the new owners");
        Eq(c.Grudge.GetValueOrDefault(Faction.Vipers), 1, "and an enemy of the old ones");
        // New jobs follow the turf.
        c.Turf["docks"] = new[] { 0f, 0f, 1f };
        var jobs = Enumerable.Range(0, 300).Select(_ => c.MakeJob(city)).Where(j => j != null && j.District == "docks" && j.Style != SiteStyle.Lab && !j.Driving && j.Type != JobType.Bounty).ToList();
        Check(jobs.Count > 5 && jobs.All(j => j.Faction == Faction.Lotus), $"jobs come from whoever holds the district ({jobs.Count})");
        // The week drifts back.
        float lotus = c.TurfOf("docks")[(int)Faction.Lotus];
        c.Day = 6;
        c.NextDay(city);
        Check(c.TurfOf("docks")[(int)Faction.Lotus] < lotus, "each week the city drifts back towards how it was");
    }

    private void Atonement()
    {
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        c.Cash = 200000;
        c.Notoriety = 9;
        Check(c.Shut("doc") != null && c.Shut("talismonger") != null, "at notoriety 9 Ichikawa and Sabine shut their doors");
        Check(c.Eligible(Hook.Morning).Any(s => s.Id == "atone-doc") && c.Eligible(Hook.Morning).Any(s => s.Id == "atone-sabine"), "and each offers a way back");
        // Ichikawa's: hold her free clinic, for expenses.
        Check(c.Choose(c.Send("atone-doc"), 0) == null, "taking the clinic");
        Job clinic = c.Board.FirstOrDefault(b => b.Tag == "atone:doc");
        Check(clinic != null && clinic.Type == JobType.Defend && clinic.District == "barrens", "puts a defend in the Barrens on the board");
        Check(!c.Eligible(Hook.Morning).Any(s => s.Id == "atone-doc"), "and isn't offered twice");
        int trust = c.Loyalty.GetValueOrDefault("doc");
        c.Finish(clinic, Won(c, alarm: true, kills: 4));
        Eq(c.Notoriety, 9 - Tables.AtoneCut, "held, it takes 2 off your notoriety");
        Eq(c.Loyalty.GetValueOrDefault("doc"), trust + 1, "and wins back a point of Ichikawa's trust");
        Check(c.Shut("doc") == null, "and her door opens again");
        Check(!c.Eligible(Hook.Morning).Any(s => s.Id == "atone-doc"), "not again for a week");
        // Sabine's: clear the nest under the market; a failed penance changes nothing.
        Check(c.Choose(c.Send("atone-sabine"), 0) == null, "Sabine's note");
        Job nest = c.Board.FirstOrDefault(b => b.Tag == "atone:talismonger");
        Check(nest != null && nest.Type == JobType.Bounty, "is a nest to clear");
        c.Finish(nest, Lost(c));
        Eq(c.Notoriety, 7, "failing it forgives nothing");
        // Going quiet: three clean wins in a row take one off; a loud one starts the count again.
        c.Notoriety = 5;
        c.Finish(StoryJob(seed: 41), Won(c));
        c.Finish(StoryJob(seed: 42), Won(c));
        c.Finish(StoryJob(seed: 43), Won(c, alarm: true));
        c.Finish(StoryJob(seed: 44), Won(c));
        Eq(c.Notoriety, 5, "a loud job breaks the streak");
        c.Finish(StoryJob(seed: 45), Won(c));
        c.Finish(StoryJob(seed: 46), Won(c));
        Eq(c.Notoriety, 4, "three quiet jobs in a row: notoriety -1");
        // Juno's restitution: by the point, once a week.
        c.Notoriety = 2;
        int two = c.Offers("fixer").First(o => o.Id == "restitution").Price;
        c.Notoriety = 4;
        Offer rest = c.Offers("fixer").First(o => o.Id == "restitution");
        Check(rest.Block == null && Math.Abs(rest.Price - 2 * two) <= 100, $"restitution costs by the point ({two} at 2, {rest.Price} at 4)");
        long cash = c.Cash;
        Check(c.Buy(rest) == null && c.Notoriety == 3 && c.Cash == cash - rest.Price, "paid, notoriety -1");
        Check(c.Offers("fixer").First(o => o.Id == "restitution").Block != null, "and not again this week");
        c.Notoriety = 0;
        Check(c.Offers("fixer").First(o => o.Id == "restitution").Block != null, "nor with nothing to atone for");
    }

    private void Happenings()
    {
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        // Clocks.
        c.Clocks["vipers"] = 7;
        c.Tick("vipers", 1);
        Check(c.On("gangwar") && c.Clock("vipers") == 2, "a faction plan that fills sets the city on fire");
        Check(c.HappeningLines().Any(l => l.Contains("Gang war")), "the terminal says so");
        Check(c.Eligible(Hook.Morning).Any(s => s.Id == "gangwar-side"), "Juno asks whose side you're on");
        Check(c.AmbushChance(new Job { Faction = Faction.Halcyon, District = "spire" }) >= 0.8f, "a gang war follows you home from any job");
        // Every plan has a cost for you too.
        int rent = c.RentDue;
        c.Happenings.Add(new Happening { Id = "expansion", Start = c.Day, End = c.Day + 3 });
        Check(c.RentDue > rent, $"Halcyon's urban renewal puts the rent up ({rent} -> {c.RentDue})");
        c.Happenings.Add(new Happening { Id = "council", Start = c.Day, End = c.Day + 3 });
        c.Rep = 9;
        var lj = Enumerable.Range(0, 300).Select(_ => c.MakeJob(city)).Where(x => x != null && x.Faction == Faction.Lotus && !x.Driving && x.Type != JobType.Bounty).ToList();
        Check(lj.Count > 0 && lj.All(x => x.Has(RunMod.Elite)), "a Lotus council fields its best on every site");
        c.Clocks["lotus"] = 5;
        Check(c.Gossip() != null, "the street kids know what's brewing");
        int before = c.Clock("halcyon");
        c.Finish(StoryJob(JobType.Datasteal, Faction.Halcyon, "industrial", 41), Won(c));
        Check(c.Clock("halcyon") <= Math.Max(0, before - 2) + 1, "a datasteal sets their plans back");
        // The calendar.
        Career s = TestCareer();
        s.CityRef = city;
        s.Day = 2;
        s.NextDay(city);
        Check(s.On("stadium"), "every 7th day from the 3rd, a game at the stadium");
        Career m = TestCareer();
        m.Archetype = "mage";
        float p = m.SpellPower;
        m.Happenings.Add(new Happening { Id = "surge", Start = m.Day, End = m.Day });
        Check(m.SpellPower > p * 1.15f, "a mana surge strengthens spells");
        Career q = TestCareer();
        q.CityRef = city;
        q.Happenings.Add(new Happening { Id = "quarterly", Start = q.Day, End = q.Day + 1 });
        var hj = Enumerable.Range(0, 300).Select(_ => q.MakeJob(city)).Where(j => j != null && j.Faction == Faction.Halcyon && !j.Driving).ToList();
        Check(hj.Count > 0 && hj.All(j => j.Has(RunMod.ShiftChange)), "the corporate quarterly doubles Halcyon patrols");
        // A blackout in one district.
        Career g = TestCareer();
        g.Happenings.Add(new Happening { Id = "grid", Start = g.Day, End = g.Day, District = "docks" });
        Job dj = TestJob();
        dj.District = "docks";
        var plan = Site("#########", "#P......#", "#########");
        var run = new Run(dj, g, plan);
        Check(run.FovPenalty >= 4, "a grid failure blacks out every site in the district");
        // A strike: no police.
        Career st = TestCareer();
        st.Heat = 3;
        st.Happenings.Add(new Happening { Id = "strike", Start = st.Day, End = st.Day });
        var sim = new CitySim(city, st, 9);
        sim.Traffic.RemoveAll(t => t.Kind == CarKind.Patrol);
        for (int k = 0; k < 60 * 30; k++) sim.Step(1 / 60f, new DriveInput());
        Check(!sim.Chasers.Any(x => x.Kind == ChaserKind.Police), "Lone Star on strike sends nobody");
        // Rent.
        Career r = TestCareer();
        r.CityRef = city;
        r.Day = 6;
        r.Cash = 1000;
        r.NextDay(city);
        Eq(r.Cash, 1000 - r.DailyRent, "a seventh of the week's rent comes out every morning");
        Eq(r.DailyRent, 57, "¥400 a week is ¥57 a day");
        // No dodging it: live high for six days and move out on the seventh, and you paid for six days of it.
        r.Cash = 100000;
        r.Lifestyle = "luxury";
        for (int k = 0; k < 6; k++) r.NextDay(city);
        r.Lifestyle = "low";
        Check(100000 - r.Cash >= 6 * 15000 / 7 - 6, $"living well costs by the night (¥{100000 - r.Cash})");
        r.Cash = 0;
        r.Day = 13;
        r.NextDay(city);
        Check(r.RentOwed == 57 && r.Flag("rentlate") == 1, "can't pay: you owe it, and the week turns on it");
        Check(r.SceneAt("home")?.Story == "block-rent", "and the landlady is waiting on the stairs");
        r.Day = 20;
        r.NextDay(city);
        Check(r.Heat >= 2, "twice behind, she gives Lone Star your address");
        r.Cash = 5000;
        r.NextDay(city);
        Check(r.RentOwed == 0 && r.Flag("rentlate") == 0, "money in the bank pays what you owe");
    }

    private void Crew()
    {
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        Job j = StoryJob(seed: 51);
        j.Hired = "ox";
        Debrief d = c.Finish(j, Won(c));
        Eq(c.CrewTrust("ox"), 1, "a job together builds trust");
        Check(d.QuoteBy == "Ox" && d.Quote != null, "and they have something to say about it");
        c.CrewOf("ox").Trust = 2;
        Check(c.Eligible(Hook.Morning).Any(s => s.Id == "crew2-ox"), "at trust 2 they ask for a word");
        Mail m = c.Send("crew2-ox");
        c.Choose(m, 0);
        Check(c.CrewHas("ox", "plating"), "and learn what you pick");
        Job oj = TestJob();
        oj.Hired = "ox";
        var run = new Run(oj, c, Site("#########", "#P......#", "#########"));
        Check(run.Ally.MaxHp >= Tables.Hires.First(h => h.id == "ox").def.Hp + 80, "plated Ox is tougher");
        // A personal job.
        c.CrewOf("ox").Trust = 3;
        Mail pj = c.Send("crewjob-ox");
        c.Choose(pj, 0);
        Job mine = c.Board.FirstOrDefault(x => x.Tag == "crew:ox");
        Check(mine != null && mine.Hired == "ox", "at trust 3 a personal job, and they come along");
        float cut = c.CrewCut("ox");
        c.Finish(mine, Won(c));
        Check(c.CrewHas("ox", "loyal") && c.CrewCut("ox") < cut, "doing it for them lowers their cut for good");
        // Death.
        Career dc = TestCareer();
        dc.CityRef = city;
        Job dj = StoryJob(seed: 52);
        dj.Hired = "wisp";
        var died = Won(dc);
        died.AllyDied = true;
        Debrief dd = dc.Finish(dj, died);
        Check(dc.CrewOf("wisp").Dead && dd.Lines.Any(l => l.amount == "killed"), "a crew member killed on a run is gone");
        dc.Board.Add(StoryJob(seed: 53));
        dc.ActiveJob = dc.Board[^1].Id;
        Check(dc.Offers("fixer").First(o => o.Id == "wisp").Block?.Contains("dead") == true, "and cannot be hired again");
        Check(dc.Eligible(Hook.Morning).Any(s => s.Id == "crewdead-wisp"), "there is a funeral");
        // Carried out, or saved by Gold.
        Career gc = TestCareer();
        gc.CityRef = city;
        gc.Set("tlgold");
        Job gj = StoryJob(seed: 54);
        gj.Hired = "patch";
        var gd = Won(gc);
        gd.AllyDied = true;
        gc.Finish(gj, gd);
        Check(!gc.CrewOf("patch").Dead && gc.CrewBlock("patch") != null, "TraumaLink Gold pulls them out: a few days to mend");
        string[] rows = { "############", "#P.........#", "#..........#", "############" };
        Job aj = TestJob(JobType.Destroy);
        aj.Hired = "ox";
        var ar = new Run(aj, TestCareer(), Site(rows));
        ar.Ally.Pos = new V2(8.5f, 2.5f);
        ar.Hurt(ar.Ally, 1e6f, null, false);
        Check(ar.Ally.Dead, "the hire can go down");
        ar.Player.Pos = ar.Ally.Pos + new V2(0.2f, 0);
        ar.Step(new RunInput { Slot = -1, Interact = true, InteractPressed = true, Aim = ar.Player.Pos + new V2(1, 0) });
        Steps(ar, 0.8f, new RunInput { Slot = -1, Interact = true, Aim = ar.Player.Pos + new V2(1, 0) });
        Check(ar.CarriedBody == ar.Ally, "you can carry them");
        ar.Player.Pos = ar.Plan.Entry.Centre;
        Check(ar.AllyCarriedOut, "out to the extraction, they live");
        // Second wind.
        Career wc = TestCareer();
        wc.CrewLearn("ox", "wind");
        Job wj = TestJob();
        wj.Hired = "ox";
        var wr = new Run(wj, wc, Site(rows));
        wr.Hurt(wr.Ally, 1e6f, null, false);
        Check(!wr.Ally.Dead && wr.Ally.Hp > 0, "Ox's second wind gets him up once");
        wr.Hurt(wr.Ally, 1e6f, null, false);
        Check(wr.Ally.Dead, "only once");
    }

    private void NewJobs()
    {
        string[] rows =
        {
            "################",
            "#P.............#",
            "#..............#",
            "#..............#",
            "################",
        };
        // Investigate: three pieces of evidence.
        var plan = Site(rows);
        for (int k = 0; k < 3; k++) plan.Pickups.Add(new PickupSpawn { Kind = PickupKind.Clue, Pos = new V2(5.5f + 3 * k, 2.5f), Name = "a ledger" });
        var r = Make(plan, JobType.Investigate);
        Check(r.ObjectivePos() is V2 o && MathF.Abs(o.X - 5.5f) < 0.1f, "the nearest clue is the objective");
        r.Player.Pos = new V2(5.5f, 2.5f); Steps(r, 0.1f);
        r.Player.Pos = new V2(8.5f, 2.5f); Steps(r, 0.1f);
        Check(r.CluesFound == 2 && !r.ObjectiveDone, "two of three is not enough");
        r.Player.Pos = new V2(11.5f, 2.5f); Steps(r, 0.1f);
        Check(r.ObjectiveDone, "three is");
        // Plant.
        var pp = Site(rows);
        pp.PlantAt = new V2(10.5f, 2.5f);
        Job pj = TestJob(JobType.Plant);
        pj.Item = "wiretap";
        var p = new Run(pj, TestCareer(), pp);
        p.Player.Pos = pp.PlantAt;
        Steps(p, 1f, new RunInput { Slot = -1, Interact = true, Aim = p.Player.Pos + new V2(1, 0) });
        Check(p.PlantProgress > 0 && !p.ObjectiveDone, "planting takes a few seconds");
        Steps(p, 3f, new RunInput { Slot = -1, Interact = true, Aim = p.Player.Pos + new V2(1, 0) });
        Check(p.ObjectiveDone, "held long enough, it is in");
        // Defend.
        Job dj = TestJob(JobType.Defend, Faction.Vipers);
        var d = new Run(dj, TestCareer(), Site(rows));
        d.Player.MaxHp = d.Player.Hp = 1e7f;
        Steps(d, d.DefendFor + 2);
        Check(d.Waves >= 3 && d.Success && d.Phase == RunPhase.Extracted, $"hold out through the waves and it's yours ({d.Waves} waves)");
        Check(d.Result().Alarm, "a defence is never quiet");
        Job dj2 = TestJob(JobType.Defend, Faction.Vipers);
        dj2.Hired = "ox";
        var d2 = new Run(dj2, TestCareer(), Site(rows));
        d2.Player.MaxHp = d2.Player.Hp = 1e7f;
        d2.Hurt(d2.Ally, 1e6f, null, false);
        Steps(d2, d2.DefendFor + 2);
        Check(d2.Success && d2.Ally.Dead && d2.AllyCarriedOut && !d2.Result().AllyDied, "a hire down in a defence you held mends; nobody had to carry them out");
        // Generated sites.
        for (ulong seed = 1; seed <= 12; seed++)
        foreach (SiteStyle st in new[] { SiteStyle.Warehouse, SiteStyle.Office, SiteStyle.Den, SiteStyle.Lab, SiteStyle.Club })
        {
            var job = new Job { Type = JobType.Investigate, Style = st, Faction = Faction.Vipers, Threat = 1 + (int)(seed % 5), Seed = seed, Target = "x", Item = "x", Title = "x" };
            Check(LevelGen.Generate(job).Pickups.Count(k => k.Kind == PickupKind.Clue) == 3, $"investigate {st} {seed}: three clues");
            job.Type = JobType.Plant;
            Check(LevelGen.Generate(job).PlantAt.X >= 0, $"plant {st} {seed}: a spot to plant");
            job.Type = JobType.Defend;
            Check(!LevelGen.Generate(job).Enemies.Any(), $"defend {st} {seed}: nobody inside to start with");
        }
    }

    private void Block()
    {
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        c.Day = 3;
        Check(c.SceneAt("home")?.Story == "block-noodles", "there's a noodle stand under your window");
        // The raid.
        Career r = TestCareer();
        r.CityRef = city;
        r.Awareness = 7;
        r.Day = 22;
        Check(r.Eligible(Hook.Morning).Any(s => s.Id == "raid"), "too famous: they come for your door");
        Mail m = r.Send("raid");
        r.Choose(m, 0);
        Job raid = r.Active;
        Check(raid != null && raid.Tag == "raid" && raid.Type == JobType.Defend && raid.Lot == city.Place("home").Index, "it's your active job, at home");
        r.Cash = 9000;
        r.Finish(raid, Lost(r));
        Eq(r.Cash, 6000 - r.DailyRent, "lose it and they take a third");
        Career h = TestCareer();
        h.CityRef = city;
        h.Awareness = 7;
        Job hr = h.OfferRaid();
        h.ActiveJob = hr.Id;
        h.Finish(hr, Won(h));
        Check(h.Awareness <= 4 && h.Stat("raidsheld") == 1, "hold it and they lose interest");
        Career x = TestCareer();
        x.CityRef = city;
        x.Cash = 3000;
        x.ActiveJob = x.OfferRaid().Id;
        x.NextDay(city);
        Eq(x.Cash, 2000 - x.DailyRent, "nobody home: they take it anyway");
        Career y = TestCareer();
        y.CityRef = city;
        y.Awareness = 7;
        y.Day = 22;
        y.Cash = 500;
        Mail ym = y.Send("raid");
        Check(y.Choose(ym, 1) != null && y.Cash == 500 && y.Awareness == 7, "no ¥2,000, no clearing out");
    }

    private void Campaign()
    {
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        c.JobsDone = 3;
        c.RefreshBoard(city);
        Job s1 = c.Board.FirstOrDefault(j => j.Story == 1);
        Check(s1 != null && s1.Type == JobType.Retrieve && s1.Threat == 1 && s1.Pay >= 2000, "after three jobs, a gentle chapter 1 appears, well paid");
        Accept(c, s1);
        c.Finish(s1, Won(c));
        Eq(c.Chapter, 1, "chapter 1 done");
        c.JobsDone = 5;
        c.RefreshBoard(city);
        Check(!c.Board.Any(j => j.Story == 2), "chapter 2 waits for its lead");
        Check(c.SceneAt("decker")?.Story == "story-trace", "Null can trace the chip");
        c.Cash = 5000;
        c.Choose(c.Inbox.First(m => m.Story == "story-trace"), 0);
        c.RefreshBoard(city);
        Job s2 = c.Board.FirstOrDefault(j => j.Story == 2);
        Check(s2 != null, "with the lead, chapter 2 is offered");
        if (s2 == null) return;
        Accept(c, s2);
        c.Finish(s2, Won(c));
        Mail mid = c.Inbox.FirstOrDefault(m => m.Story == "story-mid");
        Check(mid != null && mid.Call, "after chapter 2, a call: what to do with the buyer list");
        c.Choose(mid, 0);
        Eq(c.StoryChoice, 1, "leaked");
        c.JobsDone = 8;
        c.Day++;
        Check(c.SceneAt("doc")?.Story == "story-sister", "Pryce's sister waits at the clinic");
        c.Choose(c.Inbox.First(m => m.Story == "story-sister"), 0);
        c.RefreshBoard(city);
        Job s3 = c.Board.FirstOrDefault(j => j.Story == 3);
        Check(s3 != null && s3.Type == JobType.Rescue, "chapter 3");
        if (s3 == null) return;
        Accept(c, s3);
        c.Finish(s3, Won(c));
        c.JobsDone = 14;
        c.CrewOf("wisp").Trust = 3;
        c.ArcStep["decker"] = 3;
        c.RefreshBoard(city);
        Job s4 = c.Board.FirstOrDefault(j => j.Story == 4);
        Check(s4 != null, "three jobs past the mark, the finale finds you without the lead");
        if (s4 == null) return;
        Check(s4.Tag == "finale:leak" && s4.DoubleCross && !s4.Stiff, "the leak put Lone Star at the Spire's exit");
        Check(s4.Hired == "wisp", "the crew member who trusts you most comes along");
        var run = new Run(s4, c);
        Check(run.Actors.Where(a => a.Is(EnemyKind.Camera)).All(a => a.Stun > 1000), "Null loops the Spire's cameras");
        Accept(c, s4);
        Debrief d = c.Finish(s4, Won(c));
        Check(!d.CareerOver && c.Flag("story:won") > 0 && d.Headline == "IRON SPIRE FALLS", "the Spire falls, and the career need not end");
        Check(d.Epilogue.Count >= 2, "an epilogue of what became of everyone");
        Check(d.Lines.Any(l => l.what.Contains("Wisp") && l.amount == "on the house"), "the crew member came for free");
        Mail end = c.Inbox.FirstOrDefault(m => m.Story == "story-end");
        Check(end != null && end.Call, "Juno asks what now");
        var p = new Profile();
        p.Earn(c);
        Check(p.Legacy.Contains("legend") && p.Legacy.Contains("folk"), "the ending is a legacy");
        // Keep running: Toxic Tide.
        c.Choose(end, 1);
        Check(!c.Retired, "you can keep running");
        c.RefreshBoard(city);
        Check(c.Inbox.Any(m => m.Story == "tide-open"), "Madame Sabine has something to tell you");
        for (int ch = 1; ch <= 4; ch++)
        {
            c.JobsDone += 2;
            c.RefreshBoard(city);
            Job t = c.Board.FirstOrDefault(j => j.Story == 4 + ch);
            Check(t != null, $"Toxic Tide chapter {ch}");
            if (t == null) return;
            if (ch == 1) Check(t.Type == JobType.Investigate, "it starts with legwork");
            Accept(c, t);
            c.Finish(t, Won(c));
            if (ch == 2) { Mail tm = c.Inbox.First(m => m.Story == "tide-mid"); c.Choose(tm, 1); }
        }
        Check(c.Flag("tide:won") > 0, "the tide turns");
        p.Earn(c);
        Check(p.Legacy.Contains("warded"), "and that's a legacy too");
        // Retiring after all.
        Career rc = TestCareer();
        rc.Set("story:won", 1);
        rc.Last = new LastRun { Job = new Job { Story = 4 }, Success = true };
        Mail re = rc.Send("story-end");
        rc.Choose(re, 0);
        Check(rc.Retired, "or retire a legend");
    }

    /// <summary>
    /// Sixty days of a career with everything on: jobs taken and settled (most
    /// won), every place visited every day, every message answered -- with
    /// nothing breaking, the books balancing, and the story getting told.
    /// </summary>
    private void LongCareer()
    {
        City city = City.Generate(77);
        Career c = Career.New("Sim", "face", 77);
        c.CityRef = city;
        c.RefreshBoard(city);
        var r = new Rng(5);
        string[] places = Tables.Contacts.Select(k => k.Id).Append("home").ToArray();
        int answered = 0;
        for (int day = 0; day < 70 && !c.Dead && !c.Retired; day++)
        {
            foreach (string place in places)
            {
                Mail sc = c.SceneAt(place);
                if (sc != null) answered += Answer(c, sc, r);
            }
            foreach (Mail m in c.Inbox.Where(c.Waiting).ToList()) answered += Answer(c, m, r);
            Job j = c.Active ?? c.Board.Where(x => x.Type != JobType.Hub).OrderByDescending(x => x.Story).ThenBy(x => x.Threat).FirstOrDefault();
            if (j == null) { c.NextDay(city, rested: true); continue; }
            if (j.Johnson) j.Negotiated = true;
            if (c.Accept(j) != null) { c.NextDay(city, rested: true); continue; }
            if (c.Active?.Hired == null && c.Active?.Story == 0 && r.Chance(0.4f)) c.Active.Hired = r.Pick(Tables.Hires.Where(h => c.CrewBlock(h.id) == null).Select(h => h.id).DefaultIfEmpty("ox").ToList());
            var res = r.Chance(0.85f) ? Won(c, alarm: r.Chance(0.4f), kills: r.Range(0, 12)) : Lost(c);
            res.ExfilBurned = r.Chance(0.1f);
            res.AllyDied = j.Hired != null && r.Chance(0.05f);
            res.TargetFled = j.Type == JobType.Eliminate && !res.Success;
            c.TraumaLinks = Math.Max(c.TraumaLinks, 1);
            c.Finish(j, res);
            Check(c.Cash >= 0, $"day {c.Day}: no debt (¥{c.Cash})");
            foreach (DistrictDef d in Tables.Districts) if (MathF.Abs(c.TurfOf(d.Id).Sum() - 1) > 1e-3f) Check(false, $"day {c.Day}: {d.Name} turf adds up");
            if (c.Clocks.Values.Any(v => v < 0 || v > Career.ClockFull)) Check(false, $"day {c.Day}: clocks in range");
        }
        int fired = c.Fired.Count;
        GD.Print($"    a simulated career: day {c.Day}, {c.JobsDone} jobs, chapter {c.Chapter}/4, tide {c.Chapter2}/4, {fired} storylets, {answered} answers, ¥{c.Cash}, notoriety {c.Notoriety}, awareness {c.Awareness}, holders {string.Join(" ", Tables.Districts.Select(d => d.Id + ":" + c.Holder(d.Id)))}");
        Check(fired >= 25, $"a career meets plenty of storylets ({fired})");
        Check(c.Flag("story:won") > 0 || c.Retired, $"and the story gets told (chapter {c.Chapter})");
        string json = c.ToJson();
        Career back = Career.FromJson(json);
        Check(back.Inbox.Count == c.Inbox.Count && back.Flags.Count == c.Flags.Count && back.Turf.Count == c.Turf.Count && back.Crew.Count == c.Crew.Count && back.Happenings.Count == c.Happenings.Count,
            "all of it saves and loads");
    }

    /// <summary>Take the first answer that is open (or, now and then, a random open one).</summary>
    private static int Answer(Career c, Mail m, Rng r)
    {
        var opts = c.Options(m);
        m.Read = true;
        if (!c.Waiting(m)) return 0;
        // The simulated runner always keeps running, to see what comes after.
        if (m.Story == "story-end") return c.Choose(m, 1) == null ? 1 : 0;
        var open = Enumerable.Range(0, opts.Count).Where(i => opts[i].block == null).ToList();
        if (open.Count == 0) return 0;
        int k = r.Chance(0.7f) ? open[0] : open[r.Int(open.Count)];
        return c.Choose(m, k) == null ? 1 : 0;
    }
}

public partial class SmokeTest
{
    private void MoreJobs()
    {
        string[] rows =
        {
            "##################",
            "#P...............#",
            "#................#",
            "#................#",
            "##################",
        };
        // Escort: the client walks with you, talks, and walks out.
        var ep = Site(rows);
        ep.Hostage = new V2(2.5f, 2.5f);
        ep.PlantAt = new V2(13.5f, 2.5f);
        Job ej = TestJob(JobType.Escort);
        var e = new Run(ej, TestCareer(), ep);
        Check(e.Hostage != null && e.Hostage.State == AiState.Follow, "the client follows from the start");
        e.Player.Pos = new V2(13.5f, 2.5f);
        Steps(e, 4f);
        Check(V2.Distance(e.Hostage.Pos, ep.PlantAt) < 2.5f, $"they keep up ({V2.Distance(e.Hostage.Pos, ep.PlantAt):0.0} from the meeting)");
        Steps(e, 11f);
        Check(e.EscortProgress >= 1 && e.ObjectiveDone, $"they finish the meeting ({e.EscortProgress:0.00})");
        e.Player.Pos = ep.Entry.Centre;
        Steps(e, 5f);
        Check(e.Success, "and you walk them out");
        // Kidnap: knocked out and carried, or it's blown.
        Job kj = TestJob(JobType.Kidnap);
        var k = new Run(kj, TestCareer(), Site(rows));
        k.Vip = k.AddEnemy(EnemyKind.Vip, new V2(10.5f, 2.5f), 0);
        k.Hurt(k.Vip, 1e6f, k.Player, false, nonLethal: true);
        Check(k.Vip.KO && !k.ObjectiveLost, "out cold is fine");
        k.Player.Pos = k.Vip.Pos + new V2(0.3f, 0);
        Steps(k, 0.1f);
        k.Step(new RunInput { Slot = -1, Interact = true, InteractPressed = true, Aim = k.Player.Pos + new V2(1, 0) });
        Steps(k, 0.8f, new RunInput { Slot = -1, Interact = true, Aim = k.Player.Pos + new V2(1, 0) });
        Check(k.CarriedBody == k.Vip, $"carry them (prompt '{k.Prompt}', vip at {k.Vip.Pos}, you at {k.Player.Pos}, dead {k.Vip.Dead} ko {k.Vip.KO} esc {k.Vip.Escaped})");
        k.Player.Pos = k.Plan.Entry.Centre;
        Steps(k, 0.3f);
        Check(k.Success, "out alive: done");
        var k2 = new Run(TestJob(JobType.Kidnap), TestCareer(), Site(rows));
        k2.Vip = k2.AddEnemy(EnemyKind.Vip, new V2(10.5f, 2.5f), 0);
        k2.Hurt(k2.Vip, 1e6f, k2.Player, false);
        k2.Step(new RunInput { Slot = -1 });
        Check(k2.ObjectiveLost, "dead: blown");
        // Hack: from home.
        Job hj = new() { Id = 5, Type = JobType.Hack, Threat = 2, Seed = 9, Title = "x", Target = "x", Item = "x", Lot = -1 };
        var hp = LevelGen.Generate(hj);
        Check(hp.Mainframe.X >= 0 && !hp.Enemies.Any() && hp.L.W < 20, "a hack is done at your own rig");
        // Bounty: nothing with a gun.
        for (ulong seed = 1; seed < 6; seed++)
        {
            var bj = new Job { Type = JobType.Bounty, Style = SiteStyle.Den, Faction = Faction.Vipers, Threat = 1 + (int)seed % 5, Seed = seed, Title = "x", Target = "x", Item = "x" };
            var bp = LevelGen.Generate(bj);
            Check(bp.Enemies.Count > 0 && bp.Enemies.All(x => x.Kind is EnemyKind.Hound or EnemyKind.Bruiser or EnemyKind.Spirit or EnemyKind.Barghest), $"a nest is critters ({string.Join(",", bp.Enemies.Select(x => x.Kind).Distinct())})");
            Check(bp.Enemies.All(x => x.Mood != Mood.OffDuty), "nothing in a nest is on its break");
        }
        // Stirred all the way up, a nest sends more of itself, never somebody's gangers.
        {
            var bj = new Job { Id = 6, Type = JobType.Bounty, Style = SiteStyle.Den, Faction = Faction.Vipers, Threat = 4, Seed = 3, Title = "x", Target = "x", Item = "x", Lot = -1 };
            var br = new Run(bj, TestCareer(), LevelGen.Generate(bj));
            br.RaiseSecurity(6);
            for (int i = 0; i < 60 * 60; i++) br.Step(new RunInput { Slot = -1, Aim = br.Player.Pos + new V2(1, 0) });
            var foes = br.Actors.Where(a => a.Team == 1 && a.Def != null).ToList();
            Check(br.Alarm && foes.All(a => a.Kind is EnemyKind.Hound or EnemyKind.Bruiser or EnemyKind.Spirit or EnemyKind.Barghest),
                $"a stirred nest sends only critters ({string.Join(",", foes.Select(a => a.Kind).Distinct())})");
            Check(foes.Where(a => a.Kind is EnemyKind.Hound or EnemyKind.Bruiser).All(a => a.Wild), "and draws them as ghouls and hellhounds");
        }
        // The board: races for the rigger, hacks for the decker, all with a place to be.
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        var jobs = Enumerable.Range(0, 400).Select(_ => c.MakeJob(city)).Where(x => x != null).ToList();
        Check(jobs.Any(x => x.Type == JobType.Race && x.Employer == "rigger" && x.Lot2 >= 0), "Torque offers street races");
        Check(jobs.Where(x => x.Type == JobType.Hack).All(x => x.Lot == city.Place("home").Index && x.Employer == "decker"), "hacks are done from home, for the decker");
        // A race: the rival drives to the finish.
        Job race = jobs.First(x => x.Type == JobType.Race);
        c.Board.Add(race);
        c.ActiveJob = race.Id;
        var sim = new CitySim(city, c, 5);
        sim.ParkAt(city.Lots[race.Lot]);
        sim.StartRace(race);
        Check(sim.Racer != null && c.Courier, "lights, and go");
        for (int i = 0; i < 60 * 240 && sim.CourierFailed == null; i++) sim.Step(1 / 60f, new DriveInput());
        Check(sim.CourierFailed == race, "sit still and the rival wins");
    }
}

public partial class SmokeTest
{
    private void Fifth()
    {
        City city = City.Generate(55);
        string[] rows =
        {
            "####################",
            "#P.................#",
            "#..................#",
            "#..................#",
            "####################",
        };
        // Legwork.
        Career lc = TestCareer();
        lc.Cash = 10000;
        Job lj = StoryJob(JobType.Retrieve, seed: 61);
        var offers = lc.LegworkOffers(lj);
        Check(offers.Any(o => o.id == "bribe") && offers.First(o => o.id == "door").block != null, "legwork on sale; the inside man needs Juno's trust");
        Check(lc.BuyLegwork(lj, "bribe") == null && lc.BuyLegwork(lj, "routes") == null && lj.Legwork.Count == 2 && lc.Cash < 10000, "bought");
        Check(lc.BuyLegwork(lj, "bribe") != null, "once");
        Check(offers.First(o => o.id == "door").price == 200 * lj.Threat, "Juno's cleaner wants paying too");
        var lp = Site(rows);
        var lr = new Run(lj, lc, lp);
        lr.AddEnemy(EnemyKind.Guard, new V2(8.5f, 2.5f), 0);
        var lr2 = new Run(lj, lc, Site(rows));
        Check(lr2.OverwatchT >= 14, "the patrol routes show everyone at the start");
        var plan = Site(rows);
        plan.Enemies.Add(new Spawn { Kind = EnemyKind.Guard, Pos = new V2(8.5f, 2.5f) });
        plan.Enemies.Add(new Spawn { Kind = EnemyKind.Guard, Pos = new V2(10.5f, 2.5f) });
        plan.Enemies.Add(new Spawn { Kind = EnemyKind.Guard, Pos = new V2(12.5f, 2.5f) });
        var br = new Run(lj, lc, plan);
        Eq(br.Actors.Count(a => a.Hostile), 1, "two guards called in sick");
        // The trauma team.
        Job tj = StoryJob(JobType.Retrieve, Faction.Halcyon, "spire", 0);
        tj.Threat = 4;
        Run tr = null;
        for (ulong seed = 0; seed < 40 && tr == null; seed++)
        {
            tj.Seed = seed;
            if (new Rng(seed ^ 0x7E4UL).Chance(0.5f)) tr = new Run(tj, TestCareer(), Site(rows));
        }
        tr.Player.MaxHp = tr.Player.Hp = 1e6f;
        Actor g = tr.AddEnemy(EnemyKind.Guard, new V2(12.5f, 2.5f), 0);
        tr.Hurt(g, 1e6f, tr.Player, false);
        Steps(tr, 7f);
        var medics = tr.Actors.Where(a => a.IsMedic).ToList();
        Check(medics.Count == 3 && medics.All(m => m.Team == 2), "a downed corp guard brings a TraumaLink team, neutral");
        Steps(tr, 25f);
        Check(g.Escaped, "they bag the body and leave");
        Job tj2 = StoryJob(JobType.Retrieve, Faction.Halcyon, "spire", tj.Seed);
        tj2.Threat = 4;
        var tr2 = new Run(tj2, TestCareer(), Site(rows));
        tr2.Player.MaxHp = tr2.Player.Hp = 1e6f;
        Actor g2 = tr2.AddEnemy(EnemyKind.Guard, new V2(12.5f, 2.5f), 0);
        tr2.Hurt(g2, 1e6f, tr2.Player, false);
        Steps(tr2, 7f);
        Actor med = tr2.Actors.First(a => a.IsMedic);
        tr2.Hurt(med, 5, tr2.Player, false);
        Check(tr2.HtrHostile && tr2.Actors.Where(a => a.IsMedic && !a.Dead).All(a => a.Team == 1), "shoot at them and they shoot back");
        Career gc = TestCareer();
        gc.Cash = 100000;
        int price = gc.TraumaLinkPrice;
        var res = Won(gc);
        res.ShotTrauma = true;
        gc.Finish(StoryJob(seed: 62), res);
        Check(gc.TraumaGrudge && gc.TraumaLinkPrice > price * 1.5f, "TraumaLink remembers: contracts cost double");
        Check(gc.Buy(gc.Offers("doc").First(o => o.Id == "amends")) == null && !gc.TraumaGrudge, "until you make amends");
        // Platinum.
        Career pc = TestCareer();
        pc.Cash = 100000;
        pc.Buy(pc.Offers("doc").First(o => o.Id == "tlgold"));
        Check(pc.Buy(pc.Offers("doc").First(o => o.Id == "tlplat")) == null && pc.TraumaPlatinum, "Platinum, on top of Gold");
        pc.TraumaLinks = 1;
        var died = new RunResult { Died = true, ObjectiveDone = true, GrenadesLeft = pc.RunGrenades(), MedkitsLeft = pc.RunMedkits() };
        int cash = pc.Cash;
        Debrief pd = pc.Finish(StoryJob(seed: 63), died);
        Check(!pd.CareerOver && pc.JobsDone == 1 && pc.Cash > cash && pc.TraumaLinks == 0, "down with the job done: you and the job come out, at half pay");
        // Johnsons.
        Career jc = TestCareer();
        jc.CityRef = city;
        var js = Enumerable.Range(0, 600).Select(_ => jc.MakeJob(city)).Where(x => x != null && x.Johnson).ToList();
        Check(js.Count > 20 && js.All(x => Career.JohnsonById(x.JohnsonId) != null), "every negotiated job has a Johnson with a name");
        Check(js.Select(x => x.JohnsonId).Distinct().Count() == Career.Johnsons.Length, "all five are working");
        Job silk = js.First(x => x.JohnsonId == "silk" && !x.DoubleCross);
        silk.Negotiated = true;
        jc.Accept(silk);
        for (int k = 0; k < 3; k++) { jc.Set("johnson:grey:done", k); }
        Job grey = js.First(x => x.JohnsonId == "grey" && !x.DoubleCross);
        for (int k = 0; k < 3; k++) jc.Finish(grey, Won(jc));
        Check(jc.JohnsonJobs("grey") >= 3 && jc.Inbox.Any(m => m.Story == "johnson-grey"), "three clean jobs and Mr. Grey says thank you");
        Job crossed = js.First(x => x.DoubleCross && !x.Stiff);
        var tres = Won(jc);
        tres.Trap = true;
        jc.Finish(crossed, tres);
        Check(jc.JohnsonBurned(crossed.JohnsonId), "a Johnson who sold you out never calls again");
        var after = Enumerable.Range(0, 300).Select(_ => jc.MakeJob(city)).Where(x => x != null && x.Johnson).ToList();
        Check(after.All(x => x.JohnsonId != crossed.JohnsonId), "and doesn't");
        // Loot.
        Career mc = TestCareer();
        mc.Day = 10;
        var l = new LootItem { Name = "paydata chip", Value = 1000, Faction = (int)Faction.Halcyon, Day = 10, Hot = true };
        int hot = mc.LootPrice(l);
        l.Hot = false;
        int cold = mc.LootPrice(l);
        Check(hot < cold, "hot goods sell for less");
        mc.Clocks["halcyon"] = 6;
        Check(mc.LootPrice(l) > cold, "a busy faction's secrets sell for more");
        mc.Set("wanted:paydata chip", 20);
        Check(mc.LootPrice(l) > cold * 2, "and wanted ones for double");
        mc.Loot.Add(new LootItem { Name = "diamond chip", Value = 500, Day = 10, Hot = true });
        mc.Buy(mc.Offers("fence").First(o => o.Kind == ShopKind.Sell));
        Check(mc.Heat > 0, "selling hot goods draws heat");
        // The other ending of a contact's story.
        Career fc = TestCareer();
        fc.ArcStep["talismonger"] = 3;
        fc.Set("arc:talismonger", -1);
        float ess = fc.Essence;
        fc.Send("fork-talismonger");
        fc.Choose(fc.Inbox.Last(), 1);
        Check(fc.ArcPath("talismonger") == 2 && fc.Essence < ess && fc.ArmorValue == TestCareer().ArmorValue, "drawing on the circle costs essence, and no ward");
        // Ichikawa's files burned, his chrome declined: no booster, better medkits.
        Career nc = TestCareer();
        nc.ArcStep["doc"] = 3;
        nc.Set("arc:doc", -1);
        float heal = nc.MedkitHeal, essN = nc.Essence;
        nc.Send("fork-doc");
        nc.Choose(nc.Inbox.Last(), 2);
        Check(nc.ArcPath("doc") == 3 && !nc.Cyber.Contains("booster") && nc.Essence == essN && MathF.Abs(nc.MedkitHeal - heal * 1.5f) < 0.01f, "no thanks to the booster: essence kept, medkits heal half as much again");
        Career oc = TestCareer();
        oc.ArcStep["arms"] = 3;
        Eq(oc.ArcPath("arms"), 1, "a story finished before the forks counts as the usual ending");
        // Contacts in danger.
        Career dc = TestCareer();
        dc.CityRef = city;
        dc.Day = 30;
        dc.Loyalty["arms"] = 2;
        dc.Grudge[dc.Holder("industrial")] = 6;
        Check(dc.Eligible(Hook.Morning).Any(s => s.Id == "danger-arms"), "your enemies come for your contacts");
        Mail dm = dc.Send("danger-arms");
        dc.Choose(dm, 1);
        Check(dc.Shut("arms") != null && dc.Offers("arms").All(o => o.Block != null), "abandon one and their shop is shut");
        dc.HurtContact("arms");
        Check(dc.Gone("arms"), "twice, and they leave town");
        dc.Day += 10;
        Check(dc.Shut("arms") != null && dc.SceneAt("arms") == null, "for good");
        Career sc = TestCareer();
        sc.CityRef = city;
        sc.Day = 30;
        sc.Loyalty["doc"] = 2;
        sc.Grudge[sc.Holder("docks")] = 6;
        Mail sm = sc.Send("danger-doc");
        sc.Choose(sm, 0);
        Job help = sc.Board.FirstOrDefault(x => x.Tag == "danger:doc");
        Check(help != null && help.Type == JobType.Defend, "help them: a defence tonight");
        sc.Finish(help, Won(sc));
        Check(sc.Shut("doc") == null && sc.Loyalty["doc"] >= 3, "and they don't forget it");
        // The heist.
        Career hc = TestCareer();
        hc.CityRef = city;
        hc.Rep = 9;
        hc.Day = 12;
        Mail hm = hc.Send("heist");
        hc.Choose(hm, 0);
        for (int n = 1; n <= 3; n++)
        {
            Job h = hc.Board.FirstOrDefault(x => x.Tag == $"heist:{n}");
            Check(h != null, $"heist night {n}");
            if (h == null) break;
            if (n > 1) Eq(h.Lot, hc.Flag("heist:lot") - 1, "same building every night");
            if (n == 3) Check(h.Schematics && h.Has(RunMod.Blackout) && h.Pay > 10000, "the vault: dark, known, and rich");
            hc.Finish(h, Won(hc));
        }
        Check(hc.Flag("heists") == 1 && hc.Flag("heist:stage") == 0, "the score of the decade");
        // And the heist you don't show up for: warned every morning, named when it lapses.
        Career hl = TestCareer();
        hl.CityRef = city;
        hl.Rep = 9;
        hl.Day = 12;
        hl.Choose(hl.Send("heist"), 0);
        Job night = hl.Board.FirstOrDefault(x => x.Tag == "heist:1");
        Check(night != null && Career.CountedOn(night), "a heist night is one somebody is counting on");
        Check(night != null && hl.StakeOf(night).Contains("heist") && hl.DeadlineOf(night).Contains($"day {night.Expires}"),
            "and the morning card says what missing it costs, and by when");
        if (night != null)
        {
            hl.Day = night.Expires;
            Check(hl.DeadlineOf(night) == "today is the last chance", "on its last day it says so");
            hl.Day = night.Expires + 1;
            hl.RefreshBoard(city);
            Check(hl.Flag("heist:stage") == 0 && hl.News.Any(n => n.Contains("never showed") && n.Contains(night.Title)),
                "a lapsed night calls the heist off, and says which night was missed");
        }
    }
}

public partial class SmokeTest
{
    private void LateCampaigns()
    {
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        c.Set("story:won", 1);
        c.Chapter = 4;
        c.Rep = 12;
        c.JobsDone = 15;
        c.Set("tide:start", 1);
        c.Chapter2 = 4;
        c.RefreshBoard(city);
        Check(c.Inbox.Any(m => m.Story == "cut-open") && c.Inbox.Any(m => m.Story == "wyrm-open"), "a seasoned legend is offered both late campaigns");
        // The Cut, playing along with Marrow.
        for (int ch = 1; ch <= 4; ch++)
        {
            c.RefreshBoard(city);
            Job j = c.Board.FirstOrDefault(x => x.Story == 8 + ch);
            Check(j != null, $"The Cut chapter {ch}");
            if (j == null) break;
            if (ch == 3) Check(j.Type == JobType.Plant && j.Lot != city.Place("home").Index, "playing along: wire his house instead of waiting at home");
            if (ch == 4) Check(j.Has(RunMod.Skeleton) && j.Schematics && j.Target == "Kade Marrow", "and the finale knows where his people sleep");
            Accept(c, j);
            c.Finish(j, Won(c));
            if (ch == 2) c.Choose(c.Inbox.First(m => m.Story == "cut-mid"), 1);
            c.JobsDone += 2;
        }
        Check(c.Flag("cut:won") > 0 && c.Inbox.Any(m => m.Story == "cut-end"), "Marrow is down");
        // The Cut, refusing: he comes to your door.
        Career r = TestCareer();
        r.CityRef = city;
        r.Rep = 9;
        r.JobsDone = 15;
        r.Campaign["cut"] = 2;
        r.Set("cut:start", 1);
        r.Set("cut:choice", 1);
        r.RefreshBoard(city);
        Job home = r.Board.FirstOrDefault(x => x.Story == 11);
        Check(home != null && home.Type == JobType.Defend && home.Lot == city.Place("home").Index && home.Tag == "raid", "refuse him and he raids your home");
        // He studied you.
        Career s = TestCareer();
        s.CityRef = city;
        s.Rep = 9;
        s.JobsDone = 25;
        s.Skills[(int)Skill.Stealth] = 4;
        s.Skills[(int)Skill.Firearms] = 4;
        s.Campaign["cut"] = 3;
        s.Set("cut:start", 1);
        s.RefreshBoard(city);
        Job fin = s.Board.FirstOrDefault(x => x.Story == 12);
        Check(fin != null && fin.Has(RunMod.ShiftChange) && fin.Has(RunMod.Elite), "a sneaky gunslinger meets double patrols in heavy armour");
        // Your past: "come and see me" means something when you do.
        foreach (var (bg, place, first) in new[] { ("street", "fence", 17), ("corp", "press", 21), ("cop", "cop", 25), ("shaman", "talismonger", 29), ("barrens", "rigger", 33) })
        {
            Career pc = TestCareer();
            pc.CityRef = city;
            pc.Background = bg;
            pc.JobsDone = 4;
            pc.RefreshBoard(city);
            Job pj = pc.Board.FirstOrDefault(x => x.Story == first);
            Check(pj != null && pc.Inbox.Any(m => m.Story == $"past-{bg}-open"), $"{bg}: the mail, and the first chapter on the board");
            Mail visit = pc.Tell(Hook.Place, place).FirstOrDefault();
            Check(visit?.Story == $"past-{bg}-visit" && pj != null && visit.Text.Contains(pj.Title), $"{bg}: go and see them and they tell you about it");
            Check(pc.Tell(Hook.Place, place).All(m => m.Story != $"past-{bg}-visit"), $"{bg}: once");
        }
        // Story work can't fill the whole board: half of it stays ordinary jobs.
        Career full = TestCareer();
        full.CityRef = city;
        for (int k = 0; k < full.BoardSize; k++)
            full.Board.Add(new Job { Id = full.NextJobId++, Story = 1 + k, Type = JobType.Retrieve, Employer = "fixer", District = "docks", Expires = full.Day + 30, Lot = -1, Title = $"story {k}", Target = "x", Item = "x" });
        full.RefreshBoard(city);
        Check(full.Board.Count(Career.Ordinary) >= (full.BoardSize + 1) / 2, "a board full of story work still gets ordinary jobs: half of it");
        Check(full.Board.Count(j => !Career.Ordinary(j)) >= full.BoardSize, "and the story jobs all stay");
        // The Wyrm's Ledger.
        for (int ch = 1; ch <= 4; ch++)
        {
            c.RefreshBoard(city);
            Job j = c.Board.FirstOrDefault(x => x.Story == 12 + ch);
            Check(j != null, $"The Wyrm's Ledger chapter {ch}");
            if (j == null) break;
            Check(j.Employer == "decker", "Null's campaign");
            Accept(c, j);
            c.Finish(j, Won(c));
            c.JobsDone += 2;
        }
        Mail w = c.Inbox.FirstOrDefault(m => m.Story == "wyrm-end");
        Check(w != null && w.Call && w.From == "Veyrasz", "the dragon calls");
        if (w != null) c.Choose(w, 1);
        var p = new Profile();
        p.Earn(c);
        Check(p.Legacy.Contains("streetking") && p.Legacy.Contains("dragonfoe"), "both endings are legacies");
        // Dreams.
        Career m = Career.New("Dreamer", "mage", 3);
        m.CityRef = city;
        Job cross = StoryJob(seed: 71);
        cross.Johnson = cross.DoubleCross = cross.Negotiated = true;
        m.Board.Add(cross);
        m.Accept(cross);
        Check(m.Eligible(Hook.Sleep).Any(x => x.Id == "dream-cross"), "a mage dreams the set-up");
        Career chrome = TestCareer();
        foreach (string id in new[] { "eyes", "smartlink", "dermal", "muscle", "synth", "bone" }) chrome.Install(id);
        Check(chrome.Essence < 1.5f && chrome.Eligible(Hook.Morning).Any(x => x.Id == "psy-mail"), "too much chrome writes to itself");
    }
}

public partial class SmokeTest
{
    private void NewFaces()
    {
        City city = City.Generate(55);
        Check(city.Place("cop") != null && city.Place("press") != null, "Dace and Quill have doors");
        Check(Tables.Contacts.Where(k => !k.Late).Select(k => city.Place(k.Id).Index).Distinct().Count() == Tables.Contacts.Count(k => !k.Late), "everyone else kept a lot of their own");
        Career c = TestCareer();
        c.CityRef = city;
        c.Cash = 50000;
        c.Heat = 3;
        c.Awareness = 5;
        Check(c.Buy(c.Offers("cop").First(o => o.Id == "file")) == null && c.Heat == 0, "Dace loses your file");
        Check(c.Buy(c.Offers("press").First(o => o.Id == "kill")) == null && c.Awareness == 2, "Quill kills a story");
        int cash = c.Cash;
        Check(c.Buy(c.Offers("press").First(o => o.Id == "sell")) == null && c.Cash > cash && c.Awareness == 4, "or buys yours");
        Check(c.Offers("press").First(o => o.Id == "sell").Block?.Contains("this week") == true, "once a week");
        c.Day += 7;
        Check(c.Offers("press").First(o => o.Id == "sell").Block == null, "and again the week after");
        c.Awareness = 9;
        Check(c.Offers("press").First(o => o.Id == "sell").Block != null, "not when the whole city knows you already");
        c.Awareness = 4;
        c.Clocks["halcyon"] = 6;
        c.Loyalty["press"] = 1;
        Check(c.Buy(c.Offers("press").First(o => o.Id == "smear:halcyon")) == null && c.Clock("halcyon") == 3, "a week of bad press sets a faction back");
        foreach (string who in new[] { "cop", "press" })
        {
            Check(Career.Arcs.ContainsKey(who) && global::Sprawl.Storylets.ById("fork-" + who) != null && Career.Favours.ContainsKey(who), $"{who}: a story, an ending to choose, a favour");
            var job = new Job { Type = Career.Arcs[who].parts[0].Type, Style = Career.Arcs[who].parts[0].Style, Faction = Career.Arcs[who].parts[0].Faction, Threat = 1, Seed = 3, Count = 3, Target = "x", Item = "x", Title = "x" };
            Check(LevelGen.Generate(job).Rooms.Count > 0, $"{who}: part 1 builds");
        }
        Career b = TestCareer();
        b.ArcStep["cop"] = 3;
        b.Set("arc:cop", 1);
        b.Heat = 3;
        var sim = new CitySim(city, b, 3);
        sim.Chasers.Add(new Chaser { Kind = ChaserKind.Police, Car = new Car { Def = CitySim.PoliceCar, Hp = 90, Pos = sim.Player.Pos + new V2(1.5f, 0), Heading = sim.Player.Heading } });
        for (int i = 0; i < 60 * 4; i++) sim.Step(1 / 60f, new DriveInput());
        Check(b.Stat("busts") == 0, "Dace's badge: Lone Star never pulls you over");
        // Recruits from the stories.
        Career r = TestCareer();
        Check(r.CrewBlock("ade") != null && r.CrewBlock("byte") != null, "the recruits aren't free yet");
        r.ArcStep["doc"] = 2;
        r.ArcStep["decker"] = 2;
        Check(r.CrewBlock("ade") == null && r.CrewBlock("byte") == null, "until their stories bring them to you");
        Job bj = TestJob(JobType.Destroy);
        bj.Hired = "byte";
        var run = new Run(bj, r, Site("############", "#P.........#", "#..........#", "############"));
        Actor cam = run.AddEnemy(EnemyKind.Camera, new V2(8.5f, 1.5f), 0);
        Steps(run, 1f);
        Check(cam.Stun > 0, "Byte blinds a camera");
        // The radio and the walls.
        Career f = TestCareer();
        f.Name = "Static";
        f.Awareness = 6;
        f.Heat = 2;
        f.Say("KBN: Something happened.");
        var radio = f.Radio("docks", true);
        Check(radio.Any(x => x.station == "KBN") && radio.Any(x => x.station == "SCANNER" && x.text.Contains("suspect")) && radio.Any(x => x.station == "KBAY PIRATE" && x.text.Contains("Static")), "the radio: the news, the scanner, and a pirate who knows your name");
        f.Set("held:docks", 3);
        var tags = f.Graffiti(city);
        Check(tags.Values.Any(t => t.text == "STATIC WAS HERE") && tags.Values.Any(t => t.text is "ASH VIPERS RULE" or "THE LOTUS BLOOMS" or "HALCYON SECURE ZONE"), "and the walls talk about you, and whoever runs the street");
        // Each runner looks like their trade.
        var looks = Career.Archetypes.Select(a => Art.PlayerLook(a.id)).ToList();
        Check(looks.Select(l => l.Mark).Distinct().Count() == looks.Count && looks.Select(l => l.Body).Distinct().Count() == looks.Count, "every archetype looks different");
    }
}

public partial class SmokeTest
{
    private void Towers()
    {
        // On the board: the brief and the client are for the tower as built (Halcyon's, at threat 6).
        {
            City tcity = City.Generate(55);
            Career tc = TestCareer();
            tc.CityRef = tcity;
            tc.Rep = 30;
            var tw = Enumerable.Range(0, 400).Select(_ => tc.MakeJob(tcity)).Where(x => x != null && x.Floors > 0).ToList();
            Check(tw.Count > 0 && tw.All(x => x.Client != (int)x.Faction), $"a tower's client is never its owner ({tw.Count} towers)");
            Check(tw.Where(x => x.Threat >= 6 && x.Type is JobType.Eliminate or JobType.Retrieve or JobType.Datasteal).All(x => x.Brief.Contains(Tables.FactionNames[(int)Faction.Halcyon]) && (x.Brief.Contains("office") || x.Brief.Contains("lab"))), "and the brief names the tower's real owner and building");
        }
        // The plan: a lobby and floors side by side, a core on every floor, stairs between, secure floors at the top.
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var job = new Job { Type = seed % 2 == 0 ? JobType.Datasteal : JobType.Eliminate, Style = SiteStyle.Office, Faction = Faction.Halcyon, Threat = 5, Seed = seed, Floors = 3 + (int)(seed % 3), Target = "x", Item = "x", Title = "x" };
            LevelPlan p = LevelGen.Generate(job);
            string tag = $"tower {seed} ({job.Floors} floors)";
            Check(p.Tower && p.Regions.Count == job.Floors + 1 && p.Lifts.Count == job.Floors + 1, $"{tag}: a lobby, its floors, an elevator on each");
            Check(p.Secure.Last() && !p.Secure[0], $"{tag}: the top is secure, the lobby is not");
            Check(Enumerable.Range(0, job.Floors).All(k => p.L.Stair.Any(kv => p.FloorAt(p.L.PtOf(kv.Key).X) == k && p.FloorAt(p.L.PtOf(kv.Value).X) == k + 1)), $"{tag}: stairs join every floor to the next");
            V2 obj = job.Type == JobType.Datasteal ? p.Mainframe.Centre : p.Enemies.First(e => e.Kind == EnemyKind.Vip).Pos;
            Check(p.FloorAt(obj.X) == job.Floors, $"{tag}: the objective is on the top floor (floor {p.FloorAt(obj.X)})");
            Check(LevelGen.Reachable(p, Pt.Of(obj)), $"{tag}: and reachable by the stairs");
            Check(Enumerable.Range(1, job.Floors).All(k => p.Enemies.Count(e => p.FloorAt(e.Pos.X) == k) >= 3), $"{tag}: every floor is guarded");
            int low = p.Enemies.Count(e => p.FloorAt(e.Pos.X) == 1), high = p.Enemies.Count(e => p.FloorAt(e.Pos.X) == job.Floors);
            Check(high > low, $"{tag}: more guards the higher you go ({low} -> {high})");
        }
        // The elevator.
        var tj = new Job { Id = 3, Type = JobType.Datasteal, Style = SiteStyle.Office, Faction = Faction.Halcyon, Threat = 5, Seed = 4, Floors = 4, Target = "x", Item = "x", Title = "x", Lot = -1 };
        var c = TestCareer();
        var r = new Run(tj, c, LevelGen.Generate(tj));
        foreach (Actor a in r.Actors.Where(a => a.Hostile).ToList()) r.Actors.Remove(a);
        r.Player.MaxHp = r.Player.Hp = 1e6f;
        var input = new RunInput { Slot = -1, Interact = true, Aim = r.Player.Pos + new V2(1, 0) };
        r.Player.Pos = r.Plan.LiftSpots[0];
        Steps(r, 0.3f, input);
        Check(r.Prompt != null && r.Prompt.Contains("hack the elevator"), $"the elevator needs hacking first ('{r.Prompt}')");
        Steps(r, 8f, input);
        Check(r.LiftHacked, "hacked");
        Check(r.Served(1) && r.Served(2) && !r.Served(4), "it serves the public floors, not the secure ones");
        r.Step(new RunInput { Slot = -1, Interact = true, InteractPressed = true, Aim = r.Player.Pos + new V2(1, 0) });
        Steps(r, 3f);
        Eq(r.PlayerFloor, 1, "a ride up");
        r.Player.Pos = r.Plan.LiftSpots[1];
        r.Step(new RunInput { Slot = -1, Interact = true, InteractPressed = true, Sneak = true, Aim = r.Player.Pos + new V2(1, 0) });
        Steps(r, 3f);
        Eq(r.PlayerFloor, 0, "and back down (Shift)");
        // The stairs reach the secure floors; hacking the panel there opens them.
        int top = r.FloorCount - 1;
        r.Player.Pos = r.Plan.LiftSpots[top];
        Steps(r, 0.3f, input);
        Check(r.Prompt != null && r.Prompt.Contains("this floor"), $"on a secure floor, the panel is locked ('{r.Prompt}')");
        Steps(r, 8f, input);
        Check(r.Served(top), "hacked from inside: the elevator stops here now");
        // The Matrix override.
        Career d = Career.New("Decker", "decker", 4);
        d.Skills[(int)Skill.Decking] = 4;
        // (An easier tower: at threat 5 the security host runs black ICE, which the test bot is no match for.)
        var ej = new Job { Id = 4, Type = JobType.Datasteal, Style = SiteStyle.Office, Faction = Faction.Halcyon, Threat = 2, Seed = 4, Floors = 4, Target = "x", Item = "x", Title = "x", Lot = -1 };
        var dr = new Run(ej, d, LevelGen.Generate(ej));
        foreach (Actor a in dr.Actors.Where(a => a.Hostile).ToList()) dr.Actors.Remove(a);
        dr.Player.MaxHp = dr.Player.Hp = 1e6f;
        dr.LiftHacked = true;
        int under = dr.FloorCount - 3;
        dr.Player.Pos = dr.Plan.LiftSpots[under];
        while (dr.Served(dr.PlayerFloor + 1)) dr.Player.Pos = dr.Plan.LiftSpots[dr.PlayerFloor + 1];
        Check(dr.CanOverride, "a decker can override the pass");
        V2 spot = dr.Player.Pos;
        for (int attempt = 0; attempt < 6 && !dr.LiftOverride; attempt++)
        {
            dr.Player.Pos = spot;
            dr.Player.Hp = dr.Player.MaxHp;
            dr.Step(new RunInput { Slot = -1, Interact = true, InteractPressed = true, Aim = dr.Player.Pos + new V2(1, 0) });
            if (attempt == 0) Check(dr.Matrix != null, "jacked into the security host");
            // The bot is a poor decker: give it two of the keys and let it fetch the last.
            if (dr.Matrix != null) dr.Matrix.Taken.AddRange(dr.Matrix.Stores.Take(2).Where(st => !dr.Matrix.Taken.Contains(st)));
            if (dr.Matrix != null) foreach (MxFile f in dr.Matrix.Files.Where(f => dr.Matrix.Taken.Contains(f.At))) f.Taken = true;
            for (int i = 0; i < 60 * 120 && dr.Matrix != null; i++) dr.Step(MatrixInput(dr.Matrix, i));
            Steps(dr, 0.7f);
        }
        Check(dr.LiftOverride && dr.Served(dr.FloorCount - 1), "three keys, and every floor is open");
        // The board: late, rich, and hard.
        City city = City.Generate(55);
        Career late = TestCareer();
        late.CityRef = city;
        late.Rep = 20;
        var jobs = Enumerable.Range(0, 400).Select(_ => late.MakeJob(city)).Where(x => x != null).ToList();
        var towers = jobs.Where(x => x.Floors > 0).ToList();
        Check(towers.Count > 5 && towers.All(x => x.Threat >= 5), $"towers are late-game sites ({towers.Count})");
        Check(jobs.Any(x => x.Threat == 6 && x.Floors >= 4 && x.Has(RunMod.Elite)), "prestige towers: threat 6, four floors and up, elite");
        double tp = towers.Where(x => x.Threat == 5).Select(x => x.Pay).DefaultIfEmpty(0).Average(), np = jobs.Where(x => x.Threat == 5 && x.Floors == 0 && !x.Driving).Select(x => x.Pay).DefaultIfEmpty(1).Average();
        Check(tp > np * 1.4f, $"and they pay for every floor ({tp:0} vs {np:0})");
        Check(TestCareer().MakeJob(city) is Job early && early.Floors == 0 && !TestCareer().Prestige, "nobody starts with a tower");
    }
}

public partial class SmokeTest
{
    private void TopTier()
    {
        City city = City.Generate(55);
        // Elite stock waits for a name.
        Career c = TestCareer();
        c.Cash = 2000000;
        foreach (ContactDef k in Tables.Contacts) c.Loyalty[k.Id] = 5;
        Check(c.Offers("arms").First(o => o.Id == "laser").Block?.Contains("name") == true, "the heavy laser isn't sold to just anyone");
        c.Rep = 18;
        Check(c.Buy(c.Offers("arms").First(o => o.Id == "laser")) == null, "a runner with a name can buy it");
        Check(c.Buy(c.Offers("arms").First(o => o.Id == "milspec")) == null && c.ArmorValue >= 15, "mil-spec armour");
        // The laser ignores armour; the rail rifle goes through walls.
        string[] rows = { "##########", "#P.......#", "##########" };
        var r = new Run(TestJob(), c, Site(rows));
        Actor heavy = r.AddEnemy(EnemyKind.Heavy, new V2(6.5f, 1.5f), 0);
        float before = heavy.Hp;
        r.Shots.Clear();
        WeaponDef laser = Tables.Weapon("laser");
        // The shot as the player fires it (armour x0).
        r.Hurt(heavy, laser.Damage, r.Player, false, armorMul: laser.ArmorMul);
        Check(before - heavy.Hp >= laser.Damage - 0.01f, "the laser's light doesn't care about armour");
        // Cyberware grades.
        Career g = TestCareer();
        g.Cash = 1000000;
        g.Loyalty["doc"] = 5;
        g.Rep = 18;
        g.Buy(g.Offers("doc").First(o => o.Id == "bone"));
        float ess = g.Essence;
        Offer refit = g.Offers("doc").First(o => o.Kind == ShopKind.Grade && o.Id == "bone");
        Check(refit.Name.Contains("Alphaware") && g.Buy(refit) == null && g.Essence > ess, "refit an implant as alphaware: less essence");
        for (int k = 0; k < 2; k++) g.Buy(g.Offers("doc").First(o => o.Kind == ShopKind.Grade && o.Id == "bone"));
        Eq(g.GradeOf("bone").id, "delta", "all the way to deltaware");
        Check(MathF.Abs(g.EssenceOf("bone") - 0.4f) < 0.01f, "half the essence");
        // Or bought graded from the box, at five, ten or twenty times the price.
        Check(Tables.Grades.Select(x => x.price).SequenceEqual(new[] { 1f, 5f, 10f, 20f }) && Tables.Grades.Select(x => x.essence).SequenceEqual(new[] { 1f, 0.8f, 0.7f, 0.5f }), "grades: 0.8/0.7/0.5 essence at 5/10/20 x");
        Offer std = g.Offers("doc").First(o => o.Kind == ShopKind.Cyber && o.Id == "eyes" && o.Extra == null);
        Offer box = g.Offers("doc").First(o => o.Kind == ShopKind.Cyber && o.Id == "eyes" && o.Extra != null);
        Check(box.Extra == "delta" && box.Price > std.Price * 15, $"the doc sells new deltaware ({box.Name}, {M.Money(box.Price)} vs {M.Money(std.Price)})");
        float before2 = g.Essence;
        Check(g.Buy(box) == null && g.GradeOf("eyes").id == "delta" && MathF.Abs(before2 - g.Essence - Tables.CyberById("eyes").Essence * 0.5f) < 0.01f, "installed at deltaware's essence");
        Career lowTrust = TestCareer();
        lowTrust.Loyalty["doc"] = 2;
        Check(lowTrust.Offers("doc").Where(o => o.Kind == ShopKind.Cyber && o.Extra != null).All(o => o.Extra == "alpha"), "at trust 2, only alphaware");
        // Bioware and wiring.
        Career w = TestCareer();
        float speed = w.MoveSpeed, dash = w.DashCooldown;
        w.Cyber.Add("movebywire");
        Check(w.MoveSpeed > speed && w.DashCooldown < dash, "move-by-wire: faster, dash back sooner");
        w.Cyber.Add("wired2");
        Check(w.Duration(Ability.Overclock) > 4 && w.Cooldown(Ability.Overclock) < Career.AbilityCooldown(Ability.Overclock) * w.CooldownMul, "wired reflexes II: a longer, sooner Overclock");
        // Car mods.
        Career m = TestCareer();
        m.Cash = 500000;
        m.Loyalty["rigger"] = 5;
        float top = m.Ride().Top;
        m.Buy(m.Offers("rigger").First(o => o.Id == "engine1"));
        Check(m.Ride().Top > top && m.Offers("rigger").Any(o => o.Id == "engine2") && !m.Offers("rigger").Any(o => o.Id == "engine1"), "a tuned engine, and the next step on offer");
        m.Buy(m.Offers("rigger").First(o => o.Id == "engine2"));
        Check(m.CarHas("engine2") && !m.CarHas("engine1"), "each step replaces the last");
        int hp = m.CarMaxHp;
        m.Buy(m.Offers("rigger").First(o => o.Id == "armour1"));
        Check(m.CarMaxHp > hp, "armour plate: more hull");
        foreach (string id in new[] { "nitro", "smoke", "oil" }) m.Buy(m.Offers("rigger").First(o => o.Id == id));
        var sim = new CitySim(city, m, 7);
        sim.Step(1 / 60f, new DriveInput { Throttle = 1, Boost = true });
        Check(sim.BoostT > 0 && sim.Nitro == 2, "nitro: Shift, three a drive");
        sim.Chasers.Add(new Chaser { Kind = ChaserKind.Gang, Car = new Car { Def = CitySim.GangCar, Hp = 70, Pos = sim.Player.Pos - sim.Player.Forward * 6, Heading = sim.Player.Heading } });
        sim.Step(1 / 60f, new DriveInput { Smoke = true });
        Check(sim.Chasers[0].BlindT > 0, "smoke blinds whoever is behind");
        sim.Step(1 / 60f, new DriveInput { Oil = true });
        sim.Chasers[0].Car.Pos = sim.OilPatches[0].pos;
        sim.Step(1 / 60f, new DriveInput());
        Check(sim.Chasers[0].SpinT > 0, "and oil spins them out");
        // Lifestyle, property, retainers.
        Career l = TestCareer();
        l.CityRef = city;
        l.Cash = 1000000;
        l.Loyalty["fixer"] = 5;
        int rent = l.RentDue;
        var tiers = l.Offers("home").Where(o => o.Kind == ShopKind.Lifestyle).ToList();
        Check(tiers.Count == Career.Lifestyles.Length && tiers.Single(o => o.Owned == "current").Id == l.Lifestyle, "every lifestyle is listed, yours marked");
        Check(l.LifestyleLine.StartsWith("Low lifestyle") && l.LifestyleLine.Contains("every morning"), $"and said in one line ({l.LifestyleLine})");
        l.Buy(l.Offers("home").First(o => o.Kind == ShopKind.Lifestyle && o.Id == "high"));
        Check(l.Offers("home").Single(o => o.Owned == "current").Id == "high", "moving moves the mark");
        Check(l.RentDue > rent * 5, "high living costs");
        l.Injuries = 3;
        l.NextDay(city, rested: true);
        Check(l.Injuries == 0 && l.WellFed, "and mends you, and feeds you");
        l.Buy(l.Offers("fixer").First(o => o.Kind == ShopKind.Property && o.Id == "noodles"));
        l.Day = 6;
        int cash = l.Cash;
        Faction street = l.Holder("docks");
        l.Grudge[street] = 0;
        int standing = l.StandingOf(street);
        l.NextDay(city);
        Check(l.Cash > cash - l.RentDue, $"a business pays every week ({l.Cash - cash})");
        Check(Career.Businesses.All(b => b.cost / b.week is >= 9 and <= 11), "and pays for itself in about ten weeks");
        Check(l.StandingOf(street) == standing + 1, "and makes friends of whoever runs the street");
        l.Buy(l.Offers("fixer").First(o => o.Kind == ShopKind.Retainer && o.Id == "ox"));
        Check(l.Retained.Contains("ox") && l.CrewCut("ox") == 0, "Ox on retainer takes no cut");
        // Spending makes you a regular, and they say so.
        Career reg = TestCareer();
        reg.Cash = 100000;
        reg.Loyalty["arms"] = 0;
        foreach (Offer o in reg.Offers("arms").Where(o => o.Kind == ShopKind.Weapon && o.Block == null).OrderByDescending(o => o.Price).Take(4).ToList()) reg.Buy(o);
        Check(reg.Loyalty["arms"] >= 1 && reg.News.Any(n => n.Contains("regular now")), "loyalty from spending is announced");
        Check(global::Sprawl.Storylets.ById("turf").Weight >= 20, "a district changing hands takes the morning's news");
        // Luxury: TraumaLink for less, and three karma a week.
        Career lux = TestCareer();
        lux.CityRef = city;
        lux.Cash = 1000000;
        int tl = lux.TraumaLinkPrice;
        lux.Lifestyle = "luxury";
        Check(lux.TraumaLinkPrice < tl * 0.8f, $"luxury: TraumaLink for less ({tl} -> {lux.TraumaLinkPrice})");
        lux.Day = 6;
        int karma = lux.Karma;
        lux.NextDay(city);
        Eq(lux.Karma - karma, 3, "and three karma a week");
        // Sgt. Dace on the payroll: fame brings no patrols, and a loud run half the heat.
        Career dc = TestCareer();
        dc.CityRef = city;
        dc.Awareness = 9;
        dc.NextDay(city);
        float famous = dc.Heat;
        dc.Set("dace", dc.Day);
        dc.NextDay(city);
        Check(famous >= 2 && dc.Heat == 0, $"paying Dace: your face on the news brings no heat ({famous:0.0} -> {dc.Heat:0.0})");
        Job dj = new() { Faction = Faction.Vipers };
        dc.RunHeat(dj, alarm: true, died: false, kills: 0);
        Check(MathF.Abs(dc.Heat - 0.5f) < 0.01f, "and a loud job draws half the heat");
        dc.Day += 9;
        dc.Heat = 0;
        dc.RunHeat(dj, alarm: true, died: false, kills: 0);
        Check(MathF.Abs(dc.Heat - 1) < 0.01f, "stop paying, and it's all yours again");
        // Crew on retainer come on the job: the most trusted one free to.
        Career rc = TestCareer();
        rc.CityRef = city;
        rc.RefreshBoard(city);
        rc.Retained.Add("ox");
        rc.Retained.Add("wisp");
        rc.CrewOf("wisp").Trust = 2;
        Job rj = rc.Board.First(b => !b.Driving && b.Type != JobType.Hack && !b.Johnson);
        Accept(rc, rj);
        Check(rj.Hired == "wisp", $"accept a job: the retainer comes along ({rj.Hired})");
        var rrun = new Run(rj, rc, Site("#########", "#P......#", "#########"));
        Check(rrun.Ally != null && rrun.Ally.Name == Tables.Hires.First(h => h.id == "wisp").name, "and is there on the run");
        Check(rc.Offers("fixer").First(o => o.Kind == ShopKind.Hire && o.Id == "ox").Block == null, "another hire can still take their place");
        rc.Buy(rc.Offers("fixer").First(o => o.Kind == ShopKind.Hire && o.Id == "ox"));
        Check(rj.Hired == "ox", "and does");
        rc.Drop();
        rc.CrewOf("wisp").OutUntil = rc.Day + 3;
        rc.Retained.Remove("ox");
        Job rj2 = rc.Board.First(b => !b.Driving && b.Type != JobType.Hack && !b.Johnson && b.Hired == null);
        Accept(rc, rj2);
        Check(rj2.Hired == null, "a recovering retainee stays home");
        rc.Loyalty["fixer"] = 5;
        rc.Buy(rc.Offers("fixer").First(o => o.Kind == ShopKind.Retainer && o.Id == "ox"));
        Check(rj2.Hired == "ox", $"put someone on retainer mid-job: they join it now ({rj2.Hired})");
        rc.Retained.Remove("ox");
        rj2.Hired = null;
        rc.Drop();
        rc.CrewOf("wisp").OutUntil = 0;
        Job rd = rc.Board.FirstOrDefault(b => b.Driving) ?? rj2;
        if (rd == rj2) { rj2.Type = JobType.Courier; }
        rd.Hired = null;
        rd.Johnson = false;
        Accept(rc, rd);
        Check(rd.Hired == null, "a driving job brings nobody");
        int oxWage = l.RetainerWage("ox");
        l.Rep = 30;
        Check(l.RetainerWage("ox") == Math.Max(oxWage, 600 * l.MaxThreat) && l.RetainerWage("ade") >= 3000, $"a retainer keeps up with the jobs you take ({l.RetainerWage("ade")} at threat {l.MaxThreat})");
        // Focus ranks.
        Career f = Career.New("Mage", "mage", 2);
        f.Cash = 500000;
        f.Loyalty["talismonger"] = 5;
        f.Rep = 18;
        f.Buy(f.Offers("talismonger").First(o => o.Kind == ShopKind.Focus));
        float p1 = f.SpellPower;
        f.Buy(f.Offers("talismonger").First(o => o.Kind == ShopKind.Focus));
        f.Buy(f.Offers("talismonger").First(o => o.Kind == ShopKind.Focus));
        Check(f.FocusRank == 3 && f.SpellPower > p1 * 1.2f, "power focus III");
        // The helicopter.
        Career h = TestCareer();
        h.CityRef = city;
        h.Cash = 1000000;
        h.Loyalty["rigger"] = 5;
        h.Rep = 18;
        string car = h.Vehicle;
        Check(h.Buy(h.Offers("rigger").First(o => o.Id == "kite")) == null && h.OwnsHeli && h.Vehicle == car, "a helicopter, and the car is still the car");
        var hs = new CitySim(city, h, 3);
        Lot far = city.Place("decker");
        hs.TravelTo(far, true);
        Check(V2.Distance(hs.Player.Pos, far.Door.Centre) < 3, "fly anywhere");
        var tj = new Job { Id = 3, Type = JobType.Datasteal, Style = SiteStyle.Office, Faction = Faction.Halcyon, Threat = 3, Seed = 4, Floors = 3, Target = "x", Item = "x", Title = "x", Lot = -1 };
        var tr = new Run(tj, h, LevelGen.Generate(tj));
        Check(tr.RoofPickup && tr.InExtraction(tr.Plan.LiftSpots[^1]), "and get picked up from a tower's top floor");
        // A cab.
        Career t = TestCareer();
        t.Cash = 5000;
        var ts = new CitySim(city, t, 3);
        int fare = ts.Fare(far);
        ts.TravelTo(far, false);
        Check(t.Cash == 5000 - fare && V2.Distance(ts.Player.Pos, far.Door.Centre) < 3, $"a cab: pay the fare ({fare}), get there");
    }
}

public partial class SmokeTest
{
    private void Verbs()
    {
        string[] rows =
        {
            "##################",
            "#P...............#",
            "#................#",
            "#........d.......#",
            "#........d.......#",
            "##################",
        };
        // The Face talks.
        Career face = Career.New("Face", "face", 3);
        face.Attrs[(int)Attr.Charisma] = 6;
        var r = new Run(TestJob(JobType.Destroy, Faction.Vipers), face, Site(rows));
        Actor g = r.AddEnemy(EnemyKind.Ganger, new V2(4.5f, 2.5f), MathF.PI);
        r.Player.Pos = new V2(3.5f, 2.5f);
        r.Step(new RunInput { Slot = -1, Aim = g.Pos });
        Check(r.Prompt != null && r.Prompt.Contains("talk your way"), $"a face can talk to an unaware ganger ('{r.Prompt}')");
        Check(r.BluffChance(g) > 0.7f, $"good odds with Charisma 6 and the right code ({r.BluffChance(g):0.00})");
        r.Step(new RunInput { Slot = -1, Interact = true, InteractPressed = true, Aim = g.Pos });
        Check(g.BluffTried && (g.BluffT > 0) != g.Alert, "and either walks past or gets made");
        if (g.BluffT > 0)
        {
            Steps(r, 3, new RunInput { Slot = -1, Aim = g.Pos });
            Check(!g.Alert && g.Detect == 0, "talked round: they let you be");
        }
        // The uniform.
        var u = new Run(TestJob(JobType.Destroy, Faction.Halcyon), TestCareer(), Site(rows));
        Actor dg = u.AddEnemy(EnemyKind.Guard, new V2(5.5f, 2.5f), 0);
        u.Hurt(dg, 1e6f, u.Player, false, nonLethal: true);
        u.Player.Pos = dg.Pos + new V2(0.3f, 0);
        Steps(u, 0.2f);
        Steps(u, 3f, new RunInput { Slot = -1, Interact = true, Sneak = true, Aim = u.Player.Pos + new V2(1, 0) });
        Check(u.Disguised, "Shift and hold E: the guard's uniform");
        Actor watcher = u.AddEnemy(EnemyKind.Guard, new V2(12.5f, 2.5f), MathF.PI);
        Steps(u, 1f, new RunInput { Slot = -1, Aim = watcher.Pos });
        float masked = watcher.Detect;
        var plain = new Run(TestJob(JobType.Destroy, Faction.Halcyon), TestCareer(), Site(rows));
        plain.Player.Pos = u.Player.Pos;
        Actor w2 = plain.AddEnemy(EnemyKind.Guard, new V2(12.5f, 2.5f), MathF.PI);
        Steps(plain, 1f, new RunInput { Slot = -1, Aim = w2.Pos });
        Check(masked < w2.Detect * 0.5f || w2.Alert, $"in uniform you're spotted far slower ({masked:0.00} vs {w2.Detect:0.00})");
        u.Step(new RunInput { Slot = 1, Fire = true, Aim = watcher.Pos });
        Steps(u, 0.3f, new RunInput { Slot = -1, Fire = true, Aim = watcher.Pos });
        Check(!u.Disguised, "and it's blown the moment you shoot");
        // The mage leaves the body.
        Career mage = Career.New("Mage", "mage", 3);
        mage.Spells.Add("projection");
        mage.Spells.Add("ward");
        mage.SlotQ = Ability.Project;
        mage.SlotF = Ability.Ward;
        var m = new Run(TestJob(JobType.Destroy, Faction.Lotus), mage, Site(rows));
        Actor hidden = m.AddEnemy(EnemyKind.Guard, new V2(12.5f, 4.5f), 0);
        m.Player.Pos = new V2(7.5f, 4.5f);
        m.Step(new RunInput { Slot = -1, AbilityQ = true, Aim = m.Player.Pos + new V2(1, 0) });
        Check(m.Projecting, "astral projection");
        V2 body = m.Player.Pos;
        Steps(m, 1f, new RunInput { Slot = -1, Move = new V2(1, 0), Aim = m.Player.Pos + new V2(1, 0) });
        Check(m.AstralForm.Pos.X > 10 && m.Player.Pos == body, "the form drifts through the wall; the body stays");
        Check(m.Shown(hidden), "and sees who is behind it");
        m.Hurt(m.Player, 10, null, false);
        Check(!m.Projecting, "a hit pulls you back");
        // The ward.
        var wr = new Run(TestJob(JobType.Datasteal, Faction.Lotus), mage, Site(rows));
        wr.Player.Pos = new V2(2.5f, 2.5f);
        wr.Step(new RunInput { Slot = -1, AbilityF = true, Aim = new V2(6.5f, 2.5f) });
        Check(wr.L[6, 2] == Tile.Ward && Level.Defs[(int)Tile.Ward].BlocksShot, "a ward of light bars the corridor, and stops bullets");
        Steps(wr, 13f);
        Check(wr.L[6, 2] == Tile.Floor, "for a while");
        // Spell ranks.
        float p0 = mage.PowerOf(Ability.Manabolt);
        mage.Karma = 100;
        mage.RaiseSpell("manabolt");
        Check(mage.PowerOf(Ability.Manabolt) > p0 * 1.1f, "practised spells hit harder");
        // The decker at a console.
        var dk = Career.New("Decker", "decker", 3);
        var plan = Site(rows);
        plan.L.Set(3, 3, Tile.Console);
        var dr = new Run(TestJob(JobType.Datasteal, Faction.Halcyon), dk, plan);
        Actor ga = dr.AddEnemy(EnemyKind.Guard, new V2(6.5f, 3.5f), MathF.PI);
        ga.BaseFacing = 0;
        ga.Facing = 0;
        dr.Player.Pos = new V2(3.5f, 2.5f);
        dr.Step(new RunInput { Slot = -1, Interact = true, InteractPressed = true, Sneak = true, Aim = new V2(3.5f, 3.5f) });
        Check(ga.State == AiState.Suspicious && ga.Goal.X > 8, "a spoofed radio call sends the guard away");
        // The getaway.
        City city = City.Generate(55);
        var sim = new CitySim(city, TestCareer(), 4);
        sim.Getaway(2);
        Check(sim.Chasers.Count(c => c.Kind == ChaserKind.Police) == 2 && sim.C.Heat >= 2, "a loud exit: Lone Star is outside");
    }
}
