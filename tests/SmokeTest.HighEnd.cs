using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>The high end: the spider, the ghost operative, the bulwark, the barghest and the hive.</summary>
public partial class SmokeTest
{
    private static readonly string[] Hall =
    {
        "####################",
        "#P.................#",
        "#..................#",
        "#.......L..........#",
        "#..................#",
        "####################",
    };

    private static Run HighRun(Career c = null, JobType type = JobType.Datasteal, int threat = 5)
    {
        Job j = TestJob(type);
        j.Threat = threat;
        var r = new Run(j, c ?? TestCareer(), Site(Hall));
        r.Events.Clear();
        return r;
    }

    private void HighEnd()
    {
        // ---- the spider ----
        Run s = HighRun();
        float clean = s.HackRate;
        Actor spider = s.AddEnemy(EnemyKind.Spider, new V2(17.5f, 4.5f), MathF.PI);
        Check(s.HackRate < clean && s.SpiderUp, $"a spider in the system slows every hack ({clean:0.00} -> {s.HackRate:0.00})");
        Actor turret = s.AddEnemy(EnemyKind.Turret, new V2(12.5f, 1.5f), 0);
        turret.Team = 0;
        turret.Hacked = 60;
        // (Stunned, so it can't simply shoot him first.)
        turret.Stun = 60;
        s.Player.MaxHp = s.Player.Hp = 1e6f;
        s.L.Set(new Pt(8, 3), Tile.Door);
        s.BecomeAlert(spider, s.Player.Pos, false);
        Check(s.HackRate < clean * 0.6f, "more once he knows you're there");
        Steps(s, Tables.SpiderRelock + 0.5f);
        Check(turret.Team == 1, "he takes your hacked turret back");
        Check(s.L[new Pt(8, 3)] == Tile.LockedDoor, "and re-locks a door you opened");
        s.Hurt(spider, 999, s.Player, false, ignoreArmor: true);
        Check(!s.SpiderUp && s.HackRate == clean, "find him, and the system is yours again");
        // Placed at a console on hard corporate sites.
        int spiders = 0;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var hj = new Job { Id = 1, Type = JobType.Datasteal, Style = SiteStyle.Office, Faction = Faction.Halcyon, Threat = 5, Seed = seed, Target = "x", Item = "x", Title = "x", Lot = -1 };
            LevelPlan sitePlan = LevelGen.Generate(hj);
            Spawn sp = sitePlan.Enemies.FirstOrDefault(e => e.Kind == EnemyKind.Spider);
            if (sp == null) continue;
            spiders++;
            Check(Pt.Dirs4.Any(d => sitePlan.L[Pt.Of(sp.Pos).X + d.X, Pt.Of(sp.Pos).Y + d.Y] == Tile.Console), "the spider sits at a console");
        }
        Check(spiders >= 4, $"threat-5 corporate sites have a spider ({spiders}/6)");
        var low = new Job { Id = 1, Type = JobType.Datasteal, Style = SiteStyle.Office, Faction = Faction.Halcyon, Threat = 4, Seed = 3, Target = "x", Item = "x", Title = "x", Lot = -1 };
        Check(Enumerable.Range(1, 8).All(k => { low.Seed = (ulong)k; return LevelGen.Generate(low).Enemies.All(e => e.Kind is not (EnemyKind.Cloaker or EnemyKind.Bulwark)); }), "none of the high end below threat 5 (bar the threat-4 spider, in the Decking section)");

        // ---- the ghost operative ----
        Run g = HighRun();
        Actor ghost = g.AddEnemy(EnemyKind.Cloaker, new V2(10.5f, 1.5f), MathF.PI);
        g.Step(new RunInput { Slot = -1, Aim = g.Player.Pos + new V2(1, 0) });
        Check(g.Seen(ghost.Pos) && !g.Shown(ghost), "the ghost operative stands in plain sight and isn't there");
        ghost.Pos = new V2(5.5f, 1.5f);
        Check(g.Shown(ghost), "up close, the camouflage gives");
        ghost.Pos = new V2(10.5f, 1.5f);
        g.Hurt(ghost, 1, g.Player, false);
        Check(g.Shown(ghost), "hit, it shows for a moment");
        Career eyes = TestCareer();
        eyes.Install("eyes");
        eyes.Install("thermal");
        Run gt = HighRun(eyes);
        Actor gh2 = gt.AddEnemy(EnemyKind.Cloaker, new V2(7.5f, 1.5f), MathF.PI);
        gt.Step(new RunInput { Slot = -1, Aim = gt.Player.Pos + new V2(1, 0) });
        Check(gt.Shown(gh2), "thermographic eyes see its heat");
        Run gm = HighRun(Career.New("Mage", "mage", 3));
        Actor gh3 = gm.AddEnemy(EnemyKind.Cloaker, new V2(9.5f, 1.5f), MathF.PI);
        gm.Step(new RunInput { Slot = -1, Aim = gm.Player.Pos + new V2(1, 0) });
        Check(gm.Shown(gh3), "a mage sees its aura");

