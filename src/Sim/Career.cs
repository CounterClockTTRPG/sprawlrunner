using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Sprawl;

public sealed class LootItem
{
    public string Name;
    public int Value;
    /// <summary>Whose it was (-1: nobody's), the day it was carried out, and whether that made the news.</summary>
    public int Faction = -1;
    public int Day;
    public bool Hot;
}

/// <summary>
/// One shadowrunner's whole life as plain data: what they are, own and know.
/// A save is this as JSON. Derived numbers (max HP, spread, hacking speed...)
/// are read off it here so the run, the shops and the UI agree.
/// </summary>
public sealed partial class Career
{
    public string Name = "Runner";
    public string Archetype = "samurai";
    public ulong Seed;
    public Rng Rng = new(1);
    public int Day = 1;
    public int Cash;
    /// <summary>Real time spent playing this career, in seconds: driving, in places, on runs (never paused, never on the title).</summary>
    public double PlaySeconds;
    public int Karma, KarmaEarned;
    public int Rep;
    public int[] Attrs = new int[6];
    public int[] Skills = new int[8];
    public List<string> Cyber = new();
    public List<string> Programs = new();
    public List<string> Spells = new();
    public bool PowerFocus;
    public List<string> Weapons = new();
    public List<string> Mods = new();
    /// <summary>Ammo types bought (gel, apds, explosive), and which one each gun is loaded with.</summary>
    public List<string> AmmoOwned = new();
    public Dictionary<string, string> AmmoFor = new();
    public string Primary, Secondary, Melee = "knife";
    public List<string> ArmorsOwned = new() { "clothes" };
    public string ArmorId = "clothes";
    public Dictionary<string, int> Grenades = new();
    public int Medkits;
    public List<string> Vehicles = new();
    /// <summary>Drones owned, and the one that comes on runs.</summary>
    public List<string> Drones = new();
    public string DroneSel;
    public string Vehicle = "junker";
    public Dictionary<string, int> CarDamage = new();
    public List<string> Upgrades = new();
    public Dictionary<string, int> Loyalty = new();
    public List<LootItem> Loot = new();
    public int TraumaLinks;
    /// <summary>Police interest, 0..5 stars.</summary>
    public float Heat;
    /// <summary>A courier package in the car, and the time left to deliver it.</summary>
    public bool Courier;
    public float CourierT;
    /// <summary>A good meal at the market: +15 % max HP on the next run.</summary>
    public bool WellFed;
    public List<Job> Board = new();
    public int ActiveJob = -1;
    /// <summary>The job whose run is under way (-1 between runs): a quit mid-run counts as bailing out.</summary>
    public int InRun = -1;
    /// <summary>The city generator this career's map was made with.</summary>
    public int CityVersion = City.Version;
    /// <summary>Nuyen spent at each contact: every ¥8,000 earns a point of loyalty, up to 3.</summary>
    public Dictionary<string, int> Spent = new();
    /// <summary>How hard each faction remembers you: +1 a job against them, -1 a week.</summary>
    public Dictionary<Faction, int> Grudge = new();
    /// <summary>The story so far (chapters finished, 0..4), and whether you walked away a winner.</summary>
    public int Chapter;
    /// <summary>How far through each contact's own story (0-3).</summary>
    public Dictionary<string, int> ArcStep = new();
    public bool Retired;
    /// <summary>Lasting wounds: each is -10 % max HP until slept off or patched up.</summary>
    public int Injuries;
    /// <summary>The day each contact last did you a favour.</summary>
    public Dictionary<string, int> FavourDay = new();
    /// <summary>Favours waiting on the next run.</summary>
    public bool NextNoAmbush, NextCamerasDown, NextFastSpells;
    public int NextJobId = 1;
    /// <summary>The two chrome ability slots (Q, F).</summary>
    public Ability SlotQ, SlotF;
    /// <summary>The three spells carried onto a run (Z, X, C), by spell id; null for an empty slot.</summary>
    public string[] SpellSlots = new string[3];
    /// <summary>Old saves kept spells in the Q/F slots; set once they have moved into <see cref="SpellSlots"/>.</summary>
    public bool SpellSlotsSet;
    public int JobsDone, JobsFailed, Kills, Deaths;
    public bool Dead;
    public List<string> News = new();
    /// <summary>Where the car is parked in the city, in tiles; NaN means at home.</summary>
    public float CarX = float.NaN, CarY = float.NaN, CarHeading;
    /// <summary>The place you set as your target on the map (a lot index; -1: none, and the job is the target).</summary>
    public int TargetLot = -1;

