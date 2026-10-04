using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>Choices on the run, and the story's knowledge of who you were.</summary>
public partial class SmokeTest
{
    private void RunChoices()
    {
        // The hostage who begs at the door: find a job where they do.
        ulong seed = 1;
        while (!new Rng(seed ^ 0xBE65UL).Chance(1 / 3f)) seed++;
        var j = new Job { Id = 77, Type = JobType.Rescue, Style = SiteStyle.Den, Faction = Faction.Vipers, District = "barrens", Threat = 2, Seed = seed, Target = "Ilya Brandt", Item = "x", Title = "x", Lot = -1, Pay = 3000, Karma = 5, Employer = "fixer" };
        Career c = TestCareer();
        var r = new Run(j, c, LevelGen.Generate(j));
        foreach (Actor a in r.Actors.Where(a => a.Hostile).ToList()) r.Actors.Remove(a);
        r.Hostage.State = AiState.Follow;
        // A couple of steps short of the exit, on open floor.
        Rect ex = r.Plan.Extraction;
        r.Player.Pos = Enumerable.Range(0, 400).Select(k => new V2(ex.X - 4 + k % 20 * 0.6f, ex.Y - 4 + k / 20 * 0.6f))
            .First(p => !ex.Contains(p) && r.L.Walkable((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y)) && Math.Max(Math.Max(ex.X - p.X, p.X - ex.Right), Math.Max(ex.Y - p.Y, p.Y - ex.Bottom)) is > 1 and < 2.5f);
        r.Hostage.Pos = r.Player.Pos + new V2(0.6f, 0);
        Steps(r, 0.2f);
        Check(r.Pending?.Id == "begs", "at the exit, the hostage begs to be let go");
        float before = r.Time;
        Steps(r, 2f);
        Check(r.Pending != null && r.Time == before, "the clock stops until you decide");
        r.Step(new RunInput { Slot = 1, Aim = r.Player.Pos + new V2(1, 0) });
        Check(r.Pending == null && r.ObjectiveLost, "let go: the job fails");
        RunResult res = r.Result();
        Check(res.Choices.Contains("begs:1") && !res.HostageDead, "the result remembers the choice, and nobody died");
        int karma = c.Karma;
        Debrief d = c.Finish(j, res);
        Check(c.Karma >= karma + 3 && c.Flag("fate:Ilya Brandt") == Career.Fates["rescued"] && c.Flag("friend:Ilya Brandt") > 0, "karma and a friend for it");
        Check(d.Lines.Any(l => l.Item1.Contains("let")), "and the receipt says so");

        // The dirt on the mainframe.
        ulong ds = 1;
        while (!new Rng(ds ^ 0xD127UL).Chance(1 / 3f)) ds++;
        var dj = new Job { Id = 78, Type = JobType.Datasteal, Style = SiteStyle.Office, Faction = Faction.Halcyon, District = "industrial", Threat = 2, Seed = ds, Target = "x", Item = "x", Title = "x", Lot = -1, Pay = 3000, Karma = 5, Employer = "fixer" };
        var dr = new Run(dj, TestCareer(), LevelGen.Generate(dj));
        foreach (Actor a in dr.Actors.Where(a => a.Hostile).ToList()) dr.Actors.Remove(a);
        dr.DataProgress = 1;
        dr.Player.Pos = dr.Plan.Mainframe.Centre;
        Steps(dr, 0.2f);
        Check(dr.Pending?.Id == "dirt", "the paydata has a second folder");
        dr.Step(new RunInput { Slot = 1, Aim = dr.Player.Pos + new V2(1, 0) });
        Check(dr.Decided.Contains("dirt:1"), "pulled");
        var last = new LastRun { Job = dj, Success = true, Choices = new List<string>(dr.Decided) };
        Check(last.Picked("dirt:1") && last.Asked("dirt") && !last.Asked("begs"), "the debrief knows what was asked and picked");
        Career dc = TestCareer();
        dc.Last = last;
        Check(global::Sprawl.Storylets.ById("dirt").When(dc), "and Null's call follows it");

        // A guard who gives up.
        string[] rows =
        {
            "############",
            "#P.........#",
            "#..........#",
            "#..........#",
            "############",
        };
        int spared = 0, gave = 0;
        for (ulong s = 1; s <= 60; s++)
        {
            Job sj = TestJob();
            sj.Seed = s;
            var sr = new Run(sj, TestCareer(), Site(rows));
            Actor g = sr.AddEnemy(EnemyKind.Guard, new V2(8.5f, 3.5f), MathF.PI);
            g.Hp = g.MaxHp * 0.3f;
            sr.Hurt(g, g.MaxHp * 0.1f, sr.Player, false, ignoreArmor: true);
            if (!g.Surrendered) { Check(g.Hostile || g.Dead, "a guard who doesn't give up keeps fighting"); continue; }
            gave++;
            Check(!g.Hostile && g.Gun == null, "hands up: out of the fight and unarmed");
            if (s % 2 == 0) { sr.Hurt(g, 1e4f, sr.Player, false, ignoreArmor: true); Check(sr.Result().KilledHelpless == 1, "shooting one counts against you"); }
            else { Check(sr.Result().Spared == 1, "sparing one is remembered"); spared++; }
            if (gave >= 4) break;
        }
        Check(gave > 0 && spared > 0, $"some guards surrender ({gave})");
    }

