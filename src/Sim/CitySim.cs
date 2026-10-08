using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

public struct DriveInput
{
    /// <summary>-1 brake/reverse .. 1 full throttle.</summary>
    public float Throttle;
    /// <summary>-1 left .. 1 right.</summary>
    public float Steer;
    public bool Handbrake;
    /// <summary>Lean on the horn this step (it has its own cooldown).</summary>
    public bool Horn;
    /// <summary>Car gadgets (Torque's mods): nitro held, smoke and oil pressed.</summary>
    public bool Boost, Smoke, Oil;
}

public sealed class Car
{
    public VehicleDef Def;
    public V2 Pos, Vel;
    public float Heading, Hp;
    /// <summary>Bikes: how far the rider leans (-1..1), and the front wheel off the ground.</summary>
    public float Lean, Wheelie;
    public V2 Forward => M.Dir(Heading);
    public V2 Right => M.Perp(Forward);
    public float Speed => V2.Dot(Vel, Forward);
    public float Lateral => V2.Dot(Vel, Right);
    /// <summary>The two circles a car collides as: front and back.</summary>
    public float R => Def.Width / 2;
    public V2 Front => Pos + Forward * (Def.Length / 2 - R);
    public V2 Back => Pos - Forward * (Def.Length / 2 - R);
}

/// <summary>What is on the road besides you.</summary>
public enum CarKind { Sedan, Van, Taxi, IceCream, Garbage, Limo, Bus, Bike, Patrol, Ambulance }

public sealed class TrafficCar
{
    public CarKind Kind;
    public float JingleT;
    public bool Gone;
    /// <summary>Pulling out to pass something stopped in the lane.</summary>
    public float SwerveT, Stuck;
    public float Lane => SwerveT > 0 ? -0.9f : 1f;
    public V2 Pos;
    public float Heading, Speed, Cruise, Wait, Ghost, Bumped;
    public int Ai, Aj, Bi, Bj, Ci = -1, Cj = -1;
    public bool Turning;
    /// <summary>Already counted as rammed (once per car).</summary>
    public bool Rammed;
    /// <summary>An ambulance heading for a scene: where (NaN: just cruising).</summary>
    public V2 Call = new(float.NaN, float.NaN);
    public uint Colour;
    public float Length = 1.9f, Width = 0.95f;
    public V2 Forward => M.Dir(Heading);
}

public sealed class Ped
{
    public V2 Pos;
    public Pt Dir;
    public float Speed, Down, Flee, Phase;
    public V2 FleeDir;
    public uint Colour;
}

public enum CityEv { Jingle, Crash, Bump, Honk, Knock, Scrape, Siren, Gunfire, Busted, Lost, Message, Pickup, Delivered, Wail, Scream }

public readonly record struct CityEvent(CityEv Kind, V2 Pos, float Strength, string Text = null);

/// <summary>The city in motion: your car, the traffic and the people on the pavements.</summary>
public sealed partial class CitySim
{
    public readonly City City;
    public readonly Career C;
    public readonly Car Player;
    public readonly List<TrafficCar> Traffic = new();
    public readonly List<Ped> Peds = new();
    public readonly List<CityEvent> Events = new();
    public readonly Rng R;
    public float Time;
    public bool Wrecked;
    private float _odo;
    private readonly List<TrafficCar> _ambulances = new();
    private readonly List<Ped> _roadPeds = new();
    private bool _blockedByPlayer;

    /// <summary>The collision tests handed to <see cref="Collide.PushOut"/>, made once.</summary>
    private Func<int, int, bool> _citySolid, _carSolid;
    /// <summary>A walker's ways on at a corner (reused).</summary>
    private readonly List<Pt> _pedOpts = new(4);

    private static readonly uint[] CarColours = { 0x3a3f4aff, 0x6a1e2aff, 0x2a4a6aff, 0xb0b0a8ff, 0x1a1a1eff, 0x7a6a3aff, 0x2e5a3eff, 0xc8c0a0ff };
    private static readonly uint[] PedColours = { 0xff2a6dff, 0x05d9e8ff, 0xe0e0e0ff, 0x8a8a9aff, 0xf9c80eff, 0x40ffb0ff, 0x9a6bffff, 0x5a4a3aff };

    public CitySim(City city, Career c, ulong seed)
    {
        City = city;
        C = c;
        R = new Rng(seed);
        VehicleDef def = c.Ride();
        Player = new Car { Def = def, Hp = c.CarHp };
        if (float.IsNaN(c.CarX))
        {
            (V2 pos, float h) = city.Kerb(HomeLot);
            Player.Pos = pos;
            Player.Heading = h;
        }
        else
        {
            Player.Pos = new V2(c.CarX, c.CarY);
            Player.Heading = c.CarHeading;
        }
        for (int k = 0; k < 80; k++) SpawnTraffic(k);
        for (int k = 0; k < 190; k++) SpawnPed();
    }

