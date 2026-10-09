using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// Trouble at the businesses you own: a shakedown by whoever holds the
/// street, an inspector who wants paying, staff skimming the till. At most
/// one every <see cref="Tables.BizGap"/> days across all of them, on a
/// seeded draw of the morning; what each answer costs lands in
/// Career.Business.cs and shows on the payday line.
/// </summary>
public static partial class Storylets
{
    /// <summary>Who runs each business day to day, and who'd have their hand in the till.</summary>
    private static (string boss, string staff, string role) BizPeople(string id) => id switch
    {
        "garage" => ("Dutch, the shop foreman", "Kiko", "parts girl"),
        "club" => ("Lena Marsh, the club manager", "Rook", "head barman"),
        "node" => (Null, "Sixfold", "night sysop"),
        _ => (Mama, "Tobi", "night cook"),
    };

    private static Dictionary<string, string> BizVars(Career c, string id)
    {
        var def = Career.BusinessOf(id);
        Faction f = c.Holder(def.district);
        var (boss, staff, role) = BizPeople(id);
        return V(("biz", id), ("name", def.name.ToLower()), ("Name", def.name), ("fac", Tables.FactionNames[(int)f]), ("faci", ((int)f).ToString()),
            ("boss", boss), ("bossname", boss.Split(',')[0]), ("staff", staff), ("role", role), ("week", def.week.ToString()));
    }

    private static string Biz(Career c, string kind, Func<string, bool> fits) => c.TroubleAt(kind, fits);

    private static bool Shakeable(Career c, string b) =>
        c.CutOf(b) == null && c.Flag("bizsafe:" + b) < c.Day && c.Grudge.GetValueOrDefault(c.Holder(Career.BusinessOf(b).district)) < 4;

