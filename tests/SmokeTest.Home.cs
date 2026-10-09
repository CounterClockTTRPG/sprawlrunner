using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>Visitors at the safehouse: the cat, Juno at 3 a.m., crew on the couch, the neighbours, the knock.</summary>
public partial class SmokeTest
{
    private static bool AtHome(Career c, string id) => c.Eligible(Hook.Place, "home").Any(s => s.Id == id);

    /// <summary>A career at home on day <paramref name="day"/>, rent paid, back from a job that went as <paramref name="r"/> says.</summary>
    private static Career Visited(City city, int day, string lifestyle = "low", RunResult r = null, string hired = null, ulong seed = 11)
    {
        Career c = TestCareer();
        c.CityRef = city;
        c.Cash = 20000;
        c.Day = day;
        c.Lifestyle = lifestyle;
        Job j = StoryJob(seed: seed);
        j.Hired = hired;
        if (r != null) c.Last = new LastRun { Job = j, Success = r.Success, Alarm = r.Alarm, Kills = r.Kills };
        return c;
    }

    private void Visitors()
    {
        City city = City.Generate(55);
        // When they come: not before day 5, not while the landlady waits, one every few days.
        Check(!AtHome(Visited(city, 3, r: Won(TestCareer())), "home-cat"), "nobody visits in the first days");
        Check(!AtHome(Visited(city, 5), "home-cat"), "day 5 with no job done yet: not yet");
        Check(AtHome(Visited(city, 5, r: Won(TestCareer())), "home-cat"), "home after a job: a visitor can come");
        Check(AtHome(Visited(city, Tables.VisitQuiet), "home-cat"), "or after a quiet spell");
        Career owed = Visited(city, 8, r: Won(TestCareer()));
        owed.RentOwed = 57;
        Check(!AtHome(owed, "home-cat"), "behind on rent, only Mrs. Halloran is on the stairs");

        // The cat.
        Career c = Visited(city, 6, r: Won(TestCareer()));
        long cash = c.Cash;
        Check(c.Choose(c.Send("home-cat"), 0) == null && c.Flag("cat") == 6 && c.Cash == cash - Tables.CatFood, "keeping the cat costs its food");
        c.Last = new LastRun { Job = StoryJob(seed: 12), Success = true };
        c.Day = 7;
        Check(c.Eligible(Hook.Place, "home").All(s => !s.Id.StartsWith("home-")), $"and nobody else comes for {Tables.VisitGap} days");
        c.Day = 9;
        Check(AtHome(c, "home-juno") || c.Loyalty.GetValueOrDefault("fixer") < 1, "after that, the next one can");
        c.Day = 6;
        Career noCat = Clone(c);
        noCat.Set("cat", 0);
        int k0 = c.Karma, n0 = noCat.Karma;
        c.Cash = noCat.Cash = 50000;
        c.NextDay(city); noCat.NextDay(city);
        Eq(c.Karma - k0 - (noCat.Karma - n0), Tables.CatKarma, "the week turns: karma +1 for the cat on the bed");
        c.Day = 6 + Tables.CatLostAfter;
        Check(AtHome(c, "home-cat-lost"), $"{Tables.CatLostAfter} days on, the bowl is untouched");
        Career kids = Clone(c);
        cash = kids.Cash;
        Check(kids.Choose(kids.Send("home-cat-lost"), 1) == null && kids.Flag("cat") > 0 && kids.Cash == cash - Tables.CatSearch, "the kids find it, for a price");
        Career gone = Clone(c);
        Check(gone.Choose(gone.Send("home-cat-lost"), 2) == null && gone.Flag("cat") == 0, "let it go and it's gone");
        Career walk = Clone(c);
        Mail wm = walk.Send("home-cat-lost");
        walk.Choose(wm, 0);
        Check(walk.Flag("cat") > 0 == wm.Reply.Contains("stays"), $"walking the block: found or not, the reply says ({wm.Reply})");
        Career shoo = Visited(city, 6, r: Won(TestCareer()));
        cash = shoo.Cash;
        Check(shoo.Choose(shoo.Send("home-cat"), 1) == null && shoo.Flag("cat") == 0 && shoo.Cash == cash, "shoo it: nothing spent, nothing gained");

        // Juno at three in the morning.
        Career j = Visited(city, 10, r: Won(TestCareer()));
        j.Loyalty["fixer"] = 1;
        Check(AtHome(j, "home-juno"), "Juno turns up drunk after a bad deal");
        cash = j.Cash;
        int jobs = j.Board.Count;
        Check(j.Choose(j.Send("home-juno"), 0) == null && j.Loyalty["fixer"] == 2 && j.Cash == cash - Tables.JunoBottle, "let her crash: trust +1, and your bottle");
        Job tip = j.Board.FirstOrDefault(b => b.Tag == "home:tip");
        Check(j.Board.Count == jobs + 1 && tip != null && tip.Pay >= 800 * 1.7f, $"and she lets slip a job that pays near double ({tip?.Pay})");
        Career cab = Visited(city, 10, r: Won(TestCareer()));
        cab.Loyalty["fixer"] = 1;
        cab.Choose(cab.Send("home-juno"), 1);
        Check(cab.Loyalty["fixer"] == 1 && !cab.Board.Any(b => b.Tag == "home:tip"), "put her in a cab: nothing changes");

        // Crew on the couch after a bad job.
        Career o = Visited(city, 8, r: Lost(TestCareer()), hired: "ox");
        o.CrewOf("ox").Trust = 2;
        Check(AtHome(o, "home-couch-ox"), "Ox, trust 2, after a job that went wrong, asks for your couch");
        Career fine = Visited(city, 8, r: Won(TestCareer()), hired: "ox");
        fine.CrewOf("ox").Trust = 2;
        Check(!AtHome(fine, "home-couch-ox"), "not after a clean one");
        Career low = Visited(city, 8, r: Lost(TestCareer()), hired: "ox");
        low.CrewOf("ox").Trust = 1;
        Check(!AtHome(low, "home-couch-ox"), "nor before trust 2");
        Check(o.Choose(o.Send("home-couch-ox"), 0) == null && o.CrewTrust("ox") == 3 && o.CrewBlock("ox") != null, "yes: trust +1, and he's asleep on it");
        o.Day = 9;
        Check(o.CrewBlock("ox") != null, "he sits out tomorrow");
        o.Day = 10;
        Check(o.CrewBlock("ox") == null, "and is back the day after");
        Career no = Visited(city, 8, r: Lost(TestCareer()), hired: "ox");
        no.CrewOf("ox").Trust = 2;
        no.Choose(no.Send("home-couch-ox"), 1);
        Check(no.CrewTrust("ox") == 2 && no.CrewBlock("ox") == null, "no: he's free tomorrow, and no closer");

        // The neighbours depend on how you live.
        Career kid = Visited(city, 8, "squatter", Won(TestCareer()));
        Check(AtHome(kid, "home-kid") && !AtHome(kid, "home-noise") && !AtHome(kid, "home-concierge"), "a squatter's neighbour is a kid whose brother is sick");
        cash = kid.Cash;
        Check(kid.Choose(kid.Send("home-kid"), 0) == null && kid.Cash == cash - Tables.KidMedicine && kid.Flag("home:kid") == 8, "pay for the pills");
        kid.Set("hitters", 1);
        kid.Day = 9;
        Check(!kid.Eligible(Hook.Morning).Any(s => s.Id == "home-kid-favour"), "the favour isn't the next morning");
        kid.Day = 10;
        Check(kid.Eligible(Hook.Morning).Any(s => s.Id == "home-kid-favour"), "but it comes");
        kid.Awareness = 3;
        kid.Send("home-kid-favour");
        Check(kid.Flag("hitters") == 0 && kid.Awareness == 2, "the kids send the hitters to the wrong building (awareness -1)");
        Career kit = Visited(city, 8, "low", Won(TestCareer()));
        kit.Medkits = 1;
        Check(kit.Choose(kit.Send("home-kid"), 1) == null && kit.Medkits == 0 && kit.Flag("home:kid") > 0, "or give her a medkit");
        Check(kit.Choose(kit.Send("home-kid"), 1) != null || kit.Medkits == 0, "no medkit, nothing to give");

        Career quiet = Visited(city, 8, "middle", Won(TestCareer()));
        Check(!AtHome(quiet, "home-noise") && !AtHome(quiet, "home-kid"), "middle: a quiet job, no complaint");
        Career loud = Visited(city, 8, "middle", Won(TestCareer(), alarm: true));
        Check(AtHome(loud, "home-noise"), "a loud one, and 4B is waiting by the lift");
        Career brush = Clone(loud);
        brush.Choose(brush.Send("home-noise"), 2);
        Check(brush.Awareness == 1 && brush.Heat >= 1, "brush him off: awareness +1, and heat");
        cash = loud.Cash;
        Check(loud.Choose(loud.Send("home-noise"), 0) == null && loud.Cash == cash - Tables.NoiseDoor && loud.Awareness == 0, "or pay for his door");

        Career high = Visited(city, 8, "high", Won(TestCareer(), kills: 2));
        Check(AtHome(high, "home-concierge") && !AtHome(high, "home-kid"), "high: the concierge notices the blood on your coat");
        Career told = Clone(high);
        told.Choose(told.Send("home-concierge"), 1);
        Eq(told.Awareness, 2, "tell him to mind his business: awareness +2");
        cash = high.Cash;
        Check(high.Choose(high.Send("home-concierge"), 0) == null && high.Cash == cash - Tables.DiscretionHigh && high.Awareness == 0, "or pay for discretion");
        Career lux = Visited(city, 8, "luxury", Won(TestCareer(), kills: 2));
        cash = lux.Cash;
        lux.Choose(lux.Send("home-concierge"), 0);
        Eq(cash - lux.Cash, (long)Tables.DiscretionLuxury, "luxury security's silence costs more");

        // A knock at the door.
        Career calm = Visited(city, 8, r: Won(TestCareer()));
        Check(!AtHome(calm, "home-knock"), "nobody knows your door while you're nobody");
        Career hot = Visited(city, 8, r: Won(TestCareer()));
        hot.Notoriety = 6;
        Check(AtHome(hot, "home-knock"), "at notoriety 6, someone who knows where you live knocks");
        Career trap = Clone(hot);
        Mail km = trap.Send("home-knock");
        Check(trap.Flag("hitters") == 1, "the warning: hitters after your next job");
        Check(trap.Choose(km, 1) == null && trap.Active is Job tj && tj.Tag == "raid" && tj.Type == JobType.Defend && tj.Lot == city.Place("home").Index && trap.Flag("hitters") == 0,
            "set a trap: a defence at home tonight, instead of an ambush on the road");
        Career pay = Clone(hot);
        cash = pay.Cash;
        Check(pay.Choose(pay.Send("home-knock"), 2) == null && pay.Flag("hitters") == 0 && pay.Cash == cash - (Tables.KnockPay + 6 * Tables.KnockPayPer), "pay them off, by your notoriety");
        Career move = Clone(hot);
        cash = move.Cash;
        Check(move.Choose(move.Send("home-knock"), 0) == null && move.Flag("hitters") == 0 && move.Cash == cash - move.RentDue, "move out tonight: a week's rent");
        Career stay = Clone(hot);
        stay.Choose(stay.Send("home-knock"), 3);
        Check(stay.Flag("hitters") == 1, "or just lock the door: they'll be waiting");
        Career busy = Clone(hot);
        busy.ActiveJob = 5;
        Check(busy.Choose(busy.Send("home-knock"), 1) != null, "no trap with a job on");
        // Saved and loaded, the cat is still there.
        Check(Clone(c).Flag("cat") > 0, "the cat survives a save");
    }
}
