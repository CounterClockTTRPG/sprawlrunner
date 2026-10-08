using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>
/// The headless self-check: <c>./play.sh --check</c>. Exits non-zero on any
/// failed assertion. Rules are checked on tiny hand-drawn sites where the
/// answer is certain; the generators over many seeds.
/// </summary>
public partial class SmokeTest : Node
{
    public Root Root;
    private int _checks, _failures;
    private string _only;

    public override void _Ready()
    {
        GD.Print("SPRAWLRUNNER self-check");
        ulong started = Time.GetTicksMsec();
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--only");
        if (at >= 0 && at + 1 < args.Length) _only = args[at + 1];
        foreach (Action s in Sections()) Run(s);
        GD.Print($"{_checks} checks, {_failures} failed, {(Time.GetTicksMsec() - started) / 1000.0:0.0} s");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private IEnumerable<Action> Sections() => new Action[]
    {
        Sight, Shots, Destruction, Explosions, Fire, Stealth, Noise, Fighting, Grenades, Abilities, Magic, Bodies, EnemyGrenades, Conditions, NonLethal, Carrying, Lures, Fixtures, Ammo, Shields, Riggers, Bosses, Allies, Basements, PlaytestFixes, Hubs, Johnsons, Grudges, Spirits, Campaign, Storylets, Consequences, Crosses, TraumaLinkStreets, Turf, Happenings, Crew, NewJobs, MoreJobs, Fifth, LateCampaigns, NewFaces, Towers, TopTier, Verbs, RunChoices, Backgrounds, Receipts, Roadblocks, Standing, EdgePoints, DialogsClose, Crowds, LegacyChoice, HighEnd, DangerCalls, SecurityLadder, Lamplighter, Balance, Performance, Block, LongCareer, Favours, Injuries, Legacy, Phantoms, Tutorial, Bindings, Playtest2Fixes, Twists, ContactStories, Trust, StatsAndAchievements, Rigging, Playtest3Fixes,
        Objectives, Extraction, LevelGenSeeds, LevelGenDebug, Bot, CityGen, Driving, Autopilot, Traffic, Police, Gangs, Couriers, Motorcycles, Shops, Karma, Board, Debriefs, Saving, Audio, GeneratedAssets, Retiring, StreetEncounters, Lighting, RoomKinds, StyleRules, EntryPoints, RivalCrews, Presentation,
    };

    private void Run(Action section)
    {
        string name = section.Method.Name;
        if (_only != null && !name.Contains(_only, StringComparison.OrdinalIgnoreCase)) return;
        ulong t = Time.GetTicksMsec();
        int before = _failures;
        try { section(); }
        catch (Exception e)
        {
            _failures++;
            GD.PrintErr($"  {name}: threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
        }
        GD.Print($"  {(before == _failures ? "ok  " : "FAIL")} {name} ({Time.GetTicksMsec() - t} ms)");
    }

    private void Check(bool cond, string what)
    {
        _checks++;
        if (cond) return;
        _failures++;
        GD.PrintErr($"    failed: {what}");
    }

    private void Eq<T>(T actual, T expected, string what) => Check(Equals(actual, expected), $"{what}: expected {expected}, got {actual}");

    /// <summary>A Matrix route: towards the nearest datastore not yet taken, steering clear of ICE.</summary>
    internal static V2 MatrixMove(MatrixRun m)
    {
        var targets = m.Stores.Where(s => !m.Taken.Contains(s)).ToList();
        if (targets.Count == 0) return V2.Zero;
        Pt best = m.You;
        int bestScore = int.MaxValue;
        var dYou = m.Bfs(m.You);
        Pt target = targets.OrderBy(t => dYou[MatrixRun.Idx(t)]).First();
        var dist = m.Bfs(target);
        foreach (Pt d in Pt.Dirs4)
        {
            Pt n = m.You + d;
            if (!m.Linked(m.You, n)) continue;
            int danger = m.Ice.Any(i => i == n) ? 1000 : m.Ice.Any(i => i.Chebyshev(n) <= 1 && (i.X == n.X || i.Y == n.Y)) ? 6 : 0;
            int score = dist[MatrixRun.Idx(n)] * 2 + danger;
            if (score < bestScore) { bestScore = score; best = n; }
        }
        return new V2(best.X - m.You.X, best.Y - m.You.Y);
    }

    // ---- tiny sites -------------------------------------------------------------------

    /// <summary>
    /// A hand-drawn site. Legend: # reinforced, c concrete, d drywall, b brick,
    /// g glass, + door, L locked door, x crate, o barrel, r rack, m mainframe,
    /// P the entry (player start), E extraction (3x3 around it), . floor.
    /// </summary>
    private static LevelPlan Site(params string[] rows)
    {
        int h = rows.Length, w = rows[0].Length;
        var L = new Level(w, h);
        var plan = new LevelPlan { L = L, RoomOf = new int[w * h], Building = new Rect(1, 1, w - 2, h - 2), MainW = w };
        Array.Fill(plan.RoomOf, -1);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            char c = rows[y][x];
            L.Set(x, y, c switch
            {
                '#' => Tile.Reinforced, 'c' => Tile.Concrete, 'd' => Tile.Drywall, 'b' => Tile.Brick, 'g' => Tile.Glass,
                '+' => Tile.Door, 'L' => Tile.LockedDoor, 'x' => Tile.Crate, 'o' => Tile.Barrel, 'r' => Tile.Rack, 'm' => Tile.Mainframe,
                _ => Tile.Floor,
            });
            if (c == 'P') plan.Entry = new Pt(x, y);
            if (c == 'r') plan.Targets.Add(new Pt(x, y));
            if (c == 'm') plan.Mainframe = new Pt(x, y);
            if (c == 'E') plan.Extraction = new Rect(x - 1, y - 1, 3, 3);
        }
        if (plan.Extraction.W == 0) plan.Extraction = new Rect(plan.Entry.X - 1, plan.Entry.Y - 1, 3, 3);
        plan.Reinforce.Add(new V2(w - 2.5f, h - 2.5f));
        return plan;
    }

    private static Job TestJob(JobType type = JobType.Datasteal, Faction f = Faction.Halcyon) =>
        new() { Id = 1, Type = type, Faction = f, Style = SiteStyle.Office, Threat = 1, Seed = 7, Target = "Tester", Item = "widget", Title = "Test", Lot = -1, Pay = 1000, Karma = 4 };

    /// <summary>Accept a job, settling terms with any Mr. Johnson first.</summary>
    private static string Accept(Career c, Job j)
    {
        if (j != null) j.Negotiated = true;
        return c.Accept(j);
    }

    private static Career TestCareer()
    {
        var c = Career.New("Test", "samurai", 5);
        return c;
    }

    private static Run Make(LevelPlan plan, JobType type = JobType.Datasteal, Career c = null)
    {
        var run = new Run(TestJob(type), c ?? TestCareer(), plan);
        run.Events.Clear();
        return run;
    }

    private static void Steps(Run r, float seconds, RunInput? inp = null)
    {
        int n = (int)(seconds * 60);
        for (int i = 0; i < n && r.Phase == RunPhase.Playing; i++) r.Step(inp ?? new RunInput { Slot = -1, Aim = r.Player.Pos + new V2(1, 0) });
    }

    // ---- sight -------------------------------------------------------------------------

    private void Sight()
    {
        LevelPlan p = Site(
            "###########",
            "#P...d....#",
            "#....d....#",
            "#....g....#",
            "#.........#",
            "###########");
        var vis = new bool[p.L.W * p.L.H];
        p.L.Fov(new Pt(1, 1), 12, vis);
        bool V(int x, int y) => vis[y * p.L.W + x];
        Check(V(5, 1), "the wall itself is seen");
        Check(!V(7, 1), "drywall hides what is behind it");
        Check(V(8, 4), "open floor is seen");
        Check(V(7, 3), "glass lets sight through");
        Check(p.L.Explored[p.L.Idx(8, 4)], "seen tiles are explored");
        Check(!p.L.Los(new V2(2.5f, 1.5f), new V2(8.5f, 1.5f)), "no line of sight through drywall");
        Check(p.L.Los(new V2(2.5f, 3.5f), new V2(8.5f, 3.5f)), "line of sight through glass");
        Check(!p.L.ShotClear(new V2(2.5f, 3.5f), new V2(8.5f, 3.5f)), "glass stops a shot");
    }

    private void Shots()
    {
        LevelPlan p = Site(
            "############",
            "#P.........#",
            "#..........#",
            "############");
        Run r = Make(p);
        Actor g = r.AddEnemy(EnemyKind.Guard, new V2(8.5f, 1.5f), MathF.PI);
        g.State = AiState.Idle;
        float hp = g.Hp;
        var inp = new RunInput { Slot = -1, Fire = true, Aim = g.Pos };
        for (int i = 0; i < 30; i++) r.Step(inp);
        Check(g.Hp < hp || g.Dead, "shooting a guard down a corridor hurts it");
        Check(g.Alert || g.Dead, "being shot alerts the guard");
        Check(r.Events.Count(e => e.Kind == Ev.Shot && e.A == r.Player.Id) > 0, "shots are reported");
    }

    // ---- destruction ----------------------------------------------------------------------

    private void Destruction()
    {
        LevelPlan p = Site(
            "##########",
            "#P..d....#",
            "#...g....#",
            "#...c....#",
            "##########");
        Run r = Make(p);
        float before = p.L.Hp[p.L.Idx(4, 1)];
        r.DamageTile(new Pt(4, 1), 10, new V2(4, 1.5f));
        Check(p.L.Hp[p.L.Idx(4, 1)] < before, "damage lowers a wall's hit points");
        r.DamageTile(new Pt(4, 2), 6, new V2(4, 2.5f));
        Eq(p.L[4, 2], Tile.Rubble, "glass breaks to rubble");
        int v = p.L.Version;
        r.DamageTile(new Pt(4, 1), 1000, new V2(4, 1.5f));
        Eq(p.L[4, 1], Tile.Rubble, "drywall destroyed");
        Check(p.L.Version > v, "level version moves on change");
        Check(!p.L.Solid(4, 1), "rubble is walkable");
        Check(!p.L.BlocksSight(4, 1), "rubble does not block sight");
        r.DamageTile(new Pt(0, 0), 1e6f, default);
        Eq(p.L[0, 0], Tile.Reinforced, "reinforced walls never break");
        Check(r.Events.Any(e => e.Kind == Ev.Broken), "breaking is reported");
    }

    private void Explosions()
    {
        LevelPlan p = Site(
            "############",
            "#P.........#",
            "#...dd.....#",
            "#....o.o...#",
            "#..........#",
            "############");
        Run r = Make(p);
        Actor g = r.AddEnemy(EnemyKind.Guard, new V2(7.5f, 4.5f), 0);
        r.Explode(new V2(5.5f, 3.5f), 3, 90, GrenadeKind.Frag, r.Player.Id, 2.2f);
        Eq(p.L[5, 3], Tile.Rubble, "the barrel at the centre is gone");
        Eq(p.L[7, 3], Tile.Rubble, "the next barrel chained");
        Check(p.L[4, 2] == Tile.Rubble || p.L.Hp[p.L.Idx(4, 2)] < 60, "drywall near a blast is wrecked");
        Check(g.Hp < g.MaxHp, "a guard in the blast is hurt");
        Check(r.Events.Count(e => e.Kind == Ev.Explosion) >= 3, "each explosion is reported");
        // Walls shield.
        LevelPlan q = Site(
            "##########",
            "#P..c....#",
            "#...c....#",
            "#...c....#",
            "##########");
        Run r2 = Make(q);
        Actor h = r2.AddEnemy(EnemyKind.Guard, new V2(6.5f, 2.5f), 0);
        r2.Explode(new V2(2.5f, 2.5f), 3, 90, GrenadeKind.Frag, r2.Player.Id, 2.2f);
        Eq(h.Hp, h.MaxHp, "a concrete wall shields from the blast");
        Check(r2.Player.Hp < r2.Player.MaxHp, "standing in your own blast hurts");
    }

    private void Fire()
    {
        LevelPlan p = Site(
            "##########",
            "#P.......#",
            "#...xx...#",
            "#........#",
            "##########");
        Run r = Make(p);
        r.Ignite(new V2(4.5f, 2.5f), 1.2f, 3);
        Check(r.Fire.Count > 0, "fire starts");
        Steps(r, 8);
        Check(p.L[4, 2] == Tile.Rubble || p.L[5, 2] == Tile.Rubble, "crates burn away");
    }

    // ---- the opposition ------------------------------------------------------------------

    private void Stealth()
    {
        LevelPlan p = Site(
            "###############",
            "#P......c.....#",
            "#.......c.....#",
            "#.......c.....#",
            "###############");
        Run r = Make(p);
        Actor g = r.AddEnemy(EnemyKind.Guard, new V2(11.5f, 2.5f), MathF.PI);
        Steps(r, 3);
        Check(!g.Alert, "a guard behind a wall does not notice you");
        LevelPlan q = Site(
            "###############",
            "#P............#",
            "#.............#",
            "#.............#",
            "###############");
        Run r2 = Make(q);
        Actor g2 = r2.AddEnemy(EnemyKind.Guard, new V2(7.5f, 1.5f), MathF.PI);
        r2.Step(new RunInput { Slot = -1, Aim = g2.Pos });
        Check(!g2.Alert, "detection is not instant");
        Steps(r2, 4);
        Check(g2.Alert, "a guard who can see you raises the alert");
        // Facing away: no cone, no detection.
        Run r3 = Make(Site(
            "###############",
            "#P............#",
            "#.............#",
            "###############"));
        Actor g3 = r3.AddEnemy(EnemyKind.Guard, new V2(9.5f, 1.5f), 0);
        g3.BaseFacing = 0;
        Steps(r3, 0.6f, new RunInput { Slot = -1, Sneak = true, Aim = new V2(3, 1.5f) });
        Check(!g3.Alert, "a guard looking the other way does not see you");
        // Cameras raise the alarm; Spoof stops them.
        Run r4 = Make(Site(
            "###############",
            "#P............#",
            "#.............#",
            "###############"));
        r4.AddEnemy(EnemyKind.Camera, new V2(6.5f, 1.5f), MathF.PI);
        Steps(r4, 3);
        Check(r4.Alarm, "a camera that sees you raises the alarm");
        Career spoof = TestCareer();
        spoof.Programs.Add("spoof");
        Run r5 = Make(Site(
            "###############",
            "#P............#",
            "#.............#",
            "###############"), JobType.Datasteal, spoof);
        r5.AddEnemy(EnemyKind.Camera, new V2(6.5f, 1.5f), MathF.PI);
        Steps(r5, 3);
        Check(!r5.Alarm, "spoofed cameras ignore you");
    }

    private void Noise()
    {
        LevelPlan p = Site(
            "###############",
            "#P.....+......#",
            "#......d......#",
            "###############");
        Run r = Make(p);
        Actor g = r.AddEnemy(EnemyKind.Guard, new V2(10.5f, 2.5f), 0);
        r.Noise(new V2(2, 2), 20, true);
        Check(g.State is AiState.Alert or AiState.Suspicious, "gunfire brings guards");
        Run r2 = Make(Site(
            "###############",
            "#P.....+......#",
            "#......d......#",
            "###############"));
        Actor g2 = r2.AddEnemy(EnemyKind.Guard, new V2(10.5f, 2.5f), 0);
        r2.Noise(new V2(2, 2), 3, true);
        Eq(g2.State, AiState.Idle, "a suppressed shot far away goes unheard");
        // Alerted guards hunt: a guard who heard you comes to the door.
        Steps(r, 4, new RunInput { Slot = -1, Aim = new V2(5, 1.5f) });
        Check(g.Pos.X < 10, "an alerted guard moves towards you");
    }

    private void Fighting()
    {
        LevelPlan p = Site(
            "##############",
            "#P...........#",
            "#............#",
            "##############");
        Career c = TestCareer();
        c.Weapons.Add("vanta");
        c.Primary = "vanta";
        Run r = Make(p, JobType.Datasteal, c);
        Actor g = r.AddEnemy(EnemyKind.Ganger, new V2(9.5f, 1.5f), MathF.PI);
        for (int i = 0; i < 600 && !g.Dead && r.Phase == RunPhase.Playing; i++) r.Step(new RunInput { Slot = 0, Fire = true, Aim = g.Pos });
        Check(g.Dead, "a rifle kills a ganger");
        Eq(r.Kills, 1, "the kill is counted");
        // The enemy fights back.
        Run r2 = Make(p);
        Actor h = r2.AddEnemy(EnemyKind.Heavy, new V2(8.5f, 1.5f), MathF.PI);
        r2.BecomeAlert(h, r2.Player.Pos);
        Steps(r2, 6);
        Check(r2.Player.Hp < r2.Player.MaxHp, "an alerted heavy shoots you");
        // Melee on an unaware guard is a takedown.
        Run r3 = Make(p);
        Actor t = r3.AddEnemy(EnemyKind.Guard, r3.Player.Pos + new V2(0.9f, 0), 0);
        r3.Step(new RunInput { Slot = 2, Aim = t.Pos });
        for (int i = 0; i < 20; i++) r3.Step(new RunInput { Slot = -1, Aim = t.Pos });
        r3.Step(new RunInput { Slot = -1, Fire = true, Aim = t.Pos });
        Check(t.Dead || t.Hp < t.MaxHp * 0.3f, "a blade from behind nearly or wholly kills");
        // Dash dodges bullets.
        Run r4 = Make(p);
        float hp = r4.Player.Hp;
        r4.Step(new RunInput { Slot = -1, Dash = true, Move = new V2(0, 1) });
        Check(r4.DashT > 0, "dash starts");
        r4.Hurt(r4.Player, 50, null, false);
        Eq(r4.Player.Hp, hp, "no bullet damage while dashing");
    }

    private void Grenades()
    {
        LevelPlan p = Site(
            "##############",
            "#P...........#",
            "#............#",
            "#............#",
            "##############");
        Run r = Make(p);
        r.Nades["frag"] = 2;
        r.NadeSel = "frag";
        Actor g = r.AddEnemy(EnemyKind.Guard, new V2(6.5f, 2.5f), MathF.PI);
        r.Step(new RunInput { Slot = -1, ThrowRelease = true, Aim = g.Pos });
        Eq(r.Grenades.Count, 1, "a grenade is in the air");
        Eq(r.Nades["frag"], 1, "and one fewer carried");
        Steps(r, 2.5f, new RunInput { Slot = -1, Aim = g.Pos });
        Check(r.Grenades.Count == 0, "it went off");
        Check(g.Dead || g.Hp < g.MaxHp * 0.6f, "a frag at a guard's feet hurts badly");
        // A breach charge opens concrete.
        LevelPlan q = Site(
            "##########",
            "#..c.....#",
            "#P.c.....#",
            "#..c.....#",
            "##########");
        Run r2 = Make(q);
        r2.Player.Pos = new V2(2.6f, 2.5f);
        r2.Nades["breach"] = 1;
        r2.NadeSel = "breach";
        r2.Step(new RunInput { Slot = -1, ThrowRelease = true, Aim = new V2(4, 2.5f) });
        r2.Player.Pos = new V2(1.3f, 1.3f);
        Steps(r2, 3);
        Eq(q.L[3, 2], Tile.Rubble, "a breach charge blows through concrete");
        // EMP downs drones.
        Run r3 = Make(p);
        r3.Nades["emp"] = 1;
        r3.NadeSel = "emp";
        Actor d = r3.AddEnemy(EnemyKind.Drone, new V2(6.5f, 2.5f), 0);
        Actor t = r3.AddEnemy(EnemyKind.Turret, new V2(7.5f, 1.5f), 0);
        r3.Step(new RunInput { Slot = -1, ThrowRelease = true, Aim = new V2(6.5f, 2.2f) });
        Steps(r3, 2.5f);
        Check(d.Dead, "EMP kills a drone");
        Check(t.Stun > 0, "EMP stuns a turret");
    }

    private void Abilities()
    {
        LevelPlan p = Site(
            "##############",
            "#P...........#",
            "#.......L....#",
            "##############");
        Career c = TestCareer();
        Run r = Make(p, JobType.Datasteal, c);
        Eq(c.SlotQ, Ability.Overclock, "the samurai starts with Overclock on Q");
        r.Step(new RunInput { Slot = -1, AbilityQ = true });
        Check(r.OverclockT > 0 && r.WorldScale < 1, "Overclock slows the world");
        Check(r.AbilityCd[Ability.Overclock] > 0, "and goes on cooldown");
        Career d = Career.New("D", "decker", 3);
        Eq(d.SlotQ, Ability.Hack, "the decker starts with Hack");
        Run r2 = Make(p, JobType.Datasteal, d);
        Actor t = r2.AddEnemy(EnemyKind.Turret, new V2(5.5f, 1.5f), 0);
        r2.Step(new RunInput { Slot = -1, AbilityQ = true, Aim = t.Pos });
        Eq(t.Team, 0, "a hacked turret changes sides");
        Run r3 = Make(p, JobType.Datasteal, d);
        r3.Step(new RunInput { Slot = -1, AbilityQ = true, Aim = new V2(8.5f, 2.5f) });
        Eq(p.L[8, 2], Tile.Door, "Hack opens a maglock at range");
    }

    private void Magic()
    {
        LevelPlan p = Site(
            "##############",
            "#P...........#",
            "#............#",
            "##############");
        Career mage = Career.New("M", "mage", 4);
        Check(mage.Awakened && mage.Abilities.Contains(Ability.Manabolt) && mage.Abilities.Contains(Ability.Heal), "the mage starts with Manabolt and Heal");
        Run r = Make(p, JobType.Datasteal, mage);
        Actor g = r.AddEnemy(EnemyKind.Heavy, new V2(8.5f, 1.5f), 0);
        float before = g.Hp;
        r.Step(new RunInput { Slot = -1, AbilityQ = true, Aim = g.Pos });
        Check(before - g.Hp >= 40, $"Manabolt ignores a heavy's armour ({before - g.Hp:0} damage)");
        Check(r.AbilityCd[Ability.Manabolt] > 0, "and goes on cooldown");
        Actor d = r.AddEnemy(EnemyKind.Drone, new V2(6.5f, 2.5f), 0);
        r.AbilityCd[Ability.Manabolt] = 0;
        r.Step(new RunInput { Slot = -1, AbilityQ = true, Aim = d.Pos });
        Eq(d.Hp, d.MaxHp, "Manabolt does nothing to machines");
        r.Player.Hp = 20;
        r.Step(new RunInput { Slot = -1, AbilityF = true });
        Check(r.Player.Hp > 50, "Heal heals");
        float power = mage.SpellPower;
        mage.Install("wired");
        Check(mage.SpellPower < power, "chrome weakens magic");
        Career sam = TestCareer();
        sam.Cash = 99999;
        Check(sam.Offers("talismonger").All(o => o.Block != null), "the talismonger teaches only the Awakened");
        mage.Cash = 99999;
        Check(mage.Offers("talismonger").First(o => o.Id == "manabolt").Block == "Known.", "known spells are marked");
        mage.Loyalty["talismonger"] = 5;
        Check(mage.Buy(mage.Offers("talismonger").First(o => o.Id == "fireball")) == null && mage.Abilities.Contains(Ability.Fireball), "a mage learns Fireball");
        LevelPlan q = Site(
            "##############",
            "#P...........#",
            "#............#",
            "##############");
        mage.SlotQ = Ability.Fireball;
        Run rf = Make(q, JobType.Datasteal, mage);
        Actor t = rf.AddEnemy(EnemyKind.Guard, new V2(7.5f, 1.5f), 0);
        rf.Step(new RunInput { Slot = -1, AbilityQ = true, Aim = t.Pos });
        Steps(rf, 1.5f, new RunInput { Slot = -1, Aim = t.Pos });
        Check(t.Hp < t.MaxHp, "a fireball burns a guard");
    }

    private void Bodies()
    {
        LevelPlan p = Site(
            "###############",
            "#P.....c......#",
            "#......c......#",
            "#.............#",
            "###############");
        Run r = Make(p);
        Actor dead = r.AddEnemy(EnemyKind.Guard, new V2(10.5f, 3.5f), 0);
        Actor walker = r.AddEnemy(EnemyKind.Guard, new V2(12.5f, 1.5f), MathF.PI / 2);
        walker.BaseFacing = MathF.PI / 2;
        r.Hurt(dead, 9999, r.Player, false);
        Steps(r, 1.5f);
        Check(walker.Alert, "a guard who sees a body raises the alert");
        // Armour: past it, a share always gets through -- smaller the heavier the armour, never under 8 %.
        Actor plated = r.AddEnemy(EnemyKind.Guard, new V2(3.5f, 3.5f), 0);
        plated.MaxHp = plated.Hp = 1000;
        foreach (var (arm, share) in new[] { (0, 1f), (20, 0.125f), (40, 0.08f) })
        {
            plated.Armor = arm;
            float hp = plated.Hp;
            r.Hurt(plated, 10, null, false);
            Check(MathF.Abs(hp - plated.Hp - 10 * share) < 0.01f, $"armour {arm}: {hp - plated.Hp:0.00} of 10 gets through");
        }
    }

    private void EnemyGrenades()
    {
        LevelPlan p = Site(
            "###############",
            "#P...c........#",
            "#....c........#",
            "#....c........#",
            "###############");
        Run r = Make(p);
        Job hard = TestJob();
        hard.Threat = 4;
        var rr = new Run(hard, TestCareer(), p);
        Actor g = rr.AddEnemy(EnemyKind.Guard, new V2(9.5f, 2.5f), MathF.PI);
        Check(g.Nades == 1, "guards carry a grenade on hard jobs");
        rr.BecomeAlert(g, rr.Player.Pos);
        rr.Player.MaxHp = rr.Player.Hp = 1e6f;
        bool thrown = false;
        for (int i = 0; i < 600 && !thrown; i++) { rr.Step(new RunInput { Slot = -1 }); thrown = rr.Events.Any(e => e.Kind == Ev.EnemyThrow); rr.Events.Clear(); }
        Check(thrown, "a guard throws at you behind cover");
    }

    private void Conditions()
    {
        string[] rows =
        {
            "###############",
            "#P............#",
            "#.............#",
            "###############",
        };
        Job j = TestJob(JobType.Eliminate);
        j.Mods.Add(RunMod.Blackout);
        var r = new Run(j, TestCareer(), Site(rows));
        Actor cam = r.AddEnemy(EnemyKind.Camera, new V2(6.5f, 1.5f), MathF.PI);
        var clear = new Run(TestJob(), TestCareer(), Site(rows));
        Check(r.FovRadius < clear.FovRadius, "a blackout shortens your sight");
        Check(r.VisionMul < 1, "and theirs");
        Job dl = TestJob(JobType.Eliminate);
        dl.Mods.Add(RunMod.Deadline);
        dl.Deadline = 2;
        var rd = new Run(dl, TestCareer(), Site(rows));
        rd.Vip = rd.AddEnemy(EnemyKind.Vip, new V2(12.5f, 2.5f), 0);
        rd.Player.Pos = new V2(6.5f, 1.5f);
        Steps(rd, 3);
        Check(rd.ObjectiveLost && rd.Vip.Escaped, "past the deadline the target is gone");
        Job el = TestJob();
        el.Mods.Add(RunMod.Elite);
        var re = new Run(el, TestCareer(), Site(rows));
        Actor eg = re.AddEnemy(EnemyKind.Guard, new V2(9.5f, 2.5f), 0);
        Check(eg.Armor > Tables.Enemy(EnemyKind.Guard).Armor, "elite guards are better armoured");
        // Generated jobs carry their conditions in the brief.
        City city = City.Generate(55);
        Career c = TestCareer();
        var jobs = Enumerable.Range(0, 120).Select(_ => c.MakeJob(city)).Where(x => x != null).ToList();
        Check(jobs.Any(x => x.Mods.Count > 0), "some jobs come with conditions");
        Check(jobs.Where(x => x.Mods.Count > 0).All(x => x.Brief.Contains("[")), "conditions are in the brief");
        Check(jobs.Where(x => x.Has(RunMod.Deadline)).All(x => x.Deadline > 60), "deadlines are sane");
    }

    private void NonLethal()
    {
        LevelPlan p = Site(
            "##############",
            "#P...........#",
            "#............#",
            "##############");
        Career c = TestCareer();
        c.Weapons.Add("baton");
        c.Melee = "baton";
        Run r = Make(p, JobType.Datasteal, c);
        Actor t = r.AddEnemy(EnemyKind.Guard, r.Player.Pos + new V2(0.9f, 0), 0);
        r.Step(new RunInput { Slot = 2, Aim = t.Pos });
        for (int i = 0; i < 20; i++) r.Step(new RunInput { Slot = -1, Aim = t.Pos });
        for (int i = 0; i < 90 && !t.Dead; i++) r.Step(new RunInput { Slot = -1, Fire = true, Aim = t.Pos });
        Check(t.Dead && t.KO, "a stun baton knocks out");
        Eq(r.Kills, 0, "a knockout is not a kill");
        Eq(r.KnockOuts, 1, "it is a knockout");
        Career tz = TestCareer();
        tz.Weapons.Add("taser");
        tz.Primary = "taser";
        Run r2 = Make(p, JobType.Datasteal, tz);
        Actor g = r2.AddEnemy(EnemyKind.Guard, new V2(6.5f, 1.5f), MathF.PI);
        for (int i = 0; i < 40 && g.Stun <= 0; i++) r2.Step(new RunInput { Slot = 0, Fire = true, Aim = g.Pos });
        Check(g.Stun > 0, "a taser stuns");
        // The no-kill bonus.
        City city = City.Generate(55);
        Career k = TestCareer();
        k.CityRef = city;
        k.RefreshBoard(city);
        Job j = k.Board.First(x => x.Type is not (JobType.Eliminate or JobType.Clear or JobType.Courier));
        Accept(k, j);
        int cash = k.Cash;
        Debrief d = k.Finish(j, new RunResult { Success = true, Alarm = true, Kills = 0, Seconds = 60, GrenadesLeft = k.RunGrenades(), MedkitsLeft = k.RunMedkits() });
        Check(d.Lines.Any(l => l.what.StartsWith("No-kill")), "no kills pays a bonus");
        // Not where it means nothing: a courier raises no alarm (no ghost bonus), a hack kills nobody (no no-kill bonus).
        foreach (JobType jt in new[] { JobType.Courier, JobType.Hack })
        {
            Job x = k.Board.First(b => !b.Johnson && b.Story == 0);
            x.Type = jt;
            Accept(k, x);
            Debrief dx = k.Finish(x, new RunResult { Success = true, Alarm = false, Kills = 0, Seconds = 60, GrenadesLeft = k.RunGrenades(), MedkitsLeft = k.RunMedkits() });
            Check(dx.Lines.Any(l => l.what == "Payment") && (jt == JobType.Hack || !dx.Lines.Any(l => l.what.StartsWith("Ghost"))), $"paid, and no ghost bonus on a courier ({jt})");
            Check(jt != JobType.Hack || !dx.Lines.Any(l => l.what.StartsWith("No-kill")), "no no-kill bonus on a hack");
        }
    }

    private void Carrying()
    {
        LevelPlan p = Site(
            "###############",
            "#P.....c......#",
            "#......c......#",
            "#.............#",
            "###############");
        Run r = Make(p);
        Actor dead = r.AddEnemy(EnemyKind.Guard, new V2(2.5f, 3.5f), 0);
        r.Hurt(dead, 9999, r.Player, false);
        r.Player.Pos = new V2(2.5f, 3.0f);
        Steps(r, 0.8f, new RunInput { Slot = -1, Interact = true });
        Check(r.CarriedBody == null, "an E held from before does not grab a body");
        r.Step(new RunInput { Slot = -1, Interact = true, InteractPressed = true });
        Steps(r, 0.8f, new RunInput { Slot = -1, Interact = true });
        Check(r.CarriedBody == dead, "press and hold E by a body to pick it up");
        Steps(r, 1.0f, new RunInput { Slot = -1, Move = new V2(0, -1) });
        Check(dead.Pos.Y < 3.0f, "the body comes with you");
        r.Step(new RunInput { Slot = -1, InteractPressed = true });
        Check(r.CarriedBody == null, "E puts it down");
    }

    private void Lures()
    {
        LevelPlan p = Site(
            "###############",
            "#P............#",
            "#.............#",
            "###############");
        Run r = Make(p);
        Actor g = r.AddEnemy(EnemyKind.Guard, new V2(6.5f, 2.5f), 0);
        r.Step(new RunInput { Slot = -1, Whistle = true });
        Check(g.State == AiState.Suspicious && V2.Distance(g.Goal, r.Player.Pos) < 1, "a whistle brings a guard to look");
        Check(!g.Alert, "but does not alert him");
    }

    private void Fixtures()
    {
        string[] rows =
        {
            "###############",
            "#P.%...&...l..#",
            "#.............#",
            "###############",
        };
        LevelPlan p = Site(rows);
        p.L.Set(3, 1, Tile.Console);
        p.L.Set(7, 1, Tile.Safe);
        p.L.Set(11, 1, Tile.Locker);
        Run r = Make(p);
        Actor cam = r.AddEnemy(EnemyKind.Camera, new V2(13.5f, 2.5f), MathF.PI);
        r.Player.Pos = new V2(3.5f, 2.4f);
        Steps(r, 5, new RunInput { Slot = -1, Interact = true });
        Check(cam.Stun > 1000, "the security console loops the cameras");
        int cash = r.CashFound;
        r.Player.Pos = new V2(7.5f, 2.4f);
        Steps(r, 9, new RunInput { Slot = -1, Interact = true });
        Steps(r, 0.3f, new RunInput { Slot = -1, Move = new V2(0.1f, 0) });
        Check(r.CashFound > cash, "a cracked safe pays");
        int nades = r.Nades.Values.Sum();
        r.Player.Pos = new V2(11.5f, 2.4f);
        Steps(r, 2, new RunInput { Slot = -1, Interact = true });
        Steps(r, 0.3f, new RunInput { Slot = -1, Move = new V2(0.1f, 0) });
        Check(r.Nades.Values.Sum() > nades, "lockers hold grenades");
    }

    private void Ammo()
    {
        Career c = TestCareer();
        c.Cash = 99999;
        c.Loyalty["arms"] = 5;
        c.Buy(c.Offers("arms").First(o => o.Id == "apds"));
        c.Buy(c.Offers("arms").First(o => o.Id == "gel"));
        Check(c.AmmoOwned.Contains("apds") && c.AmmoOwned.Contains("gel"), "ammo types are bought");
        WeaponDef w = Tables.Weapon("hollow9");
        Eq(c.Ammo(w), "standard", "guns start on standard");
        c.AmmoFor["hollow9"] = "gel";
        Check(c.DamageOf(w) < w.Damage, "gel hits softer");
        LevelPlan p = Site(
            "##############",
            "#P...........#",
            "#............#",
            "##############");
        Run r = Make(p, JobType.Datasteal, c);
        Actor g = r.AddEnemy(EnemyKind.Ganger, new V2(5.5f, 1.5f), MathF.PI);
        for (int i = 0; i < 600 && !g.Dead; i++) r.Step(new RunInput { Slot = 1, Fire = true, Aim = g.Pos });
        Check(g.Dead && g.KO, "gel rounds knock out");
        // Smart rounds curve.
        Career s = TestCareer();
        s.Install("smartlink");
        s.Weapons.Add("hornet");
        s.Primary = "hornet";
        Run rs = Make(p, JobType.Datasteal, s);
        Actor t = rs.AddEnemy(EnemyKind.Heavy, new V2(10.5f, 2.2f), MathF.PI);
        rs.Step(new RunInput { Slot = 0, Fire = true, Aim = new V2(10.5f, 1.2f) });
        Check(rs.Shots.Any(x => x.Homing), "a smartlinked Hornet fires homing rounds");
        // Spur blades take the knife's place, at their own pace.
        Career sp = TestCareer();
        sp.Weapons.Add("katana");
        sp.Melee = "katana";
        sp.Install("spurs");
        Check(sp.Melee == "knife" && sp.Wield("knife").Name == "Spur blades" && sp.Wield("knife").Rate == Tables.SpurRate, "spurs become your melee, at 1.6 a second");
        Run rsp = Make(p, JobType.Datasteal, sp);
        Check(rsp.MeleeDef.Name == "Spur blades" && rsp.MeleeDef.Rate == Tables.SpurRate, "and that's what you swing on a run");
        Check(MathF.Abs(sp.Dps(sp.Wield("knife")) - sp.DamageOf(sp.Wield("knife")) * Tables.SpurRate) < 0.01f && sp.DamageOf(sp.Wield("knife")) > Tables.SpurDamage - 0.01f, "the shop's DPS is what you really do");
        Check(sp.Offers("arms").First(o => o.Id == "monowhip").Desc.Contains("vs your Spur blades"), "and compares against the spurs");
    }

    private void Shields()
    {
        LevelPlan p = Site(
            "##############",
            "#P...........#",
            "#............#",
            "##############");
        Career c = TestCareer();
        c.Weapons.Add("vanta");
        c.Primary = "vanta";
        Run r = Make(p, JobType.Datasteal, c);
        Actor s = r.AddEnemy(EnemyKind.Shield, new V2(8.5f, 1.5f), MathF.PI);
        s.State = AiState.Idle;
        for (int i = 0; i < 60; i++) { r.Step(new RunInput { Slot = 0, Fire = true, Aim = s.Pos }); s.Facing = M.Angle(r.Player.Pos - s.Pos); s.Pos = new V2(8.5f, 1.5f); }
        Check(s.Hp > s.MaxHp * 0.7f, $"a shield soaks fire from the front ({s.Hp:0}/{s.MaxHp:0})");
        float hp = s.Hp;
        for (int i = 0; i < 60; i++) { r.Step(new RunInput { Slot = 0, Fire = true, Aim = s.Pos }); s.Facing = 0; s.Pos = new V2(8.5f, 1.5f); }
        Check(s.Hp < hp - 30 || s.Dead, "but not from behind");
    }

    private void Riggers()
    {
        LevelPlan p = Site(
            "##############",
            "#P...........#",
            "#............#",
            "##############");
        Run r = Make(p);
        Actor g = r.AddEnemy(EnemyKind.Rigger, new V2(9.5f, 2.5f), MathF.PI);
        int before = r.Actors.Count(a => a.Is(EnemyKind.Drone));
        r.BecomeAlert(g, r.Player.Pos);
        r.Player.MaxHp = r.Player.Hp = 1e6f;
        Steps(r, 1.5f);
        Check(r.Actors.Count(a => a.Is(EnemyKind.Drone)) == before + 2, "an alerted rigger launches two drones");
    }

    private void Bosses()
    {
        var job = new Job { Id = 1, Type = JobType.Clear, Style = SiteStyle.Den, Threat = 5, Seed = 4545, Faction = Faction.Vipers, Target = "T", Item = "I", Title = "x" };
        LevelPlan plan = LevelGen.Generate(job);
        Check(plan.Enemies.Count(e => e.Kind == EnemyKind.Boss) == 1, "threat-5 sites have one boss");
        var run = new Run(job, TestCareer(), plan);
        Check(run.Boss != null && run.Boss.Name == "Big Mamba" && run.Boss.Gun.Def.Id == "e_shotgun", "the Vipers' boss is Big Mamba with a shotgun");
        LevelPlan p = Site(
            "################",
            "#P.............#",
            "#..............#",
            "################");
        Job j = TestJob();
        j.Threat = 5;
        var r = new Run(j, TestCareer(), p);
        Actor b = r.AddEnemy(EnemyKind.Boss, new V2(8.5f, 1.5f), MathF.PI);
        r.Boss = b;
        r.BecomeAlert(b, r.Player.Pos);
        b.ReactT = 0;
        b.ChargeCd = 0;
        r.Player.MaxHp = r.Player.Hp = 1e6f;
        bool charged = false;
        for (int i = 0; i < 300 && !charged; i++) { r.Step(new RunInput { Slot = -1 }); charged = r.Events.Any(e => e.Kind == Ev.Telegraph && e.Text == "charge"); r.Events.Clear(); }
        Check(charged, "the boss winds up a charge");
        r.Hurt(b, 1e6f, r.Player, false);
        Check(r.Pickups.Any(k => k.Kind == PickupKind.Loot && k.Name.Contains("Big") || k.Kind == PickupKind.Loot), "a downed boss drops their chrome");
    }

    private void Allies()
    {
        LevelPlan p = Site(
            "###############",
            "#P............#",
            "#.............#",
            "#.............#",
            "###############");
        Job j = TestJob();
        j.Hired = "ox";
        var r = new Run(j, TestCareer(), p);
        Check(r.Ally != null && r.Ally.Team == 0 && r.Ally.Name == "Ox", "a hired runner comes along");
        Actor g = r.AddEnemy(EnemyKind.Ganger, new V2(6.5f, 2.5f), 0);
        g.BaseFacing = 0;
        Steps(r, 1);
        Eq(g.Hp, g.MaxHp, "the hire holds fire while nobody has noticed you");
        r.BecomeAlert(g, r.Player.Pos);
        r.Player.MaxHp = r.Player.Hp = 1e6f;
        Steps(r, 6);
        Check(g.Hp < g.MaxHp, "and fights once it is loud");
        r.Player.Pos = new V2(12.5f, 3.5f);
        Steps(r, 4);
        Check(V2.Distance(r.Ally.Pos, r.Player.Pos) < 4, "and keeps up");
        // The sniper shoots too.
        Job jw = TestJob();
        jw.Hired = "wisp";
        var rw = new Run(jw, TestCareer(), Site(
            "#################",
            "#P..............#",
            "#...............#",
            "#...............#",
            "#################"));
        Actor gw = rw.AddEnemy(EnemyKind.Ganger, new V2(12.5f, 2.5f), MathF.PI);
        rw.BecomeAlert(gw, rw.Player.Pos);
        rw.Player.MaxHp = rw.Player.Hp = 1e6f;
        Steps(rw, 6);
        Check(gw.Hp < gw.MaxHp, $"Wisp the sniper fires at an alerted enemy (ganger hp {gw.Hp:0}/{gw.MaxHp:0})");
        // Patch heals.
        Job jp = TestJob();
        jp.Hired = "patch";
        var rp = new Run(jp, TestCareer(), p);
        rp.Player.Hp = 20;
        Steps(rp, 1);
        Check(rp.Player.Hp > 50 && rp.Ally.Heals == 2, "the medic patches you up");
        // The cut comes off the pay.
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        c.RefreshBoard(city);
        Job job = c.Board.First(x => x.Type != JobType.Courier);
        Accept(c, job);
        c.Loyalty["fixer"] = 5;
        Check(c.Buy(c.Offers("fixer").First(o => o.Kind == ShopKind.Hire && o.Id == "wisp")) == null && job.Hired == "wisp", "hire at the fixer");
        int cash = c.Cash;
        c.Finish(job, new RunResult { Success = true, Alarm = true, Kills = 1, Seconds = 30, GrenadesLeft = c.RunGrenades(), MedkitsLeft = c.RunMedkits() });
        Eq(c.Cash, cash + job.Pay - (int)(job.Pay * 0.25f) - c.DailyRent, "paid less the hire's cut");
    }

    private void Basements()
    {
        int found = 0;
        for (int seed = 1; seed < 40 && found < 4; seed++)
        {
            var job = new Job { Id = 1, Type = JobType.Retrieve, Style = SiteStyle.Office, Threat = 4, Seed = (ulong)(seed * 977), Faction = Faction.Halcyon, Target = "T", Item = "I", Title = "x" };
            LevelPlan p = LevelGen.Generate(job);
            if (p.Basement.W == 0) continue;
            found++;
            Check(p.L.Stair.Count == 2, "a basement has a pair of stairs");
            Check(p.Rooms.Any(r => r.R.X >= p.MainW), "and rooms down there");
            PickupSpawn o = p.Pickups.First(k => k.Kind == PickupKind.Objective);
            Check(LevelGen.Reachable(p, Pt.Of(o.Pos)), "the objective is reachable, stairs included");
            if (found == 1)
            {
                // Walk onto the stairs: out at the other end.
                var run = new Run(job, TestCareer(), p);
                Pt top = p.L.PtOf(p.L.Stair.Keys.First(k => p.L.PtOf(k).X < p.MainW));
                run.Player.Pos = top.Centre;
                run.Step(new RunInput { Slot = -1 });
                Check(run.Downstairs, "stairs take you down");
                Pt topEnd = top;
                var up = new Run(job, TestCareer(), LevelGen.Generate(job));
                Check(up.FloorNote() != null && up.NavTarget() is V2 nt && Pt.Of(nt).X < p.MainW, "the marker points upstairs-side at the stairs when the objective is below");
                // An alerted guard follows you down.
                Actor g = run.Actors.First(a => a.Hostile && a.Def != null && !a.Static && a.Pos.X < p.MainW);
                run.Player.MaxHp = run.Player.Hp = 1e6f;
                run.RaiseAlarm("test");
                for (int i = 0; i < 60 * 40 && g.Pos.X < p.MainW && !g.Dead; i++) run.Step(new RunInput { Slot = -1 });
                Check(g.Pos.X >= p.MainW, "hunting guards take the stairs too");
            }
        }
        Check(found >= 2, $"high-threat sites sometimes have a basement ({found})");
    }

    /// <summary>Things the agent playtest found, kept fixed.</summary>
    private void PlaytestFixes()
    {
        Career c = TestCareer();
        Check(c.TraumaLinks == 1, "a new career has one TraumaLink on file");
        // Wrecking is never cheaper than repairing.
        c.Cash = 3000;
        c.CarHp = 20;
        int repair = c.RepairPrice(false);
        Check(c.TowFee >= 300 && c.TowFee <= c.Cash / 2, $"the tow ({c.TowFee}) costs, but never more than half your cash");
        c.Cash = 390;
        Check(c.TowFee <= 195, "a broke runner keeps something after a tow");
        c.Cash = 3000;
        Check(repair < 1000, $"a near-total repair is affordable ({repair})");
        Offer patch = c.Offers("home").FirstOrDefault(o => o.Id == "patch");
        Check(patch != null && patch.Price == 0, "a badly hurt car can be patched up free at home");
        c.Buy(patch);
        Check(c.CarHp >= Tables.Vehicle(c.Vehicle).Hp * 0.6f - 1, "to 60 % hull");
        // Spending earns loyalty.
        c.Cash = 50000;
        int loy = c.Loyalty["arms"];
        foreach (string id in new[] { "kestrel", "breacher", "whisper", "jacket" }) c.Buy(c.Offers("arms").First(o => o.Id == id));
        Check(c.Loyalty["arms"] > loy, "spending at a contact earns loyalty");
        // Getting hit interrupts a held action.
        LevelPlan p = Site(
            "##############",
            "#P...L.......#",
            "#............#",
            "##############");
        Run r = Make(p, JobType.Datasteal, c);
        r.Player.Pos = new V2(4.5f, 2.3f);
        Steps(r, 0.5f, new RunInput { Slot = -1, Interact = true });
        float progress = r.HackProgress;
        r.Hurt(r.Player, 20, null, false);
        Steps(r, 0.3f, new RunInput { Slot = -1, Interact = true });
        Check(progress > 0 && r.HackProgress < progress, "a hit breaks a held hack");
        // The eliminate target's last known spot is marked.
        Run el = Make(Site(
            "##############",
            "#P......c....#",
            "#.......c....#",
            "##############"), JobType.Eliminate);
        el.Vip = el.AddEnemy(EnemyKind.Vip, new V2(11.5f, 1.5f), 0);
        el.Step(new RunInput { Slot = -1 });
        Check(el.ObjectivePos() is V2 hint && V2.Distance(hint, el.Vip.Home) < 0.1f, "the target's last known spot is marked");
        // A run in progress is remembered, so a quit can be settled as a bail-out.
        City city = City.Generate(55);
        Career k = TestCareer();
        k.CityRef = city;
        k.RefreshBoard(city);
        Job j = k.Board[0];
        Accept(k, j);
        k.InRun = j.Id;
        Career back = Career.FromJson(k.ToJson());
        Eq(back.InRun, j.Id, "the run in progress is saved");
        int rep = back.Rep;
        back.Finish(back.Board.First(x => x.Id == back.InRun), new RunResult { Aborted = true, GrenadesLeft = back.RunGrenades(), MedkitsLeft = back.RunMedkits() });
        Check(back.InRun < 0 && back.ActiveJob < 0, "settling it closes the job");
    }

    private void Hubs()
    {
        City city = City.Generate(55);
        foreach (var h in City.Hubs) Check(city.Place(h.id)?.District == h.district, $"the {h.name} is in the {h.district}");
        Career c = TestCareer();
        c.CityRef = city;
        c.Cash = 5000;
        c.Heat = 2;
        c.RefreshBoard(city);
        Accept(c, c.Board.First(x => x.Type != JobType.Courier));
        var job = new Job { Id = -1, Type = JobType.Hub, Title = "Market", Lot = city.Place("market").Index, Seed = 3, Threat = 1, Faction = Faction.Lotus, Style = SiteStyle.Club };
        LevelPlan p = HubGen.Generate(3, "market");
        var r = new Run(job, c, p);
        Check(r.Peaceful && !r.Actors.Any(a => a.Hostile), "a hub has nobody hostile");
        Check(r.Actors.Count(a => a.IsNpc && a.Vendor != null) == HubGen.Vendors.Length, "every vendor is there");
        Check(r.Actors.Count(a => a.IsNpc && a.Vendor == null) > 10, "and a crowd");
        foreach (var (vid, _, _) in HubGen.Vendors)
        {
            Actor v = r.Actors.First(a => a.Vendor == vid);
            Pt near = Pt.Dirs8.Select(d => Pt.Of(v.Pos) + d).First(q => !p.L.Solid(q.X, q.Y));
            r.Player.Pos = near.Centre;
            r.Step(new RunInput { Slot = -1, InteractPressed = true, Interact = true });
        }
        Check(c.WellFed && c.Active.Schematics && c.Heat == 0 && c.Cash < 5000, "the noodle stand, the broker and the informant all deliver");
        r.Step(new RunInput { Slot = -1, Fire = true, Aim = r.Player.Pos + new V2(2, 0) });
        Check(r.Shots.Count == 0, "no shooting in the market");
        Check(c.MaxHp > TestCareer().MaxHp, "a good meal means more HP next run");
        // Walk out, then back to the car.
        r.Player.Pos = new V2(8.5f, p.Entry.Y + 0.5f);
        r.Step(new RunInput { Slot = -1 });
        r.Player.Pos = p.Entry.Centre;
        Steps(r, 0.2f);
        Check(r.Phase == RunPhase.Extracted, "walking back to the car leaves");
    }

    private void Johnsons()
    {
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        var jobs = Enumerable.Range(0, 200).Select(_ => c.MakeJob(city)).Where(x => x != null).ToList();
        Check(jobs.Any(x => x.Johnson), "some jobs come through a Mr. Johnson");
        Check(jobs.Any(x => x.DoubleCross) && jobs.Where(x => x.DoubleCross).All(x => x.Johnson), "a few are set-ups, always Johnsons");
        Job j = jobs.First(x => x.Johnson);
        c.Board.Add(j);
        Check(c.Accept(j) != null, "a Johnson job must be negotiated first");
        int pay = j.Pay;
        string said = c.Haggle(j);
        Check(said != null && (j.Pay != pay || !c.Board.Contains(j)), "haggling changes the deal");
        Check(c.Haggle(j).StartsWith("No more"), "only once");
        if (!j.DoubleCross)
        {
            Check(c.Flag("haggled:" + j.JohnsonId) > 0, "and the Johnson remembers it");
            Job again = Enumerable.Range(0, 400).Select(_ => c.MakeJob(city)).FirstOrDefault(x => x != null && x.JohnsonId == j.JohnsonId);
            Check(again != null && again.Brief.Contains("remembers you haggled") && c.Flag("haggled:" + j.JohnsonId) == 0, "their next offer opens 10 % lower, once");
        }
        // Nobody wins every haggle.
        int sure = 0;
        for (int k = 0; k < 200; k++)
        {
            Career h = TestCareer();
            h.Rng = new Rng((ulong)k + 501);
            h.Attrs[(int)Attr.Charisma] = 10;
            h.Loyalty["fixer"] = 5;
            var hj = new Job { Id = 1, Johnson = true, Pay = 1000, Title = "x" };
            h.Board.Add(hj);
            h.Haggle(hj);
            if (hj.Pay > 1000) sure++;
        }
        Check(sure < 195 && sure > 140, $"the best talker still loses one in seven or so ({sure}/200)");
        // Haggling well: high Charisma wins more often.
        int wins = 0;
        for (int k = 0; k < 40; k++)
        {
            Career h = TestCareer();
            h.Rng = new Rng((ulong)k + 9);
            h.Attrs[(int)Attr.Charisma] = 6;
            var hj = new Job { Id = 1, Johnson = true, Pay = 1000, Title = "x" };
            h.Board.Add(hj);
            h.Haggle(hj);
            if (hj.Pay > 1000) wins++;
        }
        Check(wins > 20, $"a silver tongue usually gets more ({wins}/40)");
        // The set-up.
        string[] rows =
        {
            "###############",
            "#P...........r#",
            "#.............#",
            "###############",
        };
        Job dc = TestJob(JobType.Destroy);
        dc.Johnson = dc.DoubleCross = true;
        var run = new Run(dc, TestCareer(), Site(rows));
        int before = run.Actors.Count(a => a.Hostile);
        run.DamageTile(new Pt(13, 1), 999, default);
        run.Player.Pos = new V2(8.5f, 2.5f);
        run.Step(new RunInput { Slot = -1 });
        Check(run.Actors.Count(a => a.Hostile) > before && run.Alarm, "a double-cross springs a squad when the job is done");
    }

    private void Grudges()
    {
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        c.RefreshBoard(city);
        Job j = c.Board.First(x => x.Type != JobType.Courier);
        Accept(c, j);
        Debrief dj = c.Finish(j, new RunResult { Success = true, Alarm = true, GrenadesLeft = c.RunGrenades(), MedkitsLeft = c.RunMedkits() });
        Eq(c.Grudge.GetValueOrDefault(j.Faction), 1, "a job against a faction earns a grudge");
        Check(dj.Lines.Any(l => l.what.EndsWith("grudge") && l.amount.StartsWith("+1") && l.amount.Contains("now 1")), "and the debrief says so, with where it leads");
        // A ghost leaves nobody to hold it against you.
        Job q = c.Board.First(x => !x.Driving && x.Type != JobType.Bounty && !x.Johnson);
        int before = c.Grudge.GetValueOrDefault(q.Faction);
        Accept(c, q);
        Debrief dq = c.Finish(q, new RunResult { Success = true, Alarm = false, Kills = 0, GrenadesLeft = c.RunGrenades(), MedkitsLeft = c.RunMedkits() });
        Eq(c.Grudge.GetValueOrDefault(q.Faction), before, "no alarm, no kills: no grudge");
        Check(dq.Lines.Any(l => l.what.EndsWith("grudge") && l.amount.StartsWith("+0")), "and the debrief says why");
        // Seeing them off a contact's door (or your own) cools them.
        c.Grudge[Faction.Vipers] = 5;
        Job dd = c.OfferSpecial(JobType.Defend, SiteStyle.Den, Faction.Vipers, "barrens", 2, "x", "x", tag: "danger:fence");
        Accept(c, dd);
        c.Finish(dd, new RunResult { Success = true, Alarm = true, Kills = 6, GrenadesLeft = c.RunGrenades(), MedkitsLeft = c.RunMedkits() });
        Eq(c.Grudge[Faction.Vipers], 3, "holding a contact's door: grudge -2");
        c.Grudge[Faction.Halcyon] = 4;
        c.Rep = 15;
        var hal = Enumerable.Range(0, 300).Select(_ => c.MakeJob(city)).Where(x => x != null && x.Faction == Faction.Halcyon && !x.Driving).ToList();
        Check(hal.Count > 0 && hal.All(x => x.Has(RunMod.Elite)), "with a big grudge, their sites get elite security");
        for (int d = 0; d < 14; d++) c.NextDay(city);
        Check(c.Grudge[Faction.Halcyon] < 4, "grudges fade with time");
    }

    private void Spirits()
    {
        LevelPlan p = Site(
            "###############",
            "#P............#",
            "#.............#",
            "###############");
        Career mage = Career.New("M", "mage", 4);
        mage.Spells.Add("summon");
        mage.Spells.Add("astral");
        mage.SlotQ = Ability.Summon;
        mage.SlotF = Ability.Astral;
        Run r = Make(p, JobType.Datasteal, mage);
        Actor g = r.AddEnemy(EnemyKind.Ganger, new V2(9.5f, 2.5f), 0);
        r.Step(new RunInput { Slot = -1, AbilityQ = true });
        Actor spirit = r.Actors.FirstOrDefault(a => a.Summoned);
        Check(spirit != null && spirit.Team == 0, "Summon calls a fire spirit to your side");
        r.Player.MaxHp = r.Player.Hp = 1e6f;
        Steps(r, 6);
        Check(g.Hp < g.MaxHp || g.Dead, "the spirit goes for the enemy");
        Steps(r, 30);
        Check(spirit.Dead, "and fades in time");
        // Astral sight through walls.
        LevelPlan q = Site(
            "###############",
            "#P.....c......#",
            "#......c......#",
            "###############");
        Run ra = Make(q, JobType.Datasteal, mage);
        Actor hid = ra.AddEnemy(EnemyKind.Guard, new V2(10.5f, 1.5f), 0);
        ra.Step(new RunInput { Slot = -1 });
        Check(!ra.Shown(hid), "a guard behind a wall is hidden");
        ra.Step(new RunInput { Slot = -1, AbilityF = true });
        Check(ra.Shown(hid), "astral sight shows him");
        // Hostile spirits shrug off bullets.
        Run rs = Make(p);
        Actor sp = rs.AddEnemy(EnemyKind.Spirit, new V2(6.5f, 1.5f), 0);
        rs.Hurt(sp, 40, rs.Player, false);
        Check(sp.MaxHp - sp.Hp < 15, "bullets barely touch a spirit");
        float hp = sp.Hp;
        rs.Hurt(sp, 40, rs.Player, false, ignoreArmor: true);
        Check(hp - sp.Hp >= 39, "magic does");
    }

    private void Favours()
    {
        Career c = TestCareer();
        c.Heat = 3;
        Check(c.Offers("fixer").First(o => o.Kind == ShopKind.Favour).Block != null, "favours need loyalty");
        c.Loyalty["fixer"] = 2;
        Offer f = c.Offers("fixer").First(o => o.Kind == ShopKind.Favour);
        Check(c.Buy(f) == null && c.Heat == 0, "Juno makes the heat go away");
        c.Heat = 2;
        Check(c.Offers("fixer").First(o => o.Kind == ShopKind.Favour).Block != null, "once every few days");
        c.Loyalty["decker"] = 2;
        c.Buy(c.Offers("decker").First(o => o.Kind == ShopKind.Favour));
        Check(c.NextCamerasDown, "Null queues a camera loop");
        var run = new Run(TestJob(), c, Site("#######", "#P....#", "#######"));
        Actor cam = run.AddEnemy(EnemyKind.Camera, new V2(4.5f, 1.5f), 0);
        var run2 = new Run(TestJob(), c, Site("#######", "#P....#", "#######"));
        Check(run2.Actors.Where(a => a.Is(EnemyKind.Camera)).All(a => a.Stun > 0), "cameras placed on the site are looped");
    }

    private void Injuries()
    {
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        c.RefreshBoard(city);
        float hp = c.MaxHp;
        Job j = c.Board.First(x => x.Type != JobType.Courier);
        Accept(c, j);
        c.Finish(j, new RunResult { Success = true, HpLeft = 0.15f, GrenadesLeft = c.RunGrenades(), MedkitsLeft = c.RunMedkits() });
        Check(c.Injuries == 1 && c.MaxHp < hp, "limping out of a run leaves an injury");
        c.Cash = 9999;
        Offer heal = c.Offers("doc").FirstOrDefault(o => o.Kind == ShopKind.Heal);
        Check(heal != null && c.Buy(heal) == null && c.Injuries == 0, "the doc patches it up");
        c.Injuries = 2;
        c.NextDay(city);
        Eq(c.Injuries, 2, "time on the job does not heal");
        c.NextDay(city, rested: true);
        Check(c.Injuries < 2, "sleeping does");
    }

    private void Legacy()
    {
        var prof = new Profile();
        Career old = TestCareer();
        old.JobsDone = 6;
        old.Day = 25;
        prof.Earn(old);
        Check(prof.Legacy.Contains("stash") && prof.Legacy.Contains("friend") && !prof.Legacy.Contains("legend"), "a long career earns perks");
        Career fresh = TestCareer();
        int cash = fresh.Cash;
        prof.ApplyLegacy(fresh);
        Check(fresh.Cash == cash + 1500 && fresh.Loyalty["fixer"] >= 1, "perks carry into a new career");
    }

    private void Phantoms()
    {
        LevelPlan p = Site(
            "#################",
            "#P..............#",
            "#...............#",
            "#...............#",
            "#################");
        Career c = TestCareer();
        foreach (string id in new[] { "eyes", "smartlink", "dermal", "muscle", "bone", "synth" }) c.Install(id);
        Check(c.Essence < 1.5f, $"heavy chrome ({c.Essence:0.00} essence)");
        Run r = Make(p, JobType.Datasteal, c);
        r.Player.Pos = new V2(8.5f, 2.5f);
        bool saw = false;
        for (int i = 0; i < 60 * 90 && !saw; i++) { r.Step(new RunInput { Slot = -1 }); saw = r.Actors.Any(a => a.IsGhost && !a.Dead); }
        Check(saw, "the low-essence mind sees things");
        Actor g = r.Actors.First(a => a.IsGhost && !a.Dead);
        Check(!g.Hostile, "phantoms are not real enemies");
        Career clean = TestCareer();
        Run rc = Make(p, JobType.Datasteal, clean);
        for (int i = 0; i < 60 * 60; i++) rc.Step(new RunInput { Slot = -1 });
        Check(!rc.Actors.Any(a => a.IsGhost), "a clean mind sees none");
    }

    private void Tutorial()
    {
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        c.RefreshBoard(city);
        Job t = c.Board.FirstOrDefault(j => j.Tutorial);
        Check(t != null && t.Threat == 1 && !t.Johnson, "day 1 offers a gentle tutorial job");
        if (t == null) return;
        LevelPlan plan = LevelGen.Generate(t);
        var r = new Run(t, c, plan);
        bool hint = false;
        for (int i = 0; i < 10 && !hint; i++) { r.Step(new RunInput { Slot = -1 }); hint = r.Events.Any(e => e.Kind == Ev.Hint); }
        Check(hint, "the tutorial talks you through it");
        c.JobsDone = 1;
        c.Board.Clear();
        c.RefreshBoard(city);
        Check(!c.Board.Any(j => j.Tutorial), "and only on the first day");
    }

    private void Bindings()
    {
        Eq(Controls.KeyOf(Act.Dash), Key.Space, "dash defaults to Space");
        Profile.Current.Bindings[Act.Dash] = Key.C;
        Eq(Controls.KeyOf(Act.Dash), Key.C, "rebinding takes effect");
        Check(Controls.Is(new InputEventKey { Keycode = Key.C, PhysicalKeycode = Key.C, Pressed = true }, Act.Dash), "the new key triggers it");
        Check(!Controls.Is(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = true }, Act.Dash), "the old one does not");
        Profile.Current.Bindings.Clear();
        Check(Enum.GetValues<Act>().All(a => Controls.Defaults.ContainsKey(a) && Controls.Names.ContainsKey(a)), "every action has a default key and a name");
    }

