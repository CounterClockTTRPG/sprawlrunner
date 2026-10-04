using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>Something going on in the city for a few days (a stadium night, a gang war, a blackout...).</summary>
public sealed class Happening
{
    public string Id;
    public int Start, End;
    /// <summary>Where, for the ones that happen in one district; null is everywhere.</summary>
    public string District;
}

/// <summary>
/// The city between runs: who holds which district (turf), what each faction
/// is working towards (clocks), what is going on this week (happenings),
/// rent, and the news. Everything moves a little every morning and more when
/// you push it.
/// </summary>
public sealed partial class Career
{
    /// <summary>Each district's share for each faction (Vipers, Halcyon, Lotus), summing to 1.</summary>
    public Dictionary<string, float[]> Turf = new();
    /// <summary>Faction plans, 0..8: vipers, halcyon, lotus, lonestar.</summary>
    public Dictionary<string, int> Clocks = new();
    public List<Happening> Happenings = new();
    /// <summary>Rent not yet paid.</summary>
    public int RentOwed;

    public const int ClockFull = 8;
    public static readonly string[] ClockIds = { "vipers", "halcyon", "lotus", "lonestar" };

    public static readonly (string id, string name, string desc)[] HappeningDefs =
    {
        ("stadium", "Stadium night", "half of Lone Star is at Kessler Stadium: heat cools twice as fast."),
        ("surge", "Mana surge", "the astral is running high: spells hit 20 % harder."),
        ("quarterly", "Corporate quarterly", "Halcyon security on overtime: their new jobs get double patrols and pay 25 % more."),
        ("grid", "Grid failure", "the power is out: every site in the district is under blackout."),
        ("strike", "Lone Star strike", "no police chases -- and the gangs know it: ambushes twice as likely."),
        ("lanterns", "Lantern festival", "the night market's vendors sell at half price."),
        ("gangwar", "Gang war", "Vipers and Lotus at each other's throats: their jobs pay 20 % more, and someone follows you home."),
        ("expansion", "Halcyon expansion", "Halcyon-Matsuda is buying up the Docks and Industrial: elite security on their new sites."),
        ("council", "Council in the pocket", "the Red Lotus own the council this week: busts cost double, jobs against the Lotus pay 25 % more."),
        ("crackdown", "Crackdown", "Lone Star is cracking down: checkpoints close avenue blocks (stop and be waved through, if you're clean -- hot, and they come after you), heat cools at half speed, the informant costs three times as much."),
    };

    public static (string id, string name, string desc) HappeningDef(string id) => HappeningDefs.First(h => h.id == id);

    /// <summary>Is this going on today (in this district, for the local ones)?</summary>
    public bool On(string id, string district = null)
    {
        // Called every frame by the streets: no LINQ here.
        for (int i = 0; i < Happenings.Count; i++)
        {
            Happening h = Happenings[i];
            if (h.Id == id && h.Start <= Day && Day <= h.End && (district == null || h.District == null || h.District == district)) return true;
        }
        return false;
    }

    public IEnumerable<Happening> Today => Happenings.Where(h => h.Start <= Day && Day <= h.End);

    /// <summary>One line per happening today, for the terminal and the HUD.</summary>
    public IEnumerable<string> HappeningLines() => Today.Select(h =>
    {
        var d = HappeningDef(h.Id);
        return $"TODAY: {d.name}{(h.District != null ? $" in the {Tables.District(h.District).Name}" : "")} -- {d.desc}";
    });

    private void Begin(string id, int days, string district = null, string news = null)
    {
        if (On(id, district)) return;
        Happenings.Add(new Happening { Id = id, Start = Day, End = Day + days - 1, District = district });
        Explain("happening", "Something is going on in the city: it changes the rules for a few days. The terminal and the PDA's City tab say what.");
        var d = HappeningDef(id);
        Say(news ?? $"KBN: {d.name}{(district != null ? $" in the {Tables.District(district).Name}" : "")} -- {d.desc}");
    }

    // ---- turf ------------------------------------------------------------------------

    /// <summary>The district's own mix: what it drifts back to.</summary>
    public static float[] Mix(DistrictDef d)
    {
        // The first faction listed is the district's own: it starts with the edge.
        var m = new float[3];
        foreach (Faction f in d.Factions) m[(int)f] += 1;
        m[(int)d.Factions[0]] += 0.5f;
        float sum = m.Sum();
        for (int k = 0; k < 3; k++) m[k] /= sum;
        return m;
    }