    public Lot HomeLot => City.Place(C.HasUpgrade("loft") ? "loft" : "home");

    public void ParkAt(Lot lot)
    {
        (V2 pos, float h) = City.Kerb(lot);
        Player.Pos = pos;
        Player.Heading = h;
        Player.Vel = V2.Zero;
        SaveCar();
    }

    /// <summary>A cab fare across town: by distance, at least ¥100.</summary>
    public int Fare(Lot to) => Math.Max(100, (int)(V2.Distance(Player.Pos, to.Door.Centre) * 3 / 10) * 10);

    /// <summary>Why you can't be driven or flown somewhere right now (null: you can).</summary>
    public string TravelBlock(bool heli)
    {
        if (C.Courier) return "Not with a package in the car -- the client wants it driven.";
        if (!heli && Chasers.Any()) return "No cab will stop with someone on your tail.";
        if (!heli && C.Cash < 100) return "You can't afford a cab.";
        return null;
    }

    /// <summary>Cab or helicopter: you and the car (on a sling, or towed behind) arrive at a door.</summary>
    public void TravelTo(Lot lot, bool heli)
    {
        if (!heli) { int fare = Fare(lot); C.Cash -= fare; C.Count("spent", fare); }
        Chasers.Clear();
        ParkAt(lot);
        Events.Add(new CityEvent(CityEv.Message, Player.Pos, 0, heli ? "The Kite sets you down, car and all." : "The cab drops you off. Your car was towed behind, gently."));
    }

    public void SaveCar()
    {
        C.CarX = Player.Pos.X;
        C.CarY = Player.Pos.Y;
        C.CarHeading = Player.Heading;
    }

    /// <summary>Swap to the career's current vehicle (after buying or choosing one).</summary>
    public void RefreshVehicle()
    {
        Player.Def = C.Ride();
        Player.Hp = C.CarHp;
    }

    // ---- places -------------------------------------------------------------------

    /// <summary>Lots you can go into: home, contacts, the active job.</summary>
    public IEnumerable<Lot> Destinations()
    {
        yield return HomeLot;
        foreach (ContactDef k in Tables.Contacts) yield return City.Place(k.Id);
        foreach (var h in City.Hubs) yield return City.Place(h.id);
        Job j = C.Active;
        if (j != null && j.Driving) yield return CourierLot(j);
        else if (j != null && j.Lot >= 0) yield return City.Lots[j.Lot];
    }

    /// <summary>The place whose door the car is stopped at, if any.</summary>
    public Lot AtDoor()
    {
        if (MathF.Abs(Player.Speed) > 3.5f) return null;
        Lot best = null;
        float bd = 2.6f;
        foreach (Lot l in Destinations())
        {
            float d = V2.Distance(l.Door.Centre, Player.Pos);
            if (d < bd) { bd = d; best = l; }
        }
        return best;
    }

    // ---- stepping -----------------------------------------------------------------

    public void Step(float dt, DriveInput input)
    {
        Time += dt;
        Events.Clear();
        StepStreet(dt);
        StepGadgets(dt, input);
        StepRoadblocks(dt);
        Drive(Player, dt, input, true);
        _hornT -= dt;
        if (input.Horn && _hornT <= 0) Honk();
        _odo += Player.Vel.Length() * dt;
        if (_odo >= 1) { C.Count("tiles", (int)_odo); _odo -= (int)_odo; }
        StepPursuit(dt);
        _ambulances.Clear();
        foreach (TrafficCar t in Traffic) if (t.Kind == CarKind.Ambulance) _ambulances.Add(t);
        // Only people standing in the road can block a car: find them once a step, not once a car.
        _roadPeds.Clear();
        foreach (Ped p in Peds) if (p.Down <= 0 && City[(int)p.Pos.X, (int)p.Pos.Y] == CTile.Road) _roadPeds.Add(p);
        foreach (TrafficCar t in Traffic) StepTraffic(t, dt);
        SeparateTraffic();
        bool ambulance = Traffic.Any(t => t.Gone && t.Kind == CarKind.Ambulance);
        int gone = Traffic.RemoveAll(t => t.Gone);
        for (int k = 0; k < gone; k++) SpawnTraffic(ambulance && k == 0 ? 2 : 99);
        foreach (Ped p in Peds) StepPed(p, dt);
    }

