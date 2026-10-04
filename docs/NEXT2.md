# Sprawlrunner: a third look

> **Status.** Built so far: the quick fixes (section 1) and the security
> ladder with proper searching (2.1, and the searching part of 3) -- see
> `docs/DESIGN.md`, "Seventh pass". Also added on request: the Lamplighter
> campaign. Next in the suggested order: room kinds and set pieces (2.2).

The sixth pass made the career layer deep and legible. This analysis looks at
what is now the weakest part of the game: **the runs themselves**. Systems on top
of the run have outgrown the run's rules. Sources: a survey of the code (with
file:line evidence), the bot's results, the balance report, and outside research
(Invisible, Inc., Satellite Reign, Door Kickers, Heat Signature, Darkest Dungeon,
Disco Elysium, Griftlands, the Shadowrun CRPGs and tabletop).

The headline findings:

1. **Every site plays the same way once you know the trick.** Site style only
   changes the paint (no `Run*.cs` reads `SiteStyle`), and the objective is
   always in the deepest room or the vault (`LevelGen.cs:912-1003`).
2. **The alarm is a cliff, not a slope.** One camera sighting tells every
   guard where you are, forever: after `RaiseAlarm`, hunters walk the distance
   field to your *true* position (`Run.Ai.cs:322`). You cannot lose them, so
   stealth has no recovery and loud play has no texture.
3. **Guards are brave and stupid.** No cover, no flanking, no retreat, no
   squad; they arrive in a line down the same distance field.
4. **Two archetypes have thin kits.** The Combat Engineer has no ability and no
   unique verb; the Face has nothing in a fight.
5. **The late career runs out of goals and sinks.** Money piles up (¥390k on
   day 70), property *adds* income, karma has nothing left to buy after about
   900, and after "Keep running" the only ending is death.

---

## 1. Quick fixes found on the way (bugs and loose ends)

| What | Where | Fix |
|---|---|---|
| Defend waves skip the high-end threat gate: cloakers and bulwarks can turn up at threat 3-4 | `Run.Jobs.cs:47` | apply `Tables.FirstThreat` as LevelGen does |
| Alarm waves take only the first roster entries, ignoring weights (no Halcyon drones, shields or riggers in waves) | `Run.Ai.cs:648` | weighted pick |
| Moved / SecondTarget twists can drop the item outside the building or on another floor | `Run.cs:592-608` | skip `Outer` rooms, stay on the objective's floor |
| "Never there" achievement counts any Plant and any no-alarm job separately | `Achievements.cs:49` | count no-alarm Plants |
| HUD key labels ignore rebinding (`[H]`, `[Space]`, `[V]`, `[G]`...) | `RunScreen.cs:241-290`, `CityScreen.cs:487` | use `Controls.KeyName` |
| `friend:<name>` is promised ("karma and a friend") but never read | `Career.World.cs:312` | make friends do something (see 6.4) |
| `Tables.DestroyThings` unused; mod id `laser` collides with the weapon id | `Tables.cs:502, 207/258` | wire up / rename |
| Blackout pays ×1.0 though it blinds the runner too | `Tables.cs:532` | ×1.1 |
| No gamepad buttons for the PDA or the drone | `Controls.cs:46-52` | bind them |
| The run HUD is laid out in absolute pixels; large text sizes will overlap | `RunScreen.cs:231` | lay out from measured text |

None is big; together they're an afternoon.

---

## 2. The run: make every site a different problem

### 2.1 Security level instead of an on/off alarm (Invisible, Inc.)
Klei's alarm went from 20-30 opaque ticks to 6 named levels, each with a
**previewed** consequence, and frustration dropped. Do the same:

- Security rises slowly with time and in steps with each sighting, body found,
  camera or loud noise (the existing events).
- Six levels, each named on a segmented bar with the next consequence written
  beside it: *L1 a guard sent to check · L2 patrols doubled · L3 doors lock ·
  L4 response team at the entrance · L5 the site's hunters know where you are ·
  L6 a corp kill team*.
