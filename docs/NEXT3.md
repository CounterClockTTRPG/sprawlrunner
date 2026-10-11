# Sprawlrunner: a fourth look -- what makes it Shadowrun

> **Status.** Section 1 (the quick fixes) is built, with checks in
> `tests/SmokeTest.LooseEnds.cs`; the balance report's archetype edge is a rough
> per-kit guess, not yet the bot's results. The rest is unbuilt. Written after the
> eighth pass. Sources: a survey of the code (file:line evidence), the
> self-check and balance report of 2026-10-11 (all sections pass), and the
> leftovers of `docs/NEXT2.md`.

The first three analyses asked "is the game deep enough?" and the answer is now
yes: storylets, turf, clocks, campaigns, a Matrix minigame, a security ladder,
room kinds, entry points, a rival crew. This one asks a narrower question:
**does it feel like a Shadowrun game?** The setting is original, but the pillars
players recognise -- metatypes, the cost of magic, the SIN, the Johnson, the
legwork, the crew -- are where its identity comes from.

Of about 40 pillars, 12 are rich, 15 thin and 13 absent. The rich ones are the
career loop (Johnsons who betray, legwork, the fence, lifestyles), essence and
graded chrome, the Matrix, vehicles and chases, and policing in the city. The
headline gaps:

1. **No metatypes.** Everyone is a human-shaped "runner"; the troll and elf
   exist only as two hires' descriptions (`Tables.cs:569-570`) and Mr. Tusk.
2. **Magic is free.** No drain (no code mentions it), one tradition, one spirit
   type that can't be bound, no adepts, sites with no background count or wards.
3. **No SIN.** Being SINless is the intro's first line (`Intro.cs:34`) and then
   never matters: no fake SINs, licences, legality or scans.
4. **The crew can't be told anything.** One hire per job, no orders
   (`Controls.cs` has no command action).
5. **Guards still charge.** Cover, flanking and morale from NEXT2 §3 were never
   built; the AI never chooses cover (`Run.Combat.cs:102-121` is the shot rule
   only).
6. **Money still piles up.** Every archetype ends a 70-day sim with ¥220-460k
   unspent, past `Tables.RetireSavings` (¥250k); karma likewise.

---

## 1. Quick fixes found on the way

| What | Where | Fix |
|---|---|---|
| Second-skin suit promises "footsteps near silent"; armour never touches step noise | `Tables.cs:332`, `Run.Player.cs:168` | an `ArmorDef.Quiet` multiplier on step noise (0.4 for the suit) |
| `lead:collector` set ("Sorensen-Aoki") and never read | `Storylets.cs:199` | a follow-up: a Sorensen-Aoki job or a call that the collector got careless |
| `burned:johnson` has no follow-up storylet, unlike heat/scanner/fled/crew | `Career.World.cs:313`, `Storylets.cs:319-352` | a storylet (Juno confronts the Johnson, or you do) -- check `crossed` first |
| Write-only fields: `LastRun.Died`, `MatrixRun.PlantT`, `CitySim.LastFine` | `Story.cs:48`, `Matrix.cs:108`, `CitySim.Pursuit.cs:215` | use or remove |
| Balance report buys medkits at ¥300; the shop charges ¥400 | `tests/SmokeTest.Balance.cs:82`, `Tables.cs:338` | use `Tables.MedkitCost` |
| Balance report: seed 23 gives near-identical numbers for every archetype | `SmokeTest.Balance.cs:48` | archetype-aware win rates from the bot's results |
| Escape/Enter matched by keycode; no Back action, so no gamepad "B" in menus | `PlaceScreen.cs:144`, `Screens.cs:52`, `Pda.cs:79`, `StoryUi.cs:85,92`, `Terminal.cs:74` | `Act.Back` in `Controls` |
| Boss bar draws a fill under `DrawSegBar`, which already fills | `RunScreen.cs:499-501` | drop the fill |

---

## 2. Who you are

### 2.1 Metatypes (the biggest identity gap)
Choose at creation: **human, elf, dwarf, ork, troll** (original names are
fine; the shapes are what players know). Each is a small, legible package:

| Metatype | Attributes | On a run | In the city |
|---|---|---|---|
| Human | +1 Edge | -- | -- |
| Elf | +1 Agi, +1 Cha, -1 Body | low-light eyes (see further in dark rooms, `Level.LightAt`) | Corporate etiquette easier |
| Dwarf | +1 Body, +1 Str, -1 Reaction | thermographic, resists toxins (lab gas, `Run.Rooms.cs`) | -- |
| Ork | +2 Body, +1 Str, -1 Logic | -- | Gang etiquette easier; some shops +10 % |
| Troll | +3 Body, +2 Str, -1 Agi, -1 Cha | natural armour; bigger, so cover hides you less and doors are tight | gear costs +10 % (custom fit); Spire shops +25 % |

