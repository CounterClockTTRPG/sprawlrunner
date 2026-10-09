using System;
using System.Linq;

namespace Sprawl;

public partial class SmokeTest
{
    /// <summary>A rival crew a few contests in, ready to be burned.</summary>
    private static Career Burnable(City city, int tier = 0)
    {
        Career c = TestCareer();
        c.CityRef = city;
        c.MeetRival();
        c.Rival.Wins = 1;
        c.Rival.Losses = 2;
        c.Rival.Tier = tier;
        c.Day = c.Rival.Since + Tables.RivalBurnDay;
        return c;
    }

    private static Mail Burn(Career c, string how)
    {
        Mail m = c.Send("rival-burned");
        int k = Array.FindIndex(Sprawl.Storylets.ById("rival-burned").Choices, ch => ch.Text.StartsWith(how));
        c.Cash = Math.Max(c.Cash, 0);
        return c.Choose(m, k) == null ? m : null;
    }

    private static bool Contests(Career c, City city)
    {
        var contest = typeof(Career).GetMethod("Contest", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        for (int k = 0; k < 300; k++)
        {
            Job j = c.MakeJob(city);
            if (j == null || j.Type is not (JobType.Retrieve or JobType.Datasteal or JobType.Eliminate)) continue;
            contest.Invoke(c, new object[] { j });
            if (j.Contested) return true;
        }
        return false;
    }

    private static Debrief Settle(Career c, Job j, bool success)
    {
        c.ActiveJob = j.Id;
        return c.Finish(j, new RunResult { Success = success });
    }

    /// <summary>
    /// The rival crew's three acts: their Johnson burns them (warn them, sell
    /// the warning, or stay out), and it ends by the history -- Rook joins you,
    /// a showdown at their hideout, or they leave town and send a postcard.
    /// </summary>
    private void RivalArc()
    {
        City city = City.Generate(777);

        // Act two waits for enough contests and enough days.
        Career early = Burnable(city);
        early.Rival.Losses = 1;
        Check(!early.RivalBurnDue && !Sprawl.Storylets.ById("rival-burned").When(early), "two contests in, nobody's burned yet");
        early.Rival.Losses = 2;
        early.Day = early.Rival.Since + Tables.RivalBurnDay - 1;
        Check(!early.RivalBurnDue, "nor in their first days in town");
        early.Day++;
        Check(Sprawl.Storylets.ById("rival-burned").When(early), $"after {Tables.RivalBurnAfter} contests, their Johnson burns them");
        early.Loyalty["decker"] = 2;
        Check(early.Send("rival-burned").From == Tables.Contact("decker").Name, "Null hears it in the traffic, if Null likes you");

        // (a) Warn them; pull Rook out; Rook joins you.
        Career w = Burnable(city);
        int g0 = w.Rival.Grudge;
        Check(Burn(w, "Warn") != null && w.Rival.Burn == "warned" && w.Rival.Owed, "warned, they owe you");
        Check(w.Rival.Grudge == Math.Max(0, g0 - Tables.RivalWarnGrudge), $"and their grudge drops ({g0} to {w.Rival.Grudge})");
        Check(!Sprawl.Storylets.ById("rival-burned").When(w), "it happens once");
        Check(!Sprawl.Storylets.ById("rival-offer").When(w) && !Sprawl.Storylets.ById("rival-hideout").When(w), "after the burn, the old offer and Juno's hideout give way to the ending");
        Check(!Contests(w, city), "a crew that owes you keeps off your work");
        Job rook = w.Board.FirstOrDefault(j => j.Tag == "rival-rook");
        Check(rook != null && rook.Type == JobType.Rescue && rook.Target == "Rook", "Rook was already at the meet: a rescue on your board");
        w.Day += Tables.RivalEndAfter;
        Check(!w.RivalEndDue, "the ending waits while Rook needs pulling out");
        Debrief rd = Settle(w, rook, true);
        Check(w.Rival.Rook == 1 && rd.Lines.Any(l => l.what == "Rook"), "Rook pulled out, and the debrief says so");
        Check(w.RivalEndDue && w.RivalEnding() == "merge" && Sprawl.Storylets.ById("rival-merge").When(w), $"warned, rescued, little bad blood (grudge {w.Rival.Grudge}): Rook asks to join");
        Check(!Sprawl.Storylets.ById("rival-showdown").When(w) && !Sprawl.Storylets.ById("rival-leave").When(w), "and only that ending");
        Check(w.CrewBlock("rook") != null, "until then, Rook isn't yours to hire");
        Mail mm = w.Send("rival-merge");
        Check(w.Choose(mm, 0) == null && w.Rival.Fate == "merged" && !w.Rival.Active, "welcomed aboard, the crew is gone");
        Check(w.CrewBlock("rook") == null && w.CrewTrust("rook") == Tables.RivalMergeTrust, "Rook is a hire now, and already trusts you");
        Check(!Contests(w, city) && !Sprawl.Storylets.ById("rival-gloat").When(w), "no more contested jobs");
        Check(Sprawl.Storylets.ById("crew2-rook") != null && Sprawl.Storylets.ById("crewjob-rook") != null, "Rook has a crew story like the others");
        var hj = new Job { Id = 501, Type = JobType.Retrieve, Faction = Faction.Halcyon, Style = SiteStyle.Office, Threat = 1, Seed = 3, Target = "x", Item = "x", Title = "x", Lot = -1, Pay = 1000, Hired = "rook" };
        Run hr = new Run(hj, w, Site(new[] { "#######", "#P....#", "#.....#", "#######" }));
        Check(hr.Ally?.Name == "Rook", "and comes along on a run");
        w.Board.Add(hj);
        w.ActiveJob = hj.Id;
        Check(Sprawl.Storylets.ById("rival-tip").When(w), "the old boss still sends the odd floor plan");
        Check(w.RivalLine().Contains("Rook runs with you"), "the PDA says how it ended");

        // Warned, but nobody went for Rook: they leave, and what they owe comes on a postcard.
        Career lapse = Burnable(city);
        Burn(lapse, "Warn");
        Job lj = lapse.Board.First(j => j.Tag == "rival-rook");
        lj.Expires = lapse.Day - 1;
        lapse.RefreshBoard(city, 0);
        Check(lapse.Rival.Rook == 2 && !lapse.Board.Contains(lj), "a rescue you let lapse: Rook is lost");
        lapse.Day = lapse.Rival.BurnDay + Tables.RivalEndAfter;
        Check(lapse.RivalEnding() == "leave", "without Rook, nobody's left to join you: they leave");
        lapse.Send("rival-leave");
        Check(lapse.Rival.Fate == "left" && !lapse.Rival.Active && !Contests(lapse, city), "they leave town, and the contested jobs stop");
        Check(!Sprawl.Storylets.ById("rival-postcard").When(lapse), "the postcard takes a while");
        lapse.Day += Tables.RivalPostcardAfter;
        int cash = lapse.Cash;
        Check(Sprawl.Storylets.ById("rival-postcard").When(lapse), "then a postcard");
        lapse.Send("rival-postcard");
        Check(lapse.Cash == cash + Tables.RivalPostcardGift, "with what they owe you under the stamp");

        // A butcher doesn't get a merge, even warned and with Rook out.
        Career bad = Burnable(city);
        Burn(bad, "Warn");
        bad.Rival.Rook = 1;
        bad.Board.RemoveAll(j => j.Tag == "rival-rook");
        bad.Notoriety = Tables.RivalMergeNotoriety;
        bad.Day += Tables.RivalEndAfter;
        Check(bad.RivalEnding() == "leave", "Rook won't run with a butcher");
        bad.Notoriety = 0;
        bad.Rival.Dead = 4;
        Check(bad.RivalEnding() == "showdown", $"bury enough of theirs and it's a showdown whatever you did (grudge {bad.Rival.Grudge})");

        // (b) Sell the warning: cash, notoriety, two of theirs dead, and a showdown at their hideout.
        Career s = Burnable(city, tier: 1);
        int sc = s.Cash, sn = s.Notoriety, sg = s.Rival.Grudge, members = s.Rival.Members.Count, price = s.RivalSellPrice;
        Check(price == Tables.RivalSellPay + Tables.RivalSellPerTier, "the warning is worth more on a better crew");
        Check(Burn(s, "Sell") != null && s.Cash == sc + price && s.Notoriety == sn + 1, $"sold: {M.Money(price)} and notoriety +1");
        Check(s.Rival.Rook == 2 && s.Rival.Members.Count == members - 1 && s.Rival.Grudge == Math.Min(9, sg + Tables.RivalSellGrudge), "Rook and one more die, and the rest hate you for it");
        Check(!s.Board.Any(j => j.Tag == "rival-rook"), "nobody to pull out");
        Check(!s.RivalEndDue, "the ending takes a few days");
        s.Day += Tables.RivalEndAfter;
        Check(s.RivalEnding() == "showdown" && Sprawl.Storylets.ById("rival-showdown").When(s), "sold, it ends at their hideout");
        Mail sm = s.Send("rival-showdown");
        Check(sm.Text.Contains("sold us"), "and they know who talked");
        s.Choose(sm, 0);
        Job hide = s.Board.FirstOrDefault(j => j.Tag == "rival");
        Check(hide != null && hide.Type == JobType.Clear && !Sprawl.Storylets.ById("rival-showdown").When(s), "the finale goes on the board, once");
        if (hide != null)
        {
            Run fr = new Run(hide, s, LevelGen.Generate(hide));
            Check(fr.Actors.Count(a => a.Rival && a.Team == 1) == s.Rival.Members.Count + 1, "the whole crew at home, and their boss");
            int before = s.Cash, karma = s.Karma;
            Debrief fd = Settle(s, hide, true);
            Check(s.Rival.Fate == "finished" && !s.Rival.Active, "won, they're gone for good");
            Check(s.Cash >= before + Tables.RivalStash && s.Karma >= karma + Tables.RivalShowdownKarma && fd.Lines.Any(l => l.what.EndsWith("stash")), "with their stash and the karma for it");
            Check(!Contests(s, city), "and nobody bids on your work any more");
        }

        // (c) Stay out: Rook is taken; a small crew leaves, a tier-3 one is too proud to.
        Career o = Burnable(city);
        Check(Burn(o, "Stay") != null && o.Rival.Rook == 2 && !o.Rival.Owed && o.Board.All(j => j.Tag != "rival-rook"), "stayed out: Rook is taken, nobody owes anybody");
        o.Day += Tables.RivalEndAfter;
        Check(o.RivalEnding() == "leave" && Sprawl.Storylets.ById("rival-leave").When(o), "and they leave town");
        o.Send("rival-leave");
        o.Day += Tables.RivalPostcardAfter;
        int oc = o.Cash;
        o.Send("rival-postcard");
        Check(o.Cash == oc, "a postcard with nothing under the stamp");
        Career proud = Burnable(city, tier: 3);
        Burn(proud, "Stay");
        proud.Day += Tables.RivalEndAfter;
        Check(proud.RivalEnding() == "showdown", "a tier-3 crew you let burn won't run: showdown");

        // Old saves: a crew from before the arc reads as not yet burned.
        var old = System.Text.Json.JsonSerializer.Deserialize<RivalCrew>("{\"Name\":\"x\",\"Leader\":\"y\",\"Members\":[\"a\"],\"Losses\":2,\"Dead\":1}", Json.Options);
        Check(old.Burn == null && old.Rook == 0 && old.Grudge == 4, $"an old save's crew starts the arc clean (grudge {old.Grudge})");
    }
}
