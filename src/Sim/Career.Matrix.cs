using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// The runner's deck: the hardware owned and the one carried, how its four
/// values are dealt out to Attack, Sleaze, Data processing and Firewall, and
/// which programs are loaded into its memory (only loaded programs run; what
/// memory is left holds the paydata pulled on a run). See docs/MATRIX.md.
/// </summary>
public sealed partial class Career
{
    /// <summary>Hardware decks owned (Tables.Decks ids), and the one carried (null: the best of what you have).</summary>
    public List<string> Decks = new();
    public string DeckSel;
    /// <summary>Per deck: its values in Attack, Sleaze, Data processing, Firewall order, as the owner dealt them.</summary>
    public Dictionary<string, int[]> DeckConfig = new();
    /// <summary>Programs loaded into the deck, in load order (the active ones get Q, F, R in that order).</summary>
    public List<string> Loaded = new();
    /// <summary>Loading has been sorted out for this career (old saves load what fits, once).</summary>
    public bool LoadedSet;

    /// <summary>The deck you jack in with: the one chosen, else the best hardware, else the implant, else the commlink.</summary>
    public DeckDef ActiveDeck
    {
        get
        {
            if (DeckSel != null && DeckOwned(DeckSel)) return Tables.Deck(DeckSel);
            DeckDef best = Decks.Select(Tables.Deck).OrderByDescending(d => d.Rating).ThenByDescending(d => d.Cost).FirstOrDefault();
            return best ?? Tables.Deck(Has("deck") ? "implant" : "commlink");
        }
    }

    /// <summary>Decks you could carry: the commlink always, the implant if it's in your head, and what you bought.</summary>
    public IEnumerable<string> DecksOwned => new[] { "commlink" }.Concat(Has("deck") ? new[] { "implant" } : Array.Empty<string>()).Concat(Decks);
    public bool DeckOwned(string id) => id == "commlink" || id == "implant" && Has("deck") || Decks.Contains(id);

    /// <summary>A deck's values as dealt out (Attack, Sleaze, Data processing, Firewall).</summary>
    public int[] DeckValues(string id)
    {
        DeckDef d = Tables.Deck(id);
        return DeckConfig.TryGetValue(id, out int[] v) && v.Length == 4 && v.OrderBy(x => x).SequenceEqual(d.Array.OrderBy(x => x)) ? v : d.Array;
    }

    public int Mx(MxAttr a) => DeckValues(ActiveDeck.Id)[(int)a];

    /// <summary>Swap two of the carried deck's values (SR5's reconfiguration, done at the safehouse).</summary>
    public void SwapDeck(MxAttr a, MxAttr b)
    {
        string id = ActiveDeck.Id;
        int[] v = (int[])DeckValues(id).Clone();
        (v[(int)a], v[(int)b]) = (v[(int)b], v[(int)a]);
        DeckConfig[id] = v;
    }

    /// <summary>The implant runs any other deck hot-sim.</summary>
    public bool HotSim => Has("deck") && ActiveDeck.Id != "implant";
    public int DeckBoxes => Tables.MxBoxesBase + Tables.MxBoxesPer * ActiveDeck.Rating;
    public int Memory => ActiveDeck.Memory;
    public int MemoryUsed => Loaded.Where(Programs.Contains).Sum(p => Tables.Program(p).Mp);
    public int MemoryFree => Memory - MemoryUsed;

    /// <summary>The deck's rating speeds hacking outside the Matrix too.</summary>
    public float DeckMul => ActiveDeck.Rating switch { >= 6 => 1.7f, 5 => 1.5f, 4 => 1.3f, 3 => 1.15f, _ => 1f };

    /// <summary>How quick you are in the Matrix, apart from the deck's Data processing.</summary>
    public float MatrixSpeed => (1 + 0.05f * LogicEff) * (HotSim ? 1.15f : 1f) * (HasFactionItem("chip") ? 1.15f : 1f) * (HasProgram("lamplight") ? 1.15f : 1f);

