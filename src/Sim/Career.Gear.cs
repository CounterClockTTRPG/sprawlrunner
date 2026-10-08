using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// The top tier and the money sinks: cyberware grades, power focus ranks,
/// Torque's car work, lifestyle, property and crew retainers. Elite stock is
/// sold only to a runner with a name (<see cref="Prestige"/>).
/// </summary>
public sealed partial class Career
{
    /// <summary>The grade each implant was fitted at (missing: standard).</summary>
    public Dictionary<string, string> CyberGrade = new();
    /// <summary>Torque's work on each vehicle you own.</summary>
    public Dictionary<string, List<string>> CarMods = new();
    /// <summary>Power focus rank beyond the first (0: none or just the one).</summary>
    public int FocusRank;
    /// <summary>How you live: squatter, low, middle, high, luxury.</summary>
    public string Lifestyle = "low";
    /// <summary>Businesses you own.</summary>
    public List<string> Property = new();
    /// <summary>Crew on a weekly retainer instead of a cut.</summary>
    public List<string> Retained = new();

    /// <summary>A saved loadout: what you carry onto a run.</summary>
    public sealed class Kit
    {
        public string Primary, Secondary, Melee, ArmorId, DroneSel;
        public Ability SlotQ, SlotF;
    }

    public Dictionary<string, Kit> Kits = new();

    public void SaveKit(string name) => Kits[name] = new Kit { Primary = Primary, Secondary = Secondary, Melee = Melee, ArmorId = ArmorId, DroneSel = DroneSel, SlotQ = SlotQ, SlotF = SlotF };

    public string UseKit(string name)
    {
        if (!Kits.TryGetValue(name, out Kit k)) return "Nothing saved there yet.";
        // Only what you still own comes out of the locker.
        if (k.Primary == null || Weapons.Contains(k.Primary)) Primary = k.Primary;
        if (k.Secondary == null || Weapons.Contains(k.Secondary)) Secondary = k.Secondary;
        if (Weapons.Contains(k.Melee)) Melee = k.Melee;
        if (ArmorsOwned.Contains(k.ArmorId)) ArmorId = k.ArmorId;
        if (k.DroneSel == null || Drones.Contains(k.DroneSel)) DroneSel = k.DroneSel;
        var have = Abilities.ToHashSet();
        if (k.SlotQ == Ability.None || have.Contains(k.SlotQ)) SlotQ = k.SlotQ;
        if (k.SlotF == Ability.None || have.Contains(k.SlotF)) SlotF = k.SlotF;
        return null;
    }

    // ---- where you come from -------------------------------------------------------------

    public string Background = "street";
    /// <summary>Gentle mode: a save each morning to wake up to if you die. Not ironman.</summary>
    public bool Gentle;

    public static readonly (string id, string name, string blurb)[] Backgrounds =
    {
        ("street", "Street kid", "You grew up on the Barrens' roofs. Street etiquette; Old Wen has known you since you were stealing from him. Your past: the crew you ran with as a kid."),
        ("corp", "Ex-corporate", "You were a Halcyon-Matsuda wage slave until you saw something you shouldn't have. Corporate etiquette, +¥2,000 of severance. Your past: Halcyon wants its property back."),
        ("cop", "Ex-Lone Star", "You wore the badge until the badge wore you out. Security etiquette; Sgt. Dace was your sergeant. Your past: the partner who went dirty."),
        ("shaman", "Outsider shaman", "You came to the city from the hills, following a spirit's voice. Magic etiquette; Madame Sabine took you in. Your past: the totem that sent you."),
        ("barrens", "Barrens-born", "Born in a Viper squat, out before you were ten. Gang etiquette; Torque taught you to drive. Your past: the Viper who killed your brother."),
    };

    public void SetBackground(string id)
    {
        Background = Backgrounds.Any(b => b.id == id) ? id : "street";
        string code = Background switch { "corp" => "Corporate", "cop" => "Security", "shaman" => "Magic", "barrens" => "Gang", _ => "Street" };
        if (!Etiquettes.Contains(code)) Etiquettes.Add(code);
        string friend = Background switch { "corp" => "press", "cop" => "cop", "shaman" => "talismonger", "barrens" => "rigger", _ => "fence" };
        Loyalty[friend] = Math.Max(1, Loyalty.GetValueOrDefault(friend));
        if (Background == "corp") Cash += 2000;
    }