    private void Backgrounds()
    {
        foreach (var (id, name, _) in Career.Backgrounds)
        {
            Career c = Career.New("Bg", "samurai", 3);
            int cash = c.Cash;
            c.SetBackground(id);
            Check(c.Background == id && c.Etiquettes.Count >= 1, $"{name}: a background and its etiquette");
            Check(Career.Campaigns.Any(k => k.Id == "past-" + id), $"{name}: a past to catch up with");
            if (id == "corp") Check(c.Cash == cash + 2000, "the corp severance pays");
        }
        // Only that background sees its scenes.
        City city = City.Generate(5);
        foreach (var (id, place, scene) in new[] { ("street", "fence", "bg-street-wen"), ("barrens", "rigger", "bg-barrens-torque"), ("cop", "cop", "bg-cop-dace"), ("corp", "press", "bg-corp-quill") })
        {
            Storylet s = global::Sprawl.Storylets.ById(scene);
            Career mine = RichCareer(city), other = RichCareer(city);
            mine.Background = id;
            other.Background = id == "street" ? "corp" : "street";
            Check(s != null && s.Place == place && s.When(mine) && !s.When(other), $"{scene}: only a {id} background");
        }
        // Background-only choices in the shared storylets.
        int pasts = global::Sprawl.Storylets.All.Sum(s => s.Choices?.Count(ch => Career.Backgrounds.Any(b => ch.Text.StartsWith($"({b.name})"))) ?? 0);
        Check(pasts >= 6, $"storylets with a choice only your past can take ({pasts})");
        // The crew at home.
        Career h = RichCareer(city);
        h.Background = "street";
        h.CrewOf("ox").Trust = 1;
        Storylet first = global::Sprawl.Storylets.ById("crewhome1-ox"), second = global::Sprawl.Storylets.ById("crewhome2-ox");
        Check(first.Place == "home" && first.When(h) && !second.When(h), "Ox talks at home; the second talk waits");
        foreach (string hid in Tables.Hires.Select(x => x.id))
            Check(global::Sprawl.Storylets.ById($"crewhome1-{hid}") != null, $"{hid} has a conversation at home");
    }

    private void Receipts()
    {
        // Briefs in the employer's voice.
        City city = City.Generate(9);
        Career c = RichCareer(city);
        c.Board.Clear();
        c.RefreshBoard(city);
        foreach (Job j in c.Board.Where(j => j.Story == 0 && j.Arc == null && Career.EmployerVoices.ContainsKey(j.Employer ?? "")))
            Check(Career.EmployerVoices[j.Employer].Any(v => j.Brief.StartsWith(v)), $"{j.Title}: the brief opens in {j.Employer}'s voice");
        Check(Career.Johnsons.All(x => Career.JohnsonVoices.ContainsKey(x.Id)), "every Johnson has a manner");
        Check(Tables.Contacts.All(k => Career.EmployerVoices.ContainsKey(k.Id)), "every contact has a voice for briefs");

        // The receipt at the end.
        c.JobsDone = 23;
        c.CrewOf("ox").Dead = true;
        c.Set("cut:won", 40);
        c.Inc("spared", 2);
        var r = c.Receipt();
        Check(r.Any(x => x.what == "Days on the street" && x.value == $"{c.Day}"), "days");
        Check(r.Any(x => x.what == "Jobs done / failed" && x.value.StartsWith("23")), "jobs");
        Check(r.Any(x => x.what == "People you got out" && x.value == "1"), "people saved");
        Check(r.Any(x => x.what == "Crew buried" && x.value.Contains("Ox")), "crew lost");
        Check(r.Any(x => x.what == "Stories finished" && x.value.Contains("The Cut")), "stories finished");
        Check(Tables.Districts.All(d => r.Any(x => x.what == $"The {d.Name}")), "who runs every district");
        Check(c.Obituary(true) != c.Obituary(false) && c.Obituary(true).Contains(c.Name), "the radio's last word");
        var e = c.Ending();
        Check(e.All(x => x != null) && e.Any(x => x.Contains("Marrow")), "the ending knows the later campaigns");
        Check(!TestCareer().Ending().Any(x => x.Contains("Iron Saint")), "and doesn't claim a story you never finished");
    }

