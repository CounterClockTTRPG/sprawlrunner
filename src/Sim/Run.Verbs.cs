using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// What each kind of runner does that a gun doesn't: the Face talks past
/// guards and wears their uniform, the mage leaves the body and bars
/// corridors with light, the decker reads and spoofs a security console.
/// </summary>
public sealed partial class Run
{
    // ---- the Face -------------------------------------------------------------------------

    public bool Disguised;
    private float _uniform;

    /// <summary>How likely a bluff is to work on this one.</summary>
    public float BluffChance(Actor a)
    {
        string code = Job.Faction switch { Faction.Halcyon => "Corporate", Faction.Vipers => "Gang", _ => "Street" };
        float c = 0.25f + 0.08f * C.A(Attr.Charisma) + (C.Talks(code) ? 0.2f : 0) + (C.Talks("Security") && a.Is(EnemyKind.Guard) ? 0.1f : 0) + (Disguised ? 0.25f : 0) - 0.06f * Job.Threat;
        if (C.Archetype == "face") c += 0.15f;
        if (Alarm) c -= 0.3f;
        return Math.Clamp(c, 0.05f, 0.95f);
    }

    private static bool Talker(Actor a) => a.Kind is EnemyKind.Guard or EnemyKind.Ganger or EnemyKind.Blade or EnemyKind.Heavy or EnemyKind.Sniper or EnemyKind.Bruiser or EnemyKind.Shield or EnemyKind.Firebug;
    private static bool Uniformed(Actor a) => a.Kind is EnemyKind.Guard or EnemyKind.Ganger or EnemyKind.Blade or EnemyKind.Heavy or EnemyKind.Shield;

    private bool TalkInteract(bool held, bool pressed, float dt)
    {
        Actor p = Player;
        // An unaware guard in front of you, who hasn't heard your story yet.
        Actor g = Actors.Where(a => a.Hostile && a.Def != null && Talker(a) && !a.Alert && !a.BluffTried && a.BluffT <= 0 && V2.Distance(a.Pos, p.Pos) < 1.7f)
            .OrderBy(a => V2.Distance(a.Pos, p.Pos)).FirstOrDefault();
        if (g != null && C.A(Attr.Charisma) >= 3)
        {
            float ch = BluffChance(g);
            Prompt = $"E: talk your way past ({ch * 100:0} %)";
            if (!pressed) return true;
            g.BluffTried = true;
            if (R.Chance(ch))
            {
                g.BluffT = 60;
                g.Detect = 0;
                g.State = AiState.Idle;
                // A good enough story sends them off somewhere else entirely.
                if (C.A(Attr.Charisma) >= 5 || C.Archetype == "face")
                {
                    Room far = Plan.Rooms.Where(r => FloorOf(r.Centre) == FloorOf(g.Pos)).OrderByDescending(r => V2.Distance(r.Centre, g.Pos)).FirstOrDefault();
                    if (far != null) { g.Patrol.Clear(); g.Patrol.Add(far.Centre); g.Home = far.Centre; }
                    Say($"{g.Name}: \"Right, right. I'll go and check, then.\"");
                }
                else Say($"{g.Name}: \"Fine. Go on. Don't make me regret it.\"");
                Events.Add(new RunEvent(Ev.Hint, p.Pos));
            }
            else
            {
                Say($"{g.Name}: \"Nice try.\"");
                BecomeAlert(g, p.Pos);
            }
            return true;
        }
        // A downed guard's uniform: Shift and hold E.
        if (Sneaking && !Disguised)
        {
            Actor body = Actors.FirstOrDefault(a => a.Dead && !a.Escaped && a.Team == 1 && a.Def != null && Uniformed(a) && V2.Distance(a.Pos, p.Pos) < 0.9f);
            if (body != null)
            {
                Prompt = "Shift + hold E: put on their uniform";
                if (held)
                {
                    _uniform += dt;
                    PromptProgress = _uniform / 2.5f;
                    if (_uniform >= 2.5f)
                    {
                        Disguised = true;
                        _uniform = 0;
                        Say("You're one of them now. Don't run, don't shoot, and don't let anyone look too closely.");
                    }
                }
                else _uniform = 0;
                return true;
            }
        }
        return false;
    }

    /// <summary>Violence doesn't go with the uniform.</summary>
    private void Unmask()
    {
        if (!Disguised) return;
        Disguised = false;
        Say("The uniform won't explain that. Disguise blown.");
    }

    // ---- the mage ----------------------------------------------------------------------------

    public bool Projecting;
    public Actor AstralForm;
    private float _projectT;

