using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>What a hired runner thinks of you, what they have learned, and whether they are still alive.</summary>
public sealed class CrewState
{
    public int Trust;
    public List<string> Perks = new();
    public bool Dead;
    /// <summary>Recovering until this day (pulled out by TraumaLink, or carried out).</summary>
    public int OutUntil;
}

/// <summary>
/// The hires as a crew: trust from jobs together, a word in the debrief,
/// an upgrade at trust 2 and 4, a personal job at trust 3, and death that
/// lasts unless someone carries them out or TraumaLink Gold does.
/// </summary>
public sealed partial class Career
{
    public Dictionary<string, CrewState> Crew = new();

    public CrewState CrewOf(string id)
    {
        if (!Crew.TryGetValue(id, out CrewState s)) Crew[id] = s = new CrewState();
        return s;
    }

    public int CrewTrust(string id) => Crew.TryGetValue(id, out CrewState s) ? s.Trust : 0;
    public bool CrewHas(string id, string perk) => Crew.TryGetValue(id, out CrewState s) && s.Perks.Contains(perk);
    public bool TraumaGold => Flag("tlgold") > 0;

    /// <summary>A crew member's share of the pay: lower once they have their own job done, or the cheap perk.</summary>
    public float CrewCut(string id)
    {
        if (Retained.Contains(id)) return 0;
        float cut = Tables.Hires.First(h => h.id == id).cut;
        if (CrewHas(id, "loyal")) cut -= 0.05f;
        if (CrewHas(id, "cheap")) cut -= 0.05f;
        return MathF.Max(0.05f, cut);
    }

    /// <summary>Why this crew member cannot come (null: they can).</summary>
    public string CrewBlock(string id)
    {
        // The recruits come from the stories that saved them.
        if (id == "ade" && ArcStep.GetValueOrDefault("doc") < 2) return "Ade is still at Ichikawa's clinic (part 2 of the doc's story).";
        if (id == "byte" && ArcStep.GetValueOrDefault("decker") < 2) return "Byte is still in Null's back room (part 2 of Null's story).";
        if (id == "rook" && Rival?.Fate != "merged") return "Rook runs with another crew (the rival crew's story).";
        if (!Crew.TryGetValue(id, out CrewState s)) return null;
        if (s.Dead) return $"{Tables.Hires.First(h => h.id == id).name} is dead.";
        if (s.OutUntil >= Day) return $"Recovering until day {s.OutUntil + 1}.";
        return null;
    }

    /// <summary>Crew on retainer come along: the most trusted one free to, when nobody is hired yet (not in a car, not on a hack).</summary>
    public void BringRetained(Job j)
    {
        if (j == null || j.Hired != null || j.Driving || j.Type is JobType.Hack or JobType.Hub) return;
        string best = null;
        foreach (var h in Tables.Hires)
            if (Retained.Contains(h.id) && CrewBlock(h.id) == null && (best == null || CrewTrust(h.id) > CrewTrust(best))) best = h.id;
        if (best != null) j.Hired = best;
    }

    /// <summary>Two upgrades on offer at trust 2 and again at trust 4: (perk id, what it does).</summary>
    public static readonly Dictionary<string, (string a, string ad, string b, string bd)[]> CrewUpgrades = new()
    {
        ["ox"] = new[]
        {
            ("plating", "Dermal plates: +80 health, +2 armour.", "breacher", "Breacher shells: his shotgun hits 40 % harder."),
            ("wind", "Second wind: the first time he goes down, he gets back up at half health.", "cheap", "Old friends: his cut drops by 5 points."),
        },
        ["wisp"] = new[]
        {
            ("apds", "Armour-piercing rounds: armour counts half against her shots.", "quick", "A lighter rifle: she fires half again as fast."),
            ("spotter", "Spotter: whoever she can see, you see.", "cheap", "Old friends: her cut drops by 5 points."),
        },
        ["patch"] = new[]
        {
            ("kits", "More kit: five patch-ups a run instead of three.", "vest", "A trauma vest: +60 health, +2 armour."),
            ("stims", "Combat stims: her patch-ups heal half as much again.", "cheap", "Old friends: her cut drops by 5 points."),
        },
        ["ade"] = new[]
        {
            ("kits", "A bigger bag: six patch-ups a run.", "vest", "A trauma vest: +60 health, +2 armour."),
            ("stims", "Ichikawa's stims: her patch-ups heal half as much again.", "cheap", "Old friends: her cut drops by 5 points."),
        },
        ["byte"] = new[]
        {
            ("reach", "A better antenna: reaches machines 14 tiles away.", "fast", "Faster scripts: a machine every 5 seconds."),
            ("loop", "Long loops: what Byte blinds stays blind twice as long.", "cheap", "Old friends: his cut drops by 5 points."),
        },
        ["rook"] = new[]
        {
            ("quick", "A better SMG: he fires half again as fast.", "plating", "Dermal plates: +80 health, +2 armour."),
            ("wind", "Second wind: the first time he goes down, he gets back up at half health.", "cheap", "Old friends: his cut drops by 5 points."),
        },
    };

