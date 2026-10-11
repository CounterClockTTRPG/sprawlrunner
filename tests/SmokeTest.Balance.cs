using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>
/// The balance report: a simulated career for every archetype over several
/// seeds, a runner who shops with what they earn, printed at days 20, 40 and
/// 70, so a tuning change shows what it does to the economy. It checks only
/// the broad shape -- careers last, money is spent, nobody ends in debt.
/// </summary>
public partial class SmokeTest
{
    private static readonly ShopKind[] Buys = { ShopKind.Weapon, ShopKind.Armor, ShopKind.Cyber, ShopKind.Program, ShopKind.Deck, ShopKind.Drone, ShopKind.Vehicle, ShopKind.CarMod, ShopKind.Grade, ShopKind.Spell, ShopKind.Focus, ShopKind.Lifestyle, ShopKind.Property, ShopKind.Upgrade, ShopKind.Mod };

    private void Balance()
    {
        int[] marks = { 20, 40, 70 };
        GD.Print("    balance: archetype seed | day 20 | day 40 | day 70   (cash, spent so far / karma earned / notoriety / awareness; income from jobs + from everything else; grudge Vipers/Halcyon/Lotus)");
        var endCash = new Dictionary<string, List<int>>();
        foreach (var (arch, _, _) in Career.Archetypes)
        {
            endCash[arch] = new List<int>();
            foreach (ulong seed in new ulong[] { 11, 23, 37 })
            {
                City city = City.Generate(seed);
                Career c = Career.New("Bal", arch, seed);
                c.CityRef = city;
                c.RefreshBoard(city);
                var r = new Rng(seed * 7 + 1);
                var row = new List<string>();
                int startCash = c.Cash;
                string[] places = Tables.Contacts.Select(k => k.Id).Append("home").ToArray();
                for (int day = 0; day < 70 && !c.Dead && !c.Retired; day++)
                {
                    foreach (string place in places) if (c.SceneAt(place) is Mail sc) Answer(c, sc, r);
                    foreach (Mail m in c.Inbox.Where(c.Waiting).ToList()) Answer(c, m, r);
                    Shop(c, r);
                    Job j = c.Active ?? c.Board.Where(x => x.Type != JobType.Hub).OrderByDescending(x => x.Story).ThenByDescending(x => x.Threat <= c.MaxThreat ? x.Pay : 0).FirstOrDefault();
                    if (j != null)
                    {
                        // A runner with the mouth for it pushes the Johnson once; a walk-out loses the job.
                        if (j.Johnson && c.A(Attr.Charisma) >= 4) c.Haggle(j);
                        if (j.Johnson) j.Negotiated = true;
                        if (c.Board.Contains(j) && c.Accept(j) == null)
                        {
                            // Harder jobs go wrong more often; good gear and chrome help a little, and so does a kit made for the job.
                            float win = 0.93f - 0.04f * j.Threat + 0.01f * Math.Min(8, c.Cyber.Count + c.Weapons.Count) + KitFit(arch, j.Type);
                            var res = r.Chance(win) ? Won(c, alarm: r.Chance(0.4f), kills: r.Range(0, 12)) : Lost(c);
                            res.TargetFled = j.Type == JobType.Eliminate && !res.Success;
                            c.TraumaLinks = Math.Max(c.TraumaLinks, 1);
                            c.Finish(j, res);
                        }
                    }
                    if (c.Day >= 1 && c.Active == null) c.NextDay(city, rested: true);
                    if (row.Count < marks.Length && c.Day >= marks[row.Count])
                    {
                        // Everything that came in, less what the jobs paid: services, storylets, property, the fence.
                        int income = c.Cash - startCash + c.Stat("spent"), jobs = c.Stat("jobpay");
                        string g = string.Join("/", Enum.GetValues<Faction>().Select(f => c.Grudge.GetValueOrDefault(f)));
                        row.Add($"¥{c.Cash / 1000,4}k ¥{c.Stat("spent") / 1000,4}k/{c.KarmaEarned}/{c.Notoriety}/{c.Awareness} +{jobs / 1000}k+{Math.Max(0, income - jobs) / 1000}k g{g}");
                    }
                    Check(c.Cash >= 0, $"{arch} {seed} day {c.Day}: no debt");
                }
                endCash[arch].Add(c.Cash);
                GD.Print($"      {arch,-8} {seed,3} | {string.Join(" | ", row)}  {c.JobsDone} jobs, chapter {c.Chapter}/4, score {c.Score:N0} ({c.Rank})");
                Check(c.Day >= 60, $"{arch} {seed}: the career lasts ({c.Day} days)");
                Check(c.Stat("spent") > c.Stat("earned") / 4, $"{arch} {seed}: the money finds somewhere to go (spent ¥{c.Stat("spent")} of ¥{c.Stat("earned")})");
            }
        }
        GD.Print("    balance: average cash when the career ends -- " + string.Join(", ", endCash.Select(kv => $"{kv.Key} ¥{kv.Value.Sum() / kv.Value.Count / 1000}k")));
    }

    /// <summary>The simulated runner's shopping: now and then, the best thing they can comfortably afford.</summary>
    private static void Shop(Career c, Rng r)
    {
        if (!r.Chance(0.5f)) return;
        string where = r.Pick(Tables.Contacts.Where(k => c.Shut(k.Id) == null).Select(k => k.Id).Append("home").ToList());
        Offer best = c.Offers(where).Where(o => o.Block == null && o.Owned == null && Buys.Contains(o.Kind) && o.Price > 0 && o.Price < c.Cash * 0.5f)
            .OrderByDescending(o => o.Price).FirstOrDefault();
        if (best != null) c.Buy(best);
        if (c.Medkits < 2 && c.Cash > 2000 && c.Offers("arms").FirstOrDefault(o => o.Kind == ShopKind.Medkit && o.Block == null) is Offer kit) c.Buy(kit);
    }

    /// <summary>A rough guess at what each kit is for: a little more likely to win those jobs, a little less the rest.</summary>
    private static float KitFit(string arch, JobType t) => (arch, t) switch
    {
        ("samurai", JobType.Eliminate or JobType.Clear or JobType.Defend or JobType.Bounty) => 0.05f,
        ("decker", JobType.Datasteal or JobType.Hack) => 0.07f,
        ("face", JobType.Retrieve or JobType.Investigate or JobType.Escort or JobType.Kidnap) => 0.05f,
        ("engineer", JobType.Destroy or JobType.Plant or JobType.Clear) => 0.06f,
        ("mage", JobType.Rescue or JobType.Escort or JobType.Clear) => 0.05f,
        _ => -0.02f,
    };
}
