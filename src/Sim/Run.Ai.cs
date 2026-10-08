using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// The opposition. Unaware guards patrol and fill a detection meter while
/// they can see you; noise brings them to look; an alerted guard shouts to
/// those nearby, fights (melee rushes, guns hold range and strafe), hunts
/// your last known position and eventually gives up -- unless the alarm is
/// up, in which case everyone knows where you are and help is on the way.
/// </summary>
public sealed partial class Run
{
    public bool Alarm;
    public float AlarmMeter;
    public const float AlarmAfter = 8f;
    private float _waveT;
    private int _waves;
    private ushort[] _walkField, _flyField;
    private Pt _fieldFrom = new(-1, -1);
    private float _fieldT;
    public bool Sneaking;
    /// <summary>Spotted earlier, but nobody has eyes on you now: the alarm meter is falling.</summary>
    public bool CoolingOff;

    private int _fieldPass = -1;

    private ushort[] PlayerField(bool fly)
    {
        Pt p = Pt.Of(Player.Pos);
        // Rebuilt as you move (a few times a second at most), and at once when a wall is blown open or a way shut.
        if (_walkField == null || (p != _fieldFrom && Time - _fieldT > 0.25f) || _fieldPass != L.PassVersion)
        {
            _walkField = L.Field(p, 400, false, _walkField);
            _fieldFrom = p;
            _fieldT = Time;
            _fieldPass = L.PassVersion;
            _flyFresh = false;
        }
        if (!fly) return _walkField;
        // Few things fly: build their field only when one asks, from the same tile.
        if (!_flyFresh) { _flyField = L.Field(_fieldFrom, 400, true, _flyField); _flyFresh = true; }
        return _flyField;
    }

    private bool _flyFresh;

    private void StepActor(Actor a, float dt)
    {
        if (a.HitFlash > 0) a.HitFlash -= dt;
        Burning(a, dt);
        if (a.Dead) return;
        if (a.IsHostage) { StepHostage(a, dt); return; }
        if (a.IsMedic && a.Team == 2) { StepMedic(a, dt); return; }
        if (a.Surrendered) return;
        if (a.IsNpc) { StepNpc(a, dt); return; }
        if (a.IsGhost) return;
        if (a.Hacked > 0)
        {
            a.Hacked -= dt;
            if (a.Hacked <= 0) { a.Team = 1; BecomeAlert(a, Player.Pos, false); Say($"{a.Name} shook off your hack."); }
        }
        // Egg sacs only wait (StepHive hatches them).
        if (a.Is(EnemyKind.EggSac)) return;
        if (a.Stun > 0) { a.Stun -= dt * (a.Is(EnemyKind.Boss) || a.Is(EnemyKind.Bulwark) ? 3 : 1); return; }
        if (a.Gun != null)
        {
            Gun g = a.Gun;
            if (g.Cooldown > 0) g.Cooldown -= dt;
            if (g.Reload > 0) { g.Reload -= dt; if (g.Reload <= 0) g.Ammo = g.Mag; }
        }
        if (a.AttackCd > 0) a.AttackCd -= dt;
        if (a.Is(EnemyKind.Camera)) { StepCamera(a, dt); return; }
        if (a.IsAlly) { StepAlly(a, dt); return; }
        if (a.Team == 0) { StepHacked(a, dt); return; }
        if (a.Rival && a.Team == 3) { StepRivalActor(a, dt); return; }

        Actor target = PickTarget(a, out bool sees);
        Perceive(a, dt, sees && target == Player);
        if (!a.Alert && Drone != null && !Drone.Dead && a.State != AiState.Suspicious && CanSee(a, Drone) && R.Chance(dt * 1.5f))
        {
            a.State = AiState.Suspicious;
            a.Goal = Drone.Pos;
            a.StateT = 0;
            a.Detect = MathF.Max(a.Detect, 0.4f);
        }
        if (!a.Alert && !a.Static) CheckBodies(a, dt);
        float speed = a.Speed * (a.FrenzyT > 0 ? 1.3f : 1) * (a.Drunk ? Tables.DrunkPace : 1);
        switch (a.State)
        {
            case AiState.Idle: Idle(a, dt); break;
            case AiState.Suspicious: Suspicious(a, dt); break;
            case AiState.Search: Search(a, dt); break;
            case AiState.Alert: Fight(a, target, sees, dt, speed); break;
        }
    }

    // ---- seeing ---------------------------------------------------------------------

