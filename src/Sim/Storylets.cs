using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// Every storylet in the game, by what it answers: what became of the people
/// you met on jobs, choices after a run, double-crosses, your contacts' lives,
/// the people who only ever write, the city, your crew, the block, and the
/// story. Words in braces are filled in when the storylet fires.
/// </summary>
public static partial class Storylets
{
    private static Storylet[] _all;
    public static Storylet[] All => _all ??= Build().ToArray();
    public static Storylet ById(string id) => All.FirstOrDefault(s => s.Id == id);

    private static IEnumerable<Storylet> Build() =>
        Basics().Concat(Fates()).Concat(RunChoices()).Concat(Crosses()).Concat(ContactLives()).Concat(Writers())
            .Concat(World()).Concat(CrewStories()).Concat(Block()).Concat(Story()).Concat(Tide())
            .Concat(Forks()).Concat(Danger()).Concat(Heists()).Concat(Campaigns()).Concat(Night()).Concat(Pasts()).Concat(Backgrounds()).Concat(CrewHome()).Concat(Lamp()).Concat(Streets()).Concat(Rivals());

    // ---- shorthand ------------------------------------------------------------------------

    private static string Juno => Tables.Contact("fixer").Name;
    private static string Doc => Tables.Contact("doc").Name;
    private static string Sal => Tables.Contact("arms").Name;
    private static string Torque => Tables.Contact("rigger").Name;
    private static string Null => Tables.Contact("decker").Name;
    private static string Wen => Tables.Contact("fence").Name;
    private static string Sabine => Tables.Contact("talismonger").Name;
    public const string Quill = "Mara Quill", Dace = "Sgt. Dace", Vesper = "Vesper", Halloran = "Mrs. Halloran", Mama = "Mama Oyelaran", Pip = "Pip";

    private static Dictionary<string, string> V(params (string k, string v)[] kv) => kv.ToDictionary(x => x.k, x => x.v);

    /// <summary>A choice that needs money: blocked without it, pays it when taken.</summary>
    private static Choice Pay(string text, int price, Func<Career, Mail, string> then) =>
        new(text, (c, m) => { c.Cash -= price; c.Count("spent", price); return then(c, m); }, (c, m) => c.Cash < price ? $"Needs {M.Money(price)}." : null, price > 0 ? $"-{M.Money(price)}" : null);

    /// <summary>A choice with its numbers spelled out beside it.</summary>
    private static Choice Fx(Choice c, string effect) => c with { Effect = effect };

    /// <summary>A choice only someone with this past can take.</summary>
    private static Choice Past(string background, string text, Func<Career, Mail, string> then) =>
        new($"({Career.Backgrounds.First(b => b.id == background).name}) {text}", then, (c, m) => c.Background == background ? null : $"Only a {Career.Backgrounds.First(b => b.id == background).name.ToLower()} could say that.");

    /// <summary>A choice only someone who talks the code can take.</summary>
    private static Choice Talk(string etiquette, string text, Func<Career, Mail, string> then) =>
        new($"({etiquette}) {text}", then, (c, m) => c.Talks(etiquette) ? null : $"Needs the {etiquette} etiquette.");

    private static Choice Ok(string text = "OK", string reply = null) => new(text, (c, m) => reply);

    private static int Money(Mail m, string key) => int.Parse(m.Vars[key]);

    /// <summary>A name whose fate is <paramref name="fate"/>, settled at least <paramref name="days"/> ago, not yet followed up under <paramref name="done"/>.</summary>
    private static string FateOf(Career c, string fate, int days, string done) =>
        c.Flags.Where(kv => kv.Key.StartsWith("fate:") && kv.Value == Career.Fates[fate] && c.Day - c.Flag("fateday:" + kv.Key[5..]) >= days && c.Flag(done + ":" + kv.Key[5..]) == 0)
            .Select(kv => kv.Key[5..]).OrderBy(n => n).FirstOrDefault();

    /// <summary>A friend made (set to 1) at least four days ago who hasn't yet repaid it (2).</summary>
    private static string Friend(Career c) =>
        c.Flags.Where(kv => kv.Key.StartsWith("friend:") && kv.Value == 1 && c.Day - c.Flag("fateday:" + kv.Key[7..]) >= 4).Select(kv => kv.Key[7..]).OrderBy(n => n).FirstOrDefault();

    /// <summary>An ordinary job just settled: not the story, not a favour, not a storylet's own.</summary>
    private static bool Ordinary(Career c) => c.Last?.Job is Job j && j.Story == 0 && j.Arc == null && !j.Tutorial && j.Tag == null && j.Type is not (JobType.Courier or JobType.Race or JobType.Hub);

    /// <summary>About one ordinary job in three ends with a decision.</summary>
    private static bool Decides(Career c) => Ordinary(c) && c.Last.Success && !c.Last.Stiffed && new Rng(c.Last.Job.Seed ^ 0xDEC1DEUL).Chance(1 / 3f);

    // ---- basics ----------------------------------------------------------------------------

    private static IEnumerable<Storylet> Basics()
    {
        yield return new("welcome", Hook.Morning, Juno, "Your inbox", "Juno: 'You've got a mailbox now -- people will write. Some want an answer. Read it at the terminal (mail) or on your PDA (P). Calls ring when you're on the street. And if somebody stops you at a door, listen: in this town, that's half the job.'",
            c => c.Day >= 2, Weight: 50);
    }

    // ---- what became of them ---------------------------------------------------------------

