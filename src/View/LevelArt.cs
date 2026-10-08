using System;
using Godot;

namespace Sprawl;

/// <summary>How each tile of a run's map looks, drawn from primitives.</summary>
public static class LevelArt
{
    public const int T = 32;

    public static Color FloorColour(Floor f, int x, int y)
    {
        float n = Art.Hash(x, y) * 0.04f;
        return f switch
        {
            Floor.Street => new Color(0.085f + n, 0.09f + n, 0.105f + n),
            Floor.Yard => new Color(0.13f + n, 0.13f + n, 0.125f + n),
            Floor.Concrete => new Color(0.2f + n, 0.205f + n, 0.215f + n),
            Floor.Carpet => new Color(0.15f + n * 0.5f, 0.13f + n * 0.5f, 0.21f + n * 0.5f),
            Floor.LabTile => new Color(0.5f + n, 0.56f + n, 0.58f + n),
            Floor.Dance => ((x + y) % 2 == 0) ? new Color(0.06f, 0.04f, 0.1f) : new Color(0.1f, 0.05f, 0.16f),
            Floor.Wood => new Color(0.27f + n, 0.18f + n, 0.11f + n),
            Floor.Ritual => new Color(0.12f + n, 0.1f + n, 0.12f + n),
            _ => new Color(0.1f, 0.2f, 0.1f),
        };
    }

    public static Color WallColour(Tile t) => t switch
    {
        Tile.Drywall => new Color(0.6f, 0.6f, 0.58f),
        Tile.Brick => new Color(0.46f, 0.24f, 0.18f),
        Tile.Concrete => new Color(0.37f, 0.39f, 0.41f),
        Tile.Reinforced => new Color(0.2f, 0.22f, 0.26f),
        _ => new Color(0.5f, 0.5f, 0.5f),
    };

    private static bool IsWall(Tile t) => t is Tile.Drywall or Tile.Brick or Tile.Concrete or Tile.Reinforced or Tile.LockedDoor;

