using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

public enum ChaserKind { Police, Gang, Racer }

/// <summary>A car that wants you: Lone Star with a warrant, or gangers with a grudge.</summary>
public sealed class Chaser
{
    public ChaserKind Kind;
    public Car Car;
    public DriveAutopilot Route;
    public float ReplanT, ShootT, LostT, BustT, BackoffT;
    /// <summary>Blinded by your smoke, or spinning on your oil.</summary>
    public float BlindT, SpinT;
    public int BackoffSteer;
    public bool Gone;
}

/// <summary>
/// Trouble on the streets. <b>Heat</b> (0-5 stars) rises from loud runs,
/// hit-and-runs and ramming police, and falls while no patrol is near you;
/// sleeping it off clears it. Each star puts a Lone Star car on your tail --
/// stop next to one for two seconds and you are busted (a fine). Gangers
/// ambush you after rich scores and on courier jobs, and shoot at your car.
/// Courier jobs are pure driving: collect a package, deliver it before the
/// clock runs out.
/// </summary>
public sealed partial class CitySim
{
    public readonly List<Chaser> Chasers = new();
    private float _policeT = 25, _sirenT;

    public static readonly VehicleDef PoliceCar = new() { Id = "police", Name = "Lone Star cruiser", Top = 13, Accel = 8, Grip = 0.9f, Hp = 90, Colour = 0xe8e8f0ff };
    public static readonly VehicleDef GangBike = new() { Id = "gangbike", Name = "Viper bike", Top = 14.5f, Accel = 10, Grip = 0.88f, Hp = 45, Colour = 0x3a5a1aff, Bike = true, Length = 1.3f, Width = 0.5f };
    public static readonly VehicleDef GangCar = new() { Id = "gang", Name = "Viper ride", Top = 12.5f, Accel = 8, Grip = 0.86f, Hp = 70, Colour = 0x2a3a1aff };

    public int Stars => (int)MathF.Floor(C.Heat);

    public void AddHeat(float amount, string why = null)
    {
        int before = Stars;
        C.Heat = MathF.Min(5, C.Heat + amount);
        C.Max("maxheat", Stars);
        if (Stars > before && why != null) Events.Add(new CityEvent(CityEv.Message, Player.Pos, Stars, $"{why} Heat {Stars}."));
    }

    private void OnTrafficHit(float into)
    {
        if (into > 6) AddHeat(0.25f, "Reckless driving reported.");
    }

    /// <summary>A loud exit: Lone Star is already outside, n cars, and you have the heat to match.</summary>
    public void Getaway(int n)
    {
        C.Heat = MathF.Max(C.Heat, n + 0.5f);
        for (int k = 0; k < n; k++) Spawn(ChaserKind.Police);
        Events.Add(new CityEvent(CityEv.Siren, Player.Pos, 1, "Sirens -- Lone Star got there while you were inside. Drive."));
    }

    /// <summary>Gangers who saw the score: n cars, out of sight but closing.</summary>
    public void Ambush(int n)
    {
        C.Count("ambushes");
        for (int k = 0; k < n; k++) Spawn(ChaserKind.Gang);
        Events.Add(new CityEvent(CityEv.Message, Player.Pos, n, "Headlights in the mirror. Somebody followed you from the job."));
    }

    private void Spawn(ChaserKind kind)
    {
        // A junction 22-34 tiles away, so they come from off screen.
        var nodes = new List<V2>();
        for (int i = 0; i < City.RoadX.Count; i++)
        for (int j = 0; j < City.RoadY.Count; j++)
        {
            V2 n = City.Node(i, j);
            float d = V2.Distance(n, Player.Pos);
            if (d > 22 && d < 34) nodes.Add(n);
        }
        if (nodes.Count == 0)
            for (int i = 0; i < City.RoadX.Count; i++)
            for (int j = 0; j < City.RoadY.Count; j++) nodes.Add(City.Node(i, j));
        V2 at = R.Pick(nodes);
        VehicleDef d0 = kind == ChaserKind.Police ? PoliceCar : R.Chance(0.4f) ? GangBike : GangCar;
        var car = new Car { Def = d0, Hp = d0.Hp, Pos = at, Heading = M.Angle(Player.Pos - at) };
        Chasers.Add(new Chaser { Kind = kind, Car = car });
    }