    /// <summary>Car physics for anyone: the player's car, police, gangers.</summary>
    private void Drive(Car car, float dt, DriveInput inp, bool player)
    {
        VehicleDef d = car.Def;
        float dm = player ? C.DrivingMul : 1.05f;
        V2 f = car.Forward, right = car.Right;
        float vf = V2.Dot(car.Vel, f), vr = V2.Dot(car.Vel, right);
        float boost = player && BoostT > 0 ? 1.4f : 1f;
        float top = d.Top * boost * (City[(int)car.Pos.X, (int)car.Pos.Y] == CTile.Grass && !d.Offroad ? 0.55f : 1f);

        if (inp.Throttle > 0.05f)
        {
            if (vf < -0.3f) vf += 16 * dt * inp.Throttle;
            else if (vf < top) vf += d.Accel * boost * inp.Throttle * dt * (1.15f - 0.5f * vf / top);
        }
        else if (inp.Throttle < -0.05f)
        {
            if (vf > 0.3f) vf -= (d.Bike ? 20 : 16) * dt * -inp.Throttle;
            else if (vf > -top * 0.35f) vf -= d.Accel * 0.6f * -inp.Throttle * dt;
        }
        else vf -= vf * 0.6f * dt + MathF.Sign(vf) * MathF.Min(MathF.Abs(vf), 0.8f * dt);
        // Parked means parked.
        if (MathF.Abs(inp.Throttle) < 0.05f && MathF.Abs(vf) < 0.4f) { vf = 0; vr *= 0.5f; }
        if (vf > top) vf -= (vf - top) * 2 * dt;

        // Steering bites with speed and loosens towards the top end.
        float sp = MathF.Abs(vf);
        float steer = inp.Steer * 2.7f * dm * M.Clamp(sp / 2.5f, 0, 1) * (1 - 0.35f * M.Clamp(sp / d.Top, 0, 1)) * (d.Bike ? 1.3f : 1f);
        if (inp.Handbrake) steer *= 1.35f;
        car.Heading += steer * MathF.Sign(vf) * dt;

        float grip = d.Grip * dm * (inp.Handbrake ? 0.22f : 1f);
        if (d.Bike)
        {
            // Two wheels: the back steps out when pushed hard at speed.
            grip *= 1 - 0.35f * M.Clamp(sp / d.Top, 0, 1) * MathF.Abs(inp.Steer);
            car.Lean += (inp.Steer * M.Clamp(sp / 6, 0, 1) - car.Lean) * MathF.Min(1, dt * 8);
            float pull = inp.Throttle > 0.5f && sp < 6 ? 1 : 0;
            car.Wheelie += (pull - car.Wheelie) * MathF.Min(1, dt * (pull > 0 ? 3 : 6));
        }
        vr *= MathF.Exp(-grip * 11 * dt);
        if (inp.Handbrake) vf -= vf * 1.2f * dt;

        f = car.Forward;
        right = car.Right;
        car.Vel = f * vf + right * vr;
        car.Pos += car.Vel * dt;

        // Walls and water.
        Func<int, int, bool> solid = car.Def == PoliceCar ? _citySolid ??= City.Solid : _carSolid ??= CarSolid;
        for (int pass = 0; pass < 2; pass++)
        for (int end = 0; end < 2; end++)
        {
            V2 c = end == 0 ? car.Front : car.Back;
            V2 before = c;
            // Police drive through their own checkpoints.
            if (!Collide.PushOut(ref c, car.R, solid, out V2 n)) continue;
            car.Pos += c - before;
            float into = -V2.Dot(car.Vel, n);
            if (into > 0)
            {
                car.Vel += n * into * 1.3f;
                car.Vel *= 0.85f;
                if (into > 4.5f) { if (player) Hurt((into - 4.5f) * 2.2f, CityEv.Crash, c); else car.Hp -= (into - 4.5f) * 3; }
                else if (into > 1 && player) Events.Add(new CityEvent(CityEv.Scrape, c, into));
            }
        }

        // Traffic.
        float carReach = MathF.Abs(car.Def.Length / 2 - car.R) + car.R;
        foreach (TrafficCar t in Traffic)
        {
            if (t.Ghost > 0) continue;
            float tr = t.Width / 2;
            // Too far apart for any of the four circle pairs to touch.
            float reach = carReach + MathF.Abs(t.Length / 2 - tr) + tr + 0.05f;
            if (V2.DistanceSquared(car.Pos, t.Pos) >= reach * reach) continue;
            V2 tf = t.Forward;
            V2 t1 = t.Pos + tf * (t.Length / 2 - tr), t2 = t.Pos - tf * (t.Length / 2 - tr);
            V2 front = car.Front, back = car.Back;
            for (int k = 0; k < 4; k++)
            {
                V2 pc = k < 2 ? front : back, tc = k % 2 == 0 ? t1 : t2;
                V2 delta = pc - tc;
                float dist = delta.Length(), min = car.R + tr;
                if (dist >= min || dist < 1e-4f) continue;
                V2 n = delta / dist;
                bool heavy = d.Id is "bulldog" or "rhino";
                car.Pos += n * (min - dist) * (heavy ? 0.3f : 0.8f);
                t.Pos -= n * (min - dist) * (heavy ? 0.7f : 0.2f);
                float into = -V2.Dot(car.Vel, n);
                if (into > 0)
                {
                    car.Vel += n * into * (heavy ? 0.4f : 1.2f);
                    t.Bumped = 1.5f;
                    t.Speed = 0;
                    if (into > 4.5f) { if (player) { Hurt((into - 4.5f) * (heavy ? 0.5f : 1.5f), CityEv.Bump, pc); OnTrafficHit(into); RammedAmbulance(t); } else car.Hp -= (into - 4.5f) * 2; }
                    else if (player) Events.Add(new CityEvent(CityEv.Scrape, pc, into));
                }
            }
        }
    }

