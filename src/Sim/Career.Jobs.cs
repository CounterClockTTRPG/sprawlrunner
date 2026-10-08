using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>A job on the board. Everything a run needs is here, including its seed.</summary>
public sealed class Job
{
    public int Id;
    public JobType Type;
    public string Employer;
    public string District;
    public Faction Faction;
    public SiteStyle Style;
    public int Threat;
    public int Pay, Karma;
    public ulong Seed;
    public int Expires;
    /// <summary>The city lot the site is in.</summary>
    public int Lot = -1;
    /// <summary>Courier jobs: the drop-off lot, and the seconds allowed once the package is aboard.</summary>
    public int Lot2 = -1;
    public float TimeLimit;
    public string Target, Item, Title, Brief;
    public int Count;
    public bool Schematics;
    public List<RunMod> Mods = new();
    /// <summary>A runner hired for this job (Tables.Hires id), or null.</summary>
    public string Hired;
    /// <summary>A Mr. Johnson job: negotiated before it is accepted; some are set-ups.</summary>
    public bool Johnson, Haggled, Negotiated, DoubleCross;
    /// <summary>Which recurring Johnson (Career.Johnsons) is on the line.</summary>
    public string JohnsonId;
    /// <summary>Story chapter (1-4) or 0 for an ordinary job.</summary>
    public int Story;
    /// <summary>The guided first job.</summary>
    public bool Tutorial;
    /// <summary>A contact's personal story job: whose (contact id) and which part (1-3).</summary>
    public string Arc;
    public int ArcPart;
    /// <summary>Deadline jobs: seconds from going in.</summary>
    public float Deadline;
    /// <summary>Made by a storylet (revenge jobs, crew jobs...): it says which, for the follow-up.</summary>
    public string Tag;
    /// <summary>Who pays (a Faction, -1: nobody in particular); a faction's own job for a friend.</summary>
    public int Client = -1;
    public bool Favour;
    public bool Has(RunMod m) => Mods.Contains(m);
    /// <summary>A tower: how many floors above the lobby (0: an ordinary site).</summary>
    public int Floors;
    /// <summary>The rival crew want this job too (they turn up in the run, and take it if it lapses).</summary>
    public bool Contested;
    /// <summary>The way in chosen on the briefing map (an <see cref="EntryPoint"/> id; null: the front gate).</summary>
    public string Entry;
    /// <summary>A bounty that is a hive: a queen, her egg sacs and her workers (prestige, the Industrial district).</summary>
    public bool Hive;
    /// <summary>Legwork bought in the briefing (Career.Legwork ids).</summary>
    public List<string> Legwork = new();
    /// <summary>A job done behind the wheel: courier runs and street races.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool Driving => Type is JobType.Courier or JobType.Race;
    /// <summary>A double-cross that never pays rather than sending a squad (one in three).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool Stiff => DoubleCross && Story == 0 && new Rng(Seed ^ 0x571FFUL).Chance(0.34f);

    public string Skulls => Threat > 5 ? "[#####+]" : "[" + new string('#', Threat) + new string('.', 5 - Threat) + "]";
}

public sealed class RunResult
{
    public bool Success, Died, Aborted, Alarm;
    public int Kills, Cash;
    public List<LootItem> Loot = new();
    public Dictionary<string, int> GrenadesLeft = new();
    public int MedkitsLeft;
    public float Seconds;
    /// <summary>Health left at the end, as a fraction (for injuries).</summary>
    public float HpLeft = 1;
    /// <summary>A mid-run twist: its effect on the pay and what happened.</summary>
    public float PayMul = 1;
    public string Twist;
    public int KnockOuts, Bosses, Walls, Spells, Datastores;
    public bool DroneLost;
    /// <summary>The target got out of the building; the target was carried out alive; the hostage died.</summary>
    public bool TargetFled, TargetTaken, HostageDead;
    /// <summary>The rival crew came; who of theirs died, how many went down alive; they got away with the objective.</summary>
    public bool RivalCame, RivalRobbed;
    public List<string> RivalDead = new();
    public int RivalSpared;
    /// <summary>The extraction moved mid-run; a Johnson's squad came in by the exit.</summary>
    public bool ExfilBurned, Trap;
    /// <summary>The hired runner went down for good -- or was carried out, down but alive.</summary>
    public bool AllyDied, AllyCarried;
    /// <summary>The objective was done when it ended (TraumaLink Platinum brings it out with you); you shot at a trauma team.</summary>
    public bool ObjectiveDone, ShotTrauma;
    /// <summary>Decisions made on the run ("begs:1", "dirt:0"), guards who surrendered and lived, and ones who didn't.</summary>
    public List<string> Choices = new();
    public int Spared, KilledHelpless, EdgeUsed;
}

public sealed class Debrief
{
    public string Headline;
    public List<(string what, string amount)> Lines = new();
    public bool CareerOver;
    /// <summary>A word from the contact, for story jobs.</summary>
    public string Quote, QuoteBy;
    /// <summary>Achievements unlocked by this job.</summary>
    public List<string> Unlocked = new();
    /// <summary>What became of everyone, when a story ends.</summary>
    public List<string> Epilogue = new();
}

public sealed partial class Career
{
    // ---- the board --------------------------------------------------------------

    /// <summary>Drop expired jobs and top the board up to its size.</summary>
    public void RefreshBoard(City city, int maxNew = 99)
    {
        foreach (Job old in Board.Where(j => j.Id != ActiveJob && j.Expires < Day).ToList()) Lapsed(old);
        Board.RemoveAll(j => j.Id != ActiveJob && j.Expires < Day);
        OfferStory(city);
        OfferArcs(city);
        if (Day == 1 && JobsDone + JobsFailed == 0 && !Board.Any(j => j.Tutorial))
        {
            int lot = city.FreeSite("docks", Rng, new HashSet<int>(Board.Select(b => b.Lot)));
            if (lot >= 0)
                Board.Add(new Job
                {
                    Id = NextJobId++, Tutorial = true, Type = JobType.Retrieve, Style = SiteStyle.Warehouse, Faction = Faction.Vipers, District = "docks", Threat = 1,
                    Seed = 4242, Expires = Day + 6, Lot = lot, Employer = "fixer", Pay = 700, Karma = 4, Item = "sealed briefcase", Target = "x",
                    Title = "First job: the dockside warehouse",
                    Brief = "Juno: 'Easy one to start. Ash Vipers are sitting on a sealed briefcase in a warehouse by the docks. Walk in, find it, walk out. I'll talk you through it.'",
                });
        }
        int added = 0;
        while (Board.Count < BoardSize && added < maxNew)
        {
            Job j = MakeJob(city);
            if (j == null) break;
            Contest(j);
            Board.Add(j);
            added++;
        }
        OfferFactionWork(city);
    }

    private static readonly string[] Employers = { "fixer", "fixer", "fixer", "decker", "arms", "rigger", "doc", "fence", "talismonger", "cop", "press" };

    // ---- the story ---------------------------------------------------------------------

