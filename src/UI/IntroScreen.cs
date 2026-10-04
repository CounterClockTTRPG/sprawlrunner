using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>
/// The intro a new career opens with: painted backdrops drifting slowly (the
/// first one a short film when it has been generated), Juno's voice over
/// them, her words typed out underneath. Space, Enter or a click moves on;
/// Esc skips the lot. Every picture, clip and line is optional: missing ones
/// fall back to a dark gradient and silence.
/// </summary>
public partial class IntroScreen : Control
{
    private readonly IntroCard[] _cards;
    private readonly Action _done;
    private int _at = -1;
    private float _t, _fade, _wait;
    private Texture2D _pic;
    private VideoStreamPlayer _film;
    private Label _heading, _text;
    private Shade _shade;

    /// <summary>The dark band under the words and the key hint: above the film, below the words.</summary>
    private sealed partial class Shade : Control
    {
        public IntroScreen Of;

        public override void _Draw()
        {
            Vector2 size = GetViewportRect().Size;
            for (int k = 0; k < 24; k++)
                DrawRect(new Rect2(0, size.Y - 330 + k * 14, size.X, 14), new Color(0, 0, 0, MathF.Min(0.82f, k / 18f)));
            UITheme.Write(this, new Vector2(0, size.Y - 24), $"{Of._at + 1} / {Of._cards.Length}     Space, Enter or click: next     Esc: skip", 13, UITheme.Faint, HorizontalAlignment.Center, size.X);
        }
    }
    private bool _leaving;

    /// <summary>Seconds a card waits after its line (or its text) is done before moving on by itself.</summary>
    private const float Linger = 2.5f, CharsPerSecond = 55;

    public IntroScreen(IEnumerable<IntroCard> cards, Action done)
    {
        _cards = cards.ToArray();
        _done = done;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Ready()
    {
        _heading = UITheme.Label("", 30, UITheme.Amber, heading: true);
        _text = UITheme.Label("", 20, UITheme.Text);
        _text.VisibleCharacters = 0;
        _shade = new Shade { Of = this, MouseFilter = MouseFilterEnum.Ignore };
        _shade.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_shade);
        AddChild(_heading);
        AddChild(_text);
        Next();
    }

    private void Next()
    {
        _at++;
        if (_at >= _cards.Length) { Finish(); return; }
        IntroCard c = _cards[_at];
        _t = 0;
        _fade = 0;
        _wait = 0;
        _pic = Assets.Picture(c.Picture);
        _heading.Text = c.Heading;
        _text.Text = c.Text;
        _text.VisibleCharacters = 0;
        _film?.QueueFree();
        _film = null;
        // The first card opens on film, when there is one (it holds its last frame; without it, the still drifts).
        string clip = Assets.Clip(c.Picture + "-move");
        if (clip != null && !AgentRunner.Active)
        {
            _film = new VideoStreamPlayer { Stream = new VideoStreamTheora { File = clip }, Autoplay = true, Expand = true, MouseFilter = MouseFilterEnum.Ignore };
            _film.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(_film);
            MoveChild(_film, 0);
        }
        Sfx.Instance?.Voice(Assets.Voice("say-" + c.Picture));
    }

    private void Finish()
    {
        if (_leaving) return;
        _leaving = true;
        Sfx.Instance?.Voice(null);
        _done();
        QueueFree();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true }) Advance();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (AgentRunner.Active && e.Device != Controls.AgentDevice) return;
        GetViewport().SetInputAsHandled();
        if (Controls.Is(e, Act.Pause)) Finish();
        else if (k.Keycode is Key.Space or Key.Enter or Key.KpEnter || Controls.Is(e, Act.Interact)) Advance();
    }

    /// <summary>First press finishes the typing; the next moves on.</summary>
    private void Advance()
    {
        if (_text.VisibleCharacters >= 0 && _text.VisibleCharacters < _text.Text.Length) { _text.VisibleCharacters = -1; return; }
        Next();
    }

    public override void _Process(double delta)
    {
        if (_at < 0 || _at >= _cards.Length) return;
        float dt = (float)delta;
        _t += dt;
        _fade = MathF.Min(1, _fade + dt / 0.8f);
        if (_text.VisibleCharacters >= 0)
        {
            int n = (int)(_t * CharsPerSecond);
            _text.VisibleCharacters = n >= _text.Text.Length ? -1 : n;
        }
        // Moves on by itself once the line has been said (and read).
        bool speaking = Sfx.Instance?.Speaking == true;
        if (_text.VisibleCharacters < 0 && !speaking) _wait += dt;
        if (_wait > Linger + _text.Text.Length / 60f) Next();
        Vector2 size = GetViewportRect().Size;
        float w = MathF.Min(1100, size.X - 120);
        _heading.Position = new Vector2((size.X - w) / 2, size.Y - 250);
        _heading.Size = new Vector2(w, 40);
        _text.Position = new Vector2((size.X - w) / 2, size.Y - 200);
        _text.Size = new Vector2(w, 160);
        QueueRedraw();
        _shade.QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.02f, 0.02f, 0.03f));
        if (_pic != null && (_film == null || !_film.Visible))
        {
            // Cover the screen, then drift: a slow push in and a slight slide, a different way each card.
            Vector2 ts = _pic.GetSize();
            float cover = MathF.Max(size.X / ts.X, size.Y / ts.Y);
            float zoom = cover * (1.04f + 0.0045f * _t);
            Vector2 drawn = ts * zoom;
            float dir = _at % 2 == 0 ? 1 : -1;
            Vector2 at = (size - drawn) / 2 + new Vector2(dir * (_t * 4 - 20), -_t * 1.5f);
            DrawTextureRect(_pic, new Rect2(at, drawn), false, new Color(1, 1, 1, _fade));
        }
    }
}