    public const float StartEssence = 6f;

    // ---- creation -------------------------------------------------------------

    public static readonly (string id, string name, string blurb)[] Archetypes =
    {
        ("samurai", "Street Samurai", "Chrome and guns. Tough, quick, wired reflexes (Overclock)."),
        ("decker", "Decker", "Logic and a cyberdeck (Hack). Turns the site's own turrets on it."),
        ("face", "Face", "Talks prices down, walks past guards. Silenced pistol, more nuyen."),
        ("engineer", "Combat Engineer", "Strength, demolitions and a shotgun. Brings extra grenades."),
        ("mage", "Street Mage", "Awakened. Manabolt and Heal; more spells from the talismonger. Chrome dims the magic."),
    };

    public static Career New(string name, string archetype, ulong seed)
    {
        var c = new Career { Name = string.IsNullOrWhiteSpace(name) ? "Runner" : name.Trim(), Archetype = archetype, Seed = seed, Rng = new Rng(seed ^ 0xC0FFEE), SpellSlotsSet = true };
        c.Cash = 2500;
        c.Weapons.AddRange(new[] { "hollow9", "knife" });
        c.Secondary = "hollow9";
        c.Melee = "knife";
        c.TraumaLinks = 1;
        c.Grenades["frag"] = 2;
        c.Grenades["flash"] = 1;
        c.Medkits = 2;
        c.Vehicles.Add("junker");
        c.Vehicle = "junker";
        foreach (ContactDef k in Tables.Contacts) c.Loyalty[k.Id] = 0;
        int[] a, s;
        switch (archetype)
        {
            case "decker":
                a = new[] { 2, 2, 1, 2, 4, 2 }; s = new[] { 1, 0, 0, 0, 2, 1, 0, 3 };
                c.Install("deck");
                c.GiveProgram("breaker");
                break;
            case "face":
                a = new[] { 2, 3, 1, 2, 2, 4 }; s = new[] { 1, 0, 0, 0, 1, 2, 1, 0 };
                c.Weapons.Add("whisper");
                c.Primary = "whisper";
                c.Cash = 6000;
                c.ArmorsOwned.Add("jacket");
                c.ArmorId = "jacket";
                break;
            case "engineer":
                a = new[] { 3, 2, 3, 2, 2, 1 }; s = new[] { 0, 2, 0, 3, 1, 0, 0, 0 };
                c.Weapons.Add("breacher");
                c.Primary = "breacher";
                c.Grenades["frag"] = 4;
                c.Grenades["breach"] = 1;
                break;
            case "mage":
                a = new[] { 3, 2, 1, 2, 4, 2 }; s = new[] { 1, 0, 0, 0, 0, 2, 0, 0 };
                c.Spells.AddRange(new[] { "manabolt", "heal" });
                c.SpellSlots = new[] { "manabolt", "heal", null };
                c.ArmorsOwned.Add("jacket");
                c.ArmorId = "jacket";
                break;
            default:
                c.Archetype = "samurai";
                a = new[] { 3, 3, 2, 3, 1, 1 }; s = new[] { 2, 0, 2, 0, 0, 0, 1, 0 };
                c.Install("wired");
                c.Weapons.Add("katana");
                c.Melee = "katana";
                break;
        }
        c.Attrs = a;
        c.Skills = s;
        c.LoadedSet = true;
        c.Etiquettes.AddRange(archetype switch
        {
            "decker" => new[] { "Corporate" },
            "face" => new[] { "Street", "Corporate" },
            "engineer" => new[] { "Gang" },
            "mage" => new[] { "Magic" },
            _ => new[] { "Street", "Security" },
        });
        c.News.Add("Day 1. A burner commlink, a car that mostly starts, one TraumaLink pick-up the fixer paid for, and a terminal full of work.");
        return c;
    }

    public enum Sky { Clear, Rain, Storm, Fog }

