using System;

namespace Sprawl;

/// <summary>
/// Bystanders (clubbers on the floor, the market crowd): gunfire, blasts and
/// anyone falling near them send them running -- away from the danger, at a
/// sprint, screaming -- for several seconds. Vendors duck behind their stalls.
/// A scream carries: guards who hear one come to look.
/// </summary>
public sealed partial class Run
{
    private float _screamT;

    /// <summary>Everyone on foot near <paramref name="from"/> who can hear it panics.</summary>
    public void Panic(V2 from, float radius)
    {
        foreach (Actor a in Actors)
        {
            if (!a.IsNpc || a.Dead || a.Sedated) continue;
            float d = V2.Distance(a.Pos, from);
            if (d > radius || !L.Los(a.Pos, from) && d > radius * 0.6f) continue;
            bool fresh = a.PanicT <= 0;
            a.PanicT = MathF.Max(a.PanicT, R.Range(Tables.PanicMin, Tables.PanicMax));
            a.PanicFrom = from;
            a.StateT = 0;
            if (fresh && _screamT <= 0)
            {
                _screamT = 0.6f;
                Events.Add(new RunEvent(Ev.Panic, a.Pos));
                // A scream is heard: whoever is on guard comes to see.
                if (!Peaceful) Noise(a.Pos, Tables.ScreamCarries, false);
            }
        }
    }

    /// <summary>Running from it: away from where it came from, by open floor, at a sprint.</summary>
    private void Flee(Actor a, float dt)
    {
        a.PanicT -= dt;
        // Calmed down where they ended up: they mill about there now, not back in the fight.
        if (a.PanicT <= 0) a.Home = a.Pos;
        if (a.Static) return;
        a.StateT -= dt;
        if (a.StateT <= 0 || V2.Distance(a.Pos, a.Goal) < 0.6f)
        {
            a.StateT = 0.6f;
            V2 best = a.Pos;
            float bestScore = float.MinValue;
            for (int k = 0; k < 14; k++)
            {
                V2 g = a.Pos + M.Dir(k / 14f * MathF.Tau + R.Range(-0.2f, 0.2f)) * R.Range(3f, 7f);
                if (!L.Walkable((int)MathF.Floor(g.X), (int)MathF.Floor(g.Y)) || !L.Los(a.Pos, g)) continue;
                float score = V2.Distance(g, a.PanicFrom) - 0.5f * V2.Distance(g, Player.Pos) * (V2.Distance(a.Pos, Player.Pos) < 3 ? 1 : 0);
                if (score > bestScore) { bestScore = score; best = g; }
            }
            a.Goal = best;
        }
        V2 to = a.Goal - a.Pos;
        if (to.LengthSquared() < 0.01f) return;
        V2 before = a.Pos;
        Move(a, M.Norm(to) * Tables.PanicSpeed * dt);
        a.Facing = M.TurnTo(a.Facing, M.Angle(to), 10 * dt);
        if (V2.Distance(before, a.Pos) < Tables.PanicSpeed * dt * 0.2f) a.StateT = 0;
    }

    private void StepCrowd(float dt)
    {
        if (_screamT > 0) _screamT -= dt;
    }
}