    /// <summary>Things the second agent playtest found, kept fixed.</summary>
    private void Playtest2Fixes()
    {
        Career c = TestCareer();
        Check(!c.Offers("arms").Any(o => o.Kind == ShopKind.Weapon && (o.Id.StartsWith("a_") || o.Id.StartsWith("e_") || o.Price == 0)), "no free hire weapons at Sal's");
        // A rack right next to you can be shot.
        LevelPlan p = Site(
            "##########",
            "#P.r.....#",
            "#........#",
            "##########");
        Run r = Make(p, JobType.Destroy, c);
        r.Player.Pos = new V2(1.9f, 1.5f);
        float hp = p.L.Hp[p.L.Idx(3, 1)];
        for (int i = 0; i < 60; i++) r.Step(new RunInput { Slot = 1, Fire = true, Aim = new V2(3.5f, 1.5f) });
        Check(p.L[3, 1] != Tile.Rack || p.L.Hp[p.L.Idx(3, 1)] < hp, "point-blank shots hit the objective");
        // Silencing the spotter calms the alarm meter.
        LevelPlan q = Site(
            "##############",
            "#P...........#",
            "#............#",
            "##############");
        Run ra = Make(q, JobType.Datasteal, c);
        Actor g = ra.AddEnemy(EnemyKind.Guard, new V2(6.5f, 1.5f), MathF.PI);
        ra.BecomeAlert(g, ra.Player.Pos);
        ra.Player.MaxHp = ra.Player.Hp = 1e6f;
        Steps(ra, 2);
        Check(ra.Security > 1 && ra.SecurityLevel >= 1, "a spotter calling it in pushes security up");
        ra.Hurt(g, 1e6f, ra.Player, false);
        float level = MathF.Floor(ra.Security);
        Steps(ra, 10);
        Check(ra.Security >= level && ra.Security < level + 10f / Tables.SecurityTrickle + 0.02f && !ra.Alarm, "silenced: what he hadn't reported fades, but the level he reached stays (security levels never fall)");
        // Police leave when the heat is off.
        City city = City.Generate(777);
        Career k = TestCareer();
        k.Heat = 1.5f;
        var sim = new CitySim(city, k, 1);
        for (int i = 0; i < 60 * 3; i++) sim.Step(1 / 60f, default);
        k.Heat = 0;
        sim.Step(1 / 60f, default);
        sim.Step(1 / 60f, default);
        Check(!sim.Chasers.Any(x => x.Kind == ChaserKind.Police), "no heat, no police");
        // The retainer is paid even when the job kills you.
        City c2 = City.Generate(55);
        Career m = TestCareer();
        m.CityRef = c2;
        m.RefreshBoard(c2);
        Job j = m.Board.First(x => x.Type != JobType.Courier);
        Accept(m, j);
        j.Hired = "ox";
        m.Cash = 1000;
        Debrief d = m.Finish(j, new RunResult { Died = true, GrenadesLeft = m.RunGrenades(), MedkitsLeft = m.RunMedkits() });
        Check(d.Lines.Any(l => l.what.Contains("retainer")), "a failed job still owes the hire's retainer");
    }