    /// <summary>Each day's weather, from the career seed.</summary>
    public Sky Weather => ((int)(((Seed ^ (ulong)Day * 0x9E3779B97F4A7C15UL) >> 40) % 7)) switch { 0 or 1 or 2 => Sky.Clear, 3 or 4 => Sky.Rain, 5 => Sky.Storm, _ => Sky.Fog };

    public string ArchetypeName => Archetypes.FirstOrDefault(x => x.id == Archetype).name ?? Archetype;

    // ---- derived numbers --------------------------------------------------------

    public bool Has(string cyber) => Cyber.Contains(cyber);
    public bool ArcDone(string contact) => ArcStep.GetValueOrDefault(contact) >= 3;
    /// <summary>
    /// How a finished contact story ended: 1 the way they hoped, 2 the other
    /// way (see the fork storylets), 3 Ichikawa's files burned but his chrome
    /// declined, 0 not finished or not yet decided.
    /// Stories finished before the fork existed count as 1.
    /// </summary>
    public int ArcPath(string contact) => !ArcDone(contact) ? 0 : Flag("arc:" + contact) switch { 0 => 1, < 0 => 0, var v => v };
    /// <summary>A program that is owned and loaded into the deck: only those run.</summary>
    public bool HasProgram(string p) => Programs.Contains(p) && Loaded.Contains(p);
    public bool OwnsProgram(string p) => Programs.Contains(p);
    public bool HasUpgrade(string u) => Upgrades.Contains(u);
    public bool HasMod(string weapon, string mod) => Mods.Contains(weapon + ":" + mod);
    public int A(Attr a) => Attrs[(int)a];
    public int S(Skill s) => Skills[(int)s];

    public float Essence => StartEssence - Cyber.Sum(EssenceOf) - (ArcPath("talismonger") == 2 ? 0.5f : 0);
    public int Strength => A(Attr.Strength) + (Has("muscle") ? 2 : 0);
    public float MaxHp => (60 + 20 * A(Attr.Body) + (Has("bone") ? 40 : 0)) * (WellFed ? 1.15f : 1f) * (Has("painedit") ? 1 : 1 - 0.1f * Injuries);
    public int ArmorValue => Tables.Armor(ArmorId).Armor + (Has("dermal") ? 3 : 0) + (ArcPath("talismonger") == 1 ? 2 : 0) + (Flag("totem") > 0 ? 1 : 0);
    public float MoveSpeed => 4.2f * (1 + 0.04f * A(Attr.Agility)) * Tables.Armor(ArmorId).SpeedMul * (Has("movebywire") ? 1.15f : 1f);
    public int Fov => 13 + (Has("eyes") ? 3 : 0);
    public bool Thermal => Has("thermal");
    public float ThrowRange => 6 + Strength;
    public float HackRate => (1 + 0.2f * LogicEff) * (1 + 0.15f * S(Skill.Electronics)) * (HasProgram("breaker") ? 1.5f : 1f) * DeckMul * (HasFactionItem("chip") ? 1.25f : 1f) * (HasProgram("lamplight") ? 1.25f : 1f);
    public float DetectMul => MathF.Max(0.2f, (1 - 0.12f * S(Skill.Stealth)) * (1 - Tables.Armor(ArmorId).Stealth));
    /// <summary>Reaction including the doc's booster.</summary>
    public int Reflexes => A(Attr.Reaction) + (Has("booster") ? 2 : 0) + (Has("synaptic") ? 1 : 0);
    public float DashCooldown => 1.6f * (Has("synth") ? 0.6f : 1f) * (Has("movebywire") ? 0.5f : 1f) * (1 - 0.06f * Reflexes);
    public float CooldownMul => (1 - 0.06f * Reflexes) * (NextFastSpells ? 0.5f : 1f) * (Has("synaptic") ? 0.9f : 1f);
    public float GrenadeDamageMul => 1 + 0.1f * S(Skill.Demolitions);
    public float DrivingMul => 1 + 0.06f * S(Skill.Driving);
    public float MedkitHeal => Tables.MedkitHeal * (0.5f + Essence / 12f) * (ArcPath("doc") == 3 ? 1.5f : 1f);
    public int GrenadeCap => 3 + (S(Skill.Demolitions) >= 2 ? 1 : 0) + (S(Skill.Demolitions) >= 4 ? 1 : 0) + (HasUpgrade("armory") ? 1 : 0);
    public int VehicleCap => HasUpgrade("garage") ? 5 : 2;
    public int BoardSize => 4 + (HasUpgrade("rig1") ? 2 : 0) + (HasUpgrade("rig2") ? 2 : 0);
    public int MaxThreat => Math.Min(5, 1 + Rep / 3);