    public static void DrawTile(CanvasItem ci, Level L, int x, int y)
    {
        int i = L.Idx(x, y);
        Tile t = L.T[i];
        Floor f = L.F[i];
        var r = new Rect2(x * T, y * T, T, T);
        Vector2 o = r.Position;
        Color fc = FloorColour(f, x, y);
        ci.DrawRect(r, fc);
        DrawFloorDetail(ci, L, f, x, y, o, fc);
        // The walls' shadow on the floor beside them: light comes from the top left.
        if (!IsWall(t) && t != Tile.Glass)
        {
            if (IsWall(L[x, y - 1])) for (int k = 0; k < 4; k++) ci.DrawRect(new Rect2(o + new Vector2(0, k * 2), new Vector2(T, 2)), new Color(0, 0, 0, 0.28f - k * 0.065f));
            if (IsWall(L[x - 1, y])) for (int k = 0; k < 3; k++) ci.DrawRect(new Rect2(o + new Vector2(k * 2, 0), new Vector2(2, T)), new Color(0, 0, 0, 0.22f - k * 0.07f));
            if (IsWall(L[x - 1, y - 1]) && !IsWall(L[x, y - 1]) && !IsWall(L[x - 1, y])) ci.DrawRect(new Rect2(o, new Vector2(5, 5)), new Color(0, 0, 0, 0.18f));
        }

        float hpFrac = Level.Def(t).Hp > 0 && !float.IsPositiveInfinity(Level.Def(t).Hp) ? L.Hp[i] / Level.Def(t).Hp : 1;
        switch (t)
        {
            case Tile.Rubble:
                for (int k = 0; k < 5; k++)
                {
                    float hx = Art.Hash(x, y, k) * (T - 8), hy = Art.Hash(x, y, k + 9) * (T - 8);
                    float s = 3 + Art.Hash(x, y, k + 20) * 5;
                    ci.DrawRect(new Rect2(o + new Vector2(hx, hy), new Vector2(s, s * 0.8f)), new Color(0.32f, 0.31f, 0.3f, 0.9f));
                }
                break;
            case Tile.Drywall or Tile.Brick or Tile.Concrete or Tile.Reinforced:
                DrawWall(ci, L, x, y, t, o, hpFrac);
                break;
            case Tile.Glass:
                DrawGlassPane(ci, L, x, y, o);
                break;
            case Tile.Door or Tile.DoorOpen or Tile.LockedDoor:
                DrawDoor(ci, L, x, y, t, o, hpFrac);
                break;
            case Tile.Crate:
            {
                var b = new Rect2(o + new Vector2(3, 3), new Vector2(T - 6, T - 6));
                ci.DrawRect(new Rect2(b.Position + new Vector2(3, 4), b.Size), new Color(0, 0, 0, 0.35f));
                if (Art.Hash(x, y, 60) > 0.65f)
                {
                    // A steel shipping case: ribbed lid, corner clamps, a stencilled code.
                    Color m = new Color(0.27f, 0.33f, 0.4f).Darkened((1 - hpFrac) * 0.4f);
                    ci.DrawRect(b, m);
                    for (int k = 1; k < 4; k++) ci.DrawLine(b.Position + new Vector2(0, k * b.Size.Y / 4), b.Position + new Vector2(b.Size.X, k * b.Size.Y / 4), m.Darkened(0.3f), 1);
                    foreach (Vector2 cc in new[] { b.Position, b.Position + new Vector2(b.Size.X - 4, 0), b.Position + new Vector2(0, b.Size.Y - 4), b.End - new Vector2(4, 4) })
                        ci.DrawRect(new Rect2(cc, new Vector2(4, 4)), m.Lightened(0.25f));
                    ci.DrawRect(new Rect2(b.Position + new Vector2(6, b.Size.Y / 2f - 2), new Vector2(10, 3)), new Color(0.95f, 0.8f, 0.2f, 0.75f));
                    ci.DrawRect(b, m.Darkened(0.45f), false, 1.5f);
                    break;
                }
                Color c = new Color(0.45f, 0.33f, 0.18f).Lerp(new Color(0.52f, 0.4f, 0.22f), Art.Hash(x, y, 61)).Darkened((1 - hpFrac) * 0.4f);
                ci.DrawRect(b, c);
                // Slats, then the cross-brace, then the stencil.
                for (int k = 1; k < 3; k++) ci.DrawLine(b.Position + new Vector2(0, k * b.Size.Y / 3), b.Position + new Vector2(b.Size.X, k * b.Size.Y / 3), c.Darkened(0.2f), 1);
                ci.DrawRect(b, c.Darkened(0.4f), false, 2);
                ci.DrawLine(b.Position, b.End, c.Darkened(0.35f), 2);
                ci.DrawLine(b.Position + new Vector2(b.Size.X, 0), b.Position + new Vector2(0, b.Size.Y), c.Darkened(0.35f), 2);
                if (Art.Hash(x, y, 62) > 0.6f) ci.DrawRect(new Rect2(b.Position + new Vector2(3, 3), new Vector2(7, 3)), new Color(0.1f, 0.08f, 0.05f, 0.6f));
                break;
            }
            case Tile.Desk:
            {
                var b = new Rect2(o + new Vector2(2, 6), new Vector2(T - 4, T - 12));
                // The chair pushed back under it.
                ci.DrawCircle(o + new Vector2(T / 2f + (Art.Hash(x, y, 70) - 0.5f) * 8, T - 4), 4.5f, new Color(0.12f, 0.12f, 0.14f));
                ci.DrawRect(new Rect2(b.Position + new Vector2(2, 3), b.Size), new Color(0, 0, 0, 0.3f));
                Color top = new Color(0.3f, 0.3f, 0.33f).Lerp(new Color(0.36f, 0.27f, 0.2f), Art.Hash(x, y, 71) > 0.6f ? 1 : 0).Darkened((1 - hpFrac) * 0.4f);
                ci.DrawRect(b, top);
                ci.DrawRect(b, top.Darkened(0.35f), false, 1);
                // A monitor, a keyboard, and whatever else is lying about.
                ci.DrawRect(new Rect2(o + new Vector2(10, 8), new Vector2(12, 5)), new Color(0.05f, 0.05f, 0.07f));
                ci.DrawRect(new Rect2(o + new Vector2(11, 9), new Vector2(10, 3)), new Color(0.2f, 0.8f, 1f, 0.8f));
                ci.DrawRect(new Rect2(o + new Vector2(11, 15), new Vector2(10, 3)), new Color(0.16f, 0.16f, 0.18f));
                if (Art.Hash(x, y, 72) > 0.5f) ci.DrawRect(new Rect2(o + new Vector2(3.5f, 8), new Vector2(5, 6)), new Color(0.88f, 0.86f, 0.8f));
                if (Art.Hash(x, y, 73) > 0.6f) { ci.DrawCircle(o + new Vector2(T - 6, 10), 2.2f, new Color(0.85f, 0.85f, 0.85f)); ci.DrawCircle(o + new Vector2(T - 6, 10), 1.4f, new Color(0.25f, 0.15f, 0.08f)); }
                break;
            }
            case Tile.Barrel:
            {
                Vector2 c = o + new Vector2(T / 2f, T / 2f);
                ci.DrawCircle(c + new Vector2(2, 3), 11, new Color(0, 0, 0, 0.35f));
                // Always red, always explosive; the wear varies.
                Color red = new Color(0.7f, 0.14f, 0.1f).Darkened(Art.Hash(x, y, 80) * 0.15f);
                ci.DrawCircle(c, 11, red);
                ci.DrawArc(c, 11, 0, MathF.Tau, 16, new Color(0.35f, 0.06f, 0.05f), 2);
                ci.DrawArc(c, 8.5f, 0, MathF.Tau, 16, red.Darkened(0.25f), 1);
                ci.DrawArc(c, 6, 0, MathF.Tau, 12, new Color(0.9f, 0.6f, 0.1f), 2);
                // The bung, and a flame warning.
                ci.DrawCircle(c + new Vector2(-5, -4), 1.8f, new Color(0.3f, 0.3f, 0.32f));
                ci.DrawColoredPolygon(new[] { c + new Vector2(0, -3), c + new Vector2(2.6f, 1.6f), c + new Vector2(-2.6f, 1.6f) }, new Color(1f, 0.85f, 0.2f));
                if (Art.Hash(x, y, 81) > 0.6f) ci.DrawArc(c, 10, 2.2f, 3.0f, 4, new Color(0.45f, 0.25f, 0.1f), 2);
                break;
            }
            case Tile.Fence:
            {
                Color m = new(0.55f, 0.6f, 0.62f, 0.7f);
                for (int k = -T; k < T; k += 8)
                {
                    ci.DrawLine(o + new Vector2(Math.Max(0, k), Math.Max(0, -k)), o + new Vector2(Math.Min(T, T + k), Math.Min(T, T - k)), m, 1);
                    ci.DrawLine(o + new Vector2(Math.Max(0, k), T - Math.Max(0, -k)), o + new Vector2(Math.Min(T, T + k), T - Math.Min(T, T - k)), m, 1);
                }
                ci.DrawRect(r, new Color(0.4f, 0.45f, 0.48f, 0.8f), false, 2);
                break;
            }
            case Tile.Wreck:
            {
                bool horiz = L[x - 1, y] == Tile.Wreck || L[x + 1, y] == Tile.Wreck;
                bool first = horiz ? L[x - 1, y] != Tile.Wreck : L[x, y - 1] != Tile.Wreck;
                var b = horiz ? new Rect2(o + new Vector2(first ? 3 : 0, 5), new Vector2(T - 3, T - 10)) : new Rect2(o + new Vector2(5, first ? 3 : 0), new Vector2(T - 10, T - 3));
                ci.DrawRect(new Rect2(b.Position + new Vector2(3, 4), b.Size), new Color(0, 0, 0, 0.35f));
                Color c = new Color(0.32f, 0.2f, 0.14f).Lerp(new Color(0.25f, 0.3f, 0.35f), Art.Hash(x / 2, y / 2));
                ci.DrawRect(b, c);
                ci.DrawRect(b.Grow(-5), c.Darkened(0.35f));
                break;
            }
            case Tile.Rack:
            {
                var b = new Rect2(o + new Vector2(3, 2), new Vector2(T - 6, T - 4));
                ci.DrawRect(new Rect2(b.Position + new Vector2(3, 3), b.Size), new Color(0, 0, 0, 0.4f));
                ci.DrawRect(b, new Color(0.08f, 0.09f, 0.11f));
                // Vent slots down the side, cables off the top.
                for (int k = 0; k < 6; k++) ci.DrawLine(o + new Vector2(T - 7, 5 + k * 4), o + new Vector2(T - 5, 5 + k * 4), new Color(0.2f, 0.22f, 0.26f), 1);
                ci.DrawLine(o + new Vector2(8, 2), o + new Vector2(6, -1), new Color(0.1f, 0.1f, 0.12f), 1.5f);
                ci.DrawRect(b, new Color(0.3f, 0.5f, 0.6f).Darkened(1 - hpFrac), false, 1.5f);
                for (int k = 0; k < 5; k++)
                    ci.DrawRect(new Rect2(o + new Vector2(7 + (k % 2) * 10, 6 + k * 4.5f), new Vector2(4, 2)), Art.Hash(x, y, k) > 0.5f ? new Color(0.2f, 1f, 0.4f) : new Color(1f, 0.6f, 0.1f));
                break;
            }
            case Tile.Vat:
            {
                Vector2 c = o + new Vector2(T / 2f, T / 2f);
                ci.DrawCircle(c + new Vector2(2, 3), 14, new Color(0, 0, 0, 0.35f));
                // Pipes in and out, then the drum.
                ci.DrawLine(c + new Vector2(12, -6), c + new Vector2(T / 2f, -6), new Color(0.4f, 0.42f, 0.45f), 3);
                ci.DrawLine(c + new Vector2(-12, 6), c + new Vector2(-T / 2f, 6), new Color(0.4f, 0.42f, 0.45f), 3);
                ci.DrawCircle(c, 14, new Color(0.35f, 0.38f, 0.4f));
                for (int k = 0; k < 8; k++) ci.DrawCircle(c + new Vector2(MathF.Cos(k * 0.785f), MathF.Sin(k * 0.785f)) * 12.5f, 0.9f, new Color(0.55f, 0.58f, 0.6f));
                ci.DrawCircle(c, 11, new Color(0.3f, 0.95f, 0.35f).Darkened((1 - hpFrac) * 0.5f));
                ci.DrawCircle(c + new Vector2(-3, -3), 3, new Color(0.7f, 1f, 0.7f, 0.7f));
                break;
            }
            case Tile.Console:
            {
                var b = new Rect2(o + new Vector2(3, 4), new Vector2(T - 6, T - 10));
                ci.DrawRect(new Rect2(b.Position + new Vector2(2, 3), b.Size), new Color(0, 0, 0, 0.35f));
                ci.DrawRect(b, new Color(0.12f, 0.13f, 0.16f));
                for (int k = 0; k < 3; k++) ci.DrawRect(new Rect2(o + new Vector2(5 + k * 8, 7), new Vector2(6, 5)), new Color(0.2f, 0.7f, 1f, 0.85f));
                ci.DrawRect(new Rect2(o + new Vector2(6, 16), new Vector2(T - 12, 4)), new Color(1f, 0.25f, 0.2f, 0.8f));
                // A keyboard, and the operator's stool in front.
                for (int k = 0; k < 5; k++) ci.DrawRect(new Rect2(o + new Vector2(6 + k * 4, 22), new Vector2(3, 2)), new Color(0.3f, 0.32f, 0.36f));
                ci.DrawCircle(o + new Vector2(T / 2f, T - 1), 3.5f, new Color(0.1f, 0.1f, 0.12f));
                break;
            }
            case Tile.Safe:
            {
                var b = new Rect2(o + new Vector2(4, 4), new Vector2(T - 8, T - 8));
                ci.DrawRect(new Rect2(b.Position + new Vector2(2, 3), b.Size), new Color(0, 0, 0, 0.4f));
                ci.DrawRect(b, new Color(0.35f, 0.37f, 0.4f));
                ci.DrawRect(b, new Color(0.6f, 0.62f, 0.66f), false, 1.5f);
                // Hinges on the left, a handle on the right, the dial with its ticks.
                ci.DrawRect(new Rect2(o + new Vector2(4, 8), new Vector2(2, 4)), new Color(0.2f, 0.2f, 0.22f));
                ci.DrawRect(new Rect2(o + new Vector2(4, T - 12), new Vector2(2, 4)), new Color(0.2f, 0.2f, 0.22f));
                ci.DrawLine(o + new Vector2(T - 8, T / 2f - 5), o + new Vector2(T - 8, T / 2f + 5), new Color(0.7f, 0.72f, 0.76f), 2);
                ci.DrawCircle(o + new Vector2(T / 2f, T / 2f), 5, new Color(0.2f, 0.2f, 0.22f));
                for (int k = 0; k < 8; k++) ci.DrawCircle(o + new Vector2(T / 2f, T / 2f) + new Vector2(MathF.Cos(k * 0.785f), MathF.Sin(k * 0.785f)) * 6.3f, 0.7f, new Color(0.75f, 0.75f, 0.8f));
                ci.DrawLine(o + new Vector2(T / 2f, T / 2f), o + new Vector2(T / 2f + 4, T / 2f - 2), UITheme.Yellow, 1.5f);
                break;
            }
            case Tile.Locker:
            {
                var b = new Rect2(o + new Vector2(5, 2), new Vector2(T - 10, T - 4));
                ci.DrawRect(new Rect2(b.Position + new Vector2(2, 2), b.Size), new Color(0, 0, 0, 0.3f));
                ci.DrawRect(b, new Color(0.3f, 0.36f, 0.3f));
                ci.DrawRect(b, new Color(0.18f, 0.22f, 0.18f), false, 1);
                ci.DrawLine(o + new Vector2(T / 2f, 3), o + new Vector2(T / 2f, T - 3), new Color(0.15f, 0.18f, 0.15f), 1.5f);
                // Vents on both doors, the handles, a name tag.
                for (int k = 0; k < 3; k++)
                {
                    ci.DrawLine(o + new Vector2(8, 6 + k * 3), o + new Vector2(13, 6 + k * 3), new Color(0.15f, 0.18f, 0.15f), 1);
                    ci.DrawLine(o + new Vector2(T / 2f + 3, 6 + k * 3), o + new Vector2(T / 2f + 8, 6 + k * 3), new Color(0.15f, 0.18f, 0.15f), 1);
                }
                ci.DrawRect(new Rect2(o + new Vector2(T / 2f - 3, T / 2f), new Vector2(2, 4)), new Color(0.7f, 0.72f, 0.7f));
                ci.DrawRect(new Rect2(o + new Vector2(T / 2f + 1, T / 2f), new Vector2(2, 4)), new Color(0.7f, 0.72f, 0.7f));
                ci.DrawRect(new Rect2(o + new Vector2(8, T - 10), new Vector2(5, 3)), new Color(0.85f, 0.83f, 0.75f));
                break;
            }
            case Tile.Bunk:
            {
                // A steel cot: a frame, a thin mattress, a pillow, a blanket thrown over it.
                var b = new Rect2(o + new Vector2(3, 4), new Vector2(T - 6, T - 8));
                ci.DrawRect(new Rect2(b.Position + new Vector2(2, 3), b.Size), new Color(0, 0, 0, 0.3f));
                ci.DrawRect(b, new Color(0.25f, 0.26f, 0.28f).Darkened((1 - hpFrac) * 0.4f));
                ci.DrawRect(b.Grow(-2), new Color(0.42f, 0.4f, 0.36f));
                ci.DrawRect(new Rect2(b.Position + new Vector2(3, 3), new Vector2(7, b.Size.Y - 6)), new Color(0.75f, 0.73f, 0.68f));
                Color blanket = Art.Hash(x, y, 90) > 0.5f ? new Color(0.25f, 0.32f, 0.22f) : new Color(0.32f, 0.2f, 0.2f);
                ci.DrawRect(new Rect2(b.Position + new Vector2(11, 2), new Vector2(b.Size.X - 13, b.Size.Y - 4)), blanket);
                ci.DrawLine(b.Position + new Vector2(11, b.Size.Y / 2), b.Position + new Vector2(b.Size.X - 2, b.Size.Y / 2 + 2), blanket.Darkened(0.3f), 1);
                break;
            }
            case Tile.Pipe:
            {
                // A coolant run: a fat pipe with a valve wheel and frost on it.
                Color pipe = new Color(0.45f, 0.55f, 0.62f).Darkened((1 - hpFrac) * 0.4f);
                ci.DrawRect(new Rect2(o + new Vector2(0, 11), new Vector2(T, 12)), new Color(0, 0, 0, 0.3f));
                ci.DrawRect(new Rect2(o + new Vector2(0, 9), new Vector2(T, 11)), pipe);
                ci.DrawLine(o + new Vector2(0, 11), o + new Vector2(T, 11), pipe.Lightened(0.35f), 2);
                ci.DrawRect(new Rect2(o + new Vector2(T / 2f - 2, 7), new Vector2(4, 15)), pipe.Darkened(0.3f));
                ci.DrawArc(o + new Vector2(T / 2f, 14), 6, 0, MathF.Tau, 12, new Color(0.8f, 0.2f, 0.15f), 2);
                ci.DrawLine(o + new Vector2(T / 2f - 6, 14), o + new Vector2(T / 2f + 6, 14), new Color(0.8f, 0.2f, 0.15f), 1.5f);
                for (int k = 0; k < 3; k++) ci.DrawCircle(o + new Vector2(4 + Art.Hash(x, y, 91 + k) * (T - 8), 18), 1.3f, new Color(0.85f, 0.95f, 1f, 0.7f));
                ci.DrawString(ThemeDB.FallbackFont, o + new Vector2(3, 29), "COOLANT", HorizontalAlignment.Left, -1, 6, new Color(0.9f, 0.9f, 0.3f, 0.7f));
                break;
            }
            case Tile.Ward:
                ci.DrawRect(new Rect2(o + new Vector2(2, 2), new Vector2(T - 4, T - 4)), new Color(0.62f, 0.45f, 1f, 0.35f));
                ci.DrawRect(new Rect2(o + new Vector2(2, 2), new Vector2(T - 4, T - 4)), new Color(0.8f, 0.7f, 1f, 0.9f), false, 2);
                break;
            case Tile.Lift:
            {
                // Brushed steel doors with a seam and a lit call panel.
                ci.DrawRect(new Rect2(o, new Vector2(T, T)), new Color(0.32f, 0.34f, 0.38f));
                ci.DrawRect(new Rect2(o + new Vector2(3, 3), new Vector2(T - 6, T - 6)), new Color(0.55f, 0.58f, 0.62f));
                ci.DrawLine(o + new Vector2(T / 2f, 3), o + new Vector2(T / 2f, T - 3), new Color(0.2f, 0.2f, 0.24f), 1.5f);
                ci.DrawRect(new Rect2(o + new Vector2(T - 7, T / 2f - 4), new Vector2(4, 8)), UITheme.Cyan);
                break;
            }
            case Tile.Stairs:
                for (int k = 0; k < 6; k++) ci.DrawRect(new Rect2(o + new Vector2(2, 2 + k * 5), new Vector2(T - 4, 3)), new Color(0.45f, 0.45f, 0.5f).Darkened(k * 0.1f));
                ci.DrawRect(new Rect2(o, new Vector2(T, T)), UITheme.Yellow with { A = 0.6f }, false, 1.5f);
                break;
            case Tile.Mainframe:
            {
                var b = new Rect2(o + new Vector2(1, 1), new Vector2(T - 2, T - 2));
                ci.DrawRect(b, new Color(0.06f, 0.06f, 0.09f));
                ci.DrawRect(b, UITheme.Violet, false, 2);
                ci.DrawRect(new Rect2(o + new Vector2(6, 6), new Vector2(T - 12, 10)), new Color(0.3f, 0.9f, 1f));
                for (int k = 0; k < 3; k++) ci.DrawRect(new Rect2(o + new Vector2(6 + k * 7, 20), new Vector2(5, 5)), UITheme.Magenta);
                break;
            }
        }
    }

