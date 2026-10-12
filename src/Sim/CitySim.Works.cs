using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>A wreck burning in one half of an avenue block (gang war).</summary>
public sealed class Wreck
{
    public V2 At;
    public float Heading;
    public readonly List<Pt> Tiles = new();
}

/// <summary>A street race picked up at the lights: first to the finish takes the pot.</summary>
public sealed class StreetRace
{
    public V2 Finish;
    public string Where;
    public int Stake;
    public Chaser Rival;
}

/// <summary>
/// The city's happenings, on the street itself: the lantern festival shuts the
/// avenues round the night market (barriers, lanterns, people in the road); a
/// gang war leaves wrecks burning across lanes in the Barrens and the Docks; a
/// stadium night jams Industrial with traffic and fans; election week sends a
/// motorcade round town with an escort. Plus what a street scene sets in motion
/// once it closes: bikes after you, a cruiser, a race.
/// </summary>
public sealed partial class CitySim
{
    /// <summary>Avenue blocks the festival has closed to cars (junction to junction).</summary>
    public readonly List<((int, int) a, (int, int) b)> Festival = new();
    /// <summary>The road tiles inside the festival's barriers (for drawing the lanterns and stalls).</summary>
    public readonly List<Pt> FestivalTiles = new();
    /// <summary>The festival's barriers across the avenues (for drawing).</summary>
    public readonly List<Pt> FestivalBarriers = new();
    public readonly List<Wreck> Wrecks = new();
    /// <summary>Barriers and wrecks: solid to every car, police included.</summary>
    private readonly HashSet<int> _shutTiles = new();
    private (int day, bool lanterns, bool war, bool stadium, bool election) _worksFor = (-1, false, false, false, false);
    public StreetRace Race;
    private bool _motorcadeHit;

    /// <summary>Cruisers stay out of the lanes through the blocks, and nobody drives through a barrier or a fire.</summary>
    private bool PoliceSolid(int x, int y) => City.Solid(x, y) || City.Narrow(x, y) || _shutTiles.Count > 0 && _shutTiles.Contains(y * City.W + x);

    /// <summary>Any closure a planned route should go round (checkpoints, the festival).</summary>
    public bool AnyClosed => Roadblocks.Count > 0 || Festival.Count > 0;

    /// <summary>Closed to traffic (not to Lone Star's own checkpoints, which wave traffic through): the festival.</summary>
    private bool Shut(int i, int j, int i2, int j2)
    {
        foreach (var (a, b) in Festival)
            if (a == (i, j) && b == (i2, j2) || a == (i2, j2) && b == (i, j)) return true;
        return false;
    }

    /// <summary>Raise and take down what today's happenings put on the street.</summary>
    private void StepWorks(float dt)
    {
        var want = (C.Day, C.On("lanterns"), C.On("gangwar"), C.On("stadium"), C.On("election"));
        if (!want.Equals(_worksFor))
        {
            _worksFor = want;
            PlaceWorks();
        }
        StepRace();
    }

    private void PlaceWorks()
    {
        Festival.Clear();
        FestivalTiles.Clear();
        FestivalBarriers.Clear();
        Wrecks.Clear();
        _shutTiles.Clear();
        var r = new Rng(C.Seed ^ (ulong)C.Day * 0x5EE7UL ^ 0x0A11EUL);
        if (_worksFor.lanterns) CloseForFestival();
        if (_worksFor.war) PlaceWrecks(r);
        // Anyone inside the barriers when they went up goes elsewhere.
        foreach (TrafficCar t in Traffic)
            if (InShut(t.Pos) || OnFestival(t)) t.Gone = true;
        if (_worksFor.stadium) { for (int k = 0; k < Tables.StadiumCars; k++) SpawnEvent(); for (int k = 0; k < Tables.StadiumFans; k++) SpawnFan(); }
        if (_worksFor.election && !Traffic.Any(t => t.Motorcade)) SpawnMotorcade();
        // A day without them: the extra cars and fans and the motorcade drift off once they're out of sight.
        if (!_worksFor.stadium) foreach (TrafficCar t in Traffic) if (t.Event) t.Leaving = true;
        if (!_worksFor.election) foreach (TrafficCar t in Traffic) if (t.Motorcade) t.Leaving = true;
        if (!_worksFor.stadium) foreach (Ped p in Peds) if (p.Fan) p.Leaving = true;
        if (!_worksFor.lanterns) foreach (Ped p in Peds) if (p.Festival) p.Leaving = true;
    }