    private void Twists()
    {
        // About one run in eight changes its plan.
        int twisted = 0;
        for (int k = 0; k < 400; k++)
        {
            var j = new Job { Id = 1, Type = JobType.Eliminate, Seed = (ulong)(k * 7919 + 3), Threat = 1, Target = "T", Title = "x" };
            if (new Run(j, TestCareer(), Site("#######", "#P....#", "#######")).Twist != Twist.None) twisted++;
        }
        Check(twisted > 20 && twisted < 100, $"twists are rare ({twisted}/400)");
        string[] rows =
        {
            "##################",
            "#P...............#",
            "#................#",
            "#................#",
            "##################",
        };
        // A second target.
        var r = Make(Site(rows), JobType.Eliminate);
        r.Twist = Twist.SecondTarget;
        r.Vip = r.AddEnemy(EnemyKind.Vip, new V2(10.5f, 2.5f), 0);
        r.Player.MaxHp = r.Player.Hp = 1e6f;
        r.Hurt(r.Vip, 1e6f, r.Player, false);
        r.Step(new RunInput { Slot = -1 });
        Check(r.Vip2 != null && !r.ObjectiveDone, "a second target appears after the first");
        r.Hurt(r.Vip2, 1e6f, r.Player, false);
        r.Step(new RunInput { Slot = -1 });
        Check(r.ObjectiveDone && r.PayMul > 1, "both down pays extra");
        // Alive.
        var ra = Make(Site(rows), JobType.Eliminate);
        ra.Twist = Twist.Alive;
        ra.Vip = ra.AddEnemy(EnemyKind.Vip, new V2(4.5f, 2.5f), 0);
        Steps(ra, 0.1f);
        Check(ra.TwistFired, "the client changes their mind");
        ra.Hurt(ra.Vip, 1e6f, ra.Player, false, nonLethal: true);
        ra.Player.Pos = ra.Vip.Pos + new V2(0.3f, 0);
        ra.Step(new RunInput { Slot = -1, Interact = true, InteractPressed = true });
        Steps(ra, 0.8f, new RunInput { Slot = -1, Interact = true });
        Check(ra.CarriedBody == ra.Vip, "carry the knocked-out target");
        Check(!ra.ObjectiveDone, "not done until they are out");
        ra.Player.Pos = ra.Plan.Entry.Centre;
        Steps(ra, 0.3f);
        Check(ra.Phase == RunPhase.Extracted && ra.Success && ra.PayMul > 1.3f, "carried out alive: paid extra");
        // Exfil moved.
        var re = Make(Site(rows), JobType.Destroy);
        re.Twist = Twist.ExfilMoved;
        Rect before = re.Plan.Extraction;
        re.Player.Pos = new V2(10.5f, 2.5f);
        Steps(re, 0.2f);
        Check(re.Plan.Extraction != before, "a compromised exfil moves the extraction");
    }