    public bool CanSee(Actor a, Actor t)
    {
        if (t.Dead || a.Def == null) return false;
        float dist = V2.Distance(a.Pos, t.Pos);
        if (t.IsPlayer && CloakT > 0 && dist > 1.0f) return false;
        // Asleep sees nothing; nobody sees through a cloud of gas.
        if (a.Mood == Mood.Asleep) return false;
        if (dist > 1.2f && Clouds.Count > 0 && GasBetween(a.Pos, t.Pos)) return false;
        if (a.Team == 0 && Cloaked(t, a.Pos)) return false;
        float range = a.Def.Vision * (a.Alert ? 1.4f : 1f) * (a.Is(EnemyKind.Camera) || a.Is(EnemyKind.Turret) ? 1 : VisionMul) * (a.Mood == Mood.OffDuty ? Tables.OffDutySight : 1);
        if (dist > range) return false;
        bool cone = !a.Alert || a.Is(EnemyKind.Camera);
        if (cone && dist > 1.5f)
        {
            float half = a.Is(EnemyKind.Camera) ? 0.62f : a.Is(EnemyKind.Turret) ? 1.05f : 0.96f;
            if (MathF.Abs(M.AngleDiff(a.Facing, M.Angle(t.Pos - a.Pos))) > half) return false;
        }
        return L.Los(a.Pos, t.Pos);
    }

    /// <summary>The nearest runner-side actor this one can see (falls back to the player).</summary>
    private Actor PickTarget(Actor a, out bool sees)
    {
        Actor best = null;
        float bd = float.MaxValue;
        foreach (Actor t in Actors)
        {
            if (t.Dead || !Foes(a, t) || t.IsNpc) continue;
            if (t.IsHostage && t.State == AiState.Tied) continue;
            if (!t.IsPlayer && !a.Alert) continue;
            float d = V2.DistanceSquared(a.Pos, t.Pos);
            if (d >= bd || !CanSee(a, t)) continue;
            bd = d;
            best = t;
        }
        sees = best != null;
        return best ?? Player;
    }

    private void Perceive(Actor a, float dt, bool seesPlayer)
    {
        float dist = V2.Distance(a.Pos, Player.Pos);
        if (a.Alert)
        {
            if (seesPlayer) { a.LastKnown = Player.Pos; a.LostT = 0; }
            else a.LostT += dt;
            if (a.LostT > (Alarm ? 6 : 14) && !Hunted && !a.Static)
            {
                a.State = AiState.Search;
                a.StateT = 0;
                a.Goal = a.LastKnown;
            }
            return;
        }
        // Talked round: they've decided you belong here.
        if (a.BluffT > 0) { a.BluffT -= dt; a.Detect = 0; return; }
        if (seesPlayer)
        {
            float vision = a.Def.Vision;
            float rate = dist < 2 ? 4f : 1.6f * (1.25f - dist / vision);
            // In their uniform you're one of them -- until they get a close look.
            if (Disguised) rate *= dist < 2.2f ? 0.6f : 0.2f;
            rate = MathF.Max(0.25f, rate) * C.DetectMul * (Sneaking ? 0.5f : 1f) * (a.State == AiState.Search ? 2f : 1f) * LightDetect(Player.Pos) * (a.Mood == Mood.OffDuty ? Tables.OffDutyDetect : 1) * (a.Drunk ? Tables.DrunkNotice : 1)
                // Guards who have searched for you, or a site on edge, notice you faster.
                * (a.Jumpy ? Tables.JumpyDetect : 1f) * (SecurityLevel >= 1 && !Alarm ? Tables.OnEdgeDetect : 1f);
            if (a.Is(EnemyKind.Turret)) rate *= 2.5f;
            a.Detect += dt * rate;
            if (a.Detect >= 1 && !a.Is(EnemyKind.Turret) && SlipDetection(a)) return;
            if (a.Detect >= 1) BecomeAlert(a, Player.Pos);
            else if (a.Detect > 0.3f && a.State != AiState.Search)
            {
                if (a.State != AiState.Suspicious) a.StateT = 0;
                a.State = AiState.Suspicious;
                a.Goal = Player.Pos;
            }
        }
        else if (a.Is(EnemyKind.Hound) && Smells(a, dist))
        {
            // A dog doesn't need to see you: it noses round the wall to where you are (never past wondering on scent alone).
            a.Detect = MathF.Min(0.9f, a.Detect + dt * Tables.SmellRate * (Sneaking ? 0.5f : 1));
            if (a.Detect > 0.3f && a.State == AiState.Idle) { a.State = AiState.Suspicious; a.Goal = Player.Pos; a.StateT = 0; }
            else if (a.State == AiState.Suspicious) a.Goal = Player.Pos;
        }
        else a.Detect = MathF.Max(0, a.Detect - dt * 0.12f);
    }

    /// <summary>Close enough to smell (rain washes most of it out; a cloak doesn't).</summary>
    private bool Smells(Actor a, float dist) => dist < Tables.Smell * (C.Weather is Career.Sky.Rain or Career.Sky.Storm ? 0.6f : 1) && a.Mood != Mood.Asleep;

    /// <summary>How fast you're noticed for the light you stand in: <see cref="Tables.DarkDetect"/> in the dark, <see cref="Tables.LitDetect"/> under a lamp.</summary>
    public float LightDetect(V2 p)
    {
        float lit = M.Clamp(L.LightAt(p) / 0.6f, 0, 1);
        return Tables.DarkDetect + (Tables.LitDetect - Tables.DarkDetect) * lit;
    }