    private bool InShut(V2 p) => _shutTiles.Contains((int)MathF.Floor(p.Y) * City.W + (int)MathF.Floor(p.X));

    private bool OnFestival(TrafficCar t) => Shut(t.Ai, t.Aj, t.Bi, t.Bj);

    /// <summary>
    /// The avenue blocks round the night market's own block, all but the one its
    /// door is on (so you can still drive to it): barriers across both ends,
    /// lanterns and stalls and people inside.
    /// </summary>
    private void CloseForFestival()
    {
        Lot market = City.Place("market");
        if (market == null) return;
        V2 kerb = City.Kerb(market).pos;
        var ends = DriveAutopilot.Ends(City, kerb, market.Facing.Y != 0);
        if (ends.Count < 2) return;
        var (a, b) = (ends[0].node, ends[1].node);
        // The block is on the far side of the street from the door.
        int di = -market.Facing.X, dj = -market.Facing.Y;
        var a2 = (a.Item1 + di, a.Item2 + dj);
        var b2 = (b.Item1 + di, b.Item2 + dj);
        foreach (var (p, q) in new[] { (a, a2), (a2, b2), (b2, b) })
        {
            if (p.Item1 < 0 || p.Item2 < 0 || q.Item1 < 0 || q.Item2 < 0 || p.Item1 >= City.RoadX.Count || q.Item1 >= City.RoadX.Count || p.Item2 >= City.RoadY.Count || q.Item2 >= City.RoadY.Count) continue;
            Festival.Add((p, q));
            CloseBlock(p, q);
        }
    }

    /// <summary>Barriers across an avenue block at both ends; the road between is the festival's.</summary>
    private void CloseBlock((int, int) p, (int, int) q)
    {
        bool alongX = p.Item2 == q.Item2;
        int lo, hi;
        if (alongX)
        {
            int i = Math.Min(p.Item1, q.Item1), y0 = City.RoadY[p.Item2];
            lo = City.RoadX[i] + City.Road;
            hi = City.RoadX[i + 1] - 1;
            for (int x = lo; x <= hi; x++)
                for (int k = 0; k < City.Road; k++)
                {
                    var t = new Pt(x, y0 + k);
                    if (x == lo || x == hi) { _shutTiles.Add(t.Y * City.W + t.X); FestivalBarriers.Add(t); }
                    else FestivalTiles.Add(t);
                }
        }
        else
        {
            int j = Math.Min(p.Item2, q.Item2), x0 = City.RoadX[p.Item1];
            lo = City.RoadY[j] + City.Road;
            hi = City.RoadY[j + 1] - 1;
            for (int y = lo; y <= hi; y++)
                for (int k = 0; k < City.Road; k++)
                {
                    var t = new Pt(x0 + k, y);
                    if (y == lo || y == hi) { _shutTiles.Add(t.Y * City.W + t.X); FestivalBarriers.Add(t); }
                    else FestivalTiles.Add(t);
                }
        }
        // People in the road between the barriers (once: the works can be put up again the same day).
        for (int k = 0; k < 10 && FestivalTiles.Count > 0 && Peds.Count(p => p.Festival && !p.Leaving) < 10 * Festival.Count; k++)
        {
            Pt at = FestivalTiles[R.Int(FestivalTiles.Count)];
            Peds.Add(new Ped { Pos = new V2(at.X + R.Range(0.2f, 0.8f), at.Y + R.Range(0.2f, 0.8f)), Speed = R.Range(0.5f, 1f), Colour = R.Pick(PedColours), Phase = R.Float() * 10, Dir = R.Pick(Pt.Dirs4), Festival = true });
        }
    }

