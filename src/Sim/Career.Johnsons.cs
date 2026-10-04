using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>A Mr. Johnson who keeps coming back: a name, a manner, and a memory of you.</summary>
public sealed record JohnsonDef(string Id, string Name, string Look, float Cross, float Haggle, string Habit);

/// <summary>
/// The same five Johnsons broker most of the negotiated work. Each has a
/// habit (one never pays more than a little over, one pays extra for clean
/// work, one sells you out if you've hurt her family...), and each remembers:
/// three clean jobs and they stop setting you up -- one set-up and they never
/// call again. And what loot is worth, which depends on the city.
/// </summary>
public sealed partial class Career
{
    public static readonly JohnsonDef[] Johnsons =
    {
        new("grey", "Mr. Grey", "a grey suit with a corporate pin turned face-down", 0.10f, -0.15f, "never goes more than 15 % over his first offer"),
        new("vale", "Dr. Vale", "a nervous academic who keeps cleaning his glasses", 0.05f, 0.15f, "pays double the no-kill bonus"),
        new("mei", "Auntie Mei", "a Red Lotus matriarch who pours tea for everyone", 0.10f, 0.05f, "sells out anyone who has hurt the Lotus"),
        new("tusk", "Mr. Tusk", "an ork in a loud suit who pays in used notes", 0.12f, 0.10f, "tips ¥500 when the job made the news"),
        new("silk", "Ms. Silk", "a smiling woman with no last name and a great deal of money", 0.30f, 0.05f, "pays a quarter over the odds, and can't be trusted"),
    };

    public static JohnsonDef JohnsonById(string id) => Johnsons.FirstOrDefault(x => x.Id == id);

    public int JohnsonJobs(string id) => Flag($"johnson:{id}:done");
    public bool JohnsonBurned(string id) => Flag($"johnson:{id}:burned") > 0;

    /// <summary>Who is on the line for a negotiated job, and whether they mean to sell you out.</summary>
    private void PickJohnson(Job j)
    {
        var free = Johnsons.Where(x => !JohnsonBurned(x.Id)).ToList();
        if (free.Count == 0) return;
        JohnsonDef d = free[new Rng(j.Seed ^ 0x105UL).Int(free.Count)];
        j.JohnsonId = d.Id;
        float cross = d.Cross + 0.02f * Notoriety;
        if (d.Id == "mei" && Grudge.GetValueOrDefault(Faction.Lotus) >= 3) cross += 0.35f;
        // Three clean jobs and they trust you -- except the one nobody should trust.
        if (JohnsonJobs(d.Id) >= 3 && d.Id != "silk") cross *= 0.25f;
        j.DoubleCross = new Rng(j.Seed ^ 0x106UL).Chance(cross);
        if (d.Id == "silk") j.Pay = (int)(MathF.Round(j.Pay * 1.25f / 50) * 50);
    }

    /// <summary>After the job: the Johnson remembers.</summary>
    private void JohnsonAfter(Job j, RunResult r, Debrief d)
    {
        JohnsonDef jd = JohnsonById(j.JohnsonId);
        if (jd == null) return;
        if (j.DoubleCross && (r.Trap || j.Stiff && r.Success))
        {
            Set($"johnson:{jd.Id}:burned");
            Say($"{jd.Name} sold you out. That's the last job from {jd.Name}.");
            return;
        }
        if (!r.Success) return;
        Inc($"johnson:{jd.Id}:done");
        if (jd.Id == "tusk" && (r.Alarm || r.Kills >= 5)) { Cash += 500; Count("earned", 500); d.Lines.Add(("Mr. Tusk's tip", M.Money(500))); }
    }

    // ---- loot ------------------------------------------------------------------------------

    private static readonly string[] Wantable = { "paydata chip", "corporate ledger", "gold-plated commlink", "prototype optic", "art deco cyberhand", "bearer credstick", "bearer bonds", "diamond chip", "corporate scrip" };

    /// <summary>What Wen pays for this today: hot goods for less, secrets of a busy faction for more, wanted pieces double.</summary>
    public int LootPrice(LootItem l)
    {
        float v = l.Value * SellMul;
        if (l.Faction >= 0 && Clock(ClockOf((Faction)l.Faction)) >= 5) v *= 1.3f;
        if (Hot(l)) v *= 0.7f;
        if (Wanted(l)) v *= 2;
        if (HasFactionItem("jade")) v *= 1.15f;
        return (int)v;
    }

    public bool Hot(LootItem l) => l.Hot && Day - l.Day < 3;
    public bool Wanted(LootItem l) => Wantable.Any(w => l.Name.Contains(w) && Flag("wanted:" + w) >= Day);

    public string LootNote(LootItem l)
    {
        var notes = new List<string>();
        if (Wanted(l)) notes.Add("Wen has a buyer who wants exactly this (x2)");
        if (l.Faction >= 0 && Clock(ClockOf((Faction)l.Faction)) >= 5) notes.Add($"{Tables.FactionNames[l.Faction]} secrets are in demand (+30 %)");
        if (Hot(l)) notes.Add("hot: it was on the news (-30 %, and selling it draws heat)");
        return notes.Count == 0 ? "" : " " + string.Join("; ", notes) + ".";
    }

    /// <summary>A buyer for something particular, for a week.</summary>
    public string WantSomething(Rng r)
    {
        string w = Wantable[r.Int(Wantable.Length)];
        Set("wanted:" + w, Day + 7);
        return w;
    }
}