    private void StepPursuit(float dt)
    {
        // Heat: patrols come while it is up, and it cools while none is near.
        int police = Chasers.Count(c => c.Kind == ChaserKind.Police);
        bool near = Chasers.Any(c => c.Kind == ChaserKind.Police && V2.Distance(c.Car.Pos, Player.Pos) < 25);
        float cool = (C.On("stadium") ? 2f : 1f) * (C.On("crackdown") || C.On("council") ? 0.5f : 1f) * (C.CarHas("plates") ? 1.5f : 1f);
        if (!near) C.Heat = MathF.Max(0, C.Heat - dt / 22f * cool);
        _policeT -= dt;
        // On strike: the cars stay in the garage.
        if (_policeT <= 0 && police < Math.Min(4, Stars) && !C.On("strike"))
        {
            _policeT = 7;
            Spawn(ChaserKind.Police);
        }
        if (near && (_sirenT -= dt) <= 0)
        {
            _sirenT = 1.6f;
            Events.Add(new CityEvent(CityEv.Siren, Chasers.First(c => c.Kind == ChaserKind.Police).Car.Pos, 1));
        }
        foreach (Chaser ch in Chasers.ToList()) if (!ch.Gone) { if (ch.Kind == ChaserKind.Racer) StepRacer(ch, dt); else StepChaser(ch, dt); }
        Chasers.RemoveAll(c => c.Gone);
        StepCourier(dt);
    }