    /// <summary>Wrecks burning in one half of an avenue block, in the Barrens and the Docks: you get round on the other side.</summary>
    private void PlaceWrecks(Rng r)
    {
        var keep = new List<V2> { Player.Pos };
        foreach (Lot l in Destinations()) keep.Add(l.Door.Centre);
        for (int tries = 0; tries < 300 && Wrecks.Count < Tables.WarWrecks; tries++)
        {
            bool alongX = r.Chance(0.5f);
            int i = r.Int(City.RoadX.Count - (alongX ? 1 : 0)), j = r.Int(City.RoadY.Count - (alongX ? 0 : 1));
            V2 a = City.Node(i, j), b = alongX ? City.Node(i + 1, j) : City.Node(i, j + 1);
            V2 at = (a + b) / 2;
            string dist = City.DistrictAt[(int)at.Y * City.W + (int)at.X];
            if (dist is not ("barrens" or "docks")) continue;
            if (keep.Any(p => V2.Distance(p, at) < 12) || Wrecks.Any(w => V2.Distance(w.At, at) < 24)) continue;
            // Two tiles of one direction's lanes, mid-block.
            bool near = r.Chance(0.5f);
            int cx = (int)MathF.Floor(at.X), cy = (int)MathF.Floor(at.Y);
            var w = new Wreck { Heading = alongX ? 0 : MathF.PI / 2 + r.Range(-0.4f, 0.4f) };
            for (int u = -1; u <= 0; u++)
            for (int v = 0; v < 2; v++)
            {
                int lane = near ? -2 + v : v;
                int x = alongX ? cx + u : cx + lane, y = alongX ? cy + lane : cy + u;
                if (City[x, y] != CTile.Road) continue;
                w.Tiles.Add(new Pt(x, y));
            }
            if (w.Tiles.Count < 4) continue;
            w.At = new V2((float)w.Tiles.Average(t => t.X) + 0.5f, (float)w.Tiles.Average(t => t.Y) + 0.5f);
            Wrecks.Add(w);
            foreach (Pt t in w.Tiles) _shutTiles.Add(t.Y * City.W + t.X);
        }
    }

    /// <summary>A burning wreck within <paramref name="range"/> ahead in this car's lane.</summary>
    private bool WreckAhead(V2 pos, V2 fwd, float range)
    {
        for (float d = 1; d <= range; d += 0.7f)
            if (InShut(pos + fwd * d)) return true;
        return false;
    }

    // ---- stadium night -----------------------------------------------------------------

    private Lot Stadium => City.Lots.FirstOrDefault(l => l.Landmark == "stadium");

    /// <summary>A car heading for the game: on an avenue block near the stadium.</summary>
    private void SpawnEvent()
    {
        Lot st = Stadium;
        if (st == null) return;
        SpawnTraffic(99);
        TrafficCar t = Traffic[^1];
        int ni = City.RoadX.Count, nj = City.RoadY.Count;
        var near = new List<(int, int)>();
        for (int i = 0; i + 1 < ni; i++)
        for (int j = 0; j < nj; j++)
            if (V2.Distance(City.Node(i, j), st.R.Centre.Centre) < 34) near.Add((i, j));
        if (near.Count == 0) return;
        (t.Ai, t.Aj) = R.Pick(near);
        (t.Bi, t.Bj) = PickNext(t.Ai, t.Aj, -1, -1);
        V2 a = City.Node(t.Ai, t.Aj), b = City.Node(t.Bi, t.Bj), dir = M.Norm(b - a);
        t.Pos = V2.Lerp(a, b, R.Range(0.2f, 0.8f)) + M.Perp(dir);
        t.Heading = M.Angle(dir);
        t.Event = true;
        t.Turning = false;
        if (V2.Distance(t.Pos, Player.Pos) < 6) t.Pos += dir * 8;
    }