    private void Hurt(float dmg, CityEv kind, V2 at)
    {
        if (Player.Def.Id is "bulldog" or "rhino") dmg *= 0.5f;
        if (kind is CityEv.Crash or CityEv.Bump && C.CarHas("runflat")) dmg *= 0.7f;
        if (kind == CityEv.Bump && C.CarHas("ram")) dmg *= 0.5f;
        if (Player.Def.Bike) dmg *= kind == CityEv.Gunfire ? 1.5f : 1.4f;
        Player.Hp = MathF.Max(0, Player.Hp - dmg);
        C.CarHp = (int)MathF.Ceiling(Player.Hp);
        Events.Add(new CityEvent(kind, at, dmg));
        if (Player.Hp <= 0) Wrecked = true;
    }

    // ---- traffic ------------------------------------------------------------------

    private static readonly (CarKind kind, float weight)[] Mix =
    {
        (CarKind.Sedan, 48), (CarKind.Van, 12), (CarKind.Taxi, 11), (CarKind.Bike, 8), (CarKind.Bus, 4), (CarKind.Garbage, 3), (CarKind.Limo, 3), (CarKind.Patrol, 3),
    };

    private void SpawnTraffic(int k = 99)
    {
        int ni = City.RoadX.Count, nj = City.RoadY.Count;
        var t = new TrafficCar { Ai = R.Int(ni), Aj = R.Int(nj), Colour = R.Pick(CarColours), Cruise = R.Range(5.5f, 8.5f) };
        // One or two ice cream trucks always about, and a TraumaLink ambulance; the rest from the mix.
        if (k < 2) t.Kind = CarKind.IceCream;
        else if (k == 2) t.Kind = CarKind.Ambulance;
        else
        {
            float roll = R.Float() * Mix.Sum(m => m.weight);
            foreach (var m in Mix) { roll -= m.weight; if (roll <= 0) { t.Kind = m.kind; break; } }
        }
        switch (t.Kind)
        {
            case CarKind.Van: t.Length = 2.3f; t.Width = 1.1f; t.Colour = R.Pick(new uint[] { 0xd8d8d0ff, 0x30404aff, 0x6a5a3aff }); break;
            case CarKind.Taxi: t.Colour = 0xf2c418ff; t.Cruise = R.Range(7, 9.5f); break;
            case CarKind.IceCream: t.Length = 2.4f; t.Width = 1.15f; t.Colour = 0xf6f0f4ff; t.Cruise = 4.5f; break;
            case CarKind.Garbage: t.Length = 2.7f; t.Width = 1.2f; t.Colour = 0x3d6a3aff; t.Cruise = 4; break;
            case CarKind.Limo: t.Length = 2.9f; t.Width = 1f; t.Colour = 0x0c0c10ff; t.Cruise = 6; break;
            case CarKind.Bus: t.Length = 3.6f; t.Width = 1.25f; t.Colour = 0x2a6ad0ff; t.Cruise = 4.5f; break;
            case CarKind.Bike: t.Length = 1.3f; t.Width = 0.5f; t.Cruise = R.Range(8, 11); break;
            case CarKind.Patrol: t.Colour = 0xe8e8f0ff; t.Cruise = 6.5f; break;
            case CarKind.Ambulance: t.Length = 2.4f; t.Width = 1.1f; t.Colour = 0xf2f4f0ff; t.Cruise = 9.5f; break;
        }
        (t.Bi, t.Bj) = PickNext(t.Ai, t.Aj, -1, -1);
        V2 a = City.Node(t.Ai, t.Aj), b = City.Node(t.Bi, t.Bj);
        V2 dir = M.Norm(b - a);
        t.Pos = V2.Lerp(a, b, R.Range(0.2f, 0.8f)) + M.Perp(dir) * 1f;
        t.Heading = M.Angle(dir);
        t.Speed = t.Cruise;
        if (V2.Distance(t.Pos, Player.Pos) < 6) t.Pos += dir * 8;
        Traffic.Add(t);
    }

