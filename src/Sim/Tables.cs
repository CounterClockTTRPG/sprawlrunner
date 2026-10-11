using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

public enum Attr { Body, Agility, Strength, Reaction, Logic, Charisma }
public enum Skill { Firearms, Heavy, Blades, Demolitions, Electronics, Stealth, Driving, Decking }
public enum WeaponClass { Pistol, Smg, Shotgun, Rifle, Sniper, Lmg, Launcher, Melee }
public enum GrenadeKind { Frag, Flash, Emp, Fire, Breach }
public enum Ability { None, Overclock, Hack, Cloak, Manabolt, Fireball, Heal, Summon, Astral, Project, Ward }
public enum JobType { Eliminate, Retrieve, Destroy, Datasteal, Rescue, Clear, Courier, Hub, Investigate, Plant, Defend, Kidnap, Escort, Bounty, Hack, Race }
public enum Faction { Vipers, Halcyon, Lotus }
public enum SiteStyle { Warehouse, Office, Den, Lab, Club, Tunnels, Store, Clinic }
public enum RunMod { Blackout, ShiftChange, Deadline, Elite, Skeleton }

public enum EnemyKind { Ganger, Hound, Mage, Guard, Blade, Heavy, Sniper, Drone, Turret, Camera, Vip, Shield, Rigger, Boss, Ally, Spirit, Bruiser, Firebug, Spider, Cloaker, Bulwark, Barghest, HiveQueen, HiveWorker, EggSac }

public sealed class WeaponDef
{
    public string Id, Name, Desc;
    public WeaponClass Class;
    public float Damage, Rate, Reload, Spread, Speed = 48, Range = 26, Noise, Blast, Arc = 100, Reach = 1.1f;
    public int Mag, Pellets = 1, Cost, Loyalty;
    /// <summary>Shots that destroy the tile they hit carry on through it.</summary>
    public bool Pierce;
    /// <summary>Knocks out instead of killing; Stun seconds on every hit.</summary>
    public bool NonLethal;
    public float Stun;
    /// <summary>Top tier: armour counts this much (lasers 0); homing rounds; sets fire where it hits.</summary>
    public float ArmorMul = 1;
    public bool Homing, Incendiary;
    /// <summary>Only sold to a runner with a name (prestige: street rep 18).</summary>
    public bool Elite;
    public bool Melee => Class == WeaponClass.Melee;
    public Skill Skill => Class switch
    {
        WeaponClass.Shotgun or WeaponClass.Lmg or WeaponClass.Launcher => Skill.Heavy,
        WeaponClass.Melee => Skill.Blades,
        _ => Skill.Firearms,
    };
    public WeaponDef Copy() => (WeaponDef)MemberwiseClone();
    public bool Moddable => Class is WeaponClass.Pistol or WeaponClass.Smg or WeaponClass.Rifle or WeaponClass.Sniper or WeaponClass.Lmg;
}

public sealed class GrenadeDef
{
    public string Id, Name, Desc;
    public GrenadeKind Kind;
    public float Radius, Damage, Fuse = 1.4f;
    public int Cost, Loyalty;
}

public sealed class ArmorDef
{
    public string Id, Name, Desc;
    public int Armor, Cost, Loyalty;
    public float SpeedMul = 1;
    /// <summary>Detection slower by this much (a stealth suit); spell power up by this much (a warded coat).</summary>
    public float Stealth, Magic;
    /// <summary>Footstep noise times this (sound-dampening soles).</summary>
    public float Steps = 1;
    public bool Elite;
}

public sealed class CyberDef
{
    public string Id, Name, Desc, Requires;
    public float Essence;
    public int Cost, Loyalty;
    public Ability Grants;
    /// <summary>Grown, not built: bioware (the doc says so; grades still apply).</summary>
    public bool Bio, Elite;
}

public sealed class ProgramDef
{
    public string Id, Name, Desc;
    public int Cost, Loyalty;
    /// <summary>Memory it takes in the deck (Mp), and whether it is run by a key inside the Matrix (Q, F, R).</summary>
    public int Mp;
    public bool Active;
}

public sealed class SpellDef
{
    public string Id, Name, Desc;
    public int Cost, Loyalty;
    public Ability Grants;
}

public sealed class ModDef
{
    public string Id, Name, Desc;
    public int Cost;
}

public sealed class VehicleDef
{
    public string Id, Name, Desc;
    public float Top, Accel, Grip, Length = 1.9f, Width = 0.95f;
    public int Hp, Cost, Loyalty;
    public uint Colour;
    /// <summary>Motorcycles: sharp steering, slides at the limit, fragile rider. Offroad ignores grass.</summary>
    public bool Bike, Offroad;
    /// <summary>Not a car: a helicopter you own (free fast travel, roof extraction).</summary>
    public bool Air, Elite;
    public VehicleDef Copy() => (VehicleDef)MemberwiseClone();
}

public sealed class EnemyDef
{
    public EnemyKind Kind;
    public string Name;
    public float Hp, Speed, Radius = 0.32f, Aim, Budget, Vision = 10, PreferRange = 6;
    public int Armor;
    public string Weapon;
    public bool Flying, Static, Melee;
    public int Cash;
}

public sealed class UpgradeDef
{
    public string Id, Name, Desc, Requires;
    public int Cost;
}

public sealed class ContactDef
{
    public string Id, Name, Role, Venue, District, Blurb;
    /// <summary>Added after the city's layout was fixed: claims its lot after the markets, so old cities keep their addresses.</summary>
    public bool Late;
    public string[] Greetings;
    public uint Colour;
}

public sealed class DistrictDef
{
    public string Id, Name;
    public int MinThreat, MaxThreat;
    public SiteStyle[] Styles;
    public Faction[] Factions;
    public uint Colour;
    /// <summary>Building heights, in tiles of fake 3D lean.</summary>
    public float MinHeight, MaxHeight;
}