    private void Roadblocks()
    {
        City city = City.Generate(777);
        Career c = TestCareer();
        var sim = new CitySim(city, c, 4);
        sim.Step(1 / 60f, default);
        Check(sim.Roadblocks.Count == 0, "no checkpoints on an ordinary day");
        c.Happenings.Add(new Happening { Id = "crackdown", Start = c.Day, End = c.Day + 3 });
        sim.Traffic.Clear();
        sim.Step(1 / 60f, default);
        Check(sim.Roadblocks.Count == Tables.Roadblocks, $"a crackdown puts up checkpoints ({sim.Roadblocks.Count})");
        Roadblock rb = sim.Roadblocks[0];
        Check(rb.Tiles.All(t => sim.CarSolid(t.X, t.Y)), "the barrier stops a car");
        Check(sim.Roadblocks.All(b => V2.Distance(b.At, sim.HomeLot.Door.Centre) >= Tables.RoadblockClear), "never at your own door");
        // A route round them.
        foreach (Lot to in new[] { city.Place("fixer"), city.Place("fence"), city.Place("rigger"), city.Place("decker") })
        {
            var w = sim.RouteTo(to).Waypoints;
            bool through = false;
            for (int k = 0; k + 1 < w.Count; k++)
                foreach (Roadblock b in sim.Roadblocks)
                {
                    V2 a0 = w[k], a1 = w[k + 1], d = a1 - a0;
                    float u = d.LengthSquared() < 1e-4f ? 0 : Math.Clamp(V2.Dot(b.At - a0, d) / d.LengthSquared(), 0, 1);
                    through |= V2.Distance(a0 + d * u, b.At) < 1.5f;
                }
            Check(!through, $"the route to {to.Place} goes round the checkpoints");
        }
        // Clean, stopped at the barrier: waved through.
        V2 along = new(-rb.Across.Y, rb.Across.X);
        sim.Player.Pos = rb.At - along * 3;
        sim.Player.Vel = V2.Zero;
        c.Heat = 0;
        sim.Chasers.Clear();
        for (int i = 0; i < 60 * 3; i++) sim.Step(1 / 60f, default);
        Check(rb.OpenT > 0 && !sim.CarSolid(rb.Tiles[0].X, rb.Tiles[0].Y), "a clean car is waved through");
        // Hot: the checkpoint calls it in.
        Roadblock hot = sim.Roadblocks[1];
        sim.Player.Pos = hot.At - new V2(-hot.Across.Y, hot.Across.X) * 6;
        c.Heat = 1.5f;
        sim.Chasers.Clear();
        sim.Step(1 / 60f, default);
        Check(hot.Alerted && sim.Chasers.Count(x => x.Kind == ChaserKind.Police) >= 2 && c.Heat >= 2.5f, "a hot car: they run the plate and come after you");
        // When it ends, they come down.
        c.Happenings.Clear();
        sim.Step(1 / 60f, default);
        Check(sim.Roadblocks.Count == 0 && !sim.CarSolid(rb.Tiles[0].X, rb.Tiles[0].Y), "and come down when the crackdown ends");
    }

