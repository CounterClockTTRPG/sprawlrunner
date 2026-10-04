using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// The Iron Spire, second draft: the legwork between chapters (each chapter
/// leaves a lead to chase with a contact), the choice at the midpoint, the
/// ending; and Toxic Tide, the campaign that comes after.
/// </summary>
public static partial class Storylets
{
    private static bool Done(Career c, int story) => c.Last?.Job?.Story == story && c.Last.Success;

    private static IEnumerable<Storylet> Story()
    {
        // After chapter 1: the chip had a serial number.
        yield return new("story-1", Hook.Morning, Juno, "The serial number", "Juno: 'The chip you brought back has a serial number burned into the casing. Somebody made a batch of these. Null could trace it -- if Null feels like talking to you.'",
            c => c.Chapter == 1 && c.Flag("lead:ch2") == 0);
        yield return new("story-trace", Hook.Place, Null, "The serial", "Null turns the chip's serial over in a window full of green text. 'Halcyon-Matsuda, Industrial lab four. A thousand of these, ordered by somebody with no name and a lot of money. I can get you the lab. It costs.'",
            c => c.Chapter == 1 && c.Flag("lead:ch2") == 0,
            new[]
            {
                new Choice("Pay for the trace (¥1,000, free at trust 2)", (c, m) =>
                {
                    if (c.Loyalty.GetValueOrDefault("decker") < 2) { c.Cash -= 1000; c.Count("spent", 1000); }
                    c.Set("lead:ch2");
                    return "Null: 'Lab four. Juno will want to hear.' The next chapter is on its way to your board.";
                }, (c, m) => c.Loyalty.GetValueOrDefault("decker") < 2 && c.Cash < 1000 ? "Needs ¥1,000 (or trust 2 with Null)." : null),
                Talk("Corporate", "Read the order codes yourself", (c, m) => { c.Set("lead:ch2"); return "Purchase codes, cost centres, a lab number. You speak this. Null looks almost impressed."; }),
                Ok("Later"),
            }, Place: "decker");

        // After chapter 2: the midpoint.
        yield return new("story-mid", Hook.Debrief, Juno, "The buyer list", "Juno, very quietly: 'Your download was the buyer list. Iron Saint is a chip that makes people do what they're told, and every name on that list has ordered one for their own security. Half the city's powerful people. What we do with this decides what happens next. You choose.'",
            c => Done(c, 2),
            new[]
            {
                Fx(new Choice("Leak it to Mara Quill", (c, m) => { c.Set("story:choice", 1); c.Fame(3); c.Set("quill", Math.Max(1, c.Flag("quill"))); return "Mara Quill: 'This is the story of my life. They will come for you, you know.' Awareness +3."; }), "awareness +3; the Spire will know you are coming"),
                Fx(new Choice("Sell it to the Red Lotus (¥15,000)", (c, m) => { c.Set("story:choice", 2); c.Cash += 15000; c.Count("earned", 15000); c.Infamy(2); c.Grudge[Faction.Lotus] = 0; return "Auntie Mei counts out the money herself. 'The Lotus remember their friends.' +¥15,000, notoriety +2."; }), "+¥15,000, notoriety +2, Lotus grudge 0"),
                Fx(new Choice("Give it to Sorensen-Aoki (¥8,000)", (c, m) => { c.Set("story:choice", 3); c.Cash += 8000; c.Count("earned", 8000); return "Halcyon's biggest rival pays promptly, politely, and asks a lot of questions about you. +¥8,000."; }), "+¥8,000; they want to meet you after"),
            }, Call: true);

        // After the midpoint: Pryce's sister.
        yield return new("story-2", Hook.Morning, Doc, "A visitor", "Ichikawa: 'A woman came in asking about a Dr. Amon Pryce. Says she's his sister. Says you're the only one who can find him. I told her to wait. She's still waiting.'",
            c => c.Chapter == 2 && c.StoryChoice > 0 && c.Flag("lead:ch3") == 0);
        yield return new("story-sister", Hook.Place, "Ilse Pryce", "My brother", "A tired woman with Pryce's eyes is sitting in Ichikawa's waiting room. 'Amon designed it. Then he understood what it was for and ran -- straight into the Red Lotus. They're selling him. I know where. Please.'",
            c => c.Chapter == 2 && c.StoryChoice > 0 && c.Flag("lead:ch3") == 0,
            new[]
            {
                new Choice("Promise to bring him back", (c, m) => { c.Set("lead:ch3"); c.Karma += 1; c.KarmaEarned += 1; return "She grips your hand too hard. 'Thank you.' Juno will have the job by morning. Karma +1."; }),
                Fx(new Choice("Ask what it pays", (c, m) => { c.Set("lead:ch3"); c.Cash += 1000; c.Count("earned", 1000); c.Infamy(1); return "She empties her account for you: ¥1,000. Ichikawa watches you take it. Notoriety +1."; }), "+¥1,000, notoriety +1"),
            }, Place: "doc");

        // After chapter 3: Pryce draws the Spire.
        yield return new("story-3", Hook.Morning, "Dr. Amon Pryce", "The Spire", "Pryce: 'I owe you my life, and I know the Spire -- I built the systems on the top floor. Meet me at the Rusty Halo. Bring a pen.'",
            c => c.Chapter == 3 && c.Flag("lead:ch4") == 0);
        yield return new("story-pryce", Hook.Place, "Dr. Amon Pryce", "Plans", "In Juno's back room Pryce draws the top of the Spire on a napkin, then on six more. 'Cores here, here and here. The test subject runs security -- he won't feel pain, he won't stop. Don't let him get close.'",
            c => c.Chapter == 3 && c.Flag("lead:ch4") == 0,
            new[] { new Choice("Take the plans", (c, m) => { c.Set("lead:ch4"); c.Set("story:plans"); return "Juno puts the last job on the board. You will have the Spire's schematics when you go in."; }) }, Place: "fixer");

        // The end, or not.
        yield return new("story-end", Hook.Debrief, Juno, "What now", "Juno, pouring two drinks she doesn't usually pour: 'It's over. You did the thing nobody does -- you won. You can disappear tonight, a legend, and never work again. Or you can stay. The city will still need runners tomorrow.'",
            c => Done(c, 4),
            new[]
            {
                Fx(new Choice("Retire a legend", (c, m) => { c.Retired = true; return "You sell the car, buy a ticket out, and are never heard from again. The street remembers your name."; }), "the career ends: the epilogue and your legacy"),
                Fx(new Choice("Keep running", (c, m) => "Juno smiles. 'I hoped you'd say that.' The career goes on -- and Madame Sabine has been asking for you."), "the career goes on"),
            }, Call: true);
    }