    public static readonly (int after, int threat, JobType type, SiteStyle style, Faction faction, string district, string title, string brief)[] Chapters =
    {
        (3, 1, JobType.Retrieve, SiteStyle.Den, Faction.Vipers, "barrens", "The package",
            "Juno, quiet for once: 'A Viper crew lifted a neural chip off a corporate courier. A friend wants it back before they sell it. Don't open the case.'"),
        (5, 3, JobType.Datasteal, SiteStyle.Lab, Faction.Halcyon, "industrial", "Who's buying",
            "The chip was a Halcyon-Matsuda prototype. Somebody is buying them all up. Jack into their lab and find out who -- and why they need a thousand of them."),
        (8, 4, JobType.Rescue, SiteStyle.Club, Faction.Lotus, "strip", "Loose ends",
            "The designer, Dr. Amon Pryce, ran -- straight into the Red Lotus, who are selling him to the highest bidder. He knows what 'Iron Saint' is. Get him out."),
        (11, 5, JobType.Destroy, SiteStyle.Office, Faction.Halcyon, "spire", "Iron Spire",
            "Iron Saint: a chip that turns guards into puppets, and its first test subject runs the Spire's security. Wreck the prototype cores at the top of the Spire. Then disappear. This is the last one."),
    };

    /// <summary>A contact's three-part story: what each job is, and what it ends with.</summary>
    public sealed record ArcPart(JobType Type, SiteStyle Style, Faction Faction, string District, int Threat, string Title, string Brief, string Quote);

    public static readonly Dictionary<string, (string title, string reward, ArcPart[] parts)> Arcs = new()
    {
        ["doc"] = ("Old debts", "Ichikawa reflex booster (Reaction +2)", new[]
        {
            new ArcPart(JobType.Retrieve, SiteStyle.Den, Faction.Vipers, "docks", 1, "his surgical kit", "Doc Ichikawa, sleeves rolled up: 'Vipers broke in last night and took my kit. Nanosutures, a bone saw that's older than you. I need it back before I need it on you.'", "Ichikawa turns the case over in his hands. 'Every instrument in here has a name. Thank you.'"),
            new ArcPart(JobType.Rescue, SiteStyle.Club, Faction.Lotus, "strip", 2, "Nurse Ade", "'Ade worked for me eight years. The Lotus took her as a message -- to me. Bring her home.'", "'Ade says you carried her the last stretch. I'm not good at this part. Thank you.'"),
            new ArcPart(JobType.Eliminate, SiteStyle.Office, Faction.Halcyon, "spire", 4, "the headhunter", "'Halcyon wants their star surgeon back. The headhunter they sent doesn't take no -- or prisoners. Make it so he never files his report.'", "'I'm free for the first time in eleven years. This is the best chrome I ever made. It's yours.'"),
        }),
        ["arms"] = ("Family business", "Sal's Thunder (one-of-a-kind LMG)", new[]
        {
            new ArcPart(JobType.Destroy, SiteStyle.Warehouse, Faction.Vipers, "docks", 1, "the knock-offs", "Big Sal, red-faced: 'Somebody's flooding the street with fake Kestrels that blow up in your hand. With my name stamped on them. Burn the lot.'", "'Ha! Smell that? That's my reputation, not burning.'"),
            new ArcPart(JobType.Retrieve, SiteStyle.Club, Faction.Lotus, "strip", 2, "grandpa's rifle", "'My grandfather's rifle. Hand-built, the Lotus took it off my cousin to pay a debt. It's not about the money. It's about the rifle.'", "Sal goes quiet for a long time. 'He'd have liked you.'"),
            new ArcPart(JobType.Clear, SiteStyle.Den, Faction.Vipers, "barrens", 3, "the torch crew", "'The crew that firebombed my first shop are back in business. Two of my people died in that fire. You know what to do.'", "'It's done, then. Here. I built her for me, but she shoots better in your hands.'"),
        }),
        ["rigger"] = ("Grand theft", "Torque's Interceptor (unique car)", new[]
        {
            new ArcPart(JobType.Retrieve, SiteStyle.Lab, Faction.Halcyon, "industrial", 2, "the engine prototype", "Torque, wiping grease off a wrench: 'Halcyon built an engine that does things engines shouldn't. It's in a lab in the Industrial district. I want to see it. Closely.'", "'Oh, you beauty. Look at this manifold. I might cry.'"),
            new ArcPart(JobType.Courier, SiteStyle.Warehouse, Faction.Vipers, "barrens", 2, "the hot part", "'Got a buyer for the spare injector -- across town, today, before Halcyon knows it's missing. Drive like you stole it. You did.'", "'That's the fastest anybody's ever run that route. I've got plans for you.'"),
            new ArcPart(JobType.Destroy, SiteStyle.Warehouse, Faction.Vipers, "docks", 3, "the rival chop shop", "'The Vipers' chop shop is selling my designs. Wreck their machines -- every last lift.'", "'Keys. Don't scratch it. Actually, scratch it. It's yours. That engine's in there.'"),
        }),
        ["decker"] = ("Ghost in the wire", "Ghost protocol (program)", new[]
        {
            new ArcPart(JobType.Datasteal, SiteStyle.Office, Faction.Halcyon, "spire", 2, "the archive", "Null, not looking up: 'Halcyon keeps a cold archive with my name in it. Before I was Null. I want it gone from their side and on mine.'", "'...So that's who I was. Huh. Doesn't matter. Thanks.'"),
            new ArcPart(JobType.Rescue, SiteStyle.Club, Faction.Lotus, "strip", 3, "the kid", "'A kid I taught got caught decking the Lotus books. They'll fry his brain to see who taught him. That would be me.'", "'He's fine. Mouthy. Reminds me of someone.'"),
            new ArcPart(JobType.Datasteal, SiteStyle.Lab, Faction.Halcyon, "industrial", 4, "the black site", "'The archive pointed at a black site. Whatever they do there, they do it with stolen minds. Jack in, pull it all, and let me burn the rest.'", "'Here. Ghost protocol. I wrote it for me. Cameras won't see you, ICE won't keep up. Don't make me regret it.'"),
        }),
        ["fence"] = ("The ledger", "Wen's gratitude (fence pays full value) and ¥5,000", new[]
        {
            new ArcPart(JobType.Retrieve, SiteStyle.Den, Faction.Vipers, "barrens", 1, "Wen's ledger", "Old Wen, whispering: 'My ledger -- who sold what, to whom. A Viper kid lifted it. If the wrong people read it, half the Barrens dies, me first.'", "'Every page here is a life. You didn't read it? Good. Good.'"),
            new ArcPart(JobType.Eliminate, SiteStyle.Club, Faction.Lotus, "strip", 2, "the blackmailer", "'Someone made a copy before you got it back. He's been sending me pages. He works out of a Lotus club.'", "'He won't be sending any more pages. Neither will I. I'm retiring the ledger.'"),
            new ArcPart(JobType.Retrieve, SiteStyle.Office, Faction.Halcyon, "spire", 3, "the copy", "'The copy's in a Halcyon office now. Corporate leverage. Get it back and I'll owe you for the rest of my life.'", "'For the rest of my life, I said. I meant it. Anything you bring me, full price.'"),
        }),
        ["talismonger"] = ("Old magic", "Sabine's ward (+2 armour; mages +10 % spell power)", new[]
        {
            new ArcPart(JobType.Retrieve, SiteStyle.Den, Faction.Vipers, "barrens", 1, "the stolen fetish", "Madame Sabine, stirring something that smokes: 'A Viper stole a bone fetish from my window. He doesn't know what it is. It knows what he is.'", "'It's calmer now. So am I.'"),
            new ArcPart(JobType.Clear, SiteStyle.Den, Faction.Lotus, "barrens", 3, "the toxic shaman", "'A toxic shaman is binding spirits in the Barrens and feeding them on people. The Lotus pays him for it. End the cult.'", "'The spirits are free. They told me your name. That's rare.'"),
            new ArcPart(JobType.Destroy, SiteStyle.Lab, Faction.Lotus, "industrial", 4, "the blood circle", "'The shaman learned it from someone. There's a circle in a lab, drawn in blood, still running. Break the vats that feed it.'", "'Take this ward. It will turn a bullet or two, and it listens when you cast.'"),
        }),
        ["cop"] = ("Getting out", "Dace's badge (Lone Star never pulls you over)", new[]
        {
            new ArcPart(JobType.Retrieve, SiteStyle.Den, Faction.Vipers, "docks", 1, "the tape", "Sgt. Dace, low: 'The Vipers have a tape of me taking an envelope. If internal affairs sees it, I'm finished -- and so is the file I keep losing for you. Get it back.'", "Dace watches the tape burn in an ashtray. 'You didn't see what was on it.'"),
            new ArcPart(JobType.Rescue, SiteStyle.Club, Faction.Lotus, "strip", 2, "his partner", "'My old partner went undercover with the Lotus three months ago and never came out. Officially she quit. I know she didn't.'", "'She's in a hospital under another name. She'll live. I owe you a debt I can't pay in cash.'"),
            new ArcPart(JobType.Datasteal, SiteStyle.Office, Faction.Halcyon, "spire", 4, "the contract", "'Halcyon is buying Lone Star, precinct by precinct. Get me the contract and I can take it somewhere it can't be bought. Or so I tell myself.'", "'That's it, then. That's the whole rotten thing on one chip.'"),
        }),
        ["press"] = ("The big story", "Quill's front page (notoriety wiped, and your fame fades twice as fast)", new[]
        {
            new ArcPart(JobType.Investigate, SiteStyle.Office, Faction.Halcyon, "spire", 2, "the source", "Mara Quill: 'My source inside Halcyon went quiet a week ago. Her desk is still there. She'd have left me something -- she always did. Find it.'", "'Notes, photographs, a key. She knew. God, she knew.'"),
            new ArcPart(JobType.Rescue, SiteStyle.Den, Faction.Vipers, "barrens", 3, "her cameraman", "'They took Teo, my cameraman, the night we started digging. The Vipers have him -- Halcyon pays the Vipers for this kind of thing.'", "'Teo's alive. He kept the footage in his shoe. He's an idiot. I love him.'"),
            new ArcPart(JobType.Datasteal, SiteStyle.Lab, Faction.Halcyon, "industrial", 4, "the cover-up", "'The last piece: the lab where they buried it. Pull everything. This is the story that makes careers -- mine, and maybe yours.'", "'It's all here. Every lie. Now I have to decide if I'm brave enough.'"),
        }),
    };

