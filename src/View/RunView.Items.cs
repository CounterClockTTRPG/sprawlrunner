using System;
using Godot;

namespace Sprawl;

/// <summary>
/// Things worth carrying out, drawn as what they are: a chip, a stack of papers, a commlink, a cyberhand, an
/// optic, a credstick, a case, a drone core, a dragon's scale -- picked by the item's name, a gem when nothing
/// fits. Loot carries a gold glow and a glint so it still reads as loot at a glance.
/// </summary>
public partial class RunView
{
    /// <summary>The warm gold every valuable glows (cash keeps its own yellow).</summary>
    private static readonly Color LootGold = new(1f, 0.72f, 0.28f);

    private partial class Entities
    {
        /// <summary>The kind of thing a name is.</summary>
        private enum ItemShape { Chip, Papers, Commlink, Hand, Optic, Credstick, Case, Core, Scale, Gem }

        private static ItemShape ShapeOf(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("chip") || n.Contains("btl")) return ItemShape.Chip;
            if (n.Contains("ledger") || n.Contains("bond") || n.Contains("scrip") || n.Contains("manifest") || n.Contains("photograph") || n.Contains("file") || n.Contains("contract")) return ItemShape.Papers;
            if (n.Contains("commlink") || n.Contains("recording")) return ItemShape.Commlink;
            if (n.Contains("hand") || n.Contains("arm")) return ItemShape.Hand;
            if (n.Contains("optic") || n.Contains("eye")) return ItemShape.Optic;
            if (n.Contains("credstick")) return ItemShape.Credstick;
            if (n.Contains("case") || n.Contains("kit") || n.Contains("package")) return ItemShape.Case;
            if (n.Contains("core") || n.Contains("drone")) return ItemShape.Core;
            if (n.Contains("scale")) return ItemShape.Scale;
            return ItemShape.Gem;
        }

