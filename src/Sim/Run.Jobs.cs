using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// The newer kinds of job: Investigate (three pieces of evidence), Plant (a
/// device left at a marked spot) and Defend (hold out against waves until the
/// clock runs down -- the safehouse raid is one of these).
/// </summary>
public sealed partial class Run
{
    public int CluesFound;
    public int ClueCount => Math.Max(1, Pickups.Count(p => p.Kind == PickupKind.Clue));
    public float PlantProgress;
    public int Waves;
    /// <summary>Escort: how far the client's meeting has got (0..1).</summary>
    public float EscortProgress;
    /// <summary>Hack: times ICE traced you.</summary>
    public int Traces;

    private void StepEscort(float dt)
    {
        if (Hostage == null || Hostage.Dead || EscortProgress >= 1 || Plan.PlantAt.X < 0) return;
        if (V2.Distance(Hostage.Pos, Plan.PlantAt) < 3.2f && V2.Distance(Player.Pos, Plan.PlantAt) < 4)
        {
            float before = EscortProgress;
            EscortProgress = MathF.Min(1, EscortProgress + dt / 10f);
            if (before == 0) Say($"{Job.Target}: \"Give me a minute. Watch the door.\"");
            if (EscortProgress >= 1) Say($"{Job.Target}: \"Done. Get me out of here.\"");
        }
    }
    private float _defendT = 4;

    /// <summary>How long a Defend job has to be held.</summary>
    public float DefendFor => 75 + 12 * Job.Threat;

    private void StepDefend(float dt)
    {
        if (ObjectiveDone || Time >= DefendFor - 8) return;
        _defendT -= dt;
        if (_defendT > 0) return;
        _defendT = 16 - Job.Threat;
        Waves++;
        var roster = WaveRoster();
        V2 at = Plan.Reinforce[R.Int(Plan.Reinforce.Count)];
        int n = 2 + Job.Threat / 2 + Waves / 2;
        for (int k = 0; k < n; k++)
        {
            V2 p = at + new V2(R.Range(-1.2f, 1.2f), R.Range(-1.2f, 1.2f));
            if (L.Solid((int)p.X, (int)p.Y)) p = at;
            Actor a = AddEnemy(PickFrom(roster), p, M.Angle(Player.Pos - p));
            BecomeAlert(a, Player.Pos, false);
        }
        Events.Add(new RunEvent(Ev.Wave, at, Text: Waves == 1 ? "Here they come!" : $"Wave {Waves} coming in."));
    }

    /// <summary>The Iron Spire: whoever you helped on the way sends something.</summary>
    private void Finale()
    {
        if (C.ArcDone("arms")) { Nades["frag"] = Nades.GetValueOrDefault("frag") + 2; Nades["breach"] = Nades.GetValueOrDefault("breach") + 1; }
        if (C.ArcDone("decker")) foreach (Actor a in Actors.Where(a => a.Is(EnemyKind.Camera))) a.Stun = 1e9f;
        if (C.ArcDone("doc")) Medkits += 2;
        var help = C.FinaleHelp();
        if (help.Count > 0) Say("Help arrived. " + string.Join(". ", help) + ".");
    }
}

/// <summary>
/// TraumaLink on a corporate site: the insured get a High Threat Response
/// team. They come for the downed, bag them and leave -- unless someone shoots
/// at them. And what the legwork bought before the run.
/// </summary>
public sealed partial class Run
{
    public bool HtrHostile;
    private bool _htrCalled;
    private float _htrT = -1;
    private Actor _htrBody;
    private V2 _htrDoor;

    /// <summary>A corp guard is down on a big Halcyon site: half the time, someone's policy pays out.</summary>
    private void TraumaCall(Actor dead)
    {
        if (_htrCalled || Peaceful || Job.Faction != Faction.Halcyon || Job.Threat < 4 || Job.Type == JobType.Bounty) return;
        if (dead.Kind is EnemyKind.Drone or EnemyKind.Turret or EnemyKind.Camera or EnemyKind.Hound or EnemyKind.Spirit) return;
        _htrCalled = true;
        if (Job.Threat < 6 && !new Rng(Job.Seed ^ 0x7E4UL).Chance(0.5f)) return;
        _htrBody = dead;
        _htrT = 6;
    }

