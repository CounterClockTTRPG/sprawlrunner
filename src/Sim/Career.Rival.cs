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
    /// <summary>How it ended: null while they're working; "paid" (left town), "peace" (split the city), "finished".</summary>
    public string Fate;
    public int Since;
    public bool Active => Fate == null && Members.Count > 0;
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
        if (Rival is not { Active: true } || j.Story > 0 || j.Arc != null || j.Tag != null || j.Tutorial) return;
        if (j.Type is not (JobType.Retrieve or JobType.Datasteal or JobType.Eliminate)) return;
        if (!new Rng(j.Seed ^ 0x2C0DEUL).Chance(Tables.RivalContest)) return;
        j.Contested = true;
        j.Expires = Math.Min(j.Expires, Day + Tables.RivalWait);
        j.Brief += $" {Rival.Name} want this one too. If you're slow, they'll take it -- and if you're both in there at once, they'll fight anyone for it.";
    }

    /// <summary>A contested job you let lapse: they did it.</summary>
    private void RivalTookIt(Job j)
    {
        if (!j.Contested || Rival is not { Active: true }) return;
        Rival.Wins++;
        RivalGrows();
        Say($"{Rival.Name} did the {Tables.JobNames[(int)j.Type].ToLower()} in {Tables.District(j.District).Name} you passed on. The street says they're good.");
    }

    private void RivalGrows() => Rival.Tier = Math.Min(3, (Rival.Wins + Rival.Losses + 1) / 2);

    /// <summary>After a contested run where they came: who they lost, and who came out ahead.</summary>
    private void RivalAftermath(Job j, RunResult r, Debrief d)
    {
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
        _ => $"{Rival.Name} ({Rival.Leader}; {string.Join(", ", Rival.Members)}): tier {Rival.Tier}, they've taken {Rival.Wins} job(s) from you, you've beaten them {Rival.Losses} time(s), {Rival.Dead} of theirs dead.",
    };
}
