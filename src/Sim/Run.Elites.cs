using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// The high end. Each of these answers something the runner has learned to
/// lean on:
/// - the spider (a security decker at his deck) slows every hack while he's in
///   the system, and once he knows you're there takes back what you hacked and
///   re-locks the doors you opened -- find him first;
/// - the ghost operative is only visible up close (or to thermographic eyes,
///   or astral sight), and for a moment after it fires;
/// - the bulwark's armour shrugs off small arms, and its slam throws you;
/// - the barghest's howl freezes you where you stand and calls the pack;
/// - the hive: a queen who lays egg sacs, sacs that hatch workers.
/// </summary>
public sealed partial class Run
{
    private float _retakeT = Tables.SpiderRetake, _relockT = Tables.SpiderRelock;
    private List<Pt> _lockedAtStart;
    /// <summary>A howl froze you: no moving, shooting or dashing until it wears off.</summary>
    public float DazedT;

    /// <summary>A security spider is at his deck, and awake.</summary>
    public bool SpiderUp => Actors.Any(a => a.Is(EnemyKind.Spider) && !a.Dead && a.Team == 1 && a.Stun <= 0);
    private bool SpiderAlert => Actors.Any(a => a.Is(EnemyKind.Spider) && !a.Dead && a.Team == 1 && a.Stun <= 0 && (a.Alert || Alarm));

    /// <summary>How fast the runner hacks here: the deck's speed, less whatever the spider takes off it.</summary>
    public float HackRate => C.HackRate * (!SpiderUp ? 1 : SpiderAlert ? Tables.SpiderSlowAlert : Tables.SpiderSlow);

    private void StepElites(float dt)
    {
        if (DazedT > 0) DazedT -= dt;
        bool roused = Alarm;
        foreach (Actor a in Actors)
        {
            if (a.RevealT > 0) a.RevealT -= dt;
            if (a.FrenzyT > 0) a.FrenzyT -= dt;
            if (!a.Dead && a.Alert && (a.Is(EnemyKind.HiveQueen) || a.Is(EnemyKind.HiveWorker))) roused = true;
        }
        if (SpiderUp) StepSpider(dt);
        StepHive(dt, roused);
    }

    // ---- the spider ---------------------------------------------------------------------

    private void StepSpider(float dt)
    {
        if (!SpiderAlert) return;
        if ((_retakeT -= dt) <= 0)
        {
            _retakeT = Tables.SpiderRetake;
            Actor back = Actors.FirstOrDefault(a => !a.Dead && a.Team == 0 && a.Hacked > 0 && !a.Summoned && !a.IsAlly && !a.IsDrone && a.Def != null);
            if (back != null)
            {
                back.Hacked = 0.01f;
                Say($"The security spider takes the {back.Name.ToLower()} back.");
                Events.Add(new RunEvent(Ev.Hack, back.Pos));
            }
        }
        if ((_relockT -= dt) <= 0)
        {
            _relockT = Tables.SpiderRelock;
            foreach (Pt d in _lockedAtStart.OrderBy(_ => R.Next()))
            {
                if (L[d] is not (Tile.Door or Tile.DoorOpen)) continue;
                if (V2.Distance(d.Centre, Player.Pos) < 2 || Actors.Any(a => !a.Dead && V2.Distance(a.Pos, d.Centre) < 0.8f)) continue;
                L.Set(d, Tile.LockedDoor);
                Say("A door locks itself behind you. The spider is in the system.");
                Events.Add(new RunEvent(Ev.Door, d.Centre));
                break;
            }
        }
    }

    // ---- the ghost operative --------------------------------------------------------------

    /// <summary>Camouflage holds: the operative can't be seen from here (it can still be shot).</summary>
    public bool Cloaked(Actor a, V2 from)
    {
        if (!a.Is(EnemyKind.Cloaker) || a.Dead || a.RevealT > 0 || a.Stun > 0) return false;
        float d = V2.Distance(a.Pos, from);
        if (d <= Tables.CloakSeen) return false;
        if (from == Player.Pos && (C.Thermal && d <= 7 || (Projecting || AstralT > 0 || C.Awakened) && d <= 9)) return false;
        return true;
    }

