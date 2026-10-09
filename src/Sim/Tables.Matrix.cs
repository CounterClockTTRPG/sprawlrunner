using System;
using System.Linq;

namespace Sprawl;

/// <summary>A cyberdeck: its rating (condition boxes, the outside-the-Matrix hacking bonus),
/// the four values its owner deals out to Attack, Sleaze, Data processing and Firewall, and its memory.</summary>
public sealed class DeckDef
{
    public string Id, Name, Desc;
    public int Rating, Memory, Cost, Loyalty;
    /// <summary>The values in their factory order: Attack, Sleaze, Data processing, Firewall.</summary>
    public int[] Array;
    /// <summary>Not on Null's counter (the commlink everyone has, the implant from the clinic).</summary>
    public bool Builtin, Elite;
}

/// <summary>Matrix attributes, in the order a deck's array is dealt out.</summary>
public enum MxAttr { Attack, Sleaze, Data, Firewall }

public enum IcKind { Patrol, Killer, Track, TarBaby, Acid, Crash, Blaster, Black, Spider }

/// <summary>One kind of intrusion countermeasure: when it first turns up (host rating), how fast it moves,
/// how tough it is, what a hit does.</summary>
public sealed class IcDef
{
    public IcKind Kind;
    public string Name, Desc;
    public char Glyph;
    public int From;
    /// <summary>Seconds per node; hit points as a multiple of the host's IC; boxes a hit; biofeedback a hit (x rating); link-lock seconds.</summary>
    public float Step, HpMul = 1, Bio, Lock;
    public int Boxes;
}

public static partial class Tables
{
    public static readonly DeckDef[] Decks =
    {
        new() { Id = "commlink", Name = "Stock commlink", Rating = 1, Array = new[] { 2, 1, 2, 1 }, Memory = 4, Builtin = true, Desc = "Everyone's. It can jack in; it can't fight much." },
        new() { Id = "implant", Name = "Cyberdeck implant", Rating = 2, Array = new[] { 3, 3, 2, 2 }, Memory = 6, Builtin = true, Desc = "The deck in your head. With any other deck it runs hot-sim: 15 % quicker in the Matrix." },
        new() { Id = "kestrel", Name = "Kestrel MCD-2", Rating = 2, Array = new[] { 4, 3, 3, 2 }, Memory = 8, Cost = 12000, Desc = "A street deck: solid, cheap, held together with tape." },
        new() { Id = "hauler", Name = "Hauler Datavault", Rating = 3, Array = new[] { 3, 3, 4, 4 }, Memory = 16, Cost = 30000, Loyalty = 1, Desc = "A courier's deck: memory to burn, built to carry paydata out." },
        new() { Id = "shiv", Name = "Ronin Shiv", Rating = 3, Array = new[] { 6, 4, 3, 2 }, Memory = 8, Cost = 35000, Loyalty = 1, Desc = "An assault deck: hits like a truck, folds like paper." },
        new() { Id = "veil", Name = "Mirrorshade Veil", Rating = 3, Array = new[] { 3, 6, 4, 3 }, Memory = 10, Cost = 42000, Loyalty = 2, Desc = "A sleaze deck: patrols look straight through it." },
        new() { Id = "deck2", Name = "Obsidian-class deck", Rating = 4, Array = new[] { 6, 5, 5, 4 }, Memory = 14, Cost = 110000, Loyalty = 4, Desc = "A serious deck. Everything a professional needs, and hacking outside the Matrix 30 % faster." },
        new() { Id = "deck3", Name = "Excalibur-class deck", Rating = 6, Array = new[] { 8, 7, 7, 6 }, Memory = 20, Cost = 400000, Loyalty = 5, Elite = true, Desc = "The best deck money can buy. Hacking outside the Matrix 70 % faster. (Prestige.)" },
    };

    public static DeckDef Deck(string id) => Decks.FirstOrDefault(d => d.Id == id) ?? Decks[0];

    public static readonly string[] MxAttrNames = { "Attack", "Sleaze", "Data processing", "Firewall" };
    public static readonly string[] MxAttrDesc =
    {
        "spike damage; brute-forcing gates",
        "how far patrols see you; quiet cracking; how fast the trace fills",
        "moving node to node; pulling files",
        "boxes off every IC hit; shrugging off Crash IC",
    };