    private static IEnumerable<Storylet> BusinessTroubles()
    {
        static string Pick(Career c, string kind, Func<string, bool> fits) => Biz(c, kind, fits) ?? c.Property.FirstOrDefault() ?? "noodles";

        // ---- the shakedown ----
        Func<string, bool> shake(Career c) => b => Shakeable(c, b);
        yield return new("biz-shakedown", Hook.Morning, "{boss}", "Protection",
            "{bossname}: 'Three of the {fac} came into {name} last night. Very polite. They hold this street, they said, and a business on their street pays its way -- {cut} % of the week, every week. Or they come back with bats. They want an answer by tomorrow.'",
            c => c.CityRef != null && c.TroubleToday() && Biz(c, "shakedown", shake(c)) != null,
            new[]
            {
                Fx(new Choice("Pay them their {cut} %", (c, m) =>
                {
                    c.TakeCut(m.Vars["biz"], (Faction)int.Parse(m.Vars["faci"]), Tables.BizCut);
                    return "{bossname} hands over the first envelope. The {fac} are very friendly about it. Every payday they'll take their share, and think the better of you for it.";
                }), "-{cut} % of {name}'s takings every week while the {fac} hold the street; {fac} standing +2 a week instead of +1"),
                Fx(new Choice("Tell them no", (c, m) =>
                {
                    Job j = c.DefendBusiness(m.Vars["biz"]);
                    return j == null ? "You say no. Nobody's there when they come back. {Name} is shut for a week." : "It's on your board: be at {name} by the end of day " + j.Expires + " and hold it.";
                }), "a defend at {name} within 2 days: held, no shakedowns for " + Tables.BizSafeDays + " days but {fac} standing -2; lost or not there, shut " + Tables.BizWrecked + " days"),
                Fx(new Choice("({etq}) Talk them down to " + Tables.BizTalkCut + " %", (c, m) =>
                {
                    c.TakeCut(m.Vars["biz"], (Faction)int.Parse(m.Vars["faci"]), Tables.BizTalkCut);
                    return "You meet their lieutenant over tea and say it the way they say things. They settle for a token -- respect, more than money.";
                }, (c, m) => c.Talks(m.Vars["etq"]) ? null : $"Needs the {m.Vars["etq"]} etiquette."), "-" + Tables.BizTalkCut + " % a week instead; {fac} standing +2 a week instead of +1"),
            },
            Weight: 3, Cooldown: Tables.BizGap,
            Bind: c =>
            {
                var v = BizVars(c, Pick(c, "shakedown", shake(c)));
                v["cut"] = Tables.BizCut.ToString();
                v["etq"] = int.Parse(v["faci"]) == (int)Faction.Halcyon ? "Corporate" : "Gang";
                return v;
            },
            Fire: (c, m) => c.Set("biztrouble", c.Day), Call: true);

        yield return new("biz-held", Hook.Debrief, "{boss}", "Still open", "{bossname}: 'They came, they met you, they left. The regulars are already back at the counter. The {fac} won't try that again for a while.'",
            c => c.Last?.Job?.Tag?.StartsWith("biz:") == true && c.Last.Success, Cooldown: 0,
            Bind: c => BizVars(c, c.Last?.Job?.Tag?.StartsWith("biz:") == true ? c.Last.Job.Tag[4..] : c.Property.FirstOrDefault() ?? "noodles"));

        // ---- the inspector ----
        Func<string, bool> inspect(Career c) => b => c.Flag("bizclean:" + b) < c.Day;
        yield return new("biz-inspector", Hook.Morning, "Kessler Bay Licensing", "Notice of inspection",
            "{bossname}: 'A man from the council licensing office was at {name} this morning with a clipboard and a list. Fire exits, wiring, permits. He says he could overlook all of it for {¥bribe}. If not, he shuts us down for {days} days while we \"bring it up to code\".'{council}",
            c => c.CityRef != null && c.TroubleToday() && Biz(c, "inspector", inspect(c)) != null,
            new[]
            {
                new Choice("Pay him ({¥bribe})", (c, m) =>
                {
                    int bribe = Money(m, "bribe");
                    c.Cash -= bribe;
                    c.Count("spent", bribe);
                    return "He counts it, ticks every box on the list and tips his hat. Men like him always come back.";
                }, (c, m) => c.Cash < Money(m, "bribe") ? $"Needs {M.Money(Money(m, "bribe"))}." : null, "-{¥bribe} now; he'll be back"),
                Fx(new Choice("Refuse", (c, m) =>
                {
                    c.ShutBusiness(m.Vars["biz"], int.Parse(m.Vars["days"]));
                    c.Set("bizclean:" + m.Vars["biz"], c.Day + Tables.BizCleanDays);
                    return "The shutters come down with a council seal on them. {days} days. But the paperwork you file makes {name} the cleanest business on the street -- he won't bother coming back.";
                }), "{name} shut {days} days (about -{¥loss} at payday); no inspector for " + Tables.BizCleanDays + " days"),
                Fx(new Choice("(Security) Quote the fire code back at him", (c, m) =>
                    "You walk him through his own regulations, clause by clause, and mention which Lone Star sergeant you'd call about a council officer soliciting. He leaves without his envelope.",
                    (c, m) => !c.Talks("Security") ? "Needs the Security etiquette." : c.On("council") ? "The Lotus own the council this week: nobody's listening to the regulations." : null), "no bribe, no closure; he'll be back"),
            },
            Weight: 3, Cooldown: Tables.BizGap,
            Bind: c =>
            {
                var v = BizVars(c, Pick(c, "inspector", inspect(c)));
                int week = int.Parse(v["week"]);
                bool council = c.On("council");
                int days = council ? Tables.BizCouncilShut : Tables.BizInspectShut;
                v["bribe"] = ((int)(MathF.Round(week * Tables.BizBribe / 100f / 50) * 50)).ToString();
                v["days"] = days.ToString();
                v["loss"] = ((int)(MathF.Round(week * Math.Min(7, days) / 7f / 50) * 50)).ToString();
                v["council"] = council ? " (The Red Lotus own the council this week: his office will keep it shut twice as long, and nobody will hear a complaint.)" : "";
                return v;
            },
            Fire: (c, m) => c.Set("biztrouble", c.Day));

        // ---- the hand in the till ----
        Func<string, bool> skim(Career c) => b => c.Flag("bizskim:" + b) == 0 && c.Flag("bizrehire:" + b) == 0;
        yield return new("biz-skim", Hook.Morning, "{boss}", "The till is light",
            "{bossname}: 'The takings at {name} have been short four nights running. It's {staff}, the {role} -- I've seen it. Good with the customers, owes money to the wrong people. What do you want done?'",
            c => c.CityRef != null && c.TroubleToday() && Biz(c, "skim", skim(c)) != null,
            new[]
            {
                Fx(new Choice("Fire {staff}", (c, m) =>
                {
                    c.Set("bizrehire:" + m.Vars["biz"]);
                    return "{staff} is out the back door by noon. Finding someone who knows the job will take a while.";
                }), "the skim stops, but next week's takings are halved while you rehire"),
                Fx(Talk("Street", "Have a quiet word with {staff}", (c, m) =>
                {
                    int back = Money(m, "back");
                    c.Cash += back;
                    c.Count("earned", back);
                    return "You buy {staff} a drink and explain, quietly, how this goes. The money is back in the till by morning, with interest, and the hands stay out of it.";
                }), "+{¥back} back; the skim stops and {staff} stays"),
                Fx(new Choice("Let it go", (c, m) =>
                {
                    c.Set("bizskim:" + m.Vars["biz"], c.Day);
                    c.Set("bizskimtip:" + m.Vars["biz"], 0);
                    return "You let it go. {staff} knows you know. People who owe you are worth something in this city.";
                }), "-" + Tables.BizSkim + " % of {name}'s takings until you deal with it; {staff} owes you"),
            },
            Weight: 3, Cooldown: Tables.BizGap,
            Bind: c =>
            {
                var v = BizVars(c, Pick(c, "skim", skim(c)));
                v["back"] = ((int)(MathF.Round(int.Parse(v["week"]) * Tables.BizSkim * 2 / 100f / 50) * 50)).ToString();
                return v;
            },
            Fire: (c, m) => c.Set("biztrouble", c.Day));

        // The skimmer you let keep their job pays it back in their own coin.
        static string Owing(Career c) => c.Property.Where(b => c.Flag("bizskim:" + b) > 0 && c.Day - c.Flag("bizskim:" + b) >= Tables.BizSkimTip && c.Flag("bizskimtip:" + b) == 0).OrderBy(b => b).FirstOrDefault();
        yield return new("biz-skim-tip", Hook.Morning, "{staff}", "I owe you one",
            "{staff}, the {role} at {name}: 'You knew, and you let me keep the job. So here's something. Whatever you hit next, I know someone who's been inside -- the floor plan's on your deck.' (Your next job comes with its schematics.)",
            c => Owing(c) != null,
            new[]
            {
                Ok("Keep them on", "{staff} keeps the job, and keeps a little of the till. You know what you're paying for now."),
                Fx(new Choice("Thank them -- then let them go", (c, m) =>
                {
                    c.Set("bizskim:" + m.Vars["biz"], 0);
                    c.Set("bizrehire:" + m.Vars["biz"]);
                    return "You take the plans, and then {staff}'s keys. No hard feelings, as far as anyone says.";
                }), "the skim stops; next week's takings are halved while you rehire"),
            },
            Cooldown: 0,
            Bind: c => BizVars(c, Owing(c) ?? c.Property.FirstOrDefault() ?? "noodles"),
            Fire: (c, m) => { c.Set("bizskimtip:" + m.Vars["biz"]); c.Set("freeschem"); });
    }
}
