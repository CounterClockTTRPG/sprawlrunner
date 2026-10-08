using System;
using System.Linq;
using System.Collections.Generic;
using Godot;

namespace Sprawl;

/// <summary>
/// The main scene, and the only .tscn in the project. Owns audio and the flow
/// between the title, the city, places and runs, and the command-line entry
/// points: <c>--smoke</c> for the headless self-check and <c>--shots</c> for
/// the screenshot walkthrough.
/// </summary>
public partial class Root : Node
{
    private Node2D _world;
    private CanvasLayer _ui;
    public Career Career;
    public City City;
    public CitySim CitySim;
    public CityView CityView;
    public CityScreen CityScreen;
    public RunView RunView;
    public RunScreen RunScreen;
    public Control Screen;

    private readonly HashSet<Script> _scripts = new();

    public override void _Ready()
    {
        GetWindow().Title = "Sprawlrunner";
        // Hold on to every script once used. Godot drops a C# script resource when its
        // last node goes, and making the next one reloads it: ~40 ms per screen change
        // when the project is read over \\wsl.localhost (exported builds barely notice).
        GetTree().NodeAdded += n => { if (n.GetScript().Obj is Script s) _scripts.Add(s); };
        string[] args = OS.GetCmdlineUserArgs();
        bool smoke = Array.IndexOf(args, "--smoke") >= 0;
        bool shots = Array.IndexOf(args, "--shots") >= 0;
        bool agent = Array.IndexOf(args, "--agent") >= 0;
        Profile.Scratch = smoke || shots;
        Profile.Agent = agent;
        Profile.Load();
        AddChild(new Sfx { Name = "Sfx" });
        AddChild(new Music { Name = "Music" });
        Profile.Current.Apply();
        _world = new Node2D { Name = "World" };
        AddChild(_world);
        _ui = new CanvasLayer { Name = "UI", Layer = 10 };
        AddChild(_ui);
        AddChild(new ScreenFx { Name = "ScreenFx" });
        if (smoke) { AddChild(new SmokeTest { Root = this }); return; }
        if (shots) { AddChild(new ShotRunner { Root = this }); return; }
        if (agent) AddChild(new AgentRunner { Root = this, Name = "Agent" });
        ShowTitle();
    }

