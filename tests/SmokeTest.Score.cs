using System.Linq;

namespace Sprawl;

/// <summary>The career score: what counts, what costs, how it ended, the rank, and the best kept on the profile.</summary>
public partial class SmokeTest
{
    private void CareerScore()
    {
        Career c = TestCareer();
        c.Day = 0;
        Check(c.Score >= 0 && c.Rank == Tables.Ranks[0].name, $"a new runner scores next to nothing and is a {Tables.Ranks[0].name} ({c.Score})");

        // Every day, job, life saved and spared guard counts; karma too.
        Career a = TestCareer();
        a.Day = 30;
        a.JobsDone = 20;
        a.KarmaEarned = 200;
        a.Set("fate:Mira", Career.Fates["rescued"]);
        a.Count("spared", 3);
        int before = a.Score;
        Check(before >= 30 * Tables.ScoreDay + 20 * Tables.ScoreJob + Tables.ScoreRescue + 3 * Tables.ScoreSpared + 200 * Tables.ScoreKarma, $"days, jobs, rescues, the spared and karma all count ({before:N0})");
        Check(a.AchievementsHere.Any(x => x.Id == "pro"), "and the achievements this career earned are on it (Professional, for ten jobs)");

        // What costs: a failed job, crew buried, a guard shot after he gave up, a TraumaLink pick-up.
        Career b = TestCareer();
        b.Day = 30; b.JobsDone = 20; b.KarmaEarned = 200;
        b.Set("fate:Mira", Career.Fates["rescued"]);
        b.Count("spared", 3);
        int clean = b.Score;
        b.JobsFailed = 2;
        b.Crew["ox"] = new CrewState { Dead = true };
        b.Count("helpless", 1);
        b.Count("traumalink", 1);
        // (Being picked up by TraumaLink is an achievement of its own, so the penalty lines, not the total, are what's checked.)
        int cost = -b.ScoreLines().Where(l => l.points < 0).Sum(l => l.points);
        Eq(cost, 2 * Tables.ScoreFailed + Tables.ScoreCrewLost + Tables.ScoreHelpless + Tables.ScoreTrauma, "failure, the dead, the helpless and the trauma team cost points");
        Check(b.Score < clean, $"and they cost more than they earn ({b.Score:N0} < {clean:N0})");

        // How it ended: walking away beats dying; walking away a legend beats both.
        Career d = TestCareer();
        d.Day = 40; d.JobsDone = 25;
        int died = d.Score;
        d.Retired = true;
        int walked = d.Score;
        d.Set("story:won");
        int legend = d.Score;
        Check(walked > died && legend > walked && d.HowItEnded.how.Contains("legend"), $"retiring scores more than dying, a legend more again ({died:N0} < {walked:N0} < {legend:N0})");
        Check(d.ScoreLines().Any(l => l.what == "Stories finished" && l.points == Tables.ScoreStory), "a finished story is worth a good deal");

        // The ranks run in order.
        Check(Tables.Ranks.Zip(Tables.Ranks.Skip(1)).All(p => p.First.from < p.Second.from), "the ranks climb");
        Eq(Career.RankFor(Tables.Ranks[^1].from + 1), Tables.Ranks[^1].name, "and the last is the top");

        // The receipt lists the score's pieces, and this career's achievements.
        Check(a.Receipt().Any(r => r.what == "Achievements this career" && r.value.Contains("Professional")), "the last receipt names this career's achievements");
        Check(a.Receipt().Any(r => r.what == "Guards you let live" && r.value == "3"), "and the guards let live (counted, now)");

        // The profile keeps the best.
        var pf = new Profile();
        Check(pf.RecordCareer(a) && pf.BestScore == a.Score && pf.BestRank == a.Rank && pf.BestName.StartsWith(a.Name), "the first career over is the best yet");
        Check(!pf.RecordCareer(c) && pf.BestScore == a.Score, "a lesser one doesn't replace it");
        Check(pf.BestJobs == 20 && pf.BestDays == 30, "the longest and busiest are still kept");
        Check(pf.RecordCareer(d) && pf.BestScore == d.Score, "a better one does");
    }
}
