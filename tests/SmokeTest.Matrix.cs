using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>The Matrix: hosts, IC, the trace, the deck's memory, spiders in and out of the host, decks and programs.</summary>
public partial class SmokeTest
{
    private static MatrixRun Host(int rating = 3, MxGoal goal = MxGoal.Pull, Action<MatrixSetup> tweak = null, ulong seed = 11)
    {
        var s = new MatrixSetup { Seed = seed, Rating = rating, Threat = rating, Goal = goal, Mx = new[] { 4, 3, 3, 3 }, Boxes = 12, Memory = 10, Decking = 2 };
        tweak?.Invoke(s);
        return new MatrixRun(s);
    }

    private static void MxSteps(MatrixRun m, float seconds, MxInput inp)
    {
        for (int i = 0; i < (int)(seconds * 60) && !m.Out; i++) m.Step(1 / 60f, inp);
    }

    private static readonly MxInput Idle = new() { Program = -1 };

    private void Decking()
    {
        // ---- the host ----
        MatrixRun h = Host(4);
        Eq(h.Stores.Count, 3, "three datastores for the job");
        Check(h.Files.Count(f => !f.Objective) >= 2, "paydata on the side");
        Check(h.Core.X >= 0 && h.CoreHp > 0, "a core that launches IC");
        var reach = h.BfsAll(h.Entry);
        Check(Enumerable.Range(0, MatrixRun.W * MatrixRun.H).All(i => reach[i] < 999), "every node can be reached (gates crack)");
        Check(Enumerable.Range(0, MatrixRun.W * MatrixRun.H).Any(i => h.Gates[i] != 0), "a rating-4 host has firewall gates");
        Check(h.Ice.Any(i => i.Kind == IcKind.Patrol) && h.Ice.Any(i => i.Guard), "patrols and guards waiting");
        Check(!Host(1).Ice.Any(i => i.Kind == IcKind.Black) && Host(5).Ice.Any(i => i.Kind == IcKind.Black), "black IC only in the hardest hosts");

        // ---- pulling a file takes holding E, and memory ----
        MatrixRun p = Host(1);
        p.Ice.Clear();
        p.You = p.Stores[0];
        MxSteps(p, 2, Idle);
        Check(p.Taken.Count == 0, "standing on a datastore doesn't pull it");
        MxSteps(p, 3, new MxInput { Hold = true, Program = -1 });
        Check(p.Taken.Count == 1 && p.UsedMp == 0, "holding E pulls it (the job's own files need no memory)");
        MatrixRun full = Host(1, tweak: s => { s.Memory = 4; s.Programs = new List<string> { "hammer", "armor" }; });
        full.Ice.Clear();
        full.You = full.Stores[0];
        Eq(full.Free, 0, "two programs fill a 4 Mp deck");
        MxSteps(full, 3, new MxInput { Hold = true, Program = -1 });
        Check(full.Taken.Count == 1 && full.NoRoom == null, "a full deck still pulls the job's datastore");
        // Paydata does need room.
        MxFile pay = full.Files.First(f => !f.Objective);
        pay.Mp = 2;
        pay.Encrypted = false;
        full.You = pay.At;
        MxSteps(full, 3, new MxInput { Hold = true, Program = -1 });
        Check(!pay.Taken && full.NoRoom != null && full.NoRoom.Contains("Armor") && full.HoldT < 0.01f, "no room in memory, no file -- and the prompt names what G would purge");
        full.Step(1 / 60f, new MxInput { Purge = true, Program = -1 });
        Check(full.Free == 2 && !full.Has("armor"), "G purges the last program loaded");
        MxSteps(full, 3, new MxInput { Hold = true, Program = -1 });
        Check(pay.Taken && full.NoRoom == null, "and then the file fits");

        // ---- spikes ----
        MatrixRun k = Host(2);
        k.Ice.Clear();
        Pt next = Pt.Dirs4.Select(d => k.You + d).First(n => k.Passable(k.You, n));
        k.Ice.Add(new Ic { Kind = IcKind.Killer, Pos = next, Next = next, Hp = 7, MaxHp = 7, Hunting = true, StepT = 99 });
        float t0 = k.Trace;
        MxSteps(k, 3, new MxInput { Spike = true, Program = -1 });
        Check(k.Ice.Count == 0, "spikes crash IC");
        Check(k.Trace > t0, "and every spike is noise on the trace");

        // ---- IC hits ----
        MatrixRun hit = Host(3);
        hit.Ice.Clear();
        hit.Ice.Add(new Ic { Kind = IcKind.Killer, Pos = hit.You, Next = hit.You, Hp = 50, MaxHp = 50, Hunting = true, StepT = 99 });
        MxSteps(hit, 2.5f, Idle);
        Check(hit.Damage > 0, "Killer IC damages the deck");
        MatrixRun armored = Host(3, tweak: s => { s.Mx = new[] { 1, 1, 1, 9 }; s.Programs = new List<string> { "armor" }; });
        armored.Ice.Clear();
        armored.Ice.Add(new Ic { Kind = IcKind.Killer, Pos = armored.You, Next = armored.You, Hp = 50, MaxHp = 50, Hunting = true, StepT = 99 });
        MxSteps(armored, 2.5f, Idle);
        Check(armored.Damage < hit.Damage, $"Firewall and Armor take boxes off ({hit.Damage} -> {armored.Damage})");
        MatrixRun tar = Host(3);
        tar.Ice.Clear();
        tar.Ice.Add(new Ic { Kind = IcKind.TarBaby, Pos = tar.You, Next = tar.You, Hp = 50, MaxHp = 50, Hunting = true, StepT = 99 });
        MxSteps(tar, 1, Idle);
        tar.Step(1 / 60f, new MxInput { Press = true, Program = -1 });
        Check(tar.Locked > 0 && !tar.Out, "a tar baby link-locks you: no jacking out");
        MatrixRun bio = Host(5), filt = Host(5, tweak: s => s.Programs = new List<string> { "biofilter" });
        int Bio(MatrixRun m)
        {
            m.Ice.Clear();
            m.Ice.Add(new Ic { Kind = IcKind.Black, Pos = m.You, Next = m.You, Hp = 50, MaxHp = 50, Hunting = true, StepT = 99 });
            int sum = 0;
            for (int i = 0; i < 90; i++) { m.Step(1 / 60f, Idle); sum += m.Events.Where(e => e.Kind == MatrixEvent.Bio).Sum(e => e.Value); }
            return sum;
        }
        int b0 = Bio(bio), b1 = Bio(filt);
        Check(b0 > 0 && b1 < b0, $"black IC hurts the body, the filter halves it ({b0} -> {b1})");
        MatrixRun crash = Host(3, tweak: s => { s.Mx = new[] { 4, 3, 3, 0 }; s.Programs = new List<string> { "hammer" }; });
        crash.Ice.Clear();
        crash.Ice.Add(new Ic { Kind = IcKind.Crash, Pos = crash.You, Next = crash.You, Hp = 50, MaxHp = 50, Hunting = true, StepT = 99 });
        MxSteps(crash, 1.5f, Idle);
        Check(!crash.Has("hammer") && crash.Crashed.Contains("hammer"), "Crash IC takes a program out");

        // ---- patrols and the alert ----
        MatrixRun pat = Host(1);
        pat.Ice.Clear();
        pat.Ice.Add(new Ic { Kind = IcKind.Patrol, Pos = pat.You, Next = pat.You, Hp = 50, MaxHp = 50, StepT = 99 });
        MxSteps(pat, 0.2f, Idle);
        Check(pat.Ice[0].Report > 0 && !pat.Alert, "a patrol that sees you starts reporting");
        MxSteps(pat, Tables.MxReport + 0.2f, Idle);
        Check(pat.Alert, "and the host goes on alert when it does");
        MatrixRun quiet = Host(1, tweak: s => s.Mx = new[] { 30, 3, 3, 3 });
        quiet.Ice.Clear();
        quiet.Ice.Add(new Ic { Kind = IcKind.Patrol, Pos = quiet.You, Next = quiet.You, Hp = 3, MaxHp = 3, StepT = 99 });
        MxSteps(quiet, 0.2f, new MxInput { Spike = true, Program = -1 });
        MxSteps(quiet, Tables.MxReport + 0.5f, Idle);
        Check(!quiet.Alert, "crash the patrol before it reports and nobody knows");
        MatrixRun sneaky = Host(1, tweak: s => s.Programs = new List<string> { "sneak" });
        sneaky.Ice.Clear();
        Pt nb = Pt.Dirs4.Select(d => sneaky.You + d).First(n => sneaky.Passable(sneaky.You, n));
        sneaky.Ice.Add(new Ic { Kind = IcKind.Patrol, Pos = nb, Next = nb, Hp = 50, MaxHp = 50, StepT = 99 });
        MxSteps(sneaky, 1, Idle);
        Check(sneaky.Ice[0].Report < 0, "with Sneak a patrol one line away doesn't see you");
        MatrixRun alert = Host(4, tweak: s => { s.Alert = true; s.Boxes = 999; });
        alert.Ice.Clear();
        MxSteps(alert, 15, Idle);
        Check(alert.Hunters > 0, "an alert core launches hunters");
        MatrixRun downed = Host(4, tweak: s => { s.Alert = true; s.Boxes = 999; });
        downed.Ice.Clear();
        downed.CoreHp = 0.1f;
        downed.You = downed.Core;
        downed.Step(1 / 60f, new MxInput { Spike = true, Program = -1 });
        MxSteps(downed, 15, Idle);
        Check(downed.CoreDown && downed.Hunters == 0, "a crashed core launches nothing");

        // ---- the trace ----
        MatrixRun calm = Host(3), hot = Host(3, tweak: s => s.Alert = true), slick = Host(3, tweak: s => { s.Mx = new[] { 1, 6, 3, 3 }; s.Decking = 5; });
        Check(hot.TraceRate > calm.TraceRate * 2 && slick.TraceRate < calm.TraceRate, "alert fills the trace faster; Sleaze and Decking slow it");
        MatrixRun tr = Host(3);
        tr.Ice.Clear();
        tr.Trace = 0.999f;
        MxSteps(tr, 1, Idle);
        Check(tr.Out && tr.Ended.Contains("traced"), "a full trace dumps you");
        MatrixRun sp = Host(3, tweak: s => s.Spoof = true);
        sp.Ice.Clear();
        sp.Trace = 0.999f;
        MxSteps(sp, 0.5f, Idle);
        Check(!sp.Out && sp.Trace < 0.7f, "spoof takes the first full trace");
        MatrixRun scrub = Host(3, tweak: s => s.Programs = new List<string> { "scrub" });
        scrub.Ice.Clear();
        scrub.Trace = 0.8f;
        scrub.Step(1 / 60f, new MxInput { Program = 0 });
        Check(scrub.Trace < 0.45f, "Signal scrub on Q wipes the trace back");

        // ---- gates ----
        MatrixRun g = Host(4);
        g.Ice.Clear();
        int gi = Enumerable.Range(0, MatrixRun.W * MatrixRun.H).First(i => g.Gates[i] != 0);
        Pt ga = new(gi % MatrixRun.W, gi / MatrixRun.W);
        Pt gb = ga + ((g.Gates[gi] & 1) != 0 ? new Pt(1, 0) : new Pt(0, 1));
        g.You = ga;
        MxSteps(g, 1, new MxInput { Move = new V2(gb.X - ga.X, gb.Y - ga.Y), Program = -1 });
        Check(g.You == ga && g.CrackOf(ga, gb) > 0, "a gate stops you, and pushing cracks it");
        MxSteps(g, 12, new MxInput { Move = new V2(gb.X - ga.X, gb.Y - ga.Y), Program = -1 });
        Check(!g.Gated(ga, gb), "until it gives");

        // ---- on a site: the spider, controls, biofeedback, what carries over ----
        string[] rows =
        {
            "##############",
            "#P......L...m#",
            "#............#",
            "#............#",
            "##############",
        };
        Career dk = Career.New("D", "decker", 3);
        Run sr = Make(Site(rows), JobType.Datasteal, dk);
        Actor spider = sr.AddEnemy(EnemyKind.Spider, new V2(6.5f, 3.5f), 0);
        sr.Player.Pos = new V2(11.5f, 1.5f);
        sr.Step(new RunInput { Slot = -1, InteractPressed = true, Interact = true });
        Check(sr.Matrix != null && sr.Matrix.SpiderIn, "the site's spider is in the host");
        Check(sr.Matrix.Rating == sr.Job.Threat + 1, "and makes it a rating harder");
        Ic persona = sr.Matrix.Ice.First(i => i.Kind == IcKind.Spider);
        sr.Matrix.Ice.RemoveAll(i => i != persona);
        persona.Pos = sr.Matrix.You;
        persona.Hp = 0.1f;
        sr.Step(new RunInput { Slot = -1, Fire = true });
        Check(!sr.Matrix.SpiderIn && spider.Stun >= Tables.MxSpiderStun - 1, "crash his persona and his body slumps at the console");
        // The maglocks node.
        int lockNode = sr.Matrix.ControlAt.First(kv => kv.Value == MxControl.Maglocks).Key;
        sr.Matrix.You = new Pt(lockNode % MatrixRun.W, lockNode / MatrixRun.W);
        Steps(sr, 3, new RunInput { Slot = -1, Interact = true });
        Check(sr.L[new Pt(8, 1)] == Tile.Door, "the maglocks node opens the site's locked doors");
        // Biofeedback hurts without pulling you out.
        sr.Matrix.Ice.Clear();
        sr.Matrix.Ice.Add(new Ic { Kind = IcKind.Black, Pos = sr.Matrix.You, Next = sr.Matrix.You, Hp = 50, MaxHp = 50, Hunting = true, StepT = 99 });
        float hp0 = sr.Player.Hp;
        Steps(sr, 1.2f, new RunInput { Slot = -1 });
        Check(sr.Player.Hp < hp0 && sr.Matrix != null, "black IC's biofeedback hurts your body, and you stay in");
        int dmg = sr.Matrix.Damage;
        sr.Matrix.Ice.Clear();
        sr.Matrix.You = sr.Matrix.Entry;
        sr.Step(new RunInput { Slot = -1, InteractPressed = true });
        Check(sr.Matrix != null && sr.Matrix.Locked > 0, "black IC link-locked you: no jacking out yet");
        Steps(sr, 4.5f, new RunInput { Slot = -1 });
        sr.Step(new RunInput { Slot = -1, InteractPressed = true });
        Check(sr.Matrix == null && sr.DeckDamage == dmg, "jack out at the entry; the deck's damage stays with you");
        sr.Player.Pos = new V2(11.5f, 1.5f);
        sr.Step(new RunInput { Slot = -1, InteractPressed = true, Interact = true });
        Check(sr.Matrix != null && sr.Matrix.Damage == dmg, "and is there when you jack back in");
        // Kill his body and the persona goes.
        Run sr2 = Make(Site(rows), JobType.Datasteal, dk);
        Actor sp2 = sr2.AddEnemy(EnemyKind.Spider, new V2(6.5f, 3.5f), 0);
        sr2.Player.Pos = new V2(11.5f, 1.5f);
        sr2.Step(new RunInput { Slot = -1, InteractPressed = true, Interact = true });
        sr2.Hurt(sp2, 999, null, false, ignoreArmor: true);
        sr2.Step(new RunInput { Slot = -1 });
        Check(sr2.Matrix != null && !sr2.Matrix.SpiderIn, "take his body out and the persona is gone from the host");
        // A full trace on site: the alarm, and dumpshock.
        Run st = Make(Site(rows), JobType.Datasteal, TestCareer());
        st.Player.Pos = new V2(11.5f, 1.5f);
        st.Step(new RunInput { Slot = -1, InteractPressed = true, Interact = true });
        st.Matrix.Ice.Clear();
        st.Matrix.Trace = 0.999f;
        float hpT = st.Player.Hp;
        Steps(st, 0.5f, new RunInput { Slot = -1 });
        Check(st.Matrix == null && st.Alarm && st.Player.Hp < hpT, "a full trace dumps you, raises the alarm and hurts");
        // Paydata comes home as loot.
        Run pd = Make(Site(rows), JobType.Datasteal, dk);
        pd.Player.Pos = new V2(11.5f, 1.5f);
        pd.Step(new RunInput { Slot = -1, InteractPressed = true, Interact = true });
        pd.Matrix.Ice.Clear();
        MxFile pf = pd.Matrix.Files.First(f => !f.Objective && f.Mp <= pd.Matrix.Free);
        pd.Matrix.You = pf.At;
        Steps(pd, 12, new RunInput { Slot = -1, Interact = true });
        Check(pd.LootFound.Any(l => l.Name.StartsWith(Career.PaydataPrefix) && l.Value == pf.Value), "paydata goes into the loot");

        // ---- Null's jobs from home ----
        var hj = new Job { Id = 1, Type = JobType.Hack, Host = MxGoal.Crash, Faction = Faction.Halcyon, Style = SiteStyle.Office, Threat = 2, Seed = 5, Target = "x", Item = "x", Title = "x", Lot = -1, Pay = 1000 };
        Run home = new Run(hj, dk, LevelGen.Generate(hj));
        home.Player.Pos = home.Plan.Mainframe.Centre + new V2(-1, 0);
        home.Step(new RunInput { Slot = -1, InteractPressed = true, Interact = true });
        Check(home.Matrix != null && home.Matrix.S.Use == MxUse.Home && home.Matrix.Stores.Count == 0, "a crash job: no datastores, just the core");
        home.Matrix.Ice.Clear();
        home.Matrix.CoreHp = 0.1f;
        home.Matrix.You = home.Matrix.Core;
        home.Step(new RunInput { Slot = -1, Fire = true });
        home.Step(new RunInput { Slot = -1 });
        Check(home.ObjectiveDone && home.Phase == RunPhase.Playing, "crash the core: done, but you are still in there");
        for (int i = 0; i < 60 * 40 && home.Matrix != null; i++) home.Step(MatrixInput(home.Matrix, i));
        home.Step(new RunInput { Slot = -1 });
        Check(home.Phase == RunPhase.Extracted && home.Success, "jack out and the job is done");
        var pj = new Job { Id = 2, Type = JobType.Hack, Host = MxGoal.Plant, Faction = Faction.Lotus, Style = SiteStyle.Club, Threat = 1, Seed = 9, Target = "x", Item = "x", Title = "x", Lot = -1, Pay = 1000 };
        Run plant = new Run(pj, dk, LevelGen.Generate(pj));
        plant.Player.Pos = plant.Plan.Mainframe.Centre + new V2(-1, 0);
        plant.Step(new RunInput { Slot = -1, InteractPressed = true, Interact = true });
        plant.Matrix.Ice.Clear();
        for (int i = 0; i < 60 * 90 && plant.Matrix != null; i++) plant.Step(MatrixInput(plant.Matrix, i));
        plant.Step(new RunInput { Slot = -1 });
        Check(plant.Success, "a plant job: the backdoor goes in (hold E in the archive)");
        int kinds = Enumerable.Range(1, 40).Select(sd => new Rng((ulong)sd ^ 0xDEC4UL).Int(3)).Distinct().Count();
        Eq(kinds, 3, "Null's remote jobs come in three kinds");

        // ---- spider teams ----
        int Spiders(int threat, ulong seed) => LevelGen.Generate(new Job { Id = 1, Type = JobType.Datasteal, Style = SiteStyle.Office, Faction = Faction.Halcyon, Threat = threat, Seed = seed, Target = "x", Item = "x", Title = "x", Lot = -1 }).Enemies.Count(e => e.Kind == EnemyKind.Spider);
        Check(Enumerable.Range(1, 6).Count(sd => Spiders(5, (ulong)sd) >= 2) >= 4, "threat-5 corporate datasteals have a spider team");
        Check(Enumerable.Range(1, 6).Count(sd => Spiders(4, (ulong)sd) == 1) >= 4, "threat 4: one spider");
        Check(Enumerable.Range(1, 6).All(sd => Spiders(3, (ulong)sd) == 0), "below that, none");

        // ---- decks, programs, memory ----
        Career c = TestCareer();
        Eq(c.ActiveDeck.Id, "commlink", "everyone has a commlink");
        Eq(dk.ActiveDeck.Id, "implant", "the decker jacks in with the implant");
        Check(dk.HasProgram("breaker") && dk.S(Skill.Decking) == 3, "the decker starts with Icebreaker loaded and Decking 3");
        c.GiveProgram("hammer");
        c.GiveProgram("armor");
        c.GiveProgram("sneak");
        Check(c.HasProgram("hammer") && c.HasProgram("armor") && !c.HasProgram("sneak") && c.OwnsProgram("sneak"), "a 4 Mp commlink loads two programs; the third waits");
        Check(c.ToggleProgram("sneak") != null, "and won't load over the limit");
        c.ToggleProgram("hammer");
        Check(c.ToggleProgram("sneak") == null && c.HasProgram("sneak"), "unload one and it fits");
        c.Cash = 100000;
        c.Loyalty["decker"] = 2;
        Offer deck = c.Offers("decker").First(o => o.Kind == ShopKind.Deck && o.Id == "veil");
        Check(c.Buy(deck) == null && c.ActiveDeck.Id == "veil" && c.Memory == 10, "buy a deck from Null and you carry it");
        int s0 = c.Mx(MxAttr.Sleaze), a0 = c.Mx(MxAttr.Attack);
        c.SwapDeck(MxAttr.Attack, MxAttr.Sleaze);
        Check(c.Mx(MxAttr.Attack) == s0 && c.Mx(MxAttr.Sleaze) == a0, "the array can be dealt out differently");
        Career back = Career.FromJson(c.ToJson());
        Check(back.ActiveDeck.Id == "veil" && back.Mx(MxAttr.Attack) == s0 && back.HasProgram("sneak"), "deck, array and loaded programs survive a save");
        c.Loot.Add(new LootItem { Name = Career.PaydataPrefix + "Board minutes", Value = 1234, Day = c.Day });
        Offer sell = c.Offers("decker").First(o => o.Kind == ShopKind.Sell);
        Check(sell.Price == 1234, "Null buys paydata at full value");
        // An old save: seven skills, the big decks sold as programs, nothing loaded.
        Career old = Career.New("Old", "samurai", 9);
        old.Skills = new[] { 1, 0, 0, 0, 4, 0, 0 };
        old.Programs.AddRange(new[] { "deck2", "spoof", "overwatch" });
        old.LoadedSet = false;
        old.Loaded.Clear();
        Career mig = Career.FromJson(old.ToJson());
        Check(mig.Skills.Length == 8 && mig.S(Skill.Decking) == 4, "old saves get Decking at their Electronics");
        Check(mig.Decks.Contains("deck2") && !mig.Programs.Contains("deck2") && mig.ActiveDeck.Id == "deck2", "the Obsidian deck becomes a deck");
        Check(mig.HasProgram("spoof") && mig.HasProgram("overwatch"), "and what they own is loaded");
    }
}
