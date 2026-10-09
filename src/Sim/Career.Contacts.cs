using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// Contacts can be hurt and lost. A faction that hates you and holds a
/// contact's district comes for them; help, or their shop is shut for a week
/// and they trust you less -- twice, and they leave town for good. The clean
/// ones also shut the door on a runner with too bad a name.
/// </summary>
public sealed partial class Career
{
    public bool Gone(string contact) => Flag("gone:" + contact) > 0;
    public bool ClosedNow(string contact) => Flag("closed:" + contact) >= Day;

    /// <summary>Why a contact won't deal with you today (null: they will).</summary>
    public string Shut(string contact)
    {
        if (contact is null or "home") return null;
        ContactDef k = Tables.Contacts.FirstOrDefault(x => x.Id == contact);
        if (k == null) return null;
        if (Gone(contact)) return $"{k.Name} has left Kessler Bay. The shop is empty.";
        if (ClosedNow(contact)) return $"{k.Name}'s shutters are down. A note says: back on day {Flag("closed:" + contact) + 1}.";
        if (contact is "doc" or "talismonger" && Notoriety >= 9) return $"{k.Name} won't open the door to you. Not with your name. (Notoriety {Notoriety}: a penance, restitution through Juno or a run of quiet jobs brings it down.)";
        return null;
    }

    /// <summary>A contact paid for your enemies: shut for a week, trust -1, and gone after the second time.</summary>
    public void HurtContact(string contact)
    {
        ContactDef k = Tables.Contact(contact);
        Set("closed:" + contact, Day + 6);
        Loyalty[contact] = Math.Max(0, Loyalty.GetValueOrDefault(contact) - 1);
        Inc("dangerfail:" + contact);
        if (Flag("dangerfail:" + contact) >= 2)
        {
            Set("gone:" + contact);
            Say($"{k.Name} has left Kessler Bay. Nobody knows where. You know why.");
        }
        else Say($"They wrecked {k.Venue}. {k.Name} is in a hospital bed, and the shop is shut for a week.");
    }

    /// <summary>What a contact's place is like inside, for a fight there: a bar is a club, a clinic a lab, a pawn shop a den.</summary>
    public static SiteStyle VenueStyle(string contact) => contact switch
    {
        "fixer" or "cop" => SiteStyle.Club,
        "doc" => SiteStyle.Lab,
        "decker" or "press" => SiteStyle.Office,
        "arms" or "rigger" => SiteStyle.Warehouse,
        _ => SiteStyle.Den,
    };

    /// <summary>The grudge at which a faction goes after your contacts on its streets.</summary>
    public const int DangerGrudge = 5;

    /// <summary>The contacts a faction can reach: friends of yours, open for business, on streets it holds.</summary>
    public List<ContactDef> Exposed(Faction f) => Tables.Contacts.Where(k => !Gone(k.Id) && Loyalty.GetValueOrDefault(k.Id) >= 1 && CityRef != null && Holder(k.District) == f).ToList();

    /// <summary>One short of a grudge that hurts your people: say so, and who's in reach.</summary>
    private void WarnGrudge(Faction f)
    {
        int g = Grudge.GetValueOrDefault(f);
        if (g != DangerGrudge - 1 && g != DangerGrudge) return;
        var who = Exposed(f);
        if (who.Count == 0) return;
        string names = string.Join(", ", who.Select(k => k.Name));
        Say(g < DangerGrudge
            ? $"Word on the street: the {Tables.FactionNames[(int)f]} are sore at you (grudge {g}). One more job against them and they'll go after your people on their streets: {names}. It fades a point a week."
            : $"The {Tables.FactionNames[(int)f]} want blood (grudge {g}). Expect a call from {names} -- they're on {Tables.FactionNames[(int)f]} streets.");
        Explain("danger", $"A faction with a grudge of {DangerGrudge} goes after your contacts in the districts it holds. When one calls for help, go: it's a defend job at their door, today or tomorrow. Miss it and their shop is shut for a week; twice, and they leave town for good.");
    }

    /// <summary>A job someone was counting on ran out: what follows from it.</summary>
    private void Lapsed(Job j)
    {
        RivalTookIt(j);
        if (j.Tag != null && j.Tag.StartsWith("danger:")) HurtContact(j.Tag[7..]);
        if (j.Tag != null && j.Tag.StartsWith("heist:")) { Set("heist:stage", 0); Say("The heist crew scattered when you didn't show."); }
    }
}
