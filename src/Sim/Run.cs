using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

public enum RunPhase { Playing, Extracted, Dead }

/// <summary>A change of plan mid-run (rare).</summary>
public enum Twist { None, Moved, SecondTarget, Alive, ExfilMoved }
public enum AiState { Idle, Suspicious, Alert, Search, Tied, Follow }

public sealed class Gun
{
    public WeaponDef Def;
    public int Ammo, Mag;
    public float Cooldown, Reload;
}

public sealed class Actor
{
    public int Id;
    public EnemyDef Def;
    public bool IsPlayer, IsHostage;
    public int Team;
    public string Name;
    public V2 Pos, Vel, Push;
    public float Radius = 0.3f, Facing, Hp, MaxHp, Speed;
    public int Armor;
    public bool Flying, Static, Dead;
    /// <summary>Down but breathing: knocked out, not killed.</summary>
    public bool KO, Escaped;
    public Gun Gun;
    public float Stun, Burn, Hacked, HitFlash;
    // Mind.
    public AiState State;
    public float Detect, ReactT, StateT, LostT, StrafeT, Telegraph, AttackCd, LookT, BodyT, NadeCd, ChargeCd, ChargeT, HealCd;
    public V2 ChargeDir;
    public bool Deployed, IsAlly, IsNpc, Summoned, IsGhost, IsDrone;
    public string Vendor;
    public int Heals;
    public int Nades;
    public int StrafeDir = 1;
    public V2 Goal, LastKnown, Home;
    public float BaseFacing;
    public List<V2> Patrol = new();
    public int PatrolIdx;
    public ushort[] Field;
    public Pt FieldFor = new(-1, -1);
    /// <summary>The <see cref="Level.PassVersion"/> and the reach (see <see cref="Level.Field(Pt, int, bool, ushort[], int, int, out int)"/>) <see cref="Field"/> was searched for.</summary>
    public int FieldPass = -1, FieldReach;
    public bool Bodyguard;
    /// <summary>A TraumaLink trauma team member: neutral until someone shoots at them.</summary>
    public bool IsMedic;
    /// <summary>Talked round by the runner: ignores them for this long; tried once.</summary>
    public float BluffT;
    public bool BluffTried;
    /// <summary>Hands up: out of the fight, until someone decides what to do with them.</summary>
    public bool Surrendered, SurrenderRolled;
    /// <summary>A bystander running from trouble: for how long, and from where.</summary>
    public float PanicT;
    public V2 PanicFrom;
    /// <summary>The high end: a cloaked operative seen for a moment; a howl or slam winding up or cooling; a pack in frenzy; an egg's clock.</summary>
    public float RevealT, HowlCd, HowlT, FrenzyT, SlamCd, EggT;
    /// <summary>When it last fired (the view kicks the gun back).</summary>
    public float ShotAt = -10, MovedAt = -10;
    /// <summary>A search in progress (the rooms still to look in); searched once and on edge since.</summary>
    public Queue<V2> Sweep;
    public bool Jumpy;
    /// <summary>Off duty, asleep, or bound to a circle (see <see cref="Sprawl.Mood"/>).</summary>
    public Mood Mood;
    /// <summary>Had a few (a den's ganger); carries the keycard; a night cleaner, and how long until they reach a phone (0: not calling).</summary>
    public bool Drunk, Badge, Cleaner;
    /// <summary>One of the rival crew (team 3: fights the guards and you).</summary>
    public bool Rival;
    public float CallT;
    public EnemyKind Kind => Def?.Kind ?? EnemyKind.Ganger;
    public bool Hostile => Team == 1 && !Dead;
    public bool Is(EnemyKind k) => Def != null && Def.Kind == k;
    public bool Alert => State == AiState.Alert;
}

public enum ShotKind { Bullet, Pellet, Rocket, Fireball }

public sealed class Projectile
{
    public V2 Pos, Vel, Origin;
    public float Damage, Range, Blast, WallMul = 0.5f, AimDist = 99;
    public int Team, Owner;
    public ShotKind Kind;
    public bool Pierce, Crit, NonLethal, Homing, Incendiary;
    public float ArmorMul = 1;
    public float Stun;
}

public sealed class Grenade
{
    public GrenadeDef Def;
    public V2 Pos, Vel;
    public float T, Flight, Fuse;
    public int Owner;
    public float Z => T < Flight ? MathF.Sin(T / Flight * MathF.PI) * (0.4f + Flight * 1.6f) : 0;
}

public sealed class Pickup
{
    public PickupKind Kind;
    public V2 Pos;
    public int Amount;
    public string Name, Id;
    public bool Taken;
}

public enum Ev
{
    Shot, Impact, Hit, Death, Explosion, Broken, Throw, Bounce, Pickup, Alarm, Alert, Reload, Dash, Melee,
    Hack, Matrix, Message, Door, Flash, Emp, Ignite, Telegraph, Spotted, Objective, Empty, Swap, Heal, Ability, Wave, Spell, HitMark, EnemyThrow, Whistle, Carry, Safe, Stairs, Hint, Panic, LampOut, Gas,
}

public readonly record struct RunEvent(Ev Kind, V2 Pos, V2 Dir = default, float Value = 0, int A = 0, string Text = null);

public struct RunInput
{
    public V2 Move, Aim;
    public bool Fire, ThrowRelease, ThrowHeld, Reload, Dash, Interact, InteractPressed, Medkit, AbilityQ, AbilityF, NextGrenade, Sneak, Whistle, Rig;
    /// <summary>-1 none, else the slot pressed (0 primary, 1 secondary, 2 melee).</summary>
    public int Slot;
    public int Cycle;
}

/// <summary>
/// One run: the site, everyone on it, and the rules of the fight, stepped at a
/// fixed rate. Never touches Godot; reports through <see cref="Events"/>,
/// which the view drains for sound, particles and screen shake.
/// </summary>
public sealed partial class Run
{
    public readonly Job Job;
    /// <summary>The way in taken, when it wasn't the front gate.</summary>
    public EntryPoint Entry;
    public readonly Career C;
    public readonly LevelPlan Plan;
    public Level L => Plan.L;
    public readonly Rng R;
    public readonly List<Actor> Actors = new();

    /// <summary>
    /// Everyone on site as the loop starts: like iterating <c>Actors.ToList()</c> (anyone
    /// added during the loop waits for the next one) without the copy.
    /// </summary>
    public ActorsSoFar ActorsNow => new(Actors);

