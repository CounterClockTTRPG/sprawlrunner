using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

public enum MxNode { Plain, Entry, Store, File, Control, Core, Plant }
public enum MxControl { Cameras, Maglocks, Turrets, Alarm, Accounts }
/// <summary>What the host is for: a site's mainframe, a host reached from your own chair, a tower's security host.</summary>
public enum MxUse { Site, Home, Lift }
/// <summary>What the job wants in there: three datastores, the core crashed, a backdoor planted, three pass keys.</summary>
public enum MxGoal { Pull, Crash, Plant, Keys }

/// <summary>Everything a host needs to know about the runner and the site when you jack in.</summary>
public sealed class MatrixSetup
{
    public ulong Seed;
    public int Rating = 1, Threat = 1;
    public bool Alert, OnEdge;
    public MxUse Use;
    public MxGoal Goal;
    /// <summary>Attack, Sleaze, Data processing, Firewall.</summary>
    public int[] Mx = { 2, 1, 2, 1 };
    public int Boxes = 8, Damage, Memory = 4, UsedMp, Decking, Taken;
    public float Speed = 1, Trace;
    /// <summary>Programs loaded and running.</summary>
    public List<string> Programs = new();
    /// <summary>Spoof still has its decoy; Decking 6 still has its slip.</summary>
    public bool Spoof, Slip;
    /// <summary>Spiders in the system: their actor ids on site (-1: a spider with no body here, from home).</summary>
    public List<int> Spiders = new();
    public List<MxControl> Controls = new();
}

/// <summary>A file in a datastore: the job's own (◆) or paydata (◇) to sell.</summary>
public sealed class MxFile
{
    public Pt At;
    public int Mp, Value;
    public bool Objective, Encrypted, Taken;
    public string Name;
}

/// <summary>Intrusion countermeasures (and spiders' personas) moving through the host.</summary>
public sealed class Ic
{
    public IcKind Kind;
    public Pt Pos, Next;
    public float Hp, MaxHp, StepT, HitT, Report = -1;
    /// <summary>Guards sit at their post until the host is alert or you come near; patrols walk their beat.</summary>
    public bool Guard, Hunting, InReach;
    public Pt Post;
    public List<Pt> Beat;
    public int BeatAt, BeatDir = 1;
    /// <summary>A spider's body on site (actor id), -1 for one with no body here.</summary>
    public int Spider = -1;
    public float RelaunchT = 8;
    public IcDef Def => Tables.IcOf(Kind);
}

public enum MatrixEvent
{
    None, Moved, Store, File, Control, Spike, IcDown, Hit, Bio, Locked, Crashed, Acid, Reporting, Alerted, Converged, Bricked,
    Spoofed, Slipped, Left, CoreDown, Planted, SpiderDown, SpiderGone, Gate, Purged, Program, Launched, NoRoom, Patched,
}

public readonly record struct MxEvent(MatrixEvent Kind, int Value = 0, string Text = null);

/// <summary>What the runner does in there this step.</summary>
public struct MxInput
{
    public V2 Move;
    /// <summary>Spike (attack); hold E (pull a file, take a node); press E (jack out at the entry); run active program 0-2; purge a program.</summary>
    public bool Spike, Hold, Press, Purge;
    public int Program;
}

/// <summary>
/// A jack into a host: a grid of nodes joined by data lines (some locked by
/// firewall gates). Your persona steps node to node, pulls files into the
/// deck's memory, takes control nodes and fights IC with spikes. IC patrols,
/// guards and -- once the host is alert -- hunts you; every second inside,
/// and every loud thing done, fills the trace; full, and you are dumped.
/// Spiders on the site are in here too, as personas. Your body stays in the
/// room, sitting very still. Rules and numbers: docs/MATRIX.md, Tables.Matrix.
/// </summary>
public sealed class MatrixRun
{
    public const int W = 9, H = 5;
    /// <summary>Links: bit 0 right, bit 1 down; gates are locked links (same bits).</summary>
    public readonly byte[] Links = new byte[W * H], Gates = new byte[W * H];
    public readonly MxNode[] Nodes = new MxNode[W * H];
    public readonly Dictionary<int, MxControl> ControlAt = new();
    public readonly HashSet<int> ControlsTaken = new();
    /// <summary>Crack progress (0-1) on each locked gate, by edge key.</summary>
    public readonly Dictionary<int, float> Crack = new();
    public readonly List<MxFile> Files = new();
    /// <summary>The job's datastores, and those already pulled (kept as points for the bot, the tests and the HUD).</summary>
    public readonly List<Pt> Stores = new();
    public readonly List<Pt> Taken = new();
    public readonly List<Ic> Ice = new();
    public readonly List<MxEvent> Events = new();
    public readonly List<string> Programs, Crashed = new();
    private readonly HashSet<string> _used = new();
    public readonly MatrixSetup S;
    public Pt You, From, Entry, Core = new(-1, -1), PlantAt = new(-1, -1);
    public int Rating, Damage, Boxes, Firewall, UsedMp;
    public float Trace, CoreHp, CoreMax, Locked, SlowT, DecoyT, LaunchT, HoldT, PlantT, MoveCd, SpikeCd, DecoyCd;
    public Pt Decoy;
    public bool Alert, CoreDown, Planted, SpoofLeft, SlipLeft, Out;
    public string Ended;
    /// <summary>What holding E is doing here, for the HUD (null: nothing).</summary>
    public string Holding;
    public float HoldNeed;
    private readonly Rng _r;
    private readonly float _moveTime;

