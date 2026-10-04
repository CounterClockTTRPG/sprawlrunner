using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// Factions remember help as well as harm. Every ordinary job has a client --
/// whoever gains most from the hit, usually the target's rival in that
/// district -- and doing it raises your standing with them. Standing buys safe
/// passage on their streets, their own work on the board, and one thing only
/// a friend of theirs can buy.
/// </summary>
public sealed partial class Career
{
    /// <summary>0-10 with each faction.</summary>
    public Dictionary<Faction, int> Standing = new();

    public int StandingOf(Faction f) => Standing.GetValueOrDefault(f);

    public static readonly (Faction faction, string id, string name, string desc, int price)[] FactionItems =
    {
        (Faction.Vipers, "colours", "Viper colours", "A jacket with the snake on the back. Viper crews never ambush you after a job, and no Viper ride comes after a courier run.", 6000),
        (Faction.Halcyon, "chip", "Halcyon contractor chip", "A real one. Every hack -- doors, cameras, the Matrix -- runs 25 % faster.", 12000),
        (Faction.Lotus, "jade", "Lotus jade marker", "The fences know what it means: everything you sell is worth 15 % more.", 9000),
    };

    public bool HasFactionItem(string id) => Flag("fitem:" + id) > 0;

    /// <summary>Who pays for this job: the faction that gains most from it (null for drives, hacks, hunts).</summary>
    public Faction? ClientFor(Job j)
    {
        if (j.Client >= 0) return (Faction)j.Client;
        return null;
    }

    private void PickClient(Job j)
    {
        j.Client = -1;
        if (j.Type is JobType.Courier or JobType.Race or JobType.Hack or JobType.Bounty or JobType.Hub || j.District == null || !Tables.Districts.Any(d => d.Id == j.District)) return;
        float[] t = TurfOf(j.District);
        int best = -1;
        for (int k = 0; k < 3; k++) if (k != (int)j.Faction && (best < 0 || t[k] > t[best])) best = k;
        j.Client = best;
    }

    /// <summary>A faction that likes you puts its own work on the board -- against its rivals, for more.</summary>
    private void OfferFactionWork(City city)
    {
        foreach (Faction f in Enum.GetValues<Faction>())
        {
            if (StandingOf(f) < Tables.StandingJobs || Board.Any(b => b.Favour && b.Client == (int)f)) continue;
            for (int tries = 0; tries < 8; tries++)
            {
                Job j = MakeJob(city);
                if (j == null) break;
                if (j.Client != (int)f) { NextJobId--; continue; }
                j.Favour = true;
                j.Pay = (int)(MathF.Round(j.Pay * Tables.FavourPay / 50) * 50);
                j.Title = $"For the {Tables.FactionNames[(int)f]}: {j.Title}";
                j.Brief = $"The {Tables.FactionNames[(int)f]} asked for you by name. " + j.Brief;
                Board.Add(j);
                break;
            }
        }
    }

    /// <summary>After an ordinary job: the client is grateful, the target less so.</summary>
    private void StandingAfter(Job j, RunResult r, Debrief d)
    {
        if (!r.Success || j.Client < 0 || j.Story != 0 || j.Tutorial) return;
        var client = (Faction)j.Client;
        int before = StandingOf(client);
        Standing[client] = Math.Min(10, before + (j.Favour ? 2 : 1));
        Standing[j.Faction] = Math.Max(0, StandingOf(j.Faction) - 2);
        d.Lines.Add(($"{Tables.FactionNames[(int)client]} standing", $"+{StandingOf(client) - before} (now {StandingOf(client)})"));
        if (before < Tables.StandingSafe && StandingOf(client) >= Tables.StandingSafe)
            Say($"The {Tables.FactionNames[(int)client]} consider you a friend: no ambushes on their streets.");
        if (before < Tables.StandingJobs && StandingOf(client) >= Tables.StandingJobs)
            Say($"The {Tables.FactionNames[(int)client]} will put their own work on your board.");
        if (before < Tables.StandingItem && StandingOf(client) >= Tables.StandingItem)
            Say($"Juno has something from the {Tables.FactionNames[(int)client]} for you -- only their friends can buy it.");
        Explain("standing", "Every job has a client -- usually the target's rival. Doing their work raises your standing with them (PDA, City tab): safe passage at 3, their own jobs at 4, a faction item at Juno's at 5.");
    }

    private void AddFactionItems(List<Offer> list, string contact)
    {
        foreach (var (f, id, name, desc, price) in FactionItems)
        {
            if (StandingOf(f) < Tables.StandingItem - 2 && !HasFactionItem(id)) continue;
            list.Add(new Offer
            {
                Kind = ShopKind.Service, Id = "fitem:" + id, Name = name, Desc = desc, Price = P(price, contact),
                Block = HasFactionItem(id) ? "Owned." : StandingOf(f) < Tables.StandingItem ? $"Only for friends of the {Tables.FactionNames[(int)f]} (standing {Tables.StandingItem}, you have {StandingOf(f)})." : null,
                Owned = HasFactionItem(id) ? "owned" : null,
            });
        }
    }
}
