using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>Election week: the calendar, the jobs that add weight, the count, and each winner's season.</summary>
public partial class SmokeTest
{
    private void Election()
    {
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        c.Cash = 100000;
        int s0 = c.ElectionStart(0), s1 = c.ElectionStart(1);
        Check(s0 is >= 25 and <= 28 && s1 is >= 55 and <= 58, $"the council votes about every 30 days from day 25 (weeks open on {s0}, {s1})");
        Eq(TestCareer().ElectionStart(0), s0, "the same career gets the same calendar");
        // The week opens.
        c.Day = s0 - 1;
        c.NextDay(city);
        Check(c.On("election") && c.ElectionCycle == 1, "election week opens");
        Check(c.News.Any(n => n.Contains("KBN: Election week")), "KBN announces it");
        Check(c.Inbox.Any(m => m.Story == "vote-open"), "and Mara Quill writes to explain");
        var votes = c.Board.Where(j => j.Tag?.StartsWith("vote:") == true).ToList();
        Check(votes.Count == 3 && Career.Candidates.All(x => votes.Any(j => j.Tag == $"vote:{x.id}:1")), $"one job per campaign on the board ({string.Join(", ", votes.Select(j => j.Tag))})");
        Check(votes.All(j => j.Expires < c.VoteDay(0)), "gone by the count");
        Check(votes.First(j => j.Tag.StartsWith("vote:halcyon")).Faction == Faction.Lotus && votes.First(j => j.Tag.StartsWith("vote:lotus")).Faction == Faction.Halcyon, "rigging it for one side means hitting the other");
        Check(c.HappeningLines().Any(l => l.Contains("Election week") && l.Contains("Rosa Ibarra") && l.Contains("7 days")), "the happenings list shows the candidates and the days to the vote");
        Check(c.Radio("strip", false).Any(x => x.text.Contains("Election countdown")), "so does the radio");
        Check(c.Graffiti(city).Values.Any(t => t.text.Contains("IBARRA")), "and the rooftops");
        // Jobs add weight.
        Job hj = votes.First(j => j.Tag.StartsWith("vote:halcyon"));
        Job rj = votes.First(j => j.Tag.StartsWith("vote:reform"));
        Career miss = Clone(c);
        c.Finish(hj, Won(c));
        Eq(c.VoteWeight("halcyon"), 1, "a finished Halcyon job is a vote for Vell");
        miss.Finish(miss.Board.First(j => j.Tag == rj.Tag), Lost(miss));
        Eq(miss.VoteWeight("reform"), 0, "a botched one is no vote at all");
        // The count, by itself: heaviest wins.
        Career hal = Clone(c);
        hal.Clocks["lonestar"] = 3;
        hal.Day = hal.VoteDay(0) - 1;
        hal.Happenings.RemoveAll(h => h.Id != "election");
        hal.NextDay(city);
        Check(hal.Flag("vote:held") == 1 && hal.On("renewal") && hal.Inbox.Any(m => m.Story == "vote-result"), "on the day of the vote, the heaviest campaign wins (Vell) and Quill reports it");
        Check(hal.News.Any(n => n.Contains("Adrian Vell takes the council seat")), "KBN calls it");
        Check(hal.Clock("lonestar") >= 3 + Tables.RenewalPolice - 1, $"renewal: more police (Lone Star plans {hal.Clock("lonestar")})");
        int grew = Tables.Districts.Count(d => hal.TurfOf(d.Id)[(int)Faction.Halcyon] > c.TurfOf(d.Id)[(int)Faction.Halcyon] + 0.03f);
        Check(grew >= 2, $"renewal: Halcyon takes ground in two districts ({grew})");
        Career plain = Clone(hal);
        plain.Happenings.RemoveAll(h => h.Id == "renewal");
        plain.Rep = hal.Rep = 9;
        var withR = Enumerable.Range(0, 200).Select(_ => hal.MakeJob(city)).ToList();
        var without = Enumerable.Range(0, 200).Select(_ => plain.MakeJob(city)).ToList();
        var pairs = withR.Zip(without).Where(p => p.First != null && p.Second != null && p.First.Faction == Faction.Halcyon && !p.First.Driving).ToList();
        float ratio = pairs.Sum(p => (float)p.First.Pay) / Math.Max(1, pairs.Sum(p => (float)p.Second.Pay));
        Check(pairs.Count > 5 && ratio is > 1.06f and < 1.14f, $"renewal: jobs against Halcyon pay 10 % more ({pairs.Count}, x{ratio:0.00})");
        // A job left over from a week that's been counted is no vote.
        Job late = new() { Id = 991, Type = JobType.Plant, Faction = Faction.Halcyon, Style = SiteStyle.Office, District = "spire", Threat = 2, Seed = 7, Tag = "vote:lotus:1", Title = "Late", Lot = -1, Pay = 2000, Karma = 6, Employer = "fixer", Target = "x", Item = "y" };
        hal.Finish(late, Won(hal));
        Eq(hal.VoteWeight("lotus"), 0, "a vote after the count counts for nothing");
        // Each season ends.
        hal.Day = hal.Happenings.First(h => h.Id == "renewal").End;
        hal.NextDay(city);
        Check(!hal.On("renewal"), $"renewal lasts {Tables.SeasonDays} days");
        // Ties and empty weeks: the Lotus' money.
        int lotusWins = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            Career e = Career.New("Test", "samurai", seed);
            e.Set("vote:n", 1);
            if (e.ElectionWinner() == "lotus") lotusWins++;
            Eq(e.ElectionWinner(), e.ElectionWinner(), "the baseline is seeded");
        }
        Check(lotusWins >= 15, $"nobody does anything, the Lotus' money wins ({lotusWins} of 20)");
        Career tie = Clone(c);
        tie.Set("vote:reform", 1);
        tie.Set("vote:lotus", 1);
        Check(tie.ElectionWinner() != "reform", "a tie with Ibarra goes to the money");
        // The Lotus win: the council in their pocket, for the season (an older one runs on).
        Career lot = Clone(c);
        lot.Set("vote:halcyon", 0);
        lot.Happenings.Add(new Happening { Id = "council", Start = lot.Day, End = lot.Day + 1 });
        Eq(lot.ElectionWinner(), "lotus", "with no votes cast, Tsao");
        lot.CountVotes();
        Check(lot.On("council") && lot.Happenings.Count(h => h.Id == "council") == 1 && lot.Happenings.First(h => h.Id == "council").End == lot.Day + Tables.SeasonDays - 1, "Tsao wins: the council is the Lotus' for 20 days");
        Check(lot.News.Any(n => n.Contains("Henry Tsao is re-elected")), "KBN reports it");
        // The reformer: police set back, rent down, the street remembers.
        Career rf = Clone(c);
        rf.Set("vote:halcyon", 0);
        rf.Finish(rf.Board.First(j => j.Tag == rj.Tag), Won(rf));
        Check(rf.VoteWeight("reform") == 1 && rf.Flag("vote:helped") > 0, "walking Ibarra in is a vote for her");
        rf.Clocks["lonestar"] = 5;
        rf.Notoriety = 3;
        int rent = rf.RentDue;
        rf.CountVotes();
        Check(rf.On("reform") && rf.News.Any(n => n.Contains("Rosa Ibarra takes the seat")), "Ibarra wins, and KBN can't believe it");
        Eq(rf.Clock("lonestar"), 5 - Tables.ReformPolice, "reform: Lone Star's plans -3");
        Eq(rf.Notoriety, 2, "reform: the street remembers who helped (notoriety -1, once)");
        Check(rf.RentDue < rent && rf.RentDue >= (int)(rent * 0.8f), $"reform: rents 15 % lower ({rent} -> {rf.RentDue})");
        rf.Rep = 9;
        var rjobs = Enumerable.Range(0, 200).Select(_ => rf.MakeJob(city)).Where(j => j != null && !j.Driving).ToList();
        Check(rjobs.Count > 5 && rjobs.All(j => j.Has(RunMod.ShiftChange) && !j.Has(RunMod.Skeleton)), "reform: every faction doubles its patrols, no skeleton crews");
        rf.Day += Tables.SeasonDays;
        Check(!rf.On("reform") && rf.RentDue == rent, "and the season ends");
        // A whole week, played: the count comes and goes on its own.
        Career w = TestCareer();
        w.CityRef = city;
        w.Cash = 100000;
        w.Day = 20;
        while (w.Day < w.VoteDay(0) + 1) w.NextDay(city);
        Check(w.Flag("vote:held") == 1 && w.Inbox.Count(m => m.Story == "vote-result") == 1, "a career that ignores it still has an election, once");
        Check(new[] { "renewal", "council", "reform" }.Count(id => w.Happenings.Any(h => h.Id == id && h.End >= w.VoteDay(0) + Tables.SeasonDays - 1)) == 1, "and lives with whoever won");
    }
}
