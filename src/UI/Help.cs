using System;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;

namespace Sprawl;

/// <summary>One page of help: what it is about, the screenshot that shows it (a walkthrough shot's name), and the words.</summary>
public sealed record HelpTopic(string Id, string Title, string Picture, string[] Text);

/// <summary>
/// The Help pages, from the title, the pause menus and the PDA: the core of the game, a page a topic,
/// each with a screenshot taped in. The pictures are walkthrough shots shrunk into <c>assets/help</c>
/// (<c>./play.sh --shots help</c> makes them); a page whose picture is missing is words alone.
/// Keys are written <c>{Act.Name}</c> and shown as whatever the player has bound.
/// </summary>
public static partial class Help
{
    private static readonly Regex KeyToken = new(@"\{Act\.(\w+)\}", RegexOptions.Compiled);

    /// <summary>A page's words with every {Act.X} replaced by the bound key, in brackets.</summary>
    public static string Keys(string text) =>
        KeyToken.Replace(text, m => Enum.TryParse(m.Groups[1].Value, out Act a) ? $"[{Controls.KeyName(a)}]" : m.Value);

    /// <summary>Every {Act.X} a page names that isn't an action (the self-check wants none).</summary>
    public static string[] BadTokens(HelpTopic t) =>
        t.Text.SelectMany(p => KeyToken.Matches(p).Select(m => m.Groups[1].Value)).Where(n => !Enum.TryParse(n, out Act _)).ToArray();

    private static int _last;

    /// <summary>Open the pages over <paramref name="host"/>, at <paramref name="topic"/> or where the player last left off.</summary>
    public static Modal Open(Node host, string topic = null)
    {
        int at = topic != null ? Math.Max(0, Array.FindIndex(Topics, t => t.Id == topic)) : _last;
        var m = new Modal("HELP", 1240);
        var row = UITheme.HBox(18);
        // The contents down the left, the page on the right.
        var index = UITheme.VBox(4);
        index.CustomMinimumSize = new Vector2(250, 0);
        var page = UITheme.VBox(10);
        page.CustomMinimumSize = new Vector2(950, 0);
        page.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var buttons = new Button[Topics.Length];
        void Show(int i)
        {
            _last = at = (i + Topics.Length) % Topics.Length;
            for (int k = 0; k < buttons.Length; k++) buttons[k].ButtonPressed = k == at;
            foreach (Node n in page.GetChildren()) n.QueueFree();
            HelpTopic t = Topics[at];
            var head = UITheme.HBox(12);
            head.AddChild(UITheme.Grow(UITheme.Label(t.Title.ToUpperInvariant(), 26, UITheme.Amber, heading: true, wrap: false)));
            head.AddChild(UITheme.Label($"{at + 1} / {Topics.Length}", 14, UITheme.Dim, wrap: false, mono: true));
            page.AddChild(head);
            // The screenshot, taped in at 16:9 so nothing is cropped.
            if (t.Picture != null && Assets.HelpPicture(t.Id) is Texture2D pic)
            {
                var box = new CenterContainer();
                box.AddChild(new Photo { Texture = pic, Seed = at * 7 + 3, CustomMinimumSize = new Vector2(720, 405) });
                page.AddChild(box);
            }
            var words = UITheme.VBox(8);
            words.CustomMinimumSize = new Vector2(920, 0);
            foreach (string p in t.Text) words.AddChild(UITheme.Label(Keys(p), 16, UITheme.Text));
            var pad = new MarginContainer();
            pad.AddThemeConstantOverride("margin_right", 18);
            pad.AddChild(words);
            page.AddChild(UITheme.Scroll(pad, t.Picture != null && Assets.HelpPicture(t.Id) != null ? 200 : 560));
        }
        for (int i = 0; i < Topics.Length; i++)
        {
            int ii = i;
            var b = UITheme.Button(Topics[i].Title, () => Show(ii), 250, 34);
            b.ToggleMode = true;
            b.Alignment = HorizontalAlignment.Left;
            buttons[i] = b;
            index.AddChild(b);
        }
        row.AddChild(UITheme.Scroll(index, 640));
        row.AddChild(page);
        m.Add(row);
        var nav = UITheme.HBox(12);
        nav.Alignment = BoxContainer.AlignmentMode.Center;
        nav.AddChild(UITheme.Button("< Previous", () => Show(at - 1), 160, 40));
        nav.AddChild(UITheme.Button("Next >", () => Show(at + 1), 160, 40));
        nav.AddChild(UITheme.Button("Done", () => m.Close(), 160, 40));
        m.Add(nav);
        Show(at);
        host.AddChild(m);
        return m;
    }
}
