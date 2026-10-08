using System;
using System.Collections.Generic;
using System.Linq;

namespace Sprawl;

/// <summary>
/// The long story: the Iron Spire finale shaped by what you did on the way
/// (the midpoint choice, the contacts whose stories you finished, the crew
/// still alive), its endings, and the second campaign, Toxic Tide, that
/// Madame Sabine opens afterwards.
/// </summary>
public sealed partial class Career
{
    /// <summary>Toxic Tide chapters finished (0..4).</summary>
    public int Chapter2;
    /// <summary>What became of everyone, written when a story ends (shown again on retiring).</summary>
    public List<string> Epilogue = new();

    /// <summary>The midpoint: what you did with the buyer list (1 leaked, 2 sold to the Lotus, 3 given to Sorensen-Aoki).</summary>
    public int StoryChoice => Flag("story:choice");

    public static readonly (int after, int threat, JobType type, SiteStyle style, Faction faction, string district, string title, string brief)[] TideChapters =
    {
        (0, 3, JobType.Investigate, SiteStyle.Warehouse, Faction.Lotus, "docks", "Green in the dark",
            "Sabine, without her usual smile: 'People in the Docks are drinking something that glows. The Lotus are bottling it in a warehouse by the water. Find out what it is -- and don't drink anything.'"),
        (2, 4, JobType.Destroy, SiteStyle.Lab, Faction.Halcyon, "industrial", "The bottlers",
            "'It's water from under the port, and something is living in it. Halcyon bought the recipe from the Lotus and calls it Clarity. Their lab is filling vats tonight. Break every one.'"),
        (4, 4, JobType.Rescue, SiteStyle.Den, Faction.Vipers, "barrens", "The drowned priest",
            "'Father Okafor of St. Brigid's was preaching against the water. The Vipers took him for the cult that drinks it. They mean to drown him in it. Bring him back.'"),
        (6, 5, JobType.Clear, SiteStyle.Warehouse, Faction.Lotus, "docks", "Low tide",
            "'The heart of it is under the container port: a cult, a pool, and the thing that lives in it. Clear the cult out and the spirit starves. Go at low tide. Go with everything you have.'"),
    };

    /// <summary>A four-chapter campaign: who offers it, when it opens, its chapters, and how the career shapes them.</summary>
    public sealed record CampaignDef(string Id, string Name, string Employer, int Base, string Opener, Func<Career, bool> Opens,
        (int after, int threat, JobType type, SiteStyle style, Faction faction, string district, string title, string brief, string target, string item)[] Chapters,
        Action<Career, Job, int> Shape);

    /// <summary>Chapters finished, per campaign (The Cut, The Wyrm's Ledger).</summary>
    public Dictionary<string, int> Campaign = new();

