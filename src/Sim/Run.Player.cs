using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

public sealed partial class Run
{
    public readonly Gun[] Guns = new Gun[2];
    public WeaponDef MeleeDef;
    public int Slot;
    public float MeleeCd, SwitchCd, MeleeSwing;
    public Dictionary<string, int> Nades = new();
    public string NadeSel;
    public int Medkits;
    public float DashT, DashCd, StepNoiseT;
    public V2 DashDir;
    public readonly Dictionary<Ability, float> AbilityCd = new();
    public float HackProgress, FreeProgress, ExtractHold;
    public Pt HackTarget = new(-1, -1);
    /// <summary>What E would do here, for the HUD.</summary>
    public string Prompt;
    public float PromptProgress = -1;
    private bool _leftEntry;
    /// <summary>A body you are carrying out of sight.</summary>
    public Actor CarriedBody;
    public float WhistleCd, UseProgress;
    private bool _bodyArmed;
    /// <summary>Jacked into the mainframe (null when not).</summary>
    public MatrixRun Matrix;
    /// <summary>The adrenaline pump is open: below 40 % health.</summary>
    public bool Adrenaline => C.Has("adrenaline") && Player.Hp < Player.MaxHp * 0.4f;
    private float _mediT = 6;

    /// <summary>The MediCart: patches you up while it's close.</summary>
    private void StepMediDrone(float dt)
    {
        if (Drone == null || Drone.Dead || C.DroneSel != "medicart") return;
        if ((_mediT -= dt) > 0) return;
        _mediT = 6;
        if (V2.Distance(Drone.Pos, Player.Pos) < 3.5f && Player.Hp < Player.MaxHp)
        {
            Player.Hp = MathF.Min(Player.MaxHp, Player.Hp + 15);
            Events.Add(new RunEvent(Ev.Heal, Player.Pos));
        }
    }
    private int _jacks;
    private bool _spoofUsed, _lampUsed;
    private readonly List<Pt> _matrixTaken = new();

    /// <summary>In the Matrix the body stands still; the deck does the moving.</summary>
    private void StepMatrix(RunInput inp, float dt)
    {
        Player.Vel = V2.Zero;
        MatrixEvent ev = Matrix.Step(dt, inp.Move, inp.InteractPressed);
        if (_matrixLift)
        {
            // The elevator's security host: three keys and the pass is yours; nothing here counts as paydata.
            if (ev == MatrixEvent.Store && Matrix.Taken.Count >= 3) { Matrix = null; LiftOverridden(); Events.Add(new RunEvent(Ev.Hack, Player.Pos)); return; }
            if (ev == MatrixEvent.Store) { Events.Add(new RunEvent(Ev.Pickup, Player.Pos, Value: (int)PickupKind.Loot, Text: $"Pass key {Matrix.Taken.Count}/3")); return; }
            if (ev is MatrixEvent.Caught or MatrixEvent.Left) _matrixLift = false;
            if (ev == MatrixEvent.Left) { Matrix = null; Say("Jacked out of the security host."); return; }
        }
        while (_matrixTaken.Count < Matrix.Taken.Count) _matrixTaken.Add(Matrix.Taken[_matrixTaken.Count]);
        DataProgress = MathF.Max(DataProgress, _matrixTaken.Count / 3f);
        switch (ev)
        {
            case MatrixEvent.Store:
                Events.Add(new RunEvent(Ev.Pickup, Player.Pos, Value: (int)PickupKind.Loot, Text: $"Paydata {_matrixTaken.Count}/3"));
                if (_matrixTaken.Count >= 3) { Matrix = null; Say("Download complete. Jacked out."); Events.Add(new RunEvent(Ev.Hack, Player.Pos)); }
                break;
            case MatrixEvent.Spoofed:
                _spoofUsed = true;
                Say("ICE hit your spoof and lost you.");
                Events.Add(new RunEvent(Ev.Emp, Player.Pos, default, 0.5f));
                break;
            case MatrixEvent.Caught when SpendEdge(true, "you tear the trace out of your deck before it lands. Jack back in."):
                Matrix = null;
                break;
            case MatrixEvent.Caught when C.HasProgram("lamplight") && !_lampUsed:
                _lampUsed = true;
                Matrix = null;
                Say("Something in the system pulls you out before the trace lands. For a moment the screen is warm, like a lamp.");
                break;
            case MatrixEvent.Caught:
            {
                bool black = Matrix.Black;
                Matrix = null;
                Hurt(Player, black ? 25 : 10, null, false, ignoreArmor: true);
                if (Job.Type == JobType.Hack)
                {
                    // From home there is nobody to come running -- but a trace is a trace.
                    Traces++;
                    if (Traces >= 2) { ObjectiveLost = true; Say("Traced twice. They have your node -- the job is blown. Pull the plug."); }
                    else Say(black ? "Black ICE! Dumpshock. One more trace and they have you." : "Traced! Dumped out. One more and they have you.");
                    break;
                }
                RaiseAlarm(black ? "Black ICE! Dumpshock -- and security knows where you are." : "Traced by ICE! Dumped out, and security is coming.");
                break;
            }
            case MatrixEvent.Left:
                Matrix = null;
                Say("Jacked out.");
                break;
        }
    }
    private float _interruptT;