    private void OfferArcs(City city)
    {
        if (Board.Count(j => j.Arc != null) >= 2) return;
        foreach (var (who, arc) in Arcs)
        {
            int step = ArcStep.GetValueOrDefault(who);
            if (step >= 3 || Loyalty.GetValueOrDefault(who) < step + 1 || Board.Any(j => j.Arc == who) || Shut(who) != null) continue;
            ArcPart part = arc.parts[step];
            var taken = new HashSet<int>(Board.Select(b => b.Lot).Concat(Board.Select(b => b.Lot2)));
            int lot = city.FreeSite(part.District, Rng, taken);
            if (lot < 0) continue;
            var j = new Job
            {
                Id = NextJobId++, Arc = who, ArcPart = step + 1, Type = part.Type, Style = part.Style, Faction = part.Faction, District = part.District,
                Threat = part.Threat, Seed = Rng.Next(), Expires = Day + 20, Lot = lot, Employer = who, Title = $"{Tables.Contact(who).Name} {step + 1}/3: {part.Title}",
                Brief = part.Brief, Target = part.Title, Item = part.Title.Replace("the ", ""), Count = 3,
            };
            j.Pay = (int)(MathF.Round(800 * MathF.Pow(part.Threat, 1.6f) * 1.6f / 50) * 50);
            j.Karma = 3 + 2 * part.Threat;
            if (part.Type == JobType.Courier)
            {
                taken.Add(lot);
                int drop = city.FreeSite(Tables.Districts.First(d => d.Id != part.District).Id, Rng, taken);
                if (drop < 0) continue;
                j.Lot2 = drop;
                j.TimeLimit = MathF.Round(V2.Distance(city.Lots[drop].Door.Centre, city.Lots[lot].Door.Centre) / 7 + 20);
            }
            Board.Add(j);
            Say($"{Tables.Contact(who).Name} wants a personal favour: {j.Title}.");
            if (Board.Count(x => x.Arc != null) >= 2) return;
        }
    }

    private void FinishArc(Job j, Debrief d)
    {
        var arc = Arcs[j.Arc];
        ArcStep[j.Arc] = Math.Max(ArcStep.GetValueOrDefault(j.Arc), j.ArcPart);
        d.Quote = arc.parts[j.ArcPart - 1].Quote;
        d.QuoteBy = Tables.Contact(j.Arc).Name;
        d.Lines.Add(($"{arc.title}", $"part {j.ArcPart} of 3"));
        if (j.ArcPart < 3) return;
        // The end of their story is a decision: they call to ask what you want.
        d.Lines.Add(("Reward", "they'll call"));
        Set("arc:" + j.Arc, -1);
        Send("fork-" + j.Arc);
    }

    /// <summary>The ending a contact hoped for: their one-of-a-kind reward.</summary>
    public string ArcReward(string who)
    {
        Set("arc:" + who, 1);
        switch (who)
        {
            case "doc": if (!Cyber.Contains("booster")) Cyber.Add("booster"); break;
            case "arms": if (!Weapons.Contains("thunder")) { Weapons.Add("thunder"); Primary = "thunder"; } break;
            case "rigger": if (!Vehicles.Contains("interceptor")) { Vehicles.Add("interceptor"); Vehicle = "interceptor"; } break;
            case "decker": if (!Programs.Contains("ghost")) Programs.Add("ghost"); break;
            case "fence": Cash += 5000; Count("earned", 5000); break;
            case "press": Notoriety = 0; break;
        }
        return Arcs.TryGetValue(who, out var a) ? a.reward : null;
    }