    public readonly struct ActorsSoFar
    {
        private readonly List<Actor> _list;
        public ActorsSoFar(List<Actor> list) => _list = list;
        public Enumerator GetEnumerator() => new(_list);

        public struct Enumerator
        {
            private readonly List<Actor> _list;
            private readonly int _count;
            private int _i;
            public Enumerator(List<Actor> list) { _list = list; _count = list.Count; _i = -1; }
            public Actor Current => _list[_i];
            public bool MoveNext() => ++_i < _count && _i < _list.Count;
        }
    }

    private readonly Dictionary<int, int> _actorAt = new();
    private int _actorAtCount = -1;

    /// <summary>The actor with this id, or null: an index kept beside <see cref="Actors"/>, checked on every use and rebuilt when it no longer matches.</summary>
    private Actor FindActor(int id)
    {
        if (_actorAtCount == Actors.Count && _actorAt.TryGetValue(id, out int at) && at < Actors.Count && Actors[at].Id == id) return Actors[at];
        _actorAt.Clear();
        for (int i = 0; i < Actors.Count; i++) _actorAt.TryAdd(Actors[i].Id, i);
        _actorAtCount = Actors.Count;
        return _actorAt.TryGetValue(id, out at) ? Actors[at] : null;
    }
    public readonly Actor Player;
    public Actor Vip, Hostage, Boss, Ally, Vip2;
    public Twist Twist;
    public bool TwistFired;
    /// <summary>What the twist does to the pay (1 = nothing).</summary>
    public float PayMul = 1;
    public string TwistNote;
    public readonly List<Projectile> Shots = new();
    public readonly List<Grenade> Grenades = new();
    public readonly List<Pickup> Pickups = new();
    public readonly Dictionary<int, float> Fire = new();
    public readonly List<RunEvent> Events = new();
    public readonly bool[] Visible;
    public float Time;
    public RunPhase Phase;
    public bool Success, Aborted;

    private int _nextId = 1;

    public Run(Job job, Career c, LevelPlan plan = null)
    {
        Job = job;
        C = c;
        Plan = plan ?? LevelGen.Generate(job);
        R = new Rng(job.Seed ^ 0xA11CE);
        Edge = c.EdgeMax;
        // The doors that start locked: the ones a spider can lock again.
        _lockedAtStart = Enumerable.Range(0, L.W * L.H).Where(i => L.T[i] == Tile.LockedDoor).Select(i => new Pt(i % L.W, i / L.W)).ToList();
        Visible = new bool[L.W * L.H];

        Player = new Actor
        {
            Id = _nextId++, IsPlayer = true, Team = 0, Name = c.Name, Pos = Plan.Entry.Centre, Radius = 0.3f,
            MaxHp = c.MaxHp, Hp = c.MaxHp, Armor = c.ArmorValue, Speed = c.MoveSpeed,
        };
        // The way in chosen on the briefing map (the roof only with the Kite).
        if (job.Entry != null && Plan.Entries.FirstOrDefault(e => e.Id == job.Entry) is EntryPoint ep && (ep.Id != "roof" || c.OwnsHeli))
        {
            Player.Pos = ep.At.Centre;
            Entry = ep;
        }
        Player.Facing = M.Angle(Plan.Building.Centre.Centre - Player.Pos);
        Actors.Add(Player);
        SetupKit();

        foreach (Spawn s in Plan.Enemies)
        {
            Actor e = AddEnemy(s.Kind, s.Pos, s.Facing, s.Patrol, s.Bodyguard);
            e.Mood = s.Mood;
            e.Drunk = s.Drunk;
            e.Badge = s.Badge;
            // Bound to the chalk: it can't leave the circle.
            if (s.Mood == Mood.Bound) e.Static = true;
        }
        Vip = Actors.FirstOrDefault(a => a.Is(EnemyKind.Vip));
        Boss = Actors.FirstOrDefault(a => a.Is(EnemyKind.Boss));
        if (Vip != null) Vip.Name = job.Target;
        if (Plan.Hostage.X >= 0)
        {
            Hostage = new Actor { Id = _nextId++, IsHostage = true, Team = 0, Name = job.Target, Pos = Plan.Hostage, Radius = 0.3f, MaxHp = 100, Hp = 100, Speed = 3.6f, State = job.Type == JobType.Escort ? AiState.Follow : AiState.Tied };
            Actors.Add(Hostage);
        }
        if (job.Hired != null)
        {
            var h = Tables.Hires.First(x => x.id == job.Hired);
            WeaponDef aw = Tables.Weapon(h.def.Weapon);
            // What the crew member has learned with you.
            bool Perk(string id) => c.CrewHas(job.Hired, id);
            if (Perk("breacher") || Perk("quick"))
            {
                aw = aw.Copy();
                if (Perk("breacher")) aw.Damage *= 1.4f;
                if (Perk("quick")) aw.Rate *= 1.5f;
            }
            float hp = h.def.Hp + (Perk("plating") ? 80 : 0) + (Perk("vest") ? 60 : 0);
            Ally = new Actor
            {
                Id = _nextId++, Def = h.def, Team = 0, IsAlly = true, Name = h.name, Pos = Player.Pos + new V2(0.6f, 0.6f), Radius = h.def.Radius,
                MaxHp = hp, Hp = hp, Armor = h.def.Armor + (Perk("plating") || Perk("vest") ? 2 : 0), Speed = h.def.Speed, Heals = job.Hired switch { "patch" => Perk("kits") ? 5 : 3, "ade" => Perk("kits") ? 6 : 4, _ => 0 },
                Gun = new Gun { Def = aw, Ammo = aw.Mag, Mag = aw.Mag }, State = AiState.Alert,
            };
            _secondWind = Perk("wind");
            if (L.Solid((int)Ally.Pos.X, (int)Ally.Pos.Y)) Ally.Pos = Player.Pos;
            Actors.Add(Ally);
        }
        if (!Peaceful) SetupDrone();
        foreach (var (pos, vendor) in Plan.Npcs)
        {
            // An office's night cleaner wanders with a cart, and isn't a vendor.
            bool cleaner = vendor == "cleaner";
            var n = new Actor { Id = _nextId++, IsNpc = true, Team = 2, Vendor = cleaner ? null : vendor, Cleaner = cleaner, Pos = pos, Home = pos, Radius = 0.3f, MaxHp = 50, Hp = 50, Speed = R.Range(0.8f, 1.5f), Static = vendor != null && !cleaner, Facing = MathF.PI / 2 };
            n.Name = cleaner ? "night cleaner" : vendor != null ? HubGen.Vendors.First(v => v.id == vendor).name : "passer-by";
            Actors.Add(n);
        }
        foreach (PickupSpawn p in Plan.Pickups) Pickups.Add(new Pickup { Kind = p.Kind, Pos = p.Pos, Amount = p.Amount, Name = p.Name, Id = p.Id });

        // The night's conditions.
        Career.Sky sky = c.Weather;
        if (sky is Career.Sky.Rain or Career.Sky.Storm) { StepNoiseMul = 0.45f; VisionMul *= 0.85f; }
        if (sky == Career.Sky.Fog) { VisionMul *= 0.75f; FovPenalty += 2; }
        if (job.Has(RunMod.Blackout) || c.On("grid", job.District) && !Peaceful)
        {
            VisionMul *= 0.7f;
            FovPenalty += 4;
            foreach (Actor a in Actors.Where(a => a.Is(EnemyKind.Camera))) a.Stun = 1e9f;
        }
        if (job.Has(RunMod.Deadline)) Deadline = job.Deadline;
        PlanRival();
        // About one run in eight, the plan changes on the way.
        var twists = new List<Twist>();
        if (job.Type == JobType.Retrieve) twists.Add(Twist.Moved);
        if (job.Type == JobType.Eliminate) { twists.Add(Twist.SecondTarget); twists.Add(Twist.Alive); }
        // A scrambled commlink gives Lone Star less to listen to.
        if (job.Type is not (JobType.Hub or JobType.Courier or JobType.Race or JobType.Defend or JobType.Hack) && !job.Tutorial && !(c.Flag("scrambled") > 0 && job.Seed % 2 == 0) && !(job.Story == 4 && c.ArcDone("rigger"))) twists.Add(Twist.ExfilMoved);
        if (twists.Count > 0 && new Rng(job.Seed ^ 0x7157).Chance(0.12f)) Twist = twists[new Rng(job.Seed ^ 0x7158).Int(twists.Count)];
        if (c.NextCamerasDown) foreach (Actor a in Actors.Where(a => a.Is(EnemyKind.Camera))) a.Stun = 1e9f;
        if (job.Story == 4) Finale();
        ApplyLegwork();
        if (job.Schematics || c.HasUpgrade("rig2")) Array.Fill(L.Explored, true);
        if (c.HasProgram("overwatch")) OverwatchT = 6;
        UpdateFov(true);
        if (Peaceful) Array.Fill(L.Explored, true);
        // The objective has its own panel; only a walk round a market needs saying.
        if (Peaceful) Say(Briefing());
    }

