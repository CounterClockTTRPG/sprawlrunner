using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// How each contact's own story ends (their way, or yours), contacts in
/// danger when your enemies hold their street, and the three-night heist.
/// </summary>
public static partial class Storylets
{
    private static Choice Theirs(string who, string text, string reply) => new(text, (c, m) => { c.ArcReward(who); return reply; });

    private static IEnumerable<Storylet> Forks()
    {
        yield return new("fork-doc", Hook.Debrief, Doc, "The headhunter's files", "Ichikawa: 'The headhunter carried files. Everything Halcyon knows about me -- and about a dozen other runaways. Sorensen-Aoki would pay a fortune for them. I want them burned. It's your call; you took them.'",
            c => false, new[]
            {
                Theirs("doc", "Burn them -- and take the booster", "Ichikawa watches them burn, then opens a case. 'The best chrome I ever made. Reaction +2.'"),
                new("Sell them to Sorensen-Aoki (¥10,000)", (c, m) => { c.Set("arc:doc", 2); c.Cash += 10000; c.Count("earned", 10000); c.Infamy(1); c.Loyalty["doc"] = Math.Max(0, c.Loyalty.GetValueOrDefault("doc") - 1); return "¥10,000. Ichikawa doesn't say anything. Nothing needs saying. Notoriety +1, trust -1."; }),
            }, Call: true);
        yield return new("fork-arms", Hook.Debrief, Sal, "Thunder", "Big Sal, holding the LMG he built for himself: 'She's yours if you want her. Or -- leave her here, on the wall, and I'll see you right every week for as long as I'm in business.'",
            c => false, new[]
            {
                Theirs("arms", "Take Sal's Thunder", "'Treat her right.' The one-of-a-kind LMG is yours."),
                new("Leave her on the wall", (c, m) => { c.Set("arc:arms", 2); return "Sal grins. 'A crate every week, then.' Frags, flashbangs, whatever came in."; }),
            }, Call: true);
        yield return new("fork-rigger", Hook.Debrief, Torque, "The Interceptor", "Torque, practically vibrating: 'She's done. The Halcyon engine in a body I built. You can drive her -- or I race her, every weekend, and you never pay for a repair again.'",
            c => false, new[]
            {
                Theirs("rigger", "Drive her", "Torque hands over the keys like they're glass. The Interceptor is yours."),
                new("Let him race her", (c, m) => { c.Set("arc:rigger", 2); c.Cash += 8000; c.Count("earned", 8000); return "First race, first place: ¥8,000 for you. And Torque fixes anything you drive, free, for good."; }),
            }, Call: true);
        yield return new("fork-decker", Hook.Debrief, Null, "Ghost protocol", "Null: 'Ghost protocol works. Cameras won't see you, ICE won't keep up. I wrote it for me. I can give it to you, or I can sell it -- every corp in town would bid, and I'd split it with you.'",
            c => false, new[]
            {
                Theirs("decker", "I'll take it", "Null: 'Don't make me regret it.' Ghost protocol is yours."),
                new("Sell it, split it (¥12,000)", (c, m) => { c.Set("arc:decker", 2); c.Cash += 12000; c.Count("earned", 12000); return "Three corps bid. Null's half buys a new rig; yours is ¥12,000. Every security system in the city gets a little better."; }),
            }, Call: true);
        yield return new("fork-fence", Hook.Debrief, Wen, "The copy", "Old Wen, holding the copy of his ledger: 'Every page is a life. I should burn it. But it is also leverage on half the Barrens... You brought it back. You decide.'",
            c => false, new[]
            {
                Theirs("fence", "Burn it", "It burns. Wen breathes for the first time in years. ¥5,000 'for your trouble', and he pays full value for anything you bring him, for life."),
                new("Keep it -- and use it", (c, m) => { c.Set("arc:fence", 2); c.Infamy(2); c.Loyalty["fence"] = Math.Max(0, c.Loyalty.GetValueOrDefault("fence") - 2); return "You keep the copy. The pages start paying: ¥1,500 a week from people who would rather not be in it. Wen won't look at you. Notoriety +2, trust -2."; }),
            }, Call: true);
        yield return new("fork-talismonger", Hook.Debrief, Sabine, "The circle", "Sabine, very still: 'The circle is broken -- but not dead. I can seal it for ever. Or you can draw on it. It will make your magic terrible, darling. It will also take a piece of you.'",
            c => false, new[]
            {
                Theirs("talismonger", "Seal it", "The circle closes. Sabine gives you her ward: +2 armour, and spells +10 %."),
                new("Draw on it", (c, m) => { c.Set("arc:talismonger", 2); c.Infamy(1); c.Loyalty["talismonger"] = Math.Max(0, c.Loyalty.GetValueOrDefault("talismonger") - 2); return "Something cold settles behind your eyes. Spells +25 % -- and half a point of essence gone. Sabine doesn't say goodbye. Notoriety +1, trust -2."; }),
            }, Call: true);

        yield return new("fork-cop", Hook.Debrief, Dace, "The contract", "Sgt. Dace, turning the chip over: 'I can take this to the federal inspector and burn half the precinct, me included, probably. Or Halcyon would pay a fortune to have it back -- and I could retire. You got it. You choose.'",
            c => false, new[]
            {
                Theirs("cop", "Burn the precinct", "Dace takes it to the inspector. Three captains are suspended; Dace keeps his badge, somehow, and gives you a copy of it. Lone Star cars never pull you over again."),
                new("Sell it back to Halcyon (¥15,000)", (c, m) => { c.Set("arc:cop", 2); c.Cash += 15000; c.Count("earned", 15000); c.Infamy(2); c.Tick("lonestar", 3); c.Loyalty["cop"] = Math.Max(0, c.Loyalty.GetValueOrDefault("cop") - 2); return "Halcyon pays ¥15,000 and buys two more precincts. Dace stops returning calls for a while. Notoriety +2, trust -2."; }),
            }, Call: true);
        yield return new("fork-press", Hook.Debrief, Quill, "The story", "Mara Quill, at four in the morning: 'It runs tomorrow if I say so. Halcyon's lawyers have already called -- they'll pay us both to bury it. A lot. I'll do what you say. You took the risks.'",
            c => false, new[]
            {
                Theirs("press", "Run it", "It leads every bulletin for a week. Quill's front page calls the runner who helped 'a public servant'. The street forgets what you did; fame fades from you twice as fast."),
                new("Bury it (¥15,000)", (c, m) => { c.Set("arc:press", 2); c.Cash += 15000; c.Count("earned", 15000); c.Infamy(2); c.Loyalty["press"] = Math.Max(0, c.Loyalty.GetValueOrDefault("press") - 2); return "The story dies. ¥15,000 for you. Quill takes her half and doesn't write anything for a month. Notoriety +2, trust -2."; }),
            }, Call: true);

        // What the other endings pay, week by week.
        yield return new("sal-crate", Hook.Morning, Sal, "This week's crate", "Sal: 'Crate's in. Your share's by the door.' Two frags and a flashbang.",
            c => c.ArcPath("arms") == 2, Cooldown: 7, Fire: (c, m) =>
            {
                c.Grenades["frag"] = Math.Min(c.GrenadeCap, c.Grenades.GetValueOrDefault("frag") + 2);
                c.Grenades["flash"] = Math.Min(c.GrenadeCap, c.Grenades.GetValueOrDefault("flash") + 1);
            });
        yield return new("ledger-money", Hook.Morning, "The ledger", "This week's pages", "Three envelopes under your door: ¥1,500 from people who would rather stay out of Wen's ledger.",
            c => c.ArcPath("fence") == 2, Cooldown: 7, Fire: (c, m) => { c.Cash += 1500; c.Count("earned", 1500); });
    }

