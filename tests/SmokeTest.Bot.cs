using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>
/// A crude bot that plays generated jobs through the real run: walks to the
/// objective, shoots what it sees, holds E where E does something, heals when
/// low, and walks out. It is no good at stealth -- if it can win, a player can.
/// </summary>
public partial class SmokeTest
{
    private static ushort[] BotField(Level L, Pt goal)
    {
        var dist = new ushort[L.W * L.H];
        Array.Fill(dist, Level.Far);
        var q = new Queue<Pt>();
        if (!L.In(goal)) return dist;
        dist[L.Idx(goal.X, goal.Y)] = 0;
        q.Enqueue(goal);
        while (q.Count > 0)
        {
            Pt p = q.Dequeue();
            foreach (Pt d in Pt.Dirs4)
            {
                Pt n = p + d;
                if (!L.In(n) || dist[L.Idx(n.X, n.Y)] != Level.Far) continue;
                Tile t = L[n];
                bool pass = LevelGen.Passable(t) || n == goal;
                if (!pass) continue;
                dist[L.Idx(n.X, n.Y)] = (ushort)(dist[L.Idx(p.X, p.Y)] + 1);
                q.Enqueue(n);
            }
        }
        return dist;
    }

    /// <summary>Play one job; returns the run when it ends or times out.</summary>
    private static Run BotPlay(Job job, Career c, float limit = 240)
    {
        long g0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var r = new Run(job, c);
        _botGen = Ms(g0);
        _botTickMax = _botTickSum = 0;
        _botTicks = _botWorstAt = _botSlow = 0;
        ushort[] field = null;
        Pt fieldFor = new(-1, -1);
        int frames = (int)(limit * 60);
        for (int i = 0; i < frames && r.Phase == RunPhase.Playing; i++)
        {
            Actor p = r.Player;
            // A decision on the run (a hostage begging, a second folder): the first answer, and on.
            if (r.Pending != null) { r.Step(new RunInput { Slot = 0, Aim = p.Pos }); continue; }
            // Where to go.
            V2 goal;
            if (r.ObjectiveDone || r.ObjectiveLost) goal = new V2(r.Plan.Extraction.X + r.Plan.Extraction.W / 2f, r.Plan.Extraction.Y + r.Plan.Extraction.H / 2f);
            else if (job.Type == JobType.Eliminate && r.Vip != null) goal = r.Vip.Pos;
            else if (job.Type == JobType.Kidnap && r.Vip != null && r.CarriedBody != r.Vip) goal = r.Vip.Pos;
            else if (job.Type is JobType.Clear or JobType.Bounty)
                goal = r.Actors.Where(a => a.Hostile && a.Def != null && a.Kind is not (EnemyKind.Camera or EnemyKind.Turret)).OrderBy(a => V2.Distance(a.Pos, p.Pos)).Select(a => a.Pos).DefaultIfEmpty(p.Pos).First();
            else goal = r.ObjectivePos() ?? p.Pos;
            Pt gt = Pt.Of(goal);
            if (gt != fieldFor || i % 30 == 0) { field = BotField(r.L, gt); fieldFor = gt; }
            Pt here = Pt.Of(p.Pos);
            V2 move = V2.Zero;
            int best = field[r.L.Idx(here.X, here.Y)];
            foreach (Pt d in Pt.Dirs4)
            {
                Pt n = here + d;
                if (r.L.In(n) && field[r.L.Idx(n.X, n.Y)] < best) { best = field[r.L.Idx(n.X, n.Y)]; move = M.Norm(n.Centre - p.Pos); }
            }
            float dGoal = V2.Distance(p.Pos, goal);
            bool atObjective = !r.ObjectiveDone && dGoal < 1.2f && job.Type is JobType.Datasteal or JobType.Rescue or JobType.Destroy or JobType.Plant or JobType.Escort or JobType.Hack;
            if (atObjective) move = V2.Zero;
            // What to shoot.
            Actor foe = r.Actors.Where(a => a.Hostile && a.Def != null && r.Seen(a.Pos) && V2.Distance(a.Pos, p.Pos) < 13 && r.L.Los(p.Pos, a.Pos))
                .OrderBy(a => V2.Distance(a.Pos, p.Pos)).FirstOrDefault();
            var inp = new RunInput { Slot = -1, Move = move };
            if (r.Matrix != null)
            {
                TimedStep(r, MatrixInput(r.Matrix, i));
                r.Events.Clear();
                continue;
            }
            if (foe != null) { inp.Aim = foe.Pos; inp.Fire = true; }
            else if (job.Type == JobType.Destroy && !r.ObjectiveDone && r.ObjectivePos() is V2 tgt && V2.Distance(tgt, p.Pos) < 6 && r.L.Los(p.Pos, tgt))
            { inp.Aim = tgt; inp.Fire = true; if (V2.Distance(tgt, p.Pos) < 3) inp.Move = V2.Zero; }
            else inp.Aim = p.Pos + (move.LengthSquared() > 0 ? move : new V2(1, 0));
            inp.Interact = !r.InExtraction(p.Pos) || r.ObjectiveLost;
            inp.InteractPressed = i % 20 == 0 && foe == null;
            inp.Medkit = p.Hp < p.MaxHp * 0.4f;
            inp.Slot = r.CurrentGun == null && r.Guns.Any(g => g != null) ? Array.FindIndex(r.Guns, g => g != null) : -1;
            TimedStep(r, inp);
            r.Events.Clear();
        }
        return r;
    }