    private static IEnumerable<Storylet> Fates()
    {
        yield return new("fled", Hook.Morning, "{name}", "You missed", "No signature, no trace on the relay. Just: 'You came for me and you missed. I know your face now. Sleep well, runner.'",
            c => FateOf(c, "fled", 2, "followed") != null,
            new[]
            {
                Pay("Have Juno find {name} first (¥1,000)", 1000, (c, m) =>
                {
                    string n = m.Vars["name"];
                    c.OfferSpecial(JobType.Eliminate, SiteStyle.Office, (Faction)int.Parse(m.Vars["fac"]), c.Last?.Job?.District ?? "strip", Math.Min(5, c.MaxThreat + 1),
                        $"Unfinished business: {n}", $"Juno: '{n} has gone to ground in a place with better locks, and more guards. They know you're coming. Make it the last time.'", tag: "revenge:" + n, target: n, payMul: 1.6f);
                    return $"Juno: 'Found {n}. It's on your board. They'll have friends this time.'";
                }),
                Fx(new Choice("Let them come", (c, m) => { c.Set("hitters", 1); return "You check the mirror more often. After your next job, somebody will be waiting."; }), "hitters after your next job"),
            },
            Bind: c => { string n = FateOf(c, "fled", 2, "followed"); return V(("name", n), ("fac", ((int)(c.Last?.Job?.Faction ?? Faction.Lotus)).ToString())); },
            Fire: (c, m) => c.Set("followed:" + m.Vars["name"]));

        yield return new("rescued", Hook.Morning, "{name}", "Thank you", "{text}",
            c => FateOf(c, "rescued", 3, "thanked") != null,
            Bind: c =>
            {
                string n = FateOf(c, "rescued", 3, "thanked");
                int gift = (int)(Career.Stable(n) % 3);
                string text = gift switch
                {
                    0 => $"A cred transfer, ¥1,500, and one line: 'For the door you opened. -- {n}'",
                    1 => $"A parcel left with Juno: two medkits and a note. 'I work at a clinic now. Come by if you're ever bleeding. -- {n}'",
                    _ => $"'I used to clean offices for the people who held me. I still have the floor plans of half the buildings in this city. Your next job's site is attached. -- {n}'",
                };
                return V(("name", n), ("gift", gift.ToString()), ("text", text));
            },
            Fire: (c, m) =>
            {
                c.Set("thanked:" + m.Vars["name"]);
                switch (m.Vars["gift"])
                {
                    case "0": c.Cash += 1500; c.Count("earned", 1500); break;
                    case "1": c.Medkits = Math.Min(5, c.Medkits + 2); break;
                    default: c.Set("freeschem"); break;
                }
            });

        yield return new("family", Hook.Morning, "{who}", "{name}", "'You don't know me. {name} was my {rel}. The news called it gang trouble. I know it was a job, and I know who took it. I'm not like you -- I can't do anything about it. I just wanted you to know someone is counting.'",
            c => FateOf(c, "killed", 5, "mourned") is string n && Career.Stable(n) % 3 == 0,
            new[]
            {
                Pay("Send money for the funeral, no name (¥1,500)", 1500, (c, m) => { c.Karma += 2; c.KarmaEarned += 2; return "The money goes. Nothing comes back. Karma +2."; }),
                Fx(new Choice("Delete it", (c, m) => { c.Set("vendetta", 1); return "You delete it. Somebody keeps counting."; }), "someone keeps counting"),
            },
            Weight: 0.6f,
            Bind: c =>
            {
                string n = FateOf(c, "killed", 5, "mourned");
                string rel = (Career.Stable(n) / 3 % 3) switch { 0 => "brother", 1 => "mother", _ => "partner" };
                return V(("name", n), ("rel", rel), ("who", "Somebody who knew " + n.Split(' ')[0]));
            },
            Fire: (c, m) => c.Set("mourned:" + m.Vars["name"]));

        // Someone you let go, or let off: a friend now, and friends turn up.
        yield return new("friend-help", Hook.Morning, "{name}", "I owe you", "{text}",
            c => Friend(c) != null, Cooldown: 6,
            Bind: c =>
            {
                string n = Friend(c);
                int kind = (int)(Career.Stable(n + c.Day) % 3);
                return V(("name", n), ("kind", kind.ToString()), ("text", kind switch
                {
                    0 => $"{n}: 'I got out because of you. I work nights at a building you'll be visiting soon -- here are the plans. Don't tell me which one.'",
                    1 => $"{n}: 'I heard you're still in the life. I left a medkit with Ichikawa for you. Two, actually. Stay alive.'",
                    _ => $"{n}: 'My cousin is in dispatch at Lone Star. Your plate just went missing from the system. You're welcome.'",
                }));
            },
            Fire: (c, m) =>
            {
                string n = m.Vars["name"];
                c.Set("friend:" + n, 2);
                switch (m.Vars["kind"])
                {
                    case "0": c.Set("freeschem"); break;
                    case "1": c.Medkits = Math.Min(5, c.Medkits + 2); break;
                    default: c.Heat = 0; c.Fame(-1); break;
                }
            });

        yield return new("spared", Hook.Morning, "A guard you let go", "Thanks", "A message from a number you don't know: 'You could have shot me. You didn't. Here's the floor plan of the next place they're sending you to. We never spoke.'",
            c => c.Flag("spared") > 0, Fire: (c, m) => { c.Set("spared", 0); c.Set("freeschem"); }, Cooldown: 8);

        // Grudges that never stopped counting.
        yield return new("vendetta", Hook.Morning, "Unknown sender", "Counting", "Just a number this time: the days since. And a photo of your car, parked outside your own front door.",
            c => c.Flag("vendetta") > 0 && c.Day > 8,
            Fire: (c, m) => { c.Set("vendetta", 0); c.Set("hitters", 1); });
    }

    // ---- after a run: decisions -------------------------------------------------------------