    public void BecomeAlert(Actor a, V2 where, bool shout = true)
    {
        if (a.Team != 1 || a.Dead || a.Def == null) return;
        bool was = a.Alert;
        a.State = AiState.Alert;
        a.Sweep = null;
        a.Detect = 1;
        a.LastKnown = where;
        a.LostT = 0;
        if (was) return;
        a.ReactT = R.Range(0.35f, 0.75f);
        if (a.Is(EnemyKind.Camera)) return;
        Events.Add(new RunEvent(Ev.Alert, a.Pos, default, 0, a.Id));
        if (!shout) return;
        foreach (Actor o in Actors)
        {
            if (o == a || !o.Hostile || o.Alert || o.Def == null || o.Is(EnemyKind.Camera)) continue;
            float d = V2.Distance(o.Pos, a.Pos);
            if (d < 3.5f || d < 7 && L.Los(o.Pos, a.Pos))
            {
                BecomeAlert(o, where, false);
                o.ReactT += 0.3f;
            }
        }
    }

    // ---- states ---------------------------------------------------------------------

    private void Idle(Actor a, float dt)
    {
        if (a.Mood == Mood.Asleep) return;
        if (a.Static) { Sweep(a, dt); return; }
        if (a.Patrol.Count > 1)
        {
            if (a.StateT > 0) { a.StateT -= dt; LookAround(a, dt); return; }
            V2 goal = a.Patrol[a.PatrolIdx % a.Patrol.Count];
            if (V2.Distance(a.Pos, goal) < 0.6f)
            {
                a.PatrolIdx++;
                a.StateT = R.Range(1.5f, 4f);
                return;
            }
            MoveTo(a, goal, a.Speed * 0.45f, dt);
            return;
        }
        if (V2.Distance(a.Pos, a.Home) > 1.0f) { MoveTo(a, a.Home, a.Speed * 0.45f, dt); return; }
        LookAround(a, dt);
    }

    private void LookAround(Actor a, float dt)
    {
        a.LookT -= dt;
        if (a.LookT <= 0)
        {
            a.LookT = R.Range(2f, 5f);
            a.Goal = a.Pos + M.Dir(a.BaseFacing + R.Range(-1.4f, 1.4f));
        }
        a.Facing = M.TurnTo(a.Facing, M.Angle(a.Goal - a.Pos), 1.5f * dt);
    }

    private void Sweep(Actor a, float dt)
    {
        a.Facing = M.TurnTo(a.Facing, a.BaseFacing + MathF.Sin(Time * 0.55f + a.Id) * 0.9f, 1.2f * dt);
    }

    private void Suspicious(Actor a, float dt)
    {
        a.StateT += dt;
        a.Facing = M.TurnTo(a.Facing, M.Angle(a.Goal - a.Pos), 4f * dt);
        if (a.Static) { if (a.StateT > 4) a.State = AiState.Idle; return; }
        if (a.Bodyguard && V2.Distance(a.Goal, a.Home) > 6) { if (a.StateT > 5) a.State = AiState.Idle; return; }
        if (a.StateT > 0.7f && V2.Distance(a.Pos, a.Goal) > 0.9f && a.StateT < 12) MoveTo(a, a.Goal, a.Speed * 0.6f, dt, face: false);
        else if (a.StateT > 4 && (V2.Distance(a.Pos, a.Goal) <= 0.9f || a.StateT >= 12))
        {
            a.State = AiState.Idle;
            a.Detect = 0.15f;
            a.StateT = 0;
        }
    }

    /// <summary>
    /// Losing you: sweep the rooms around where you were last seen, nearest
    /// first, a look round in each -- then back to post, but jumpy for the rest
    /// of the run.
    /// </summary>
    private void Search(Actor a, float dt)
    {
        a.StateT += dt;
        if (a.Sweep == null)
        {
            a.Sweep = new Queue<V2>(Plan.Rooms.Where(r => V2.Distance(r.Centre, a.LastKnown) < Tables.SearchReach && FloorOf(r.Centre) == FloorOf(a.Pos))
                .OrderBy(r => V2.Distance(r.Centre, a.LastKnown)).Take(Tables.SearchRooms).Select(r => r.Centre).Prepend(a.LastKnown));
            a.Goal = a.Sweep.Dequeue();
            a.LookT = 0;
        }
        if (V2.Distance(a.Pos, a.Goal) < 1.1f)
        {
            // A look round, then the next room.
            a.LookT += dt;
            a.Facing += dt * 2.2f;
            if (a.LookT > 1.4f)
            {
                a.LookT = 0;
                if (a.Sweep.Count > 0) a.Goal = a.Sweep.Dequeue();
                else { EndSearch(a); return; }
            }
            return;
        }
        MoveTo(a, a.Goal, a.Speed * 0.7f, dt);
        if (a.StateT > Tables.SearchFor) EndSearch(a);
    }

    private void EndSearch(Actor a)
    {
        a.State = AiState.Idle;
        a.Detect = 0.3f;
        a.StateT = 0;
        a.Sweep = null;
        a.Jumpy = true;
    }