    // ---- contacts in danger -----------------------------------------------------------------

    private static IEnumerable<Storylet> Danger()
    {
        foreach (ContactDef k in Tables.Contacts)
        {
            ContactDef kk = k;
            yield return new($"danger-{k.Id}", Hook.Morning, k.Name, "They're outside", "{text}",
                c => c.CityRef != null && !c.Gone(kk.Id) && !c.ClosedNow(kk.Id) && c.Loyalty.GetValueOrDefault(kk.Id) >= 1
                    && c.Grudge.GetValueOrDefault(c.Holder(kk.District)) >= Career.DangerGrudge && c.Day - c.Flag("dangerday:" + kk.Id) > 20 && c.Day >= 10,
                new[]
                {
                    Fx(new Choice("On my way", (c, m) =>
                    {
                        Faction f = c.Holder(kk.District);
                        // At their own door: the site is their place.
                        int at = c.CityRef?.Place(kk.Id)?.Index ?? -1;
                        Job j = c.OfferSpecial(JobType.Defend, Career.VenueStyle(kk.Id), f, kk.District, Math.Min(5, Math.Max(2, c.MaxThreat)),
                            $"Protect {kk.Name}", $"{Tables.FactionNames[(int)f]} are coming for {kk.Venue} because of you. Hold the place until they give up. Get there by the end of day {c.Day + 1}.", employer: kk.Id, tag: "danger:" + kk.Id, expires: 1, payMul: 0.6f, lot: at);
                        if (j == null) { c.HurtContact(kk.Id); return $"You get there too late. {kk.Name} is already in hospital."; }
                        // Drop everything: it's your job now, and the map points at their door.
                        c.Urgent(j);
                        return $"It's your job now -- the map points at {kk.Venue}. Get there by the end of day {j.Expires}.";
                    }, (c, m) => c.Courier ? "Not with a package in the car: deliver it first (or let it go)." : null), "a defend job at {venue}, yours right away (any job you're on waits); win and they owe you"),
                    Fx(new Choice("I can't", (c, m) => { c.HurtContact(kk.Id); return $"You don't go. In the morning {kk.Venue} is a burned-out shell."; }), "{venue} shut for a week, trust -1{last}"),
                },
                Call: true,
                Bind: c =>
                {
                    Faction f = c.Holder(kk.District);
                    bool last = c.Flag("dangerfail:" + kk.Id) >= 1;
                    return V(("venue", kk.Venue),
                        ("last", last ? "; it already happened once -- this time they leave Kessler Bay for good" : "; a second time and they leave for good"),
                        ("text", $"{kk.Name}, very fast: 'The {Tables.FactionNames[(int)f]} are outside {kk.Venue}. They asked for you by name, and when I said you weren't here they said they'd wait. With petrol. Please.' "
                            + $"(The {Tables.FactionNames[(int)f]} hold the {Tables.District(kk.District).Name} and have a grudge of {c.Grudge.GetValueOrDefault(f)} against you.{(last ? $" They did this once before: if {kk.Venue} burns again, {kk.Name} is gone for good." : "")})"));
                },
                Fire: (c, m) => c.Set("dangerday:" + kk.Id, c.Day));
            yield return new($"saved-{k.Id}", Hook.Debrief, k.Name, "Still standing", $"{k.Name}: 'You came. Most people wouldn't have. I won't forget it.'",
                c => c.Last?.Job?.Tag == "danger:" + kk.Id && c.Last.Success, Fire: (c, m) => c.Befriend(kk.Id));
        }
    }

