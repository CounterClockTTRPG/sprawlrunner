using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// Visitors at the safehouse: quiet scenes as you walk in, at most one every
/// few days -- mostly the first time you come home after a job. Who knocks
/// depends on how you live: the squat across the landing, the man in 4B, the
/// concierge. A stray cat, Juno after a bad deal, a crew member after a bad
/// job, and, when the street knows your face, a stranger with a warning.
/// </summary>
public static partial class Storylets
{
    /// <summary>A job has been settled since the last visitor came.</summary>
    private static bool AfterJob(Career c) => c.Last?.Job is Job j && c.Flag("home:job") != j.Id + 1;

    /// <summary>Room for a visitor: not before day 5, not while the landlady waits, one every few days -- after a job, or after a quiet spell.</summary>
    private static bool Visiting(Career c)
    {
        int since = c.Day - c.Flag("home:visit");
        return c.Day >= 5 && c.RentOwed == 0 && since >= Tables.VisitGap && (AfterJob(c) || since >= Tables.VisitQuiet);
    }

    private static void Visited(Career c, Mail m)
    {
        c.Set("home:visit", c.Day);
        if (c.Last?.Job is Job j) c.Set("home:job", j.Id + 1);
    }

    /// <summary>The last job went badly: lost, loud, or a lot of bodies.</summary>
    private static bool BadJob(Career c) => c.Last is LastRun l && (!l.Success || l.Alarm || l.Kills >= 8);

