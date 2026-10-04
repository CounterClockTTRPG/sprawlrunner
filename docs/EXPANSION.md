# Sprawlrunner: story events and world dynamics

> **Status.** Everything here is built (fourth and fifth passes); the sixth pass
> (`docs/NEXT.md`) built on it.
>
> **Fourth pass.** Built, and described with every number in
> `docs/DESIGN.md` ("Fourth pass: a city with a memory"): the storylet engine
> with mail, calls and scenes (sections 3); turf, faction clocks, happenings
> and the news (4.1-4.3); TraumaLink ambulances, the pick-up scene and Gold
> (4.4); fates, run choices, Notoriety and Public Awareness (5.1-5.3); burned
> exits and double-crosses with causes, stiffs, tells and payback (5.4); the
> main story with leads, a midpoint choice, a finale shaped by the career,
> endings and legacy perks (6.1); Toxic Tide (6.2); the contacts' lives after
> their stories and a fork-free set of scenes (6.3, partly); crew trust,
> banter, upgrades, personal jobs and permadeath (7); rent, the block and the
> safehouse raid (8); Investigate, Plant and Defend jobs (9); etiquettes;
> mail-only contacts Mara Quill, Sgt. Dace and Vesper (6.4, partly).
>
> **Fifth pass**: everything that was left -- see "Fifth pass: the rest of the plan" in
> `docs/DESIGN.md`. Nothing from the plan is left; what is still open
> is balance, once a person has played the new systems.

An analysis of where the career layer stands and how to grow it: story events,
a city that changes, consequences that come back, and ideas from the Shadowrun
CRPGs turned into systems that fit this codebase (`Sim` rules with no Godot in
them, everything in tables, deterministic from the career seed, checked by the
self-check and the bot).

---

## 1. Where the game stands

The run layer is deep: destructible sites, stealth, factions with rosters,
twists, conditions, bosses, drones, the Matrix. The career layer around it is
wide, but it is mostly **numbers changing on a static stage**.

What already carries story:

| System | Where | What it is |
|---|---|---|
| Main story | `Career.Chapters` | 4 fixed jobs from Juno, gated by `JobsDone` (3/5/8/11), ends in retirement |
| Contact arcs | `Career.Arcs` | 6 x 3 fixed jobs, gated by loyalty, end in a unique reward |
| Mr. Johnson | `Job.Johnson`, `Haggle` | negotiate / haggle / walk; 15 % set-ups |
| Twists | `Run.Twist` | 4 mid-run plan changes |
| Grudges | `Career.Grudge` | per-faction counter: elite security at 3, hitters at 5 |
| Heat | `CitySim.Pursuit` | street-level police pressure, cleared by sleep |
| News | `Career.News` | a 40-line log, shown in the terminal, PDA and city HUD |
| Rumours | `HubGen.Rumours` | 10 fixed tips from street kids |

The gaps, in order of how much they hold the game back:

1. **The world never changes.** Districts have fixed threat ranges and
   factions; nothing the player does moves them. Destroying the Vipers' chop
   shop three times leaves the Barrens exactly as Viper as before. Grudge is
   the only faction memory and it decays back to zero.
2. **Jobs don't connect.** Each generated job is a sealed seed. The target you
   let flee, the hostage you rescued, the datachip you read -- none of them
   exist after the debrief.
3. **No choices with weight.** The only decisions outside runs are what to buy
   and which job to take. The story and every arc are linear; nothing branches,
   nothing can be lost except by dying.
4. **Story is paced by a job counter.** Chapter 2 arrives after 5 jobs whatever
   happened in them. The city doesn't *push* -- no event ever comes to the
   player unasked.
5. **Few channels.** Story reaches the player through a brief, a debrief quote
   and a news line. There is no inbox, no call, no scene in the city.
6. **The long career runs dry.** After the four chapters and six arcs (~29
   scripted jobs) there is nothing authored left; a legend who keeps going
   gets only the generator.

The good news: every one of these can be fixed with **data on the `Career`
plus one small rules engine**, and the hooks it needs already exist
(`NextDay`, `Finish`, `RefreshBoard`, `MakeJob`, shop offers, `CityEvent.Message`).

---

## 2. What the Shadowrun CRPGs teach

Read for mechanics, not setting -- names and lore stay original.

