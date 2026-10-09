using System;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>Romance: who asks and when, one partner at a time, what a partner does, ending it, losing them.</summary>
public partial class SmokeTest
{
    private void Romance()
    {
        City city = City.Generate(55);
        Career c = RichCareer(city);
        Storylet ask = global::Sprawl.Storylets.ById("romance-ox");
        c.CrewOf("ox").Trust = 4;
        c.Fired["crewhome2-ox"] = c.Day;
        Check(ask != null && !ask.When(c), "nobody asks before full trust");
        c.CrewOf("ox").Trust = 5;
        Check(ask.When(c) && ask.Place == "home", "at trust 5, after the second talk, Ox asks at the safehouse");
        foreach (string id in new[] { "wisp", "patch", "ade", "rook" })
            Check(global::Sprawl.Storylets.ById($"romance-{id}") != null && global::Sprawl.Storylets.ById($"partner-{id}") != null, $"{id} can be a partner");
        Check(global::Sprawl.Storylets.ById("romance-byte") == null && global::Sprawl.Storylets.ById("partner-byte") == null, "Byte is a kid: never");

        // Not yet: again in a few weeks.
        Check(c.Choose(c.Send(ask), 1) == null && c.Partner == null && !c.Eligible(Hook.Place, "home").Contains(ask), "'not yet': not tonight");
        c.Day += Tables.RomanceAskAgain;
        Check(c.Eligible(Hook.Place, "home").Contains(ask), "asked again a few weeks on");
        Check(c.Choose(c.Send(ask), 0) == null && c.Partner == "ox", "'stay': Ox is your partner");
        c.CrewOf("wisp").Trust = 5;
        c.Fired["crewhome2-wisp"] = c.Day;
        Check(!global::Sprawl.Storylets.ById("romance-wisp").When(c), "one partner at a time");

        // What a partner does: fights harder beside you, mends you overnight, comes round on quiet nights, worries.
        string[] rows = { "############", "#P.........#", "#..........#", "############" };
        Job j = TestJob(JobType.Destroy);
        j.Hired = "ox";
        float with = new Run(j, c, Site(rows)).Ally.MaxHp, alone = new Run(j, RichCareer(city), Site(rows)).Ally.MaxHp;
        Check(MathF.Abs(with - alone * Tables.PartnerAllyHp) < 0.01f, $"a partner on the job has more to give ({with} vs {alone})");
        c.Lifestyle = "squatter";
        c.Injuries = 1;
        c.NextDay(city, rested: true);
        Check(c.Injuries == 0, "even in a squat, a night at home with them mends an injury");
        Storylet night = global::Sprawl.Storylets.ById("partner-ox");
        c.Day = c.Flag("partner:since") + 2;
        c.WellFed = false;
        Check(night.When(c), "quiet nights in");
        Mail nm = c.Send(night);
        Check(!string.IsNullOrEmpty(nm.Text) && !nm.Text.Contains('{') && c.Choose(nm, 0) == null && c.WellFed, $"stay in: well fed ({nm.Text})");
        c.Last = new LastRun { Job = StoryJob(seed: 61), Success = false };
        c.Injuries = 2;
        Storylet worry = global::Sprawl.Storylets.ById("partner-worry-ox");
        Check(worry.When(c), "a bad job without them: they worry");
        Check(c.Choose(c.Send(worry), 0) == null && c.Injuries == 1, "and patch you up");
        c.Last.Job.Hired = "ox";
        c.Set("home:job", 0);
        Check(!worry.When(c), "not when they were there");
        Check(c.Ending().Any(e => e.StartsWith("Ox is still with you")), "the ending remembers them");

        // Ending it.
        Career b = RichCareer(city);
        b.CrewOf("wisp").Trust = 5;
        b.Partner = "wisp";
        b.Day = 10;
        Check(b.Choose(b.Send("partner-wisp"), 1) == null && b.Partner == null && b.CrewTrust("wisp") == 5 - Tables.PartnerBreakTrust && b.CrewBlock("wisp") != null, "ending it costs trust and a few days apart");
        b.Fired["crewhome2-wisp"] = b.Day;
        b.CrewOf("wisp").Trust = 5;
        b.Day += Tables.RomanceAskAgain + 5;
        Check(!global::Sprawl.Storylets.ById("romance-wisp").When(b), "and she doesn't ask again");

        // Losing them.
        Career d = TestCareer();
        d.CityRef = city;
        d.CrewOf("patch").Trust = 5;
        d.Partner = "patch";
        Job dj = StoryJob(seed: 62);
        dj.Hired = "patch";
        var died = Won(d);
        died.AllyDied = true;
        d.Finish(dj, died);
        Check(d.Partner == null && d.CrewOf("patch").Dead, "a partner killed on a run is gone");
        Check(d.Eligible(Hook.Morning).Any(s => s.Id == "partner-dead-patch") && !d.Eligible(Hook.Morning).Any(s => s.Id == "crewdead-patch"), "and grieved, not just buried");
        d.Day += Tables.PartnerKeepsakeAfter;
        Check(global::Sprawl.Storylets.ById("partner-keepsake-patch").When(d), "weeks later, something of theirs in a drawer");
        Check(d.Ending().Any(e => e.StartsWith("Patch is buried") && e.Contains("every year")), "the ending remembers that too");
    }
}