/// <summary>Every number in the game.</summary>
public static partial class Tables
{
    /// <summary>A guard below this share of health rolls once to surrender, at this chance.</summary>
    public const float SurrenderBelow = 0.25f, SurrenderChance = 0.15f;
    /// <summary>The security ladder: levels a second while a guard is calling it in (0 to 3 in about 8 s, the old alarm); how fast unreported sub-ticks fade;
    /// seconds a level by the clock once they know you're there; a camera sighting, a body found; patrols at level 2; seconds a camera ignores you after a sighting.</summary>
    public const float SecurityCalling = 0.25f, SecurityFade = 0.15f, SecurityTrickle = 120, SecurityCamera = 1.2f, SecurityBody = 0.5f, CameraRearm = 6;
    public const int SecurityPatrols = 2;
    /// <summary>A search: rooms within this reach of where you were seen, at most this many, for at most this long; after it, and on a site on edge, guards notice you this much faster.</summary>
    public const float SearchReach = 12, SearchFor = 30, JumpyDetect = 1.35f, OnEdgeDetect = 1.2f;
    public const int SearchRooms = 3;
    /// <summary>Bystanders panic: for 6-10 s at a sprint; any shot within at least 3 tiles (a loud one 0.8 x its noise); anyone hit within 6; enemy noise this loud counts; a scream carries this far to guards.</summary>
    public const float PanicMin = 6, PanicMax = 10, PanicSpeed = 4.4f, PanicQuiet = 3, PanicSeen = 6, PanicLoud = 8, ScreamCarries = 7;
    /// <summary>City pedestrians scatter from gunfire within this many tiles.</summary>
    public const float GunfireScatter = 10;
    /// <summary>Crackdown checkpoints: how many, kept this far from doors you need, how near one sees a hot car, where you stop, how long the look takes.</summary>
    public const int Roadblocks = 5;
    /// <summary>Light: how much faster a guard notices you in full light, and how much slower in the dark (light 0);
    /// a shot aimed this close to a lamp, passing this close, puts it out; a blast puts out lamps in this share of its radius.</summary>
    public const float LitDetect = 1.15f, DarkDetect = 0.65f, LampAim = 0.9f, LampHit = 0.3f, LampBlast = 1f;
    /// <summary>The night on a generated site, before any lamp: a little light from the sky and the city.</summary>
    public const float NightAmbient = 0.08f;
    /// <summary>A room hangs a lamp every this many tiles along each side (one, for a small room).</summary>
    public const int LampEvery = 8;
    /// <summary>Rooms with a purpose: an off-duty guard sees this far and notices this fast (of normal); a sleeper wakes to a quiet sound
    /// this close, or a loud one within this share of its reach; a ruptured pipe's cloud (radius, seconds), its cold (hp a second: a guard, you)
    /// and your pace in it; a lab's toxic cloud.</summary>
    public const float OffDutySight = 0.5f, OffDutyDetect = 0.5f, WakeQuiet = 1.6f, WakeLoud = 0.6f;
    public const float CoolantRadius = 2.6f, CoolantLife = 9, CoolantHurt = 3, CoolantHurtYou = 4, CoolantSlow = 0.55f;
    public const float ToxicRadius = 2.8f, ToxicLife = 10, ToxicHurt = 9, ToxicHurtYou = 7;
    /// <summary>Site styles: how many night cleaners an office has, and how long one takes to reach a phone; the share of a den's gangers who are drunk
    /// (their pace, aim spread and noticing, of a sober one's); how far a dog smells you through walls (rain cuts it) and how fast;
    /// how far the club's music carries a gunshot on its floor (of the usual); how fast a lab's sprinklers drown a fire;
    /// what pushed-over shelving does to whoever is under it.</summary>
    public const int Cleaners = 2;
    /// <summary>The new sites: shoppers on a store's floor; sedated patients in a clinic's wards; how long a clinic's anaesthetic
    /// keeps a guard under; wading slows to this and splashes this much louder; a store's panic button brings Lone Star after this long.</summary>
    public const int Shoppers = 7, Patients = 2;
    /// <summary>Where jobs happen (Career.PlaceFor): ghoul nests in the sewers; kidnappings and some wetwork at a store; the
    /// Lotus' hostages in a body bank, some datasteals and retrievals in a clinic; a gang's stash or prisoner in the tunnels.</summary>
    public const float NestInTunnels = 0.75f, KidnapAtStore = 0.5f, WetworkAtStore = 0.15f, BodyBank = 0.5f, ClinicBusiness = 0.12f, GangTunnels = 0.15f;
    public const float AnaestheticSleep = 6f, WadeSlow = 0.62f, WadeNoise = 1.7f, PanicButton = 2.5f;
    public const float CleanerCall = 3f, DrunkShare = 0.35f, DrunkPace = 0.8f, DrunkAim = 1.6f, DrunkNotice = 0.75f, Smell = 4.5f, SmellRate = 0.6f;
    public const float MusicMask = 0.45f, Sprinklers = 3f, ToppleHurt = 45, ToppleStun = 2.5f;
    /// <summary>The rival crew: the share of jobs they can do that they bid on, how many days such a job waits, when in the run they turn up (s), and their kit.</summary>
    public const float RivalContest = 0.25f, RivalFrom = 30, RivalTo = 70;
    public const int RivalWait = 3;
    /// <summary>Seconds a rival needs at the mainframe to pull the data (you take about 12-20).</summary>
    public const float RivalHack = 22;
    /// <summary>The rival arc. Act two (their Johnson burns them) comes after this many contests, at least this many days after they arrive;
    /// warning them takes this off their grudge, selling the warning adds this and pays base + per tier; pulling Rook out takes this off.
    /// Act three this many days after the burn: a showdown at this grudge or more, a merge at this or less (and notoriety under this);
    /// the hideout finale's stash and karma; the postcard this many days after they leave, and what's in it if they owe you.</summary>
    public const int RivalBurnAfter = 3, RivalBurnDay = 6, RivalWarnGrudge = 3, RivalSellGrudge = 3, RivalSellPay = 4000, RivalSellPerTier = 2000, RivalRookGrudge = 2;
    public const int RivalEndAfter = 4, RivalShowdownGrudge = 6, RivalMergeGrudge = 3, RivalMergeNotoriety = 6, RivalStash = 6000, RivalShowdownKarma = 6;
    public const int RivalPostcardAfter = 10, RivalPostcardGift = 3000, RivalMergeTrust = 2;
    /// <summary>The Johnson who burns the rival crew.</summary>
    public const string RivalJohnson = "Mr. Halvorsen";
    /// <summary>Street encounters: seconds of driving before the first look and between looks, at most this many a day,
    /// how long one waits on its kerb, and how far past it you can be before it's gone.</summary>
    public const float StreetFirst = 60, StreetEvery = 140, StreetLife = 60, StreetGone = 45;
    public const int StreetPerDay = 2;
    /// <summary>Saved this much, a runner can walk away a success whenever they like (as after a finished campaign).</summary>
    public const int RetireSavings = 250_000;
    /// <summary>Faction standing (0-10): safe passage, their own jobs on the board (paying this much more), the faction item.</summary>
    public const int StandingSafe = 3, StandingJobs = 4, StandingItem = 5;
    public const float FavourPay = 1.25f;
    /// <summary>The share of a hit that gets through any armour: a quarter, less a fortieth per point of armour, never below 8 %.</summary>
    public const float ArmorFloor = 0.25f, ArmorFloorFade = 40, ArmorFloorMin = 0.08f;
    /// <summary>The best odds of talking a Johnson up, however charming you are.</summary>
    public const float HaggleMax = 0.85f;
    /// <summary>Karma a week for living in luxury.</summary>
    public const int LuxuryKarma = 3;
    /// <summary>A faction's grudge from which their sites field elite security.</summary>
    public const int GrudgeElite = 3;
    /// <summary>Ways back from notoriety: the penance jobs open at 7+ and take 2 off; three quiet wins in a row take 1;
    /// Juno's restitution costs this much per point, once a week, and takes 1.</summary>
    public const int AtoneFrom = 7, AtoneCut = 2, QuietStreak = 3, RestitutionPer = 2000;
    /// <summary>How many jobs are remembered; an echo comes 10-30 days after its job, and no two within 4 days.</summary>
    public const int MemoryCap = 20, EchoMin = 10, EchoSpread = 21, EchoGap = 4;
    /// <summary>Business troubles (Career.Business.cs): at most one every BizGap days across all you own, on BizChance of the mornings that may;
    /// a shakedown's weekly cut (%) and the cut talked down to, the days a business is shut when it is wrecked, how long seeing them off buys;
    /// the inspector's bribe (% of a week), the days shut for refusing him (and under a Lotus council), the days a refusal keeps him away;
    /// the skim (% of the takings) and the days before the skimmer does you a favour.</summary>
    public const int BizGap = 6, BizCut = 25, BizTalkCut = 10, BizWrecked = 7, BizSafeDays = 30,
        BizBribe = 40, BizInspectShut = 4, BizCouncilShut = 8, BizCleanDays = 30, BizSkim = 15, BizSkimTip = 7;
    public const float BizChance = 0.35f;
    /// <summary>Election week (Career.Election): first around day 25, every 30 days (+0..3 seeded), a week long; the winner's season lasts 20 days.</summary>
    public const int ElectionFirst = 25, ElectionEvery = 30, ElectionJitter = 3, ElectionWeek = 7, SeasonDays = 20, RenewalPolice = 2, ReformPolice = 3;
    public const float VotePay = 1.3f, RenewalPay = 1.1f, RenewalTurf = 0.1f, ReformRent = 0.85f;
    /// <summary>A partner (Storylets.Romance): their hit points when they come on a job with you, days between quiet nights,
    /// days before a "not yet" is asked again, what ending it costs (trust, days out), days before the keepsake turns up.</summary>
    public const float PartnerAllyHp = 1.2f;
    public const int PartnerNightEvery = 8, RomanceAskAgain = 20, PartnerBreakTrust = 2, PartnerBreakDays = 3, PartnerKeepsakeAfter = 20;
    /// <summary>Visitors at the safehouse: at most one every <c>VisitGap</c> days -- after a job, or after
    /// <c>VisitQuiet</c> quiet days. The cat: food to keep it, karma a week while it stays, the kids to find it.</summary>
    public const int VisitGap = 3, VisitQuiet = 6, CatFood = 150, CatKarma = 1, CatLostAfter = 12, CatSearch = 300;
    /// <summary>Neighbours: the squat kid's medicine, the door a loud night broke, the concierge's silence (high, luxury);
    /// Juno's bottle; the stranger's price for sending your hitters elsewhere (plus this much per point of notoriety).</summary>
    public const int KidMedicine = 300, NoiseDoor = 400, DiscretionHigh = 1000, DiscretionLuxury = 2500, JunoBottle = 150, KnockPay = 1000, KnockPayPer = 250;
    /// <summary>A crew retainer costs at least this much a week per threat level you are offered.</summary>
    public const int RetainerPerThreat = 600;
    /// <summary>Spur blades: they take the combat knife's place, heavier and slower.</summary>
    public const float SpurDamage = 65, SpurRate = 1.6f;
    /// <summary>The high-end hostiles, and the threat each first appears at.</summary>
    public static int FirstThreat(EnemyKind k) => k switch
    {
        EnemyKind.Cloaker or EnemyKind.Bulwark or EnemyKind.Spider => 5,
        EnemyKind.Barghest => 4,
        _ => 0,
    };
    /// <summary>The spider: hacks run this much slower while he's in the system (more once he knows you're there); every so many seconds, alert, he takes back a hacked device and re-locks a door.</summary>
    public const float SpiderSlow = 0.7f, SpiderSlowAlert = 0.5f, SpiderRetake = 6, SpiderRelock = 10;
    /// <summary>The ghost operative is only visible this close (thermographic eyes see it at 7, astral sight at 9), and for this long after it fires or is hit.</summary>
    public const float CloakSeen = 4, CloakReveal = 1.5f;
    /// <summary>The bulwark's shoulder slam: in reach, damage, how far it throws you, how often.</summary>
    public const float SlamReach = 1.6f, SlamDamage = 35, SlamPush = 1.8f, SlamCd = 3.5f;
    /// <summary>The barghest's howl: wind-up, reach, how long it freezes you, how often; how far it calls the pack (who run 30 % faster for a while).</summary>
    public const float HowlWind = 0.7f, HowlReach = 5, HowlFreeze = 1.1f, HowlCd = 9, HowlCalls = 12, FrenzyTime = 5;
    /// <summary>The hive: an egg sac hatches a worker after this long; the queen lays a new sac this often once she's roused.</summary>
    public const float EggHatch = 24, QueenLays = 11;
    /// <summary>A threat-6 Industrial job is a hive this often; it pays this much more; its workers get this share of a site's budget.</summary>
    public const float HiveChance = 0.2f, HivePay = 1.3f, HiveCrowd = 0.6f;
    /// <summary>Edge: at most this many points a run (1 + Charisma/3 + bought); karma for the first bought point (twice for the second); health after cheating death; invulnerable for this long after.</summary>
    public const int EdgeMax = 4, EdgeKarma = 15;
    public const float EdgeHeal = 0.33f, EdgeGrace = 1.2f;
    public const float RoadblockClear = 14, RoadblockSees = 12, RoadblockStop = 4, RoadblockWait = 2;
    /// <summary>The city's own streets: a pothole jolts above this speed (tiles/s) for this much hull; top speed on the causeway.</summary>
    public const float PotholeSpeed = 7, PotholeDamage = 1.5f, CausewaySpeed = 1.2f;
    /// <summary>Happenings on the street: stadium night's extra cars and fans; the gang war's wrecks; the motorcade's pace and the heat for ramming it.</summary>
    public const int StadiumCars = 24, StadiumFans = 40, WarWrecks = 4;
    public const float MotorcadeCruise = 5, MotorcadeHeat = 2;
    /// <summary>A street race: how near the finish counts as over the line; the hotshot's pace.</summary>
    public const float RaceFinish = 4, StreetRacerCruise = 11.5f;

