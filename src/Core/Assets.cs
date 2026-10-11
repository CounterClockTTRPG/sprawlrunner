using System.Collections.Generic;
using Godot;

namespace Sprawl;

/// <summary>
/// The few files the game does not make itself: painted portraits and briefing
/// banners (<c>assets/art/*.webp</c>) and the contacts' spoken greetings
/// (<c>assets/voice/*.ogg</c>), generated through ../assetgen by
/// <c>scripts/assets.sh</c> from the two manifests beside them.
/// </summary>
///
/// <remarks>
/// They are optional by design: every caller has procedural art to fall back
/// on, so a missing file costs a picture and never a crash. They are read as
/// bytes and decoded here rather than imported -- <c>assets/.gdignore</c> keeps
/// the editor's importer out -- so the project stays one .tscn and code, and a
/// headless run needs no import step. (An export would need <c>assets/*</c> in
/// its non-resource include filter.)
/// </remarks>
public static class Assets
{
    /// <summary>
    /// Where the generated files are: in the project (res://, when run from the editor or
    /// play.sh), or in an assets folder beside the exported executable -- the .gdignore
    /// that keeps the importer out also keeps them out of an exported pack.
    /// </summary>
    public static readonly string Root = FileAccess.FileExists("res://assets/art/manifest.txt") || OS.HasFeature("editor")
        ? "res://assets"
        : OS.GetExecutablePath().GetBaseDir() + "/assets";
    public static readonly string ArtDir = Root + "/art", VoiceDir = Root + "/voice", VideoDir = Root + "/video", FontDir = Root + "/fonts";

    private static readonly Dictionary<string, FontFile> _fonts = new();

    /// <summary>A bundled font (<c>assets/fonts</c>, all OFL), or null when the file is missing.</summary>
    public static FontFile Font(string file)
    {
        if (_fonts.TryGetValue(file, out FontFile f)) return f;
        string path = $"{FontDir}/{file}";
        f = FileAccess.FileExists(path) ? new FontFile { Data = FileAccess.GetFileAsBytes(path), Antialiasing = TextServer.FontAntialiasing.Gray } : null;
        return _fonts[file] = f;
    }

    /// <summary>A clip's path (Ogg Theora, played from the file), or null when it has not been generated.</summary>
    public static string Clip(string name)
    {
        string path = $"{VideoDir}/{name}.ogv";
        return FileAccess.FileExists(path) ? path : null;
    }

    private static readonly Dictionary<string, Texture2D> _pictures = new();
    private static readonly Dictionary<string, AudioStream> _voices = new();

    /// <summary>A picture by manifest name, or null when it has not been generated.</summary>
    public static Texture2D Picture(string name)
    {
        if (_pictures.TryGetValue(name, out Texture2D t)) return t;
        t = null;
        string path = $"{ArtDir}/{name}.webp";
        if (FileAccess.FileExists(path))
        {
            var image = new Image();
            if (image.LoadWebpFromBuffer(FileAccess.GetFileAsBytes(path)) == Error.Ok)
            {
                // Portraits are drawn at 340 px and at 120 px on the PDA, so they
                // need mipmaps to shrink without shimmering.
                image.GenerateMipmaps();
                t = ImageTexture.CreateFromImage(image);
            }
        }
        return _pictures[name] = t;
    }

    /// <summary>A spoken line by manifest name, or null when it has not been generated.</summary>
    public static AudioStream Voice(string name)
    {
        if (_voices.TryGetValue(name, out AudioStream s)) return s;
        string path = $"{VoiceDir}/{name}.ogg";
        s = FileAccess.FileExists(path) ? AudioStreamOggVorbis.LoadFromBuffer(FileAccess.GetFileAsBytes(path)) : null;
        return _voices[name] = s;
    }

    public static string ContactPicture(ContactDef k) => $"contact-{k.Id}";
    /// <summary>The safehouse as you live in it: one picture per lifestyle (low is plain place-home); the loft is the loft.</summary>
    public static string HomePicture(string lifestyle, bool loft) => loft ? "place-loft" : lifestyle == "low" ? "place-home" : $"place-home-{lifestyle}";
    public static string HirePicture(string hireId) => $"hire-{hireId}";
    /// <summary>A crew member as a scene shows them (<see cref="Career.CrewMood"/>): at home, together, after a no -- or their hire portrait.</summary>
    public static string CrewPicture(string hireId, string mood) => mood == null ? HirePicture(hireId) : $"hire-{hireId}-{mood}";
    /// <summary>The moods a romanceable crew member has pictures for.</summary>
    public static readonly string[] CrewMoods = { "home", "partner", "rejected" };
    public static string BossPicture(Faction f) => $"boss-{f.ToString().ToLowerInvariant()}";
    public static string SitePicture(SiteStyle s) => $"site-{s.ToString().ToLowerInvariant()}";
    public static string Greeting(ContactDef k, int index) => $"greet-{k.Id}-{index}";
    /// <summary>A story beat read aloud: the quoted speech in a storylet's text.</summary>
    public static string Line(string storyId) => $"line-{storyId}";

    /// <summary>
    /// A manifest's entries as (name, second field, rest), skipping comments and
    /// blanks -- the same reading as scripts/assets.sh. For the self-check.
    /// </summary>
    public static List<(string name, string b, string rest)> Manifest(string path)
    {
        var list = new List<(string, string, string)>();
        if (!FileAccess.FileExists(path)) return list;
        foreach (string raw in FileAccess.GetFileAsString(path).Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0 || line.StartsWith('#')) continue;
            string[] f = line.Split('|', 3);
            if (f.Length < 3) { list.Add((line.Trim(), null, null)); continue; }
            list.Add((f[0].Trim(), f[1].Trim(), f[2].Trim()));
        }
        return list;
    }
}
