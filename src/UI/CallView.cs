using System;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>
/// The commlink's call screen: while it rings, the caller ID and the rings
/// going out; once answered, the caller live on video -- their face with
/// scanlines and the odd glitch -- or, for people who would rather not be
/// seen (Null, Johnsons, a dragon, a number you don't know), static and NO
/// VIDEO, with their voice as a level meter.
/// </summary>
public partial class CallView : Control
{
    /// <summary>Who is on the line, as the commlink shows them.</summary>
    public sealed record Caller(string Name, string Handle, string Picture, Color Tint, string Hidden, string[] Faces = null);

    public Caller Who;
    public bool Ringing = true;
    /// <summary>Talking (the voice is playing, or the words are still typing): the level meter moves.</summary>
    public Func<bool> Talking;
    private float _t, _live;

    public CallView() { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(600, 300); ClipContents = true; }

    /// <summary>Contacts, crew and the story's faces call on video; the rest call without it.</summary>
    public static Caller Lookup(string from)
    {
        from ??= "";
        ContactDef k = Tables.Contacts.FirstOrDefault(x => x.Name == from);
        if (k != null)
            return k.Id == "decker"
                ? new Caller(k.Name, "NODE 0x4E554C4C", null, Art.C(k.Colour), "null never shows a face. A cursor blinks where one would be.")
                : new Caller(k.Name, k.Role.ToUpperInvariant(), Assets.ContactPicture(k), Art.C(k.Colour), null);
        var hire = Tables.Hires.FirstOrDefault(h => h.name == from);
        if (hire.id != null) return new Caller(hire.name, "CREW", Assets.HirePicture(hire.id), UITheme.Cyan, null);
        if (Career.Johnsons.Any(j => j.Name == from)) return new Caller(from, "MR. JOHNSON", null, UITheme.Dim, "A Johnson never shows a face. That's what Johnsons are for.");
        string pic = from switch { "Dr. Amon Pryce" => "caller-pryce", "Ilse Pryce" => "caller-ilse", "Kade Marrow" => "caller-kade", _ => null };
        if (pic != null) return new Caller(from, from == "Kade Marrow" ? "ASH VIPERS" : "PRIVATE", pic, from == "Kade Marrow" ? UITheme.Good : UITheme.Cyan, null);
        // Something that isn't anyone: a different borrowed face every few seconds.
        if (from == "The Lamplighter")
        {
            string[] faces = Tables.Contacts.Select(Assets.ContactPicture).Concat(Tables.Hires.Select(h => Assets.HirePicture(h.id))).Concat(new[] { "caller-ilse", "caller-pryce" }).ToArray();
            return new Caller(from, "CALLER ID: 000-000-0000", faces[0], UITheme.Term, null, faces);
        }
        if (from == "Registrar Hale") return new Caller(from, "KESSLER BAY AI REGISTRY", null, UITheme.Dim, "The Registry doesn't do video. The Registry doesn't do anything you can prove.");
        if (from == "Veyrasz") return new Caller("NO CALLER ID", "ROUTED THROUGH 41 COUNTRIES", null, UITheme.Amber, "The screen stays dark. Something on the other end is very large, and very old.");
        return new Caller(from.Length > 0 ? from : "UNKNOWN", "NUMBER WITHHELD", null, UITheme.Dim, "Video withheld by the caller.");
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _t += dt;
        if (!Ringing) _live += dt;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 s = Size;
        var screen = new Rect2(Vector2.Zero, s);
        DrawRect(screen, new Color(0.012f, 0.016f, 0.03f));
        if (Ringing) DrawRinging(s);
        else if (Who.Faces != null && _live > 0.45f && Assets.Picture(Who.Faces[(int)(_live / 2.6f) % Who.Faces.Length]) is Texture2D face)
        {
            // A new face every 2.6 s, torn across as it switches.
            DrawVideo(s, face);
            if (_live % 2.6f < 0.18f)
                for (int k = 0; k < 6; k++) DrawRect(new Rect2(0, Art.Hash((int)(_live * 40), k) * s.Y, s.X, 4 + 8 * Art.Hash(k, (int)(_live * 40))), Who.Tint with { A = 0.35f });
        }
        else if (Who.Picture != null && Assets.Picture(Who.Picture) is Texture2D pic && _live > 0.45f) DrawVideo(s, pic);
        else DrawStatic(s, _live <= 0.45f && Who.Picture != null);
        // The glass over everything.
        for (float y = 0; y < s.Y; y += 3) DrawLine(new Vector2(0, y), new Vector2(s.X, y), new Color(0, 0, 0, 0.18f), 1);
        DrawRect(screen, Who.Tint with { A = 0.9f }, false, 2);
    }

