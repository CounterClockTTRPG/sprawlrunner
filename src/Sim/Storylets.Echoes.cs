using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// Echoes: weeks after a job (10-30 days, seeded per job), somebody from it
/// comes back -- the hostage you cut loose, the guard you let get up, the
/// widow of the target, the one who got away, the Johnson who sold you out,
/// the face across the street, the kids in the back room. Each is read off
/// <see cref="Career.Memories"/>, names the job, the district and the day,
/// fires once per job, and no two come within four days of each other.
/// </summary>
public static partial class Storylets
{
    /// <summary>How long after its job this echo comes back: 10 to 30 days, the same on every run.</summary>
    public static int EchoDelay(PastJob p, string kind) => Tables.EchoMin + (int)(Career.Stable($"{kind}:{p.Seed}:{p.Id}") % Tables.EchoSpread);

    private static bool EchoQuiet(Career c) => c.Flag("echo:last") == 0 || c.Day - c.Flag("echo:last") >= Tables.EchoGap;

    /// <summary>The oldest remembered job that is due to send this echo back, if any (and if the last echo was long enough ago).</summary>
    public static PastJob EchoOf(Career c, string kind, Func<PastJob, bool> fits) =>
        !EchoQuiet(c) ? null : c.Memories.Where(p => !p.Echoed.Contains(kind) && c.Day - p.Day >= EchoDelay(p, kind) && fits(p)).OrderBy(p => p.Day).FirstOrDefault();

    /// <summary>When an echo is sent by hand with nothing to remember: the last job stands in.</summary>
    private static PastJob StandIn(Career c)
    {
        Job j = c.Last?.Job;
        return new PastJob
        {
            Id = -1, Day = Math.Max(1, c.Day - 14), Threat = j?.Threat ?? 2, Pay = j?.Pay ?? 2000, Seed = j?.Seed ?? 1, Type = j?.Type ?? JobType.Retrieve, Faction = j?.Faction ?? Faction.Vipers,
            Title = j?.Title ?? "a job", Target = j?.Target ?? "Vera Lindqvist", District = j?.District, Johnson = j?.JohnsonId ?? "grey", Kills = 3, Alarm = true,
        };
    }

    private static PastJob MemoryOf(Career c, Mail m) => int.TryParse(m.Vars.GetValueOrDefault("mem"), out int id) ? c.Memories.LastOrDefault(p => p.Id == id) : null;

    private static Faction FacOf(Mail m) => (Faction)int.Parse(m.Vars["f"]);

    /// <summary>An echo: due when <paramref name="fits"/> holds for a remembered job; its words can name {name}, {title}, {place}, {day} and {faction}.</summary>
    private static Storylet Echo(string kind, Hook hook, string from, string subject, string text, Func<Career, PastJob, bool> fits, Choice[] choices = null,
        string place = null, bool call = false, Func<Career, PastJob, (string, string)[]> more = null, Action<Career, Mail> fire = null, float weight = 1.5f) =>
        new(kind, hook, from, subject, text, c => EchoOf(c, kind, p => fits(c, p)) != null, choices, weight, Cooldown: 0, Place: place, Call: call,
            Bind: c =>
            {
                PastJob p = EchoOf(c, kind, x => fits(c, x)) ?? StandIn(c);
                var v = V(("mem", p.Id.ToString()), ("name", p.Target ?? "someone"), ("title", p.Title ?? "a job"), ("place", p.Place), ("day", p.Day.ToString()),
                    ("f", ((int)p.Faction).ToString()), ("faction", Tables.FactionNames[(int)p.Faction]));
                if (more != null) foreach (var (k, x) in more(c, p)) v[k] = x;
                return v;
            },
            Fire: (c, m) =>
            {
                MemoryOf(c, m)?.Echoed.Add(kind);
                c.Set("echo:last", c.Day);
                fire?.Invoke(c, m);
            });

    private static bool Still(Career c, string name, string fate) => name != null && c.Flag("fate:" + name) == Career.Fates[fate];