        // ---- the bulwark ----
        Run b = HighRun();
        Actor bul = b.AddEnemy(EnemyKind.Bulwark, new V2(12.5f, 2.5f), MathF.PI);
        float hp = bul.Hp;
        b.Hurt(bul, Tables.Weapon("hollow9").Damage, b.Player, false);
        float pistol = hp - bul.Hp;
        hp = bul.Hp;
        b.Hurt(bul, Tables.Weapon("amr").Damage, b.Player, false);
        float amr = hp - bul.Hp;
        Check(pistol < 6 && amr > 100, $"small arms barely scratch it ({pistol:0}); an anti-materiel round doesn't ({amr:0})");
        Run bs = HighRun();
        bs.Player.MaxHp = bs.Player.Hp = 1000;
        Actor bw = bs.AddEnemy(EnemyKind.Bulwark, new V2(4.2f, 2.5f), MathF.PI);
        bs.Player.Pos = new V2(3f, 2.5f);
        bs.BecomeAlert(bw, bs.Player.Pos);
        bw.ReactT = 0;
        V2 before = bs.Player.Pos;
        float life = bs.Player.Hp;
        Steps(bs, 1.5f);
        Check(bs.Player.Hp < life - 20 && V2.Distance(bs.Player.Pos, before) > 0.8f, "its slam hurts and throws you");

        // ---- the barghest ----
        Run h = HighRun(type: JobType.Bounty, threat: 3);
        h.Player.MaxHp = h.Player.Hp = 1000;
        Actor bg = h.AddEnemy(EnemyKind.Barghest, new V2(5.5f, 2.5f), MathF.PI);
        Actor hound = h.AddEnemy(EnemyKind.Hound, new V2(16.5f, 4.5f), MathF.PI);
        h.BecomeAlert(bg, h.Player.Pos, false);
        bg.ReactT = 0;
        bg.HowlCd = 0;
        h.Player.Pos = new V2(2.5f, 2.5f);
        bool froze = false;
        for (int i = 0; i < 180 && !froze; i++) { h.Step(new RunInput { Slot = -1, Aim = h.Player.Pos + new V2(1, 0) }); froze = h.DazedT > 0; }
        Check(froze, "the barghest's howl freezes you");
        V2 at = h.Player.Pos;
        h.Step(new RunInput { Slot = -1, Move = new V2(1, 0), Aim = h.Player.Pos + new V2(1, 0) });
        Check(V2.Distance(h.Player.Pos, at) < 0.02f, "frozen: you can't move");
        Check(hound.Alert && hound.FrenzyT > 0, "and the pack comes running");

