using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// The other crew: runners who want the same work you do. They bid on your
/// jobs, sometimes turn up in the same building, remember how it went, and
/// get better -- until somebody ends it.
/// </summary>
public sealed class RivalCrew
{
    public string Name, Leader;
    /// <summary>Who's still running with them.</summary>
    public List<string> Members = new();
    /// <summary>0-3: three runners, then a drone, a sniper, then chrome.</summary>
    public int Tier;
    /// <summary>Jobs they took from you (beat you to it, or you passed); runs where you beat them; members you killed; members you left alive.</summary>
    public int Wins, Losses, Dead, Spared;
    /// <summary>How it ended: null while they're working; "paid" (left town), "peace" (split the city), "finished",
    /// and after their Johnson burned them: "merged" (Rook runs with you), "left" (they left town).</summary>
    public string Fate;
    public int Since;
    /// <summary>Act two: what you did when their Johnson burned them (null: not yet; "warned", "sold", "out"), and the day.</summary>
    public string Burn;
    public int BurnDay;
    /// <summary>They owe you: you warned them.</summary>
    public bool Owed;
    /// <summary>Rook, who plans their jobs: 0 with them, 1 pulled out by you, 2 lost in the burn.</summary>
    public int Rook;
    /// <summary>Bad blood beyond the record: the burn, the rescue.</summary>
    public int Bad;
    public bool Active => Fate == null && Members.Count > 0;
    /// <summary>Times you've crossed: jobs they took from you and runs where you beat them.</summary>
    public int Contests => Wins + Losses;
    /// <summary>How much they hate you (0-9): two for every one of theirs you killed, one for every beating, one off for every one you let live, and the burn.</summary>
    public int Grudge => Math.Clamp(2 * Dead + Losses - Spared + Bad, 0, 9);
}

public sealed partial class Career
{
    public RivalCrew Rival;

    private static readonly string[] RivalNames = { "the Glass Saints", "the Neon Jackals", "the Hollow Choir", "the Rust Wolves" };
    private static readonly string[] RivalLeaders = { "Vex Okafor", "Mina 'Static' Rourke", "Dutch Calloway", "Sable Ito" };
    private static readonly string[] RivalRunners = { "Kestrel", "Bones", "Juniper", "Two-Bit", "Riot", "Lark", "Ferro", "Pike", "Hush", "Cinder" };

    /// <summary>The crew shows up in town (once): named from the career's seed, three runners and their boss.</summary>
    public void MeetRival()
    {
        var r = new Rng(Seed ^ 0x21FA1UL);
        var names = RivalRunners.OrderBy(_ => r.Next()).Take(3).ToList();
        Rival = new RivalCrew { Name = r.Pick(RivalNames), Leader = r.Pick(RivalLeaders), Members = names, Since = Day };
        Explain("rival", $"A rival crew: {Rival.Name}. Jobs they want are marked R on the board; take them fast, or they will -- and when you're both in the building, it's three ways.");
    }

    /// <summary>A new job the rivals want too: about one in four of the ones they can do, and it won't wait long.</summary>
    private void Contest(Job j)
    {
        // A crew that owes you keeps off your work.
        if (Rival is not { Active: true, Owed: false } || j.Story > 0 || j.Arc != null || j.Tag != null || j.Tutorial) return;
        if (j.Type is not (JobType.Retrieve or JobType.Datasteal or JobType.Eliminate)) return;
        if (!new Rng(j.Seed ^ 0x2C0DEUL).Chance(Tables.RivalContest)) return;
        j.Contested = true;
        j.Expires = Math.Min(j.Expires, Day + Tables.RivalWait);
        j.Brief += $" {Rival.Name} want this one too. If you're slow, they'll take it -- and if you're both in there at once, they'll fight anyone for it.";
    }

    /// <summary>A contested job you let lapse: they did it.</summary>
    private void RivalTookIt(Job j)
    {
        if (j.Tag == "rival-rook" && Rival is { Rook: 0 })
        {
            Rival.Rook = 2;
            Say($"Nobody went in for Rook. {Rival.Leader} stops answering your calls.");
        }
        if (!j.Contested || Rival is not { Active: true }) return;
        Rival.Wins++;
        RivalGrows();
        Say($"{Rival.Name} did the {Tables.JobNames[(int)j.Type].ToLower()} in {Tables.District(j.District).Name} you passed on. The street says they're good.");
    }

    private void RivalGrows() => Rival.Tier = Math.Min(3, (Rival.Wins + Rival.Losses + 1) / 2);