    private static void DrawFloorDetail(CanvasItem ci, Level L, Floor f, int x, int y, Vector2 o, Color fc)
    {
        switch (f)
        {
            case Floor.Concrete:
                if (x % 3 == 0) ci.DrawLine(o, o + new Vector2(0, T), fc.Darkened(0.25f), 1);
                if (y % 3 == 0) ci.DrawLine(o, o + new Vector2(T, 0), fc.Darkened(0.25f), 1);
                // Oil stains, hairline cracks, now and then a drain.
                if (Art.Hash(x, y, 11) > 0.9f) Blot(ci, o + new Vector2(Art.Hash(x, y, 12) * T, Art.Hash(x, y, 13) * T), 5 + Art.Hash(x, y, 14) * 7, new Color(0.05f, 0.05f, 0.06f, 0.25f));
                if (Art.Hash(x, y, 15) > 0.88f) ci.DrawPolyline(new[] { o + new Vector2(Art.Hash(x, y, 16) * T, 0), o + new Vector2(T * 0.5f, T * 0.45f), o + new Vector2(Art.Hash(x, y, 17) * T, T) }, fc.Darkened(0.35f), 1);
                if (Art.Hash(x, y, 18) > 0.985f) Drain(ci, o, fc);
                break;
            case Floor.LabTile:
                ci.DrawRect(new Rect2(o, new Vector2(T, T)), fc.Darkened(0.12f), false, 1);
                ci.DrawLine(o + new Vector2(T / 2f, 0), o + new Vector2(T / 2f, T), fc.Darkened(0.06f), 1);
                ci.DrawLine(o + new Vector2(0, T / 2f), o + new Vector2(T, T / 2f), fc.Darkened(0.06f), 1);
                if (Art.Hash(x, y, 19) > 0.96f) Drain(ci, o, fc);
                break;
            case Floor.Wood:
                // Planks with their ends staggered row to row, and the odd knot.
                for (int k = 0; k < 4; k++)
                {
                    ci.DrawLine(o + new Vector2(0, k * 8), o + new Vector2(T, k * 8), fc.Darkened(0.3f), 1);
                    float end = ((x * 7 + (y * 4 + k) * 13) % 4) * 8 + 4;
                    ci.DrawLine(o + new Vector2(end, k * 8), o + new Vector2(end, k * 8 + 8), fc.Darkened(0.3f), 1);
                    if (Art.Hash(x, y, k + 30) > 0.9f) ci.DrawCircle(o + new Vector2(Art.Hash(x, y, k + 40) * T, k * 8 + 4), 1.3f, fc.Darkened(0.4f));
                }
                break;
            case Floor.Ritual:
                // Chalk dust; the circle itself is drawn over the floor by the run view (RunView.TileLife).
                if (Art.Hash(x, y, 95) > 0.5f) ci.DrawLine(o + new Vector2(Art.Hash(x, y, 97) * T, 6), o + new Vector2(Art.Hash(x, y, 98) * T, T - 6), new Color(0.85f, 0.85f, 0.9f, 0.18f), 1);
                break;
            case Floor.Dance:
                if (Art.Hash(x, y, 3) > 0.7f)
                {
                    Color glow = Art.Hash(x, y, 4) > 0.5f ? UITheme.Magenta : UITheme.Cyan;
                    ci.DrawRect(new Rect2(o + new Vector2(2, 2), new Vector2(T - 4, T - 4)), glow with { A = 0.25f });
                }
                break;
            case Floor.Street or Floor.Yard:
                if (Art.Hash(x, y, 5) > 0.85f) ci.DrawLine(o + new Vector2(Art.Hash(x, y, 6) * T, 0), o + new Vector2(Art.Hash(x, y, 7) * T, T), fc.Darkened(0.4f), 1);
                // Puddles catching the neon, and the city's litter.
                if (Art.Hash(x, y, 8) > 0.95f)
                {
                    // A shallow puddle: flat, faint, a little neon in it.
                    ci.DrawSetTransform(o + new Vector2(16, 16), Art.Hash(x, y, 9) * 3, new Vector2(1.8f, 0.7f));
                    ci.DrawCircle(Vector2.Zero, 6 + Art.Hash(x, y, 9) * 5, new Color(0.2f, 0.28f, 0.38f, 0.14f));
                    ci.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                }
                if (Art.Hash(x, y, 20) > 0.95f)
                {
                    Vector2 lp = o + new Vector2(Art.Hash(x, y, 21) * (T - 6), Art.Hash(x, y, 22) * (T - 6));
                    ci.DrawRect(new Rect2(lp, new Vector2(5, 3)), Art.Hash(x, y, 23) > 0.5f ? new Color(0.7f, 0.68f, 0.6f, 0.5f) : new Color(0.8f, 0.2f, 0.25f, 0.5f));
                }
                break;
            case Floor.Carpet:
                // A woven diamond, worn paler where people walk.
                if ((x + y) % 2 == 0) ci.DrawColoredPolygon(new[] { o + new Vector2(T / 2f, 6), o + new Vector2(T - 6, T / 2f), o + new Vector2(T / 2f, T - 6), o + new Vector2(6, T / 2f) }, fc.Lightened(0.04f));
                if (Art.Hash(x, y, 24) > 0.9f) Blot(ci, o + new Vector2(T / 2f, T / 2f), 8, fc.Lightened(0.06f) with { A = 0.5f });
                break;
        }
    }