    private static IEnumerable<Storylet> Tide()
    {
        yield return new("tide-open", Hook.Morning, Sabine, "Bad water", "Madame Sabine: 'Darling, I need someone who has seen the worst of this city and walked away. Something under the Docks is waking, and people are drinking it. Come and see me when you are ready. Bring a strong stomach.'",
            c => false);
        yield return new("tide-mid", Hook.Debrief, Sabine, "Two answers", "Sabine and Ichikawa, in the same room for the first time anyone can remember, arguing. Sabine: 'Starve the spirit -- burn the pool, and it dies.' Ichikawa: 'Four hundred people drank that water. Burn it and they go with it. Give me a week to make a cure.' They both look at you.",
            c => Done(c, 6),
            new[]
            {
                new Choice("Sabine: starve the spirit", (c, m) => { c.Set("tide:choice", 1); return "Sabine: 'It's weaker already. The cult will be thin on the ground.' The last job will be lightly guarded."; }),
                new Choice("Ichikawa: save the drinkers", (c, m) => { c.Set("tide:choice", 2); c.Karma += 5; c.KarmaEarned += 5; c.Medkits = Math.Min(5, c.Medkits + 2); return "Ichikawa: 'Thank you. Take these.' Two medkits, karma +5 -- but the cult will be at full strength."; }),
            }, Call: true);
    }
}
