using System;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>
/// A storylet in front of you: an incoming call, a scene where you walked in,
/// or a mail opened from the inbox. The choices are buttons (greyed out, with
/// the reason, when something is missing); taking one shows what came of it.
/// A call is a call: it rings (E or Enter answers, Esc calls back later),
/// then the caller is on video -- or on voice only, if they keep their face
/// to themselves -- with their words typed out underneath like subtitles.
/// </summary>
public partial class StoryModal : Modal
{
    private readonly Career _c;
    private readonly Mail _m;
    public Action Answered;
    private readonly bool _fresh, _phone;
    private bool _ringing;
    private float _ringT;
    private readonly VBoxContainer _rest;
    private CallView _view;
    private Label _words;

    /// <summary>Subtitles: characters a second.</summary>
    private const float Typing = 45;

    public StoryModal(Career c, Mail m) : base(m.Call ? "" : m.Place != null ? PlaceTitle(m.Place) : "MESSAGE", m.Call ? 660 : 640)
    {
        _c = c;
        _m = m;
        _fresh = !m.Read;
        // Only a call that's ringing now is a phone call; one reopened from the inbox reads as a message.
        _phone = m.Call && _fresh;
        _ringing = _phone;
        m.Read = true;
        EscapeCloses = true;
        if (_phone)
        {
            _view = new CallView { Who = CallView.Lookup(m.From), Talking = () => Sfx.Instance?.Speaking == true || _words != null && _words.VisibleCharacters >= 0 };
            Add(_view);
        }
        _rest = UITheme.VBox(10);
        Add(_rest);
        Build();
    }

    private static string PlaceTitle(string place) => place == "home" ? "THE BLOCK" : place.StartsWith("street:") ? "ON THE STREET" : Tables.Contacts.FirstOrDefault(k => k.Id == place)?.Venue.ToUpperInvariant() ?? "SOMEWHERE";

    public override void _Ready()
    {
        if (_ringing) { _ringT = 0; return; }
        Sfx.Instance?.Play(_m.Call ? Sound.Objective : Sound.UiSelect);
        Speak();
    }

    /// <summary>The beats that matter are spoken (the text on screen is the subtitle).</summary>
    private void Speak()
    {
        if (_m.Story != null && _fresh) Sfx.Instance?.Voice(Assets.Voice(Assets.Line(_m.Story)));
    }