    private void ContactStories()
    {
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        c.RefreshBoard(city);
        Check(!c.Board.Any(j => j.Arc != null), "no personal stories with strangers");
        c.Loyalty["arms"] = 1;
        c.RefreshBoard(city);
        Job a1 = c.Board.FirstOrDefault(j => j.Arc == "arms");
        Check(a1 != null && a1.ArcPart == 1 && a1.Employer == "arms", "at trust 1, Big Sal asks for a personal favour");
        if (a1 == null) return;
        Accept(c, a1);
        Debrief d = c.Finish(a1, new RunResult { Success = true, GrenadesLeft = c.RunGrenades(), MedkitsLeft = c.RunMedkits() });
        Check(d.Quote != null && d.QuoteBy == "Big Sal", "he has something to say afterwards");
        Eq(c.ArcStep["arms"], 1, "part 1 done");
        for (int part = 2; part <= 3; part++)
        {
            c.Loyalty["arms"] = part;
            c.RefreshBoard(city);
            Job aj = c.Board.FirstOrDefault(j => j.Arc == "arms" && j.ArcPart == part);
            Check(aj != null, $"part {part} opens at trust {part}");
            if (aj == null) return;
            Accept(c, aj);
            c.Finish(aj, new RunResult { Success = true, GrenadesLeft = c.RunGrenades(), MedkitsLeft = c.RunMedkits() });
        }
        Mail fork = c.Inbox.FirstOrDefault(m => m.Story == "fork-arms");
        Check(fork != null && fork.Call && !c.Weapons.Contains("thunder"), "the story ends with a call: what do you want?");
        if (fork != null) c.Choose(fork, 0);
        Check(c.ArcDone("arms") && c.Weapons.Contains("thunder"), "the story ends with Sal's Thunder");
        Check(!c.Offers("arms").Any(o => o.Id == "thunder"), "which is not for sale");
        // Every arc's parts are buildable sites (or a courier run).
        foreach (var (who, arc) in Career.Arcs)
            foreach (var part in arc.parts.Where(x => x.Type != JobType.Courier))
            {
                var job = new Job { Type = part.Type, Style = part.Style, Faction = part.Faction, Threat = part.Threat, Seed = 77, Count = 3, Target = "x", Item = "x", Title = "x" };
                LevelPlan plan = LevelGen.Generate(job);
                Check(plan.Rooms.Count > 0, $"{who}: '{part.Title}' builds a site");
            }
        // The other rewards.
        Career k = TestCareer();
        k.ArcStep["talismonger"] = 3;
        Check(k.ArmorValue == TestCareer().ArmorValue + 2, "Sabine's ward adds armour");
        k.ArcStep["fence"] = 3;
        Eq(k.SellMul, 1f, "Wen pays full value");
    }

