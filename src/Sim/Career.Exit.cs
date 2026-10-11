using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// How a career ends. Retiring is a choice of how you walk away -- out of Kessler
/// Bay, with your partner, into Juno's chair, or behind the counter of a business
/// of your own -- each with its own last scene (<see cref="Finale"/>), shown full
/// screen before the receipt; dying has one too. And the crew's moods: which of a
/// crew member's pictures a scene with them shows (<see cref="CrewMood"/>).
/// </summary>
public sealed partial class Career
{
    /// <summary>How the career was walked away from (an <see cref="Exits"/> id), or null: still running, or dead.</summary>
    public string Exit;

    /// <summary>The ways out, in order, each with why it isn't open yet (null when it is).</summary>
    public List<(string id, string name, string desc, string block)> Exits()
    {
        string partner = LivingPartner;
        string pname = partner == null ? null : Tables.Hires.First(h => h.id == partner).name;
        string place = Property.Count == 0 ? null : BusinessOf(BestProperty).name;
        return new()
        {
            ("leave", "Get out of Kessler Bay", "Sell the car, buy a ticket south, and never come back.", null),
            ("partner", pname == null ? "Stay for someone" : $"Stay with {pname}", pname == null ? "Somewhere quiet, together." : $"Somewhere quiet, with {pname}. The street can do without you both.",
                pname == null ? "Nobody to stay for (a crew member you're with)" : null),
            ("fixer", "Take Juno's chair", "Juno is getting out too. Somebody has to sit in the back booth of the Rusty Halo and hand out the jobs.",
                Gone("fixer") ? "Juno is gone" : Loyalty.GetValueOrDefault("fixer") < Tables.ExitFixerTrust ? $"Juno would have to trust you (trust {Tables.ExitFixerTrust} with her)" : null),
            ("legit", "Go straight", place == null ? "Run a business of your own, and pay your taxes. Mostly." : $"Run {Lower(place)}, and pay your taxes. Mostly.",
                place == null ? "No business of your own (Juno sells them)" : null),
        };
    }

    /// <summary>The partner still alive to retire with, or null.</summary>
    public string LivingPartner => Partner != null && !CrewOf(Partner).Dead ? Partner : null;

    /// <summary>The business you'd run: the dearest one you own.</summary>
    private string BestProperty => Property.OrderByDescending(p => BusinessOf(p).cost).First();

    private static string Lower(string s) => s.Length > 0 ? char.ToLowerInvariant(s[0]) + s[1..] : s;

    /// <summary>Walk away: the way out is fixed and the career is over (the caller writes the receipt).</summary>
    public void Retire(string exit)
    {
        Exit = Exits().Any(e => e.id == exit && e.block == null) ? exit : "leave";
        Retired = true;
    }

    /// <summary>
    /// The last scenes, full screen, before the receipt: how it ended, then what became
    /// of everyone (the first few lines of <see cref="Ending"/>; the receipt has them all).
    /// </summary>
    public List<IntroCard> Finale(bool died)
    {
        string pic = FinalePicture(died);
        var cards = new List<IntroCard> { new(pic, FinaleHeading(died), FinaleText(died)) };
        var after = (Epilogue.Count > 0 ? Epilogue : Ending()).Take(3).ToList();
        if (after.Count > 0) cards.Add(new(pic, "AFTERWARDS", string.Join(" ", after)));
        return cards;
    }

    /// <summary>The picture behind the last scene (assets/art; the screen is dark without it).</summary>
    public string FinalePicture(bool died) => died ? "ending-dead" : (Exit ?? "leave") switch
    {
        "partner" when Partner != null => $"ending-partner-{Partner}",
        "fixer" => "ending-fixer",
        "legit" => "ending-legit",
        _ => "ending-leave",
    };

    private string FinaleHeading(bool died) => died ? "LIGHT A CANDLE" : (Exit ?? "leave") switch
    {
        "partner" => "THE LONG WAY HOME",
        "fixer" => "THE BACK BOOTH",
        "legit" => "OPEN FOR BUSINESS",
        _ => RetiresALegend ? "OUT OF KESSLER BAY" : "THE MORNING TRAIN",
    };