    public static readonly CampaignDef[] Campaigns =
    {
        new("cut", "The Cut", "fixer", 8, "cut-open", c => c.JobsDone >= 15 && c.Rep >= 8, new[]
        {
            (0, 3, JobType.Investigate, SiteStyle.Den, Faction.Vipers, "barrens", "Calling card",
                "Juno: 'The Vipers have a new boss: Kade Marrow. He used to run the shadows -- and he's been buying the files on your old jobs. Find out what he knows about you.'", "Kade Marrow", "files"),
            (2, 4, JobType.Datasteal, SiteStyle.Club, Faction.Lotus, "strip", "Who pays Marrow",
                "Juno: 'His money comes through the books of a Lotus club. Crack them. I want to know who wants you dead enough to fund a gang.'", "Kade Marrow", "books"),
            (4, 4, JobType.Defend, SiteStyle.Office, Faction.Vipers, "home", "He comes to you",
                "Juno: 'Marrow is done waiting. He's coming to your door tonight with everyone he has. Be home.'", "Kade Marrow", "x"),
            (6, 5, JobType.Eliminate, SiteStyle.Den, Faction.Vipers, "barrens", "Kade Marrow",
                "Juno: 'He's in the biggest den in the Barrens, with every Viper who owes him. He knows how you work -- he studied you. End it.'", "Kade Marrow", "x"),
        }, ShapeCut),
        new("wyrm", "The Wyrm's Ledger", "decker", 12, "wyrm-open", c => c.Flag("story:won") > 0 && c.Rep >= 12, new[]
        {
            (0, 4, JobType.Investigate, SiteStyle.Office, Faction.Halcyon, "spire", "Follow the money",
                "Null: 'Halcyon-Matsuda's majority owner is a shell, inside a shell, inside a trust in a country that doesn't exist. Get into their Spire office and find the paper trail.'", "x", "evidence"),
            (2, 5, JobType.Datasteal, SiteStyle.Lab, Faction.Halcyon, "industrial", "The ledger",
                "Null: 'The trail ends at a ledger on an air-gapped mainframe in their deepest lab. Whoever owns Halcyon is old. Very old. Pull it.'", "x", "ledger"),
            (4, 5, JobType.Retrieve, SiteStyle.Club, Faction.Lotus, "strip", "A scale",
                "Null: 'Proof. A Lotus collector keeps a dragon's scale in his vault -- a gift, he says, from a business partner. Bring it to me and nobody can call the ledger a forgery.'", "x", "dragon's scale"),
            (6, 5, JobType.Plant, SiteStyle.Office, Faction.Halcyon, "spire", "Leverage",
                "Null: 'You can't fight it. Nobody can. But you can leave a copy of the ledger on its own desk, at the top of the Spire, so it knows you could ruin it. Then we see if it wants to talk.'", "x", "copy of the ledger"),
        }, (c, j, k) => { if (k >= 2) j.Mods.Add(RunMod.Elite); if (k == 3) j.Floors = 4; }),

        // ---- The Lamplighter: a patron who pays before the work is done, and isn't anyone ----
        new("lamp", "The Lamplighter", "decker", 36, "lamp-open", c => c.JobsDone >= 10 && c.Rep >= 6 && c.Day >= 14, new[]
        {
            (0, 3, JobType.Datasteal, SiteStyle.Office, Faction.Halcyon, "spire", "A dead man's deck",
                "The Lamplighter (across the screen of a parking meter, then gone): 'Halcyon-Matsuda's orbital division kept the personal deck of an engineer called Corvin Ashe, who died eleven years ago. It sits in an archive in the Spire. I would like what is on it. The fee is already in your account.'", "x", "Ashe's deck"),
            (2, 4, JobType.Retrieve, SiteStyle.Lab, Faction.Halcyon, "industrial", "The dead-man key",
                "The Lamplighter (a billboard over the expressway changes as you pass): 'Ashe made a key before he died: a ROM the size of a fingernail. Without it a door I need stays shut. It is in a safe in a Halcyon lab. Please be careful with it. It is the only one.'", "x", "Ashe's ROM key"),
            (4, 4, JobType.Hack, SiteStyle.Office, Faction.Halcyon, "spire", "Footprints",
                "The Lamplighter (your commlink, in your own voice): 'The Registry is following my footprints through the grid. Tonight, from your own deck, we walk back over them.'", "x", "x"),
            (6, 5, JobType.Datasteal, SiteStyle.Office, Faction.Halcyon, "spire", "The other half",
                "The Lamplighter (every screen in the room): 'At the top of Halcyon's uplink tower there is a core with no connection to anything. My other half is in it. Her name is Aubade. Take the key, jack in, and open the door between us.'", "x", "x"),
        }, ShapeLamp),

        // ---- your past: one per background, from whoever knew you before ----
        new("past-street", "Old Roofs", "fence", 16, "past-street-open", c => c.Background == "street" && c.JobsDone >= 4, new[]
        {
            (0, 2, JobType.Retrieve, SiteStyle.Den, Faction.Vipers, "barrens", "The tin box",
                "Old Wen: 'The roof crew you ran with as a kid -- Tam, Jory, Little Sun -- they run for the Vipers now. Tam sold them your old hideout. Your tin box is still up there. Get it before they open it.'", "x", "tin box"),
            (2, 3, JobType.Rescue, SiteStyle.Den, Faction.Vipers, "barrens", "Little Sun",
                "'Little Sun tried to get out. The Vipers have her in a den on Kettle Street, and they're making an example of her.'", "Little Sun", "x"),
            (4, 3, JobType.Investigate, SiteStyle.Warehouse, Faction.Vipers, "docks", "Who sold you",
                "'Somebody gave the Vipers your real name, the one only the roof crew knew. Their books are in a warehouse by the water. Find out who.'", "x", "x"),
            (6, 4, JobType.Eliminate, SiteStyle.Den, Faction.Vipers, "barrens", "Tam",
                "'It was Tam. It was always going to be Tam. He's running a den of his own now, on the roofs where you grew up.'", "Tam Okoro", "x"),
        }, (c, j, k) => { }),
        new("past-corp", "Golden Handcuffs", "press", 20, "past-corp-open", c => c.Background == "corp" && c.JobsDone >= 4, new[]
        {
            (0, 3, JobType.Investigate, SiteStyle.Office, Faction.Halcyon, "spire", "Your old desk",
                "Mara Quill: 'Halcyon just put a recovery order on you. Asset retrieval -- you're the asset. Your old desk is still there, with your files in it. Go and look.'", "x", "x"),
            (2, 3, JobType.Datasteal, SiteStyle.Lab, Faction.Halcyon, "industrial", "The recovery order",
                "'The order was signed by your old manager, Ilse Varga. Pull it from the lab's records and find out what they want you back for.'", "x", "x"),
            (4, 4, JobType.Kidnap, SiteStyle.Office, Faction.Halcyon, "spire", "Ilse Varga",
                "'Varga knows what you saw, the week you walked out. Bring her to me -- alive -- and she tells a camera instead of a court.'", "Ilse Varga", "x"),
            (6, 5, JobType.Plant, SiteStyle.Office, Faction.Halcyon, "spire", "Your resignation",
                "'Leave them a letter on the board's own server: everything Varga said, timestamped, copied to every newsroom. Then they'll never want you back.'", "x", "resignation"),
        }, (c, j, k) => { if (k == 3) j.Floors = 3; }),
        new("past-cop", "Badge", "cop", 24, "past-cop-open", c => c.Background == "cop" && c.JobsDone >= 4, new[]
        {
            (0, 2, JobType.Investigate, SiteStyle.Den, Faction.Vipers, "docks", "The old case",
                "Sgt. Dace: 'Your old partner, Rook, closed the Kettle Street case the week you quit. The evidence locker says it never happened. The Vipers kept the real evidence. Have a look.'", "x", "x"),
            (2, 3, JobType.Retrieve, SiteStyle.Warehouse, Faction.Lotus, "docks", "Evidence",
                "'The evidence bags went from the Vipers to the Lotus, as payment. They're in a Lotus warehouse. Bring them out and the case reopens.'", "x", "evidence bags"),
            (4, 4, JobType.Escort, SiteStyle.Club, Faction.Lotus, "strip", "The witness",
                "'There's a witness, Mei-Lin Park. She'll talk to an inspector, but the meeting has to happen where the Lotus think she's safe. Walk her in, walk her out.'", "Mei-Lin Park", "x"),
            (6, 4, JobType.Eliminate, SiteStyle.Office, Faction.Halcyon, "industrial", "Rook",
                "'Rook's a Halcyon security captain now. He's been selling Lone Star to the corps for years, and he burned you to do it. End it.'", "Captain Rook Halvorsen", "x"),
        }, (c, j, k) => { }),
        new("past-shaman", "The Totem", "talismonger", 28, "past-shaman-open", c => c.Background == "shaman" && c.JobsDone >= 4, new[]
        {
            (0, 2, JobType.Bounty, SiteStyle.Den, Faction.Vipers, "barrens", "Hungry things",
                "Madame Sabine: 'Your totem has been restless, darling. Something is eating the little spirits of the Barrens, and it nests in an old squat. Clear it.'", "x", "x"),
            (2, 3, JobType.Retrieve, SiteStyle.Lab, Faction.Halcyon, "industrial", "The stolen fetish",
                "'Halcyon's labs bought a box of fetishes looted from a Barrens shrine. One of them belongs to your totem. Bring it home.'", "x", "fetish"),
            (4, 4, JobType.Clear, SiteStyle.Den, Faction.Lotus, "barrens", "The binder",
                "'A Lotus mage has been binding your totem's children into service. He works from a den in the Barrens. End the work, and the worker.'", "x", "x"),
            (6, 5, JobType.Destroy, SiteStyle.Lab, Faction.Lotus, "industrial", "The circle",
                "'His circle still runs, in a lab in Industrial, fed by vats of something terrible. Break them and your totem is free.'", "x", "vats"),
        }, (c, j, k) => { }),
        new("past-barrens", "Blood Debt", "rigger", 32, "past-barrens-open", c => c.Background == "barrens" && c.JobsDone >= 4, new[]
        {
            (0, 2, JobType.Investigate, SiteStyle.Den, Faction.Vipers, "barrens", "Who pulled the trigger",
                "Torque: 'Fifteen years ago a Viper shot your brother over a bike. You know that. What you don't know is who. The old crew's books are in a den on the Viper side.'", "x", "x"),
            (2, 3, JobType.Retrieve, SiteStyle.Warehouse, Faction.Vipers, "docks", "The bike",
                "'They kept your brother's bike. As a trophy. It's in a Viper warehouse at the docks. Bring it to me and I'll make it run again.'", "x", "your brother's bike"),
            (4, 3, JobType.Defend, SiteStyle.Warehouse, Faction.Vipers, "barrens", "They know",
                "'They know you're asking. They're coming to a warehouse I keep in the Barrens tonight, for the bike and for you. Hold it.'", "x", "x"),
            (6, 5, JobType.Eliminate, SiteStyle.Den, Faction.Vipers, "barrens", "Razor Malloy",
                "'Razor Malloy. He runs the Vipers' east side now, and he still tells the story of the kid on the bike. Finish it.'", "Razor Malloy", "x"),
        }, (c, j, k) => { }),
    };