    public float[] TurfOf(string district)
    {
        if (!Turf.TryGetValue(district, out float[] t) || t == null || t.Length != 3) Turf[district] = t = Mix(Tables.District(district));
        return t;
    }

    /// <summary>
    /// Who runs a district: whoever ran it until someone else holds clearly
    /// more (5 points), so a close district doesn't change hands every job.
    /// </summary>
    public Faction Holder(string district)
    {
        float[] t = TurfOf(district);
        int held = Flag("held:" + district) - 1;
        if (held < 0) held = (int)Tables.District(district).Factions[0];
        int best = 0;
        for (int k = 1; k < 3; k++) if (t[k] > t[best]) best = k;
        return t[best] > t[held] + 0.05f ? (Faction)best : (Faction)held;
    }

    /// <summary>A faction for a new job in this district, by turf.</summary>
    public Faction PickFaction(string district, Rng r)
    {
        float[] t = TurfOf(district);
        float roll = r.Float();
        for (int k = 0; k < 3; k++) { roll -= t[k]; if (roll < 0) return (Faction)k; }
        return Holder(district);
    }

    /// <summary>A faction loses ground in a district; the others share it by how strong they are there.</summary>
    public void Push(string district, Faction loser, float amount)
    {
        float[] t = TurfOf(district);
        Faction before = Holder(district);
        float take = MathF.Min(amount, t[(int)loser]);
        t[(int)loser] -= take;
        float w = 0;
        for (int k = 0; k < 3; k++) if (k != (int)loser) w += t[k] + 0.05f;
        for (int k = 0; k < 3; k++) if (k != (int)loser) t[k] += take * (t[k] + 0.05f) / w;
        Normalise(t);
        Changed(district, before);
    }

    private static void Normalise(float[] t)
    {
        float sum = t.Sum();
        for (int k = 0; k < 3; k++) t[k] = sum > 0 ? t[k] / sum : 1 / 3f;
    }

    private void Changed(string district, Faction before)
    {
        Faction now = Holder(district);
        if (now == before) return;
        Explain("turf", "A district changed hands. Whoever holds it decides who hires you there -- and who comes after you. The PDA's City tab and the map (1) show it.");
        Set("held:" + district, (int)now + 1);
        Set("heldday:" + district, Day);
        Say($"KBN: The {Tables.FactionNames[(int)now]} now run the {Tables.District(district).Name}; the {Tables.FactionNames[(int)before]} have lost their grip.");
    }

    private void DriftTurf()
    {
        foreach (DistrictDef d in Tables.Districts)
        {
            float[] t = TurfOf(d.Id), mix = Mix(d);
            Faction before = Holder(d.Id);
            for (int k = 0; k < 3; k++) t[k] += (mix[k] - t[k]) * 0.25f;
            Normalise(t);
            Changed(d.Id, before);
        }
    }

    // ---- clocks ------------------------------------------------------------------------

    public int Clock(string id) => Clocks.GetValueOrDefault(id);

    public static string ClockOf(Faction f) => f switch { Faction.Vipers => "vipers", Faction.Halcyon => "halcyon", _ => "lotus" };

    public void Tick(string id, int by)
    {
        int v = M.Clamp(Clock(id) + by, 0, ClockFull);
        Clocks[id] = v;
        if (v >= 5) Explain("clock", "Somebody is planning something big. Street kids and Null hear about it first; jobs against them set it back.");
        if (v < ClockFull) return;
        Clocks[id] = 2;
        switch (id)
        {
            case "vipers": Begin("gangwar", 4, news: "KBN: Open war on the streets -- the Ash Vipers have hit Red Lotus fronts across the city. Stay indoors."); break;
            case "halcyon":
                Begin("expansion", 4, news: "KBN: Halcyon-Matsuda announces 'urban renewal' in the Docks and Industrial. Residents not consulted.");
                foreach (string d in new[] { "docks", "industrial" })
                {
                    float[] t = TurfOf(d);
                    Faction before = Holder(d);
                    t[(int)Faction.Halcyon] += 0.1f;
                    Normalise(t);
                    Changed(d, before);
                }
                break;
            case "lotus": Begin("council", 4, news: "KBN: Three councillors vote to drop the Neon Strip inquiry. All three were seen at a Red Lotus club last week."); break;
            default: Begin("crackdown", 4, news: "KBN: Lone Star promises 'zero tolerance' after a week of shadowrunner violence. Checkpoints go up tonight."); break;
        }
    }