    public float SpreadMul(WeaponDef w)
    {
        float m = 1 - 0.06f * A(Attr.Agility);
        if (!w.Melee) m *= 1 - 0.1f * S(w.Skill);
        if (Has("smartlink")) m *= 0.65f;
        if (HasMod(w.Id, "laser")) m *= 0.8f;
        return MathF.Max(0.12f, m);
    }

    public float ReloadMul(WeaponDef w) => 1f / (1 + 0.05f * S(w.Skill));
    public float CritChance(WeaponDef w) => 0.08f + (w.Skill == Skill.Firearms ? 0.02f * S(Skill.Firearms) : 0);
    public int MagSize(WeaponDef w) => HasMod(w.Id, "extmag") ? (int)MathF.Ceiling(w.Mag * 1.5f) : w.Mag;
    public float NoiseOf(WeaponDef w) => (HasMod(w.Id, "silencer") ? w.Noise * 0.3f : w.Noise) * (Ammo(w) == "explosive" ? 1.3f : 1f);

    /// <summary>The ammo a gun is loaded with: standard, gel, apds or explosive.</summary>
    public string Ammo(WeaponDef w) => !w.Melee && w.Blast <= 0 && !w.NonLethal && AmmoFor.TryGetValue(w.Id, out string a) && AmmoOwned.Contains(a) ? a : "standard";

    /// <summary>A weapon as you would carry it: with spur blades in, the knife is the spurs.</summary>
    public WeaponDef Wield(string id)
    {
        WeaponDef w = Tables.Weapon(id);
        if (id != "knife" || !Has("spurs")) return w;
        w = w.Copy();
        w.Name = "Spur blades";
        w.Damage = Tables.SpurDamage;
        w.Rate = Tables.SpurRate;
        return w;
    }

    public float DamageOf(WeaponDef w)
    {
        if (w.Melee)
        {
            float baseDmg = Has("spurs") && w.Id == "knife" ? Tables.SpurDamage : w.Damage;
            return baseDmg * (1 + 0.12f * Strength) * (1 + 0.12f * S(Skill.Blades)) * (Has("bone") ? 1.3f : 1f);
        }
        return w.Damage * (HasMod(w.Id, "silencer") ? 0.95f : 1f) * Ammo(w) switch { "gel" => 0.7f, "explosive" => 1.2f, _ => 1f };
    }

    public IEnumerable<Ability> Abilities => ChromeAbilities
        .Concat(Spells.Select(id => Tables.Spells.First(x => x.Id == id).Grants)).Where(a => a != Ability.None).Distinct();

    /// <summary>What your chrome can do: the choices for the Q/F slots.</summary>
    public IEnumerable<Ability> ChromeAbilities => Cyber.Select(id => Tables.CyberById(id).Grants).Where(a => a != Ability.None).Distinct();

    /// <summary>What the spell in slot <paramref name="i"/> (0..2) casts.</summary>
    public Ability SpellAbility(int i) => i >= 0 && i < SpellSlots.Length && Tables.Spells.FirstOrDefault(x => x.Id == SpellSlots[i]) is SpellDef d ? d.Grants : Ability.None;

    /// <summary>Put a spell in slot <paramref name="i"/>; if it was already in another slot, the two swap.</summary>
    public void SetSpellSlot(int i, string id)
    {
        int was = id == null ? -1 : Array.IndexOf(SpellSlots, id);
        if (was >= 0 && was != i) SpellSlots[was] = SpellSlots[i];
        SpellSlots[i] = id;
    }

    /// <summary>A newly learned spell goes in the first empty slot.</summary>
    public void SlotSpell(string id)
    {
        int free = Array.IndexOf(SpellSlots, null);
        if (free >= 0 && !SpellSlots.Contains(id)) SpellSlots[free] = id;
    }

    public bool Awakened => Archetype == "mage";