    public Actor AddEnemy(EnemyKind kind, V2 pos, float facing, List<V2> patrol = null, bool bodyguard = false)
    {
        EnemyDef d = Tables.Enemy(kind);
        float scale = 1 + 0.08f * (Job.Threat - 1);
        var a = new Actor
        {
            Id = _nextId++, Def = d, Team = 1, Name = d.Name, Pos = pos, Home = pos, Facing = facing, BaseFacing = facing,
            Radius = d.Radius, MaxHp = d.Hp * scale, Hp = d.Hp * scale, Armor = d.Armor, Speed = d.Speed,
            Flying = d.Flying, Static = d.Static, Bodyguard = bodyguard,
        };
        if (d.Weapon != null)
        {
            WeaponDef w = Tables.Weapon(d.Weapon);
            a.Gun = new Gun { Def = w, Ammo = w.Mag, Mag = w.Mag };
        }
        if (patrol != null) a.Patrol.AddRange(patrol);
        // Not everyone carries the same piece.
        string alt = kind switch
        {
            EnemyKind.Ganger => R.Pick(new[] { "e_pistol", "e_pistol", "e_smg", "e_shotgun" }),
            EnemyKind.Guard => R.Pick(new[] { "e_smg", "e_smg", "e_smg", "e_rifle", "e_shotgun", "e_pistol" }),
            _ => null,
        };
        if (alt != null) { WeaponDef aw = Tables.Weapon(alt); a.Gun = new Gun { Def = aw, Ammo = aw.Mag, Mag = aw.Mag }; }
        if (kind == EnemyKind.Hound && Job.Faction == Faction.Halcyon) a.Name = "K9 unit";
        if (Job.Type == JobType.Bounty) a.Name = kind switch { EnemyKind.Hound => "Hellhound", EnemyKind.Bruiser => "Ghoul", EnemyKind.Spirit => "Hungry spirit", _ => a.Name };
        if (kind == EnemyKind.EggSac) a.EggT = R.Range(0, 8);
        if (kind is EnemyKind.Barghest) a.HowlCd = R.Range(1, 3);
        if (kind == EnemyKind.Firebug) a.Nades = 2;
        if (kind == EnemyKind.Boss)
        {
            var (bn, bw) = Tables.BossOf(Job.Faction);
            a.Name = bn;
            WeaponDef w = Tables.Weapon(bw);
            a.Gun = new Gun { Def = w, Ammo = w.Mag, Mag = w.Mag };
            a.ChargeCd = 4;
        }
        if (Job.Has(RunMod.Elite) && d.Kind is not (EnemyKind.Camera or EnemyKind.Vip)) { a.Armor += 2; a.MaxHp *= 1.2f; a.Hp = a.MaxHp; }
        if (Job.Threat >= 3 && kind is EnemyKind.Guard or EnemyKind.Heavy or EnemyKind.Ganger) a.Nades = 1;
        Actors.Add(a);
        return a;
    }

    public void Say(string text) => Events.Add(new RunEvent(Ev.Message, Player.Pos, Text: text));

    /// <summary>The item, with an article unless it already has an owner ("her kit", "Wen's ledger").</summary>
    public string TheItem => Job.Item != null && (Job.Item.Contains('\'') || Job.Item.StartsWith("her ") || Job.Item.StartsWith("his ")) ? Job.Item : "the " + Job.Item;

    /// <summary>A hub: a walk around, nobody hostile, weapons holstered.</summary>
    public bool Peaceful => Job.Type == JobType.Hub;