    private void StepChaser(Chaser ch, float dt)
    {
        Car car = ch.Car;
        float dist = V2.Distance(car.Pos, Player.Pos);
        bool police = ch.Kind == ChaserKind.Police;
        // Give up when you are long gone, or the heat is off (sooner against ECM, or blinded by smoke).
        if (dist > (C.CarHas("ecm") ? 35 : 45) || ch.BlindT > 0 && dist > 12) ch.LostT += dt * (ch.BlindT > 0 ? 2 : 1); else ch.LostT = 0;
        if (ch.BlindT > 0) ch.BlindT -= dt;
        if (ch.LostT > 6 || police && Stars == 0 || car.Hp <= 0)
        {
            ch.Gone = true;
            if (car.Hp <= 0) Events.Add(new CityEvent(CityEv.Crash, car.Pos, 5));
            else if (!police) Events.Add(new CityEvent(CityEv.Lost, car.Pos, 0, "You lost them."));
            return;
        }
        DriveInput inp;
        if (ch.SpinT > 0)
        {
            // On your oil: wheels locked, the back end gone.
            ch.SpinT -= dt;
            inp = new DriveInput { Throttle = 0, Steer = MathF.Sin(ch.SpinT * 9) > 0 ? 1 : -1, Handbrake = true };
        }
        else if (ch.BackoffT > 0)
        {
            // Just hit you: reverse and peel away before coming again.
            ch.BackoffT -= dt;
            inp = new DriveInput { Throttle = ch.BackoffT > 0.5f ? -1 : 0.6f, Steer = ch.BackoffSteer };
        }
        else if (dist < 14 && ch.BlindT <= 0 && ClearRoad(car.Pos, Player.Pos))
        {
            // In sight: aim at where you are going.
            V2 lead = Player.Pos + Player.Vel * MathF.Min(0.8f, dist / 15);
            float diff = M.AngleDiff(car.Heading, M.Angle(lead - car.Pos));
            inp = new DriveInput { Steer = M.Clamp(diff * 3, -1, 1), Throttle = MathF.Abs(diff) > 1.8f ? -1 : 1 };
            // Close in, then match your speed and box you in rather than ram.
            // Close in, then hold off: police box you in, gangers keep a shooting distance.
            float want = dist < (police ? 5 : 6) ? Player.Vel.Length() + (police ? 0.5f : 0f) : 99;
            if (!police && dist < 3.5f) inp.Throttle = car.Speed > 0.5f ? -1 : 0;
            if (car.Speed > want + 0.5f) inp.Throttle = -1;
            else if (car.Speed > want) inp.Throttle = 0;
            if (police && Stars == 0) inp.Throttle = 0;
        }
        else
        {
            ch.ReplanT -= dt;
            if (ch.Route == null || ch.ReplanT <= 0)
            {
                ch.Route = new DriveAutopilot(City, car.Pos, Player.Pos);
                ch.ReplanT = 1.2f;
            }
            inp = ch.Route.Step(this, car, dt, 12);
        }
        Drive(car, dt, inp, false);

        // Contact with your car.
        V2 pFront = Player.Front, pBack = Player.Back, cFront = car.Front, cBack = car.Back;
        for (int k = 0; k < 4; k++)
        {
            V2 pc = k < 2 ? pFront : pBack, cc = k % 2 == 0 ? cFront : cBack;
            V2 delta = pc - cc;
            float d = delta.Length(), min = Player.R + car.R;
            if (d >= min || d < 1e-4f) continue;
            V2 n = delta / d;
            Player.Pos += n * (min - d) * 0.5f;
            car.Pos -= n * (min - d) * 0.5f;
            if (ch.BackoffT <= 0) { ch.BackoffT = 1.1f; ch.BackoffSteer = V2.Dot(M.Perp(car.Forward), n) > 0 ? -1 : 1; }
            float rel = V2.Dot(car.Vel - Player.Vel, n);
            if (rel > 0)
            {
                Player.Vel += n * rel * 0.6f;
                car.Vel -= n * rel * 0.6f;
                if (rel > 4)
                {
                    Hurt((rel - 4) * 1.5f, CityEv.Bump, pc);
                    car.Hp -= (rel - 4) * 3 * (C.CarHas("ram") ? 2 : 1);
                    // Only your own ramming counts against you.
                    if (police && V2.Dot(Player.Vel, -n) > 4) AddHeat(0.3f, "Assaulting an officer.");
                }
            }
        }

        if (police && Stars > 0 && C.ArcPath("cop") != 1)
        {
            // Pulled over (not if you carry Dace's badge).
            if (dist < 3 && Player.Vel.Length() < 1.5f) ch.BustT += dt; else ch.BustT = MathF.Max(0, ch.BustT - dt);
            if (ch.BustT > 2) Bust();
        }
        else if (!police && dist < 10 && ch.BlindT <= 0 && ch.SpinT <= 0 && ClearRoad(car.Pos, Player.Pos))
        {
            ch.ShootT -= dt;
            if (ch.ShootT <= 0)
            {
                ch.ShootT = R.Range(0.25f, 0.5f);
                Events.Add(new CityEvent(CityEv.Gunfire, car.Pos, 0, null));
                Gunfire(car.Pos);
                if (R.Chance(0.55f)) Hurt(2.5f, CityEv.Gunfire, Player.Pos + new V2(R.Range(-0.4f, 0.4f), R.Range(-0.4f, 0.4f)));
            }
        }
    }

    private void Bust()
    {
        int fine = Math.Min(C.Cash * 2 / 5, 200 + C.Cash / 20 + (int)(150 * C.Heat));
        if (C.On("council")) fine = Math.Min(C.Cash * 3 / 5, fine * 2);
        if (C.CarHas("smuggle")) fine /= 2;
        C.Count("busts");
        C.Count("spent", fine);
        C.Say("Busted by Lone Star.");
        C.Cash -= fine;
        C.Heat = 0;
        foreach (Chaser c in Chasers) if (c.Kind == ChaserKind.Police) c.Gone = true;
        Player.Vel = V2.Zero;
        Events.Add(new CityEvent(CityEv.Busted, Player.Pos, fine, $"Busted. Lone Star wrote you a fine of {M.Money(fine)} and let you go."));
        if (C.Courier && !C.CarHas("smuggle") && C.Active?.Type != JobType.Race)
        {
            C.Courier = false;
            Job j = C.Active;
            if (j != null) Events.Add(new CityEvent(CityEv.Message, Player.Pos, 0, "They impounded the package."));
            CourierFailed = j;
        }
    }