    /// <summary>Fans on the pavement round the stadium.</summary>
    private void SpawnFan()
    {
        Lot st = Stadium;
        if (st == null) return;
        for (int tries = 0; tries < 60; tries++)
        {
            int x = st.R.X - 3 + R.Int(st.R.W + 6), y = st.R.Y - 3 + R.Int(st.R.H + 6);
            if (City[x, y] != CTile.Sidewalk) continue;
            Peds.Add(new Ped { Pos = new V2(x + R.Range(0.2f, 0.8f), y + R.Range(0.2f, 0.8f)), Speed = R.Range(0.9f, 1.5f), Colour = R.Chance(0.6f) ? 0x2a6ad0ffu : 0xf2c418ffu, Phase = R.Float() * 10, Dir = R.Pick(Pt.Dirs4), Fan = true });
            return;
        }
    }

    // ---- the motorcade ------------------------------------------------------------------

    /// <summary>Election week: a candidate's limo doing the rounds, a cruiser either side of it.</summary>
    private void SpawnMotorcade()
    {
        SpawnTraffic(99);
        TrafficCar lead = Traffic[^1];
        lead.Kind = CarKind.Limo;
        lead.Length = 2.9f;
        lead.Width = 1f;
        lead.Colour = 0x0c0c10ff;
        lead.Cruise = Tables.MotorcadeCruise;
        lead.Speed = lead.Cruise;
        lead.Motorcade = true;
        lead.Trail = new List<(int, int)> { (lead.Bi, lead.Bj) };
        if (V2.Distance(lead.Pos, Player.Pos) < 30) lead.Pos = V2.Lerp(City.Node(lead.Ai, lead.Aj), City.Node(lead.Bi, lead.Bj), 0.5f) + M.Perp(lead.Forward);
        for (int k = 0; k < 2; k++)
        {
            SpawnTraffic(99);
            TrafficCar e = Traffic[^1];
            e.Kind = CarKind.Patrol;
            e.Colour = 0xe8e8f0ff;
            e.Length = 1.9f;
            e.Width = 0.95f;
            (e.Ai, e.Aj, e.Bi, e.Bj) = (lead.Ai, lead.Aj, lead.Bi, lead.Bj);
            e.Turning = false;
            e.Heading = lead.Heading;
            e.Pos = lead.Pos - lead.Forward * (3.4f * (k + 1));
            e.Cruise = lead.Cruise + 1.5f;
            e.Speed = lead.Cruise;
            e.Follow = k == 0 ? lead : Traffic[^2];
            e.Motorcade = true;
        }
    }

    /// <summary>An escort's next junction: wherever the limo went from the one it is heading for (null: it lost the trail).</summary>
    private static (int, int)? TrailNext(TrafficCar t)
    {
        TrafficCar lead = t.Follow;
        while (lead?.Follow != null) lead = lead.Follow;
        if (lead?.Trail == null || lead.Gone) return null;
        int at = lead.Trail.LastIndexOf((t.Bi, t.Bj));
        return at >= 0 && at + 1 < lead.Trail.Count ? lead.Trail[at + 1] : null;
    }

    /// <summary>You hit the motorcade: the escort is on you, once a week.</summary>
    private void HitMotorcade(TrafficCar t)
    {
        if (!t.Motorcade || _motorcadeHit) return;
        _motorcadeHit = true;
        AddHeat(Tables.MotorcadeHeat, "You rammed the candidate's motorcade.");
        Events.Add(new CityEvent(CityEv.Siren, t.Pos, 1, "You rammed the candidate's motorcade. KBN will have that on the morning news."));
        C.Set("motorcade:hit", C.Day);
    }

    // ---- what a street scene sets going ----------------------------------------------------

    /// <summary>A street scene just closed: the bikes after you, the cruiser, the race.</summary>
    public void AfterStreet()
    {
        // What the scene did to the car (a ghoul under the bonnet).
        if (C.CarHp < Player.Hp) Player.Hp = C.CarHp;
        if (C.Flag("street:ambush") is int n && n > 0) { C.Set("street:ambush", 0); Ambush(n); }
        if (C.Flag("street:cruiser") > 0)
        {
            C.Set("street:cruiser", 0);
            if (!C.On("strike")) SpawnAt(ChaserKind.Police, Player.Pos - Player.Forward * 6);
            Events.Add(new CityEvent(CityEv.Siren, Player.Pos, 1, "Lights in the mirror. The cruiser pulls out after you."));
        }
        if (C.Flag("street:race") is int stake && stake > 0)
        {
            C.Set("street:race", 0);
            StartStreetRace(stake);
        }
    }

