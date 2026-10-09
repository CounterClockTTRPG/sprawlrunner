using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Sprawl;

/// <summary>
/// Settings, and the one career slot (ironman: saved after every run and at
/// the safehouse, deleted when the runner dies). Kept in Godot's user:// as
/// JSON. <see cref="Scratch"/> (the self-check and the walkthrough) keeps
/// everything in memory so a test never touches the real files.
/// </summary>
public sealed class Profile
{
    public float Master = 0.8f, MusicVolume = 0.5f, Effects = 0.8f;
    public bool Fullscreen;
    /// <summary>The window's size when not full screen (one of <see cref="WindowSizes"/>), vsync, a frame-rate cap (0: none),
    /// and whether the game pauses itself when the window loses focus.</summary>
    public int WindowW = 1600, WindowH = 900;
    public bool VSync = true;
    public int FpsCap;
    public bool AutoPause = true;

    public static readonly (int w, int h)[] WindowSizes = { (1280, 720), (1600, 900), (1920, 1080), (2560, 1440) };
    public static readonly int[] FpsCaps = { 0, 30, 60, 120, 144 };
    public bool ScreenShake = true;
    /// <summary>The dirty-monitor effect (<see cref="ScreenFx"/>), 0 off .. 1 heavy.</summary>
    public float Grime = 0.6f;
    /// <summary>The grime as a light overlay (no screen read-back, no fringing or grain) except during hits and glitches: for weaker GPUs.</summary>
    public bool LightScreenFx;
    /// <summary>Off until the player turns it on in the settings.</summary>
    public bool SoundOn;
    /// <summary>Faction colours that colour-blind players can tell apart; all text a little larger.</summary>
    public bool ColourBlind;
    public float TextScale = 1;
    public int BestDays, BestJobs;
    /// <summary>Perks earned by past careers, carried into new ones.</summary>
    public List<string> Legacy = new();
    /// <summary>Rebound keys (actions missing here use the defaults).</summary>
    public Dictionary<Act, Key> Bindings = new();
    /// <summary>Achievements earned, by any career.</summary>
    public List<string> Achieved = new();

    /// <summary>Mark what this career has newly earned; returns their names.</summary>
    public List<string> CheckAchievements(Career c)
    {
        var got = Achievements.NewlyEarned(c, Achieved);
        foreach (var a in got) Achieved.Add(a.Id);
        if (got.Count > 0) Save();
        return got.Select(a => a.Name).ToList();
    }

    public static readonly (string id, string name, string how)[] LegacyPerks =
    {
        ("stash", "Old stash: +¥1,500 at the start", "finish 5 jobs in one career"),
        ("friend", "Old friend: the fixer starts at loyalty 1", "survive to day 20"),
        ("chrome", "Spare chrome: start with cybereyes", "carry 4 implants at once"),
        ("legend", "Legend: start on a Kuro R9", "finish the Iron Spire story"),
        ("folk", "Folk hero: trust 1 with every contact", "leak the buyer list and finish the Iron Spire"),
        ("marker", "Lotus marker: +¥5,000, and the Lotus owe you", "sell the buyer list to the Lotus and finish the Iron Spire"),
        ("corpfriend", "Corporate friend: start with a smartlink", "give the buyer list to Sorensen-Aoki and finish the Iron Spire"),
        ("warded", "Warded: start with TraumaLink Gold", "finish Toxic Tide"),
        ("streetking", "Street king: +2 street rep at the start", "finish The Cut"),
        ("wyrmcoin", "Wyrm's coin: +¥10,000 at the start", "take the dragon's money"),
        ("dragonfoe", "Dragon-touched: Logic +1 at the start", "tell the world about the dragon"),
        ("lamplight", "Lamplight: start with the program", "help the Lamplighter become whole"),
    };

    /// <summary>Unlock what this career earned.</summary>
    public void Earn(Career c)
    {
        void U(string id, bool ok) { if (ok && !Legacy.Contains(id)) Legacy.Add(id); }
        U("stash", c.JobsDone >= 5);
        U("friend", c.Day >= 20);
        U("chrome", c.Cyber.Count >= 4);
        bool won = c.Retired && c.Chapter >= Career.Chapters.Length || c.Flag("story:won") > 0;
        U("legend", won);
        U("folk", won && c.StoryChoice == 1);
        U("marker", won && c.StoryChoice == 2);
        U("corpfriend", won && c.StoryChoice == 3);
        U("warded", c.Flag("tide:won") > 0);
        U("streetking", c.Flag("cut:won") > 0);
        U("wyrmcoin", c.Flag("wyrm:choice") == 1);
        U("dragonfoe", c.Flag("wyrm:choice") == 2);
        U("lamplight", c.Flag("lamp:won") == 1);
    }

    /// <summary>Perks that put chrome in you: a mage may well say no.</summary>
    public static readonly string[] ChromePerks = { "chrome", "corpfriend" };