    private string Briefing() => Job.Type switch
    {
        JobType.Hub => "Weapons holstered. Talk to people (E). Walk back to your car (green) to leave.",
        JobType.Eliminate => $"Objective: eliminate {Job.Target}.",
        JobType.Retrieve => $"Objective: find {TheItem} and carry it out.",
        JobType.Destroy => $"Objective: destroy {Plan.Targets.Count} {Job.Item}.",
        JobType.Datasteal => "Objective: jack into the mainframe (E) and pull its three datastores (hold E on each).",
        JobType.Rescue => $"Objective: free {Job.Target} (hold E) and bring them out.",
        JobType.Investigate => "Objective: find three pieces of evidence, then get out.",
        JobType.Plant => $"Objective: plant the {Job.Item} at the marked spot (hold E), unseen if you can.",
        JobType.Defend => $"Objective: hold out for {DefendFor:0} seconds. They are coming in from the edges.",
        JobType.Kidnap => $"Objective: take {Job.Target} alive -- knock them out (baton, taser, gel, Manabolt to finish) and carry them out.",
        JobType.Escort => $"Objective: walk {Job.Target} to the meeting (they follow you), wait while they talk, and bring them back out alive.",
        JobType.Bounty when Job.Hive => "Objective: burn out the hive -- the queen, every egg sac, everything that hatched.",
        JobType.Bounty => "Objective: clear out the nest. Everything in there is hungry.",
        JobType.Hack => Job.Host switch
        {
            MxGoal.Crash => "Objective: jack in (E at your rig) and crash the host's core (spike it). Two traces and the job is blown.",
            MxGoal.Plant => "Objective: jack in (E at your rig), reach the archive and plant the backdoor (hold E). Two traces and the job is blown.",
            _ => "Objective: jack in (E at your rig) and pull three datastores. Two traces and the job is blown.",
        },
        _ => $"Objective: clear out every {Tables.FactionNames[(int)Job.Faction]} on site.",
    };

    // ---- the loop -----------------------------------------------------------------

    public const float Dt = 1f / 60f;
    public float OverclockT, CloakT, OverwatchT, FlashT, AstralT;
    public float VisionMul = 1, StepNoiseMul = 1, Deadline;
    public int FovPenalty;
    private bool _deadlineHit;

    /// <summary>The world's clock runs slower while Overclock is on; yours does not.</summary>
    public float WorldScale => OverclockT > 0 ? 0.33f : 1f;

    public void Step(RunInput input, float dt = Dt)
    {
        if (Phase != RunPhase.Playing) return;
        // A decision on the run stops the clock until it is made (1 or 2).
        if (Pending != null) { Decide(input); return; }
        Time += dt;
        if (_graceT > 0) _graceT -= dt;
        float wdt = dt * WorldScale;
        StepPlayer(input, dt);
        foreach (Actor a in ActorsNow)
            if (!a.IsPlayer && !a.Dead) StepActor(a, wdt);
        Separate();
        StepShots(wdt);
        StepGrenades(wdt);
        StepFire(wdt);
        StepGas(wdt);
        StepRivalArrival();
        FlushExplosions();
        StepRooms();
        StepAlarm(wdt);
        StepDeadline();
        StepPhantoms(wdt);
        StepTwist();
        StepTrauma(wdt);
        StepCrowd(dt);
        StepElites(dt);
        OfferChoices();
        if (Job.Type == JobType.Defend) StepDefend(wdt);
        if (Job.Type == JobType.Escort) StepEscort(dt);
        if (Job.Tutorial) StepTutorial();
        CheckObjective();
        UpdateFov(false);
        if (Player.Dead) Phase = RunPhase.Dead;
    }

    // ---- objectives ---------------------------------------------------------------

    public bool ObjectiveDone { get; private set; }
    public bool ObjectiveLost { get; private set; }
    public bool Carrying;
    public float DataProgress;

    public int TargetsLeft => Plan.Targets.Count(p => L[p] is Tile.Rack or Tile.Vat);
    public int HostilesLeft => Actors.Count(a => a.Hostile && a.Def != null && a.Kind is not (EnemyKind.Camera or EnemyKind.Turret));

    public string ObjectiveText()
    {
        if (Peaceful) return "Stroll the market. Your car is at the exit (green).";
        if (ObjectiveLost) return "Objective lost. Get out (hold E at the extraction).";
        if (ObjectiveDone) return Job.Type == JobType.Rescue ? $"Bring {Job.Target} to the extraction." : "Done. Get to the extraction.";
        return Job.Type switch
        {
            JobType.Eliminate when Twist == Twist.Alive && TwistFired => Vip != null && Vip.KO ? (CarriedBody == Vip ? $"Carry {Job.Target} to the extraction." : $"Pick up {Job.Target} (E) and carry them out.") : $"Knock {Job.Target} out (baton, taser, gel, Manabolt to finish) -- alive.",
            JobType.Eliminate when Vip2 != null && Vip != null && Vip.Dead => "Eliminate the lieutenant.",
            JobType.Eliminate => Vip != null && !Shown(Vip) ? $"Eliminate {Job.Target} -- last seen at the marker." : $"Eliminate {Job.Target}.",
            JobType.Retrieve when RivalCarrier != null => $"{RivalCarrier.Name} has {TheItem} -- get it back.",
            JobType.Retrieve => $"Find {TheItem}.",
            JobType.Destroy => $"Destroy {TheItem}: {TargetsLeft} left.",
            JobType.Datasteal => DataProgress > 0 ? $"Datastores {DataProgress * 3:0}/3.{(Matrix != null && DataProgress >= 1 ? " Jack out at the entry." : "")}" : "Jack into the mainframe.",
            JobType.Rescue => Hostage.State == AiState.Tied ? $"Free {Job.Target}." : $"Bring {Job.Target} out.",
            JobType.Investigate => $"Find the evidence: {CluesFound}/{ClueCount}.",
            JobType.Plant => PlantProgress > 0 ? $"Planting the {Job.Item}: {PlantProgress * 100:0} %." : $"Plant the {Job.Item} at the marker.",
            JobType.Defend => $"Hold out: {MathF.Max(0, DefendFor - Time):0} s.  Wave {Waves}.",
            JobType.Kidnap => Vip != null && Vip.KO ? (CarriedBody == Vip ? $"Carry {Job.Target} to the extraction." : $"Pick up {Job.Target} (hold E) and carry them out.") : $"Knock {Job.Target} out -- alive.",
            JobType.Escort => EscortProgress > 0 ? $"{Job.Target} is talking: {EscortProgress * 100:0} %. Keep them alive." : $"Bring {Job.Target} to the meeting.",
            JobType.Bounty when Job.Hive => $"Burn out the hive: {HostilesLeft} left{(Actors.Any(a => a.Is(EnemyKind.HiveQueen) && !a.Dead) ? ", the queen among them" : "")}.",
            JobType.Bounty => $"Clear the nest: {HostilesLeft} left.",
            JobType.Hack => (DataProgress >= 1 ? "Done -- jack out at the entry." : Job.Host switch { MxGoal.Crash => "Crash the host's core.", MxGoal.Plant => "Plant the backdoor in the archive.", _ => $"Pull the datastores: {DataProgress * 3:0}/3." }) + $" Traces: {Traces}/2.",
            _ => $"Clear the site: {HostilesLeft} hostiles left.",
        };
    }