    public MatrixRun(MatrixSetup s)
    {
        S = s;
        _r = new Rng(s.Seed);
        Rating = Math.Clamp(s.Rating, 1, 6);
        Programs = new List<string>(s.Programs);
        Boxes = s.Boxes;
        Damage = Math.Min(s.Damage, s.Boxes - 1);
        Firewall = s.Mx[3];
        UsedMp = s.UsedMp;
        Trace = s.Trace;
        Alert = s.Alert;
        SpoofLeft = s.Spoof;
        SlipLeft = s.Slip;
        _moveTime = MathF.Max(0.1f, Tables.MxMove / (1 + 0.1f * Data) / s.Speed);
        Build();
    }

    public int Attack => S.Mx[0];
    public int Sleaze => S.Mx[1];
    public int Data => S.Mx[2];
    public bool Has(string p) => Programs.Contains(p);
    public int Free => S.Memory - UsedMp - Programs.Sum(p => Tables.Program(p).Mp);
    public float Progress => Taken.Count / 3f;
    public bool Black => Ice.Any(i => i.Kind == IcKind.Black);
    public bool SpiderIn => Ice.Any(i => i.Kind == IcKind.Spider);
    public int Hunters => Ice.Count(i => !i.Guard && i.Kind != IcKind.Patrol && i.Kind != IcKind.Spider);

    // ---- the host ------------------------------------------------------------------------