    /// <summary>Start a career with what earlier careers left you -- except the perks you turned down (<paramref name="declined"/>).</summary>
    public void ApplyLegacy(Career c, ICollection<string> declined = null)
    {
        ICollection<string> Legacy = declined == null ? this.Legacy : this.Legacy.Where(p => !declined.Contains(p)).ToList();
        if (Legacy.Contains("stash")) c.Cash += 1500;
        if (Legacy.Contains("friend")) c.Loyalty["fixer"] = Math.Max(1, c.Loyalty.GetValueOrDefault("fixer"));
        if (Legacy.Contains("chrome") && !c.Has("eyes")) c.Install("eyes");
        if (Legacy.Contains("legend") && !c.Vehicles.Contains("kuro")) { c.Vehicles.Add("kuro"); c.Vehicle = "kuro"; }
        if (Legacy.Contains("folk")) foreach (ContactDef k in Tables.Contacts) c.Loyalty[k.Id] = Math.Max(1, c.Loyalty.GetValueOrDefault(k.Id));
        if (Legacy.Contains("marker")) c.Cash += 5000;
        if (Legacy.Contains("corpfriend") && !c.Has("smartlink")) c.Install("smartlink");
        if (Legacy.Contains("warded")) c.Set("tlgold");
        if (Legacy.Contains("streetking")) c.Rep += 2;
        if (Legacy.Contains("wyrmcoin")) c.Cash += 10000;
        if (Legacy.Contains("lamplight")) c.GiveProgram("lamplight");
        if (Legacy.Contains("dragonfoe")) c.Attrs[(int)Attr.Logic] = Math.Min(6, c.Attrs[(int)Attr.Logic] + 1);
    }

    public static bool Scratch;
    /// <summary>An agent playtester: its own career file, settings never saved.</summary>
    public static bool Agent;
    public static Profile Current { get; private set; } = new();

    private const string SettingsPath = "user://sprawlrunner.json";
    private static string CareerPath => Agent ? "user://agent_career.json" : "user://career.json";
    private static string _scratchCareer;

    public static void Load()
    {
        // Tests and the agent playtester get a clean profile: never the player's achievements or settings.
        if (Scratch || Agent) { Current = new Profile(); return; }
        try
        {
            string path = ProjectSettings.GlobalizePath(SettingsPath);
            if (File.Exists(path)) Current = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), Json.Options) ?? new Profile();
        }
        catch (Exception e)
        {
            GD.PrintErr($"settings unreadable, starting fresh: {e.Message}");
            Current = new Profile();
        }
    }

    public void Save()
    {
        if (Scratch || Agent) return;
        try { File.WriteAllText(ProjectSettings.GlobalizePath(SettingsPath), JsonSerializer.Serialize(this, Json.Options)); }
        catch (Exception e) { GD.PrintErr($"could not save settings: {e.Message}"); }
    }

    public void Apply()
    {
        AudioBuses.SetVolume("Master", SoundOn ? Master : 0);
        AudioBuses.SetVolume(AudioBuses.Music, MusicVolume);
        AudioBuses.SetVolume(AudioBuses.Sfx, Effects);
        ScreenFx.Instance?.Apply();
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetMode(Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
            // The agent harness and the walkthrough keep their own 1600x900 window.
            if (!Fullscreen && !Agent && !Scratch) SetWindow(WindowW, WindowH);
        }
        Engine.MaxFps = FpsCap;
    }

    /// <summary>Size the window (never past the screen's usable area) and centre it.</summary>
    private static void SetWindow(int w, int h)
    {
        Rect2I usable = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
        var size = new Vector2I(Math.Min(w, usable.Size.X), Math.Min(h, usable.Size.Y));
        if (DisplayServer.WindowGetSize() == size) return;
        DisplayServer.WindowSetSize(size);
        DisplayServer.WindowSetPosition(usable.Position + (usable.Size - size) / 2);
    }

    // ---- the career ------------------------------------------------------------------

    public static bool HasCareer => Scratch ? _scratchCareer != null : File.Exists(ProjectSettings.GlobalizePath(CareerPath));

    public static void SaveCareer(Career c)
    {
        string json = c.ToJson();
        if (Scratch) { _scratchCareer = json; return; }
        try
        {
            string path = ProjectSettings.GlobalizePath(CareerPath);
            File.WriteAllText(path + ".tmp", json);
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception e) { GD.PrintErr($"could not save the career: {e.Message}"); }
    }

    public static Career LoadCareer()
    {
        try
        {
            string json = Scratch ? _scratchCareer : File.ReadAllText(ProjectSettings.GlobalizePath(CareerPath));
            return json == null ? null : Career.FromJson(json);
        }
        catch (Exception e)
        {
            GD.PrintErr($"career unreadable: {e.Message}");
            return null;
        }
    }

    // ---- gentle mode: the morning's save ---------------------------------------------------

    private static string SnapPath => Agent ? "user://agent_morning.json" : "user://career_morning.json";
    private static string _scratchSnap;

    public static void SaveMorning(Career c)
    {
        string json = c.ToJson();
        if (Scratch) { _scratchSnap = json; return; }
        try { File.WriteAllText(ProjectSettings.GlobalizePath(SnapPath), json); }
        catch (Exception e) { GD.PrintErr($"could not save the morning: {e.Message}"); }
    }

    public static Career LoadMorning()
    {
        try
        {
            string json = Scratch ? _scratchSnap : File.Exists(ProjectSettings.GlobalizePath(SnapPath)) ? File.ReadAllText(ProjectSettings.GlobalizePath(SnapPath)) : null;
            return json == null ? null : Career.FromJson(json);
        }
        catch (Exception e) { GD.PrintErr($"morning save unreadable: {e.Message}"); return null; }
    }

    public static void DeleteCareer()
    {
        if (Scratch) { _scratchCareer = null; return; }
        try
        {
            string path = ProjectSettings.GlobalizePath(CareerPath);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e) { GD.PrintErr($"could not delete the career: {e.Message}"); }
    }
}