    /// <summary>After a contested run where they came: who they lost, and who came out ahead.</summary>
    private void RivalAftermath(Job j, RunResult r, Debrief d)
    {
        RivalArcAfter(j, r, d);
        Set("rival:last", 0);
        if (!j.Contested || Rival is not { Active: true } || !r.RivalCame) return;
        Set("rival:last", r.RivalRobbed ? 1 : r.Success ? 2 : 0);
        foreach (string n in r.RivalDead) Rival.Members.Remove(n);
        Rival.Dead += r.RivalDead.Count;
        Rival.Spared += r.RivalSpared;
        if (r.RivalRobbed) Rival.Wins++;
        else if (r.Success) Rival.Losses++;
        RivalGrows();
        string how = r.RivalRobbed ? "got away with it" : r.Success ? "came off worse" : "were there too";
        string dead = r.RivalDead.Count > 0 ? $" -- {string.Join(" and ", r.RivalDead)} won't be back" : "";
        d.Lines.Add((Rival.Name, how + dead));
        if (Rival.Members.Count == 0)
        {
            Rival.Fate = "finished";
            d.Epilogue.Add($"{Rival.Name} are finished: nobody left to run with {Rival.Leader}, who has stopped answering anyone's calls.");
            Count("rivals");
        }
        else if (Rival.Members.Count < 3 && Rival.Tier >= 1)
        {
            // They recruit to make up the numbers.
            var rr = new Rng(Seed ^ (ulong)Day * 0x2F1UL);
            string fresh = RivalRunners.Where(x => !Rival.Members.Contains(x)).OrderBy(_ => rr.Next()).First();
            Rival.Members.Add(fresh);
        }
    }

    /// <summary>Their hideout, cleared: the end of them.</summary>
    private void RivalHideoutDone(Job j, RunResult r)
    {
        if (j.Tag != "rival" || Rival == null || !r.Success) return;
        Rival.Fate = "finished";
        Rival.Members.Clear();
        Count("rivals");
        Say($"{Rival.Name} are finished. {Rival.Leader} was seen at the bus station with one bag.");
    }

    /// <summary>One line on the crew, for the PDA and the agent.</summary>
    public string RivalLine() => Rival == null ? null : Rival.Fate switch
    {
        "paid" => $"{Rival.Name}: paid to leave town.",
        "peace" => $"{Rival.Name}: an understanding -- they keep out of your work.",
        "finished" => $"{Rival.Name}: finished.",
        "merged" => $"{Rival.Name}: gone. {Rival.Leader} went straight; Rook runs with you now.",
        "left" => $"{Rival.Name}: left town after their Johnson burned them.",
        _ => $"{Rival.Name} ({Rival.Leader}; {string.Join(", ", Rival.Members)}): tier {Rival.Tier}, they've taken {Rival.Wins} job(s) from you, you've beaten them {Rival.Losses} time(s), {Rival.Dead} of theirs dead; grudge {Rival.Grudge}."
            + (Rival.Burn == null ? "" : Rival.Burn switch { "warned" => " You warned them about the burn.", "sold" => " You sold them out.", _ => " You stayed out of the burn." })
            + (RivalEndDue ? RivalEnding() switch { "showdown" => " It ends at their hideout.", "merge" => " Rook wants a word.", _ => " They're packing." } : ""),
    };

    // ---- the arc: act two, the burn; act three, how it ends -----------------------------------------

    /// <summary>What warning them is worth to the Johnson's side.</summary>
    public int RivalSellPrice => Tables.RivalSellPay + Tables.RivalSellPerTier * (Rival?.Tier ?? 0);

    /// <summary>Act two is due: they've crossed you often enough and been around long enough.</summary>
    public bool RivalBurnDue => Rival is { Active: true, Burn: null } && Rival.Contests >= Tables.RivalBurnAfter && Day - Rival.Since >= Tables.RivalBurnDay;

    /// <summary>
    /// Their Johnson burns them, and you knew: warn them (they owe you, the
    /// grudge drops, and Rook needs pulling out), sell the warning (cash,
    /// notoriety, the grudge climbs, Rook and one of the others die), or stay
    /// out of it (Rook is lost). Returns what happened.
    /// </summary>
    public string RivalBurned(string how)
    {
        RivalCrew x = Rival;
        if (x is not { Active: true } || x.Burn != null) return "Too late for that.";
        x.Burn = how;
        x.BurnDay = Day;
        switch (how)
        {
            case "warned":
                x.Owed = true;
                x.Bad -= Tables.RivalWarnGrudge;
                Job j = OfferSpecial(JobType.Rescue, SiteStyle.Office, Faction.Halcyon, "industrial", Math.Clamp(2 + x.Tier, 2, 5), "Pull Rook out",
                    $"{x.Leader}: 'We scattered in time -- all but Rook, who was already at the meet. {Tables.RivalJohnson}'s people have him in a Halcyon office, asking him who else we know. Get him out. I'll pay what I've got.'",
                    payMul: 1.2f, tag: "rival-rook", target: "Rook", expires: 5);
                return j != null ? $"{x.Leader} goes quiet, then: 'We owe you.' They scatter before the team arrives -- all but Rook. It's on your board: {j.Title}."
                    : $"{x.Leader} goes quiet, then: 'We owe you.' They scatter before the team arrives.";
            case "sold":
                Cash += RivalSellPrice;
                Count("earned", RivalSellPrice);
                Infamy(1);
                x.Bad += Tables.RivalSellGrudge;
                x.Rook = 2;
                string lost = RivalBurnLoss();
                return $"{Tables.RivalJohnson}'s people pay on time. The team goes in at the address you sold: Rook{(lost != null ? $" and {lost}" : "")} don't come out. {x.Leader} will work out who talked.";
            default:
                x.Rook = 2;
                return $"You stay out of it. The team takes Rook at the meet; the rest of {x.Name} hear the sirens and run. Nobody's seen Rook since.";
        }
    }

