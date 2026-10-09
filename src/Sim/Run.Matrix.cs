using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// The run's side of the Matrix: jacking in (the host is built from the job,
/// the site's security, its spiders and your deck), what the host's events do
/// out here (paydata to the loot, control nodes to the site, biofeedback to
/// your body, a full trace to the alarm), and what carries over from one
/// jack-in to the next on the same run (deck damage, half the trace, files in
/// memory, programs lost). The host's own rules are in <see cref="MatrixRun"/>.
/// </summary>
public sealed partial class Run
{
    /// <summary>Jacked into a host (null when not).</summary>
    public MatrixRun Matrix;
    private int _jacks, _liftKeys, _matrixMp;
    private bool _spoofUsed, _lampUsed, _slipUsed, _biofeedback, _matrixLooped;
    private readonly List<Pt> _matrixTaken = new();
    private readonly HashSet<string> _deckLost = new();
    private float _traceLeft;
    /// <summary>Boxes of damage on the deck (it carries over between jack-ins on this run); seconds until a bricked deck has rebooted.</summary>
    public int DeckDamage;
    public float BrickedT;
    public int SpidersCrashed;

    /// <summary>Spiders at their decks on site, awake.</summary>
    public IEnumerable<Actor> SpidersAwake => Actors.Where(a => a.Is(EnemyKind.Spider) && !a.Dead && a.Team == 1 && a.Stun <= 0);

    /// <summary>How hard a host here is: the job's threat, +1 at ALARM, +1 while a spider is in the system (+1 for a lift's security host).</summary>
    public int HostRating(MxUse use) => Math.Clamp(Job.Threat + (use == MxUse.Lift ? 1 : 0) + (use != MxUse.Home && SecurityLevel >= 3 ? 1 : 0) + (use != MxUse.Home && SpidersAwake.Any() ? 1 : 0), 1, 6);

    public MatrixSetup MatrixFor(MxUse use, MxGoal goal)
    {
        var s = new MatrixSetup
        {
            Seed = Job.Seed ^ (ulong)(_jacks * 7919 + (use == MxUse.Lift ? 104729 : 0) + 1),
            Rating = HostRating(use), Threat = Job.Threat, Use = use, Goal = goal,
            Alert = use != MxUse.Home && (SecurityLevel >= 3 || SpiderAlert),
            OnEdge = use != MxUse.Home && SecurityLevel >= 1,
            Mx = (int[])C.DeckValues(C.ActiveDeck.Id).Clone(),
            Boxes = C.DeckBoxes, Damage = DeckDamage, Memory = C.Memory, UsedMp = _matrixMp,
            Decking = C.S(Skill.Decking), Speed = C.MatrixSpeed, Trace = _traceLeft * Tables.MxTraceCarry,
            Programs = C.Loaded.Where(p => C.HasProgram(p) && !_deckLost.Contains(p)).ToList(),
            Spoof = C.HasProgram("spoof") && !_spoofUsed,
            Slip = C.S(Skill.Decking) >= 6 && !_slipUsed,
        };
        if (use == MxUse.Home)
        {
            if (Job.Threat >= 4) s.Spiders.Add(-1);
            s.Controls.Add(MxControl.Accounts);
        }
        else
        {
            s.Spiders.AddRange(SpidersAwake.Select(a => a.Id));
            if (use == MxUse.Site)
            {
                if (Actors.Any(a => a.Is(EnemyKind.Camera) && !a.Dead && a.Team == 1 && a.Stun < 1e8f)) s.Controls.Add(MxControl.Cameras);
                if (Enumerable.Range(0, L.W * L.H).Any(i => L.T[i] == Tile.LockedDoor)) s.Controls.Add(MxControl.Maglocks);
                if (Actors.Any(a => a.Kind is EnemyKind.Turret or EnemyKind.Drone && !a.Dead && a.Team == 1)) s.Controls.Add(MxControl.Turrets);
                if (!Looped) s.Controls.Add(MxControl.Alarm);
            }
        }
        return s;
    }

    public void JackIn(MxUse use, MxGoal goal)
    {
        Matrix = new MatrixRun(MatrixFor(use, goal));
        _jacks++;
        // What was pulled stays pulled.
        int done = use == MxUse.Lift ? _liftKeys : _matrixTaken.Count;
        foreach (MxFile f in Matrix.Files.Where(f => f.Objective).Take(done)) { f.Taken = true; Matrix.Taken.Add(f.At); }
        if (goal == MxGoal.Crash && DataProgress >= 1) Matrix.CoreDown = true;
        if (goal == MxGoal.Plant && DataProgress >= 1) Matrix.Planted = true;
        Events.Add(new RunEvent(Ev.Hack, Player.Pos));
        string host = $"rating {Matrix.Rating}{(Matrix.Alert ? ", ON ALERT" : "")}{(Matrix.SpiderIn ? ", a spider in the system" : "")}";
        Say(use switch
        {
            MxUse.Lift => $"Jacked into the building's security host ({host}). Pull three keys and the pass is yours.",
            MxUse.Home => $"Jacked in from your chair ({host}).",
            _ => $"Jacked in ({host}). Your body is sitting very still out there.",
        });
        Hint("matrix", "The Matrix: WASD along the lines; Space or click spikes IC next to you; hold E to pull a file or take a node; Q/F/R run programs; G purges one; E at the ring jacks out. Watch the trace.");
    }