        // ---- the hive ----
        var hiveJob = new Job { Id = 5, Type = JobType.Bounty, Hive = true, Style = SiteStyle.Warehouse, Faction = Faction.Vipers, District = "industrial", Threat = 6, Seed = 21, Target = "x", Item = "x", Title = "x", Lot = -1 };
        LevelPlan hivePlan = LevelGen.Generate(hiveJob);
        Check(hivePlan.Enemies.Count(e => e.Kind == EnemyKind.HiveQueen) == 1 && hivePlan.Enemies.Count(e => e.Kind == EnemyKind.EggSac) >= 9, "a hive: one queen, a clutch of egg sacs");
        Check(hivePlan.Enemies.All(e => e.Kind is EnemyKind.HiveQueen or EnemyKind.EggSac or EnemyKind.HiveWorker), "and nothing else lives there");
        var hr = new Run(hiveJob, TestCareer(), hivePlan);
        foreach (Actor a in hr.Actors.Where(a => a.Is(EnemyKind.HiveWorker)).ToList()) hr.Actors.Remove(a);
        hr.Player.MaxHp = hr.Player.Hp = 1e6f;
        int eggs = hr.Actors.Count(a => a.Is(EnemyKind.EggSac) && !a.Dead);
        Steps(hr, 2);
        Check(hr.Actors.Count(a => a.Is(EnemyKind.EggSac) && !a.Dead) == eggs, "eggs wait while the hive sleeps");
        hr.Alarm = true;
        Steps(hr, Tables.EggHatch + 1);
        Check(hr.Actors.Count(a => a.Is(EnemyKind.HiveWorker)) > 0, "roused, the sacs hatch workers");
        Check(hr.Actors.Count(a => a.Is(EnemyKind.EggSac) && a.Dead && a.Escaped) > 0 && hr.Actors.Any(a => a.Is(EnemyKind.EggSac) && a.EggT < Tables.EggHatch && !a.Dead), "and the queen lays more");
        Check(hr.ObjectiveText().Contains("queen"), $"the objective names her ({hr.ObjectiveText()})");
        // Offered to a runner with a name, in the Industrial district.
        City city = City.Generate(41);
        Career pro = TestCareer();
        pro.CityRef = city;
        pro.Rep = 30;
        Job found = null;
        for (int k = 0; k < 400 && found == null; k++)
        {
            Job m = pro.MakeJob(city);
            if (m != null && m.Hive) found = m;
        }
        Check(found != null && found.Threat == 6 && found.District == "industrial" && found.Type == JobType.Bounty && found.Title.Contains("Hive"), "prestige careers get the Hive on the board");
    }

    private void SecurityLadder()
    {
        string[] rows =
        {
            "##############################",
            "#P...........................#",
            "#............................#",
            "#.......dddddddd.............#",
            "#.......d......d.............#",
            "#.......d......d.............#",
            "#.......dddd+ddd.............#",
            "#............................#",
            "##############################",
        };
        Run r = new Run(TestJob(), TestCareer(), Site(rows));
        r.Player.MaxHp = r.Player.Hp = 1e6f;
        Check(r.SecurityLevel == 0 && Sprawl.Run.SecurityLevels.Length == 7, "security starts quiet, six levels above it");
        // Spotted: on edge at once, and climbing while he calls it in.
        Actor g = r.AddEnemy(EnemyKind.Guard, new V2(6.5f, 1.5f), MathF.PI);
        r.BecomeAlert(g, r.Player.Pos);
        g.ReactT = 99;
        Steps(r, 0.2f);
        Check(r.SecurityLevel == 1, "the first pair of eyes: on edge");
        int before = r.Actors.Count(a => a.Hostile);
        Steps(r, 4.5f);
        Check(r.SecurityLevel >= 2 && r.Actors.Count(a => a.Hostile) >= before + Tables.SecurityPatrols, "level 2: extra patrols walk in");
        Steps(r, 4.5f);
        Check(r.SecurityLevel >= 3 && r.Alarm, "level 3: the alarm (about the old 8 s of being called in)");
        // Not omniscient yet: out of sight, they lose you and search.
        r.Hurt(g, 1e6f, r.Player, false);
        foreach (Actor a in r.Actors.Where(a => a.Hostile).ToList()) r.Actors.Remove(a);
        Actor s1 = r.AddEnemy(EnemyKind.Guard, new V2(25.5f, 7.5f), MathF.PI);
        r.BecomeAlert(s1, new V2(20.5f, 7.5f), false);
        s1.LostT = 10;
        r.Player.Pos = new V2(11.5f, 4.5f);
        Steps(r, 1.5f);
        Check(!r.Hunted && s1.State == AiState.Search, "below level 5 they search where they last saw you");
        Check(s1.Sweep != null, "a sweep under way (rooms nearest first, on a generated site)");
        // Level 5 and up: hunted.
        r.RaiseSecurity(5 - r.Security);
        Check(r.Hunted && s1.Alert, "level 5: they know where you are");
        int pre6 = r.Actors.Count(a => a.Hostile);
        r.RaiseSecurity(1);
        Check(r.SecurityLevel == 6 && r.Actors.Count(a => a.Hostile) >= pre6 + 3, "level 6: a kill team");
        // A camera doesn't sound the alarm: security up, and someone is sent.
        Run c = new Run(TestJob(), TestCareer(), Site(rows));
        Actor cam = c.AddEnemy(EnemyKind.Camera, new V2(3.5f, 2.5f), MathF.PI);
        Actor walker = c.AddEnemy(EnemyKind.Guard, new V2(26.5f, 7.5f), 0);
        for (int i = 0; i < 300 && c.Security < 1; i++) c.Step(new RunInput { Slot = -1, Aim = c.Player.Pos + new V2(1, 0) });
        Check(c.Security >= Tables.SecurityCamera - 0.01f && !c.Alarm, "a camera pushes security up, not straight to the alarm");
        Check(walker.State == AiState.Suspicious, "and sends a guard to look");
        // A looped console stops the clock.
        Run lp = new Run(TestJob(), TestCareer(), Site(rows));
        lp.RaiseSecurity(1.0f);
        float s0 = lp.Security;
        Steps(lp, 5);
        Check(lp.Security > s0, "once they know you're there, the clock ticks");
        Check(Sprawl.Run.SecurityLevels.All(l => l.name.Length > 0 && l.does.Length > 0), "every level says what it does");
        // On a generated site the sweep goes room to room, nearest first.
        var gj = new Job { Id = 9, Type = JobType.Retrieve, Style = SiteStyle.Office, Faction = Faction.Halcyon, Threat = 2, Seed = 5, Target = "x", Item = "x", Title = "x", Lot = -1 };
        var gr = new Run(gj, TestCareer(), LevelGen.Generate(gj));
        Actor gs = gr.Actors.First(a => a.Hostile && a.Is(EnemyKind.Guard));
        gr.BecomeAlert(gs, gs.Pos + new V2(1, 0), false);
        gs.LostT = 20;
        gr.Player.Pos = gr.Plan.Entry.Centre;
        for (int i = 0; i < 120 && gs.State != AiState.Search; i++) gr.Step(new RunInput { Slot = -1, Aim = gr.Player.Pos + new V2(1, 0) });
        gr.Step(new RunInput { Slot = -1, Aim = gr.Player.Pos + new V2(1, 0) });
        Check(gs.State == AiState.Search && gs.Sweep != null && gs.Sweep.Count >= 1, $"a sweep of the rooms around where you were seen ({gs.Sweep?.Count ?? 0} more to look in)");
        for (int i = 0; i < 60 * (int)Tables.SearchFor + 60 && gs.State == AiState.Search; i++) gr.Step(new RunInput { Slot = -1, Aim = gr.Player.Pos + new V2(1, 0) });
        Check(gs.State != AiState.Search && gs.Jumpy, "and after it, on edge for the rest of the run");
    }

    /// <summary>The Lamplighter: an AI patron, the choice about it, and both ends.</summary>
    private void Lamplighter()
    {
        City city = City.Generate(55);
        foreach (int choice in new[] { 1, 2, 3 })
        {
            Career c = choice == 3 ? Career.New("Decker", "decker", 4) : TestCareer();
            c.CityRef = city;
            c.Day = 20;
            c.Rep = 8;
            c.JobsDone = 10;
            c.RefreshBoard(city);
            Mail open = c.Inbox.FirstOrDefault(m => m.Story == "lamp-open");
            if (choice == 1) Check(open != null && c.Board.Any(j => j.Story == 37), "a patron who pays before the work is done");
            for (int ch = 1; ch <= 4; ch++)
            {
                c.RefreshBoard(city);
                Job j = c.Board.FirstOrDefault(x => x.Story == 36 + ch);
                Check(j != null, $"Lamplighter {choice}: chapter {ch}");
                if (j == null) break;
                if (ch == 4)
                {
                    Check(j.Floors == 4, "the last job is the top of a tower");
                    if (choice == 2) Check(j.Type == JobType.Destroy && j.Title.Contains("Lights out"), "sold to the Registry: burn its core instead");
                    if (choice == 3) Check(j.Schematics && j.Has(RunMod.Skeleton), "it told you its name: the way up is easy");
                }
                Accept(c, j);
                c.Finish(j, Won(c));
                if (ch == 2)
                {
                    Mail mid = c.Inbox.First(m => m.Story == "lamp-mid");
                    if (choice == 1) Check(c.Options(mid)[2].block != null, "only a decker can follow it into the grid");
                    Check(c.Choose(mid, choice - 1) == null, $"the choice about it ({choice})");
                }
                c.JobsDone += 2;
            }
            if (choice == 2) Check(c.Flag("lamp:won") == 2 && !c.HasProgram("lamplight"), "betrayed: the bounty, and the lights go out");
            else Check(c.Flag("lamp:won") == 1 && c.HasProgram("lamplight"), "made whole: the Lamplight program");
        }
        // What the program does.
        Career d = TestCareer();
        float rate = d.HackRate;
        d.GiveProgram("lamplight");
        Check(d.HackRate > rate * 1.2f, "Lamplight: hacks faster");
        var prof = new Profile();
        Career whole = TestCareer();
        whole.Set("lamp:won", 1);
        prof.Earn(whole);
        Career next = TestCareer();
        prof.ApplyLegacy(next);
        Check(next.HasProgram("lamplight"), "and a legacy for the next career");
        // On the line it wears a different face every few seconds.
        var who = CallView.Lookup("The Lamplighter");
        Check(who.Faces != null && who.Faces.Length > 5 && who.Faces.All(f => Assets.Picture(f) != null), "it calls wearing borrowed faces");
    }
}