    private void Fight(Actor a, Actor target, bool sees, float dt, float speed)
    {
        float dist = V2.Distance(a.Pos, target.Pos);
        if (a.Static)
        {
            if (a.ReactT > 0) a.ReactT -= dt;
            if (!sees) { Sweep(a, dt); return; }
            a.Facing = M.TurnTo(a.Facing, M.Angle(target.Pos - a.Pos), 2.4f * dt);
            Attack(a, target, dist, dt);
            return;
        }
        if (a.Is(EnemyKind.Rigger) && !a.Deployed)
        {
            // Two drones off the back rack.
            a.Deployed = true;
            for (int k = 0; k < 2; k++)
            {
                Actor dr = AddEnemy(EnemyKind.Drone, a.Pos + M.Dir(a.Facing + 1.5f + k * 3.1f) * 0.6f, a.Facing);
                BecomeAlert(dr, a.LastKnown, false);
            }
            Say("A rigger launches drones.");
        }
        if (a.Is(EnemyKind.Boss) && Charge(a, target, sees, dist, dt)) return;
        if (a.Is(EnemyKind.Barghest) && Howl(a, target, sees, dist, dt)) return;
        if (a.Is(EnemyKind.Bulwark) && sees) Slam(a, target, dist, dt);
        if (a.Is(EnemyKind.Vip) && PanicGoal(a) is V2 safe)
        {
            // For the panic room, firing back only when you're in the way.
            MoveTo(a, safe, speed * 1.15f, dt);
            if (sees && dist < 3) { a.Facing = M.TurnTo(a.Facing, M.Angle(target.Pos - a.Pos), 8 * dt); Attack(a, target, dist, dt); }
            return;
        }
        if (a.Is(EnemyKind.Vip))
        {
            // Run from the player; fire back when cornered or close.
            V2 before = a.Pos;
            FollowField(a, PlayerField(false), a.Pos, speed * 1.1f, dt, away: true);
            bool cornered = V2.Distance(before, a.Pos) < speed * dt * 0.3f;
            if (sees && (cornered || dist < 4))
            {
                a.Facing = M.TurnTo(a.Facing, M.Angle(target.Pos - a.Pos), 8 * dt);
                Attack(a, target, dist, dt);
            }
            return;
        }
        if (!sees)
        {
            a.Telegraph = 0;
            // On hard jobs, flush you out of cover with a grenade.
            if (a.Nades > 0 && (a.NadeCd -= dt) <= 0 && a.LostT > 0.8f && a.LostT < 6)
            {
                float d = V2.Distance(a.Pos, a.LastKnown);
                if (d > 3.5f && d < 9 && R.Chance(0.5f)) EnemyGrenade(a, a.LastKnown);
                else a.NadeCd = 1.5f;
            }
            bool hunt = Hunted || a.LostT < 5;
            if (hunt && dist > 1.2f) FollowField(a, PlayerField(a.Flying), Player.Pos, speed, dt);
            else MoveTo(a, a.LastKnown, speed * 0.8f, dt);
            if (!hunt && V2.Distance(a.Pos, a.LastKnown) < 0.8f && a.LostT > 3)
            {
                a.State = AiState.Search;
                a.StateT = 0;
                a.Goal = a.LastKnown;
            }
            return;
        }

        float turn = a.Def.Melee ? 10f : 7f;
        if (a.Telegraph <= 0) a.Facing = M.TurnTo(a.Facing, M.Angle(target.Pos - a.Pos), turn * dt);
        else a.Facing = M.TurnTo(a.Facing, M.Angle(target.Pos - a.Pos), 1.2f * dt);
        if (a.ReactT > 0) a.ReactT -= dt;

        if (a.Def.Melee)
        {
            if (dist > a.Gun.Def.Reach + target.Radius * 0.8f) FollowField(a, PlayerField(a.Flying), target.Pos, speed, dt, face: false);
            Attack(a, target, dist, dt);
            return;
        }
        // Keep the preferred range; strafe when there.
        float pref = a.Def.PreferRange;
        V2 to = M.Norm(target.Pos - a.Pos);
        V2 dir = V2.Zero;
        if (dist > pref + 2.5f || !L.ShotClear(a.Pos, target.Pos)) { FollowField(a, PlayerField(a.Flying), target.Pos, speed * 0.85f, dt, face: false); }
        else
        {
            if (dist < pref - 2) dir -= to;
            a.StrafeT -= dt;
            if (a.StrafeT <= 0) { a.StrafeT = R.Range(0.8f, 2.2f); a.StrafeDir = R.Chance(0.5f) ? 1 : -1; if (R.Chance(0.25f)) a.StrafeDir = 0; }
            dir += M.Perp(to) * a.StrafeDir * 0.8f;
            if (dir.LengthSquared() > 0.01f)
            {
                V2 before = a.Pos;
                Move(a, M.Norm(dir) * speed * 0.6f * dt);
                if (V2.Distance(before, a.Pos) < speed * 0.2f * dt) a.StrafeDir = -a.StrafeDir;
            }
        }
        // Firebugs lob a bottle at you in the open too.
        if (a.Is(EnemyKind.Firebug) && a.Nades > 0 && (a.NadeCd -= dt) <= 0 && dist > 3.5f && dist < 8 && R.Chance(0.4f)) { EnemyGrenade(a, target.Pos); a.NadeCd = 4; }
        Attack(a, target, dist, dt);
    }