    private void OfferStory(City city)
    {
        OfferTide(city);
        if (Chapter >= Chapters.Length || Retired || Board.Any(j => j.Story is > 0 and <= 4)) return;
        var ch = Chapters[Chapter];
        if (JobsDone < ch.after) return;
        // Past the first chapter, each needs the lead the last one left -- or three more jobs, and it finds you.
        if (Chapter > 0 && Flag($"lead:ch{Chapter + 1}") == 0 && JobsDone < ch.after + 3) return;
        int lot = city.FreeSite(ch.district, Rng, new HashSet<int>(Board.Select(b => b.Lot)));
        if (lot < 0) return;
        var j = new Job
        {
            Id = NextJobId++, Story = Chapter + 1, Type = ch.type, Style = ch.style, Faction = ch.faction, District = ch.district, Threat = ch.threat,
            Seed = Rng.Next(), Expires = Day + 30, Lot = lot, Employer = "fixer", Title = $"STORY {Chapter + 1}/4: {ch.title}", Brief = ch.brief,
            Target = "Dr. Amon Pryce", Item = ch.type == JobType.Destroy ? "prototype cores" : "neural chip", Count = 4,
        };
        j.Pay = (int)(MathF.Round(800 * MathF.Pow(ch.threat, 1.6f) * 2.5f / 50) * 50);
        j.Karma = 4 + 3 * ch.threat;
        if (Chapter == 3) ShapeFinale(j);
        Board.Add(j);
        Say($"Juno has a special job: {j.Title}.");
    }

    public Job MakeJob(City city)
    {
        Rng r = Rng;
        // Who is asking: a contact who trusts you pays better and offers harder work.
        string employer = r.Pick(Employers);
        if (Shut(employer) != null) employer = "fixer";
        int trust = Loyalty.GetValueOrDefault(employer);
        int maxT = Math.Min(5, MaxThreat + (trust >= 3 ? 1 : 0));
        var districts = Tables.Districts.Where(d => d.MinThreat <= maxT).ToList();
        DistrictDef dist = r.Pick(districts);
        int threat = r.Range(dist.MinThreat, Math.Min(dist.MaxThreat, maxT));
        // Prestige: the city's hardest buildings, for the runners who have outgrown everything else.
        if (Prestige && new Rng(r.State ^ 0x6666UL).Chance(0.25f) && dist.Id is "spire" or "industrial") threat = 6;
        SiteStyle style = r.Pick(dist.Styles);
        Faction fac = PickFaction(dist.Id, r);
        if (style is SiteStyle.Office or SiteStyle.Lab && r.Chance(0.8f)) fac = Faction.Halcyon;

        var types = new List<JobType> { JobType.Eliminate, JobType.Retrieve, JobType.Rescue, JobType.Destroy };
        // Everyone keeps their books somewhere: even gang dens have a terminal worth cracking.
        types.Add(JobType.Datasteal);
        if (style is SiteStyle.Office or SiteStyle.Lab or SiteStyle.Club) types.Add(JobType.Datasteal);
        if (style is SiteStyle.Office or SiteStyle.Lab) types.Add(JobType.Datasteal);
        if (fac != Faction.Halcyon) { types.Add(JobType.Clear); types.Add(JobType.Clear); }
        types.Add(JobType.Courier);
        types.Add(JobType.Investigate);
        types.Add(JobType.Plant);
        if (style is SiteStyle.Warehouse or SiteStyle.Den && r.Chance(0.5f)) types.Add(JobType.Defend);
        types.Add(JobType.Kidnap);
        types.Add(JobType.Escort);
        // Slum basements breed things Lone Star pays to have cleared.
        if (dist.Id is "barrens" or "docks") types.Add(JobType.Bounty);
        // The decker's clients like their work done remotely; the rigger's like it fast.
        if (employer == "decker") { types.Add(JobType.Hack); types.Add(JobType.Hack); }
        if (employer == "rigger") { types.Add(JobType.Race); types.Add(JobType.Race); }
        JobType type = r.Pick(types);
        // Prestige: something has nested under the Industrial district (its own draw, so boards stay as they were).
        bool hive = threat == 6 && dist.Id == "industrial" && new Rng(r.State ^ 0x41BEUL).Chance(Tables.HiveChance);
        if (hive) { type = JobType.Bounty; style = SiteStyle.Warehouse; }
        if (type == JobType.Bounty) fac = Faction.Vipers;

        var taken = new HashSet<int>(Board.Select(b => b.Lot).Concat(Board.Select(b => b.Lot2)));
        int lot = city.FreeSite(dist.Id, r, taken);
        if (lot < 0) return null;

        var j = new Job
        {
            Id = NextJobId++, Type = type, District = dist.Id, Faction = fac, Style = style, Threat = threat,
            Seed = r.Next(), Expires = Day + r.Range(3, 6), Lot = lot, Employer = employer,
        };
        float pay = 800 * MathF.Pow(threat, 1.6f) * r.Range(0.9f, 1.2f);
        if (type is JobType.Clear or JobType.Destroy) pay *= 1.1f;
        if (type == JobType.Datasteal) pay *= 1.15f;
        if (type == JobType.Defend) pay *= 1.1f;
        if (type == JobType.Investigate) pay *= 0.9f;
        if (type == JobType.Kidnap) pay *= 1.25f;
        if (type is JobType.Hack or JobType.Race) pay *= 0.85f;
        pay *= 1 + 0.08f * trust;
        if (hive) pay *= Tables.HivePay;
        j.Hive = hive;
        j.Pay = (int)(MathF.Round(pay / 50) * 50);
        j.Karma = 2 + 2 * threat;
        var used = new HashSet<string>(Board.Select(b => b.Target));
        var names = Tables.TargetNames.Where(n => !used.Contains(n)).ToList();
        j.Target = r.Pick(names.Count > 0 ? names : Tables.TargetNames.ToList());
        j.Item = type == JobType.Plant ? r.Pick(Tables.Plants) : r.Pick(Tables.Items);
        j.Count = type == JobType.Destroy ? 2 + (threat + 1) / 2 : 0;
        bool squeezed = false;
        if (type is not (JobType.Courier or JobType.Race))
        {
            int n = r.Chance(0.5f) ? (r.Chance(0.3f) ? 2 : 1) : 0;
            for (int k = 0; k < n; k++)
            {
                RunMod m = r.Pick(Tables.RunMods).mod;
                if (j.Mods.Contains(m) || m == RunMod.Elite && j.Mods.Contains(RunMod.Skeleton) || m == RunMod.Skeleton && j.Mods.Contains(RunMod.Elite)) continue;
                j.Mods.Add(m);
                j.Pay = (int)(MathF.Round(j.Pay * Tables.RunMods.First(x => x.mod == m).pay / 50) * 50);
            }
            if (j.Has(RunMod.Deadline)) j.Deadline = 150 + 25 * threat;
            j.Johnson = r.Chance(0.3f);
            // Bad company: every point of notoriety makes a set-up likelier (the draw stays, so boards stay as they were).
            j.DoubleCross = j.Johnson && r.Chance(0.15f + 0.02f * Notoriety);
            if (j.Johnson) PickJohnson(j);
            // A Johnson you squeezed last time opens lower.
            if (j.Johnson && j.JohnsonId != null && Flag("haggled:" + j.JohnsonId) > 0)
            {
                Set("haggled:" + j.JohnsonId, 0);
                j.Pay = (int)(MathF.Round(j.Pay * 0.9f / 50) * 50);
                squeezed = true;
            }
            // The tell: a set-up pays a little over the odds.
            if (j.DoubleCross) j.Pay = (int)(MathF.Round(j.Pay * 1.15f / 50) * 50);
            // Old enemies: a faction with a long memory fields its best.
            // A faction's plan come good: Halcyon's expansion and the Lotus council both bring in their best, for less of a rise.
            bool grudged = Grudge.GetValueOrDefault(fac) >= Tables.GrudgeElite;
            if ((grudged || fac == Faction.Halcyon && On("expansion") || fac == Faction.Lotus && On("council")) && !j.Has(RunMod.Elite))
            {
                j.Mods.Remove(RunMod.Skeleton);
                j.Mods.Add(RunMod.Elite);
                j.Pay = (int)(MathF.Round(j.Pay * (grudged ? 1.3f : fac == Faction.Halcyon ? 1.15f : 1f) / 50) * 50);
            }
            // What the city is going through this week.
            if (fac == Faction.Halcyon && On("quarterly"))
            {
                if (!j.Has(RunMod.ShiftChange)) j.Mods.Add(RunMod.ShiftChange);
                j.Pay = (int)(MathF.Round(j.Pay * 1.25f / 50) * 50);
            }
            if (fac is Faction.Vipers or Faction.Lotus && On("gangwar")) j.Pay = (int)(MathF.Round(j.Pay * 1.1f / 50) * 50);
            if (fac == Faction.Lotus && On("council")) j.Pay = (int)(MathF.Round(j.Pay * 1.12f / 50) * 50);
        }
        if (type is JobType.Courier or JobType.Race)
        {
            // The drop is in another district, well across town.
            taken.Add(lot);
            var far = Tables.Districts.Where(d => d.Id != dist.Id).ToList();
            int drop = -1;
            for (int k = 0; k < 8 && drop < 0; k++)
            {
                int cand = city.FreeSite(r.Pick(far).Id, r, taken);
                if (cand >= 0 && V2.Distance(city.Lots[cand].Door.Centre, city.Lots[lot].Door.Centre) > 45) drop = cand;
            }
            if (drop < 0) return null;
            j.Lot2 = drop;
            float km = V2.Distance(city.Lots[drop].Door.Centre, city.Lots[lot].Door.Centre);
            j.TimeLimit = MathF.Round(km / (6.5f + 0.4f * threat) + 22 - 2 * threat);
            if (type == JobType.Courier) j.Pay = (int)(MathF.Round(j.Pay * 0.8f / 50) * 50);
        }
        // Done from the safehouse rig: the job's address is your own.
        if (type == JobType.Hack) j.Lot = city.Place(HasUpgrade("loft") ? "loft" : "home").Index;
        // The building first (a tower is Halcyon's, an office or a lab), then who it's for and what the brief says.
        MakeTower(j, city);
        Describe(j, city);
        PickClient(j);
        Voice(j);
        TowerBrief(j);
        if (squeezed) j.Brief += " This Johnson remembers you haggled last time: the opening offer is 10 % lower.";
        foreach (RunMod m in j.Mods)
        {
            var (_, name, desc, _) = Tables.RunMods.First(x => x.mod == m);
            j.Brief += $" [{name}] {desc}" + (m == RunMod.Deadline ? $" ({(int)j.Deadline / 60}:{(int)j.Deadline % 60:00} from when you go in)" : "");
        }
        return j;
    }

