using System;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>The new kinds of site: ghoul tunnels, the all-night store, the back-alley clinic -- how they're built, who's there, and their rules.</summary>
public partial class SmokeTest
{
    private static Job SiteJob(SiteStyle st, JobType type, ulong seed, int threat = 3, Faction f = Faction.Vipers) =>
        new() { Id = 1, Type = type, Style = st, Threat = threat, Seed = seed, Faction = f, Count = 3, Target = "T", Item = "I", Title = "site" };

    private void NewSites()
    {
        // ---- where jobs happen ----
        int nests = 0, kidnaps = 0, banks = 0, hives = 0;
        for (ulong s = 1; s <= 2000; s++)
        {
            if (Career.PlaceFor(JobType.Bounty, SiteStyle.Den, Faction.Vipers, "barrens", false, new Rng(s)) == SiteStyle.Tunnels) nests++;
            if (Career.PlaceFor(JobType.Kidnap, SiteStyle.Office, Faction.Halcyon, "spire", false, new Rng(s)) == SiteStyle.Store) kidnaps++;
            if (Career.PlaceFor(JobType.Rescue, SiteStyle.Club, Faction.Lotus, "strip", false, new Rng(s)) == SiteStyle.Clinic) banks++;
            if (Career.PlaceFor(JobType.Bounty, SiteStyle.Warehouse, Faction.Vipers, "industrial", true, new Rng(s)) != SiteStyle.Warehouse) hives++;
        }
        Check(nests is > 1350 and < 1650, $"three ghoul nests in four are in the tunnels ({nests}/2000)");
        Check(kidnaps is > 850 and < 1150, $"half the kidnappings are at a store ({kidnaps}/2000)");
        Check(banks is > 850 and < 1150, $"half the Red Lotus' hostages are in a body bank ({banks}/2000)");
        Eq(hives, 0, "a hive stays where it is");
        Eq(Career.PlaceFor(JobType.Destroy, SiteStyle.Lab, Faction.Halcyon, "industrial", false, new Rng(5)), SiteStyle.Lab, "a sabotage job stays in its lab");
        // The board, over a long career, deals all three.
        Career bc = TestCareer();
        City city = City.Generate(31);
        var dealt = new System.Collections.Generic.HashSet<SiteStyle>();
        for (int k = 0; k < 600; k++) if (bc.MakeJob(city) is Job j) dealt.Add(j.Style);
        Check(dealt.Contains(SiteStyle.Tunnels) && dealt.Contains(SiteStyle.Store) && dealt.Contains(SiteStyle.Clinic), $"the board deals tunnels, stores and clinics ({string.Join(", ", dealt)})");

        // ---- ghoul tunnels ----
        int arches = 0, water = 0, earth = 0, glass = 0, sleeping = 0, nestRooms = 0, yardJunk = 0;
        for (ulong s = 1; s <= 6; s++)
        {
            LevelPlan p = LevelGen.Generate(SiteJob(SiteStyle.Tunnels, JobType.Bounty, s * 7919, 2 + (int)s % 3));
            Level L = p.L;
            for (int y = 0; y < L.H; y++)
            for (int x = 0; x < L.W; x++)
            {
                if (L[x, y] == Tile.DoorOpen) arches++;
                if (L[x, y] == Tile.Glass) glass++;
                if (L[x, y] == Tile.Floor && L.F[L.Idx(x, y)] == Floor.Water) water++;
                if (L[x, y] == Tile.Floor && L.F[L.Idx(x, y)] == Floor.Earth) earth++;
                if (!p.Building.Contains(new Pt(x, y)) && L[x, y] is Tile.Wreck or Tile.Crate or Tile.Fence) yardJunk++;
            }
            nestRooms += p.Rooms.Count(r => r.Kind == RoomKind.Nest);
            sleeping += p.Enemies.Count(e => e.Mood == Mood.Asleep);
            Check(p.Rooms.Count >= 8, $"tunnels {s}: a warren of caverns and tunnels ({p.Rooms.Count} rooms)");
            int tunnels = p.Rooms.Count(r =>
            {
                int sewer = 0, all = 0;
                for (int y = r.R.Y; y < r.R.Bottom; y++)
                for (int x = r.R.X; x < r.R.Right; x++)
                    if (p.RoomOf[L.Idx(x, y)] == r.Id) { all++; if (L.F[L.Idx(x, y)] is Floor.Sewer or Floor.Water) sewer++; }
                return sewer * 2 > all;
            });
            Check(tunnels >= 3, $"tunnels {s}: sewer tunnels between the caverns ({tunnels})");
            Check(p.Enemies.All(e => e.Kind is EnemyKind.Hound or EnemyKind.Bruiser or EnemyKind.Spirit or EnemyKind.Barghest), $"tunnels {s}: only what lives down there");
        }
        Check(arches > 30, $"arches where the tunnels meet the caverns ({arches})");
        Check(water > 20 && earth > 200, $"earth caverns and black water ({earth} earth, {water} water)");
        Eq(glass, 0, "no windows underground");
        Eq(yardJunk, 0, "no cars or fences in the culvert");
        Check(nestRooms >= 6 && sleeping >= 4, $"nests, with things asleep in them ({nestRooms} nests, {sleeping} asleep)");

        // Wading: slower, and a splash carries further than a step.
        string[] pool =
        {
            "##############",
            "#P...........#",
            "#............#",
            "##############",
        };
        float Walked(bool wet)
        {
            LevelPlan wp = Site(pool);
            if (wet) for (int x = 1; x < 13; x++) for (int y = 1; y < 3; y++) wp.L.F[wp.L.Idx(x, y)] = Floor.Water;
            Run wr = Make(wp);
            V2 from = wr.Player.Pos;
            Steps(wr, 1f, new RunInput { Slot = -1, Move = new V2(1, 0), Aim = from + new V2(5, 0) });
            return wr.Player.Pos.X - from.X;
        }
        float dry = Walked(false), wet = Walked(true);
        Check(wet < dry * 0.8f && wet > 0.1f, $"wading is slower ({wet:0.0} vs {dry:0.0} tiles in a second)");
        foreach (bool wetFloor in new[] { false, true })
        {
            LevelPlan sp = Site(pool);
            if (wetFloor) for (int x = 1; x < 13; x++) for (int y = 1; y < 3; y++) sp.L.F[sp.L.Idx(x, y)] = Floor.Water;
            Run sr = Make(sp);
            Actor g = sr.AddEnemy(EnemyKind.Guard, sr.Player.Pos + new V2(1.8f, 0), 0);
            Steps(sr, 0.05f, new RunInput { Slot = -1, Move = new V2(-1, 0), Sneak = true, Aim = sr.Player.Pos + new V2(-1, 0) });
            if (wetFloor) Check(g.State == AiState.Suspicious, "sneaking through water still splashes");
            else Eq(g.State, AiState.Idle, "sneaking on a dry floor makes no sound");
        }

        // ---- the all-night store ----
        int onFloor = 0, kidnapSites = 0;
        for (ulong s = 1; s <= 8; s++)
        {
            LevelPlan p = LevelGen.Generate(SiteJob(SiteStyle.Store, JobType.Kidnap, s * 104729, 1 + (int)s % 5, Faction.Halcyon));
            Level L = p.L;
            Rect b = p.Building;
            kidnapSites++;
            int racks = 0, panes = 0;
            for (int y = 0; y < L.H; y++) for (int x = 0; x < L.W; x++) { if (L[x, y] == Tile.Rack) racks++; if (L[x, y] == Tile.Glass) panes++; }
            Check(racks > 20, $"store {s}: rows of shelving ({racks})");
            Check(panes > 8, $"store {s}: a plate-glass front ({panes} panes)");
            Check(p.Npcs.Count(n => n.vendor == null) >= Tables.Shoppers && p.Npcs.Count(n => n.vendor == "clerk") == 1, $"store {s}: shoppers in the aisles and one clerk");
            Check(p.Rooms.Any(r => r.Kind == RoomKind.Stock), $"store {s}: a stockroom out the back");
            Check(b.W * b.H < L.W * L.H / 2, $"store {s}: a store on its car park, not a warehouse ({b.W}x{b.H} on {L.W}x{L.H})");
            Spawn vip = p.Enemies.First(e => e.Kind == EnemyKind.Vip);
            Room vr = p.Rooms.FirstOrDefault(r => r.R.Contains(Pt.Of(vip.Pos)));
            if (vr != null && vr == p.Rooms.OrderByDescending(r => r.R.Area).First()) onFloor++;
        }
        Check(onFloor >= kidnapSites / 2, $"the target is usually out on the shop floor ({onFloor}/{kidnapSites})");
        Check(p0Entries(SiteStyle.Store).Contains("Delivery door"), "a store can be come into by its delivery door");

        // The clerk's panic button: Lone Star, sooner than a cleaner's phone call, and two steps up the ladder.
        string[] shop =
        {
            "##############",
            "#P...........#",
            "#............#",
            "##############",
        };
        LevelPlan cp = Site(shop);
        cp.Npcs.Add((new V2(5.5f, 1.5f), "clerk"));
        var job = TestJob();
        job.Style = SiteStyle.Store;
        var cr = new Run(job, TestCareer(), cp);
        cr.Events.Clear();
        Actor clerk = cr.Actors.First(a => a.Cleaner);
        clerk.Facing = MathF.PI;
        int before = cr.SecurityLevel;
        Steps(cr, Tables.PanicButton + 1f, new RunInput { Slot = 0, Aim = clerk.Pos });
        Check(cr.SecurityLevel >= before + 2, $"the clerk hits the panic button: Lone Star's coming (security {before} -> {cr.SecurityLevel})");
        Check(cr.StyleRule?.name == "STORE", "the HUD names the store's rules");

        // ---- the back-alley clinic ----
        int wards = 0, patients = 0, theatres = 0;
        for (ulong s = 1; s <= 6; s++)
        {
            LevelPlan p = LevelGen.Generate(SiteJob(SiteStyle.Clinic, JobType.Rescue, s * 15485863, 2 + (int)s % 3, Faction.Lotus));
            wards += p.Rooms.Count(r => r.Kind == RoomKind.Ward);
            theatres += p.Rooms.Count(r => r.Kind == RoomKind.Theatre);
            patients += p.Npcs.Count(n => n.vendor == "patient");
            Check(p.Npcs.Count(n => n.vendor == "orderly") == Tables.Cleaners, $"clinic {s}: orderlies on their rounds");
        }
        Check(wards >= 5 && theatres >= 4 && patients >= wards * Tables.Patients, $"wards with patients in them, and operating theatres ({wards} wards, {patients} patients, {theatres} theatres)");
        // Sedated: a patient doesn't run and doesn't scream.
        LevelPlan wp2 = Site(shop);
        wp2.Npcs.Add((new V2(6.5f, 2.5f), "patient"));
        Run pr = Make(wp2);
        Actor patient = pr.Actors.First(a => a.Sedated);
        pr.Panic(patient.Pos, 10);
        Check(patient.PanicT <= 0 && patient.Static, "a sedated patient neither runs nor screams");
        // The theatre's gas lines: a burst one puts a guard under, and only slows you.
        string[] theatre =
        {
            "##########",
            "#P.......#",
            "#........#",
            "#....p...#",
            "##########",
        };
        LevelPlan tp = Site(theatre);
        tp.L.Set(5, 3, Tile.Pipe);
        var tj = TestJob();
        tj.Style = SiteStyle.Clinic;
        var tr = new Run(tj, TestCareer(), tp);
        tr.Events.Clear();
        Actor guard = tr.AddEnemy(EnemyKind.Guard, new V2(6.5f, 2.5f), 0);
        float hp = guard.Hp;
        tr.DamageTile(new Pt(5, 3), 1e4f, new V2(5.5f, 3.5f));
        Check(tr.Clouds.Any(c => c.Kind == GasKind.Anaesthetic), "a clinic's burst pipe lets out anaesthetic");
        Steps(tr, 1f);
        Check(guard.Stun > 1 && guard.Hp == hp, "the guard beside it goes under, unhurt");

        // The crude bot plays the jobs each new place was made for: can it be played through?
        int won = 0, played = 0;
        foreach (var (st, type, f) in new[]
        {
            (SiteStyle.Tunnels, JobType.Bounty, Faction.Vipers), (SiteStyle.Tunnels, JobType.Retrieve, Faction.Vipers),
            (SiteStyle.Store, JobType.Kidnap, Faction.Halcyon), (SiteStyle.Store, JobType.Eliminate, Faction.Lotus),
            (SiteStyle.Clinic, JobType.Rescue, Faction.Lotus), (SiteStyle.Clinic, JobType.Datasteal, Faction.Lotus),
        })
        for (int t = 1; t <= 2; t++)
        {
            var bj = new Job { Id = 1, Type = type, Style = st, Threat = t, Seed = (ulong)(57 + (int)type * 1000 + (int)st * 100 + t), Faction = f, Count = 2, Target = "T", Item = "I", Title = "bot" };
            Career bc2 = Career.New("Bot", "samurai", 9);
            bc2.Weapons.Add("kestrel");
            bc2.Primary = "kestrel";
            bc2.TraumaLinks = 1;
            if (type == JobType.Kidnap) { bc2.AmmoOwned.Add("gel"); bc2.AmmoFor["kestrel"] = "gel"; }
            Run br = BotPlay(bj, bc2);
            played++;
            bool ok = br.Phase == RunPhase.Extracted && br.Success;
            if (ok) won++;
            GD.Print($"    {type,-10} {st,-8} t{t}: {(ok ? "WON " : br.Phase == RunPhase.Dead ? "DIED" : br.Aborted ? "LEFT" : "TIME")} {br.Time,5:0}s kills {br.Kills,2} alarm {(br.Alarm ? "y" : "n")} obj {(br.ObjectiveDone ? "y" : "n")}");
        }
        Check(won >= played / 3, $"the bot wins at least a third of easy jobs in the new places ({won}/{played})");
    }

    /// <summary>The names of the ways in that some store, over a few seeds, offers.</summary>
    private static System.Collections.Generic.HashSet<string> p0Entries(SiteStyle st)
    {
        var names = new System.Collections.Generic.HashSet<string>();
        for (ulong s = 1; s <= 12; s++) foreach (EntryPoint e in LevelGen.Generate(SiteJob(st, JobType.Retrieve, s * 31337, 2 + (int)s % 3)).Entries) names.Add(e.Name);
        return names;
    }
}