    /// <summary>The next junction on the way to a point (ambulances on a call).</summary>
    private (int, int) Toward(int i, int j, V2 goal)
    {
        var opts = new List<(int, int)>();
        if (i > 0) opts.Add((i - 1, j));
        if (i + 1 < City.RoadX.Count) opts.Add((i + 1, j));
        if (j > 0) opts.Add((i, j - 1));
        if (j + 1 < City.RoadY.Count) opts.Add((i, j + 1));
        return opts.OrderBy(o => V2.Distance(City.Node(o.Item1, o.Item2), goal)).First();
    }

    private (int, int) PickNext(int i, int j, int fromI, int fromJ)
    {
        var opts = new List<(int, int)>();
        if (i > 0) opts.Add((i - 1, j));
        if (i + 1 < City.RoadX.Count) opts.Add((i + 1, j));
        if (j > 0) opts.Add((i, j - 1));
        if (j + 1 < City.RoadY.Count) opts.Add((i, j + 1));
        if (opts.Count > 1) opts.Remove((fromI, fromJ));
        return R.Pick(opts);
    }

    private void StepTraffic(TrafficCar t, float dt)
    {
        if (t.Kind == CarKind.IceCream && (t.JingleT -= dt) <= 0 && V2.Distance(t.Pos, Player.Pos) < 14)
        {
            t.JingleT = 7;
            Events.Add(new CityEvent(CityEv.Jingle, t.Pos, 1 - V2.Distance(t.Pos, Player.Pos) / 14));
        }
        if (t.Kind == CarKind.Ambulance && (t.JingleT -= dt) <= 0 && V2.Distance(t.Pos, Player.Pos) < 18)
        {
            t.JingleT = 2.2f;
            Events.Add(new CityEvent(CityEv.Wail, t.Pos, 1 - V2.Distance(t.Pos, Player.Pos) / 18));
        }
        if (t.Kind == CarKind.Ambulance && !float.IsNaN(t.Call.X) && V2.Distance(t.Pos, t.Call) < 3) t.Call = new V2(float.NaN, float.NaN);
        if (t.Kind == CarKind.Patrol && Stars >= 1 && !C.On("strike") && V2.Distance(t.Pos, Player.Pos) < (C.CarHas("ecm") ? 6 : 10) && ClearRoad(t.Pos, Player.Pos))
        {
            // A cruising patrol spots a wanted runner and joins the chase.
            Chasers.Add(new Chaser { Kind = ChaserKind.Police, Car = new Car { Def = PoliceCar, Hp = PoliceCar.Hp, Pos = t.Pos, Heading = t.Heading, Vel = t.Forward * t.Speed } });
            t.Gone = true;
            Events.Add(new CityEvent(CityEv.Siren, t.Pos, 1));
            return;
        }
        V2 a = City.Node(t.Ai, t.Aj), b = City.Node(t.Bi, t.Bj);
        V2 d = M.Norm(b - a);
        V2 target;
        if (!t.Turning)
        {
            target = b + M.Perp(d) * t.Lane - d * 2.8f;
            if (V2.Distance(t.Pos, target) < 0.6f)
            {
                t.Turning = true;
                (t.Ci, t.Cj) = float.IsNaN(t.Call.X) ? PickNext(t.Bi, t.Bj, t.Ai, t.Aj) : Toward(t.Bi, t.Bj, t.Call);
            }
        }
        if (t.Turning)
        {
            V2 d2 = M.Norm(City.Node(t.Ci, t.Cj) - b);
            target = b + M.Perp(d2) * 1f + d2 * 2.8f;
            if (V2.Distance(t.Pos, target) < 0.6f)
            {
                t.Ai = t.Bi; t.Aj = t.Bj; t.Bi = t.Ci; t.Bj = t.Cj;
                t.Turning = false;
                a = b;
                b = City.Node(t.Bi, t.Bj);
                d = M.Norm(b - a);
                target = b + M.Perp(d) * t.Lane - d * 2.8f;
            }
        }
        else target = b + M.Perp(d) * t.Lane - d * 2.8f;
        // Changing lanes: aim a few tiles ahead in the new lane, not at the far end of the block.
        if (!t.Turning && t.SwerveT > 0)
        {
            V2 proj = target + d * V2.Dot(t.Pos - target, d);
            V2 near = proj + d * 2.5f;
            if (V2.Dot(near - target, d) < 0) target = near;
        }

        // Brake for anything in front.
        float want = t.Turning ? MathF.Min(t.Cruise, 4.5f) : t.Cruise;
        // An ambulance coming up behind: pull over and let it by.
        if (t.Kind != CarKind.Ambulance && t.SwerveT <= 0 && !t.Turning)
            foreach (TrafficCar o in _ambulances)
            {
                V2 rel = o.Pos - t.Pos;
                float behind = -V2.Dot(rel, t.Forward);
                if (behind > 0.5f && behind < 6 && MathF.Abs(V2.Dot(rel, M.Perp(t.Forward))) < 1 && V2.Dot(o.Forward, t.Forward) > 0.7f) { t.SwerveT = 1.6f; break; }
            }
        if (t.Bumped > 0) { t.Bumped -= dt; want = 0; }
        if (t.SwerveT > 0) { t.SwerveT -= dt; want = MathF.Min(want, 3.5f); }
        if (t.Ghost > 0) t.Ghost -= dt;
        else if (Blocked(t))
        {
            want = 0;
            t.Wait += dt;
            // Stuck behind your stopped car: pull out and go round. Stuck behind
            // another car (a junction knot): slip through after a moment.
            if (_blockedByPlayer && t.Wait > 1.5f && t.SwerveT <= 0) { t.SwerveT = 3.5f; t.Wait = 0; }
            else if (!_blockedByPlayer && t.Wait > 2.5f) { t.Ghost = 1.5f; t.Wait = 0; }
            else if (t.Wait > 5f) { t.Ghost = 1.5f; t.Wait = 0; }
            t.Stuck += dt;
        }
        else { t.Stuck = 0; t.Wait = 0; }
        // Long knotted, out of sight: quietly respawn elsewhere.
        if (t.Stuck > 8 && V2.Distance(t.Pos, Player.Pos) > 16) t.Gone = true;
        else if (t.Stuck > 5) { t.Ghost = 2f; t.Stuck = 0; }
        t.Speed += M.Clamp(want - t.Speed, -14 * dt, 5 * dt);

        V2 to = target - t.Pos;
        float dist = to.Length();
        if (dist > 1e-3f)
        {
            V2 dir = to / dist;
            t.Pos += dir * MathF.Min(dist, t.Speed * dt);
            if (t.Speed > 0.2f) t.Heading = M.TurnTo(t.Heading, M.Angle(dir), 5f * dt);
        }
    }

