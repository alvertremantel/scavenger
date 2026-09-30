# Playing Scavenger headlessly

Run `dotnet run --project src/Scavenger.Cli -- --play`. This opens the same seeded
compound as Desktop, with no automatic walkthrough. Every accepted action advances
one tick; reading state, exploring known stock, and rejected actions cost no time.
EOF, `quit` or `exit` ends the console. `help` lists the complete command syntax.

`--seed 42` selects another generated region. `--authored` selects the small five-site
regional scenario. `--industry` selects the well-stocked industrial scenario.
`--load saves/compound.json` resumes a Desktop or CLI save. The previous CLI
`--world`, `--walkthrough`, `--roundtrip`, `--json`, and other demonstration modes
remain available outside play mode.

## Inspect, decide and advance

Start with `status`, `crew`, `skills 2`, `stock`, `stores`, `recipes`, `buildings`,
`plans`, `projects`, `services`, `energy`, and `jobs`. `site <id>` shows a destination's actual remaining objects,
qualifications, structure and travel time. `find "book/mechanical/2"` searches known
finite stock for an exact skill-level manual. `find fuel.diesel` finds actual fuel;
it does not generate it. Quoted arguments may contain spaces; `#` starts a comment.

Use `advance 12` or `advance 96` to stop on completions, warnings and pending field
decisions. `advance 100 all` explicitly runs the full interval. `until <task-id>`
waits for that task, continuing through other routine completions but stopping for
warnings, new fuel/power problems and pending decisions. `until last` selects the
last task started by this console. A maximum interval can be supplied, for example
`until last 300`. A still-active task prints its state and reason. Inspect the
problem, act, then resume `until` as appropriate; blocked work earns no XP.

IDs are printed when orders are accepted and in `jobs`, `learning`, and `teams`.
`respond <expedition-id> Wait` assesses/waits at an incident; `Retreat` brings the
team back. `recall <id>` retains recovered cargo while returning home.

## Learn a new role

The [tested command file](playthroughs/skills.cli) explores the default seed, recovers
Noor's next construction book, studies it across save/load, trains her from rating
60 to 70, then finds a mechanics volume and trains Ivo from 15 to 20 with Noor
mentoring. Run:

```sh
dotnet run --project src/Scavenger.Cli -- --script docs/playthroughs/skills.cli
```

The file writes saves under `saves/` and a JSON audit under `artifacts/skills/`.
It is a replayable example for the default seed, not an automatic gameplay system.
Other seeds have different volumes; inspect their known stock first.

An example recovery uses explicit crew and cargo:

```text
expedition apartments-010 2 manual-2-mechanical-2=0.4
until last
books mechanical
study 2 mechanical
until last
skills 2 mechanical
practice 2 mechanical 3
until last
skills 2 mechanical
```

`study` chooses the first available matching recovered volume when its batch ID is
omitted. `study 2 mechanical transfer-2` chooses an explicit volume. Higher-level or
wrong-subject books are rejected. One volume serves one reader at a time and is
reusable; remote volumes and inbound cargo cannot be read. `practice` needs finished
next-level theory; it can run independently or with an optional qualified mentor.
`cancel learning <id>` retains study/XP and releases claims. `learning` reports
active and historical learning jobs. `skills` reports both gates for every member.

## Run the compound

`recover pharmacy`, `recover sorbent`, `recover fuel` and `recover materials` use the
same simulation preflight and finite stock as Desktop's supply plans. They select a
small qualified available crew and dispatch; they can be rejected if no safe plan
exists. Custom expeditions use `expedition <site> <workers> <object=kg,...>` with
optional approach, protective supplies and receiving store.

For the clinic, recover pharmacy and sorbent, then queue the recipes:

```text
produce refine-fever-stock clinic-station 4 2 clinic
until last
produce formulate-fever-dose clinic-station 4 2 clinic
until last
treat 1
until last
```

Deliveries and incidents take time. A warning may require more recovery, a different
carrier, refuelling or simply waiting for an already active delivery. Repeated
`until` calls after inspection resume the same task. `recipes` lists actual recipe
IDs, inputs, skill gates and stations. `produce` accepts operator, carrier, product
store and comma-separated source stores. Sources default to salvage and clinic;
products default to salvage; wastes go to the waste store. `auto` in the operator
position selects controller operation, with the existing automatic recipe and
computer-control gates. A `-` technician selects an unattended continuous computer
monitor; ordinary technician tasks still require a qualified member.

The trained Ivo can operate `dry-hdpe` and `shred-hdpe`, freeing Mira for other work.
Wash stock first, then issue `produce dry-hdpe dryer 2 3 salvage`, using Noor as
carrier. Keep real fuel supplied: `refuel 6 3` physically carries up to six kilograms
of depot diesel with Noor. Recover more when depot fuel is gone. `maintain` creates
a custom stock target; `recovery` creates the authored polymer recovery targets;
`order <id> false` pauses one. `orders` shows stock targets and blockers.

Construction supports `build <plan> <x> <y> [builder] [carrier]`, `repair <fixture>`,
`door <x> <y> true|false`, `assign <worker> <project>` and `release <worker>`.
`generator <id> true|false`, `battery <id> true|false` and `conduit` control utilities;
`room` designates room rectangles. `transfer <batch> <store> <kg> <carrier>` moves actual batches or whole items.
The CLI also exposes computer components, digital assets, diagnosis, assembly,
repair, installation, certification study, telemetry, training, validation and
monitoring through `computer`; `power <machine> true|false` controls machine power; consult `help` and the industrial play guide for
those workloads. All their physical input, power, storage and qualification gates
remain simulation rules.

`cancel build|produce|haul|computer|treat|learning <id>` cancels eligible tasks.
Loaded hauls retain the existing cancellation rules. `report path.json` exports
physical, industry, regional and progression state; `map path.svg` exports actual
geography. Save/load refuses inconsistent claims or progression ledgers.

## Automation and limits

`--script path` executes the same commands, echoes them, and stops at the first
rejected or malformed action with exit code 1. Invalid startup options or file
errors return 2. Interactive input keeps running after rejected actions. There is
no hidden tick loop between commands and no automatic incident response.

The CLI exposes exact quantities and claims, whereas Desktop emphasizes maps and
contextual controls. Its playtests verify simulation progression and resource
behavior; the separate actual-mouse tests verify the Desktop paths and layout.