    /// <summary>No building between two points on the street.</summary>
    public bool ClearRoad(V2 a, V2 b)
    {
        var w = new Level.Walker(a, b);
        while (w.Next(out int x, out int y))
            if (City.Solid(x, y)) return false;
        return true;
    }

    // ---- Torque's gadgets ---------------------------------------------------------------

    public float BoostT;
    public int Nitro = 3, SmokeLeft = 3, OilLeft = 3;
    public readonly List<(V2 pos, float t)> OilPatches = new();
    public readonly List<(V2 pos, float t)> SmokeClouds = new();
    private float _gunT, _nanoT;

    private void StepGadgets(float dt, DriveInput inp)
    {
        if (BoostT > 0) BoostT -= dt;
        else if (inp.Boost && Nitro > 0 && C.CarHas("nitro")) { Nitro--; BoostT = 3; Events.Add(new CityEvent(CityEv.Message, Player.Pos, 0, $"NITRO ({Nitro} left)")); }
        if (inp.Smoke && SmokeLeft > 0 && C.CarHas("smoke"))
        {
            SmokeLeft--;
            SmokeClouds.Add((Player.Pos - Player.Forward * 2, 6));
            foreach (Chaser ch in Chasers.Where(c => V2.Distance(c.Car.Pos, Player.Pos) < 22 && V2.Dot(c.Car.Pos - Player.Pos, Player.Forward) < 0)) ch.BlindT = 5;
            Events.Add(new CityEvent(CityEv.Message, Player.Pos, 0, $"Smoke screen ({SmokeLeft} left)."));
        }
        if (inp.Oil && OilLeft > 0 && C.CarHas("oil"))
        {
            OilLeft--;
            OilPatches.Add((Player.Pos - Player.Forward * 2.2f, 12));
            Events.Add(new CityEvent(CityEv.Message, Player.Pos, 0, $"Oil slick ({OilLeft} left)."));
        }
        for (int i = OilPatches.Count - 1; i >= 0; i--)
        {
            var (pos, t) = OilPatches[i];
            if (t - dt <= 0) { OilPatches.RemoveAt(i); continue; }
            OilPatches[i] = (pos, t - dt);
            foreach (Chaser ch in Chasers) if (ch.SpinT <= 0 && V2.Distance(ch.Car.Pos, pos) < 1.6f) { ch.SpinT = 1.8f; ch.Car.Vel *= 0.6f; }
            foreach (TrafficCar tc in Traffic) if (tc.Bumped <= 0 && V2.Distance(tc.Pos, pos) < 1.4f) tc.Bumped = 1.2f;
        }
        for (int i = SmokeClouds.Count - 1; i >= 0; i--) { var (pos, t) = SmokeClouds[i]; if (t - dt <= 0) SmokeClouds.RemoveAt(i); else SmokeClouds[i] = (pos, t - dt); }
        // The gun mount: twin SMGs at whoever is in front of you.
        if (C.CarHas("guns") && (_gunT -= dt) <= 0)
        {
            Chaser target = Chasers.Where(c => c.Kind != ChaserKind.Racer && V2.Distance(c.Car.Pos, Player.Pos) < 11 && V2.Dot(M.Norm(c.Car.Pos - Player.Pos), Player.Forward) > 0.85f && ClearRoad(Player.Pos, c.Car.Pos))
                .OrderBy(c => V2.Distance(c.Car.Pos, Player.Pos)).FirstOrDefault();
            if (target != null)
            {
                _gunT = 0.2f;
                target.Car.Hp -= 3.5f;
                Events.Add(new CityEvent(CityEv.Gunfire, Player.Pos, 0));
                Gunfire(Player.Pos);
                // Shooting at Lone Star is a crime of its own.
                if (target.Kind == ChaserKind.Police) AddHeat(0.05f);
            }
        }
        if (C.CarHas("nano") && (_nanoT -= dt) <= 0 && Player.Hp < Player.Def.Hp)
        {
            _nanoT = 3;
            Player.Hp = MathF.Min(Player.Def.Hp, Player.Hp + 1);
            C.CarHp = (int)MathF.Ceiling(Player.Hp);
        }
    }