    /// <summary>Leave the host, keeping what carries over.</summary>
    private void EndMatrix()
    {
        if (Matrix == null) return;
        DeckDamage = Matrix.Damage;
        _traceLeft = Matrix.Trace;
        _matrixMp = Matrix.UsedMp;
        foreach (string p in Matrix.Crashed) _deckLost.Add(p);
        foreach (string p in Matrix.S.Programs.Where(p => !Matrix.Programs.Contains(p))) _deckLost.Add(p);
        Matrix = null;
    }

    /// <summary>In the Matrix the body stands still; the deck does the moving.</summary>
    private void StepMatrix(RunInput inp, float dt)
    {
        Player.Vel = V2.Zero;
        var mi = new MxInput
        {
            Move = inp.Move, Spike = inp.Fire || inp.Dash, Hold = inp.Interact, Press = inp.InteractPressed, Purge = inp.NextGrenade,
            Program = inp.AbilityQ ? 0 : inp.AbilityF ? 1 : inp.Reload ? 2 : -1,
        };
        MatrixRun m = Matrix;
        m.Step(dt, mi);
        // A spider whose body is down out here is gone from in there.
        foreach (Ic sp in m.Ice.Where(i => i.Kind == IcKind.Spider && i.Spider >= 0).ToList())
            if (ActorById(sp.Spider) is not Actor a || a.Dead || a.Stun > 0 || a.Team != 1) m.SpiderGone(sp.Spider);
        bool lift = m.S.Use == MxUse.Lift;
        foreach (MxEvent ev in m.Events.ToList())
        {
            if (Matrix == null) break;
            switch (ev.Kind)
            {
                case MatrixEvent.Store when lift:
                    _liftKeys = m.Taken.Count;
                    if (_liftKeys >= 3) { EndMatrix(); LiftOverridden(); Events.Add(new RunEvent(Ev.Hack, Player.Pos)); }
                    else Events.Add(new RunEvent(Ev.Pickup, Player.Pos, Value: (int)PickupKind.Loot, Text: $"Pass key {_liftKeys}/3"));
                    break;
                case MatrixEvent.Store:
                    while (_matrixTaken.Count < m.Taken.Count) _matrixTaken.Add(m.Taken[_matrixTaken.Count]);
                    DataProgress = MathF.Max(DataProgress, _matrixTaken.Count / 3f);
                    Events.Add(new RunEvent(Ev.Pickup, Player.Pos, Value: (int)PickupKind.Loot, Text: $"Datastore {_matrixTaken.Count}/3"));
                    if (_matrixTaken.Count >= 3) Say("All three datastores are yours. Jack out at the entry -- or look for paydata first.");
                    break;
                case MatrixEvent.File:
                {
                    string name = Career.PaydataPrefix + char.ToUpper(ev.Text[0]) + ev.Text[1..];
                    LootFound.Add(new LootItem { Name = name, Value = ev.Value, Faction = (int)Job.Faction, Day = C.Day });
                    Events.Add(new RunEvent(Ev.Pickup, Player.Pos, Value: (int)PickupKind.Loot, Text: $"Paydata: {M.Money(ev.Value)}"));
                    break;
                }
                case MatrixEvent.Control:
                    TakeControl((MxControl)ev.Value);
                    break;
                case MatrixEvent.CoreDown:
                    Say(ev.Text);
                    Events.Add(new RunEvent(Ev.Emp, Player.Pos, default, 0.6f));
                    if (Job.Type == JobType.Hack && Job.Host == MxGoal.Crash) DataProgress = 1;
                    break;
                case MatrixEvent.Planted:
                    Say(ev.Text);
                    if (Job.Type == JobType.Hack && Job.Host == MxGoal.Plant) DataProgress = 1;
                    break;
                case MatrixEvent.Spike:
                    Events.Add(new RunEvent(Ev.Matrix, Player.Pos, default, 0));
                    break;
                case MatrixEvent.IcDown:
                    Events.Add(new RunEvent(Ev.Matrix, Player.Pos, default, 1));
                    break;
                case MatrixEvent.Hit:
                    if (ev.Value > 0) Events.Add(new RunEvent(Ev.Matrix, Player.Pos, default, 2));
                    else if (ev.Text != null && ev.Text.StartsWith("Your")) Say(ev.Text);
                    break;
                case MatrixEvent.Bio:
                    _biofeedback = true;
                    Hurt(Player, ev.Value, null, false, ignoreArmor: true);
                    _biofeedback = false;
                    Say($"Biofeedback from the {ev.Text}: your body convulses in the chair.");
                    if (Player.Dead) { EndMatrix(); return; }
                    break;
                case MatrixEvent.SpiderDown:
                    SpidersCrashed++;
                    if (ActorById(ev.Value) is Actor body && !body.Dead) body.Stun = MathF.Max(body.Stun, Tables.MxSpiderStun);
                    Say(ev.Text);
                    Events.Add(new RunEvent(Ev.Matrix, Player.Pos, default, 1));
                    break;
                case MatrixEvent.Alerted:
                    Say(ev.Text);
                    Events.Add(new RunEvent(Ev.Matrix, Player.Pos, default, 3));
                    // The host calls it in: the spiders know, and the site is jumpier.
                    if (m.S.Use == MxUse.Site)
                    {
                        foreach (Actor sp in SpidersAwake) if (!sp.Alert) BecomeAlert(sp, sp.Pos, false);
                        RaiseSecurity(0.5f);
                    }
                    break;
                case MatrixEvent.Spoofed:
                    _spoofUsed = true;
                    Say(ev.Text);
                    Events.Add(new RunEvent(Ev.Emp, Player.Pos, default, 0.5f));
                    break;
                case MatrixEvent.Slipped:
                    _slipUsed = true;
                    Say(ev.Text);
                    break;
                case MatrixEvent.Crashed or MatrixEvent.Purged:
                    Say(ev.Text);
                    break;
                case MatrixEvent.Converged:
                    Converged(m);
                    return;
                case MatrixEvent.Bricked:
                    EndMatrix();
                    DeckDamage = C.DeckBoxes / 2;
                    BrickedT = 15;
                    Hurt(Player, 10, null, false, ignoreArmor: true);
                    Say("Your deck bricks. Dumpshock -- and it needs 15 s to reboot.");
                    return;
                case MatrixEvent.Left:
                    EndMatrix();
                    Say(lift ? "Jacked out of the security host." : "Jacked out.");
                    return;
                default:
                    if (ev.Text != null) Say(ev.Text);
                    break;
            }
        }
        if (Matrix != null)
        {
            DeckDamage = m.Damage;
            _matrixMp = m.UsedMp;
        }
    }