    private void Build()
    {
        // A spanning tree over the grid (every node reachable), then a few extra lines for loops.
        var seen = new bool[W * H];
        var stack = new Stack<Pt>();
        Entry = You = From = new Pt(0, H / 2);
        Nodes[Idx(Entry)] = MxNode.Entry;
        stack.Push(Entry);
        seen[Idx(Entry)] = true;
        while (stack.Count > 0)
        {
            Pt p = stack.Peek();
            var next = Pt.Dirs4.Select(d => p + d).Where(n => In(n) && !seen[Idx(n)]).ToList();
            if (next.Count == 0) { stack.Pop(); continue; }
            Pt n = _r.Pick(next);
            Link(Links, p, n);
            seen[Idx(n)] = true;
            stack.Push(n);
        }
        for (int k = 0; k < W * H / 3; k++)
        {
            Pt p = new(_r.Int(W), _r.Int(H));
            Pt d = _r.Chance(0.5f) ? new Pt(1, 0) : new Pt(0, 1);
            if (In(p + d)) Link(Links, p, p + d);
        }
        Pt Spot(int x0, int x1)
        {
            for (int t = 0; t < 200; t++)
            {
                Pt p = new(_r.Range(x0, Math.Max(x0, x1 - 1)), _r.Int(H));
                if (Nodes[Idx(p)] == MxNode.Plain) return p;
            }
            return Enumerable.Range(0, W * H).Select(i => new Pt(i % W, i / W)).First(p => Nodes[Idx(p)] == MxNode.Plain);
        }
        // The core: IC comes from here. Far side.
        Core = Spot(W - 2, W);
        Nodes[Idx(Core)] = MxNode.Core;
        CoreMax = CoreHp = Tables.MxCoreHpBase + Tables.MxCoreHpPer * Rating;
        // The job's datastores on the far half, unless the job is the core or a backdoor.
        if (S.Goal is MxGoal.Pull or MxGoal.Keys)
            while (Stores.Count < 3)
            {
                Pt p = Spot(W / 2, W);
                Nodes[Idx(p)] = MxNode.Store;
                Stores.Add(p);
                Files.Add(new MxFile { At = p, Objective = true, Mp = S.Goal == MxGoal.Keys ? 0 : 1, Name = S.Goal == MxGoal.Keys ? "pass key" : "the paydata" });
            }
        if (S.Goal == MxGoal.Plant)
        {
            PlantAt = Spot(W - 3, W);
            Nodes[Idx(PlantAt)] = MxNode.Plant;
        }
        // Paydata worth stealing on the side (not in a lift's security host).
        if (S.Use != MxUse.Lift)
            for (int k = 2 + _r.Int(3); k > 0; k--)
            {
                Pt p = Spot(2, W);
                Nodes[Idx(p)] = MxNode.File;
                int mp = 1 + _r.Int(Math.Min(4, 1 + Rating / 2));
                int value = (int)(mp * (Tables.MxFileBase + Tables.MxFilePer * S.Threat) * _r.Range(0.7f, 1.3f) / 50) * 50;
                Files.Add(new MxFile { At = p, Mp = mp, Value = value, Name = _r.Pick(Tables.PaydataNames), Encrypted = Rating >= 3 && _r.Chance(0.4f) });
            }
        foreach (MxControl c in S.Controls)
        {
            Pt p = Spot(2, W - 2);
            Nodes[Idx(p)] = MxNode.Control;
            ControlAt[Idx(p)] = c;
        }
        // Firewall gates on a few lines past the entry.
        for (int g = Rating / 2, tries = 0; g > 0 && tries < 100; tries++)
        {
            Pt p = new(_r.Range(1, W - 1), _r.Int(H));
            Pt d = _r.Chance(0.5f) ? new Pt(1, 0) : new Pt(0, 1);
            if (!Linked(p, p + d) || Gated(p, p + d)) continue;
            Link(Gates, p, p + d);
            g--;
        }
        // Who is in here: patrols walking beats, guards on the datastores, the spiders.
        int patrols = 1 + Rating / 2 + (S.OnEdge ? 1 : 0);
        for (int k = 0; k < patrols; k++)
        {
            Ic ic = NewIc(IcKind.Patrol, Spot(3, W));
            ic.Beat = BeatFrom(ic.Pos);
            Ice.Add(ic);
        }
        var guarded = Stores.Concat(Files.Where(f => !f.Objective).OrderByDescending(f => f.Value).Select(f => f.At)).ToList();
        if (PlantAt.X >= 0) guarded.Insert(0, PlantAt);
        if (S.Goal == MxGoal.Crash) guarded.Insert(0, Core);
        int guards = Rating >= 4 ? 2 : Rating >= 2 ? 1 : 0;
        for (int k = 0; k < guards && k < guarded.Count; k++)
        {
            IcKind kind = Rating >= 5 && k == 0 ? IcKind.Black : IcKind.Killer;
            Ic ic = NewIc(kind, guarded[k]);
            ic.Guard = true;
            ic.Post = guarded[k];
            Ice.Add(ic);
        }
        foreach (int id in S.Spiders) Ice.Add(NewSpider(id));
        LaunchT = LaunchEvery * 0.5f;
    }

    private Ic NewIc(IcKind kind, Pt at)
    {
        float hp = (Tables.MxIcHpBase + Tables.MxIcHpPer * Rating) * Tables.IcOf(kind).HpMul;
        return new Ic { Kind = kind, Pos = at, Next = at, Hp = hp, MaxHp = hp, StepT = _r.Range(0, 0.5f) };
    }

    private Ic NewSpider(int actor)
    {
        float hp = Tables.MxSpiderHpBase + Tables.MxSpiderHpPer * Rating;
        return new Ic { Kind = IcKind.Spider, Pos = Core, Next = Core, Hp = hp, MaxHp = hp, Spider = actor, Hunting = true };
    }

    /// <summary>A patrol's beat: a walk of a few nodes, walked there and back.</summary>
    private List<Pt> BeatFrom(Pt start)
    {
        var beat = new List<Pt> { start };
        Pt at = start, prev = start;
        for (int k = 0; k < 5; k++)
        {
            var next = Pt.Dirs4.Select(d => at + d).Where(n => Passable(at, n) && n != prev && !beat.Contains(n)).ToList();
            if (next.Count == 0) break;
            prev = at;
            at = _r.Pick(next);
            beat.Add(at);
        }
        return beat;
    }

    private float LaunchEvery => MathF.Max(Tables.MxLaunchMin, Tables.MxLaunchBase - Rating);
    private int HunterCap => 2 + Rating;

