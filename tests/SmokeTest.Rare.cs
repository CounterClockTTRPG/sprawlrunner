using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>The rarer places: a building site, a casino, a container ship, a cemetery, a hotel, the rooftops.</summary>
public partial class SmokeTest
{
    private static int Count(LevelPlan p, Tile t)
    {
        int n = 0;
        for (int i = 0; i < p.L.W * p.L.H; i++) if (p.L.T[i] == t) n++;
        return n;
    }

    private void RareSites()
    {
        // ---- how often ----
        var dealt = new System.Collections.Generic.Dictionary<SiteStyle, int>();
        foreach (JobType type in new[] { JobType.Eliminate, JobType.Retrieve, JobType.Datasteal, JobType.Destroy, JobType.Kidnap, JobType.Rescue, JobType.Plant })
        foreach (string d in new[] { "spire", "strip", "docks", "barrens", "industrial" })
        for (ulong s = 1; s <= 300; s++)
        {
            SiteStyle st = Career.PlaceFor(type, SiteStyle.Office, Faction.Vipers, d, false, new Rng(s * 31 + (ulong)type * 7 + (ulong)d.Length));
            dealt[st] = dealt.GetValueOrDefault(st) + 1;
        }
        int total = dealt.Values.Sum();
        foreach (SiteStyle st in new[] { SiteStyle.Construction, SiteStyle.Casino, SiteStyle.Ship, SiteStyle.Cemetery, SiteStyle.Hotel, SiteStyle.Rooftops })
        {
            int n = dealt.GetValueOrDefault(st);
            Check(n > 0 && n < total / 12, $"{st} turns up, rarely ({n} of {total})");
        }
        Eq(Career.PlaceFor(JobType.Courier, SiteStyle.Warehouse, Faction.Vipers, "docks", false, new Rng(3)), SiteStyle.Warehouse, "a courier job stays where it is");
        // Not late-game only: fresh careers' boards over their first fortnight already offer the new places.
        var early = new Dictionary<SiteStyle, int>();
        int offered = 0;
        foreach (ulong seed in new ulong[] { 11, 23, 37, 51, 77 })
        {
            City ec = City.Generate(seed);
            Career fresh = Career.New("Fresh", "samurai", seed);
            fresh.CityRef = ec;
            var ids = new HashSet<int>();
            for (int day = 0; day < 15; day++)
            {
                fresh.RefreshBoard(ec);
                foreach (Job j in fresh.Board) if (ids.Add(j.Id)) { early[j.Style] = early.GetValueOrDefault(j.Style) + 1; offered++; }
                fresh.NextDay(ec, rested: true);
            }
        }
        int newPlaces = early.Where(kv => kv.Key >= SiteStyle.Tunnels).Sum(kv => kv.Value);
        if (_only == "RareSites") GD.Print($"    {offered} jobs on 5 fresh boards over 15 days: " + string.Join(", ", early.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
        Check(newPlaces >= offered / 10, $"a new career's board offers the new places from the start ({newPlaces} of {offered} jobs)");
        Check(Tables.SiteNames.Length == Enum.GetValues<SiteStyle>().Length, "every kind of site has its name on the board");

        // ---- the building site ----
        int dogs = 0, gaps = 0, openings = 0;
        for (ulong s = 1; s <= 5; s++)
        {
            LevelPlan p = LevelGen.Generate(SiteJob(SiteStyle.Construction, JobType.Destroy, s * 7907, 2, Faction.Halcyon));
            dogs += p.Enemies.Count(e => e.Kind == EnemyKind.Hound);
            Rect b = p.Building;
            for (int x = b.X + 1; x < b.Right - 1; x++) { if (p.L[x, b.Y] == Tile.Floor) gaps++; if (p.L[x, b.Bottom - 1] == Tile.Floor) gaps++; }
            openings += Count(p, Tile.DoorOpen);
            Eq(Count(p, Tile.Door), 0, $"build site {s}: no doors hung yet");
        }
        Check(dogs >= 5 && gaps > 10 && openings > 20, $"a building site: open bays in the shell, wide openings, the watchman's dogs ({gaps} gaps, {openings} openings, {dogs} dogs)");

        // ---- the casino ----
        for (ulong s = 1; s <= 4; s++)
        {
            LevelPlan p = LevelGen.Generate(SiteJob(SiteStyle.Casino, JobType.Datasteal, s * 7919, 3, Faction.Lotus));
            int tables = 0, slots = 0;
            for (int i = 0; i < p.L.W * p.L.H; i++)
                if (p.L.F[i] == Floor.Casino) { if (p.L.T[i] == Tile.Desk) tables++; if (p.L.T[i] == Tile.Rack) slots++; }
            Check(tables > 12 && slots > 6, $"casino {s}: tables and slot banks on the gaming floor ({tables} table tiles, {slots} slots)");
            Check(p.Rooms.Any(r => r.Kind == RoomKind.Cage) && p.Rooms.Any(r => r.Kind == RoomKind.Count), $"casino {s}: a cashier's cage and a count room");
            Check(p.Enemies.Count(e => e.Kind == EnemyKind.Camera) >= 4, $"casino {s}: cameras on the floor, though it isn't Halcyon's");
            Check(p.Npcs.Count(n => n.vendor == null) >= Tables.Gamblers, $"casino {s}: gamblers at the tables");
        }

        // ---- the container ship ----
        for (ulong s = 1; s <= 4; s++)
        {
            LevelPlan p = LevelGen.Generate(SiteJob(SiteStyle.Ship, JobType.Retrieve, s * 104723, 2 + (int)s % 3));
            Check(Count(p, Tile.Cargo) > 60 && Count(p, Tile.Drop) > 400, $"ship {s}: containers on deck, the sea all round");
            Check(p.Basement.W > 0 && p.BelowName == "HOLD", $"ship {s}: the hold below");
            Check(p.L[p.Entry] == Tile.Floor && p.L.F[p.L.Idx(p.Entry.X, p.Entry.Y)] == Floor.Concrete, $"ship {s}: you come along the quay");
            Check(p.Building.W > 2 * p.Building.H || p.Building.H > 2 * p.Building.W, $"ship {s}: long and narrow ({p.Building.W}x{p.Building.H})");
        }
        // She sails: a ship's job always has a deadline.
        Career sc = TestCareer();
        City city = City.Generate(31);
        int ships = 0, timed = 0;
        for (int k = 0; k < 3000 && ships < 3; k++)
            if (sc.MakeJob(city) is Job j && j.Style == SiteStyle.Ship) { ships++; if (j.Has(RunMod.Deadline) && j.Deadline > 0) timed++; }
        Check(ships > 0 && timed == ships, $"a ship's job sails on a deadline ({timed}/{ships})");

        // ---- the cemetery ----
        for (ulong s = 1; s <= 4; s++)
        {
            LevelPlan p = LevelGen.Generate(SiteJob(SiteStyle.Cemetery, JobType.Bounty, s * 15485867, 2 + (int)s % 3));
            Check(Count(p, Tile.Grave) > 60, $"cemetery {s}: rows of graves ({Count(p, Tile.Grave)})");
            Check(p.Basement.W > 0 && p.BelowName == "CRYPT", $"cemetery {s}: a crypt below the chapel");
            Check(p.Building.W * p.Building.H < p.L.W * p.L.H / 3, $"cemetery {s}: a chapel in its graveyard, not a cathedral");
        }
        // Hallowed ground: a manabolt hits harder here.
        string[] yard =
        {
            "##############",
            "#P...........#",
            "#............#",
            "##############",
        };
        float Bolt(SiteStyle st)
        {
            Career mc = Career.New("Mage", "mage", 3);
            var jb = TestJob();
            jb.Style = st;
            var mr = new Run(jb, mc, Site(yard));
            mr.Events.Clear();
            Actor g = mr.AddEnemy(EnemyKind.Bruiser, new V2(8.5f, 1.5f), MathF.PI);
            g.MaxHp = g.Hp = 1e4f;
            for (int i = 0; i < 30 && g.Hp == 1e4f; i++) mr.Step(new RunInput { Slot = -1, Cast = i == 0 ? 1 : 0, Aim = g.Pos });
            return 1e4f - g.Hp;
        }
        float plain = Bolt(SiteStyle.Office), hallowed = Bolt(SiteStyle.Cemetery);
        Check(plain > 0 && hallowed > plain * 1.15f, $"hallowed ground: spells hit harder ({hallowed:0} vs {plain:0})");

        // ---- the hotel ----
        for (ulong s = 1; s <= 4; s++)
        {
            LevelPlan p = LevelGen.Generate(SiteJob(SiteStyle.Hotel, JobType.Eliminate, s * 2147483647UL, 2 + (int)s % 3, Faction.Halcyon));
            Check(p.Tower && p.Floors.Count >= 2, $"hotel {s}: floors of rooms over the lobby ({p.Floors.Count})");
            var guestRooms = p.Rooms.Where(r => r.R.X >= p.MainW && r.R.Area <= 30).ToList();
            Check(guestRooms.Count >= 10, $"hotel {s}: room after room ({guestRooms.Count})");
            Check(p.Rooms.Any(r => r.Kind == RoomKind.Suite && p.Floors[^1].Contains(Pt.Of(r.Centre))), $"hotel {s}: the suite on the top floor");
            Check(p.Pickups.Any(k => k.Kind == PickupKind.Uniform), $"hotel {s}: a housekeeping uniform somewhere");
            Check(p.Npcs.Count(n => n.vendor == null) >= 2, $"hotel {s}: guests in their rooms");
        }
        // The uniform: put it on and you're staff.
        LevelPlan up = Site(yard);
        up.Pickups.Add(new PickupSpawn { Kind = PickupKind.Uniform, Pos = new V2(2.5f, 1.5f), Name = "a housekeeping uniform" });
        Run ur = Make(up);
        Steps(ur, 0.5f, new RunInput { Slot = -1, Move = new V2(1, 0), Interact = true, InteractPressed = true, Aim = new V2(5, 1.5f) });
        Check(ur.Disguised, "a housekeeping uniform is a disguise");

        // ---- the rooftops ----
        for (ulong s = 1; s <= 5; s++)
        {
            LevelPlan p = LevelGen.Generate(SiteJob(SiteStyle.Rooftops, JobType.Eliminate, s * 4999, 2 + (int)s % 3, Faction.Lotus));
            int roofs = p.Rooms.Count(r => p.L.F[p.L.Idx(r.R.X + r.R.W / 2, r.R.Y + r.R.H / 2)] == Floor.Roof || r.R.Area >= 40);
            Check(roofs >= 5, $"rooftops {s}: five or six roofs ({roofs})");
            Check(Count(p, Tile.Drop) > p.L.W * p.L.H / 5 && Count(p, Tile.DoorOpen) >= 5, $"rooftops {s}: the drop between them, planks across");
            Check(p.Enemies.All(e => p.L[Pt.Of(e.Pos)] != Tile.Drop) && p.Reinforce.All(r => p.L[Pt.Of(r)] != Tile.Drop), $"rooftops {s}: nobody stands on thin air");
            Check(LevelGen.Reachable(p, new Pt(p.Extraction.X + 1, p.Extraction.Y + 1)), $"rooftops {s}: the helicopter can be walked to");
        }
        // A dash carries you over a narrow gap; a short one drops you back where you jumped from.
        string[] gap =
        {
            "##############",
            "#P.......__..#",
            "#........__..#",
            "##############",
        };
        LevelPlan gp = Site(gap);
        for (int y = 1; y <= 2; y++) for (int x = 9; x <= 10; x++) gp.L.Set(x, y, Tile.Drop);
        Run gr = Make(gp);
        gr.Player.Pos = new V2(8.6f, 1.5f);
        Steps(gr, 0.1f, new RunInput { Slot = -1, Move = new V2(1, 0), Aim = new V2(12, 1.5f) });
        Check(gr.Player.Pos.X < 9.1f, "walking, you stop at the edge");
        Steps(gr, 0.4f, new RunInput { Slot = -1, Move = new V2(1, 0), Dash = true, Aim = new V2(12, 1.5f) });
        Check(gr.Player.Pos.X > 11, $"a dash carries you over the gap ({gr.Player.Pos.X:0.0})");
        LevelPlan wide = Site(gap);
        for (int y = 1; y <= 2; y++) for (int x = 5; x <= 11; x++) wide.L.Set(x, y, Tile.Drop);
        Run wr = Make(wide);
        wr.Player.Pos = new V2(4.6f, 1.5f);
        float hp = wr.Player.Hp;
        Steps(wr, 0.4f, new RunInput { Slot = -1, Move = new V2(1, 0), Dash = true, Aim = new V2(12, 1.5f) });
        Check(wr.Player.Pos.X < 5 && wr.Player.Hp < hp, "too far: you catch the ledge, back where you jumped, bruised");

        // ---- the bot plays each ----
        int won = 0, played = 0;
        foreach (var (st, type, f) in new[]
        {
            (SiteStyle.Construction, JobType.Destroy, Faction.Halcyon), (SiteStyle.Casino, JobType.Datasteal, Faction.Lotus),
            (SiteStyle.Ship, JobType.Retrieve, Faction.Vipers), (SiteStyle.Cemetery, JobType.Bounty, Faction.Vipers),
            (SiteStyle.Hotel, JobType.Eliminate, Faction.Halcyon), (SiteStyle.Rooftops, JobType.Eliminate, Faction.Lotus),
        })
        for (int t = 1; t <= 2; t++)
        {
            var bj = new Job { Id = 1, Type = type, Style = st, Threat = t, Seed = (ulong)(91 + (int)type * 1000 + (int)st * 100 + t), Faction = f, Count = 2, Target = "T", Item = "I", Title = "bot" };
            Career bc = Career.New("Bot", "samurai", 9);
            bc.Weapons.Add("kestrel");
            bc.Primary = "kestrel";
            bc.TraumaLinks = 1;
            Run br = BotPlay(bj, bc);
            played++;
            bool ok = br.Phase == RunPhase.Extracted && br.Success;
            if (ok) won++;
            GD.Print($"    {type,-10} {st,-12} t{t}: {(ok ? "WON " : br.Phase == RunPhase.Dead ? "DIED" : br.Aborted ? "LEFT" : "TIME")} {br.Time,5:0}s kills {br.Kills,2} alarm {(br.Alarm ? "y" : "n")} obj {(br.ObjectiveDone ? "y" : "n")}");
        }
        Check(won >= played / 3, $"the bot wins at least a third of easy jobs in the rare places ({won}/{played})");
    }
}