    private static IEnumerable<Storylet> RunChoices()
    {
        yield return new("collector", Hook.Debrief, "A collector", "Before you hand it over", "A soft voice, no face on the line: 'You're carrying the {item}. I collect such things -- only to look, only for an hour. Nobody needs to know. {¥cash} for your trouble.'",
            c => Decides(c) && c.Last.Job.Type == JobType.Retrieve,
            new[]
            {
                Fx(new Choice("Hand it over untouched", (c, m) => { c.Karma += 1; c.KarmaEarned += 1; return "You deliver it sealed. A professional. Karma +1."; }), "karma +1"),
                Fx(new Choice("Let him look ({¥cash})", (c, m) =>
                {
                    int cash = Money(m, "cash");
                    c.Cash += cash;
                    c.Count("earned", cash);
                    c.Infamy(1);
                    if (m.Vars["caught"] == "1")
                    {
                        string emp = c.Last.Job.Employer;
                        c.Loyalty[emp] = Math.Max(0, c.Loyalty.GetValueOrDefault(emp) - 1);
                        return $"An hour, a scanner, {M.Money(cash)}. Three days later {Tables.Contact(emp).Name} asks why the seals were warm. Trust -1, notoriety +1.";
                    }
                    return $"An hour, a scanner, {M.Money(cash)}. Nobody ever knows. Notoriety +1.";
                }), "+{¥cash}, notoriety +1, maybe trust -1"),
                Past("corp", "Quote his own non-disclosure terms back at him", (c, m) => { c.Cash += 2000; c.Count("earned", 2000); return "He pays ¥2,000 for your silence and doesn't get to look. Corporates understand corporates."; }),
                Talk("Corporate", "Ask who he works for", (c, m) => { c.Set("lead:collector"); return "'Sorensen-Aoki,' he says after a while, 'though you didn't hear it from me.' He pays ¥500 for your discretion anyway."; }),
            },
            Call: true,
            Bind: c => V(("item", c.Last.Job.Item ?? "package"), ("cash", ((int)(c.Last.Pay * 0.6f / 50) * 50).ToString()), ("caught", (c.Last.Job.Seed >> 13) % 4 == 0 ? "1" : "0")));

        yield return new("dirt", Hook.Debrief, Null, "Look at what you pulled", "Null, flatly: 'Your paydata has a second folder in it. It's the Johnson's. Payroll for a black site, names, the lot. Your client is dirtier than the people you stole from. What do you want done with it?'",
            c => c.Last?.Picked("dirt:1") == true || Decides(c) && c.Last.Job.Type == JobType.Datasteal && !c.Last.Asked("dirt"),
            new[]
            {
                Fx(new Choice("Deliver it all and forget it", (c, m) => "Paid and forgotten. Null shrugs. 'Your call.'"), "nothing changes"),
                Fx(new Choice("Leak the folder through Mara Quill", (c, m) =>
                {
                    c.Fame(2);
                    c.Karma += 3;
                    c.KarmaEarned += 3;
                    c.Inc("quill");
                    string emp = c.Last.Job.Employer;
                    c.Loyalty[emp] = Math.Max(0, c.Loyalty.GetValueOrDefault(emp) - 1);
                    c.Tick(c.Last.Job.Faction switch { Faction.Vipers => "vipers", Faction.Halcyon => "halcyon", _ => "lotus" }, -2);
                    return $"KBN runs it at six. Somebody resigns at nine. {Tables.Contact(emp).Name} is not pleased with you (trust -1). Awareness +2, karma +3.";
                }), "awareness +2, karma +3, trust -1, their plan set back"),
                Fx(new Choice("Sell it back to the people it's about ({¥cash})", (c, m) =>
                {
                    int cash = Money(m, "cash");
                    c.Cash += cash;
                    c.Count("earned", cash);
                    c.Infamy(2);
                    return $"They pay {M.Money(cash)} without a word. Nobody will ever trust you with a secret again, if they find out. Notoriety +2.";
                }), "+{¥cash}, notoriety +2"),
            },
            Call: true,
            Bind: c => V(("cash", ((int)(c.Last.Pay * 0.5f / 50) * 50).ToString())));

        yield return new("begs", Hook.Debrief, "{name}", "Please", "In the car, {name} grabs your arm: 'The people who hired you aren't saving me. They're collecting me. Let me out here -- please. I can pay you back, someday.'",
            c => Decides(c) && c.Last.Job.Type == JobType.Rescue && !c.Last.Asked("begs"),
            new[]
            {
                Fx(new Choice("Hand them over as agreed", (c, m) => "You hand them over. They don't look at you. The pay clears."), "nothing changes"),
                Fx(new Choice("Let them go and give back half the pay ({¥cash})", (c, m) =>
                {
                    int cash = Money(m, "cash");
                    c.Cash -= cash;
                    c.Karma += 3;
                    c.KarmaEarned += 3;
                    c.Set("friend:" + m.Vars["name"]);
                    c.Set("fate:" + m.Vars["name"], Career.Fates["rescued"]);
                    c.Set("fateday:" + m.Vars["name"], c.Day);
                    return "You tell the client they died in the crossfire and wire half the money back to keep it clean. Somewhere, someone owes you. Karma +3.";
                }, (c, m) => c.Cash < Money(m, "cash") ? $"Needs {M.Money(Money(m, "cash"))} to pay back." : null), "-{¥cash}, karma +3, a friend"),
            },
            Call: true,
            Bind: c => V(("name", c.Last.Job.Target ?? "the hostage"), ("cash", ((int)(c.Last.Pay * 0.5f / 50) * 50).ToString())));

        yield return new("kids", Hook.Debrief, Juno, "The cleaners found something", "Juno: 'The cleaners went through the place after you. Three kids in a back room, hiding. Viper runners, maybe twelve. Lone Star pays a bounty on gang kids, you know. Your call.'",
            c => Decides(c) && c.Last.Job.Type == JobType.Clear,
            new[]
            {
                Fx(new Choice("Let them go", (c, m) => { c.Karma += 1; c.KarmaEarned += 1; return "'They'll be back on a corner by Friday,' Juno says. 'But fine.' Karma +1."; }), "karma +1"),
                Fx(new Choice("Call Lone Star (¥500)", (c, m) => { c.Cash += 500; c.Count("earned", 500); c.Infamy(1); return "Lone Star pays ¥500 a head for two of them. The third ran. Notoriety +1."; }), "+¥500, notoriety +1"),
                Past("street", "Tell them about the roofs", (c, m) => { c.Karma += 2; c.KarmaEarned += 2; return "You tell them how you got out. One of them listens. Karma +2."; }),
                Talk("Gang", "Give them a message for their boss", (c, m) => { c.Grudge[c.Last.Job.Faction] = Math.Max(0, c.Grudge.GetValueOrDefault(c.Last.Job.Faction) - 2); return "You send them home with a message: business, not personal. It lands. Grudge -2."; }),
            },
            Call: true);

        yield return new("witness", Hook.Debrief, Juno, "Somebody saw you", "Juno: 'A cleaner on the night shift saw your face on the way out. She went straight to a Lone Star box. I can make that go away, but it's not cheap.'",
            c => Decides(c) && c.Last.Job.Type is JobType.Eliminate or JobType.Destroy && c.Last.Alarm,
            new[]
            {
                Pay("Pay her to forget (¥800)", 800, (c, m) => "She buys a ticket to somewhere warm. Your face stays off the wire."),
                Past("cop", "Flash the old badge and take her statement", (c, m) => "You take her statement, very seriously, and lose it on the way out. Nobody else ever asks."),
                Fx(new Choice("Let it ride", (c, m) => { c.Fame(1); c.Heat = MathF.Min(5, c.Heat + 1); return "Your face is on the evening bulletin, badly drawn. Heat +1, awareness +1."; }), "heat +1, awareness +1"),
            },
            Call: true);
    }

    // ---- double-crosses and burned exits ---------------------------------------------------------

