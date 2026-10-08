using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

public enum ShopKind { Service, Grade, CarMod, Lifestyle, Property, Retainer, Drone, Favour, Heal, Hire, Ammo, Spell, Focus, Weapon, Grenade, Armor, Medkit, Mod, Cyber, Program, Vehicle, Upgrade, TraumaLink, Schematics, Repair, Sell, SellAll }

/// <summary>One line in a shop: what, how much, and why not (if not).</summary>
public sealed class Offer
{
    public ShopKind Kind;
    public string Id, Name, Desc, Extra;
    public int Price;
    /// <summary>Null when it can be bought right now; otherwise the reason it cannot.</summary>
    public string Block;
    public string Owned;
}

public sealed partial class Career
{
    public float PriceMul(string contact)
    {
        int loyalty = contact != null ? Loyalty.GetValueOrDefault(contact) : 0;
        // The clean contacts charge a runner with a bad name extra.
        float bad = contact is "doc" or "talismonger" && Notoriety >= 4 ? 1.25f : 1f;
        return MathF.Max(0.6f, 1 - 0.04f * loyalty - 0.03f * A(Attr.Charisma)) * bad;
    }

    public float SellMul => ArcPath("fence") == 1 ? 1f : MathF.Min(0.95f, 0.6f + 0.05f * Loyalty.GetValueOrDefault("fence") + 0.02f * A(Attr.Charisma));

    public int TraumaLinkPrice => (int)(MathF.Round(1800 * (1 + Rep / 5f) * (HasUpgrade("medbay") ? 0.7f : 1f) * PriceMul("doc") * (TraumaGrudge ? 2 : 1) / 50f) * 50);

    public int RepairPrice(bool atHome)
    {
        int dmg = CarDamage.GetValueOrDefault(Vehicle);
        if (dmg <= 0 || (atHome && HasUpgrade("garage")) || !atHome && (ArcPath("rigger") == 2 || Property.Contains("garage"))) return 0;
        return (int)(dmg * 5 * PriceMul("rigger"));
    }

    /// <summary>The tow when the car is wrecked: dearer than a repair, so wrecking is never the cheap way home.</summary>
    public int TowFee => Math.Min(Cash / 2, 300 + Cash / 10);

    private int P(int cost, string contact) => (int)(MathF.Round(cost * PriceMul(contact) / 10f) * 10);