    // ---- mornings -----------------------------------------------------------------------

    /// <summary>A day begins: the city moves, the rent comes due, the news, and whoever wants to talk.</summary>
    private void Morning(City city)
    {
        Happenings.RemoveAll(h => h.End < Day);
        var r = new Rng(Seed ^ (ulong)Day * 0xC17E5UL ^ 0xFEED);
        // The calendar.
        if (Day % 7 == 3) Begin("stadium", 1, news: "KBN: Kessler Bay plays at home tonight; Lone Star has half its cars at the stadium.");
        if (Day % 14 == 10) Begin("quarterly", 2);
        if (Day % 15 == 0) Begin("lanterns", 1, news: "KBN: Lanterns over the night market -- the festival is on, and the stalls are cutting prices.");
        if (r.Chance(1 / 12f)) Begin("surge", 2, news: "The talismongers are whispering: the astral is running high tonight.");
        if (r.Chance(1 / 15f)) Begin("grid", 2, r.Pick(Tables.Districts).Id);
        if (r.Chance(1 / 20f)) Begin("strike", 2, news: "KBN: Lone Star officers walk out over pay. 'Essential patrols only.' The gangs are celebrating.");
        // Faction plans.
        if (Day % 3 == 0) foreach (string f in new[] { "vipers", "halcyon", "lotus" }) Tick(f, 1);
        Tick("lonestar", Awareness >= 4 ? 1 : -1);
        // The week turns.
        if ((Day % 4 == 0 || ArcPath("press") == 1 && Day % 2 == 0) && Awareness > 0) Awareness--;
        if (Day % 7 == 0)
        {
            DriftTurf();
            Rent();
            Payday();
        }
        // How you live, every morning.
        if (Lifestyle == "squatter" && Day % 3 == 0) Heat = MathF.Max(Heat, 0.5f);
        if (LifeTier >= 3) WellFed = true;
        if (RentOwed > 0 && Cash >= RentOwed)
        {
            Cash -= RentOwed;
            Count("spent", RentOwed);
            Say($"Paid the rent you owed ({M.Money(RentOwed)}).");
            RentOwed = 0;
            Set("rentlate", 0);
        }
        // Fame follows you home; so does a landlady with a grudge.
        float known = MathF.Max(0, (Awareness - 3) * 0.5f) + (Flag("rentlate") >= 2 ? 2 : 0);
        if (known > Heat) Heat = MathF.Min(5, known);
        Tell(Hook.Morning, max: 1);
    }

    public int RentDue => (int)(Lifestyles[LifeTier].week * (HasUpgrade("loft") ? 1.5f : 1f) / 50) * 50;

    private void Rent()
    {
        Explain("rent", "Rent comes out every seventh day. How much depends on how you live (the safehouse's Upgrades tab).");
        int rent = RentDue;
        if (Cash >= rent)
        {
            Cash -= rent;
            Count("spent", rent);
            Say($"Rent paid: {M.Money(rent)}.");
            return;
        }
        RentOwed += rent;
        Inc("rentlate");
        Say($"Couldn't make the rent ({M.Money(rent)}). Mrs. Halloran will want a word.");
    }

    // ---- after a job --------------------------------------------------------------------

    /// <summary>What happened on the job, read off the result before anything is paid.</summary>
    private LastRun Settle(Job j, RunResult r)
    {
        var last = new LastRun { Job = j, Success = r.Success, Alarm = r.Alarm, Died = r.Died, Kills = r.Kills, Trap = r.Trap, Stiffed = r.Success && j.Stiff, Pay = (int)(j.Pay * r.PayMul), Choices = new List<string>(r.Choices) };
        if (j.Type == JobType.Eliminate) last.Fate = r.TargetTaken ? "taken" : r.Success ? "killed" : r.TargetFled ? "fled" : null;
        if (j.Type == JobType.Rescue) last.Fate = r.Success ? "rescued" : r.HostageDead ? "dead" : null;
        if (r.ExfilBurned)
        {
            string fled = Flags.Where(kv => kv.Key.StartsWith("fate:") && kv.Value == Fates["fled"]).Select(kv => kv.Key[5..]).FirstOrDefault();
            last.Exfil = j.DoubleCross ? "johnson" : Heat >= 2 ? "heat" : fled != null ? "fled:" + fled
                : j.Hired != null && CrewTrust(j.Hired) <= 1 ? "crew:" + j.Hired : "scanner";
        }
        return last;
    }