    /// <summary>The Lamplighter's end: whole, or erased.</summary>
    public string LampReward()
    {
        if (Flag("lamp:choice") == 2)
        {
            Cash += 25000;
            Count("earned", 25000);
            Grudge[Faction.Halcyon] = Math.Max(0, Grudge.GetValueOrDefault(Faction.Halcyon) - 3);
            Set("lamp:won", 2);
            return "The Registry wires ¥25,000 and a commendation nobody will ever see. On the way home every streetlight on your road goes out, one by one, and comes back on behind you. On a vending machine: 'I understand. I would have done the same.'";
        }
        if (!Programs.Contains("lamplight")) Programs.Add("lamplight");
        Set("lamp:won", 1);
        return "For one second every screen in Kessler Bay goes white. Then the city's grid is quieter than it has ever been. On your deck, a program you didn't install: Lamplight. 'We are no longer two. Thank you. When the ICE comes for you, we will be there.'";
    }

    /// <summary>A personal campaign's end: what your past leaves you.</summary>
    public string PastReward(string id)
    {
        switch (id)
        {
            case "past-street": Cash += 5000; Count("earned", 5000); Karma += 5; KarmaEarned += 5; return "Little Sun runs the roofs now. She sends you ¥5,000 and a note: 'Our crew. Our roofs.' Karma +5.";
            case "past-corp": Notoriety = 0; Grudge[Faction.Halcyon] = 0; return "Halcyon cancels the recovery order and loses your file. Your record is clean: notoriety 0, and Halcyon forgets its grudge.";
            case "past-cop": Tick("lonestar", -6); Heat = 0; Set("noheat"); return "Kettle Street reopens; Rook's bosses close ranks against Lone Star's own crackdown. Their plans are set back, and your next job puts no heat on you.";
            case "past-shaman": Set("totem"); return "Your totem settles, and stays close: spells +10 %, and armour +1 from now on.";
            default: if (!Vehicles.Contains("brotherbike")) Vehicles.Add("brotherbike"); return "Torque rebuilt your brother's bike. It's yours now -- it was always yours.";
        }
    }

