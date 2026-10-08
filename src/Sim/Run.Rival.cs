using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// The rival crew on a contested job: they come in partway through, from the
/// far side, and race you for the objective -- shooting the guards, the
/// guards shooting them, both shooting you. A carrier who goes down drops it.
/// </summary>
public sealed partial class Run
{
    /// <summary>When they turn up (s; 0: they aren't coming), and whether they have.</summary>
    private float _rivalAt;
    public bool RivalsCame;
    /// <summary>The rival with the objective, and where they're heading out; how far into the mainframe they are.</summary>
    public Actor RivalCarrier;
    private V2 _rivalExit;
    private float _rivalHack;
    public bool RivalRobbed, RivalKilledTarget;
    public readonly List<string> RivalDead = new();
    public int RivalSpared;

    private void PlanRival()
    {
        if (Job.Tag == "rival" && C.Rival is { Active: true }) { HomeRivals(); return; }
        if (!Job.Contested || C.Rival is not { Active: true } || Peaceful) return;
        _rivalAt = new Rng(Job.Seed ^ 0x717171UL).Range(Tables.RivalFrom, Tables.RivalTo);
    }

    /// <summary>Their hideout: the crew at home in the deepest rooms, on the same side as the muscle they hired.</summary>
    private void HomeRivals()
    {
        var rooms = Plan.Rooms.OrderByDescending(x => x.Depth).ToList();
        RivalCrew crew = C.Rival;
        for (int k = 0; k < crew.Members.Count + 1 && rooms.Count > 0; k++)
        {
            Room room = rooms[k % Math.Min(rooms.Count, 3)];
            V2 at = new(room.R.X + 1.5f + (k % 3), room.R.Y + 1.5f);
            if (L.Solid((int)at.X, (int)at.Y)) at = room.Centre;
            Actor a = AddEnemy(k == 0 ? EnemyKind.Heavy : EnemyKind.Guard, at, 0);
            a.Rival = true;
            a.Name = k == 0 ? crew.Leader : crew.Members[k - 1];
        }
        RivalsCame = true;
    }

    /// <summary>Friend or foe, for anyone picking a target: guards and rivals fight each other, and both fight you.</summary>
    private static bool Foes(Actor a, Actor t) => a.Team switch
    {
        1 => t.Team is 0 or 3,
        3 => t.Team is 0 or 1 && !t.IsMedic,
        _ => t.Team == 0,
    };

    private void StepRivalArrival()
    {
        if (_rivalAt <= 0 || RivalsCame || Time < _rivalAt) return;
        RivalsCame = true;
        RivalCrew crew = C.Rival;
        // In from the edge farthest from you, and out the same way.
        V2 at = Plan.Reinforce.OrderByDescending(p => V2.Distance(p, Player.Pos)).First();
        _rivalExit = at;
        float hp = crew.Tier >= 3 ? 1.3f : 1;
        for (int k = 0; k < crew.Members.Count; k++)
        {
            EnemyKind kind = crew.Tier >= 2 && k == crew.Members.Count - 1 ? EnemyKind.Sniper : EnemyKind.Guard;
            Actor a = AddEnemy(kind, at + new V2((k % 2) * 0.8f, (k / 2) * 0.8f), 0);
            a.Team = 3;
            a.Rival = true;
            a.Name = crew.Members[k];
            a.MaxHp = a.Hp = a.MaxHp * hp * 1.15f;
            if (crew.Tier >= 3) a.Armor += 2;
            a.State = AiState.Alert;
            a.Detect = 1;
        }
        if (crew.Tier >= 1)
        {
            Actor d = AddEnemy(EnemyKind.Drone, at + new V2(0.4f, -0.6f), 0);
            d.Team = 3;
            d.Rival = true;
            d.Name = $"{crew.Name}'s drone";
            d.State = AiState.Alert;
        }
        Events.Add(new RunEvent(Ev.Alarm, at, Text: $"{crew.Name} are here. {crew.Leader} sent {crew.Members.Count} -- it's three ways now."));
        Noise(at, 14, false);
    }