    /// <summary>Lines in the debrief, by how it went.</summary>
    private static readonly Dictionary<string, string[]> Banter = new()
    {
        ["ox:loud"] = new[] { "Now that was a party.", "Loud is honest. I like honest.", "My ears are ringing. Good ringing." },
        ["ox:quiet"] = new[] { "I didn't shoot anybody. Feels weird.", "All that sneaking. My knees hate you." },
        ["ox:close"] = new[] { "You nearly got us both flatlined. Again sometime?", "I took a few for you back there. You owe me a drink." },
        ["wisp:loud"] = new[] { "Subtle. Very subtle.", "I charge extra for wars, you know." },
        ["wisp:quiet"] = new[] { "Clean. That's how it's done.", "Nobody saw us. That's the only review I want." },
        ["wisp:close"] = new[] { "Next time, let me do the shooting from far away. That's the whole idea.", "Too close. I hate close." },
        ["patch:loud"] = new[] { "I'm going to be stitching people for a week. Not ours, at least.", "Did we have to kill all of them?" },
        ["patch:quiet"] = new[] { "Nobody bled. That's a good night.", "I like jobs where my kit stays packed." },
        ["patch:close"] = new[] { "Your vitals were doing things vitals should not do. Sleep. Now.", "Lie down when we get back. Doctor's orders." },
        ["ade:loud"] = new[] { "Ichikawa is going to ask me why I smell of cordite.", "So much noise. So many people I couldn't help." },
        ["ade:quiet"] = new[] { "In and out and nobody bleeding. I could get used to this.", "That was the calmest night I've had in a year." },
        ["ade:close"] = new[] { "You carried me out once. Don't make me return the favour.", "Sit down. Let me look at that. Now." },
        ["byte:loud"] = new[] { "Was that the plan? That didn't feel like the plan.", "Next time let me hack the loud bits quieter." },
        ["byte:quiet"] = new[] { "Not one camera saw us. Null's going to be so annoyed I'm this good.", "Clean. Ghosted. Beautiful." },
        ["byte:close"] = new[] { "I nearly died! Is it always like this?", "Okay. Okay. I'm fine. Are you fine? We're fine." },
        ["rook:loud"] = new[] { "We used to plan around crews like us. Now I know why.", "Loud works. It's just expensive." },
        ["rook:quiet"] = new[] { "That's how I'd have planned it. Better, actually.", "Nobody knew we were there. Old habits." },
        ["rook:close"] = new[] { "You pulled me out once. Let's not make it a habit for either of us.", "Too close. I'll draw it up better next time." },
    };

    private void CrewAfter(Job j, RunResult r, Debrief d)
    {
        if (j.Hired == null) return;
        var h = Tables.Hires.First(x => x.id == j.Hired);
        CrewState s = CrewOf(j.Hired);
        if (r.AllyDied)
        {
            if (TraumaGold)
            {
                s.OutUntil = Day + 2;
                d.Lines.Add(($"{h.name}", "TraumaLink pick-up"));
                Say($"TraumaLink Gold pulled {h.name} out. Three days in a tank and they'll be back.");
            }
            else
            {
                s.Dead = true;
                Set("crewdead:" + j.Hired, Day);
                d.Lines.Add(($"{h.name}", "killed"));
                Say($"{h.name} didn't make it out.");
            }
            return;
        }
        if (r.AllyCarried)
        {
            s.OutUntil = Day + 2;
            d.Lines.Add((j.Type == JobType.Defend ? $"{h.name} was down, but you held" : $"You carried {h.name} out", "3 days to mend"));
        }
        if (!r.Success) return;
        if (s.Trust < 5)
        {
            s.Trust++;
            d.Lines.Add(($"{h.name} trust", $"{s.Trust}"));
            Explain("crew", "Your crew remember who they worked with. Trust 2 and 4 bring an upgrade, 3 a personal job. If they go down, carry them out.");
        }
        if (j.Tag == "crew:" + j.Hired && !s.Perks.Contains("loyal")) s.Perks.Add("loyal");
        string mood = r.HpLeft < 0.35f ? "close" : r.Alarm || r.Kills >= 6 ? "loud" : "quiet";
        string[] lines = Banter[$"{j.Hired}:{mood}"];
        if (d.Quote == null) { d.Quote = lines[(int)(j.Seed % (ulong)lines.Length)]; d.QuoteBy = h.name; }
    }

    /// <summary>Pick an upgrade (from a crew storylet).</summary>
    public string CrewLearn(string id, string perk)
    {
        CrewState s = CrewOf(id);
        if (!s.Perks.Contains(perk)) s.Perks.Add(perk);
        return null;
    }
}