    private static IEnumerable<Storylet> Echoes()
    {
        // The hostage you got out works at Ichikawa's now.
        yield return Echo("echo-nurse", Hook.Place, "{name}", "The new nurse",
            "The nurse who takes your arm has a pale band on each wrist where the cable ties were. '{name}. \"{title}\", {place}, day {day}. You cut me loose and walked me out.' She doesn't let Ichikawa near the bill. {gift}",
            (c, p) => p.Fate == "rescued" && Still(c, p.Target, "rescued"),
            new[] { Ok("Thank her", "'Don't. Just come back in one piece. Or in several -- I can work with several.'") }, place: "doc",
            more: (c, p) => new[] { ("gift", c.Injuries > 0 ? "She patches up every injury you came in with herself, free." : "You aren't hurt, so she presses two medkits into your hands instead."), ("heal", c.Injuries > 0 ? "1" : "0") },
            fire: (c, m) => { if (m.Vars["heal"] == "1") c.Injuries = 0; else c.Medkits = Math.Min(5, c.Medkits + 2); });

        // A guard you let get up.
        yield return Echo("echo-guard", Hook.Morning, "A guard you let walk", "Night roster",
            "A number you don't know: 'Day {day}. {place}. \"{title}\". You had me on the floor and you let me get up. They moved me to another {faction} site. Next time {faction} is on your board, the doors on my shift will be open. We never spoke.'",
            (c, p) => (p.Spared > 0 || p.KnockOuts > 0) && p.Type is not (JobType.Bounty or JobType.Hack or JobType.Defend) && c.Flag("inside:" + p.Faction) == 0,
            fire: (c, m) => c.Set("inside:" + FacOf(m)));

        // The widow of a target, on your landing.
        yield return Echo("echo-widow", Hook.Place, "{name}'s widow", "At your door",
            "A woman on your landing, coat buttoned wrong, who has been waiting a long time. '{name} was my husband. {place}, day {day} -- the news called it gang trouble. I found your door anyway. I want to hear you say it.'",
            (c, p) => p.Fate == "killed" && Still(c, p.Target, "killed") && Career.Stable(p.Target) % 2 == 0 && c.Flag("mourned:" + p.Target) == 0,
            new[]
            {
                Fx(Pay($"Give her money for the kids ({M.Money(Tables.RestitutionPer)})", Tables.RestitutionPer, (c, m) => { c.Infamy(-1); c.Karma += 1; c.KarmaEarned += 1; return "She takes it without looking at it. By the weekend the street knows you paid. Notoriety -1, karma +1."; }),
                    $"-{M.Money(Tables.RestitutionPer)}, notoriety -1, karma +1"),
                Fx(new Choice("Say it: you're sorry", (c, m) => { c.Karma += 1; c.KarmaEarned += 1; return "You say it. She nods once, as if a bill has been paid, and goes down the stairs. Karma +1."; }), "karma +1"),
                Fx(new Choice("Tell her never to come here again", (c, m) => { c.Infamy(1); return "You make sure she understands. She does. So does everyone she tells. Notoriety +1."; }), "notoriety +1"),
                Fx(new Choice("Shut the door", (c, m) => { c.Set("hitters", 1); return "You shut the door. Her brother doesn't shut doors. After your next job, somebody will be waiting."; }), "hitters after your next job"),
            },
            place: "home", fire: (c, m) => c.Set("mourned:" + m.Vars["name"]));

        // Or the brother, with a gun and two friends.
        yield return Echo("echo-revenge", Hook.Morning, Juno, "{name}'s brother",
            "Juno: '{name} -- \"{title}\", {place}, day {day}? The brother has been asking about you in every bar in {place}. He's bought a gun and two friends. They'll be waiting after your next job.'",
            (c, p) => p.Fate == "killed" && Still(c, p.Target, "killed") && Career.Stable(p.Target) % 2 == 1 && c.Flag("mourned:" + p.Target) == 0,
            new[]
            {
                Pay("Have Juno talk him down (¥1,500)", 1500, (c, m) => "Juno: 'He took the money and a long look at what he was about to do. He's gone home.'"),
                Talk("Street", "Meet him first, alone, hands empty", (c, m) => { c.Karma += 1; c.KarmaEarned += 1; return "He swings once and misses. Then he sits down on the kerb and cries. He goes home. Karma +1."; }),
                Fx(new Choice("Let him come", (c, m) => { c.Set("hitters", 1); return "You check the mirror more often. After your next job, somebody will be waiting."; }), "hitters after your next job"),
            },
            fire: (c, m) => c.Set("mourned:" + m.Vars["name"]));

        // The one who got away, a second time: they wrote once to say they knew your face; now they want something.
        yield return Echo("echo-fled-offer", Hook.Morning, "{name}", "A proposal",
            "{name}: '\"{title}\". {place}, day {day}. You came for me and you missed. I've had time to find out who put up the money: {other}. So here's a thought, runner. They paid you. I pay better.'",
            (c, p) => p.Fate == "fled" && Still(c, p.Target, "fled") && c.Flag("followed:" + p.Target) > 0 && Career.Stable(p.Target) % 2 == 0,
            new[]
            {
                Fx(new Choice("Take their money", (c, m) =>
                {
                    string n = m.Vars["name"];
                    Faction o = (Faction)int.Parse(m.Vars["o"]);
                    c.OfferSpecial(JobType.Destroy, o == Faction.Halcyon ? SiteStyle.Office : SiteStyle.Warehouse, o, m.Vars["district"], Math.Clamp(c.MaxThreat, 2, 5),
                        $"{n}'s offer", $"{n}: 'The people who wanted me dead keep their records at this place. Burn it. Then we're even, you and I.'", tag: "echo:fled", target: n, payMul: 1.5f);
                    c.Set("friend:" + n);
                    return $"{n}: 'Good. It's on your board.' Somewhere in the city, an enemy becomes something else.";
                }), "a job against {other}, x1.5 pay; a friend"),
                Ok("Tell them you don't switch sides", "No reply. The number stops working an hour later."),
            },
            more: (c, p) =>
            {
                Faction o = (Faction)(((int)p.Faction + 1) % 3);
                return new[] { ("o", ((int)o).ToString()), ("other", Tables.FactionNames[(int)o]), ("district", p.District ?? "strip") };
            });

        yield return Echo("echo-fled-grudge", Hook.Morning, Juno, "{name} is talking",
            "Juno: '{name}, the one who got away from you at {place} on day {day}? They've been talking to {faction}. Your name, your car, where you drink. A little money would stop it.'",
            (c, p) => p.Fate == "fled" && Still(c, p.Target, "fled") && c.Flag("followed:" + p.Target) > 0 && Career.Stable(p.Target) % 2 == 1,
            new[]
            {
                Pay("Pay {name} to forget your name (¥1,500)", 1500, (c, m) => "Juno: 'Done. They'd rather be rich than right.'"),
                Fx(new Choice("Let them talk", (c, m) => { Faction f = FacOf(m); c.Grudge[f] = Math.Min(9, c.Grudge.GetValueOrDefault(f) + 2); return $"{m.Vars["faction"]} listen. Grudge +2."; }), "{faction} grudge +2"),
            });

        // The Johnson who sold you out wants back in.
        yield return Echo("echo-johnson", Hook.Morning, "{johnson}", "About \"{title}\"",
            "{johnson}: '\"{title}\", day {day}. You'll remember. I had reasons; they were bad ones. The people who leaned on me are gone now, and I find I still need someone reliable. Let me pay for my mistake.'",
            (c, p) => p.Crossed && p.Johnson != null && c.JohnsonBurned(p.Johnson) && Career.JohnsonById(p.Johnson) is JohnsonDef jd && c.Flag("fate:" + jd.Name) != Career.Fates["killed"],
            new[]
            {
                Fx(new Choice("Take the apology ({¥sorry}) and his calls again", (c, m) =>
                {
                    int sorry = Money(m, "sorry");
                    c.Cash += sorry;
                    c.Count("earned", sorry);
                    c.Set($"johnson:{m.Vars["jid"]}:burned", 0);
                    return $"{M.Money(sorry)} lands, and {m.Vars["johnson"]}'s jobs can come up on your board again. Whether you trust them is your business.";
                }), "+{¥sorry}; their jobs come back"),
                Fx(new Choice("Give the name to Mara Quill", (c, m) => { c.Fame(1); c.Karma += 2; c.KarmaEarned += 2; c.Inc("quill"); return "Quill runs a piece on Johnsons who sell out their runners. No names, but everyone knows. Awareness +1, karma +2."; }), "awareness +1, karma +2"),
                Ok("Never call this number again", "You block it. Some bridges are better burned."),
            },
            more: (c, p) =>
            {
                JohnsonDef jd = Career.JohnsonById(p.Johnson) ?? Career.Johnsons[0];
                return new[] { ("johnson", jd.Name), ("jid", jd.Id), ("sorry", (Math.Max(500, p.Pay / 2) / 50 * 50).ToString()) };
            });

        // A witness from a loud job has your description, and a buyer.
        yield return Echo("echo-witness", Hook.Morning, Juno, "Your description",
            "Juno: 'Remember \"{title}\" -- {place}, day {day}? Somebody across the street that night has your description, a good one, and a buyer at KBN. For {¥price} it never airs.'",
            (c, p) => p.Alarm && (p.Kills >= 3 || p.Type is JobType.Eliminate or JobType.Destroy),
            new[]
            {
                new Choice("Pay ({¥price})", (c, m) => { int pr = Money(m, "price"); c.Cash -= pr; c.Count("spent", pr); return "The description goes in a drawer at KBN that nobody opens."; },
                    (c, m) => c.Cash < Money(m, "price") ? $"Needs {M.Money(Money(m, "price"))}." : null, "-{¥price}"),
                Past("cop", "Call in an old favour at Lone Star", (c, m) => "Somebody at Lone Star tells KBN the witness is unreliable. Nothing airs."),
                Fx(new Choice("Let it air", (c, m) => { c.Fame(2); c.Heat = MathF.Min(5, c.Heat + 1); return "Your face on the evening bulletin, very nearly right. Awareness +2, heat +1."; }), "awareness +2, heat +1"),
            },
            call: true, more: (c, p) => new[] { ("price", ((300 + 300 * p.Threat) / 50 * 50).ToString()) });

        // The kids in the back room of a den you cleared.
        yield return Echo("echo-kid", Hook.Morning, "A kid on a bike", "You don't remember me",
            "A kid on a bike with no brakes catches you at the door. 'You don't remember me. {place}, day {day}. \"{title}\". The back room -- you let us go.' Thirteen now, maybe. 'I run messages. Anything you need to know, I'll hear it first.'",
            (c, p) => p.Tags.Contains("kids:free") && c.Flag("echo:runner") == 0,
            new[] { Ok("Tell the kid to stay off the corners", "'I am off the corners. I'm on the roofs.'") },
            fire: (c, m) => { c.Set("echo:runner", c.Day); c.Set("echo:runnerjob", int.Parse(m.Vars["mem"])); });

        // And then, every week or so, the kid's tip.
        yield return new("echo-tip", Hook.Morning, "The kid from {place}", "Heard something", "{text}",
            c => c.Flag("echo:runner") > 0 && c.Day - c.Flag("echo:runner") >= 3, Weight: 0.8f, Cooldown: 6,
            Bind: c =>
            {
                PastJob p = c.Memories.LastOrDefault(x => x.Id == c.Flag("echo:runnerjob"));
                int kind = (int)(Career.Stable("tip:" + c.Day) % 3);
                string thing = kind == 2 ? c.WantSomething(new Rng(c.Seed ^ (ulong)c.Day * 0xEC0UL)) : "";
                return V(("place", p?.Place ?? "the den"), ("kind", kind.ToString()), ("text", kind switch
                {
                    0 => "'Lone Star car parked on your block since four. They're bored, not looking. Go out the back.' Heat drops.",
                    1 => "'My cousin cleans the place you're going next. Here's how the rooms go.' The floor plans of your next job's site.",
                    _ => $"'Wen's got a buyer who wants a {thing}. Pays double. This week.'",
                }));
            },
            Fire: (c, m) =>
            {
                if (m.Vars["kind"] == "0") c.Heat = MathF.Max(0, c.Heat - 1);
                else if (m.Vars["kind"] == "1") c.Set("freeschem");
            });
    }
}
