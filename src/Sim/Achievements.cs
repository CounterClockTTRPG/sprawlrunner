using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// Milestones, checked against the career's counters. Unlocks are kept by the
/// profile across careers; each is announced once.
/// </summary>
public static class Achievements
{
    public sealed record Def(string Id, string Name, string Desc, Func<Career, bool> Done);

    public static readonly Def[] All =
    {
        new("first", "First paycheck", "Finish a job.", c => c.JobsDone >= 1),
        new("pro", "Professional", "Finish 10 jobs in one career.", c => c.JobsDone >= 10),
        new("veteran", "Veteran", "Finish 25 jobs in one career.", c => c.JobsDone >= 25),
        new("ghost", "Ghost", "Finish a job without raising the alarm.", c => c.Stat("noalarm") >= 1),
        new("clean", "Clean hands", "Finish a job without killing anyone.", c => c.Stat("nokill") >= 1),
        new("lightsout", "Lights out", "Knock out 10 people.", c => c.Stat("kos") >= 10),
        new("hundred", "Body count", "Kill 100 hostiles.", c => c.Kills >= 100),
        new("giant", "Giant killer", "Take down a boss.", c => c.Stat("bosses") >= 1),
        new("cowboy", "Console cowboy", "Pull all three datastores out of the Matrix.", c => c.Stat("matrix") >= 1),
        new("spiderbite", "Spider bite", "Crash a security spider's persona in the Matrix.", c => c.Stat("spiders") >= 1),
        new("renewal", "Urban renewal", "Bring down 200 walls.", c => c.Stat("walls") >= 200),
        new("spells", "Spellslinger", "Cast 50 spells.", c => c.Stat("spells") >= 50),
        new("delivery", "Special delivery", "Finish a courier job.", c => c.Stat("done.Courier") >= 1),
        new("twist", "Roll with it", "Finish a job whose plan changed half-way.", c => c.Stat("twist") >= 1),
        new("team", "Team player", "Finish a job with a hired runner.", c => c.Stat("hire") >= 1),
        new("rights", "Read your rights", "Get busted by Lone Star.", c => c.Stat("busts") >= 1),
        new("wanted", "Most wanted", "Reach five stars of heat.", c => c.Stat("maxheat") >= 5),
        new("ambush", "Eyes in the mirror", "Survive a gang ambush.", c => c.Stat("ambushes") >= 1),
        new("roadtrip", "Road trip", "Drive 50 km.", c => c.Stat("tiles") >= 12500),
        new("twowheels", "Two wheels", "Own a motorcycle.", c => c.Vehicles.Any(v => Tables.Vehicle(v).Bike)),
        new("chromed", "Chromed", "Carry four implants at once.", c => c.Cyber.Count >= 4),
        new("rich", "Six figures", "Hold ¥100,000 at once.", c => c.Cash >= 100000),
        new("friend", "Friend in need", "Finish a contact's personal story.", c => Career.Arcs.Keys.Any(c.ArcDone)),
        new("everyone", "Everybody's runner", "Finish every contact's personal story.", c => Career.Arcs.Keys.All(c.ArcDone)),
        new("trusted", "Trusted", "Reach trust 5 with a contact.", c => c.Loyalty.Values.Any(v => v >= 5)),
        new("second", "Second chance", "Get pulled out by TraumaLink.", c => c.Stat("traumalink") >= 1),
        new("oldtimer", "Old timer", "Survive to day 30.", c => c.Day >= 30),
        new("legend", "Legend", "Finish the Iron Spire story.", c => c.Retired && c.Chapter >= 4 || c.Flag("story:won") > 0),
        new("tide", "Low tide", "Finish Toxic Tide.", c => c.Flag("tide:won") > 0),
        new("together", "The long way home", "Retire with your partner.", c => c.Retired && c.Exit == "partner"),
        new("booth", "The back booth", "Retire into Juno's chair.", c => c.Retired && c.Exit == "fixer"),
        new("straight", "Gone straight", "Retire to run a business of your own.", c => c.Retired && c.Exit == "legit"),
        new("kingmaker", "Kingmaker", "Push a faction out of a district.", c => c.Flags.Keys.Any(k => k.StartsWith("held:"))),
        new("crew", "Ride or die", "Reach trust 5 with a crew member.", c => c.Crew.Values.Any(s => s.Trust >= 5)),
        new("held", "My house", "Hold your safehouse against a raid.", c => c.Stat("raidsheld") >= 1),
        new("detective", "Legwork", "Finish an Investigate job.", c => c.Stat("done.Investigate") >= 1),
        new("planted", "Never there", "Finish a Plant job without raising the alarm.", c => c.Stat("noalarm.Plant") >= 1),
        new("cut", "Studied", "Finish The Cut.", c => c.Flag("cut:won") > 0),
        new("wyrm", "Talking to dragons", "Finish The Wyrm's Ledger.", c => c.Flag("wyrm:won") > 0),
        new("hive", "Burn it all", "Burn out the Hive: the queen, and every egg she laid.", c => c.Flag("hive:won") > 0),
        new("heist", "Score of the decade", "Pull off the three-night heist.", c => c.Flag("heists") >= 1),
        new("regular", "A regular", "Do three clean jobs for the same Mr. Johnson.", c => Career.Johnsons.Any(j => c.JohnsonJobs(j.Id) >= 3)),
        new("platinum", "Platinum service", "Get carried out by TraumaLink with the job done.", c => c.Stat("done.platinum") >= 1),
        new("pen", "Pen pal", "Answer 15 messages.", c => c.Inbox.Count(m => m.Answer >= 0 && c.StoryOf(m)?.Choices != null) >= 15),
    };

    /// <summary>Ids now earned that are not in <paramref name="have"/>.</summary>
    public static List<Def> NewlyEarned(Career c, ICollection<string> have) => All.Where(a => !have.Contains(a.Id) && a.Done(c)).ToList();
}

public sealed partial class Career
{
    /// <summary>Career counters (jobs by type, kills, walls, distance...).</summary>
    public Dictionary<string, int> Stats = new();

    public int Stat(string key) => Stats.GetValueOrDefault(key);
    public void Count(string key, int by = 1) => Stats[key] = Stats.GetValueOrDefault(key) + by;
    public void Max(string key, int value) { if (value > Stat(key)) Stats[key] = value; }
}