        /// <summary>The thing itself, about a tile across, centred on <paramref name="p"/>.</summary>
        private void DrawItem(Vector2 p, string name)
        {
            var shadow = new Color(0, 0, 0, 0.4f);
            switch (ShapeOf(name))
            {
                case ItemShape.Chip:
                {
                    // A circuit board: green, gold pins down both sides, a black die in the middle.
                    var b = new Rect2(p + new Vector2(-6, -5), new Vector2(12, 10));
                    DrawRect(new Rect2(b.Position + new Vector2(1.5f, 2), b.Size), shadow);
                    for (int k = 0; k < 4; k++)
                    {
                        DrawRect(new Rect2(p + new Vector2(-8, -4 + k * 2.6f), new Vector2(2, 1.4f)), new Color(0.95f, 0.78f, 0.3f));
                        DrawRect(new Rect2(p + new Vector2(6, -4 + k * 2.6f), new Vector2(2, 1.4f)), new Color(0.95f, 0.78f, 0.3f));
                    }
                    DrawRect(b, new Color(0.12f, 0.42f, 0.24f));
                    DrawRect(new Rect2(p + new Vector2(-3, -2.5f), new Vector2(6, 5)), new Color(0.08f, 0.08f, 0.1f));
                    DrawLine(p + new Vector2(-5, 3.5f), p + new Vector2(-1, 3.5f), new Color(0.6f, 0.9f, 0.6f, 0.7f), 1);
                    break;
                }
                case ItemShape.Papers:
                {
                    // A bound stack: three sheets fanned, ruled lines, a red band across.
                    for (int k = 2; k >= 0; k--)
                    {
                        var s = new Rect2(p + new Vector2(-6 + k * 1.2f, -7 + k * 1.2f), new Vector2(11, 13));
                        if (k == 2) DrawRect(new Rect2(s.Position + new Vector2(1.5f, 2), s.Size), shadow);
                        DrawRect(s, new Color(0.92f, 0.88f, 0.76f).Darkened(k * 0.08f));
                    }
                    for (int k = 0; k < 4; k++) DrawLine(p + new Vector2(-4, -4 + k * 2.6f), p + new Vector2(3, -4 + k * 2.6f), new Color(0.35f, 0.32f, 0.3f, 0.7f), 1);
                    DrawRect(new Rect2(p + new Vector2(-6, 0), new Vector2(11, 2)), new Color(0.75f, 0.15f, 0.12f));
                    break;
                }
                case ItemShape.Commlink:
                {
                    // A slab with a lit screen.
                    var b = new Rect2(p + new Vector2(-4.5f, -7), new Vector2(9, 14));
                    DrawRect(new Rect2(b.Position + new Vector2(1.5f, 2), b.Size), shadow);
                    DrawRect(b, name.Contains("gold") ? new Color(0.85f, 0.65f, 0.25f) : new Color(0.16f, 0.17f, 0.2f));
                    DrawRect(new Rect2(p + new Vector2(-3, -5), new Vector2(6, 8)), new Color(0.3f, 0.85f, 1f, 0.9f));
                    DrawCircle(p + new Vector2(0, 5), 1, new Color(0.6f, 0.6f, 0.65f));
                    break;
                }
                case ItemShape.Hand:
                {
                    // A chrome hand: the palm, four fingers and a thumb, a seam of light at the wrist.
                    Color metal = name.Contains("deco") ? new Color(0.88f, 0.7f, 0.35f) : new Color(0.72f, 0.74f, 0.78f);
                    DrawCircle(p + new Vector2(1.5f, 3), 5.5f, shadow);
                    DrawRect(new Rect2(p + new Vector2(-4, -1), new Vector2(8, 7)), metal);
                    for (int k = 0; k < 4; k++) DrawRect(new Rect2(p + new Vector2(-4 + k * 2.1f, -7), new Vector2(1.6f, 6.5f)), metal.Darkened(k % 2 * 0.12f));
                    DrawRect(new Rect2(p + new Vector2(4, 0), new Vector2(3, 1.8f)), metal);
                    DrawRect(new Rect2(p + new Vector2(-4, 6), new Vector2(8, 1.5f)), new Color(0.3f, 0.85f, 1f));
                    break;
                }
                case ItemShape.Optic:
                {
                    // An eye in a chrome socket: iris, pupil, a catch of light.
                    DrawCircle(p + new Vector2(1.5f, 2), 6.5f, shadow);
                    DrawCircle(p, 6.5f, new Color(0.7f, 0.72f, 0.76f));
                    DrawCircle(p, 4.5f, new Color(0.95f, 0.95f, 0.92f));
                    DrawCircle(p, 2.8f, new Color(0.9f, 0.25f, 0.2f));
                    DrawCircle(p, 1.3f, new Color(0.05f, 0.05f, 0.06f));
                    DrawCircle(p + new Vector2(-1.5f, -1.5f), 0.9f, Colors.White);
                    break;
                }
                case ItemShape.Credstick:
                {
                    // A slim gold bar with a contact strip.
                    DrawRect(new Rect2(p + new Vector2(-6.5f, -1), new Vector2(13, 5)), shadow);
                    DrawRect(new Rect2(p + new Vector2(-7, -2.5f), new Vector2(13, 5)), new Color(0.95f, 0.75f, 0.25f));
                    DrawRect(new Rect2(p + new Vector2(3, -2.5f), new Vector2(3, 5)), new Color(0.35f, 0.3f, 0.25f));
                    DrawLine(p + new Vector2(-6, -1), p + new Vector2(2, -1), new Color(1f, 0.95f, 0.7f), 1);
                    break;
                }
                case ItemShape.Case:
                {
                    // A hard case: the body, its handle, two clasps.
                    bool sample = name.Contains("sample") || name.Contains("nanite");
                    Color body = sample ? new Color(0.82f, 0.84f, 0.86f) : new Color(0.32f, 0.2f, 0.12f);
                    var b = new Rect2(p + new Vector2(-7, -4), new Vector2(14, 10));
                    DrawRect(new Rect2(b.Position + new Vector2(1.5f, 2), b.Size), shadow);
                    DrawRect(b, body);
                    DrawRect(new Rect2(p + new Vector2(-3, -7), new Vector2(6, 3)), body.Darkened(0.3f), false, 1.5f);
                    DrawRect(new Rect2(p + new Vector2(-6, -2), new Vector2(2, 2)), new Color(0.85f, 0.8f, 0.6f));
                    DrawRect(new Rect2(p + new Vector2(4, -2), new Vector2(2, 2)), new Color(0.85f, 0.8f, 0.6f));
                    if (sample) DrawRect(new Rect2(p + new Vector2(-2, 1), new Vector2(4, 3)), new Color(0.9f, 0.25f, 0.25f));
                    break;
                }
                case ItemShape.Core:
                {
                    // A drone's core: a hexagon of dark casing round a glowing heart.
                    var hex = new Vector2[6];
                    for (int k = 0; k < 6; k++) hex[k] = p + new Vector2(MathF.Cos(k * MathF.Tau / 6), MathF.Sin(k * MathF.Tau / 6)) * 7;
                    DrawColoredPolygon(hex, new Color(0.2f, 0.22f, 0.26f));
                    DrawPolyline(new[] { hex[0], hex[1], hex[2], hex[3], hex[4], hex[5], hex[0] }, new Color(0.45f, 0.5f, 0.55f), 1);
                    DrawCircle(p, 3, new Color(1f, 0.45f, 0.15f));
                    break;
                }
                case ItemShape.Scale:
                {
                    // A dragon's scale: a teardrop of iridescent green-gold.
                    var pts = new[] { p + new Vector2(0, -8), p + new Vector2(6, -1), p + new Vector2(4, 6), p + new Vector2(0, 8), p + new Vector2(-4, 6), p + new Vector2(-6, -1) };
                    DrawColoredPolygon(pts, new Color(0.2f, 0.6f, 0.45f));
                    DrawLine(p + new Vector2(0, -6), p + new Vector2(0, 6), new Color(0.85f, 0.8f, 0.4f, 0.8f), 1);
                    break;
                }
                default:
                {
                    // A cut stone: a faceted diamond shape catching the light.
                    var top = p + new Vector2(0, -7);
                    var pts = new[] { top, p + new Vector2(6, -2), p + new Vector2(0, 7), p + new Vector2(-6, -2) };
                    DrawColoredPolygon(new[] { pts[0] + new Vector2(1.5f, 2), pts[1] + new Vector2(1.5f, 2), pts[2] + new Vector2(1.5f, 2), pts[3] + new Vector2(1.5f, 2) }, shadow);
                    DrawColoredPolygon(pts, new Color(0.55f, 0.85f, 1f));
                    DrawColoredPolygon(new[] { pts[0], pts[1], p + new Vector2(0, -2) }, new Color(0.85f, 0.97f, 1f));
                    DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[3], pts[0] }, new Color(0.3f, 0.55f, 0.8f), 1);
                    break;
                }
            }
        }

        /// <summary>Four gold corner ticks round a valuable: the steady mark that it is worth taking.</summary>
        private void LootBrackets(Vector2 p, Color c)
        {
            const float r = 11, l = 4;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    var corner = p + new Vector2(sx * r, sy * r);
                    DrawLine(corner, corner - new Vector2(sx * l, 0), c, 1.5f);
                    DrawLine(corner, corner - new Vector2(0, sy * l), c, 1.5f);
                }
        }

        /// <summary>A four-pointed glint that swells and fades, so a valuable catches the eye.</summary>
        private void Glint(Vector2 at, float t, Color c)
        {
            float s = MathF.Max(0, MathF.Sin(t * 2.2f)) * 4.5f;
            if (s < 0.4f) return;
            DrawLine(at - new Vector2(s, 0), at + new Vector2(s, 0), c, 1.5f);
            DrawLine(at - new Vector2(0, s), at + new Vector2(0, s), c, 1.5f);
            DrawCircle(at, 1.2f, Colors.White);
        }
    }
}
