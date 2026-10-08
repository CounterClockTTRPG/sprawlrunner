using System;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>
/// The briefing's map of the site: the building's outline (the whole layout
/// with schematics, and the rooms that have a purpose), the ways in numbered,
/// the way out, and what legwork revealed -- the patrols, the cameras.
/// </summary>
public partial class SitePlan : Control
{
    public LevelPlan Plan;
    public Job Job;
    public bool Schematics;
    /// <summary>The way in chosen (an entry id; null: the front).</summary>
    public string Chosen;

    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        if (Plan == null) return;
        Level L = Plan.L;
        Vector2 size = Size;
        float s = MathF.Min(size.X / L.W, size.Y / L.H);
        Vector2 o = (size - new Vector2(L.W, L.H) * s) / 2;
        Vector2 At(V2 p) => o + new Vector2(p.X, p.Y) * s;
        Rect2 Cell(int x, int y) => new(o + new Vector2(x, y) * s, new Vector2(s + 0.5f, s + 0.5f));
        UITheme.DrawPanel(this, new Rect2(Vector2.Zero, size));
        var line = new Color(0.55f, 0.75f, 0.85f);
        if (Schematics)
        {
            // The blueprint: walls, doors, glass; the rooms with a purpose named.
            for (int y = 0; y < L.H; y++)
            for (int x = 0; x < L.W; x++)
            {
                Tile t = L[x, y];
                if (t is Tile.Floor or Tile.Rubble || t == Tile.Reinforced && (x == 0 || y == 0 || x == L.W - 1 || y == L.H - 1)) continue;
                Color c = t switch
                {
                    Tile.Door or Tile.DoorOpen => new Color(0.9f, 0.7f, 0.3f),
                    Tile.LockedDoor => L.Keycards.Contains(L.Idx(x, y)) ? new Color(0.4f, 0.65f, 1f) : UITheme.Bad,
                    Tile.Glass => new Color(0.5f, 0.85f, 1f, 0.7f),
                    Tile.Drywall or Tile.Brick or Tile.Concrete or Tile.Reinforced => line with { A = 0.85f },
                    Tile.Stairs => UITheme.Yellow,
                    _ => line with { A = 0.25f },
                };
                DrawRect(Cell(x, y), c);
            }
            foreach (Room r in Plan.Rooms.Where(r => r.Kind != RoomKind.Plain))
                UITheme.Write(this, At(r.Centre) + new Vector2(-60, 4), SetPieces.Name(r.Kind).ToUpperInvariant(), 9, UITheme.Amber, HorizontalAlignment.Center, 120, heading: true);
            if (Plan.CleanRoom.W > 0) UITheme.Write(this, At(new V2(Plan.CleanRoom.X + Plan.CleanRoom.W / 2f, Plan.CleanRoom.Y + Plan.CleanRoom.H / 2f)) + new Vector2(-60, 4), "CLEAN ROOM", 9, UITheme.Cyan, HorizontalAlignment.Center, 120, heading: true);
        }
        else
        {
            // Without schematics: the building's footprint, and nothing of what's inside.
            foreach (Rect b in L.Buildings.Concat(Plan.Floors))
            {
                var r = new Rect2(At(new V2(b.X, b.Y)), new Vector2(b.W, b.H) * s);
                DrawRect(r, new Color(0.12f, 0.13f, 0.15f));
                for (float k = 6; k < r.Size.X + r.Size.Y; k += 9)
                    DrawLine(r.Position + new Vector2(MathF.Min(k, r.Size.X), MathF.Max(0, k - r.Size.X)), r.Position + new Vector2(MathF.Max(0, k - r.Size.Y), MathF.Min(k, r.Size.Y)), new Color(0.2f, 0.21f, 0.24f), 1);
                DrawRect(r, line, false, 1.5f);
            }
            UITheme.Write(this, new Vector2(0, size.Y / 2 + 6), "LAYOUT UNKNOWN", 13, UITheme.Faint, HorizontalAlignment.Center, size.X, heading: true);
        }
        // What the patrol routes showed you: everyone on the rota, and the cameras' eyes.
        if (Job.Legwork.Contains("routes"))
            foreach (Spawn e in Plan.Enemies)
            {
                Vector2 p = At(e.Pos);
                if (e.Kind == EnemyKind.Camera)
                {
                    float a = e.Facing;
                    DrawColoredPolygon(new[] { p, p + new Vector2(MathF.Cos(a - 0.6f), MathF.Sin(a - 0.6f)) * 6 * s, p + new Vector2(MathF.Cos(a + 0.6f), MathF.Sin(a + 0.6f)) * 6 * s }, UITheme.Bad with { A = 0.25f });
                    DrawCircle(p, 2, UITheme.Bad);
                    continue;
                }
                DrawCircle(p, e.Kind == EnemyKind.Boss ? 3.5f : 2.2f, e.Mood == Mood.Asleep ? UITheme.Dim : UITheme.Bad);
                for (int k = 0; k + 1 < e.Patrol.Count; k++) DrawLine(At(e.Patrol[k]), At(e.Patrol[k + 1]), UITheme.Bad with { A = 0.35f }, 1);
            }
        // The way out (and the dock's, when there is one).
        foreach (Rect ex in new[] { Plan.Extraction, Plan.BackDoor }.Where(x => x.W > 0))
            DrawRect(new Rect2(At(new V2(ex.X, ex.Y)), new Vector2(ex.W, ex.H) * s), UITheme.Good, false, 1.5f);
        // The ways in, numbered.
        for (int k = 0; k < Plan.Entries.Count; k++)
        {
            EntryPoint e = Plan.Entries[k];
            bool on = (Chosen ?? "front") == e.Id;
            Vector2 p = At(e.At.Centre);
            DrawCircle(p, 8, on ? UITheme.Cyan : new Color(0.08f, 0.08f, 0.1f));
            DrawArc(p, 8, 0, MathF.Tau, 20, on ? UITheme.Cyan : UITheme.Amber, 1.5f);
            UITheme.Write(this, p + new Vector2(-8, 4.5f), (k + 1).ToString(), 12, on ? new Color(0.02f, 0.05f, 0.08f) : UITheme.Amber, HorizontalAlignment.Center, 16, heading: true);
        }
    }
}
