using System;
using System.Collections.Generic;
using Godot;

namespace Sprawl;

public enum FxKind { Spark, Debris, Smoke, Flash, Blood, Ember, Shell, Ring, Text }

public sealed class Particle
{
    public FxKind Kind;
    public Vector2 Pos, Vel;
    public float Life, Max, Size, Drag = 3, Spin;
    public Color Colour;
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
        var p = new Particle { Kind = kind, Pos = pos, Vel = vel, Life = life, Max = life, Size = size, Colour = c };
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
            Add(kind, pos, new Vector2(MathF.Cos(a), MathF.Sin(a)) * s, life * Rand(0.6f, 1.2f), size * Rand(0.6f, 1.3f), c);
        }
    }

    public void Step(float dt)
    {
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            Particle p = Items[i];
            p.Life -= dt;
            if (p.Life <= 0) { Items.RemoveAt(i); continue; }
            p.Pos += p.Vel * dt;
            p.Vel *= MathF.Exp(-p.Drag * dt);
            if (p.Kind == FxKind.Smoke) p.Size += dt * 18;
            if (p.Kind == FxKind.Ring) p.Size += dt * p.Vel.X;
        }
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
                    DrawRect(new Rect2(p.Pos - new Vector2(p.Size / 2, p.Size / 2), new Vector2(p.Size, p.Size * 0.7f)), p.Colour with { A = MathF.Min(1, u * 3) });
                    break;
                case FxKind.Blood:
                    DrawCircle(p.Pos, p.Size, p.Colour with { A = p.Colour.A * MathF.Min(1, u * 2) });
                    break;
                case FxKind.Ring:
                    DrawArc(p.Pos, p.Size, 0, MathF.Tau, 32, p.Colour with { A = p.Colour.A * u }, 3);
                    break;
                case FxKind.Text:
                    UITheme.Write(this, p.Pos - new Vector2(150, 0), p.Text, (int)p.Size, p.Colour with { A = MathF.Min(1, u * 2) }, HorizontalAlignment.Center, 300, heading: true);
                    break;
            }
        }
    }
}