| Game | What it did well | What it becomes here |
|---|---|---|
| **Shadowrun (SNES, 1993)** | *Keyword conversations*: you learn words ("Dog", "Jester Spirit") and ask people about them; knowledge is the progression. A mystery told through places, not briefs. Hired runners met in bars. | **Leads**: storylets give the player keywords (`lead:iron_saint`); asking contacts about a lead in their shop opens new info, jobs or prices. An *investigation* job type whose objective is finding clues on site. |
| **Shadowrun (Genesis, 1994)** | Open city, emergent careers, paydata you sell (value decays), random encounters on the street, contacts you phone, a personal revenge plot threaded through open-ended work. | **Paydata market** (prices that move by faction), street encounters while driving, a phone/inbox, the main story as one thread among the job flow rather than a gate. |
| **Shadowrun Returns: Dead Man's Switch** | A murder mystery with a clear antagonist; *Etiquettes* (Street, Corporate, Security, Gang, Academic...) unlock dialogue options by background. | **Etiquettes** per archetype / bought with karma: extra options in negotiations and storylet choices ("You speak Corporate: point out the clause"). Cheap to add, makes builds matter outside combat. |
| **Dragonfall** | The best one to steal from. **A home that lives**: the Kreuzbasar neighbours change between runs. **Crew** with personalities, banter, personal missions and *upgrade choices*. Runs with **branching outcomes** (who you side with on a job is remembered). A finale where earlier choices decide who helps. | **Neighbourhood** around the safehouse; **crew** grown out of the existing hires; **choices on runs** written to flags; a finale whose allies are whoever you kept faith with. |
| **Hong Kong** | A district (Heoi) under a looming threat; the **Walled City** as a dense hub; crew loyalty conversations; a nemesis-like pursuer; the Matrix as a place. | **District pressure** (a district getting worse until you act), hubs as story locations, a **recurring antagonist** who learns from your runs. |
| **Shadowrun tabletop** | Legwork before the run; **Street Cred / Notoriety / Public Awareness** as three separate reputations; lifestyle costs; Johnsons who betray; contacts with Connection *and* Loyalty; the Awakened world (spirits, toxic shamans, dragons). | Three reputations instead of one Rep; a weekly lifestyle bill; **legwork** as optional pre-run storylets; a dragon as the hidden owner of a corp in the late story. |

The common thread: **every Shadowrun game that people remember made the job
board feel like part of a world with memory.** That is the target.

---

## 3. The foundation: storylets

A **storylet** is a small piece of narrative with a precondition, a channel and
effects. Hundreds of small ones beat a few big ones: they interleave, they
react to state, and each is cheap to write and test. (This is the
Fallen London / Wildermyth model; it fits a roguelike career because it never
assumes an order.)

### 3.1 Shape

In `src/Sim/Story.cs`, a table next to `Chapters` and `Arcs`:

```csharp
public enum Hook { Morning, Debrief, EnterPlace, Drive, Terminal, Sleep, RunStart }
public enum Channel { Mail, Call, News, Contact, Street, Scene }

public sealed record Choice(string Text, Func<Career, bool> Can, Action<Career, Debrief> Then, string Needs = null);

public sealed record Storylet(
    string Id, Hook Hook, Channel Channel,
    Func<Career, bool> When,          // preconditions, read-only
    float Weight,                     // among eligible ones
    string From, string Text,         // who says it, what
    Choice[] Choices = null,          // null: just news; one: "OK"; several: a decision
    int Cooldown = -1,                // -1: once per career; n: days before it may fire again
    string Place = null);             // EnterPlace: which contact or "safehouse"
```

Career gains only plain data, so saves stay JSON:

```csharp
/// <summary>Story state: named counters and flags (0 = unset), and when each storylet last fired.</summary>
public Dictionary<string, int> Flags = new();
public Dictionary<string, int> Fired = new();
/// <summary>Storylets waiting for the player: mail unread, a call to take, a scene at a door.</summary>
public List<string> Pending = new();
```

`Flags` is the whole trick. Every system below writes to it
(`"lead:iron_saint"`, `"spared:Vera Lindqvist"`, `"turf:barrens:lotus"`,
`"crew:wisp:trust"`) and every storylet reads it. No schema change per story.

### 3.2 The engine