    /// <summary>A rival runner: fight whoever is in the way, else go for the objective (or out, holding it).</summary>
    private void StepRivalActor(Actor a, float dt)
    {
        Actor target = PickTarget(a, out bool sees);
        float dist = V2.Distance(a.Pos, target.Pos);
        float reach = a.Gun?.Def.Range ?? 2;
        if (sees && dist < reach * 0.9f && a != RivalCarrier)
        {
            a.Facing = M.TurnTo(a.Facing, M.Angle(target.Pos - a.Pos), 8 * dt);
            Attack(a, target, dist, dt);
            return;
        }
        if (a.Is(EnemyKind.Drone))
        {
            // The drone stays with its crew.
            Actor lead = Actors.FirstOrDefault(x => x.Rival && !x.Dead && !x.Is(EnemyKind.Drone));
            if (lead != null && V2.Distance(a.Pos, lead.Pos) > 2) MoveTo(a, lead.Pos, a.Speed, dt);
            return;
        }
        V2 goal = RivalGoal(a);
        if (a == RivalCarrier && V2.Distance(a.Pos, _rivalExit) < 1.5f)
        {
            // Gone, with it.
            a.Escaped = true;
            a.Dead = true;
            RivalCarrier = null;
            RivalRobbed = true;
            ObjectiveLost = true;
            Say($"{a.Name} got out with {TheItem}. The job is blown.");
            return;
        }
        MoveTo(a, goal, a.Speed * (a == RivalCarrier ? 1.05f : 0.95f), dt);
        if (sees && dist < reach) { a.Facing = M.TurnTo(a.Facing, M.Angle(target.Pos - a.Pos), 6 * dt); Attack(a, target, dist, dt); }
        RivalWork(a, dt);
    }

    private V2 RivalGoal(Actor a)
    {
        if (a == RivalCarrier || RivalRobbed) return _rivalExit;
        switch (Job.Type)
        {
            case JobType.Retrieve when Pickups.FirstOrDefault(p => p.Kind == PickupKind.Objective && !p.Taken) is Pickup o: return o.Pos;
            case JobType.Retrieve when Carrying: return Player.Pos;
            case JobType.Datasteal when !ObjectiveDone: return Plan.Mainframe.Centre;
            case JobType.Eliminate when Vip != null && !Vip.Dead: return Vip.Pos;
        }
        return Player.Pos;
    }

    /// <summary>At the objective: pick it up, or start pulling the data (they're slower at it than you).</summary>
    private void RivalWork(Actor a, float dt)
    {
        if (ObjectiveLost) return;
        if (Job.Type == JobType.Retrieve && RivalCarrier == null)
        {
            Pickup o = Pickups.FirstOrDefault(p => p.Kind == PickupKind.Objective && !p.Taken);
            if (o != null && V2.Distance(o.Pos, a.Pos) < 0.8f)
            {
                o.Taken = true;
                RivalCarrier = a;
                Say($"{a.Name} has {TheItem} and is running for it. Stop them.");
            }
        }
        if (Job.Type == JobType.Datasteal && !ObjectiveDone && V2.Distance(Plan.Mainframe.Centre, a.Pos) < 1.6f)
        {
            if (_rivalHack == 0) Say($"{a.Name} is jacked into the mainframe. Pull them off it.");
            _rivalHack += dt;
            if (_rivalHack >= Tables.RivalHack)
            {
                RivalRobbed = true;
                ObjectiveLost = true;
                Say($"{C.Rival.Name} pulled the data first. The job is blown.");
            }
        }
    }

    /// <summary>A rival down: the dead are remembered, the knocked out spared; a carrier drops what they carried.</summary>
    private void RivalDown(Actor a, Actor by, bool ko)
    {
        if (!a.Rival || a.Is(EnemyKind.Drone) || a.Name == C.Rival?.Leader) return;
        if (ko) RivalSpared++;
        else RivalDead.Add(a.Name);
        if (a == RivalCarrier)
        {
            RivalCarrier = null;
            Pickups.Add(new Pickup { Kind = PickupKind.Objective, Pos = a.Pos, Name = Job.Item });
            Say($"{a.Name} dropped {TheItem}.");
        }
        if (Job.Type == JobType.Datasteal && Actors.All(x => !x.Rival || x.Dead || V2.Distance(x.Pos, Plan.Mainframe.Centre) > 1.6f)) _rivalHack = 0;
    }
}