    /// <summary>Cars never sit inside each other: overlapping ones are nudged apart.</summary>
    private void SeparateTraffic()
    {
        for (int i = 0; i < Traffic.Count; i++)
        for (int j = i + 1; j < Traffic.Count; j++)
        {
            TrafficCar a = Traffic[i], b = Traffic[j];
            if (a.Ghost > 0 || b.Ghost > 0) continue;
            V2 d = b.Pos - a.Pos;
            float min = (a.Width + b.Width) * 0.5f + 0.1f, dist = d.Length();
            if (dist >= min || dist < 1e-4f) continue;
            V2 n = d / dist * ((min - dist) * 0.5f);
            a.Pos -= n;
            b.Pos += n;
        }
    }

    private bool Blocked(TrafficCar t)
    {
        V2 f = t.Forward;
        bool Ahead(V2 p, float range, float side = 0.9f)
        {
            V2 rel = p - t.Pos;
            float along = V2.Dot(rel, f);
            if (along <= 0.2f || along > range) return false;
            return MathF.Abs(V2.Dot(rel, M.Perp(f))) < side;
        }
        _blockedByPlayer = false;
        // A checkpoint: stop, get looked at, get waved through (the wait-then-slip below).
        if (Roadblocks.Count > 0 && RoadblockAhead(t.Pos, f, 3)) return true;
        // Pulling out to pass: only yield to you when you are right in its new line.
        if (t.SwerveT > 0 ? Ahead(Player.Pos, 1.2f, 0.6f) : Ahead(Player.Pos, 3.4f))
        {
            _blockedByPlayer = true;
            if (R.Chance(0.01f)) Events.Add(new CityEvent(CityEv.Honk, t.Pos, 1));
            return true;
        }
        // Everyone gets out of an ambulance's way; it only stops for you and for people.
        if (t.Kind != CarKind.Ambulance)
            foreach (TrafficCar o in Traffic)
            {
                float dx = o.Pos.X - t.Pos.X, dy = o.Pos.Y - t.Pos.Y;
                if (o != t && dx * dx + dy * dy < 9 && Ahead(o.Pos, 2.9f)) return true;
            }
        foreach (Ped p in _roadPeds)
        {
            float dx = p.Pos.X - t.Pos.X, dy = p.Pos.Y - t.Pos.Y;
            if (dx * dx + dy * dy < 6 && Ahead(p.Pos, 2.4f)) return true;
        }
        return false;
    }

    // ---- TraumaLink -----------------------------------------------------------------

    private void RammedAmbulance(TrafficCar t)
    {
        if (t.Kind != CarKind.Ambulance || t.Rammed) return;
        t.Rammed = true;
        C.Infamy(1);
        AddHeat(0.5f, "You rammed a TraumaLink ambulance.");
        Events.Add(new CityEvent(CityEv.Message, Player.Pos, 0, "You rammed a TraumaLink ambulance. People will remember that."));
    }