    /// <summary>Where the objective is, for the HUD's arrow (null when there is nothing to point at).</summary>
    /// <summary>Is this point in the basement?</summary>
    public bool Below(V2 p) => Plan.Regions.Count > 0 ? FloorOf(p) != FloorOf(Player.Pos) : Plan.Basement.W > 0 && p.X >= Plan.MainW;

    /// <summary>
    /// Where to steer: the objective itself when it is on your floor, else the
    /// stairs on your floor that lead to it.
    /// </summary>
    public V2? NavTarget() => NavTarget(ObjectivePos());

    /// <summary><see cref="NavTarget()"/> from an <see cref="ObjectivePos"/> already worked out this frame.</summary>
    public V2? NavTarget(V2? objective)
    {
        if (objective is not V2 o) return null;
        if (Plan.Tower)
        {
            int fo = FloorOf(o), fp = FloorOf(Player.Pos);
            return fo == fp ? o : FloorWay(fp, fo) ?? o;
        }
        if (Below(o) == Below(Player.Pos)) return o;
        foreach (var kv in L.Stair)
        {
            Pt end = L.PtOf(kv.Key);
            if (Below(end.Centre) == Below(Player.Pos)) return end.Centre;
        }
        return o;
    }

    /// <summary>A note for the HUD when the objective is on the other floor.</summary>
    public string FloorNote() => FloorNote(ObjectivePos());

    public string FloorNote(V2? objective) => Plan.Tower
        ? objective is V2 to && FloorOf(to) != FloorOf(Player.Pos) ? $"It's on {Plan.FloorName(FloorOf(to))} -- take the stairs{(LiftHacked && Served(FloorOf(to)) ? " or the elevator" : "")}." : null
        : objective is V2 o && Below(o) != Below(Player.Pos)
        ? (Below(o) ? "It's in the BASEMENT -- take the stairs (^)." : "It's UPSTAIRS -- take the stairs (^).") : null;

    public V2? ObjectivePos()
    {
        if (ObjectiveDone || ObjectiveLost) return ExitCentre;
        switch (Job.Type)
        {
            case JobType.Retrieve:
                Pickup o = Pickups.FirstOrDefault(p => p.Kind == PickupKind.Objective && !p.Taken);
                return o?.Pos ?? RivalCarrier?.Pos;
            case JobType.Datasteal: return Plan.Mainframe.Centre;
            // Intel: where the target was last seen -- their spot until you have eyes on them.
            case JobType.Eliminate:
                if (Vip2 != null && Vip != null && Vip.Dead) return Vip2.Pos;
                if (Twist == Twist.Alive && TwistFired && Vip != null && Vip.KO && CarriedBody == Vip) return ExitCentre;
                return Vip == null ? null : Shown(Vip) || Vip.Dead ? Vip.Pos : Vip.Home;
            case JobType.Rescue: return Hostage.Pos;
            case JobType.Investigate:
                return Pickups.Where(p => p.Kind == PickupKind.Clue && !p.Taken).OrderBy(p => V2.Distance(p.Pos, Player.Pos)).Select(p => (V2?)p.Pos).FirstOrDefault();
            case JobType.Plant: return Plan.PlantAt;
            case JobType.Escort: return Plan.PlantAt;
            case JobType.Hack: return Plan.Mainframe.Centre;
            case JobType.Kidnap:
                if (Vip == null) return null;
                if (Vip.KO && CarriedBody == Vip) return ExitCentre;
                return Shown(Vip) || Vip.Dead ? Vip.Pos : Vip.Home;
            case JobType.Destroy:
                Pt t = Plan.Targets.Where(p => L[p] is Tile.Rack or Tile.Vat).OrderBy(p => V2.Distance(p.Centre, Player.Pos)).FirstOrDefault(new Pt(-1, -1));
                return t.X >= 0 ? t.Centre : null;
            default: return null;
        }
    }

    private bool _hubLeft;

    /// <summary>The Johnson sold you out: a squad comes in by the way out.</summary>
    private bool _trapped;

    private void SpringTrap()
    {
        _trapped = true;
        V2 near = new(Plan.Extraction.X + Plan.Extraction.W / 2f, Plan.Extraction.Y + Plan.Extraction.H / 2f);
        V2 at = Plan.Reinforce.OrderBy(p => V2.Distance(p, near)).First();
        var roster = Tables.Roster(Job.Faction);
        for (int k = 0; k < 3 + Job.Threat / 2; k++)
        {
            V2 p = at + new V2(R.Range(-1.2f, 1.2f), R.Range(-1.2f, 1.2f));
            if (L.Solid((int)p.X, (int)p.Y)) p = at;
            Actor e = AddEnemy(roster[R.Int(Math.Min(2, roster.Length))].kind, p, M.Angle(Player.Pos - p));
            BecomeAlert(e, Player.Pos, false);
        }
        string why = Job.Tag switch
        {
            "finale:leak" => "Lone Star SWAT, live on KBN, coming in by the exit -- the leak made you famous.",
            "finale:rival" => "Sorensen-Aoki's 'exit team' is here -- and they are not here to drive you home.",
            _ => "It's a set-up! Johnson sold you out -- a squad is coming in by the exit.",
        };
        Events.Add(new RunEvent(Ev.Alarm, Player.Pos, Text: why));
        Alarm = true;
    }