    private string FinaleText(bool died)
    {
        if (died)
        {
            string p = Partner != null && !CrewOf(Partner).Dead ? Tables.Hires.First(h => h.id == Partner).name : null;
            return p != null
                ? $"{p} pays for the stone: a plain one, in the Barrens, with {Name} on it and one line under it that only the two of you would understand. {p} still goes, every year, and lights a candle in a jar."
                : Gone("fixer")
                    ? $"Somebody pays for a plain stone in the Barrens with {Name} on it. Nobody knows who. Every year a candle turns up on it, in a jar, out of the rain."
                    : $"Juno pays for a plain stone in the Barrens with {Name} on it, and doesn't tell anyone. Every year a candle turns up on it, in a jar, out of the rain.";
        }
        switch (Exit)
        {
            case "partner" when Partner != null:
                return Partner switch
                {
                    "ox" => $"{Name} sells the car, and Ox sells the shotgun. They buy a place on the edge of the Barrens with a sofa that finally fits him and a stove big enough for his mother's pot. Some nights the old crew come round to eat. Nobody brings a gun. Ox checks the door anyway.",
                    "wisp" => $"Wisp packs the rifle into its case one last time and doesn't open it again. {Name} finds a flat with one window and no doorways worth counting. She still reads the wind off the rain on the glass. {Name} is still terrible at it.",
                    "patch" => $"Patch takes a job on a children's ward in a town by the sea, and {Name} walks the long way to meet them after every shift. The medkits in the hall cupboard are still labelled. One of them still has a heart on it.",
                    "ade" => $"Ade runs the night shift at Ichikawa's now, and {Name} walks her home every morning, past Mama Oyelaran's, where they keep the noodles warm. {Name} never takes another job. Ade checks anyway: blood type, old breaks, all of it.",
                    _ => $"Rook burns every plan but one: the long way home, past the water. He takes it with {Name} every evening, and for once nothing goes the fifth way.",
                };
            case "fixer":
                return $"Juno hands {Name} the key to the back booth of the Rusty Halo and a chip with every number in Kessler Bay on it, and takes the morning train south. The next night a kid with a cheap deck and too much chrome sits down across the table. {Name} slides a job over. 'Don't get killed.' The kid says that's what Juno used to say.";
            case "legit":
                string place = Lower(BusinessOf(BestProperty).name);
                return $"{Name} hangs up the jacket and opens {place} every morning. The gangs get their envelope, Lone Star gets theirs, and the books are almost honest. Some nights a runner comes in bleeding and asks for {Name} by name. They get sent to Ichikawa, and nobody charges them.";
            default:
                return RetiresALegend
                    ? $"{Name} sells the car, buys a ticket out of Kessler Bay, and is never heard from again. The street remembers the name."
                    : $"{Name} sells the car, pays off the people who need paying, and takes the morning train south with the money. Nobody writes songs about it. Nobody has to.";
        }
    }

    /// <summary>
    /// How a scene with a crew member shows them: at your place ("home"), together ("partner"),
    /// or hurt by a no ("rejected"); null for their plain hire portrait. The asking and the
    /// break-up show the answer you gave, so the picture changes as you choose.
    /// </summary>
    public string CrewMood(Mail m)
    {
        string id = Tables.Hires.FirstOrDefault(h => h.name == m.From).id;
        if (id == null || id == "byte" || m.Story == null) return null;
        string s = m.Story;
        if (s == $"romance-{id}") return m.Answer switch { 0 => "partner", 2 => "rejected", _ => "home" };
        if (s == $"partner-{id}") return m.Answer == 1 ? "rejected" : "partner";
        if (s.StartsWith("partner-")) return "partner";
        if (m.Place == "home") return Partner == id ? "partner" : "home";
        return null;
    }
}
