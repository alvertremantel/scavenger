# Skills, knowledge and practice

This document describes the implemented progression and its design decisions. Simulation
owns it; Desktop and CLI submit the same commands. The CLI is a playable interaction
head, alongside the existing automated demonstration modes.

## Progression model

Every compound member has all sixteen specialties, independently rated. Ten levels
(L0 untrained through L10 expert) map to existing 0–100 qualification ratings. Existing
specialists keep their exact starting ratings: a 75-rated medic begins at L7 with a
75 rating and advances to 80 only after meeting both L8 gates. Background knowledge
and experience explain existing levels; no retroactive book hunt is required.

For each next level, in order:

1. Recover a readable volume for that exact skill and level. Possessing a book alone
   changes no capability. A higher-level book cannot skip prerequisites.
2. Assign a member to study it. They physically walk to its store, claim the book and
   their own labor, and spend simulation time studying. Books remain reusable after
   completion or cancellation. Study progress survives interruption and save/load.
3. After theory is complete, earn practical XP in that specialty through actual
   productive effort or a hands-on exercise. Earlier work does not bank XP for a
   theory gate that has not been learned. Idle time, walking to work, blocked jobs,
   unattended computers, and cancelled/completed tasks earn no repeated XP.
4. Promotion happens when both gates are met. XP is capped at the current level's
   requirement and excess does not spill into an unstudied level. One job cannot
   bypass the next book. Qualifying work becomes available and rated work speeds
   increase through the existing qualification/rate rules.

Level n takes `8 + 4n` study effort and `12 + 8n` practical XP. One unit of productive
manual effort gives one XP, with smaller secondary contributions where appropriate.
At 96 ticks/day, a basic level takes roughly half a day of study plus practice, with
walking and productive logistics adding real time. Higher levels require increasingly
long attention from scarce specialists. Research competence accelerates study by up
to 25%; it does not bypass either gate. Knowledge never decays, books are not consumed,
and advancement is deterministic. There is no passive offline XP or global level.

Hands-on exercises provide a bootstrap path when a member cannot yet qualify for any
productive work. They require the next theory gate, reserve the member, walk to the
appropriate fixture, and accrue practical effort only while physically there and the
fixture is serviceable. Optional qualified mentors reserve their own labor and walk
to the exercise too; mentoring improves exercise pace by 25% but grants no free theory.
Exercises train competence; they do not create resources. Productive work is preferable
when it also yields urgently needed stock. No mentor is required to avoid a circular
unlock dependency.

## Level costs and compatibility

| New level | Resulting rating | Study effort | Practical XP |
| --- | --- | --- | --- |
| 1 | 10 | 12 | 20 |
| 2 | 20 | 16 | 28 |
| 3 | 30 | 20 | 36 |
| 4 | 40 | 24 | 44 |
| 5 | 50 | 28 | 52 |
| 6 | 60 | 32 | 60 |
| 7 | 70 | 36 | 68 |
| 8 | 80 | 40 | 76 |
| 9 | 90 | 44 | 84 |
| 10 | 100 | 48 | 92 |

Untrained means L0/rating 0, not a general-purpose competence bonus. Practical
exercises contribute `.5 + rating/100` XP per active tick, optionally multiplied
by 1.25 for mentoring. Ordinary manual production/construction/computer tasks retain
their `.25 + rating/100` effort rates. Loaded transport credits at most one XP per
step, scaled by load / 6 kg, and never credits the empty walk to pickup. Field
primary work credits its actual accepted search effort to the qualified specialist;
secondary scavenging and material-handling specialties split half that effort
across the crew. Navigation credits .25 XP per outbound tick, safety .1 XP per
active recovery tick, and reading credits quarter effort to already-studied research.

Scavenging and relevant material-handling ratings each add up to 20% to search pace.
Individual field safety reduces exposure by up to 25%. Navigation reduces structural
assessment from six to as few as two ticks; it does not alter saved road distances.
Research adds up to 25% to physical reading pace. Existing qualifications and rated
work speeds read promoted workforce profiles, so promotion has concrete effects.