    // ---- the bulwark ----------------------------------------------------------------------

    /// <summary>In reach: the shoulder slam. Hurts, and throws whoever it hits.</summary>
    private bool Slam(Actor a, Actor target, float dist, float dt)
    {
        if (a.SlamCd > 0) { a.SlamCd -= dt; return false; }
        if (dist > Tables.SlamReach + target.Radius || a.ReactT > 0) return false;
        a.SlamCd = Tables.SlamCd;
        V2 dir = M.Norm(target.Pos - a.Pos);
        Hurt(target, Tables.SlamDamage * (0.85f + 0.12f * (Job.Threat - 1)), a, false);
        if (!target.Static) Move(target, dir * Tables.SlamPush);
        Events.Add(new RunEvent(Ev.Melee, a.Pos, dir, 1, a.Id));
        Events.Add(new RunEvent(Ev.Explosion, target.Pos, default, 0.4f));
        return true;
    }

    // ---- the barghest ---------------------------------------------------------------------

    /// <summary>The howl: a wind-up you can hear, then everyone near freezes and the pack comes running.</summary>
    private bool Howl(Actor a, Actor target, bool sees, float dist, float dt)
    {
        if (a.HowlT > 0)
        {
            a.HowlT -= dt;
            if (a.HowlT > 0) return true;
            foreach (Actor t in Actors)
            {
                if (t.Dead || t.Team != 0 || V2.Distance(t.Pos, a.Pos) > Tables.HowlReach || !L.Los(a.Pos, t.Pos)) continue;
                if (t.IsPlayer) DazedT = MathF.Max(DazedT, Tables.HowlFreeze);
                else t.Stun = MathF.Max(t.Stun, Tables.HowlFreeze);
            }
            foreach (Actor h in Actors)
            {
                if (h.Dead || !h.Hostile || !(h.Is(EnemyKind.Hound) || h.Is(EnemyKind.Barghest)) || V2.Distance(h.Pos, a.Pos) > Tables.HowlCalls) continue;
                BecomeAlert(h, Player.Pos, false);
                h.FrenzyT = Tables.FrenzyTime;
            }
            Say("The barghest HOWLS. Your legs won't move.");
            Events.Add(new RunEvent(Ev.Flash, a.Pos, default, 0.4f));
            return true;
        }
        if ((a.HowlCd -= dt) > 0 || !sees || dist > Tables.HowlReach + 1) return false;
        a.HowlCd = Tables.HowlCd;
        a.HowlT = Tables.HowlWind;
        Events.Add(new RunEvent(Ev.Telegraph, a.Pos, M.Dir(a.Facing), Tables.HowlWind, a.Id, "howl"));
        return true;
    }

    // ---- the hive -------------------------------------------------------------------------

    private void StepHive(float dt, bool roused)
    {
        if (!roused) return;
        foreach (Actor a in ActorsNow)
        {
            if (a.Dead || a.Team != 1) continue;
            if (a.Is(EnemyKind.EggSac) && (a.EggT += dt) >= Tables.EggHatch)
            {
                a.Dead = true;
                a.Escaped = true;
                Actor w = AddEnemy(EnemyKind.HiveWorker, a.Pos, R.Float() * MathF.Tau);
                BecomeAlert(w, Player.Pos, false);
                Say("An egg sac splits. Something crawls out.");
                Events.Add(new RunEvent(Ev.Death, a.Pos, default, (int)EnemyKind.EggSac, a.Id));
            }
            else if (a.Is(EnemyKind.HiveQueen) && a.Stun <= 0 && (a.EggT += dt) >= Tables.QueenLays)
            {
                a.EggT = 0;
                V2 at = a.Pos - M.Dir(a.Facing) * 1.1f;
                if (!L.Walkable((int)MathF.Floor(at.X), (int)MathF.Floor(at.Y))) at = a.Pos;
                Actor egg = AddEnemy(EnemyKind.EggSac, at, 0);
                egg.EggT = 0;
                Say("The queen lays another sac.");
            }
        }
    }
}