    /// <summary>The stock of a contact ("home" for the safehouse).</summary>
    public List<Offer> Offers(string contact)
    {
        var list = new List<Offer>();
        int loyalty = Loyalty.GetValueOrDefault(contact);
        string Need(int l) => loyalty < l ? $"Needs loyalty {l}." : null;
        switch (contact)
        {
            case "arms":
                foreach (WeaponDef w in Tables.Weapons.Where(w => !w.Id.StartsWith("e_") && !w.Id.StartsWith("a_") && w.Cost > 0 && w.Id != "hollow9" && w.Id != "knife"))
                    list.Add(new Offer { Kind = ShopKind.Weapon, Id = w.Id, Name = w.Name, Desc = WeaponLine(w) + (Weapons.Contains(w.Id) ? "" : Compare(w)), Price = P(w.Cost, contact), Block = Weapons.Contains(w.Id) ? "Owned." : Need(w.Loyalty) ?? EliteBlock(w.Elite), Owned = Weapons.Contains(w.Id) ? "owned" : null });
                foreach (GrenadeDef g in Tables.Grenades)
                {
                    int have = Grenades.GetValueOrDefault(g.Id);
                    list.Add(new Offer { Kind = ShopKind.Grenade, Id = g.Id, Name = g.Name, Desc = g.Desc, Price = P(g.Cost, contact), Block = have >= GrenadeCap ? $"Carrying the most you can ({GrenadeCap})." : Need(g.Loyalty), Owned = $"{have}/{GrenadeCap}" });
                }
                list.Add(new Offer { Kind = ShopKind.Medkit, Id = "medkit", Name = "Medkit", Desc = $"Heals {MedkitHeal:0} (essence-adjusted).", Price = P(Tables.MedkitCost, contact), Block = Medkits >= 5 ? "Carrying five already." : null, Owned = $"{Medkits}/5" });
                foreach (ArmorDef a in Tables.Armors.Skip(1))
                    list.Add(new Offer { Kind = ShopKind.Armor, Id = a.Id, Name = a.Name, Desc = $"Armour {a.Armor}. {a.Desc}", Price = P(a.Cost, contact), Block = ArmorsOwned.Contains(a.Id) ? "Owned." : Need(a.Loyalty) ?? EliteBlock(a.Elite), Owned = ArmorsOwned.Contains(a.Id) ? "owned" : null });
                foreach (var am in Tables.AmmoTypes)
                    list.Add(new Offer { Kind = ShopKind.Ammo, Id = am.id, Name = am.name, Desc = am.desc + " Load it per gun in the safehouse loadout.", Price = P(am.cost, contact), Block = AmmoOwned.Contains(am.id) ? "Owned." : Need(am.loyalty), Owned = AmmoOwned.Contains(am.id) ? "owned" : null });
                foreach (string wid in Weapons.Where(id => Tables.Weapon(id).Moddable))
                foreach (ModDef m in Tables.Mods)
                {
                    if (HasMod(wid, m.Id)) continue;
                    if (m.Id == "silencer" && wid == "whisper") continue;
                    list.Add(new Offer { Kind = ShopKind.Mod, Id = m.Id, Extra = wid, Name = $"{m.Name} for {Tables.Weapon(wid).Name}", Desc = m.Desc, Price = P(m.Cost, contact) });
                }
                break;
            case "doc":
                if (TraumaGrudge)
                    list.Add(new Offer { Kind = ShopKind.TraumaLink, Id = "amends", Name = "Make amends with TraumaLink", Desc = "You shot at one of their crews. A donation to the widows' fund, and your contracts go back to the normal price.", Price = P(5000, contact) });
                list.Add(new Offer { Kind = ShopKind.TraumaLink, Id = "tlplat", Name = "TraumaLink Platinum", Desc = "For good: go down with the job already done, and the HTR team brings you out with it -- the job counts, at half pay. Needs Gold.", Price = P(40000, contact), Block = TraumaPlatinum ? "On file." : !TraumaGold ? "Needs TraumaLink Gold." : TraumaGrudge ? "Not while TraumaLink remembers what you did." : null, Owned = TraumaPlatinum ? "platinum" : null });
                list.Add(new Offer { Kind = ShopKind.TraumaLink, Id = "tlgold", Name = "TraumaLink Gold", Desc = "For good: a pick-up no longer costs you a fifth of your cash, and the trauma team pulls out whoever you hired too. Still needs a contract on file for you.", Price = P(15000, contact), Block = TraumaGold ? "On file." : TraumaGrudge ? "Not while TraumaLink remembers what you did." : null, Owned = TraumaGold ? "gold" : null });
                list.Add(new Offer { Kind = ShopKind.TraumaLink, Id = "traumalink", Name = "TraumaLink contract", Desc = "If you go down on a run, a trauma team pulls you out. Job lost, 20 % of your cash for the trouble. One pick-up per contract.", Price = TraumaLinkPrice, Block = TraumaLinks >= 3 ? "Three on file already." : null, Owned = $"{TraumaLinks} on file" });
                foreach (CyberDef d in Tables.Cyber.Where(c => c.Loyalty < 9))
                {
                    string block = Has(d.Id) ? "Installed." : Need(d.Loyalty) ?? EliteBlock(d.Elite)
                        ?? (d.Requires != null && !Has(d.Requires) ? $"Needs {Tables.CyberById(d.Requires).Name}." : null)
                        ?? (Essence - d.Essence < 0.1f - 1e-4f ? "Not enough essence." : null);
                    list.Add(new Offer { Kind = ShopKind.Cyber, Id = d.Id, Name = d.Name, Desc = $"Essence {(Has(d.Id) ? EssenceOf(d.Id) : d.Essence):0.00}{(Has(d.Id) ? $" ({GradeOf(d.Id).name})" : "")}. {d.Desc}{(Awakened ? $" Spell power -{0.6f * d.Essence / StartEssence * 100:0} %." : "")}", Price = P(d.Cost, contact), Block = block, Owned = Has(d.Id) ? "installed" : null });
                    // The same implant, finer work, straight from the box: the best grade Ichikawa trusts you with.
                    if (!Has(d.Id))
                    {
                        var g = Tables.Grades.LastOrDefault(x => x.id != "standard" && Loyalty.GetValueOrDefault(contact) >= x.loyalty && (x.id != "delta" || Prestige));
                        if (g.id != null)
                        {
                            string gblock = block == "Not enough essence." || block == null
                                ? (Essence - d.Essence * g.essence < 0.1f - 1e-4f ? "Not enough essence." : null)
                                : block;
                            list.Add(new Offer { Kind = ShopKind.Cyber, Id = d.Id, Extra = g.id, Name = $"{d.Name} ({g.name})", Desc = $"Essence {d.Essence * g.essence:0.00} instead of {d.Essence:0.00}. {d.Desc}", Price = P((int)(d.Cost * g.price), contact), Block = gblock });
                        }
                    }
                }
                foreach (string id in Cyber)
                {
                    CyberDef d = Tables.CyberById(id);
                    if (d.Loyalty >= 9 || NextGrade(id) is not var (gid, gname, gess, gprice, gloy)) continue;
                    var cur = GradeOf(id);
                    list.Add(new Offer { Kind = ShopKind.Grade, Id = id, Extra = gid, Name = $"Refit {d.Name} as {gname}", Desc = $"Same implant, finer work: essence {EssenceOf(id):0.00} -> {d.Essence * gess:0.00}.", Price = P((int)(d.Cost * (gprice - cur.price)), contact), Block = Need(gloy) ?? EliteBlock(gid == "delta") });
                }
                break;
            case "rigger":
                foreach (VehicleDef v in Tables.Vehicles.Skip(1).Where(v => v.Loyalty < 9))
                    list.Add(new Offer { Kind = ShopKind.Vehicle, Id = v.Id, Name = v.Name, Desc = $"Top {v.Top:0}, accel {v.Accel:0.#}, grip {v.Grip:0.00}, hull {v.Hp}. {v.Desc}", Price = P(v.Cost, contact), Block = Vehicles.Contains(v.Id) ? "Owned." : Need(v.Loyalty) ?? EliteBlock(v.Elite) ?? (v.Air ? null : Vehicles.Count(x => !Tables.Vehicle(x).Air) >= VehicleCap ? $"No room: {VehicleCap} vehicles (a garage holds more)." : null), Owned = Vehicles.Contains(v.Id) ? "owned" : null });
                foreach (DroneDef dr in Tables.Drones)
                    list.Add(new Offer { Kind = ShopKind.Drone, Id = dr.Id, Name = dr.Name + " drone", Desc = dr.Desc + " Jack in with T on a run.", Price = P(dr.Cost, contact), Block = Drones.Contains(dr.Id) ? "Owned." : Need(dr.Loyalty) ?? EliteBlock(dr.Id == "juggernaut"), Owned = Drones.Contains(dr.Id) ? "owned" : null });
                AddCarMods(list, contact, loyalty);
                int rp = RepairPrice(false);
                list.Add(new Offer { Kind = ShopKind.Repair, Id = "repair", Name = $"Repair the {Tables.Vehicle(Vehicle).Name}", Desc = $"Hull {CarHp}/{Tables.Vehicle(Vehicle).Hp}.", Price = rp, Block = rp <= 0 ? "Not a scratch on it." : null });
                break;
            case "decker":
                foreach (ProgramDef p in Tables.Programs.Where(p => p.Loyalty < 9))
                    list.Add(new Offer { Kind = ShopKind.Program, Id = p.Id, Name = p.Name, Desc = p.Desc, Price = P(p.Cost, contact), Block = HasProgram(p.Id) ? "Owned." : Need(p.Loyalty) ?? EliteBlock(p.Id == "deck3") ?? (p.Id == "deck3" && !HasProgram("deck2") ? "Needs the Obsidian-class deck first." : null), Owned = HasProgram(p.Id) ? "owned" : null });
                AddSchematics(list, contact);
                break;
            case "fixer":
                AddSchematics(list, contact);
                AddFactionItems(list, contact);
                if (Active is Job hj && !hj.Driving)
                    foreach (var h in Tables.Hires)
                    {
                        CrewState cs = Crew.GetValueOrDefault(h.id);
                        string perks = cs == null || cs.Perks.Count == 0 ? "" : " Knows: " + string.Join(", ", cs.Perks.Select(PerkName)) + ".";
                        string pay = Retained.Contains(h.id) ? "On retainer: no cut." : $"Takes {CrewCut(h.id) * 100:0} % of the pay ({M.Money((int)(hj.Pay * CrewCut(h.id)))}); ¥300 retainer if the job fails.";
                        // One hire per job: hiring someone else sends the first one home.
                        string swap = hj.Hired != null && hj.Hired != h.id ? $" Instead of {Tables.Hires.First(x => x.id == hj.Hired).name}." : "";
                        list.Add(new Offer { Kind = ShopKind.Hire, Id = h.id, Name = $"Hire {h.name} for {hj.Title}" + (cs?.Trust > 0 ? $"  (trust {cs.Trust})" : ""), Desc = $"{h.desc} {pay}{swap}{perks}", Price = 0,
                            Block = hj.Hired == h.id ? "Hired." : CrewBlock(h.id) ?? Need(h.loyalty), Owned = hj.Hired == h.id ? "hired" : cs?.Dead == true ? "dead" : null });
                    }
                foreach (var h in Tables.Hires)
                {
                    int wage = RetainerWage(h.id);
                    bool on = Retained.Contains(h.id);
                    list.Add(new Offer { Kind = ShopKind.Retainer, Id = h.id, Name = on ? $"End {h.name}'s retainer" : $"Put {h.name} on retainer", Desc = on ? "Back to a cut of each job." : $"{M.Money(wage)} every week instead of a cut. One comes at a time: the most trusted free one on retainer joins every job (not driving or hacks) for nothing extra.", Price = 0, Block = on ? null : CrewBlock(h.id) ?? Need(h.loyalty + 1), Owned = on ? "retained" : null });
                }
                foreach (var b in Businesses)
                    list.Add(new Offer { Kind = ShopKind.Property, Id = b.id, Name = $"Buy: {b.name}", Desc = b.desc + " If whoever runs that street hates you, they take half.", Price = b.cost, Block = Property.Contains(b.id) ? "Owned." : Need(b.cost >= 150000 ? 3 : 1), Owned = Property.Contains(b.id) ? "yours" : null });
                break;
            case "talismonger":
            {
                string mundane = Awakened ? null : "Only the Awakened can learn spells.";
                foreach (SpellDef sp in Tables.Spells)
                    list.Add(new Offer { Kind = ShopKind.Spell, Id = sp.Id, Name = sp.Name, Desc = sp.Desc, Price = P(sp.Cost, contact), Block = Spells.Contains(sp.Id) ? "Known." : mundane ?? Need(sp.Loyalty), Owned = Spells.Contains(sp.Id) ? "known" : null });
                if (!PowerFocus)
                    list.Add(new Offer { Kind = ShopKind.Focus, Id = "focus", Name = "Power focus", Desc = $"A bound talisman: spells +25 %. Your spell power now: x{SpellPower:0.00}.", Price = P(Tables.FocusCost, contact), Block = mundane ?? Need(1) });
                else if (FocusRank < Tables.FocusRanks.Length)
                {
                    var next = Tables.FocusRanks[Math.Max(2, FocusRank + 1) - 1];
                    list.Add(new Offer { Kind = ShopKind.Focus, Id = "focus" + Math.Max(2, FocusRank + 1), Name = next.name, Desc = $"Sabine rebinds your focus, deeper: spells x{next.power:0.00} instead of x{FocusPower:0.00}.", Price = P(next.cost, contact), Block = mundane ?? Need(next.loyalty) ?? EliteBlock(next.power >= 1.6f), Owned = $"rank {Math.Max(1, FocusRank)}" });
                }
                break;
            }
            case "fence":
                for (int i = 0; i < Loot.Count; i++)
                    list.Add(new Offer { Kind = ShopKind.Sell, Id = i.ToString(), Name = Loot[i].Name, Desc = $"Worth {M.Money(Loot[i].Value)} on paper.{LootNote(Loot[i])}", Price = LootPrice(Loot[i]) });
                if (Loot.Count > 1)
                    list.Add(new Offer { Kind = ShopKind.SellAll, Id = "all", Name = "Sell everything", Desc = $"{Loot.Count} items.{(Loot.Any(Hot) ? " Some of it is hot." : "")}", Price = Loot.Sum(LootPrice) });
                break;
            case "cop":
            {
                int file = P(300 + (int)(300 * Heat), contact);
                list.Add(new Offer { Kind = ShopKind.Service, Id = "file", Name = "Lose your file", Desc = $"Dace misplaces tonight's paperwork: heat to nothing (now {Heat:0.0}).", Price = file, Block = Heat < 0.2f ? "Nobody's looking for you." : null });
                list.Add(new Offer { Kind = ShopKind.Service, Id = "page", Name = "Pull a page from your file", Desc = $"Fewer people know your face: public awareness -2 (now {Awareness}).", Price = P(2000, contact), Block = Awareness == 0 ? "There's nothing in it." : null });
                list.Add(new Offer { Kind = ShopKind.Service, Id = "warrant", Name = "A patrol outside their door", Desc = "Whoever is waiting to hit you after your next job finds a Lone Star cruiser parked outside instead.", Price = P(2500, contact), Block = Flag("hitters") == 0 && Flag("vendetta") == 0 ? "Nobody is waiting for you. That he knows of." : null });
                list.Add(new Offer { Kind = ShopKind.Service, Id = "plans", Name = "Lose the crackdown plans", Desc = $"Lone Star's crackdown slips a few weeks (its plan -3, now {Clock("lonestar")}/8).", Price = P(3000, contact), Block = Clock("lonestar") < 3 ? "There isn't one coming. Yet." : Need(1) });
                break;
            }
            case "press":
            {
                int story = 400 * Math.Max(1, Awareness);
                list.Add(new Offer { Kind = ShopKind.Service, Id = "sell", Name = "Sell her your story", Desc = $"Names changed, nothing traceable: she pays for the colour. Awareness +2. Once a week.", Price = 0, Extra = story.ToString(),
                    Block = Awareness < 2 ? "You're not a story yet." : Awareness >= 9 ? "You're the whole front page already." : Flag("storyday") > 0 && Day < Flag("storyday") + 6 ? $"She ran you this week. Come back on day {Flag("storyday") + 6}." : null });
                list.Add(new Offer { Kind = ShopKind.Service, Id = "kill", Name = "Kill a story about you", Desc = $"Her editor owes her: awareness -3 (now {Awareness}).", Price = P(2500, contact), Block = Awareness == 0 ? "Nobody's writing about you." : null });
                foreach (Faction f in Enum.GetValues<Faction>())
                {
                    string id = ClockOf(f);
                    list.Add(new Offer { Kind = ShopKind.Service, Id = "smear:" + id, Name = $"Run a story on the {Tables.FactionNames[(int)f]}", Desc = $"A week of bad press sets their plans back (-3, now {Clock(id)}/8).", Price = P(3000, contact), Block = Clock(id) < 3 ? "They aren't up to anything worth printing." : Need(1) });
                }
                break;
            }
            case "home":
                // Every tier, the one you live at marked: moving is free, the rent is what changes.
                foreach (var ls in Lifestyles)
                {
                    bool here = ls.id == Lifestyle;
                    int week = RentOf(Array.IndexOf(Lifestyles, ls));
                    string change = here ? "" : week > RentDue ? $" (+{M.Money(week - RentDue)} on what you pay now)" : $" ({M.Money(RentDue - week)} less than now)";
                    list.Add(new Offer
                    {
                        Kind = ShopKind.Lifestyle, Id = ls.id, Name = here ? $"{ls.name} lifestyle" : $"Move to a {ls.name.ToLower()} lifestyle",
                        Desc = $"{ls.desc} Rent {M.Money(week)} a week{change}.", Price = 0,
                        Block = here ? "You live like this now." : null, Owned = here ? "current" : null,
                    });
                }
                foreach (UpgradeDef u in Tables.Upgrades)
                    list.Add(new Offer { Kind = ShopKind.Upgrade, Id = u.Id, Name = u.Name, Desc = u.Desc, Price = u.Cost, Block = HasUpgrade(u.Id) ? "Installed." : u.Requires != null && !HasUpgrade(u.Requires) ? $"Needs {Tables.Upgrade(u.Requires).Name}." : null, Owned = HasUpgrade(u.Id) ? "installed" : null });
                if (CarDamage.GetValueOrDefault(Vehicle) > 0)
                {
                    VehicleDef vd = Tables.Vehicle(Vehicle);
                    if (HasUpgrade("garage"))
                        list.Add(new Offer { Kind = ShopKind.Repair, Id = "repair", Name = $"Repair the {vd.Name}", Desc = "Your garage does it for free.", Price = 0 });
                    else if (CarHp < MathF.Round(vd.Hp * 0.6f))
                        list.Add(new Offer { Kind = ShopKind.Repair, Id = "patch", Name = $"Patch up the {vd.Name} yourself", Desc = "Duct tape and swearing: back to 60 % hull, free. Torque does the rest properly; a garage does it all.", Price = 0 });
                }
                break;
        }
        AddFavour(list, contact);
        if (Shut(contact) is string shut)
        {
            foreach (Offer o in list) o.Block = shut;
            return list;
        }
        // Past a point, they only patch you up.
        if (contact is "doc" or "talismonger" && Notoriety >= 7)
            foreach (Offer o in list.Where(o => o.Block == null && o.Kind is not (ShopKind.TraumaLink or ShopKind.Favour)))
                o.Block = $"{Tables.Contact(contact).Name} won't sell that to someone with your name.";
        if (contact == "doc" && Injuries > 0)
            list.Insert(0, new Offer { Kind = ShopKind.Heal, Id = "heal", Name = $"Patch up your injuries ({Injuries})", Desc = "Stitches, nanites and a lecture. Back to full health.", Price = P(400 * Injuries, contact) });
        foreach (Offer o in list)
            if (o.Block == null && o.Kind != ShopKind.Sell && o.Kind != ShopKind.SellAll && Cash < o.Price) o.Block = "Not enough nuyen.";
        return list;
    }

