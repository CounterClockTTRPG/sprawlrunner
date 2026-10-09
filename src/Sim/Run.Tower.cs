using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// Towers: floors joined by stairs and an elevator. The elevator needs its
/// controls hacked before it goes anywhere, and then only to the public
/// floors; the secure floors at the top need a security pass -- override it
/// in the Matrix (a deck and Electronics 3, or Electronics 5), or fight up
/// the stairs and hack the panel on each secure floor from inside.
/// </summary>
public sealed partial class Run
{
    public bool LiftHacked, LiftOverride;
    private readonly HashSet<int> _liftUnlocked = new();
    /// <summary>Riding the elevator: seconds left, and the floor it is going to (-1: not riding).</summary>
    public float LiftT;
    public int LiftTo = -1;

    public int FloorOf(V2 p) => Plan.FloorAt(p.X);
    public int PlayerFloor => FloorOf(Projecting && AstralForm != null ? AstralForm.Pos : Rigging && Drone != null ? Drone.Pos : Player.Pos);
    public int FloorCount => Math.Max(1, Plan.Regions.Count);
    public bool Secure(int floor) => Plan.Tower && floor < Plan.Secure.Count && Plan.Secure[floor];

    /// <summary>Does the elevator stop at this floor for you?</summary>
    public bool Served(int floor) => LiftOverride || _liftUnlocked.Contains(floor) || LiftHacked && !Secure(floor);

    /// <summary>Good enough in the Matrix to override the security pass.</summary>
    public bool CanOverride => (C.Has("deck") || C.Decks.Count > 0) && C.S(Skill.Decking) >= 3 || C.S(Skill.Electronics) >= 5;

    private int NextServed(int from, int dir)
    {
        for (int k = from + dir; k >= 0 && k < FloorCount; k += dir) if (Served(k)) return k;
        return -1;
    }

    private void StepLift(float dt)
    {
        Player.Vel = V2.Zero;
        LiftT -= dt;
        if (LiftT > 0) return;
        Player.Pos = Plan.LiftSpots[LiftTo];
        Say($"Ding. {Plan.FloorName(LiftTo)}.");
        Events.Add(new RunEvent(Ev.Stairs, Player.Pos, Text: Plan.FloorName(LiftTo)));
        _liftUnlocked.Add(LiftTo);
        LiftTo = -1;
        UpdateFov(true);
    }

    /// <summary>At an elevator panel: hack it, ride it, or override its pass. True when the panel took the key.</summary>
    private bool LiftInteract(bool held, bool pressed, float dt)
    {
        if (!Plan.Tower) return false;
        Actor p = Player;
        int f = FloorOf(p.Pos);
        Pt panel = Plan.Lifts[f];
        if (V2.Distance(panel.Centre, p.Pos) > 1.6f) return false;
        // On a secure floor you reached by the stairs, hacking the panel opens this floor (and the controls) from inside.
        bool needHack = !LiftHacked || Secure(f) && !Served(f);
        if (needHack)
        {
            if (HackTarget != panel) { HackTarget = panel; HackProgress = 0; }
            Prompt = Secure(f) ? "Hold E: hack this floor's elevator controls" : "Hold E: hack the elevator controls";
            if (held)
            {
                HackProgress += dt * HackRate / 5f;
                PromptProgress = HackProgress;
                if (HackProgress >= 1)
                {
                    HackProgress = 0;
                    LiftHacked = true;
                    _liftUnlocked.Add(f);
                    Events.Add(new RunEvent(Ev.Hack, panel.Centre));
                    Say(Secure(f) ? $"The elevator will stop at {Plan.FloorName(f)} for you now." : "Elevator controls hacked. It will take you to the public floors.");
                }
            }
            return true;
        }
        int up = NextServed(f, 1), down = NextServed(f, -1);
        bool lockedAbove = Enumerable.Range(f + 1, Math.Max(0, FloorCount - f - 1)).Any(k => !Served(k));
        var parts = new List<string>();
        if (up >= 0) parts.Add($"E: up to {Plan.FloorName(up)}");
        if (down >= 0) parts.Add($"Shift+E: down to {Plan.FloorName(down)}");
        if (lockedAbove && up < 0) parts.Add(CanOverride ? "E: override the security pass (Matrix)" : "the floors above need a pass -- take the stairs");
        else if (lockedAbove) parts.Add("secure floors above");
        Prompt = string.Join("  ·  ", parts);
        if (!pressed) return true;
        if (Sneaking && down >= 0) Ride(down);
        else if (!Sneaking && up >= 0) Ride(up);
        else if (!Sneaking && lockedAbove && CanOverride)
        {
            if (BrickedT > 0) Say($"Your deck is still rebooting ({BrickedT:0} s).");
            else JackIn(MxUse.Lift, MxGoal.Keys);
        }
        return true;
    }

    private void Ride(int to)
    {
        LiftTo = to;
        LiftT = 1.4f + 0.25f * Math.Abs(to - FloorOf(Player.Pos));
        Events.Add(new RunEvent(Ev.Door, Player.Pos));
        Say(to > FloorOf(Player.Pos) ? "Going up." : "Going down.");
    }

    /// <summary>The Matrix run at an elevator panel paid off: every floor is open.</summary>
    private void LiftOverridden()
    {
        LiftOverride = true;
        LiftHacked = true;
        Say("Override accepted. The elevator will take you anywhere in the building.");
    }

    /// <summary>Where to go to change floors towards <paramref name="to"/>: the stairs on your floor that lead closer, or the elevator if it goes there.</summary>
    private V2? FloorWay(int from, int to)
    {
        if (Plan.Tower && Served(to) && LiftHacked) return Plan.Lifts[from].Centre;
        V2? best = null;
        float bd = float.MaxValue;
        foreach (var kv in L.Stair)
        {
            Pt a = L.PtOf(kv.Key), b = L.PtOf(kv.Value);
            if (FloorOf(a.Centre) != from) continue;
            if (Math.Abs(FloorOf(b.Centre) - to) >= Math.Abs(from - to)) continue;
            float d = V2.Distance(a.Centre, Player.Pos);
            if (d < bd) { bd = d; best = a.Centre; }
        }
        return best;
    }
}