    // ---- courier jobs ---------------------------------------------------------------------

    /// <summary>Set when a courier job fails in the city (out of time, impounded, wrecked); the screen settles it.</summary>
    public Job CourierFailed;

    /// <summary>The lot a courier job needs you at next: the pickup, then the drop.</summary>
    public Lot CourierLot(Job j) => City.Lots[C.Courier ? j.Lot2 : j.Lot];

    public static readonly VehicleDef RacerCar = new() { Id = "racer", Name = "Viper hotshot", Top = 13.5f, Accel = 9.5f, Grip = 0.92f, Hp = 120, Colour = 0xd8ff30ff };

    /// <summary>The rival in a street race, if one is out there.</summary>
    public Chaser Racer => Chasers.FirstOrDefault(c => c.Kind == ChaserKind.Racer && !c.Gone);

    /// <summary>Lights, and go: the rival pulls up beside you and you both race for the finish.</summary>
    public void StartRace(Job j)
    {
        C.Courier = true;
        C.CourierT = 9999;
        V2 at = Player.Pos + Player.Right * 1.4f;
        if (City.Solid((int)at.X, (int)at.Y)) at = Player.Pos - Player.Right * 1.4f;
        VehicleDef d = RacerCar;
        var car = new Car { Def = d, Hp = d.Hp, Pos = at, Heading = Player.Heading };
        Lot finish = City.Lots[j.Lot2];
        Chasers.Add(new Chaser { Kind = ChaserKind.Racer, Car = car, Route = new DriveAutopilot(City, at, City.Kerb(finish).pos) });
        Events.Add(new CityEvent(CityEv.Pickup, Player.Pos, 0, "Three... two... GO! First to the finish."));
    }

    private void StepRacer(Chaser ch, float dt)
    {
        Job j = C.Active;
        if (j == null || j.Type != JobType.Race || !C.Courier) { ch.Gone = true; return; }
        Car car = ch.Car;
        // Faster with every skull on the job.
        DriveInput inp = ch.Route.Step(this, car, dt, 10 + j.Threat * 0.6f);
        Drive(car, dt, inp, false);
        if (car.Hp <= 0)
        {
            ch.Gone = true;
            Events.Add(new CityEvent(CityEv.Crash, car.Pos, 5, "The Viper hotshot wraps his car round a lamppost. You just have to finish."));
            return;
        }
        if (V2.Distance(car.Pos, City.Kerb(City.Lots[j.Lot2]).pos) < 3)
        {
            ch.Gone = true;
            C.Courier = false;
            CourierFailed = j;
            Events.Add(new CityEvent(CityEv.Message, car.Pos, 0, "The Viper hotshot crosses the line first. Lost the race."));
        }
    }

    public void CollectPackage(Job j)
    {
        C.Courier = true;
        C.CourierT = j.TimeLimit * (Player.Def.Bike ? 1.2f : 1f);
        Events.Add(new CityEvent(CityEv.Pickup, Player.Pos, 0, $"Package aboard. {j.TimeLimit:0} seconds to the drop."));
        int gangs = 1 + (j.Threat >= 3 ? 1 : 0) + (j.Threat >= 5 ? 1 : 0);
        // In Viper colours, the Vipers let the package go by.
        if (j.Faction == Faction.Vipers && C.HasFactionItem("colours")) gangs = 0;
        for (int k = 0; k < gangs; k++) Spawn(ChaserKind.Gang);
    }

    private void StepCourier(float dt)
    {
        if (!C.Courier) return;
        C.CourierT -= dt;
        if (C.CourierT <= 0 || Wrecked)
        {
            C.Courier = false;
            CourierFailed = C.Active;
            Events.Add(new CityEvent(CityEv.Message, Player.Pos, 0, Wrecked ? "The package went up with the car." : "Too late. The client walked."));
        }
    }
}
