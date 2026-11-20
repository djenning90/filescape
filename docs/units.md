# Units, coordinates and quantities

How FileScape represents physical quantities, so a reader can tell seconds from degrees from metres without reconstructing the arithmetic. It complements `docs/development-methodology.md` (make important distinctions explicit; preserve behaviour; verify) and applies to all new and changed code.

## World scale

| Space | 1 world unit is | Notes |
|---|---|---|
| **Places** (walking world) | **1 metre** | Human scale: eye 1.75 m, step 0.45 m, towers 1.25 m wide and up to 12 m tall, gates 5 × 7 m. Walking, jumping and flight speeds are real m/s. |
| **Maps** (Classic FSN, District) | **1 layout unit (u), abstract** | `LayoutParams` cells and gaps have no physical meaning; camera speeds scale with the viewing distance. Never compare map units with metres. |
| File heights (both) | a visual encoding | `SizeScale` maps log10(bytes) to a height: metres in Places, layout units in the maps. It is not the size of anything physical. |

The engine (Godot) is unit-agnostic; the meaning comes from which experience is shown. Code that serves both (camera math, the camera rig) says **world units** and keeps plain floats.

## Axes and spaces

- **World space:** right-handed, **Y up**. In a place, **north = −Z**, east = +X, south = +Z; the landmark is at the origin, the arrival terrace to the south, child gates to the north. A place's frame *is* world space (one place is shown at a time).
- **Positions vs displacements:** both are `System.Numerics.Vector3` in Core and `Godot.Vector3` in the app. Name them for what they are (`Feet`, `Eye`, `Target` are positions; `delta`, `displacement`, `push` are displacements; a velocity is a `…Velocity` vector in units per second).
- **Footprints:** terrain and picking in the ground plane use `Vector2(x, z)` — the vector's `Y` component is **world Z**.
- **World and place frames (2026-11-06, AD-26):** the aerial world has a **world plane** (metres, north = −Z) on which `WorldLayout` places every folder. Each place has a `PlaceFrame` — its world centre and the world place angle of its own north. Everything rendered is in the **current place's local frame** (a floating origin): `frame.ToLocal(world)` / `ToWorld(local)`, angles with `AngleToLocal` / `AngleToWorld`, and a camera yaw converts as `worldYaw = localYaw − frame.NorthDeg` (yaw is the negated place angle). The place you are in is always at the origin. (The planetoid prototype's per-body chart, PLANET-02, was retired on 2026-11-20; the planets use `PlanetShape`.)
- **Screen:** see *Screen and UI* below.

## Angles

All angles are **degrees** (`Degrees`); radians appear only at a trigonometry or engine call (`ToRadians()`, `Degrees.FromRadians`, Godot `Basis.FromEuler`). Two sign conventions exist, by design, and meet in exactly one function:

| Convention | Used by | 0 means | Positive means |
|---|---|---|---|
| **Camera yaw** | `CameraMath`, `CameraRig`, `Walker`, `PlacePose.Yaw` | looking −Z (north) | turning **left** (counter-clockwise seen from above) |
| **Look pitch** | same | level | looking **up**; clamped to ±85° so the view never flips |
| **Elevation** | `CameraRig.Frame`, `SetDesired` | level | camera **above** the target looking down (= −look pitch) |
| **Place angle** (`…Deg` floats) | gates, file quarters, the overlook, `PlaceGeometry.Direction` | north (−Z) | **clockwise** (90 = east) |

`PlaceGeometry.YawFor(angleFromNorthDeg)` converts a place angle to a camera yaw (it negates). Angles are not normalised automatically — a yaw accumulates turns; use `Degrees.Wrapped()` for (−180°, 180°] or `CameraRig.NearestAngle` for the short way round.

## Time

- **Durations are `System.TimeSpan`.** It is already an allocation-free value type with no implicit conversion to a number.
- **The app clock** is `Main._clock.Elapsed` (time since start), held per frame in `Main._now`.
- **Frame time:** Godot hands `_Process` a `double` of seconds. Convert it with `Durations.FromSeconds(delta)`, which **rounds** to the nearest 100 ns tick. Do not use `TimeSpan.FromSeconds` for computed values: it truncates (1/60 s → 166,666 ticks), and the deficit accumulates, so a 120 Hz integrator fed 60 Hz frames lags a full step. The golden behaviour test caught exactly this.
- **Inner loops** may work in `float` seconds for speed; such variables end in `Seconds` (`dtSeconds`, `_accumulatorSeconds`). Convert at the boundary with `ToSecondsF()`, once if possible (`Walker` caches its parameter durations).

## Rates and smoothing

| Quantity | Type | Example |
|---|---|---|
| speed / signed velocity component | `MetersPerSecond` | walk 8, run 16 (user-adjustable 2–30), jump take-off 5.3 (up is positive) |
| acceleration | `MetersPerSecondSquared` | gravity 17 (a positive magnitude acting downwards) |
| angular speed | `DegreesPerSecond` | arrow-key turn 120°/s |
| **smoothing rate** (inverse seconds) | `PerSecond` | ground response 14/s, camera easing 7/s |
| frequency | `int …PerSecond` | walker sub-steps 120 per second |
| sensitivity | `float …DegreesPerPixel` | mouse look 0.12°/px |
| ratio / multiplier | `float` (dimensionless) | Shift ×3, wheel ×1.25 per notch, zoom factor |