    private static IEnumerable<Storylet> Crosses()
    {
        yield return new("crossed", Hook.Morning, Juno, "Your Johnson", "Juno, cold: '{who}, who sold you out on that job? I found them. They think the city's big enough to hide in. It isn't.'",
            c => c.Flag("crossed") > 0 && c.Day - c.Flag("crossed") <= 4,
            new[]
            {
                Pay("Find him (¥2,000)", 2000, (c, m) =>
                {
                    Faction f = (Faction)Math.Max(0, c.Flag("crossedby") - 1);
                    c.OfferSpecial(JobType.Eliminate, f == Faction.Halcyon ? SiteStyle.Office : SiteStyle.Club, f, f == Faction.Halcyon ? "spire" : "strip", Math.Min(5, Math.Max(2, c.MaxThreat)),
                        $"Payback: {m.Vars["who"]}", $"Juno: '{m.Vars["who"]} is in a room with three exits and a lot of guards, sure they're safe. They sold you out. Return the favour.'", tag: "revenge:johnson", target: m.Vars["who"], payMul: 1.6f);
                    return "Juno: 'It's on your board. He pays out of his own pocket this time -- you'll see.'";
                }),
                Fx(new Choice("Squeeze him for the money ({¥half})", (c, m) =>
                {
                    int half = Money(m, "half");
                    c.Cash += half;
                    c.Count("earned", half);
                    c.Infamy(1);
                    return $"Juno makes one call. An hour later {M.Money(half)} lands, with an apology nobody means. Notoriety +1.";
                }, (c, m) => c.Loyalty.GetValueOrDefault("fixer") < 4 ? "Needs trust 4 with Juno." : null), "+{¥half}, notoriety +1"),
                Fx(new Choice("Let it go", (c, m) => { c.Karma += 1; c.KarmaEarned += 1; return "'Bigger than me,' you tell her. She shakes her head. Karma +1."; }), "karma +1"),
            },
            Bind: c => V(("half", (c.Flag("crossedpay") / 2 / 50 * 50).ToString()), ("who", c.Last?.Job?.JohnsonId is string id && Career.JohnsonById(id) is JohnsonDef jd ? jd.Name : "The Johnson")),
            Fire: (c, m) => c.Set("crossed", 0), Cooldown: 1);

        // The regulars: three clean jobs and a Johnson says thank you, in their own way.
        foreach (JohnsonDef jd in Career.Johnsons)
        {
            JohnsonDef d = jd;
            (string text, Action<Career> gift) = d.Id switch
            {
                "grey" => ("Mr. Grey: 'You are reliable. Reliability is rare. Accept a small retainer.' ¥3,000 lands without a reference.", (Action<Career>)(c => { c.Cash += 3000; c.Count("earned", 3000); })),
                "vale" => ("Dr. Vale: 'I, ah, wanted to say -- nobody got hurt. That matters. To me.' A courier brings two medkits, carefully labelled.", c => c.Medkits = Math.Min(5, c.Medkits + 2)),
                "mei" => ("Auntie Mei: 'You work well and you don't ask questions. The family has noticed.' The Red Lotus forget a little of what you did to them.", c => c.Grudge[Faction.Lotus] = Math.Max(0, c.Grudge.GetValueOrDefault(Faction.Lotus) - 2)),
                "tusk" => ("Mr. Tusk: 'Three for three, chummer! Got you something.' A crate of frags, still in the shrink wrap.", c => c.Grenades["frag"] = Math.Min(c.GrenadeCap, c.Grenades.GetValueOrDefault("frag") + 2)),
                _ => ("Ms. Silk: 'Darling, you're my favourite. Here -- the plans for wherever you go next. Don't say I never gave you anything.'", c => c.Set("freeschem")),
            };
            yield return new($"johnson-{d.Id}", Hook.Debrief, d.Name, "A word of thanks", text,
                c => c.Last?.Job?.JohnsonId == d.Id && c.Last.Success && c.JohnsonJobs(d.Id) >= 3, Fire: (c, m) => gift(c));
        }

        yield return new("burned-heat", Hook.Morning, Torque, "Your plates", "Torque: 'Heard Lone Star read your plate at the exit. They had you before you went in. Bring her by and I'll put the old plates in the crusher. Free, this once.'",
            c => c.Flag("burned:heat") > 0 && c.Day - c.Flag("burned:heat") <= 3,
            new[] { new Choice("Take him up on it", (c, m) => { c.Heat = 0; c.Set("burned:heat", 0); return "New plates, new registration. Lone Star loses you. Heat 0."; }) }, Cooldown: 6);

        yield return new("burned-scanner", Hook.Morning, Null, "Your comms", "Null: 'Your exit got burned because your commlink talks in its sleep. Lone Star scanners heard your crew chatter. I can scramble it.'",
            c => c.Flag("burned:scanner") > 0 && !c.HasProgram("ghost") && c.Flag("scrambled") == 0,
            new[]
            {
                Pay("Scramble it (¥1,500)", 1500, (c, m) => { c.Set("scrambled"); return "Null: 'Done. Now you just sound like static. Most people do.' Exits are burned half as often."; }),
                Ok("Not now"),
            });

        yield return new("burned-fled", Hook.Morning, Juno, "Who sold the exit", "Juno: 'Your exit wasn't luck. {name} -- the one who got away from you -- was watching the building. Paid a lookout to call it in. You want them? I know where they sleep.'",
            c => c.Flag("burned:fled") > 0 && FateOf(c, "fled", 0, "hunted") != null,
            new[]
            {
                new Choice("Put it on my board", (c, m) =>
                {
                    string n = m.Vars["name"];
                    c.OfferSpecial(JobType.Eliminate, SiteStyle.Den, Faction.Vipers, "barrens", Math.Min(5, c.MaxThreat), $"Loose end: {n}", $"Juno: '{n} is in a Viper den in the Barrens, paying for protection with what they know about you. Close it.'", tag: "revenge:" + n, target: n);
                    return "It's on your board.";
                }),
                Ok("Leave it"),
            },
            Bind: c => V(("name", FateOf(c, "fled", 0, "hunted"))), Fire: (c, m) => { c.Set("hunted:" + m.Vars["name"]); c.Set("burned:fled", 0); });

        yield return new("burned-crew", Hook.Morning, "{name}", "About the exit", "{name}, not quite meeting your eye: 'I mentioned the job to someone. One drink. I didn't think -- look, it won't happen again.'",
            c => c.Flag("burned:crew") > 0 && c.Crew.Any(x => !x.Value.Dead && x.Value.Trust <= 1),
            new[]
            {
                new Choice("Forgive them", (c, m) => { c.CrewOf(m.Vars["id"]).Trust += 1; return "'You won't regret it.' Their trust in you goes up a point."; }),
                new Choice("Never again", (c, m) => { c.CrewOf(m.Vars["id"]).Trust = 0; return "You tell them so. They'll still work for money. Only for money."; }),
            },
            Bind: c => { var id = c.Crew.First(x => !x.Value.Dead && x.Value.Trust <= 1).Key; return V(("id", id), ("name", Tables.Hires.First(h => h.id == id).name)); },
            Fire: (c, m) => c.Set("burned:crew", 0), Cooldown: 10);
    }

    // ---- the contacts' own lives --------------------------------------------------------------------