    public static bool In(Pt p) => p.X >= 0 && p.Y >= 0 && p.X < W && p.Y < H;
    public static int Idx(Pt p) => p.Y * W + p.X;
    private static int Edge(Pt a, Pt b)
    {
        if (b.X < a.X || b.Y < a.Y) (a, b) = (b, a);
        return Idx(a) * 2 + (b.X > a.X ? 0 : 1);
    }

    private static void Link(byte[] set, Pt a, Pt b)
    {
        if (b.X < a.X || b.Y < a.Y) (a, b) = (b, a);
        set[Idx(a)] |= (byte)(b.X > a.X ? 1 : 2);
    }

    private static bool Has(byte[] set, Pt a, Pt b)
    {
        if (!In(a) || !In(b) || a.Chebyshev(b) != 1 || a.X != b.X && a.Y != b.Y) return false;
        if (b.X < a.X || b.Y < a.Y) (a, b) = (b, a);
        return (set[Idx(a)] & (b.X > a.X ? 1 : 2)) != 0;
    }

    public bool Linked(Pt a, Pt b) => Has(Links, a, b);
    /// <summary>A firewall gate on this line, still locked.</summary>
    public bool Gated(Pt a, Pt b) => Has(Gates, a, b);
    public bool Passable(Pt a, Pt b) => Linked(a, b) && !Gated(a, b);
    public float CrackOf(Pt a, Pt b) => Crack.GetValueOrDefault(Edge(a, b));

    /// <summary>Within one line of you (or on your node), with no locked gate between.</summary>
    public bool Near(Pt a, Pt b) => a == b || Passable(a, b);

    // ---- a step ----------------------------------------------------------------------------

    public void Step(float dt, MxInput inp)
    {
        Events.Clear();
        if (Out) return;
        MoveCd -= dt;
        SpikeCd -= dt;
        DecoyCd -= dt;
        if (Locked > 0) Locked -= dt;
        if (SlowT > 0) SlowT -= dt;
        if (DecoyT > 0) DecoyT -= dt;
        if (inp.Press && You == Entry)
        {
            if (Locked > 0) Events.Add(new MxEvent(MatrixEvent.Locked, 0, "Link-locked: you can't jack out yet."));
            else { Out = true; Ended = "jacked out"; Events.Add(new MxEvent(MatrixEvent.Left)); return; }
        }
        if (inp.Program >= 0) RunProgram(inp.Program);
        if (inp.Purge) Purge();
        Move(dt, inp.Move);
        if (inp.Spike) Spike();
        HoldAction(dt, inp.Hold);
        StepIce(dt);
        Launch(dt);
        StepTrace(dt);
        if (Out) return;
        if (Damage >= Boxes)
        {
            Out = true;
            Ended = "your deck bricked";
            Events.Add(new MxEvent(MatrixEvent.Bricked));
        }
    }

    private void Move(float dt, V2 move)
    {
        if (MoveCd > 0 || move.LengthSquared() < 0.25f) return;
        Pt d = MathF.Abs(move.X) > MathF.Abs(move.Y) ? new Pt(Math.Sign(move.X), 0) : new Pt(0, Math.Sign(move.Y));
        Pt to = You + d;
        if (!Linked(You, to)) return;
        if (Gated(You, to))
        {
            // Hack on the fly: push into the gate and it gives, quietly (Sleaze, Decking, the right programs).
            float rate = (1 + 0.15f * Sleaze) * (1 + 0.15f * S.Decking) * (Has("breaker") ? 1.5f : 1f) * (Has("decrypt") ? 2f : 1f) / Tables.MxGateCrack / 2.5f;
            AddCrack(You, to, dt * rate);
            if (S.Decking < 4) Trace += dt * 0.01f;
            return;
        }
        From = You;
        You = to;
        MoveCd = _moveTime * (SlowT > 0 ? 2 : 1);
        HoldT = 0;
        Events.Add(new MxEvent(MatrixEvent.Moved));
    }

    private void AddCrack(Pt a, Pt b, float by)
    {
        int e = Edge(a, b);
        Crack[e] = Crack.GetValueOrDefault(e) + by;
        if (Crack[e] < 1) return;
        if (b.X < a.X || b.Y < a.Y) (a, b) = (b, a);
        Gates[Idx(a)] &= (byte)~(b.X > a.X ? 1 : 2);
        Crack.Remove(e);
        Events.Add(new MxEvent(MatrixEvent.Gate, 0, "The firewall gate gives."));
    }

