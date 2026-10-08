using System;
using System.Linq;
using Godot;

namespace Sprawl;

public partial class SmokeTest
{
    /// <summary>Walking away: only with a story to end on (a campaign won, or enough saved), never twice.</summary>
    private void Retiring()
    {
        Career c = TestCareer();
        Check(c.RetireReason() == null, "a new runner has nothing to retire on");
        c.Cash = Tables.RetireSavings;
        Check(c.RetireReason()?.Contains("put away") == true, $"enough saved is a reason to stop ({c.RetireReason()})");
        Check(!c.RetiresALegend, "savings alone retire you quietly, not a legend");
        c.Cash = 100;
        c.Set("tide:won", 30);
        Check(c.RetireReason()?.Contains("Toxic Tide") == true && c.RetiresALegend, "a finished campaign is a story to end on, and a legend's");
        c.Retired = true;
        Check(c.RetireReason() == null, "a retired runner can't retire again");
    }

    /// <summary>The street from the car: an encounter turns up ahead on a drive, its choices all work, a fare pays at its kerb.</summary>
    private void StreetEncounters()
    {
        City city = City.Generate(777);
        Career c = TestCareer();
        c.CityRef = city;
        c.Day = 3;
        var sim = new CitySim(city, c, 77) { Encounters = true };
        // Drive across town until the street has something.
        string[] round = { "rigger", "arms", "decker", "fence", "doc", "fixer" };
        float drove = 0;
        for (int leg = 0; leg < round.Length * 2 && sim.Street == null && drove < 300; leg++)
        {
            var ap = new DriveAutopilot(city, sim.Player.Pos, city.Place(round[leg % round.Length]));
            for (int i = 0; i < 60 * 120 && sim.Street == null && !ap.Done; i++) { sim.Step(1 / 60f, ap.Step(sim, 1 / 60f)); drove += 1 / 60f; }
        }
        Check(sim.Street != null, $"driving long enough, something turns up on the street ({drove:0} s)");
        if (sim.Street != null)
        {
            Check(V2.Distance(sim.Street.At, sim.Player.Pos) is > 8 and < 32, $"it waits ahead, not on top of you ({V2.Distance(sim.Street.At, sim.Player.Pos):0} tiles)");
            Check(city[(int)sim.Street.At.X, (int)sim.Street.At.Y] is CTile.Sidewalk or CTile.Road, "on the pavement or the road");
        }
        var other = new CitySim(city, TestCareer(), 1);
        for (int i = 0; i < 60 * 120; i++) other.Step(1 / 60f, new DriveInput { Throttle = 1 });
        Check(other.Street == null, "no encounters unless asked for (the title's camera, the checks)");

        // The toll only on Viper streets.
        var docks = Sprawl.Storylets.ById("street-toll");
        c.Here = Tables.Districts.First(d => c.Holder(d.Id) == Faction.Vipers).Id;
        Check(docks.When(c), "a Viper toll on Viper streets");
        c.Here = Tables.Districts.First(d => c.Holder(d.Id) != Faction.Vipers).Id;
        Check(!docks.When(c), "and nowhere else");

        // Every encounter: each choice runs, and closing one is driving on.
        foreach (Storylet s in Sprawl.Storylets.All.Where(x => x.Hook == Hook.Drive))
        {
            Check(s.Place?.StartsWith("street:") == true && System.Enum.TryParse<StreetKind>(s.Place[7..], true, out _), $"{s.Id} says what it looks like");
            for (int k = 0; k < s.Choices.Length; k++)
            {
                Career t = TestCareer();
                t.CityRef = city;
                t.Cash = 5000;
                t.Etiquettes.Add("Gang");
                Mail m = t.Send(s);
                string err = t.Choose(m, k);
                Check(err == null && m.Reply != null, $"{s.Id}: '{s.Choices[k].Text}' runs ({err ?? m.Reply})");
            }
            Check(s.Choices[^1].Text is "Drive on" or "Floor it", $"{s.Id}: the last choice drives on");
        }

        // A fare: aboard, then stopped at the kerb, it pays; overnight it lapses.
        Career f = TestCareer();
        f.CityRef = city;
        Lot doc = city.Place("doc");
        f.TakeAboard(doc.Index, 600, "a box", "Delivered.", karma: 1);
        var fs = new CitySim(city, f, 3);
        fs.Player.Pos = city.Kerb(doc).pos;
        fs.Player.Vel = V2.Zero;
        int cash = f.Cash;
        fs.Step(1 / 60f, default);
        Check(f.Passenger == null && f.Cash == cash + 600 && fs.Events.Any(e => e.Kind == CityEv.Pickup && e.Text.Contains("Delivered")), "a fare pays at its kerb");
        f.TakeAboard(doc.Index, 600, "a box", "Delivered.");
        f.NextDay(city);
        Check(f.Passenger == null, "a fare lapses overnight");
    }

