# Planetoid worlds: feasibility assessment (2026-11-06)

DJ's next direction: a world of small planetoids, a folder possibly becoming an inhabitable body, with travel between them, in the spirit of *Super Mario Galaxy*'s playful small worlds. This page looks at what that would ask of the current systems. The experiment's results are **demonstrated** (code and tests); the effort estimates are **provisional**.

## The question and how it was tested

The question was whether FileScape's place systems can carry over to curved bodies, or whether walking, collision, composition and the camera would have to be rebuilt.

It was tested with an isolated experiment (`--experiment=planetoids`; Core `Experiments/Planetoids.cs`, `PlanetoidTests`; app `Experiments/PlanetoidExperiment.cs`), built from three pieces:

- **A chart per body.** An azimuthal-equidistant map from the body's pole. A place's flat local coordinates (landmark at the origin, north = −Z, y = height) become a point on the sphere, and back again.
- **The unchanged walker.** `Walker` and `PlaceTerrain` integrate in the chart, so steps, collision, coyote time and the jump all carry over. The chart pose is mapped onto the sphere for the camera and avatar.
- **Free flight between bodies.** A 3D phase under every body's inverse-square gravity. Touching a body lands the traveller in its chart. Launch speeds come from simulating the arc.

**Results (demonstrated):**

| Check | Result |
|---|---|
| Chart → sphere → chart round trip | within 1 cm |
| Distance from the pole along the surface | preserved exactly |
| Walking 64 m round a 40 m body | feet stay on the surface (r = 40.00 m) |
| A launch from one body | lands walking on the other (in Core and in the app) |

Screenshots are in `docs/evidence/2026-11-06-explore/` (`planetoid-*.png`).

**What the experiment does not establish:**
- The feel of walking on a small sphere. The camera here is a simple chase camera.
- Distortion far from the pole. The chart shrinks distances *across* by sin φ / φ: 0.64 at a quarter turn, 0 at the antipode, so a full place would need its walkable region limited, or several charts.
- Picking, labels or rendering of a full place on a sphere.
- Performance with real places.

## Update 2026-11-06: corrections, a real folder-place, a fast transfer

**Handedness (corrected, demonstrated).**
- **The defect:** the chart's east at the pole was computed with the wrong sign, so every chart was mirrored on the sphere: chart +x came out at world −X, and a clockwise ring of gates read anticlockwise.
- **Why no test caught it:** round trips agreed (the mirror is consistent), and the walker felt right, so the existing tests could not see it.
- **The fix:** east = north × up, so east × north = up, the place's own handedness.
- **The test:** `The_chart_keeps_its_handedness_on_the_sphere` fails on the old code (east = −0.9999 X) and passes now.

**Distortion (measured).**
- Across distances shrink by sin φ / φ: `Distances_across_shrink_by_sin_phi_over_phi` checks it at 10, 28 and 40 m on a 40 m body.
- The experiment's place reaches 24 m on a 48 m world, about 29° from the pole, where across distances are 94–100 % of the chart's.
- **Cap:** walkable content is kept within about 35° of the pole. Beyond about 60° the mismatch exceeds 10 %.
- **Seam:** the antipode is a singular point of the chart; the terrain boundary keeps walkers far from it.

**Flat collision is not universally correct on a sphere.**
- Collision runs in the chart, so it matches the drawn boxes only to the chart's accuracy.
- Along the radius from the pole it is exact; across, a box's drawn footprint is narrower than its collision footprint by the factor above (≤ 6 % in the experiment).
- Larger places need the cap, several charts, or footprint correction.

**A real folder-place on a curved world (PLANET-04, demonstrated in the experiment).**
- "Orchard", an original small folder built in memory, is composed by the unchanged `PlaceComposer` (compact layout) and drawn on the teal world: every terrain box is placed in the local tangent frame, and gates carry their names.
- `--automate=planetoids` checks:
  - walking (6 m; feet at r = 48.00 m; never inside a structure);
  - a jump (apex 1.20 m);
  - looking up 30° in first person (view·up = 0.500, no roll);
  - picking a gate pillar and a file tower by marching the world ray and testing in the chart.
- Screenshots are in `docs/evidence/2026-11-06-iter/`.

**Transfer (PLANET-03, demonstrated).**
- The slowest ballistic arc took **13.56 s**. The guided arc (`LaunchGuided`: quadratic, raised away from both worlds, linear in time) takes **2.23 s**.
- Its speed is highest leaving and arriving and lowest at the top, so it reads as a burst, a moment to see the target, and a decisive landing in the target's chart exactly where aimed.
- The chase camera pulls back and widens a little in flight and dips on landing; it never shakes or cuts.
- The physical free flight remains in Core for comparison.

**Still open:** camera frames for flight between bodies in the product, aerial layout in 3D, and how place size maps to body size.

## System by system

