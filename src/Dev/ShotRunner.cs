using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Sprawl;

/// <summary>
/// The screenshot walkthrough: <c>./play.sh --shots [filter]</c> drives the
/// real game through its screens and writes <c>shots/*.png</c>, so the look
/// can be checked by eye.
/// </summary>
public partial class ShotRunner : Node
{
    public Root Root;
    private string _filter;

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--shots");
        if (at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--")) _filter = args[at + 1];
        _ = Run();
    }

    private bool Want(string name) => _filter == null || name.Contains(_filter, StringComparison.OrdinalIgnoreCase);

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        await Frames(3);
        Image img = GetViewport().GetTexture().GetImage();
        string dir = ProjectSettings.GlobalizePath("res://shots");
        System.IO.Directory.CreateDirectory(dir);
        img.SavePng(System.IO.Path.Combine(dir, name + ".png"));
        GD.Print($"shot: {name}");
    }

    private void CloseModals(Node n)
    {
        foreach (Node c in n.GetChildren())
        {
            if (c is Modal m) m.QueueFree();
            else CloseModals(c);
        }
    }

    private async Task Run()
    {
        try
        {
            ScreenFx.Flicker = false;
            PlaceScreen.Scenes = false;
            DebriefScreen.Roll = false;
            if (Want("title"))
            {
                Root.ShowTitle();
                await Frames(60);
                await Shot("01_title");
                Root.Screen.AddChild(new CreateScreen { Root = Root });
                await Frames(10);
                await Shot("02_create");
                // With every legacy perk earned, as a mage: the implant perks start unticked.
                CloseModals(Root);
                var had = Profile.Current.Legacy.ToList();
                Profile.Current.Legacy = Profile.LegacyPerks.Select(p => p.id).ToList();
                var cs2 = new CreateScreen { Root = Root };
                Root.Screen.AddChild(cs2);
                await Frames(5);
                ((Button)cs2.FindChild("mage", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(10);
                await Shot("02b_create_perks");
                cs2.QueueFree();
                Profile.Current.Legacy = had;
                // The achievements, earned and open.
                var hadAch = Profile.Current.Achieved.ToList();
                Profile.Current.Achieved = Achievements.All.Take(6).Select(a => a.Id).ToList();
                Modal ach = AchievementList.Open(Root.Screen);
                await Frames(10);
                await Shot("02d_achievements");
                ach.QueueFree();
                Profile.Current.Achieved = hadAch;
                // The settings, every row on screen.
                Modal settings = Settings.Open(Root.Screen);
                await Frames(10);
                await Shot("02c_settings");
                settings.QueueFree();
            }
            if (Want("intro"))
            {
                // Two cards: the first (on its still, the film off in the walkthrough) and one past, typed out.
                foreach (var (bg, k) in new[] { ("street", 0), ("cop", 1) })
                {
                    var intro = new IntroScreen(Intro.For(bg).Skip(k).Take(1), () => { });
                    Root.Show(intro);
                    await Frames(240);
                    await Shot($"02_intro_{bg}");
                    intro.QueueFree();
                }
            }
            Root.NewCareer("Wraith", "samurai", intro: false);
            Root.Career.Cash = 60000;
            Root.Career.Karma = 40;
            await Frames(10);
            CloseModals(Root);
            await Frames(5);
            if (Want("city"))
            {
                // Drive a little so the camera settles with traffic around.
                for (int i = 0; i < 40; i++) { Root.CitySim.Step(1 / 60f, new DriveInput { Throttle = 1 }); Root.CityView.Frame(1 / 60f); }
                await Frames(40);
                await Shot("03_city");
                Root.CitySim.ParkAt(Root.CitySim.HomeLot);
                await Frames(30);
                await Shot("04_city_home");
            }
            if (Want("layout"))
            {
                // Each site style, seen whole (schematics on), from high up.
                foreach (SiteStyle st in Enum.GetValues<SiteStyle>())
                {
                    var lj = new Job { Id = 900 + (int)st, Type = JobType.Retrieve, Style = st, Threat = 3, Seed = 1234 + (ulong)st, Faction = st is SiteStyle.Office or SiteStyle.Lab ? Faction.Halcyon : Faction.Vipers, Target = "x", Item = "case", Title = st.ToString(), Schematics = true, Lot = -1 };
                    Root.StartRun(lj);
                    await Frames(10);
                    Sprawl.Run lr = Root.RunScreen.R;
                    lr.Player.Pos = lr.Plan.Building.Centre.Centre;
                    Root.RunView.Cam.Zoom = new Vector2(0.55f, 0.55f);
                    for (int i = 0; i < 10; i++) { Root.RunView.Frame(1 / 60f); lr.Events.Clear(); await Frames(1); }
                    Root.RunView.Cam.Zoom = new Vector2(0.55f, 0.55f);
                    await Frames(3);
                    await Shot($"15_layout_{st}");
                }
                Root.EnterCity();
                await Frames(10);
            }
            if (Want("pda"))
            {
                Root.Career.Loyalty["arms"] = 2;
                Root.Career.ArcStep["arms"] = 1;
                Root.Career.Grudge[Faction.Vipers] = 3;
                var pda = new Pda(Root.Career);
                Root.CityScreen.AddChild(pda);
                await Frames(10);
                await Shot("16_pda_contacts");
                pda.QueueFree();
                await Frames(3);
            }
            if (Want("tower"))
            {
                var tj = new Job { Id = 960, Type = JobType.Datasteal, Style = SiteStyle.Office, Faction = Faction.Halcyon, Threat = 5, Seed = 21, Floors = 4, Target = "x", Item = "x", Title = "TOWER: test", Lot = -1, Schematics = true };
                Root.StartRun(tj);
                await Frames(5);
                Sprawl.Run tr = Root.RunScreen.R;
                tr.Player.Pos = tr.Plan.LiftSpots[0];
                for (int i = 0; i < 20; i++) { tr.Step(new RunInput { Slot = -1, Aim = tr.Player.Pos + new V2(1, 0) }); Root.RunView.Frame(1 / 60f); tr.Events.Clear(); await Frames(1); }
                await Shot("24_tower_lobby");
                tr.LiftHacked = true;
                tr.Player.Pos = tr.Plan.LiftSpots[2];
                Root.RunView.Cam.Zoom = new Vector2(0.6f, 0.6f);
                for (int i = 0; i < 20; i++) { tr.Step(new RunInput { Slot = -1, Aim = tr.Player.Pos + new V2(1, 0) }); Root.RunView.Frame(1 / 60f); tr.Events.Clear(); await Frames(1); }
                Root.RunView.Cam.Zoom = new Vector2(0.6f, 0.6f);
                await Frames(3);
                await Shot("25_tower_floor");
                Root.EnterCity();
                await Frames(10);
            }
            if (Want("classes"))
            {
                // Each archetype in their own room, close up.
                foreach (var (id, _, _) in Career.Archetypes)
                {
                    Root.Career.Archetype = id;
                    var hj = new Job { Id = 950, Type = JobType.Hack, Threat = 1, Seed = 5, Title = id, Target = "x", Item = "x", Lot = -1 };
                    Root.StartRun(hj);
                    await Frames(5);
                    Sprawl.Run hr = Root.RunScreen.R;
                    hr.Player.Pos = new V2(6.5f, 4.5f);
                    hr.Player.Facing = 0.6f;
                    Root.RunView.Cam.Zoom = new Vector2(3f, 3f);
                    for (int i = 0; i < 8; i++) { Root.RunView.Frame(1 / 60f); hr.Events.Clear(); await Frames(1); }
                    Root.RunView.Cam.Zoom = new Vector2(3f, 3f);
                    await Frames(3);
                    await Shot($"23_class_{id}");
                }
                Root.Career.Archetype = "samurai";
                Root.EnterCity();
                await Frames(10);
            }
            if (Want("story"))
            {
                Career c = Root.Career;
                c.Day = 9;
                c.Last = new LastRun { Job = new Job { Type = JobType.Retrieve, Item = "prototype cyberarm", Seed = 3, Employer = "fixer", District = "docks", Faction = Faction.Vipers, Target = "Vera Lindqvist", Title = "x" }, Success = true, Pay = 3000 };
                c.Set("fate:Vera Lindqvist", Career.Fates["fled"]);
                c.Set("fateday:Vera Lindqvist", 5);
                c.Send("welcome").Read = true;
                c.Send("fled");
                c.Send("quill-hello");
                Mail call = c.Send("collector");
                var sm = new StoryModal(c, call);
                Root.CityScreen.AddChild(sm);
                await Frames(10);
                await Shot("17_story_call");
                // Picked up: a caller who keeps their face hidden.
                sm.PickUp();
                await Frames(150);
                await Shot("17b_call_hidden");
                sm.QueueFree();
                await Frames(3);
                // And one on video: Juno.
                Mail jc = c.Send("story-1");
                jc.Call = true;
                var vm = new StoryModal(c, jc);
                Root.CityScreen.AddChild(vm);
                await Frames(5);
                vm.PickUp();
                await Frames(150);
                await Shot("17c_call_video");
                vm.QueueFree();
                await Frames(3);
                // At full trust, at the safehouse: a crew member asks.
                var rmod = new StoryModal(c, c.Send("romance-wisp"));
                Root.CityScreen.AddChild(rmod);
                await Frames(10);
                await Shot("17d_romance");
                rmod.QueueFree();
                await Frames(3);
                // The Lamplighter on the line, wearing someone else's face.
                Mail lm = c.Send("lamp-open");
                lm.Call = true;
                var lmod = new StoryModal(c, lm);
                Root.CityScreen.AddChild(lmod);
                await Frames(5);
                lmod.PickUp();
                await Frames(220);
                await Shot("17f_lamplighter");
                lmod.QueueFree();
                await Frames(3);
                // A contact in danger: the call, then the urgent job on the HUD with the map pointing at the door.
                int day0 = c.Day;
                c.Day = 25;
                c.Loyalty["fence"] = 2;
                c.Grudge[c.Holder(Tables.Contact("fence").District)] = Career.DangerGrudge;
                Mail dm = c.Send("danger-fence");
                var dmod = new StoryModal(c, dm);
                Root.CityScreen.AddChild(dmod);
                await Frames(5);
                dmod.PickUp();
                await Frames(200);
                await Shot("17d_danger_call");
                c.Choose(dm, 0);
                dmod.QueueFree();
                for (int i = 0; i < 20; i++) { Root.CitySim.Step(1 / 60f, new DriveInput()); Root.CityView.Frame(1 / 60f); await Frames(1); }
                await Shot("17e_urgent");
                if (c.Active is Job dj) { c.Board.Remove(dj); c.ActiveJob = -1; }
                c.TargetLot = -1;
                Root.CityScreen.SetRoute(null);
                c.Day = day0;
                var pda = new Pda(c);
                Root.CityScreen.AddChild(pda);
                await Frames(10);
                await Shot("18_pda_inbox");
                pda.QueueFree();
                await Frames(3);
                c.Push("docks", Faction.Vipers, 0.25f);
                c.Clocks["lotus"] = 6;
                c.Happenings.Add(new Happening { Id = "stadium", Start = c.Day, End = c.Day });
                c.Happenings.Add(new Happening { Id = "grid", Start = c.Day, End = c.Day + 1, District = "barrens" });
                c.CrewOf("ox").Trust = 3;
                c.CrewLearn("ox", "plating");
                c.Notoriety = 3;
                c.Awareness = 5;
                var pda2 = new Pda(c);
                Root.CityScreen.AddChild(pda2);
                await Frames(5);
                foreach (Node n in pda2.FindChildren("City", "Button", true, false)) if (n is Button b) b.EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(10);
                await Shot("19_pda_city");
                pda2.QueueFree();
                await Frames(3);
                // A scene at the door: Sal and the stolen crate.
                Lot arms = Root.City.Place("arms");
                c.Fired.Remove("sal-supplier");
                PlaceScreen.Scenes = true;
                var ps = new PlaceScreen(Root, arms, "arms");
                Root.CityScreen.AddChild(ps);
                await Frames(20);
                await Shot("20_story_scene");
                CloseModals(ps);
                ps.QueueFree();
                PlaceScreen.Scenes = false;
                await Frames(3);
                // The Spire's receipt, with its epilogue.
                c.ArcStep["arms"] = 3;
                c.ArcStep["decker"] = 3;
                c.Set("story:choice", 1);
                var fj = new Job { Id = 4444, Type = JobType.Destroy, Story = 4, Title = "STORY 4/4: Iron Spire", Seed = 99, District = "spire", Faction = Faction.Halcyon, Employer = "fixer", Pay = 30000, Karma = 19, Threat = 5, Target = "x", Item = "cores" };
                var deb = new Debrief { Headline = "IRON SPIRE FALLS" };
                deb.Lines.Add(("Payment", M.Money(30000)));
                deb.Lines.Add(("You walk out", "a legend"));
                deb.Epilogue.AddRange(c.Ending());
                var ds = new DebriefScreen(deb, fj, true, "TIME 07:41   KILLS 22   ALARM RAISED", () => { });
                Root.CityScreen.AddChild(ds);
                await Frames(15);
                await Shot("21_story_epilogue");
                ds.QueueFree();
                await Frames(3);
                // The career receipt, as a death would print it.
                var rc = Root.CareerReceipt(c, "THE LAST RECEIPT", c.Ending(), died: true);
                Root.CityScreen.AddChild(rc);
                await Frames(15);
                await Shot("21b_last_receipt");
                rc.QueueFree();
                await Frames(3);
                // The ambulance, lights going.
                TrafficCar amb = Root.CitySim.Traffic.First(t => t.Kind == CarKind.Ambulance);
                amb.Cruise = amb.Speed = 0;
                amb.Bumped = 99;
                Root.CitySim.Player.Pos = amb.Pos - amb.Forward * 3;
                Root.CitySim.Player.Heading = amb.Heading;
                Root.CitySim.Player.Vel = V2.Zero;
                for (int i = 0; i < 20; i++) { Root.CitySim.Step(1 / 60f, default); Root.CityView.Frame(1 / 60f); }
                await Frames(10);
                await Shot("22_ambulance");
            }
            if (Want("landmark"))
            {
                foreach (Lot l in Root.City.Lots.Where(x => x.Landmark != null))
                {
                    Root.CitySim.Player.Pos = new V2(l.R.X + l.R.W / 2f + 4, l.R.Bottom + 2.5f);
                    Root.CitySim.Player.Vel = V2.Zero;
                    for (int i = 0; i < 90; i++) { Root.CitySim.Step(1 / 60f, default); Root.CityView.Frame(1 / 60f); }
                    await Frames(5);
                    await Shot($"04d_landmark_{l.Landmark}");
                }
                Root.CitySim.ParkAt(Root.CitySim.HomeLot);
            }
            if (Want("bike"))
            {
                Root.Career.Vehicles.Add("kuro");
                Root.Career.Vehicle = "kuro";
                Root.CitySim.RefreshVehicle();
                Root.CitySim.Ambush(2);
                for (int i = 0; i < 70; i++) { Root.CitySim.Step(1 / 60f, new DriveInput { Throttle = 1, Steer = i > 40 ? 1 : 0 }); Root.CityView.Frame(1 / 60f); }
                await Frames(20);
                await Shot("04b_bike");
                Root.CitySim.Chasers.Clear();
            }
            if (Want("hub"))
            {
                Root.EnterHub(Root.City.Place("market"));
                await Frames(20);
                Sprawl.Run hr = Root.RunScreen.R;
                Actor v = hr.Actors.First(a => a.Vendor == "broker");
                for (int i = 0; i < 200 && V2.Distance(hr.Player.Pos, v.Pos) > 1.5f; i++) { hr.Step(new RunInput { Slot = -1, Move = M.Norm(v.Pos + new V2(0, -1) - hr.Player.Pos), Aim = v.Pos }); Root.RunView.Frame(1 / 60f); hr.Events.Clear(); }
                await Frames(10);
                await Shot("04c_hub");
                Root.EnterCity(Root.City.Place("market"));
                await Frames(10);
            }
            if (Want("terminal") || Want("place"))
            {
                Root.CityScreen.Enter(Root.CitySim.HomeLot);
                await Frames(90);
                await Shot("05_terminal");
                var ps = Root.CityScreen.GetChildren().OfType<PlaceScreen>().First();
                ps.Show("Runner");
                await Frames(10);
                await Shot("06_runner");
                // A mage who knows every spell: the longest Runner tab there is.
                {
                    Career rc = Root.Career;
                    string arch = rc.Archetype;
                    var spells = rc.Spells.ToList();
                    rc.Archetype = "mage";
                    rc.Spells = Tables.Spells.Select(s => s.Id).ToList();
                    ps.Show("Runner");
                    await Frames(10);
                    await Shot("06b_runner_mage");
                    rc.Archetype = arch;
                    rc.Spells = spells;
                }
                ps.Show("Loadout");
                await Frames(10);
                await Shot("07_loadout_bare");
                Root.Career.Decks.Add("kestrel");
                Root.Career.GiveProgram("hammer");
                Root.Career.GiveProgram("patch");
                Root.Career.GiveProgram("sneak");
                ps.Show("Loadout");
                await Frames(10);
                await Shot("07_loadout");
                Root.Career.Cyber.AddRange(new[] { "eyes", "dermal" });
                Root.Career.CyberGrade["dermal"] = "alpha";
                ps.Show("Deck");
                await Frames(10);
                await Shot("07a_deck");
                ps.Show("Upgrades");
                await Frames(10);
                await Shot("07b_upgrades");
                ps.Leave();
                await Frames(5);
                // Null's counter: decks.
                Root.CityScreen.Enter(Root.City.Place("decker"));
                await Frames(60);
                var nps = Root.CityScreen.GetChildren().OfType<PlaceScreen>().First();
                nps.Show("Decks");
                await Frames(10);
                await Shot("07c_null_decks");
                nps.Leave();
                await Frames(5);
                // The same safehouse, living differently: the picture and the panel follow the lifestyle.
                string was = Root.Career.Lifestyle;
                Root.Career.Lifestyle = "squatter";
                Root.CityScreen.Enter(Root.CitySim.HomeLot);
                await Frames(30);
                Root.CityScreen.GetChildren().OfType<PlaceScreen>().First().Show("Upgrades");
                await Frames(10);
                await Shot("07c_squatter");
                var lps = Root.CityScreen.GetChildren().OfType<PlaceScreen>().First();
                Root.Career.Buy(Root.Career.Offers("home").First(o => o.Kind == ShopKind.Lifestyle && o.Id == "luxury"));
                lps.Show("Upgrades");
                await Frames(10);
                await Shot("07d_luxury");
                lps.Leave();
                Root.Career.Lifestyle = was;
                await Frames(5);
                foreach (string contact in Tables.Contacts.Select(k => k.Id))
                {
                    Lot l = Root.City.Place(contact);
                    Root.CitySim.ParkAt(l);
                    Root.CityScreen.Enter(l);
                    await Frames(15);
                    await Shot($"08_shop_{contact}");
                    foreach (var p in Root.CityScreen.GetChildren().OfType<PlaceScreen>()) p.Leave();
                    await Frames(5);
                }
            }
            Job job = Root.Career.Board.OrderByDescending(j => j.Threat).First();
            if (_filter != null && Enum.TryParse(_filter, true, out JobType jt)) { job.Type = jt; }
            Root.Career.Accept(job);
            if (Want("map"))
            {
                Root.CitySim.ParkAt(Root.CitySim.HomeLot);
                await Frames(5);
                // A target of your own (the fence), on the map and then on the street.
                Root.CityScreen.SetRoute(Root.City.Place("fence"));
                Root.CityScreen._UnhandledInput(new InputEventKey { Keycode = Key.M, Pressed = true });
                await Frames(10);
                await Shot("09_map");
                Root.CityScreen._UnhandledInput(new InputEventKey { Keycode = Key.M, Pressed = true });
                for (int i = 0; i < 20; i++) { Root.CitySim.Step(1 / 60f, new DriveInput()); Root.CityView.Frame(1 / 60f); await Frames(1); }
                await Shot("09b_target");
                Root.CityScreen.SetRoute(null);
            }
            if (Want("run") || _filter != null && Enum.TryParse(_filter, true, out JobType _))
            {
                Lot site = Root.City.Lots[job.Lot];
                Root.CitySim.ParkAt(site);
                Root.CityScreen.Enter(site);
                await Frames(10);
                await Shot("10_briefing");
                foreach (var p in Root.CityScreen.GetChildren().OfType<PlaceScreen>()) p.Leave();
                // The same briefing with a boss and a hire, for the faces row; put back before the run.
                (int threat, string hired) = (job.Threat, job.Hired);
                (job.Threat, job.Hired) = (5, "ox");
                Root.CityScreen.Enter(site);
                await Frames(10);
                await Shot("10b_briefing_faces");
                foreach (var p in Root.CityScreen.GetChildren().OfType<PlaceScreen>()) p.Leave();
                (job.Threat, job.Hired) = (threat, hired);
                job.Schematics = true;
                Root.Career.Drones.Add("doberman");
                Root.Career.DroneSel = "doberman";
                Root.StartRun(job);
                await Frames(30);
                await Shot("11_run_start");
                Sprawl.Run r = Root.RunScreen.R;
                // Walk towards the building, then make some noise.
                V2 target = r.Plan.Building.Centre.Centre;
                for (int i = 0; i < 150; i++)
                {
                    r.Step(new RunInput { Slot = -1, Move = M.Norm(target - r.Player.Pos), Aim = target });
                    Root.RunView.Frame(1 / 60f);
                    r.Events.Clear();
                }
                await Frames(10);
                await Shot("12_run_inside");
                r.Step(new RunInput { Slot = -1, Rig = true });
                for (int i = 0; i < 50; i++) { r.Step(new RunInput { Slot = -1, Move = new V2(1, -0.3f), Aim = r.Drone.Pos + new V2(4, 0) }); Root.RunView.Frame(1 / 60f); r.Events.Clear(); }
                await Frames(5);
                await Shot("12b_rigging");
                r.Step(new RunInput { Slot = -1, Rig = true });
                Actor e = r.Actors.Where(a => a.Hostile && a.Def != null && !a.Static).OrderBy(a => V2.Distance(a.Pos, r.Player.Pos)).FirstOrDefault();
                if (e != null)
                {
                    r.Player.Pos = e.Pos + new V2(-4, 0);
                    if (r.L.Solid((int)r.Player.Pos.X, (int)r.Player.Pos.Y)) r.Player.Pos = e.Pos + new V2(0, 3);
                    r.Explode(e.Pos + new V2(1, 1), 3, 90, GrenadeKind.Frag, r.Player.Id, 2.2f);
                    for (int i = 0; i < 12; i++)
                    {
                        r.Step(new RunInput { Slot = 0, Fire = true, Aim = e.Pos });
                        Root.RunView.Frame(1 / 60f);
                        r.Events.Clear();
                    }
                    await Frames(2);
                    await Shot("13_run_fight");
                }
                // The fallen, one of each, beside a living guard for comparison.
                V2 c0 = r.Player.Pos;
                var kinds = new[] { EnemyKind.Ganger, EnemyKind.Hound, EnemyKind.Mage, EnemyKind.Heavy, EnemyKind.Guard };
                for (int k = 0; k < kinds.Length; k++)
                {
                    V2 at = c0 + new V2(-2.5f + k * 1.3f, 1.6f);
                    if (r.L.Solid((int)at.X, (int)at.Y)) at = c0 + new V2(-2.5f + k * 1.3f, -1.6f);
                    Actor d = r.AddEnemy(kinds[k], at, k);
                    r.Hurt(d, 1e6f, r.Player, false, nonLethal: k == 4);
                }
                r.AddEnemy(EnemyKind.Guard, c0 + new V2(2.5f, 0), MathF.PI);
                for (int i = 0; i < 6; i++) { Root.RunView.Frame(1 / 60f); r.Events.Clear(); await Frames(1); }
                await Shot("13c_corpses");
                // The same HUD at the largest text size.
                float ts = Profile.Current.TextScale;
                Profile.Current.TextScale = 1.4f;
                for (int i = 0; i < 4; i++) { Root.RunView.Frame(1 / 60f); await Frames(1); }
                await Shot("13x_textscale");
                Profile.Current.TextScale = ts;
                // A busy host: on alert, a spider in it, gates, control nodes, some damage and some trace.
                MatrixSetup ms = r.MatrixFor(MxUse.Site, MxGoal.Pull);
                ms.Seed = 7; ms.Rating = 5; ms.Alert = true; ms.Spiders = new() { -1 }; ms.Spoof = true; ms.Damage = 3; ms.Trace = 0.46f; ms.Decking = 4;
                ms.Programs = new() { "hammer", "patch", "scrub" }; ms.Memory = 10;
                ms.Controls = new() { MxControl.Cameras, MxControl.Maglocks, MxControl.Alarm };
                // (Nobody out there gets to shoot the body for the shot: a hit would pull you out.)
                foreach (Actor a in r.Actors.Where(a => a.Hostile)) a.Stun = 10;
                r.Matrix = new MatrixRun(ms);
                r.Matrix.Taken.Add(r.Matrix.Stores[0]);
                r.Matrix.Files.First(f => f.Objective).Taken = true;
                r.Matrix.UsedMp = 1;
                if (r.Matrix.Ice.FirstOrDefault(i => i.Kind == IcKind.Patrol) is Ic pat) pat.Report = 1.2f;
                for (int i = 0; i < 20; i++) { Root.RunView.Frame(1 / 60f); await Frames(1); }
                await Shot("13b_matrix");
                r.Matrix = null;
                r.Phase = RunPhase.Extracted;
                r.Success = true;
                await Frames(90);
                await Shot("14_debrief");
            }
            if (Want("run") || Want("club"))
            {
                // A shot on a nightclub's dance floor: the crowd runs.
                Root.EnterCity();
                await Frames(5);
                var cj = new Job { Id = 950, Type = JobType.Retrieve, Style = SiteStyle.Club, Threat = 2, Seed = 4711, Faction = Faction.Lotus, District = "strip", Target = "x", Item = "case", Title = "Club", Lot = -1 };
                Root.StartRun(cj);
                await Frames(10);
                Sprawl.Run cr = Root.RunScreen.R;
                Actor first = cr.Actors.First(a => a.IsNpc);
                foreach (Actor a in cr.Actors.Where(a => a.Hostile).ToList()) cr.Actors.Remove(a);
                cr.Player.Pos = first.Pos + new V2(1.2f, 0);
                cr.Noise(cr.Player.Pos, 14, true);
                for (int i = 0; i < 60; i++) { cr.Step(new RunInput { Slot = -1, Aim = cr.Player.Pos + new V2(1, 0) }); Root.RunView.Frame(1 / 60f); cr.Events.Clear(); await Frames(1); }
                await Shot("13d_club_panic");
            }
            if (Want("run") || Want("fixtures"))
            {
                // Working a safe: the dial swinging, tumblers lit as they fall. Then the room after: safe open, locker searched.
                Root.EnterCity();
                await Frames(5);
                Sprawl.Run fr = null;
                Pt safe = new(-1, -1);
                for (ulong sd = 1; sd < 40 && safe.X < 0; sd++)
                {
                    var fj = new Job { Id = 952, Type = JobType.Retrieve, Style = SiteStyle.Office, Threat = 4, Seed = 500 + sd, Faction = Faction.Halcyon, District = "spire", Target = "x", Item = "case", Title = "Fixtures", Lot = -1 };
                    LevelPlan fp = LevelGen.Generate(fj);
                    for (int i = 0; i < fp.L.W * fp.L.H && safe.X < 0; i++) if (fp.L.T[i] == Tile.Safe) safe = new Pt(i % fp.L.W, i / fp.L.W);
                    if (safe.X < 0) continue;
                    Root.StartRun(fj);
                    await Frames(10);
                    fr = Root.RunScreen.R;
                }
                if (fr != null)
                {
                    foreach (Actor a in fr.Actors.Where(a => a.Hostile).ToList()) fr.Actors.Remove(a);
                    fr.Player.MaxHp = fr.Player.Hp = 1e6f;
                    Pt stand = Pt.Dirs4.Select(d => new Pt(safe.X + d.X, safe.Y + d.Y)).First(q => fr.L[q] == Tile.Floor);
                    fr.Player.Pos = stand.Centre;
                    Root.RunView.Cam.Zoom = new Vector2(2.6f, 2.6f);
                    var hold = new RunInput { Slot = -1, Interact = true, Aim = safe.Centre };
                    for (int i = 0; i < 150; i++) { fr.Step(i == 0 ? hold with { InteractPressed = true } : hold); Root.RunView.Frame(1 / 60f); fr.Events.Clear(); await Frames(1); }
                    await Shot("13f_safe_crack");
                    for (int i = 0; i < 600 && !fr.Used(safe); i++) { fr.Step(hold); Root.RunView.Frame(1 / 60f); fr.Events.Clear(); if (i % 20 == 0) await Frames(1); }
                    fr.Step(new RunInput { Slot = -1, Aim = safe.Centre });
                    for (int i = 0; i < 10; i++) { Root.RunView.Frame(1 / 60f); await Frames(1); }
                    Root.RunView.Cam.Zoom = new Vector2(1.8f, 1.8f);
                    for (int i = 0; i < 10; i++) { Root.RunView.Frame(1 / 60f); await Frames(1); }
                    await Shot("13g_fixtures");
                }
            }
            if (Want("run") || Want("rooms"))
            {
                // Rooms with a purpose: one site per kind worth seeing, the runner stood in it, the map known.
                var wanted = new (RoomKind kind, SiteStyle style, Faction f, string name)[]
                {
                    (RoomKind.Server, SiteStyle.Office, Faction.Halcyon, "13h_room_server"), (RoomKind.Barracks, SiteStyle.Den, Faction.Vipers, "13i_room_barracks"),
                    (RoomKind.Break, SiteStyle.Office, Faction.Halcyon, "13j_room_break"), (RoomKind.Armoury, SiteStyle.Den, Faction.Vipers, "13k_room_armoury"),
                    (RoomKind.Ritual, SiteStyle.Den, Faction.Vipers, "13l_room_ritual"),
                    (RoomKind.Dock, SiteStyle.Warehouse, Faction.Vipers, "13m_room_dock"), (RoomKind.LabBay, SiteStyle.Lab, Faction.Halcyon, "13n_room_lab"),
                    (RoomKind.Booth, SiteStyle.Club, Faction.Lotus, "13o_room_booth"),
                };
                foreach (var (kind, style, f, name) in wanted)
                {
                    Root.EnterCity();
                    await Frames(5);
                    for (ulong sd = 1; sd < 80; sd++)
                    {
                        var rj = new Job { Id = 954, Type = JobType.Retrieve, Style = style, Threat = 3, Seed = 700 + sd, Faction = f, District = "docks", Target = "x", Item = "case", Title = "Rooms", Lot = -1, Schematics = true };
                        LevelPlan rp = LevelGen.Generate(rj);
                        Room room = rp.Rooms.FirstOrDefault(x => x.Kind == kind);
                        if (room == null) continue;
                        Root.StartRun(rj);
                        await Frames(10);
                        Sprawl.Run rr = Root.RunScreen.R;
                        rr.Player.MaxHp = rr.Player.Hp = 1e6f;
                        // Stand in a free corner of the room, out of everyone's sight.
                        foreach (Actor a in rr.Actors.Where(a => a.Hostile && a.Mood == Mood.None && !a.Static)) a.Pos = new V2(-50, -50);
                        Pt stand = Enumerable.Range(0, room.R.Area).Select(i => new Pt(room.R.X + i % room.R.W, room.R.Y + i / room.R.W)).Where(q => rr.L[q] == Tile.Floor).OrderBy(q => V2.Distance(q.Centre, room.Centre)).FirstOrDefault();
                        rr.Player.Pos = stand.Centre;
                        Root.RunView.Cam.Zoom = new Vector2(1.9f, 1.9f);
                        for (int i = 0; i < 30; i++) { rr.Step(new RunInput { Slot = -1, Aim = room.Centre }); Root.RunView.Frame(1 / 60f); rr.Events.Clear(); await Frames(1); }
                        await Shot(name);
                        break;
                    }
                }
            }
            if (Want("run") || Want("cleaner"))
            {
                // An office's night cleaners up close: one wheeling the cart, one running for the phone.
                Root.EnterCity();
                await Frames(5);
                var cj = new Job { Id = 956, Type = JobType.Retrieve, Style = SiteStyle.Office, Threat = 2, Seed = 4242, Faction = Faction.Halcyon, District = "spire", Target = "x", Item = "case", Title = "Night shift", Lot = -1, Schematics = true };
                Root.StartRun(cj);
                await Frames(10);
                Sprawl.Run cr = Root.RunScreen.R;
                cr.Player.MaxHp = cr.Player.Hp = 1e6f;
                foreach (Actor a in cr.Actors.Where(a => a.Hostile)) a.Pos = new V2(-50, -50);
                var cleaners = cr.Actors.Where(a => a.Cleaner).ToList();
                // The free floor nearest a point.
                V2 Floor(V2 near) => Enumerable.Range(0, cr.L.W * cr.L.H).Select(cr.L.PtOf).Where(q => cr.L.Walkable(q.X, q.Y)).OrderBy(q => V2.Distance(q.Centre, near)).First().Centre;
                if (cleaners.Count > 0)
                {
                    V2 at = cleaners[0].Pos;
                    cr.Player.Pos = Floor(at + new V2(-2.5f, 0.5f));
                    for (int k = 1; k < cleaners.Count; k++) cleaners[k].Pos = Floor(at + new V2(-1f, -3f));
                    Root.RunView.Cam.Zoom = new Vector2(3f, 3f);
                    for (int i = 0; i < 40; i++)
                    {
                        if (cleaners.Count > 1 && i >= 20) { cleaners[1].CallT = 2.5f; cleaners[1].PanicT = 8; cleaners[1].PanicFrom = cr.Player.Pos; }
                        cr.Step(new RunInput { Slot = -1, Aim = at });
                        Root.RunView.Frame(1 / 60f);
                        cr.Events.Clear();
                        await Frames(1);
                    }
                    await Shot("13p_cleaner");
                }
            }
            if (Want("run") || Want("rivals"))
            {
                // The other crew in the same building: in lime, three ways with the guards.
                Root.EnterCity();
                await Frames(5);
                if (Root.Career.Rival == null) Root.Career.MeetRival();
                var vj = new Job { Id = 955, Type = JobType.Retrieve, Style = SiteStyle.Warehouse, Threat = 2, Seed = 4711, Faction = Faction.Vipers, District = "docks", Target = "x", Item = "case", Title = "Contested", Lot = -1, Contested = true };
                Root.StartRun(vj);
                await Frames(10);
                Sprawl.Run vr = Root.RunScreen.R;
                vr.Player.MaxHp = vr.Player.Hp = 1e6f;
                for (int i = 0; i < 60 * 75 && !vr.RivalsCame; i++) { vr.Step(new RunInput { Slot = -1, Aim = vr.Player.Pos + new V2(1, 0) }); vr.Events.Clear(); }
                for (int i = 0; i < 60 * 3; i++) { vr.Step(new RunInput { Slot = -1, Aim = vr.Player.Pos + new V2(1, 0) }); vr.Events.Clear(); }
                Actor lead = vr.Actors.FirstOrDefault(a => a.Rival && !a.Dead);
                if (lead != null)
                {
                    vr.Player.Pos = lead.Pos + new V2(-4, 0);
                    vr.Player.Hp = vr.Player.MaxHp;
                    Root.RunView.Cam.Zoom = new Vector2(1.7f, 1.7f);
                    for (int i = 0; i < 40; i++) { vr.Step(new RunInput { Slot = -1, Aim = lead.Pos }); Root.RunView.Frame(1 / 60f); vr.Events.Clear(); await Frames(1); }
                    await Shot("13p_rivals");
                }
            }
            if (Want("people"))
            {
                // Close-ups of each faction's people: gear, shadows, the breath at rest, a gun kicking.
                // The last pass is a bounty's nest: ghouls, hellhounds and a barghest, with a Viper ganger
                // at the end of the line to tell them apart by, then the same line at the game's own zoom.
                foreach (int pass in Enumerable.Range(0, 4))
                {
                    bool nest = pass == 3;
                    Faction pf = nest ? Faction.Vipers : (Faction)pass;
                    Root.EnterCity();
                    await Frames(5);
                    var pj = new Job { Id = 953 + pass, Type = nest ? JobType.Bounty : JobType.Clear, Style = SiteStyle.Warehouse, Threat = 3, Seed = 77, Faction = pf, District = "docks", Target = "x", Item = "case", Title = "People", Lot = -1 };
                    Root.StartRun(pj);
                    await Frames(10);
                    Sprawl.Run pr = Root.RunScreen.R;
                    foreach (Actor a in pr.Actors.Where(a => a.Hostile).ToList()) pr.Actors.Remove(a);
                    pr.Player.MaxHp = pr.Player.Hp = 1e6f;
                    V2 c0 = pr.Player.Pos;
                    for (int k = 0; k < 40; k++) { V2 t = pr.Player.Pos + new V2(0, 3 + k * 0.25f); if (Enumerable.Range(-4, 9).All(dx => pr.L[Pt.Of(t + new V2(dx, 1.4f))] == Tile.Floor && pr.L[Pt.Of(t + new V2(dx, -1.2f))] == Tile.Floor)) { c0 = t; break; } }
                    pr.Player.Pos = c0 - new V2(0, 1.2f);
                    EnemyKind[] who = nest ? new[] { EnemyKind.Bruiser, EnemyKind.Bruiser, EnemyKind.Hound, EnemyKind.Barghest, EnemyKind.Ganger } : pf switch
                    {
                        Faction.Vipers => new[] { EnemyKind.Ganger, EnemyKind.Bruiser, EnemyKind.Firebug, EnemyKind.Hound, EnemyKind.Barghest },
                        Faction.Lotus => new[] { EnemyKind.Guard, EnemyKind.Blade, EnemyKind.Mage, EnemyKind.Bruiser, EnemyKind.Heavy },
                        _ => new[] { EnemyKind.Guard, EnemyKind.Sniper, EnemyKind.Shield, EnemyKind.Heavy, EnemyKind.Bulwark },
                    };
                    for (int k = 0; k < who.Length; k++)
                    {
                        Actor e = pr.AddEnemy(who[k], c0 + new V2(-3 + k * 1.5f, 1.4f), -MathF.PI / 2);
                        e.Stun = 0;
                        if (k == 0) e.ShotAt = 0.05f;
                    }
                    Root.RunView.Cam.Zoom = new Vector2(3.2f, 3.2f);
                    for (int i = 0; i < 4; i++) { pr.Time = 0.08f; Root.RunView.Frame(1 / 60f); pr.Events.Clear(); await Frames(1); }
                    await Shot($"23z_people_{(nest ? "nest" : pf.ToString().ToLowerInvariant())}");
                    if (nest)
                    {
                        Root.RunView.Cam.Zoom = new Vector2(1.45f, 1.45f);
                        for (int i = 0; i < 4; i++) { pr.Time = 0.08f; Root.RunView.Frame(1 / 60f); pr.Events.Clear(); await Frames(1); }
                        await Shot("23z_people_nest_far");
                    }
                }
                Root.EnterCity();
            }
            if (Want("run") || Want("highend"))
            {
                // The high end, lined up in one warehouse: spider, ghost operative (revealed), bulwark, barghest howling, and a hive.
                Root.EnterCity();
                await Frames(5);
                var hj = new Job { Id = 951, Type = JobType.Clear, Style = SiteStyle.Warehouse, Threat = 5, Seed = 99, Faction = Faction.Halcyon, District = "industrial", Target = "x", Item = "case", Title = "High end", Lot = -1 };
                Root.StartRun(hj);
                await Frames(10);
                Sprawl.Run hr = Root.RunScreen.R;
                foreach (Actor a in hr.Actors.Where(a => a.Hostile).ToList()) hr.Actors.Remove(a);
                hr.Player.MaxHp = hr.Player.Hp = 1e6f;
                // Out in the open yard, so the line-up has room.
                V2 c0 = hr.Player.Pos;
                for (int k = 0; k < 40; k++) { V2 t = hr.Player.Pos + new V2(0, 4 + k * 0.25f); if (Enumerable.Range(-6, 13).All(dx => hr.L[Pt.Of(t + new V2(dx, -4))] == Tile.Floor && hr.L[Pt.Of(t + new V2(dx, 4))] == Tile.Floor)) { c0 = t; break; } }
                hr.Player.Pos = c0;
                Root.RunView.Cam.Zoom = new Vector2(1.5f, 1.5f);
                Pt Open(V2 want) { for (int k = 0; k < 60; k++) { Pt q = Pt.Of(want + new V2(k % 8 - 4, k / 8 - 4) * 0.7f); if (hr.L[q] == Tile.Floor) return q; } return Pt.Of(c0); }
                var line = new (EnemyKind kind, V2 off)[]
                {
                    (EnemyKind.Spider, new V2(-5, -2)), (EnemyKind.Cloaker, new V2(-2.5f, -3)), (EnemyKind.Bulwark, new V2(1, -3.5f)), (EnemyKind.Barghest, new V2(4, -2.5f)),
                    (EnemyKind.HiveQueen, new V2(-4, 2.5f)), (EnemyKind.HiveWorker, new V2(-1, 3)), (EnemyKind.EggSac, new V2(1.5f, 3)), (EnemyKind.EggSac, new V2(2.5f, 3.6f)), (EnemyKind.HiveWorker, new V2(4.5f, 2.5f)),
                };
                foreach (var (kind, off) in line)
                {
                    Actor e = hr.AddEnemy(kind, Open(c0 + off).Centre, M.Angle(c0 - (c0 + off)));
                    if (kind == EnemyKind.Cloaker) e.RevealT = 99;
                    if (kind == EnemyKind.Barghest) e.HowlT = 0.4f;
                    if (kind == EnemyKind.EggSac) e.EggT = Tables.EggHatch * 0.7f;
                    e.Stun = 0;
                }
                for (int i = 0; i < 20; i++) { Root.RunView.Frame(1 / 60f); hr.Events.Clear(); await Frames(1); }
                await Shot("13e_high_end");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"shots failed: {ex}");
        }
        GetTree().Quit();
    }
}