    private void Trust()
    {
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        foreach (ContactDef k in Tables.Contacts) c.Loyalty[k.Id] = 4;
        var jobs = Enumerable.Range(0, 200).Select(_ => c.MakeJob(city)).Where(x => x != null && x.Type != JobType.Courier && x.Story == 0).ToList();
        Check(jobs.Any(x => x.Threat > c.MaxThreat), "trusted contacts offer harder work than your rep alone");
        Career s = TestCareer();
        var cold = Enumerable.Range(0, 200).Select(_ => s.MakeJob(city)).Where(x => x != null && x.Type != JobType.Courier).ToList();
        Check(cold.All(x => x.Threat <= s.MaxThreat), "strangers stick to your rep");
        double warm = jobs.Where(x => x.Threat == 1).DefaultIfEmpty().Average(x => x?.Pay ?? 0), chill = cold.Where(x => x.Threat == 1).Average(x => x.Pay);
        Check(warm > chill, $"and pay better ({warm:0} vs {chill:0})");
    }

    private void StatsAndAchievements()
    {
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        c.RefreshBoard(city);
        Job j = c.Board.First(x => x.Type != JobType.Courier);
        Accept(c, j);
        c.Finish(j, new RunResult { Success = true, Alarm = false, Kills = 0, KnockOuts = 3, Walls = 12, GrenadesLeft = c.RunGrenades(), MedkitsLeft = c.RunMedkits() });
        Check(c.Stat($"done.{j.Type}") == 1 && c.Stat("noalarm") == 1 && c.Stat("nokill") == 1 && c.Stat("kos") == 3 && c.Stat("walls") == 12, "a job's numbers are counted");
        var got = Achievements.NewlyEarned(c, new List<string>());
        Check(got.Any(a => a.Id == "first") && got.Any(a => a.Id == "ghost") && got.Any(a => a.Id == "clean"), "and earn achievements");
        Check(!got.Any(a => a.Id == "legend"), "but not ones not earned");
        Check(Achievements.All.Select(a => a.Id).Distinct().Count() == Achievements.All.Length, "achievement ids are unique");
        // Driving is counted.
        var sim = new CitySim(City.Generate(777), c, 1);
        sim.Traffic.Clear();
        int before = c.Stat("tiles");
        for (int i = 0; i < 120; i++) sim.Step(1 / 60f, new DriveInput { Throttle = 1 });
        Check(c.Stat("tiles") > before, "distance driven adds up");
    }

    private void Rigging()
    {
        Career c = TestCareer();
        c.Cash = 99999;
        c.Loyalty["rigger"] = 2;
        Check(c.Buy(c.Offers("rigger").First(o => o.Id == "doberman")) == null && c.DroneSel == "doberman", "Torque sells drones");
        LevelPlan p = Site(
            "######################",
            "#P...........c.......#",
            "#............c.......#",
            "#............c.......#",
            "######################");
        Run r = Make(p, JobType.Datasteal, c);
        Check(r.Drone != null && r.Drone.Team == 0, "the drone comes along");
        V2 body = r.Player.Pos;
        r.Step(new RunInput { Slot = -1, Rig = true });
        Check(r.Rigging, "T jacks into it");
        V2 d0 = r.Drone.Pos;
        Steps(r, 1, new RunInput { Slot = -1, Move = new V2(1, 0), Aim = r.Drone.Pos + new V2(3, 0) });
        Check(r.Drone.Pos.X > d0.X + 2 && V2.Distance(r.Player.Pos, body) < 0.2f, "WASD flies the drone; the body stays put");
        // Seeing through its eyes: past a wall the body cannot see beyond.
        r.Drone.Pos = new V2(15.5f, 2.5f);
        Steps(r, 0.1f, new RunInput { Slot = -1, Aim = new V2(18, 2.5f) });
        Check(r.SeenTile(new Pt(18, 2)), "you see what the drone sees");
        Actor g = r.AddEnemy(EnemyKind.Guard, new V2(18.5f, 2.5f), MathF.PI);
        for (int i = 0; i < 60 && !g.Dead && g.Hp == g.MaxHp; i++) r.Step(new RunInput { Slot = -1, Fire = true, Aim = g.Pos });
        Check(g.Hp < g.MaxHp, "an armed drone shoots where you aim");
        // Too far: signal lost.
        r.Drone.Pos = r.Player.Pos + new V2(r.DroneRange + 2, 0);
        r.Step(new RunInput { Slot = -1 });
        Check(!r.Rigging, "out of range, the signal drops");
        // A hit on the body pulls you out.
        r.Drone.Pos = r.Player.Pos + new V2(1, 0);
        r.Step(new RunInput { Slot = -1, Rig = true });
        r.Hurt(r.Player, 10, null, false);
        Check(!r.Rigging, "a hit on your body pulls you out");
        // Off the stick, what it sees is shown.
        var q = Make(Site(
            "######################",
            "#P...........c.......#",
            "#............c.......#",
            "######################"), JobType.Datasteal, c);
        q.Drone.Pos = new V2(15.5f, 1.5f);
        Actor h = q.AddEnemy(EnemyKind.Guard, new V2(18.5f, 1.5f), 0);
        q.Step(new RunInput { Slot = -1 });
        Check(q.Shown(h), "the drone spots for you");
        // Losing it costs a rebuild.
        City city = City.Generate(55);
        c.CityRef = city;
        c.RefreshBoard(city);
        Job j = c.Board.First(x => x.Type != JobType.Courier);
        Accept(c, j);
        Debrief d = c.Finish(j, new RunResult { Success = true, DroneLost = true, GrenadesLeft = c.RunGrenades(), MedkitsLeft = c.RunMedkits() });
        Check(d.Lines.Any(l => l.what.Contains("Rebuilding")), "a lost drone is rebuilt for a fee");
    }