    private void Attack(Actor a, Actor target, float dist, float dt)
    {
        Gun g = a.Gun;
        if (g == null || a.ReactT > 0) return;
        WeaponDef w = g.Def;
        float threat = 0.85f + 0.12f * (Job.Threat - 1);
        if (w.Melee)
        {
            if (dist > w.Reach + target.Radius || a.AttackCd > 0) return;
            a.AttackCd = 1f / w.Rate;
            Hurt(target, w.Damage * threat, a, false);
            Events.Add(new RunEvent(Ev.Melee, a.Pos, M.Norm(target.Pos - a.Pos), 1, a.Id));
            return;
        }
        if (g.Reload > 0) return;
        if (g.Ammo <= 0) { g.Reload = w.Reload; return; }
        if (g.Cooldown > 0 || dist > w.Range * 0.9f) return;
        float off = MathF.Abs(M.AngleDiff(a.Facing, M.Angle(target.Pos - a.Pos)));
        bool slow = a.Is(EnemyKind.Sniper) || a.Is(EnemyKind.Mage);
        if (slow)
        {
            if (a.Telegraph <= 0)
            {
                if (off > 0.25f) return;
                a.Telegraph = a.Is(EnemyKind.Sniper) ? 1.1f : 0.75f;
                Events.Add(new RunEvent(Ev.Telegraph, a.Pos, M.Dir(a.Facing), a.Telegraph, a.Id));
                return;
            }
            a.Telegraph -= dt;
            if (a.Telegraph > 0) return;
            a.Telegraph = 0;
        }
        else if (off > 0.22f) return;

        float spread = (w.Spread + a.Def.Aim) * (a.Drunk ? Tables.DrunkAim : 1);
        for (int k = 0; k < w.Pellets; k++)
            SpawnShot(a, w, a.Facing + R.Bell() * spread * MathF.PI / 360f, w.Damage * threat, false, dist);
        g.Ammo--;
        g.Cooldown = 1f / w.Rate * R.Range(0.9f, 1.3f);
        // Bursts, then a breath.
        if (w.Rate > 3 && R.Chance(0.22f)) g.Cooldown += R.Range(0.5f, 1.1f);
        Events.Add(new RunEvent(Ev.Shot, a.Pos + M.Dir(a.Facing) * 0.4f, M.Dir(a.Facing), (int)w.Class, a.Id));
        a.ShotAt = Time;
        Noise(a.Pos, w.Noise * 0.7f, false);
        // The muzzle flash gives the ghost away, for a moment.
        if (a.Is(EnemyKind.Cloaker)) a.RevealT = Tables.CloakReveal;
    }

    /// <summary>The boss's charge: a wind-up you can see, then a rush that hits like a truck.</summary>
    private bool Charge(Actor a, Actor target, bool sees, float dist, float dt)
    {
        if (a.ChargeT > 0)
        {
            a.ChargeT -= dt;
            if (a.ChargeT > 1.2f) return true; // winding up
            Move(a, a.ChargeDir * 9 * dt);
            foreach (Actor t in Actors)
                if (t.Team == 0 && !t.Dead && V2.Distance(t.Pos, a.Pos) < a.Radius + t.Radius + 0.1f)
                {
                    Hurt(t, 45, a, false);
                    if (!t.Static) Move(t, a.ChargeDir * 1.2f);
                    a.ChargeT = 0;
                    Events.Add(new RunEvent(Ev.Melee, a.Pos, a.ChargeDir, 1, a.Id));
                }
            return true;
        }
        a.ChargeCd -= dt;
        if (a.ChargeCd <= 0 && sees && dist > 2.5f && dist < 9)
        {
            a.ChargeCd = 7;
            a.ChargeT = 1.8f;
            a.ChargeDir = M.Norm(target.Pos - a.Pos);
            a.Facing = M.Angle(a.ChargeDir);
            Events.Add(new RunEvent(Ev.Telegraph, a.Pos, a.ChargeDir, 0.6f, a.Id, "charge"));
            return true;
        }
        return false;
    }

    private void EnemyGrenade(Actor a, V2 at)
    {
        a.Nades--;
        GrenadeDef def = Tables.Grenade(a.Is(EnemyKind.Firebug) ? "fire" : "frag");
        float dist = V2.Distance(at, a.Pos);
        var g = new Grenade { Def = def, Owner = a.Id, Fuse = 1.9f, Pos = a.Pos, Flight = 0.3f + dist * 0.045f };
        g.Vel = M.Norm(at - a.Pos) * (dist / g.Flight) * 0.9f;
        Grenades.Add(g);
        Events.Add(new RunEvent(Ev.EnemyThrow, a.Pos, at, 0, a.Id));
    }

