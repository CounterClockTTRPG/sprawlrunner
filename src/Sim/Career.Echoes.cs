using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// A job the city remembers: who was there, how loud it was, who you let
/// walk. Written when a job closes; the echoes (<c>Storylets.Echoes</c>)
/// read it weeks later.
/// </summary>
public sealed class PastJob
{
    public int Id, Day, Threat, Pay, Kills, Spared, KnockOuts;
    public ulong Seed;
    public JobType Type;
    public Faction Faction;
    public string Title, Target, District, Johnson;
    /// <summary>What became of the named target (as <see cref="LastRun.Fate"/>), or null.</summary>
    public string Fate;
    public bool Success, Alarm, Crossed;
    /// <summary>What was decided about it afterwards ("kids:free").</summary>
    public List<string> Tags = new();
    /// <summary>The echoes this job has already sent back.</summary>
    public List<string> Echoed = new();

    public string Place => District != null && Tables.Districts.Any(d => d.Id == District) ? Tables.District(District).Name : "across town";
}

public sealed partial class Career
{
    /// <summary>The last jobs, oldest first, for the echoes.</summary>
    public List<PastJob> Memories = new();

    /// <summary>How many jobs are remembered; an echo comes 10-30 days after its job, and no two within 4 days.</summary>
    public const int MemoryCap = 20, EchoMin = 10, EchoSpread = 21, EchoGap = 4;

    /// <summary>A job closes: remember it.</summary>
    private void Remember(Job j, RunResult r)
    {
        if (j.Tutorial || j.Driving || j.Type == JobType.Hub || r.Aborted) return;
        string fate = j.Story == 0 && Last?.Job == j ? Last.Fate : null;
        if (r.Choices.Contains("begs:1")) fate = "rescued";
        Memories.Add(new PastJob
        {
            Id = j.Id, Day = Day, Threat = j.Threat, Pay = j.Pay, Kills = r.Kills, Spared = r.Spared, KnockOuts = r.KnockOuts, Seed = j.Seed,
            Type = j.Type, Faction = j.Faction, Title = j.Title, Target = j.Target, District = j.District, Johnson = j.JohnsonId,
            Fate = fate, Success = r.Success, Alarm = r.Alarm, Crossed = r.Trap || r.Success && j.Stiff,
        });
        while (Memories.Count > MemoryCap) Memories.RemoveAt(0);
    }

    /// <summary>Something decided after the job (a mail answered days later): tag the job it was about.</summary>
    public void Recall(Mail m, string tag, JobType? type = null)
    {
        PastJob p = Memories.LastOrDefault(x => x.Day < m.Day && (type == null || x.Type == type));
        if (p != null && !p.Tags.Contains(tag)) p.Tags.Add(tag);
    }

    /// <summary>
    /// A guard you let live works another of their sites now: the next job
    /// against that faction starts with the locked doors open (or, if you
    /// bought that already, two of the guards off sick).
    /// </summary>
    private void Inside(Job j)
    {
        string key = "inside:" + j.Faction;
        if (Flag(key) == 0 || j.Driving || j.Type is JobType.Hub or JobType.Hack or JobType.Defend or JobType.Bounty) return;
        Set(key, 0);
        string lw = j.Legwork.Contains("door") ? "bribe" : "door";
        if (!j.Legwork.Contains(lw)) j.Legwork.Add(lw);
        Say($"Someone on the inside at {Tables.FactionNames[(int)j.Faction]}: {LegworkName(lw)}.");
    }
}