    public static readonly Dictionary<string, (string name, string desc)> Favours = new()
    {
        ["fixer"] = ("Make the heat go away", "Juno makes some calls: Lone Star loses interest (heat to 0)."),
        ["rigger"] = ("Getaway driver", "Torque waits by the site after your next job: no ambush on the way home, and he fixes the car."),
        ["decker"] = ("Loop the cameras", "Null loops every camera on your next job's site before you go in."),
        ["doc"] = ("On the house", "A TraumaLink contract, free."),
        ["arms"] = ("A crate of frags", "Two frag grenades, no charge."),
        ["fence"] = ("A collector", "Wen has a buyer who pays full value for everything you carry."),
        ["cop"] = ("Look the other way", "Your next job puts no heat on you, however loud it gets."),
        ["press"] = ("A puff piece", "A warm profile of a 'security consultant' who sounds a lot like you: notoriety -1."),
        ["talismonger"] = ("A charged fetish", "Your spells (and other abilities) recharge twice as fast on the next job."),
    };

    /// <summary>A loyal contact does you a favour now and then (loyalty 2+, every 5 days).</summary>
    private void AddFavour(List<Offer> list, string contact)
    {
        if (!Favours.TryGetValue(contact, out var f)) return;
        int loy = Loyalty.GetValueOrDefault(contact);
        int next = FavourDay.TryGetValue(contact, out int last) ? last + 5 : 0;
        string block = loy < 2 ? "Favours need loyalty 2." : Day < next ? $"Ask again on day {next}." : null;
        if (block == null && contact == "fixer" && Heat <= 0) block = "Nobody's looking for you.";
        if (block == null && contact == "doc" && TraumaLinks >= 3) block = "Three on file already.";
        if (block == null && contact == "fence" && Loot.Count == 0) block = "Nothing to sell.";
        if (block == null && contact == "press" && Notoriety == 0) block = "There's nothing bad to bury.";
        list.Insert(0, new Offer { Kind = ShopKind.Favour, Id = contact, Name = "Favour: " + f.name, Desc = f.desc, Price = 0, Block = block });
    }

