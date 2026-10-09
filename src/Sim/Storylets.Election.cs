using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>Election week: the opening letter, the two people who care about the reformer, and the count.</summary>
public static partial class Storylets
{
    private static IEnumerable<Storylet> Election()
    {
        // Sent by Career.OpenElection on the week's first morning.
        yield return new("vote-open", Hook.Morning, Quill, "Election week", "Mara Quill: 'The council votes in seven days, and this city is for sale again. Adrian Vell is Halcyon's -- if he wins, it's \"urban renewal\": more Lone Star, more Halcyon, and they pay well to hit their rivals. Henry Tsao is the Lotus' -- if he wins, the cops go soft on them and hard on you. Rosa Ibarra is nobody's. If she wins, Lone Star's brass have a bad month and rents come down. All three campaigns want runners; Juno has the first two, I have Ibarra's. Every job done is a vote. And if nobody does anything, Tsao's money wins it. It always does.'",
            c => false);

        yield return new("vote-quill", Hook.Morning, Quill, "Ibarra is at nine percent", "Mara Quill: 'Ibarra's polling at nine percent and Tsao's people are already ordering the champagne. If you know anything about who pays Vell -- anything I can print -- now is when it matters.'",
            c => c.DaysToVote is >= 1 and <= 5 && c.Flag("quill") >= 0,
            new[]
            {
                Fx(Talk("Corporate", "Walk her through Vell's backers", (c, m) => { c.Inc("vote:reform"); c.Befriend("press"); return "It runs above the fold. Vell's people spend a day denying it. Ibarra +1 at the count; Quill's trust +1."; }), "Ibarra +1, Quill's trust +1"),
                Ok("Nothing to say", "'Then I'll find someone who has.'"),
            }, Cooldown: 20, Weight: 3);

        yield return new("vote-dace", Hook.Morning, Dace, "The ballot boxes", "Sgt. Dace, from a burner: 'My captain's taking Lotus money to lose the Docks ballot boxes. Ibarra's the only one on the ticket who'd fire him. For ¥2,000 I lose them somewhere else -- somewhere they get counted.'",
            c => c.DaysToVote is >= 1 and <= 3 && c.Flag("dace") >= 0,
            new[]
            {
                Fx(Pay("Pay him (¥2,000)", 2000, (c, m) => { c.Inc("vote:reform"); return "'Pleasure doing democracy.' Ibarra +1 at the count."; }), "Ibarra +1"),
                Ok("Not my election", "'It's everybody's, runner. That's the joke.'"),
            }, Cooldown: 20, Weight: 3);

        // Sent by Career.CountVotes on the morning of the count.
        yield return new("vote-result", Hook.Morning, Quill, "The count", "{text}",
            c => false,
            Bind: c => V(("text", (c.Flag("vote:won") switch
            {
                1 => "Mara Quill: 'Vell's in. Halcyon calls it renewal: two districts are theirs by the end of the month, Lone Star gets new cars, and Halcyon's rivals pay well for anyone who hits Halcyon back. Twenty days of it, then the council forgets again.'",
                2 => "Mara Quill: 'Tsao's in. Of course he is. Twenty days of a council in the Lotus' pocket: busts cost double, the heat stays on you, and their clubs hire their best.'",
                _ => "Mara Quill: 'Ibarra won. I'm still not sure I believe it. Lone Star's brass are hiding from an inquiry, landlords have a rent cap for twenty days -- and every faction in the city is doubling its guards, because nobody knows who owns the council now.'",
            }) + (c.Flag("vote:helped") > 0 ? " 'And I know who walked her to the count. So does the street.'" : ""))));
    }
}