    /// <summary>How hard spells hit: essence lost to chrome weakens them; Logic and a focus strengthen them.</summary>
    public float SpellPower => (0.4f + 0.6f * Essence / StartEssence) * (1 + 0.08f * LogicEff) * FocusPower * (1 + Tables.Armor(ArmorId).Magic) * (ArcPath("talismonger") switch { 1 => 1.1f, 2 => 1.25f, _ => 1f }) * (On("surge") ? 1.2f : 1f) * (Flag("totem") > 0 ? 1.1f : 1f);

    public static float AbilityCooldown(Ability a) => a switch { Ability.Overclock => 22, Ability.Hack => 12, Ability.Cloak => 25, Ability.Manabolt => 3.5f, Ability.Fireball => 9, Ability.Heal => 16, Ability.Summon => 45, Ability.Astral => 20, Ability.Project => 30, Ability.Ward => 20, _ => 0 };
    public static float AbilityDuration(Ability a) => a switch { Ability.Overclock => 4, Ability.Cloak => 5, Ability.Astral => 8, Ability.Summon => 30, Ability.Project => 15, Ability.Ward => 10, _ => 0 };
    public static string AbilityName(Ability a) => a switch
    {
        Ability.Overclock => "Overclock", Ability.Hack => "Hack", Ability.Cloak => "Cloak", Ability.Manabolt => "Manabolt",
        Ability.Fireball => "Fireball", Ability.Heal => "Heal", Ability.Summon => "Summon", Ability.Astral => "Astral", Ability.Project => "Project", Ability.Ward => "Ward", _ => "--",
    };

    public float KarmaMul => HasUpgrade("training") ? 0.85f : 1f;
    public int AttrCost(Attr a) => A(a) >= 6 ? -1 : (int)MathF.Round((A(a) + 1) * 5 * KarmaMul);
    public int SkillCost(Skill s) => S(s) >= 6 ? -1 : (int)MathF.Round((S(s) + 1) * 3 * KarmaMul);

    public string RaiseAttr(Attr a)
    {
        int cost = AttrCost(a);
        if (cost < 0) return "Already at the maximum.";
        if (Karma < cost) return $"Needs {cost} karma.";
        Karma -= cost;
        Attrs[(int)a]++;
        return null;
    }

    public string RaiseSkill(Skill s)
    {
        int cost = SkillCost(s);
        if (cost < 0) return "Already at the maximum.";
        if (Karma < cost) return $"Needs {cost} karma.";
        Karma -= cost;
        Skills[(int)s]++;
        return null;
    }

    /// <summary>Put an implant in (no payment): checks essence and prerequisites.</summary>
    public string Install(string id)
    {
        CyberDef d = Tables.CyberById(id);
        if (Has(id)) return "Already installed.";
        if (d.Requires != null && !Has(d.Requires)) return $"Needs {Tables.CyberById(d.Requires).Name} first.";
        if (Essence - d.Essence * Tables.Grades.First(g => g.id == CyberGrade.GetValueOrDefault(id, "standard")).essence < 0.1f - 1e-4f) return "Not enough essence left. Any more chrome and there's no you.";
        Cyber.Add(id);
        // The spurs are your melee from now on: they take the knife's place.
        if (id == "spurs")
        {
            if (!Weapons.Contains("knife")) Weapons.Add("knife");
            Melee = "knife";
        }
        if (d.Grants != Ability.None)
        {
            if (SlotQ == Ability.None) SlotQ = d.Grants;
            else if (SlotF == Ability.None) SlotF = d.Grants;
        }
        return null;
    }

    public Job Active => Board.FirstOrDefault(j => j.Id == ActiveJob);

    public int CarHp
    {
        get => CarMaxHp - CarDamage.GetValueOrDefault(Vehicle);
        set => CarDamage[Vehicle] = M.Clamp(CarMaxHp - value, 0, CarMaxHp);
    }

    public void Say(string news)
    {
        News.Add($"Day {Day}. {news}");
        if (News.Count > 40) News.RemoveAt(0);
    }

    // ---- saving ----------------------------------------------------------------

    public string ToJson() => JsonSerializer.Serialize(this, Json.Options);
    public static Career FromJson(string s) => JsonSerializer.Deserialize<Career>(s, Json.Options)?.Migrated();
}