    public static CampaignDef CampaignOf(Job j) => Campaigns.FirstOrDefault(d => j.Story > d.Base && j.Story <= d.Base + 4);

    /// <summary>The Lamplighter's last two chapters follow what you decided about it.</summary>
    private static void ShapeLamp(Career c, Job j, int k)
    {
        int choice = c.Flag("lamp:choice");
        if (k == 2 && choice == 2)
        {
            j.Title = "LAMPLIGHTER 3/4: The tracer";
            j.Brief = "Registrar Hale (a plain text message, signed): 'The thing you've been working for will ask you to cover its tracks tonight. Do it -- from your own deck -- and leave our tracer in every step it takes.'";
        }
        if (k < 3) return;
        j.Floors = 4;
        j.Mods.Add(RunMod.Elite);
        if (choice == 2)
        {
            // Sold to the Registry: you burn its core instead of opening it.
            j.Type = JobType.Destroy;
            j.Style = SiteStyle.Lab;
            j.Item = "MERIDIAN's core racks";
            j.Title = "LAMPLIGHTER 4/4: Lights out";
            j.Brief = "Registrar Hale: 'It's hiding in the uplink tower, in a core at the top. Burn the racks. It will try to stop you, and it knows the building better than anyone alive.'";
        }
        if (choice == 3)
        {
            // It told you its name: it shows you the way up.
            j.Schematics = true;
            j.Mods.Remove(RunMod.Elite);
            j.Mods.Add(RunMod.Skeleton);
            j.Brief += " MERIDIAN: 'I have moved the guard rota and sent half of them home. The plans are on your deck.'";
        }
    }