    // ---- spell ranks -----------------------------------------------------------------------

    /// <summary>How far each spell has been practised (0-3): +15 % power a rank.</summary>
    public Dictionary<string, int> SpellRank = new();

    public float PowerOf(Ability a)
    {
        string id = Tables.Spells.FirstOrDefault(s => s.Grants == a && Spells.Contains(s.Id))?.Id;
        return SpellPower * (1 + 0.15f * (id == null ? 0 : SpellRank.GetValueOrDefault(id)));
    }

    public int SpellRankCost(string id) => SpellRank.GetValueOrDefault(id) >= 3 ? -1 : (int)MathF.Round(8 * (SpellRank.GetValueOrDefault(id) + 1) * KarmaMul);

    public string RaiseSpell(string id)
    {
        int cost = SpellRankCost(id);
        if (cost < 0) return "Mastered.";
        if (Karma < cost) return $"Needs {cost} karma.";
        Karma -= cost;
        SpellRank[id] = SpellRank.GetValueOrDefault(id) + 1;
        return null;
    }

    // ---- edge -------------------------------------------------------------------------------

    /// <summary>Edge bought with karma (0-2), on top of what Charisma gives.</summary>
    public int EdgeBought;

    public int EdgeMax => System.Math.Min(Tables.EdgeMax, 1 + A(Attr.Charisma) / 3 + EdgeBought);

    public int EdgeCost => EdgeBought >= 2 ? -1 : (int)MathF.Round(Tables.EdgeKarma * (EdgeBought + 1) * KarmaMul);

    public string BuyEdge()
    {
        int cost = EdgeCost;
        if (cost < 0) return "As lucky as anyone gets.";
        if (Karma < cost) return $"Needs {cost} karma.";
        Karma -= cost;
        EdgeBought++;
        return null;
    }

    public string EliteBlock(bool elite) => elite && !Prestige ? "Only for a runner with a name (street rep 18, or 14 after the Spire)." : null;

    // ---- cyberware grades ---------------------------------------------------------------

    public (string id, string name, float essence, float price, int loyalty) GradeOf(string implant) =>
        Tables.Grades.FirstOrDefault(g => g.id == CyberGrade.GetValueOrDefault(implant, "standard"));

    public float EssenceOf(string implant) => Tables.CyberById(implant).Essence * GradeOf(implant).essence;

    /// <summary>The next grade an installed implant could be refitted at, or null.</summary>
    public (string id, string name, float essence, float price, int loyalty)? NextGrade(string implant)
    {
        int at = Array.FindIndex(Tables.Grades, g => g.id == GradeOf(implant).id);
        return at + 1 < Tables.Grades.Length ? Tables.Grades[at + 1] : null;
    }

    // ---- derived numbers from the top tier --------------------------------------------------

    public int LogicEff => A(Attr.Logic) + (Has("cerebral") ? 1 : 0);
    public float DeckMul => HasProgram("deck3") ? 1.7f : HasProgram("deck2") ? 1.3f : 1f;
    /// <summary>The power focus: rank 1 is the one Sabine has always sold; ranks 2 and 3 are refinements of it.</summary>
    public float FocusPower => FocusRank >= 2 ? Tables.FocusRanks[Math.Min(FocusRank, Tables.FocusRanks.Length) - 1].power : PowerFocus ? 1.25f : 1f;
    public bool OwnsHeli => Vehicles.Any(v => Tables.Vehicle(v).Air);
    public float OverclockFor => Has("wired2") ? 6 : 4;

    public float Duration(Ability a) => a == Ability.Overclock ? OverclockFor : AbilityDuration(a);
    public float Cooldown(Ability a) => AbilityCooldown(a) * CooldownMul * (a == Ability.Overclock && Has("wired2") ? 0.7f : 1f);

    // ---- cars --------------------------------------------------------------------------------

    public bool CarHas(string mod, string vehicle = null) => CarMods.TryGetValue(vehicle ?? Vehicle, out var l) && l.Contains(mod);