    private static readonly Dictionary<SiteStyle, string> Places = new()
    {
        [SiteStyle.Warehouse] = "warehouse", [SiteStyle.Office] = "office block", [SiteStyle.Den] = "gang den",
        [SiteStyle.Lab] = "research lab", [SiteStyle.Club] = "nightclub",
    };

    private void Describe(Job j, City city)
    {
        string place = Places[j.Style];
        string where = $"{Art(place).ToLower()} {place} in the {Tables.District(j.District).Name}";
        string who = Tables.FactionNames[(int)j.Faction];
        string emp = Tables.Contact(j.Employer).Name;
        string thing = j.Style == SiteStyle.Lab ? "chemical vats" : "server racks";
        switch (j.Type)
        {
            case JobType.Eliminate:
                j.Title = $"Wetwork: {j.Target}";
                j.Brief = $"{emp} has a client who wants {j.Target} gone. The target is holed up in {where}, guarded by {who}. They will run when it gets loud.";
                break;
            case JobType.Retrieve:
                j.Title = $"Extraction: {j.Item}";
                j.Brief = $"{Art(j.Item)} {j.Item} is stored in {where}, held by {who}. {emp}'s buyer wants it in hand -- find it and carry it out.";
                break;
            case JobType.Destroy:
                j.Item = thing;
                j.Title = $"Sabotage: {j.Count} {thing}";
                j.Brief = $"{who} runs {thing} out of {where}. Wreck all {j.Count}. Explosives recommended; the {thing} are tough.";
                break;
            case JobType.Datasteal:
                j.Title = "Datasteal: mainframe";
                j.Brief = $"Paydata on a mainframe in {where}. Jack in and hold the connection until the download finishes. {who} will trace you halfway through.";
                break;
            case JobType.Rescue:
                j.Title = $"Rescue: {j.Target}";
                j.Brief = $"{j.Target} is being held by {who} in {where}. Cut them loose and walk them out alive.";
                break;
            case JobType.Courier:
                j.Title = $"Courier: {j.Item}";
                j.Brief = $"Collect {Art(j.Item).ToLower()} {j.Item} from a contact in the {Tables.District(j.District).Name} and get it to the drop in the {Tables.District(city.Lots[j.Lot2].District).Name} -- {j.TimeLimit:0} seconds once it is in your car. {who} want it too and will come after you.";
                break;
            case JobType.Clear:
                j.Title = $"Clean sweep: {who}";
                j.Brief = $"{who} moved into {where} and someone wants them moved out. Permanently. Every last one.";
                break;
            case JobType.Investigate:
                j.Title = $"Legwork: {who}";
                j.Brief = $"{emp}'s client wants to know what {who} are really doing in {where}. Find three pieces of evidence -- files, recordings, whatever they left lying around -- and get out with them.";
                break;
            case JobType.Plant:
                j.Title = $"Plant: {j.Item}";
                j.Brief = $"Get into {where}, held by {who}, and leave {Art(j.Item).ToLower()} {j.Item} in the marked room (hold E). Nobody should ever know you were there.";
                break;
            case JobType.Kidnap:
                j.Title = $"Extraction: {j.Target}";
                j.Brief = $"{emp}'s client wants {j.Target} -- alive, and out of {where}. {who} guard them. Knock them out (baton, taser, gel rounds, a spell to finish), carry them to the extraction. Dead is worth nothing.";
                break;
            case JobType.Escort:
                j.Title = $"Escort: {j.Target}";
                j.Brief = $"{j.Target} has a meeting in {where}, a place held by {who}, and doesn't trust them. Walk in with them, keep them alive while they talk, and walk them out.";
                break;
            case JobType.Bounty when j.Hive:
                j.Title = "Bounty: the Hive";
                j.Brief = $"Workers at {where} in the {Tables.District(j.District).Name} stopped coming home. Lone Star sent a team in; the helmet cameras show walls of resin and things moving in it. There's a queen, and she's laying. Burn every egg sac, kill the queen, leave nothing. Bring fire.";
                break;
            case JobType.Bounty:
                j.Title = "Bounty: a ghoul nest";
                j.Brief = $"Something has been eating people in the {Tables.District(j.District).Name}. Lone Star traced it to {where} and doesn't want to go in. Ghouls, hellhounds, maybe worse. Clear the nest; the bounty pays per head, so they say.";
                break;
            case JobType.Hack:
                j.Title = $"Remote: {who} systems";
                j.Brief = $"{emp} has a client who wants into {who}'s systems -- and you can do it from your own chair. Jack in at the safehouse rig and pull three datastores. ICE that traces you twice blows the job (and gives you a headache).";
                break;
            case JobType.Race:
                j.Title = $"Street race: {Tables.District(j.District).Name} to {Tables.District(city.Lots[j.Lot2].District).Name}";
                j.Brief = $"Torque put your name down for a race. Start at the marker in the {Tables.District(j.District).Name} (E), finish at the drop in the {Tables.District(city.Lots[j.Lot2].District).Name}. A Viper hotshot in a tuned car is racing you. First to the finish takes the purse.";
                break;
            case JobType.Defend:
                j.Title = $"Hold the line: {place}";
                j.Brief = $"{emp}'s client owns {where} and {who} want it. Hold it for {75 + 12 * j.Threat} seconds until the client's own people get there. They will come from every side.";
                break;
        }
    }

