using System;
using System.Collections.Generic;

namespace Sprawl;

/// <summary>
/// A street you walk around in: a market square between shopfronts, rows of
/// stalls under neon, a crowd, and vendors with something to sell. Same tiles
/// and engine as a run, with no one hostile and weapons holstered.
/// </summary>
public static class HubGen
{
    public static readonly (string id, string name, string line)[] Vendors =
    {
        ("noodles", "Noodle stand", "Hot soy noodles, ¥40: +15 % max HP on your next run."),
        ("broker", "Info broker", "Site schematics for your current job: ¥300."),
        ("chips", "Chip dealer", "A grenade off the back of a truck: ¥120."),
        ("informant", "Lone Star informant", "I can make the cops forget you. For a price."),
        ("kid", "Street kid", "Wanna hear something? Free, 'cause I like your coat."),
    };

    public static LevelPlan Generate(ulong seed, string hub)
    {
        var r = new Rng(seed);
        int w = 52, h = 34;
        var L = new Level(w, h);
        var P = new LevelPlan { L = L, Style = SiteStyle.Club, RoomOf = new int[w * h], MainW = w };
        Array.Fill(P.RoomOf, -1);
        for (int i = 0; i < w * h; i++) { L.T[i] = Tile.Floor; L.F[i] = Floor.Street; }
        for (int x = 0; x < w; x++) for (int y = 0; y < h; y++)
        {
            bool border = x == 0 || y == 0 || x == w - 1 || y == h - 1;
            // Shopfronts along the top and bottom.
            bool front = y <= 4 || y >= h - 5;
            if (border) L.Set(x, y, Tile.Reinforced);
            else if (front)
            {
                bool face = y == 4 || y == h - 5;
                L.Set(x, y, face ? (r.Chance(0.35f) ? Tile.Glass : x % 9 == 4 ? Tile.LockedDoor : Tile.Concrete) : Tile.Concrete);
            }
        }
        P.Building = new Rect(0, 0, w, 5);
        // Rows of stalls.
        var vendorSpots = new List<V2>();
        foreach (int row in new[] { 11, 21 })
            for (int x = 8; x < w - 6; x += 7)
            {
                for (int k = 0; k < 3; k++) L.Set(x + k, row, Tile.Desk);
                vendorSpots.Add(new V2(x + 1.5f, row - 0.6f));
                L.Lights.Add((new V2(x + 1.5f, row + 0.5f), r.Pick(new uint[] { 0xff3aa0ff, 0x40e0ffff, 0xffd040ff, 0x9a6bffff, 0x40ffb0ff }), 3.5f));
            }
        for (int k = 0; k < 8; k++) L.Lights.Add((new V2(r.Range(4f, w - 4f), r.Chance(0.5f) ? 5.5f : h - 5.5f), r.Pick(new uint[] { 0xff2a6dff, 0x05d9e8ff, 0xf9c80eff }), 4.5f));
        for (int k = 0; k < 10; k++)
        {
            Pt p = new(r.Range(6, w - 4), r.Range(6, h - 7));
            if (L[p] == Tile.Floor && p.Y != 10 && p.Y != 20) L.Set(p, Tile.Crate);
        }
        P.Entry = new Pt(2, h / 2);
        P.Extraction = new Rect(1, h / 2 - 2, 3, 4);
        // Vendors at their stalls, one of each, the rest of the stalls staffed by the crowd.
        var spots = new List<V2>(vendorSpots);
        foreach (var v in Vendors)
        {
            int i = r.Int(spots.Count);
            P.Npcs.Add((spots[i], v.id));
            spots.RemoveAt(i);
        }
        for (int k = 0; k < 26; k++)
        {
            V2 p = new(r.Range(5f, w - 3f), r.Range(6f, h - 7f));
            if (!L.Solid((int)p.X, (int)p.Y)) P.Npcs.Add((p, null));
        }
        P.Reinforce.Add(new V2(w - 3, h / 2));
        L.Ambient = Tables.NightAmbient;
        return P;
    }

    public static readonly string[] Rumours =
    {
        "Halcyon-Matsuda pays its guards by the hour. Nobody fights hard for overtime.",
        "Them Lotus blades? Hit 'em from behind. They never look back.",
        "Lone Star stops chasing if you go to ground and sleep it off.",
        "A Viper rig followed my cousin home after a big score. Watch your mirrors.",
        "Mages don't like chrome. Doc Ichikawa says every implant dims the light.",
        "Every big site's got a boss. They wind up before they charge. Get out of the way.",
        "Shield troopers can't turn fast. Get round the side.",
        "Riggers bring drones. EMP, chummer. EMP.",
        "Security consoles loop every camera in a building. Worth the detour.",
        "Gel rounds put 'em to sleep. Clients pay extra when nobody dies.",
    };
}