    private static IEnumerable<Storylet> ContactLives()
    {
        yield return new("wen-wants", Hook.Morning, Wen, "A buyer", "Old Wen: 'A collector came by. He wants a {thing} -- any {thing} -- and he pays double. For a week. Bring me one.'",
            c => c.Day >= 6 && c.Loyalty.GetValueOrDefault("fence") >= 1,
            Bind: c => V(("thing", c.WantSomething(new Rng(c.Seed ^ (ulong)c.Day * 31UL)))), Cooldown: 8);

        yield return new("doc-after", Hook.Place, Doc, "A framed photo", "There's a framed photo on Ichikawa's desk now: Ade and the old crew, younger, laughing. Ichikawa catches you looking. 'Sit. You're bleeding on my floor again, and this one's on the house.'",
            c => c.ArcDone("doc"), new[] { new Choice("Let her", (c, m) => { c.Injuries = 0; return "Stitches, nanites and, for once, no lecture. Injuries healed."; }) }, Place: "doc");
        yield return new("arms-after", Hook.Place, Sal, "Grandpa's rifle", "Sal has the old rifle on the wall behind the counter, lit like a shrine. 'Nobody touches it. Except to clean it. You want to clean it?' He doesn't wait. He hands you a crate of frags while you do.",
            c => c.ArcDone("arms"), new[] { new Choice("Clean the rifle", (c, m) => { c.Grenades["frag"] = Math.Min(c.GrenadeCap, c.Grenades.GetValueOrDefault("frag") + 2); return "Two frags, 'for the polish'."; }) }, Place: "arms");
        yield return new("rigger-after", Hook.Place, Torque, "Under the hood", "Torque is under your car before you've stopped. 'You drive like a lunatic and I love you for it. Don't move. Five minutes.'",
            c => c.ArcDone("rigger"), new[] { new Choice("Wait", (c, m) => { c.CarDamage[c.Vehicle] = 0; return "Twenty minutes, and the car is perfect."; }) }, Place: "rigger", Cooldown: 12);
        yield return new("decker-after", Hook.Place, Null, "The kid", "The kid from the Lotus job is on a cot in Null's back room, decking. Null pretends not to be proud. 'He found something. Tell them.' The kid: 'Every corp in town watches the same three cameras on the Spire. Funny, right?'",
            c => c.ArcDone("decker"), new[] { new Choice("Funny", (c, m) => { c.NextCamerasDown = true; return "The kid loops the cameras on your next job, for practice."; }) }, Place: "decker");
        yield return new("fence-after", Hook.Place, Wen, "Tea", "Wen pours two cups of tea and pushes one over. He has never done that before. 'The ledger is ash. I sleep now. You want to know something? Ask.'",
            c => c.ArcDone("fence"), new[]
            {
                new Choice("Ask about the street", (c, m) => c.Gossip() ?? "'Quiet. Too quiet. Enjoy it.'"),
                new Choice("Just drink the tea", (c, m) => { c.Karma += 1; c.KarmaEarned += 1; return "You drink it. It's terrible. It's the best thing that happened all week. Karma +1."; }),
            }, Place: "fence");
        yield return new("sabine-after", Hook.Place, Sabine, "The cards", "Sabine lays out three cards without asking. 'The tower. The wheel. And this one -- you, running. Always running.' She smiles. 'The spirits like you. Use it.'",
            c => c.ArcDone("talismonger"), new[] { new Choice("Thank the spirits", (c, m) => { c.NextFastSpells = true; return "Your abilities recharge twice as fast on the next job."; }) }, Place: "talismonger", Cooldown: 14);

        // Life goes on in the shops, a little at a time.
        yield return new("juno-fight", Hook.Place, Juno, "A scene at the bar", "Two men are about to start something at the Rusty Halo's bar. Juno doesn't look up from her drink. 'If you sort that out, the next round is on me.'",
            c => c.Day >= 4, new[]
            {
                new Choice("Sort it out", (c, m) => { c.Befriend("fixer"); return "One look and they find somewhere else to be. Juno raises her glass. Trust +1 (if not already high)."; }),
                Talk("Street", "Talk them down", (c, m) => { c.Befriend("fixer"); c.Karma += 1; c.KarmaEarned += 1; return "A few words in the right accent, and they're buying each other drinks. Juno is impressed. Trust +1, karma +1."; }),
                Ok("Not my business"),
            }, Place: "fixer");
        yield return new("sal-supplier", Hook.Place, Sal, "A bad crate", "Sal is shouting at a delivery man over a crate of rifles with filed-off serials -- and fresh Halcyon stamps under the paint. 'Stolen from Halcyon, sold to me as clean. Do I look clean?'",
            c => c.Day >= 6, new[]
            {
                new Choice("Help him dump the crate", (c, m) => { c.Spent["arms"] = c.Spent.GetValueOrDefault("arms") + 2000; return "You drive it to the bay with him and tip it in. Sal swears you're family now (it counts towards his loyalty, like ¥2,000 spent)."; }),
                Talk("Security", "Tell him how Halcyon tracks its crates", (c, m) => { c.Grenades["emp"] = Math.Min(c.GrenadeCap, c.Grenades.GetValueOrDefault("emp") + 1); return "Tracker in the false bottom. Sal crushes it and hands you an EMP grenade for your trouble."; }),
                Ok("Leave him to it"),
            }, Place: "arms");
        yield return new("torque-race", Hook.Place, Torque, "A bet", "Torque is wiping his hands, grinning. 'Viper kid says his bike beats your car round the block. I said you'd do it for ¥500.'",
            c => c.Day >= 5, new[]
            {
                new Choice("Race him (Driving)", (c, m) =>
                {
                    bool win = c.S(Skill.Driving) + (int)(c.Seed % 3) >= 2;
                    if (win) { c.Cash += 500; c.Count("earned", 500); return "You win by a bumper. The kid pays up, sulking. +¥500."; }
                    c.Cash = Math.Max(0, c.Cash - 500);
                    return "The kid wins by a length and does a wheelie on the way back. -¥500. (More Driving helps.)";
                }, (c, m) => c.Cash < 500 ? "Needs ¥500 to bet." : null),
                Ok("Not today"),
            }, Place: "rigger");
        yield return new("null-glitch", Hook.Place, Null, "Static", "Null's screens are full of the same face, flickering. 'Somebody's been searching for you on the Matrix. Pretty hard.' A pause. 'I could make them find something else.'",
            c => c.Awareness >= 3, new[]
            {
                Pay("Bury your trail (¥1,200)", 1200, (c, m) => { c.Fame(-2); return "Null seeds a dozen fake runners with your handle. Awareness -2."; }),
                Ok("Let them look"),
            }, Place: "decker", Cooldown: 10);
        yield return new("wen-kid", Hook.Place, Wen, "A shoplifter", "A kid in a too-big jacket is pocketing a watch in Wen's shop. Wen hasn't noticed, or pretends not to.",
            c => c.Day >= 3, new[]
            {
                new Choice("Pay for the watch (¥200)", (c, m) => { c.Cash -= 200; c.Karma += 1; c.KarmaEarned += 1; c.Set("block:pipmet"); return "The kid stares at you, then runs. Wen counts the money, says nothing, and nods. Karma +1. (You'll see that kid again.)"; }, (c, m) => c.Cash < 200 ? "Needs ¥200." : null),
                new Choice("Tell Wen", (c, m) => { c.Befriend("fence"); return "Wen thanks you with a long look and better prices. The kid gets away anyway."; }),
            }, Place: "fence");
        yield return new("sabine-warning", Hook.Place, Sabine, "Bad water", "Sabine is burning something bitter. 'The water under the Docks has gone wrong. Something drinks from it that should be asleep. If you see green in the dark, run.'",
            c => c.Day >= 8, Place: "talismonger", Fire: (c, m) => c.Set("lead:tide"));
        yield return new("doc-essence", Hook.Place, Doc, "A word about the chrome", "Ichikawa taps your arm with a stylus. 'You're more machine than meat, now. I'll keep installing it -- that's the job -- but people in your state start seeing things. Come to me first, not a bottle.'",
            c => c.Essence < 2f, new[] { Ok("I'll be fine", "'They all say that.'") }, Place: "doc");
    }

    // ---- people who only write --------------------------------------------------------------