    /// <summary>Unaware guards who see a fallen comrade raise the alert.</summary>
    private void CheckBodies(Actor a, float dt)
    {
        if ((a.BodyT -= dt) > 0) return;
        a.BodyT = 0.5f;
        foreach (Actor b in Actors)
        {
            if (!b.Dead || b.Escaped || b.Team != 1 || b.Def == null || b.Is(EnemyKind.Camera) || b == CarriedBody || V2.Distance(a.Pos, b.Pos) > 7 * VisionMul) continue;
            if (MathF.Abs(M.AngleDiff(a.Facing, M.Angle(b.Pos - a.Pos))) > 0.96f || !L.Los(a.Pos, b.Pos)) continue;
            BecomeAlert(a, b.Pos);
            a.LostT = 3;
            RaiseSecurity(Tables.SecurityBody);
            Say($"A {a.Name.ToLower()} found a body.");
            return;
        }
    }

    private void StepCamera(Actor a, float dt)
    {
        Sweep(a, dt);
        if (Alarm || C.HasProgram("spoof") || C.HasProgram("ghost")) return;
        if (CanSee(a, Player))
        {
            a.Detect += dt * 1.4f * C.DetectMul * (Sneaking ? 0.7f : 1f);
            if (a.Detect >= 1 && SlipDetection(a)) return;
            if (a.Detect >= 1)
            {
                Events.Add(new RunEvent(Ev.Spotted, a.Pos, default, 0, a.Id));
                // A camera doesn't sound the alarm by itself: it pushes security up and sends the nearest guard to look.
                a.Detect = 0;
                a.BluffT = Tables.CameraRearm;
                RaiseSecurity(Tables.SecurityCamera);
                Actor look = Actors.Where(g => g.Hostile && g.Def != null && !g.Static && !g.Alert && !g.Is(EnemyKind.Camera)).OrderBy(g => V2.Distance(g.Pos, Player.Pos)).FirstOrDefault();
                if (look != null) { look.State = AiState.Suspicious; look.Goal = Player.Pos; look.StateT = 0; look.Detect = MathF.Max(look.Detect, 0.5f); }
                Say(look != null ? $"A camera caught you. Security is up, and a {look.Name.ToLower()} is coming to look." : "A camera caught you. Security is up.");
            }
        }
        else a.Detect = MathF.Max(0, a.Detect - dt * 0.3f);
    }

    private void StepHacked(Actor a, float dt)
    {
        Actor best = null;
        float bd = float.MaxValue;
        foreach (Actor t in Actors)
        {
            if (!t.Hostile || t.Is(EnemyKind.Camera)) continue;
            float d = V2.DistanceSquared(a.Pos, t.Pos);
            if (d < bd && d < a.Def.Vision * a.Def.Vision * 2 && L.Los(a.Pos, t.Pos)) { bd = d; best = t; }
        }
        if (best != null)
        {
            a.Facing = M.TurnTo(a.Facing, M.Angle(best.Pos - a.Pos), (a.Static ? 2.6f : 7f) * dt);
            a.ReactT = 0;
            Attack(a, best, MathF.Sqrt(bd), dt);
        }
        else if (a.Static) Sweep(a, dt);
        if (!a.Static && V2.Distance(a.Pos, Player.Pos) > 3) FollowField(a, PlayerField(a.Flying), Player.Pos, a.Speed, dt, face: best == null);
    }

    public float LastPlayerShot = -99;