    private void AddSchematics(List<Offer> list, string contact)
    {
        Job j = Active;
        if (j == null) return;
        int price = P(300 + 250 * j.Threat, contact);
        list.Add(new Offer { Kind = ShopKind.Schematics, Id = "schematics", Name = $"Site schematics: {j.Title}", Desc = "The building plans of your current job's site: the whole map is known when you go in.", Price = price, Block = j.Schematics || HasUpgrade("rig2") ? "Already have them." : null });
    }

    /// <summary>Torque's work for the car you drive: the next step of each line he hasn't done yet.</summary>
    private void AddCarMods(List<Offer> list, string contact, int loyalty)
    {
        VehicleDef v = Tables.Vehicle(Vehicle);
        foreach (var m in Tables.CarMods)
        {
            if (CarHas(m.id)) continue;
            // Only the next rung of a ladder: a turbo before a twin turbo, and nothing below what's fitted.
            if (m.replaces != null && !CarHas(m.replaces)) continue;
            if (Tables.CarMods.Any(x => x.replaces == m.id && CarHas(x.id))) continue;
            if (m.id == "offroad" && v.Offroad) continue;
            list.Add(new Offer { Kind = ShopKind.CarMod, Id = m.id, Extra = Vehicle, Name = $"{m.name} for the {v.Name}", Desc = m.desc, Price = P(m.cost, contact), Block = loyalty < m.loyalty ? $"Needs loyalty {m.loyalty}." : null });
        }
    }