    private static IEnumerable<Storylet> Writers()
    {
        yield return new("quill-hello", Hook.Morning, Quill, "I've been following your work", "Mara Quill, KBN stringer: 'Nobody else connects your jobs. I do. I don't want your name -- I want footage, and the occasional true story. I pay. Interested?'",
            c => c.Awareness >= 3 || c.Flag("quill") > 0,
            new[]
            {
                new Choice("Keep her number", (c, m) => { c.Set("quill", Math.Max(1, c.Flag("quill"))); return "'Smart. Call me when something burns.'"; }),
                Past("corp", "Offer her an inside source: you", (c, m) => { c.Set("quill", 2); c.Befriend("press"); return "'A real Halcyon insider? I'll buy you a drink. I'll buy you several.' Quill's trust +1."; }),
                new Choice("Hang up", (c, m) => { c.Set("quill", -1); return "She doesn't call again. Probably."; }),
            });
        yield return new("quill-buy", Hook.Debrief, Quill, "That was you", "Mara Quill: 'Half the {district} heard that last night. If you've got helmet cam of it, I'll pay {¥cash}. Faces blurred -- yours, anyway.'",
            c => c.Flag("quill") > 0 && c.Last?.Success == true && c.Last.Alarm && c.Last.Job.Type is not (JobType.Courier or JobType.Race or JobType.Hub),
            new[]
            {
                new Choice("Sell the footage ({¥cash})", (c, m) => { int cash = Money(m, "cash"); c.Cash += cash; c.Count("earned", cash); c.Fame(1); return $"It leads the late bulletin. {M.Money(cash)}. Awareness +1."; }),
                Ok("No"),
            },
            Bind: c => V(("district", Tables.District(c.Last.Job.District).Name), ("cash", (300 * c.Last.Job.Threat).ToString())), Cooldown: 4);

        yield return new("dace-hello", Hook.Morning, Dace, "Your file", "Sgt. Dace, Lone Star, from a burner: 'I've seen your file. It's getting thick. For ¥1,500 a week it gets thin, and I tell you when the brass are planning something. Cash only.'",
            c => c.Stat("busts") >= 1 || c.Awareness >= 4 || c.Clock("lonestar") >= 4,
            new[]
            {
                Pay("Pay him (¥1,500)", 1500, (c, m) => { c.Set("dace", c.Day); c.Heat = 0; return "'Pleasure.' Your heat drops to nothing, and he'll call before a crackdown."; }),
                Past("cop", "'Sarge. It's me.'", (c, m) => { c.Set("dace", c.Day); c.Heat = 0; return "A long silence. 'Well, I'll be damned.' The first week's on him, for old times."; }),
                new("Tell him where to go", (c, m) => { c.Set("dace", -1); return "'Your funeral,' he says, and means it."; }),
            });
        yield return new("dace-renew", Hook.Morning, Dace, "Renewal", "Sgt. Dace: 'Week's up. Same again, or your file goes back on the pile.'",
            c => c.Flag("dace") > 0 && c.Day - c.Flag("dace") >= 7,
            new[]
            {
                Pay("Pay (¥1,500)", 1500, (c, m) => { c.Set("dace", c.Day); c.Heat = 0; return "'Good doing business.' Heat 0."; }),
                new("Stop paying", (c, m) => { c.Set("dace", -1); c.Heat = MathF.Min(5, c.Heat + 1); return "Dace is hurt. Your file gets thicker overnight. Heat +1."; }),
            }, Cooldown: 7);
        yield return new("dace-warn", Hook.Morning, Dace, "Heads up", "Sgt. Dace: 'Brass is planning a crackdown. Checkpoints, zero tolerance, the works. Lie low. That's free -- I want you alive to keep paying.'",
            c => c.Flag("dace") > 0 && c.Clock("lonestar") >= 6, Cooldown: 6, Fire: (c, m) => c.Tick("lonestar", -2));

        yield return new("vesper-hello", Hook.Morning, Vesper, "Work for people like us", "Vesper: 'Juno is too clean for you now. I'm not. I have jobs nobody else will take, for people nobody should work for. They pay double. Shall I put one on your board?'",
            c => c.Notoriety >= 5 && c.CityRef != null,
            new[]
            {
                new Choice("Put one on my board", (c, m) =>
                {
                    c.OfferSpecial(JobType.Eliminate, SiteStyle.Club, Faction.Lotus, "strip", Math.Min(5, c.MaxThreat + 1), "Vesper: a quiet word", "Vesper: 'A union organiser in a Lotus club is about to say something inconvenient. Make sure she doesn't. It pays double, and you never met me.'", tag: "vesper", payMul: 2.6f);
                    return "Vesper: 'It's there. Don't be squeamish.'";
                }),
                Ok("Not that kind of runner"),
            }, Cooldown: 10);
    }

    // ---- the city ---------------------------------------------------------------------------

    private static IEnumerable<Storylet> World()
    {
        yield return new("turf", Hook.Morning, "{from}", "{district}", "{text}",
            c => Flipped(c) != null,
            new[]
            {
                Fx(new Choice("Offer them your services", (c, m) =>
                {
                    Faction f = (Faction)int.Parse(m.Vars["fac"]), lost = (Faction)int.Parse(m.Vars["lost"]);
                    c.Grudge[f] = Math.Max(0, c.Grudge.GetValueOrDefault(f) - 2);
                    // Whoever was pushed out hears who came running.
                    if (lost == f) return "They remember who answers. Their grudge against you drops by 2.";
                    c.Grudge[lost] = c.Grudge.GetValueOrDefault(lost) + 1;
                    return $"They remember who answers. Their grudge against you drops by 2 -- and the {Tables.FactionNames[(int)lost]} hear about it (grudge +1).";
                }), "their grudge -2, the {lostname} grudge +1"),
                Ok("Ignore it"),
            },
            Bind: c =>
            {
                string d = Flipped(c);
                Faction f = c.Holder(d);
                int prev = c.Flag("heldprev:" + d) - 1;
                Faction lost = prev >= 0 ? (Faction)prev : Tables.District(d).Factions[0];
                string from = f switch { Faction.Vipers => "A Viper lieutenant", Faction.Halcyon => "Halcyon Community Relations", _ => "Auntie Mei, Red Lotus" };
                string text = f switch
                {
                    Faction.Vipers => $"'The {Tables.District(d).Name} is ours now. We know you helped. Come by sometime, runner -- or don't come by at all.'",
                    Faction.Halcyon => $"'Halcyon-Matsuda is proud to welcome the {Tables.District(d).Name} into its family of secure communities. Independent contractors of good standing are invited to apply.'",
                    _ => $"'The {Tables.District(d).Name} has new management. We are polite people. We would like to stay polite with you.'",
                };
                return V(("district", Tables.District(d).Name), ("from", from), ("text", text), ("fac", ((int)f).ToString()), ("lost", ((int)lost).ToString()), ("lostname", Tables.FactionNames[(int)lost]), ("id", d));
            },
            Fire: (c, m) => c.Set("turfseen:" + m.Vars["id"], c.Day), Cooldown: 2);

        yield return new("null-clocks", Hook.Morning, Null, "Corp traffic", "{text}",
            c => Plotter(c) != null,
            new[]
            {
                Pay("Set them back (¥3,000)", 3000, (c, m) => { c.Tick(m.Vars["clock"], -3); return "Null: 'A few files go missing, a few meetings get cancelled. That buys you a week.'"; }),
                Ok("Noted"),
            },
            Bind: c =>
            {
                string k = Plotter(c);
                string text = k switch
                {
                    "vipers" => "Null: 'The Vipers are buying guns in bulk and moving them to the Lotus border. A war's coming. Days, not weeks.'",
                    "halcyon" => "Null: 'Halcyon's property arm is filing permits all over the Docks and Industrial. They're about to buy the streets out from under people.'",
                    "lotus" => "Null: 'Lotus money is landing in three councillors' accounts. When the vote goes through, Lone Star goes soft on them.'",
                    _ => "Null: 'Lone Star's tactical budget just tripled. They're planning a crackdown, and your name is in the planning documents.'",
                };
                return V(("clock", k), ("text", text));
            }, Cooldown: 6);

        yield return new("gangwar-side", Hook.Morning, Juno, "Pick a side?", "Juno: 'Vipers and Lotus are at war. Both of them are calling me, both want runners. You could pick one. Or you could stay out of it and take money from both.'",
            c => c.On("gangwar"),
            new[]
            {
                Fx(new Choice("Side with the Vipers", (c, m) => { c.Grudge[Faction.Vipers] = 0; c.Grudge[Faction.Lotus] = Math.Min(9, c.Grudge.GetValueOrDefault(Faction.Lotus) + 2); c.Push("barrens", Faction.Lotus, 0.08f); return "The Vipers love you this week. The Lotus will remember."; }), "Viper grudge 0, Lotus grudge +2"),
                Fx(new Choice("Side with the Lotus", (c, m) => { c.Grudge[Faction.Lotus] = 0; c.Grudge[Faction.Vipers] = Math.Min(9, c.Grudge.GetValueOrDefault(Faction.Vipers) + 2); c.Push("strip", Faction.Vipers, 0.08f); return "Auntie Mei sends flowers. The Vipers send a drive-by, eventually."; }), "Lotus grudge 0, Viper grudge +2"),
                Ok("Stay out of it", "Juno: 'Smart. Neutral pays.'"),
            }, Cooldown: 20);

        yield return new("grid", Hook.Morning, Torque, "Lights out", "Torque: 'Whole {district} is dark. Grid failure, they say. My generator's fine. If you've got work over there, now's the time -- nobody's cameras are working.'",
            c => c.Today.Any(h => h.Id == "grid" && h.Start == c.Day),
            Bind: c => V(("district", Tables.District(c.Today.First(h => h.Id == "grid").District).Name)), Cooldown: 5);

        yield return new("surge", Hook.Morning, Sabine, "The tide is high", "Sabine: 'The astral is running high tonight, darling. Spells bite harder. So do the things that live there.'",
            c => c.On("surge") && c.Awakened, Cooldown: 5);
    }