    /// <summary>The host converged on you: what being caught by ICE always was.</summary>
    private void Converged(MatrixRun m)
    {
        bool black = m.Black || m.SpiderIn;
        bool lift = m.S.Use == MxUse.Lift;
        EndMatrix();
        _traceLeft = 0.5f;
        if (SpendEdge(true, "you tear the trace out of your deck before it lands. Jack back in.")) return;
        if (C.HasProgram("lamplight") && !_lampUsed)
        {
            _lampUsed = true;
            Say("Something in the system pulls you out before the trace lands. For a moment the screen is warm, like a lamp.");
            return;
        }
        Hurt(Player, black ? 25 : 10, null, false, ignoreArmor: true);
        if (Job.Type == JobType.Hack && !lift)
        {
            // From home there is nobody to come running -- but a trace is a trace.
            Traces++;
            if (Traces >= 2) { ObjectiveLost = true; Say("Traced twice. They have your node -- the job is blown. Pull the plug."); }
            else Say(black ? "Black IC! Dumpshock. One more trace and they have you." : "Traced! Dumped out. One more and they have you.");
            return;
        }
        if (C.HasProgram("spoof")) { Say(black ? "Black IC! Dumpshock -- but your spoof keeps them guessing where you are." : "Traced and dumped out -- your spoof keeps the alarm quiet."); return; }
        RaiseAlarm(black ? "Black IC! Dumpshock -- and security knows where you are." : "The host traced you! Dumped out, and security is coming.");
    }

    /// <summary>A control node taken: the site does what the host tells it.</summary>
    private void TakeControl(MxControl c)
    {
        switch (c)
        {
            case MxControl.Cameras:
                foreach (Actor a in Actors.Where(a => a.Is(EnemyKind.Camera) && !a.Dead)) a.Stun = 1e9f;
                Say("Every camera on the site is looping an empty corridor.");
                break;
            case MxControl.Maglocks:
                for (int i = 0; i < L.W * L.H; i++)
                    if (L.T[i] == Tile.LockedDoor) L.Set(L.PtOf(i), Tile.Door);
                L.Keycards.Clear();
                Say("Every maglock on the site clicks open.");
                break;
            case MxControl.Turrets:
                foreach (Actor a in Actors.Where(a => a.Kind is EnemyKind.Turret or EnemyKind.Drone && !a.Dead && a.Team == 1))
                {
                    a.Team = 0;
                    a.Hacked = C.HasProgram("daemon") ? 9999 : 60;
                    a.State = AiState.Alert;
                }
                Say($"The site's turrets and drones are yours{(C.HasProgram("daemon") ? "" : " for a minute")}.");
                break;
            case MxControl.Alarm:
                _matrixLooped = true;
                Say("The security clock stops: whatever they know, it won't get worse on its own.");
                break;
            case MxControl.Accounts:
            {
                int cash = (int)(Job.Threat * R.Range(150, 300) / 10) * 10;
                CashFound += cash;
                Events.Add(new RunEvent(Ev.Pickup, Player.Pos, Value: (int)PickupKind.Cash, Text: $"Skimmed {M.Money(cash)}"));
                break;
            }
        }
        Events.Add(new RunEvent(Ev.Hack, Player.Pos));
    }

    private void StepDeck(float dt)
    {
        if (BrickedT > 0) BrickedT -= dt;
    }
}
