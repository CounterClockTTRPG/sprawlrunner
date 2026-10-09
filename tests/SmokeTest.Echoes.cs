using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>Echoes: people from past jobs, weeks later -- each one's trigger, timing and effect.</summary>
public partial class SmokeTest
{
    private void Echoes()
    {
        City city = City.Generate(55);
        Career New()
        {
            Career k = TestCareer();
            k.CityRef = city;
            k.Cash = 20000;
            return k;
        }
        bool Has(Career k, string id, string place = null) => k.Eligible(place == null ? Hook.Morning : Hook.Place, place).Any(s => s.Id == id);
        // Puts the career on the day this echo of its last job is due.
        PastJob Due(Career k, string kind)
        {
            PastJob p = k.Memories.Last();
            k.Day = p.Day + global::Sprawl.Storylets.EchoDelay(p, kind);
            return p;
        }
        string Named(int parity) => new[] { "Ada Kerr", "Bo Lindt", "Cas Ferro", "Dina Wolk", "Eli Sato", "Fen Ruiz" }.First(n => Career.Stable(n) % 2 == parity);

        // ---- the memory -----------------------------------------------------------------
        Career c = New();
        Job res = StoryJob(JobType.Rescue, seed: 31);
        res.Target = "Ines Moreau";
        res.Title = "Get Ines out";
        c.Finish(res, Won(c));
        PastJob p = c.Memories.LastOrDefault();
        Check(p != null && p.Title == "Get Ines out" && p.Fate == "rescued" && p.District == "barrens" && p.Target == "Ines Moreau", "a closed job is remembered: its name, place and what became of the target");
        int delay = global::Sprawl.Storylets.EchoDelay(p, "echo-nurse");
        Check(delay >= Tables.EchoMin && delay <= Tables.EchoMin + Tables.EchoSpread - 1, $"its echo is due 10-30 days later ({delay})");
        Eq(global::Sprawl.Storylets.EchoDelay(Clone(c).Memories.Last(), "echo-nurse"), delay, "on the same day on every run, and the memory survives a save");

        // ---- the hostage, now a nurse at Ichikawa's ------------------------------------------
        c.Day = p.Day + delay - 1;
        Check(!Has(c, "echo-nurse", "doc"), "no echo before its time");
        c.Day = p.Day + delay;
        Check(Has(c, "echo-nurse", "doc"), "the hostage you got out turns up at Ichikawa's");
        c.Injuries = 2;
        Mail m = c.Send("echo-nurse");
        Check(m.Place == "doc" && m.Text.Contains("Get Ines out") && m.Text.Contains($"day {p.Day}") && m.Text.Contains(Tables.District("barrens").Name) && m.From == "Ines Moreau", $"naming the job, the place and the day ({m.Text})");
        Eq(c.Injuries, 0, "and patches you up for free");
        Check(!Has(c, "echo-nurse", "doc"), "once per job");
        Career unhurt = New();
        unhurt.Finish(res, Won(unhurt));
        Due(unhurt, "echo-nurse");
        int kits = unhurt.Medkits = 1;
        unhurt.Send("echo-nurse");
        Eq(unhurt.Medkits, kits + 2, "unhurt, two medkits instead");
        Career failed = New();
        failed.Finish(StoryJob(JobType.Rescue, seed: 31), Lost(failed));
        Due(failed, "echo-nurse");
        Check(!Has(failed, "echo-nurse", "doc"), "nobody you didn't get out comes back as a nurse");

        // ---- the guard you let get up ---------------------------------------------------------
        Career g = New();
        RunResult spared = Won(g);
        spared.Spared = 2;
        g.Finish(StoryJob(JobType.Datasteal, Faction.Halcyon, "spire", 32), spared);
        Due(g, "echo-guard");
        Check(Has(g, "echo-guard"), "a guard you spared writes");
        g.Send("echo-guard");
        Check(g.Flag("inside:Halcyon") > 0, "and will be on the inside at Halcyon");
        Job viper = StoryJob(JobType.Retrieve, Faction.Vipers, seed: 33);
        g.Accept(viper);
        Check(viper.Legwork.Count == 0 && g.Flag("inside:Halcyon") > 0, "not on a Vipers job");
        g.ActiveJob = -1;
        Job halcyon = StoryJob(JobType.Retrieve, Faction.Halcyon, seed: 34);
        g.Accept(halcyon);
        Check(halcyon.Legwork.Contains("door") && g.Flag("inside:Halcyon") == 0, "the next Halcyon job starts with the locked doors open, once");
        Career killer = New();
        killer.Finish(StoryJob(JobType.Datasteal, Faction.Halcyon, "spire", 32), Won(killer, kills: 3));
        Due(killer, "echo-guard");
        Check(!Has(killer, "echo-guard"), "nobody writes if you let nobody walk");

        // ---- the widow at your door, or the brother with a gun -----------------------------------
        string even = Named(0), odd = Named(1);
        Career w = New();
        Job hit = StoryJob(JobType.Eliminate, seed: 35);
        hit.Target = even;
        hit.Title = "Retire " + even;
        w.Finish(hit, Won(w));
        Due(w, "echo-widow");
        Check(Has(w, "echo-widow", "home") && !Has(w, "echo-revenge"), "a target you killed: the widow waits at your door");
        w.Notoriety = 3;
        Mail wm = w.Send("echo-widow");
        Check(wm.Place == "home" && wm.Text.Contains(even), "about them, by name");
        Career wk = Clone(w);
        long cash = w.Cash;
        Check(w.Choose(wm, 0) == null && w.Notoriety == 2 && w.Cash == cash - Tables.RestitutionPer, "paying her: notoriety -1");
        Mail wkm = wk.Inbox.First(x => x.Id == wm.Id);
        Career wt = Clone(wk);
        wk.Choose(wkm, 3);
        Check(wk.Flag("hitters") > 0, "shutting the door: her brother will be waiting");
        wt.Choose(wt.Inbox.First(x => x.Id == wm.Id), 2);
        Eq(wt.Notoriety, 4, "threatening her: notoriety +1");
        Check(w.Flag("mourned:" + even) > 0 && !Has(w, "echo-widow", "home"), "she comes once");
        Career rv = New();
        Job hit2 = StoryJob(JobType.Eliminate, seed: 36);
        hit2.Target = odd;
        rv.Finish(hit2, Won(rv));
        Due(rv, "echo-revenge");
        Check(Has(rv, "echo-revenge") && !Has(rv, "echo-widow", "home"), "or her brother comes looking");
        rv.Choose(rv.Send("echo-revenge"), 2);
        Check(rv.Flag("hitters") > 0, "and let come, is waiting after the next job");

        // ---- the one who got away, a second time ------------------------------------------------
        foreach (int parity in new[] { 0, 1 })
        {
            Career f = New();
            Job miss = StoryJob(JobType.Eliminate, Faction.Lotus, "strip", 37);
            miss.Target = Named(parity);
            RunResult lost = Lost(f);
            lost.TargetFled = true;
            f.Finish(miss, lost);
            string kind = parity == 0 ? "echo-fled-offer" : "echo-fled-grudge";
            Due(f, kind);
            Check(!Has(f, kind), "the one who got away waits until they've written once");
            f.Send("fled");
            Check(Has(f, kind) && !Has(f, parity == 0 ? "echo-fled-grudge" : "echo-fled-offer"), $"then comes back weeks later ({kind})");
            Mail fm = f.Send(kind);
            Check(fm.Text.Contains(miss.Target), "by name");
            if (parity == 0)
            {
                f.Choose(fm, 0);
                Job offer = f.Board.FirstOrDefault(b => b.Tag == "echo:fled");
                Check(offer != null && offer.Faction != Faction.Lotus, "with a job against the people who paid for the hit");
            }
            else
            {
                int before = f.Grudge.GetValueOrDefault(Faction.Lotus);
                f.Choose(fm, 1);
                Eq(f.Grudge.GetValueOrDefault(Faction.Lotus), before + 2, "or talking to their people: grudge +2");
            }
        }

        // ---- the Johnson who sold you out -----------------------------------------------------
        Career jc = New();
        Job cross = StoryJob(JobType.Retrieve, seed: 38);
        cross.JohnsonId = "grey";
        cross.DoubleCross = true;
        cross.Title = "The Tanaka briefcase";
        RunResult trap = Won(jc);
        trap.Trap = true;
        jc.Finish(cross, trap);
        Check(jc.JohnsonBurned("grey") && jc.Memories.Last().Crossed, "a set-up is remembered");
        Due(jc, "echo-johnson");
        Check(Has(jc, "echo-johnson"), "the Johnson who sold you out turns up again");
        Mail jm = jc.Send("echo-johnson");
        Check(jm.From == "Mr. Grey" && jm.Text.Contains("The Tanaka briefcase"), "himself, about that job");
        cash = jc.Cash;
        jc.Choose(jm, 0);
        Check(!jc.JohnsonBurned("grey") && jc.Cash > cash, "his apology pays, and his jobs come back");

        // ---- the witness, and the four days between echoes -------------------------------------------
        Career wi = New();
        RunResult loud = Won(wi, alarm: true, kills: 4);
        loud.Spared = 1;
        wi.Finish(StoryJob(JobType.Destroy, Faction.Vipers, "docks", 39), loud);
        PastJob lj = wi.Memories.Last();
        wi.Day = lj.Day + Math.Max(global::Sprawl.Storylets.EchoDelay(lj, "echo-witness"), global::Sprawl.Storylets.EchoDelay(lj, "echo-guard"));
        Check(Has(wi, "echo-witness") && Has(wi, "echo-guard"), "a loud job with a guard let go: two echoes due");
        Mail wim = wi.Send("echo-witness");
        Check(wim.Call, "the witness is a call");
        Check(!Has(wi, "echo-guard"), "but never two echoes in four days");
        wi.Day += Tables.EchoGap - 1;
        Check(!Has(wi, "echo-guard"), "not even three days later");
        wi.Day++;
        Check(Has(wi, "echo-guard"), "four days later the next one can come");
        Career wa = Clone(wi);
        int aware = wi.Awareness;
        wi.Choose(wi.Inbox.First(x => x.Id == wim.Id), 2);
        Eq(wi.Awareness, aware + 2, "unpaid, the witness sells your description: awareness +2");
        cash = wa.Cash;
        wa.Choose(wa.Inbox.First(x => x.Id == wim.Id), 0);
        Check(wa.Cash < cash && wa.Awareness == aware, "paid, it never airs");
        Career quiet = New();
        quiet.Finish(StoryJob(JobType.Destroy, seed: 40), Won(quiet));
        Due(quiet, "echo-witness");
        Check(!Has(quiet, "echo-witness"), "a quiet job leaves no witness");

        // ---- the kids from the den -----------------------------------------------------------
        Career kc = New();
        kc.Finish(StoryJob(JobType.Clear, seed: 41), Won(kc, kills: 5));
        Due(kc, "echo-kid");
        Check(!Has(kc, "echo-kid"), "no kid unless you let them go");
        Career kids = New();
        kids.Finish(StoryJob(JobType.Clear, seed: 41), Won(kids, kills: 5));
        kids.Choose(kids.Send("kids"), 0);
        Check(kids.Memories.Last().Tags.Contains("kids:free"), "letting the kids go is remembered on that job");
        Due(kids, "echo-kid");
        Check(Has(kids, "echo-kid"), "and one of them comes back");
        kids.Choose(kids.Send("echo-kid"), 0);
        Check(kids.Flag("echo:runner") > 0, "to run messages for you");
        Check(!Has(kids, "echo-tip"), "the first tip takes a few days");
        kids.Day += 3;
        Check(Has(kids, "echo-tip"), "then the kid has heard something");
        kids.Heat = 2;
        Mail tip = kids.Send("echo-tip");
        Check(kids.Heat < 2 || kids.Flag("freeschem") > 0 || kids.Flags.Keys.Any(k => k.StartsWith("wanted:")), $"something useful ({tip.Text})");
        kids.Day += 1;
        Check(!Has(kids, "echo-tip"), "about once a week");
        kids.Day += 5;
        Check(Has(kids, "echo-tip"), "and again");

        // ---- bounded -------------------------------------------------------------------------
        Career many = New();
        for (int i = 0; i < Tables.MemoryCap + 5; i++) many.Finish(StoryJob(JobType.Retrieve, seed: (ulong)(100 + i)), Won(many));
        Eq(many.Memories.Count, Tables.MemoryCap, "only the last jobs are remembered");
    }
}