    /// <summary>A district that changed hands in the last couple of days and hasn't been answered.</summary>
    private static string Flipped(Career c) => Tables.Districts.Select(d => d.Id)
        .FirstOrDefault(d => c.Flag("heldday:" + d) > 0 && c.Day - c.Flag("heldday:" + d) <= 2 && c.Flag("turfseen:" + d) < c.Flag("heldday:" + d));

    /// <summary>The faction plan nearest to coming off (6+), if any.</summary>
    private static string Plotter(Career c) => Career.ClockIds.Where(k => c.Clock(k) >= 6).OrderByDescending(c.Clock).FirstOrDefault();

    // ---- the crew ------------------------------------------------------------------------------

    private static IEnumerable<Storylet> CrewStories()
    {
        foreach (var h in Tables.Hires)
        {
            string id = h.id, name = h.name;
            var (a, ad, b, bd) = Career.CrewUpgrades[id][0];
            yield return new($"crew2-{id}", Hook.Morning, name, "A word", $"{name}: 'We work well together. I've been thinking about how to be more use to you. Two ways I could go -- your call.'",
                c => c.CrewTrust(id) >= 2 && !c.CrewOf(id).Dead && !c.CrewHas(id, a) && !c.CrewHas(id, b),
                new[] { new Choice(ad, (c, m) => { c.CrewLearn(id, a); return $"{name}: 'Done. You'll see.'"; }), new Choice(bd, (c, m) => { c.CrewLearn(id, b); return $"{name}: 'Done. You'll see.'"; }) }, Call: true);
            var (a2, ad2, b2, bd2) = Career.CrewUpgrades[id][1];
            yield return new($"crew4-{id}", Hook.Morning, name, "Another word", $"{name}: 'I trust you. That's rare for me. Let me give you something back.'",
                c => c.CrewTrust(id) >= 4 && !c.CrewOf(id).Dead && !c.CrewHas(id, a2) && !c.CrewHas(id, b2),
                new[] { new Choice(ad2, (c, m) => { c.CrewLearn(id, a2); return $"{name} nods."; }), new Choice(bd2, (c, m) => { c.CrewLearn(id, b2); return $"{name} nods."; }) }, Call: true);
            var (type, style, fac, district, threat, title, brief) = id switch
            {
                "ox" => (JobType.Clear, SiteStyle.Den, Faction.Vipers, "barrens", 3, "Ox: the old crew", "Ox, quietly for a troll: 'My old crew sold me to the Vipers for a debt. They're running a den in the Barrens now, getting fat. I want to look them in the eye. Come with me.'"),
                "wisp" => (JobType.Eliminate, SiteStyle.Office, Faction.Halcyon, "spire", 4, "Wisp: the teacher", "Wisp: 'The man who taught me to shoot works corporate security now. He trained me, then he sold me to Halcyon's labs. He's on the top floor. Let me take the shot.'"),
                "ade" => (JobType.Escort, SiteStyle.Club, Faction.Lotus, "strip", 3, "Ade: the testimony", "Ade: 'The Lotus took me to send Ichikawa a message. I want to send one back: I'm testifying to a Lone Star inspector who isn't on their payroll. The meeting is in one of their clubs -- their idea of a joke. Walk me in and out.'"),
                "byte" => (JobType.Hack, SiteStyle.Office, Faction.Lotus, "strip", 3, "Byte: the books", "Byte: 'The Lotus still have my face in their books -- they'll find me one day. Let me jack in from your place and burn the records. You hold the ICE off. Please.'"),
                _ => (JobType.Rescue, SiteStyle.Lab, Faction.Halcyon, "industrial", 3, "Patch: the ward", "Patch: 'There's a girl in a Halcyon test ward. I was her nurse before I was a runner. They're going to use her up. I can't do it alone.'"),
            };
            yield return new($"crewjob-{id}", Hook.Morning, name, "Something personal", brief,
                c => c.CrewTrust(id) >= 3 && !c.CrewOf(id).Dead && !c.CrewHas(id, "loyal") && c.CityRef != null && !c.Board.Any(j => j.Tag == "crew:" + id),
                new[]
                {
                    new Choice("I'm in", (c, m) =>
                    {
                        Job j = c.OfferSpecial(type, style, fac, district, threat, title, brief, tag: "crew:" + id, target: id switch { "wisp" => "Garrick Vance", "ade" => "Ade", _ => "Lena Sorel" }, item: "the girl", expires: 15);
                        if (j != null && type != JobType.Escort) j.Hired = id;
                        if (j != null && type == JobType.Hack && c.CityRef != null) j.Lot = c.CityRef.Place(c.HasUpgrade("loft") ? "loft" : "home").Index;
                        return $"{name}: 'Thank you.' It's on your board -- {name} comes along, and finishing it means their cut drops for good.";
                    }),
                    Ok("Not now", $"{name}: 'I understand. The offer stands.'"),
                }, Cooldown: 8);
            yield return new($"crewdead-{id}", Hook.Morning, Juno, $"{name}", $"Juno: 'They buried {name} this morning, in the Barrens, where the crew always buries its own. A few of the old faces came. They asked if you'd come.'",
                c => c.Flag("crewdead:" + id) > 0 && c.Day - c.Flag("crewdead:" + id) <= 3,
                new[]
                {
                    Pay("Go, and pay for the stone (¥1,000)", 1000, (c, m) => { c.Karma += 2; c.KarmaEarned += 2; return $"A plain stone, '{name}. Ran with the best.' Karma +2."; }),
                    Ok("Stay away", "You don't go. Nobody says anything about it, which is worse."),
                });
        }
    }