    private static IEnumerable<Storylet> Visitors()
    {
        // ---- the cat ----
        yield return new("home-cat", Hook.Place, "A stray", "Company", "{text}",
            c => Visiting(c) && c.Flag("cat") == 0,
            new[]
            {
                Fx(Pay($"Keep it ({M.Money(Tables.CatFood)} for food and a bowl)", Tables.CatFood, (c, m) =>
                {
                    c.Set("cat", c.Day);
                    return $"It eats, washes, and goes to sleep on your jacket as if it had always lived here. It's staying. (-{M.Money(Tables.CatFood)}; karma +{Tables.CatKarma} every week while it does.)";
                }), $"-{M.Money(Tables.CatFood)}, karma +{Tables.CatKarma} a week"),
                Ok("Shoo it out", "It goes without hurrying, and sits on the fire escape for an hour, looking in. Then it's gone, and the place is as quiet as it was."),
            },
            Place: "home", Fire: Visited,
            Bind: c => V(("text", c.LifeTier switch
            {
                0 => "Something is moving in the pile of blankets you call a bed: a scrawny grey cat with a torn ear, in through the broken window. It looks at you as if you're the one trespassing.",
                1 => "A scrawny grey cat with a torn ear is sitting on your windowsill in the rain, looking at the radiator like it pays rent.",
                2 => "There's a grey cat in your kitchen. Nobody knows how -- the door locks properly. It has one torn ear and opinions about your fridge.",
                3 => "The doorman is apologetic: a grey stray with a torn ear got past him and is asleep on your sofa. 'Shall I have it removed?'",
                _ => "Building security has flagged an intruder on your balcony, forty floors up: a grey cat with a torn ear. Nobody can explain it. It is eating the orchid.",
            })));

        yield return new("home-cat-lost", Hook.Place, "The cat", "An empty bowl", "The cat's bowl hasn't been touched for two days. No grey shape on the windowsill, no complaints at the door at four in the morning.",
            c => Visiting(c) && c.Flag("cat") > 0 && c.Day - c.Flag("cat") >= Tables.CatLostAfter,
            new[]
            {
                Fx(new Choice("Walk the block calling for it", (c, m) =>
                {
                    if (new Rng(c.Seed ^ Career.Stable("cat-search" + c.Day)).Chance(0.6f))
                        return "Two hours of rattling a tin of fish, and there it is, under the stairs three doors down, looking unimpressed. It lets you carry it home. (The cat stays.)";
                    c.Set("cat", 0);
                    return "You walk until your voice goes. Nothing. The bowl stays by the door for a week before you put it away. (The cat is gone: no more karma from it.)";
                }), "a chance to find it"),
                Fx(Pay($"Pay the kids on the block to look ({M.Money(Tables.CatSearch)})", Tables.CatSearch, (c, m) =>
                    $"Within the hour a kid hands it over, filthy and furious, and holds out a hand for the rest. (-{M.Money(Tables.CatSearch)}; the cat stays.)"), $"-{M.Money(Tables.CatSearch)}, the cat comes back"),
                Fx(new Choice("Let it go", (c, m) => { c.Set("cat", 0); return "Cats leave. You put the bowl away. (The cat is gone: no more karma from it.)"; }), "the cat is gone"),
            },
            Place: "home", Fire: Visited);

        // ---- Juno, after a bad deal ----
        yield return new("home-juno", Hook.Place, Juno, "Three in the morning", "{text}",
            c => Visiting(c) && c.Day >= 8 && c.Loyalty.GetValueOrDefault("fixer") >= 1 && c.Shut("fixer") == null,
            new[]
            {
                Fx(new Choice("Let her crash, and listen", (c, m) =>
                {
                    c.Befriend("fixer");
                    int bottle = (int)Math.Min(c.Cash, Tables.JunoBottle);
                    c.Cash -= bottle;
                    c.Count("spent", bottle);
                    Job tip = JunoTip(c);
                    string tipped = tip != null
                        ? $"'There's a job that Johnson didn't give anyone. Pays near double. Take it before he remembers.' It's on your board: {tip.Title}, {M.Money(tip.Pay)}."
                        : "'Next place they send you, there's a back way in. I'll send you the plans.' The floor plans of your next job are yours.";
                    if (tip == null) c.Set("freeschem");
                    return $"She talks until the windows go grey -- about the bar, her sister, the deal. And at some point: {tipped} Juno trust +1. She finished your good bottle (-{M.Money(bottle)}).";
                }), $"Juno trust +1, a tip, -{M.Money(Tables.JunoBottle)}"),
                Ok("Put her in a cab", "'You're a good kid,' she says, and means you're not. The cab takes her. You sleep. In the morning neither of you mentions it."),
            },
            Cooldown: 30, Place: "home", Fire: Visited,
            Bind: c => V(("text", (c.LifeTier >= 3 ? "The doorman calls up at three in the morning: a woman in the lobby who says she knows you and won't leave. " : "Someone is leaning on your door at three in the morning. ")
                + "It's Juno, very drunk, the shoulder of her good coat torn. 'Deal went bad. Johnson's bodyguard pulled a gun on me. In my own bar. In my own bar.' She sways. 'Can I sit down?'")));

        // ---- crew who don't want to go home ----
        var couch = new (string id, string text)[]
        {
            ("ox", "Ox is on your doorstep, filling it. 'That one went sideways. I don't want to go back to my place and stare at the wall. Your couch. One night.'"),
            ("wisp", "Wisp is sitting on your stairs with her rifle case across her knees. 'I keep seeing the angles. The ones I missed.' She doesn't look up. 'Can I stay? I'll be quiet.'"),
            ("patch", "Patch is at your door, still in the bloody scrubs. 'I can't go home like this. My neighbours think I work in a hospital. Can I use your shower? And your couch?'"),
            ("ade", "Ade is waiting outside, hugging herself. 'Ichikawa will ask how it went. I can't lie to her face tonight. Can I sleep here?'"),
            ("byte", "Byte is at your door with a backpack and a very straight face. 'My place has windows. Lots of windows. Can I sleep on your floor? Just tonight. Just until my hands stop.'"),
        };
        foreach (var (id, text) in couch)
        {
            string hid = id;
            string name = Tables.Hires.First(h => h.id == hid).name;
            yield return new($"home-couch-{hid}", Hook.Place, name, "Your couch", text,
                c => Visiting(c) && AfterJob(c) && c.Last.Job.Hired == hid && BadJob(c) && c.CrewTrust(hid) >= 2 && !c.CrewOf(hid).Dead,
                new[]
                {
                    Fx(new Choice("'Couch is yours'", (c, m) =>
                    {
                        CrewState s = c.CrewOf(hid);
                        s.Trust = Math.Min(5, s.Trust + 1);
                        s.OutUntil = Math.Max(s.OutUntil, c.Day + 1);
                        return $"{name} sleeps for fourteen hours and eats everything in the place. Trust +1; {name} sits out tomorrow.";
                    }), "trust +1, sits out tomorrow"),
                    Ok("'Not tonight'", $"{name} nods. 'Sure.' They go. They'll be ready in the morning, and a little further away."),
                },
                Weight: 2, Cooldown: 12, Place: "home", Fire: Visited);
        }

        // ---- the neighbours, by how you live ----
        yield return new("home-kid", Hook.Place, "The girl across the landing", "Medicine", "A girl of about ten from {where} is at your door. 'My brother's burning up. The clinic wants {¥price} for the pills. You've got money -- everybody knows you've got money.'",
            c => Visiting(c) && c.LifeTier <= 1,
            new[]
            {
                Fx(Pay($"Pay for the pills ({M.Money(Tables.KidMedicine)})", Tables.KidMedicine, (c, m) => { c.Set("home:kid", c.Day); return $"She runs. Two days later her brother is on the stairs again, thin and grinning. 'We watch your door now. Free.' (-{M.Money(Tables.KidMedicine)}; the kids owe you.)"; }),
                    $"-{M.Money(Tables.KidMedicine)}, a lookout favour later"),
                Fx(new Choice("Give her one of your medkits", (c, m) => { c.Medkits--; c.Set("home:kid", c.Day); return "You show her how the injector works. She's back the next day to say it worked. (-1 medkit; the kids owe you.)"; },
                    (c, m) => c.Medkits < 1 ? "No medkit to give." : null), "-1 medkit, a lookout favour later"),
                Ok("Close the door", "You close the door. Through it you hear her go down the stairs, slowly."),
            },
            Place: "home", Fire: Visited,
            Bind: c => V(("where", c.LifeTier == 0 ? "the squat across the landing" : "the flat below yours"), ("price", Tables.KidMedicine.ToString())));

        yield return new("home-kid-favour", Hook.Morning, "The kids across the landing", "Wrong building", "A note under your door, in crayon: 'TWO MEN IN GOOD SHOES ASKED WHICH DOOR IS YOURS. WE SENT THEM TO THE WRONG BUILDING. THEN THE WRONG DISTRICT.' Whoever was waiting for you isn't any more. (Awareness -1.)",
            c => c.Flag("home:kid") > 0 && c.Day - c.Flag("home:kid") >= 2,
            Fire: (c, m) => { c.Set("hitters", 0); c.Set("vendetta", 0); c.Fame(-1); });

        yield return new("home-noise", Hook.Place, "The man from 4B", "Some of us work", "The man from 4B is waiting by the lift in his dressing gown. 'Some of us work in the morning. The door at four a.m., the shouting -- was that a gunshot? I've written it all down. I can write it down for Lone Star, too.'",
            c => Visiting(c) && AfterJob(c) && c.LifeTier == 2 && c.Last.Alarm,
            new[]
            {
                Pay($"Apologise, and pay for his door ({M.Money(Tables.NoiseDoor)})", Tables.NoiseDoor, (c, m) => $"He folds the transfer slip twice. 'Well. As long as it doesn't happen again.' It will. (-{M.Money(Tables.NoiseDoor)}.)"),
                Talk("Corporate", "Talk to him like the building's management would", (c, m) => "Ten minutes of liability, insurance and 'regrettable' and he is apologising to you."),
                Fx(new Choice("Brush him off", (c, m) => { c.Fame(1); c.Heat = MathF.Max(c.Heat, 1); return "He writes it down for Lone Star, in triplicate. Awareness +1, and a patrol car slows past your building (heat)."; }), "awareness +1, heat"),
            },
            Cooldown: 10, Place: "home", Fire: Visited);

        yield return new("home-concierge", Hook.Place, "{who}", "Your coat", "{who} holds the lift for you and looks at your coat. There's blood on it, and {who2} is the kind of person who notices whose. 'Building policy is to report incidents to the residents' committee. Building policy is... flexible.'",
            c => Visiting(c) && AfterJob(c) && c.LifeTier >= 3 && (c.Last.Kills > 0 || c.Last.Alarm || !c.Last.Success),
            new[]
            {
                new Choice("Pay for discretion ({¥price})", (c, m) =>
                {
                    int p = Money(m, "price");
                    c.Cash -= p;
                    c.Count("spent", p);
                    return $"The coat goes to be cleaned and your face is forgotten. Nobody hears a thing. (-{M.Money(p)}.)";
                }, (c, m) => c.Cash < Money(m, "price") ? $"Needs {M.Money(Money(m, "price"))}." : null, "-{¥price}"),
                Fx(new Choice("Tell them to mind their business", (c, m) => { c.Fame(2); return "They mind it -- by telling the residents' committee, who tell everyone they know. Awareness +2."; }), "awareness +2"),
            },
            Cooldown: 10, Place: "home", Fire: Visited,
            Bind: c => c.LifeTier >= 4
                ? V(("who", "The head of building security"), ("who2", "she"), ("price", Tables.DiscretionLuxury.ToString()))
                : V(("who", "The night concierge"), ("who2", "he"), ("price", Tables.DiscretionHigh.ToString())));

        // ---- someone who knows where you live ----
        yield return new("home-knock", Hook.Place, "{who}", "A knock at the door", "{who} knocks twice, softly, and doesn't come in. 'People have been asking for you by name. The kind who bring their own bags. They know this door. Tonight, tomorrow -- after your next job, for sure.'",
            c => c.Day >= 5 && c.RentOwed == 0 && c.Day - c.Flag("home:visit") >= Tables.VisitGap && (c.Heat >= 2 || c.Notoriety >= 6),
            new[]
            {
                Fx(new Choice("Move out tonight ({¥move})", (c, m) =>
                {
                    int p = Money(m, "move");
                    c.Cash -= p;
                    c.Count("spent", p);
                    c.Set("hitters", 0);
                    c.Set("vendetta", 0);
                    c.Set("raided", c.Day);
                    c.Fame(-1);
                    return $"You're gone before midnight: a new door, the same rent, and a week's rent lost on the old one (-{M.Money(p)}). Whoever comes finds an empty room. Awareness -1.";
                }, (c, m) => c.Cash < Money(m, "move") ? $"Needs {M.Money(Money(m, "move"))}." : null), "-a week's rent, nobody waiting, awareness -1"),
                Fx(new Choice("Set a trap", (c, m) =>
                {
                    Job j = c.OfferRaid();
                    if (j == null) { c.Set("hitters", 0); return "You wait up all night with the lights off. Nobody comes -- or they saw you waiting."; }
                    j.Threat = Math.Max(1, j.Threat - 1);
                    j.Title = "TRAP: your safehouse";
                    j.Brief = $"{Tables.FactionNames[(int)j.Faction]} hitters are coming to your door tonight, and you know it. Be waiting. Miss it, and they take a third of everything you have.";
                    c.ActiveJob = j.Id;
                    c.BringRetained(j);
                    c.Set("hitters", 0);
                    c.Set("raided", c.Day);
                    return "They'll come tonight, and you'll be waiting with the lights off. It's your active job: hold the safehouse. Sleep through it and they take a third of your cash.";
                }, (c, m) => c.ActiveJob >= 0 ? "Not with a job on." : null), "a defence at home tonight"),
                Fx(new Choice("Pay them to point the hitters elsewhere ({¥price})", (c, m) =>
                {
                    int p = Money(m, "price");
                    c.Cash -= p;
                    c.Count("spent", p);
                    c.Set("hitters", 0);
                    c.Set("vendetta", 0);
                    return $"Money changes hands. Somewhere across the city, a door that isn't yours gets a visit. (-{M.Money(p)}; nobody is waiting for you.)";
                }, (c, m) => c.Cash < Money(m, "price") ? $"Needs {M.Money(Money(m, "price"))}." : null), "-{¥price}, nobody waiting"),
                Fx(Ok("Thank them and lock the door", "You lock it twice. Somebody will be waiting after your next job -- at least you know."), "hitters after your next job"),
            },
            Weight: 2, Cooldown: 10, Place: "home",
            Fire: (c, m) => { Visited(c, m); c.Set("hitters", 1); },
            Bind: c => V(
                ("who", c.LifeTier switch { 0 => "The old man from the ground floor", 1 => "The woman from the laundrette downstairs", 2 => "A courier who isn't one", 3 => "Your doorman", _ => "Your building's head of security" }),
                ("move", Math.Max(Tables.KnockPay / 5, c.RentDue).ToString()),
                ("price", (Tables.KnockPay + Tables.KnockPayPer * c.Notoriety).ToString())));
    }

    /// <summary>What Juno lets slip: a job nobody else was given, paying near double.</summary>
    private static Job JunoTip(Career c)
    {
        var r = new Rng(c.Seed ^ Career.Stable("juno-tip" + c.Day));
        var picks = new (JobType type, SiteStyle style, Faction f, string district, string what)[]
        {
            (JobType.Datasteal, SiteStyle.Lab, Faction.Halcyon, "industrial", "a Halcyon server nobody's watching"),
            (JobType.Retrieve, SiteStyle.Club, Faction.Lotus, "strip", "a Lotus strongbox"),
            (JobType.Destroy, SiteStyle.Warehouse, Faction.Vipers, "docks", "a Viper chop shop"),
        };
        var p = picks[(int)(r.Next() % (ulong)picks.Length)];
        return c.OfferSpecial(p.type, p.style, p.f, p.district, Math.Max(1, c.MaxThreat), $"Juno's tip: {p.what}",
            $"{Juno}, hoarse: 'The Johnson who pulled a gun on me had this lined up for someone else. Now it's yours. Don't tell him where you heard it.'", payMul: 1.8f, tag: "home:tip");
    }
}
