using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// The street, from the car: someone waving you down, a wreck with its hazards
/// on, a Viper toll across the lane. They wait on a kerb ahead (see
/// <c>CitySim.Street</c>); stop beside one and press E. Their place says what
/// they look like ("street:waving", "street:wreck", "street:toll"); the last
/// choice is always to drive on, and is what happens if you just close it.
/// </summary>
public static partial class Storylets
{
    private const string Street = "The street";

    private static IEnumerable<Storylet> Streets()
    {
        yield return new("street-hurt", Hook.Drive, Street, "Flagged down",
            "A woman steps off the kerb into your lights, one arm wrapped in a jacket, the blood coming through it. 'Not a hospital. Please. Anywhere they don't ask.'",
            c => c.CityRef?.Place("doc") != null,
            new[]
            {
                Fx(new Choice("Take her to Ichikawa", (c, m) =>
                {
                    c.TakeAboard(c.CityRef.Place("doc").Index, 0, "a woman, bleeding", "Ichikawa takes her in through the back without a word. She squeezes your hand on the way. 'I won't forget.'", karma: 2);
                    return "She slides into the back seat and keeps her eyes shut. The clinic is on your route now.";
                }), "drive her to the clinic: karma +2"),
                Ok("Drive on", "You pull away. In the mirror she's waving at the next car."),
            }, Weight: 1.2f, Cooldown: 6, Place: "street:waving");

        yield return new("street-wreck", Hook.Drive, Street, "Hazard lights",
            "A sedan wrapped round a lamp post, hazards blinking, steam from the bonnet. The driver is slumped on the wheel. The boot has sprung open: a sports bag, a briefcase.",
            c => true,
            new[]
            {
                Fx(new Choice("Pull him out", (c, m) => { c.Karma += 1; c.KarmaEarned += 1; c.Count("rescues"); return "He comes round on the pavement and swears at you, which is a good sign. An ambulance is already on its way. Karma +1."; }), "karma +1"),
                Fx(new Choice("Go through the boot", (c, m) =>
                {
                    int cash = Money(m, "loot");
                    c.Cash += cash;
                    c.Count("earned", cash);
                    c.Infamy(1);
                    c.Heat = Math.Min(5, c.Heat + 0.3f);
                    return $"The briefcase has {M.Money(cash)} in an envelope with a casino stamp. Someone at the window across the street saw your plates. Notoriety +1.";
                }), "+{¥loot}, notoriety +1, a little heat"),
                Ok("Drive on", "Not your problem. Not tonight."),
            }, Cooldown: 5, Place: "street:wreck",
            Bind: c => V(("loot", (400 + (int)(Career.Stable("wreck" + c.Day) % 10) * 60).ToString())));

        yield return new("street-toll", Hook.Drive, "Ash Vipers", "Toll road",
            "Two Viper bikes are parked across the lane, engines running. A ganger with a plastic bucket walks up to your window and taps it. 'Road tax. Everybody pays.'",
            c => c.Here != null && c.Holder(c.Here) == Faction.Vipers && c.Day >= 3,
            new[]
            {
                new Choice("Pay ({¥toll})", (c, m) => { c.Cash -= Money(m, "toll"); c.Count("spent", Money(m, "toll")); return "The bucket rattles. The bikes roll back just far enough to let you through."; },
                    (c, m) => c.Cash >= Money(m, "toll") ? null : "You don't have it."),
                Talk("Gang", "Talk your way through", (c, m) => "You name the right names in the right order. The ganger laughs, slaps the roof, and waves you on. No charge."),
                Fx(new Choice("Floor it", (c, m) => { c.Set("street:ambush", 2); c.Grudge[Faction.Vipers] = c.Grudge.GetValueOrDefault(Faction.Vipers) + 1; return "You clip a bike on the way through. The bucket hits the back window. Engines start behind you."; }), "two Viper rides after you, Viper grudge +1"),
            }, Cooldown: 4, Place: "street:toll",
            Bind: c => V(("toll", (150 + 25 * Math.Min(c.Day, 20)).ToString())));

        yield return new("street-filmed", Hook.Drive, "A kid with a phone", "Your face, his phone",
            "A kid on the corner holds a phone up to your windscreen: your plates, your face, and a clip from last night that KBN would love. 'Mara Quill pays two hundred. You can pay more.'",
            c => c.Awareness >= 2,
            new[]
            {
                new Choice("Pay him off (¥500)", (c, m) => { c.Cash -= 500; c.Count("spent", 500); return "He deletes it in front of you, which means nothing, and runs, which means he was honest."; },
                    (c, m) => c.Cash >= 500 ? null : "You don't have it."),
                Fx(new Choice("Drive on", (c, m) => { c.Fame(1); return "He's already uploading it as you pull away."; }), "awareness +1"),
            }, Cooldown: 8, Place: "street:waving");

        yield return new("street-runner", Hook.Drive, "A runner", "Engine trouble",
            "A runner you half know from the Rusty Halo, standing by a car with smoke pouring out of the bonnet. 'Ride to Juno's and I'll give you something better than money.'",
            c => c.JobsDone >= 3 && c.CityRef?.Place("fixer") != null,
            new[]
            {
                Fx(new Choice("Give her a ride", (c, m) =>
                {
                    c.TakeAboard(c.CityRef.Place("fixer").Index, 0, "a runner with a tip", "She gets out at the Rusty Halo and leans back in. 'Halcyon courier, a briefcase, a night shift that sleeps. Yours.'", extra: "lead");
                    return "She climbs in, lights a cigarette you don't let her smoke, and tells you to drive to Juno's.";
                }), "drive her to the Rusty Halo: a job tip"),
                Ok("Drive on", "'Your loss,' she shouts after you."),
            }, Cooldown: 10, Place: "street:waving");

        yield return new("street-package", Hook.Drive, "A delivery man", "Don't open it",
            "A man in a delivery jacket, a taped box under his arm, steps up to your window. 'Two hundred to take it to the Docks. Six hundred if you don't open it.'",
            c => c.Heat < 2 && c.CityRef != null,
            new[]
            {
                Fx(new Choice("Take the box", (c, m) =>
                {
                    int lot = c.CityRef.FreeSite("docks", new Rng((ulong)m.Id * 7919 + (ulong)c.Day), new HashSet<int>());
                    if (lot < 0) return "He changes his mind and walks off.";
                    c.Heat = Math.Min(5, c.Heat + 1);
                    c.TakeAboard(lot, 600, "a taped box", "A man in a doorway takes the box without looking at you and hands over an envelope.");
                    return "The box is heavier than it looks, and something in it ticks. Lone Star has your plates in the area already. Heat +1.";
                }), "¥600 at the drop, heat +1"),
                Ok("Drive on", "He shrugs and waves at the next car."),
            }, Cooldown: 6, Place: "street:waving");

        yield return new("street-stop", Hook.Drive, "Lone Star", "Pulled over",
            "The cruiser's lights fill your mirrors. An officer in a mirrored visor walks up, slow, one hand on her holster. 'Licence. And step out of the car.'",
            c => c.Here != null && c.Holder(c.Here) != Faction.Vipers && !c.On("strike") && c.Day >= 3,
            new[]
            {
                Fx(new Choice("Hand her the fine ({¥fine})", (c, m) => { c.Cash -= Money(m, "fine"); c.Count("spent", Money(m, "fine")); c.Heat = 0; return "She writes you up for a broken tail light that isn't broken. Your file stays closed. Heat 0."; },
                    (c, m) => c.Cash >= Money(m, "fine") ? null : "You don't have it."), "-{¥fine}, heat 0"),
                Talk("Security", "Talk shop with her", (c, m) => { c.Heat = MathF.Max(0, c.Heat - 1); return "You know the shift rota, the sergeant's name and the canteen's coffee. She laughs and waves you on. Heat -1."; }),
                Fx(new Choice("Floor it", (c, m) => { c.Set("street:cruiser"); c.Heat = MathF.Min(5, MathF.Max(c.Heat, 1) + 1); return "You're gone before she gets back to her car. She isn't far behind."; }), "a cruiser after you, heat +1"),
            }, Weight: 0.9f, Cooldown: 5, Place: "street:patrol",
            Bind: c => V(("fine", (250 + 150 * (int)c.Heat + 10 * Math.Min(c.Day, 30)).ToString())));

        yield return new("street-race", Hook.Drive, "A hotshot", "At the lights",
            "A lime-green hotshot rolls up beside you at the lights and drops his window. 'Nice ride. Bet it's all paint.' He holds up a credstick. '{¥stake} says I'm across town before you.'",
            c => c.Here is "strip" or "industrial" or "docks" && c.CityRef != null,
            new[]
            {
                Fx(new Choice("You're on ({¥stake})", (c, m) => { int s = Money(m, "stake"); c.Cash -= s; c.Count("spent", s); c.Set("street:race", s); return "The light goes green."; },
                    (c, m) => c.Cash >= Money(m, "stake") ? null : "You can't cover it."), "race him: double or nothing"),
                Ok("Drive on", "You wind the window up. 'All paint,' he shouts, and burns off on the red."),
            }, Cooldown: 4, Place: "street:racer",
            Bind: c => V(("stake", (300 + 100 * Math.Min(c.Day / 4, 10)).ToString())));

        yield return new("street-van", Hook.Drive, "The street", "Armoured van",
            "A Halcyon cash van lies on its side, wheels still turning. One guard is out cold behind the wheel; the other is crawling away. The back doors are buckled half open, and the alarm hasn't gone off. Yet.",
            c => c.Here is "spire" or "industrial" or "strip",
            new[]
            {
                Fx(new Choice("Help the guards out", (c, m) => { c.Karma += 1; c.KarmaEarned += 1; c.Count("rescues"); return "You drag the driver clear before the fuel goes. The other one remembers your face, kindly. Karma +1."; }), "karma +1"),
                Fx(new Choice("Empty the back", (c, m) =>
                {
                    int cash = Money(m, "cash");
                    c.Cash += cash;
                    c.Count("earned", cash);
                    c.Grudge[Faction.Halcyon] = c.Grudge.GetValueOrDefault(Faction.Halcyon) + 1;
                    c.Heat = MathF.Min(5, c.Heat + 1.5f);
                    c.Infamy(1);
                    return $"Bricks of corporate scrip, {M.Money(cash)} of it, still in Halcyon bands. The alarm starts as you pull away. Heat +1.5, notoriety +1, Halcyon grudge +1.";
                }), "+{¥cash}, heat +1.5, Halcyon grudge +1"),
                Ok("Drive on", "Somebody else's bad night."),
            }, Weight: 0.7f, Cooldown: 9, Place: "street:van",
            Bind: c => V(("cash", (1500 + (int)(Career.Stable("van" + c.Day) % 8) * 250).ToString())));

        yield return new("street-ghoul", Hook.Drive, "The street", "In your lights",
            "It was a man, once. Grey skin, milk-white eyes, crouched over something in the road. It looks up into your headlights and doesn't move.",
            c => c.Here == "barrens" && c.Day >= 4,
            new[]
            {
                Fx(new Choice("Put your foot down", (c, m) => { c.CarHp = Math.Max(1, c.CarHp - 12); return "It doesn't get out of the way. Your bonnet will need Torque, and you'll need a drink."; }), "car damage"),
                Fx(new Choice("Lean on the horn and back off", (c, m) => "The horn does nothing. Reversing does. It goes back to what it was eating."), "nothing happens"),
                Talk("Magic", "Look at it properly", (c, m) => { c.Karma += 1; c.KarmaEarned += 1; c.Count("ghouls"); return "Its aura's starving, not mad. You throw it the protein bars from the glovebox. It takes them and is gone into the dark. Karma +1."; }),
                Ok("Drive on", "You go round it very slowly. It watches you all the way."),
            }, Weight: 0.8f, Cooldown: 7, Place: "street:ghoul");
    }
}