    private void Standing()
    {
        City city = City.Generate(31);
        Career c = TestCareer();
        c.CityRef = city;
        c.Cash = 100000;
        Job j = null;
        for (int k = 0; k < 40 && j == null; k++)
        {
            Job m = c.MakeJob(city);
            if (m != null && m.Client >= 0 && m.Type is JobType.Retrieve or JobType.Destroy or JobType.Datasteal) j = m;
        }
        Check(j != null && j.Client != (int)j.Faction, "a job has a client, and it isn't the target");
        Check(j.Brief.Contains($"The money is the {Tables.FactionNames[j.Client]}'s"), "the brief says whose money it is");
        var client = (Faction)j.Client;
        c.Standing[j.Faction] = 3;
        Debrief d = c.Finish(j, Won(c));
        Check(c.StandingOf(client) == 1 && c.StandingOf(j.Faction) == 1, "the client likes you more, the target less");
        Check(d.Lines.Any(l => l.Item1.Contains("standing")), "and the receipt says so");
        // Their own work.
        c.Standing[client] = Tables.StandingJobs;
        c.Board.Clear();
        c.RefreshBoard(city);
        Job fav = c.Board.FirstOrDefault(b => b.Favour);
        Check(fav != null && fav.Client == (int)client && fav.Title.StartsWith("For the"), "a friend gets the faction's own jobs");
        // The item.
        var (_, id, _, _, _) = Career.FactionItems.First(x => x.faction == client);
        Offer o = c.Offers("fixer").FirstOrDefault(x => x.Id == "fitem:" + id);
        Check(o != null && o.Block != null, "Juno shows the faction item, locked below standing 5");
        c.Standing[client] = Tables.StandingItem;
        o = c.Offers("fixer").First(x => x.Id == "fitem:" + id);
        Check(o.Block == null && c.Buy(o) == null && c.HasFactionItem(id), "a friend can buy it");
        // What the items do.
        Career e = TestCareer();
        float hack = e.HackRate;
        e.Set("fitem:chip");
        Check(e.HackRate > hack * 1.2f, "the contractor chip hacks faster");
        var loot = new LootItem { Name = "x", Value = 1000, Faction = -1 };
        int price = e.LootPrice(loot);
        e.Set("fitem:jade");
        Check(e.LootPrice(loot) > price, "the jade marker sells higher");
        var vj = new Job { Type = JobType.Retrieve, Faction = Faction.Vipers, District = "barrens" };
        Check(e.AmbushChance(vj) > 0, "Vipers ambush");
        e.Set("fitem:colours");
        Check(e.AmbushChance(vj) == 0, "not someone in their colours");
    }

    private void EdgePoints()
    {
        Career c = TestCareer();
        int base0 = c.EdgeMax;
        c.Karma = 200;
        Check(c.BuyEdge() == null && c.EdgeMax == base0 + 1, "karma buys edge");
        Check(c.BuyEdge() == null && c.BuyEdge() != null, "up to two points");
        // A spare point slips a guard who has just made you; the last one is kept for death.
        string[] rows =
        {
            "##########",
            "#P.......#",
            "#........#",
            "##########",
        };
        c.Attrs[(int)Attr.Charisma] = 6;
        var r = new Run(TestJob(), c, Site(rows));
        Actor g = r.AddEnemy(EnemyKind.Guard, new V2(5.5f, 1.5f), MathF.PI);
        int had = r.Edge;
        Check(had >= 2, $"a charismatic runner has spare edge ({had})");
        Steps(r, 3f);
        Check(r.EdgeUsed >= 1 && !r.Alarm, "edge: the guard looks straight through you");
        while (r.Edge > 1) { g.Detect = 0; g.BluffT = 0; Steps(r, 3f); }
        g.BluffT = 0;
        Steps(r, 3f);
        Check(r.Edge == 1 && g.Alert, "the last point is kept for a killing blow, and he sees you");
    }

