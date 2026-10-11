using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>What fills a gas cloud: coolant from a ruptured pipe (freezes), a lab's toxic vapour (burns), a clinic's anaesthetic (puts people under).</summary>
public enum GasKind { Coolant, Toxic, Anaesthetic }

/// <summary>A cloud of gas on the floor: it spreads to its radius, hangs, then thins.</summary>
public sealed class GasCloud
{
    public V2 Pos;
    public float Radius, T, Life;
    public GasKind Kind;
    /// <summary>How far it has spread (it grows over its first second and a half).</summary>
    public float Reach => Radius * MathF.Min(1, 0.35f + T / 1.5f);
}

/// <summary>
/// Rooms with a purpose, on the run: guards on their break or asleep, a
/// spirit bound to its circle, coolant pipes that freeze whoever's beside
/// them, a target who runs for the panic room, and a word on walking in.
/// </summary>
public sealed partial class Run
{
    public readonly List<GasCloud> Clouds = new();
    private readonly HashSet<int> _announced = new();
    private bool _panicSealed;

    /// <summary>Gas from a broken pipe or vat at this spot.</summary>
    public void Gas(V2 at, GasKind kind, float radius, float life)
    {
        Clouds.Add(new GasCloud { Pos = at, Kind = kind, Radius = radius, Life = life });
        Events.Add(new RunEvent(Ev.Gas, at, default, radius, (int)kind));
        Noise(at, 8, false);
    }

    /// <summary>Inside a cloud (of this kind, or any).</summary>
    public bool InGas(V2 p, GasKind? kind = null) => Clouds.Any(c => (kind == null || c.Kind == kind) && V2.Distance(c.Pos, p) < c.Reach);

    /// <summary>A cloud between two points (or round either): you can't see through it.</summary>
    public bool GasBetween(V2 a, V2 b) => Clouds.Any(c => M.SegDist(a, b, c.Pos, out _) < c.Reach * 0.8f);

    private void StepGas(float dt)
    {
        for (int i = Clouds.Count - 1; i >= 0; i--)
        {
            GasCloud c = Clouds[i];
            c.T += dt;
            if (c.T > c.Life) { Clouds.RemoveAt(i); continue; }
            foreach (Actor a in ActorsNow)
            {
                if (a.Dead || a.IsGhost || a.Def != null && (a.Def.Kind is EnemyKind.Camera or EnemyKind.Turret or EnemyKind.Drone or EnemyKind.Spirit)) continue;
                if (V2.Distance(a.Pos, c.Pos) >= c.Reach) continue;
                if (c.Kind == GasKind.Coolant)
                {
                    // The cold locks the joints: a guard in it can't fight; you can, slowly.
                    a.Hp -= (a.IsPlayer ? Tables.CoolantHurtYou : Tables.CoolantHurt) * dt;
                    if (!a.IsPlayer && !a.IsAlly) a.Stun = MathF.Max(a.Stun, 0.4f);
                }
                else if (c.Kind == GasKind.Anaesthetic)
                {
                    // Under: a guard who breathes it drops where they stand; you only slow (Run.Player).
                    if (!a.IsPlayer && !a.IsAlly && !a.IsNpc) a.Stun = MathF.Max(a.Stun, Tables.AnaestheticSleep);
                    continue;
                }
                else a.Hp -= (a.IsPlayer ? Tables.ToxicHurtYou : Tables.ToxicHurt) * dt;
                if (a.Hp <= 0 && !a.Dead) Kill(a, a.IsPlayer ? null : Player);
            }
        }
    }

    /// <summary>Guards off duty or asleep go back to normal once something's up; the target runs for the panic room.</summary>
    private void StepRooms()
    {
        foreach (Actor a in Actors)
        {
            if (a.Dead || a.Mood is not (Mood.OffDuty or Mood.Asleep)) continue;
            if (a.State != AiState.Idle || SecurityLevel >= 3 || a.Stun > 0) a.Mood = Mood.None;
        }
        // A word on walking into a room with a purpose (once per room).
        if (!Peaceful && Plan.RoomOf != null)
        {
            Pt p = Pt.Of(Player.Pos);
            int i = L.In(p) ? Plan.RoomOf[L.Idx(p.X, p.Y)] : -1;
            if (i >= 0 && i < Plan.Rooms.Count && _announced.Add(i) && SetPieces.Line(Plan.Rooms[i].Kind) is string line) Say(line);
        }
        StepPanic();
        StepStyle();
    }

    private bool _cleanTripped, _sprinklers;

