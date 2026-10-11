using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// The career's score, printed on the last receipt: points for every day survived, job done, story
/// finished, life saved and guard spared, and for every achievement this career earned (whether or not
/// the profile had it already); points off for failed jobs, crew buried, guards shot after they gave up
/// and every TraumaLink pick-up; then the way it ended -- walking away a legend, walking away, or dying.
/// The rank is what the street calls you for it.
/// </summary>
public sealed partial class Career
{
    /// <summary>The score's lines: what, how many, and the points for it.</summary>
    public List<(string what, string count, int points)> ScoreLines()
    {
        var lines = new List<(string, string, int)>();
        void Add(string what, int n, int each) { if (n != 0) lines.Add((what, $"{n}", n * each)); }
        Add("Days on the street", Day, Tables.ScoreDay);
        Add("Jobs done", JobsDone, Tables.ScoreJob);
        Add("Jobs failed", JobsFailed, -Tables.ScoreFailed);
        Add("Stories finished", StoriesFinished.Count, Tables.ScoreStory);
        Add("People you got out", Flags.Count(kv => kv.Key.StartsWith("fate:") && kv.Value == Fates["rescued"]), Tables.ScoreRescue);
        Add("Guards you let live", Stat("spared"), Tables.ScoreSpared);
        Add("Achievements this career", AchievementsHere.Count, Tables.ScoreAchievement);
        lines.Add(("Karma earned", $"{KarmaEarned}", KarmaEarned * Tables.ScoreKarma));
        Add("Crew buried", Crew.Count(kv => kv.Value.Dead), -Tables.ScoreCrewLost);
        Add("Shot after they gave up", Stat("helpless"), -Tables.ScoreHelpless);
        Add("TraumaLink pick-ups", Stat("traumalink"), -Tables.ScoreTrauma);
        return lines;
    }

    /// <summary>How the career ended, as a multiplier on its points.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public (string how, float mul) HowItEnded => Retired
        ? (Exit switch
            {
                "partner" when Partner != null => $"Settled down with {Tables.Hires.First(h => h.id == Partner).name}",
                "fixer" => "Took Juno's chair",
                "legit" => "Went straight",
                _ => RetiresALegend ? "Retired a legend" : "Walked away",
            }, RetiresALegend ? Tables.ScoreLegend : Tables.ScoreRetired)
        : ("Died on the job", 1f);

    [System.Text.Json.Serialization.JsonIgnore] public int Score => Math.Max(0, (int)MathF.Round(ScoreLines().Sum(l => l.points) * HowItEnded.mul));

    /// <summary>What the street calls a runner with this score.</summary>
    public static string RankFor(int score)
    {
        string name = Tables.Ranks[0].name;
        foreach (var (from, rank) in Tables.Ranks) if (score >= from) name = rank;
        return name;
    }

    [System.Text.Json.Serialization.JsonIgnore] public string Rank => RankFor(Score);

    /// <summary>The achievements this career earned (the profile keeps them across careers; this is just this one's).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public List<Achievements.Def> AchievementsHere => Achievements.All.Where(a => Safe(a.Done)).ToList();

    /// <summary>The stories this career saw through.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public List<string> StoriesFinished
    {
        get
        {
            var stories = new List<string>();
            if (Flag("story:won") > 0) stories.Add("the Iron Spire");
            foreach (CampaignDef c in Campaigns) if (Flag(c.Id + ":won") > 0) stories.Add(c.Name);
            if (Flag("tide:won") > 0) stories.Add("Toxic Tide");
            return stories;
        }
    }
}