    public float SpikeDamage => Attack * (1 + 0.12f * S.Decking) * (Has("hammer") ? 1.4f : 1f);

    /// <summary>What a spike would hit from here: the weakest IC in reach, else the core, else a gate to brute-force.</summary>
    public Ic SpikeTarget => Ice.Where(i => Near(You, i.Pos)).OrderBy(i => i.Kind == IcKind.Spider ? 1 : 0).ThenBy(i => i.Hp).FirstOrDefault();

    private void Spike()
    {
        if (SpikeCd > 0) return;
        float cd = Tables.MxSpikeCd * (S.Decking >= 5 ? 0.5f : 1f);
        if (SpikeTarget is Ic ic)
        {
            SpikeCd = cd;
            Trace += Tables.MxTraceSpike * TraceMul;
            ic.Hp -= SpikeDamage;
            ic.Hunting = true;
            // A patrol you hit and didn't crash is on the radio at once.
            if (ic.Kind == IcKind.Patrol && ic.Report < 0 && !Alert) ic.Report = Tables.MxReport * 0.5f;
            Events.Add(new MxEvent(MatrixEvent.Spike, (int)SpikeDamage, ic.Def.Name));
            if (ic.Hp <= 0) Crash(ic);
            return;
        }
        if (!CoreDown && Near(You, Core))
        {
            SpikeCd = cd;
            Trace += Tables.MxTraceSpike * TraceMul;
            CoreHp -= SpikeDamage;
            if (!Alert) BecomeAlert("You're hammering on the host's core. It knows.");
            Events.Add(new MxEvent(MatrixEvent.Spike, (int)SpikeDamage, "the core"));
            if (CoreHp <= 0)
            {
                CoreHp = 0;
                CoreDown = true;
                Events.Add(new MxEvent(MatrixEvent.CoreDown, 0, "The core crashes. No more IC is coming."));
            }
            return;
        }
        foreach (Pt d in Pt.Dirs4)
            if (Gated(You, You + d))
            {
                // Brute force: quick, and everyone hears it.
                SpikeCd = cd;
                Trace += Tables.MxTraceBrute * TraceMul;
                AddCrack(You, You + d, 0.25f + 0.06f * Attack * (Has("breaker") ? 1.5f : 1f));
                Events.Add(new MxEvent(MatrixEvent.Spike, 0, "the gate"));
                return;
            }
    }

    private void Crash(Ic ic)
    {
        Ice.Remove(ic);
        if (ic.Kind == IcKind.Spider) Events.Add(new MxEvent(MatrixEvent.SpiderDown, ic.Spider, "The spider's persona crashes -- out there, he slumps over his console."));
        else Events.Add(new MxEvent(MatrixEvent.IcDown, (int)ic.Kind, $"{ic.Def.Name} crashed."));
    }

    private void HoldAction(float dt, bool hold)
    {
        Holding = null;
        int i = Idx(You);
        MxFile f = Files.FirstOrDefault(x => x.At == You && !x.Taken);
        if (f != null)
        {
            Holding = f.Objective ? (S.Goal == MxGoal.Keys ? "pull the pass key" : "pull the datastore") : $"pull {f.Name} ({f.Mp} Mp{(f.Encrypted ? ", encrypted" : "")})";
            HoldNeed = MathF.Max(0.4f, f.Mp * Tables.MxDownload) * (f.Encrypted && !Has("decrypt") ? 2 : 1) / (1 + 0.15f * Data);
            if (!hold) return;
            if (f.Mp > Free)
            {
                if (HoldT == 0) Events.Add(new MxEvent(MatrixEvent.NoRoom, f.Mp, $"No room in the deck: {f.Mp} Mp, {Free} free. G purges a program."));
                HoldT = 0.001f;
                return;
            }
            HoldT += dt;
            Trace += dt * Tables.MxTraceDownload;
            if (HoldT < HoldNeed) return;
            HoldT = 0;
            f.Taken = true;
            UsedMp += f.Mp;
            if (f.Objective) { Taken.Add(f.At); Events.Add(new MxEvent(MatrixEvent.Store, Taken.Count)); }
            else Events.Add(new MxEvent(MatrixEvent.File, f.Value, f.Name));
            return;
        }
        if (ControlAt.TryGetValue(i, out MxControl c) && !ControlsTaken.Contains(i))
        {
            Holding = c switch { MxControl.Cameras => "loop every camera", MxControl.Maglocks => "open every maglock", MxControl.Turrets => "take the turrets", MxControl.Alarm => "stop the security clock", _ => "skim the accounts" };
            HoldNeed = Tables.MxControl / (1 + 0.1f * Data);
            if (!hold) return;
            HoldT += dt;
            if (HoldT < HoldNeed) return;
            HoldT = 0;
            ControlsTaken.Add(i);
            Trace += Tables.MxTraceControl * TraceMul;
            Events.Add(new MxEvent(MatrixEvent.Control, (int)c));
            return;
        }
        if (You == PlantAt && !Planted)
        {
            Holding = "plant the backdoor";
            HoldNeed = Tables.MxPlant / (1 + 0.1f * Data);
            if (!hold) return;
            HoldT += dt;
            Trace += dt * Tables.MxTraceDownload * 2;
            if (HoldT < HoldNeed) return;
            Planted = true;
            Events.Add(new MxEvent(MatrixEvent.Planted, 0, "Backdoor planted."));
        }
    }