    /// <summary>
    /// A hired runner: keeps close, keeps quiet while you are sneaking, and
    /// fights once anyone is onto you (or you open fire). The medic patches
    /// you up when you are hurting.
    /// </summary>
    private void StepAlly(Actor a, float dt)
    {
        if (a.IsDrone && Rigging) return;
        if (a.Summoned)
        {
            // A spirit: hunts the nearest hostile, burns what it touches, and fades when its time is up.
            a.Hacked -= dt;
            if (a.Hacked <= 0) { a.Dead = true; a.Escaped = true; Say("The spirit departs."); return; }
            Actor prey = Actors.Where(t => t.Hostile && !t.Is(EnemyKind.Camera)).OrderBy(t => V2.Distance(t.Pos, a.Pos)).FirstOrDefault();
            if (prey != null && V2.Distance(prey.Pos, a.Pos) < 14)
            {
                float pd = V2.Distance(prey.Pos, a.Pos);
                if (pd > a.Gun.Def.Reach + prey.Radius * 0.8f) MoveTo(a, prey.Pos, a.Speed, dt);
                a.ReactT = 0;
                if (pd < a.Gun.Def.Reach + prey.Radius && a.AttackCd <= 0) { Attack(a, prey, pd, dt); prey.Burn = 2; }
            }
            else if (V2.Distance(a.Pos, Player.Pos) > 2.5f) FollowField(a, PlayerField(true), Player.Pos, a.Speed, dt);
            return;
        }
        if (a.HealCd > 0) a.HealCd -= dt;
        float d = V2.Distance(a.Pos, Player.Pos);
        bool ade = a == Ally && Job.Hired == "ade";
        if (a.Heals > 0 && a.HealCd <= 0 && Player.Hp < Player.MaxHp * (ade ? 0.6f : 0.5f) && d < 3)
        {
            a.Heals--;
            a.HealCd = ade ? 7 : 12;
            Player.Hp = MathF.Min(Player.MaxHp, Player.Hp + (ade ? 30 : 40) * (C.CrewHas(Job.Hired, "stims") ? 1.5f : 1f));
            Events.Add(new RunEvent(Ev.Heal, Player.Pos));
            Say($"{a.Name}: \"Hold still.\" ({a.Heals} left)");
        }
        // Byte, the kid decker: blinds the site's machines near you, one at a time.
        if (a == Ally && Job.Hired == "byte" && a.HealCd <= 0)
        {
            Actor mark = Actors.Where(t => t.Hostile && t.Kind is EnemyKind.Camera or EnemyKind.Turret or EnemyKind.Drone && t.Stun <= 0 && V2.Distance(t.Pos, a.Pos) < (C.CrewHas("byte", "reach") ? 14 : 10) && L.Los(a.Pos, t.Pos))
                .OrderBy(t => V2.Distance(t.Pos, a.Pos)).FirstOrDefault();
            if (mark != null)
            {
                mark.Stun = C.CrewHas("byte", "loop") ? 16 : 8;
                a.HealCd = C.CrewHas("byte", "fast") ? 5 : 9;
                Events.Add(new RunEvent(Ev.Hack, mark.Pos));
                Say($"Byte: \"Got the {mark.Name.ToLower()}.\"");
            }
        }
        bool loud = Alarm || Time - LastPlayerShot < 2.5f || Actors.Any(e => e.Hostile && e.Alert && e.Def != null && !e.Is(EnemyKind.Camera));
        Actor foe = null;
        float fd = float.MaxValue;
        if (loud)
            foreach (Actor t in Actors)
            {
                if (!t.Hostile || t.Is(EnemyKind.Camera)) continue;
                float td = V2.Distance(a.Pos, t.Pos);
                if (td < fd && td < a.Def.Vision && L.Los(a.Pos, t.Pos)) { fd = td; foe = t; }
            }
        if (foe != null)
        {
            a.Facing = M.TurnTo(a.Facing, M.Angle(foe.Pos - a.Pos), 7 * dt);
            a.ReactT = 0;
            Attack(a, foe, fd, dt);
        }
        if (a.IsDrone && Rigging) return;
        float follow = Sneaking ? Player.Speed * 0.6f : a.Speed;
        // In a fight with nothing to shoot at: close up behind you to find a line.
        float keep = loud && foe == null ? 1.2f : 2.6f;
        if (d > keep) FollowField(a, PlayerField(a.Flying), Player.Pos, follow * (d > 6 ? 1.3f : 1), dt, face: foe == null);
    }

    /// <summary>The crowd mills about; vendors stay at their stalls and watch you.</summary>
    private void StepNpc(Actor a, float dt)
    {
        if (a.PanicT > 0) { Flee(a, dt); return; }
        if (a.Vendor != null)
        {
            if (V2.Distance(a.Pos, Player.Pos) < 5) a.Facing = M.TurnTo(a.Facing, M.Angle(Player.Pos - a.Pos), 3 * dt);
            return;
        }
        a.StateT -= dt;
        if (a.StateT <= 0 || V2.Distance(a.Pos, a.Goal) < 0.5f)
        {
            a.StateT = R.Range(4f, 10f);
            for (int k = 0; k < 8; k++)
            {
                V2 g = a.Home + new V2(R.Range(-8f, 8f), R.Range(-5f, 5f));
                if (L.Walkable((int)g.X, (int)g.Y)) { a.Goal = g; break; }
            }
        }
        V2 d = a.Goal - a.Pos;
        if (d.LengthSquared() > 0.05f)
        {
            V2 before = a.Pos;
            Move(a, M.Norm(d) * a.Speed * dt);
            a.Facing = M.TurnTo(a.Facing, M.Angle(d), 4 * dt);
            if (V2.Distance(before, a.Pos) < a.Speed * dt * 0.2f) a.StateT = 0;
        }
    }

    private void StepHostage(Actor a, float dt)
    {
        if (a.State != AiState.Follow) return;
        float d = V2.Distance(a.Pos, Player.Pos);
        if (d > 1.7f) FollowField(a, PlayerField(false), Player.Pos, a.Speed * (d > 5 ? 1.2f : 1f), dt);
    }

    // ---- alarm ----------------------------------------------------------------------

    public void RaiseAlarm(string why)
    {
        if (Alarm) return;
        Alarm = true;
        AlarmMeter = AlarmAfter;
        if (Security < 3) { Security = MathF.Max(Security, 3); while (_levelDone < 3) { _levelDone++; if (_levelDone < 3) OnLevel(_levelDone); } }
        _waveT = 18;
        Events.Add(new RunEvent(Ev.Alarm, Player.Pos, Text: why));
        foreach (Actor a in Actors)
            if (a.Hostile && a.Def != null && !a.Is(EnemyKind.Camera)) { BecomeAlert(a, Player.Pos, false); a.LostT = 0; }
    }