    /// <summary>Getting hit breaks concentration: held actions stall and lose their progress (downloads keep theirs).</summary>
    private void Interrupted()
    {
        _interruptT = 0.6f;
        if (Matrix != null) { Matrix = null; Say("Something hit your body -- you were yanked out of the Matrix."); }
        if (Rigging) { Rigging = false; Say("Your body is hit -- you snap back out of the drone."); UpdateFov(true); }
        if (Projecting) EndProjection("Something hit your body -- you snap back into it.");
        FreeProgress = 0;
        HackProgress = 0;
        UseProgress = 0;
        ExtractHold = 0;
    }
    private Pt _stairLock = new(-1, -1);
    public bool Downstairs => Plan.Basement.W > 0 && Player.Pos.X >= Plan.MainW;
    /// <summary>The floor's name when the site has more than one ("BASEMENT", "FLOOR 3"), else null.</summary>
    public string FloorLabel => Plan.Tower ? Plan.FloorName(PlayerFloor) : Downstairs ? "BASEMENT" : null;
    private Pt _useTile = new(-1, -1);
    private readonly HashSet<Pt> _used = new();
    /// <summary>Fixtures already used up (safes open, lockers searched, consoles looped): the view draws them that way.</summary>
    public IReadOnlyCollection<Pt> UsedFixtures => _used;
    /// <summary>The tile being worked on right now (a safe cracked, a lock bypassed...), and how far along; the view animates it.</summary>
    public Pt WorkTile = new(-1, -1);
    public float WorkFrac, WorkAt = -10;
    public bool WorkingNow => Time - WorkAt < 0.15f && WorkTile.X >= 0;

    private void Working(Pt t, float frac) { WorkTile = t; WorkFrac = Math.Clamp(frac, 0, 1); WorkAt = Time; }

    private void SetupKit()
    {
        if (C.Primary != null) Guns[0] = MakeGun(C.Primary);
        if (C.Secondary != null) Guns[1] = MakeGun(C.Secondary);
        MeleeDef = C.Wield(C.Melee ?? "knife");
        Slot = Guns[0] != null ? 0 : Guns[1] != null ? 1 : 2;
        Nades = C.RunGrenades();
        NadeSel = Tables.Grenades.Select(g => g.Id).FirstOrDefault(id => Nades.GetValueOrDefault(id) > 0) ?? "frag";
        Medkits = C.RunMedkits();
    }

    private Gun MakeGun(string id)
    {
        WeaponDef w = Tables.Weapon(id);
        int mag = C.MagSize(w);
        return new Gun { Def = w, Ammo = mag, Mag = mag };
    }

    public Gun CurrentGun => Slot < 2 ? Guns[Slot] : null;
    public WeaponDef CurrentWeapon => Slot < 2 ? Guns[Slot]?.Def : MeleeDef;