    private void RunProgram(int slot)
    {
        var actives = Programs.Where(p => Tables.Program(p).Active).Take(3).ToList();
        if (slot >= actives.Count) return;
        string p = actives[slot];
        switch (p)
        {
            case "patch":
                if (_used.Contains(p) || Damage == 0) return;
                _used.Add(p);
                Damage = Math.Max(0, Damage - 4);
                Events.Add(new MxEvent(MatrixEvent.Patched, 4, "Patch: the deck knits itself back together."));
                break;
            case "scrub":
                if (_used.Contains(p)) return;
                _used.Add(p);
                Trace = MathF.Max(0, Trace - 0.4f);
                Events.Add(new MxEvent(MatrixEvent.Program, 0, "Signal scrub: your trail goes cold."));
                break;
            case "decoy":
                if (DecoyCd > 0) return;
                DecoyCd = 25;
                DecoyT = 6;
                Decoy = You;
                Events.Add(new MxEvent(MatrixEvent.Program, 0, "Decoy: a copy of your icon peels off."));
                break;
        }
    }

    /// <summary>Unload the last program loaded (not a free one) to make room for files.</summary>
    private void Purge()
    {
        string p = Programs.LastOrDefault(x => Tables.Program(x).Mp > 0);
        if (p == null) return;
        Programs.Remove(p);
        Events.Add(new MxEvent(MatrixEvent.Purged, Tables.Program(p).Mp, $"Purged {Tables.Program(p).Name}: {Tables.Program(p).Mp} Mp free."));
    }

    public void BecomeAlert(string why)
    {
        if (Alert) return;
        Alert = true;
        Trace += 0.1f;
        foreach (Ic ic in Ice) if (ic.Kind != IcKind.Patrol) ic.Hunting = true;
        LaunchT = MathF.Min(LaunchT, 2);
        Events.Add(new MxEvent(MatrixEvent.Alerted, 0, why));
    }

    // ---- IC --------------------------------------------------------------------------------

    /// <summary>How far patrols see: the next node along a line, or only their own with the Sleaze or the program for it.</summary>
    public int Sight => Has("sneak") || Sleaze >= 5 ? 0 : 1;