    /// <summary>What the last bot job cost the sim: site generation, and the average and worst tick (ms).</summary>
    private static double _botGen, _botTickMax, _botTickSum;
    private static int _botTicks, _botWorstAt, _botSlow;
    private static string BotCost => $"gen {_botGen,4:0} ms  tick {_botTickSum / Math.Max(1, _botTicks),5:0.00}/{_botTickMax,5:0.0} ms (worst at tick {_botWorstAt}, {_botSlow} over 4 ms)";

    private static double Ms(long t0) => (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    private static void TimedStep(Run r, RunInput inp)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        r.Step(inp);
        double ms = Ms(t0);
        _botTickSum += ms;
        if (ms > _botTickMax) { _botTickMax = ms; _botWorstAt = _botTicks; }
        if (ms > 4) _botSlow++;
        _botTicks++;
    }

    private void Bot()
    {
        int won = 0, total = 0;
        var lines = new List<string>();
        foreach (JobType type in Enum.GetValues<JobType>().Where(t => t is not (JobType.Courier or JobType.Hub or JobType.Race)))
        foreach (SiteStyle st in new[] { SiteStyle.Warehouse, SiteStyle.Den, SiteStyle.Office })
        for (int t = 1; t <= 2; t++)
        {
            var job = new Job { Id = 1, Type = type, Style = st, Threat = t, Seed = (ulong)(31 + (int)type * 1000 + (int)st * 100 + t), Faction = st == SiteStyle.Office ? Faction.Halcyon : Faction.Vipers, Count = 2, Target = "T", Item = "I", Title = "bot" };
            Career c = Career.New("Bot", "samurai", 9);
            c.Weapons.Add("kestrel");
            c.Primary = "kestrel";
            c.TraumaLinks = 1;
            // Taking someone alive takes gel rounds.
            if (type == JobType.Kidnap) { c.AmmoOwned.Add("gel"); c.AmmoFor["kestrel"] = "gel"; }
            Run r = BotPlay(job, c);
            total++;
            bool ok = r.Phase == RunPhase.Extracted && r.Success;
            if (ok) won++;
            lines.Add($"{type,-10} {st,-9} t{t}: {(ok ? "WON " : r.Phase == RunPhase.Dead ? "DIED" : r.Aborted ? "LEFT" : "TIME")} {r.Time,5:0}s hp {r.Player.Hp,4:0} kills {r.Kills,2} alarm {(r.Alarm ? "y" : "n")} obj {(r.ObjectiveDone ? "y" : "n")}  {BotCost}");
        }
        foreach (string l in lines) GD.Print("    " + l);
        GD.Print($"    bot won {won}/{total}");
        Check(won >= total / 3, $"a crude bot wins at least a third of easy jobs ({won}/{total})");

        // Hard jobs with starting gear should usually go badly.
        int hardWon = 0, hard = 0;
        foreach (JobType type in new[] { JobType.Eliminate, JobType.Datasteal, JobType.Clear, JobType.Retrieve })
        foreach (SiteStyle st in new[] { SiteStyle.Office, SiteStyle.Lab, SiteStyle.Club })
        {
            var job = new Job { Id = 1, Type = type, Style = st, Threat = 5, Seed = (ulong)(77 + (int)type * 1000 + (int)st * 100), Faction = st == SiteStyle.Club ? Faction.Lotus : Faction.Halcyon, Count = 3, Target = "T", Item = "I", Title = "bot" };
            Career c = Career.New("Bot", "samurai", 9);
            c.Weapons.Add("kestrel");
            c.Primary = "kestrel";
            Run r = BotPlay(job, c);
            hard++;
            GD.Print($"    t5 {type,-10} {st,-7} {r.L.W}x{r.L.H} actors {r.Actors.Count,3}  {BotCost}");
            if (r.Phase == RunPhase.Extracted && r.Success) hardWon++;
        }
        GD.Print($"    bot won {hardWon}/{hard} threat-5 jobs with starting gear");
        Check(hardWon <= hard / 2, $"threat 5 is dangerous with starting gear ({hardWon}/{hard})");
    }
}
