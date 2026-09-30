# Basic playability slice

The existing simulation supports physical travel and a complete recovery/clinic chain,
but the player interface exposes experiments rather than a legible game loop.

## Confirmed obstacles

- Prepare clinic assigns the only medic to a 60-effort demonstration project. It
  produces no medicine and provides no cure, yet blocks both real medical processes
  and treatment. There is no project-release control.
- Expedition cargo uses one quantity for every object; choosing a mixed medical
  manifest requires knowledge of internal recipes. Crew selection hides specialties,
  plain clicks are awkward for multiple selection, and stock/access are elsewhere.
- Clinic production is queued without showing ingredients, delivery progress,
  duplicate orders, or a useful next action in the clinic itself.
- Fast forward only watches newly emitted events. A team already awaiting a decision
  can sit exposed while the player skips an entire day. +day ignores interrupts.
- The overview describes constraints but provides no short opening progression.
  Worker markers overlap, and a large advance hides the underlying walk steps.
- The refuel button and industrial walkthrough reference a missing physical haul
  route. Starting industrial fuel concealed the failure until longer play.
- The regional map only has fixed zoom buttons, with no drag navigation or cursor
  anchoring. A large region is awkward to browse.
- Recovery orders are incorrectly restricted to tick zero even though the player
  needs them after treating Mira. Controls mix demo commands with useful actions.

## Scope and acceptance

Build a live opening checklist and read-only supply suggestions using actual finite
stock, quality, skills, paths, and receiving capacity. Suggestions populate the
planner; the player reviews and dispatches. Keep custom planning available, with
individually editable quantities and plain-click multiple selection.

Show medicine inputs and local process jobs, with explicit readiness and cancellation.
Separate opening the clinic from assigning experimental projects. Release project
assignments and identify active duty/physical destinations. Halt normal time advances
on existing field decisions, new decision/completion events, and power loss. A manual
single step remains available. Keep queued blocked jobs recoverable and explain them.

Add cursor-anchored wheel zoom, captured left/middle-button drag panning, a drag
threshold that separates inspection clicks from navigation, zoom-out and Fit controls.
Verify anchoring, selection stability, outside-map release, zoom bounds and rendered
minimum-window layout with actual mouse input.

Acceptance is a generated-world desktop playthrough of at least three days: two
supply expeditions, medicine production, cure, repeated recovery, fuel delivery,
production and construction/repair, save/reload while work is active. Verify physical
movement, field decision handling, and mass/save invariants. Render and inspect fresh,
clinic, planner, and developed states at the minimum window size.

No new world generation, population hunger/food systems, freehand pawn control,
combat, or real pharmaceutical recipes. Older saves receive no compatibility promise.
