using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// The rival crew's story: Juno names them, they gloat when they win and
/// grudge you when they don't, their boss offers a way out after two beatings,
/// and with two of theirs dead Juno finds where they sleep.
/// </summary>
public static partial class Storylets
{
    /// <summary>The crew's name and their boss's (safe on a career that never met them).</summary>
    private static string Crew(Career c) => c.Rival?.Name ?? "the other crew";
    private static string Boss(Career c) => c.Rival?.Leader ?? "Their boss";

    private static IEnumerable<Storylet> Rivals()
    {
        yield return new("rival-intro", Hook.Morning, Juno, "Competition",
            "Juno: 'Heads up. There's a crew undercutting everyone in town -- {crew}. {leader} runs them; {three} do the work. They're good, and they've noticed you. Jobs they want too, I'll mark on the board. Be quick on those, or be ready to meet them in there.'",
            c => c.Rival == null && c.JobsDone >= 4 && c.Day >= 5,
            Bind: c =>
            {
                if (c.Rival == null) c.MeetRival();
                return V(("crew", Crew(c)), ("leader", Boss(c)), ("three", string.Join(", ", c.Rival.Members)));
            });

        yield return new("rival-gloat", Hook.Debrief, "{leader}", "Too slow",
            "{leader}: 'Saw you in there. Nice of you to keep the guards busy for us. Same time next week?'",
            c => c.Rival is { Active: true } && c.Flag("rival:last") == 1,
            Cooldown: 3, Bind: c => V(("leader", Boss(c))));

        yield return new("rival-grudge", Hook.Debrief, "{leader}", "Next time",
            "{leader}: 'All right. You were better tonight. My people won't let it go -- and neither will I.' {crew} are gearing up.",
            c => c.Rival is { Active: true } && c.Flag("rival:last") == 2,
            Cooldown: 3, Bind: c => V(("leader", Boss(c)), ("crew", Crew(c))));

        yield return new("rival-offer", Hook.Morning, "{leader}", "Terms",
            "{leader}, on a line that keeps cutting out: 'We keep running into each other, and it's costing us both. So. Pay us to take our business to another city. Or we split this one -- you keep out of ours, we keep out of yours. Or we keep doing this until one of us stops.'",
            c => c.Rival is { Active: true } && c.Rival.Losses >= 2 && c.Flag("rival:offered") == 0,
            new[]
            {
                Fx(new Choice("Pay them to leave ({¥price})", (c, m) =>
                {
                    int price = Money(m, "price");
                    c.Cash -= price;
                    c.Count("spent", price);
                    if (c.Rival != null) c.Rival.Fate = "paid";
                    return $"The money goes; so do {Crew(c)}. A bus ticket south, a postcard nobody sends.";
                }, (c, m) => c.Cash >= Money(m, "price") ? null : "You don't have it."), "they leave Kessler Bay"),
                Talk("Street", "Split the city", (c, m) =>
                {
                    if (c.Rival != null) c.Rival.Fate = "peace";
                    return $"A handshake over the phone, as much as that means. {Crew(c)} stay off your jobs -- and now and then, {Boss(c)} sends something useful.";
                }),
                Fx(new Choice("No deal", (c, m) =>
                {
                    if (c.Rival != null) c.Rival.Tier = Math.Min(3, c.Rival.Tier + 1);
                    return $"'Your call.' {Crew(c)} will bring more next time.";
                }), "they gear up"),
            }, Call: true,
            Bind: c => V(("leader", Boss(c)), ("price", (4000 * ((c.Rival?.Tier ?? 0) + 1)).ToString())),
            Fire: (c, m) => c.Set("rival:offered"));

        yield return new("rival-hideout", Hook.Morning, Juno, "Where they sleep",
            "Juno: '{crew} lost two of theirs, and they're scared. Scared people get careless -- I know where they sleep. Say the word and it's on your board. Nobody will mourn them.'",
            c => c.Rival is { Active: true } && c.Rival.Dead >= 2 && !c.Board.Any(j => j.Tag == "rival"),
            new[]
            {
                new Choice("Put it on the board (¥1,000)", (c, m) =>
                {
                    c.Cash -= 1000;
                    c.Count("spent", 1000);
                    Job j = c.OfferSpecial(JobType.Clear, SiteStyle.Den, Faction.Vipers, "barrens", Math.Clamp(2 + (c.Rival?.Tier ?? 0), 2, 5), $"{Crew(c)}: the hideout",
                        $"Juno: 'An old tenement in the Barrens. {Boss(c)} and whoever's left, and some Viper muscle they're paying by the hour. End it.'", payMul: 1.3f, tag: "rival", expires: 6);
                    return j != null ? $"Juno puts it on the board: {j.Title}." : "Juno can't find a way in this week.";
                }, (c, m) => c.Cash >= 1000 ? null : "You don't have it."),
                Ok("Not yet", "'It'll keep. They won't get any less careless.'"),
            }, Cooldown: 7, Bind: c => V(("crew", Crew(c))));

        yield return new("rival-tip", Hook.Morning, "{leader}", "Something useful",
            "{leader}: 'We looked at your next one before we stepped off it. Here's the floor plan. We're square.'",
            c => c.Rival is { Fate: "peace" } && c.Active != null && !c.Active.Schematics && c.Active.Type is not (JobType.Courier or JobType.Race or JobType.Hack),
            Cooldown: 6, Bind: c => V(("leader", Boss(c))),
            Fire: (c, m) => { if (c.Active != null) c.Active.Schematics = true; });
    }
}