    /// <summary>Light: lamps light their rooms (walls cast shadows), the dark hides you, and lamps can be shot out or blown.</summary>
    private void Lighting()
    {
        // Two rooms side by side; one lamp in the left one.
        string[] rows =
        {
            "#####################",
            "#P........#.........#",
            "#.........#.........#",
            "#...................#",
            "#.........#.........#",
            "#####################",
        };
        LevelPlan p = Site(rows);
        p.L.Ambient = Tables.NightAmbient;
        p.L.Lights.Add((new V2(5.5f, 2.5f), 0xffe0a0ff, 6));
        Level L = p.L;
        Check(L.LightAt(new V2(5.5f, 2.5f)) > 0.9f, "under the lamp it's bright");
        Check(L.LightAt(new V2(15.5f, 1.5f)) <= Tables.NightAmbient + 0.01f, $"through the wall, only the night ({L.LightAt(new V2(15.5f, 1.5f)):0.00})");
        Check(L.LightAt(new V2(10.5f, 3.5f)) > Tables.NightAmbient + 0.05f, "light spills into the doorway");

        // Seen in the light vs in the dark: the same guard, the same distance, two runs.
        float Detect(V2 at)
        {
            LevelPlan q = Site(rows);
            q.L.Ambient = Tables.NightAmbient;
            q.L.Lights.Add((new V2(5.5f, 2.5f), 0xffe0a0ff, 6));
            Run r = Make(q);
            r.Player.Pos = at;
            Actor g = r.AddEnemy(EnemyKind.Guard, at + new V2(4, 0), MathF.PI);
            g.BaseFacing = MathF.PI;
            Steps(r, 0.4f, new RunInput { Slot = -1, Aim = g.Pos });
            return g.Detect;
        }
        float lit = Detect(new V2(4.5f, 2.5f)), dark = Detect(new V2(12.5f, 1.5f));
        Check(lit > 0 && dark > 0 && lit > dark * 1.5f, $"you're noticed much faster under a lamp than in the dark ({lit:0.00} vs {dark:0.00})");

        // Shoot it out: aim at the lamp and fire.
        Run s = Make(Site(rows));
        s.L.Ambient = Tables.NightAmbient;
        s.L.Lights.Add((new V2(5.5f, 2.5f), 0xffe0a0ff, 6));
        bool popped = false;
        for (int k = 0; k < 60 && !popped; k++)
        {
            s.Step(new RunInput { Slot = 1, Fire = true, Aim = new V2(5.5f, 2.5f) });
            popped = s.Events.Any(e => e.Kind == Ev.LampOut);
            s.Events.Clear();
        }
        Check(popped && s.L.LampsOut.Contains(0), "a shot aimed at a lamp puts it out");
        Check(s.L.LightAt(new V2(5.5f, 2.5f)) <= Tables.NightAmbient + 0.01f, "and the room goes dark");

        // A grenade takes the lamps near it.
        Run b = Make(Site(rows));
        b.L.Ambient = Tables.NightAmbient;
        b.L.Lights.Add((new V2(15.5f, 2.5f), 0xffe0a0ff, 5));
        b.Explode(new V2(15f, 2.5f), 3, 10, GrenadeKind.Flash, b.Player.Id, 0);
        Check(b.L.LampsOut.Contains(0), "a blast blows the lamps near it");

        // Generated sites are night: inside, some floor lit and some dark, in every style.
        foreach (SiteStyle st in Enum.GetValues<SiteStyle>())
        {
            int floor = 0, litTiles = 0, darkTiles = 0;
            bool night = true;
            foreach (ulong seed in new ulong[] { 991, 1201, 7333 })
            {
                var job = new Job { Id = 1, Type = JobType.Retrieve, Style = st, Threat = 2, Seed = seed, Faction = Faction.Vipers, Count = 2, Target = "T", Item = "I", Title = "light" };
                LevelPlan gp = LevelGen.Generate(job);
                Level g = gp.L;
                night &= g.Ambient == Tables.NightAmbient;
                for (int y = 0; y < g.H; y++)
                for (int x = 0; x < g.W; x++)
                {
                    if (g.Solid(x, y) || gp.RoomOf[g.Idx(x, y)] < 0) continue;
                    floor++;
                    float v = g.LightAt(new Pt(x, y).Centre);
                    if (v > 0.55f) litTiles++;
                    if (v < 0.15f) darkTiles++;
                }
            }
            if (_only == "Lighting") GD.Print($"    {st}: {litTiles * 100 / floor} % lit, {darkTiles * 100 / floor} % dark");
            Check(night && litTiles > floor / 5 && darkTiles > floor / 10, $"{st}: inside is light and dark ({litTiles} lit, {darkTiles} dark of {floor})");
        }
    }
}
