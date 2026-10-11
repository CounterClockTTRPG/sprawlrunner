using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>The city's own streets: lanes through blocks, yards, waste ground, the causeway; the happenings on the street; street races.</summary>
public partial class SmokeTest
{
    private static Career StreetCareer(City city, params (string id, string district)[] on)
    {
        Career c = TestCareer();
        c.CityRef = city;
        c.Day = 5;
        foreach (var (id, district) in on) c.Happenings.Add(new Happening { Id = id, Start = c.Day, End = c.Day, District = district });
        return c;
    }

    private void CityStreets()
    {
        // ---- generation ----
        foreach (ulong seed in new ulong[] { 1, 7, 777, 4242 })
        {
            City city = City.Generate(seed);
            Check(city.Alleys.Count >= 6, $"{seed}: lanes are cut through blocks ({city.Alleys.Count})");
            Check(city.Alleys.All(a => a.W == 2 || a.H == 2), $"{seed}: every lane is two tiles wide");
            // Each lane runs avenue to avenue: road at both mouths.
            bool mouths = city.Alleys.All(a => a.W == 2
                ? city[a.X, a.Y - 1] == CTile.Road && city[a.X, a.Bottom] == CTile.Road
                : city[a.X - 1, a.Y] == CTile.Road && city[a.Right, a.Y] == CTile.Road);
            Check(mouths, $"{seed}: every lane opens onto an avenue at both ends");
            Check(city.Lots.All(l => city.Alleys.All(a => !Overlap(a, l.R))), $"{seed}: no lot is left inside a lane");
            Check(city.Lots.Where(l => l.Door.X >= 0).All(l => city[l.Door.X, l.Door.Y] == CTile.Sidewalk), $"{seed}: every door still opens onto the pavement");
            foreach (string place in new[] { "home", "fixer", "doc", "market", "bazaar" })
            {
                Lot l = city.Place(place);
                V2 k = city.Kerb(l).pos;
                Check(!city.Solid((int)k.X, (int)k.Y), $"{seed}: {place}'s kerb is open road");
            }
            Check(city.Lots.Any(l => l.District == "docks" && l.Kind == LotKind.Yard), $"{seed}: container yards at the Docks");
            Check(city.Potholes.Count > 10 && city.Potholes.All(p => city.T[p] == CTile.Road && city.DistrictAt[p] == "barrens"), $"{seed}: potholes, only in the Barrens' roads ({city.Potholes.Count})");
            Check(city.Ramps.Count >= 3, $"{seed}: the causeway has ramps ({city.Ramps.Count})");
            Check(city.Ramps.All(r => r.H > r.W ? city[r.X, r.Y - 1] == CTile.Road : city[r.X - 1, r.Y] == CTile.Road), $"{seed}: every ramp starts from an avenue");
            Check(city.CausewaySouth.W > 100 && city.CausewayEast.H > 100, $"{seed}: the causeway runs along both shores");
            bool walled = true;
            for (int x = city.CausewaySouth.X; x < city.CausewaySouth.Right; x++) walled &= city[x, city.CausewaySouth.Bottom] == CTile.Barrier;
            Check(walled, $"{seed}: a barrier all along the causeway's seaward side");
        }
        Eq(City.Generate(99).Alleys.Count, City.Generate(99).Alleys.Count, "the same seed cuts the same lanes");

        // ---- surfaces ----
        City c0 = City.Generate(777);
        VehicleDef sedan = Tables.Vehicles.First(v => !v.Bike && !v.Offroad);
        Check(CitySim.Surface(CTile.Dirt, sedan) < 1 && CitySim.Surface(CTile.Highway, sedan) > 1, "dirt slows a road car; the causeway is quicker than an avenue");
        VehicleDef scrambler = Tables.Vehicles.FirstOrDefault(v => v.Offroad);
        if (scrambler != null) Eq(CitySim.Surface(CTile.Dirt, scrambler), 1f, "dirt is nothing to a scrambler");
        // Flat out on the causeway beats flat out on an avenue.
        float Flat(V2 at, float heading)
        {
            var s = new CitySim(c0, StreetCareer(c0), 4);
            s.Traffic.Clear();
            s.Peds.Clear();
            s.Player.Pos = at;
            s.Player.Heading = heading;
            for (int i = 0; i < 60 * 4; i++) s.Step(1 / 60f, new DriveInput { Throttle = 1 });
            return s.Player.Speed;
        }
        float deck = Flat(new V2(c0.CausewaySouth.X + 20, c0.CausewaySouth.Y + 2.5f), 0);
        float ave = Flat(new V2(c0.RoadX[0] + 20, c0.RoadY[1] + 2.5f), 0);
        Check(deck > ave + 0.5f, $"the causeway is fast ({deck:0.0} vs {ave:0.0} tiles/s)");
        // A pothole at speed jolts the car.
        int hole = c0.Potholes.First();
        var ps = new CitySim(c0, StreetCareer(c0), 5);
        ps.Traffic.Clear();
        ps.Player.Def = sedan;
        ps.Player.Pos = new V2(hole % City.W + 0.5f, hole / City.W + 0.5f) - new V2(1.5f, 0);
        ps.Player.Heading = 0;
        ps.Player.Vel = new V2(10, 0);
        float hp = ps.Player.Hp;
        bool jolted = false;
        for (int i = 0; i < 20; i++) { ps.Step(1 / 60f, new DriveInput { Throttle = 1 }); jolted |= ps.Events.Any(e => e.Kind == CityEv.Pothole); }
        Check(jolted && ps.Player.Hp < hp, "a pothole at speed jolts and dents the car");
        // A car saved where a container yard now stands goes home.
        Lot yard = c0.Lots.First(l => l.Kind == LotKind.Yard && Enumerable.Range(0, l.R.W).Any(dx => c0[l.R.X + dx, l.R.Y + 1] == CTile.Container));
        Career sc = StreetCareer(c0);
        int cx = Enumerable.Range(0, yard.R.W).First(dx => c0[yard.R.X + dx, yard.R.Y + 1] == CTile.Container) + yard.R.X;
        sc.CarX = cx + 0.5f;
        sc.CarY = yard.R.Y + 1.5f;
        var moved = new CitySim(c0, sc, 6);
        Check(!c0.Solid((int)moved.Player.Pos.X, (int)moved.Player.Pos.Y), "a car saved inside what is now a container stack is towed home");
        Check(c0.Narrow(c0.Alleys[0].X, c0.Alleys[0].Y) && !c0.Solid(c0.Alleys[0].X, c0.Alleys[0].Y), "a lane is open to you, narrow for a cruiser");

        // ---- the lantern festival ----
        Career fc = StreetCareer(c0, ("lanterns", null));
        var fs = new CitySim(c0, fc, 7);
        fs.Step(1 / 60f, default);
        Check(fs.Festival.Count >= 2 && fs.FestivalBarriers.Count > 0, $"the festival closes the blocks round the market ({fs.Festival.Count})");
        Check(fs.Festival.All(f => fs.Closed(f.a, f.b)), "and planned routes go round them");
        Check(fs.FestivalBarriers.All(t => fs.CarSolid(t.X, t.Y)), "its barriers stop a car");
        fs.Player.Pos = new V2(-50, -50);
        for (int i = 0; i < 60 * 20; i++) fs.Step(1 / 60f, default);
        int inside = fs.Traffic.Count(t => fs.FestivalTiles.Any(p => (int)t.Pos.X == p.X && (int)t.Pos.Y == p.Y));
        Check(inside == 0, $"traffic keeps out of the festival ({inside} inside)");
        // You can still drive to the market's door.
        var fd = new CitySim(c0, StreetCareer(c0, ("lanterns", null)), 8);
        fd.Step(1 / 60f, default);
        fd.Traffic.Clear();
        fd.Peds.Clear();
        Lot market = c0.Place("market");
        fd.Player.Pos = c0.Kerb(c0.Place("fixer")).pos;
        DriveAutopilot ap = fd.RouteTo(market);
        for (int i = 0; i < 60 * 150 && !ap.Done; i++) fd.Step(1 / 60f, ap.Step(fd, 1 / 60f));
        Check(fd.AtDoor() == market, "during the festival, the market's door can still be driven to");

        // ---- the gang war ----
        var ws = new CitySim(c0, StreetCareer(c0, ("gangwar", null)), 9);
        ws.Step(1 / 60f, default);
        Check(ws.Wrecks.Count >= 2, $"the gang war leaves wrecks in the road ({ws.Wrecks.Count})");
        Check(ws.Wrecks.All(w => c0.DistrictAt[(int)w.At.Y * City.W + (int)w.At.X] is "barrens" or "docks"), "in the Barrens and the Docks");
        Check(ws.Wrecks.All(w => w.Tiles.All(t => ws.CarSolid(t.X, t.Y))), "a burning wreck is solid");
        Check(ws.Wrecks.All(w => w.Tiles.Count == 4), "and fills only half the road");
        var calm = new CitySim(c0, StreetCareer(c0), 9);
        calm.Step(1 / 60f, default);
        Check(calm.Wrecks.Count == 0 && calm.Festival.Count == 0, "on a quiet day the street is clear");

        // ---- stadium night ----
        Career stc = StreetCareer(c0, ("stadium", null));
        var st = new CitySim(c0, stc, 10);
        int before = st.Traffic.Count;
        st.Step(1 / 60f, default);
        Check(st.Traffic.Count(t => t.Event) >= Tables.StadiumCars - 2 && st.Peds.Count(p => p.Fan) > 10, $"stadium night: traffic and fans round the stadium ({st.Traffic.Count(t => t.Event)} cars)");
        stc.Happenings.Clear();
        stc.Day++;
        st.Player.Pos = new V2(-80, -80);
        for (int i = 0; i < 60 * 2; i++) st.Step(1 / 60f, default);
        Check(st.Traffic.Count(t => t.Event) == 0 && st.Traffic.Count == before, "the next day they've gone home (out of sight)");

        // ---- the motorcade ----
        Career mc = StreetCareer(c0, ("election", null));
        var ms = new CitySim(c0, mc, 11);
        ms.Step(1 / 60f, default);
        var cade = ms.Traffic.Where(t => t.Motorcade).ToList();
        Check(cade.Count == 3 && cade.Count(t => t.Kind == CarKind.Limo) == 1 && cade.Count(t => t.Follow != null) == 2, "election week: a limo and two cruisers");
        TrafficCar limo = cade.First(t => t.Kind == CarKind.Limo);
        // Round town for a while: the escort keeps to the limo.
        ms.Player.Pos = new V2(-80, -80);
        float worst = 0;
        for (int i = 0; i < 60 * 40; i++)
        {
            ms.Step(1 / 60f, default);
            if (i > 60 * 5 && i % 30 == 0) worst = MathF.Max(worst, cade.Where(t => t.Follow != null).Max(t => V2.Distance(t.Pos, limo.Pos)));
        }
        Check(!limo.Gone && worst < 16, $"the escort keeps to the limo round town (at worst {worst:0} tiles off)");
        ms.Player.Pos = limo.Pos - limo.Forward * 3;
        ms.Player.Heading = limo.Heading;
        ms.Player.Vel = limo.Forward * 12;
        float heat = mc.Heat;
        for (int i = 0; i < 30; i++) ms.Step(1 / 60f, new DriveInput { Throttle = 1 });
        Check(mc.Heat >= heat + Tables.MotorcadeHeat - 0.1f && mc.Flag("motorcade:hit") > 0, $"ramming the motorcade brings the heat ({mc.Heat:0.0})");

        // ---- grid failure: the district's lamps go dark (the view reads this) ----
        Career gc = StreetCareer(c0, ("grid", "barrens"));
        Check(gc.On("grid", "barrens") && !gc.On("grid", "spire"), "a grid failure is one district's");

        // ---- what the street sets going ----
        Career rc = StreetCareer(c0);
        var rs = new CitySim(c0, rc, 12);
        rs.Traffic.Clear();
        rs.Player.Pos = c0.Kerb(c0.Place("fixer")).pos;
        rc.Set("street:race", 500);
        rs.AfterStreet();
        Check(rs.Race != null && rs.Race.Rival != null && rs.Chasers.Contains(rs.Race.Rival), "the race is on, the hotshot beside you");
        Check(V2.Distance(rs.Race.Finish, rs.Player.Pos) > 25, $"to a landmark across town ({V2.Distance(rs.Race.Finish, rs.Player.Pos):0} tiles)");
        int cash = rc.Cash;
        rs.Player.Pos = rs.Race.Finish;
        rs.Step(1 / 60f, default);
        Check(rs.Race == null && rc.Cash == cash + 1000, "first over the line takes the pot (twice the stake)");
        // The hotshot gets there first: the stake is gone.
        rc.Set("street:race", 500);
        rs.Player.Pos = c0.Kerb(c0.Place("fixer")).pos;
        rs.AfterStreet();
        rs.Race.Rival.Car.Pos = rs.Race.Finish;
        cash = rc.Cash;
        rs.Step(1 / 60f, default);
        Check(rs.Race == null && rc.Cash == cash, "second over the line: nothing back");
        Check(!rs.Chasers.Any(ch => ch.Kind == ChaserKind.Racer), "and he drives off");
        rc.Set("street:cruiser");
        rs.AfterStreet();
        Check(rs.Chasers.Any(ch => ch.Kind == ChaserKind.Police), "running a stop puts a cruiser behind you");
        // The scene dented the car: the street sees it.
        rc.CarHp = 10;
        rs.AfterStreet();
        Check(rs.Player.Hp <= 10, "damage done in a street scene shows on the car");

        // Each new encounter only where it belongs.
        Career here = StreetCareer(c0);
        here.Here = "spire";
        Check(!global::Sprawl.Storylets.ById("street-ghoul").When(here) && global::Sprawl.Storylets.ById("street-van").When(here), "ghouls in the Barrens, cash vans uptown");
        here.Here = "barrens";
        Check(global::Sprawl.Storylets.ById("street-ghoul").When(here), "and a ghoul in the Barrens");
    }

    private static bool Overlap(Rect a, Rect b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
}