    private void StepPlayer(RunInput inp, float dt)
    {
        Actor p = Player;
        if (p.Dead) return;
        // Frozen by a howl: you can look, nothing else.
        if (DazedT > 0) { inp.Move = V2.Zero; inp.Fire = inp.Dash = inp.ThrowRelease = inp.AbilityQ = inp.AbilityF = inp.Medkit = false; }
        if (OverclockT > 0) OverclockT -= dt;
        if (CloakT > 0) CloakT -= dt;
        if (OverwatchT > 0) OverwatchT -= dt;
        if (AstralT > 0) AstralT -= dt;
        if (FlashT > 0) FlashT -= dt;
        if (p.HitFlash > 0) p.HitFlash -= dt;
        foreach (Ability a in AbilityCd.Keys.ToList()) AbilityCd[a] = MathF.Max(0, AbilityCd[a] - dt);
        if (DashCd > 0) DashCd -= dt;
        if (MeleeCd > 0) MeleeCd -= dt;
        if (SwitchCd > 0) SwitchCd -= dt;
        if (MeleeSwing > 0) MeleeSwing -= dt;
        Burning(p, dt);
        Sneaking = inp.Sneak;
        if (Matrix != null) { StepMatrix(inp, dt); return; }
        if (LiftTo >= 0) { StepLift(dt); return; }
        StepWards(dt);
        if (Projecting) { StepProjection(inp, dt); return; }
        if (inp.Rig) ToggleRig();
        if (Rigging) { StepRig(inp, dt); return; }

        // Aim and move.
        V2 toAim = inp.Aim - p.Pos;
        if (toAim.LengthSquared() > 0.01f) p.Facing = M.Angle(toAim);
        V2 move = inp.Move.LengthSquared() > 1 ? M.Norm(inp.Move) : inp.Move;
        float speed = p.Speed * (inp.Sneak ? 0.55f : 1f) * (CarriedBody != null ? 0.6f : 1f) * (Adrenaline ? 1.25f : 1f) * (Clouds.Count > 0 && InGas(p.Pos, GasKind.Coolant) ? Tables.CoolantSlow : 1f);
        // Platelet factories: below half health, the bleeding stops on its own.
        if (C.Has("platelet") && p.Hp < p.MaxHp * 0.5f) p.Hp = MathF.Min(p.MaxHp * 0.5f, p.Hp + dt);
        StepMediDrone(dt);
        if (WhistleCd > 0) WhistleCd -= dt;
        if (inp.Whistle && WhistleCd <= 0)
        {
            WhistleCd = 3;
            Events.Add(new RunEvent(Ev.Whistle, p.Pos));
            Noise(p.Pos, 7, true, quiet: true);
        }
        if (inp.Dash && DashCd <= 0 && DashT <= 0)
        {
            DashT = 0.2f;
            DashDir = move.LengthSquared() > 0.01f ? M.Norm(move) : M.Dir(p.Facing);
            DashCd = C.DashCooldown;
            Events.Add(new RunEvent(Ev.Dash, p.Pos, DashDir));
        }
        V2 want = move * speed;
        if (DashT > 0) { DashT -= dt; want = DashDir * p.Speed * 3.4f; p.Vel = want; }
        else p.Vel += (want - p.Vel) * MathF.Min(1, dt * 18);
        Move(p, p.Vel * dt);
        // Stairs: step on, come out at the other end.
        Pt on = Pt.Of(p.Pos);
        if (on != _stairLock) _stairLock = new Pt(-1, -1);
        if (L.OtherEnd(on, out Pt end) && _stairLock.X < 0)
        {
            p.Pos = end.Centre;
            _stairLock = end;
            string where = Plan.Tower ? Plan.FloorName(FloorOf(p.Pos)) + "." : Downstairs ? "Down to the basement." : "Back up.";
            Events.Add(new RunEvent(Ev.Stairs, p.Pos, Text: where));
            Say(where);
        }
        if (CarriedBody != null) CarriedBody.Pos = p.Pos - M.Dir(p.Facing) * 0.35f;

        // Footsteps: running near someone unaware makes them turn round.
        if (!inp.Sneak && move.LengthSquared() > 0.1f)
        {
            StepNoiseT -= dt;
            if (StepNoiseT <= 0)
            {
                StepNoiseT = 0.35f;
                Noise(p.Pos, 2.4f * (1 - 0.1f * C.S(Skill.Stealth)) * StepNoiseMul, true, quiet: true);
            }
        }

        // Weapons.
        if (inp.Slot >= 0 && inp.Slot != Slot && (inp.Slot == 2 || Guns[inp.Slot] != null)) Swap(inp.Slot);
        if (inp.Cycle != 0)
        {
            for (int k = 1; k <= 3; k++)
            {
                int s = ((Slot + inp.Cycle * k) % 3 + 3) % 3;
                if (s == 2 || Guns[s] != null) { Swap(s); break; }
            }
        }
        Gun g = Peaceful ? null : CurrentGun;
        if (Peaceful) { Interact(inp.Interact, inp.InteractPressed, dt); return; }
        if (g != null)
        {
            if (g.Cooldown > 0) g.Cooldown -= dt;
            if (g.Reload > 0)
            {
                g.Reload -= dt;
                if (g.Reload <= 0) g.Ammo = g.Mag;
            }
            if (inp.Reload && g.Reload <= 0 && g.Ammo < g.Mag) StartReload(g);
            if (inp.Fire && SwitchCd <= 0 && g.Reload <= 0 && CarriedBody == null)
            {
                if (g.Ammo <= 0) { StartReload(g); Events.Add(new RunEvent(Ev.Empty, p.Pos)); }
                else if (g.Cooldown <= 0) { FirePlayer(g, move.LengthSquared() > 0.1f, inp.Sneak, V2.Distance(inp.Aim, p.Pos)); Unmask(); }
            }
        }
        else if (inp.Fire && MeleeCd <= 0 && SwitchCd <= 0 && CarriedBody == null) { Swing(); Unmask(); }

        if (inp.NextGrenade) CycleGrenade();
        if (inp.ThrowRelease) Throw(inp.Aim);
        if (inp.Medkit) UseMedkit();
        if (inp.AbilityQ) UseAbility(C.SlotQ, inp.Aim);
        if (inp.AbilityF) UseAbility(C.SlotF, inp.Aim);

        Interact(inp.Interact, inp.InteractPressed, dt);
        TakePickups();
    }

    private void Swap(int slot)
    {
        Gun old = CurrentGun;
        if (old != null && old.Reload > 0) old.Reload = 0;
        Slot = slot;
        SwitchCd = 0.25f;
        Events.Add(new RunEvent(Ev.Swap, Player.Pos));
    }

    private void StartReload(Gun g)
    {
        g.Reload = g.Def.Reload * C.ReloadMul(g.Def);
        Events.Add(new RunEvent(Ev.Reload, Player.Pos));
    }

    private void FirePlayer(Gun g, bool moving, bool sneak, float aimDist)
    {
        Actor p = Player;
        WeaponDef w = g.Def;
        float spread = w.Spread * C.SpreadMul(w) * (moving ? (w.Class == WeaponClass.Sniper ? 3f : 1.5f) : 1f) * (sneak ? 0.8f : 1f);
        float dmg = C.DamageOf(w);
        bool crit = R.Chance(C.CritChance(w));
        for (int k = 0; k < w.Pellets; k++)
        {
            float ang = p.Facing + R.Bell() * spread * MathF.PI / 360f;
            SpawnShot(p, w, ang, dmg * (crit ? 2 : 1), crit, aimDist);
        }
        g.Ammo--;
        g.Cooldown = 1f / w.Rate;
        CloakT = 0;
        LastPlayerShot = Time;
        p.ShotAt = Time;
        Events.Add(new RunEvent(Ev.Shot, p.Pos + M.Dir(p.Facing) * 0.45f, M.Dir(p.Facing), (int)w.Class, p.Id));
        Noise(p.Pos, C.NoiseOf(w), true);
    }