    private void StepAlarm(float dt)
    {
        StepSecurity(dt);
        // (Kept for the agent and the music: how close to the alarm, in the old seconds.)
        AlarmMeter = Alarm ? AlarmAfter : MathF.Min(AlarmAfter, Security / 3 * AlarmAfter);
        if (!Alarm) return;
        _waveT -= dt;
        if (_waveT > 0 || _waves >= (Job.Threat == 1 ? 1 : 4)) return;
        _waveT = 25;
        _waves++;
        V2 at = Plan.Reinforce.OrderByDescending(p => V2.Distance(p, Player.Pos)).First();
        // In a tower, they come up in the elevator to whatever floor you're on.
        if (Plan.Tower && FloorOf(Player.Pos) > 0) at = Plan.LiftSpots[FloorOf(Player.Pos)];
        var roster = WaveRoster();
        int n = 2 + Job.Threat / 2;
        for (int k = 0; k < n; k++)
        {
            EnemyKind kind = PickFrom(roster);
            V2 p = at + new V2(R.Range(-1f, 1f), R.Range(-1f, 1f));
            if (L.Solid((int)p.X, (int)p.Y)) p = at;
            Actor e = AddEnemy(kind, p, M.Angle(Player.Pos - p));
            BecomeAlert(e, Player.Pos, false);
        }
        Events.Add(new RunEvent(Ev.Wave, at, Text: "Reinforcements inbound."));
    }

    /// <summary>Who comes as reinforcements: the faction's roster, gated by threat the way the site's own guards are.</summary>
    private (EnemyKind kind, float weight)[] WaveRoster() => Tables.Roster(Job.Faction)
        .Where(e => (Job.Threat >= 2 || e.kind is not (EnemyKind.Heavy or EnemyKind.Sniper or EnemyKind.Mage))
            && (Job.Threat >= 3 || e.kind is not (EnemyKind.Shield or EnemyKind.Rigger or EnemyKind.Spirit))
            && Job.Threat >= Tables.FirstThreat(e.kind)
            && e.kind is not (EnemyKind.Sniper or EnemyKind.Hound))
        .DefaultIfEmpty((EnemyKind.Guard, 1f)).ToArray();

    private EnemyKind PickFrom((EnemyKind kind, float weight)[] roster)
    {
        float roll = R.Float() * roster.Sum(x => x.weight);
        foreach (var e in roster) { roll -= e.weight; if (roll <= 0) return e.kind; }
        return roster[0].kind;
    }

    // ---- getting about --------------------------------------------------------------

    private void MoveTo(Actor a, V2 goal, float speed, float dt, bool face = true)
    {
        Pt gt = Pt.Of(goal);
        if (!L.In(gt)) return;
        Pt here = Pt.Of(a.Pos);
        int hi = L.In(here) ? L.Idx(here.X, here.Y) : -1;
        // Heading for the tile you're standing on: the shared field from you is the same
        // search (out to 400 tiles rather than 200, so it agrees wherever this one would be final).
        if (gt == _fieldFrom && _walkField != null && _fieldPass == L.PassVersion && gt == Pt.Of(Player.Pos) && hi >= 0)
        {
            ushort[] shared = a.Flying ? PlayerField(true) : _walkField;
            if (shared[hi] <= 2000) { FollowField(a, shared, goal, speed, dt, face: face); return; }
        }
        // The search stops a few tiles past the walker: the steps it takes read nothing further.
        // Searched again for a new goal, when the way changes, or once pushed out past what it covered.
        if (a.Field == null || a.FieldFor != gt || a.FieldPass != L.PassVersion || hi >= 0 && a.Field[hi] > a.FieldReach)
        {
            a.Field = L.Field(gt, 200, a.Flying, a.Field, hi, 40, out a.FieldReach);
            a.FieldFor = gt;
            a.FieldPass = L.PassVersion;
        }
        FollowField(a, a.Field, goal, speed, dt, face: face);
    }

    /// <summary>Walk down (or, fleeing, up) a distance field, cutting corners where the line is clear.</summary>
    private void FollowField(Actor a, ushort[] field, V2 goal, float speed, float dt, bool away = false, bool face = true)
    {
        Pt here = Pt.Of(a.Pos);
        V2 target;
        if (!away && here == Pt.Of(goal)) target = goal;
        else
        {
            Pt n = L.Step(field, here, a.Flying, away);
            if (L.OtherEnd(here, out Pt end) && n == end) { a.Pos = end.Centre; return; }
            if (n == here) target = away ? a.Pos : goal;
            else
            {
                Pt n2 = L.Step(field, n, a.Flying, away);
                target = n2 != n && Clear(a.Pos, n2.Centre, a.Flying) ? n2.Centre : n.Centre;
            }
        }
        V2 d = target - a.Pos;
        float len = d.Length();
        if (len < 0.02f) return;
        V2 dir = d / len;
        Move(a, dir * MathF.Min(len, speed * dt));
        if (face) a.Facing = M.TurnTo(a.Facing, M.Angle(dir), 8 * dt);
    }

    private bool Clear(V2 a, V2 b, bool fly)
    {
        var w = new Level.Walker(a, b);
        while (w.Next(out int x, out int y))
            if (L.Solid(x, y, fly) && L[x, y] != Tile.Door) return false;
        return true;
    }
}
