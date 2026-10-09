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
            c => c.Rival is { Active: true, Burn: null } && c.Rival.Losses >= 2 && c.Flag("rival:offered") == 0,
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
                // Mercy is remembered: knock two of them out and leave them breathing, and they'll trust a handshake.
                Fx(new Choice("Remind them who you let walk away", (c, m) =>
                {
                    if (c.Rival != null) c.Rival.Fate = "peace";
                    return $"A pause on the line. 'You could have finished my people. Twice.' {Crew(c)} stay off your jobs -- and now and then, {Boss(c)} sends something useful.";
                }, (c, m) => c.Rival?.Spared >= 2 ? null : $"You've spared {c.Rival?.Spared ?? 0} of them; it takes 2."), "split the city (you spared 2 of them)"),
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
            c => c.Rival is { Active: true, Burn: null } && c.Rival.Dead >= 2 && !c.Board.Any(j => j.Tag == "rival"),
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
            c => c.Rival is { Fate: "peace" or "merged" } && c.Active != null && !c.Active.Schematics && c.Active.Type is not (JobType.Courier or JobType.Race or JobType.Hack),
            Cooldown: 6, Bind: c => V(("leader", Boss(c))),
            Fire: (c, m) => { if (c.Active != null) c.Active.Schematics = true; });

        // ---- act two: their Johnson burns them ----------------------------------------------------

        yield return new("rival-burned", Hook.Morning, "{tipper}", "A crew being burned",
            "{tipper}: '{news} {crew}'s Johnson -- {johnson}, Halcyon money -- is cleaning house. Their next meet is a Lone Star tactical team with the safeties off. Deniable crews get denied. {leader} doesn't know, and Rook -- the one who plans their jobs -- walks in first. You could tell them. You could tell {johnson} they're about to find out, for a price. Or you could have heard nothing.'",
            c => c.RivalBurnDue && c.CityRef != null,
            new[]
            {
                Fx(new Choice("Warn {leader}", (c, m) => c.RivalBurned("warned")), $"they owe you; their grudge -{Tables.RivalWarnGrudge}; Rook needs pulling out"),
                Fx(new Choice("Sell the warning to {johnson} ({¥pay})", (c, m) => c.RivalBurned("sold")), $"+{{¥pay}}, notoriety +1, their grudge +{Tables.RivalSellGrudge}; Rook and another die"),
                Fx(new Choice("Stay out of it", (c, m) => c.RivalBurned("out")), "Rook is taken; nothing changes for you"),
            }, Call: true,
            Bind: c =>
            {
                // Null hears it in the traffic if Null likes you; otherwise it's Juno, over noodles.
                bool nul = c.Loyalty.GetValueOrDefault("decker") >= 2;
                return V(("tipper", nul ? Null : Juno), ("news", nul ? "Funny thing in the Halcyon traffic last night." : "Something you should hear before it's news."),
                    ("crew", Crew(c)), ("leader", Boss(c)), ("johnson", Tables.RivalJohnson), ("pay", c.RivalSellPrice.ToString()));
            });

        // ---- act three: how it ends ------------------------------------------------------------------

        yield return new("rival-merge", Hook.Morning, "Rook", "Last of {crew}",
            "Rook: 'After the burn there's no {crew}. Nobody takes a meet with a crew a Johnson sold. {leader} is going straight, or says so. Me, I'm not built for straight. You warned us when it cost you, and then you came in for me. If you'll have me, I run with you now. {leader} says you'll get the odd floor plan, for old times.'",
            c => c.RivalEndDue && c.RivalEnding() == "merge",
            new[]
            {
                Fx(new Choice("Welcome aboard", (c, m) => { c.RivalEnd("merged"); return "Rook nods once, like signing something. He's at Juno's when you need him -- and he already trusts you."; }), $"Rook joins your crew (trust {Tables.RivalMergeTrust})"),
                Fx(new Choice("Wish them both luck", (c, m) => { c.RivalEnd("left"); return "'Fair.' Rook takes the bus south with what's left of them."; }), "they leave town"),
            }, Call: true,
            Bind: c => V(("crew", Crew(c)), ("leader", Boss(c))));

        yield return new("rival-showdown", Hook.Morning, "{leader}", "Where we sleep",
            "{leader}: '{why} So here it is: the tenement in the Barrens, where we sleep. Come and finish it, or look over your shoulder until I do.'",
            c => c.RivalEndDue && c.RivalEnding() == "showdown" && !c.Board.Any(j => j.Tag == "rival") && c.CityRef != null,
            new[]
            {
                Fx(new Choice("Finish it", (c, m) =>
                {
                    Job j = c.OfferSpecial(JobType.Clear, SiteStyle.Den, Faction.Vipers, "barrens", Math.Clamp(3 + (c.Rival?.Tier ?? 0), 3, 6), $"{Crew(c)}: the last stand",
                        $"{Boss(c)} and whoever's left, dug into an old tenement in the Barrens with Viper muscle on the doors. They know you're coming. End it.", payMul: 1.5f, tag: "rival", expires: 7);
                    return j != null ? $"It's on your board: {j.Title}." : "The tenement's sealed up this week. They'll call again.";
                }), $"the finale: their stash (+{M.Money(Tables.RivalStash)}, karma +{Tables.RivalShowdownKarma}) and the end of them"),
                Ok("Let them wait", "They'll still be there. So will the contested jobs."),
            }, Call: true, Cooldown: 7,
            Bind: c => V(("leader", Boss(c)), ("why", c.Rival?.Burn switch
            {
                "sold" => $"You sold us to {Tables.RivalJohnson}. Rook's dead because a runner wanted pocket money.",
                "out" => "You heard what was coming and let it come. A crew our size doesn't run from that, or from you.",
                _ => "You've buried enough of mine. The Johnson burned us; you'd have done it slower.",
            })));

        yield return new("rival-leave", Hook.Morning, "{leader}", "Leaving",
            "{leader}: '{why} We're taking what's left of {crew} out of Kessler Bay. The board's yours.'",
            c => c.RivalEndDue && c.RivalEnding() == "leave",
            Bind: c => V(("leader", Boss(c)), ("crew", Crew(c)), ("why", c.Rival?.Owed == true
                ? "You warned us. We don't forget that, even if Rook didn't make it."
                : "After the burn there's nothing here for us -- no Johnson takes our calls, and nobody we'd want to work for.")),
            Fire: (c, m) => c.RivalEnd("left"));

        yield return new("rival-postcard", Hook.Morning, "{leader}", "A postcard",
            "A real postcard, paper, through Juno: a beach that can't be that blue any more. '{text}'",
            c => c.Rival is { Fate: "left" } && c.Day - c.Flag("rival:endday") >= Tables.RivalPostcardAfter,
            Bind: c => V(("leader", Boss(c)), ("text", c.Rival?.Owed == true
                ? $"Warm here. Work's honest, mostly. There's a cred chip under the stamp -- {M.Money(Tables.RivalPostcardGift)}, the rest of what we owe you. Don't come looking. -- {Boss(c)}"
                : $"Warm here. Nobody shoots at us. Don't come looking. -- {Boss(c)}")),
            Fire: (c, m) =>
            {
                if (c.Rival?.Owed != true) return;
                c.Cash += Tables.RivalPostcardGift;
                c.Count("earned", Tables.RivalPostcardGift);
            });
    }
}
