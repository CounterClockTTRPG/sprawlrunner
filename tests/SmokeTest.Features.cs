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
}