    // ---- penance ---------------------------------------------------------------------------------

    /// <summary>
    /// When your name shuts the clean contacts' doors (notoriety 7+), each
    /// offers a way back through someone else: unpaid work for the people they
    /// look after. Done, it takes 2 off your notoriety and wins back a point of
    /// their trust (see Career.Atone).
    /// </summary>
    private static IEnumerable<Storylet> Penance()
    {
        static bool Open(Career c, string who) => c.Notoriety >= Tables.AtoneFrom && c.CityRef != null && !c.Gone(who)
            && !c.Board.Any(b => b.Tag == "atone:" + who) && (c.Flag("atoned:" + who) == 0 || c.Day - c.Flag("atoned:" + who) >= 7);
        static int Threat(Career c) => Math.Clamp(c.MaxThreat, 2, 4);
        string effect = $"a job for expenses only; done, notoriety -{Tables.AtoneCut} and {{who}} trusts you a little again";

        yield return new("atone-doc", Hook.Morning, Juno, "Ichikawa's other clinic",
            "Juno: 'Ichikawa won't have your name said in her surgery. But she keeps a free clinic going in the Barrens out of her own pocket, and the {fac} lean on it for protection money. Tonight they're coming to collect. Hold the door -- for nothing -- and I'll make sure she hears who did.'",
            c => Open(c, "doc"),
            new[]
            {
                Fx(new Choice("I'll be there", (c, m) =>
                {
                    Faction f = (Faction)int.Parse(m.Vars["f"]);
                    Job j = c.OfferSpecial(JobType.Defend, SiteStyle.Lab, f, "barrens", Threat(c), "Penance: the free clinic",
                        $"Ichikawa's free clinic in the Barrens. The {Tables.FactionNames[(int)f]} are coming for their money tonight. Hold the place until they give up. Nobody's paying you; that's the point.", tag: "atone:doc", payMul: 0.25f, expires: 3);
                    return j == null ? "Juno: 'They moved the clinic. I'll let you know.'" : "It's on your board. Get there in the next three days.";
                }), effect),
                Ok("Not tonight"),
            },
            Cooldown: 8,
            Bind: c => { Faction f = c.Holder("barrens") == Faction.Halcyon ? Faction.Vipers : c.Holder("barrens"); return V(("f", ((int)f).ToString()), ("fac", Tables.FactionNames[(int)f]), ("who", Doc)); });

        yield return new("atone-sabine", Hook.Morning, Sabine, "A note under the door",
            "A note in Madame Sabine's hand, pushed under your door: 'Something hungry has nested under the Docks fish market. It eats the people nobody counts. I will not see you. But if it were gone, I would know who did it -- and the spirits would stop saying your name the way they do.'",
            c => Open(c, "talismonger"),
            new[]
            {
                Fx(new Choice("Go down there", (c, m) =>
                {
                    Job j = c.OfferSpecial(JobType.Bounty, SiteStyle.Warehouse, Faction.Vipers, "docks", Threat(c), "Penance: the nest under the market",
                        "Under the Docks fish market something has been feeding on the people nobody counts. Clear the nest. Nobody's paying you; Sabine will know.", tag: "atone:talismonger", payMul: 0.25f, expires: 3);
                    return j == null ? "By the time you look, the market's been bricked up. Another time." : "It's on your board. Go in the next three days.";
                }), effect),
                Ok("Not now"),
            },
            Cooldown: 8,
            Bind: c => V(("who", Sabine)));

        yield return new("atoned-doc", Hook.Debrief, Doc, "A voice message", "Ichikawa, short and tired: 'I heard about the clinic. I don't forgive easily. I'm starting.'",
            c => c.Last?.Job?.Tag == "atone:doc" && c.Last.Success, Weight: 50);
        yield return new("atoned-sabine", Hook.Debrief, Sabine, "Quieter", "Sabine: 'The spirits have stopped spitting when they say your name, darling. Only a little. Come by, if you like.'",
            c => c.Last?.Job?.Tag == "atone:talismonger" && c.Last.Success, Weight: 50);
    }

