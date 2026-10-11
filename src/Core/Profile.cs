using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Sprawl;

/// <summary>
/// Settings, and the three career slots (ironman: each saved after every run and at
/// the safehouse, emptied when its runner dies or retires). Kept in Godot's user:// as
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
    /// <summary>The best career's score, and who ran it (name, archetype, rank).</summary>
    public int BestScore;
    public string BestName, BestRank;

    /// <summary>A career is over: the longest, the busiest and the best-scoring kept. True when this one is the new best score.</summary>
    public bool RecordCareer(Career c)
    {
        BestDays = Math.Max(BestDays, c.Day);
        BestJobs = Math.Max(BestJobs, c.JobsDone);
        int score = c.Score;
        if (score <= BestScore && BestName != null) return false;
        BestScore = score;
        BestName = $"{c.Name} ({Career.Archetypes.FirstOrDefault(a => a.id == c.Archetype).name ?? c.Archetype})";
        BestRank = c.Rank;
        return true;
    }
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

    /// <summary>The career slot played last (what "New career" falls back on when every slot is taken).</summary>
    public int LastSlot = 1;

    private const string SettingsPath = "user://sprawlrunner.json";

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
        MigrateOldSave();
        Slot = Math.Clamp(Current.LastSlot, 1, Slots);
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

    // ---- the files: on disk, or in memory for the scratch profile ---------------------------

    private static readonly Dictionary<string, string> _scratchFiles = new();

    internal static bool FileExists(string path) => Scratch ? _scratchFiles.ContainsKey(path) : File.Exists(ProjectSettings.GlobalizePath(path));
    internal static string ReadFile(string path) => Scratch ? _scratchFiles.GetValueOrDefault(path) : File.Exists(ProjectSettings.GlobalizePath(path)) ? File.ReadAllText(ProjectSettings.GlobalizePath(path)) : null;

    /// <summary>Written to a temporary file first and moved over, so a crash mid-save never leaves half a career.</summary>
    internal static void WriteFile(string path, string text)
    {
        if (Scratch) { _scratchFiles[path] = text; return; }
        string full = ProjectSettings.GlobalizePath(path);
        File.WriteAllText(full + ".tmp", text);
        File.Move(full + ".tmp", full, true);
    }

    internal static void DeleteFile(string path)
    {
        if (Scratch) { _scratchFiles.Remove(path); return; }
        string full = ProjectSettings.GlobalizePath(path);
        if (File.Exists(full)) File.Delete(full);
    }

    private static void MoveFile(string from, string to)
    {
        if (Scratch) { if (_scratchFiles.Remove(from, out string t)) _scratchFiles[to] = t; return; }
        File.Move(ProjectSettings.GlobalizePath(from), ProjectSettings.GlobalizePath(to), true);
    }

    // ---- the career slots ------------------------------------------------------------------

    /// <summary>Three careers side by side (each its own ironman file); the agent playtester keeps its one.</summary>
    public const int SlotCount = 3;
    public static int Slots => Agent ? 1 : SlotCount;
    /// <summary>The slot being played: saves, the morning copy and a death all touch only this one.</summary>
    public static int Slot = 1;

    /// <summary>Where one slot's career lives.</summary>
    public static string CareerFile(int slot) => Agent ? "user://agent_career.json" : $"user://career{slot}.json";
    private static string MorningFile(int slot) => Agent ? "user://agent_morning.json" : $"user://career{slot}_morning.json";

    /// <summary>Before slots there was one file. It moves into slot 1 (or the first free one), with its morning copy.</summary>
    internal const string OldCareerFile = "user://career.json", OldMorningFile = "user://career_morning.json";

    public static void MigrateOldSave()
    {
        if (Agent) return;
        try
        {
            if (!FileExists(OldCareerFile)) return;
            int to = FileExists(CareerFile(1)) ? FreeSlot() : 1;
            if (to < 0) { GD.PrintErr("an old career.json is left over and every slot is taken: leaving it where it is"); return; }
            MoveFile(OldCareerFile, CareerFile(to));
            if (FileExists(OldMorningFile)) MoveFile(OldMorningFile, MorningFile(to));
            Current.LastSlot = to;
            Current.Save();
        }
        catch (Exception e) { GD.PrintErr($"could not move the old career into a slot: {e.Message}"); }
    }

    /// <summary>The first empty slot, or -1.</summary>
    public static int FreeSlot()
    {
        for (int s = 1; s <= Slots; s++) if (!HasSlot(s)) return s;
        return -1;
    }

    public static bool HasSlot(int slot) => FileExists(CareerFile(slot));

    /// <summary>Play this slot from now on (and start the title there next time).</summary>
    public static void UseSlot(int slot)
    {
        Slot = Math.Clamp(slot, 1, Slots);
        if (Current.LastSlot == Slot) return;
        Current.LastSlot = Slot;
        Current.Save();
    }

    // ---- the career ------------------------------------------------------------------

    public static bool HasCareer => HasSlot(Slot);

    public static void SaveCareer(Career c)
    {
        try { WriteFile(CareerFile(Slot), c.ToJson()); }
        catch (Exception e) { GD.PrintErr($"could not save the career: {e.Message}"); }
    }

    public static Career LoadCareer() => LoadCareer(Slot);

    public static Career LoadCareer(int slot)
    {
        try
        {
            string json = ReadFile(CareerFile(slot));
            return json == null ? null : Career.FromJson(json);
        }
        catch (Exception e)
        {
            GD.PrintErr($"career in slot {slot} unreadable: {e.Message}");
            return null;
        }
    }

    // ---- gentle mode: the morning's save ---------------------------------------------------

    public static void SaveMorning(Career c)
    {
        try { WriteFile(MorningFile(Slot), c.ToJson()); }
        catch (Exception e) { GD.PrintErr($"could not save the morning: {e.Message}"); }
    }

    public static Career LoadMorning()
    {
        try
        {
            string json = ReadFile(MorningFile(Slot));
            return json == null ? null : Career.FromJson(json);
        }
        catch (Exception e) { GD.PrintErr($"morning save unreadable: {e.Message}"); return null; }
    }

    /// <summary>Ironman: the end of a career empties its slot (the others are untouched).</summary>
    public static void DeleteCareer()
    {
        try
        {
            DeleteFile(CareerFile(Slot));
            DeleteFile(MorningFile(Slot));
        }
        catch (Exception e) { GD.PrintErr($"could not delete the career: {e.Message}"); }
    }
}
