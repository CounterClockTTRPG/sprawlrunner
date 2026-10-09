using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// The city talks back: the car radio (KBN news, the Lone Star scanner, a
/// pirate station that has heard of you) and rooftop graffiti about whoever
/// runs a district -- and, once you are famous, about you.
/// </summary>
public sealed partial class Career
{
    /// <summary>What's on the radio right now, newest first: (station, words).</summary>
    public List<(string station, string text)> Radio(string district, bool chased)
    {
        var r = new List<(string, string)>();
        string here = Tables.Districts.FirstOrDefault(d => d.Id == district)?.Name ?? "the city";
        foreach (string n in News.AsEnumerable().Reverse().Where(n => n.Contains("KBN:")).Take(4))
            r.Add(("KBN", n[(n.IndexOf("KBN:") + 4)..].Trim()));
        if (On("strike")) r.Add(("SCANNER", "...static. Lone Star is on strike."));
        else if (chased || Heat >= 1)
            r.Add(("SCANNER", $"All units, {here}: suspect vehicle, a {Tables.Vehicle(Vehicle).Name.ToLower()}, approach with caution{(Heat >= 3 ? ", lethal force authorised" : "")}."));
        else r.Add(("SCANNER", $"Unit nine, {here}, code four. Coffee run."));
        if (DaysToVote >= 0) r.Add(("KBN", $"Election countdown: {DaysToVote} day{(DaysToVote == 1 ? "" : "s")} to the council vote. Vell, Tsao, Ibarra -- polls say 'Tsao, probably'."));
        foreach (string h in HappeningLines().Take(1)) r.Add(("KBN", h.Replace("TODAY: ", "")));
        if (Awareness >= 5 || Flag("story:won") > 0)
        {
            string[] pirate =
            {
                $"This one goes out to {Name}, wherever you're hiding tonight. The corps hate you, and we love you for it.",
                $"Word on the wire: {Name} did the {here}. Didn't see a thing, did we, listeners?",
                Flag("story:won") > 0 ? $"Who brought down the Spire? Everybody knows. Nobody's saying. {Name}, this one's yours." : $"Halcyon wants {Name}'s name. They're not getting it from us.",
            };
            r.Add(("KBAY PIRATE", pirate[Day % pirate.Length]));
        }
        else r.Add(("KBAY PIRATE", "Rain all night, chummers. Keep your heads down and your wheels dry."));
        return r;
    }

    /// <summary>Rooftop tags, by lot: who runs the district, and -- once you are known -- you.</summary>
    public Dictionary<int, (string text, uint colour)> Graffiti(City city)
    {
        var tags = new Dictionary<int, (string, uint)>();
        var rng = new Rng(Seed ^ 0x6A2FUL ^ (ulong)(Day / 3));
        void Tag(string district, string text, uint colour, int n)
        {
            var lots = city.Lots.Where(l => l.Kind == LotKind.Building && l.Place == null && l.Landmark == null && (district == null || l.District == district) && !tags.ContainsKey(l.Index)).ToList();
            for (int k = 0; k < n && lots.Count > 0; k++)
            {
                Lot l = lots[rng.Int(lots.Count)];
                lots.Remove(l);
                tags[l.Index] = (text, colour);
            }
        }
        foreach (DistrictDef d in Tables.Districts)
        {
            if (Flag("held:" + d.Id) == 0) continue;
            Faction f = Holder(d.Id);
            Tag(d.Id, f switch { Faction.Vipers => "ASH VIPERS RULE", Faction.Halcyon => "HALCYON SECURE ZONE", _ => "THE LOTUS BLOOMS" }, f switch { Faction.Vipers => 0x70ff40ffu, Faction.Halcyon => 0x60e0ffffu, _ => 0xff4080ffu }, 3);
        }
        if (On("election")) { Tag(null, "VELL = RENEWAL", 0x60e0ffffu, 2); Tag("strip", "TSAO DELIVERS", 0xff4080ffu, 2); Tag("barrens", "IBARRA FOR KESSLER", 0xffd040ffu, 2); }
        string me = Name.ToUpperInvariant();
        if (Awareness >= 5) Tag(null, $"{me} WAS HERE", 0xffd040ffu, 3);
        if (Notoriety >= 5) Tag("barrens", $"{me} = RAT", 0xff6040ffu, 2);
        if (Flag("story:won") > 0) Tag("spire", "THE SPIRE FELL", 0xffffffffu, 2);
        if (Flag("cut:won") > 0) Tag("barrens", "MARROW R.I.P.", 0xa0a0a0ffu, 2);
        if (Flag("wyrm:choice") == 2) Tag(null, "THE DRAGON IS REAL", 0xff3030ffu, 3);
        return tags;
    }
}