    // ---- the block ------------------------------------------------------------------------------

    private static IEnumerable<Storylet> Block()
    {
        yield return new("block-rent", Hook.Place, Halloran, "The rent", "Mrs. Halloran, the landlady, is waiting on the stairs with her arms folded. 'You owe me {owed}. I don't care what you do for a living, I care that it pays.'",
            c => c.RentOwed > 0,
            new[]
            {
                Pay("Pay her ({owed})", 0, (c, m) => { int owed = c.RentOwed; c.Cash -= owed; c.Count("spent", owed); c.RentOwed = 0; c.Set("rentlate", 0); return "She counts it twice. 'Same time next week.'"; }) with { Block = (c, m) => c.Cash < c.RentOwed ? $"Needs {M.Money(c.RentOwed)}." : null },
                Ok("Next week, I swear", "'You said that last week.'"),
            },
            Place: "home", Cooldown: 3, Bind: c => V(("owed", M.Money(c.RentOwed))));

        yield return new("block-noodles", Hook.Place, Mama, "The stand downstairs", "There's a noodle stand under your window now, run by a woman with forearms like hawsers. 'Mama Oyelaran. You're the one who comes home at four in the morning bleeding. First bowl's free.'",
            c => c.Day >= 3, new[] { new Choice("Eat", (c, m) => { c.WellFed = true; c.Set("block:noodles"); return "It's the best thing you've eaten in this city. Well fed (+15 % max HP next run)."; }) }, Place: "home");

        yield return new("block-racket", Hook.Place, Mama, "Protection", "Mama Oyelaran's stand has a broken window and two Viper kids leaning on the counter. 'Two hundred a week, or the next window is me,' one says. Mama looks at you.",
            c => c.Flag("block:noodles") > 0 && c.Day >= 7 && c.Flag("block:racket") == 0,
            new[]
            {
                Pay("Pay them off for her (¥800)", 800, (c, m) => { c.Set("block:racket", 1); c.Set("block:meals"); return "The kids take the money and swagger off. Mama won't look at you, but your bowl is always full now."; }),
                new("Have a word with their boss", (c, m) =>
                {
                    c.Set("block:racket", 2);
                    c.OfferSpecial(JobType.Clear, SiteStyle.Den, Faction.Vipers, "barrens", Math.Min(3, c.MaxThreat), "The block: the shakedown crew", $"{Mama}: 'They work out of a den on the edge of the Barrens. Nobody's ever told them no.' Tell them no.", tag: "block:racket", payMul: 0.8f);
                    return "It's on your board. Mama says nothing, but she puts an extra egg in your bowl.";
                }),
                Past("barrens", "Tell them whose kid you are", (c, m) => { c.Set("block:racket", 1); c.Set("block:meals"); return "You name your mother's squat and your brother's bike. They go pale and leave. Mama feeds you for free from now on."; }),
                Talk("Gang", "Explain the block is under your protection", (c, m) => { c.Set("block:racket", 1); c.Set("block:meals"); return "You say it in a way the kids understand. They leave and don't come back. Mama feeds you for free from now on."; }),
                Fx(new Choice("Stay out of it", (c, m) => { c.Set("block:racket", 3); c.Set("block:noodles", 0); return "Next week the stand is boarded up. The street is quieter, and colder."; }), "the stand closes"),
            }, Place: "home");

        yield return new("block-racket-done", Hook.Debrief, Mama, "Word gets around", "Mama Oyelaran has heard. Everyone on the block has heard. 'Eat. You eat here for nothing now. Don't argue with me.'",
            c => c.Last?.Job?.Tag == "block:racket" && c.Last.Success, Fire: (c, m) => c.Set("block:meals"));

        yield return new("block-meal", Hook.Place, Mama, "A bowl", "There's a covered bowl outside your door, still warm, and a note: 'Eat. -- M.'",
            c => c.Flag("block:meals") > 0 && !c.WellFed, new[] { new Choice("Eat", (c, m) => { c.WellFed = true; return "Well fed (+15 % max HP on the next run)."; }) }, Place: "home", Cooldown: 6);

        yield return new("block-pip", Hook.Place, Pip, "A kid on the stairs", "The kid from Wen's shop is sitting on your stairs. 'I'm Pip. I watch the street. I know every car that parks here and every face that doesn't belong. For two grand I'll watch it for you, forever.'",
            c => c.Flag("block:pipmet") > 0 || c.Day >= 12,
            new[]
            {
                Pay("Hire a lookout (¥2,000)", 2000, (c, m) => { c.Set("block:lookout"); return "Pip spits on a hand and shakes yours. Half the ambushes on the way home won't happen -- Pip calls ahead."; }),
                Ok("Go home, kid", "'This is home,' Pip says, and doesn't move."),
            }, Place: "home", Cooldown: 9);

        yield return new("block-pip-warn", Hook.Morning, Pip, "Watch out", "Pip: 'Two cars been circling the block all night. Viper plates. Not the noodle kids -- older. They're waiting for you to do something loud.'",
            c => c.Flag("block:lookout") > 0 && (c.Grudge.Values.Any(g => g >= 4) || c.Flag("hitters") > 0), Cooldown: 7, Fire: (c, m) => c.Set("hitters", 0));

        // Too famous to live quietly: they come to your door.
        yield return new("raid", Hook.Morning, "{who}", "Tonight", "{who}: 'They're coming for your place tonight -- a full team, because you've been on the news too often. You can be gone when they get there, or you can be waiting.'",
            c => c.Awareness >= c.RaidFame && c.CityRef != null && c.Day - c.Flag("raided") > 14 && !c.Board.Any(j => j.Tag == "raid") && c.ActiveJob < 0,
            new[]
            {
                Fx(new Choice("Be waiting", (c, m) =>
                {
                    Job j = c.OfferRaid();
                    if (j != null) { c.ActiveJob = j.Id; c.BringRetained(j); }
                    return j == null ? "They never come." : "It's your active job: go home and hold the place until they give up.";
                }), "a defence at home, tonight"),
                Fx(new Choice("Clear out for a few nights (¥2,000)", (c, m) => { c.Cash = Math.Max(0, c.Cash - 2000); c.Fame(-2); c.Set("raided", c.Day); return "You sleep in your car in a car park in the Docks. They kick your door in and find nothing worth taking. Awareness -2."; }), "-¥2,000, awareness -2"),
            },
            Bind: c => V(("who", c.Flag("block:lookout") > 0 ? Pip : c.Flag("dace") > 0 ? Dace : Juno)),
            Fire: (c, m) => c.Set("raided", c.Day), Call: true);
    }
}
