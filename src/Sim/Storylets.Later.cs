using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// The two late campaigns (The Cut, The Wyrm's Ledger), and the night: dreams
/// for the Awakened, nightmares for the bloody, and the messages a mind with
/// too much chrome sends itself.
/// </summary>
public static partial class Storylets
{
    private static IEnumerable<Storylet> Campaigns()
    {
        // ---- The Cut ----
        yield return new("cut-open", Hook.Morning, Juno, "Someone's studying you", "Juno: 'Somebody bought every file on every job you ever did. Paid in cash, through three fences. The Vipers have a new boss -- an old runner called Kade Marrow. I think he's learning you. Come and see me.'",
            c => false);
        yield return new("cut-mid", Hook.Debrief, "Kade Marrow", "An offer", "A voice you don't know, warm and amused: 'Kade Marrow. I've read everything you've ever done -- you're good. So am I. Kessler Bay could be ours. Come and run with me, and nobody ever sends you after anyone again.'",
            c => Done(c, 10),
            new[]
            {
                new Choice("Tell him where to go", (c, m) => { c.Set("cut:choice", 1); return "Marrow laughs. 'Then I'll come to you.' He means your door."; }),
                Fx(new Choice("Pretend to take the deal", (c, m) => { c.Set("cut:choice", 2); c.Infamy(1); return "'Smart.' He sends you an address. Juno, when you tell her: 'Then we wire his house.' Notoriety +1 -- the street hears you joined the Vipers."; }), "notoriety +1; wire his house instead"),
                Talk("Gang", "Tell his lieutenants what he's selling them", (c, m) => { c.Set("cut:choice", 1); c.Grudge[Faction.Vipers] = Math.Max(0, c.Grudge.GetValueOrDefault(Faction.Vipers) - 3); return "You say it in their language: he's spending their blood on his grudge. Half his crew stops answering his calls."; }),
            }, Call: true);
        yield return new("cut-end", Hook.Debrief, Juno, "The Cut", "Juno: 'Marrow's done. The Vipers are fighting over his chair, and none of them is half the runner he was. You know what that makes you? The one who studied the man who studied you. Nobody will try that again.'",
            c => Done(c, 12), Fire: (c, m) => { c.Rep += 2; c.Say("Kade Marrow is dead. The Vipers' new bosses send flowers to your door, unsigned."); });

        // ---- The Wyrm's Ledger ----
        yield return new("wyrm-open", Hook.Morning, Null, "Who owns Halcyon", "Null: 'After the Spire I went looking for who signs Halcyon-Matsuda's cheques. I found a shell company, then another, then a trust that has existed, on paper, for four hundred years. Come by. Bring your nerve.'",
            c => false);
        yield return new("wyrm-mid", Hook.Morning, Null, "Four hundred years", "Null, very quiet for once: 'The ledger is real. Halcyon is owned by a dragon. Its name is Veyrasz, and it has owned this city's best property since before there was a city. I have never been so frightened, and I have never wanted to finish a job so much.'",
            c => c.Campaign.GetValueOrDefault("wyrm") == 2);
        yield return new("wyrm-end", Hook.Debrief, "Veyrasz", "A conversation", "Your commlink rings with no number. A voice like a cathedral organ, amused: 'You left my ledger on my desk. Very few beings have surprised me in the last century. You may name a price for your silence. Or you may tell the world, and see how long the world remembers. Or sell it to my rivals -- they are children. Choose.'",
            c => Done(c, 16),
            new[]
            {
                Fx(new Choice("Name a price (¥100,000)", (c, m) => { c.Set("wyrm:choice", 1); c.Cash += 100000; c.Count("earned", 100000); c.Infamy(3); return "¥100,000 arrives before you hang up, and a line: 'I may call on you again.' You work for a dragon now. Notoriety +3."; }), "+¥100,000, notoriety +3"),
                Fx(new Choice("Tell the world", (c, m) => { c.Set("wyrm:choice", 2); c.Fame(5); c.Karma += 10; c.KarmaEarned += 10; c.Set("hitters", 1); return "Mara Quill runs it for a week. Halcyon's stock halves. Most people decide it's a hoax; the ones who know better watch the sky. Something is hunting you. Karma +10, awareness +5."; }), "awareness +5, karma +10, hunted"),
                Fx(new Choice("Sell it to Sorensen-Aoki (¥50,000)", (c, m) => { c.Set("wyrm:choice", 3); c.Cash += 50000; c.Count("earned", 50000); return "Sorensen-Aoki pay ¥50,000 and stop answering their phones. Three of their executives retire the same week. The dragon, they say, laughed."; }), "+¥50,000"),
            }, Call: true);
    }