    // ---- weapons ----------------------------------------------------------------
    public static readonly WeaponDef[] Weapons =
    {
        new() { Id = "hollow9", Name = "Hollow-9 pistol", Class = WeaponClass.Pistol, Damage = 18, Rate = 4, Mag = 12, Reload = 1.1f, Spread = 2.8f, Noise = 12, Cost = 600, Desc = "Reliable sidearm. Every runner's first friend." },
        new() { Id = "whisper", Name = "Whisper pistol", Class = WeaponClass.Pistol, Damage = 16, Rate = 4, Mag = 10, Reload = 1.2f, Spread = 4, Noise = 3, Cost = 1800, Desc = "Integral suppressor. Guards two rooms away hear nothing." },
        new() { Id = "kestrel", Name = "Kestrel SMG", Class = WeaponClass.Smg, Damage = 11, Rate = 12, Mag = 32, Reload = 1.6f, Spread = 9, Noise = 14, Cost = 2600, Desc = "Compact bullet hose." },
        new() { Id = "breacher", Name = "Breacher shotgun", Class = WeaponClass.Shotgun, Damage = 9, Rate = 1.4f, Mag = 6, Reload = 2.4f, Spread = 16, Pellets = 8, Speed = 40, Range = 14, Noise = 18, Cost = 3400, Desc = "Eight pellets. Opens drywall and faces alike." },
        new() { Id = "vanta", Name = "Vanta assault rifle", Class = WeaponClass.Rifle, Damage = 20, Rate = 8, Mag = 30, Reload = 2.0f, Spread = 5, Noise = 18, Cost = 6500, Loyalty = 1, Desc = "Corporate-grade rifle that fell off a truck." },
        new() { Id = "longshot", Name = "Longshot sniper", Class = WeaponClass.Sniper, Damage = 95, Rate = 0.8f, Mag = 5, Reload = 2.6f, Spread = 0.5f, Speed = 90, Range = 40, Noise = 22, Pierce = true, Cost = 9000, Loyalty = 2, Desc = "Punches through glass, drywall and whoever is behind it." },
        new() { Id = "mauler", Name = "Mauler LMG", Class = WeaponClass.Lmg, Damage = 17, Rate = 11, Mag = 80, Reload = 3.8f, Spread = 8, Noise = 20, Cost = 12000, Loyalty = 3, Desc = "Eighty rounds of argument." },
        new() { Id = "pike", Name = "Pike launcher", Class = WeaponClass.Launcher, Damage = 120, Rate = 0.6f, Mag = 1, Reload = 2.2f, Spread = 1, Speed = 15, Range = 30, Noise = 26, Blast = 2.4f, Cost = 15000, Loyalty = 4, Desc = "Rockets. For walls, cars, and arguments the LMG lost." },
        new() { Id = "taser", Name = "Taser", Class = WeaponClass.Pistol, Damage = 22, Rate = 1.5f, Mag = 4, Reload = 1.8f, Spread = 3, Speed = 30, Range = 9, Noise = 4, NonLethal = true, Stun = 2.5f, Cost = 1500, Desc = "Non-lethal. Stuns for 2.5 s; enough zaps and they sleep. No kills, no bodies -- well, sleeping ones." },
        new() { Id = "crossbow", Name = "Hush crossbow", Class = WeaponClass.Rifle, Damage = 70, Rate = 0.7f, Mag = 1, Reload = 1.3f, Spread = 1, Speed = 36, Range = 30, Noise = 1.5f, Cost = 4200, Loyalty = 1, Desc = "Near silent. One bolt, then a reload." },
        new() { Id = "hornet", Name = "Hornet smart-SMG", Class = WeaponClass.Smg, Damage = 12, Rate = 11, Mag = 36, Reload = 1.6f, Spread = 8, Noise = 13, Cost = 8500, Loyalty = 2, Desc = "With a smartlink, rounds curve towards the target nearest your aim." },
        new() { Id = "minigun", Name = "Cyclone minigun", Class = WeaponClass.Lmg, Damage = 12, Rate = 22, Mag = 200, Reload = 5f, Spread = 11, Noise = 24, Cost = 26000, Loyalty = 5, Desc = "Two hundred rounds of no." },
        new() { Id = "baton", Name = "Stun baton", Class = WeaponClass.Melee, Damage = 40, Rate = 2f, Noise = 1, Arc = 100, Reach = 1.1f, NonLethal = true, Stun = 1.5f, Cost = 900, Desc = "Non-lethal. Knocks them cold -- from behind, in one." },
        new() { Id = "thunder", Name = "Sal's Thunder", Class = WeaponClass.Lmg, Damage = 22, Rate = 10, Mag = 100, Reload = 3.2f, Spread = 6, Noise = 20, Cost = 0, Desc = "Hand-built by Big Sal. There is only one." },
        new() { Id = "knife", Name = "Combat knife", Class = WeaponClass.Melee, Damage = 30, Rate = 2.5f, Noise = 1, Arc = 90, Reach = 1.0f, Cost = 200, Desc = "Quiet." },
        new() { Id = "katana", Name = "Katana", Class = WeaponClass.Melee, Damage = 42, Rate = 1.8f, Noise = 1, Arc = 120, Reach = 1.3f, Cost = 2200, Desc = "Monomolecular edge, traditional grip." },
        // The top tier: Sal's back room, for runners with a name.
        new() { Id = "amr", Name = "Kodiak anti-materiel rifle", Class = WeaponClass.Sniper, Damage = 160, Rate = 0.5f, Mag = 5, Reload = 3f, Spread = 0.5f, Speed = 100, Range = 44, Noise = 28, Pierce = true, ArmorMul = 0.5f, Cost = 38000, Loyalty = 4, Desc = "Made for engine blocks. Through two walls, through armour, through the guard behind it." },
        new() { Id = "flamer", Name = "Incinerator flamethrower", Class = WeaponClass.Shotgun, Damage = 6, Rate = 6, Mag = 30, Reload = 3f, Spread = 18, Pellets = 4, Speed = 14, Range = 7, Noise = 10, Incendiary = true, Cost = 18000, Loyalty = 3, Desc = "A short, hungry cone of fire. The floor keeps burning." },
        new() { Id = "sonic", Name = "Screech sonic rifle", Class = WeaponClass.Shotgun, Damage = 12, Rate = 1.2f, Mag = 8, Reload = 2.2f, Spread = 20, Pellets = 5, Speed = 26, Range = 10, Noise = 6, NonLethal = true, Stun = 2f, Cost = 15000, Loyalty = 3, Desc = "Riot control: a wide cone of sound that drops a room without a drop of blood." },
        new() { Id = "missile", Name = "Yakusoku missile launcher", Class = WeaponClass.Launcher, Damage = 150, Rate = 0.5f, Mag = 4, Reload = 4f, Spread = 2, Speed = 18, Range = 34, Noise = 28, Blast = 3f, Homing = true, Cost = 45000, Loyalty = 5, Elite = true, Desc = "Four smart missiles that find the nearest hostile. Corporate heavy security calls it 'unsporting'." },
        new() { Id = "cannon", Name = "Ogre assault cannon", Class = WeaponClass.Launcher, Damage = 160, Rate = 0.7f, Mag = 6, Reload = 3.4f, Spread = 1.5f, Speed = 60, Range = 34, Noise = 30, Blast = 1.4f, Pierce = true, Cost = 55000, Loyalty = 4, Elite = true, Desc = "A shell the size of your forearm. Turrets, walls, doors, bosses: yes." },
        new() { Id = "rail", Name = "Kagero rail rifle", Class = WeaponClass.Rifle, Damage = 85, Rate = 1.2f, Mag = 10, Reload = 2.4f, Spread = 0.8f, Speed = 140, Range = 44, Noise = 12, Pierce = true, ArmorMul = 0.6f, Cost = 60000, Loyalty = 5, Elite = true, Desc = "Magnetic coils and a hum. Through walls, through armour, nearly silent." },
        new() { Id = "laser", Name = "Lancer heavy laser", Class = WeaponClass.Rifle, Damage = 32, Rate = 3, Mag = 12, Reload = 3f, Spread = 0.3f, Speed = 220, Range = 46, Noise = 4, ArmorMul = 0, Cost = 75000, Loyalty = 5, Elite = true, Desc = "Light, at a price. No recoil, no sound to speak of, and armour means nothing to it." },
        new() { Id = "monowhip", Name = "Monowhip", Class = WeaponClass.Melee, Damage = 70, Rate = 1.4f, Noise = 1, Arc = 150, Reach = 2.0f, Cost = 8000, Loyalty = 2, Desc = "A wire one molecule thick. Mind your fingers." },
        // Enemy-only.
        new() { Id = "e_pistol", Name = "pistol", Class = WeaponClass.Pistol, Damage = 9, Rate = 1.6f, Mag = 10, Reload = 1.6f, Spread = 6, Speed = 30, Noise = 12 },
        new() { Id = "e_smg", Name = "SMG", Class = WeaponClass.Smg, Damage = 6, Rate = 6, Mag = 24, Reload = 2.2f, Spread = 10, Speed = 30, Noise = 14 },
        new() { Id = "e_rifle", Name = "rifle", Class = WeaponClass.Rifle, Damage = 9, Rate = 4, Mag = 20, Reload = 2.4f, Spread = 6, Speed = 34, Noise = 18 },
        new() { Id = "e_shotgun", Name = "shotgun", Class = WeaponClass.Shotgun, Damage = 6, Rate = 0.9f, Mag = 6, Reload = 2.8f, Spread = 18, Pellets = 6, Speed = 28, Range = 10, Noise = 18 },
        new() { Id = "e_lmg", Name = "LMG", Class = WeaponClass.Lmg, Damage = 7, Rate = 7, Mag = 50, Reload = 3.5f, Spread = 11, Speed = 30, Noise = 20 },
        new() { Id = "e_sniper", Name = "sniper rifle", Class = WeaponClass.Sniper, Damage = 38, Rate = 0.35f, Mag = 4, Reload = 3f, Spread = 0.5f, Speed = 70, Range = 40, Noise = 22, Pierce = true },
        new() { Id = "e_drone", Name = "drone gun", Class = WeaponClass.Smg, Damage = 5, Rate = 4, Mag = 16, Reload = 2f, Spread = 8, Speed = 30, Noise = 8 },
        new() { Id = "e_turret", Name = "autocannon", Class = WeaponClass.Lmg, Damage = 8, Rate = 8, Mag = 30, Reload = 2.5f, Spread = 7, Speed = 36, Noise = 20 },
        new() { Id = "e_fireball", Name = "fireball", Class = WeaponClass.Launcher, Damage = 40, Rate = 0.28f, Mag = 1, Reload = 0.1f, Spread = 2, Speed = 9, Range = 18, Noise = 10, Blast = 1.8f },
        new() { Id = "a_shotgun", Name = "shotgun", Class = WeaponClass.Shotgun, Damage = 9, Rate = 1.1f, Mag = 8, Reload = 2.4f, Spread = 14, Pellets = 6, Speed = 34, Range = 12, Noise = 18 },
        new() { Id = "a_sniper", Name = "sniper rifle", Class = WeaponClass.Sniper, Damage = 70, Rate = 0.5f, Mag = 5, Reload = 2.6f, Spread = 0.5f, Speed = 80, Range = 36, Noise = 16, Pierce = true },
        new() { Id = "a_smg", Name = "SMG", Class = WeaponClass.Smg, Damage = 8, Rate = 7, Mag = 30, Reload = 1.8f, Spread = 8, Speed = 34, Noise = 12 },
        new() { Id = "e_bite", Name = "bite", Class = WeaponClass.Melee, Damage = 8, Rate = 1.4f, Noise = 2, Arc = 90, Reach = 0.9f },
        new() { Id = "e_fangs", Name = "fangs", Class = WeaponClass.Melee, Damage = 22, Rate = 1.1f, Noise = 2, Arc = 100, Reach = 1.15f },
        new() { Id = "e_mandible", Name = "mandibles", Class = WeaponClass.Melee, Damage = 38, Rate = 0.8f, Noise = 3, Arc = 140, Reach = 1.6f },
        new() { Id = "e_spirit", Name = "flame touch", Class = WeaponClass.Melee, Damage = 14, Rate = 1.2f, Noise = 2, Arc = 120, Reach = 1.1f },
        new() { Id = "e_pipe", Name = "pipe", Class = WeaponClass.Melee, Damage = 15, Rate = 1.0f, Noise = 3, Arc = 110, Reach = 1.2f },
        new() { Id = "e_blade", Name = "blade", Class = WeaponClass.Melee, Damage = 13, Rate = 1.2f, Noise = 1, Arc = 110, Reach = 1.1f },
    };

