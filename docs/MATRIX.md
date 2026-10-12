# The Matrix, second pass

What decking was, what was wrong with it, and what it is now. Numbers live
in `src/Sim/Tables.cs` (`Decks`, `Programs`, `IcDefs`, the `Mx*` constants);
the rules in `src/Sim/Matrix.cs`; the site side in `Run.Player.cs`
(`StepMatrix`) and `Run.Elites.cs` (the spiders).

## 1. What there was

- **One minigame, three uses**: Datasteal (jack in at the site's mainframe),
  Hack (Null's jobs, from your own chair) and the tower's security pass.
  A fixed 9x5 grid of nodes, three datastores, `1 + threat/2` ICE that walk
  towards you on a clock. Step on a store and it is yours; touch ICE and you
  are traced (alarm, dumpshock 10, or 25 for black ICE on threat 4+).
- **It was Pac-Man**: no verbs but moving. ICE could not be fought, slowed,
  fooled (bar one Spoof) or read; every ICE was the same ICE.
- **The site and the host did not know each other**: the security level on
  site, the alarm, cameras and turrets changed nothing inside. The spider
  (a security decker at a console, threat 5 Halcyon only) slowed hacks and
  added +1 to the host's threat, and that was all he was in there.
- **Decks were a number**: the cyberdeck implant (ability Hack), then
  "Obsidian" and "Excalibur" sold as *programs* that multiplied speed.
- **Programs were perks**: owned = always on, no cost to carrying them all.
- **Electronics** did everything: maglocks, turrets, the Matrix, the tower
  override. Nothing about the Matrix rewarded skill except speed.

## 2. What SR5 offers (and what we take)

From the SR5 Matrix rules (Basics, Cyberdecks, Programs, IC):

| SR5 | Here |
|---|---|
| Deck **Device Rating** -> Matrix condition monitor | deck **rating**: condition boxes `6 + 2 x rating`, damage carries over between jack-ins on a run |
| **ASDF** attribute array, assigned and swapped by the decker | four values a deck comes with; you choose which goes to **Attack, Sleaze, Data processing, Firewall** in the safehouse |
| program **slots** | **memory** (Mp): programs and downloaded paydata share it |
| **Overwatch score**, GOD converges at 40 | the **trace**: a meter that every second inside and every loud act fills; full = converged |
| hack on the fly vs brute force | **firewall gates** on some data lines: push through quietly (Sleaze), or spike them open (Attack, loud) |
| **IC**: Patrol, Killer, Blaster, Black, Acid, Tar Baby, Crash, Track... | eight IC with one rule each (below) |
| link-lock, dumpshock, biofeedback | locked: can't jack out; bricked deck: dumped, dumpshock; black IC and the spider hurt your body |
| spiders (security hackers) | the on-site spider is **in the host**: crash his persona and his body sits stunned; shoot his body and the persona vanishes |
| host rating | the host's **rating** 1-6: the job's threat, +1 if the site is at ALARM, +1 if a spider is in the system |
| Hacking / Cybercombat skills | a new skill, **Decking** |

## 3. The host

`MatrixRun` is still a 9x5 grid of nodes (it fits the screen, and the bot
and the agent can read it), but nodes are now things:

- **Entry**: where you come in and the only place to jack out.
- **Datastores**: files. The job's three (◆, the objective as before; they
  take no memory, so a full deck can still do the job) and two to four **paydata** files (◇, 1-4 Mp, worth
  `size x (250 + 150 x threat)` give or take): optional, sold to Null at
  full value or to Wen. Some are **encrypted** (rating 3+): twice as slow to
  pull, unless Decryption is loaded. Pulling a file is *hold E* on its
  node -- it takes time, and you are a sitting target.
- **Control nodes** (site runs only, one per thing the site has): CAMERAS
  (every camera looped), MAGLOCKS (every locked door opens), TURRETS (turrets
  and drones fight for you for a minute), ALARM (the security clock stops).
  Hold E. Each is a jolt of trace.
  From home there is an ACCOUNTS node instead: a little cash skimmed.
- **The core** (the security node): where IC is launched from once the host
  is alert. It has hit points; crash it and no more IC comes, and the trace
  builds half as fast. Null's "crash the host" jobs are this.
- **Firewall gates**: a few data lines are locked (rating 2+). Push into one
  to crack it (speed from Sleaze, Decking, Icebreaker/Decryption; quiet), or
  spike it (Attack; quick and loud: trace +6 %).

### Alert

A host starts **calm** unless the site is at ALARM (security level 3+) or
a spider on site is alert -- then it starts **alert**. On edge (level 1+)
adds a patrol. A patrol that sees you **reports** after 2 s: crash it first
and nobody knows. An alert host launches a hunter IC from the core every
`12 - rating` s (at least 4), up to `2 + rating` of them, and the trace fills
2.5 times as fast.

### The trace

0-100 %. Per second: `0.4 % x rating`, x2.5 alert; a Track IC on you or next
to you +10 %/s; each spike +1.5 %; a brute-forced gate +6 %; a control node
+8 %; downloading +0.4 %/s. Sleaze (-5 % a point), Decking (-6 % a point)
and Sneak (-25 %) slow it. Full: **converged** -- dumped, dumpshock,
alarm (or one of the two traces a Hack job allows), exactly as being caught
by ICE was. Spoof, Edge and Lamplight still save you the way they did.
Jack out and back in and half the trace is still on you.

### IC

| IC | Rating | What it does |
|---|---|---|
| Patrol | 1+ | walks a beat, never hunts; sees you on its node or the next one along a line, reports after 2 s |
| Killer | 1+ | guards a datastore until you come near or the host is alert, then hunts; 2 boxes a hit |
| Track | 1+ | hunts fast, does no damage: while it is on you or next to you the trace fills +10 %/s. Fragile |
| Tar baby | 2+ | slow; a hit link-locks you 5 s (no jacking out) and halves your speed |
| Acid | 3+ | a hit burns a point of Firewall (for the run) -- then boxes |
| Crash | 3+ | a hit crashes one of your loaded programs (for the run) |
| Blaster | 3+ | 2 boxes, a little biofeedback (your body), link-lock 3 s |
| Black IC | 5+ | 3 boxes, `6 + 2 x rating` biofeedback through armour, link-lock 4 s. The one that kills |

IC hit for `base + rating/3 - firewall/3` boxes (at least 1), on a clock,
when it is on your node or next to you along a line. Your hits: **spike**
(Space, or click): `Attack x (1 + 0.12 x Decking)` (Hammer +40 %) to the
weakest IC on your node or next to you. IC has `3 + 2 x rating` (black IC
twice that). A crashed IC is gone; an alert core makes more.

### Spiders: the opposing deckers

- On site the spider is where he was (a console, as far from the way in as
  there is one) -- and now from **threat 4** on Halcyon datasteals and
  offices and labs, and on **threat 5** Halcyon sites a **team of two**.
- In the host each spider is a **persona** (S): hunts you smartly, hits for 3
  boxes plus biofeedback, relaunches crashed IC, makes the trace fill 30 %
  faster. He has `14 + 3 x rating`.
- **Crash his persona** and his body slumps at the console for 20 s
  (dumpshock): free to walk past, or to finish.
- **Take his body out** (kill, knock out, stun) -- and he is gone from the
  host, and the host is one rating easier. That is the "simplify the run"
  play: clear the spider's room first, then jack in.
- On Null's jobs from home, rating 4+ hosts have a spider of their own: no
  body to shoot, so crash him or avoid him.

## 4. Decks

Owned hardware, chosen in the safehouse Loadout tab; Null sells them on a
new **Decks** counter. Every runner has the stock commlink; the cyberdeck
implant is a deck of its own and, with any other deck, runs it **hot-sim**
(moves 15 % faster in the Matrix). It still gives the Hack ability.

| Deck | Rating | Array | Memory | Price | Trust |
|---|---|---|---|---|---|
| Stock commlink | 1 | 2 1 2 1 | 4 Mp | -- | |
| Cyberdeck implant | 2 | 3 3 2 2 | 6 Mp | (clinic) | |
| Kestrel MCD-2 | 2 | 4 3 3 2 | 8 Mp | ¥12,000 | 0 |
| Hauler Datavault | 3 | 3 3 4 4 | 16 Mp | ¥30,000 | 1 |
| Ronin Shiv | 3 | 6 4 3 2 | 8 Mp | ¥35,000 | 1 |
| Mirrorshade Veil | 3 | 3 6 4 3 | 10 Mp | ¥42,000 | 2 |
| Obsidian-class | 4 | 6 5 5 4 | 14 Mp | ¥110,000 | 4 |
| Excalibur-class | 6 | 8 7 7 6 | 20 Mp | ¥400,000 | 5, prestige |

- **Attack**: spike damage, brute-forcing gates.
- **Sleaze**: how far a patrol sees (Sleaze 5+: only on its own node),
  how fast a gate cracks quietly, how fast the trace fills.
- **Data processing**: moving node to node, pulling files.
- **Firewall**: boxes off every IC hit, the chance to shrug a Crash.

The rating still gives the old outside-the-Matrix hacking bonus (Obsidian
x1.3, Excalibur x1.7). Saves move the old Obsidian/Excalibur "programs"
into the deck list.

## 5. Programs and memory

Programs are **loaded** into the deck in the Loadout tab, up to its memory;
an unloaded program does nothing. Downloaded files fill the rest, so a deck
full of programs has no room for paydata. In the Matrix, **G purges** the
last program loaded (it frees its memory for the rest of the run) when a
file won't fit (the prompt says so, and names the program G would purge).
Buying a program loads it if it fits. The job's own datastores and pass keys
need no memory.

| Program | Mp | Kind | Effect |
|---|---|---|---|
| Icebreaker | 2 | | hacking +50 %; gates crack 50 % faster |
| Spoof | 2 | | cameras ignore you; the first full trace is shrugged off, traces never raise the alarm |
| Daemon | 2 | | hacked turrets and drones stay yours |
| Overwatch | 1 | | pings every hostile on site for 6 s |
| Ghost protocol | 1 | | Null's own: cameras ignore you, IC 30 % slower |
| Lamplight | 0 | | as before |
| Hammer | 2 | | spikes +40 % |
| Armor | 2 | | IC hits -1 box |
| Sneak | 2 | | patrols only see you on their own node; trace -25 % |
| Decryption | 1 | | encrypted files and gates twice as fast |
| Biofeedback filter | 1 | | black IC and spider biofeedback halved |
| Patch | 1 | active | once a jack-in: repair 4 boxes |
| Signal scrub | 2 | active | once a jack-in: trace -40 % |
| Decoy | 2 | active | every 25 s: hunters chase a decoy for 6 s |

Active programs go on **Q, F, R** inside the Matrix, in load order.

## 6. Decking (the skill)

The eighth skill. Saves start it at their Electronics (so nobody loses);
the Decker archetype starts with Electronics 2 and Decking 3. Electronics
keeps maglocks, turrets, cameras and lifts; Decking is the Matrix:

- every point: spikes +12 %, trace -6 %, gates crack +15 % faster;
- **3**: you see each IC's next step and its hit points;
- **4**: quiet gate cracking makes no trace at all;
- **5**: spikes come twice as fast;
- **6**: once a run, a full trace slips off you.

The tower's security override needs a deck and **Decking 3** (or
Electronics 5, as before).

## 7. Null's work

Null already gives Hack jobs from home and has an arc; now the jobs vary:
**pull** (three datastores, as before), **crash** (crash the host's core)
and **plant** (reach the archive node and hold E for 6 s to leave a
backdoor). Null buys paydata at its full value (Wen pays her usual cut).

## 8. The interface

Inside: the trace bar and the host's state across the top, the deck's
condition boxes, memory (programs in violet, files in yellow, free) and
the active programs with their keys at the bottom; IC as lettered glyphs
coloured by kind, with their hit points and next step at Decking 3; gates
as red dashed lines with a crack bar; control nodes and the core as
labelled squares; download/control progress as a ring round your node.
The agent harness prints the same thing as text.

## 9. Not done (next)

- A rival crew's decker as a persona racing you for the datastores.
- Reconfiguring the array inside the Matrix (SR5's free action).
- Agents (autonomous programs that fight beside you).
- A separate hardware dealer contact (Null covers decks for now: one contact,
  one voice, one place to go for anything with a jack in it).