    /// <summary>Switching away from the game pauses it (a run or a drive), unless the setting is off -- never for the agent or the checks.</summary>
    public override void _Notification(int what)
    {
        if (what != NotificationApplicationFocusOut || !Profile.Current.AutoPause || AgentRunner.Active || Profile.Scratch) return;
        if (RunScreen != null && IsInstanceValid(RunScreen)) RunScreen.PauseIfPlaying();
        else if (CityScreen != null && IsInstanceValid(CityScreen)) CityScreen.PauseIfDriving();
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F11 })
        {
            Profile.Current.Fullscreen = !Profile.Current.Fullscreen;
            Profile.Current.Apply();
            Profile.Current.Save();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Clear()
    {
        foreach (Node n in _ui.GetChildren()) n.QueueFree();
        foreach (Node n in _world.GetChildren()) n.QueueFree();
        Screen = null;
        CityScreen = null;
        CityView = null;
        RunView = null;
        RunScreen = null;
        Sfx.Instance?.Engine(0, 0);
        Sfx.Instance?.Rain(0);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>The mouse mode for runs: hidden in play, but never captured when an agent is driving.</summary>
    public static Input.MouseModeEnum RunMouse => AgentRunner.Active ? Input.MouseModeEnum.Hidden : Input.MouseModeEnum.ConfinedHidden;

    public void Show(Control screen)
    {
        _ui.AddChild(screen);
        Screen = screen;
    }

    public void ShowTitle()
    {
        Clear();
        // A living city behind the title: the traffic of a throwaway career.
        var demo = Career.New("demo", "samurai", (ulong)GD.Randi());
        var city = City.Generate(demo.Seed);
        var sim = new CitySim(city, demo, demo.Seed);
        CityView = new CityView();
        _world.AddChild(CityView);
        CityView.Bind(sim);
        CityView.HidePlayer = true;
        Show(new TitleScreen { Root = this, Sim = sim, View = CityView });
        Music.Instance?.Play(MusicTrack.Title);
        Sfx.Instance?.Rain(0.25f);
    }

    public void NewCareer(string name, string archetype, string background = "street", bool gentle = false, bool intro = true, ICollection<string> declined = null)
    {
        ulong seed = ((ulong)GD.Randi() << 32) | GD.Randi();
        Career = Career.New(name, archetype, seed);
        Career.SetBackground(background);
        Career.Gentle = gentle;
        Profile.Current.ApplyLegacy(Career, declined);
        City = City.Generate(Career.Seed);
        Career.CityRef = City;
        Career.RefreshBoard(City);
        Profile.SaveCareer(Career);
        // Who you are and why you're here, before the streets.
        if (!intro) { EnterCity(); return; }
        Clear();
        Show(new IntroScreen(Intro.For(background), () => EnterCity()));
    }

    public bool Continue()
    {
        Career c = Profile.LoadCareer();
        if (c == null || c.Dead) return false;
        Career = c;
        City = City.Generate(Career.Seed);
        Career.CityRef = City;
        bool rebuilt = Career.CityVersion != City.Version;
        if (rebuilt)
        {
            // A game update changed the map: everything has a new address. Start at home.
            Career.CityVersion = City.Version;
            Career.CarX = float.NaN;
            Career.Board.RemoveAll(j => j.Id != Career.ActiveJob);
            Job act = Career.Active;
            if (act != null)
            {
                act.Lot = City.FreeSite(act.District, Career.Rng, new System.Collections.Generic.HashSet<int>());
                if (act.Lot2 >= 0) act.Lot2 = City.FreeSite(Tables.Districts.First(d => d.Id != act.District).Id, Career.Rng, new System.Collections.Generic.HashSet<int> { act.Lot });
                if (act.Lot < 0) { Career.Board.Remove(act); Career.ActiveJob = -1; }
            }
        }
        // A newer version may have opened a contact's door on a lot a job was using.
        foreach (Job moved in Career.Board.Where(j => j.Lot >= 0 && City.Lots[j.Lot].Place != null && City.Lots[j.Lot].Place is not ("home" or "loft")).ToList())
        {
            if (moved.Id != Career.ActiveJob) { Career.Board.Remove(moved); continue; }
            moved.Lot = City.FreeSite(moved.District, Career.Rng, new System.Collections.Generic.HashSet<int>(Career.Board.Select(b => b.Lot)));
            if (moved.Lot < 0) { Career.Board.Remove(moved); Career.ActiveJob = -1; }
        }
        foreach (ContactDef k in Tables.Contacts) Career.Loyalty.TryAdd(k.Id, 0);
        // Ironman: a run that was quit half-way counts as bailing out of it.
        Job quit = Career.Board.FirstOrDefault(j => j.Id == Career.InRun);
        Career.InRun = -1;
        if (quit != null)
        {
            Debrief d = Career.Finish(quit, new RunResult { Aborted = true, GrenadesLeft = Career.RunGrenades(), MedkitsLeft = Career.RunMedkits() });
            Career.Say($"You walked out on {quit.Title} mid-run.");
            Profile.SaveCareer(Career);
            Career.RefreshBoard(City);
            EnterCity(quit.Lot >= 0 ? City.Lots[quit.Lot] : null);
            Modal.Tell(CityScreen, "YOU WALKED OUT", $"Quitting during a run counts as bailing out: {quit.Title} failed, street rep -1.");
            return true;
        }
        Career.RefreshBoard(City);
        EnterCity();
        if (rebuilt) Modal.Tell(CityScreen, "KESSLER BAY, REBUILT", "This version of the game lays the city out differently, so everything has a new address -- your safehouse included (the green marker; you're parked outside it). Your gear, contacts and job are kept; the job board was refreshed.");
        Profile.SaveCareer(Career);
        return true;
    }

    /// <summary>Back in the car. <paramref name="parkAt"/> puts it at a lot's kerb.</summary>
    private int _ambush, _respond = -1, _getaway;

    public void EnterCity(Lot parkAt = null)
    {
        // Gentle mode: the first time out each day, keep a copy of the morning.
        if (Career.Gentle && Career.Flag("snap") < Career.Day && !Career.Dead)
        {
            Career.Set("snap", Career.Day);
            Profile.SaveMorning(Career);
        }
        using var _e = Perf.Event("enter city");
        using (Perf.Event("  clear")) Clear();
        using (Perf.Event("  city sim")) CitySim = new CitySim(City, Career, Career.Seed ^ (ulong)Career.Day * 7919) { Encounters = true };
        if (parkAt != null) CitySim.ParkAt(parkAt);
        CityView = new CityView();
        _world.AddChild(CityView);
        using (Perf.Event("  city view bind")) CityView.Bind(CitySim);
        if (_ambush > 0) { CitySim.Ambush(_ambush); _ambush = 0; }
        if (_respond >= 0) { CitySim.Respond(City.Lots[_respond]); _respond = -1; }
        if (_getaway > 0) { CitySim.Getaway(_getaway); _getaway = 0; }
        CityScreen = new CityScreen { Root = this };
        using (Perf.Event("  show screen")) Show(CityScreen);
        using (Perf.Event("  music")) Music.Instance?.Play(MusicTrack.City);
        Sfx.Instance?.Rain(Career.Weather switch { Career.Sky.Rain => 0.35f, Career.Sky.Storm => 0.6f, _ => 0 });
    }

    public void StartRun(Job job)
    {
        CitySim?.SaveCar();
        Career.InRun = job.Id;
        Career.BringRetained(job);
        using var _e = Perf.Event("start run");
        using (Perf.Event("  save career")) Profile.SaveCareer(Career);
        using (Perf.Event("  clear")) Clear();
        Run run;
        using (Perf.Event("  generate site")) run = new Run(job, Career);
        RunView = new RunView();
        using (Perf.Event("  add view node")) _world.AddChild(RunView);
        using (Perf.Event("  run view bind")) RunView.Bind(run);
        using (Perf.Event("  new screen")) RunScreen = new RunScreen { Root = this, R = run, View = RunView };
        using (Perf.Event("  show screen")) Show(RunScreen);
        using (Perf.Event("  music")) Music.Instance?.Play(MusicTrack.Run);
        if (Music.Instance != null) Music.Instance.Intensity = 0;
    }

    /// <summary>Park and walk around a market on foot.</summary>
    public void EnterHub(Lot lot)
    {
        CitySim?.SaveCar();
        Profile.SaveCareer(Career);
        Clear();
        var job = new Job { Id = -1, Type = JobType.Hub, Title = City.PlaceName(lot.Place), Lot = lot.Index, Seed = (ulong)lot.Index * 7919 + (ulong)Career.Day, Threat = 1, Faction = Faction.Lotus, Style = SiteStyle.Club };
        var run = new Run(job, Career, HubGen.Generate(job.Seed, lot.Place));
        RunView = new RunView();
        _world.AddChild(RunView);
        RunView.Bind(run);
        RunScreen = new RunScreen { Root = this, R = run, View = RunView };
        Show(RunScreen);
        Music.Instance?.Play(MusicTrack.City);
    }

    /// <summary>A run is over (extracted, aborted or dead): settle it and show the debrief.</summary>
    public void EndRun(Run run)
    {
        if (run.Peaceful)
        {
            Profile.SaveCareer(Career);
            EnterCity(City.Lots[run.Job.Lot]);
            return;
        }
        Job job = run.Job;
        Lot site = job.Lot >= 0 ? City.Lots[job.Lot] : null;
        bool noAmbush = Career.NextNoAmbush;
        Debrief d = Career.Finish(job, run.Result());
        // Noise follows you home: a loud run puts Lone Star on alert, a rich one draws gangers.
        if (!d.CareerOver)
        {
            Career.RunHeat(job, run.Alarm, run.Result().Died, run.Kills);
            bool rich = run.LootFound.Count > 0 || run.CashFound > 600 * job.Threat;
            // Gangers only bother with runners who have a name.
            _ambush = Career.Rep >= 3 && run.Success && (rich || run.Alarm) && new Rng(job.Seed ^ 0xAB).Chance(Career.AmbushChance(job)) ? 1 + job.Threat / 3 : 0;
            if (Career.Grudge.GetValueOrDefault(job.Faction) >= 5) _ambush = Math.Max(_ambush, 2);
            // Somebody you crossed has been waiting for this.
            if (Career.Flag("hitters") > 0 && !run.Result().Died) { _ambush = Math.Max(_ambush, 2); Career.Set("hitters", 0); }
            if (noAmbush) _ambush = 0;
            // A loud exit from a big site: Lone Star is already outside. A good driver, or Torque at the wheel, slips away.
            _getaway = run.Alarm && job.Threat >= 3 && !run.Result().Died && !noAmbush && Career.S(Skill.Driving) < 3 && !Career.On("strike") ? 1 + job.Threat / 4 : 0;
        }
        d.Unlocked.AddRange(Profile.Current.CheckAchievements(Career));
        // Gentle mode: death sends you back to this morning.
        Career morning = d.CareerOver && Career.Dead && Career.Gentle ? Profile.LoadMorning() : null;
        if (morning != null)
        {
            Music.Instance?.Play(MusicTrack.Debrief);
            Sfx.Instance?.Play(Sound.Fail);
            Show(new DebriefScreen(d, run, () =>
            {
                Career = morning;
                Career.CityRef = City;
                Career.Say("You wake up with the taste of a death you didn't have. It's this morning again.");
                Profile.SaveCareer(Career);
                EnterCity();
            }, wakeUp: true));
            return;
        }
        // A story won is a legacy earned, whether or not you keep running.
        if (Career.Flag("story:won") > 0 || Career.Flag("tide:won") > 0 || Career.Flag("cut:won") > 0 || Career.Flag("wyrm:choice") > 0) { Profile.Current.Earn(Career); Profile.Current.Save(); }
        if (d.CareerOver)
        {
            Profile.Current.Earn(Career);
            Profile.Current.BestDays = Math.Max(Profile.Current.BestDays, Career.Day);
            Profile.Current.BestJobs = Math.Max(Profile.Current.BestJobs, Career.JobsDone);
            Profile.Current.Save();
            Profile.DeleteCareer();
        }
        else Profile.SaveCareer(Career);
        Music.Instance?.Play(MusicTrack.Debrief);
        Sfx.Instance?.Play(run.Success ? Sound.Victory : Sound.Fail);
        // A trauma team drops you at the clinic; a loud run brings an ambulance and a patrol to the site.
        bool pickedUp = run.Phase == RunPhase.Dead && !d.CareerOver;
        if (run.Alarm && !pickedUp && site != null) _respond = site.Index;
        var screen = new DebriefScreen(d, run, () =>
        {
            if (d.CareerOver)
            {
                Career ended = Career;
                ShowTitle();
                Screen?.AddChild(CareerReceipt(ended, "THE LAST RECEIPT", ended.Ending(), died: ended.Dead));
                return;
            }
            if (!pickedUp) { EnterCity(site); return; }
            Lot clinic = City.Place("doc");
            EnterCity(clinic);
            CitySim.DropOff(clinic);
            Modal.Tell(CityScreen, "TRAUMALINK", "You come round on a gurney on the pavement outside Ichikawa's clinic, a foil blanket round your shoulders. The ambulance is already pulling away, lights still going. Somewhere, your car is still parked where you left it -- they brought it round.");
        });
        Show(screen);
    }

    /// <summary>A courier job settled in the city: debrief over the streets.</summary>
    public void EndCourier(Job job, bool success, float secondsLeft)
    {
        // Nothing is spent on a drive: hand back what a run would have started with.
        var res = new RunResult { Success = success, GrenadesLeft = Career.RunGrenades(), MedkitsLeft = Career.RunMedkits() };
        Debrief d = Career.Finish(job, res);
        d.Unlocked.AddRange(Profile.Current.CheckAchievements(Career));
        Profile.SaveCareer(Career);
        Sfx.Instance?.Play(success ? Sound.Victory : Sound.Fail);
        string stats = success ? $"Delivered with {secondsLeft:0} s to spare" : "Not delivered";
        var screen = new DebriefScreen(d, job, success, stats, () => { CitySim?.SaveCar(); });
        Show(screen);
    }

    /// <summary>Walking away a legend: the epilogue, the legacy, and back to the title.</summary>
    public void Retire()
    {
        Career.Retired = true;
        Profile.Current.Earn(Career);
        Profile.Current.BestDays = Math.Max(Profile.Current.BestDays, Career.Day);
        Profile.Current.BestJobs = Math.Max(Profile.Current.BestJobs, Career.JobsDone);
        Profile.Current.CheckAchievements(Career);
        Profile.Current.Save();
        Profile.DeleteCareer();
        bool legend = Career.RetiresALegend;
        string last = legend
            ? $"{Career.Name} sells the car, buys a ticket out of Kessler Bay, and is never heard from again. The street remembers the name."
            : $"{Career.Name} sells the car, pays off the people who need paying, and takes the morning train south with the money. Nobody writes songs about it. Nobody has to.";
        var epilogue = (Career.Epilogue.Count > 0 ? Career.Epilogue : Career.Ending()).Prepend(last);
        var m = CareerReceipt(Career, legend ? "A LEGEND RETIRES" : "OUT OF THE GAME", epilogue, died: false);
        m.Closed = ShowTitle;
        (CityScreen ?? (Node)Screen).AddChild(m);
    }

    /// <summary>The whole career on one receipt: the numbers, the people, the city, the last word.</summary>
    public static Modal CareerReceipt(Career c, string title, IEnumerable<string> epilogue, bool died)
    {
        var m = new Modal(title, 760);
        var list = UITheme.VBox(6);
        // Wide enough to read: wrapped labels take their width from here, not from their text.
        list.CustomMinimumSize = new Vector2(680, 0);
        foreach (var (what, value) in c.Receipt())
        {
            // The label in a fixed column, the value taking the rest (long lists wrap onto a second line).
            var row = UITheme.HBox(12);
            var name = UITheme.Label(what, 15, UITheme.Dim, wrap: false);
            name.CustomMinimumSize = new Vector2(300, 0);
            row.AddChild(name);
            var val = UITheme.Label(value, 15, UITheme.Text, HorizontalAlignment.Right);
            val.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(val);
            list.AddChild(row);
        }
        list.AddChild(UITheme.Spacer(6));
        list.AddChild(UITheme.Label(c.Obituary(died), 16, UITheme.Cyan));
        foreach (string e in epilogue) list.AddChild(UITheme.Label(e, 16, UITheme.Text));
        // Clear of the scrollbar.
        var pad = new MarginContainer();
        pad.AddThemeConstantOverride("margin_right", 22);
        pad.AddChild(list);
        m.Add(UITheme.Scroll(pad, 480));
        var end = UITheme.HBox();
        end.Alignment = BoxContainer.AlignmentMode.Center;
        end.AddChild(UITheme.Button("The end", () => m.Close(), 200, 44));
        m.Add(end);
        return m;
    }

    public void SaveAndQuit()
    {
        if (CitySim != null) CitySim.SaveCar();
        if (Career != null && !Career.Dead) Profile.SaveCareer(Career);
        ShowTitle();
    }
}