| System | Today | On planetoids | Verdict |
|---|---|---|---|
| **Composition** (`PlaceComposer`, `PlaceComposition`) | Polar layout in a flat plane: a landmark at the origin, gate rings and file quarters by angle and radius | Polar rings and angles map naturally onto a sphere cap: landmark at the pole, rings as latitudes, gates as longitudes. The composer's output is the chart. | **Keeps working.** It needs a bound on the cap (≈ 100° from the pole), so ring radii must be capped by body size. |
| **Terrain and collision** (`PlaceTerrain`) | Boxes standing on a plane, support heights, ramps, rings | Unchanged in chart space (demonstrated). Near the pole, chart and sphere agree. Far out, a box's footprint is slightly narrower on the sphere than in the chart, so visuals and collision drift apart by up to ~10% at 60°. | **Works with a limit.** Keep walkable content within ~60° of the pole, or correct the box widths. |
| **Walking and jumping** (`Walker`) | Kinematic, Y-up, 120 Hz | Unchanged in the chart; gravity is "down the chart's Y", which is radial on the sphere (demonstrated). | **Works.** Low-gravity bodies need only a lower `Gravity`. |
| **Camera** (`CameraRig.SetPose(eye, yaw, pitch)`) | Yaw and pitch about the world's Y axis | Needs a local frame: up = radial, with north and east carried by the chart (`Planetoid.FrameAt`). The rig needs a `SetPose(eye, basis)` path, and yaw and pitch become relative to the local frame. | **Change needed.** Small and contained: one new rig entry point and a frame-relative look. |
| **Flight** (`Main.Walk`) | Level W/S/A/D, vertical E/Q along world Y | "Level" and "up" become tangent and radial at the camera. Between bodies, the experiment's free flight under gravity already works. | **Change needed.** Medium: flight relative to the local up. |
| **Landing** (`FindLandingSpot`) | Searches the plane below | Searches the chart below the radial projection. Capture by the nearest body works in the experiment. | **Change needed.** Small. |
| **Gates and thresholds** (`ThresholdTracker`) | Plane geometry | Unchanged in the chart. | **Works.** |
| **Picking** (`PlacePicker`) | Ray against boxes in the plane | A world ray is not a straight line in the chart. Pick in world space against the mapped meshes, or march the ray and test in the chart. | **Change needed.** Medium. |
| **Rendering** (`PlaceWorld`, MultiMesh) | Instances in the plane | Each instance needs a transform from its chart position to the sphere (position and tangent basis), as the experiment does for its arches. Long items such as avenues, arcs and terraces must be subdivided to follow the curve. Shaders that use world Y as "up" (fog height, the plaza rings) need a radial up. | **Change needed.** Medium to large, mostly mechanical. |
| **Signs and labels** (`SignLayer`) | Screen space | Unchanged: anchors are world points. | **Works.** |
| **Aerial world** (`WorldLayout`, `PlaceFrame`) | Islands on a plane; the current place's frame is a floating origin | Bodies would be placed in 3D space, for example along the same tree layout with height for depth or orbits around the parent. `PlaceFrame` (2D origin and north) becomes a 3D transform. The floating origin and "the place you are in never moves" carry over. | **Change needed.** Medium; the boundary already exists. |
| **Identity, style and layout** | `PlaceKinds` is identity; `PlaceComposer` is layout; `PlaceWorld` and `PlaceAtmosphere` are style | A planetoid is a new *style* plus a *mapping*. Identity and listing state are untouched. | **The separation holds** (AD-20). |
| **Navigation, history, links, Windows/WSL** | Engine-independent Core | Unchanged. | **Works.** |
| **Engine** (Godot 4.7 .NET) | No physics engine is used for walking | Spheres, frames and gravity are all ours. Nothing in Godot blocks this; no new engine or library is needed. | **Fine.** |

## Recommended path (if DJ wants it)

1. **A planetoid style for one place** (≈ 2–3 days). The composed place on a sphere cap: per-instance chart → sphere transforms, subdivided avenues, the camera's frame-relative look, radial "up" in flight, picking in world space. The flat style remains the default.
2. **Travel between planetoids** (≈ 2 days). Bodies placed by the aerial layout in 3D (children orbiting their parent, or set in a line ahead). The experiment's launch, flight and capture, with the aerial world's history and landing rules.
3. **Feel** (DJ in the loop). Body size against place size (a 40 m body holds a small place; wide folders need larger bodies or several charts), gravity, the jump, and the camera (first person against a chase view: *Galaxy* uses a chase camera for its small worlds).

**Risks:**
- Distortion and the cap limit for large places.
- Readability of gate rings that curve away from view; labels in screen space mitigate this.
- Motion comfort when "up" turns as you walk; a chase camera and smoothing help.

**What to avoid:** a general physics or engine rewrite. The walker-in-a-chart result shows it is not needed.