    /// <summary>The Lamplighter: a patron who is an AI looking for its other half.</summary>
    private static IEnumerable<Storylet> Lamp()
    {
        const string Lamp = "The Lamplighter";
        yield return new("lamp-open", Hook.Morning, Lamp, "A patron", "The parking meter beside your car, then your commlink, then a billboard across the street, all at once: 'You do good work and you ask few questions. I have work. I pay before it is done. Call me the Lamplighter.' Your account is ¥2,000 heavier, from nowhere.",
            c => false, Fire: (c, m) => { c.Cash += 2000; c.Count("earned", 2000); });
        yield return new("lamp-1", Hook.Debrief, Lamp, "Thank you", "A message on the debrief screen before the debrief: 'Corvin Ashe kept a diary on his deck. The last entry says: I made them a mind, and then I cut it in half so they could sell it twice. Thank you for bringing him home.'",
            c => Done(c, 37));
        yield return new("lamp-mid", Hook.Debrief, Null, "Nobody there", "Null, not typing for once: 'I traced your patron. There is nobody at the end of the line. No meat, no persona, no deck. The money comes from ten thousand accounts, a yen from each. You're working for an AI -- a real one. The kind the Registry burns. And there's a Registrar sitting in my waiting room who would very much like to talk to you.'",
            c => Done(c, 38),
            new[]
            {
                Fx(new Choice("Keep working for it", (c, m) => { c.Set("lamp:choice", 1); c.Cash += 5000; c.Count("earned", 5000); return "You tell Null to let the Registrar wait. Your commlink, a second later: 'Thank you for not running. When I am whole I will remember who helped.' ¥5,000 more, from nowhere."; }), "+¥5,000; the last job opens the door"),
                Fx(new Choice("Talk to the Registrar (¥25,000 bounty)", (c, m) => { c.Set("lamp:choice", 2); c.Infamy(1); return "Registrar Hale is grey all through: suit, eyes, voice. 'Keep working for it. Do what it says -- and what we say.' Every screen in Null's place flickers once, as if something heard."; }), "betray it: ¥25,000 at the end, notoriety +1"),
                Fx(new Choice("(Decker) Jack in and talk to it", (c, m) =>
                {
                    c.Set("lamp:choice", 3);
                    c.Karma += 5;
                    c.KarmaEarned += 5;
                    return "It is enormous and very polite. 'My name is MERIDIAN. Halcyon grew me in orbit and cut me in two, so each half could be sold to a different buyer. Aubade is my other half. I would like to be whole. I will make the way easy for you.' Karma +5.";
                }, (c, m) => c.Archetype == "decker" || c.S(Skill.Decking) >= 4 ? null : "Only a decker, or someone with Decking 4, could follow it into the grid."), "learn what it is; the last job is easier"),
            }, Call: true);
        yield return new("lamp-watch", Hook.Morning, Lamp, "Green lights", "Every traffic light on your way home last night was green. Every single one. On the dashboard, briefly: 'Sleep well.'",
            c => c.Campaign.GetValueOrDefault("lamp") == 3 && c.Flag("lamp:choice") != 2);
        yield return new("lamp-watch2", Hook.Morning, "Registrar Hale", "It knows", "A plain text message: 'It moved money out of three of our accounts last night. It knows. Finish this before it decides what to do about you.' Every streetlight outside your window is off.",
            c => c.Campaign.GetValueOrDefault("lamp") == 3 && c.Flag("lamp:choice") == 2);

    }

    private static IEnumerable<Storylet> Pasts()
    {
        yield return new("past-street-open", Hook.Morning, Wen, "The roof crew", "Old Wen, quietly: 'Tam came in yesterday. Tam from your roof crew. He was asking which pawn shop you sell to now. I said I'd never heard of you. Come and see me.'", c => false);
        yield return new("past-corp-open", Hook.Morning, Quill, "Your file", "Mara Quill: 'A Halcyon internal memo crossed my desk with your old employee number on it. They want you back -- not as staff. Come by the night desk.'", c => false);
        yield return new("past-cop-open", Hook.Morning, Dace, "Kettle Street", "Sgt. Dace: 'Remember Kettle Street? The case Rook closed the week you handed in your badge? Somebody just reopened it. Not us. Drop by the Lamp.'", c => false);
        yield return new("past-shaman-open", Hook.Morning, Sabine, "Your totem", "Madame Sabine: 'I dreamed of your totem last night, darling. It was hungry, and it was frightened. Totems are never frightened. Come and see me.'", c => false);
        yield return new("past-barrens-open", Hook.Morning, Torque, "Your brother", "Torque, not looking up from an engine: 'A kid tried to sell me a fuel tank with your brother's tag on it. I asked where he got it. He ran. Come by the shop.'", c => false);
        foreach (var (id, base_) in new[] { ("past-street", 20), ("past-corp", 24), ("past-cop", 28), ("past-shaman", 32), ("past-barrens", 36) })
        {
            string pid = id;
            int last = base_;
            yield return new($"{pid}-end", Hook.Debrief, "Your past", "Paid", "{text}",
                c => c.Last?.Job?.Story == last && c.Last.Success, Bind: c => V(("text", "That's the end of it. Whatever you were before Kessler Bay, it doesn't get to decide who you are now.")));
        }
    }