    private void CheckObjective()
    {
        if (Peaceful && !InExtraction(Player.Pos)) _hubLeft = true;
        if (!ObjectiveDone && !ObjectiveLost)
        {
            bool done = Job.Type switch
            {
                JobType.Eliminate => Twist == Twist.Alive && TwistFired ? Vip != null && (Vip.Dead && !Vip.KO || Vip.KO && CarriedBody == Vip && InExtraction(Player.Pos))
                    : (Vip == null || Vip.Dead && !Vip.Escaped) && (Vip2 == null || Vip2.Dead) && !(Twist == Twist.SecondTarget && !TwistFired),
                JobType.Retrieve => Carrying,
                JobType.Destroy => TargetsLeft == 0,
                JobType.Datasteal => DataProgress >= 1,
                JobType.Rescue => Hostage != null && Hostage.State == AiState.Follow,
                JobType.Hub => _hubLeft,
                JobType.Investigate => CluesFound >= ClueCount,
                JobType.Plant => PlantProgress >= 1,
                JobType.Defend => Time >= DefendFor,
                JobType.Kidnap => Vip != null && Vip.KO && CarriedBody == Vip && InExtraction(Player.Pos),
                JobType.Escort => EscortProgress >= 1,
                JobType.Hack => DataProgress >= 1,
                _ => HostilesLeft == 0,
            };
            if (done && Twist == Twist.Alive && TwistFired && Vip != null)
        {
            PayMul = Vip.KO ? 1.4f : 0.5f;
            if (!Vip.KO) TwistNote = "Delivered dead: half pay.";
        }
        if (done && Job.DoubleCross && !Job.Stiff && !Peaceful) SpringTrap();
            if (done && Peaceful) ObjectiveDone = true;
            else if (done)
            {
                ObjectiveDone = true;
                Events.Add(new RunEvent(Ev.Objective, Player.Pos, Text: "Objective complete. Get to the extraction."));
            }
        }
        if (Hostage != null && Hostage.Dead && !ObjectiveLost)
        {
            ObjectiveLost = true;
            ObjectiveDone = false;
            Say($"{Job.Target} is dead. The job is blown.");
        }
        if (Job.Type == JobType.Kidnap && Vip != null && Vip.Dead && !Vip.KO && !ObjectiveLost)
        {
            ObjectiveLost = true;
            Say($"{Job.Target} is dead. The client wanted them breathing. The job is blown.");
        }
        // Done from home: nothing to walk out of.
        if (Job.Type == JobType.Hack && Phase == RunPhase.Playing && (ObjectiveDone && Matrix == null || ObjectiveLost))
        {
            Success = ObjectiveDone;
            Phase = RunPhase.Extracted;
            return;
        }
        // Held: whoever is left breaks and runs, and the client's people take over.
        if (ObjectiveDone && Job.Type == JobType.Defend && Phase == RunPhase.Playing)
        {
            foreach (Actor a in Actors.Where(a => a.Hostile && a.Def != null)) { a.Dead = true; a.Escaped = true; }
            Say("They break and run. The place is yours.");
            Success = true;
            Phase = RunPhase.Extracted;
            return;
        }
        if (ObjectiveDone && InExtraction(Player.Pos) && (Hostage == null || V2.Distance(Hostage.Pos, Player.Pos) < 4))
        {
            Success = true;
            Phase = RunPhase.Extracted;
        }
    }

    /// <summary>The way out: the extraction zone -- or, in a tower, your own helicopter on the roof above the top floor's core.</summary>
    /// <summary>The way out nearer to the runner: the extraction, or the loading dock's roller doors.</summary>
    public V2 ExitCentre
    {
        get
        {
            V2 a = new(Plan.Extraction.X + Plan.Extraction.W / 2f, Plan.Extraction.Y + Plan.Extraction.H / 2f);
            if (Plan.BackDoor.W == 0) return a;
            V2 b = new(Plan.BackDoor.X + Plan.BackDoor.W / 2f, Plan.BackDoor.Y + Plan.BackDoor.H / 2f);
            return V2.Distance(a, Player.Pos) <= V2.Distance(b, Player.Pos) ? a : b;
        }
    }

    public bool InExtraction(V2 p) => Plan.Extraction.Contains(p) || Plan.BackDoor.W > 0 && Plan.BackDoor.Contains(p) || RoofPickup && FloorOf(p) == FloorCount - 1 && V2.Distance(p, Plan.LiftSpots[^1]) < 2.2f;
    public bool RoofPickup => _roof ??= Plan.Tower && C.OwnsHeli;
    private bool? _roof;

    private readonly HashSet<string> _hints = new();

    private void Hint(string id, string text)
    {
        if (!_hints.Add(id)) return;
        Events.Add(new RunEvent(Ev.Hint, Player.Pos, Text: text));
    }

    /// <summary>The first job talks you through it, one thing at a time, as it comes up.</summary>
    private void StepTutorial()
    {
        Hint("start", "WASD moves, the mouse aims. Hold Shift to sneak: slower, quiet, harder to spot. The pink arrow points at the briefcase.");
        Actor seen = Actors.FirstOrDefault(a => a.Hostile && a.Def != null && Seen(a.Pos));
        if (seen != null) Hint("cone", "A ganger. The pale cone is what he can see -- stay out of it. Press 3 for your blade: from behind, an unaware target drops in one.");
        if (Actors.Any(a => a.Hostile && !a.Alert && a.Detect > 0.3f)) Hint("detect", "'?' -- he has noticed something. The ring around him fills as he sees you. Back off into cover, or take him down fast.");
        if (Security > 0) Hint("alert", "Spotted! Security climbs while anyone who saw you is calling it in -- drop them fast. Each level does something (the bar under the objective says what's next); level 3 is the alarm. LMB fires, R reloads, Space dashes.");
        if (Prompt != null && Prompt.Contains("maglock")) Hint("door", "A locked door: hold E to bypass it -- or blow it with a breach charge.");
        if (CashFound > 0) Hint("cash", "Loot: cash is yours to keep. Valuables sell at Wen's Pawn in the Barrens.");
        if (Kills > 0) Hint("body", "Bodies make guards suspicious. Press E by one to carry it out of sight (E again to drop).");
        if (Time > 40) Hint("nade", "Right mouse throws a grenade -- hold it to see the arc. G picks the type. Walls and crates break.");
        if (ObjectiveDone) Hint("out", "Got it. Back to the green extraction zone where you came in.");
        if (Player.Hp < Player.MaxHp * 0.5f) Hint("hurt", "Hurting? H uses a medkit.");
    }

    private void Change(string text)
    {
        TwistFired = true;
        TwistNote = text;
        Events.Add(new RunEvent(Ev.Alarm, Player.Pos, Text: "CHANGE OF PLAN: " + text));
    }