    /// <summary>An irregular stain: three overlapping circles.</summary>
    private static void Blot(CanvasItem ci, Vector2 c, float r, Color col)
    {
        ci.DrawCircle(c, r, col);
        ci.DrawCircle(c + new Vector2(r * 0.6f, r * 0.2f), r * 0.6f, col);
        ci.DrawCircle(c + new Vector2(-r * 0.4f, r * 0.5f), r * 0.5f, col);
    }

    private static void Drain(CanvasItem ci, Vector2 o, Color fc)
    {
        var g = new Rect2(o + new Vector2(9, 9), new Vector2(14, 14));
        ci.DrawRect(g, new Color(0.06f, 0.06f, 0.07f));
        for (int k = 0; k < 4; k++) ci.DrawLine(g.Position + new Vector2(2, 2 + k * 3.3f), g.Position + new Vector2(12, 2 + k * 3.3f), fc.Darkened(0.2f), 1.5f);
        ci.DrawRect(g, fc.Darkened(0.4f), false, 1);
    }

    private static void DrawWall(CanvasItem ci, Level L, int x, int y, Tile t, Vector2 o, float hp)
    {
        Color c = WallColour(t).Darkened((1 - hp) * 0.35f);
        ci.DrawRect(new Rect2(o, new Vector2(T, T)), c);
        if (t == Tile.Brick)
        {
            // Every brick its own shade, the odd one chipped dark.
            for (int row = 0; row < 4; row++)
            {
                float yy = row * 8;
                float shift = (row + y) % 2 == 0 ? 0 : -8;
                for (float xx = shift; xx < T; xx += 16)
                {
                    float hv = Art.Hash(x * 3 + (int)(xx + 8) / 16, y * 4 + row, 51);
                    Color bc = c.Lightened((hv - 0.5f) * 0.18f);
                    if (hv > 0.94f) bc = c.Darkened(0.35f);
                    float x0 = MathF.Max(0, xx), x1 = MathF.Min(T, xx + 16);
                    ci.DrawRect(new Rect2(o + new Vector2(x0 + 0.5f, yy + 0.5f), new Vector2(x1 - x0 - 1, 7)), bc);
                }
                ci.DrawLine(o + new Vector2(0, yy), o + new Vector2(T, yy), c.Darkened(0.35f), 1);
            }
        }
        else if (t == Tile.Reinforced)
        {
            // Armour plate: seams, a row of rivets top and bottom.
            ci.DrawRect(new Rect2(o + new Vector2(3, 3), new Vector2(T - 6, T - 6)), c.Lightened(0.06f), false, 1);
            for (int k = 0; k < 3; k++)
            {
                ci.DrawCircle(o + new Vector2(6 + k * 10, 6), 1.4f, c.Lightened(0.3f));
                ci.DrawCircle(o + new Vector2(6 + k * 10, T - 6), 1.4f, c.Lightened(0.3f));
            }
            if (x % 2 == 0) ci.DrawLine(o + new Vector2(T - 0.5f, 3), o + new Vector2(T - 0.5f, T - 3), c.Darkened(0.4f), 1);
        }
        else if (t == Tile.Concrete)
        {
            // Poured concrete: the form-tie holes in a grid, and rain streaks.
            for (int k = 0; k < 4; k++) ci.DrawCircle(o + new Vector2(8 + (k % 2) * 16, 8 + (k / 2) * 16), 1.3f, c.Darkened(0.25f));
            if (Art.Hash(x, y, 2) > 0.6f) ci.DrawRect(new Rect2(o + new Vector2(4 + Art.Hash(x, y, 3) * 20, 0), new Vector2(3, T)), c.Darkened(0.08f));
        }
        else if (t == Tile.Drywall)
        {
            // Plasterboard: a seam every other tile, scuffs, and here and there a notice pinned up.
            if (x % 2 == 0) ci.DrawLine(o + new Vector2(T - 0.5f, 0), o + new Vector2(T - 0.5f, T), c.Darkened(0.12f), 1);
            if (Art.Hash(x, y, 4) > 0.85f) ci.DrawLine(o + new Vector2(6, 20), o + new Vector2(14, 22), c.Darkened(0.2f), 1);
            if (Art.Hash(x, y, 5) > 0.93f)
            {
                var n = new Rect2(o + new Vector2(9, 6), new Vector2(12, 14));
                ci.DrawRect(n, new Color(0.9f, 0.88f, 0.8f));
                for (int k = 0; k < 3; k++) ci.DrawLine(n.Position + new Vector2(2, 3 + k * 3.5f), n.Position + new Vector2(10, 3 + k * 3.5f), new Color(0.4f, 0.4f, 0.4f), 1);
                ci.DrawCircle(n.Position + new Vector2(6, 1), 1.2f, new Color(0.9f, 0.2f, 0.2f));
            }
        }
        // Bevels: lit top edge, shaded front face where the wall meets floor.
        if (!IsWall(L[x, y - 1]) && L[x, y - 1] != Tile.Glass) ci.DrawLine(o + new Vector2(0, 1), o + new Vector2(T, 1), c.Lightened(0.3f), 2);
        if (!IsWall(L[x, y + 1]) && L[x, y + 1] != Tile.Glass) ci.DrawRect(new Rect2(o + new Vector2(0, T - 7), new Vector2(T, 7)), c.Darkened(0.45f));
        if (!IsWall(L[x - 1, y])) ci.DrawLine(o + new Vector2(1, 0), o + new Vector2(1, T), c.Lightened(0.15f), 1);
        if (!IsWall(L[x + 1, y])) ci.DrawLine(o + new Vector2(T - 1, 0), o + new Vector2(T - 1, T), c.Darkened(0.3f), 2);
        if (hp < 0.65f)
        {
            Color crack = c.Darkened(0.6f);
            ci.DrawLine(o + new Vector2(6, 4), o + new Vector2(16, 14), crack, 1.5f);
            ci.DrawLine(o + new Vector2(16, 14), o + new Vector2(13, 26), crack, 1.5f);
            if (hp < 0.35f) ci.DrawLine(o + new Vector2(16, 14), o + new Vector2(28, 18), crack, 1.5f);
        }
    }