```csharp
/// <summary>Offer what fits: at most one storylet per hook, weighted, seeded from the day.</summary>
public Storylet Pick(Hook hook, string place = null)
{
    var rng = new Rng(Seed ^ (ulong)Day * 0x51ED ^ (ulong)hook);
    var ok = Story.All.Where(s => s.Hook == hook && (s.Place == null || s.Place == place)
        && Ready(s) && s.When(this)).ToList();
    return ok.Count == 0 ? null : rng.Weighted(ok, s => s.Weight);
}
```

Seeding from `Seed`, `Day` and the hook keeps it deterministic and separate
from `Career.Rng`, so adding storylets never reshuffles job generation for
existing seeds (the self-check's bot runs stay stable).

### 3.3 Hooks -- all already exist

| Hook | Call site | Typical content |
|---|---|---|
| Morning | `NextDay` | mail, news, a contact calls, a world event starts |
| Debrief | `FinishInner`, after pay | consequences of *this* job: the Johnson calls back, the press spins it |
| EnterPlace | `PlaceScreen` opening | a scene at a contact: Sal has a visitor, Wen is scared |
| Drive | `CitySim` step, rate-limited | a street encounter: someone flags you down, a crash, a checkpoint |
| Terminal | `Terminal` `mail` command | the inbox |
| RunStart | `Run` ctor | legwork paid off: a guard was bribed, a door left open |
| Sleep | safehouse sleep | dreams for mages, neighbourhood news, crew talk |

### 3.4 Channels -- mostly UI that already exists

- **Mail** (`mail`, `read N` in the terminal): the cheapest, most flexible
  channel and very in-genre (SNES/Genesis email, the SR Returns inbox). The CRT
  is already there.
- **Call**: the PDA buzzes in the city (a `CityEvent.Message` with a portrait);
  answer with P. Lets a contact push a time-limited offer.
- **News**: the existing `News` list, but written as **KBN headlines** that
  report the player's runs with corporate spin ("Halcyon-Matsuda denies
  break-in at Industrial lab; 'routine fire drill'"). Also in the car as a
  radio ticker.
- **Contact**: a line + choices on the contact panel (portraits and voices
  are already wired there).
- **Street**: a pedestrian or car in the city that you stop beside (the
  courier/door marker code does this).
- **Scene**: a short full-screen moment on the receipt/file dressing for
  milestones (chapter openings, a crew death).

### 3.5 Effects

Effects are lambdas in the table, so they can do anything the career can
(pay, add a job, set a flag, change loyalty, add a grudge). One helper makes
the common case one line:

```csharp
new Storylet("wen_scared", Hook.EnterPlace, Channel.Contact, c => c.Flags.Get("ledger_copy") > 0, 1,
    "fence", "Wen won't meet your eyes. 'A Lotus man came by. He asked about you, by name.'",
    new[] {
        new Choice("Who sent him?", c => true, (c, d) => c.Set("lead:widow_hunt")),
        new Choice("Give me his address.", c => c.A(Attr.Charisma) >= 3, (c, d) => c.OfferJob(Jobs.Hit("lotus_tail")), Needs: "Charisma 3"),
    }, Place: "fence"),
```

`OfferJob` takes a job template (like `ArcPart`) and puts it on the board with
the storylet's id on it, so the debrief can fire the follow-up.

### 3.6 Testing it (the part that keeps it maintainable)

- **Every storylet is reachable**: a self-check section builds a career that
  satisfies each `When` (from a per-storylet `Example` career setup, or by
  brute force over the flags it reads) and asserts it is picked.
- **Every choice is safe**: run each `Then` on a cloned career; assert no
  exception, cash/karma non-negative, board valid.
- **Bot careers**: extend the bot to play 60 days, take the first available
  choice, and assert the main story still finishes and no storylet fires
  twice when `Cooldown == -1`.
- **Assets**: if storylets get portraits or voiced lines, extend the
  `GeneratedAssets` manifest check, as for contacts.

---

## 4. World dynamics

The city should have **state that moves on its own, and moves more when you
push it**. Four layers, from cheapest to richest.

### 4.1 Turf: who holds each district

Replace the fixed `DistrictDef.Factions` weights with a live share per
district per faction (stored as flags or a small `Dictionary<string, float[]>`).

- Every job against a faction in a district moves share away from it, toward
  whoever **hired** you (the employer's backer: a fence job for the Lotus,
  a Johnson for Halcyon...). Clear and Destroy move it most.
- Each week the shares drift: big factions expand into neighbours, a faction
  under 15 % in a district is pushed out.
- Effects: `MakeJob` picks site factions by share; the city view tints
  graffiti/neon by the holder (`CityView` already themes districts); gang
  ambushes come from whoever holds the district you drive through;
  hubs open or close (the Barrens bazaar shut while the Lotus run it).
- Thresholds fire storylets: "The Vipers have lost the Docks" -- a war party
  in the news, a Viper lieutenant mails you an offer, Torque's prices go up
  because his supply route ran through Viper turf.

This gives the long career a strategic layer that is simple to understand
(a coloured bar per district on the PDA) and makes the player's choice of
*which* jobs to take matter.

### 4.2 Faction clocks

Each faction has a plan, advanced a tick every few days and faster by
relevant events. When a clock fills, something happens to the city.

| Faction | Clock | When full |
|---|---|---|
| Halcyon-Matsuda | "Iron Saint rollout" (tied to the main story) | Spire sites get puppet guards; Lone Star contract goes to Halcyon security (police chasers become corp cars that shoot) |
| Ash Vipers | "Gang war" | a week of Viper-vs-Lotus street fights in the Barrens: cars burning, both factions on every site, a temporary *Clear* bounty board |
| Red Lotus | "Buy the council" | Neon Strip checkpoints: heat rises faster there, the Lotus offer you protection for a cut of every job |
| Lone Star | "Crackdown" (driven by your Public Awareness) | roadblocks on avenues, more patrols, the informant's price triples |

The player sees the clocks only through rumours, news and contacts (the
hub kids, Null reading corp traffic). Runs can push them back: a Datasteal
on a Halcyon lab sets the rollout back two ticks, and the debrief says so.
This turns "random job" into "I chose to slow the Lotus down".

### 4.3 City events (the calendar)

The career already has `Day` and per-day weather. Add a calendar of
scheduled and random events that last a few days and touch city, jobs and
runs. Each is one table row with a start rule, duration and modifiers:

- **Stadium night** (weekly): traffic jams round Kessler Stadium, crowds,
  Lone Star stretched thin elsewhere (heat cools faster).
- **Acid rain front**: a storm variant; car HP slowly drops when parked
  outside, outdoor yards empty, guards stay in.
- **Blackout** in a district (a grid failure or a sabotage *you* did):
  every site there gets the Blackout condition for free, streetlights out
  in `CityView`.
- **Corporate quarterly**: Halcyon pays Johnsons 30 % more for two days.
- **Full moon / mana surge**: spells +20 %, spirits on sites, Sabine has
  rare stock. (The Awakened world deserves a calendar.)
- **Festival of lanterns** on the Strip: the night market doubles, a parade
  blocks the avenue, a once-a-year lantern vendor.
- **Election week** (once a career): candidates hire runners against each
  other; every job carries a political side, and who wins changes the police
  budget for the rest of the career.
- **Lone Star strike**: two days without police; gangs bolder, ambushes
  doubled.

Shown in the terminal header and on the PDA, forecast a day or two ahead by
Null or the rumour kids, so the player can plan around them.

### 4.4 TraumaLink on the streets

TraumaLink is the game's (original-named) answer to the tabletop's armoured
ambulance service, but it only appears as a line in the debrief. Putting it in
the city makes it real, and the traffic code already supports themed vehicles
(the ice cream truck has its own `CarKind`, size and jingle):

- **Ambulances in traffic**: a `CarKind.Ambulance`, white and green with a
  light bar and a siren, rare (1-2 in the city). It drives fast down the
  middle of the avenue and other traffic pulls over for it (the lane code that
  makes traffic stop for the player does most of this). Ramming one is heat
  and Notoriety.
- **Your own pick-up**: when TraumaLink saves you, the next scene in the city
  starts with the ambulance dropping you at the clinic, not a cut to the
  safehouse. Cheap, and it makes the contract feel bought.
- **After loud runs**: an ambulance (and a Lone Star car) arrives at the site
  you just left, so the city reacts to what you did.
- **High-threat response team**: on a threat 4-5 corp site where a
  TraumaLink-insured guard goes down, an armoured team can land mid-run to
  pull them out -- heavily armed, but they leave you alone if you leave them
  alone. Shooting them is a grudge with TraumaLink itself: contracts cost
  double until you make amends.
- **Jobs and storylets around it**: hijack an ambulance to get a hostage
  past a checkpoint (Rescue variant); a client wants a TraumaLink patient
  "lost" on the way to hospital (Eliminate on the road, a pursuit job);
  a TraumaLink medic who owes you one (a minor contact from a rescue) gives a
  free pick-up or a bed for an injury.
- **Contract tiers**: basic (today's), gold (a pick-up keeps your cash),
  platinum (the armoured team comes for you even inside a threat-5 site, and
  for one crew member). A natural money sink for the late game.

### 4.5 A news economy

The Genesis game's paydata market, generalised. Loot and paydata already
exist; make their **value depend on the world**:

- Paydata from a faction sells for more while that faction's clock is high
  (everyone wants to know what they are planning).
- A "hot" item (from a job the news covered) sells for less and raises
  heat if fenced within 3 days.
- Old Wen posts **wanted lists** in the mail (a buyer wants a Halcyon
  drone core): loot with a purpose.

---

## 5. Consequences: the world remembers

The most Shadowrun thing the game could do is make the generated jobs leave
traces. All of these are flags + storylets; none need new run systems.

### 5.1 People persist

Generated jobs already have named targets from `Tables.TargetNames`. Track
what happened to each name:

- **Fled** (`Vip.Escaped`): the target comes back a few weeks later as a
  *harder* eliminate, or hires hitters on *you* (an ambush with their name
  on it). A lightweight **nemesis**: they gain a boss-like charge, more
  bodyguards, and a mail taunting you.
- **Spared** (knocked out, not killed): sometimes becomes a contact-lite --
  sends a tip, a discount, or warns you of a set-up.
- **Rescued**: a rescued hostage can turn up later (a doctor who patches you
  for free once, an engineer who opens a door on a related site).
- **Killed**: their people remember. A sibling, a partner, a corp
  investigator. Rare, but devastating when it fires.

Store as `Flags["fate:Vera Lindqvist"] = 1..4` and extend `TargetNames` to
50-80 so names rarely repeat by accident.

### 5.2 Choices on runs

Give generated runs one optional decision at the debrief or at the
objective -- the Dragonfall pattern, done cheaply:

- **Retrieve**: the item is worth more to a rival. Deliver (pay, loyalty),
  sell to Wen's other buyer (2x cash, Johnson burned, his faction's grudge
  +2), or keep it (it is a key to a later storylet).
- **Datasteal**: the data incriminates *the Johnson*. Deliver, sell it back
  to the Johnson's victims, or leak it (Public Awareness up, that corp's
  clock back, the press loves you, Juno is furious).
