using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sprawl;

/// <summary>Trouble at the businesses you own: shakedowns, inspectors, a hand in the till -- and what each costs on payday.</summary>
public partial class SmokeTest
{
    /// <summary>Run the career to the next payday; returns what the noodle bar paid and the line that said so.</summary>
    private static (int take, string line) BizWeek(Career c, City city, string id = "noodles")
    {
        c.Day = (c.Day / 7 + 1) * 7 - 1;
        c.NextDay(city);
        string name = Career.BusinessOf(id).name;
        string line = c.News.LastOrDefault(l => l.StartsWith($"Day {c.Day}. {name}:")) ?? "";
        Match mt = Regex.Match(line, @"¥([\d,]+) this week");
        return (mt.Success ? int.Parse(mt.Groups[1].Value.Replace(",", "")) : -1, line);
    }

    private static Career BizCareer(City city)
    {
        Career c = TestCareer();
        c.CityRef = city;
        c.Cash = 100000;
        c.Property.Add("noodles");
        c.Day = 6;
        c.NextDay(city);
        return c;
    }

    private void BusinessTroubles()
    {
        City city = City.Generate(55);
        string[] kinds = { "biz-shakedown", "biz-inspector", "biz-skim" };

        // A plain week, and when trouble may come.
        Career c = BizCareer(city);
        Faction holder = c.Holder("docks");
        Check(c.News.Any(l => l.Contains("A noodle bar in the Docks: ¥4,000 this week")), "an untroubled week pays the business in full");
        Career none = TestCareer();
        none.CityRef = city;
        none.Day = 30;
        int days = 0, eligible = 0;
        for (int d = 8; d < 68; d++)
        {
            c.Day = d;
            none.Day = d;
            if (c.TroubleToday()) days++;
            if (c.Eligible(Hook.Morning).Any(s => kinds.Contains(s.Id))) eligible++;
            Check(!none.Eligible(Hook.Morning).Any(s => kinds.Contains(s.Id)), $"no business, no business trouble (day {d})");
        }
        Check(days >= 10 && days <= 35, $"trouble may come on about a third of mornings ({days} of 60)");
        Eq(eligible, days, "on every such morning, an owned business is open to it");
        int day = Enumerable.Range(70, 40).First(d => { c.Day = d; return c.TroubleToday(); });
        c.Day = day;
        ulong rng = c.Rng.State;
        c.Send("biz-shakedown");
        Eq(c.Rng.State, rng, "trouble draws never touch the career's own random numbers");
        for (int d = day; d < day + Tables.BizGap; d++)
        {
            c.Day = d;
            Check(!c.Eligible(Hook.Morning).Any(s => kinds.Contains(s.Id)), $"no more trouble for {Tables.BizGap} days ({d})");
        }

        // The shakedown, paid: a quarter every week, and the holder likes it.
        c = BizCareer(city);
        int stand = c.StandingOf(holder);
        Mail m = c.Send("biz-shakedown");
        Check(m.Text.Contains(Tables.FactionNames[(int)holder]), "the shakedown comes from whoever holds the street");
        Check(c.Choose(m, 0) == null && c.CutOf("noodles") == holder, "paying them puts them on the payroll");
        var (take, line) = BizWeek(c, city);
        Eq(take, 3000, "they take 25 % of the week");
        Check(line.Contains("25 % to the") && line.Contains("standing +2"), $"and the payday says so ({line})");
        Eq(c.StandingOf(holder), stand + 2, "standing +2 a week instead of +1");
        Check(!c.Eligible(Hook.Morning).Any(s => s.Id == "biz-shakedown"), "nobody shakes down a business that already pays");

        // Talked down: a token cut.
        c = BizCareer(city);
        m = c.Send("biz-shakedown");
        Check(c.Choose(m, 2) != null, "talking them down needs their etiquette");
        c.Etiquettes = Career.AllEtiquettes.ToList();
        Check(c.Choose(m, 2) == null, "with it, they settle");
        Eq(BizWeek(c, city).take, 3600, "for 10 %");

        // Refused and held: a month's peace, and they like you less.
        c = BizCareer(city);
        c.Standing[holder] = 5;
        c.Choose(c.Send("biz-shakedown"), 1);
        Job j = c.Board.FirstOrDefault(b => b.Tag == "biz:noodles");
        Check(j != null && j.Type == JobType.Defend && j.Faction == holder && j.District == "docks" && j.Expires == c.Day + 2, "refusing puts a two-day defence of the business on the board");
        c.Finish(j, Won(c, alarm: true, kills: 4));
        Eq(c.StandingOf(holder), 3, "held, standing -2 with them");
        Check(c.Flag("bizsafe:noodles") >= c.Day + Tables.BizSafeDays - 1 && !c.Eligible(Hook.Morning).Any(s => s.Id == "biz-shakedown"), "and no shakedowns for a month");
        Eq(BizWeek(c, city).take, 4000, "and the takings are untouched");
        // Refused and lost: shut for a week.
        c = BizCareer(city);
        c.Choose(c.Send("biz-shakedown"), 1);
        c.Finish(c.Board.First(b => b.Tag == "biz:noodles"), Lost(c));
        Check(c.BizShut("noodles"), "lost, the business is shut");
        (take, line) = BizWeek(c, city);
        Check(take == 0 && line.Contains("shut 7 days"), $"and earns nothing that week ({line})");
        Eq(BizWeek(c, city).take, 4000, "then it reopens");
        // Refused and never went: the same.
        c = BizCareer(city);
        c.Choose(c.Send("biz-shakedown"), 1);
        for (int k = 0; k < 4 && c.Board.Any(b => b.Tag == "biz:noodles"); k++) c.NextDay(city);
        Check(!c.Board.Any(b => b.Tag == "biz:noodles") && c.BizShut("noodles"), "a defence you don't go to: they smash the place");

        // The inspector: a bribe now, or days shut.
        c = BizCareer(city);
        m = c.Send("biz-inspector");
        int cash = c.Cash;
        Check(c.Choose(m, 0) == null && c.Cash == cash - 1600, "the bribe is 40 % of a week");
        Eq(BizWeek(c, city).take, 4000, "paid, the week is whole");
        c = BizCareer(city);
        m = c.Send("biz-inspector");
        Check(c.Choose(m, 1) == null && c.BizShut("noodles"), "refused, he shuts the place");
        (take, line) = BizWeek(c, city);
        Check(take == 1700 && line.Contains("shut 4 days"), $"for four days of the week ({line})");
        c.Day += 3;
        Check(c.Flag("bizclean:noodles") > c.Day && !c.Eligible(Hook.Morning).Any(s => s.Id == "biz-inspector"), "and he doesn't come back for a month");
        c = BizCareer(city);
        c.Happenings.Add(new Happening { Id = "council", Start = c.Day, End = c.Day + 3 });
        c.Etiquettes = Career.AllEtiquettes.ToList();
        m = c.Send("biz-inspector");
        Check(m.Text.Contains("8 days"), "with the Lotus council in his pocket, refusing shuts it for longer");
        Check(c.Options(m)[2].block != null, "and nobody listens to the regulations");
        c.Choose(m, 1);
        Eq(BizWeek(c, city).take, 0, "shut the whole next week");
        Career k2 = BizCareer(city);
        k2.Etiquettes = Career.AllEtiquettes.ToList();
        Check(k2.Choose(k2.Send("biz-inspector"), 2) == null && !k2.BizShut("noodles"), "without the council, Security etiquette sees him off for nothing");

        // The hand in the till.
        c = BizCareer(city);
        c.Choose(c.Send("biz-skim"), 0);
        (take, line) = BizWeek(c, city);
        Check(take == 2000 && line.Contains("half takings"), $"fired: a week at half while you rehire ({line})");
        Eq(BizWeek(c, city).take, 4000, "then whole again");
        c = BizCareer(city);
        c.Etiquettes = Career.AllEtiquettes.ToList();
        cash = c.Cash;
        Check(c.Choose(c.Send("biz-skim"), 1) == null && c.Cash == cash + 1200, "a quiet word puts two weeks' skim back in the till");
        Eq(BizWeek(c, city).take, 4000, "and the till is honest");
        c = BizCareer(city);
        c.Choose(c.Send("biz-skim"), 2);
        (take, line) = BizWeek(c, city);
        Check(take == 3400 && line.Contains("-15 %"), $"let go: 15 % off every week ({line})");
        Check(!c.Eligible(Hook.Morning).Any(s => s.Id == "biz-skim"), "the same business isn't skimmed twice");
        // A week on, the skimmer owes you one: it comes with the payday's morning, or now.
        m = c.Inbox.FirstOrDefault(x => x.Story == "biz-skim-tip");
        if (m == null)
        {
            c.Day = c.Flag("bizskim:noodles") + Tables.BizSkimTip;
            Check(c.Eligible(Hook.Morning).Any(s => s.Id == "biz-skim-tip"), "a week on, the skimmer owes you one");
            m = c.Send("biz-skim-tip");
        }
        Check(c.Flag("freeschem") > 0, "and pays it in floor plans");
        Check(!c.Eligible(Hook.Morning).Any(s => s.Id == "biz-skim-tip"), "once");
        Check(c.Choose(m, 1) == null && c.Flag("bizskim:noodles") == 0, "then you can still let them go");
        Eq(BizWeek(c, city).take, 2000, "a week at half while you rehire");
        Eq(BizWeek(c, city).take, 4000, "and whole after");
        // Saves keep it all.
        c.Choose(c.Send("biz-shakedown"), 0);
        Eq(Career.FromJson(c.ToJson()).CutOf("noodles"), holder, "a cut survives a save");
    }
}
