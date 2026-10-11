using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// The career receipt, printed when a career ends (a death, a retirement):
/// what it lasted, what it made, who you saved and lost, what you finished,
/// who runs the streets now, and what the radio says about you.
/// </summary>
public sealed partial class Career
{
    public List<(string what, string value)> Receipt()
    {
        var r = new List<(string, string)>
        {
            ("Days on the street", $"{Day}"),
            ("Time played", M.Played(PlaySeconds)),
            ("Jobs done / failed", $"{JobsDone} / {JobsFailed}"),
            ("Earned", M.Money(Stat("earned"))),
            ("Spent", M.Money(Stat("spent"))),
            ("Karma earned", $"{KarmaEarned}"),
            ("Kills", $"{Kills}"),
        };
        int saved = Flags.Count(kv => kv.Key.StartsWith("fate:") && kv.Value == Fates["rescued"]);
        int dead = Flags.Count(kv => kv.Key.StartsWith("fate:") && kv.Value is var v && (v == Fates["dead"] || v == Fates["killed"]));
        r.Add(("People you got out", $"{saved}"));
        r.Add(("People who didn't make it", $"{dead}"));
        int spared = Stat("spared");
        if (spared > 0) r.Add(("Guards you let live", $"{spared}"));
        var lost = Crew.Where(kv => kv.Value.Dead).Select(kv => Tables.Hires.First(h => h.id == kv.Key).name).ToList();
        if (lost.Count > 0) r.Add(("Crew buried", string.Join(", ", lost)));
        var stories = new List<string>();
        if (Flag("story:won") > 0) stories.Add("the Iron Spire");
        foreach (CampaignDef c in Campaigns) if (Flag(c.Id + ":won") > 0) stories.Add(c.Name);
        if (Flag("tide:won") > 0) stories.Add("Toxic Tide");
        r.Add(("Stories finished", stories.Count == 0 ? "none" : string.Join(", ", stories)));
        if (Property.Count > 0) r.Add(("Property", string.Join(", ", Property.Select(p => Businesses.First(b => b.id == p).name))));
        r.Add(("Lived", Lifestyles[LifeTier].name));
        foreach (DistrictDef d in Tables.Districts) r.Add(($"The {d.Name}", Tables.FactionNames[(int)Holder(d.Id)]));
        r.Add(("Street rep / notoriety / awareness", $"{Rep} / {Notoriety} / {Awareness}"));
        return r;
    }

    /// <summary>The last thing the radio says about you.</summary>
    public string Obituary(bool died) =>
        (died, Notoriety >= 6, Awareness >= 6) switch
        {
            (true, true, _) => $"KBN: 'Police confirm the death of the runner known as {Name}. Officers describe the city as \"noticeably quieter\".'",
            (true, _, true) => $"KBN: 'Tributes on the Neon Strip tonight for {Name}, a runner half the city knew by name and nobody knew by face.'",
            (true, _, _) => $"Pirate radio, three in the morning: 'Somebody called {Name} didn't come home tonight. Nobody's saying more. Light a candle.'",
            (false, true, _) => $"KBN: '{Name}, wanted in connection with a long list of things, appears to have left the city. Lone Star says good riddance.'",
            (false, _, true) => $"KBN: 'The legend of {Name} grows, now they're gone. Every bar in the Barrens claims to have poured them a drink.'",
            _ => $"Pirate radio: '{Name} got out. Nobody gets out. Good for them.'",
        };
}