- **Rescue**: the hostage begs not to be returned to the client (an
  extraction disguised as a rescue). Hand them over, or let them go and
  explain to the fixer.
- **Eliminate**: the target offers more than the contract to fake a death
  -- carry them out unconscious, they vanish, the client pays *if* nobody
  checks. (Uses the existing "Alive" twist's carry code.)

Each choice writes flags. Most never come up again; a few seed storylets
weeks later. Offer them on 1 job in 4 so they stay special.

### 5.3 Three reputations, not one

Split `Rep` as the tabletop does:

| Rep | Rises from | Opens / costs |
|---|---|---|
| **Street Cred** (the current Rep) | finished jobs, hard jobs | threat tiers, better Johnsons |
| **Notoriety** | betraying Johnsons, killing the helpless, a target's family hurt, burning a contact | some contacts refuse you (Sabine, Ichikawa), others warm up (the Lotus), set-ups more likely, new dark jobs |
| **Public Awareness** | loud runs on the news, landmark damage, leaked data | base heat, crackdowns, corp counter-ops, but also celebrity jobs and fan mail |

Notoriety and Awareness give the player a *style* to maintain, which is the
role-play the roguelike structure otherwise lacks. Karma could gain a small
bonus for restraint (the original tabletop rewarded karma for good choices).

### 5.4 Rare events: the burned exit and the double-cross

Both exist as mechanics: `Twist.ExfilMoved` (one of the ~12 % twists) and
`Job.DoubleCross` (15 % of Johnson jobs: a squad at the exit). Right now they
are short combat surprises that leave nothing behind. Make them rarer and
heavier, so they become the stories players tell:

- **Who burned the exit?** A compromised extraction gets a cause, chosen from
  the world state: a crew member talked (low crew trust), the Johnson sold
  you (his faction's grudge), Lone Star tracked your car (high heat), a target
  you let flee tipped them off (a fate flag). The debrief names a suspect;
  a follow-up storylet lets you find out and act on it.
- **Escalating double-crosses.** Tier 1 is today's squad at the exit. Tier 2:
  the pay never arrives and the Johnson's mail bounces. Tier 3 (very rare,
  only after a run with a choice in it): the Johnson frames *you* -- your face
  on KBN, Public Awareness up two, Lone Star at the safehouse door that night.
- **Paying it back.** Every double-cross leaves a lead (`lead:johnson:<id>`).
  Juno can find him for a fee: a revenge Eliminate, a blackmail Datasteal, or
  a "renegotiation" where you get the pay twice. Loyal fixers already warn
  you; at trust 4 Juno could let you **choose**: walk away, or take the job
  and turn the trap round (the squad arrives where you have planted charges).
- **Reading the signs.** Rare tells in the briefing for players who pay
  attention: too much pay for the threat, a Johnson who won't haggle at all,
  an exit point the Johnson insisted on. Etiquette (Corporate or Street) or a
  piece of legwork makes the tell explicit.
- **The rarest version**: the exit is compromised *and* it's a set-up *and*
  the ambush team is a rival runner crew -- named, with a leader who escapes
  and comes back (a nemesis from 5.1).

Keep the base rates low (a few per career) and tie the follow-ups to
storylets, so each one is an episode rather than a random punishment.

---

## 6. The story

### 6.1 Rework the main story into a branching campaign

Keep the four chapters (they are good beats), but:

1. **Gate by leads, not job count.** Chapter 2 opens when the player has
   the lead the chip points to -- from chapter 1's debrief, plus one piece
   of legwork (ask Null about it, or find it in a Halcyon datasteal). Job
   count stays a fallback so nobody gets stuck.
2. **Interludes between chapters**: 2-3 storylets each (Juno calls, a
   stranger mails, Pryce's sister appears at the clinic). They make the city
   feel like it is reacting to what you did.
3. **One real choice at the midpoint** (after "Who's buying"): return the
   data to Halcyon's rival, sell it to the Lotus, or leak it.
   - *Leak*: the Spire finale becomes a public assault (more Awareness, Lone
     Star at the exit, the city sides with you).
   - *Sell to the Lotus*: the Lotus become allies for the finale (Lotus
     runners in chapter 4), but Iron Saint chips spread through the Strip.
   - *Halcyon's rival*: a corp extraction team waits with you at the end --
     and might betray you.
4. **The finale reflects the career**: contacts whose arcs you finished send
   help (Sal's crate, Torque on the roof, Null blinding the cameras, Sabine's
   ward); crew you kept alive come with you; the debrief epilogue lists what
   became of each contact, district and named NPC (Fallout-style slides on the
   receipt paper).
5. **Several endings**, each a legacy perk: retire a legend, retire rich and
   hunted, take Juno's chair as the fixer (unlocks a new start), or keep
   running -- the story ends but the career continues.

### 6.2 Second and third campaigns

A legend who keeps going (or a new career with a legacy) needs new threads.
Each campaign is the same shape as the Iron Saint story -- four chapters plus
interludes -- so the engine reuses:

- **The Wyrm's Ledger** (late game, threat 5): the money behind Halcyon is a
  dragon's. A long con across all districts, a negotiation the player cannot
  win by force, and the Shadowrun-signature moment: being *hired* by the thing
  you were fighting.
- **Toxic Tide** (the Awakened thread, for mages or after Sabine's arc):
  the blood circle was a symptom; a toxic spirit is waking under the Docks.
  Spirits on every Docks site, an astral-only objective, Sabine and Ichikawa
  disagree on what to do (pick one).
- **The Cut** (the street thread): the Vipers' new boss is a former runner
  who knows how you work -- uses your favourite tactics against you (reads
  your stats: lots of grenades -> fire-proof guards; stealth -> more cameras).
  A Hong Kong-style personal antagonist.

Campaigns can be offered by different contacts, so the player can follow the
one their career leans towards.

### 6.3 Deeper contact arcs

The six arcs are linear 3-part chains. Cheap upgrades:

- **A fork in part 3**: Doc's headhunter can be paid off or killed; Wen can
  burn the ledger or keep it (Notoriety); Sabine's circle can be broken or
  turned to your use (spell power, essence cost). The reward changes with it.
- **After the arc**: a contact who finished their story keeps a life --
  occasional mail, a seasonal favour, a dinner scene. They can also be
  **lost**: let your Notoriety climb and Ichikawa closes the door; let the
  Lotus take the Strip and Sabine leaves town.
- **Contacts in danger**: when a faction holding a contact's district
  hates you (grudge 5), it goes after the contact. A defend/rescue job
  appears; ignore it and the contact is hurt (shop closed for a week) or
  gone. This is the strongest consequence in the game and should be rare.

### 6.4 New contacts

Each adds a shop, a favour, an arc, and a portrait/voice set in `assets/`:

- A **Lone Star cop on the take** (heat control, warrant info, the
  crackdown storyline from the inside).
- A **journalist** (KBN stringer): pays for footage and data, raises your
  Awareness, the leak option in every choice runs through her.
- A **fixer rival** to Juno, with worse jobs but no questions -- the dark
  path for high Notoriety.
- A **street preacher** at St. Brigid's who runs the Barrens soup kitchen,
  the neighbourhood's conscience, and a rescue/protect-the-weak arc.

---

## 7. Crew (the Dragonfall lesson)

Ox, Wisp and Patch are already allies with AI, portraits and a cut. Turning
them into **crew** is the highest-value story feature for the effort:

- **Hire for the career, not the job**: a weekly retainer instead of a cut,
  and they live at the safehouse (or the neighbourhood).
- **Trust** per crew member, raised by runs together and by choices they
  agree with (Patch hates no-mercy runs; Ox likes loud ones; Wisp likes
  money). They comment in the debrief and on the drive (one line from a
  table, keyed by flags).
- **Personal missions**: one 2-part arc each, the same `ArcPart` machinery.
- **Upgrade choices** at trust 2 and 4 (Dragonfall did this well): Ox
  learns to carry the wounded or to breach walls; Wisp picks a silenced or an
  anti-materiel rifle; Patch gets more heals or a revive.
- **Permanent death**: ironman already exists. A crew member downed on a run
  is gone unless the player carries them out (body carry exists) or has a
  TraumaLink for them. The funeral storylet, the empty bunk. This is the moment
  players will remember.
- **New recruits** come from story: the decker kid from Null's arc, Nurse
  Ade from the doc's, a spared target. Crew made of people you saved.

---

## 8. The safehouse and the block

Dragonfall's Kreuzbasar and Hong Kong's Heoi show that a home **neighbourhood**
gives the career an emotional anchor. A few NPCs on the safehouse's street
(the noodle stand, a mechanic's kid, a landlady, a street doc's apprentice),
each a short storylet chain that advances between runs:

- **Protection**: the Vipers start leaning on the noodle stand. Pay them,
  fight them (a small defend job on the street), or ignore it (the stand
  closes).
- **Investment**: money spent on the block (repaint, generator, a clinic)
  opens small perks: +1 medkit, cheaper repairs, a lookout who warns of
  ambushes on the way home.
- **Heat comes home**: at high Public Awareness, Lone Star or a faction
  **raids the safehouse** -- a defend-the-home run on a hand-built site (the
  generator can make it from the safehouse lot). Losing means losing stash,
  stored cars, or moving.
- **Lifestyle**: a weekly rent that scales with the safehouse (the loft costs
  more). A missed payment is a storylet, not a game-over. This is also the
  money sink the late game needs.

---

## 9. New job types that carry story

The run layer has the pieces (carry, escort, defend positions, the Matrix,
drones, courier). New types mostly recombine them:

| Type | Built from | Story use |
|---|---|---|
| **Investigate** | Retrieve with 3 clue pickups that set flags | SNES-style leads; the main story's legwork |
| **Plant** | Retrieve in reverse: carry an item *in* to a spot | frame a faction (moves turf), bug a boardroom (opens a datasteal) |
| **Escort / Bodyguard** | Rescue follower + defend waves | protect a contact or a witness; failure kills them for real |
| **Defend** | waves at a fixed site (the safehouse, a contact's shop) | the consequence job for grudges and raids |
| **Kidnap** | the "Alive" twist as a full type | corp extractions, the dark-path fixer's speciality |
| **Heist** | 2-3 linked runs on one seed (scout -> blackout the grid -> the vault) | chapter-sized jobs; the finale |
| **Street race** | courier driving with rival cars | Torque's arc follow-ups, Viper turf challenges |
| **Bounty** | eliminate on a paracritter/ghoul nest | the Awakened thread, Lone Star contact |
| **Run from home** | the Matrix grid alone, no body | Null's late jobs, a quiet night job between big ones |

**Legwork** before any job: in the briefing, optional paid storylets
("Bribe a guard: the back door is unlocked", "Ask Wen who's inside: you see
the boss's room") that set flags `RunStart` reads. Schematics already prove
the pattern -- this generalises it and gives Charisma and contacts more use.

---

## 10. Smaller ideas worth a row in a table

- **Etiquettes**: Street, Corporate, Gang, Security, Academic, Magic. One
  from the archetype, more for karma. Unlock storylet choices and haggling
  bonuses with matching Johnsons.
- **Johnson personalities**: a few recurring Johnsons (the polite corp, the
  nervous academic, the Lotus auntie) with memory of how you treated them.
- **The double-cross goes both ways**: let the player sell out a Johnson for
  a rival's offer (Notoriety, cash, grudge).
- **Achievements for story**: the 27 achievements are mostly mechanical; add
  ones for endings, crew survival, saving every contact.
- **Car radio**: KBN news, Lone Star scanner chatter that tells you where
  patrols are, a pirate station that plays the player's legend.
- **Graffiti that answers you**: after a big job against the Vipers, tags in
  the Barrens about you (CityView already paints signage).
- **Mage dreams** on Sleep: astral visions that foreshadow the next chapter
  or warn of a set-up -- the Awakened finally get story of their own.
- **Cyberpsychosis storylets**: below 1.5 essence, the phantoms start
  sending mail. Doc notices. A (rare) run where the guards might not be real.

---

## 11. Roadmap

Ordered so each step pays off on its own and the next one builds on it.

| # | Step | Contents | Size |
|---|---|---|---|
| 1 | **Storylet engine + mail** | `Story.cs`, `Flags`/`Fired`/`Pending` on Career, Morning/Debrief/EnterPlace hooks, terminal `mail`, contact-panel choices, the reachability and bot checks; 25-30 first storylets (interludes for the current chapters, contact life after arcs, rumours that pay off) | M |
| 2 | **Consequences** | target fates, run choices on 1 in 4 jobs, Notoriety and Public Awareness, KBN headlines, causes and follow-ups for burned exits and double-crosses | M |
| 2b | **TraumaLink in the city** | ambulance traffic, the pick-up scene, post-run response; response teams and contract tiers later | S |
| 3 | **World state** | turf shares and drift, faction clocks, the PDA district bar, the calendar of city events | M-L |
| 4 | **Crew** | retainers, trust, banter tables, upgrade choices, personal missions, permadeath | L |
| 5 | **Main story v2** | lead-gated chapters, the midpoint choice, a finale shaped by the career, several endings and legacy perks | M |
| 6 | **New job types** | Investigate, Plant, Escort, Defend first (they serve steps 2-5), then Heist | L |
| 7 | **Neighbourhood + lifestyle** | block NPCs, protection, raids on the safehouse, rent | M |
| 8 | **Second campaign + new contacts** | one campaign (Toxic Tide or The Cut), the cop and the journalist with their arcs and assets | L |

### Risks and rules to keep

- **Saves**: ironman careers in the wild. New fields default safely with
  `System.Text.Json`; never rename one. Storylets referenced by id, so
  removing one must keep its id unused. A `StoryVersion` alongside
  `CityVersion` if a rework changes meaning.
- **Determinism**: storylets draw from their own seeded `Rng`, never from
  `Career.Rng`, so existing job sequences and bot tests don't shift.
- **Sim purity**: storylets and effects live in `Sim`; UI only renders
  `Pending` and calls `Choose(id, n)`. The agent harness then gets story for
  free (`state` lists pending mail and choices; add a `choose` command).
- **Writing load**: the bottleneck will be text, not code. Write storylets
  in the voice already in `Tables.Contacts` and the arcs (short, dry, one
  image per line). Keep each under ~3 sentences. Voiced lines and portraits
  stay optional, as now.
- **Pacing**: one storylet per hook per day at most, and a budget (e.g. at
  most two Morning items a day) so the inbox never becomes homework.

### The first slice

If only one thing gets built next: **the storylet engine with mail and
contact scenes, plus target fates.** It turns every existing generated job
into a possible story seed, gives the current chapters and arcs room to
breathe, and is the substrate every later idea in this document writes to.