    /// <summary>
    /// The site's own rules: a lab's clean room logs whoever walks in without a
    /// suit; an office's night cleaner who sees a gun runs for a phone (stop
    /// them before they get there); a dog smells you through walls (Perceive).
    /// </summary>
    private void StepStyle()
    {
        if (Plan.CleanRoom.W > 0 && !_cleanTripped && !HasSuit && !Disguised && Plan.CleanRoom.Contains(Pt.Of(Player.Pos)))
        {
            _cleanTripped = true;
            RaiseSecurity(1);
            Say("Contamination sensors: the clean room logs an intruder. Security is on edge.");
        }
        for (int i = 0; i < Actors.Count; i++)
        {
            Actor a = Actors[i];
            if (!a.Cleaner || a.Dead) continue;
            if (a.CallT > 0)
            {
                a.CallT -= Dt;
                if (a.CallT <= 0)
                {
                    a.CallT = -1;
                    // A store's panic button rings Lone Star, not a guard in the back.
                    bool store = Job.Style == SiteStyle.Store;
                    RaiseSecurity(store ? 2 : 1);
                    Say(store ? "The clerk hit the panic button. Lone Star is on its way." : Job.Style == SiteStyle.Clinic ? "The orderly got through on the intercom." : "The cleaner got through to security.");
                }
                continue;
            }
            if (a.CallT < 0) continue;
            // A gun in hand, in plain sight, close enough to see it's a gun.
            float d = V2.Distance(a.Pos, Player.Pos);
            if (CurrentGun != null && d < 7 && L.Los(a.Pos, Player.Pos) && MathF.Abs(M.AngleDiff(a.Facing, M.Angle(Player.Pos - a.Pos))) < 1.2f && !Disguised)
            {
                a.CallT = Job.Style == SiteStyle.Store ? Tables.PanicButton : Tables.CleanerCall;
                a.PanicT = 8;
                a.PanicFrom = Player.Pos;
                Say(Job.Style switch
                {
                    SiteStyle.Store => "The clerk saw your gun and is reaching under the counter. Stop them.",
                    SiteStyle.Clinic => "An orderly saw your gun and is running for the intercom. Stop them.",
                    _ => "A night cleaner saw your gun and is running for a phone. Stop them.",
                });
            }
        }
    }

    /// <summary>What this site's style does, in a few words (null on a peaceful site).</summary>
    public (string name, string what)? StyleRule => Peaceful || Job.Type is JobType.Defend or JobType.Hack ? null
        : Job.Type == JobType.Bounty ? (Job.Hive ? ("HIVE", "resin walls, egg sacs that hatch") : ("NEST", "they hunt by scent, and they're hungry"))
        : Job.Style switch
    {
        SiteStyle.Lab => ("LAB", Plan.CleanRoom.W > 0 ? "toxic vats, sprinklers, a clean room" : "toxic vats, sprinklers"),
        SiteStyle.Office => ("OFFICE", L.Keycards.Count > 0 || HasBadge ? "night cleaners, keycard doors" : "night cleaners"),
        SiteStyle.Warehouse => ("WAREHOUSE", Plan.BackDoor.W > 0 ? "shelving you can push over, a dock exit" : "shelving you can push over"),
        SiteStyle.Den => ("DEN", "dogs smell you, some are drunk"),
        SiteStyle.Club => ("CLUB", "the music hides shots on the floor"),
        SiteStyle.Tunnels => ("TUNNELS", "dark, the water slows and splashes"),
        SiteStyle.Store => ("STORE", "shoppers everywhere, the clerk has a panic button"),
        SiteStyle.Clinic => ("CLINIC", "orderlies call it in, the theatre's gas puts people under"),
        _ => null,
    };

    /// <summary>A lab's sprinklers drown a fire fast (the first time, they say so).</summary>
    public float FireMul()
    {
        if (Job.Style != SiteStyle.Lab || Fire.Count == 0) return 1;
        if (!_sprinklers) { _sprinklers = true; Say("Sprinklers come on."); }
        return Tables.Sprinklers;
    }

    /// <summary>A shot on a club's dance floor is lost in the music (while the power's on).</summary>
    public float MusicMul(V2 at) => Job.Style == SiteStyle.Club && !Job.Has(RunMod.Blackout) && L.In(Pt.Of(at)) && L.F[L.Idx((int)at.X, (int)at.Y)] == Floor.Dance ? Tables.MusicMask : 1;

    /// <summary>
    /// The target, once the alarm is up (or they've seen you), runs for the
    /// panic room; inside, the doors seal (hack them or blow them).
    /// </summary>
    private void StepPanic()
    {
        if (Vip == null || Vip.Dead || Vip.KO || Plan.PanicRoom.W == 0 || _panicSealed) return;
        if (Plan.PanicRoom.Contains(Pt.Of(Vip.Pos)) && Vip.Alert)
        {
            _panicSealed = true;
            Rect rim = Plan.PanicRoom.Inset(-1);
            for (int y = rim.Y; y < rim.Bottom; y++)
            for (int x = rim.X; x < rim.Right; x++)
                if (!Plan.PanicRoom.Contains(new Pt(x, y)) && L[x, y] is Tile.Door or Tile.DoorOpen) L.Set(x, y, Tile.LockedDoor);
            Vip.Static = true;
            Say($"{Vip.Name} made it into the panic room and sealed the door. Hack it, or blow it.");
        }
    }

    /// <summary>Where a frightened target is heading: the panic room, when there is one and it isn't sealed yet.</summary>
    private V2? PanicGoal(Actor a) => a == Vip && Plan.PanicRoom.W > 0 && !_panicSealed && !Plan.PanicRoom.Contains(Pt.Of(a.Pos))
        ? new V2(Plan.PanicRoom.X + Plan.PanicRoom.W / 2f, Plan.PanicRoom.Y + Plan.PanicRoom.H / 2f) : null;
}
