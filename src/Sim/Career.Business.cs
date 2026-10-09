using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// The businesses you own have troubles of their own (Storylets.Business.cs
/// supplies the words): a shakedown by whoever holds the street, an inspector
/// with his hand out, staff with their hand in the till. What they cost lands
/// here, in the week's takings, and every line of the payday says why.
/// All state is flags, keyed by the business id:
/// bizcut (the faction taking a cut, +1), bizcutpct, bizsafe (day the
/// shakedowns stop until), bizshut/bizshutfrom (the days it is closed),
/// bizclean (day the inspector stays away until), bizskim (day the skim
/// began), bizrehire (next week at half), bizweeks (paydays owned).
/// </summary>
public sealed partial class Career
{
    public static (string id, string name, string desc, int cost, int week, string district) BusinessOf(string id) => Businesses.First(b => b.id == id);

    /// <summary>Is this business closed, or about to be (a closure starts the day after it is decided)?</summary>
    public bool BizShut(string id) => Flag("bizshut:" + id) >= Day;

    /// <summary>Close a business for the next <paramref name="days"/> days (a closure already running is extended instead).</summary>
    public void ShutBusiness(string id, int days)
    {
        int until = Day + days;
        if (BizShut(id)) { Set("bizshut:" + id, Math.Max(until, Flag("bizshut:" + id))); return; }
        Set("bizshutfrom:" + id, Day + 1);
        Set("bizshut:" + id, until);
    }

    /// <summary>The faction taking a weekly cut of this business, if any.</summary>
    public Faction? CutOf(string id) => Flag("bizcut:" + id) > 0 ? (Faction)(Flag("bizcut:" + id) - 1) : null;

    public void TakeCut(string id, Faction f, int pct)
    {
        Set("bizcut:" + id, (int)f + 1);
        Set("bizcutpct:" + id, pct);
    }

    /// <summary>A business that has been through a payday, isn't closed, and has no trouble on the board -- and no trouble anywhere for <see cref="Tables.BizGap"/> days.</summary>
    public IEnumerable<string> TroubleFree() =>
        Flag("biztrouble") > 0 && Day - Flag("biztrouble") < Tables.BizGap ? Enumerable.Empty<string>()
            : Property.Where(b => Flag("bizweeks:" + b) > 0 && !BizShut(b) && !Board.Any(j => j.Tag == "biz:" + b));

    /// <summary>Today's draw for business trouble: seeded by the day, never <see cref="Rng"/>.</summary>
    public bool TroubleToday() => new Rng(Seed ^ (ulong)Day * 0xB12B12UL ^ 0x7120B1EUL).Chance(Tables.BizChance);

    /// <summary>Which business this trouble lands on: one of those it fits, by the day.</summary>
    public string TroubleAt(string kind, Func<string, bool> fits)
    {
        var ok = TroubleFree().Where(fits).OrderBy(b => b).ToList();
        if (ok.Count == 0) return null;
        return ok[(int)(Stable(kind + Day) % (uint)ok.Count)];
    }

    /// <summary>The site a business's defence is fought at: its style.</summary>
    public static SiteStyle BizStyle(string id) => id switch { "garage" => SiteStyle.Warehouse, "club" => SiteStyle.Club, "node" => SiteStyle.Office, _ => SiteStyle.Den };

    /// <summary>Refusing a shakedown: they come to break the place; hold it.</summary>
    public Job DefendBusiness(string id)
    {
        var def = BusinessOf(id);
        Faction f = Holder(def.district);
        Job j = OfferSpecial(JobType.Defend, BizStyle(id), f, def.district, Math.Clamp(MaxThreat, 2, 5), $"Hold {def.name.ToLower()}",
            $"The {Tables.FactionNames[(int)f]} want a cut of {def.name.ToLower()} and you said no. They're sending a crew to break the place. Hold it until they give up -- by the end of day {Day + 2}, or it's shut for a week.",
            payMul: 0.8f, tag: "biz:" + id, expires: 2);
        if (j == null) ShutBusiness(id, Tables.BizWrecked);
        return j;
    }

