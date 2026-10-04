using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>Legwork before a run: what the briefing sells, and TraumaLink's better contracts.</summary>
public sealed partial class Career
{
    public static string LegworkName(string id) => id switch
    {
        "bribe" => "two guards called in sick",
        "routes" => "the patrol routes",
        "power" => "the power is cut",
        "door" => "the locked doors are open",
        _ => id,
    };

    /// <summary>What can be bought for this job before going in: (id, name, what it does, price, why not).</summary>
    public List<(string id, string name, string desc, int price, string block)> LegworkOffers(Job j)
    {
        var list = new List<(string, string, string, int, string)>();
        if (j == null || j.Driving || j.Type is JobType.Hub or JobType.Hack or JobType.Defend) return list;
        string Had(string id) => j.Legwork.Contains(id) ? "Done." : null;
        int bribe = (int)(150 * j.Threat * (Talks("Security") ? 0.5f : 1f));
        list.Add(("bribe", "Bribe a guard", $"Two of the rank and file call in sick tonight.{(Talks("Security") ? " (Security etiquette: half price.)" : "")}", bribe, Had("bribe") ?? (Cash < bribe ? $"Needs {M.Money(bribe)}." : null)));
        int routes = 100 * j.Threat;
        list.Add(("routes", "Buy the patrol routes", "Everyone on site shows on your screen for the first 15 seconds.", routes, Had("routes") ?? (Cash < routes ? $"Needs {M.Money(routes)}." : null)));
        int power = S(Skill.Electronics) >= 2 ? 0 : 250 * j.Threat;
        list.Add(("power", "Cut the power at the substation", $"A blackout: no cameras, everyone sees less (you too).{(power == 0 ? " (Electronics 2: you do it yourself, free.)" : "")}", power, Had("power") ?? (j.Has(RunMod.Blackout) ? "Already dark." : Cash < power ? $"Needs {M.Money(power)}." : null)));
        list.Add(("door", "A cleaner on the inside", "Juno's cleaner leaves every locked door unlocked.", 0, Had("door") ?? (Loyalty.GetValueOrDefault("fixer") < 3 ? "Needs trust 3 with Juno." : null)));
        return list;
    }

    public string BuyLegwork(Job j, string id)
    {
        var o = LegworkOffers(j).FirstOrDefault(x => x.id == id);
        if (o.id == null) return "Not for this job.";
        if (o.block != null) return o.block;
        Cash -= o.price;
        Count("spent", o.price);
        j.Legwork.Add(id);
        return null;
    }

    /// <summary>You shot at a trauma team: contracts cost double until you make amends.</summary>
    public bool TraumaGrudge => Flag("tlgrudge") > 0;
    public bool TraumaPlatinum => Flag("tlplat") > 0;
}

/// <summary>Towers and prestige jobs: the late game's high buildings.</summary>
public sealed partial class Career
{
    /// <summary>A runner the city's hardest sites will hire (threat 6).</summary>
    public bool Prestige => Rep >= 18 || Flag("story:won") > 0 && Rep >= 14;

    /// <summary>
    /// Big corporate buildings go up: half the threat-5 offices and labs, and
    /// every threat-6 job, are towers -- more floors for prestige, harder the
    /// higher you climb, and paid for every one of them.
    /// </summary>
    private void MakeTower(Job j, City city)
    {
        if (j.Driving || j.Type is JobType.Defend or JobType.Hack or JobType.Bounty or JobType.Hub or JobType.Escort) return;
        var r = new Rng(j.Seed ^ 0x70E4UL);
        if (j.Threat >= 6)
        {
            j.Style = r.Chance(0.3f) ? SiteStyle.Lab : SiteStyle.Office;
            j.Faction = Faction.Halcyon;
            j.Floors = r.Range(4, 6);
            if (!j.Has(RunMod.Elite)) j.Mods.Add(RunMod.Elite);
            j.Mods.Remove(RunMod.Skeleton);
        }
        else if (j.Threat == 5 && j.Style is SiteStyle.Office or SiteStyle.Lab && r.Chance(0.5f)) j.Floors = r.Range(2, 4);
        else return;
        j.Pay = (int)(MathF.Round(j.Pay * (1 + 0.3f * j.Floors) / 50) * 50);
        j.Karma += j.Floors * 2;
        int secure = j.Floors >= 3 ? 2 : 1;
        j.Brief += $" It's a tower: {j.Floors} floors above the lobby, more guards on every one. Hack the elevator for the public floors; the top {secure} need a security pass -- override it in the Matrix, or fight your way up the stairs.";
        if (!j.Title.StartsWith("TOWER")) j.Title = $"TOWER: {j.Title}";
        Explain("tower", "A tower on the board: many floors, harder the higher you go, paid for every one. Hack the elevator, or fight up the stairs.");
    }
}
