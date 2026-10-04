using System;
using System.Linq;

namespace Sprawl;

public sealed class DroneDef
{
    public string Id, Name, Desc, Weapon;
    public int Cost, Loyalty;
    public float Hp, Speed;
}

/// <summary>
/// Rigging: your drone comes along on the run. Jack in (T) and you fly it --
/// WASD, the mouse aims, LMB fires if it is armed -- seeing through its eyes,
/// while your body stands still and exposed (a hit pulls you out). Stray too
/// far and the signal drops. Off the stick it follows you, shows you what it
/// sees, and fights when things get loud.
/// </summary>
public sealed partial class Run
{
    public Actor Drone;
    public bool Rigging;
    public bool DroneLost;

    public float DroneRange => C.Has("controlrig2") ? 45 : C.Has("controlrig") ? 30 : 15;
    private float DroneDamage => (C.Has("controlrig") ? 1.3f : 1f) * (C.Has("controlrig2") ? 1.3f : 1f);

    private void SetupDrone()
    {
        DroneDef d = C.DroneSel == null ? null : Tables.Drones.FirstOrDefault(x => x.Id == C.DroneSel);
        if (d == null || !C.Drones.Contains(d.Id)) return;
        float hpMul = C.Has("controlrig") ? 1.5f : 1f;
        var def = new EnemyDef { Kind = EnemyKind.Drone, Name = d.Name, Hp = d.Hp * hpMul, Speed = d.Speed, Weapon = d.Weapon, Flying = true, Radius = 0.3f, Vision = 11, PreferRange = 5 };
        Drone = new Actor
        {
            Id = _nextId++, Def = def, Team = 0, IsAlly = true, Name = d.Name, Pos = Player.Pos + new V2(-0.7f, 0.4f), Radius = 0.3f,
            MaxHp = def.Hp, Hp = def.Hp, Speed = d.Speed, Flying = true, State = AiState.Alert, IsDrone = true,
        };
        if (d.Weapon != null) { WeaponDef w = Tables.Weapon(d.Weapon); Drone.Gun = new Gun { Def = w, Ammo = w.Mag, Mag = w.Mag }; }
        Actors.Add(Drone);
    }

    private void ToggleRig()
    {
        if (Drone == null || Drone.Dead) { Say("No drone to jack into."); Events.Add(new RunEvent(Ev.Empty, Player.Pos)); return; }
        Rigging = !Rigging;
        Events.Add(new RunEvent(Ev.Hack, Rigging ? Drone.Pos : Player.Pos));
        Say(Rigging ? $"Jacked into the {Drone.Name}. Your body is standing still -- T to come back." : "Back in your own head.");
        UpdateFov(true);
    }

    private void StepRig(RunInput inp, float dt)
    {
        Player.Vel = V2.Zero;
        Actor d = Drone;
        if (d == null || d.Dead) { Rigging = false; UpdateFov(true); return; }
        V2 move = inp.Move.LengthSquared() > 1 ? M.Norm(inp.Move) : inp.Move;
        Move(d, move * d.Speed * dt);
        V2 to = inp.Aim - d.Pos;
        if (to.LengthSquared() > 0.01f) d.Facing = M.Angle(to);
        Gun g = d.Gun;
        if (g != null)
        {
            if (g.Cooldown > 0) g.Cooldown -= dt;
            if (g.Reload > 0) { g.Reload -= dt; if (g.Reload <= 0) g.Ammo = g.Mag; }
            if (inp.Fire && g.Reload <= 0 && g.Cooldown <= 0)
            {
                if (g.Ammo <= 0) g.Reload = g.Def.Reload;
                else
                {
                    float dmg = g.Def.Damage * DroneDamage * 2.2f;
                    SpawnShot(d, g.Def, d.Facing + R.Bell() * g.Def.Spread * MathF.PI / 360f, dmg, false, to.Length());
                    g.Ammo--;
                    g.Cooldown = 1f / g.Def.Rate;
                    LastPlayerShot = Time;
                    Events.Add(new RunEvent(Ev.Shot, d.Pos, M.Dir(d.Facing), (int)g.Def.Class, d.Id));
                    Noise(d.Pos, g.Def.Noise, true);
                }
            }
        }
        float dist = V2.Distance(d.Pos, Player.Pos);
        if (dist > DroneRange)
        {
            Rigging = false;
            Say("Signal lost -- the drone is out of range. It is flying back.");
            UpdateFov(true);
        }
    }

    /// <summary>The drone's eyes, when you are not riding it: what it can see, you are shown.</summary>
    private bool DroneSees(Actor a) => Drone != null && !Drone.Dead && !Rigging && !a.Dead && V2.Distance(a.Pos, Drone.Pos) < 9 && L.Los(Drone.Pos, a.Pos);

    public float DroneSignal => Drone == null ? 0 : MathF.Max(0, 1 - V2.Distance(Drone.Pos, Player.Pos) / DroneRange);
}