    private static string Art(string noun) => "aeiou".Contains(char.ToLower(noun[0])) ? "An" : "A";

    /// <summary>
    /// Haggle with a Johnson: Charisma and the fixer's standing help. Better
    /// pay, or a cut, or he walks (the job is gone). Once per job.
    /// </summary>
    public string Haggle(Job j)
    {
        if (!j.Johnson || j.Haggled) return "No more haggling on this one.";
        j.Haggled = true;
        JohnsonDef jd = JohnsonById(j.JohnsonId);
        float chance = MathF.Min(Tables.HaggleMax, 0.3f + 0.1f * A(Attr.Charisma) + 0.05f * Loyalty.GetValueOrDefault("fixer") + (Talks("Corporate") ? 0.1f : 0) + (jd?.Haggle ?? 0));
        // Win or lose, they remember being pushed (a set-up's Johnson doesn't care).
        if (jd != null && !j.DoubleCross) Set("haggled:" + jd.Id);
        // A Johnson who means to sell you out agrees to anything. Too quickly.
        if (j.DoubleCross)
        {
            j.Pay = (int)(MathF.Round(j.Pay * 1.3f / 50) * 50);
            return Talks("Corporate")
                ? $"Johnson agrees before you finish the sentence: {M.Money(j.Pay)}. Nobody in a suit gives in that fast -- not unless they never mean to pay. (Corporate: this is a set-up.)"
                : $"\"Done. {M.Money(j.Pay)}.\" He didn't even blink.";
        }
        if (Rng.Chance(chance))
        {
            float up = jd?.Id == "grey" ? 1.15f : Rng.Range(1.15f, 1.35f);
            j.Pay = (int)(MathF.Round(j.Pay * up / 50) * 50);
            return $"{jd?.Name ?? "Johnson"} sighs. \"Fine. {M.Money(j.Pay)}.\"";
        }
        if (Rng.Chance(0.4f))
        {
            Board.Remove(j);
            return "\"We're done here.\" Johnson cuts the line. The job is gone.";
        }
        j.Pay = (int)(MathF.Round(j.Pay * 0.9f / 50) * 50);
        return $"\"Don't push your luck.\" The offer drops to {M.Money(j.Pay)}.";
    }

    public string Accept(Job j)
    {
        if (ActiveJob >= 0 && ActiveJob != j.Id) return "You already have a job on. Finish it first.";
        if (j.Johnson && !j.Negotiated) return "Mr. Johnson wants to talk terms first.";
        ActiveJob = j.Id;
        BringRetained(j);
        // A gift from someone you once got out: the floor plans.
        if (Flag("freeschem") > 0 && j.Type is not (JobType.Courier or JobType.Race or JobType.Hub)) { j.Schematics = true; Set("freeschem", 0); }
        return null;
    }

    /// <summary>They are coming to your door: a Defend job at the safehouse, tonight.</summary>
    public Job OfferRaid()
    {
        City city = CityRef;
        if (city == null) return null;
        Lot home = city.Place(HasUpgrade("loft") ? "loft" : "home");
        Faction f = Grudge.Count > 0 && Grudge.Values.Max() >= 2 ? Grudge.OrderByDescending(g => g.Value).First().Key : Faction.Halcyon;
        var j = new Job
        {
            Id = NextJobId++, Type = JobType.Defend, Style = SiteStyle.Office, Faction = f, District = home.District, Threat = M.Clamp(MaxThreat, 2, 5),
            Seed = Rng.Next(), Expires = Day, Lot = home.Index, Employer = "fixer", Tag = "raid", Title = "RAID: your safehouse",
            Brief = $"{Tables.FactionNames[(int)f]} are coming for your place tonight. Hold it until they give up -- or lose a third of everything you have.",
            Target = "x", Item = "x",
        };
        j.Pay = 0;
        j.Karma = 4 + 2 * j.Threat;
        Board.Add(j);
        return j;
    }

    public void Drop()
    {
        Job j = Active;
        if (j == null) return;
        Courier = false;
        Board.Remove(j);
        ActiveJob = -1;
        Rep = Math.Max(0, Rep - 1);
        Say($"Walked away from {j.Title}. Word gets around.");
    }

    // ---- days -------------------------------------------------------------------