    /// <summary>Things the third agent playtest found, kept fixed.</summary>
    private void Playtest3Fixes()
    {
        // Traffic goes round a stopped car instead of queueing behind it forever.
        City city = City.Generate(4242);
        var sim = new CitySim(city, TestCareer(), 3);
        sim.Traffic.Clear();
        sim.Peds.Clear();
        var tc = new TrafficCar { Ai = 1, Aj = 1, Bi = 2, Bj = 1, Kind = CarKind.Sedan, Cruise = 7, Colour = 0xffffffff };
        V2 a = city.Node(1, 1), b = city.Node(2, 1), dir = M.Norm(b - a);
        tc.Pos = a + dir * 3 + M.Perp(dir);
        tc.Heading = M.Angle(dir);
        tc.Speed = 7;
        sim.Traffic.Add(tc);
        sim.Player.Pos = a + dir * 8 + M.Perp(dir);
        sim.Player.Heading = M.Angle(dir);
        for (int i = 0; i < 60 * 10; i++)
        {
            sim.Step(1 / 60f, default);
            if (_only == "Playtest3" && i % 30 == 0) GD.Print($"    t{i / 60f:0.0} along {V2.Dot(tc.Pos - sim.Player.Pos, dir):0.0} side {V2.Dot(tc.Pos - sim.Player.Pos, M.Perp(dir)):0.0} v{tc.Speed:0.0} sw{tc.SwerveT:0.0} wait{tc.Wait:0.0} ghost{tc.Ghost:0.0} turn{tc.Turning}");
        }
        Check(V2.Dot(tc.Pos - sim.Player.Pos, dir) > 1 || tc.Bi != 2, "a car behind your stopped car gets round it");
        // An alerted turret fires.
        LevelPlan p = Site(
            "#############",
            "#P..........#",
            "#...........#",
            "#############");
        Run r = Make(p);
        Actor t = r.AddEnemy(EnemyKind.Turret, new V2(6.5f, 1.5f), MathF.PI);
        r.BecomeAlert(t, r.Player.Pos);
        r.Player.MaxHp = r.Player.Hp = 1e6f;
        Steps(r, 3);
        Check(r.Player.Hp < r.Player.MaxHp, "an alerted turret opens fire");
        // Nothing is left inside a wall.
        Run rw = Make(p);
        Actor g = rw.AddEnemy(EnemyKind.Guard, new V2(6.5f, 0.5f), 0);
        rw.Move(g, new V2(0.01f, 0));
        Check(!rw.L.Solid((int)g.Pos.X, (int)g.Pos.Y), "an actor caught in a wall is put back on open ground");
        // Text.
        Career c = TestCareer();
        var j = new Job { Deadline = 175 };
        c.CityRef = city;
        var jobs = Enumerable.Range(0, 300).Select(_ => c.MakeJob(city)).Where(x => x != null && x.Has(RunMod.Deadline)).ToList();
        Check(jobs.All(x => x.Brief.Contains($"{(int)x.Deadline / 60}:{(int)x.Deadline % 60:00}")), "deadlines read as minutes:seconds");
        Career d = TestCareer();
        d.RefreshBoard(city);
        var targets = d.Board.Where(x => x.Type is JobType.Eliminate or JobType.Rescue).Select(x => x.Target).ToList();
        Check(targets.Distinct().Count() == targets.Count, "no two jobs on the board share a target");
        Check(Tables.Districts.Where(x => x.MinThreat == 1).Select(x => x.Id).Contains("industrial"), "labs and clubs show up early too");
    }

    // ---- objectives -------------------------------------------------------------------------

    private void Objectives()
    {
        string[] rows =
        {
            "##############",
            "#P..........m#",
            "#............#",
            "#.....r......#",
            "##############",
        };
        // Datasteal: jack in and pull the paydata out of the Matrix.
        Run ds = Make(Site(rows), JobType.Datasteal);
        ds.Player.Pos = new V2(11.5f, 1.5f);
        ds.Step(new RunInput { Slot = -1, InteractPressed = true, Interact = true });
        Check(ds.Matrix != null, "E at the mainframe jacks you in");
        ds.Matrix.Ice.Clear();
        for (int i = 0; i < 60 * 30 && ds.Matrix != null; i++) ds.Step(new RunInput { Slot = -1, Move = MatrixMove(ds.Matrix) });
        Check(ds.DataProgress >= 1, "the download finishes");
        Check(ds.ObjectiveDone, "datasteal objective done");
        Check(!ds.Alarm, "a clean run through the Matrix raises no alarm");
        // ICE.
        Run di = Make(Site(rows), JobType.Datasteal);
        di.Player.Pos = new V2(11.5f, 1.5f);
        di.Step(new RunInput { Slot = -1, InteractPressed = true, Interact = true });
        MatrixRun mx = di.Matrix;
        Pt next = Pt.Dirs4.Select(d => mx.You + d).First(n => mx.Linked(mx.You, n));
        mx.Ice.Clear();
        mx.Ice.Add(next);
        float hp0 = di.Player.Hp;
        for (int i = 0; i < 30 && di.Matrix != null; i++) di.Step(new RunInput { Slot = -1, Move = new V2(next.X - mx.You.X, next.Y - mx.You.Y) });
        Check(di.Matrix == null && di.Alarm && di.Player.Hp < hp0, "ICE dumps you out, raises the alarm, and hurts");
        // Hurting the body yanks you out.
        Run dh = Make(Site(rows), JobType.Datasteal);
        dh.Player.Pos = new V2(11.5f, 1.5f);
        dh.Step(new RunInput { Slot = -1, InteractPressed = true, Interact = true });
        dh.Hurt(dh.Player, 10, null, false);
        Check(dh.Matrix == null, "a hit on your body pulls you out of the Matrix");
        // Destroy.
        Run de = Make(Site(rows), JobType.Destroy);
        Eq(de.TargetsLeft, 1, "one rack to destroy");
        de.DamageTile(new Pt(6, 3), 999, default);
        de.Step(new RunInput { Slot = -1 });
        Check(de.ObjectiveDone, "destroy objective done when the racks are gone");
        // Eliminate.
        Run el = Make(Site(rows), JobType.Eliminate);
        Actor vip = el.AddEnemy(EnemyKind.Vip, new V2(10.5f, 3.5f), 0);
        el.Vip = vip;
        el.Step(new RunInput { Slot = -1 });
        Check(!el.ObjectiveDone, "not done while the target lives");
        el.Hurt(vip, 9999, el.Player, false);
        el.Step(new RunInput { Slot = -1 });
        Check(el.ObjectiveDone, "eliminate done when the target dies");
        // Retrieve.
        LevelPlan rp = Site(rows);
        rp.Pickups.Add(new PickupSpawn { Kind = PickupKind.Objective, Pos = new V2(5.5f, 1.5f), Name = "widget" });
        Run re = Make(rp, JobType.Retrieve);
        re.Player.Pos = new V2(5.4f, 1.5f);
        re.Step(new RunInput { Slot = -1 });
        Check(re.Carrying && re.ObjectiveDone, "picking up the item completes retrieve");
        // Rescue.
        LevelPlan hp = Site(rows);
        hp.Hostage = new V2(8.5f, 2.5f);
        Run rs = Make(hp, JobType.Rescue);
        rs.Player.Pos = new V2(7.6f, 2.5f);
        Steps(rs, 1.5f, new RunInput { Slot = -1, Interact = true });
        Eq(rs.Hostage.State, AiState.Follow, "the hostage follows once freed");
        Steps(rs, 2, new RunInput { Slot = -1, Move = new V2(-1, 0) });
        Check(V2.Distance(rs.Hostage.Pos, rs.Player.Pos) < 3, "and keeps up");
        // Clear.
        Run cl = Make(Site(rows), JobType.Clear);
        Actor a = cl.AddEnemy(EnemyKind.Ganger, new V2(9.5f, 2.5f), 0);
        cl.Step(new RunInput { Slot = -1 });
        Check(!cl.ObjectiveDone, "clear not done with a hostile standing");
        cl.Hurt(a, 9999, cl.Player, false);
        cl.Step(new RunInput { Slot = -1 });
        Check(cl.ObjectiveDone, "clear done when all are down");
    }

    private void Extraction()
    {
        string[] rows =
        {
            "##############",
            "#.E.........r#",
            "#............#",
            "##############",
        };
        LevelPlan p = Site(rows);
        p.Entry = new Pt(8, 1);
        Run r = Make(p, JobType.Destroy);
        Steps(r, 0.1f);
        r.Player.Pos = new V2(2.5f, 1.5f);
        Steps(r, 1);
        Eq(r.Phase, RunPhase.Playing, "standing in extraction with the job undone does not end the run");
        Steps(r, 2, new RunInput { Slot = -1, Interact = true });
        Check(r.Phase == RunPhase.Extracted && !r.Success && r.Aborted, "holding E there aborts");
        Run r2 = Make(Site(rows), JobType.Destroy);
        r2.DamageTile(new Pt(12, 1), 999, default);
        r2.Player.Pos = new V2(2.5f, 1.5f);
        Steps(r2, 0.2f);
        Check(r2.Phase == RunPhase.Extracted && r2.Success, "with the job done, the extraction ends the run in success");
        // Dying ends it -- once the edge is spent.
        Run r3 = Make(Site(rows));
        int edge = r3.Edge;
        Check(edge >= 1, "every runner starts with edge");
        r3.Hurt(r3.Player, 9999, null, true);
        r3.Step(new RunInput { Slot = -1 });
        Check(r3.Phase == RunPhase.Playing && r3.Edge == edge - 1 && r3.Player.Hp > 0, "edge: a killing blow, and you get up");
        r3.Hurt(r3.Player, 9999, null, true);
        Check(r3.Player.Hp > 0, "and nothing touches you for a moment after");
        Steps(r3, Tables.EdgeGrace + 0.1f);
        while (r3.Edge > 0) { r3.Hurt(r3.Player, 9999, null, true); Steps(r3, Tables.EdgeGrace + 0.1f); }
        r3.Hurt(r3.Player, 9999, null, true);
        r3.Step(new RunInput { Slot = -1 });
        Eq(r3.Phase, RunPhase.Dead, "death ends the run");
    }

    // ---- generators ---------------------------------------------------------------------

    private void LevelGenDebug() { }

    private void LevelGenSeeds()
    {
        int bad = 0;
        foreach (SiteStyle st in Enum.GetValues<SiteStyle>())
        foreach (JobType type in Enum.GetValues<JobType>().Where(t => t is not (JobType.Courier or JobType.Hub or JobType.Race or JobType.Hack)))
        for (int t = 1; t <= 5; t += 2)
        for (int s = 0; s < 3; s++)
        {
            var job = new Job { Id = 1, Type = type, Style = st, Threat = t, Seed = (ulong)(1000 + s * 77 + t * 13 + (int)st * 101 + (int)type * 7), Faction = st is SiteStyle.Office or SiteStyle.Lab ? Faction.Halcyon : Faction.Vipers, Count = 3, Target = "T", Item = "I" };
            LevelPlan p = LevelGen.Generate(job);
            string tag = $"{st}/{type}/t{t}/s{s}";
            bool ok = true;
            void C(bool c, string what) { if (!c) { ok = false; Check(false, $"{tag}: {what}"); } }
            C(p.L[p.Entry] == Tile.Floor, "entry is floor");
            C(p.Rooms.Count >= 1, "has rooms");
            C(p.Extraction.X >= 1 && p.Extraction.Right <= p.L.W - 1 && p.Extraction.Y >= 1 && p.Extraction.Bottom <= p.L.H - 1, "extraction inside the walls");
            foreach (Spawn e in p.Enemies)
                C(!p.L.Solid(Pt.Of(e.Pos).X, Pt.Of(e.Pos).Y, Tables.Enemy(e.Kind).Flying), $"{e.Kind} at {e.Pos} on open ground");
            foreach (Spawn e in p.Enemies.Where(e => e.Kind != EnemyKind.Camera && e.Kind != EnemyKind.Turret))
            {
                bool er = LevelGen.Reachable(p, Pt.Of(e.Pos));
                C(er, $"{e.Kind} reachable");
                if (!er && _only == "LevelGen") GD.Print(Around(p, Pt.Of(e.Pos)));
            }
            switch (type)
            {
                case JobType.Eliminate: C(p.Enemies.Any(e => e.Kind == EnemyKind.Vip), "a target"); break;
                case JobType.Retrieve:
                    PickupSpawn o = p.Pickups.FirstOrDefault(k => k.Kind == PickupKind.Objective);
                    C(o != null && LevelGen.Reachable(p, Pt.Of(o.Pos)), "item reachable"); break;
                case JobType.Destroy:
                    C(p.Targets.Count >= 1, "targets placed");
                    foreach (Pt q in p.Targets) C(LevelGen.Reachable(p, q), "target reachable"); break;
                case JobType.Datasteal: C(p.Mainframe.X >= 0 && LevelGen.Reachable(p, p.Mainframe), "mainframe reachable"); break;
                case JobType.Rescue: C(p.Hostage.X >= 0 && LevelGen.Reachable(p, Pt.Of(p.Hostage)), "hostage reachable"); break;
            }
            foreach (PickupSpawn k in p.Pickups)
            {
                bool ok2 = LevelGen.Reachable(p, Pt.Of(k.Pos));
                C(ok2, $"{k.Kind} pickup reachable");
                if (!ok2 && _only == "LevelGen") GD.Print(Around(p, Pt.Of(k.Pos)));
            }
            for (int y = 0; y < p.L.H; y++)
            for (int x = 0; x < p.L.W; x++)
                if (p.L[x, y] is Tile.Console or Tile.Safe or Tile.Locker) C(LevelGen.Reachable(p, new Pt(x, y)), $"{p.L[x, y]} reachable");
            if (!ok) bad++;
            // The same seed builds the same site.
            if (s == 0 && t == 1)
            {
                LevelPlan again = LevelGen.Generate(job);
                C(again.L.T.SequenceEqual(p.L.T) && again.Enemies.Count == p.Enemies.Count, "deterministic");
            }
        }
        Check(bad == 0, $"{bad} generated sites had problems");
        // A whole run steps without trouble on a generated site.
        var j2 = new Job { Id = 1, Type = JobType.Datasteal, Style = SiteStyle.Office, Threat = 3, Seed = 99, Faction = Faction.Halcyon, Target = "T", Item = "I", Title = "x" };
        var run = new Run(j2, TestCareer());
        Steps(run, 10, new RunInput { Slot = -1, Move = new V2(1, 0.3f), Aim = run.Player.Pos + new V2(3, 0) });
        Check(run.Time > 1, "a generated run steps");
        // Under a full alarm with everyone hunting, nobody ends up inside a wall.
        run.Player.MaxHp = run.Player.Hp = 1e7f;
        run.RaiseAlarm("test");
        Steps(run, 40, new RunInput { Slot = -1, Aim = run.Player.Pos + new V2(3, 0) });
        int stuck = run.Actors.Count(a => !a.Dead && !a.Static && run.L.Solid((int)a.Pos.X, (int)a.Pos.Y, a.Flying));
        Eq(stuck, 0, "actors inside walls");
        Check(run.Actors.Count(a => a.Def != null) > run.Plan.Enemies.Count, "reinforcements arrived");
    }