    /// <summary>Lights, and go: to a landmark across town, the hotshot in the next lane.</summary>
    public void StartStreetRace(int stake)
    {
        var marks = City.Lots.Where(l => l.Landmark != null && l.Landmark != "arcology").ToList();
        if (marks.Count == 0) return;
        Lot to = marks.OrderBy(l => MathF.Abs(V2.Distance(l.R.Centre.Centre, Player.Pos) - 70)).First();
        // The finish: the junction nearest the landmark's middle.
        V2 best = City.Node(0, 0);
        for (int i = 0; i < City.RoadX.Count; i++)
        for (int j = 0; j < City.RoadY.Count; j++)
            if (V2.Distance(City.Node(i, j), to.R.Centre.Centre) < V2.Distance(best, to.R.Centre.Centre)) best = City.Node(i, j);
        V2 at = Player.Pos + Player.Right * 1.4f;
        if (City.Solid((int)at.X, (int)at.Y)) at = Player.Pos - Player.Right * 1.4f;
        var car = new Car { Def = RacerCar, Hp = RacerCar.Hp, Pos = at, Heading = Player.Heading };
        var rival = new Chaser { Kind = ChaserKind.Racer, Car = car, Route = new DriveAutopilot(City, at, best, AnyClosed ? Closed : null) };
        Chasers.Add(rival);
        string where = City.Landmarks.First(x => x.id == to.Landmark).name;
        Race = new StreetRace { Finish = best, Where = where, Stake = stake, Rival = rival };
        Events.Add(new CityEvent(CityEv.Pickup, Player.Pos, 0, $"Three... two... GO! First to {where} takes {M.Money(stake * 2)}."));
    }

    /// <summary>The street race: you there first, or him.</summary>
    private void StepRace()
    {
        if (Race == null) return;
        if (V2.Distance(Player.Pos, Race.Finish) < Tables.RaceFinish)
        {
            int pot = Race.Stake * 2;
            C.Cash += pot;
            C.Count("earned", pot);
            C.Count("streetraces");
            if (Race.Rival != null) Race.Rival.Gone = true;
            Events.Add(new CityEvent(CityEv.Delivered, Player.Pos, 0, $"First to {Race.Where}! The hotshot pays up: {M.Money(pot)}."));
            Race = null;
            return;
        }
        if (Race.Rival == null) return;
        if (Race.Rival.Gone || Race.Rival.Car.Hp <= 0)
        {
            Events.Add(new CityEvent(CityEv.Message, Player.Pos, 0, "The hotshot's out of it. The pot's yours: just get to the line."));
            Race.Rival = null;
        }
        else if (V2.Distance(Race.Rival.Car.Pos, Race.Finish) < Tables.RaceFinish)
        {
            Race.Rival.Gone = true;
            Events.Add(new CityEvent(CityEv.Message, Race.Rival.Car.Pos, 0, $"The hotshot gets to {Race.Where} first. Your stake's gone."));
            Race = null;
        }
    }

    /// <summary>The hotshot in a street race (no job to it): along his route to the finish.</summary>
    private void StepStreetRacer(Chaser ch, float dt)
    {
        DriveInput inp = ch.Route.Step(this, ch.Car, dt, Tables.StreetRacerCruise);
        Drive(ch.Car, dt, inp, false);
        if (ch.Car.Hp <= 0) { ch.Gone = true; Events.Add(new CityEvent(CityEv.Crash, ch.Car.Pos, 5)); Events.Add(new CityEvent(CityEv.Wreck, ch.Car.Pos, ch.Car.Def.Length, null, ch.Car.Pos + ch.Car.Forward)); }
    }
}