    private void StepIce(float dt)
    {
        var dist = Bfs(You);
        int[] decoy = DecoyT > 0 ? Bfs(Decoy) : null;
        foreach (Ic ic in Ice.ToList())
        {
            if (!Ice.Contains(ic)) continue;
            IcDef def = ic.Def;
            if (ic.Guard && !ic.Hunting && (Alert || dist[Idx(ic.Pos)] <= 2)) ic.Hunting = true;
            // Moving.
            ic.StepT -= dt;
            if (ic.StepT <= 0)
            {
                ic.StepT = def.Step * (1.25f - 0.05f * Rating) * (Has("ghost") ? 1.3f : 1f);
                Pt to = ic.Pos;
                if (ic.Kind == IcKind.Patrol && ic.Beat != null && ic.Beat.Count > 1)
                {
                    if (ic.Report < 0)
                    {
                        if (ic.BeatAt + ic.BeatDir < 0 || ic.BeatAt + ic.BeatDir >= ic.Beat.Count) ic.BeatDir = -ic.BeatDir;
                        ic.BeatAt += ic.BeatDir;
                        to = ic.Beat[ic.BeatAt];
                    }
                }
                else if (ic.Hunting)
                {
                    int[] field = decoy != null && ic.Kind != IcKind.Spider ? decoy : dist;
                    to = Toward(ic, field);
                }
                else if (ic.Guard && ic.Pos != ic.Post) to = Toward(ic, Bfs(ic.Post));
                ic.Pos = to;
                ic.Next = PlanNext(ic, dist, decoy);
            }
            // Seeing, reporting.
            if (ic.Kind == IcKind.Patrol && !Alert)
            {
                bool sees = ic.Pos == You || Sight >= 1 && Passable(ic.Pos, You);
                if (sees && ic.Report < 0) { ic.Report = Tables.MxReport; Events.Add(new MxEvent(MatrixEvent.Reporting, 0, "A patrol IC has seen you -- crash it before it reports!")); }
                if (ic.Report >= 0 && (ic.Report -= dt) <= 0) BecomeAlert("The patrol reported you. The host is on alert.");
            }
            // Fighting.
            bool reach = Near(ic.Pos, You);
            if (reach && !ic.InReach) ic.HitT = MathF.Max(ic.HitT, Tables.MxIcWindup);
            ic.InReach = reach;
            if (ic.HitT > 0) ic.HitT -= dt;
            if (ic.Kind == IcKind.Track && reach) Trace += Tables.MxTraceTrack * dt * TraceMul;
            if (reach && ic.HitT <= 0 && ic.Kind is not (IcKind.Patrol or IcKind.Track) && (ic.Hunting || ic.Guard))
            {
                ic.HitT = Tables.MxIcHitCd;
                Hit(ic);
            }
            if (ic.Kind == IcKind.Spider && (ic.RelaunchT -= dt) <= 0)
            {
                ic.RelaunchT = 8;
                if (!CoreDown && Hunters < HunterCap && Alert) { Ice.Add(Hunter()); Events.Add(new MxEvent(MatrixEvent.Launched, 0, "The spider relaunches IC.")); }
            }
        }
    }

    private Pt Toward(Ic ic, int[] field)
    {
        Pt best = ic.Pos;
        foreach (Pt d in Pt.Dirs4)
        {
            Pt n = ic.Pos + d;
            if (!Passable(ic.Pos, n) || field[Idx(n)] >= field[Idx(best)]) continue;
            if (n != You && Ice.Any(o => o != ic && o.Pos == n)) continue;
            best = n;
        }
        return best;
    }

    /// <summary>Where an IC will step next (shown at Decking 3).</summary>
    private Pt PlanNext(Ic ic, int[] dist, int[] decoy)
    {
        if (ic.Kind == IcKind.Patrol && ic.Beat != null && ic.Beat.Count > 1)
        {
            int at = ic.BeatAt + ic.BeatDir;
            return at < 0 || at >= ic.Beat.Count ? ic.Beat[ic.BeatAt - ic.BeatDir] : ic.Beat[at];
        }
        return ic.Hunting ? Toward(ic, decoy != null && ic.Kind != IcKind.Spider ? decoy : dist) : ic.Pos;
    }

    private void Hit(Ic ic)
    {
        IcDef def = ic.Def;
        switch (ic.Kind)
        {
            case IcKind.Acid when Firewall > 0:
                Firewall--;
                Events.Add(new MxEvent(MatrixEvent.Acid, Firewall, $"Acid IC burns your Firewall ({Firewall} left)."));
                return;
            case IcKind.Crash:
            {
                if (_r.Chance(0.08f * Firewall)) { Events.Add(new MxEvent(MatrixEvent.Hit, 0, "Your Firewall shrugs off a Crash IC.")); return; }
                string p = Programs.Where(x => Tables.Program(x).Mp > 0).OrderBy(_ => _r.Next()).FirstOrDefault();
                if (p == null) return;
                Programs.Remove(p);
                Crashed.Add(p);
                Events.Add(new MxEvent(MatrixEvent.Crashed, 0, $"Crash IC: {Tables.Program(p).Name} is gone for the run."));
                return;
            }
        }
        if (def.Boxes > 0)
        {
            int boxes = Math.Max(1, def.Boxes + Rating / 3 - Firewall / 3 - (Has("armor") ? 1 : 0));
            Damage += boxes;
            Events.Add(new MxEvent(MatrixEvent.Hit, boxes, def.Name));
        }
        if (def.Bio > 0)
        {
            float bio = def.Bio * (3 + Rating) * (Has("biofilter") ? 0.5f : 1f);
            Events.Add(new MxEvent(MatrixEvent.Bio, (int)MathF.Round(bio), def.Name));
        }
        if (def.Lock > 0)
        {
            if (Locked <= 0) Events.Add(new MxEvent(MatrixEvent.Locked, (int)def.Lock, $"{def.Name} link-locks you: no jacking out for {def.Lock:0} s."));
            Locked = MathF.Max(Locked, def.Lock);
            if (ic.Kind == IcKind.TarBaby) SlowT = def.Lock;
        }
    }