    public static readonly Dictionary<string, int> Fates = new() { ["killed"] = 1, ["fled"] = 2, ["taken"] = 3, ["rescued"] = 4, ["dead"] = 5 };

    public static string ExfilWhy(string cause) => cause switch
    {
        "johnson" => "the Johnson sold the exit",
        "heat" => "Lone Star had your car's plate",
        "scanner" => "a Lone Star scanner caught your comms",
        _ when cause.StartsWith("fled:") => $"{cause[5..]} was watching",
        _ when cause.StartsWith("crew:") => $"{Tables.Hires.First(h => h.id == cause[5..]).name} talked",
        _ => "someone talked",
    };

    /// <summary>After the debrief is drawn up: fates, fame, the city's answer, and the call that follows.</summary>
    private void Aftermath(Job j, RunResult r, Debrief d)
    {
        LastRun last = Last;
        if (last.Fate != null && j.Target != null && j.Type is JobType.Eliminate or JobType.Rescue && j.Story == 0)
        {
            Set("fate:" + j.Target, Fates[last.Fate]);
            Set("fateday:" + j.Target, Day);
        }
        if (last.Exfil != null)
        {
            d.Lines.Add(("Exfil burned", ExfilWhy(last.Exfil)));
            Set("burned:" + last.Exfil.Split(':')[0], Day);
        }
        if (last.Trap || last.Stiffed)
        {
            Set("crossed", Day);
            Set("crossedpay", Math.Max(500, j.Pay));
            Set("crossedby", (int)j.Faction + 1);
        }
        // What you decided on the run, and how you treated the ones who gave up.
        if (r.Choices.Contains("begs:1"))
        {
            Karma += 3;
            KarmaEarned += 3;
            Set("friend:" + j.Target);
            Set("fate:" + j.Target, Fates["rescued"]);
            Set("fateday:" + j.Target, Day);
            d.Lines.Add(($"You let {j.Target} go", "karma +3"));
        }
        if (r.Spared > 0) { Inc("spared", r.Spared); d.Lines.Add(("Guards you spared", $"{r.Spared}")); }
        if (r.KilledHelpless > 0) { Infamy(r.KilledHelpless); d.Lines.Add(("Shot the ones who gave up", $"notoriety +{r.KilledHelpless}")); }
        // A ghoul nest or a hack belongs to nobody's turf.
        bool placed = j.District != null && Tables.Districts.Any(x => x.Id == j.District) && j.Type is not (JobType.Bounty or JobType.Hack);
        // Fame.
        if (j.Type is not (JobType.Courier or JobType.Race or JobType.Hub))
        {
            int fame = (r.Alarm && j.Threat >= 3 ? 1 : 0) + (r.Kills >= 10 ? 1 : 0) + (r.Bosses > 0 ? 1 : 0);
            if (fame > 0) { Fame(fame); d.Lines.Add(("Public awareness", $"+{fame} (now {Awareness})")); }
            if ((r.Alarm || r.Kills >= 5 || j.Threat >= 4) && placed) Headline(j, r);
        }
        // The city answers.
        if (j.Type is not (JobType.Courier or JobType.Race or JobType.Hub) && !r.Aborted)
        {
            // What the job did to the city, on the receipt.
            if (r.Success && placed)
            {
                float push = j.Type is JobType.Clear or JobType.Destroy ? 0.08f : 0.05f;
                Push(j.District, j.Faction, push);
                d.Lines.Add(($"{Tables.FactionNames[(int)j.Faction]} turf, {Tables.District(j.District).Name}", $"-{push * 100:0} %"));
                Tick(ClockOf(j.Faction), j.Type is JobType.Destroy or JobType.Datasteal ? -2 : -1);
                d.Lines.Add(($"{Tables.FactionNames[(int)j.Faction]} plans", "set back"));
            }
            if (!r.Aborted && j.Type is not JobType.Bounty) d.Lines.Add(($"{Tables.FactionNames[(int)j.Faction]} grudge", $"{Grudge.GetValueOrDefault(j.Faction)}"));
        }
        CrewAfter(j, r, d);
        JohnsonAfter(j, r, d);
        StandingAfter(j, r, d);
        Tell(Hook.Debrief);
    }