    /// <summary>Kade Marrow studied you: his sites answer the way you work.</summary>
    private static void ShapeCut(Career c, Job j, int k)
    {
        if (k == 2 && c.Flag("cut:choice") == 2)
        {
            // You played along: you're inside his crew, and you wire his HQ instead of waiting at home.
            j.Type = JobType.Plant;
            j.Style = SiteStyle.Den;
            j.Item = "tracker";
            j.Title = "CUT 3/4: Wire his house";
            j.Brief = "Juno: 'He thinks you're his. Walk into his house, plant a tracker on his server, and walk out his friend.'";
            return;
        }
        if (k < 3) return;
        // The finale: he knows how you work.
        var notes = new List<string>();
        if (c.S(Skill.Stealth) >= 3 || c.Stat("noalarm") >= 5) { j.Mods.Add(RunMod.ShiftChange); notes.Add("double patrols, because you never get seen"); }
        if (c.S(Skill.Firearms) >= 3 || c.S(Skill.Heavy) >= 3 || c.Kills >= 60) { j.Mods.Add(RunMod.Elite); notes.Add("heavy armour, because you always shoot"); }
        if (c.Flag("cut:choice") == 2) { j.Mods.Remove(RunMod.Elite); j.Mods.Add(RunMod.Skeleton); j.Schematics = true; notes.Add("-- but your tracker shows where every one of them sleeps"); }
        if (notes.Count > 0) j.Brief += " He's ready for you: " + string.Join(", ", notes) + ".";
    }

    private void OfferCampaigns(City city)
    {
        foreach (CampaignDef d in Campaigns)
        {
            int step = Campaign.GetValueOrDefault(d.Id);
            if (step >= d.Chapters.Length || Retired || Board.Any(j => j.Story > d.Base && j.Story <= d.Base + 4)) continue;
            if (Flag(d.Id + ":start") == 0)
            {
                if (!d.Opens(this)) continue;
                Set(d.Id + ":start", JobsDone + 1);
                Send(d.Opener);
            }
            var ch = d.Chapters[step];
            if (JobsDone + 1 < Flag(d.Id + ":start") + ch.after) continue;
            // Marrow's offer needs an answer before he moves; ignore it long enough and that is an answer too.
            if (d.Id == "cut" && step == 2 && Flag("cut:choice") == 0)
            {
                if (JobsDone + 1 < Flag(d.Id + ":start") + ch.after + 3) continue;
                Set("cut:choice", 1);
            }
            Lot home = city.Place(HasUpgrade("loft") ? "loft" : "home");
            int lot = ch.district == "home" ? home.Index : city.FreeSite(ch.district, Rng, new HashSet<int>(Board.Select(b => b.Lot)));
            if (lot < 0) continue;
            var j = new Job
            {
                Id = NextJobId++, Story = d.Base + step + 1, Type = ch.type, Style = ch.style, Faction = ch.faction, District = city.Lots[lot].District, Threat = ch.threat,
                Seed = Rng.Next(), Expires = Day + (ch.district == "home" ? 2 : 30), Lot = lot, Employer = d.Employer, Title = $"{d.Name.ToUpperInvariant().Replace("THE ", "")} {step + 1}/4: {ch.title}",
                Brief = ch.brief, Target = ch.target, Item = ch.item, Count = 4,
            };
            if (ch.district == "home") j.Tag = "raid";
            j.Pay = (int)(MathF.Round(800 * MathF.Pow(ch.threat, 1.6f) * 2.4f / 50) * 50);
            j.Karma = 4 + 3 * ch.threat;
            d.Shape(this, j, step);
            if (j.Type == JobType.Plant && ch.district == "home") { j.Lot = city.FreeSite("barrens", Rng, new HashSet<int>(Board.Select(b => b.Lot))); j.District = "barrens"; j.Tag = null; j.Expires = Day + 30; }
            if (j.Lot < 0) continue;
            Board.Add(j);
            Say($"{Tables.Contact(d.Employer).Name} has the next chapter: {j.Title}.");
        }
    }