    /// <summary>A hunter, launched from the core: the harder the host, the nastier the pick.</summary>
    private Ic Hunter()
    {
        var pool = new List<IcKind> { IcKind.Killer, IcKind.Killer, IcKind.Track };
        if (Rating >= 2) pool.Add(IcKind.TarBaby);
        if (Rating >= 3) pool.AddRange(new[] { IcKind.Acid, IcKind.Crash, IcKind.Blaster });
        if (Rating >= 5) pool.AddRange(new[] { IcKind.Black, IcKind.Black });
        Ic ic = NewIc(_r.Pick(pool), Core);
        ic.Hunting = true;
        return ic;
    }

    private void Launch(float dt)
    {
        if (!Alert || CoreDown || Hunters >= HunterCap) return;
        if ((LaunchT -= dt) > 0) return;
        LaunchT = LaunchEvery;
        Ic ic = Hunter();
        Ice.Add(ic);
        Events.Add(new MxEvent(MatrixEvent.Launched, (int)ic.Kind, $"The core launches {ic.Def.Name}."));
    }

    /// <summary>A spider's body on site was taken out: his persona goes with it.</summary>
    public void SpiderGone(int actor)
    {
        Ic ic = Ice.FirstOrDefault(i => i.Kind == IcKind.Spider && i.Spider == actor);
        if (ic == null) return;
        Ice.Remove(ic);
        Events.Add(new MxEvent(MatrixEvent.SpiderGone, actor, "The spider's icon freezes, and is gone."));
    }

    // ---- the trace ------------------------------------------------------------------------------

    /// <summary>How fast loud things fill the trace: Sleaze, Decking and Sneak slow it.</summary>
    public float TraceMul => MathF.Max(0.25f, (1 - 0.05f * Sleaze) * (1 - 0.06f * S.Decking) * (Has("sneak") ? 0.75f : 1f));

    /// <summary>The trace per second right now (for the HUD and the tests).</summary>
    public float TraceRate => Tables.MxTraceBase * Rating * (Alert ? Tables.MxTraceAlert : 1) * (SpiderIn ? Tables.MxTraceSpider : 1) * (CoreDown ? Tables.MxTraceCore : 1) * TraceMul;

    private void StepTrace(float dt)
    {
        Trace += TraceRate * dt;
        if (Trace < 1) return;
        if (SpoofLeft)
        {
            SpoofLeft = false;
            Trace = 0.6f;
            Events.Add(new MxEvent(MatrixEvent.Spoofed, 0, "The trace lands on your spoof and follows it away."));
            return;
        }
        if (SlipLeft)
        {
            SlipLeft = false;
            Trace = 0.6f;
            Events.Add(new MxEvent(MatrixEvent.Slipped, 0, "The trace closes -- and you're not where it closes. Old habits."));
            return;
        }
        Trace = 1;
        Out = true;
        Ended = Black || SpiderIn ? "black IC fried you" : "the host traced you";
        Events.Add(new MxEvent(MatrixEvent.Converged));
    }

    public int[] Bfs(Pt from)
    {
        var d = Enumerable.Repeat(999, W * H).ToArray();
        var q = new Queue<Pt>();
        d[Idx(from)] = 0;
        q.Enqueue(from);
        while (q.Count > 0)
        {
            Pt p = q.Dequeue();
            foreach (Pt dd in Pt.Dirs4)
            {
                Pt n = p + dd;
                if (Passable(p, n) && d[Idx(n)] > d[Idx(p)] + 1) { d[Idx(n)] = d[Idx(p)] + 1; q.Enqueue(n); }
            }
        }
        return d;
    }

    /// <summary>The same, through locked gates too (a route you could crack your way along).</summary>
    public int[] BfsAll(Pt from)
    {
        var d = Enumerable.Repeat(999, W * H).ToArray();
        var q = new Queue<Pt>();
        d[Idx(from)] = 0;
        q.Enqueue(from);
        while (q.Count > 0)
        {
            Pt p = q.Dequeue();
            foreach (Pt dd in Pt.Dirs4)
            {
                Pt n = p + dd;
                int cost = Gated(p, n) ? 3 : 1;
                if (Linked(p, n) && d[Idx(n)] > d[Idx(p)] + cost) { d[Idx(n)] = d[Idx(p)] + cost; q.Enqueue(n); }
            }
        }
        return d;
    }
}
