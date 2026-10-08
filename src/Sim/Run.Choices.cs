using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>A decision on the run: the clock stops until you pick (1 or 2).</summary>
public sealed class RunChoice
{
    public string Id, Text;
    public string[] Options;
}

/// <summary>
/// Choices made where they happen, not on the receipt afterwards: the hostage
/// who begs at the exit, the Johnson's dirt found on the mainframe, the guard
/// who throws down his gun.
/// </summary>
public sealed partial class Run
{
    public RunChoice Pending;
    public readonly List<string> Decided = new();
    public int KilledHelpless;
    private bool _beggedAsked, _dirtAsked;

    /// <summary>Whether this job's hostage begs, and its paydata hides the dirt: a roll on the job's seed, made once.</summary>
    private bool Begs => _begs ??= new Rng(Job.Seed ^ 0xBE65UL).Chance(1 / 3f);
    private bool Dirt => _dirt ??= new Rng(Job.Seed ^ 0xD127UL).Chance(1 / 3f);
    private bool? _begs, _dirt;

    private bool Ordinary => Job.Story == 0 && Job.Arc == null && !Job.Tutorial && Job.Tag == null;

    private void Ask(string id, string text, params string[] options)
    {
        Pending = new RunChoice { Id = id, Text = text, Options = options };
        Events.Add(new RunEvent(Ev.Objective, Player.Pos));
    }

    /// <summary>A few steps short of the extraction (inside it, the run is already over).</summary>
    private bool NearExit(V2 p)
    {
        Rect e = Plan.Extraction;
        float dx = MathF.Max(0, MathF.Max(e.X - p.X, p.X - e.Right)), dy = MathF.Max(0, MathF.Max(e.Y - p.Y, p.Y - e.Bottom));
        return dx * dx + dy * dy < 4 * 4 && !InExtraction(p);
    }

    private void OfferChoices()
    {
        if (!Ordinary || Pending != null) return;
        // At the exit, with the hostage behind you: one in three wants to be let go.
        if (!_beggedAsked && Job.Type == JobType.Rescue && Hostage != null && Hostage.State == AiState.Follow && NearExit(Player.Pos) && Begs)
        {
            _beggedAsked = true;
            Ask("begs", $"{Job.Target} grabs your arm at the door: 'The people who hired you aren't saving me. They're collecting me. Let me go -- please.'",
                "Hand them over as agreed", "Let them go (the job fails)");
        }
        // The download's done, and there's a second folder in it.
        if (!_dirtAsked && Job.Type == JobType.Datasteal && DataProgress >= 1 && Dirt)
        {
            _dirtAsked = true;
            Ask("dirt", "There's a second folder in the paydata: your Johnson's own payroll for a black site. You could pull it too.",
                "Leave it", "Pull it (it has a tripwire: security +1)");
        }
    }

    private void Decide(RunInput inp)
    {
        int pick = inp.Slot is 0 or 1 ? inp.Slot : -1;
        if (pick < 0 || pick >= Pending.Options.Length) return;
        string id = Pending.Id;
        Pending = null;
        Decided.Add($"{id}:{pick}");
        switch (id)
        {
            case "begs" when pick == 1:
                Hostage.Dead = true;
                Hostage.Escaped = true;
                ObjectiveLost = true;
                ObjectiveDone = false;
                Say($"{Job.Target} runs into the dark without looking back. The client will not be happy. Hold E to leave.");
                break;
            case "begs":
                Say($"{Job.Target} goes quiet.");
                break;
            case "dirt" when pick == 1:
                // The Johnson's own files are watched: pulling them trips a trace.
                RaiseSecurity(1);
                Say("Folder pulled -- and something in it phoned home. Security +1. You'll have to decide what to do with it.");
                break;
        }
    }

    /// <summary>Someone badly hurt and out of fight throws down the gun -- now and then.</summary>
    private void MaybeSurrender(Actor a, Actor by)
    {
        if (a.Dead || a.Hp <= 0 || a.Surrendered || a.SurrenderRolled || a.Team != 1 || a.Def == null || by == null || !by.IsPlayer || Peaceful) return;
        if (a.Kind is not (EnemyKind.Guard or EnemyKind.Ganger or EnemyKind.Blade or EnemyKind.Sniper or EnemyKind.Firebug or EnemyKind.Rigger)) return;
        if (a.Hp > a.MaxHp * Tables.SurrenderBelow) return;
        a.SurrenderRolled = true;
        if (!R.Chance(Tables.SurrenderChance)) return;
        a.Surrendered = true;
        a.Team = 2;
        a.State = AiState.Idle;
        a.Gun = null;
        Say($"{a.Name} throws down the gun, hands up: \"Don't shoot! I've got kids!\"");
    }
}