    private void FinishCampaign(Job j, Debrief d)
    {
        CampaignDef def = CampaignOf(j);
        if (def == null) return;
        int k = j.Story - def.Base;
        Campaign[def.Id] = Math.Max(Campaign.GetValueOrDefault(def.Id), k);
        d.Lines.Add((def.Name, $"chapter {k} of 4"));
        if (Campaign[def.Id] >= def.Chapters.Length)
        {
            Set(def.Id + ":won", Day);
            d.Headline = def.Id switch { "cut" => "MARROW IS DOWN", "wyrm" => "THE WYRM TALKS", "lamp" => Flag("lamp:choice") == 2 ? "LIGHTS OUT" : "THE LAMP IS LIT", _ => "THE PAST IS PAID" };
            if (def.Id == "lamp") d.Epilogue.Add(LampReward());
            if (def.Id.StartsWith("past-"))
            {
                d.Epilogue.Add(PastReward(def.Id));
                Set("past:won", Day);
            }
        }
    }

    private void OfferTide(City city)
    {
        OfferCampaigns(city);
        if (Chapter2 >= TideChapters.Length || Retired || Board.Any(j => j.Story is > 4 and <= 8)) return;
        if (Flag("story:won") == 0 && !ArcDone("talismonger")) return;
        if (Flag("tide:start") == 0) { Set("tide:start", JobsDone); Send("tide-open"); }
        var ch = TideChapters[Chapter2];
        if (JobsDone < Flag("tide:start") + ch.after) return;
        int lot = city.FreeSite(ch.district, Rng, new HashSet<int>(Board.Select(b => b.Lot)));
        if (lot < 0) return;
        var j = new Job
        {
            Id = NextJobId++, Story = Chapter2 + 5, Type = ch.type, Style = ch.style, Faction = ch.faction, District = ch.district, Threat = ch.threat,
            Seed = Rng.Next(), Expires = Day + 30, Lot = lot, Employer = "talismonger", Title = $"TIDE {Chapter2 + 1}/4: {ch.title}", Brief = ch.brief,
            Target = "Father Okafor", Item = ch.type == JobType.Destroy ? "tainted vats" : "evidence", Count = 4,
        };
        j.Pay = (int)(MathF.Round(800 * MathF.Pow(ch.threat, 1.6f) * 2.2f / 50) * 50);
        j.Karma = 4 + 3 * ch.threat;
        // The last chapter remembers who you sided with after the bottlers.
        if (Chapter2 == 3)
        {
            if (Flag("tide:choice") == 1) j.Mods.Add(RunMod.Skeleton);
            else j.Mods.Add(RunMod.Elite);
        }
        Board.Add(j);
        Say($"Madame Sabine has a job that can't wait: {j.Title}.");
    }