    /// <summary>A day passes. Sleeping (rested) also heals an injury.</summary>
    public void NextDay(City city, bool rested = false)
    {
        Day++;
        Heat = 0;
        // Whoever was riding with you got out somewhere in the night.
        if (Passenger != null) { Say($"{Passenger.Who} got out at a red light and didn't come back."); Passenger = null; }
        // How well you live is how well you mend.
        if (rested && Injuries > 0 && LifeTier >= 1) Injuries = LifeTier >= 2 ? 0 : Injuries - 1;
        if (rested) Tell(Hook.Sleep);
        if (Day % 7 == 0) foreach (Faction f in Grudge.Keys.ToList()) Grudge[f] = Math.Max(0, Grudge[f] - 1);
        if (HasUpgrade("loft") && Day % 5 == 0) Rep++;
        Job a = Active;
        if (a != null && a.Expires < Day)
        {
            Board.Remove(a);
            ActiveJob = -1;
            ResumeHeld();
            if (a.Tag == "raid")
            {
                int lost = Cash / 3;
                Cash -= lost;
                Count("spent", lost);
                Say($"Nobody was home when they kicked the door in. They took {M.Money(lost)} and your peace of mind.");
            }
            else
            {
                Rep = Math.Max(0, Rep - 1);
                Say($"{a.Title} expired. The client found someone else.");
                Lapsed(a);
            }
        }
        RefreshBoard(city, 2);
        Morning(city);
    }

    // ---- after a run ------------------------------------------------------------

    /// <summary>What a run starts with: the carried grenades plus van/armory extras.</summary>
    public Dictionary<string, int> RunGrenades()
    {
        var g = new Dictionary<string, int>();
        foreach (var kv in Grenades) if (kv.Value > 0) g[kv.Key] = kv.Value;
        if (Vehicle == "van") foreach (string k in g.Keys.ToList()) g[k]++;
        if (HasUpgrade("armory")) g["frag"] = g.GetValueOrDefault("frag") + 1;
        return g;
    }

    public int RunMedkits() => Medkits + (HasUpgrade("medbay") ? 1 : 0);

    public Debrief Finish(Job j, RunResult r)
    {
        if (r.ShotTrauma) Set("tlgrudge", 1);
        // Platinum: down with the job done, and the HTR team brings both of you out.
        bool platinum = r.Died && r.ObjectiveDone && TraumaPlatinum && TraumaLinks > 0;
        if (platinum)
        {
            TraumaLinks--;
            Count("traumalink");
            r.Died = false;
            r.Success = true;
            r.PayMul *= 0.5f;
        }
        Last = Settle(j, r);
        Debrief result = FinishInner(j, r);
        if (platinum) { Count("done.platinum"); result.Headline = "PLATINUM PICK-UP"; result.Lines.Insert(0, ("TraumaLink Platinum", "you and the job, out")); }
        if (!result.CareerOver) Aftermath(j, r, result);
        // After the pay is in: the drone rebuild, at its full quarter price (it may leave you short).
        if (r.DroneLost && DroneSel != null && Tables.Drones.FirstOrDefault(x => x.Id == DroneSel) is DroneDef dd && !result.CareerOver)
        {
            int fee = dd.Cost / 4;
            Cash = Math.Max(0, Cash - fee);
            Count("spent", fee);
            result.Lines.Add(($"Rebuilding the {dd.Name}", "-" + M.Money(fee)));
        }
        return result;
    }

    private Debrief FinishInner(Job j, RunResult r)
    {
        var d = new Debrief();

        Count("kos", r.KnockOuts);
        Count("bosses", r.Bosses);
        Count("walls", r.Walls);
        Count("spells", r.Spells);
        if (r.Datastores >= 3) Count("matrix");
        WellFed = false;
        NextNoAmbush = NextCamerasDown = NextFastSpells = false;
        if (!r.Died && r.HpLeft > 0 && r.HpLeft < 0.3f && Injuries < 3)
        {
            Injuries++;
            d.Lines.Add(("Badly hurt", $"injury ({Injuries}): -10 % max HP until healed"));
        }
        Kills += r.Kills;
        // Extras are spent first; whatever is left (and anything picked up) is kept.
        var start = RunGrenades();
        foreach (GrenadeDef g in Tables.Grenades)
        {
            int bonus = start.GetValueOrDefault(g.Id) - Grenades.GetValueOrDefault(g.Id);
            int left = r.GrenadesLeft.GetValueOrDefault(g.Id);
            Grenades[g.Id] = M.Clamp(left - bonus, 0, GrenadeCap);
        }
        Medkits = M.Clamp(r.MedkitsLeft - (RunMedkits() - Medkits), 0, 5);

        if (j.Hired != null)
        {
            var h = Tables.Hires.First(x => x.id == j.Hired);
            int cut = j.Story == 4 || Retained.Contains(h.id) ? 0 : r.Success ? (int)(j.Pay * CrewCut(h.id)) : 300;
            cut = Math.Min(cut, Cash + (r.Success ? j.Pay : 0));
            Cash -= cut;
            Count("spent", cut);
            d.Lines.Add(($"{h.name}'s {(r.Success ? "cut" : "retainer")}", cut == 0 ? "on the house" : "-" + M.Money(cut)));
        }
        if (r.Died)
        {
            Deaths++;
            if (TraumaLinks > 0)
            {
                Count("traumalink");
                Count($"failed.{j.Type}");
                TraumaLinks--;
                int fee = TraumaGold ? 0 : Cash / 5;
                Cash -= fee;
                Count("spent", fee);
                JobsFailed++;
                Rep = Math.Max(0, Rep - 1);
                d.Headline = "FLATLINED -- TRAUMALINK PICK-UP";
                if (fee > 0) d.Lines.Add(("Trauma team fee", "-" + M.Money(fee)));
                d.Lines.Add(("Job", "failed"));
                d.Lines.Add(("Street rep", "-1"));
                Say($"Went down on {j.Title}. TraumaLink got there in time.");
                CloseJob(j, r, d);
                NextDayAfterRun();
                return d;
            }
            Dead = true;
            d.Headline = "FLATLINED";
            d.CareerOver = true;
            d.Lines.Add(("Days survived", Day.ToString()));
            d.Lines.Add(("Jobs done", JobsDone.ToString()));
            d.Lines.Add(("Kills", Kills.ToString()));
            d.Lines.Add(("Karma earned", KarmaEarned.ToString()));
            return d;
        }

        if (r.Cash > 0) { Cash += r.Cash; d.Lines.Add(("Cash found", M.Money(r.Cash))); }
        foreach (LootItem l in r.Loot)
        {
            l.Faction = j.Type is JobType.Bounty or JobType.Hub ? -1 : (int)j.Faction;
            l.Day = Day;
            l.Hot = r.Alarm && (j.Threat >= 3 || r.Kills >= 5);
            if (l.Hot) Explain("hot", "Hot goods: what you carried out of a loud job sells for less for three days, and selling it draws heat.");
            Loot.Add(l);
            d.Lines.Add((l.Name, "to fence"));
        }

        if (r.Success)
        {
            int pay = j.Stiff ? 0 : (int)(j.Pay * r.PayMul);
            Count($"done.{j.Type}");
            Count("earned", pay + r.Cash);
            Count("jobpay", pay + r.Cash);
            if (!r.Alarm && !j.Driving) { Count("noalarm"); Count("noalarm." + j.Type); }
            if (r.Kills == 0 && !j.Driving) Count("nokill");
            if (r.Twist != null) Count("twist");
            if (j.Hired != null) Count("hire");
            if (r.Twist != null) d.Lines.Add(("Change of plan", r.PayMul > 1 ? $"+{(r.PayMul - 1) * 100:0} %" : r.PayMul < 1 ? $"-{(1 - r.PayMul) * 100:0} %" : "--"));
            Cash += pay;
            d.Lines.Insert(0, ("Payment", j.Stiff ? "never arrived" : M.Money(pay)));
            if (j.Stiff) Say($"{j.Title}: the Johnson's account was closed an hour after the job. Stiffed.");
            if (!r.Alarm && pay > 0 && GhostPays(j))
            {
                int bonus = pay / 4;
                Cash += bonus;
                Count("earned", bonus);
                Count("jobpay", bonus);
                d.Lines.Insert(1, ("Ghost bonus (no alarm)", M.Money(bonus)));
            }
            if (r.Kills == 0 && NoKillPays(j) && r.Seconds > 0)
            {
                int clean = pay * (j.JohnsonId == "vale" ? 30 : 15) / 100;
                Cash += clean;
                Count("earned", clean);
                Count("jobpay", clean);
                d.Lines.Insert(1, ("No-kill bonus", M.Money(clean)));
            }
            Karma += j.Karma;
            KarmaEarned += j.Karma;
            d.Lines.Add(("Karma", "+" + j.Karma));
            int rep = 1 + (j.Threat >= 4 ? 1 : 0);
            Rep += rep;
            d.Lines.Add(("Street rep", "+" + rep));
            Bump(j.Employer, d);
            if (j.Employer != "fixer" && JobsDone % 2 == 1) Bump("fixer", d);
            JobsDone++;
            d.Headline = "JOB DONE";
            if (j.Arc != null) FinishArc(j, d);
            if (j.Story is > 0 and <= 4)
            {
                Chapter = Math.Max(Chapter, j.Story);
                d.Lines.Add(("Story", $"chapter {j.Story} of 4"));
                if (Chapter >= Chapters.Length) WinSpire(d);
            }
            else if (j.Story > 8) FinishCampaign(j, d);
            else if (j.Story > 4)
            {
                Chapter2 = Math.Max(Chapter2, j.Story - 4);
                d.Lines.Add(("Toxic Tide", $"chapter {j.Story - 4} of 4"));
                if (Chapter2 >= TideChapters.Length) WinTide(d);
            }
            if (j.Tag == "raid")
            {
                Fame(-3);
                Count("raidsheld");
                d.Headline = "SAFEHOUSE HELD";
                d.Lines.Add(("They won't try that again", $"awareness -3"));
            }
            Say($"{j.Title}: done. {M.Money(pay)} wired.");
        }
        else
        {
            JobsFailed++;
            Count($"failed.{j.Type}");
            Rep = Math.Max(0, Rep - 1);
            d.Headline = "JOB FAILED";
            d.Lines.Add(("Street rep", "-1"));
            Say($"{j.Title}: botched.");
            if (j.Tag != null && j.Tag.StartsWith("danger:") && !r.Died) HurtContact(j.Tag[7..]);
            if (j.Tag == "raid")
            {
                int lost = Cash / 3;
                Cash -= lost;
                Count("spent", lost);
                d.Headline = "SAFEHOUSE LOST";
                d.Lines.Add(("The raiders took", "-" + M.Money(lost)));
            }
        }
        CloseJob(j, r, d);
        NextDayAfterRun();
        return d;
    }