    private bool StartProjection()
    {
        if (Projecting) return false;
        Projecting = true;
        _projectT = C.Duration(Ability.Project);
        AstralForm = new Actor { Id = _nextId++, Team = 0, Name = "astral form", Pos = Player.Pos, Radius = 0.3f, MaxHp = 1, Hp = 1, Speed = 6, Flying = true, IsGhost = true };
        Say("You step out of your body. Walls are suggestions now. Your body stands very still.");
        Events.Add(new RunEvent(Ev.Spell, Player.Pos, Player.Pos, (int)Ability.Project));
        UpdateFov(true);
        return true;
    }

    private void StepProjection(RunInput inp, float dt)
    {
        Player.Vel = V2.Zero;
        _projectT -= dt;
        V2 move = inp.Move.LengthSquared() > 1 ? M.Norm(inp.Move) : inp.Move;
        // Through walls, not off the map, not past your own floor.
        V2 next = AstralForm.Pos + move * AstralForm.Speed * dt;
        var (x0, x1) = Plan.Bounds(FloorOf(Player.Pos));
        if (next.X > x0 + 1 && next.X < x1 - 1 && next.Y > 1 && next.Y < L.H - 1) AstralForm.Pos = next;
        if (inp.AbilityQ && C.SlotQ == Ability.Project || inp.AbilityF && C.SlotF == Ability.Project || _projectT <= 0 || V2.Distance(AstralForm.Pos, Player.Pos) > 24)
            EndProjection(_projectT <= 0 ? "The tether pulls. You're back in your body." : "Back in your body.");
        UpdateFov(true);
    }

    private void EndProjection(string why)
    {
        Projecting = false;
        AstralForm = null;
        Say(why);
        UpdateFov(true);
    }

    /// <summary>What the astral form shows you: every living thing near it, walls or no walls.</summary>
    private bool AstralSees(Actor a) => Projecting && AstralForm != null && !a.Dead && a.Kind is not (EnemyKind.Camera or EnemyKind.Turret or EnemyKind.Drone) && V2.Distance(a.Pos, AstralForm.Pos) < 9;

    private readonly List<(Pt at, float t)> _wards = new();

    /// <summary>A line of light across the corridor at the aim point, three tiles wide.</summary>
    private bool RaiseWard(V2 aim)
    {
        if (V2.Distance(aim, Player.Pos) > 7 || !L.Los(Player.Pos, aim)) return false;
        V2 dir = M.Norm(aim - Player.Pos);
        V2 across = M.Perp(dir);
        int placed = 0;
        for (int k = -1; k <= 1; k++)
        {
            Pt t = Pt.Of(aim + across * k);
            if (!L.In(t) || L[t] != Tile.Floor || Actors.Any(a => !a.Dead && Pt.Of(a.Pos) == t)) continue;
            L.Set(t, Tile.Ward);
            _wards.Add((t, C.Duration(Ability.Ward) * (0.8f + 0.2f * C.PowerOf(Ability.Ward))));
            placed++;
        }
        return placed > 0;
    }

    private void StepWards(float dt)
    {
        for (int i = _wards.Count - 1; i >= 0; i--)
        {
            var (at, t) = _wards[i];
            if (t - dt > 0) { _wards[i] = (at, t - dt); continue; }
            if (L[at] == Tile.Ward) L.Set(at, Tile.Floor);
            _wards.RemoveAt(i);
        }
    }

    // ---- the decker -----------------------------------------------------------------------------

    /// <summary>A runner who can do more with a console than loop it.</summary>
    public bool Decker => C.Has("deck") || C.S(Skill.Electronics) >= 3;
    private readonly HashSet<Pt> _spoofed = new();

    /// <summary>A fake radio call: the nearest guards go and look at something on the far side of the building.</summary>
    private void SpoofRadio(Pt console)
    {
        Room far = Plan.Rooms.Where(r => FloorOf(r.Centre) == FloorOf(Player.Pos)).OrderByDescending(r => V2.Distance(r.Centre, Player.Pos)).FirstOrDefault();
        V2 there = far?.Centre ?? Plan.Reinforce.OrderByDescending(p => V2.Distance(p, Player.Pos)).First();
        int sent = 0;
        foreach (Actor a in Actors.Where(a => a.Hostile && a.Def != null && !a.Alert && !a.Static && Talker(a)).OrderBy(a => V2.Distance(a.Pos, Player.Pos)).Take(3))
        {
            a.State = AiState.Suspicious;
            a.StateT = 0;
            a.Goal = there;
            a.Detect = 0;
            sent++;
        }
        Events.Add(new RunEvent(Ev.Hack, console.Centre));
        Say(sent > 0 ? $"\"All units, disturbance in the east wing.\" {sent} guard(s) go to look." : "You spoof a radio call. Nobody's near enough to answer it.");
    }
}