    private void Swing()
    {
        Actor p = Player;
        WeaponDef w = MeleeDef;
        MeleeCd = 1f / w.Rate;
        MeleeSwing = 0.18f;
        float dmg = C.DamageOf(w);
        bool hit = false;
        foreach (Actor a in Actors)
        {
            if (!a.Hostile || a.Is(EnemyKind.Camera) && V2.Distance(a.Pos, p.Pos) > w.Reach + 0.4f) continue;
            V2 d = a.Pos - p.Pos;
            if (d.Length() > w.Reach + a.Radius) continue;
            if (MathF.Abs(M.AngleDiff(p.Facing, M.Angle(d))) > w.Arc * MathF.PI / 360f) continue;
            if (!L.ShotClear(p.Pos, a.Pos)) continue;
            // Taking someone who has not noticed you is quick and quiet.
            bool unaware = a.State is AiState.Idle or AiState.Suspicious or AiState.Search && !a.Static;
            Hurt(a, unaware ? dmg * 3 : dmg, p, false, nonLethal: w.NonLethal);
            if (w.Stun > 0 && !a.Dead) a.Stun = MathF.Max(a.Stun, w.Stun);
            hit = true;
        }
        // The blade also bites into soft cover in front.
        V2 tip = p.Pos + M.Dir(p.Facing) * MathF.Max(0.85f, w.Reach * 0.9f);
        Pt tt = Pt.Of(tip);
        Tile t = L[tt];
        if (t is Tile.Glass or Tile.Crate or Tile.Desk or Tile.Drywall or Tile.Door or Tile.Fence or Tile.Barrel or Tile.Rack or Tile.Vat)
            DamageTile(tt, dmg * 0.6f, tip);
        CloakT = 0;
        Events.Add(new RunEvent(Ev.Melee, p.Pos, M.Dir(p.Facing), hit ? 1 : 0));
        Noise(p.Pos, w.Noise, true, quiet: true);
    }

    private void CycleGrenade()
    {
        var owned = Tables.Grenades.Where(g => Nades.GetValueOrDefault(g.Id) > 0).Select(g => g.Id).ToList();
        if (owned.Count == 0) return;
        int i = owned.IndexOf(NadeSel);
        NadeSel = owned[(i + 1) % owned.Count];
        Events.Add(new RunEvent(Ev.Swap, Player.Pos));
    }

    /// <summary>Where a throw at this aim point would land (for the arc preview).</summary>
    public V2 ThrowTarget(V2 aim)
    {
        V2 d = aim - Player.Pos;
        float len = d.Length();
        float range = C.ThrowRange;
        return len > range ? Player.Pos + d / len * range : aim;
    }

    private void Throw(V2 aim)
    {
        if (Nades.GetValueOrDefault(NadeSel) <= 0)
        {
            CycleGrenade();
            if (Nades.GetValueOrDefault(NadeSel) <= 0) { Events.Add(new RunEvent(Ev.Empty, Player.Pos)); return; }
        }
        GrenadeDef def = Tables.Grenade(NadeSel);
        Nades[NadeSel]--;
        var g = new Grenade { Def = def, Owner = Player.Id, Fuse = def.Fuse };
        if (def.Kind == GrenadeKind.Breach)
        {
            g.Pos = Player.Pos + M.Dir(Player.Facing) * 0.45f;
            g.Flight = 0;
        }
        else
        {
            V2 target = ThrowTarget(aim);
            float dist = V2.Distance(target, Player.Pos);
            g.Pos = Player.Pos;
            g.Flight = 0.3f + dist * 0.045f;
            g.Vel = M.Norm(target - Player.Pos) * (dist / g.Flight) * 0.92f;
        }
        Grenades.Add(g);
        CloakT = 0;
        Events.Add(new RunEvent(Ev.Throw, Player.Pos, M.Norm(aim - Player.Pos)));
        if (Nades[NadeSel] <= 0) CycleGrenade();
    }

    private void UseMedkit()
    {
        if (Medkits <= 0 || Player.Hp >= Player.MaxHp) return;
        Medkits--;
        Player.Hp = MathF.Min(Player.MaxHp, Player.Hp + C.MedkitHeal);
        Events.Add(new RunEvent(Ev.Heal, Player.Pos));
    }

