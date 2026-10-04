using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// Security is a ladder, not a switch (after Invisible, Inc.): six named levels,
/// each with one consequence the HUD shows before it happens. Guards who see you
/// call it in and push it up; a camera, a body found, the slow tick of time do
/// too. Sub-ticks fade when nobody is calling, but a level, once reached, stays.
/// Level 3 is the old alarm -- but now they search where they last saw you, and
/// only from level 5 do they simply know where you are.
/// </summary>
public sealed partial class Run
{
    /// <summary>0 to 6: the level is the whole part.</summary>
    public float Security;
    public int SecurityLevel => Math.Clamp((int)Security, 0, 6);
    /// <summary>From level 5 the site's hunters know where you are.</summary>
    public bool Hunted => SecurityLevel >= 5;
    private int _levelDone;

    public static readonly (string name, string does)[] SecurityLevels =
    {
        ("QUIET", "nobody knows you're here"),
        ("ON EDGE", "guards are jumpy: they notice you faster"),
        ("PATROLS UP", "two more patrols walk the site"),
        ("ALARM", "everyone's looking; reinforcements every 25 s"),
        ("RESPONSE TEAM", "a team covers the way out"),
        ("HUNTED", "they know where you are"),
        ("KILL TEAM", "an elite squad comes in"),
    };

    /// <summary>Push security up (never past 6); the levels crossed happen at once.</summary>
    public void RaiseSecurity(float by)
    {
        if (Peaceful || by <= 0) return;
        Security = MathF.Min(6, Security + by);
        while (_levelDone < SecurityLevel) OnLevel(++_levelDone);
    }

    private void OnLevel(int level)
    {
        switch (level)
        {
            case 1:
                Say("They know someone's here. The guards are on edge.");
                break;
            case 2:
                for (int k = 0; k < Tables.SecurityPatrols; k++)
                {
                    V2 from = Plan.Reinforce[R.Int(Plan.Reinforce.Count)];
                    Actor g = AddEnemy(PickFrom(WaveRoster()), from, 0);
                    // Walking a beat between two rooms, not yet alert.
                    if (Plan.Rooms.Count > 1)
                    {
                        Room a = Plan.Rooms[R.Int(Plan.Rooms.Count)], b = Plan.Rooms[R.Int(Plan.Rooms.Count)];
                        g.Patrol.AddRange(new[] { from, a.Centre, b.Centre });
                    }
                }
                Say("Security level 2: extra patrols are walking the site.");
                break;
            case 3:
                RaiseAlarm(Job.Faction == Faction.Halcyon ? "Security level 3: the alarm is raised!" : "They called for backup!");
                break;
            case 4:
            {
                // They cover the way out: a team between you and the extraction.
                V2 ex = new(Plan.Extraction.X + Plan.Extraction.W / 2f, Plan.Extraction.Y + Plan.Extraction.H / 2f);
                V2 at = Plan.Reinforce.OrderBy(p => V2.Distance(p, ex)).First();
                for (int k = 0; k < 2 + Job.Threat / 2; k++)
                {
                    Actor e = AddEnemy(PickFrom(WaveRoster()), at + new V2(R.Range(-1f, 1f), R.Range(-1f, 1f)), M.Angle(ex - at));
                    e.Home = ex;
                    e.Bodyguard = true;
                    BecomeAlert(e, ex, false);
                }
                Events.Add(new RunEvent(Ev.Wave, at, Text: "Security level 4: a response team is covering the exit."));
                break;
            }
            case 5:
                foreach (Actor a in Actors) if (a.Hostile && a.Def != null && !a.Is(EnemyKind.Camera)) { BecomeAlert(a, Player.Pos, false); a.LostT = 0; }
                Events.Add(new RunEvent(Ev.Alarm, Player.Pos, Text: "Security level 5: they know where you are."));
                break;
            case 6:
            {
                V2 at = Plan.Reinforce.OrderByDescending(p => V2.Distance(p, Player.Pos)).First();
                EnemyKind[] squad = Job.Faction == Faction.Halcyon && Job.Threat >= 5 ? new[] { EnemyKind.Bulwark, EnemyKind.Cloaker, EnemyKind.Heavy } : new[] { EnemyKind.Heavy, EnemyKind.Heavy, EnemyKind.Guard };
                foreach (EnemyKind k in squad)
                {
                    Actor e = AddEnemy(k, at + new V2(R.Range(-1f, 1f), R.Range(-1f, 1f)), M.Angle(Player.Pos - at));
                    e.Armor += 2;
                    e.MaxHp *= 1.25f;
                    e.Hp = e.MaxHp;
                    BecomeAlert(e, Player.Pos, false);
                }
                Events.Add(new RunEvent(Ev.Wave, at, Text: "Security level 6: a kill team is in the building."));
                break;
            }
        }
    }

    /// <summary>Calling it in, time passing, the sub-ticks fading: every step, until the run ends.</summary>
    private void StepSecurity(float dt)
    {
        if (Peaceful) return;
        bool calling = Actors.Any(a => a.Hostile && a.Alert && a.Def != null && !a.Static && a.Stun <= 0 && !a.Is(EnemyKind.Vip) && a.LostT < 3);
        bool anyAlert = calling || Actors.Any(a => a.Hostile && a.Alert && a.Def != null && !a.Static && a.Stun <= 0 && !a.Is(EnemyKind.Vip));
        // The first pair of eyes on you puts them on edge at once.
        if (calling && Security < 1) RaiseSecurity(1 - Security);
        if (calling) RaiseSecurity(dt * Tables.SecurityCalling * (SpiderAlert ? 1.3f : 1));
        else
        {
            // Nobody on the radio: what wasn't reported fades. A level reached stays.
            float floor = MathF.Floor(Security);
            Security = MathF.Max(floor, Security - dt * Tables.SecurityFade);
        }
        // Once they know someone's here, the clock runs: a level every couple of minutes, unless the cameras are looped.
        if (SecurityLevel >= 1 && !Looped) RaiseSecurity(dt / Tables.SecurityTrickle);
        CoolingOff = !calling && anyAlert;
    }

    /// <summary>The security loop is running (a console was used): no clock, no cameras.</summary>
    public bool Looped => UsedFixtures.Any(p => L[p] == Tile.Console);

    /// <summary>How far to the next level, 0-1 (the HUD's sub-ticks).</summary>
    public float SecurityTick => Security - MathF.Floor(Security);
}