The old adapter-training textbook remains a specialized technique certificate,
with its verified digital-copy and computing requirements. Reading it contributes
research practice when the next research theory is already learned; it grants no
free general skill level. Unattended training/monitoring grants no operator XP.

Older physical/industrial/regional saves initialize backgrounds from their existing
ratings without rerolling stock or creating retroactive book loot. New saves retain
all study and XP ledgers, source/labor/fixture claims and command history. A save
containing new learning commands cannot pretend to be a legacy save by deleting the
skills module. Immutable Haldane skill profiles are updated through a derived roster
projection that preserves every reservation and presence flag. No engine checkout
change is required, and no authoritative member state lives in Desktop or CLI.

## Skill roster and meaningful uses

| ID | Skill | Productive XP / capability | Likely specialist sources |
| --- | --- | --- | --- |
| hauling | Logistics | Physical loaded transport; existing carrier gates | warehouses, homes |
| construction | Construction | Building, structural recovery, safer careful access | hardware, schools |
| mechanical | Mechanics | Repairs, machinery processes and fixture recovery | workshops, fuel depots |
| electrical | Electrical work | Conduits and power installations | substations, technical schools |
| chemistry | Chemistry | Sorting/refining/formulation and qualified recovery | laboratories, pharmacies |
| medicine | Medicine | Clinic care and medical stock handling | clinics, hospitals |
| electronics | Electronics | Diagnosis, assembly, component repair/recovery | repair shops, data centers |
| computing | Computing | Hands-on software, data and automation preparation | libraries, data centers |
| scavenging | Scavenging | Every active salvage search; improves recovery pace | homes, recycling yards |
| navigation | Navigation | Outbound travel; trained teams assess blocked routes faster | homes, libraries |
| survival | Field safety | Active site exposure management; reduces exposure | farms, homes |
| agriculture | Agriculture | Fertilizer/agricultural recovery | farms, greenhouses |
| cooking | Food handling | Recovery of dry food | groceries, homes |
| metalworking | Metalworking | Metal recovery | workshops, warehouses |
| tailoring | Textiles | Cloth/protective-supply recovery | homes, apartments |
| research | Research | Actual reading; study efficiency | libraries, schools, laboratories |

These last specialties govern field efficiency/secondary work and hands-on learning;
full farming, cooking and clothing production chains are future systems. They are not
advertised as implemented production chains. Existing medical fiction and provenance
rules remain in force.

## Educational loot and bottlenecks

Each volume is a unique indivisible 0.4 kg physical item. It participates in finite
regional stock, cargo, depot capacity, transfer, and rival-removal ledgers. Only an
unreserved, complete volume physically in a reachable compound store can be studied;
remote intelligence and inbound cargo do not count. A copy admits one reader at a
time; duplicates permit parallel learning. Learned theory survives moving the source.

Seeded, independent educational-loot randomness leaves geography and existing supply
rolls stable. Homes/apartments have a 70% chance of 1–3 volumes; schools/libraries a
90% chance of 4–8; specialist sites a 60% chance of 1–3. No location or exact subject
is guaranteed. Specialist shelves bias 75% toward plausible subjects, leaving a 25%
chance of a general volume. General education and residences cover the entire roster.
Level distribution favors L1–L4 (60%), L5–L7 (30%) and L8–L10 (10%). Readable material
is common overall; finding a specific next level and choosing safe routes is the
main bottleneck. No reroll-on-load, automatic target guarantee or magical replenishment.

Known stock can be searched by subject and level in both heads. The crew progression
report shows theory, study effort, XP, and the precise remaining gate for every skill.
The playable CLI additionally exposes book IDs, task IDs, exact blockers, inventories,
finite stock, recipes, worker claims, save/load and reports; it never runs an automatic
walkthrough behind the player's back.