    public override void _ExitTree()
    {
        // The base counts open dialogs (the city pauses while any is up): it must still run.
        base._ExitTree();
        Sfx.Instance?.Voice(null);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_ringing && (_ringT -= dt) <= 0) { _ringT = 1.7f; Sfx.Instance?.Play(Sound.Ring); }
        if (_words != null && _words.VisibleCharacters >= 0)
        {
            int n = _words.VisibleCharacters + Math.Max(1, (int)(Typing * dt + 0.5f));
            _words.VisibleCharacters = n >= _words.Text.Length ? -1 : n;
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_ringing && (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter } || Controls.Is(e, Act.Interact)))
        {
            PickUp();
            GetViewport().SetInputAsHandled();
            return;
        }
        // Back out of a call that wants an answer: you'll call back (it keeps as mail).
        if (_phone && Controls.Back(e) && _c.Waiting(_m)) _m.Call = false;
        base._UnhandledInput(e);
    }

    /// <summary>Pick up: the screen connects, they start talking.</summary>
    public void PickUp()
    {
        _ringing = false;
        _view.Ringing = false;
        Sfx.Instance?.Play(Sound.UiSelect);
        Speak();
        Build();
    }

    private void Build()
    {
        foreach (Node n in _rest.GetChildren()) n.QueueFree();
        if (_ringing)
        {
            var row = UITheme.HBox(14);
            row.Alignment = BoxContainer.AlignmentMode.Center;
            row.AddChild(UITheme.Button("Answer  [E]", PickUp, 220, 46));
            row.AddChild(UITheme.Button("Call back later", () => { _m.Call = false; Close(); }, 220, 46, "It keeps in the inbox (PDA, or 'mail' at the terminal)."));
            _rest.AddChild(row);
            return;
        }
        if (_phone)
        {
            // What they say, typed out under the picture; the subject as a caption.
            _rest.AddChild(UITheme.Label($"{_m.Subject}   ·   day {_m.Day}", 13, UITheme.Dim));
            if (_words == null)
            {
                _words = UITheme.Label(_m.Text, 17, UITheme.Text);
                _words.VisibleCharacters = 0;
            }
            else _words.GetParent()?.RemoveChild(_words);
            _rest.AddChild(_words);
        }
        else _rest.AddChild(Letter(_c, _m, width: 600));
        var opts = _c.Options(_m);
        if (_c.Waiting(_m))
        {
            for (int k = 0; k < opts.Count; k++)
            {
                int kk = k;
                var (text, block) = opts[k];
                string effect = _c.EffectOf(_m, k);
                var b = UITheme.Button(text, () => Choose(kk), 0, 42, block ?? effect ?? "");
                b.Disabled = block != null;
                _rest.AddChild(b);
                if (block != null)
                {
                    _rest.AddChild(UITheme.Label("    " + block, 13, UITheme.Bad));
                    if (block.Contains("etiquette")) _c.Explain("etiquette", "Etiquettes are the codes you can talk -- Street, Corporate, Gang, Security, Magic. Learn more for karma on the safehouse's Runner tab.");
                }
                else if (effect != null) _rest.AddChild(UITheme.Label("    " + effect, 13, UITheme.Dim));
            }
            if (!_m.Call) _rest.AddChild(UITheme.Button("Later", Close, 0, 36, "It keeps in the inbox (PDA, or 'mail' at the terminal)."));
            else _rest.AddChild(UITheme.Button(_phone ? "Hang up (answer later)" : "Call back later", () => { _m.Call = false; Close(); }, 0, 36, "It keeps in the inbox (PDA, or 'mail' at the terminal)."));
            return;
        }
        if (_m.Reply != null && _m.Answer >= 0 && opts.Count > 0)
        {
            _rest.AddChild(UITheme.Label($"> {opts[_m.Answer].text}", 16, UITheme.Cyan));
            _rest.AddChild(UITheme.Label(_m.Reply, 17, UITheme.Text));
        }
        var end = UITheme.HBox();
        end.Alignment = BoxContainer.AlignmentMode.Center;
        end.AddChild(UITheme.Button(_phone ? "Hang up" : "Continue", Close, 180, 40));
        _rest.AddChild(end);
        EscapeCloses = true;
    }

    private void Choose(int k)
    {
        string err = _c.Choose(_m, k);
        if (err != null) { Sfx.Instance?.Play(Sound.Error); return; }
        Sfx.Instance?.Play(Sound.Coin);
        Profile.SaveCareer(_c);
        Answered?.Invoke();
        if (_words != null) _words.VisibleCharacters = -1;
        Build();
    }

    /// <summary>
    /// The message itself: who, what about, the words -- with the sender's face when they are a
    /// contact, or crew as the scene finds them (at your place, together, hurt), which changes with your answer.
    /// </summary>
    public static Control Letter(Career c, Mail m, float width = 560)
    {
        var row = UITheme.HBox(14);
        ContactDef k = Tables.Contacts.FirstOrDefault(x => x.Name == m.From);
        string hire = Tables.Hires.FirstOrDefault(h => h.name == m.From).id;
        string crewPic = null;
        if (k == null && hire != null)
        {
            crewPic = Assets.CrewPicture(hire, c.CrewMood(m));
            if (Assets.Picture(crewPic) == null) crewPic = Assets.HirePicture(hire);
            if (Assets.Picture(crewPic) == null) crewPic = null;
        }
        bool face = k != null || crewPic != null;
        if (k != null)
            row.AddChild(new Portrait { Contact = k, Seed = Array.IndexOf(Tables.Contacts, k) + 2, CustomMinimumSize = new Vector2(110, 110), ClipContents = true });
        else if (crewPic != null)
            row.AddChild(new Portrait { Picture = crewPic, Tint = UITheme.Cyan, CustomMinimumSize = new Vector2(150, 150), ClipContents = true });
        var t = UITheme.VBox(4);
        t.CustomMinimumSize = new Vector2(width - (face ? (k != null ? 124 : 164) : 0), 0);
        t.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        t.AddChild(UITheme.Label(m.From, 20, k != null ? Art.C(k.Colour) : crewPic != null ? UITheme.Cyan : UITheme.Amber, heading: true));
        t.AddChild(UITheme.Label($"{m.Subject}   ·   day {m.Day}", 14, UITheme.Dim));
        t.AddChild(UITheme.Label(m.Text, 17, UITheme.Text));
        row.AddChild(t);
        return row;
    }
}