    public static readonly GrenadeDef[] Grenades =
    {
        new() { Id = "frag", Name = "Frag grenade", Kind = GrenadeKind.Frag, Radius = 3f, Damage = 90, Cost = 250, Desc = "Shrapnel and a big hole." },
        new() { Id = "flash", Name = "Flashbang", Kind = GrenadeKind.Flash, Radius = 4f, Damage = 0, Cost = 180, Desc = "Stuns everyone who sees it for 3 s." },
        new() { Id = "emp", Name = "EMP grenade", Kind = GrenadeKind.Emp, Radius = 4f, Damage = 0, Cost = 400, Loyalty = 1, Desc = "Drones drop. Turrets and cameras go dark for 8 s." },
        new() { Id = "fire", Name = "Incendiary", Kind = GrenadeKind.Fire, Radius = 2.5f, Damage = 15, Cost = 350, Loyalty = 1, Desc = "Burns the floor for 6 s. Crates catch." },
        new() { Id = "breach", Name = "Breach charge", Kind = GrenadeKind.Breach, Radius = 1.6f, Damage = 40, Fuse = 2.2f, Cost = 500, Loyalty = 2, Desc = "Shaped charge, placed at your feet. Walls hate it." },
    };

    public static readonly ArmorDef[] Armors =
    {
        new() { Id = "clothes", Name = "Street clothes", Armor = 0, Cost = 0, Desc = "Synthleather and optimism." },
        new() { Id = "jacket", Name = "Armoured jacket", Armor = 3, Cost = 1500, Desc = "Ballistic weave under the chrome trim." },
        new() { Id = "coat", Name = "Lined coat", Armor = 5, Cost = 4500, Loyalty = 1, Desc = "Long, black, and bullet-resistant." },
        new() { Id = "fullbody", Name = "Full body armour", Armor = 8, SpeedMul = 0.94f, Cost = 11000, Loyalty = 3, Desc = "Riot plates. Slower, much harder to kill." },
        new() { Id = "sneaksuit", Name = "Second-skin suit", Armor = 4, Stealth = 0.25f, Steps = 0.35f, Cost = 24000, Loyalty = 3, Desc = "Chameleon fibre and sound-dampening soles: spotted 25 % slower, footsteps near silent." },
        new() { Id = "moonsilver", Name = "Moonsilver coat", Armor = 4, Magic = 0.15f, Cost = 26000, Loyalty = 3, Desc = "Warded thread woven through the lining. Spells +15 %; Sabine had a hand in it." },
        new() { Id = "security", Name = "Security armour", Armor = 11, SpeedMul = 0.92f, Cost = 22000, Loyalty = 4, Desc = "What a Halcyon SWAT team wears, minus the logo." },
        new() { Id = "milspec", Name = "Mil-spec battle armour", Armor = 15, SpeedMul = 0.88f, Cost = 48000, Loyalty = 5, Elite = true, Desc = "Hardened military plate with a sealed helmet. You walk like a tank and die like one: slowly." },
    };

    public const int MedkitCost = 400;
    public const float MedkitHeal = 50;

    public static readonly ModDef[] Mods =
    {
        new() { Id = "silencer", Name = "Suppressor", Cost = 1200, Desc = "Noise x0.3, damage -5 %." },
        new() { Id = "extmag", Name = "Extended magazine", Cost = 900, Desc = "Magazine +50 %." },
        new() { Id = "laser", Name = "Laser sight", Cost = 700, Desc = "Spread -20 %." },
    };

    public static readonly (string id, string name, string desc, int cost, int loyalty)[] AmmoTypes =
    {
        ("gel", "Gel rounds", "Non-lethal: damage x0.7, knocks out instead of killing.", 1500, 0),
        ("apds", "APDS rounds", "Armour-piercing: armour counts for half.", 3500, 1),
        ("explosive", "Explosive rounds", "Damage x1.2, walls x2.5, but louder.", 5000, 2),
    };