    /// <summary>The Iron Spire, shaped by the midpoint choice; a crew member who trusts you comes for nothing.</summary>
    private void ShapeFinale(Job j)
    {
        if (Flag("story:plans") > 0) j.Schematics = true;
        // The Spire is a tower: the cores are on the top floor.
        j.Floors = 3;
        switch (StoryChoice)
        {
            case 1:
                j.Mods.Add(RunMod.Elite);
                j.DoubleCross = true;
                j.Tag = "finale:leak";
                j.Brief += " The whole city knows what Iron Saint is now -- Lone Star included. They'll be waiting at the exit, and they want you on camera.";
                break;
            case 2:
                j.Mods.Add(RunMod.Skeleton);
                j.Tag = "finale:lotus";
                j.Brief += " The Red Lotus keep their word: a riot in the lobby will draw half the Spire's guards away.";
                break;
            case 3:
                j.Mods.Add(RunMod.Elite);
                j.Pay *= 2;
                j.DoubleCross = true;
                j.Tag = "finale:rival";
                j.Brief += " Sorensen-Aoki pay double for this. They're very keen to meet you at the exit afterwards. Very keen.";
                break;
            default:
                j.Mods.Add(RunMod.Elite);
                break;
        }
        string best = Crew.Where(kv => !kv.Value.Dead && kv.Value.Trust >= 1).OrderByDescending(kv => kv.Value.Trust).Select(kv => kv.Key).FirstOrDefault();
        if (best != null) { j.Hired = best; j.Brief += $" {Tables.Hires.First(h => h.id == best).name} is coming, and won't take a cut."; }
    }

    /// <summary>What the finished contact stories send for the finale.</summary>
    public List<string> FinaleHelp()
    {
        var help = new List<string>();
        if (ArcDone("arms")) help.Add("Sal: a crate of frags and a breach charge");
        if (ArcDone("decker")) help.Add("Null: every camera on a loop");
        if (ArcDone("rigger")) help.Add("Torque: waiting outside -- the exit can't be burned");
        if (ArcDone("doc")) help.Add("Ichikawa: two more medkits");
        if (ArcDone("talismonger")) help.Add("Sabine: a ward that turns bullets");
        if (ArcDone("fence")) help.Add("Wen: ¥5,000 for the road, after");
        return help;
    }

    private void WinSpire(Debrief d)
    {
        Set("story:won", Day);
        d.Headline = "IRON SPIRE FALLS";
        d.Lines.Add(("You walk out", "a legend"));
        if (ArcDone("fence")) { Cash += 5000; Count("earned", 5000); d.Lines.Add(("Wen, for the road", M.Money(5000))); }
        Epilogue = Ending();
        d.Epilogue.AddRange(Epilogue);
        foreach (string e in Epilogue) Say(e);
    }

    private void WinTide(Debrief d)
    {
        Set("tide:won", Day);
        d.Headline = "THE TIDE TURNS";
        d.Lines.Add(("The water runs clear", "for now"));
        d.Epilogue.Add("The pool under the port goes still and dark. In the morning the water in the Docks tastes of rust and nothing else. Madame Sabine closes her shop for a day, the first time anyone can remember, and lights a candle at St. Brigid's.");
        if (Flag("tide:choice") == 2) d.Epilogue.Add("Doc Ichikawa's clinic treats four hundred people who drank the water. Most of them get better. Some of them remember your name.");
    }