- Only at L5 does today's omniscient hunt start. Before that, alerted guards
  search where they *last saw* you, and you can break contact and hide.
- Run mods, Johnsons and factions set how fast it climbs; the spider and
  cameras feed it; a looped console stalls it.

This one change fixes stealth recovery, gives loud runs a rising curve, and
is readable. It's the single biggest improvement available.

### 2.2 Rooms with a purpose (hand-made set pieces)
Players' main complaint about generated levels is that they're "not
distinguishable"; what worked elsewhere (Spelunky, Housemarque's *Saros*) is
hand-made pieces stitched in procedurally. `Room` has no type today.

- Give rooms a **kind** (server room, break room, barracks, armoury, lab bay,
  panic room, manager's office, loading dock, kitchen, ritual circle) with
  their own furniture and **one interaction each**: coolant pipes to rupture
  (a gas cloud), a break room where off-duty guards play cards (and don't
  watch the door), an armoury with a locked rack, a panic room the VIP runs to.
- Stamp 30-60 small hand-drawn rooms (the same text format as the tests'
  `Site(...)`) into BSP leaves, one to three a building.
- **Move the objective**: sometimes the target is in the manager's office on
  the ground floor, the paydata on a laptop in the break room, the item in the
  armoury.

### 2.3 Site styles that play differently
| Style | A rule of its own |
|---|---|
| Lab | contained hazards (a vat breach spreads toxic gas; sprinklers on fire); clean rooms that alarm on entry without a suit |
| Office | night cleaners (civilians who raise the alarm if they see a gun); keycard doors that open for a stolen badge |
| Warehouse | forklifts and shelving you can topple; a loading dock as a second exit |
| Den | dogs that smell you through walls; drunk gangers (slower, louder); everyone armed |
| Club | (has its crowd) the music masks gunshots near the speakers; the VIP booth |

### 2.4 Plan before you go in (Door Kickers, Satellite Reign)
The best moments in heist games are a plan working and a plan falling apart.
- **Entry points**: two or three per site (front gate, loading dock, roof by
  drone or the Kite, a sewer hatch), each with its own risk; chosen on the
  briefing map.
- **The briefing map shows what legwork revealed** (camera cones, guard
  counts per room, the safe), per floor for towers.
- **Case the site from the street**: park near the target in the city for a
  while to learn the patrols (costs heat with that faction), tap the block's
  power box for a blackout, bribe a worker. The city becomes a planning space,
  not just roads between doors.

### 2.5 Twists that grow out of the run
Twists are four, 12 % each, mostly limited to one job type. Add situations that
happen on any site:
- **A rival crew** arrives for the same prize (see 4.1).
- **The Johnson calls mid-run** with a new demand (bring the guard too; leave
  the drive; the price just went down).
- **A trace**: someone on the inside spotted the hack -- a visible countdown to
  the response team.
- **A guard offers a passcode** to be let go (extends the surrender choice).

---

## 3. Guards worth outwitting

- **Cover and flanking.** Mark cover tiles (crates, desks, wrecks, low
  walls) once per site; gunmen with LOS to you pick the nearest cover that
  still sees you, and every third guard in a group takes the field's *second*
  route instead of the first, so they come from two sides.
- **Squad roles.** A group of three or more gets a leader; kill the leader and
  the rest hesitate (react time ×2) or fall back.
- **Morale.** A guard whose two friends just died within sight retreats to the
  nearest reinforcement point instead of charging (and the surrender roll is
  tried then too).
- **Proper searching.** After losing you, guards sweep the rooms around where
  you were, in order, and close doors behind them; afterwards they stay jumpy
  (detect decays slower) for the rest of the run. They notice an open door that
  was closed, a looped console, a missing guard on a patrol route.
- **Cameras send someone.** A camera sighting raises the security level and
  sends the nearest guard to look, instead of instantly raising the alarm.
- **Light matters.** Lamps are already placed (`Level.Lights`); use them:
  you're detected faster in light, slower in shadow, and lamps can be shot out.
  The same opacity rules for both sides still apply.

---

## 4. Long careers

### 4.1 A rival crew (Shadow of Mordor's nemesis, at a small scale)
One or two named rival teams bid on the same jobs, sometimes turn up in the
same run, and remember how it went (shot, spared, robbed). They escalate --
chrome, a decker, a sniper -- and can be destroyed, bought off or recruited
through storylets. A face for the career, and a reason the late board isn't
just bigger numbers.

### 4.2 Fake SINs (tabletop)
A fake SIN with a rating (1-6) bought from the fence or the decker. Checkpoints,
licensed gear, upscale lifestyles and Spire shops check it; a loud run can burn
it (it's on the evidence); a burned SIN means stops at every checkpoint and
blocked purchases until you buy another. A steady sink that rewards clean
work, and it ties into the roadblocks.

### 4.3 Contacts with reach (tabletop: Connection and Loyalty)
Loyalty exists; add **Connection**: how much a contact can do (how much
legwork they know, their shop's tier, the jobs they get). It grows when their
faction does well -- which your runs can arrange. A loyal nobody is a friend;
a powerful contact who barely trusts you is useful and dangerous.

### 4.4 Crew stress (Darkest Dungeon)
Hired crew gain stress from close calls, civilians killed, friends going down.
At the limit they snap into a run-long quirk (*trigger-happy*, *shaken*,
*greedy*) or, sometimes, rise to a virtue (*steady*, *loyal*). Each recovers
somewhere in the city (a bar, St. Brigid's, a sim-parlour) for money and days.
It gives the crew personalities and the city another use.

### 4.5 Money and karma that never stop mattering
- **Property should cost upkeep and attract trouble**: weekly costs, and the
  owner faction's raids as storylets -- so it's a commitment, not pure income.
- **Late karma sinks**: initiation grades for the Awakened (each adds a
  metamagic: shielding, masking, extended wards); a second Edge cap; skill
  specialisations (pistols, rifles, explosives) past 6.
- **A rainy-day fund**: a career goal to retire on, visible on the PDA ("Retire
  comfortably: ¥500k and a Middle lifestyle paid for a year"), giving a reason
  to save.

### 4.6 An ending you can choose any time
Retiring is offered once, after the Iron Spire. Offer it at the safehouse
whenever the career has a story to end on (a campaign finished, a sum saved),
with the receipt and legacy -- so a long career ends by choice, not only by
death.

---

## 5. Every archetype's own verb

| Archetype | Missing | Proposal |
|---|---|---|
| Combat Engineer | no ability, no unique verb | **Breach charge** (Q: blow a wall where you point, flash-stun the room behind) and **deployable turret** (F); traps (tripwire mines) bought at Sal's |
| Face | nothing in a fight | **Command** (a guard who is bluffed can be talked into turning on his friends for a few seconds); **Human shield** (grab a guard from behind; others hold fire) |
| Decker | strong already; the Matrix minigame scales numbers only | make Decking change the *rules* there: see ICE patrol routes at 3, an extra action at 5 |
| Mage | strong already | initiation and metamagic (4.5) |
| (Rigger) | not an archetype | a sixth archetype: starts with a control rig and the Doberman, slots two drones |

Stealth verbs for everyone: close and lock doors behind you, hide a body in a
locker (or hide in one yourself), throw a bottle to distract, peek round a
corner.

The two-slot ability bar is chosen only at the safehouse; a mage with eight
spells can't change them mid-run. A **quick wheel** (hold Tab) to swap the two
slots during a run would let deep kits play deep.

---

## 6. Choices and talking

### 6.1 Skills that speak (Disco Elysium, Citizen Sleeper)
In calls and scenes, high skills add their own lines and options -- *[Electronics
4] It's a Hikaru maglock; it fails open if...* -- and every check shows its odds
before you commit. The storylet engine already supports blocked choices and
effects; this is content plus a little UI.

### 6.2 Haggling at the briefing (Griftlands, Dragonfall)
A short negotiation before accepting: ask for more, half up front, intel, or
strike a condition; the Johnson's patience is a bar; Charisma, etiquettes and
rep feed it; push too far and they walk. Gives the Face a decision on every job
and replaces today's one-click haggle.

### 6.3 Crew missions
Dragonfall's best content was its crew's personal jobs. Each hire gets a
three-part story (the contacts have them already), unlocked at trust 3.

### 6.4 Friends who show up
The people you saved (`friend:` flags, fates) should come back: a nurse who
patches you for free, a kid who becomes a lookout, a guard you spared who lets
you in a side door. Today the flag is set and never read.

---

## 7. Tooling and code

- **A balance report that plays**: give the simulated career archetype-aware
  win rates (from the bot's real results per archetype and threat), spend karma,
  and buy medkits at the real price; then the economy tuning above can be
  measured.
- **More of the bot**: the bot wins 64/78 low-threat jobs but 1/12 threat-5
  ones with starting gear. A bot that buys the obvious gear between jobs would
  measure the real difficulty curve of a career.
- **Numbers into `Tables.cs`**: ability cooldowns and durations, lifestyles and
  businesses, the bluff formula, the AI's timings, safe cash, the pay curve
  live in code today (`Career.cs:257`, `Career.Gear.cs:172`, `Run.Verbs.cs:23`,
  `Run.Ai.cs:135+`, `Career.Jobs.cs:345`).
- **One campaign system**: the Spire (`Chapter`) and Toxic Tide (`Chapter2`)
  predate `CampaignDef`; porting them removes two special paths.
- **Split the biggest files**: `tests/SmokeTest.cs` (2190 lines), `LevelGen.cs`
  (1530), `RunView.cs` (1068).

---

## Suggested order

| # | What | Why | Size |
|---|---|---|---|
| 1 | Quick fixes (section 1) | real bugs, cheap | S |
| 2 | Security level (2.1) and proper searching (3) | fixes stealth recovery and the loud-run cliff; the biggest change to how runs feel | M |
| 3 | Room kinds, set pieces, moving objectives (2.2) | site variety, every run | M-L |
| 4 | Engineer and Face verbs, quick-swap abilities (5) | two archetypes with thin kits | M |
| 5 | Guard tactics: cover, flanking, morale, light (3) | fights worth thinking about | M |
| 6 | Entry points and the briefing map (2.4) | planning, the heist feeling | M |
| 7 | Money and karma sinks, choose-your-ending (4.5, 4.6) | the late career | S-M |
| 8 | Rival crew, SINs, crew stress (4.1, 4.2, 4.4) | long-career texture | L |
| 9 | Skill voices, haggling, crew missions, friends (6) | story depth | M |
| 10 | Site-style rules, street casing, run-grown twists (2.3, 2.4, 2.5) | more variety on top of 3 | M |
| 11 | Balance report and bot improvements, tables, campaign port (7) | tooling to tune all of the above | S-M |

Sources for the outside ideas:
- Alarm systems in Invisible, Inc. -- https://www.gamedeveloper.com/design/game-design-deep-dive-alarm-systems-in-klei-s-i-invisible-inc-i-
- Darkest Dungeon's affliction system -- https://www.gamedeveloper.com/design/game-design-deep-dive-i-darkest-dungeon-s-i-affliction-system
- Satellite Reign (GameSpot review) -- https://www.gamespot.com/reviews/satellite-reign-review/1900-6416244/
- Shadowrun: Hong Kong review (RPGWatch) -- https://rpgwatch.com/articles/shadowrun--hong-kong-review-354.html
- Door Kickers review (PC Gamer) -- https://www.pcgamer.com/door-kickers-review/
- Heat Signature (Tom Francis) -- https://www.pentadact.com/2014-02-03-heat-signature-a-game-about-sneaking-aboard-randomly-generated-spaceships/
- Housemarque on hand-made pieces stitched procedurally -- https://www.techradar.com/gaming/housemarque-says-it-hancrafted-levels-art-design-and-combat-encounters-and-kind-of-connect-them-in-a-procedural-matter-to-create-saros-ever-changing-levels
- Shadowrun 5e contacts (Connection and Loyalty) -- https://nighthawks.org.uk/shadowrun/Contacts_and_Enemies