    /// <summary>The ghost bonus is for sites: a car on the road raises no alarm to begin with.</summary>
    public static bool GhostPays(Job j) => !j.Driving;

    /// <summary>The no-kill bonus, for jobs where killing was an option and you didn't.</summary>
    public static bool NoKillPays(Job j) => j.Type is not (JobType.Courier or JobType.Race or JobType.Clear or JobType.Eliminate or JobType.Hack);

    private void Bump(string contact, Debrief d)
    {
        int l = Loyalty.GetValueOrDefault(contact);
        if (l >= 5) return;
        Loyalty[contact] = l + 1;
        d.Lines.Add(($"{Tables.Contact(contact).Name} loyalty", $"{l + 1}"));
    }

    /// <summary>What a job does to its faction's grudge: +1, nothing if they never knew you were there, -2 for seeing them off your own door.</summary>
    public static int GrudgeChange(Job j, RunResult r)
    {
        if (j.Type is JobType.Courier or JobType.Race or JobType.Hub or JobType.Bounty) return 0;
        if (r.Success && j.Type == JobType.Defend && (j.Tag == "raid" || j.Tag?.StartsWith("danger:") == true)) return -2;
        if (!r.Alarm && r.Kills == 0 && !r.Died) return 0;
        return 1;
    }

    public static string GrudgeNote(int g) => $"now {g}; at {Tables.GrudgeElite} their sites field elites, at {DangerGrudge} they come after you and your contacts";

    private void CloseJob(Job j, RunResult r, Debrief d)
    {
        InRun = -1;
        int by = GrudgeChange(j, r);
        string who = Tables.FactionNames[(int)j.Faction];
        if (by != 0)
        {
            Grudge[j.Faction] = M.Clamp(Grudge.GetValueOrDefault(j.Faction) + by, 0, 9);
            d.Lines.Add(($"{who} grudge", $"{(by > 0 ? "+" : "")}{by} ({GrudgeNote(Grudge[j.Faction])})"));
            if (by > 0) WarnGrudge(j.Faction);
        }
        else if (j.Type is not (JobType.Courier or JobType.Race or JobType.Hub or JobType.Bounty))
            d.Lines.Add(($"{who} grudge", "+0: they never knew you were there"));
        if (j.Driving) Courier = false;
        Board.Remove(j);
        if (ActiveJob == j.Id) { ActiveJob = -1; ResumeHeld(); }
    }

    /// <summary>A job you dropped everything for is over: the one you put on hold is yours again.</summary>
    private void ResumeHeld()
    {
        int id = Flag("onhold");
        if (id <= 0) return;
        Set("onhold", 0);
        Job back = Board.FirstOrDefault(b => b.Id == id - 1);
        if (back == null || ActiveJob >= 0) return;
        ActiveJob = back.Id;
        BringRetained(back);
        Say($"Back to {back.Title}.");
    }

    /// <summary>Drop everything: this job is yours now; whatever you were on waits until it's done.</summary>
    public void Urgent(Job j)
    {
        if (ActiveJob >= 0 && ActiveJob != j.Id) Set("onhold", ActiveJob + 1);
        ActiveJob = j.Id;
        BringRetained(j);
        TargetLot = j.Lot;
    }

    /// <summary>Set by the caller that owns the city; the run advances the day.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public City CityRef;

    private void NextDayAfterRun()
    {
        if (CityRef != null) NextDay(CityRef);
        else Day++;
    }
}