The street notices: some Johnsons pay less (Mr. Tusk pays an ork more), the
Spire doorman, storylets with a metahuman-rights rally in election week
(`Career.Election.cs`). Figures in `Art` get a size and silhouette per
metatype. One field on `Career`, one table in `Tables`, and many small reads.

### 2.2 The SIN (NEXT2 §4.2, still the right idea)
- Start SINless. Buy a **fake SIN** (rating 1-6) from the fence or a decker
  contact; rating sets how often it passes.
- Things that check: **roadblocks** (`CitySim.Roadblocks.cs`), being stopped by
  police (a pass lowers the fine), Spire shops and upscale lifestyles,
  **licences** for restricted gear.
- **Legality classes** on gear: legal, restricted (needs a licence on your SIN),
  forbidden (only from the fence, and it's evidence when you're busted).
- A loud run where you were seen or left blood can **burn the SIN**; the debrief
  says so. Until you buy another, every checkpoint stops you.
A steady money sink that rewards clean work, and makes the intro's line true.

### 2.3 Social skills
`Skill` has no social skill (`Tables.cs:8`); Charisma does everything.
Add **Negotiation**, **Con** and **Intimidation**:
- Negotiation feeds haggling (2.4 below) and the fence.
- Con feeds the bluff (`Run.Verbs.cs:20`) and disguise.
- Intimidation feeds surrender (`Run.Choices.cs:95`) and a new verb: shout a
  guard down at close range.
They give the Face skills to raise, and give everyone else the option of
dabbling.

### 2.4 Haggling at the briefing (NEXT2 §6.2, unbuilt)
Still the one-click roll (`Career.Jobs.cs:556-590`). A short negotiation: ask for
more, half up front, intel, a getaway car; the Johnson's patience is a segmented
bar; push too far and they walk or remember it. Show the odds of each ask.

---

## 3. Magic with a price