    /// <summary>Send an ambulance (and a patrol) to a lot: after a loud run, or pulling away from the clinic.</summary>
    public void Respond(Lot lot, bool patrol = true)
    {
        (V2 kerb, float h) = City.Kerb(lot);
        // The nearest junction 10-24 tiles off, so they arrive rather than appear.
        V2 best = kerb;
        float bd = float.MaxValue;
        for (int i = 0; i < City.RoadX.Count; i++)
        for (int j = 0; j < City.RoadY.Count; j++)
        {
            float d = V2.Distance(City.Node(i, j), kerb);
            if (d > 10 && d < bd) { bd = d; best = City.Node(i, j); }
        }
        foreach (bool police in patrol ? new[] { false, true } : new[] { false })
        {
            SpawnTraffic(police ? 99 : 2);
            TrafficCar t = Traffic[^1];
            if (police) { t.Kind = CarKind.Patrol; t.Colour = 0xe8e8f0ff; t.Cruise = 7.5f; t.Length = 1.9f; t.Width = 0.95f; }
            t.Pos = best + new V2(police ? 1.5f : 0, 0);
            t.Call = kerb;
            float dd = float.MaxValue;
            for (int i = 0; i < City.RoadX.Count; i++)
            for (int j = 0; j < City.RoadY.Count; j++)
                if (V2.Distance(City.Node(i, j), best) < dd) { dd = V2.Distance(City.Node(i, j), best); t.Ai = i; t.Aj = j; }
            (t.Bi, t.Bj) = PickNext(t.Ai, t.Aj, -1, -1);
            t.Heading = M.Angle(City.Node(t.Bi, t.Bj) - City.Node(t.Ai, t.Aj));
        }
    }

    /// <summary>The ambulance that dropped you off, pulling away from the kerb.</summary>
    public void DropOff(Lot lot)
    {
        (V2 kerb, float h) = City.Kerb(lot);
        SpawnTraffic(2);
        TrafficCar t = Traffic[^1];
        t.Pos = kerb + M.Dir(h) * 3.2f;
        t.Heading = h;
        t.Speed = 2;
        float dd = float.MaxValue;
        for (int i = 0; i < City.RoadX.Count; i++)
        for (int j = 0; j < City.RoadY.Count; j++)
        {
            V2 n = City.Node(i, j);
            float d = V2.Distance(n, t.Pos);
            if (d < dd && V2.Dot(n - t.Pos, M.Dir(h)) > 0) { dd = d; t.Bi = i; t.Bj = j; }
        }
        (t.Ai, t.Aj) = (t.Bi, t.Bj);
        (t.Bi, t.Bj) = PickNext(t.Ai, t.Aj, -1, -1);
    }

    // ---- the horn ------------------------------------------------------------------

    public const float HornRange = 9f, HornCooldown = 0.5f;
    private float _hornT;

    /// <summary>
    /// Your horn: everyone on foot within <see cref="HornRange"/> who is still
    /// standing scatters, running straight away from the car for a moment --
    /// the same flee a pedestrian does when a car bears down on them, only
    /// further and in every direction at once.
    /// </summary>
    public void Honk()
    {
        _hornT = HornCooldown;
        Events.Add(new CityEvent(CityEv.Honk, Player.Pos, 2));
        Scatter(Player.Pos, HornRange, 1.1f, 1.6f);
    }

    /// <summary>Everyone on foot within <paramref name="range"/> runs straight away from <paramref name="from"/> for a while.</summary>
    public void Scatter(V2 from, float range, float min, float max)
    {
        bool screamed = false;
        foreach (Ped p in Peds)
        {
            if (p.Down > 0 || V2.Distance(p.Pos, from) > range || p.Flee >= min) continue;
            V2 away = p.Pos - from;
            away = away.LengthSquared() < 0.01f ? M.Dir(R.Float() * MathF.Tau) : M.Norm(away);
            p.FleeDir = M.Norm(away + M.Perp(away) * R.Range(-0.35f, 0.35f));
            p.Flee = R.Range(min, max);
            if (!screamed && max > 2) { screamed = true; Events.Add(new CityEvent(CityEv.Scream, p.Pos, 1)); }
        }
    }

    /// <summary>Shots in the street: the pavement empties.</summary>
    public void Gunfire(V2 at) => Scatter(at, Tables.GunfireScatter, 2.5f, 4f);

    // ---- pedestrians --------------------------------------------------------------

    private void SpawnPed()
    {
        for (int tries = 0; tries < 50; tries++)
        {
            int x = R.Int(City.LandW), y = R.Int(City.LandH);
            if (City[x, y] != CTile.Sidewalk) continue;
            var p = new Ped { Pos = new V2(x + R.Range(0.2f, 0.8f), y + R.Range(0.2f, 0.8f)), Speed = R.Range(0.9f, 1.6f), Colour = R.Pick(PedColours), Phase = R.Float() * 10 };
            p.Dir = R.Pick(Pt.Dirs4);
            Peds.Add(p);
            return;
        }
    }

