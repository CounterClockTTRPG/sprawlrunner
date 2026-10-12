using System;
using System.Collections.Generic;
using Godot;

namespace Sprawl;

public enum FxKind { Spark, Debris, Smoke, Flash, Blood, Ember, Shell, Ring, Text, Streak, Bolt, Fire, Cone }

public sealed class Particle
{
    public FxKind Kind;
    public Vector2 Pos, Vel;
    public float Life, Max, Size, Drag = 3, Spin, Rot;
    /// <summary>A ring's line width, a streak's or bolt's width, a cone's half-angle.</summary>
    public float Width = 3;
    /// <summary>How fast a fire puff or a smoke puff grows (px/s).</summary>
    public float Grow = 18;
    public Color Colour;
    /// <summary>What a fire puff cools to as it dies (white-hot to deep red, say).</summary>
    public Color Colour2;
    /// <summary>A streak's far end.</summary>
    public Vector2 To;
    /// <summary>A bolt's path, jagged.</summary>
    public Vector2[] Points;
    public string Text;
}

/// <summary>
/// A light-weight particle system drawn with primitives: sparks and flashes
/// on an additive layer, smoke and debris on a normal one.
/// </summary>
public partial class FxLayer : Node2D
{
    public readonly List<Particle> Items = new();
    public bool AdditiveLayer;
    private readonly Random _rng = new();

    public float Rand(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

    public Particle Add(FxKind kind, Vector2 pos, Vector2 vel, float life, float size, Color c)
    {
        var p = new Particle { Kind = kind, Pos = pos, Vel = vel, Life = life, Max = life, Size = size, Colour = c, Colour2 = c };
        if (Items.Count < 1500) Items.Add(p);
        return p;
    }

    public void Burst(FxKind kind, Vector2 pos, int n, float speed, float life, float size, Color c, Vector2? dir = null, float spread = MathF.PI)
    {
        float baseAng = dir is Vector2 d ? d.Angle() : 0;
        for (int i = 0; i < n; i++)
        {
            float a = dir.HasValue ? baseAng + Rand(-spread, spread) : Rand(0, MathF.Tau);
            float s = speed * Rand(0.3f, 1f);
            Particle p = Add(kind, pos, new Vector2(MathF.Cos(a), MathF.Sin(a)) * s, life * Rand(0.6f, 1.2f), size * Rand(0.6f, 1.3f), c);
            if (kind is FxKind.Debris or FxKind.Shell) { p.Spin = Rand(-14, 14); p.Rot = Rand(0, MathF.Tau); }
        }
    }

    /// <summary>A line that stays where a bullet went and fades: a tracer's wake.</summary>
    public Particle Streak(Vector2 from, Vector2 to, float life, float width, Color c)
    {
        Particle p = Add(FxKind.Streak, from, Vector2.Zero, life, 0, c);
        p.To = to;
        p.Width = width;
        return p;
    }

    /// <summary>A jagged bolt from <paramref name="from"/> to <paramref name="to"/>, with a branch or two.</summary>
    public void Bolt(Vector2 from, Vector2 to, float life, float width, Color c, float jag = 9, int branches = 2)
    {
        Vector2 d = to - from;
        float len = d.Length();
        if (len < 1) return;
        Vector2 n = new Vector2(-d.Y, d.X) / len;
        int segs = Math.Max(3, (int)(len / 14));
        var pts = new Vector2[segs + 1];
        for (int i = 0; i <= segs; i++)
        {
            float t = i / (float)segs;
            // Pinned at both ends, wildest in the middle.
            float off = i == 0 || i == segs ? 0 : Rand(-jag, jag) * MathF.Sin(t * MathF.PI);
            pts[i] = from + d * t + n * off;
        }
        Particle p = Add(FxKind.Bolt, from, Vector2.Zero, life, 0, c);
        p.Points = pts;
        p.Width = width;
        for (int b = 0; b < branches && segs > 3; b++)
        {
            int at = 1 + _rng.Next(segs - 2);
            Vector2 start = pts[at];
            Vector2 end = start + (d / len).Rotated(Rand(-0.9f, 0.9f)) * len * Rand(0.12f, 0.3f);
            var bp = Add(FxKind.Bolt, start, Vector2.Zero, life * 0.7f, 0, c);
            bp.Points = new[] { start, (start + end) / 2 + n * Rand(-jag, jag) * 0.6f, end };
            bp.Width = width * 0.6f;
        }
    }

    /// <summary>A fire puff: it swells and cools from <paramref name="hot"/> to <paramref name="cool"/>.</summary>
    public Particle Fire(Vector2 pos, Vector2 vel, float life, float size, float grow, Color hot, Color cool)
    {
        Particle p = Add(FxKind.Fire, pos, vel, life, size, hot);
        p.Colour2 = cool;
        p.Grow = grow;
        p.Drag = 4;
        return p;
    }

    /// <summary>A muzzle flash: a short cone of light pointing along <paramref name="dir"/>.</summary>
    public Particle Cone(Vector2 pos, Vector2 dir, float life, float length, float halfAngle, Color c)
    {
        Particle p = Add(FxKind.Cone, pos, Vector2.Zero, life, length, c);
        p.Rot = dir.Angle();
        p.Width = halfAngle;
        return p;
    }

    public void Step(float dt)
    {
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            Particle p = Items[i];
            p.Life -= dt;
            if (p.Life <= 0) { Items.RemoveAt(i); continue; }
            // A ring's velocity is how fast it grows, not where it goes: it stays where it was set off.
            if (p.Kind != FxKind.Ring) p.Pos += p.Vel * dt;
            p.Vel *= MathF.Exp(-p.Drag * dt);
            p.Rot += p.Spin * dt;
            p.Spin *= MathF.Exp(-2.5f * dt);
            if (p.Kind is FxKind.Smoke or FxKind.Fire) p.Size += dt * p.Grow;
            if (p.Kind == FxKind.Ring) p.Size += dt * p.Vel.X;
        }
        _dirty = true;
    }