**Exponential smoothing.** A smoothed value moves the fraction `k = 1 − e^(−rate·Δt)` of the way to its target each frame (`PerSecond.SmoothingFactor`). This is frame-rate independent: two frames of Δt/2 close the same fraction as one of Δt. To tune it, use the time constant `1/rate`. One time constant closes 63% of a step change, 2.3 close 90%, and 4.6 close 99%. A rate is not a duration: doubling a rate halves the time to settle.

## Screen and UI

- **Viewport units:** Godot UI positions and sizes (`Control.GetGlobalRect()`, `GetViewport().GetVisibleRect()`, `Camera3D.UnprojectPosition`) are in viewport units. Their scale depends on the window's **content scale factor**: the UI scale, by default the display's DPI ÷ 96, or `--ui-scale=`. At 125% Windows scaling, one viewport unit is 1.25 physical pixels.
- **Physical pixels:** window events, `InputEventMouseMotion.ScreenRelative` and mouse warps are in physical pixels.
  - **Mouse look and orbit** use `ScreenRelative`, so their speed is independent of the UI scale.
  - **Look direction** in Places is a user preference applied at the input (`Preferences.LookDelta`): x right / y down in pixels map to yaw and pitch in the camera convention, each axis optionally reversed. Never flip the camera math to change how the mouse feels.
  - **Map panning** uses `Relative` (viewport units), so content follows the cursor.
  - **Probes** convert UI rectangles to window pixels before injecting clicks (`ProbeBase.ToWindow`).
- **Label3D:** `PixelSize` is world units per texture pixel. A 64 px font at 0.0115 is 0.74 m tall in Places.

## The quantity types (`FileScape.Core.Units`)

```csharp
var eye = Meters.Of(1.75f);                         // construction: X.Of(value)
var walk = MetersPerSecond.Of(8f);
var covered = walk * TimeSpan.FromSeconds(2);       // m/s × s → m
var pace = Meters.Of(100) / TimeSpan.FromSeconds(25);   // m ÷ s → m/s
var time = Meters.Of(10) / walk;                    // m ÷ m/s → TimeSpan
var turn = DegreesPerSecond.Of(120) * dt;           // °/s × s → °
float k = PerSecond.Of(14).SmoothingFactor(dt);     // dimensionless fraction
float radians = yaw.ToRadians();                    // explicit, at a trig call
float raw = eye.Value;                              // explicit, at an engine/math boundary
```

- **One `float` each, `readonly record struct`.** No allocation, and the operators inline. They're used in frame loops.
- **Only dimensionally correct operators.** Add and subtract the same type; scale by a dimensionless `float`; the ratio of two like quantities is a `float`; mixed operations as shown above. There is **no implicit conversion** to `float`: write `.Value` (or a named conversion), and the receiving name then states the unit.
- **Signed values are legitimate** (displacements, vertical velocity, yaw, a fall limit below ground). Constructors do **not** reject NaN or infinity, since frame arithmetic must stay branch-free. Configuration validates itself (`WalkParams.Validate`, called by `Walker`), and `IsFinite` is available.
- **Formatting:** `ToString()` prints the unit ("1.75 m", "−85°"), and they are `IFormattable`, so `{x:0.00}` formats the number. For machine-readable output (JSON, CSV), write `.Value` explicitly.

## What stays primitive, deliberately

- **Godot vectors and engine calls:** engine-native, named by space and unit (`…WorldUnits`, `…Pixels`).
- **`CameraMath` and `CameraRig` distances and speeds:** world units, because they're shared with the abstract-unit maps. Typing them as `Meters` would be false in the maps.
- **Place composition and terrain geometry** (`PlaceParams`, `PlaceTerrain`, `PlaceComposer`): plain floats in metres. Every value there is a length in one space, the inner loops run per walker sub-step, and the type-level documentation states the unit. Angles there are `…Deg` place angles.
- **Dimensionless ratios, factors and cosines**, named for what they are (`MinFacingCosine`, `WheelSpeedFactorPerNotch`).

## Writing and reviewing numeric code

- **Prefer a type** where a value's unit is easy to confuse (durations, angles, speeds, rates, walking lengths). Otherwise, put the unit in the name: `…Seconds`, `…Degrees`/`…Deg`, `…Pixels`, `…WorldUnits`, `…PerSecond`.
- **Document each configuration value** where it isn't obvious: what it means, its unit and space, its sign convention, why the default or limit exists, and what changing it does. `WalkParams` is the model. Don't add comments that only restate a type.
- **Keep current versus desired state explicit** when a value eases (`CameraRig`), and say what the smoothing is for.
- **Units refactors must preserve behaviour.** Capture golden numbers before changing code (`GoldenBehaviourTests`) and keep them passing within stated float tolerances. A behaviour change needs its own explained commit.