    private static string Around(LevelPlan p, Pt c)
    {
        var sb = new System.Text.StringBuilder($"at {c} main {p.MainW} basement {p.Basement}\n");
        for (int y = c.Y - 8; y <= c.Y + 8; y++)
        {
            for (int x = c.X - 14; x <= c.X + 14; x++)
            {
                Tile t = p.L[x, y];
                sb.Append(x == c.X && y == c.Y ? '@' : t switch { Tile.Floor or Tile.Rubble => '.', Tile.Door or Tile.DoorOpen => '+', Tile.LockedDoor => 'L', Tile.Stairs => '^', Tile.Glass => '"', Tile.Reinforced => '=', Tile.Crate or Tile.Desk or Tile.Barrel or Tile.Wreck or Tile.Fence => 'x', Tile.Console or Tile.Safe or Tile.Locker or Tile.Rack or Tile.Vat or Tile.Mainframe => 'F', _ => '#' });
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private void CityGen()
    {
        City c = City.Generate(12345);
        if (_only == "CityGen") GD.Print($"    home lot {c.Place("home").Index} at {c.Place("home").R}, lots {c.Lots.Count}");
        Check(c.Lots.Count > 100, $"plenty of lots ({c.Lots.Count})");
        foreach (string place in new[] { "home", "loft" }.Concat(Tables.Contacts.Select(k => k.Id)))
        {
            Lot l = c.Place(place);
            Check(l != null, $"{place} has a lot");
            if (l == null) continue;
            Eq(c[l.Door.X, l.Door.Y], CTile.Sidewalk, $"{place} door is on the pavement");
            (V2 kerb, _) = c.Kerb(l);
            Eq(c[(int)kerb.X, (int)kerb.Y], CTile.Road, $"{place} kerb is on the road");
        }
        foreach (ContactDef k in Tables.Contacts)
            Eq(c.Place(k.Id).District, k.District, $"{k.Name} is in the {k.District}");
        Check(City.Landmarks.All(m => c.Lots.Any(l => l.Landmark == m.id)), "every landmark is in the city");
        foreach (Lot l in c.Lots.Where(l => l.Kind == LotKind.Building && l.Landmark == null).Take(400))
            Check(c[l.Door.X, l.Door.Y] == CTile.Sidewalk, $"lot {l.Index} door on pavement");
        for (int i = 0; i < c.RoadX.Count; i++)
        for (int j = 0; j < c.RoadY.Count; j++)
        {
            V2 n = c.Node(i, j);
            Check(c[(int)n.X, (int)n.Y] == CTile.Road, "junctions are road");
        }
        City again = City.Generate(12345);
        Check(again.T.SequenceEqual(c.T) && again.Place("fixer").Index == c.Place("fixer").Index, "the city is deterministic");
        foreach (DistrictDef d in Tables.Districts)
            Check(c.Lots.Any(l => l.District == d.Id && l.Kind == LotKind.Building && l.Place == null), $"{d.Name} has free sites");
    }

    private void Driving()
    {
        City c = City.Generate(777);
        Career car = TestCareer();
        var sim = new CitySim(c, car, 1);
        sim.Traffic.Clear();
        sim.Peds.Clear();
        V2 start = sim.Player.Pos;
        for (int i = 0; i < 120; i++) sim.Step(1 / 60f, new DriveInput { Throttle = 1 });
        Check(V2.Distance(start, sim.Player.Pos) > 5, "throttle moves the car");
        Check(sim.Player.Speed > 3, "and it speeds up");
        // Into a wall.
        Lot b = c.Lots.First(l => l.Kind == LotKind.Building && l.R.W >= 5);
        sim.Player.Pos = new V2(b.R.X - 3f, b.R.Y + b.R.H / 2f);
        sim.Player.Heading = 0;
        sim.Player.Vel = new V2(14, 0);
        float hp = sim.Player.Hp;
        for (int i = 0; i < 90; i++) sim.Step(1 / 60f, new DriveInput { Throttle = 1 });
        Check(sim.Player.Pos.X < b.R.X + 0.2f, "a building stops the car");
        Check(sim.Player.Hp < hp, "a hard crash damages it");
        // Stopping at a door finds the place.
        sim.ParkAt(sim.HomeLot);
        Check(sim.AtDoor() == sim.HomeLot, "parked at home, the door is home");
    }

    private void Autopilot()
    {
        City c = City.Generate(777);
        Career car = TestCareer();
        foreach (string place in new[] { "doc", "fixer", "arms", "rigger", "decker", "fence", "home" })
        {
            var sim = new CitySim(c, car, 1);
            Lot lot = sim.City.Place(place);
            var ap = new DriveAutopilot(c, sim.Player.Pos, lot);
            float t = 0;
            while (!ap.Done && t < 120) { sim.Step(1 / 60f, ap.Step(sim, 1 / 60f)); t += 1 / 60f; }
            if (_only == "Autopilot") GD.Print($"    {place}: {t:0} s, hull {sim.Player.Hp:0}, door {lot.Door} kerb {c.Kerb(lot).pos} car {sim.Player.Pos} heading {sim.Player.Heading * 57.3f % 360:0} atdoor {sim.AtDoor()?.Place} near: {string.Join(" ", sim.Traffic.Where(x => V2.Distance(x.Pos, sim.Player.Pos) < 4).Select(x => $"{x.Kind}@{x.Pos.X:0.0},{x.Pos.Y:0.0} sw{x.SwerveT:0.0} v{x.Speed:0.0}"))} tile {c[(int)sim.Player.Pos.X, (int)sim.Player.Pos.Y]}");
            Check(ap.Done && sim.AtDoor() == lot, $"autopilot reaches {place} ({t:0} s, at {sim.Player.Pos.X:0},{sim.Player.Pos.Y:0}, hull {sim.Player.Hp:0})");
        }
    }

    private void Police()
    {
        City c = City.Generate(777);
        Career car = TestCareer();
        car.Cash = 10000;
        car.Heat = 2.5f;
        var sim = new CitySim(c, car, 1);
        sim.Traffic.Clear();
        bool sawPolice = false, closed = false, busted = false;
        for (int i = 0; i < 60 * 50 && !busted; i++)
        {
            sim.Step(1 / 60f, default);
            sawPolice |= sim.Chasers.Any(x => x.Kind == ChaserKind.Police);
            closed |= sim.Chasers.Any(x => x.Kind == ChaserKind.Police && V2.Distance(x.Car.Pos, sim.Player.Pos) < 6);
            busted = sim.Events.Any(e => e.Kind == CityEv.Busted);
        }
        Check(sawPolice, "heat brings Lone Star");
        Check(closed, "they close in");
        Check(busted, "sitting still with police on you gets you busted");
        Check(car.Cash < 10000 && car.Heat == 0, "a fine, and the heat is off");
        sim.Step(1 / 60f, default);
        Check(!sim.Chasers.Any(x => x.Kind == ChaserKind.Police), "they leave");
        // Heat cools when nobody is near.
        car.Heat = 1.2f;
        var calm = new CitySim(c, car, 2);
        calm.Traffic.Clear();
        calm.Chasers.Clear();
        for (int i = 0; i < 60 * 10; i++) { calm.Step(1 / 60f, default); calm.Chasers.Clear(); }
        Check(car.Heat < 1.0f, $"heat cools ({car.Heat:0.00})");
        car.Heat = 3;
        car.NextDay(c);
        Eq(car.Heat, 0f, "sleeping clears the heat");
        // Running a pedestrian down raises it.
        var ped = new CitySim(c, car, 3);
        ped.Traffic.Clear();
        ped.Peds.Clear();
        ped.Peds.Add(new Ped { Pos = ped.Player.Front + ped.Player.Forward * 0.3f, Dir = new Pt(1, 0), Speed = 0 });
        ped.Player.Vel = ped.Player.Forward * 8;
        for (int i = 0; i < 30; i++) ped.Step(1 / 60f, new DriveInput { Throttle = 1 });
        Check(car.Heat > 0, "a hit-and-run raises the heat");
    }

    private void Gangs()
    {
        City c = City.Generate(777);
        Career car = TestCareer();
        var sim = new CitySim(c, car, 5);
        sim.Traffic.Clear();
        sim.Ambush(2);
        Eq(sim.Chasers.Count(x => x.Kind == ChaserKind.Gang), 2, "an ambush sends gang cars");
        float hp = sim.Player.Hp;
        bool shots = false;
        for (int i = 0; i < 60 * 30; i++) { sim.Step(1 / 60f, default); shots |= sim.Events.Any(e => e.Kind == CityEv.Gunfire); }
        Check(shots && sim.Player.Hp < hp, "gangers catch up and shoot the car");
        // A biker who hits you peels off instead of staying pinned to your door.
        var pin = new CitySim(c, TestCareer(), 8);
        pin.Traffic.Clear();
        pin.Chasers.Add(new Chaser { Kind = ChaserKind.Gang, Car = new Car { Def = CitySim.GangBike, Hp = 999, Pos = pin.Player.Pos + pin.Player.Right * 3, Heading = M.Angle(-pin.Player.Right) } });
        int touching = 0;
        for (int i = 0; i < 60 * 12; i++)
        {
            pin.Step(1 / 60f, default);
            Chaser bk = pin.Chasers.FirstOrDefault();
            if (bk != null && V2.Distance(bk.Car.Pos, pin.Player.Pos) < pin.Player.R + bk.Car.R + 0.3f) touching++;
        }
        if (_only == "Gangs") GD.Print($"    biker in contact {touching * 100 / (60 * 12)} % of the time");
        Check(touching < 60 * 12 / 5, $"a ramming biker does not stay stuck to your side ({touching * 100 / (60 * 12)} % of the time in contact)");
        // Outrun them: put the car far away.
        sim.Player.Pos = new V2(5, 5);
        foreach (Chaser ch in sim.Chasers) ch.Car.Pos = new V2(City.W - 20, City.H - 20);
        for (int i = 0; i < 60 * 8; i++) { sim.Step(1 / 60f, default); sim.Player.Pos = new V2(5, 5); }
        Eq(sim.Chasers.Count, 0, "far enough away, they give up");
    }

    private void Couriers()
    {
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        Job j = null;
        for (int k = 0; k < 200 && j == null; k++)
        {
            Job x = c.MakeJob(city);
            if (x != null && x.Type == JobType.Courier) j = x;
        }
        Check(j != null, "courier jobs are generated");
        if (j == null) return;
        Check(j.Lot2 >= 0 && j.Lot2 != j.Lot && V2.Distance(city.Lots[j.Lot].Door.Centre, city.Lots[j.Lot2].Door.Centre) > 45, "the drop is across town");
        Check(j.TimeLimit > 10, $"a sane time limit ({j.TimeLimit:0} s)");
        c.Board.Add(j);
        Accept(c, j);
        var sim = new CitySim(city, c, 9);
        Eq(sim.Destinations().Last(), city.Lots[j.Lot], "first the pickup");
        sim.CollectPackage(j);
        Check(c.Courier && sim.Chasers.Count(x => x.Kind == ChaserKind.Gang) >= 1, "the package is aboard, and someone wants it");
        Eq(sim.Destinations().Last(), city.Lots[j.Lot2], "then the drop");
        // Too slow.
        sim.Chasers.Clear();
        c.CourierT = 0.5f;
        for (int i = 0; i < 60; i++) sim.Step(1 / 60f, default);
        Check(!c.Courier && sim.CourierFailed == j, "running out of time fails it");
        int rep = c.Rep;
        c.Finish(j, new RunResult { Success = false, GrenadesLeft = c.RunGrenades(), MedkitsLeft = c.RunMedkits() });
        Check(c.ActiveJob < 0 && !c.Courier, "the job is closed");
        // On time: paid.
        Job j2 = null;
        for (int k = 0; k < 300 && j2 == null; k++) { Job x = c.MakeJob(city); if (x != null && x.Type == JobType.Courier) j2 = x; }
        c.Board.Add(j2);
        Accept(c, j2);
        int cash = c.Cash;
        c.Finish(j2, new RunResult { Success = true, GrenadesLeft = c.RunGrenades(), MedkitsLeft = c.RunMedkits() });
        Check(c.Cash >= cash + j2.Pay - c.DailyRent, "a delivered package pays");
        Career w = TestCareer();
        var skies = Enumerable.Range(1, 40).Select(d => { w.Day = d; return w.Weather; }).ToList();
        Check(skies.Distinct().Count() >= 3, "the weather changes from day to day");
        w.Day = 7;
        Career.Sky s7 = w.Weather;
        w.Day = 7;
        Eq(w.Weather, s7, "and is the same for the same day");
    }

    private void Motorcycles()
    {
        City c = City.Generate(777);
        float Accel(string vehicle, int frames = 90)
        {
            Career k = TestCareer();
            k.Vehicles.Add(vehicle);
            k.Vehicle = vehicle;
            var sim = new CitySim(c, k, 1);
            sim.Traffic.Clear();
            sim.Peds.Clear();
            for (int i = 0; i < frames; i++) sim.Step(1 / 60f, new DriveInput { Throttle = 1 });
            return sim.Player.Speed;
        }
        Check(Accel("kuro") > Accel("junker") + 2, "a sport bike pulls away from a sedan");
        foreach (string bike in new[] { "bike", "kuro", "harrow", "scrambler" })
        {
            Career k = TestCareer();
            k.Vehicles.Add(bike);
            k.Vehicle = bike;
            var sim = new CitySim(c, k, 1);
            sim.Traffic.Clear();
            Lot lot = c.Place("arms");
            var ap = new DriveAutopilot(c, sim.Player.Pos, lot);
            float t = 0;
            while (!ap.Done && t < 120) { sim.Step(1 / 60f, ap.Step(sim, 1 / 60f)); t += 1 / 60f; }
            Check(ap.Done, $"the {Tables.Vehicle(bike).Name} gets across town ({t:0} s, hull {sim.Player.Hp:0})");
        }
        // Crashes hurt a rider more.
        float Crash(string vehicle)
        {
            Career k = TestCareer();
            k.Vehicles.Add(vehicle);
            k.Vehicle = vehicle;
            var sim = new CitySim(c, k, 1);
            sim.Traffic.Clear();
            Lot b = c.Lots.First(l => l.Kind == LotKind.Building && l.R.W >= 5);
            sim.Player.Pos = new V2(b.R.X - 1.5f, b.R.Y + b.R.H / 2f);
            sim.Player.Heading = 0;
            sim.Player.Vel = new V2(12, 0);
            float hp = sim.Player.Hp;
            for (int i = 0; i < 30; i++) sim.Step(1 / 60f, default);
            return hp - sim.Player.Hp;
        }
        Check(Crash("bike") > Crash("junker"), "the same crash costs a bike more");
        Career courier = TestCareer();
        courier.Vehicles.Add("kuro");
        courier.Vehicle = "kuro";
        var cs = new CitySim(c, courier, 2);
        var job = new Job { Id = 9, Type = JobType.Courier, TimeLimit = 60, Lot = 0, Lot2 = 1, Threat = 1 };
        cs.CollectPackage(job);
        Check(courier.CourierT > 60, "a bike gets more time on a courier job");
    }

    private void Traffic()
    {
        City c = City.Generate(4242);
        var sim = new CitySim(c, TestCareer(), 3);
        var start = sim.Traffic.Select(t => t.Pos).ToList();
        sim.Player.Pos = new V2(-50, -50);
        for (int i = 0; i < 600; i++) sim.Step(1 / 60f, default);
        int moved = sim.Traffic.Where((t, i) => V2.Distance(t.Pos, start[i]) > 4).Count();
        Check(moved > sim.Traffic.Count / 2, $"traffic flows ({moved}/{sim.Traffic.Count} moved)");
        int onRoad = sim.Traffic.Count(t => c[(int)t.Pos.X, (int)t.Pos.Y] == CTile.Road);
        Check(onRoad >= sim.Traffic.Count - 2, $"traffic stays on the road ({onRoad}/{sim.Traffic.Count})");
        int walking = sim.Peds.Count(p => !c.Solid((int)p.Pos.X, (int)p.Pos.Y));
        Eq(walking, sim.Peds.Count, "pedestrians never end up inside walls");

        // The horn scatters everyone nearby, away from the car, and only them.
        Ped near = sim.Peds.First(p => p.Down <= 0);
        sim.Player.Pos = near.Pos + new V2(2.5f, 0);
        sim.Player.Vel = V2.Zero;
        var close = sim.Peds.Where(p => p.Down <= 0 && V2.Distance(p.Pos, sim.Player.Pos) < CitySim.HornRange - 1).ToList();
        var far = sim.Peds.Where(p => V2.Distance(p.Pos, sim.Player.Pos) > CitySim.HornRange + 6).ToList();
        Check(close.Count > 0, "someone is near enough to hear it");
        var before = close.Select(p => V2.Distance(p.Pos, sim.Player.Pos)).ToList();
        sim.Step(1 / 60f, new DriveInput { Horn = true });
        Check(sim.Events.Any(e => e.Kind == CityEv.Honk && e.Strength > 1), "the horn sounds");
        Check(close.All(p => p.Flee > 0), $"everyone within earshot scatters ({close.Count(p => p.Flee > 0)}/{close.Count})");
        Check(far.All(p => p.Flee <= 0.7f), "people across town do not");
        for (int i = 0; i < 40; i++) sim.Step(1 / 60f, default);
        int fled = close.Where((p, i) => V2.Distance(p.Pos, sim.Player.Pos) > before[i] + 1).Count();
        Check(fled >= close.Count * 3 / 4, $"they run away from the car ({fled}/{close.Count})");
        sim.Step(1 / 60f, new DriveInput { Horn = true });
        sim.Step(1 / 60f, new DriveInput { Horn = true });
        Check(sim.Events.All(e => e.Kind != CityEv.Honk || e.Strength <= 1), "holding it down does not blare every frame");
    }

    // ---- the career -------------------------------------------------------------------------

    private void Shops()
    {
        Career c = TestCareer();
        c.Cash = 0;
        Offer kestrel = c.Offers("arms").First(o => o.Id == "kestrel");
        Check(kestrel.Block != null, "can't buy without nuyen");
        Check(c.Buy(kestrel) != null, "buying is refused");
        c.Cash = 100000;
        kestrel = c.Offers("arms").First(o => o.Id == "kestrel");
        Check(c.Buy(kestrel) == null && c.Weapons.Contains("kestrel"), "buying a weapon");
        Check(c.Cash < 100000, "costs nuyen");
        Check(c.Offers("arms").First(o => o.Id == "kestrel").Block == "Owned.", "owned weapons are marked");
        Check(c.Offers("arms").First(o => o.Id == "pike").Block != null, "loyalty gates the best stock");
        c.Loyalty["arms"] = 5;
        Check(c.Offers("arms").First(o => o.Id == "pike").Block == null, "loyalty opens it");
        Check(c.PriceMul("arms") < c.PriceMul("rigger"), "loyalty makes prices better");
        // Essence.
        c.Loyalty["doc"] = 5;
        foreach (string id in new[] { "ruthenium", "muscle", "bone", "dermal", "deck", "eyes", "thermal", "smartlink" })
        {
            Offer o = c.Offers("doc").First(x => x.Id == id);
            c.Buy(o);
        }
        Check(c.Essence >= 0.1f - 1e-4f, $"essence never below 0.1 (now {c.Essence:0.00})");
        Check(c.Offers("doc").Any(o => o.Block == "Not enough essence."), "chrome stops when essence runs out");
        Check(c.Has("thermal") || !c.Has("eyes"), "thermal needs eyes");
        // Grenade cap.
        c.Grenades["frag"] = c.GrenadeCap;
        Check(c.Offers("arms").First(o => o.Id == "frag").Block != null, "grenades are capped");
        // Sell loot.
        c.Loot.Add(new LootItem { Name = "chip", Value = 1000 });
        int cash = c.Cash;
        c.Buy(c.Offers("fence").First(o => o.Kind == ShopKind.Sell));
        Check(c.Cash > cash && c.Loot.Count == 0, "the fence buys loot");
        // Vehicles.
        c.Loyalty["rigger"] = 5;
        c.Buy(c.Offers("rigger").First(o => o.Id == "bike"));
        Eq(c.Vehicle, "bike", "a new vehicle is driven away");
        Check(c.Offers("rigger").First(o => o.Id == "van").Block != null, "garage space limits vehicles");
        c.Buy(c.Offers("home").First(o => o.Id == "garage"));
        Check(c.Offers("rigger").First(o => o.Id == "van").Block == null, "a garage holds more");
    }

    private void Karma()
    {
        Career c = TestCareer();
        c.Karma = 0;
        Check(c.RaiseAttr(Attr.Body) != null, "no karma, no raise");
        c.Karma = 100;
        int body = c.A(Attr.Body);
        float hp = c.MaxHp;
        int cost = c.AttrCost(Attr.Body);
        Eq(cost, (body + 1) * 5, "attribute cost is next level x5");
        Check(c.RaiseAttr(Attr.Body) == null, "raise body");
        Eq(c.Karma, 100 - cost, "karma spent");
        Check(c.MaxHp > hp, "more body, more HP");
        Eq(c.SkillCost(Skill.Stealth), (c.S(Skill.Stealth) + 1) * 3, "skill cost is next level x3");
        float det = c.DetectMul;
        c.RaiseSkill(Skill.Stealth);
        Check(c.DetectMul < det, "stealth slows detection");
        c.Upgrades.Add("training");
        Check(c.AttrCost(Attr.Logic) < (c.A(Attr.Logic) + 1) * 5, "training room discounts karma");
    }

    private void Board()
    {
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        c.RefreshBoard(city);
        Eq(c.Board.Count, c.BoardSize, "the board fills");
        Check(c.Board.All(j => j.Threat <= c.MaxThreat), "no job above your rep");
        Check(c.Board.All(j => j.Lot >= 0 && city.Lots[j.Lot].Place == null), "jobs are at free lots");
        Check(c.Board.Select(j => j.Lot).Distinct().Count() == c.Board.Count, "one job per lot");
        Job j = c.Board[0];
        Check(Accept(c, j) == null, "accept");
        Check(Accept(c, c.Board[1]) != null, "only one job at a time");
        c.Upgrades.Add("rig1");
        c.NextDay(city);
        Check(c.Board.Count <= c.BoardSize && c.Board.Count >= 3, "the board tops up over days");
        c.Rep = 12;
        for (int d = 0; d < 6; d++) c.NextDay(city);
        Check(c.Board.Any(x => x.Threat >= 3), "rep opens harder jobs");
    }

    private void Debriefs()
    {
        City city = City.Generate(55);
        Career c = TestCareer();
        c.CityRef = city;
        c.RefreshBoard(city);
        Job j = c.Board.First(x => !x.Driving);
        Accept(c, j);
        int cash = c.Cash, rep = c.Rep, day = c.Day;
        var res = new RunResult { Success = true, Alarm = false, Cash = 100, GrenadesLeft = c.RunGrenades(), MedkitsLeft = c.RunMedkits() };
        Debrief d = c.Finish(j, res);
        Eq(c.Cash, cash + j.Pay + j.Pay / 4 + 100 - c.DailyRent, "paid, ghost bonus, cash found");
        Check(c.Rep > rep && c.Karma >= j.Karma, "rep and karma");
        Eq(c.Day, day + 1, "a day passes");
        Check(!c.Board.Contains(j) && c.ActiveJob < 0, "the job is closed");
        Check(!d.CareerOver, "career goes on");
        // Grenades spent come off; unused carry over.
        c.Grenades["frag"] = 2;
        Job j2 = c.Board[0];
        Accept(c, j2);
        var g = c.RunGrenades();
        g["frag"] = 1;
        c.Finish(j2, new RunResult { Success = false, GrenadesLeft = g, MedkitsLeft = c.RunMedkits() });
        Eq(c.Grenades["frag"], 1, "a thrown frag is gone");
        // Death.
        Job j3 = c.Board[0];
        Accept(c, j3);
        c.TraumaLinks = 1;
        Debrief saved = c.Finish(j3, new RunResult { Died = true, GrenadesLeft = c.RunGrenades(), MedkitsLeft = c.RunMedkits() });
        Check(!saved.CareerOver && !c.Dead && c.TraumaLinks == 0, "TraumaLink pulls you out");
        Job j4 = c.Board[0];
        Accept(c, j4);
        Debrief over = c.Finish(j4, new RunResult { Died = true, GrenadesLeft = c.RunGrenades(), MedkitsLeft = c.RunMedkits() });
        Check(over.CareerOver && c.Dead, "without it, the career ends");
    }

    private void Saving()
    {
        City city = City.Generate(9);
        Career c = TestCareer();
        c.RefreshBoard(city);
        c.Cash = 4321;
        c.Install("eyes");
        c.Mods.Add("hollow9:laser");
        Accept(c, c.Board[1]);
        string json = c.ToJson();
        Career back = Career.FromJson(json);
        Eq(back.Cash, 4321, "cash survives");
        Check(back.Cyber.SequenceEqual(c.Cyber), "chrome survives");
        Eq(back.Board.Count, c.Board.Count, "the board survives");
        Eq(back.ActiveJob, c.ActiveJob, "the active job survives");
        Eq(back.Rng.State, c.Rng.State, "the random state survives");
        Eq(back.SlotQ, c.SlotQ, "ability slots survive");
        Check(back.HasMod("hollow9", "laser"), "mods survive");
        Profile.SaveCareer(c);
        Check(Profile.HasCareer && Profile.LoadCareer()?.Cash == 4321, "scratch profile round trip");
        Profile.DeleteCareer();
        Check(!Profile.HasCareer, "career deleted");
    }

    private void Audio()
    {
        foreach (Sound s in Enum.GetValues<Sound>())
        {
            float[] b = Sfx.Synthesize(s);
            Check(b.Length > 100 && b.Any(v => MathF.Abs(v) > 0.01f), $"{s} makes a sound");
        }
        foreach (MusicTrack t in Enum.GetValues<MusicTrack>())
        {
            float[] m = Music.RenderOffline(t, 3, 1);
            float peak = m.Max(MathF.Abs);
            Check(peak > 0.05f && peak <= 1f, $"{t} plays, within range (peak {peak:0.00})");
        }
    }

    /// <summary>
    /// The pictures and voices from ../assetgen (scripts/assets.sh): the manifests
    /// cover everything the game asks for by name, every file is there and
    /// decodes, and the spoken greetings are word for word what is on screen.
    /// </summary>
    private void GeneratedAssets()
    {
        var art = Assets.Manifest($"{Assets.ArtDir}/manifest.txt");
        var voice = Assets.Manifest($"{Assets.VoiceDir}/manifest.txt");
        Check(art.Count > 0 && voice.Count > 0, "both manifests are readable");
        foreach (var (name, b, rest) in art.Concat(voice))
            Check(b != null && rest != null && rest.Length > 0, $"manifest line '{name}' has all three fields");

        var pictures = art.Select(e => e.name).ToHashSet();
        var wanted = new List<string> { "place-home", "place-loft" };
        wanted.AddRange(Career.Lifestyles.Select(l => Assets.HomePicture(l.id, false)));
        wanted.AddRange(Tables.Contacts.Select(Assets.ContactPicture));
        wanted.AddRange(Tables.Hires.Select(h => Assets.HirePicture(h.id)));
        wanted.AddRange(Enum.GetValues<Faction>().Select(Assets.BossPicture));
        wanted.AddRange(Enum.GetValues<SiteStyle>().Select(Assets.SitePicture));
        wanted.AddRange(new[] { "caller-pryce", "caller-ilse", "caller-kade" });
        foreach (string w in wanted) Check(pictures.Contains(w), $"art manifest has {w}");
        foreach (var (name, aspect, _) in art)
        {
            Texture2D t = Assets.Picture(name);
            Check(t != null, $"{name}.webp is there and decodes (run scripts/assets.sh)");
            if (t == null) continue;
            float ratio = t.GetSize().X / t.GetSize().Y;
            Check(aspect == "1:1" ? MathF.Abs(ratio - 1) < 0.02f : ratio > 1.4f, $"{name} is the shape it is drawn at ({t.GetSize()})");
        }
        Check(Assets.Picture("no-such-picture") == null, "a missing picture is null, and the caller draws its own");

        var lines = voice.ToDictionary(e => e.name, e => e);
        foreach (ContactDef k in Tables.Contacts)
            for (int i = 0; i < k.Greetings.Length; i++)
            {
                string n = Assets.Greeting(k, i);
                Check(lines.TryGetValue(n, out var e) && e.b == k.Id && e.rest == k.Greetings[i], $"{n} is in the voice manifest, spoken by {k.Id}, as written");
            }
        // The story beats: the quoted speech, word for word, of a storylet that exists.
        var beats = voice.Where(e => e.name.StartsWith("line-")).ToList();
        foreach (var (name, who, text) in beats)
        {
            Storylet s = global::Sprawl.Storylets.ById(name[5..]);
            Check(s != null && s.Text.Contains(text), $"{name} is said, word for word, in storylet {name[5..]}");
        }
        Check(beats.Count >= 15, $"the story's beats are voiced ({beats.Count})");
        // The intro: Juno reads every card, word for word, over a backdrop of the same name.
        foreach (IntroCard card in Intro.All)
        {
            Check(lines.TryGetValue("say-" + card.Picture, out var e) && e.b == "fixer" && e.rest == card.Text, $"{card.Picture} is read by Juno, as written");
            Check(pictures.Contains(card.Picture), $"{card.Picture} has a backdrop");
        }
        int intro = voice.Count(e => e.name.StartsWith("say-intro-"));
        Check(voice.Count == Tables.Contacts.Sum(k => k.Greetings.Length) + beats.Count + intro && intro == Intro.All.Count(), "no stale lines in the voice manifest");
        var clips = Assets.Manifest($"{Assets.VideoDir}/manifest.txt");
        foreach (var (name, frame, _) in clips)
        {
            Check(pictures.Contains(frame), $"{name} starts from a picture that exists ({frame})");
            Check(Assets.Clip(name) != null && FileAccess.GetFileAsBytes(Assets.Clip(name)).Length > 100000, $"{name}.ogv is there and is a film, not a still");
        }
        Check(clips.Any(c => c.name == Intro.City.Picture + "-move"), "the intro opens on film");
        foreach (var (name, _, _) in voice)
        {
            AudioStream a = Assets.Voice(name);
            float most = name.StartsWith("line-") || name.StartsWith("say-") ? 25 : 8;
            Check(a != null && a.GetLength() > 0.4 && a.GetLength() < most, $"{name}.ogg is there and sounds like a line ({a?.GetLength():0.0} s)");
        }
    }
}