    /// <summary>The car you drive, with Torque's work in it.</summary>
    public VehicleDef Ride(string id = null)
    {
        id ??= Vehicle;
        VehicleDef v = Tables.Vehicle(id).Copy();
        bool H(string m) => CarHas(m, id);
        float top = H("engine3") ? 1.25f : H("engine2") ? 1.16f : H("engine1") ? 1.08f : 1f;
        top *= H("armour3") ? 0.9f : H("armour2") ? 0.94f : H("armour1") ? 0.97f : 1f;
        v.Top *= top;
        v.Accel *= H("accel2") ? 1.3f : H("accel1") ? 1.15f : 1f;
        v.Grip = MathF.Min(0.99f, v.Grip * (H("handling2") ? 1.1f : H("handling1") ? 1.05f : 1f));
        v.Hp = (int)(v.Hp * (H("armour3") ? 2f : H("armour2") ? 1.5f : H("armour1") ? 1.25f : 1f));
        if (H("offroad")) v.Offroad = true;
        return v;
    }

    public int CarMaxHp => Ride().Hp;

    // ---- lifestyle, property, retainers -------------------------------------------------------

    public static readonly (string id, string name, string desc, int week)[] Lifestyles =
    {
        ("squatter", "Squatter", "A mattress and a lock. Sleeping doesn't mend injuries, and the cops roust the building now and then (+0.5 heat some mornings).", 100),
        ("low", "Low", "Four walls that mostly keep the rain out. Sleep mends one injury.", 400),
        ("middle", "Middle", "Clean, quiet, a door that locks properly. Sleep mends every injury; raiders need more fame before they bother.", 1500),
        ("high", "High", "A doorman, a chef, a view. All of the above, and you start every run well fed.", 5000),
        ("luxury", "Luxury", "Building security that answers to you. All of the above; nobody raids you; TraumaLink costs 30 % less; and you sleep well enough to learn (+1 karma a week).", 15000),
    };

    public int LifeTier => Math.Max(0, Array.FindIndex(Lifestyles, l => l.id == Lifestyle));
    public string LifestyleName => Lifestyles[LifeTier].name;

    /// <summary>How you live, in one line: the tier and the rent.</summary>
    public string LifestyleLine => $"{LifestyleName} lifestyle -- {M.Money(RentDue)} a week, {M.Money(DailyRent)} every morning.";
    public int RaidFame => LifeTier >= 4 ? 99 : LifeTier >= 2 ? 6 + (LifeTier - 1) : 6;

    public static readonly (string id, string name, string desc, int cost, int week, string district)[] Businesses =
    {
        ("noodles", "A noodle bar in the Docks", "Pays ¥1,500 a week. Mama Oyelaran runs it for a share.", 40000, 1500, "docks"),
        ("garage", "A body shop in the Barrens", "Pays ¥3,000 a week, and fixes your car for free.", 80000, 3000, "barrens"),
        ("club", "A club on the Neon Strip", "Pays ¥6,000 a week. The Lotus will notice.", 150000, 6000, "strip"),
        ("node", "A data haven node in the Spire", "Pays ¥10,000 a week. Null keeps it running.", 250000, 10000, "spire"),
    };

    public static readonly Dictionary<string, int> Retainers = new() { ["ox"] = 2500, ["wisp"] = 3000, ["patch"] = 2000, ["ade"] = 1800, ["byte"] = 2200 };

    /// <summary>The week turns: income, wages, and the karma of a quiet life.</summary>
    private void Payday()
    {
        foreach (string b in Property.ToList())
        {
            var def = Businesses.First(x => x.id == b);
            // A business on a street your enemies hold pays them first.
            bool leaned = Grudge.GetValueOrDefault(Holder(def.district)) >= 4;
            int take = leaned ? def.week / 2 : def.week;
            Cash += take;
            Count("earned", take);
            Say($"{def.name}: {M.Money(take)} this week{(leaned ? $" (the {Tables.FactionNames[(int)Holder(def.district)]} took their cut)" : "")}.");
        }
        foreach (string id in Retained.ToList())
        {
            int wage = Retainers.GetValueOrDefault(id);
            if (CrewBlock(id)?.Contains("dead") == true) { Retained.Remove(id); continue; }
            if (Cash >= wage) { Cash -= wage; Count("spent", wage); }
            else
            {
                Retained.Remove(id);
                CrewOf(id).Trust = Math.Max(0, CrewOf(id).Trust - 1);
                Say($"You couldn't pay {Tables.Hires.First(h => h.id == id).name}'s retainer. They walked, a little hurt.");
            }
        }
        if (LifeTier >= 4) { Karma++; KarmaEarned++; }
    }
}