    private void DrawRinging(Vector2 s)
    {
        Vector2 c = new(s.X / 2, s.Y * 0.42f);
        // Rings going out, two a second.
        for (int k = 0; k < 3; k++)
        {
            float u = (_t * 0.9f + k / 3f) % 1f;
            DrawArc(c, 40 + u * 110, 0, MathF.Tau, 48, Who.Tint with { A = 0.5f * (1 - u) }, 2);
        }
        // The caller's picture in a small round frame -- or a question mark.
        float r = 40;
        if (Who.Picture != null && Assets.Picture(Who.Picture) is Texture2D pic)
        {
            var box = new Rect2(c - new Vector2(r, r), new Vector2(r * 2, r * 2));
            Portrait.DrawCover(this, pic, box, 0.3f);
            // Round it off with the screen colour.
            for (int k = 0; k < 12; k++) DrawArc(c, r + 1 + k * 1.6f, 0, MathF.Tau, 40, new Color(0.012f, 0.016f, 0.03f), 2.2f);
        }
        else
        {
            DrawCircle(c, r, new Color(0.05f, 0.06f, 0.09f));
            UITheme.Write(this, c + new Vector2(-r, 16), "?", 44, Who.Tint, HorizontalAlignment.Center, r * 2, heading: true);
        }
        DrawArc(c, r, 0, MathF.Tau, 40, Who.Tint, 2);
        bool on = (_t % 1.1f) < 0.8f;
        UITheme.Write(this, new Vector2(0, s.Y * 0.42f + 78), "INCOMING CALL", 15, (on ? Who.Tint : UITheme.Faint), HorizontalAlignment.Center, s.X, heading: true);
        UITheme.Write(this, new Vector2(0, s.Y * 0.42f + 106), Who.Name.ToUpperInvariant(), 24, UITheme.Text, HorizontalAlignment.Center, s.X, heading: true);
        UITheme.Write(this, new Vector2(0, s.Y * 0.42f + 128), Who.Handle, 12, UITheme.Dim, HorizontalAlignment.Center, s.X, mono: true);
    }

    private void DrawVideo(Vector2 s, Texture2D pic)
    {
        // A face, filling the frame, breathing very slightly.
        float z = 1.02f + 0.01f * MathF.Sin(_t * 0.7f);
        var to = new Rect2((s - s * z) / 2, s * z);
        Portrait.DrawCover(this, pic, to, 0.25f);
        // Bandwidth: now and then a band of the picture tears sideways.
        float g = (_t * 0.37f) % 1f;
        if (g < 0.04f)
        {
            float y = Art.Hash((int)(_t * 10), 3) * s.Y;
            DrawRect(new Rect2(0, y, s.X, 6 + 10 * Art.Hash((int)(_t * 10), 5)), Who.Tint with { A = 0.18f });
        }
        Overlay(s);
    }

    private void DrawStatic(Vector2 s, bool connecting)
    {
        // Snow: a fresh handful of grey cells every frame.
        int seed = (int)(_t * 30);
        for (int k = 0; k < 420; k++)
        {
            float x = Art.Hash(seed, k) * s.X, y = Art.Hash(seed, k + 999) * s.Y;
            float v = Art.Hash(seed, k + 3333);
            DrawRect(new Rect2(x, y, 3 + v * 5, 2), new Color(v, v, v, 0.12f + 0.12f * v));
        }
        if (connecting) { UITheme.Write(this, new Vector2(0, s.Y / 2 + 8), "CONNECTING...", 18, Who.Tint, HorizontalAlignment.Center, s.X, heading: true); return; }
        UITheme.Write(this, new Vector2(0, s.Y * 0.36f), "NO VIDEO", 30, Who.Tint, HorizontalAlignment.Center, s.X, heading: true);
        UITheme.Write(this, new Vector2(30, s.Y * 0.36f + 26), Who.Hidden ?? "", 13, UITheme.Dim, HorizontalAlignment.Center, s.X - 60);
        // The voice, as a level meter.
        bool talking = Talking?.Invoke() == true;
        for (int k = 0; k < 32; k++)
        {
            float h = talking ? 4 + 34 * MathF.Abs(MathF.Sin(_t * (7 + k % 5) + k * 1.7f)) * Art.Hash(k, (int)(_t * 8)) : 3;
            DrawRect(new Rect2(s.X / 2 - 32 * 6 + k * 12, s.Y * 0.72f - h / 2, 8, h), Who.Tint with { A = 0.75f });
        }
        Overlay(s);
    }

    /// <summary>Who and how long: the call's own furniture.</summary>
    private void Overlay(Vector2 s)
    {
        bool rec = (_t % 1.4f) < 0.9f;
        if (rec) DrawCircle(new Vector2(20, 20), 5, UITheme.Bad);
        UITheme.Write(this, new Vector2(32, 25), $"LIVE  {(int)_live / 60:00}:{(int)_live % 60:00}", 13, UITheme.Text, mono: true);
        DrawRect(new Rect2(0, s.Y - 34, s.X, 34), new Color(0, 0, 0, 0.6f));
        UITheme.Write(this, new Vector2(14, s.Y - 12), Who.Name.ToUpperInvariant(), 16, Who.Tint, heading: true);
        UITheme.Write(this, new Vector2(s.X - 314, s.Y - 12), Who.Handle, 12, UITheme.Dim, HorizontalAlignment.Right, 300, mono: true);
    }
}
