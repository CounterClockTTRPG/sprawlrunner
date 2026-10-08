using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>What a room is for: its furniture, and the one thing in it worth knowing about.</summary>
public enum RoomKind { Plain, Server, Break, Barracks, Armoury, Manager, Panic, Ritual, Security, LabBay, Dock }

/// <summary>How a defender starts: as usual, off duty (cards at the break-room table), asleep in a bunk, or bound to a circle.</summary>
public enum Mood { None, OffDuty, Asleep, Bound }

/// <summary>
/// Small hand-drawn rooms, stamped (turned and mirrored) into a room of their
/// kind -- the same text as the checks' <c>Site(...)</c>. A space leaves the
/// room as it is; '.' wants floor. D desk, R rack, L locker, S safe, C console,
/// B bunk, c crate, o barrel, P coolant pipe, v vat, w forklift (a wreck),
/// ~ the ritual circle's chalk, g a guard (off duty or asleep, by the room),
/// x a bound spirit, * where the objective goes when it's here.
/// </summary>
public static class SetPieces
{
    public static readonly (RoomKind kind, string[] rows)[] All =
    {
        (RoomKind.Server, new[] { "RR.RR", "RR.RR", ".....", "P.*.P" }),
        (RoomKind.Server, new[] { "R.R.R", "R.R.R", "R.R.R", ".P.P." }),
        (RoomKind.Server, new[] { "RRR.RRR", "...*...", "RRR.RRR", "P.....P" }),
        (RoomKind.Server, new[] { "RR", "RR", "..", "P*" }),
        (RoomKind.Break, new[] { ".g.g.", ".DDD.", "..g.." }),
        (RoomKind.Break, new[] { "g..", "DD.", "DDg" }),
        (RoomKind.Break, new[] { ".g..c", "gDD.c", ".DD*.", "..g.." }),
        (RoomKind.Barracks, new[] { "B.B.B", "g...g", ".....", "B.B.B" }),
        (RoomKind.Barracks, new[] { "BB.BB", ".g.g.", "BB.BB" }),
        (RoomKind.Barracks, new[] { "B.g", "B..", "B.g", "c.*" }),
        (RoomKind.Armoury, new[] { "LLL", "...", "c*c" }),
        (RoomKind.Armoury, new[] { "L.L.L", ".....", "cc*cc" }),
        (RoomKind.Manager, new[] { "S...", ".DDD", ".*.." }),
        (RoomKind.Manager, new[] { "DD.S", "...*", "c..." }),
        (RoomKind.Security, new[] { "C.C", ".g." }),
        (RoomKind.Panic, new[] { "c.c", ".*.", "..." }),
        (RoomKind.Ritual, new[] { "~~~~~", "~~x~~", "~~~~~" }),
        (RoomKind.Ritual, new[] { ".~~~.", "~~x~~", "~~*~~", ".~~~." }),
        (RoomKind.LabBay, new[] { "v.v.v", ".....", "DD*DD" }),
        (RoomKind.LabBay, new[] { "vv..vv", "...*..", ".DDDD." }),
        (RoomKind.Dock, new[] { "cc.w.cc", "cc...cc", "...*..." }),
    };

    /// <summary>A piece turned by quarter turns (0-3) and maybe mirrored.</summary>
    public static string[] Orient(string[] rows, int turns, bool mirror)
    {
        string[] r = rows;
        if (mirror) r = r.Select(s => new string(s.Reverse().ToArray())).ToArray();
        for (int t = 0; t < turns; t++)
        {
            int h = r.Length, w = r[0].Length;
            var n = new string[w];
            for (int x = 0; x < w; x++)
            {
                var line = new char[h];
                for (int y = 0; y < h; y++) line[y] = r[h - 1 - y][x];
                n[x] = new string(line);
            }
            r = n;
        }
        return r;
    }

    /// <summary>Every piece for this kind that fits inside w x h (with a tile to spare all round), in some orientation.</summary>
    public static List<string[]> Fitting(RoomKind kind, int w, int h, Rng r)
    {
        var fits = new List<string[]>();
        foreach (var (k, rows) in All)
        {
            if (k != kind) continue;
            for (int o = 0; o < 8; o++)
            {
                string[] p = Orient(rows, o % 4, o >= 4);
                if (p[0].Length <= w - 2 && p.Length <= h - 2) fits.Add(p);
            }
        }
        // Shuffle (the generator's own stream) so the same room isn't always the first fit.
        for (int i = fits.Count - 1; i > 0; i--) { int j = r.Int(i + 1); (fits[i], fits[j]) = (fits[j], fits[i]); }
        return fits;
    }

    public static Tile? TileFor(char c) => c switch
    {
        'D' => Tile.Desk, 'R' => Tile.Rack, 'L' => Tile.Locker, 'S' => Tile.Safe, 'C' => Tile.Console, 'B' => Tile.Bunk,
        'c' => Tile.Crate, 'o' => Tile.Barrel, 'P' => Tile.Pipe, 'v' => Tile.Vat, 'w' => Tile.Wreck, _ => null,
    };

    /// <summary>What the runner hears on walking in (null: nothing worth saying).</summary>
    public static string Line(RoomKind k) => k switch
    {
        RoomKind.Server => "A server room. Coolant pipes run along the racks -- rupture one and the cold takes anyone near it.",
        RoomKind.Break => "A break room. Off duty, they're watching their cards, not the door.",
        RoomKind.Barracks => "Bunks. Someone's asleep in here -- keep it quiet.",
        RoomKind.Armoury => "The armoury: lockers of kit.",
        RoomKind.Manager => "The manager's office.",
        RoomKind.Panic => "A panic room: armoured walls, a door that locks from inside.",
        RoomKind.Ritual => "A ritual circle. Something is bound to it -- it can't leave the chalk.",
        RoomKind.Security => "Security. The console runs the cameras.",
        RoomKind.LabBay => "A lab bay. The vats are under pressure.",
        RoomKind.Dock => "The loading dock.",
        _ => null,
    };

    public static string Name(RoomKind k) => k switch
    {
        RoomKind.Server => "server room", RoomKind.Break => "break room", RoomKind.Barracks => "barracks", RoomKind.Armoury => "armoury",
        RoomKind.Manager => "manager's office", RoomKind.Panic => "panic room", RoomKind.Ritual => "ritual room", RoomKind.Security => "security office",
        RoomKind.LabBay => "lab bay", RoomKind.Dock => "loading dock", _ => "room",
    };
}