    public static readonly CyberDef[] Cyber =
    {
        new() { Id = "eyes", Name = "Cybereyes", Essence = 0.2f, Cost = 3000, Desc = "Low-light optics. Field of view +3 tiles." },
        new() { Id = "thermal", Name = "Thermal overlay", Essence = 0.1f, Cost = 6000, Requires = "eyes", Loyalty = 1, Desc = "See warm bodies through walls (7 tiles)." },
        new() { Id = "smartlink", Name = "Smartlink", Essence = 0.3f, Cost = 8000, Loyalty = 1, Desc = "Gun-to-brain link. Spread -35 %, aim line." },
        new() { Id = "dermal", Name = "Dermal plating", Essence = 0.5f, Cost = 9000, Desc = "Armour +3 under the skin." },
        new() { Id = "muscle", Name = "Muscle replacement", Essence = 1.0f, Cost = 12000, Loyalty = 2, Desc = "Strength +2." },
        new() { Id = "bone", Name = "Bone lacing", Essence = 0.8f, Cost = 14000, Loyalty = 2, Desc = "Max HP +40, melee +30 %." },
        new() { Id = "synth", Name = "Synthacardium", Essence = 0.3f, Cost = 7000, Loyalty = 1, Desc = "Dash cooldown -40 %." },
        new() { Id = "spurs", Name = "Spur blades", Essence = 0.3f, Cost = 5000, Desc = "Forearm blades that replace your combat knife: melee 65 a hit, 1.6 a second." },
        new() { Id = "wired", Name = "Wired reflexes", Essence = 2.0f, Cost = 25000, Loyalty = 3, Grants = Ability.Overclock, Desc = "Ability Overclock: the world slows to a third for 4 s." },
        new() { Id = "deck", Name = "Cyberdeck implant", Essence = 0.5f, Cost = 15000, Loyalty = 1, Grants = Ability.Hack, Desc = "Ability Hack: turn a turret or drone, kill a camera, open a door at range." },
        new() { Id = "booster", Name = "Ichikawa reflex booster", Essence = 0.4f, Cost = 0, Loyalty = 9, Desc = "Reaction +2. Doc Ichikawa's own design; not for sale." },
        new() { Id = "controlrig", Name = "Control rig", Essence = 1.0f, Cost = 18000, Loyalty = 2, Desc = "A rigger's datajack: drones fly twice as far from you, tougher (+50 % hull) and hit harder (+30 %)." },
        new() { Id = "platelet", Name = "Platelet factories", Essence = 0.2f, Cost = 17000, Loyalty = 2, Bio = true, Desc = "Bioware. Below half health your blood clots fast: back 1 HP a second." },
        new() { Id = "cerebral", Name = "Cerebral booster", Essence = 0.4f, Cost = 31500, Loyalty = 3, Bio = true, Desc = "Bioware. Logic +1 (hacking, spells)." },
        new() { Id = "painedit", Name = "Pain editor", Essence = 0.3f, Cost = 48000, Loyalty = 4, Bio = true, Desc = "Bioware. Injuries no longer cost you maximum health." },
        new() { Id = "adrenaline", Name = "Adrenaline pump", Essence = 0.75f, Cost = 55000, Loyalty = 4, Bio = true, Desc = "Bioware. Below 40 % health: +25 % speed, +25 % damage." },
        new() { Id = "synaptic", Name = "Synaptic booster", Essence = 0.5f, Cost = 60000, Loyalty = 4, Bio = true, Desc = "Bioware. Reaction +1, and every cooldown 10 % shorter." },
        new() { Id = "controlrig2", Name = "Control rig II", Essence = 0.5f, Cost = 97000, Requires = "controlrig", Loyalty = 5, Elite = true, Desc = "Drones fly half as far again from you, and fight with a rigger's reflexes (+30 % more damage)." },
        new() { Id = "wired2", Name = "Wired reflexes II", Essence = 1.0f, Cost = 120000, Requires = "wired", Loyalty = 5, Elite = true, Desc = "Overclock lasts 6 s and comes back 30 % sooner." },
        new() { Id = "movebywire", Name = "Move-by-wire", Essence = 2.5f, Cost = 150000, Loyalty = 5, Elite = true, Desc = "Military reflex wiring: +15 % speed, dash back twice as fast." },
        new() { Id = "ruthenium", Name = "Ruthenium skin", Essence = 1.0f, Cost = 22000, Loyalty = 3, Grants = Ability.Cloak, Desc = "Ability Cloak: unseen for 5 s, or until you fire." },
    };

    public static readonly ProgramDef[] Programs =
    {
        new() { Id = "breaker", Name = "Icebreaker", Cost = 4000, Mp = 2, Desc = "Hacking (doors, mainframes) +50 % faster; firewall gates in the Matrix crack 50 % faster." },
        new() { Id = "spoof", Name = "Spoof", Cost = 6000, Loyalty = 1, Mp = 2, Desc = "Cameras ignore you. In the Matrix the first full trace hits a decoy, and traces never raise the alarm." },
        new() { Id = "daemon", Name = "Daemon", Cost = 9000, Loyalty = 2, Mp = 2, Desc = "Hacked turrets and drones stay yours for the whole run." },
        new() { Id = "ghost", Name = "Ghost protocol", Cost = 0, Loyalty = 9, Mp = 1, Desc = "Null's own: cameras ignore you, and IC moves 30 % slower." },
        new() { Id = "lamplight", Name = "Lamplight", Cost = 0, Loyalty = 99, Mp = 0, Desc = "A gift from something that isn't anyone: every hack 25 % faster, and once a run it pulls you out of the Matrix before a trace lands." },
        new() { Id = "overwatch", Name = "Overwatch", Cost = 7000, Loyalty = 2, Mp = 1, Desc = "Pings every hostile on the site for 6 s when you go in." },
        new() { Id = "hammer", Name = "Hammer", Cost = 5000, Mp = 2, Desc = "Matrix: your spikes hit 40 % harder." },
        new() { Id = "armor", Name = "Armor", Cost = 5000, Mp = 2, Desc = "Matrix: every IC hit on your deck does a box less." },
        new() { Id = "decrypt", Name = "Decryption", Cost = 3000, Mp = 1, Desc = "Matrix: encrypted files and firewall gates open twice as fast." },
        new() { Id = "sneak", Name = "Sneak", Cost = 7000, Loyalty = 1, Mp = 2, Desc = "Matrix: patrol IC only sees you on its own node, and the trace builds 25 % slower." },
        new() { Id = "biofilter", Name = "Biofeedback filter", Cost = 6000, Loyalty = 1, Mp = 1, Desc = "Matrix: black IC and spiders hurt your body half as much." },
        new() { Id = "patch", Name = "Patch", Cost = 4000, Loyalty = 1, Mp = 1, Active = true, Desc = "Matrix, on a key: once a jack-in, repair 4 boxes of your deck." },
        new() { Id = "scrub", Name = "Signal scrub", Cost = 8000, Loyalty = 2, Mp = 2, Active = true, Desc = "Matrix, on a key: once a jack-in, wipe 40 % off the trace." },
        new() { Id = "decoy", Name = "Decoy", Cost = 7000, Loyalty = 2, Mp = 2, Active = true, Desc = "Matrix, on a key: every 25 s, a copy of your icon that hunting IC chases for 6 s." },
    };

    public static readonly DroneDef[] Drones =
    {
        new() { Id = "flyspy", Name = "Fly-spy", Desc = "A fast scout rotor. No gun -- but everything it sees, you see.", Cost = 6000, Hp = 40, Speed = 6 },
        new() { Id = "medicart", Name = "MediCart", Desc = "A medical drone: no gun, but it patches you up for 15 every 6 s while it's near you.", Cost = 30000, Loyalty = 3, Hp = 90, Speed = 4.2f },
        new() { Id = "lynx", Name = "Steel Lynx", Desc = "A combat drone with a light machine gun and armour plate.", Cost = 45000, Loyalty = 4, Hp = 170, Speed = 3.6f, Weapon = "e_lmg" },
        new() { Id = "juggernaut", Name = "Juggernaut", Desc = "An anthroform bodyguard drone: three hundred hull and a shotgun. (Prestige.)", Cost = 100000, Loyalty = 5, Hp = 300, Speed = 3.2f, Weapon = "a_shotgun" },
        new() { Id = "doberman", Name = "Doberman", Desc = "An armed drone with an SMG. Fly it in, or let it guard your back.", Cost = 14000, Loyalty = 2, Hp = 80, Speed = 4.5f, Weapon = "e_drone" },
    };

    public static readonly SpellDef[] Spells =
    {
        new() { Id = "manabolt", Name = "Manabolt", Grants = Ability.Manabolt, Cost = 4000, Desc = "Spell: raw mana tears into one target you can see. Ignores armour; useless on machines." },
        new() { Id = "heal", Name = "Heal", Grants = Ability.Heal, Cost = 5000, Desc = "Spell: knit flesh where you aim -- you and anyone on your side in the circle. Restores health." },
        new() { Id = "fireball", Name = "Fireball", Grants = Ability.Fireball, Cost = 9000, Loyalty = 1, Desc = "Spell: a ball of fire that bursts where it lands. Walls and crates suffer too." },
        new() { Id = "astral", Name = "Astral sight", Grants = Ability.Astral, Cost = 6000, Loyalty = 1, Desc = "Spell: for 8 s see every living thing within 14 tiles, walls or no walls." },
        new() { Id = "summon", Name = "Summon fire spirit", Grants = Ability.Summon, Cost = 16000, Loyalty = 2, Desc = "Spell: a fire spirit fights beside you for 30 s, burning what it touches." },
        new() { Id = "projection", Name = "Astral projection", Grants = Ability.Project, Cost = 12000, Loyalty = 2, Desc = "Spell: leave your body for 15 s and drift through walls, seeing every living thing near you. Your body stands still; a hit pulls you back." },
        new() { Id = "ward", Name = "Ward", Grants = Ability.Ward, Cost = 10000, Loyalty = 1, Desc = "Spell: a barrier of light across a corridor for 10 s. Nothing gets through it -- bullets included." },
        new() { Id = "invisibility", Name = "Invisibility", Grants = Ability.Cloak, Cost = 14000, Loyalty = 2, Desc = "Spell: unseen for 5 s, or until you fire. Cameras too." },
    };

    /// <summary>How far an aimed spell reaches (Manabolt, Heal); a fireball's flight and burst; the circle a heal knits.</summary>
    public const float SpellRange = 15, FireballRange = 16, FireballBlast = 2.2f, HealRadius = 2f;

    public const int FocusCost = 12000;

    /// <summary>Power focus ranks after the first: (name, price, spell power).</summary>
    public static readonly (string name, int cost, float power, int loyalty)[] FocusRanks =
    {
        ("Power focus", 12000, 1.25f, 1), ("Power focus II", 36000, 1.4f, 3), ("Power focus III", 90000, 1.6f, 5),
    };