    /// <summary>Own a program, and load it if there's room.</summary>
    public void GiveProgram(string id)
    {
        if (!Programs.Contains(id)) Programs.Add(id);
        if (!Loaded.Contains(id) && Tables.Program(id).Mp <= MemoryFree) Loaded.Add(id);
    }

    /// <summary>Load or unload a program. Returns why not, or null.</summary>
    public string ToggleProgram(string id)
    {
        if (!Programs.Contains(id)) return "You don't have it.";
        if (Loaded.Remove(id)) return null;
        if (Tables.Program(id).Mp > MemoryFree) return $"No room: {Tables.Program(id).Name} needs {Tables.Program(id).Mp} Mp, {MemoryFree} free.";
        Loaded.Add(id);
        return null;
    }

    /// <summary>Carry another deck: what no longer fits in its memory is unloaded, last loaded first.</summary>
    public void CarryDeck(string id)
    {
        if (!DeckOwned(id)) return;
        DeckSel = id;
        FitLoaded();
    }

    private void FitLoaded()
    {
        Loaded.RemoveAll(p => !Programs.Contains(p));
        while (MemoryUsed > Memory && Loaded.Count > 0) Loaded.RemoveAt(Loaded.Count - 1);
    }

    /// <summary>Old saves: the skill array grows a Decking (as good as your Electronics was), the decks that were sold as
    /// programs become decks, and what you own is loaded, most useful first, until the memory is full.</summary>
    public Career Migrated()
    {
        if (Skills.Length < 8)
        {
            int[] s = new int[8];
            Array.Copy(Skills, s, Skills.Length);
            s[(int)Skill.Decking] = Math.Min(6, Skills.Length > (int)Skill.Electronics ? Skills[(int)Skill.Electronics] : 0);
            Skills = s;
        }
        foreach (string d in new[] { "deck2", "deck3" })
            if (Programs.Remove(d) && !Decks.Contains(d)) Decks.Add(d);
        Programs.RemoveAll(p => Tables.Programs.All(x => x.Id != p));
        if (!LoadedSet)
        {
            LoadedSet = true;
            Loaded.Clear();
            foreach (string p in new[] { "lamplight", "ghost", "spoof", "breaker", "overwatch", "daemon" }.Concat(Programs).Distinct())
                if (Programs.Contains(p) && Tables.Program(p).Mp <= MemoryFree) Loaded.Add(p);
        }
        FitLoaded();
        MigrateSpellSlots();
        return this;
    }

    /// <summary>Old saves: the spells in the Q/F slots move to the spell slots (then the rest you know fill them),
    /// and Q/F keep only what your chrome does.</summary>
    private void MigrateSpellSlots()
    {
        if (SpellSlots == null || SpellSlots.Length != 3) SpellSlots = (SpellSlots ?? Array.Empty<string>()).Concat(new string[3]).Take(3).ToArray();
        if (SpellSlotsSet) return;
        SpellSlotsSet = true;
        var chrome = ChromeAbilities.ToHashSet();
        string SpellFor(Ability a) => chrome.Contains(a) ? null : Spells.FirstOrDefault(id => Tables.Spells.First(x => x.Id == id).Grants == a);
        foreach (string id in new[] { SpellFor(SlotQ), SpellFor(SlotF) }.Concat(Spells))
            if (id != null) SlotSpell(id);
        if (!chrome.Contains(SlotQ)) SlotQ = Ability.None;
        if (!chrome.Contains(SlotF)) SlotF = Ability.None;
        if (SlotQ == Ability.None && SlotF != Ability.None) (SlotQ, SlotF) = (SlotF, Ability.None);
    }

    /// <summary>The active programs loaded, in key order (Q, F, R).</summary>
    public List<string> ActivePrograms => Loaded.Where(p => Programs.Contains(p) && Tables.Program(p).Active).Take(3).ToList();
}