    public static string PerkName(string perk) => perk switch
    {
        "loyal" => "loyal", "cheap" => "old friend", "wind" => "second wind", "apds" => "AP rounds", "kits" => "more kit", "vest" => "trauma vest", _ => perk,
    };

    /// <summary>Damage per second in your hands (strength, skill, chrome, ammo): what players actually choose by, with noise.</summary>
    public float Dps(WeaponDef w) => DamageOf(w) * Math.Max(1, w.Pellets) * w.Rate;

    public string WeaponLine(WeaponDef w)
    {
        if (w.Melee) return $"Melee {DamageOf(w):0}, {w.Rate:0.#}/s (DPS {Dps(w):0}), reach {w.Reach:0.#}. {w.Desc}";
        string dmg = w.Pellets > 1 ? $"{w.Pellets}x{DamageOf(w):0}" : $"{DamageOf(w):0}";
        return $"DPS {Dps(w):0}  ·  dmg {dmg}, {w.Rate:0.#}/s, mag {w.Mag}, noise {w.Noise:0}{(w.ArmorMul < 1 ? $", armour x{w.ArmorMul:0.#}" : "")}. {w.Desc}";
    }

    /// <summary>The same weapon against what you carry in that slot.</summary>
    public string Compare(WeaponDef w)
    {
        string mine = w.Melee ? Melee : Primary ?? Secondary;
        if (mine == null || mine == w.Id) return "";
        WeaponDef m = Wield(mine);
        float d = Dps(w) - Dps(m);
        return $"  [vs your {m.Name}: DPS {(d >= 0 ? "+" : "")}{d:0}, noise {(w.Noise - m.Noise >= 0 ? "+" : "")}{w.Noise - m.Noise:0}]";
    }

    /// <summary>Buy (or sell) an offer. Returns null on success, else the reason.</summary>
    public string Buy(Offer o)
    {
        if (o.Block != null) return o.Block;
        if (o.Kind is ShopKind.Sell)
        {
            int i = int.Parse(o.Id);
            if (i < 0 || i >= Loot.Count) return "Gone.";
            if (Hot(Loot[i])) Heat = MathF.Min(5, Heat + 0.5f);
            Loot.RemoveAt(i);
            Cash += o.Price;
            Count("earned", o.Price);
            return null;
        }
        if (o.Kind is ShopKind.SellAll)
        {
            Count("earned", o.Price);
            Cash += o.Price;
            if (Loot.Any(Hot)) Heat = MathF.Min(5, Heat + 0.5f);
            Loot.Clear();
            return null;
        }
        if (Cash < o.Price) return "Not enough nuyen.";
        switch (o.Kind)
        {
            case ShopKind.Grade: CyberGrade[o.Id] = o.Extra; break;
            case ShopKind.CarMod:
            {
                if (!CarMods.TryGetValue(o.Extra, out var mods)) CarMods[o.Extra] = mods = new List<string>();
                var def = Tables.CarMods.First(m => m.id == o.Id);
                if (def.replaces != null) mods.Remove(def.replaces);
                mods.Add(o.Id);
                break;
            }
            case ShopKind.Lifestyle: Lifestyle = o.Id; Say($"You moved to a {LifestyleName.ToLower()} lifestyle. Rent is now {M.Money(RentDue)} a week, {M.Money(DailyRent)} every morning."); break;
            case ShopKind.Property: Property.Add(o.Id); break;
            case ShopKind.Retainer: if (!Retained.Remove(o.Id)) Retained.Add(o.Id); break;
            case ShopKind.Service:
                switch (o.Id)
                {
                    case "file": Heat = 0; break;
                    case "page": Fame(-2); break;
                    case "warrant": Set("hitters", 0); Set("vendetta", 0); break;
                    case "plans": Tick("lonestar", -3); break;
                    case "sell": { int pay = int.Parse(o.Extra); Cash += pay; Count("earned", pay); Fame(2); Set("storyday", Day + 1); break; }
                    case "kill": Fame(-3); break;
                    default:
                        if (o.Id.StartsWith("smear:")) Tick(o.Id[6..], -3);
                        if (o.Id.StartsWith("fitem:")) Set(o.Id);
                        break;
                }
                break;
            case ShopKind.Weapon:
                Weapons.Add(o.Id);
                WeaponDef w = Tables.Weapon(o.Id);
                if (w.Melee) Melee = o.Id;
                else if (Primary == null) Primary = o.Id;
                break;
            case ShopKind.Grenade:
                Grenades[o.Id] = Grenades.GetValueOrDefault(o.Id) + 1;
                break;
            case ShopKind.Medkit:
                Medkits++;
                break;
            case ShopKind.Armor:
                ArmorsOwned.Add(o.Id);
                ArmorId = o.Id;
                break;
            case ShopKind.Mod:
                Mods.Add(o.Extra + ":" + o.Id);
                break;
            case ShopKind.Cyber:
                if (o.Extra != null) CyberGrade[o.Id] = o.Extra;
                string err = Install(o.Id);
                if (err != null) { if (o.Extra != null) CyberGrade.Remove(o.Id); return err; }
                break;
            case ShopKind.Program:
                Programs.Add(o.Id);
                break;
            case ShopKind.Drone:
                Drones.Add(o.Id);
                DroneSel = o.Id;
                break;
            case ShopKind.Favour:
                FavourDay[o.Id] = Day;
                switch (o.Id)
                {
                    case "fixer": Heat = 0; break;
                    case "rigger": NextNoAmbush = true; CarDamage[Vehicle] = 0; break;
                    case "decker": NextCamerasDown = true; break;
                    case "doc": TraumaLinks++; break;
                    case "arms": Grenades["frag"] = Math.Min(GrenadeCap, Grenades.GetValueOrDefault("frag") + 2); break;
                    case "fence": foreach (LootItem l in Loot) { int v = Math.Max(l.Value, LootPrice(l)); Cash += v; Count("earned", v); } Loot.Clear(); break;
                    case "talismonger": NextFastSpells = true; break;
                    case "cop": Set("noheat"); break;
                    case "press": Infamy(-1); break;
                }
                break;
            case ShopKind.Heal:
                Injuries = 0;
                break;
            case ShopKind.Hire:
                Active.Hired = o.Id;
                break;
            case ShopKind.Ammo:
                AmmoOwned.Add(o.Id);
                break;
            case ShopKind.Spell:
                Spells.Add(o.Id);
                Ability g = Tables.Spells.First(x => x.Id == o.Id).Grants;
                if (SlotQ == Ability.None) SlotQ = g;
                else if (SlotF == Ability.None) SlotF = g;
                break;
            case ShopKind.Focus:
                if (o.Id == "focus") PowerFocus = true;
                else FocusRank = int.Parse(o.Id[5..]);
                break;
            case ShopKind.Vehicle:
                Vehicles.Add(o.Id);
                if (!Tables.Vehicle(o.Id).Air) Vehicle = o.Id;
                break;
            case ShopKind.Upgrade:
                Upgrades.Add(o.Id);
                break;
            case ShopKind.TraumaLink:
                if (o.Id == "tlgold") Set("tlgold");
                else if (o.Id == "tlplat") Set("tlplat");
                else if (o.Id == "amends") Set("tlgrudge", 0);
                else TraumaLinks++;
                break;
            case ShopKind.Schematics:
                Active.Schematics = true;
                break;
            case ShopKind.Repair:
                if (o.Id == "patch") CarHp = (int)MathF.Round(Tables.Vehicle(Vehicle).Hp * 0.6f);
                else CarDamage[Vehicle] = 0;
                break;
        }
        Cash -= o.Price;
        Count("spent", o.Price);
        if (o.Price > 0 && contactOf(o) is string who)
        {
            int before = Spent.GetValueOrDefault(who) / 8000;
            Spent[who] = Spent.GetValueOrDefault(who) + o.Price;
            int after = Spent[who] / 8000;
            if (after > before && Loyalty.GetValueOrDefault(who) < 3) Loyalty[who] = Math.Min(3, Loyalty.GetValueOrDefault(who) + 1);
        }
        return null;
    }

    private static string contactOf(Offer o) => o.Kind switch
    {
        ShopKind.Weapon or ShopKind.Grenade or ShopKind.Armor or ShopKind.Medkit or ShopKind.Mod or ShopKind.Ammo => "arms",
        ShopKind.Cyber or ShopKind.TraumaLink or ShopKind.Grade => "doc",
        ShopKind.Vehicle or ShopKind.Repair or ShopKind.Drone or ShopKind.CarMod => "rigger",
        ShopKind.Program => "decker",
        ShopKind.Spell or ShopKind.Focus => "talismonger",
        ShopKind.Schematics => "fixer",
        _ => null,
    };
}