    private void UseAbility(Ability a, V2 aim)
    {
        if (a == Ability.None) return;
        if (AbilityCd.GetValueOrDefault(a) > 0) { Events.Add(new RunEvent(Ev.Empty, Player.Pos)); return; }
        switch (a)
        {
            case Ability.Overclock:
                OverclockT = C.Duration(a);
                break;
            case Ability.Cloak:
                CloakT = Career.AbilityDuration(a);
                foreach (Actor e in Actors.Where(e => e.Hostile && e.Alert)) e.LostT = 6;
                break;
            case Ability.Hack:
                if (!RemoteHack(aim)) { Say("Hack: nothing hackable in reach there."); Events.Add(new RunEvent(Ev.Empty, Player.Pos)); return; }
                break;
            case Ability.Manabolt:
            {
                Actor t = SpellTarget(aim);
                if (t == null) { Say("Manabolt: no living target in sight there."); Events.Add(new RunEvent(Ev.Empty, Player.Pos)); return; }
                Hurt(t, 55 * C.PowerOf(a), Player, false, ignoreArmor: true);
                Events.Add(new RunEvent(Ev.Spell, Player.Pos, t.Pos, (int)a));
                Noise(Player.Pos, 4, true, quiet: true);
                break;
            }
            case Ability.Fireball:
            {
                var w = new WeaponDef { Id = "fireball", Damage = 70 * C.PowerOf(a), Speed = 12, Range = 16, Blast = 2.2f, Pellets = 1, Class = WeaponClass.Launcher };
                SpawnShot(Player, w, Player.Facing, w.Damage, false, V2.Distance(aim, Player.Pos));
                Shots[^1].Kind = ShotKind.Fireball;
                Events.Add(new RunEvent(Ev.Spell, Player.Pos, aim, (int)a));
                Noise(Player.Pos, 10, true);
                break;
            }
            case Ability.Astral:
                AstralT = Career.AbilityDuration(a);
                break;
            case Ability.Summon:
            {
                var def = new EnemyDef { Kind = EnemyKind.Ally, Name = "Fire spirit", Hp = 140 * C.PowerOf(a), Speed = 4.4f, Aim = 0, Weapon = "e_spirit", Melee = true, Flying = true, Radius = 0.36f, Vision = 12 };
                WeaponDef sw = Tables.Weapon("e_spirit");
                var sp = new Actor
                {
                    Id = _nextId++, Def = def, Team = 0, IsAlly = true, Name = "Fire spirit", Pos = Player.Pos + M.Dir(Player.Facing) * 0.8f, Radius = def.Radius,
                    MaxHp = def.Hp, Hp = def.Hp, Speed = def.Speed, Flying = true, Gun = new Gun { Def = sw }, State = AiState.Alert, Hacked = 30, Summoned = true,
                };
                Actors.Add(sp);
                Events.Add(new RunEvent(Ev.Spell, Player.Pos, sp.Pos, (int)a));
                break;
            }
            case Ability.Project:
                if (!StartProjection()) return;
                break;
            case Ability.Ward:
                if (!RaiseWard(aim)) { Say("Ward: there's no corridor to bar there."); Events.Add(new RunEvent(Ev.Empty, Player.Pos)); return; }
                Events.Add(new RunEvent(Ev.Spell, Player.Pos, aim, (int)a));
                break;
            case Ability.Heal:
                if (Player.Hp >= Player.MaxHp) { Say("Heal: you're not hurt."); Events.Add(new RunEvent(Ev.Empty, Player.Pos)); return; }
                Player.Hp = MathF.Min(Player.MaxHp, Player.Hp + 45 * C.PowerOf(a));
                Player.Burn = 0;
                Events.Add(new RunEvent(Ev.Heal, Player.Pos));
                break;
        }
        AbilityCd[a] = C.Cooldown(a);
        if (a is Ability.Manabolt or Ability.Fireball or Ability.Heal or Ability.Summon or Ability.Astral or Ability.Project or Ability.Ward || a == Ability.Cloak && C.Spells.Contains("invisibility")) SpellsCast++;
        Events.Add(new RunEvent(Ev.Ability, Player.Pos, Value: (int)a));
    }

    /// <summary>A living (not mechanical) hostile near the aim point, in sight.</summary>
    private Actor SpellTarget(V2 aim)
    {
        Actor best = null;
        float bd = 1.8f;
        foreach (Actor e in Actors)
        {
            if (!e.Hostile || e.Def == null || e.Kind is EnemyKind.Turret or EnemyKind.Drone or EnemyKind.Camera) continue;
            float d = V2.Distance(e.Pos, aim);
            if (d < bd && V2.Distance(e.Pos, Player.Pos) < 15 && L.Los(Player.Pos, e.Pos)) { bd = d; best = e; }
        }
        return best;
    }