    /// <summary>One of the crew dies in a burn you sold (their seed picks who); with nobody left, they're finished.</summary>
    private string RivalBurnLoss()
    {
        if (Rival.Members.Count == 0) return null;
        string n = Rival.Members[(int)(new Rng(Seed ^ 0xB0A2EUL).Next() % (ulong)Rival.Members.Count)];
        Rival.Members.Remove(n);
        if (Rival.Members.Count == 0) { Rival.Fate = "finished"; Count("rivals"); }
        return n;
    }

    /// <summary>Act three is due: a few days after the burn, with Rook's rescue no longer pending.</summary>
    public bool RivalEndDue => Rival is { Active: true, Burn: not null } && Day - Rival.BurnDay >= Tables.RivalEndAfter && !Board.Any(j => j.Tag == "rival-rook");

    /// <summary>
    /// How it ends, from the history: a showdown at their hideout if you sold
    /// them, if they hate you enough, or if a tier-3 crew you didn't warn is too
    /// proud to leave; Rook joins you if you warned them, pulled him out, aren't
    /// a butcher and there's little bad blood; otherwise they leave town.
    /// </summary>
    public string RivalEnding()
    {
        RivalCrew x = Rival;
        if (x is not { Active: true } || x.Burn == null) return null;
        if (x.Burn == "sold" || x.Grudge >= Tables.RivalShowdownGrudge || x.Burn == "out" && x.Tier >= 3) return "showdown";
        if (x.Burn == "warned" && x.Owed && x.Rook == 1 && x.Grudge <= Tables.RivalMergeGrudge && Notoriety < Tables.RivalMergeNotoriety) return "merge";
        return "leave";
    }

    /// <summary>The end of them, by the arc: Rook joins you ("merged"), or they leave town ("left").</summary>
    public void RivalEnd(string fate)
    {
        if (Rival == null) return;
        Rival.Fate = fate;
        Set("rival:endday", Day);
        if (fate != "merged") return;
        CrewState s = CrewOf("rook");
        s.Trust = Math.Max(s.Trust, Tables.RivalMergeTrust);
        Explain("rook", "Rook joins your crew: hire him at Juno's like the others. He trusts you already.");
    }

    /// <summary>Jobs of the arc, settled: Rook pulled out (or lost), and the hideout as the finale.</summary>
    private void RivalArcAfter(Job j, RunResult r, Debrief d)
    {
        if (Rival == null) return;
        if (j.Tag == "rival-rook" && Rival.Rook == 0)
        {
            Rival.Rook = r.Success ? 1 : 2;
            if (r.Success) Rival.Bad -= Tables.RivalRookGrudge;
            d.Lines.Add(("Rook", r.Success ? $"pulled out; {Rival.Name}'s grudge -{Tables.RivalRookGrudge} (now {Rival.Grudge})" : "lost"));
            if (!r.Success) Say($"Rook didn't come out. {Rival.Leader} won't say your name any more.");
        }
        if (j.Tag == "rival" && r.Success && Rival is { Active: true, Burn: not null })
        {
            // The finale: their stash, and a name for having ended it.
            Cash += Tables.RivalStash;
            Count("earned", Tables.RivalStash);
            Karma += Tables.RivalShowdownKarma;
            KarmaEarned += Tables.RivalShowdownKarma;
            Rep += 2;
            d.Lines.Add(($"{Rival.Name}'s stash", $"+{M.Money(Tables.RivalStash)}, karma +{Tables.RivalShowdownKarma}, street rep +2"));
            d.Epilogue.Add($"{Rival.Name} are finished. The street tells it as the night {Rival.Leader}'s crew ended in their own tenement -- and tells it with your name.");
        }
    }
}