    /// <summary>Every dialog that closes stops pausing the city (a call once froze the streets for good).</summary>
    private void DialogsClose()
    {
        Career c = TestCareer();
        int before = Modal.Open;
        var mail = new Mail { Story = "collector", From = "A collector", Subject = "x", Text = "x", Call = true, Day = 1 };
        var dialogs = new Modal[] { new StoryModal(c, mail), new Modal("x"), new CreateScreen() };
        foreach (Modal m in dialogs)
        {
            AddChild(m);
            Check(Modal.Open == before + 1, $"{m.GetType().Name}: open, it pauses the city");
            RemoveChild(m);
            Check(Modal.Open == before, $"{m.GetType().Name}: closed, it doesn't");
            m.Free();
        }
        // Who calls on video: contacts and the story's faces; Null, Johnsons, the dragon and strangers don't.
        foreach (ContactDef k in Tables.Contacts)
        {
            var who = CallView.Lookup(k.Name);
            Check(k.Id == "decker" ? who.Picture == null && who.Hidden != null : who.Picture == Assets.ContactPicture(k), $"{k.Name} {(k.Id == "decker" ? "keeps a face hidden" : "calls on video")}");
        }
        foreach (string face in new[] { "Dr. Amon Pryce", "Ilse Pryce", "Kade Marrow" })
            Check(CallView.Lookup(face).Picture is string pic && Assets.Picture(pic) != null, $"{face} calls on video, with a painted face");
        foreach (string hidden in new[] { "Mr. Grey", "Veyrasz", "A collector", "Unknown sender" })
            Check(CallView.Lookup(hidden).Picture == null && CallView.Lookup(hidden).Hidden != null, $"{hidden}: no video");
        Check(Tables.Hires.All(h => CallView.Lookup(h.name).Picture == Assets.HirePicture(h.id)), "the crew call on video");
        // A call rings once: answered later from the inbox, it reads as a message.
        var ring = new Mail { Story = "collector", From = "A collector", Subject = "x", Text = "x", Call = true, Day = 1 };
        var first = new StoryModal(c, ring);
        Check(ring.Read, "picking up (or not) marks it read");
        first.Free();
    }

    private void Crowds()
    {
        string[] rows =
        {
            "##########################",
            "#P.......................#",
            "#........................#",
            "#........................#",
            "#........................#",
            "#........................#",
            "##########################",
        };
        Run Make2(out List<Actor> crowd)
        {
            LevelPlan p = Site(rows);
            for (int k = 0; k < 6; k++) p.Npcs.Add((new V2(6.5f + k, 3.5f), null));
            var r = new Run(TestJob(), TestCareer(), p);
            crowd = r.Actors.Where(a => a.IsNpc).ToList();
            r.Events.Clear();
            return r;
        }
        // Footsteps and a whistle don't scare anyone.
        Run calm = Make2(out var people);
        calm.Player.Pos = new V2(5.5f, 3.5f);
        Steps(calm, 1f, new RunInput { Slot = -1, Move = new V2(0, 0.3f), Aim = calm.Player.Pos + new V2(1, 0) });
        calm.Step(new RunInput { Slot = -1, Whistle = true, Aim = calm.Player.Pos + new V2(1, 0) });
        Check(people.All(a => a.PanicT <= 0), "footsteps and a whistle don't start a panic");
        // A gunshot does: they run, away from it, and somebody screams.
        Run loud = Make2(out var crowd);
        V2 gun = new(5.5f, 3.5f);
        loud.Player.Pos = gun;
        float before = crowd.Average(a => V2.Distance(a.Pos, gun));
        loud.Noise(gun, 14, true);
        Check(crowd.All(a => a.PanicT > 0), "a gunshot: everyone near panics");
        Steps(loud, 0.1f);
        Steps(loud, 2.5f);
        float after = crowd.Average(a => V2.Distance(a.Pos, gun));
        Check(after > before + 2.5f, $"and they run away from it ({before:0.0} -> {after:0.0} tiles)");
        Steps(loud, Tables.PanicMax + 1);
        Check(crowd.All(a => a.PanicT <= 0) && crowd.All(a => V2.Distance(a.Home, gun) > 3), "then calm down where they ended up, not back in the fight");
        // Someone hit beside them.
        Run hurt = Make2(out var c2);
        Actor g = hurt.AddEnemy(EnemyKind.Guard, new V2(7.5f, 4.5f), 0);
        hurt.Hurt(g, 5, null, false);
        Check(c2.Count(a => a.PanicT > 0) >= 3, "a guard shot in the crowd scatters it");
        // The scream: one sound for a crowd, and it carries to a guard.
        Run sc = Make2(out _);
        Actor guard = sc.AddEnemy(EnemyKind.Guard, new V2(12.5f, 1.5f), MathF.PI);
        sc.Panic(new V2(8.5f, 3.5f), 6);
        Check(sc.Events.Count(e => e.Kind == Ev.Panic) == 1, "a crowd screams once, not once each");
        Check(guard.State == AiState.Suspicious || guard.Alert, "a guard who hears it comes to look");

        // The streets: shots empty the pavement.
        City city = City.Generate(777);
        var sim = new CitySim(city, TestCareer(), 3);
        sim.Traffic.Clear();
        V2 at = sim.Peds[0].Pos;
        var near = sim.Peds.Where(p => V2.Distance(p.Pos, at) < Tables.GunfireScatter && p.Down <= 0).ToList();
        sim.Gunfire(at);
        Check(near.Count > 0 && near.All(p => p.Flee >= 2.5f), $"gunfire in the street: everyone within {Tables.GunfireScatter} tiles runs ({near.Count})");
        Check(sim.Peds.Where(p => V2.Distance(p.Pos, at) > Tables.GunfireScatter + 1).All(p => p.Flee < 2.5f), "further off, nobody notices");
        Check(sim.Events.Count(e => e.Kind == CityEv.Scream) == 1, "and somebody screams");
    }