    private void StepTrauma(float dt)
    {
        if (_htrT < 0) return;
        _htrT -= dt;
        if (_htrT > 0) return;
        _htrT = -1;
        _htrDoor = Plan.Reinforce.OrderBy(p => V2.Distance(p, _htrBody.Pos)).First();
        for (int k = 0; k < 3; k++)
        {
            V2 p = _htrDoor + new V2(k * 0.7f - 0.7f, 0.4f);
            if (L.Solid((int)p.X, (int)p.Y)) p = _htrDoor;
            Actor m = AddEnemy(EnemyKind.Guard, p, M.Angle(_htrBody.Pos - p));
            m.Team = 2;
            m.IsMedic = true;
            m.Name = k == 2 ? "TraumaLink escort" : "TraumaLink medic";
            m.Armor += 3;
            m.MaxHp = m.Hp = m.MaxHp * 1.5f;
            WeaponDef w = Tables.Weapon(k == 2 ? "e_rifle" : "e_smg");
            m.Gun = new Gun { Def = w, Ammo = w.Mag, Mag = w.Mag };
        }
        Events.Add(new RunEvent(Ev.Wave, _htrDoor, Text: "A TraumaLink HTR team is coming in for the downed guard. Leave them be and they'll leave you be."));
    }

    private void StepMedic(Actor a, float dt)
    {
        if (_htrBody != null && !_htrBody.Escaped)
        {
            if (V2.Distance(a.Pos, _htrBody.Pos) > 1.1f) { MoveTo(a, _htrBody.Pos, a.Speed, dt); return; }
            a.StateT += dt;
            if (a.StateT >= 3)
            {
                _htrBody.Escaped = true;
                Say("The HTR team bags the guard and pulls out.");
            }
            return;
        }
        if (V2.Distance(a.Pos, _htrDoor) > 1.5f) MoveTo(a, _htrDoor, a.Speed, dt);
        else { a.Dead = true; a.Escaped = true; }
    }

    /// <summary>Somebody shot at the ambulance crew.</summary>
    private void ShotAtTrauma()
    {
        if (HtrHostile) return;
        HtrHostile = true;
        foreach (Actor m in Actors.Where(x => x.IsMedic && !x.Dead)) { m.Team = 1; BecomeAlert(m, Player.Pos, false); }
        Events.Add(new RunEvent(Ev.Alarm, Player.Pos, Text: "You shot at TraumaLink. They shoot back -- and TraumaLink will remember."));
    }

    // ---- legwork ------------------------------------------------------------------------

    private void ApplyLegwork()
    {
        var lw = Job.Legwork;
        if (lw.Count == 0) return;
        if (lw.Contains("bribe"))
        {
            // Two of the rank and file called in sick.
            var gone = Actors.Where(a => a.Hostile && a.Def != null && !a.Static && a.Kind is not (EnemyKind.Vip or EnemyKind.Boss or EnemyKind.Camera or EnemyKind.Turret or EnemyKind.Drone) && !a.Bodyguard).Take(2).ToList();
            foreach (Actor a in gone) Actors.Remove(a);
        }
        if (lw.Contains("routes")) OverwatchT = MathF.Max(OverwatchT, 15);
        if (lw.Contains("power") && !Job.Has(RunMod.Blackout))
        {
            VisionMul *= 0.7f;
            FovPenalty += 4;
            foreach (Actor a in Actors.Where(a => a.Is(EnemyKind.Camera))) a.Stun = 1e9f;
        }
        if (lw.Contains("door"))
            for (int y = 0; y < L.H; y++)
            for (int x = 0; x < L.W; x++)
                if (L[x, y] == Tile.LockedDoor) L.Set(x, y, Tile.Door);
        Say("Legwork paid off: " + string.Join(", ", lw.Select(id => Career.LegworkName(id))) + ".");
    }
}