    private bool _dirty;

    /// <summary>Repaint once per rendered frame, however many steps ran (see CityView._Process).</summary>
    public override void _Process(double delta)
    {
        if (!_dirty) return;
        _dirty = false;
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (Particle p in Items)
        {
            float u = p.Life / p.Max;
            switch (p.Kind)
            {
                case FxKind.Spark:
                case FxKind.Ember:
                    DrawLine(p.Pos, p.Pos - p.Vel * 0.03f, p.Colour with { A = p.Colour.A * u }, p.Size);
                    break;
                case FxKind.Flash:
                    Art.DrawGlow(this, p.Pos, p.Size * (0.6f + 0.4f * u), p.Colour with { A = p.Colour.A * u });
                    break;
                case FxKind.Smoke:
                    DrawTextureRect(Art.Soft, new Rect2(p.Pos - new Vector2(p.Size, p.Size), new Vector2(p.Size * 2, p.Size * 2)), false, p.Colour with { A = p.Colour.A * u });
                    break;
                case FxKind.Debris:
                case FxKind.Shell:
                    // Tumbling as it flies, settling as the spin dies.
                    DrawSetTransform(p.Pos, p.Rot);
                    DrawRect(new Rect2(-p.Size / 2, -p.Size * 0.35f, p.Size, p.Size * 0.7f), p.Colour with { A = MathF.Min(1, u * 3) });
                    DrawSetTransform(Vector2.Zero, 0);
                    break;
                case FxKind.Blood:
                    DrawCircle(p.Pos, p.Size, p.Colour with { A = p.Colour.A * MathF.Min(1, u * 2) });
                    break;
                case FxKind.Ring:
                    DrawArc(p.Pos, p.Size, 0, MathF.Tau, 40, p.Colour with { A = p.Colour.A * u }, MathF.Max(1, p.Width * (0.4f + 0.6f * u)));
                    break;
                case FxKind.Streak:
                    // Fades from the far end first, like a wake catching up with the bullet.
                    DrawLine(p.Pos, p.To, p.Colour with { A = p.Colour.A * u * u * 0.6f }, p.Width * u);
                    DrawLine(p.Pos.Lerp(p.To, 0.55f), p.To, p.Colour with { A = p.Colour.A * u }, MathF.Max(1, p.Width * 0.5f));
                    break;
                case FxKind.Bolt:
                {
                    float flicker = 0.65f + 0.35f * MathF.Sin(p.Life * 90);
                    DrawPolyline(p.Points, p.Colour with { A = p.Colour.A * u * 0.45f * flicker }, p.Width * 2.6f);
                    DrawPolyline(p.Points, p.Colour with { A = p.Colour.A * u * flicker }, p.Width);
                    DrawPolyline(p.Points, new Color(1, 1, 1, u * 0.85f * flicker), MathF.Max(1, p.Width * 0.35f));
                    break;
                }
                case FxKind.Fire:
                {
                    // Hot and bright when it's born, cooling and thinning as it rises.
                    Color c = p.Colour2.Lerp(p.Colour, u);
                    Art.DrawGlow(this, p.Pos, p.Size, c with { A = c.A * MathF.Min(1, u * 1.6f) });
                    break;
                }
                case FxKind.Cone:
                {
                    Vector2 d = Vector2.FromAngle(p.Rot);
                    float len = p.Size * (0.7f + 0.3f * u);
                    Vector2 tip = p.Pos + d * len, l = p.Pos + d.Rotated(p.Width) * len * 0.55f, r = p.Pos + d.Rotated(-p.Width) * len * 0.55f;
                    DrawColoredPolygon(new[] { p.Pos, l, tip, r }, p.Colour with { A = p.Colour.A * u * 0.8f });
                    DrawColoredPolygon(new[] { p.Pos, p.Pos.Lerp(l, 0.5f), p.Pos.Lerp(tip, 0.7f), p.Pos.Lerp(r, 0.5f) }, new Color(1, 1, 0.9f, u));
                    break;
                }
                case FxKind.Text:
                    UITheme.Write(this, p.Pos - new Vector2(150, 0), p.Text, (int)p.Size, p.Colour with { A = MathF.Min(1, u * 2) }, HorizontalAlignment.Center, 300, heading: true);
                    break;
            }
        }
    }
}