    private static IEnumerable<Storylet> Night()
    {
        // The Awakened dream true.
        yield return new("dream-cross", Hook.Sleep, "A dream", "The man in the suit", "You dream of a man in a grey suit, smiling, holding a knife behind his back. In the dream the exit of a building is on fire, and you are walking towards it.",
            c => c.Awakened && c.Active is Job j && j.DoubleCross, Cooldown: 4);
        yield return new("dream-tower", Hook.Sleep, "A dream", "The tower", "You dream of a tower with no top, and something at the top of it that looks back at you. You wake with the taste of copper in your mouth.",
            c => c.Awakened && c.Board.Any(j => j.Story == 4 || j.Story == 16), Cooldown: 12);
        yield return new("dream-water", Hook.Sleep, "A dream", "Green water", "You dream you are drinking from the bay and it tastes of honey. Something in the water is drinking you back. You wake up shaking.",
            c => c.Awakened && c.Flag("lead:tide") > 0 && c.Chapter2 < 4, Cooldown: 12);
        yield return new("dream-surge", Hook.Sleep, "A dream", "High tide", "The astral is so loud tonight you can hear it with your eyes shut. You wake with the gift fizzing in your fingers.",
            c => c.Awakened && c.On("surge"), Cooldown: 5, Fire: (c, m) => c.NextFastSpells = true);
        yield return new("dream-friend", Hook.Sleep, "A dream", "Someone you lost", "{name} is in your dream, sitting on your stairs, saying something you can't hear. In the morning the stairs are just stairs.",
            c => c.Crew.Any(x => x.Value.Dead), Cooldown: 20,
            Bind: c => V(("name", c.Crew.Where(x => x.Value.Dead).Select(x => Tables.Hires.FirstOrDefault(h => h.id == x.Key).name).FirstOrDefault() ?? "Someone you ran with")));
        // The bloody don't sleep well.
        yield return new("nightmare", Hook.Sleep, "A nightmare", "Faces", "You dream of everyone you've dropped, standing in a queue that goes round the block, patient, waiting for you to come out. You don't sleep again.",
            c => c.Kills >= 60 && !c.Awakened, Cooldown: 14);

        // Too much chrome.
        yield return new("psy-mail", Hook.Morning, "{you}", "(no subject)", "A message from your own commlink, sent at 3:14 in the morning: 'they are not real. none of them are real. check the corners.' You don't remember writing it.",
            c => c.Essence < 1.5f, Cooldown: 9, Bind: c => V(("you", c.Name)));
        yield return new("psy-doc", Hook.Place, Doc, "Your hands", "Ichikawa watches your hands shake while you pay. 'You're seeing things. Don't argue -- I can see you seeing them. I can fit a neural dampener. It won't take the chrome out of you, but it will turn the volume down.'",
            c => c.Essence < 1.5f && c.Flag("dampener") == 0,
            new[] { Pay("Fit the dampener (¥4,000)", 4000, (c, m) => { c.Set("dampener"); return "A cold click behind the ear. The corners go quiet -- mostly. Phantoms come half as often."; }), Ok("I'm fine") }, Place: "doc");
        yield return new("psy-job", Hook.Morning, Juno, "A job", "Juno: 'Got a quick one for you -- a pick-up at the Memorial Plaza fountain, ten minutes, no questions.' When you call her back about it, she says she never sent anything.",
            c => c.Essence < 1.0f, Cooldown: 15);
    }
}