| What | Proposal |
|---|---|
| **Drain** | Every cast costs stun drain scaled by the spell's force; drain past a threshold staggers you or knocks you out. Mages choose to overcast (more power, more drain). Needs the stun track (4.2). |
| **Traditions** | At creation: hermetic (drain resisted by Logic; spells from the talismonger cheaper) or shaman (Charisma; a **mentor spirit** with a bonus and a taboo: Rat -- stealth, never fight fair; Bear -- heal, berserk when hurt; Raven -- ...). The outsider-shaman background (`Career.Gear.cs:64`) becomes one of these. |
| **Spirits** | More than fire: air (knock down, hide in fog), earth (wall, tank), beast (tracker), water (put out fire, smother). Summon into a slot; **bind** at the safehouse with a ritual (cost, karma) to carry services into runs. |
| **Spells** | Add stunbolt (non-lethal manabolt), levitate (cross gaps, reach the roof entry), physical mask (pass for a guard -- the Face's uniform for mages), control thoughts (one guard fights for you a few seconds), armour, clairvoyance (see a room on the briefing map). Ranks 0-3 already exist. |
| **Background count** | Some rooms (`Room.Kind`): the ritual circle, a toxic site, a blood-soaked den, a corporate chapel. Spells cost more drain and lose power there. Wards on Halcyon sites block projection and spirits until a mage takes them down. |
| **Astral threats** | Today astral sight and projection are free scouting (`Tables.cs:412-414`). Add astral guards (a watcher spirit) and enemy mages who perceive you astrally, so projecting has a risk. |
| **Initiation** (NEXT2 §4.5) | The late karma sink: grades for ~150 karma each, each a metamagic (masking hides you from astral perception; shielding cuts drain; centering...). |
| **Adepts** | A sixth archetype or a mage variant: no spells, power points spent on killing hands, improved reflexes, a wall run, a sense for traps. Melee becomes a build. |

---

## 4. Fights that read like Shadowrun

### 4.1 Guards worth outwitting (NEXT2 §3, unbuilt)
- **Cover and flanking**: mark cover tiles once per site; gunmen with LOS pick
  the nearest cover that still sees you; every third guard takes the second
  route of the distance field.
- **Squad leader**: a group of three or more gets one; kill them and the rest
  hesitate or fall back.
- **Morale**: two friends down in sight and the guard retreats to the
  reinforcement point (and the surrender roll is tried then).

### 4.2 Two damage tracks and wound penalties
One HP pool today. Split it into **physical** and **stun**: gel rounds, the
taser, the baton, fists, flash-bangs and drain fill stun; a full stun track
knocks you out (and a full one on a guard is the KO the game already has,
`Run.Combat.cs:217-237`). Each third of either track filled gives **wound
penalties**: aim spread up, cooldowns slower. The pain editor (`Tables.cs:371`)
ignores them. This makes non-lethal play coherent and gives drain a home.

### 4.3 Recoil and ammo
- Full auto climbs: spread grows with each shot in a burst and recovers;
  Strength, a gas vent mod and a gyro mount reduce it. SMGs and the LMG gain
  a reason to tap-fire.
- More ammo: **stick-n-shock** (stun, quiet, short range), **flechette** (more
  damage to unarmoured, less to armour), **tracer** (less spread, gives away
  your position).

### 4.4 Commands for the crew
`Act` has no command. Add one key and a short context menu, the Dragonfall
way: **hold here / follow / take that cover / go loud / stay hidden / heal
me**. Then let a high crew trust or a bigger safehouse take **two hires**.
Without orders the crew is a turret that walks.

### 4.5 Kits that still have holes
- **Engineer**: the breach charge is a grenade (`GrenadeKind.Breach`); add the
  deployable turret and tripwire mines bought at Sal's.
- **Face**: nothing in a fight. Intimidation (2.3) and a **human shield** (grab
  a guard from behind; the others hold fire).
- **Stealth verbs for everyone**: lock doors behind you, hide a body in a
  locker (the `Locker` tile exists, `Level.cs:107`), throw a bottle to lure.
- **Swap abilities mid-run**: a quick wheel (hold Tab) over the two slots.

---

## 5. The street

| What | Proposal |
|---|---|
| **Drugs** | Combat drugs from the fence: jazz (Overclock-lite), kamikaze (strength, pain immunity, crash after), novacoke (+Cha for one meeting). Each has a crash after the run and an addiction chance that becomes a storylet and a weekly cost. Ties into Lotus's turf (BTL master copy is already loot, `Tables.cs:593`). |
| **Extraterritoriality** | Halcyon sites are corporate soil: no police ever come, but their own response team does, and a kill there is a corporate-court matter (a bounty, a storylet) rather than heat. Vipers' and Lotus's turf get the police. |
| **Shady docs** | Cheap implants from a back-alley doc: chance of a tracker (a Halcyon tail until you pay Ichikawa to dig it out), used grade (cheaper, more essence). |
| **Low essence** | Phantoms already appear below 1.5 (`Run.cs:745`). Add a social cost: contacts' loyalty grows slower, the Face's bluff falls, children cross the street. |
| **Jump into your car** | With a control rig, drive by rig: better handling, and the drone deploys from the car during chases. |
| **Contacts with reach** (NEXT2 §4.3) | Connection beside Loyalty: how much a contact can do; it grows with their faction's turf. |
| **Crew stress** (NEXT2 §4.4) | Stress from close calls and civilian deaths; quirks or virtues at the limit; recovery at a bar or St. Brigid's. |
| **Run-grown twists** (NEXT2 §2.5) | The Johnson calls mid-run with a new demand; a guard offers a passcode to be let go. |

---

## 6. The late career

The sim's 70-day careers end with ¥220-460k in the bank (balance report,
"average cash when the career ends"). Sinks that fit the setting:
- SINs and licences (2.2): recurring, and burned by loud play.
- Initiation (3) and binding spirits: karma and money for the Awakened.
- Delta-grade chrome already costs ×20; add **cultured bioware** (low essence,
  long recovery) and **cyberlimbs** with their own upgrades.
- A **retirement goal on the PDA** ("Retire comfortably: ¥500k and a year of
  Middle lifestyle"), so saving is a choice, not an accident.
- The bot buys gear between jobs (NEXT2 §7), so the difficulty curve of a real
  career can be measured before any of this is tuned.

---

## Suggested order

| # | What | Why | Size |
|---|---|---|---|
| 1 | Quick fixes (1) | real bugs and loose ends | S |
| 2 | Metatypes (2.1) | the most recognisable missing pillar; touches creation, art, prices, storylets | M |
| 3 | Stun track and wound penalties (4.2), then drain and traditions (3) | magic with a price; non-lethal made coherent | M |
| 4 | Guard cover, flanking, morale (4.1) | fights worth thinking about; carried over twice now | M |
| 5 | Crew commands, two hires (4.4) | the Dragonfall lesson | M |
| 6 | SINs, licences, legality (2.2) | the setting's premise; a money sink | M |
| 7 | Social skills and haggling (2.3, 2.4) | the Face's whole game | M |
| 8 | Spirits, spells, background count, wards, astral threats (3) | depth for the Awakened | M-L |
| 9 | Kits: engineer turret, human shield, stealth verbs, ability wheel (4.5) | thin archetypes | M |
| 10 | Drugs, extraterritoriality, shady docs, rigging the car (5) | street texture | M |
| 11 | Adepts, initiation, late sinks, PDA goal, a bot that shops (3, 6) | the long career, and the tooling to tune it | M-L |