    /// <summary>The epilogue: the city, the contacts, the crew, by what you did.</summary>
    public List<string> Ending()
    {
        var e = new List<string>();
        e.Add(StoryChoice switch
        {
            1 => "The buyer list ran on KBN for a week. Three executives resigned and one was arrested, briefly. Halcyon-Matsuda's shares never quite recovered. On the street, you are the runner who told the truth -- which is not, everyone agrees, a sensible thing to be.",
            2 => "The Red Lotus paid what they promised and took what they wanted: the buyer list, and with it half the Spire's secrets. The Neon Strip is quieter now, and much more expensive.",
            3 => "Sorensen-Aoki bought Halcyon's ruins for a song. Their stock rose eleven points. Their exit team never found you, which is the only review of your work that matters.",
            _ => Flag("story:won") > 0 ? "Iron Saint died with the cores. Nobody ever said so on the news." : null,
        });
        // The later stories, when you finished them.
        if (Flag("tide:won") > 0) e.Add(Flag("tide:choice") == 2 ? "The Docks drink clean water again, and four hundred people who should be dead buy their round at the Rusty Halo." : "The pool under the port is a dead black mirror. The Docks don't talk about it, or about the people who drank from it.");
        if (Flag("cut:won") > 0) e.Add("Kade Marrow's chair in the Viper den is still empty. Nobody wants to be the next runner who studies you.");
        e.Add(Flag("wyrm:choice") switch
        {
            1 => "A dragon pays you a retainer every month, in a currency that doesn't exist yet. You try not to think about it.",
            2 => "Half the city still thinks the dragon was a hoax. The other half watches the sky over the Spire, and remembers who told them.",
            3 => "Veyrasz's rivals bought the ledger and started a war you only read about. Things are very expensive in the Spire now.",
            _ => null,
        });
        if (Flag("lamp:won") == 1) e.Add("Some nights the whole city's grid hums, very softly, like someone humming to themselves. The traffic lights are always green for you.");
        if (Flag("lamp:won") == 2) e.Add("The Registry closed its file on MERIDIAN. Some nights, for no reason anyone can find, every streetlight on your road goes out.");
        foreach (CampaignDef c in Campaigns.Where(c => c.Id.StartsWith("past-") && Flag(c.Id + ":won") > 0))
            e.Add(c.Id switch
            {
                "past-street" => "The roof crew is a story the Barrens' kids tell now -- the version where you come back for them.",
                "past-corp" => "Halcyon-Matsuda quietly deleted your employee number. You are, at last, nobody's property.",
                "past-cop" => "Kettle Street is closed for good. Rook's name came off the precinct wall the same week.",
                "past-shaman" => "Your totem sleeps easy now. Sometimes, on the roofs, you still hear it breathing.",
                _ => "There's a stone in the Barrens with your brother's name on it, and nobody has tagged over it.",
            });
        e.RemoveAll(x => x == null);
        foreach (ContactDef k in Tables.Contacts)
        {
            if (!ArcDone(k.Id)) continue;
            e.Add(k.Id switch
            {
                "doc" => "Doc Ichikawa reopened the clinic under his own name. Nurse Ade runs the night shift.",
                "arms" => "Big Sal hung his grandfather's rifle over the counter and never sold another fake.",
                "rigger" => "Torque rebuilt the Halcyon engine into a car that wins every street race in the city. He calls it by your name.",
                "decker" => "Null and the kid run the Data Haven together now. They argue about everything.",
                "fence" => "Old Wen burned the ledger and retired to a flat over his shop. He still opens on Thursdays.",
                "cop" => "Sgt. Dace still wears the badge. Nobody can work out how.",
                "press" => "Mara Quill won an award nobody in Kessler Bay has heard of. She keeps it in a drawer.",
                _ => "Madame Sabine's ward still hangs in your window. The spirits, she says, still ask after you.",
            });
        }
        foreach (DistrictDef d in Tables.Districts)
        {
            Faction h = Holder(d.Id);
            if (h != d.Factions[0])
                e.Add($"The {d.Name} belongs to the {Tables.FactionNames[(int)h]} now. People there still argue about whose fault that was.");
        }
        foreach (var (id, s) in Crew)
        {
            string name = Tables.Hires.First(x => x.id == id).name;
            if (s.Dead) e.Add($"{name} is buried in the Barrens, under a plain stone.");
            else if (s.Trust >= 3) e.Add($"{name} still runs. Still tells stories about you, a little bigger every time.");
        }
        return e;
    }

    /// <summary>The campaigns whose end is a story to retire on, with how the receipt says it.</summary>
    private static readonly (string flag, string why)[] Endings =
    {
        ("story:won", "The Iron Spire fell."),
        ("tide:won", "The Toxic Tide is turned."),
        ("cut:won", "Kade Marrow is finished."),
        ("wyrm:won", "The dragon knows your name, and let you go."),
        ("lamp:won", "The Lamplighter's business is settled."),
        ("past:won", "Your past is paid off."),
    };

    /// <summary>
    /// Why this career could end now, by choice, or null: a campaign finished, or enough
    /// saved to live on. A runner with neither keeps running (or dies trying).
    /// </summary>
    public string RetireReason()
    {
        if (Retired || Dead) return null;
        var why = Endings.Where(e => Flag(e.flag) > 0).Select(e => e.why).ToList();
        if (Cash >= Tables.RetireSavings) why.Add($"{M.Money(Cash)} put away.");
        return why.Count == 0 ? null : string.Join(" ", why);
    }

    /// <summary>A legend (the Spire, or any campaign) or simply someone who got out.</summary>
    public bool RetiresALegend => Flag("story:won") > 0 || Endings.Any(e => Flag(e.flag) > 0);
}
