using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>A Lone Star checkpoint across one block of an avenue.</summary>
public sealed class Roadblock
{
    /// <summary>The two junctions the block lies between.</summary>
    public (int i, int j) A, B;
    public V2 At;
    /// <summary>Across the road (unit), for drawing the cruisers and the barrier.</summary>
    public V2 Across;
    public readonly List<Pt> Tiles = new();
    /// <summary>Waved through: open for this long. Waiting at it: how long.</summary>
    public float OpenT, WaitT;
    /// <summary>It has already seen you hot (and called it in).</summary>
    public bool Alerted;
}

/// <summary>
/// The crackdown on the street: checkpoints across avenue blocks. Traffic is
/// stopped and waved through; so are you, if you stop and Lone Star has
/// nothing on you. With heat, the checkpoint calls it in. Planned routes go
/// round them.
/// </summary>
public sealed partial class CitySim
{
    public readonly List<Roadblock> Roadblocks = new();
    private readonly HashSet<int> _blockTiles = new();
    private bool _blocksFor;

    /// <summary>Walls, water -- and the barriers of a checkpoint that hasn't waved you through.</summary>
    public bool CarSolid(int x, int y) => City.Solid(x, y) || _blockTiles.Count > 0 && _blockTiles.Contains(y * City.W + x);

    /// <summary>Raise the checkpoints when a crackdown starts, take them down when it ends.</summary>
    private void PlaceRoadblocks()
    {
        bool want = C.On("crackdown");
        if (want == _blocksFor && (Roadblocks.Count > 0 || !want)) return;
        _blocksFor = want;
        Roadblocks.Clear();
        _blockTiles.Clear();
        if (!want) return;
        var r = new Rng(C.Seed ^ (ulong)C.Day * 0x9E37UL);
        var keep = new List<V2> { Player.Pos, HomeLot.Door.Centre };
        foreach (ContactDef k in Tables.Contacts) if (City.Place(k.Id) is Lot l) keep.Add(l.Door.Centre);
        for (int tries = 0; tries < 200 && Roadblocks.Count < Tables.Roadblocks; tries++)
        {
            bool alongX = r.Chance(0.5f);
            int i = r.Int(City.RoadX.Count - (alongX ? 1 : 0)), j = r.Int(City.RoadY.Count - (alongX ? 0 : 1));
            var a = (i, j);
            var b = alongX ? (i + 1, j) : (i, j + 1);
            V2 at = (City.Node(a.Item1, a.Item2) + City.Node(b.Item1, b.Item2)) / 2;
            if (keep.Any(p => V2.Distance(p, at) < Tables.RoadblockClear) || Roadblocks.Any(o => V2.Distance(o.At, at) < 30)) continue;
            var rb = new Roadblock { A = a, B = b, At = at, Across = alongX ? new V2(0, 1) : new V2(1, 0) };
            // A line across the road and both pavements, stopping at the buildings.
            int cx = (int)MathF.Floor(at.X), cy = (int)MathF.Floor(at.Y);
            for (int s = -City.Road; s <= City.Road; s++)
            {
                int x = alongX ? cx : cx + s, y = alongX ? cy + s : cy;
                if (City.Solid(x, y)) continue;
                rb.Tiles.Add(new Pt(x, y));
            }
            if (rb.Tiles.Count < City.Road) continue;
            Roadblocks.Add(rb);
            foreach (Pt t in rb.Tiles) _blockTiles.Add(t.Y * City.W + t.X);
        }
    }

    /// <summary>The junction-to-junction steps a checkpoint closes (for route planning).</summary>
    public bool Closed((int, int) a, (int, int) b) => Roadblocks.Any(rb => rb.A == a && rb.B == b || rb.A == b && rb.B == a);

    /// <summary>A route to a door that goes round the checkpoints.</summary>
    public DriveAutopilot RouteTo(Lot lot) => new(City, Player.Pos, lot, Roadblocks.Count > 0 ? Closed : null);

    private void StepRoadblocks(float dt)
    {
        PlaceRoadblocks();
        foreach (Roadblock rb in Roadblocks)
        {
            float d = V2.Distance(Player.Pos, rb.At);
            if (rb.OpenT > 0 && (rb.OpenT -= dt) <= 0) foreach (Pt t in rb.Tiles) _blockTiles.Add(t.Y * City.W + t.X);
            // Hot: they know the car. They call it in, once.
            if (Stars >= 1 && d < Tables.RoadblockSees && !rb.Alerted && !C.CarHas("plates"))
            {
                rb.Alerted = true;
                AddHeat(1, "The checkpoint ran your plate.");
                for (int k = 0; k < 2 && !C.On("strike"); k++) SpawnAt(ChaserKind.Police, rb.At + rb.Across * (k == 0 ? 1.5f : -1.5f));
                Events.Add(new CityEvent(CityEv.Siren, rb.At, 1, "Checkpoint! Two cruisers pull out after you."));
                continue;
            }
            // Cold, stopped at the barrier: a torch in your face, then the wave.
            if (rb.OpenT <= 0 && d < Tables.RoadblockStop && MathF.Abs(Player.Speed) < 1 && Stars == 0 && !Chasers.Any(c => c.Kind != ChaserKind.Racer))
            {
                rb.WaitT += dt;
                if (rb.WaitT >= Tables.RoadblockWait)
                {
                    rb.WaitT = 0;
                    rb.OpenT = 6;
                    foreach (Pt t in rb.Tiles) _blockTiles.Remove(t.Y * City.W + t.X);
                    C.Count("checkpoints");
                    Events.Add(new CityEvent(CityEv.Message, rb.At, 0, "A Lone Star officer shines a torch in your face, then waves you through."));
                }
            }
            else if (d > Tables.RoadblockStop) rb.WaitT = 0;
        }
    }

    /// <summary>Does a checkpoint stand within <paramref name="range"/> ahead of this car?</summary>
    private bool RoadblockAhead(V2 pos, V2 fwd, float range)
    {
        foreach (Roadblock rb in Roadblocks)
        {
            if (rb.OpenT > 0) continue;
            V2 rel = rb.At - pos;
            float along = V2.Dot(rel, fwd);
            if (along > 0.2f && along < range && MathF.Abs(V2.Dot(rel, M.Perp(fwd))) < City.Road) return true;
        }
        return false;
    }

    private void SpawnAt(ChaserKind kind, V2 at)
    {
        VehicleDef d0 = kind == ChaserKind.Police ? PoliceCar : GangCar;
        Chasers.Add(new Chaser { Kind = kind, Car = new Car { Def = d0, Hp = d0.Hp, Pos = at, Heading = M.Angle(Player.Pos - at) } });
    }
}