    private void StepTwist()
    {
        if (Twist == Twist.None || Phase != RunPhase.Playing) return;
        switch (Twist)
        {
            case Twist.Moved when !TwistFired:
            {
                Pickup o = Pickups.FirstOrDefault(k => k.Kind == PickupKind.Objective && !k.Taken);
                if (o == null || V2.Distance(o.Pos, Player.Pos) > 10 || Time < 15) break;
                Room far = FarRoom(o.Pos);
                if (far == null) break;
                for (int y = far.R.Y; y < far.R.Bottom; y++)
                for (int x = far.R.X; x < far.R.Right; x++)
                    if (L[x, y] == Tile.Floor) { o.Pos = new Pt(x, y).Centre; y = far.R.Bottom; break; }
                Change($"Juno: 'They moved {TheItem}. New location's on your map.'");
                break;
            }
            case Twist.SecondTarget when !TwistFired && Vip != null && Vip.Dead && !Vip.Escaped:
            {
                Room far = FarRoom(Vip.Pos);
                V2 at = far?.Centre ?? Plan.Building.Centre.Centre;
                for (int y = far?.R.Y ?? 0; far != null && y < far.R.Bottom; y++)
                for (int x = far.R.X; x < far.R.Right; x++)
                    if (L[x, y] == Tile.Floor) { at = new Pt(x, y).Centre; y = far.R.Bottom; break; }
                Vip2 = AddEnemy(EnemyKind.Vip, at, 0);
                Vip2.Name = "the lieutenant";
                AddEnemy(EnemyKind.Guard, at + new V2(0.8f, 0), 0);
                PayMul = 1.35f;
                Change("Juno: 'Their lieutenant is in the building too. Take him as well -- the client pays 35 % more.'");
                break;
            }
            case Twist.Alive when !TwistFired && Vip != null && (Shown(Vip) || Time > 25):
                Change($"Juno: 'Client changed their mind -- they want {Job.Target} alive. Knock them out and carry them to the extraction: +40 %. Dead, they pay half.'");
                break;
            case Twist.ExfilMoved when !TwistFired && ObjectiveDone:
            {
                V2 entry = new(Plan.Extraction.X + 1.5f, Plan.Extraction.Y + 1.5f);
                V2 p = Plan.Reinforce.OrderByDescending(x => V2.Distance(x, entry)).First();
                Plan.Extraction = new Rect((int)p.X - 1, (int)p.Y - 1, 3, 3);
                Change("Exfil compromised! A new extraction point is marked -- get there.");
                break;
            }
        }
    }

    private float _ghostT = 25;
    private bool _ghostSeen;
    public bool Glitching => Actors.Any(a => a.IsGhost && !a.Dead);

    /// <summary>Too much chrome: below 1.5 essence the mind fills the shadows with guards who are not there.</summary>
    private void StepPhantoms(float dt)
    {
        foreach (Actor g in Actors.Where(a => a.IsGhost && !a.Dead))
        {
            g.StateT -= dt;
            V2 to = Player.Pos - g.Pos;
            if (to.Length() > 0.1f) { g.Pos += M.Norm(to) * 3f * dt; g.Facing = M.Angle(to); }
            if (g.StateT <= 0 || to.Length() < 1.6f) { g.Dead = true; g.Escaped = true; }
        }
        if (C.Essence >= 1.5f || Peaceful) return;
        _ghostT -= dt;
        if (_ghostT > 0) return;
        _ghostT = R.Range(18f, 40f) * (C.Essence / 1.5f + 0.3f) * (C.Flag("dampener") > 0 ? 2 : 1);
        for (int tries = 0; tries < 20; tries++)
        {
            V2 p = Player.Pos + M.Dir(R.Float() * MathF.Tau) * R.Range(5f, 8f);
            if (!L.In(Pt.Of(p)) || L.Solid((int)p.X, (int)p.Y) || !Seen(p)) continue;
            var ghost = new Actor { Id = _nextId++, IsGhost = true, Team = 3, Def = Tables.Enemy(EnemyKind.Guard), Name = "???", Pos = p, Radius = 0.3f, MaxHp = 1, Hp = 1, StateT = 4, Facing = M.Angle(Player.Pos - p) };
            Actors.Add(ghost);
            if (!_ghostSeen) { _ghostSeen = true; Say("Movement in the corner of your eye. Was that real? (Low essence: cyberpsychosis.)"); }
            break;
        }
    }

    public float TimeLeft => Deadline > 0 ? MathF.Max(0, Deadline - Time) : -1;

    private void StepDeadline()
    {
        if (Deadline <= 0 || _deadlineHit || Time < Deadline) return;
        _deadlineHit = true;
        if (Job.Type is JobType.Eliminate or JobType.Kidnap && Vip != null && !Vip.Dead)
        {
            Vip.Dead = true;
            Vip.Escaped = true;
            ObjectiveLost = true;
            Say($"{Job.Target} slipped out of the building. The job is blown.");
        }
        else if (!ObjectiveDone) RaiseAlarm("Time's up: a security sweep is under way!");
    }

    public RunResult Result() => new()
    {
        Success = Success, Died = Phase == RunPhase.Dead, Aborted = Aborted, Alarm = Alarm || Job.Type == JobType.Defend && Waves > 0, Kills = Kills,
        Cash = CashFound, Loot = LootFound, DroneLost = Drone != null && Drone.Dead, HpLeft = Player.Hp / Player.MaxHp, PayMul = PayMul * (RivalKilledTarget ? 0.5f : 1), Twist = TwistFired ? TwistNote : null,
        RivalCame = RivalsCame, RivalRobbed = RivalRobbed, RivalDead = new List<string>(RivalDead), RivalSpared = RivalSpared,
        TargetFled = Vip != null && (Vip.Escaped || !Vip.Dead || Vip.KO), TargetTaken = Twist == Twist.Alive && TwistFired && Vip != null && Vip.KO && Success,
        HostageDead = Hostage != null && Hostage.Dead && !Hostage.Escaped, ExfilBurned = Twist == Twist.ExfilMoved && TwistFired, Trap = _trapped,
        AllyDied = Ally != null && Ally.Dead && !AllyCarriedOut, AllyCarried = AllyCarriedOut, ObjectiveDone = ObjectiveDone, ShotTrauma = HtrHostile,
        Choices = new List<string>(Decided), Spared = Actors.Count(a => a.Surrendered && !a.Dead), KilledHelpless = KilledHelpless, EdgeUsed = EdgeUsed,
        KnockOuts = KnockOuts, Bosses = BossKills, Walls = WallsBroken, Spells = SpellsCast, Datastores = (int)MathF.Round(DataProgress * 3), SpidersCrashed = SpidersCrashed, GrenadesLeft = new Dictionary<string, int>(Nades), MedkitsLeft = Medkits, Seconds = Time,
    };

    /// <summary>The crew member went down, and you carried them to the way out -- or held the place, so nobody has to leave it.</summary>
    public bool AllyCarriedOut => Ally != null && Ally.Dead && (CarriedBody == Ally && InExtraction(Player.Pos) || Job.Type == JobType.Defend && Success);
    private bool _secondWind;

    public int Kills, CashFound, BossKills, WallsBroken, SpellsCast;
    public readonly List<LootItem> LootFound = new();

    // ---- sight ---------------------------------------------------------------------

