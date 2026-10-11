using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>What a room is for: its furniture, and the one thing in it worth knowing about.</summary>
public enum RoomKind { Plain, Server, Break, Barracks, Armoury, Manager, Panic, Ritual, Security, LabBay, Dock, Booth, Nest, Stock, Ward, Theatre, Morgue, Cage, Count, Suite, Bridge, Crypt }

/// <summary>How a defender starts: as usual, off duty (cards at the break-room table), asleep in a bunk, or bound to a circle.</summary>
public enum Mood { None, OffDuty, Asleep, Bound }

/// <summary>
/// Small hand-drawn rooms, stamped (turned and mirrored) into a room of their
/// kind -- the same text as the checks' <c>Site(...)</c>. A space leaves the
/// room as it is; '.' wants floor. D desk, R rack, L locker, S safe, C console,
/// B bunk, c crate, o barrel, P coolant pipe, v vat, w forklift (a wreck),
/// r rubble (a nest's bones and refuse), ~ the ritual circle's chalk, g a guard (off duty or asleep, by the room),
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
        (RoomKind.Booth, new[] { "DDDD", ".*..", "D..D" }),
        (RoomKind.Booth, new[] { "DD.", "D*.", "DD." }),
        (RoomKind.Nest, new[] { "r.g.r", ".....", "g.*.g", "r...r" }),
        (RoomKind.Nest, new[] { "rg.", ".*g", "r.r" }),
        (RoomKind.Nest, new[] { ".g..r", "r..g.", "..*..", "o...r" }),
        (RoomKind.Stock, new[] { "cc.cc", "cc.cc", ".....", "LL*.c" }),
        (RoomKind.Stock, new[] { "c.c.c", "c.c.c", "..*.." }),
        (RoomKind.Ward, new[] { "B.B.B", ".....", "B.*.B" }),
        (RoomKind.Ward, new[] { "BB.BB", ".....", "BB*BB" }),
        (RoomKind.Ward, new[] { "B.B", "...", "B*B" }),
        (RoomKind.Theatre, new[] { ".DDD.", "C.*.P", ".DDD." }),
        (RoomKind.Theatre, new[] { "P.D.P", ".DDD.", "..*.." }),
        (RoomKind.Morgue, new[] { "LLLL", "....", "v.*v" }),
        (RoomKind.Morgue, new[] { "L.L.L", ".....", "vv*vv" }),
        (RoomKind.Cage, new[] { "S.S", "...", "D*D" }),
        (RoomKind.Cage, new[] { "SDS", "...", ".*." }),
        (RoomKind.Count, new[] { "DDD", ".*.", "DDD" }),
        (RoomKind.Count, new[] { "D.D.D", "..*..", "S...S" }),
        (RoomKind.Cage, new[] { "S.", ".*" }),
        (RoomKind.Count, new[] { "D*", "DD" }),
        (RoomKind.Suite, new[] { "BB.D", "....", ".*.." }),
        (RoomKind.Suite, new[] { "D..BB", ".....", "D.*.." }),
        (RoomKind.Bridge, new[] { "CCC", ".*.", "D.D" }),
        (RoomKind.Bridge, new[] { "C.C.C", ".....", "D.*.D" }),
        (RoomKind.Crypt, new[] { "c.c.c", ".....", "c.*.c" }),
        (RoomKind.Crypt, new[] { "c...c", "..*..", "c...c" }),
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
        'c' => Tile.Crate, 'o' => Tile.Barrel, 'r' => Tile.Rubble, 'P' => Tile.Pipe, 'v' => Tile.Vat, 'w' => Tile.Wreck, _ => null,
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
        RoomKind.Dock => "The loading dock. The roller doors are a way out too.",
        RoomKind.Booth => "The VIP booth: velvet, a view of the floor, and whoever matters tonight.",
        RoomKind.Nest => "A nest. Bones, rags, and something asleep in them. Don't wake it.",
        RoomKind.Stock => "The stockroom: pallets and cartons, and the back door the deliveries use.",
        RoomKind.Ward => "A ward. The patients are sedated -- they won't scream, and they won't walk out on their own.",
        RoomKind.Theatre => "An operating theatre. The anaesthetic lines run along the wall -- rupture one and whoever breathes it sleeps.",
        RoomKind.Morgue => "Cold storage. The organ vats are labelled with prices, not names.",
        RoomKind.Cage => "The cashier's cage: bars, a safe, and the night's chips going in the drawer.",
        RoomKind.Count => "The count room. Tonight's take, and the machine that tallies it.",
        RoomKind.Suite => "The suite. Room service trays, a view, and whoever is paying for it.",
        RoomKind.Bridge => "The bridge. The ship's manifest is on that console -- and the sailing orders.",
        RoomKind.Crypt => "The crypt. Old stone coffins, and some of the lids have been moved.",
        _ => null,
    };

    public static string Name(RoomKind k) => k switch
    {
        RoomKind.Server => "server room", RoomKind.Break => "break room", RoomKind.Barracks => "barracks", RoomKind.Armoury => "armoury",
        RoomKind.Manager => "manager's office", RoomKind.Panic => "panic room", RoomKind.Ritual => "ritual room", RoomKind.Security => "security office",
        RoomKind.LabBay => "lab bay", RoomKind.Dock => "loading dock", RoomKind.Booth => "VIP booth",
        RoomKind.Nest => "nest", RoomKind.Stock => "stockroom", RoomKind.Ward => "ward", RoomKind.Theatre => "operating theatre", RoomKind.Morgue => "cold room",
        RoomKind.Cage => "cashier's cage", RoomKind.Count => "count room", RoomKind.Suite => "suite", RoomKind.Bridge => "bridge", RoomKind.Crypt => "crypt", _ => "room",
    };
}