    /// <summary>Cyberware grades: (id, name, essence multiplier, price multiplier, doc trust).</summary>
    public static readonly (string id, string name, float essence, float price, int loyalty)[] Grades =
    {
        ("standard", "Standard", 1f, 1f, 0), ("alpha", "Alphaware", 0.8f, 5f, 2), ("beta", "Betaware", 0.7f, 10f, 4), ("delta", "Deltaware", 0.5f, 20f, 5),
    };

    /// <summary>Torque's car work: (id, name, what it does, price, trust, the one it replaces).</summary>
    public static readonly (string id, string name, string desc, int cost, int loyalty, string replaces)[] CarMods =
    {
        ("engine1", "Tuned engine", "Top speed +8 %.", 3000, 0, null),
        ("engine2", "Rebuilt engine", "Top speed +16 %.", 10000, 2, "engine1"),
        ("engine3", "Race engine", "Top speed +25 %.", 30000, 4, "engine2"),
        ("accel1", "Turbo", "Acceleration +15 %.", 4000, 0, null),
        ("accel2", "Twin turbo", "Acceleration +30 %.", 15000, 3, "accel1"),
        ("handling1", "Sport suspension", "Grip and steering +5 %.", 3000, 0, null),
        ("handling2", "Race suspension", "Grip and steering +10 %.", 12000, 2, "handling1"),
        ("armour1", "Armour plate", "Hull +25 %, top speed -3 %.", 2000, 0, null),
        ("armour2", "Heavy plate", "Hull +50 %, top speed -6 %.", 6000, 1, "armour1"),
        ("armour3", "Military plate", "Hull +100 %, top speed -10 %.", 20000, 3, "armour2"),
        ("runflat", "Run-flat tyres", "Crashes do 30 % less damage.", 1500, 0, null),
        ("offroad", "Off-road tyres", "Parks and kerbs at full speed.", 1200, 0, null),
        ("ram", "Ram plate", "Ramming: you take half, they take double.", 5000, 1, null),
        ("nitro", "Nitro", "Shift: a 3 s burst, +40 % top speed and acceleration. Three a drive.", 8000, 1, null),
        ("smoke", "Smoke projector", "Q: a smoke screen. Chasers behind you lose sight of you.", 3000, 1, null),
        ("oil", "Oil slick sprayer", "G: oil behind you. Whoever drives into it spins out.", 4000, 2, null),
        ("guns", "Weapon mount", "Twin SMGs that fire at any chaser in front of you.", 15000, 3, null),
        ("plates", "Morphing licence plates", "Lone Star loses track of you: heat cools 50 % faster.", 6000, 1, null),
        ("ecm", "ECM suite", "Patrols spot you closer (6 tiles, not 10) and lose you sooner.", 10000, 2, null),
        ("nano", "Nanomaintenance", "Repairs the hull while you drive (1 every 3 s).", 20000, 3, null),
        ("smuggle", "Smuggling compartment", "Busts are half the fine and never find a package.", 7500, 1, null),
    };

    public static readonly VehicleDef[] Vehicles =
    {
        new() { Id = "junker", Name = "Junker sedan", Top = 11, Accel = 6, Grip = 0.85f, Hp = 100, Cost = 2000, Colour = 0x8a6f4dff, Desc = "Rust-coloured, rust-made. Starts most of the time." },
        new() { Id = "bike", Name = "Vector bike", Top = 15, Accel = 10, Grip = 0.9f, Hp = 60, Cost = 6000, Colour = 0x30e0ffff, Bike = true, Length = 1.3f, Width = 0.5f, Desc = "Fast and thin. Slips through traffic. Every bike: +20 % courier time, but crashes and bullets hurt more." },
        new() { Id = "scrambler", Name = "Dirt scrambler", Top = 12, Accel = 10, Grip = 0.88f, Hp = 70, Cost = 7500, Colour = 0xd0a020ff, Bike = true, Offroad = true, Length = 1.25f, Width = 0.5f, Desc = "Knobbly tyres: parks and kerbs at full speed." },
        new() { Id = "harrow", Name = "Harrow chopper", Top = 13, Accel = 8, Grip = 0.92f, Hp = 110, Cost = 11000, Loyalty = 1, Colour = 0x202024ff, Bike = true, Length = 1.5f, Width = 0.55f, Desc = "Chrome, leather, a V-twin. Tough, for a bike." },
        new() { Id = "kuro", Name = "Kuro R9", Top = 18, Accel = 12, Grip = 0.86f, Hp = 50, Cost = 16000, Loyalty = 2, Colour = 0xff2a6dff, Bike = true, Length = 1.35f, Width = 0.5f, Desc = "A sport bike with a death wish. The fastest thing on the road." },
        new() { Id = "van", Name = "Hauler van", Top = 9, Accel = 4.5f, Grip = 0.8f, Hp = 220, Cost = 9000, Loyalty = 1, Colour = 0xd8d8d0ff, Length = 2.3f, Width = 1.1f, Desc = "Room in the back: +1 of every grenade type you own on each run." },
        new() { Id = "raptor", Name = "Raptor GT", Top = 17, Accel = 9, Grip = 0.92f, Hp = 120, Cost = 28000, Loyalty = 2, Colour = 0xff2a6dff, Desc = "Low, loud, very fast." },
        new() { Id = "brotherbike", Name = "Your brother's bike", Top = 16, Accel = 11, Grip = 0.9f, Hp = 90, Cost = 0, Loyalty = 9, Colour = 0xe0402aff, Bike = true, Length = 1.4f, Width = 0.5f, Desc = "Rebuilt by Torque from what the Vipers kept. Not for sale." },
        new() { Id = "interceptor", Name = "Torque's Interceptor", Top = 17, Accel = 11, Grip = 0.93f, Hp = 180, Cost = 0, Loyalty = 9, Colour = 0x101418ff, Desc = "Torque's masterpiece, with a stolen Halcyon engine. Not for sale." },
        new() { Id = "gladius", Name = "Gladius supercar", Top = 20, Accel = 13, Grip = 0.95f, Hp = 140, Cost = 120000, Loyalty = 5, Colour = 0xffd040ff, Desc = "Nothing on the road catches it. Nothing on the road is meant to." },
        new() { Id = "rhino", Name = "Rhino security car", Top = 12, Accel = 6, Grip = 0.86f, Hp = 650, Cost = 150000, Loyalty = 5, Elite = true, Colour = 0x2a3038ff, Length = 2.4f, Width = 1.2f, Desc = "A corporate VIP car: armoured, run-flat, and built to push through anything." },
        new() { Id = "kite", Name = "Aerodyne Kite helicopter", Top = 11, Accel = 6, Grip = 0.85f, Hp = 100, Cost = 400000, Loyalty = 5, Air = true, Elite = true, Colour = 0x303a44ff, Desc = "Your own helicopter, on the roof of your building. Fly anywhere in the city for free (PDA), and towers have a roof to be picked up from. (It doesn't drive; your car stays your car.)" },
        new() { Id = "bulldog", Name = "Bulldog armoured", Top = 11, Accel = 5, Grip = 0.85f, Hp = 400, Cost = 40000, Loyalty = 4, Colour = 0x4a5a3aff, Length = 2.2f, Width = 1.1f, Desc = "Corporate security surplus. Traffic gets out of the way." },
    };