    private Pt _fovFrom = new(-99, -99);
    private int _fovSight = -1, _fovRadius = -1;
    /// <summary>Moves every time <see cref="Visible"/> (and the explored map) is recomputed: the fog repaints only then.</summary>
    public int FovVersion { get; private set; }

    public int FovRadius => FlashT > 0 ? 3 : Peaceful ? 24 : Math.Max(6, C.Fov - FovPenalty);

    private void UpdateFov(bool force)
    {
        Pt p = Pt.Of(Projecting && AstralForm != null ? AstralForm.Pos : Rigging && Drone != null ? Drone.Pos : Player.Pos);
        int radius = Projecting ? 9 : Rigging ? 11 : FovRadius;
        // The field of view only depends on where you stand, how far you see and what blocks sight.
        if (!force && p == _fovFrom && L.SightVersion == _fovSight && radius == _fovRadius && FlashT <= 0) return;
        _fovFrom = p;
        _fovSight = L.SightVersion;
        _fovRadius = radius;
        L.Fov(p, radius, Visible);
        FovVersion++;
    }

    /// <summary>The room furthest from you that's inside the building and on the same floor as <paramref name="near"/> (for twists that move things).</summary>
    private Room FarRoom(V2 near)
    {
        int floor = FloorOf(near);
        return Plan.Rooms.Where(r => r.R.Area >= 9 && !r.Outer && FloorOf(r.Centre) == floor && Plan.Building.Contains(r.R.Centre))
            .OrderByDescending(r => V2.Distance(r.Centre, Player.Pos)).FirstOrDefault();
    }

    public bool SeenTile(Pt p) => L.In(p) && Visible[L.Idx(p.X, p.Y)];
    public bool Seen(V2 pos) => SeenTile(Pt.Of(pos));

    /// <summary>What the player is shown of an actor: seen, felt by thermal, or pinged by Overwatch.</summary>
    public bool Shown(Actor a)
    {
        if (a.IsPlayer) return true;
        // Active camouflage: in plain sight and still not there (unless it's close, firing, or you see heat or auras).
        if (Cloaked(a, Player.Pos)) return false;
        if (Seen(a.Pos)) return true;
        if (OverwatchT > 0 && a.Hostile) return true;
        if (AstralSees(a)) return true;
        if (a.Hostile && Ally != null && !Ally.Dead && C.CrewHas(Job.Hired, "spotter") && V2.Distance(a.Pos, Ally.Pos) < Ally.Def.Vision && L.Los(Ally.Pos, a.Pos)) return true;
        if (DroneSees(a)) return true;
        if (AstralT > 0 && !a.Dead && a.Kind is not (EnemyKind.Camera or EnemyKind.Turret or EnemyKind.Drone) && V2.Distance(a.Pos, Player.Pos) < 14) return true;
        if (C.Thermal && !a.Dead && a.Kind is not (EnemyKind.Camera or EnemyKind.Turret or EnemyKind.Drone) && V2.Distance(a.Pos, Player.Pos) < 7) return true;
        return false;
    }

    // ---- movement --------------------------------------------------------------------

    /// <summary>What a walker and a flier bump into, for <see cref="Collide.PushOut"/> (made once).</summary>
    private Func<int, int, bool> _solidWalk, _solidFly;

    /// <summary>Move a circle through the level, opening doors it walks into.</summary>
    public void Move(Actor a, V2 delta)
    {
        if (delta.LengthSquared() < 1e-10f) return;
        if (delta.LengthSquared() > 1e-6f) a.MovedAt = Time;
        // Sub-step fast moves so a dash cannot tunnel through a wall.
        int steps = (int)MathF.Ceiling(delta.Length() / 0.25f);
        V2 d = delta / steps;
        for (int s = 0; s < steps; s++)
        {
            a.Pos += d;
            OpenDoors(a);
            V2 p = a.Pos;
            Collide.PushOut(ref p, a.Radius, a.Flying ? _solidFly ??= (x, y) => L.Solid(x, y, true) : _solidWalk ??= (x, y) => L.Solid(x, y, false), out _);
            a.Pos = p;
        }
        // Never left inside a wall: hop to the nearest open tile.
        Pt at = Pt.Of(a.Pos);
        if (L.Solid(at.X, at.Y, a.Flying))
            for (int rr = 1; rr <= 3; rr++)
            {
                Pt best = new(-1, -1);
                float bd = float.MaxValue;
                for (int y = at.Y - rr; y <= at.Y + rr; y++)
                for (int x = at.X - rr; x <= at.X + rr; x++)
                {
                    if (!L.In(x, y) || L.Solid(x, y, a.Flying) || L[x, y] == Tile.Door) continue;
                    float dd = V2.Distance(new Pt(x, y).Centre, a.Pos);
                    if (dd < bd) { bd = dd; best = new Pt(x, y); }
                }
                if (best.X >= 0) { a.Pos = best.Centre; break; }
            }
    }

    private void OpenDoors(Actor a)
    {
        int x0 = (int)MathF.Floor(a.Pos.X - a.Radius - 0.05f), x1 = (int)MathF.Floor(a.Pos.X + a.Radius + 0.05f);
        int y0 = (int)MathF.Floor(a.Pos.Y - a.Radius - 0.05f), y1 = (int)MathF.Floor(a.Pos.Y + a.Radius + 0.05f);
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
            if (L[x, y] == Tile.Door)
            {
                float hp = L.Hp[L.Idx(x, y)];
                L.Set(x, y, Tile.DoorOpen);
                L.Hp[L.Idx(x, y)] = hp;
                Events.Add(new RunEvent(Ev.Door, new Pt(x, y).Centre));
            }
    }

    private void Separate()
    {
        for (int i = 0; i < Actors.Count; i++)
        {
            Actor a = Actors[i];
            if (a.Dead || a.Is(EnemyKind.Camera) || a.IsGhost) continue;
            for (int j = i + 1; j < Actors.Count; j++)
            {
                Actor b = Actors[j];
                if (b.Dead || b.Is(EnemyKind.Camera) || b.IsGhost || a.Flying != b.Flying) continue;
                V2 d = b.Pos - a.Pos;
                float min = a.Radius + b.Radius, dist = d.Length();
                if (dist >= min || dist < 1e-5f) continue;
                V2 n = d / dist * (min - dist);
                float wa = a.Static ? 0 : b.Static ? 1 : 0.5f, wb = b.Static ? 0 : a.Static ? 1 : 0.5f;
                if (a.Static && b.Static) continue;
                Move(a, -n * wa);
                Move(b, n * wb);
            }
        }
    }
}
