namespace Sprawl;

/// <summary>
/// Edge: a few points a run to cheat fate. The last point is kept for a
/// killing blow (you get up at a third of your health); the others also slip
/// a guard's or camera's detection the moment it lands, and tear a Matrix
/// trace out before it hits you.
/// </summary>
public sealed partial class Run
{
    public int Edge, EdgeUsed;
    private float _graceT;

    /// <summary>Spend a point (<paramref name="keepLast"/>: only if one would still be left for death).</summary>
    private bool SpendEdge(bool keepLast, string say)
    {
        if (Edge <= (keepLast ? 1 : 0)) return false;
        Edge--;
        EdgeUsed++;
        Say($"EDGE: {say}");
        Events.Add(new RunEvent(Ev.Ability, Player.Pos, default, 0, -1, "edge"));
        Hint("edge", "Edge cheats fate: the last point always saves you from a killing blow; spare points also slip a detection or a Matrix trace. Charisma and karma (the Runner tab) buy more.");
        return true;
    }

    /// <summary>A blow that would kill you: get up instead, once a point.</summary>
    private bool CheatDeath(Actor a)
    {
        if (!a.IsPlayer || a.Hp > 0 || !SpendEdge(false, "that should have killed you. It didn't.")) return false;
        a.Hp = a.MaxHp * Tables.EdgeHeal;
        _graceT = Tables.EdgeGrace;
        return true;
    }

    /// <summary>The instant a guard or camera makes you: a spare point makes them look straight through you.</summary>
    private bool SlipDetection(Actor a)
    {
        if (Alarm || !SpendEdge(true, $"the {a.Name.ToLower()} looks straight at you -- and through you.")) return false;
        a.Detect = 0;
        a.BluffT = 3;
        return true;
    }
}