    public static readonly EnemyDef[] Enemies =
    {
        new() { Kind = EnemyKind.Ganger, Name = "Viper ganger", Hp = 45, Speed = 3.2f, Aim = 7, Budget = 1, Weapon = "e_pistol", Cash = 60, PreferRange = 5 },
        new() { Kind = EnemyKind.Hound, Name = "Hellhound", Hp = 35, Speed = 5.2f, Aim = 0, Budget = 1, Weapon = "e_bite", Melee = true, Radius = 0.28f, Vision = 9 },
        new() { Kind = EnemyKind.Mage, Name = "Street mage", Hp = 55, Speed = 2.8f, Aim = 3, Budget = 2.5f, Weapon = "e_fireball", Cash = 150, PreferRange = 8 },
        new() { Kind = EnemyKind.Guard, Name = "Security guard", Hp = 75, Armor = 2, Speed = 3.0f, Aim = 4, Budget = 1.5f, Weapon = "e_smg", Cash = 80, PreferRange = 6 },
        new() { Kind = EnemyKind.Blade, Name = "Lotus blade", Hp = 80, Armor = 1, Speed = 4.4f, Aim = 0, Budget = 1.5f, Weapon = "e_blade", Melee = true, Cash = 100 },
        new() { Kind = EnemyKind.Heavy, Name = "Heavy trooper", Hp = 170, Armor = 6, Speed = 2.2f, Aim = 5, Budget = 3, Weapon = "e_lmg", Radius = 0.38f, Cash = 150, PreferRange = 5 },
        new() { Kind = EnemyKind.Sniper, Name = "Sniper", Hp = 60, Armor = 1, Speed = 2.6f, Aim = 1, Budget = 2, Weapon = "e_sniper", Vision = 16, Cash = 120, PreferRange = 12 },
        new() { Kind = EnemyKind.Drone, Name = "Security drone", Hp = 40, Armor = 1, Speed = 4.0f, Aim = 5, Budget = 2, Weapon = "e_drone", Flying = true, Radius = 0.3f, PreferRange = 5 },
        new() { Kind = EnemyKind.Turret, Name = "Turret", Hp = 130, Armor = 4, Speed = 0, Aim = 4, Budget = 2.5f, Weapon = "e_turret", Static = true, Radius = 0.4f, Vision = 11 },
        new() { Kind = EnemyKind.Camera, Name = "Camera", Hp = 15, Speed = 0, Budget = 0.5f, Static = true, Radius = 0.22f, Vision = 8 },
        new() { Kind = EnemyKind.Shield, Name = "Shield trooper", Hp = 110, Armor = 3, Speed = 2.3f, Aim = 5, Budget = 2.5f, Weapon = "e_pistol", Radius = 0.36f, Cash = 120, PreferRange = 3 },
        new() { Kind = EnemyKind.Rigger, Name = "Combat rigger", Hp = 50, Armor = 1, Speed = 2.8f, Aim = 6, Budget = 2.5f, Weapon = "e_smg", Cash = 160, PreferRange = 9 },
        new() { Kind = EnemyKind.Boss, Name = "Boss", Hp = 520, Armor = 6, Speed = 2.6f, Aim = 4, Budget = 0, Weapon = "e_lmg", Radius = 0.5f, Vision = 13, Cash = 1500, PreferRange = 5 },
        new() { Kind = EnemyKind.Spirit, Name = "Bound spirit", Hp = 120, Armor = 0, Speed = 3.6f, Aim = 0, Budget = 3, Weapon = "e_spirit", Melee = true, Flying = true, Radius = 0.36f, Vision = 11 },
        new() { Kind = EnemyKind.Bruiser, Name = "Bruiser", Hp = 75, Armor = 1, Speed = 3.7f, Aim = 0, Budget = 1.2f, Weapon = "e_pipe", Melee = true, Radius = 0.36f, Cash = 70 },
        new() { Kind = EnemyKind.Firebug, Name = "Firebug", Hp = 50, Speed = 3.2f, Aim = 7, Budget = 1.5f, Weapon = "e_pistol", Cash = 80, PreferRange = 7 },
        // The high end: a security decker, a cloaked operative, powered armour, a howling pack leader, a hive.
        new() { Kind = EnemyKind.Spider, Name = "Security spider", Hp = 55, Armor = 1, Speed = 0, Aim = 5, Budget = 3, Weapon = "e_pistol", Static = true, Vision = 9, Cash = 400 },
        new() { Kind = EnemyKind.Cloaker, Name = "Ghost operative", Hp = 75, Armor = 2, Speed = 4.2f, Aim = 3, Budget = 3, Weapon = "e_smg", Cash = 250, PreferRange = 4, Vision = 11 },
        new() { Kind = EnemyKind.Bulwark, Name = "Bulwark", Hp = 360, Armor = 14, Speed = 1.9f, Aim = 5, Budget = 5, Weapon = "e_lmg", Radius = 0.46f, Cash = 300, PreferRange = 4 },
        new() { Kind = EnemyKind.Barghest, Name = "Barghest", Hp = 150, Armor = 2, Speed = 5.6f, Aim = 0, Budget = 3.5f, Weapon = "e_fangs", Melee = true, Radius = 0.38f, Vision = 11 },
        new() { Kind = EnemyKind.HiveQueen, Name = "Hive queen", Hp = 700, Armor = 4, Speed = 1.4f, Aim = 0, Budget = 0, Weapon = "e_mandible", Melee = true, Radius = 0.62f, Vision = 12 },
        new() { Kind = EnemyKind.HiveWorker, Name = "Hive worker", Hp = 32, Speed = 5.0f, Aim = 0, Budget = 0.8f, Weapon = "e_bite", Melee = true, Radius = 0.26f, Vision = 9 },
        new() { Kind = EnemyKind.EggSac, Name = "Egg sac", Hp = 45, Speed = 0, Budget = 0.6f, Static = true, Radius = 0.3f, Vision = 0 },
        new() { Kind = EnemyKind.Vip, Name = "Target", Hp = 80, Armor = 2, Speed = 3.4f, Aim = 6, Budget = 0, Weapon = "e_pistol", Cash = 500, PreferRange = 9 },
    };

    public static readonly UpgradeDef[] Upgrades =
    {
        new() { Id = "rig1", Name = "Matrix rig I", Cost = 6000, Desc = "+2 jobs on the board." },
        new() { Id = "rig2", Name = "Matrix rig II", Cost = 18000, Requires = "rig1", Desc = "+2 more jobs, and free site schematics: every run starts with the map known." },
        new() { Id = "medbay", Name = "Med bay", Cost = 8000, Desc = "+1 medkit on every run. TraumaLink 30 % cheaper." },
        new() { Id = "armory", Name = "Armory", Cost = 7000, Desc = "+1 grenade carried of each type, one free frag every run." },
        new() { Id = "garage", Name = "Garage", Cost = 5000, Desc = "Free repairs at home. Keep up to five vehicles." },
        new() { Id = "training", Name = "Training room", Cost = 12000, Desc = "Karma costs -15 %." },
        new() { Id = "loft", Name = "Spire loft", Cost = 30000, Requires = "rig1", Desc = "Move to a loft in the Spire. +1 street rep every 5 days, the rich jobs are next door." },
    };

    public static readonly ContactDef[] Contacts =
    {
        new() { Id = "fixer", Name = "Juno Vance", Role = "Fixer", Venue = "The Rusty Halo", District = "strip", Colour = 0xff3aa0ff,
            Blurb = "Brokers jobs between people who never meet. Takes ten percent and remembers everything.",
            Greetings = new[] { "Sit. Drink. Then we talk business.", "You look alive. That's good for both of us.", "Word on the street is you deliver. Keep it that way." } },
        new() { Id = "doc", Name = "Doc Ichikawa", Role = "Street doc", Venue = "Ichikawa Clinic", District = "docks", Colour = 0x40ffb0ff,
            Blurb = "Ex-corporate surgeon. Installs chrome no questions asked, sells TraumaLink pickups.",
            Greetings = new[] { "Lie down. Don't touch anything.", "Your essence is your business. Your bill is mine.", "Back in one piece? Let's see how many." } },
        new() { Id = "arms", Name = "Big Sal", Role = "Arms dealer", Venue = "Sal's Surplus", District = "industrial", Colour = 0xffb030ff,
            Blurb = "Sells anything that goes bang, and some things that go bang twice.",
            Greetings = new[] { "Everything's legal if you don't ask.", "Got a new crate in. Don't ask from where.", "Cash first, then we're friends." } },
        new() { Id = "rigger", Name = "Torque", Role = "Rigger", Venue = "Torque's Chop Shop", District = "barrens", Colour = 0x60a0ffff,
            Blurb = "Builds cars out of other people's cars. Fixes yours for a price.",
            Greetings = new[] { "What did you do to my baby?", "Engines don't lie. People do.", "Got something with real torque out back." } },
        new() { Id = "decker", Name = "Null", Role = "Decker", Venue = "The Data Haven", District = "spire", Colour = 0x9a6bffff,
            Blurb = "Lives in the Matrix, visits meatspace under protest. Sells decks, programs and site schematics; buys paydata.",
            Greetings = new[] { "Your commlink is a sieve. Want better?", "I don't do small talk. I do packets.", "The corps changed their ICE. I didn't." } },
        new() { Id = "talismonger", Name = "Madame Sabine", Role = "Talismonger", Venue = "The Moonlit Charm", District = "strip", Colour = 0xb070ffff,
            Blurb = "Sells reagents, fetishes and formulae to the Awakened. Pretends not to know what you are.",
            Greetings = new[] { "The spirits said you'd come. They say a lot of things.", "Chrome dulls the gift, darling. Remember that.", "Touch nothing that hums." } },
        new() { Id = "fence", Name = "Old Wen", Role = "Fence", Venue = "Wen's Pawn", District = "barrens", Colour = 0xe0e070ff,
            Blurb = "Buys whatever you carried out, at a price that's almost fair.",
            Greetings = new[] { "Show me what you found.", "Hot goods cool down fast in here.", "Ah. My favourite supplier." } },
        new() { Id = "cop", Name = "Sgt. Dace", Role = "Lone Star sergeant", Venue = "The Blue Lamp", District = "docks", Colour = 0x5a8cffff, Late = true,
            Blurb = "Wears the badge, sells what it's worth: your file, the precinct's plans, and a blind eye now and then.",
            Greetings = new[] { "Sit where the cameras can't see you.", "Your file's getting thick. Thick costs extra.", "I never met you. Talk fast." } },
        new() { Id = "press", Name = "Mara Quill", Role = "KBN stringer", Venue = "KBN Night Desk", District = "strip", Colour = 0xf0d890ff, Late = true,
            Blurb = "Connects the jobs nobody else connects. Buys footage, kills stories, plants them -- and never names a source.",
            Greetings = new[] { "Off the record? Everything's off the record.", "Tell me something true. It's been a long week.", "You're the story. You know that, right?" } },
    };

