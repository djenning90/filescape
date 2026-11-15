# Code documentation conventions

DJ's standard (2026-11-14): *open an unfamiliar class and understand what it is responsible for, why it exists, and what
assumptions you need to preserve if you change it.* This applies to every change, not just documentation passes: new
code arrives documented, and code you touch leaves at least as clear as you found it.

## Class headers

- Every hand-written type (class, record, struct, enum, interface) has a `/// <summary>` on its primary declaration:
  **what it owns**, **why FileScape needs it**, and, where it matters, **how it fits with nearby classes** (who calls it,
  what it hands on). Cite requirement and decision IDs (PLANET-22, AD-46) instead of restating them.
- Simple types get one or two lines. Do not pad.
- Partial classes: the main documentation sits on the primary declaration (`Main.cs`, `PlanetWorld.cs`, `Hud.cs`…) and
  maps the other files; each other file starts with a short header saying what its part is for.
- State the assumptions a change must preserve: determinism, thread affinity ("call from the main thread"), units and
  coordinate conventions (see `docs/units.md`), what is bounded and by what.
- Check coverage with `tools/doc-coverage.py --list` (it lists types without a header).

## Methods and local comments

- Document a method when its purpose, the caller's expectations, its side effects or the reason for its approach would
  be hard to infer — especially layout maths, camera and input handling, traversal, scanning, and transitions between
  worlds.
- Use a local comment for a decision that only makes sense at that point ("the ground blocks what is behind it",
  "a jump leaves the ground in the frame it is reported: whether it left from the bench is last frame's answer").
- Keep good existing comments. Do not narrate the obvious.

## Hardcoded values

- Cutoffs, timing, rates, limits, and visual or movement tuning get a name: a nearby `private const` (or `static
  readonly`), a well-named local, or a short comment — whichever reads best at that spot. Ordinary maths and obvious
  bounds stay inline.
- Put the **unit** in the name or the doc (`OrbitRadiansPerPixel`, `MinZoomMetres`, `FocusFollowRatePerSecond`), and say
  what changing it feels like when that is not obvious.
- Keep separate tuning choices separate even when they share a value today (the overview's and the walking camera's
  field of view are both 62°, as two constants).
- A single-use colour stays one named `Color` when that reads better than separate channel names.
- Smoothing written as `lerp(current, target, 1 - exp(-rate × dt))` is frame-rate independent exponential decay: name
  the rate (per second) and give its time constant (1/rate); see `PlanetWorld.Universe.cs`.
- When you can establish what a value does but not why it was chosen, describe the observable effect and write
  "(rationale not recorded)". Do not invent history.

## Refactors that must not change behaviour

Naming a literal can change results if it changes arithmetic. Keep the exact operation order, keep `float` literals
`float`, do not fold constant products (`x * MathF.PI / 180f` is not the same as `x * DegToRad`), and keep integer
promotion and modulo as written (`int * uint` is a `long` in C#).

Verify with the tools made for this:

- **Core outputs:** `FILESCAPE_BASELINE_OUT=before.txt tools/fs.sh test-wsl` before the change and `…=after.txt` after;
  `cmp before.txt after.txt` must report no difference (`tests/FileScape.Core.Tests/BehaviourBaselineTests.cs`: every
  layout and composer, the walkers' trajectories and a hash of rendered audio, in round-trip float form).
- **Overview camera and landing flash:** `--automate=planets-camera-trace` with the engine's `--fixed-fps 60` writes a
  frame-by-frame trace. Opening and selecting are bit-exact between runs; the landing depends on the audio clock
  (its wind-up is aligned to the beat), so compare its shape, not its bytes.
- The usual tiers: `tools/fs.sh check smoke` (and `full` before an export).

## Style

XML doc comments (`/// <summary>`, `<para>`, `<see cref="…"/>`), British spelling (metres, colour), plain sentences.
Dates in comments only where they record when a finding or decision was made, as the code already does
("(2026-11-11, DJ's playtest)").