    // ---- the heist ----------------------------------------------------------------------------

    private static IEnumerable<Storylet> Heists()
    {
        yield return new("heist", Hook.Morning, Juno, "The big score", "Juno, leaning in: 'There is a Halcyon vault in the {district} with a year of bearer bonds in it. Three nights: case the place, kill the security grid, then the vault. Get caught at any point and it's off. Interested?'",
            c => c.Rep >= 6 && c.Day >= 10 && c.CityRef != null && c.Flag("heist:stage") == 0 && c.ActiveJob < 0,
            new[]
            {
                new Choice("I'm in", (c, m) =>
                {
                    string d = m.Vars["district"] == "Spire" ? "spire" : "industrial";
                    var taken = new HashSet<int>(c.Board.Select(b => b.Lot));
                    int lot = c.CityRef.FreeSite(d, c.Rng, taken);
                    if (lot < 0) return "Juno: 'The vault moved. Another time.'";
                    c.Set("heist:lot", lot + 1);
                    c.Set("heist:stage", 1);
                    c.OfferSpecial(JobType.Investigate, SiteStyle.Office, Faction.Halcyon, d, Math.Min(5, c.MaxThreat), "Heist 1/3: case the vault",
                        "Juno: 'Night one. Get in, find the guard rota, the vault specs and the grid's maintenance codes, and get out. Nobody can know you were there.'", tag: "heist:1", lot: lot, payMul: 0.8f, expires: 6);
                    return "Night one is on your board.";
                }),
                Ok("Not my kind of job"),
            },
            Bind: c => V(("district", c.MaxThreat >= 4 ? "Spire" : "Industrial district")), Cooldown: 18);
        yield return new("heist-2", Hook.Debrief, Juno, "Night two", "Juno: 'You've got the codes. Now the grid: their security mainframe is in the same building. Jack in and kill it -- tomorrow, the vault is dark.'",
            c => c.Last?.Job?.Tag == "heist:1" && c.Last.Success,
            Fire: (c, m) =>
            {
                c.Set("heist:stage", 2);
                Job last = c.Last.Job;
                c.OfferSpecial(JobType.Datasteal, SiteStyle.Office, Faction.Halcyon, last.District, last.Threat, "Heist 2/3: kill the grid",
                    "Juno: 'Night two. The mainframe runs their cameras and the vault's locks. Pull its brain out and the vault opens dark.'", tag: "heist:2", lot: c.Flag("heist:lot") - 1, payMul: 1f, expires: 6);
            });
        yield return new("heist-3", Hook.Debrief, Juno, "Night three", "Juno: 'The grid is down. Cameras dead, locks dead, half the guards sent home because the building is \"under maintenance\". The vault. Tonight.'",
            c => c.Last?.Job?.Tag == "heist:2" && c.Last.Success,
            Fire: (c, m) =>
            {
                c.Set("heist:stage", 3);
                Job last = c.Last.Job;
                Job j = c.OfferSpecial(JobType.Retrieve, SiteStyle.Office, Faction.Halcyon, last.District, last.Threat, "Heist 3/3: the vault",
                    "Juno: 'Night three. Bearer bonds, a year of them, in the vault. You know the building better than they do now. Walk in, walk out rich.'", tag: "heist:3", lot: c.Flag("heist:lot") - 1, payMul: 5f, item: "vault of bearer bonds", expires: 6);
                if (j != null) { j.Schematics = true; j.Mods.Add(RunMod.Blackout); j.Mods.Add(RunMod.Skeleton); }
            });
        yield return new("heist-done", Hook.Debrief, Juno, "Rich", "Juno raises a glass: 'The biggest score this city has seen in a decade, and nobody knows who did it. KBN is calling it an inside job. Drink.'",
            c => c.Last?.Job?.Tag == "heist:3" && c.Last.Success, Fire: (c, m) => { c.Set("heist:stage", 0); c.Inc("heists"); });
        yield return new("heist-off", Hook.Debrief, Juno, "It's off", "Juno: 'That's blown. They'll change every code and double the guards. The vault is gone for us. Lie low.'",
            c => c.Last?.Job?.Tag is "heist:1" or "heist:2" or "heist:3" && !c.Last.Success, Fire: (c, m) => c.Set("heist:stage", 0));
    }
}