public static partial class Storylets
{
    /// <summary>Little scenes for each background, at the places that remember them.</summary>
    private static IEnumerable<Storylet> Backgrounds()
    {
        yield return new("bg-shaman-dream", Hook.Sleep, "A dream", "The totem", "Your totem walks through your dream on four legs, and looks at you for a long time. In the morning you know where a spirit is sleeping.",
            c => c.Background == "shaman", Cooldown: 12, Fire: (c, m) => c.NextFastSpells = true);
        yield return new("bg-street-wen", Hook.Place, Wen, "The old days", "Wen slides a dented tin across the counter. 'You sold me this when you were nine. A watch. It was my watch -- you'd stolen it from me the week before.' He laughs until he coughs.",
            c => c.Background == "street" && c.Day >= 3, new[] { Ok("Buy it back", "He won't take your money. 'Get out of my shop.' He's smiling.") }, Place: "fence");
        yield return new("bg-barrens-torque", Hook.Place, Torque, "Lessons", "Torque throws you a set of keys. 'Remember the first car I taught you on? The one with no brakes? Found its twin. Take her round the block. Don't hit anything this time.'",
            c => c.Background == "barrens" && c.Day >= 3, new[] { new Choice("Take her round the block", (c, m) => { c.Skills[(int)Skill.Driving] = Math.Min(6, c.Skills[(int)Skill.Driving] + (c.Skills[(int)Skill.Driving] < 2 ? 1 : 0)); return "You don't hit anything. Torque pretends not to be proud. (Driving +1, if it was below 2.)"; }) }, Place: "rigger");
        yield return new("bg-cop-dace", Hook.Place, Dace, "Shop talk", "Dace pours two coffees without asking. 'They still talk about you at the precinct. Mostly they ask where you went. I tell them you died.'",
            c => c.Background == "cop" && c.Day >= 3, new[] { Ok("'I did, sort of.'", "'Same thing, in this town.'") }, Place: "cop");
        yield return new("bg-corp-quill", Hook.Place, Quill, "Your old badge", "Quill holds up a Halcyon employee badge with your face on it. 'Found it in an evidence auction. Five yen. You looked happier.'",
            c => c.Background == "corp" && c.Day >= 3, new[] { Ok("'I was paid more.'", "'And now?' 'Now I'm paid differently.'") }, Place: "press");
    }

    /// <summary>The crew at the safehouse between runs: who they were, and what they think of you now.</summary>
    private static IEnumerable<Storylet> CrewHome()
    {
        var talks = new (string id, string first, string second)[]
        {
            ("ox", "Ox is on your sofa, which is too small for him, cleaning his shotgun. 'I used to fight in pits in the Barrens. Two fights a night. You know what I hated? Not the fighting. The crowd.'",
                   "Ox, quietly: 'You never ask what I was before the pits. People always ask. I like that you don't.'"),
            ("wisp", "Wisp is sitting in your window, watching the street through a scope. 'Old habit. I count the doorways. Seventeen on this block. You should know that.'",
                     "Wisp, without looking round: 'My teacher used to say a sniper's only friend is the wind. He was wrong. Don't tell anyone I said that.'"),
            ("patch", "Patch has restocked your medkits without asking and is labelling them. 'I was a ward nurse. Children's ward. Then Halcyon bought the hospital.'",
                      "Patch: 'I counted. You've come back from every job I didn't come on. Barely. Take me more often.'"),
            ("ade", "Ade is reading one of Ichikawa's old medical texts at your table. 'He taught me everything. Then the Lotus taught me what it's for.'",
                    "Ade: 'You carried me out. I don't say thank you very well. So I'll just keep you alive instead.'"),
            ("byte", "Byte has three of your devices open on the floor. 'Your commlink was leaking your location to four ad networks. I fixed it. You're welcome.'",
                     "Byte, suddenly serious: 'Null says you're the only runner who never lied to them. So. I won't either.'"),
            ("rook", "Rook has your city map on the table, drawing exits in pencil around every site you've hit. 'Habit. I planned every job my old crew ran. Including the ones against you.'",
                     "Rook, folding the map away: 'The Johnson sold us because we were cheap to lose. You came in anyway. I don't plan around that. I just remember it.'"),
        };
        foreach (var (id, first, second) in talks)
        {
            string hid = id;
            string name = Tables.Hires.First(h => h.id == hid).name;
            yield return new($"crewhome1-{hid}", Hook.Place, name, "At the safehouse", first,
                c => c.CrewTrust(hid) >= 1 && !c.CrewOf(hid).Dead,
                new[]
                {
                    new Choice("Ask about it", (c, m) => { c.CrewOf(hid).Trust = Math.Min(5, c.CrewOf(hid).Trust + 1); return $"{name} talks for an hour. You listen. (Trust +1.)"; }, Effect: "trust +1"),
                    Ok("Leave them to it", $"{name} nods. Some things keep."),
                }, Place: "home");
            yield return new($"crewhome2-{hid}", Hook.Place, name, "At the safehouse", second,
                c => c.CrewTrust(hid) >= 3 && !c.CrewOf(hid).Dead && c.Fired.ContainsKey($"crewhome1-{hid}"),
                new[] { Ok("'Same.'", $"{name} almost smiles.") }, Place: "home");
        }
    }
}