## Verification and acceptance

Implementation order: playable CLI; simulation catalogue/state/commands/save validation;
physical educational recovery and work XP; CLI learning controls and seeded playthroughs;
Desktop crew learning and task controls; full builds/tests and real headless renders.

Regression coverage must exercise knowledge-only and XP-only rejection, missing/wrong
books, prerequisites, concurrent claims, partial/cancelled study, mentor presence,
productive versus blocked work, novice bootstrap, promotion affecting qualifications,
no experience spill, seeded variation and distribution, indivisible cargo, conserved
mass, malformed/orphaned saves, old-save migration, and mid-study/mid-expedition replay.
CLI evidence should include exploration, a real recovery, study, productive practice,
promotion, and save/load continuation, alongside the existing industrial/clinic flow.
Desktop checks cover actual input and layouts at 1050×780 and 1440×1000.

## Playtest findings and evidence

The default generated seed contains 180 readable volumes. The general supply is
abundant, but Ivo's L7 logistics book and Noor's L8 construction book are absent:
this demonstrates subject/level scarcity instead of universally guaranteed progression.
A finite world can lack a particular volume; another member's background or another
specialty can provide alternate labor routes. This slice has no book trading, new
regions within a continuing save, book authoring, or replacement stock. These are
future escape valves if longer campaigns make exact-level scarcity too restrictive.

The checked-in CLI example reaches tick 238 (day 3), recovers two volumes,
roundtrips during study, promotes Noor's construction 60→70 and Ivo's mechanics
15→20 with Noor mentoring. Both volumes remain physical/reusable, and tracked mass
remains 3915.771 kg. This opens the real dryer/shredder operator gates for Ivo.

An additional interactive CLI run recovered pharmacy stock and sorbent, responded to
a structural incident, refined and formulated actual fictional medicine, delivered
fuel, and treated Mira at tick 568. Anya then washed HDPE, and the newly qualified
Ivo processed it in the dryer while Noor carried inputs/outputs. That run discovered
that the initial `until` command could spend weeks advancing a powerless batch.
The final console pauses at actual warnings, new fuel/power loss and field decisions;
`until last` also removes the need to copy IDs for ordinary sequential play.
The stalled save was reloaded, refuelled, and resumed without losing work or mass.

Desktop real-mouse tests recover a volume through the linked stock search and
planner, verify its indivisible cargo row, study it, cancel/resume partial work,
practice and promote at both 1050×780 and 1440×1000. Real Skia renders were inspected;
controls fit the drawer and the compound map retains its minimum playable dimensions.
Artifacts are under ignored `artifacts/skills/`; saves are under ignored `saves/`.

Final verification:

- `dotnet build Scavenger.slnx -m:1 --no-restore`: zero warnings/errors.
- `dotnet test Scavenger.slnx -m:1 --no-restore`: 169 simulation, 19 Desktop and
  5 CLI tests passed (193 total). The .NET test runner requires its local socket
  outside the filesystem/network sandbox.
- `dotnet test tests/Scavenger.Cli.Tests -m:1 --no-restore`: final CLI controls and
  task-wait changes also passed the five CLI regressions.
- `dotnet ...Scavenger.Cli.dll --script docs/playthroughs/skills.cli`: the final
  checked-in gameplay example completed at tick 238 with both promotions.
- Additional CLI command files exercised hardware task queueing, computer/generator/
  battery power, conduit isolation, room designation, unattended monitoring, automatic
  production gating and save/load. Unavailable hardware/control/inputs produced real
  blockers and claimed no fictitious operator.
- Headless `--screenshot ... --view crew --width 1050 --height 780` rendered the new
  workspace, alongside the tested minimum/standard mouse-playthrough captures.
- `git diff --check` passed. The independent Haldane checkout is clean and matches
  `haldane-revision.txt` (`93c960a8504ecb7d102868b7684fb9c6655667d2`).