    private bool RemoteHack(V2 aim)
    {
        Actor best = null;
        float bd = 1.6f;
        foreach (Actor e in Actors)
        {
            if (!e.Hostile || e.Def == null || e.Kind is not (EnemyKind.Turret or EnemyKind.Drone or EnemyKind.Camera)) continue;
            float d = V2.Distance(e.Pos, aim);
            if (d < bd && V2.Distance(e.Pos, Player.Pos) < 10 && L.Los(Player.Pos, e.Pos)) { bd = d; best = e; }
        }
        if (best != null)
        {
            if (best.Is(EnemyKind.Camera)) { best.Stun = 30; Say("Camera looped for 30 s."); }
            else
            {
                best.Team = 0;
                best.Hacked = C.HasProgram("daemon") ? 9999 : 20;
                best.State = AiState.Alert;
                Say($"{best.Name} is yours{(best.Hacked > 100 ? "" : " for 20 s")}.");
            }
            Events.Add(new RunEvent(Ev.Hack, best.Pos));
            return true;
        }
        Pt t = Pt.Of(aim);
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            Pt q = t + new Pt(dx, dy);
            if (L[q] == Tile.LockedDoor && V2.Distance(q.Centre, Player.Pos) < 10)
            {
                L.Set(q, Tile.Door);
                Events.Add(new RunEvent(Ev.Hack, q.Centre));
                Say("Maglock cracked.");
                return true;
            }
        }
        return false;
    }

    private void Interact(bool held, bool pressed, float dt)
    {
        if (_interruptT > 0) { _interruptT -= dt; held = false; }
        Prompt = null;
        PromptProgress = -1;
        Actor p = Player;
        if (Peaceful)
        {
            Actor v = Actors.Where(a => a.IsNpc && a.Vendor != null && V2.Distance(a.Pos, p.Pos) < 1.8f).OrderBy(a => V2.Distance(a.Pos, p.Pos)).FirstOrDefault();
            if (v != null)
            {
                Prompt = $"E: {v.Name} -- {VendorLine(v)}";
                if (pressed) Vend(v);
            }
            return;
        }
        if (CarriedBody != null)
        {
            Prompt = "E: put the body down";
            if (pressed) { CarriedBody = null; UseProgress = 0; Events.Add(new RunEvent(Ev.Carry, p.Pos, Value: 0)); }
            return;
        }
        // The Face's verb: talk your way past, or take a uniform.
        if (TalkInteract(held, pressed, dt)) return;
        // Free the hostage.
        if (Hostage != null && Hostage.State == AiState.Tied && !Hostage.Dead && V2.Distance(Hostage.Pos, p.Pos) < 1.3f)
        {
            Prompt = $"Hold E: cut {Job.Target} loose";
            if (held)
            {
                FreeProgress += dt;
                PromptProgress = FreeProgress / 1.2f;
                if (FreeProgress >= 1.2f)
                {
                    Hostage.State = AiState.Follow;
                    Say($"{Job.Target}: \"Get me out of here!\"");
                }
            }
            return;
        }
        // The mainframe.
        if (Job.Type is JobType.Datasteal or JobType.Hack && DataProgress < 1 && !ObjectiveLost && Plan.Mainframe.X >= 0 && V2.Distance(Plan.Mainframe.Centre, p.Pos) < 1.4f)
        {
            Prompt = DataProgress > 0 ? $"E: jack back in ({DataProgress * 3:0}/3 datastores)" : "E: jack into the mainframe";
            if (pressed)
            {
                Matrix = new MatrixRun(Job.Seed ^ (ulong)(_jacks++ * 7919), Math.Min(6, Job.Threat + (SpiderUp ? 1 : 0)), C.HasProgram("breaker") || C.HasProgram("ghost"), C.HasProgram("spoof") && !_spoofUsed, HackRate * (C.HasProgram("ghost") ? 1.3f : 1f));
                // Paydata already pulled stays pulled.
                for (int k = 0; k < _matrixTaken.Count && k < 3; k++) Matrix.Taken.Add(Matrix.Stores[k]);
                Events.Add(new RunEvent(Ev.Hack, p.Pos));
                Say("Jacked in. Your body is sitting very still out there.");
            }
            return;
        }
        // The plant.
        if (Job.Type == JobType.Plant && PlantProgress < 1 && Plan.PlantAt.X >= 0 && V2.Distance(Plan.PlantAt, p.Pos) < 1.3f)
        {
            Prompt = $"Hold E: plant the {Job.Item}";
            if (held)
            {
                PlantProgress = MathF.Min(1, PlantProgress + dt / 3f * MathF.Max(1, HackRate * 0.7f));
                PromptProgress = PlantProgress;
                if (PlantProgress >= 1) { Events.Add(new RunEvent(Ev.Hack, p.Pos)); Say($"The {Job.Item} is in place."); }
            }
            return;
        }
        // An elevator panel.
        if (LiftInteract(held, pressed, dt)) return;
        // A locked door.
        Pt near = new(-1, -1);
        for (int dy = -1; dy <= 1 && near.X < 0; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            Pt q = Pt.Of(p.Pos) + new Pt(dx, dy);
            if (L[q] == Tile.LockedDoor && V2.Distance(q.Centre, p.Pos) < 1.3f) { near = q; break; }
        }
        if (near.X >= 0 && HasBadge && L.Keycards.Contains(L.Idx(near.X, near.Y)))
        {
            Prompt = "E: swipe the keycard";
            if (pressed)
            {
                L.Set(near, Tile.Door);
                L.Keycards.Remove(L.Idx(near.X, near.Y));
                Events.Add(new RunEvent(Ev.Door, near.Centre));
                Say("The reader blinks green.");
            }
            return;
        }
        if (near.X >= 0)
        {
            if (near != HackTarget) { HackTarget = near; HackProgress = 0; }
            Prompt = L.Keycards.Contains(L.Idx(near.X, near.Y)) ? "Hold E: bypass the badge reader (or find the keycard)" : "Hold E: bypass the maglock";
            if (held)
            {
                HackProgress += dt * HackRate / 4f;
                PromptProgress = HackProgress;
                Working(near, HackProgress);
                if (HackProgress >= 1)
                {
                    L.Set(near, Tile.Door);
                    Events.Add(new RunEvent(Ev.Hack, near.Centre));
                    HackProgress = 0;
                }
            }
            return;
        }
        // A warehouse's shelving: push a row over onto whoever is in the next aisle.
        if (Job.Style == SiteStyle.Warehouse && ShelfAt(p.Pos) is var (row, dir) && row.Count > 0)
        {
            Prompt = "E: push the shelving over";
            if (pressed) Topple(row, dir);
            return;
        }
        // Site furniture: the security console, safes, lockers.
        Pt fx = new(-1, -1);
        for (int dy = -1; dy <= 1 && fx.X < 0; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            Pt q = Pt.Of(p.Pos) + new Pt(dx, dy);
            if (L[q] is Tile.Console or Tile.Safe or Tile.Locker && !_used.Contains(q) && V2.Distance(q.Centre, p.Pos) < 1.4f) { fx = q; break; }
        }
        if (fx.X >= 0)
        {
            if (fx != _useTile) { _useTile = fx; UseProgress = 0; }
            Tile ft = L[fx];
            // A decker reads more off a console than the loop: Shift+E spoofs a radio call and sends guards elsewhere.
            if (ft == Tile.Console && Sneaking && pressed && Decker && _spoofed.Add(fx)) { SpoofRadio(fx); return; }
            float need = ft switch { Tile.Console => 3f, Tile.Safe => 7f, _ => 1f };
            Prompt = ft switch { Tile.Console => "Hold E: loop the cameras and turrets", Tile.Safe => "Hold E: crack the safe", _ => "Hold E: search the locker" };
            if (held)
            {
                UseProgress += dt * (ft == Tile.Locker ? 1 : HackRate);
                PromptProgress = UseProgress / need;
                Working(fx, PromptProgress);
                if (UseProgress >= need) { UseFixture(fx, ft); UseProgress = 0; }
            }
            return;
        }
        // Moving the fallen out of sight.
        Actor body = Actors.FirstOrDefault(a => a.Dead && !a.Escaped && (a.Team == 1 || a == Ally) && a.Def != null && !a.Static && !a.Flying && V2.Distance(a.Pos, p.Pos) < 0.85f);
        bool pickupHere = Pickups.Any(k => !k.Taken && V2.Distance(k.Pos, p.Pos) < 1.2f);
        if (body != null && !InExtraction(p.Pos) && !pickupHere)
        {
            bool named = body == Vip || body == Vip2 || body == Boss;
            string who = named ? body.Name : "the " + body.Name.ToLower();
            Prompt = body.KO ? $"Hold E: pick up {who} (out cold)" : $"Hold E: pick up {who}'s body";
            // Only a fresh press here starts it, so holding E for something else never grabs a body.
            if (pressed) _bodyArmed = true;
            if (!held) _bodyArmed = false;
            if (held && _bodyArmed)
            {
                UseProgress += dt;
                PromptProgress = UseProgress / 0.5f;
                if (UseProgress >= 0.5f) { CarriedBody = body; UseProgress = 0; Events.Add(new RunEvent(Ev.Carry, p.Pos, Value: 1)); }
            }
            else UseProgress = 0;
            return;
        }
        // Leaving early (once you have been in and come back).
        if (!InExtraction(p.Pos)) _leftEntry = true;
        if (InExtraction(p.Pos) && !ObjectiveDone && (_leftEntry || ObjectiveLost))
        {
            Prompt = ObjectiveLost ? "Hold E: get out" : "Hold E: abort the job and leave";
            if (held)
            {
                ExtractHold += dt;
                PromptProgress = ExtractHold / 1.5f;
                if (ExtractHold >= 1.5f) { Aborted = true; Phase = RunPhase.Extracted; }
            }
            else ExtractHold = 0;
            return;
        }
        ExtractHold = 0;
    }

    private int _rumour;

    private string VendorLine(Actor v) => v.Vendor switch
    {
        "informant" => C.Heat < 0.2f ? "nobody's looking for you right now." : $"{M.Money(InformantPrice)} and Lone Star forgets you (heat {C.Heat:0.0}).",
        "broker" => C.Active == null || C.Active.Driving ? "nothing to sell you without a job on." : C.Active.Schematics ? "you already have the schematics." : $"site schematics for {C.Active.Title}: {M.Money(Stall(300))}.",
        _ => HubGen.Vendors.First(x => x.id == v.Vendor).line,
    };

    /// <summary>A stall's price: half during the lantern festival.</summary>
    private int Stall(int price) => C.On("lanterns") ? price / 2 : price;
    private int InformantPrice => (200 + (int)(250 * C.Heat)) * (C.On("crackdown") ? 3 : 1);

    private void Vend(Actor v)
    {
        string say;
        switch (v.Vendor)
        {
            case "noodles":
                if (C.WellFed) say = "\"You already ate, chummer.\"";
                else if (C.Cash < Stall(40)) say = "\"No nuyen, no noodles.\"";
                else { C.Cash -= Stall(40); C.WellFed = true; say = "Hot, salty, mostly soy. (+15 % max HP on your next run)"; Events.Add(new RunEvent(Ev.Heal, Player.Pos)); }
                break;
            case "broker":
            {
                Job j = C.Active;
                if (j == null || j.Driving) say = "\"Get a job first. Then we talk.\"";
                else if (j.Schematics || C.HasUpgrade("rig2")) say = "\"You already have everything I have.\"";
                else if (C.Cash < Stall(300)) say = $"\"{M.Money(Stall(300))}. Come back with it.\"";
                else { C.Cash -= Stall(300); j.Schematics = true; say = $"Schematics for {j.Title}: the whole site is on your map when you go in."; Events.Add(new RunEvent(Ev.Pickup, Player.Pos, Value: 0, Text: "Schematics")); }
                break;
            }
            case "chips":
            {
                string id = R.Pick(new[] { "frag", "flash", "frag", "emp" });
                if (C.Grenades.GetValueOrDefault(id) >= C.GrenadeCap) say = "\"You can't carry more of those.\"";
                else if (C.Cash < Stall(120)) say = $"\"{M.Money(Stall(120))}. Not a yen less.\"";
                else { C.Cash -= Stall(120); C.Grenades[id] = C.Grenades.GetValueOrDefault(id) + 1; say = $"+1 {Tables.Grenade(id).Name}."; Events.Add(new RunEvent(Ev.Pickup, Player.Pos, Value: 2, Text: say)); }
                break;
            }
            case "informant":
            {
                int price = InformantPrice;
                if (C.Heat < 0.2f) say = "\"Nobody's looking for you. Yet.\"";
                else if (C.Cash < price) say = $"\"{M.Money(price)}. You're short.\"";
                else { C.Cash -= price; C.Heat = 0; say = $"{M.Money(price)} later, Lone Star has lost your file."; Events.Add(new RunEvent(Ev.Hack, Player.Pos)); }
                break;
            }
            default:
                // Every other tip is about the city right now, when there is something to tell.
                say = "\"" + (_rumour++ % 2 == 0 && C.Gossip() is string g ? g : HubGen.Rumours[_rumour % HubGen.Rumours.Length]) + "\"";
                break;
        }
        Say($"{v.Name}: {say}");
    }

    private void UseFixture(Pt t, Tile kind)
    {
        _used.Add(t);
        switch (kind)
        {
            case Tile.Console:
                foreach (Actor a in Actors.Where(a => a.Hostile && (a.Is(EnemyKind.Camera) || a.Is(EnemyKind.Turret)))) a.Stun = 1e9f;
                Events.Add(new RunEvent(Ev.Hack, t.Centre));
                Say("Security loop running: cameras and turrets are blind.");
                if (Decker) { OverwatchT = MathF.Max(OverwatchT, 20); Say("And the guard rota: you know where everyone is for a while."); }
                break;
            case Tile.Safe:
            {
                int cash = R.Range(400, 900) * Job.Threat;
                Pickups.Add(new Pickup { Kind = PickupKind.Cash, Pos = Player.Pos, Amount = cash });
                if (R.Chance(0.5f)) Pickups.Add(new Pickup { Kind = PickupKind.Loot, Pos = Player.Pos + new V2(0.3f, 0), Amount = R.Range(600, 1500) * Job.Threat, Name = R.Pick(new[] { "bearer bonds", "diamond chip", "corporate scrip" }) });
                Events.Add(new RunEvent(Ev.Safe, t.Centre));
                Say("The safe swings open.");
                break;
            }
            default:
            {
                string[] ids = { "frag", "frag", "flash", "emp", "fire", "breach" };
                int n = R.Range(1, 2);
                for (int k = 0; k < n; k++) Pickups.Add(new Pickup { Kind = PickupKind.Grenade, Pos = Player.Pos + new V2(0.2f * k, 0), Amount = 1, Id = R.Pick(ids) });
                if (R.Chance(0.5f)) Pickups.Add(new Pickup { Kind = PickupKind.Medkit, Pos = Player.Pos, Amount = 1 });
                Events.Add(new RunEvent(Ev.Door, t.Centre));
                break;
            }
        }
    }

    public bool HasSuit, HasBadge;

    /// <summary>
    /// A row of shelving beside the runner (three crates or more in a line) and
    /// the way it falls (away from them), or an empty row.
    /// </summary>
    public (List<Pt> row, Pt dir) ShelfAt(V2 at)
    {
        foreach (Pt d in Pt.Dirs4)
        {
            Pt c = Pt.Of(at) + d;
            if (L[c] != Tile.Crate) continue;
            foreach (Pt axis in new[] { new Pt(1, 0), new Pt(0, 1) })
            {
                // Pushing along the row moves nothing; push across it.
                if (axis.X != 0 && d.Y == 0 || axis.Y != 0 && d.X == 0) continue;
                var row = new List<Pt> { c };
                for (int k = 1; k < 7 && L[c + new Pt(axis.X * k, axis.Y * k)] == Tile.Crate; k++) row.Add(c + new Pt(axis.X * k, axis.Y * k));
                for (int k = 1; k < 7 && L[c - new Pt(axis.X * k, axis.Y * k)] == Tile.Crate; k++) row.Add(c - new Pt(axis.X * k, axis.Y * k));
                if (row.Count >= 3) return (row, d);
            }
        }
        return (new List<Pt>(), default);
    }

    /// <summary>Over it goes: the row comes down, anyone in the aisle beyond is crushed and stunned, and everyone hears it.</summary>
    public void Topple(List<Pt> row, Pt dir)
    {
        foreach (Pt t in row)
        {
            Pt fall = t + dir;
            L.Set(t, Tile.Rubble);
            Events.Add(new RunEvent(Ev.Broken, t.Centre, default, 0, (int)Tile.Crate));
            if (L[fall] == Tile.Floor) L.Set(fall, Tile.Rubble);
            foreach (Actor a in ActorsNow)
            {
                if (a.Dead || a.IsPlayer || a.Flying || V2.Distance(a.Pos, fall.Centre) > 0.9f) continue;
                Hurt(a, Tables.ToppleHurt, Player, false);
                if (!a.Dead && a.Def != null) a.Stun = MathF.Max(a.Stun, Tables.ToppleStun);
            }
        }
        Noise(row[0].Centre, 13, true);
        Say("The shelving goes over with a crash.");
    }

    public bool Used(Pt t) => _used.Contains(t);

    private void TakePickups()
    {
        foreach (Pickup k in Pickups)
        {
            if (k.Taken || V2.Distance(k.Pos, Player.Pos) > 0.65f) continue;
            string text;
            switch (k.Kind)
            {
                case PickupKind.Cash: CashFound += k.Amount; text = $"+{M.Money(k.Amount)}"; break;
                case PickupKind.Medkit: Medkits++; text = "+1 medkit"; break;
                case PickupKind.Grenade:
                    Nades[k.Id] = Nades.GetValueOrDefault(k.Id) + 1;
                    text = $"+1 {Tables.Grenade(k.Id).Name}";
                    if (Nades.GetValueOrDefault(NadeSel) <= 0) NadeSel = k.Id;
                    break;
                case PickupKind.Loot: LootFound.Add(new LootItem { Name = k.Name, Value = k.Amount }); text = $"Found: {k.Name}"; break;
                case PickupKind.Clue: CluesFound++; text = $"Evidence {CluesFound}/{ClueCount}: {k.Name}."; break;
                case PickupKind.Suit: HasSuit = true; text = "A clean-room suit: the sensors will take you for staff."; break;
                case PickupKind.Badge: HasBadge = true; text = "A keycard: the badge readers will open for it."; break;
                default: Carrying = true; text = $"Got {(k.Name.Contains('\'') || k.Name.StartsWith("her ") ? k.Name : "the " + k.Name)}!"; break;
            }
            k.Taken = true;
            Events.Add(new RunEvent(Ev.Pickup, k.Pos, Value: (int)k.Kind, Text: text));
        }
    }
}