    private void StepPed(Ped p, float dt)
    {
        p.Phase += dt * p.Speed * 4;
        if (p.Down > 0) { p.Down -= dt; return; }
        Car car = Player;
        float carSpeed = car.Vel.Length();
        float dist = V2.Distance(p.Pos, car.Pos);

        // Struck.
        if (carSpeed > 2 && (V2.Distance(p.Pos, car.Front) < car.R + 0.2f || V2.Distance(p.Pos, car.Back) < car.R + 0.2f))
        {
            p.Down = 3f;
            p.Pos += M.Norm(p.Pos - car.Pos + car.Vel * 0.05f) * 0.6f;
            Events.Add(new CityEvent(CityEv.Knock, p.Pos, carSpeed));
            AddHeat(0.6f, "Hit-and-run reported.");
            return;
        }
        // Dive out of the way.
        if (p.Flee <= 0 && carSpeed > 2.5f && dist < 4.5f && V2.Dot(p.Pos - car.Pos, car.Vel) > 0)
        {
            V2 side = M.Perp(M.Norm(car.Vel));
            p.FleeDir = V2.Dot(p.Pos - car.Pos, side) >= 0 ? side : -side;
            p.Flee = 0.7f;
        }
        V2 move;
        float speed = p.Speed;
        if (p.Flee > 0)
        {
            p.Flee -= dt;
            move = p.FleeDir;
            speed = 4.5f;
        }
        else
        {
            move = new V2(p.Dir.X, p.Dir.Y);
            V2 next = p.Pos + move * 0.6f;
            CTile ahead = City[(int)MathF.Floor(next.X), (int)MathF.Floor(next.Y)];
            CTile here = City[(int)MathF.Floor(p.Pos.X), (int)MathF.Floor(p.Pos.Y)];
            bool onWalk = here == CTile.Sidewalk;
            if ((onWalk && ahead != CTile.Sidewalk) || City.Solid((int)MathF.Floor(next.X), (int)MathF.Floor(next.Y)) || R.Chance(0.002f))
            {
                List<Pt> opts = _pedOpts;
                opts.Clear();
                foreach (Pt d2 in Pt.Dirs4)
                {
                    V2 n2 = p.Pos + new V2(d2.X, d2.Y) * 0.6f;
                    CTile t2 = City[(int)MathF.Floor(n2.X), (int)MathF.Floor(n2.Y)];
                    if (onWalk ? t2 == CTile.Sidewalk : !City.Solid((int)MathF.Floor(n2.X), (int)MathF.Floor(n2.Y))) opts.Add(d2);
                }
                if (opts.Count > 1) opts.Remove(new Pt(-p.Dir.X, -p.Dir.Y));
                p.Dir = opts.Count > 0 ? R.Pick(opts) : new Pt(-p.Dir.X, -p.Dir.Y);
                move = new V2(p.Dir.X, p.Dir.Y);
            }
        }
        V2 np = p.Pos + move * speed * dt;
        if (!City.Solid((int)MathF.Floor(np.X), (int)MathF.Floor(np.Y))) p.Pos = np;
    }
}

public static class Collide
{
    /// <summary>
    /// Push a circle out of every solid tile it overlaps. Returns whether it
    /// touched anything, with the average push direction.
    /// </summary>
    public static bool PushOut(ref V2 p, float r, Func<int, int, bool> solid, out V2 normal)
    {
        normal = V2.Zero;
        bool any = false;
        int x0 = (int)MathF.Floor(p.X - r), x1 = (int)MathF.Floor(p.X + r);
        int y0 = (int)MathF.Floor(p.Y - r), y1 = (int)MathF.Floor(p.Y + r);
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            if (!solid(x, y)) continue;
            V2 closest = new(M.Clamp(p.X, x, x + 1), M.Clamp(p.Y, y, y + 1));
            V2 d = p - closest;
            float dist = d.Length();
            if (dist >= r) continue;
            V2 n;
            float push;
            if (dist < 1e-5f)
            {
                // Centre inside the tile: out along the shallowest side.
                float l = p.X - x, rr = x + 1 - p.X, u = p.Y - y, dd = y + 1 - p.Y;
                float m = MathF.Min(MathF.Min(l, rr), MathF.Min(u, dd));
                n = m == l ? new V2(-1, 0) : m == rr ? new V2(1, 0) : m == u ? new V2(0, -1) : new V2(0, 1);
                push = m + r;
            }
            else
            {
                n = d / dist;
                push = r - dist;
            }
            p += n * push;
            normal += n;
            any = true;
        }
        if (any) normal = M.Norm(normal);
        return any;
    }
}