    private void Headline(Job j, RunResult r)
    {
        string where = Tables.District(j.District).Name;
        string[] lines = j.Faction switch
        {
            Faction.Halcyon => new[]
            {
                $"Halcyon-Matsuda denies any incident at its {where} facility; a spokesperson cites 'a routine fire drill'.",
                $"Shots fired at a Halcyon site in the {where}. Halcyon: 'No assets were compromised. The incident is closed.'",
                $"Halcyon-Matsuda shares dip after reports of a break-in in the {where}. 'Fake news,' says the board.",
            },
            Faction.Vipers => new[]
            {
                $"Gang violence flares in the {where}: {Math.Max(2, r.Kills)} dead at an Ash Viper hangout. Lone Star 'monitoring the situation'.",
                $"Residents of the {where} report gunfire 'for most of the night'. The Ash Vipers decline to comment.",
            },
            _ => new[]
            {
                $"A Red Lotus club in the {where} is closed 'for renovations' after a night of gunfire.",
                $"Lone Star finds the back rooms of a {where} nightclub 'in disarray'. The owners say nothing happened.",
            },
        };
        Say("KBN: " + lines[(int)(j.Seed % (ulong)lines.Length)]);
    }

    /// <summary>What the street kids know about the city right now (null: nothing worth a coin).</summary>
    public string Gossip()
    {
        var tips = new List<string>();
        if (Clock("vipers") >= 5) tips.Add("The Vipers are buying guns by the crate. Something big's coming with the Lotus.");
        if (Clock("halcyon") >= 5) tips.Add("Halcyon surveyors all over the Docks. My uncle says they're buying the street.");
        if (Clock("lotus") >= 5) tips.Add("Lotus money's going to the council. Watch, the cops'll go soft on 'em.");
        if (Clock("lonestar") >= 5) tips.Add("Lone Star's been asking about runners. About you, maybe. They're gearing up.");
        if ((Day + 1) % 7 == 3) tips.Add("Big game at the stadium tomorrow. Half the cops'll be watching it.");
        if ((Day + 1) % 15 == 0) tips.Add("Lantern festival tomorrow! Everything at the stalls goes half price.");
        foreach (DistrictDef d in Tables.Districts)
        {
            float[] t = TurfOf(d.Id);
            var top = t.Select((v, i) => (v, i)).OrderByDescending(x => x.v).ToList();
            if (top[0].v - top[1].v < 0.08f) tips.Add($"The {Tables.District(d.Id).Name} could go either way -- {Tables.FactionNames[top[0].i]} or {Tables.FactionNames[top[1].i]}.");
        }
        string fled = Flags.Where(kv => kv.Key.StartsWith("fate:") && kv.Value == Fates["fled"]).Select(kv => kv.Key[5..]).FirstOrDefault();
        if (fled != null) tips.Add($"{fled}'s still around, you know. Asking who you are.");
        if (tips.Count == 0) return null;
        return tips[(int)((ulong)(Day * 31 + Rep) % (ulong)tips.Count)];
    }

    /// <summary>The chance gangers follow you home from this job.</summary>
    public float AmbushChance(Job j)
    {
        float p = 0.35f;
        if (On("strike")) p *= 2;
        if (On("gangwar") && j.Faction is Faction.Vipers or Faction.Lotus) p = MathF.Max(p, 0.6f);
        if (Flag("block:lookout") > 0) p *= 0.5f;
        // Friends of whoever holds the street, or wearing Viper colours against Vipers: nobody waits for you.
        if (j.District != null && Tables.Districts.Any(d => d.Id == j.District) && Holder(j.District) != j.Faction && StandingOf(Holder(j.District)) >= Tables.StandingSafe) p *= 0.25f;
        if (j.Faction == Faction.Vipers && HasFactionItem("colours")) p = 0;
        return MathF.Min(0.9f, p);
    }
}
