using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>One card of the intro: a painted backdrop, a heading, Juno's words.</summary>
public sealed record IntroCard(string Picture, string Heading, string Text);

/// <summary>
/// The intro a new career opens with: the city, who you were (by background),
/// what you do now, and why tonight. Juno narrates (the text is what she says,
/// word for word; assets/voice holds the recordings as say-&lt;picture&gt;).
/// </summary>
public static class Intro
{
    public static readonly IntroCard City = new("intro-city", "KESSLER BAY, 2071",
        "Kessler Bay. Nine million people on a bay that died before you were born. Halcyon-Matsuda owns the skyline, the Red Lotus own the night, and the Ash Vipers own whatever's left. Lone Star keeps the peace, for whoever pays.");

    public static readonly Dictionary<string, IntroCard> Pasts = new()
    {
        ["street"] = new("intro-street", "WHO YOU WERE",
            "You grew up on the roofs of the Barrens with a crew of kids who stole anything that wasn't welded down. Old Wen bought most of it. The crew's gone now. You're not."),
        ["corp"] = new("intro-corp", "WHO YOU WERE",
            "Eleven years a wage slave in a Halcyon-Matsuda tower. Then you opened a file you weren't cleared for, and saw what the company does with people. You walked out with your severance and a head full of secrets."),
        ["cop"] = new("intro-cop", "WHO YOU WERE",
            "You wore a Lone Star badge until you watched your partner sell a case to the highest bidder. You handed in the badge. He got promoted. Sergeant Dace still takes your calls."),
        ["shaman"] = new("intro-shaman", "WHO YOU WERE",
            "You came down from the hills following a voice only you could hear. Your totem led you here, to the dirtiest city on the coast, and went quiet. Madame Sabine says it's waiting for something."),
        ["barrens"] = new("intro-barrens", "WHO YOU WERE",
            "Born in a Viper squat, out before you were ten. The Vipers killed your brother over a debt that wasn't his. Torque taught you to drive. You never stopped driving."),
    };

    public static readonly IntroCard Now = new("intro-juno", "WHAT YOU DO",
        "Now you're a shadowrunner. No SIN, no record, no questions. Somebody with money wants something done that their own people can't be seen doing. I find the job. You do it, and walk away clean. That's the deal.");

    public static readonly IntroCard Tonight = new("intro-tonight", "WHY YOU'RE HERE",
        "You've got a mattress in a safehouse on the wrong side of the Barrens, rent due on Sunday, and one friend in the city: me. There's a briefcase in a warehouse by the docks. Come and see me at the Rusty Halo. Let's see what you can do.");

    public static IEnumerable<IntroCard> For(string background) =>
        new[] { City, Pasts.GetValueOrDefault(background) ?? Pasts["street"], Now, Tonight };

    public static IEnumerable<IntroCard> All => new[] { City, Now, Tonight }.Concat(Pasts.Values);
}