    /// <summary>A defence of one of your businesses, settled.</summary>
    private void BusinessHeld(Job j, RunResult r, Debrief d)
    {
        if (j.Tag?.StartsWith("biz:") != true) return;
        string id = j.Tag[4..];
        if (!Property.Contains(id)) return;
        var def = BusinessOf(id);
        if (r.Success)
        {
            Set("bizsafe:" + id, Day + Tables.BizSafeDays);
            int was = StandingOf(j.Faction);
            Standing[j.Faction] = Math.Max(0, was - 2);
            d.Lines.Add((def.name, $"held: no shakedowns for {Tables.BizSafeDays} days; {Tables.FactionNames[(int)j.Faction]} standing -{was - StandingOf(j.Faction)}"));
        }
        else
        {
            ShutBusiness(id, Tables.BizWrecked);
            d.Lines.Add((def.name, $"wrecked: shut for {Tables.BizWrecked} days, no takings"));
        }
    }

    /// <summary>A defence you never went to: they broke the place.</summary>
    private void BusinessAbandoned(Job j)
    {
        if (j.Tag?.StartsWith("biz:") != true) return;
        string id = j.Tag[4..];
        if (!Property.Contains(id)) return;
        ShutBusiness(id, Tables.BizWrecked);
        Say($"Nobody held {BusinessOf(id).name.ToLower()}. They smashed it up: shut for {Tables.BizWrecked} days, no takings.");
    }

    /// <summary>How many of the last seven days (to today) this business was shut.</summary>
    public int ClosedThisWeek(string id)
    {
        int from = Flag("bizshutfrom:" + id), until = Flag("bizshut:" + id);
        if (until <= 0) return 0;
        return Math.Clamp(Math.Min(until, Day) - Math.Max(from, Day - 6) + 1, 0, 7);
    }

    /// <summary>One business's week: the takings, less whatever its troubles cost, and the line that says so.</summary>
    private void BusinessWeek(string id)
    {
        var def = BusinessOf(id);
        Inc("bizweeks:" + id);
        // A business on a street your enemies hold pays them first.
        Faction holder = Holder(def.district);
        string hn = Tables.FactionNames[(int)holder];
        bool leaned = Grudge.GetValueOrDefault(holder) >= 4;
        var why = new List<string>();
        float share = 1;
        int closed = ClosedThisWeek(id);
        if (closed > 0) { share *= (7 - closed) / 7f; why.Add($"shut {closed} day{(closed == 1 ? "" : "s")}"); }
        Faction? cut = CutOf(id);
        if (cut != null && cut != holder)
        {
            why.Add($"the {Tables.FactionNames[(int)cut]} don't hold the street any more, so nobody collects their cut");
            Set("bizcut:" + id, 0);
            Set("bizcutpct:" + id, 0);
            cut = null;
        }
        if (leaned) { share *= 0.5f; why.Add($"the {hn} took half"); }
        else if (cut != null) { int pct = Flag("bizcutpct:" + id); share *= 1 - pct / 100f; why.Add($"{pct} % to the {hn}"); }
        if (Flag("bizskim:" + id) > 0) { share *= 1 - Tables.BizSkim / 100f; why.Add($"the till is light, -{Tables.BizSkim} %"); }
        if (Flag("bizrehire:" + id) > 0) { share *= 0.5f; why.Add("new staff learning the job, half takings"); Set("bizrehire:" + id, 0); }
        int take = (int)(MathF.Round(def.week * share / 50) * 50);
        Cash += take;
        Count("earned", take);
        // A business that pays its way on their street makes friends of whoever runs it; one that pays them a cut, more so.
        int up = leaned || closed >= 7 ? 0 : cut != null ? 2 : 1;
        if (up > 0) Standing[holder] = Math.Min(10, StandingOf(holder) + up);
        string tail = up > 0 ? $"; the {hn} like a neighbour who pays (standing +{up})" : "";
        Say($"{def.name}: {M.Money(take)} this week{(why.Count > 0 ? $" ({string.Join("; ", why)})" : "")}{tail}.");
    }
}
