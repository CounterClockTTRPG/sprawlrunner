using System;
using System.Linq;
using Godot;

namespace Sprawl;

/// <summary>Three save slots, the old single save moving into one, and the time played on each career.</summary>
public partial class SmokeTest
{
    private void SaveSlots()
    {
        int had = Profile.Slot;
        for (int s = 1; s <= Profile.SlotCount; s++) { Profile.UseSlot(s); Profile.DeleteCareer(); }
        Profile.DeleteFile(Profile.OldCareerFile);
        Eq(Profile.Slots, 3, "three slots");
        Eq(Profile.FreeSlot(), 1, "all empty: the first is free");

        // Three careers side by side, each in its own slot.
        string[] names = { "", "Ash", "Brick", "Cinder" };
        for (int s = 1; s <= 3; s++)
        {
            Profile.UseSlot(s);
            Career c = Career.New(names[s], "samurai", (ulong)s);
            c.Cash = 1000 * s;
            c.PlaySeconds = 600 * s;
            Profile.SaveCareer(c);
        }
        Eq(Profile.FreeSlot(), -1, "all taken: none free");
        for (int s = 1; s <= 3; s++)
        {
            Career back = Profile.LoadCareer(s);
            Check(back != null && back.Name == names[s] && back.Cash == 1000 * s, $"slot {s} keeps its own career");
            Check(back != null && Math.Abs(back.PlaySeconds - 600 * s) < 1e-6, $"slot {s} keeps its time played");
        }
        Profile.UseSlot(2);
        Eq(Profile.LoadCareer()?.Name, "Brick", "loading reads the slot in play");
        Eq(Profile.Current.LastSlot, 2, "the slot in play is remembered");

        // A death empties its own slot and nothing else.
        Profile.SaveMorning(Profile.LoadCareer());
        Profile.DeleteCareer();
        Check(!Profile.HasSlot(2) && Profile.HasSlot(1) && Profile.HasSlot(3), "the end of a career empties only its slot");
        Check(Profile.LoadMorning() == null, "and its morning copy");
        Eq(Profile.FreeSlot(), 2, "the emptied slot is free again");

        // The old single save moves into a slot, morning copy and all.
        for (int s = 1; s <= 3; s++) { Profile.UseSlot(s); Profile.DeleteCareer(); }
        Career old = Career.New("Oldtimer", "decker", 77);
        old.Day = 41;
        old.PlaySeconds = 12345;
        Profile.WriteFile(Profile.OldCareerFile, old.ToJson());
        Profile.WriteFile(Profile.OldMorningFile, old.ToJson());
        Profile.MigrateOldSave();
        Check(!Profile.FileExists(Profile.OldCareerFile) && !Profile.FileExists(Profile.OldMorningFile), "the old career.json is moved, not copied");
        Career moved = Profile.LoadCareer(1);
        Check(moved != null && moved.Name == "Oldtimer" && moved.Day == 41, "the old career lands in slot 1");
        Check(moved != null && Math.Abs(moved.PlaySeconds - 12345) < 1e-6, "with its time played");
        Profile.UseSlot(1);
        Eq(Profile.LoadMorning()?.Name, "Oldtimer", "and its morning copy with it");
        // With slot 1 already taken (an older build run after a newer one), it takes the first free slot.
        Profile.WriteFile(Profile.OldCareerFile, Career.New("Second", "mage", 78).ToJson());
        Profile.MigrateOldSave();
        Check(Profile.LoadCareer(1)?.Name == "Oldtimer" && Profile.LoadCareer(2)?.Name == "Second", "a second old save never overwrites slot 1");
        Profile.MigrateOldSave();
        Check(Profile.LoadCareer(2)?.Name == "Second" && !Profile.HasSlot(3), "migrating twice changes nothing");
        // A save from before time was counted reads as none played.
        string noClock = System.Text.RegularExpressions.Regex.Replace(old.ToJson(), "\"PlaySeconds\":\\s*[0-9.]+,?", "");
        Check(!noClock.Contains("PlaySeconds") && Career.FromJson(noClock).PlaySeconds == 0, "an older save starts its clock at zero");

        // Time played: counted while driving, stopped by the pause menu and on the title.
        Career keepCareer = Root.Career;
        CityScreen keepCity = Root.CityScreen;
        Control keepScreen = Root.Screen;
        Career playing = Career.New("Clock", "samurai", 5);
        Root.Career = playing;
        var screen = new CityScreen { Root = Root };
        Root.CityScreen = screen;
        Root.CountPlayTime(10);
        Check(Math.Abs(playing.PlaySeconds - 0.25) < 1e-6, "a long frame counts as a quarter second, not ten");
        for (int i = 0; i < 60; i++) Root.CountPlayTime(1 / 60.0);
        Check(Math.Abs(playing.PlaySeconds - 1.25) < 1e-6, $"a second of driving is a second played ({playing.PlaySeconds:0.000})");
        Root.CityScreen = null;
        Root.Screen = new TitleScreen();
        Root.CountPlayTime(0.1);
        Check(Math.Abs(playing.PlaySeconds - 1.25) < 1e-6, "the title is not play");
        Root.Screen.Free();
        Root.Screen = null;
        screen.Free();
        playing.Dead = true;
        Root.CountPlayTime(0.1);
        Check(Math.Abs(playing.PlaySeconds - 1.25) < 1e-6, "a dead runner's clock has stopped");
        Root.Career = keepCareer;
        Root.CityScreen = keepCity;
        Root.Screen = keepScreen;
        Profile.UseSlot(3);
        playing.Dead = false;
        Profile.SaveCareer(playing);
        Check(Math.Abs(Profile.LoadCareer(3).PlaySeconds - 1.25) < 1e-6, "time played is saved with the career");
        Check(playing.Receipt().Any(r => r.what == "Time played" && r.value == "0m"), "the last receipt shows the time played");
        Eq(M.Played(59), "0m", "under a minute");
        Eq(M.Played(42 * 60 + 5), "42m", "minutes");
        Eq(M.Played(3 * 3600 + 5 * 60), "3h 05m", "hours and minutes");

        for (int s = 1; s <= 3; s++) { Profile.UseSlot(s); Profile.DeleteCareer(); }
        Profile.UseSlot(had);
    }
}