    public static readonly DistrictDef[] Districts =
    {
        new() { Id = "docks", Name = "Docks", MinThreat = 1, MaxThreat = 2, Styles = new[] { SiteStyle.Warehouse, SiteStyle.Warehouse, SiteStyle.Den }, Factions = new[] { Faction.Vipers, Faction.Lotus }, Colour = 0x3fa0c0ff, MinHeight = 0.6f, MaxHeight = 1.4f },
        new() { Id = "barrens", Name = "Barrens", MinThreat = 1, MaxThreat = 3, Styles = new[] { SiteStyle.Den, SiteStyle.Den, SiteStyle.Warehouse }, Factions = new[] { Faction.Vipers, Faction.Vipers, Faction.Lotus }, Colour = 0xc06a3aff, MinHeight = 0.4f, MaxHeight = 1.2f },
        new() { Id = "strip", Name = "Neon Strip", MinThreat = 1, MaxThreat = 4, Styles = new[] { SiteStyle.Club, SiteStyle.Club, SiteStyle.Den }, Factions = new[] { Faction.Lotus, Faction.Vipers }, Colour = 0xff3aa0ff, MinHeight = 1.0f, MaxHeight = 2.6f },
        new() { Id = "industrial", Name = "Industrial", MinThreat = 1, MaxThreat = 4, Styles = new[] { SiteStyle.Lab, SiteStyle.Warehouse }, Factions = new[] { Faction.Halcyon, Faction.Lotus }, Colour = 0xd0b040ff, MinHeight = 0.8f, MaxHeight = 1.8f },
        new() { Id = "spire", Name = "Spire", MinThreat = 3, MaxThreat = 5, Styles = new[] { SiteStyle.Office, SiteStyle.Office, SiteStyle.Lab }, Factions = new[] { Faction.Halcyon }, Colour = 0x60e0ffff, MinHeight = 2.4f, MaxHeight = 5.0f },
    };

    public static readonly string[] FactionNames = { "Ash Vipers", "Halcyon-Matsuda", "Red Lotus" };

    /// <summary>Enemy kinds a faction fields, with weights.</summary>
    public static (EnemyKind kind, float weight)[] Roster(Faction f) => f switch
    {
        Faction.Vipers => new[] { (EnemyKind.Ganger, 6f), (EnemyKind.Hound, 2f), (EnemyKind.Bruiser, 2f), (EnemyKind.Firebug, 1.2f), (EnemyKind.Mage, 1f), (EnemyKind.Heavy, 0.6f), (EnemyKind.Barghest, 0.6f) },
        Faction.Halcyon => new[] { (EnemyKind.Guard, 6f), (EnemyKind.Hound, 1.2f), (EnemyKind.Heavy, 1.4f), (EnemyKind.Sniper, 1f), (EnemyKind.Drone, 1.5f), (EnemyKind.Shield, 1f), (EnemyKind.Rigger, 0.8f), (EnemyKind.Cloaker, 0.8f), (EnemyKind.Bulwark, 0.5f) },
        _ => new[] { (EnemyKind.Guard, 4f), (EnemyKind.Blade, 3f), (EnemyKind.Bruiser, 1.5f), (EnemyKind.Mage, 1.2f), (EnemyKind.Heavy, 1f), (EnemyKind.Spirit, 0.7f) },
    };

    /// <summary>Runners for hire at the fixer: who, what they do, their cut of the pay.</summary>
    public static readonly (string id, string name, string desc, EnemyDef def, float cut, int loyalty)[] Hires =
    {
        ("ox", "Ox", "Troll street samurai. A shotgun and a lot of body to hide behind.", new EnemyDef { Kind = EnemyKind.Ally, Name = "Ox", Hp = 240, Armor = 4, Speed = 3.6f, Aim = 3, Weapon = "a_shotgun", Radius = 0.38f, Vision = 12, PreferRange = 3 }, 0.25f, 0),
        ("wisp", "Wisp", "Elf sniper. Picks guards off from across the room, quietly-ish.", new EnemyDef { Kind = EnemyKind.Ally, Name = "Wisp", Hp = 100, Armor = 1, Speed = 3.8f, Aim = 1, Weapon = "a_sniper", Vision = 16, PreferRange = 10 }, 0.25f, 1),
        ("patch", "Patch", "Combat medic. Patches you up three times a run, shoots back otherwise.", new EnemyDef { Kind = EnemyKind.Ally, Name = "Patch", Hp = 130, Armor = 2, Speed = 3.8f, Aim = 4, Weapon = "a_smg", Vision = 12, PreferRange = 6 }, 0.2f, 2),
        ("ade", "Ade", "Ichikawa's night nurse, who you carried out of a Lotus club. Four quick patch-ups a run; not much of a shot.", new EnemyDef { Kind = EnemyKind.Ally, Name = "Ade", Hp = 110, Armor = 1, Speed = 4f, Aim = 6, Weapon = "a_smg", Vision = 11, PreferRange = 6 }, 0.15f, 0),
        ("byte", "Byte", "The kid Null taught, who you got out of the Lotus books. Blinds cameras, turrets and drones near you, a few at a time.", new EnemyDef { Kind = EnemyKind.Ally, Name = "Byte", Hp = 90, Armor = 0, Speed = 4.1f, Aim = 5, Weapon = "a_smg", Vision = 12, PreferRange = 7 }, 0.2f, 0),
        ("rook", "Rook", "The rival crew's planner, who came over after their Johnson burned them. Quick, careful, a steady hand on an SMG; knows how other crews think.", new EnemyDef { Kind = EnemyKind.Ally, Name = "Rook", Hp = 150, Armor = 2, Speed = 4.2f, Aim = 2, Weapon = "a_smg", Vision = 14, PreferRange = 5 }, 0.2f, 0),
    };

    /// <summary>The name and gun of each faction's boss.</summary>
    public static (string name, string weapon) BossOf(Faction f) => f switch
    {
        Faction.Vipers => ("Big Mamba", "e_shotgun"),
        Faction.Lotus => ("The Jade Widow", "e_fireball"),
        _ => ("Iron Saint", "e_lmg"),
    };

    public static readonly string[] TargetNames =
    {
        "Kenji 'Slab' Moreau", "Vera Lindqvist", "Dr. Amon Pryce", "Tomas Szabo", "Mother Kestrel", "Ivo Castellane",
        "Haruka Delacroix", "Brick Okonkwo", "Selene Marsh", "Rafe Ostrowski", "The Glass Duchess", "Mr. Ten",
    };

    public static readonly string[] Items =
    {
        "prototype cyberarm", "encrypted datachip", "BTL master copy", "sealed briefcase", "experimental neural chip",
        "stolen drone core", "ledger of bribes", "sample case of nanites",
    };


    public static WeaponDef Weapon(string id) => Weapons.First(w => w.Id == id);
    public static GrenadeDef Grenade(string id) => Grenades.First(g => g.Id == id);
    public static ArmorDef Armor(string id) => Armors.FirstOrDefault(a => a.Id == id) ?? Armors[0];
    public static CyberDef CyberById(string id) => Cyber.First(c => c.Id == id);
    public static VehicleDef Vehicle(string id) => Vehicles.FirstOrDefault(v => v.Id == id) ?? Vehicles[0];
    public static EnemyDef Enemy(EnemyKind k) => Enemies.First(e => e.Kind == k);
    public static ContactDef Contact(string id) => Contacts.First(c => c.Id == id);
    public static DistrictDef District(string id) => Districts.First(d => d.Id == id);
    public static UpgradeDef Upgrade(string id) => Upgrades.First(u => u.Id == id);

    public static readonly string[] AttrNames = { "Body", "Agility", "Strength", "Reaction", "Logic", "Charisma" };
    public static readonly string[] AttrDesc =
    {
        "+20 max HP a point.", "+4 % speed, -6 % spread a point.", "Throw range, +12 % melee a point.",
        "-6 % ability cooldowns, faster dash a point.", "+20 % hacking speed a point.", "-3 % prices a point.",
    };
    public static readonly string[] SkillNames = { "Firearms", "Heavy weapons", "Blades", "Demolitions", "Electronics", "Stealth", "Driving", "Decking" };
    public static readonly string[] SkillDesc =
    {
        "Pistols, SMGs, rifles: -10 % spread, +5 % reload speed, +2 % crit a point.",
        "Shotguns, LMGs, launchers: -10 % spread, +5 % reload speed a point.",
        "Melee: +12 % damage a point.",
        "Grenades: +10 % damage a point, +1 carried at 2 and 4.",
        "Maglocks, turrets, cameras, lifts: +15 % hacking speed a point; turn turrets at 3.",
        "Enemies notice you 12 % slower a point, footsteps quieter.",
        "Grip and steering +6 % a point.",
        "The Matrix: spikes +12 %, the trace -6 %, gates +15 % a point. 3: see IC's next step and strength; 4: quiet cracking leaves no trace; 5: spikes twice as fast; 6: once a run a full trace slips off you.",
    };

    public static readonly (RunMod mod, string name, string desc, float pay)[] RunMods =
    {
        (RunMod.Blackout, "Blackout", "The site's power is cut: no lights, cameras dead, everyone sees less -- you included.", 1.1f),
        (RunMod.ShiftChange, "Shift change", "Double patrols walking the halls.", 1.1f),
        (RunMod.Deadline, "Deadline", "On the clock: when it runs out the target leaves -- or security sweeps the site.", 1.2f),
        (RunMod.Elite, "Elite security", "Better-armoured, tougher guards.", 1.3f),
        (RunMod.Skeleton, "Skeleton crew", "Holiday shift: fewer guards, less pay.", 0.8f),
    };

    public static readonly string[] JobNames = { "Eliminate", "Retrieve", "Destroy", "Datasteal", "Rescue", "Clear", "Courier", "Walk", "Investigate", "Plant", "Defend", "Kidnap", "Escort", "Bounty", "Hack", "Race" };

    /// <summary>What gets planted on Plant jobs.</summary>
    public static readonly string[] Plants = { "wiretap", "tracker", "data spike", "bundle of planted evidence", "camera bug" };
}