    public static readonly IcDef[] Ic =
    {
        new() { Kind = IcKind.Patrol, Name = "Patrol IC", Glyph = 'P', From = 1, Step = 1.1f, HpMul = 0.8f, Desc = "walks a beat; sees you on its node or the next, and reports after 2 s" },
        new() { Kind = IcKind.Killer, Name = "Killer IC", Glyph = 'K', From = 1, Step = 0.85f, Boxes = 2, Desc = "guards a datastore, then hunts: 2 boxes a hit" },
        new() { Kind = IcKind.Track, Name = "Track IC", Glyph = 'T', From = 1, Step = 0.6f, HpMul = 0.6f, Desc = "no damage: near you, the trace fills fast" },
        new() { Kind = IcKind.TarBaby, Name = "Tar baby", Glyph = 'B', From = 2, Step = 1.3f, HpMul = 1.2f, Lock = 5, Desc = "link-locks you and slows you" },
        new() { Kind = IcKind.Acid, Name = "Acid IC", Glyph = 'A', From = 3, Step = 0.9f, Boxes = 1, Desc = "burns your Firewall, then boxes" },
        new() { Kind = IcKind.Crash, Name = "Crash IC", Glyph = 'C', From = 3, Step = 0.9f, Desc = "crashes one of your programs" },
        new() { Kind = IcKind.Blaster, Name = "Blaster IC", Glyph = 'X', From = 3, Step = 0.85f, Boxes = 2, Bio = 1, Lock = 3, Desc = "2 boxes, a jolt of biofeedback, link-lock" },
        new() { Kind = IcKind.Black, Name = "Black IC", Glyph = '#', From = 5, Step = 0.8f, HpMul = 2, Boxes = 3, Bio = 2, Lock = 4, Desc = "3 boxes and biofeedback through any armour: it kills" },
        new() { Kind = IcKind.Spider, Name = "Security spider", Glyph = 'S', From = 99, Step = 0.55f, Boxes = 3, Bio = 1, Desc = "a decker: hunts you, relaunches crashed IC, speeds the trace" },
    };

    public static IcDef IcOf(IcKind k) => Ic[(int)k];
    public static ProgramDef Program(string id) => Programs.First(p => p.Id == id);

    /// <summary>The deck: condition boxes are base + per x rating.</summary>
    public const int MxBoxesBase = 6, MxBoxesPer = 2;
    /// <summary>The trace (0-1): per second per host rating, x alert, a Track IC near you (per second), a spike,
    /// a brute-forced gate, a control node taken, downloading (per second); what is left of it when you jack back in.</summary>
    public const float MxTraceBase = 0.004f, MxTraceAlert = 2.5f, MxTraceTrack = 0.10f, MxTraceSpike = 0.015f, MxTraceBrute = 0.06f,
        MxTraceControl = 0.08f, MxTraceDownload = 0.004f, MxTraceCarry = 0.5f, MxTraceSpider = 1.3f, MxTraceCore = 0.5f;
    /// <summary>A patrol that sees you reports after this long; an alert core launches IC every base - rating s (at least min), up to 2 + rating.</summary>
    public const float MxReport = 2, MxLaunchBase = 12, MxLaunchMin = 4;
    /// <summary>Spikes: the cooldown, and IC hit points (base + per x rating), the core's and a spider's.</summary>
    public const float MxSpikeCd = 0.75f;
    public const int MxIcHpBase = 3, MxIcHpPer = 2, MxCoreHpBase = 15, MxCoreHpPer = 5, MxSpiderHpBase = 14, MxSpiderHpPer = 3;
    /// <summary>IC hits every so often, after a wind-up when it first reaches you; a crashed spider's body sits stunned this long.</summary>
    public const float MxIcHitCd = 1.5f, MxIcWindup = 0.5f, MxSpiderStun = 20;
    /// <summary>Moving a node (base seconds), pulling a file (s per Mp), holding a control node, planting a backdoor, cracking a gate quietly.</summary>
    public const float MxMove = 0.3f, MxDownload = 1.1f, MxControl = 2, MxPlant = 6, MxGateCrack = 3;
    /// <summary>A paydata file is worth size x (base + per x threat), give or take a third.</summary>
    public const int MxFileBase = 250, MxFilePer = 150;

    public static readonly string[] PaydataNames =
    {
        "payroll records", "R&D notes", "security rosters", "shipping manifests", "a blackmail folder", "prototype schematics",
        "a Johnson's ledger", "customer lists", "board minutes", "a slush fund", "clinical trial data", "a SIN registry extract",
    };
}