    private void LegacyChoice()
    {
        var prof = new Profile { Legacy = Profile.LegacyPerks.Select(p => p.id).ToList() };
        Career all = Career.New("A", "mage", 3);
        prof.ApplyLegacy(all);
        Check(all.Has("eyes") && all.Has("smartlink"), "every perk earned and kept: the chrome goes in");
        Career mage = Career.New("M", "mage", 3);
        float ess = mage.Essence;
        prof.ApplyLegacy(mage, Profile.ChromePerks);
        Check(!mage.Has("eyes") && !mage.Has("smartlink") && mage.Essence == ess, "a mage who declines the chrome keeps every bit of essence");
        Check(mage.Cash > Career.New("M", "mage", 3).Cash && mage.Rep >= 2, "and still gets the rest of the legacy");
    }

    /// <summary>A contact in danger: why, what to do, by when -- and the warning before it comes to that.</summary>
    private void DangerCalls()
    {
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        c.Day = 25;
        ContactDef k = Tables.Contact("fence");
        Faction f = c.Holder(k.District);
        c.Loyalty["fence"] = 2;
        // One short: a warning, naming who's in reach.
        c.Grudge[f] = Career.DangerGrudge - 2;
        Job against = c.OfferSpecial(JobType.Retrieve, SiteStyle.Den, f, k.District, 2, "x", "x");
        c.Accept(against);
        c.Finish(against, Won(c));
        Check(c.Grudge[f] == Career.DangerGrudge - 1 && c.News.Any(n => n.Contains("sore at you") && n.Contains(k.Name)), "one short of the line: the street warns you, and says who's exposed");
        // Over it: the call, with the reason and the stakes. (That job may have cost them the district: whoever holds it now.)
        f = c.Holder(k.District);
        c.Grudge[f] = Career.DangerGrudge;
        Storylet s = global::Sprawl.Storylets.ById("danger-fence");
        Check(s.When(c), "a faction that hates you comes for the contact on its streets");
        Job mine = c.OfferSpecial(JobType.Retrieve, SiteStyle.Den, Faction.Lotus, "strip", 2, "Mine", "x");
        c.Accept(mine);
        Mail m = c.Send(s);
        Check(m.Text.Contains("grudge of") && m.Text.Contains(Tables.District(k.District).Name), "the call says why it's happening");
        Check(c.EffectOf(m, 1).Contains("a second time"), "and what saying no costs");
        Check(c.Choose(m, 0) == null, "on my way");
        Job p = c.Active;
        Check(p != null && p.Tag == "danger:fence" && c.TargetLot == p.Lot, "it's your job at once, and the map points at the door");
        Check(p.Lot == city.Place("fence").Index && p.Style == Career.VenueStyle("fence"), "their own door: the fight is at their place");
        Check(p.Expires == c.Day + 1 && p.Brief.Contains($"end of day {p.Expires}"), "with the real deadline in the brief");
        Check(c.Flag("onhold") == mine.Id + 1 && c.Board.Contains(mine), "the job you were on waits");
        c.Finish(p, Won(c));
        Check(c.ActiveJob == mine.Id, "and is yours again after");
        // Said no, twice: gone.
        Career n = TestCareer();
        n.CityRef = city;
        n.Day = 25;
        n.Loyalty["fence"] = 2;
        n.Grudge[n.Holder(k.District)] = Career.DangerGrudge;
        Mail m1 = n.Send(s);
        n.Choose(m1, 1);
        Check(n.ClosedNow("fence") && !n.Gone("fence"), "said no once: shut for a week");
        n.Day = 50;
        n.Loyalty["fence"] = 2;
        Mail m2 = n.Send(s);
        Check(m2.Text.Contains("gone for good") && n.EffectOf(m2, 1).Contains("for good"), "the second call says it plainly: this time they leave");
        n.Choose(m2, 1);
        Check(n.Gone("fence"), "and they do");
    }
}
