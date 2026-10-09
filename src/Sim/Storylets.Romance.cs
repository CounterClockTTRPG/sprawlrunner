using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// Romance: at full trust, after their second talk at the safehouse, a crew
/// member says what they have been not saying. Yes, and they are your partner
/// (one at a time; Byte is a kid and never asks); not yet, and they ask again
/// in a few weeks; no, and they stay crew for good. A partner comes round on
/// quiet nights, worries when you come back from a bad job without them,
/// fights harder beside you (<see cref="Tables.PartnerAllyHp"/>), mends you
/// overnight -- and is grieved, not just buried.
/// </summary>
public static partial class Storylets
{
    private static IEnumerable<Storylet> Romance()
    {
        var crew = new (string id, string ask, string yes, string no, string[] nights, string end, string worry, string fuss, string keepsake)[]
        {
            ("ox",
                "Ox is on your sofa again, which is still too small for him. He's quiet for a long time. 'In the pits I never wanted anyone in the crowd. Now I look for you before every door we go through.' He turns his big hands over. 'I'm a troll. I know what I look like. I wanted you to know anyway.'",
                "You sit down next to him. The sofa complains. Ox laughs -- properly, for the first time you've heard -- and it shakes the whole room.",
                "Ox nods slowly. 'Good. Crew is good.' He means it. He also doesn't stay for dinner.",
                new[]
                {
                    "Ox has cooked: a pot of something from the Barrens, enough for six. 'My mother's. She'd hate you. Then she'd feed you.'",
                    "Ox is asleep sitting up on your floor, back against the sofa, between you and the door. Habit, he'd say.",
                    "Ox is reading a paperback with a broken spine, lips moving slightly. 'Don't laugh. It's a romance. I wanted to see how it's done.'",
                },
                "Ox takes it like a hit he saw coming. 'Okay.' At the door: 'I'll still come when you call. Just not tonight.'",
                "Ox is waiting in your stairwell, filling it. He looks you over twice, slowly, for holes. 'You went without me.' Not angry. Worse than angry.",
                "He makes you sit, and checks every dressing with enormous, careful fingers.",
                "the paperback romance, a bus ticket marking a page near the end"),
            ("wisp",
                "Wisp is in your window again, but the scope is in its case. 'Seventeen doorways on this block. I stopped counting them weeks ago. Now I count the minutes until you come home.' She looks at you at last. 'That's a terrible habit for a sniper. Tell me to stop.'",
                "You don't tell her to stop. She laughs, surprised at herself, and for once doesn't check the street.",
                "'Good,' Wisp says, too quickly, and goes back to the scope. 'Good. Wind's changing anyway.'",
                new[]
                {
                    "Wisp has moved your furniture so that nothing can be seen from the window. 'Not paranoid. Tidy.' She's made tea.",
                    "Wisp is teaching you to read the wind off the rain on the glass. You are terrible at it. She seems to enjoy that.",
                    "Wisp falls asleep with her head on your shoulder, which she will deny in the morning.",
                },
                "Wisp nods as if she'd read it in the wind days ago. 'Clean shot,' she says. 'At least it was clean.'",
                "Wisp is on your stairs, rifle case across her knees. 'I heard it on the scanner. I counted the minutes.' She doesn't say how many.",
                "She makes you hold still while she cleans the cuts, scolding you in elvish the whole time.",
                "a spent cartridge, polished, on a string"),
            ("patch",
                "Patch has restocked your medkits again and labelled them -- and on the last one written your name and a small heart, and then tried to scratch the heart out. 'That one's... I wasn't going to give you that one.' A breath. 'I don't patch up everybody like this. You must have noticed.'",
                "You keep the medkit with the heart on it. Patch pretends to be annoyed about that for a week.",
                "'Right. Of course.' Patch peels the label off and writes a new one. Neat handwriting. Professional.",
                new[]
                {
                    "Patch has fallen asleep on your sofa still in scrubs, a half-written supply list on their chest. You put a blanket over them and finish the list.",
                    "Patch has made dinner and is explaining, in clinical detail, why you should eat the vegetables. You eat the vegetables.",
                    "Patch is listening to your heart with an old stethoscope. 'Medical reasons,' Patch says, and doesn't move for a long time.",
                },
                "Patch takes it very calmly, the way you take bad news on a ward. 'Okay. Okay.' The medkits stay labelled. The hearts are gone.",
                "Patch is at your door with a kit already open. 'Sit down. No -- sit down. You went in without a medic. You went in without me.'",
                "Patch works fast and silently, and only at the end, taping down the last dressing, says: 'Don't.'",
                "the medkit with the heart scratched out"),
            ("ade",
                "Ade closes Ichikawa's book and doesn't open it again. 'The doc says I should stop keeping you alive on my nights off. He says it isn't professional.' Ade looks up. 'I told him it isn't. It hasn't been for a while.'",
                "Ade laughs, very quietly, as if laughing were something borrowed. 'He'll be insufferable about this.'",
                "Ade nods and picks the book back up. 'He was right, then. Fine. I'll keep you alive anyway. That part isn't up to you.'",
                new[]
                {
                    "Ade has fallen asleep at your table over a textbook, pen still in hand. The margins are full of notes about you: blood type, old breaks, what you're allergic to.",
                    "Ade has brought noodles from Mama Oyelaran's and is telling you, very seriously, about the worst things that ever came into the clinic at four in the morning.",
                    "Ade is playing old music on your commlink, very low. 'The Lotus club had this on a loop. I couldn't stand it. Now I want to hear it with somebody.'",
                },
                "Ade takes it quietly. 'I've been carried out of worse places.' At the door: 'Still eat properly.'",
                "Ade is outside your door in a coat thrown over the night-shift scrubs. 'The doc heard it on the scanner and sent me home. Let me look at you.'",
                "Ade stitches you up at your kitchen table, as steady as Ichikawa ever was, and only lets go of your hand afterwards.",
                "a page of notes in Ade's handwriting: your blood type, your old breaks, and a small drawing of you asleep"),
            ("rook",
                "Rook has the city map out again, but the pencil lines have changed: every exit leads back here. He sees you see it. 'I plan for everything. I didn't plan for this.' He folds the map. 'So I'm asking. Badly, apparently.'",
                "Rook breathes out like a job just went right. 'Good. Good. I had no fallback for that one.'",
                "Rook nods once. 'Understood.' He rubs the lines out with his thumb, neatly, until the map is just a map.",
                new[]
                {
                    "Rook is cooking, precisely, from a recipe pinned to your fridge with a knife. 'Plans work. Recipes are plans.' It's very good.",
                    "Rook is asleep on your sofa with a pencil behind his ear. The map on the table has a new route on it: the long way home, past the water.",
                    "Rook has bought two tickets to a film on the Strip and is pretending it isn't a big deal. 'It's surveillance. Of a cinema.'",
                },
                "Rook takes it the way he took being burned: still, careful, filing it away. 'Understood. I'll still have your back on a job.'",
                "Rook is at your table with the scanner on, and has been for hours by the look of the coffee. 'You took that job without me. I planned four ways it could go wrong. It went the fifth.'",
                "Rook patches you up methodically, and checks the work twice.",
                "the folded map, every pencil line leading home"),
        };
        foreach (var (id, ask, yes, no, nights, end, worry, fuss, keepsake) in crew)
        {
            string hid = id;
            string name = Tables.Hires.First(h => h.id == hid).name;
            yield return new($"romance-{hid}", Hook.Place, name, "Late", ask,
                c => c.Partner == null && c.CrewTrust(hid) >= 5 && c.CrewBlock(hid) == null && c.Fired.ContainsKey($"crewhome2-{hid}") && c.Flag("romance:no:" + hid) == 0,
                new[]
                {
                    Fx(new Choice("'Stay'", (c, m) => { c.Partner = hid; c.Set("partner:since", c.Day); return yes; }), $"{name} is your partner"),
                    Ok("'Not yet'", $"{name}: 'Okay.' A small smile. 'I'm not going anywhere.'"),
                    Fx(new Choice("'You're crew. Let's keep it that way'", (c, m) => { c.Set("romance:no:" + hid); return no; }), "they won't ask again"),
                }, Cooldown: Tables.RomanceAskAgain, Place: "home");
            yield return new($"partner-{hid}", Hook.Place, name, "A quiet night", "{text}",
                c => c.Partner == hid && c.CrewBlock(hid) == null && c.Day - c.Flag("partner:since") >= 2,
                new[]
                {
                    Fx(new Choice("Stay in", (c, m) => { c.WellFed = true; return "The city can wait one night. You sleep better than you have in weeks. Well fed (+15 % max HP on the next run)."; }), "well fed on the next run"),
                    Fx(new Choice("'This isn't working'", (c, m) =>
                    {
                        CrewState s = c.CrewOf(hid);
                        s.Trust = Math.Max(0, s.Trust - Tables.PartnerBreakTrust);
                        s.OutUntil = Math.Max(s.OutUntil, c.Day + Tables.PartnerBreakDays - 1);
                        c.Partner = null;
                        c.Set("romance:no:" + hid);
                        return end;
                    }), $"it's over; trust -{Tables.PartnerBreakTrust}, {name} sits out {Tables.PartnerBreakDays} days"),
                },
                Weight: 2, Cooldown: Tables.PartnerNightEvery, Place: "home",
                Bind: c => V(("text", nights[(int)(Career.Stable(hid + c.Day) % (uint)nights.Length)])));
            yield return new($"partner-worry-{hid}", Hook.Place, name, "Back late", worry,
                c => c.Partner == hid && !c.CrewOf(hid).Dead && AfterJob(c) && BadJob(c) && c.Last.Job.Hired != hid,
                new[]
                {
                    Fx(new Choice("Let them fuss", (c, m) => { c.Injuries = Math.Max(0, c.Injuries - 1); return fuss; }), "an injury mended"),
                    Ok("'I'm fine'", $"You're not, and {name} knows it, and stays anyway."),
                },
                Weight: 3, Cooldown: 6, Place: "home", Fire: Visited);
            yield return new($"partner-dead-{hid}", Hook.Morning, Juno, name,
                $"Juno: 'They buried {name} this morning, in the Barrens. I know what {name} was to you -- everybody knew, the way you two left a room.' A pause. 'You don't have to come. They kept you a place at the front.'",
                c => c.Flag("partnerdead:" + hid) > 0 && c.Day - c.Flag("partnerdead:" + hid) <= 3,
                new[]
                {
                    Pay("Go, and pay for the stone (¥1,000)", 1000, (c, m) => { c.Karma += 2; c.KarmaEarned += 2; return $"A plain stone, '{name}'. Under it, one line that only the two of you would understand. Karma +2."; }),
                    Ok("Stay home", $"You stay home, in the quiet {name} used to fill."),
                });
            yield return new($"partner-keepsake-{hid}", Hook.Place, name, "A drawer",
                $"Clearing out a drawer, you find something of {name}'s: {keepsake}. You sit with it for a long time.",
                c => c.Flag("partnerdead:" + hid) > 0 && c.Day - c.Flag("partnerdead:" + hid) >= Tables.PartnerKeepsakeAfter,
                new[] { Fx(new Choice("Keep it", (c, m) => { c.Karma += 1; c.KarmaEarned += 1; return "It goes on the shelf by the door, where you'll see it on the way out. Karma +1."; }), "karma +1") },
                Place: "home");
        }
    }
}