    private static void DrawGlassPane(CanvasItem ci, Level L, int x, int y, Vector2 o)
    {
        bool horiz = L[x - 1, y] is not Tile.Floor || L[x + 1, y] is not Tile.Floor;
        if (L[x, y - 1] is Tile.Glass or Tile.Drywall or Tile.Brick or Tile.Concrete or Tile.Reinforced) horiz = false;
        if (L[x - 1, y] is Tile.Glass or Tile.Drywall or Tile.Brick or Tile.Concrete or Tile.Reinforced) horiz = true;
        Rect2 pane = horiz ? new Rect2(o + new Vector2(0, T / 2f - 4), new Vector2(T, 8)) : new Rect2(o + new Vector2(T / 2f - 4, 0), new Vector2(8, T));
        ci.DrawRect(pane, new Color(0.5f, 0.9f, 1f, 0.35f));
        ci.DrawRect(pane, new Color(0.7f, 0.95f, 1f, 0.8f), false, 1);
        ci.DrawLine(pane.Position + new Vector2(3, 3), pane.Position + new Vector2(horiz ? 9 : 5, horiz ? 5 : 11), new Color(1, 1, 1, 0.7f), 1);
    }

    private static void DrawDoor(CanvasItem ci, Level L, int x, int y, Tile t, Vector2 o, float hp)
    {
        // Which way does the wall run?
        bool wallH = IsWall(L[x - 1, y]) || IsWall(L[x + 1, y]) || L[x - 1, y] is Tile.Door or Tile.DoorOpen || L[x + 1, y] is Tile.Door or Tile.DoorOpen;
        Color frame = new(0.25f, 0.27f, 0.3f);
        if (wallH)
        {
            ci.DrawRect(new Rect2(o + new Vector2(0, T / 2f - 5), new Vector2(3, 10)), frame);
            ci.DrawRect(new Rect2(o + new Vector2(T - 3, T / 2f - 5), new Vector2(3, 10)), frame);
        }
        else
        {
            ci.DrawRect(new Rect2(o + new Vector2(T / 2f - 5, 0), new Vector2(10, 3)), frame);
            ci.DrawRect(new Rect2(o + new Vector2(T / 2f - 5, T - 3), new Vector2(10, 3)), frame);
        }
        if (t == Tile.DoorOpen)
        {
            // Swung open against the frame.
            Color leaf = new(0.42f, 0.3f, 0.2f);
            if (wallH) ci.DrawLine(o + new Vector2(3, T / 2f), o + new Vector2(5, T / 2f + T * 0.8f), leaf, 4);
            else ci.DrawLine(o + new Vector2(T / 2f, 3), o + new Vector2(T / 2f + T * 0.8f, 5), leaf, 4);
            return;
        }
        bool locked = t == Tile.LockedDoor;
        Color panel = (locked ? new Color(0.3f, 0.32f, 0.36f) : new Color(0.5f, 0.36f, 0.22f)).Darkened((1 - hp) * 0.4f);
        Rect2 p = wallH ? new Rect2(o + new Vector2(2, T / 2f - 5), new Vector2(T - 4, 10)) : new Rect2(o + new Vector2(T / 2f - 5, 2), new Vector2(10, T - 4));
        ci.DrawRect(p, panel);
        ci.DrawRect(p, panel.Darkened(0.4f), false, 1.5f);
        Vector2 mid = o + new Vector2(T / 2f, T / 2f);
        if (locked)
        {
            // A maglock: a keypad with its red light, and the bolt housing.
            ci.DrawRect(new Rect2(mid - new Vector2(4, 3.5f), new Vector2(8, 7)), new Color(0.08f, 0.08f, 0.1f));
            ci.DrawCircle(mid + new Vector2(0, -1), 1.6f, new Color(1f, 0.15f, 0.1f));
            ci.DrawRect(new Rect2(mid + new Vector2(-2.5f, 1.5f), new Vector2(5, 1)), new Color(0.3f, 0.3f, 0.35f));
        }
        else
        {
            // The handle, near the latch end.
            Vector2 hnd = wallH ? o + new Vector2(T - 8, T / 2f) : o + new Vector2(T / 2f, T - 8);
            ci.DrawCircle(hnd, 1.8f, new Color(0.75f, 0.7f, 0.5f));
        }
    }
}
