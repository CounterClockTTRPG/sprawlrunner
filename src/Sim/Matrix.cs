using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// A jack into a mainframe: a grid of nodes joined by data lines. Your
/// avatar steps node to node; ICE steps towards you on its own clock; three
/// datastores hold the paydata. Touch ICE and it traces you (alarm, unless
/// Spoof eats the first one) and dumps you out with a jolt of dumpshock
/// (black ICE, on hard jobs, hurts). Leave by the node you came in at, or
/// get dumped -- paydata grabbed so far is kept. Your body stays in the
/// room, sitting very still, while you are in here.
/// </summary>
public sealed class MatrixRun
{
    public const int W = 9, H = 5;
    /// <summary>Links: bit 0 right, bit 1 down.</summary>
    public readonly byte[] Links = new byte[W * H];
    public Pt You, Entry;
    public readonly List<Pt> Stores = new();
    public readonly List<Pt> Taken = new();
    public readonly List<Pt> Ice = new();
    public bool Black, SpoofLeft, Out;
    public float MoveCd, IceT, IceStep;
    public string Ended;
    private readonly Rng _r;

    public MatrixRun(ulong seed, int threat, bool breaker, bool spoof, float hackRate)
    {
        _r = new Rng(seed);
        // A spanning tree over the grid (every node reachable), then a few extra lines for loops.
        var seen = new bool[W * H];
        var stack = new Stack<Pt>();
        Entry = You = new Pt(0, H / 2);
        stack.Push(Entry);
        seen[Idx(Entry)] = true;
        while (stack.Count > 0)
        {
            Pt p = stack.Peek();
            var next = Pt.Dirs4.Select(d => p + d).Where(n => In(n) && !seen[Idx(n)]).ToList();
            if (next.Count == 0) { stack.Pop(); continue; }
            Pt n = _r.Pick(next);
            Link(p, n);
            seen[Idx(n)] = true;
            stack.Push(n);
        }
        for (int k = 0; k < W * H / 3; k++)
        {
            Pt p = new(_r.Int(W), _r.Int(H));
            Pt d = _r.Chance(0.5f) ? new Pt(1, 0) : new Pt(0, 1);
            if (In(p + d)) Link(p, p + d);
        }
        // Datastores on the far side; ICE waiting among them.
        while (Stores.Count < 3)
        {
            Pt s = new(_r.Range(W / 2, W - 1), _r.Int(H));
            if (!Stores.Contains(s)) Stores.Add(s);
        }
        int ice = 1 + threat / 2;
        while (Ice.Count < ice)
        {
            Pt s = new(_r.Range(W / 2 + 1, W - 1), _r.Int(H));
            if (!Ice.Contains(s) && !Stores.Contains(s)) Ice.Add(s);
        }
        Black = threat >= 4;
        SpoofLeft = spoof;
        IceStep = (1.05f - 0.08f * threat) * (breaker ? 1.35f : 1f);
        MoveCd = 0;
        _moveTime = MathF.Max(0.1f, 0.24f / MathF.Sqrt(hackRate));
    }

    private readonly float _moveTime;

    public static bool In(Pt p) => p.X >= 0 && p.Y >= 0 && p.X < W && p.Y < H;
    public static int Idx(Pt p) => p.Y * W + p.X;

    private void Link(Pt a, Pt b)
    {
        if (b.X < a.X || b.Y < a.Y) (a, b) = (b, a);
        Links[Idx(a)] |= (byte)(b.X > a.X ? 1 : 2);
    }

    public bool Linked(Pt a, Pt b)
    {
        if (!In(a) || !In(b) || a.Chebyshev(b) != 1 || a.X != b.X && a.Y != b.Y) return false;
        if (b.X < a.X || b.Y < a.Y) (a, b) = (b, a);
        return (Links[Idx(a)] & (b.X > a.X ? 1 : 2)) != 0;
    }

    public float Progress => Taken.Count / 3f;

    /// <summary>One step. move: the direction pressed; leave: jack out (only at the entry node).</summary>
    public MatrixEvent Step(float dt, V2 move, bool leave)
    {
        if (Out) return MatrixEvent.None;
        MoveCd -= dt;
        var ev = MatrixEvent.None;
        if (leave && You == Entry) { Out = true; Ended = "jacked out"; return MatrixEvent.Left; }
        if (MoveCd <= 0 && move.LengthSquared() > 0.25f)
        {
            Pt d = MathF.Abs(move.X) > MathF.Abs(move.Y) ? new Pt(Math.Sign(move.X), 0) : new Pt(0, Math.Sign(move.Y));
            if (Linked(You, You + d))
            {
                You += d;
                MoveCd = _moveTime;
                ev = MatrixEvent.Moved;
                if (Stores.Contains(You) && !Taken.Contains(You)) { Taken.Add(You); ev = MatrixEvent.Store; }
            }
        }
        if (Ice.Contains(You)) return Caught();
        IceT += dt;
        if (IceT >= IceStep)
        {
            IceT = 0;
            var dist = Bfs(You);
            for (int i = 0; i < Ice.Count; i++)
            {
                Pt best = Ice[i];
                foreach (Pt d in Pt.Dirs4)
                {
                    Pt n = Ice[i] + d;
                    if (Linked(Ice[i], n) && dist[Idx(n)] < dist[Idx(best)] && !Ice.Contains(n)) best = n;
                }
                Ice[i] = best;
            }
            if (Ice.Contains(You)) return Caught();
        }
        return ev;
    }

    private MatrixEvent Caught()
    {
        if (SpoofLeft)
        {
            // The spoof takes the hit; the ICE resets to its post.
            SpoofLeft = false;
            for (int i = 0; i < Ice.Count; i++) if (Ice[i] == You) Ice[i] = new Pt(W - 1, i % H);
            return MatrixEvent.Spoofed;
        }
        Out = true;
        Ended = Black ? "black ICE fried you" : "ICE traced you";
        return MatrixEvent.Caught;
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
                if (Linked(p, n) && d[Idx(n)] > d[Idx(p)] + 1) { d[Idx(n)] = d[Idx(p)] + 1; q.Enqueue(n); }
            }
        }
        return d;
    }
}

public enum MatrixEvent { None, Moved, Store, Caught, Spoofed, Left }
